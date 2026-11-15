using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services.Shipping;

public class ReturnShipmentService : IReturnShipmentService
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IShippingCarrierService _carrierService;
    private readonly ILogger<ReturnShipmentService> _logger;

    public ReturnShipmentService(
        IApplicationDbContext context,
        ITenantProvider tenantProvider,
        ICurrentUserContext currentUserContext,
        IShippingCarrierService carrierService,
        ILogger<ReturnShipmentService> logger)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _currentUserContext = currentUserContext;
        _carrierService = carrierService;
        _logger = logger;
    }

    public async Task<ReturnShipmentResult> CreateReturnShipmentAsync(
        CreateReturnShipmentRequest request,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate Input Payload
        if (request == null)
        {
            throw new ValidationException(nameof(request), "Create return shipment request cannot be null.");
        }

        if (request.AuthorizationId == Guid.Empty)
        {
            throw new ValidationException(nameof(request.AuthorizationId), "Return authorization ID is required.");
        }

        // 2. Retrieve Authorization with Navigations
        var authorization = await _context.ReturnAuthorizations
            .Include(a => a.DeliveryException)
            .Include(a => a.Package)
            .Include(a => a.Order)
                .ThenInclude(o => o.ShippingAddress)
            .Include(a => a.OriginFacility)
            .Include(a => a.DestinationFacility)
            .Include(a => a.ReturnShipment)
            .FirstOrDefaultAsync(a => a.Id == request.AuthorizationId, cancellationToken);

        if (authorization == null)
        {
            throw new EntityNotFoundException(nameof(ReturnAuthorization), request.AuthorizationId);
        }

        // 3. Multi-Tenancy Validation
        if (request.TenantIdOverride.HasValue && request.TenantIdOverride.Value != authorization.ClientId)
        {
            throw new UnauthorizedException("Authorization belongs to a different client tenant. Cross-tenant reverse shipment rejected.");
        }

        if (_tenantProvider.TenantId.HasValue &&
            !_tenantProvider.BypassTenantFilter &&
            _tenantProvider.TenantId.Value != authorization.ClientId)
        {
            throw new UnauthorizedException("Unauthorized cross-tenant reverse shipment creation attempt.");
        }

        // 4. Facility Authorization Check
        if (_currentUserContext.FacilityId.HasValue &&
            !_currentUserContext.IsSuperAdmin &&
            _currentUserContext.FacilityId.Value != authorization.OriginFacilityId &&
            _currentUserContext.FacilityId.Value != authorization.DestinationFacilityId)
        {
            throw new ForbiddenException($"User is not authorized for return facility '{authorization.DestinationFacilityId}'.");
        }

        // 5. Custody & Authorization Lifecycle Guards
        if (authorization.Status == ReturnAuthorizationStatus.Cancelled)
        {
            throw new InvalidDomainStateException("Cannot create a return shipment for a cancelled return authorization.");
        }

        if (authorization.Status == ReturnAuthorizationStatus.Expired)
        {
            throw new InvalidDomainStateException("Cannot create a return shipment for an expired return authorization.");
        }

        if (authorization.Status == ReturnAuthorizationStatus.InTransit ||
            authorization.Status == ReturnAuthorizationStatus.DeliveredAtFacility ||
            authorization.Status == ReturnAuthorizationStatus.Completed)
        {
            throw new InvalidDomainStateException("Carrier custody has already commenced for this return authorization.");
        }

        // 6. Idempotency Check & Active Shipment Custody Protection (Review Observation 2 & 3)
        var existingShipment = authorization.ReturnShipment ??
            await _context.ReturnShipments
                .FirstOrDefaultAsync(s => s.ReturnAuthorizationId == authorization.Id, cancellationToken);

        if (existingShipment != null)
        {
            if (existingShipment.CarrierHandoverAtUtc.HasValue ||
                existingShipment.Status == ReturnShipmentStatus.InTransitToWarehouse ||
                existingShipment.Status == ReturnShipmentStatus.DeliveredAtWarehouse)
            {
                throw new InvalidDomainStateException("Cannot create a replacement return shipment: carrier has already accepted custody of the existing return shipment.");
            }

            if (existingShipment.Status == ReturnShipmentStatus.Authorized)
            {
                _logger.LogInformation("Return shipment '{ShipmentId}' already exists for Authorization '{AuthNum}'. Returning idempotent result.",
                    existingShipment.Id, authorization.AuthorizationNumber);

                return BuildResult(existingShipment, authorization.AuthorizationNumber, isDuplicate: true, message: "Existing return shipment returned (idempotent).");
            }
        }

        // 7. Reverse Origin and Destination Address Resolution
        var (originAddress, destinationAddress) = ResolveAddresses(request, authorization);

        // Validate Addresses
        ValidateAddress(originAddress, "Reverse Origin");
        ValidateAddress(destinationAddress, "Reverse Destination");

        if (string.Equals(originAddress.StreetLine1.Trim(), destinationAddress.StreetLine1.Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(originAddress.PostalCode.Trim(), destinationAddress.PostalCode.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(nameof(CarrierAddress), "Reverse origin and destination addresses cannot be identical.");
        }

        // 8. Carrier & Service Level Selection
        var carrierCode = !string.IsNullOrWhiteSpace(request.CarrierCode)
            ? request.CarrierCode.Trim().ToUpperInvariant()
            : (!string.IsNullOrWhiteSpace(authorization.Package?.CarrierCode)
                ? authorization.Package.CarrierCode
                : "SIM-FEDEX");

        var serviceLevel = request.ServiceLevel ?? CarrierServiceLevel.StandardGround;
        var labelFormat = string.IsNullOrWhiteSpace(request.LabelFormat) ? "ZPL" : request.LabelFormat.Trim().ToUpperInvariant();

        // 9. External Carrier API Call (Mandatory Invariant: OUTSIDE Database Transaction!)
        var carrierRequest = new CarrierShipmentRequest
        {
            ClientId = authorization.ClientId,
            FacilityId = authorization.DestinationFacilityId,
            PackageId = authorization.PackageId,
            PackageNumber = authorization.Package?.PackageNumber ?? $"PKG-{authorization.PackageId:N}"[..12],
            CarrierCode = carrierCode,
            ServiceLevel = serviceLevel,
            OriginAddress = originAddress,
            DestinationAddress = destinationAddress,
            Parcel = new CarrierParcel
            {
                PackageNumber = authorization.Package?.PackageNumber ?? "PKG-RET",
                WeightKg = 2.0m,
                LengthCm = 15m,
                WidthCm = 15m,
                HeightCm = 10m
            },
            LabelFormat = labelFormat,
            IdempotencyKey = $"RTS_{authorization.Id:N}",
            SimulateFailure = request.SimulateCarrierFailure
        };

        CarrierShipmentResponse carrierResponse;
        try
        {
            carrierResponse = await _carrierService.CreateShipmentAsync(carrierRequest, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Carrier gateway communication exception during reverse shipment booking for Authorization '{AuthNum}'.", authorization.AuthorizationNumber);
            throw new InvalidOperationException($"Carrier gateway communication failure: {ex.Message}", ex);
        }

        if (carrierResponse == null || !carrierResponse.IsSuccess)
        {
            var err = carrierResponse?.ErrorMessage ?? "Carrier rejected reverse shipment request.";
            _logger.LogWarning("Carrier rejected reverse shipment creation for Authorization '{AuthNum}': {Error}", authorization.AuthorizationNumber, err);
            throw new InvalidOperationException($"Carrier failed to create return shipment: {err}");
        }

        // 10. Re-check Concurrency & Authorization State Inside Local Store Reconcile
        // Re-read latest authorization state to guard against race condition with cancellation/expiration
        var freshAuth = await _context.ReturnAuthorizations
            .Include(a => a.DeliveryException)
            .FirstOrDefaultAsync(a => a.Id == authorization.Id, cancellationToken);

        if (freshAuth == null || freshAuth.Status == ReturnAuthorizationStatus.Cancelled || freshAuth.Status == ReturnAuthorizationStatus.Expired)
        {
            // Race condition detected: Authorization was cancelled/expired while carrier call was in-flight.
            // Immediately void the newly booked carrier label.
            _logger.LogWarning("Authorization '{AuthNum}' cancelled/expired while carrier booking was in-flight. Voiding label '{Tracking}'.",
                authorization.AuthorizationNumber, carrierResponse.TrackingNumber);

            await _carrierService.VoidShipmentAsync(new CarrierVoidRequest
            {
                CarrierCode = carrierResponse.CarrierCode,
                TrackingNumber = carrierResponse.TrackingNumber,
                Reason = "Authorization cancelled/expired concurrently with carrier booking",
                IdempotencyKey = $"VOID_RACE_{authorization.Id:N}"
            }, cancellationToken);

            throw new InvalidDomainStateException("Authorization was cancelled or expired during carrier booking.");
        }

        // Check again if a shipment was concurrently inserted
        var concurrentShipment = await _context.ReturnShipments
            .AnyAsync(s => s.ReturnAuthorizationId == authorization.Id, cancellationToken);

        if (concurrentShipment)
        {
            // Another thread already succeeded, void this duplicate booking
            await _carrierService.VoidShipmentAsync(new CarrierVoidRequest
            {
                CarrierCode = carrierResponse.CarrierCode,
                TrackingNumber = carrierResponse.TrackingNumber,
                Reason = "Concurrent duplicate return shipment booking rejected",
                IdempotencyKey = $"VOID_DUP_{authorization.Id:N}"
            }, cancellationToken);

            throw new InvalidDomainStateException($"A return shipment has already been created for Authorization '{authorization.AuthorizationNumber}'.");
        }

        var labelObjectKey = $"tenants/{authorization.ClientId}/returns/{authorization.Id:N}/labels/{carrierResponse.TrackingNumber}.{labelFormat.ToLowerInvariant()}";

        // 11. Create ReturnShipment Aggregate
        var returnShipment = new ReturnShipment
        {
            Id = Guid.NewGuid(),
            ClientId = authorization.ClientId,
            OriginFacilityId = authorization.OriginFacilityId,
            DestinationFacilityId = authorization.DestinationFacilityId,
            OrderId = authorization.OrderId,
            OriginalShipmentId = authorization.OriginalShipmentId,
            PackageId = authorization.PackageId,
            DeliveryExceptionId = authorization.DeliveryExceptionId,
            ReturnAuthorizationId = authorization.Id,
            ReturnRmaNumber = authorization.RmaNumber ?? $"RMA-{authorization.AuthorizationNumber}",
            Reason = authorization.Reason,
            AuthorizedByUserId = authorization.AuthorizedByUserId,
            AuthorizedAtUtc = authorization.AuthorizedAtUtc,
            AuthorizationNotes = authorization.AuthorizationNotes,
            CarrierCode = carrierResponse.CarrierCode,
            TrackingNumber = carrierResponse.TrackingNumber,
            CarrierServiceLevel = serviceLevel.ToString(),
            LabelGeneratedAtUtc = DateTime.UtcNow,
            CarrierShipmentId = carrierResponse.CarrierShipmentId,
            LabelFormat = carrierResponse.LabelFormat,
            LabelData = carrierResponse.LabelData,
            LabelObjectKey = labelObjectKey,
            RateCharged = carrierResponse.RateCharged,
            Currency = carrierResponse.Currency,
            OriginAddressLine1 = originAddress.StreetLine1,
            OriginAddressLine2 = originAddress.StreetLine2,
            OriginCity = originAddress.City,
            OriginState = originAddress.StateOrProvince,
            OriginPostalCode = originAddress.PostalCode,
            OriginCountry = originAddress.CountryCode,
            DestinationAddressLine1 = destinationAddress.StreetLine1,
            DestinationAddressLine2 = destinationAddress.StreetLine2,
            DestinationCity = destinationAddress.City,
            DestinationState = destinationAddress.StateOrProvince,
            DestinationPostalCode = destinationAddress.PostalCode,
            DestinationCountry = destinationAddress.CountryCode,
            Status = ReturnShipmentStatus.Authorized
        };

        _context.ReturnShipments.Add(returnShipment);

        // 12. Transition Authorization to ShipmentCreated
        freshAuth.LinkReturnShipment(returnShipment.Id);

        // 13. Update DeliveryException Aggregate Reference
        if (freshAuth.DeliveryException != null)
        {
            freshAuth.DeliveryException.ReturnShipmentId = returnShipment.Id;
        }

        // 14. Append Tracking Event
        var trackingFp = ComputeFingerprint(authorization.ClientId, carrierResponse.CarrierCode, carrierResponse.TrackingNumber, $"RTS_SHP_{returnShipment.Id:N}", DateTime.UtcNow);
        _context.ShipmentTrackingEvents.Add(new ShipmentTrackingEvent
        {
            ClientId = authorization.ClientId,
            FacilityId = authorization.OriginFacilityId,
            PackageId = authorization.PackageId,
            ShipmentId = authorization.OriginalShipmentId,
            TrackingNumber = carrierResponse.TrackingNumber,
            CarrierCode = carrierResponse.CarrierCode,
            CarrierEventCode = "RETURN_SHIPMENT_CREATED",
            MilestoneStatus = TrackingMilestoneStatus.DeliveryException,
            CarrierDescription = $"Reverse shipment created with carrier {carrierResponse.CarrierCode}, tracking #{carrierResponse.TrackingNumber} (Auth #{authorization.AuthorizationNumber})",
            OccurredAtUtc = DateTime.UtcNow,
            ReceivedAtUtc = DateTime.UtcNow,
            EventFingerprint = trackingFp
        });

        // 15. Atomic Commit
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Return shipment '{ShipmentId}' with tracking '{Tracking}' created and linked to Authorization '{AuthNum}'.",
            returnShipment.Id, returnShipment.TrackingNumber, authorization.AuthorizationNumber);

        return BuildResult(returnShipment, authorization.AuthorizationNumber, isDuplicate: false, message: "Return shipment created and carrier label booked successfully.");
    }

    public async Task<ReturnShipment?> GetReturnShipmentByIdAsync(
        Guid returnShipmentId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ReturnShipments
            .Include(s => s.Client)
            .Include(s => s.OriginFacility)
            .Include(s => s.DestinationFacility)
            .Include(s => s.Order)
            .Include(s => s.Package)
            .Include(s => s.ReturnAuthorization)
            .Include(s => s.DeliveryException)
            .Include(s => s.TrackingEvents)
            .FirstOrDefaultAsync(s => s.Id == returnShipmentId, cancellationToken);
    }

    public async Task<ReturnShipment?> GetReturnShipmentByTrackingNumberAsync(
        string trackingNumber,
        string carrierCode,
        CancellationToken cancellationToken = default)
    {
        var normalizedTracking = trackingNumber.Trim();
        var normalizedCarrier = carrierCode.Trim().ToUpperInvariant();

        return await _context.ReturnShipments
            .Include(s => s.ReturnAuthorization)
            .Include(s => s.Package)
            .FirstOrDefaultAsync(s => s.TrackingNumber == normalizedTracking &&
                                      s.CarrierCode == normalizedCarrier, cancellationToken);
    }

    private static (CarrierAddress Origin, CarrierAddress Destination) ResolveAddresses(
        CreateReturnShipmentRequest request,
        ReturnAuthorization authorization)
    {
        // 1. Resolve Reverse Origin Address
        CarrierAddress origin;
        if (request.OriginAddressOverride != null)
        {
            origin = request.OriginAddressOverride;
        }
        else if (authorization.DeliveryException != null &&
                 !string.IsNullOrWhiteSpace(authorization.DeliveryException.CorrectedAddress))
        {
            origin = new CarrierAddress
            {
                Name = authorization.Order?.ShippingAddress?.RecipientName ?? "Consignee",
                StreetLine1 = authorization.DeliveryException.CorrectedAddress.Trim(),
                City = authorization.Order?.ShippingAddress?.City ?? "OriginCity",
                StateOrProvince = authorization.Order?.ShippingAddress?.StateOrProvince ?? "KY",
                PostalCode = authorization.Order?.ShippingAddress?.PostalCode ?? "40202",
                CountryCode = authorization.Order?.ShippingAddress?.CountryCode ?? "US"
            };
        }
        else if (authorization.DeliveryException != null &&
                 !string.IsNullOrWhiteSpace(authorization.DeliveryException.HoldDepotLocation))
        {
            origin = new CarrierAddress
            {
                Name = "Carrier Hold Depot",
                StreetLine1 = authorization.DeliveryException.HoldDepotLocation.Trim(),
                City = authorization.Order?.ShippingAddress?.City ?? "DepotCity",
                StateOrProvince = authorization.Order?.ShippingAddress?.StateOrProvince ?? "KY",
                PostalCode = authorization.Order?.ShippingAddress?.PostalCode ?? "40202",
                CountryCode = "US"
            };
        }
        else if (authorization.Order?.ShippingAddress != null)
        {
            var sa = authorization.Order.ShippingAddress;
            origin = new CarrierAddress
            {
                Name = sa.RecipientName,
                CompanyName = sa.CompanyName,
                StreetLine1 = sa.AddressLine1,
                StreetLine2 = sa.AddressLine2,
                City = sa.City,
                StateOrProvince = sa.StateOrProvince,
                PostalCode = sa.PostalCode,
                CountryCode = sa.CountryCode,
                Phone = sa.PhoneNumber
            };
        }
        else
        {
            origin = new CarrierAddress
            {
                Name = "Consignee",
                StreetLine1 = "100 Consignee Way",
                City = "Louisville",
                StateOrProvince = "KY",
                PostalCode = "40202",
                CountryCode = "US"
            };
        }

        // 2. Resolve Reverse Destination Address (Authorized Receiving Warehouse Facility)
        CarrierAddress destination;
        if (request.DestinationAddressOverride != null)
        {
            destination = request.DestinationAddressOverride;
        }
        else if (authorization.DestinationFacility != null)
        {
            var df = authorization.DestinationFacility;
            destination = new CarrierAddress
            {
                Name = df.Name,
                StreetLine1 = !string.IsNullOrWhiteSpace(df.AddressLine1) ? df.AddressLine1 : "456 Receiving Dock Way",
                StreetLine2 = df.AddressLine2,
                City = !string.IsNullOrWhiteSpace(df.City) ? df.City : "Lexington",
                StateOrProvince = !string.IsNullOrWhiteSpace(df.StateOrProvince) ? df.StateOrProvince : "KY",
                PostalCode = !string.IsNullOrWhiteSpace(df.PostalCode) ? df.PostalCode : "40509",
                CountryCode = !string.IsNullOrWhiteSpace(df.CountryCode) ? df.CountryCode : "US"
            };
        }
        else
        {
            destination = new CarrierAddress
            {
                Name = "Receiving Warehouse Dock",
                StreetLine1 = "456 Receiving Dock Way",
                City = "Lexington",
                StateOrProvince = "KY",
                PostalCode = "40509",
                CountryCode = "US"
            };
        }

        return (origin, destination);
    }

    private static void ValidateAddress(CarrierAddress address, string addressType)
    {
        if (address == null)
        {
            throw new ValidationException(nameof(CarrierAddress), $"{addressType} address cannot be null.");
        }

        if (string.IsNullOrWhiteSpace(address.StreetLine1))
        {
            throw new ValidationException(nameof(address.StreetLine1), $"{addressType} street address line 1 is required.");
        }

        if (string.IsNullOrWhiteSpace(address.City))
        {
            throw new ValidationException(nameof(address.City), $"{addressType} city is required.");
        }

        if (string.IsNullOrWhiteSpace(address.StateOrProvince))
        {
            throw new ValidationException(nameof(address.StateOrProvince), $"{addressType} state or province is required.");
        }

        if (string.IsNullOrWhiteSpace(address.PostalCode))
        {
            throw new ValidationException(nameof(address.PostalCode), $"{addressType} postal code is required.");
        }
    }

    private static ReturnShipmentResult BuildResult(
        ReturnShipment shipment,
        string authorizationNumber,
        bool isDuplicate,
        string message)
    {
        return new ReturnShipmentResult
        {
            IsSuccess = true,
            ReturnShipmentId = shipment.Id,
            ReturnAuthorizationId = shipment.ReturnAuthorizationId ?? Guid.Empty,
            AuthorizationNumber = authorizationNumber,
            TrackingNumber = shipment.TrackingNumber,
            CarrierCode = shipment.CarrierCode,
            ServiceLevel = shipment.CarrierServiceLevel ?? "StandardGround",
            LabelFormat = shipment.LabelFormat,
            LabelData = shipment.LabelData,
            LabelObjectKey = shipment.LabelObjectKey,
            RateCharged = shipment.RateCharged,
            Currency = shipment.Currency,
            Status = shipment.Status,
            IsDuplicate = isDuplicate,
            CarrierCancellationConfirmed = shipment.CarrierCancellationConfirmed,
            CarrierCancellationNotes = shipment.CarrierCancellationNotes,
            Message = message
        };
    }

    private static string ComputeFingerprint(Guid clientId, string carrierCode, string trackingNumber, string eventKey, DateTime occurredAtUtc)
    {
        var raw = $"{clientId:D}:{carrierCode.Trim().ToUpperInvariant()}:{trackingNumber.Trim()}:{eventKey}:{occurredAtUtc.ToUniversalTime():O}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

