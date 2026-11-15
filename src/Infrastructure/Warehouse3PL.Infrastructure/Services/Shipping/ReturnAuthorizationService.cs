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

public class ReturnAuthorizationService : IReturnAuthorizationService
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IShippingCarrierService _carrierService;
    private readonly ILogger<ReturnAuthorizationService> _logger;

    public ReturnAuthorizationService(
        IApplicationDbContext context,
        ITenantProvider tenantProvider,
        ICurrentUserContext currentUserContext,
        ILogger<ReturnAuthorizationService> logger)
        : this(
            context,
            tenantProvider,
            currentUserContext,
            new SimulatedCarrierService(Microsoft.Extensions.Logging.Abstractions.NullLogger<SimulatedCarrierService>.Instance),
            logger)
    {
    }

    public ReturnAuthorizationService(
        IApplicationDbContext context,
        ITenantProvider tenantProvider,
        ICurrentUserContext currentUserContext,
        IShippingCarrierService carrierService,
        ILogger<ReturnAuthorizationService> logger)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _currentUserContext = currentUserContext;
        _carrierService = carrierService;
        _logger = logger;
    }

    public async Task<ReturnAuthorizationResult> AuthorizeReturnToSenderAsync(
        AuthorizeReturnToSenderRequest request,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate Input Payload
        if (request == null)
        {
            throw new ValidationException(nameof(request), "Authorize return request cannot be null.");
        }

        if (request.DeliveryExceptionId == Guid.Empty)
        {
            throw new ValidationException(nameof(request.DeliveryExceptionId), "Delivery exception ID is required.");
        }

        if (request.DestinationFacilityId == Guid.Empty)
        {
            throw new ValidationException(nameof(request.DestinationFacilityId), "Destination facility ID is required.");
        }

        if (string.IsNullOrWhiteSpace(request.AuthorizationNotes))
        {
            throw new ValidationException(nameof(request.AuthorizationNotes), "Authorization notes are required.");
        }

        // 2. Retrieve Delivery Exception with Navigations
        var exception = await _context.ShipmentDeliveryExceptions
            .Include(e => e.Package)
            .Include(e => e.Shipment)
            .Include(e => e.Order)
            .FirstOrDefaultAsync(e => e.Id == request.DeliveryExceptionId, cancellationToken);

        if (exception == null)
        {
            throw new EntityNotFoundException(nameof(ShipmentDeliveryException), request.DeliveryExceptionId);
        }

        // 3. Multi-Tenancy Validation
        if (request.TenantIdOverride.HasValue && request.TenantIdOverride.Value != exception.ClientId)
        {
            throw new UnauthorizedException("Exception belongs to a different client tenant. Cross-tenant return authorization rejected.");
        }

        if (_tenantProvider.TenantId.HasValue &&
            !_tenantProvider.BypassTenantFilter &&
            _tenantProvider.TenantId.Value != exception.ClientId)
        {
            throw new UnauthorizedException("Unauthorized cross-tenant return authorization attempt.");
        }

        // 4. Destination Facility & Facility Permissions Validation
        var destinationFacility = await _context.Facilities
            .FirstOrDefaultAsync(f => f.Id == request.DestinationFacilityId && !f.IsDeleted, cancellationToken);

        if (destinationFacility == null)
        {
            throw new EntityNotFoundException(nameof(Facility), request.DestinationFacilityId);
        }

        if (_currentUserContext.FacilityId.HasValue &&
            !_currentUserContext.IsSuperAdmin &&
            _currentUserContext.FacilityId.Value != exception.FacilityId &&
            _currentUserContext.FacilityId.Value != request.DestinationFacilityId)
        {
            throw new ForbiddenException($"User is not authorized for destination facility '{request.DestinationFacilityId}' or exception facility '{exception.FacilityId}'.");
        }

        // 5. Exception Eligibility Rules
        if (exception.Status == DeliveryExceptionStatus.Voided)
        {
            throw new InvalidDomainStateException("A voided delivery exception cannot be authorized for Return-To-Sender.");
        }

        if (exception.Status == DeliveryExceptionStatus.Resolved)
        {
            throw new InvalidDomainStateException("An already resolved delivery exception cannot be authorized for Return-To-Sender.");
        }

        if (exception.Status == DeliveryExceptionStatus.ReturnToSenderInitiated)
        {
            throw new InvalidDomainStateException("Return-To-Sender is already initiated for this delivery exception.");
        }

        // 6. Duplicate Active Authorization Prevention (Single Active Authorization Invariant)
        var activeExists = await _context.ReturnAuthorizations
            .AnyAsync(a => (a.DeliveryExceptionId == exception.Id || a.PackageId == exception.PackageId) &&
                           a.Status != ReturnAuthorizationStatus.Cancelled &&
                           a.Status != ReturnAuthorizationStatus.Expired, cancellationToken);

        if (activeExists)
        {
            throw new InvalidDomainStateException($"An active return authorization already exists for Package '{exception.PackageId}' or Exception '{exception.Id}'.");
        }

        // 7. Calculate Expiry Date Horizon
        DateTime? expiresAt = request.ExpiresAtUtc;
        if (!expiresAt.HasValue && request.ExpiryDays.HasValue)
        {
            if (request.ExpiryDays.Value <= 0)
            {
                throw new ValidationException(nameof(request.ExpiryDays), "Expiry days must be greater than zero.");
            }
            expiresAt = DateTime.UtcNow.AddDays(request.ExpiryDays.Value);
        }
        else if (!expiresAt.HasValue)
        {
            expiresAt = DateTime.UtcNow.AddDays(30); // Default 30-day authorization window
        }

        var userId = request.AuthorizedByUserId ?? _currentUserContext.UserId ?? Guid.Empty;
        var authNumber = $"RA-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";

        // 8. Create ReturnAuthorization Aggregate
        var authorization = ReturnAuthorization.Create(
            clientId: exception.ClientId,
            originFacilityId: exception.FacilityId,
            destinationFacilityId: request.DestinationFacilityId,
            orderId: exception.OrderId ?? exception.Package.OutboundOrderId,
            originalShipmentId: exception.ShipmentId,
            packageId: exception.PackageId,
            deliveryExceptionId: exception.Id,
            authorizationNumber: authNumber,
            reason: request.Reason,
            notes: request.AuthorizationNotes,
            authorizedByUserId: userId,
            rmaNumber: request.RmaNumber,
            expiresAtUtc: expiresAt);

        // Validate complete relationship model
        authorization.ValidateRelationships(exception.Package, exception.Shipment, exception.Order, destinationFacility);

        _context.ReturnAuthorizations.Add(authorization);

        // 9. Atomically Update Exception Aggregate State
        exception.AuthorizeReturnToSender(authorization.Id, null, request.AuthorizationNotes, userId);

        // 10. Append Immutable Tracking Event
        var trackingFp = ComputeFingerprint(exception.ClientId, exception.CarrierCode, exception.TrackingNumber, $"RTS_AUTH_{authorization.Id:N}", DateTime.UtcNow);
        _context.ShipmentTrackingEvents.Add(new ShipmentTrackingEvent
        {
            ClientId = exception.ClientId,
            FacilityId = exception.FacilityId,
            PackageId = exception.PackageId,
            ShipmentId = exception.ShipmentId,
            TrackingNumber = exception.TrackingNumber,
            CarrierCode = exception.CarrierCode,
            CarrierEventCode = "RETURN_TO_SENDER_AUTHORIZED",
            MilestoneStatus = TrackingMilestoneStatus.DeliveryException,
            CarrierDescription = $"Return to sender authorized (Auth #{authorization.AuthorizationNumber}): {request.AuthorizationNotes.Trim()}",
            OccurredAtUtc = DateTime.UtcNow,
            ReceivedAtUtc = DateTime.UtcNow,
            EventFingerprint = trackingFp
        });

        // 11. Atomic Commit
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Return authorization '{AuthNum}' successfully created for Package '{Pkg}' and Exception '{Exc}'.",
            authorization.AuthorizationNumber, exception.Package.PackageNumber, exception.Id);

        return BuildResult(authorization, "Return-To-Sender authorized successfully.");
    }

    public async Task<ReturnAuthorizationResult> CancelAuthorizationAsync(
        CancelReturnAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ValidationException(nameof(request), "Cancel request cannot be null.");
        }

        if (request.AuthorizationId == Guid.Empty)
        {
            throw new ValidationException(nameof(request.AuthorizationId), "Authorization ID is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ValidationException(nameof(request.Reason), "Cancellation reason is required.");
        }

        var authorization = await _context.ReturnAuthorizations
            .Include(a => a.DeliveryException)
            .Include(a => a.ReturnShipment)
            .Include(a => a.Package)
            .FirstOrDefaultAsync(a => a.Id == request.AuthorizationId, cancellationToken);

        if (authorization == null)
        {
            throw new EntityNotFoundException(nameof(ReturnAuthorization), request.AuthorizationId);
        }

        // Multi-Tenancy Validation
        if (request.TenantIdOverride.HasValue && request.TenantIdOverride.Value != authorization.ClientId)
        {
            throw new UnauthorizedException("Authorization belongs to a different client tenant. Cross-tenant cancellation rejected.");
        }

        if (_tenantProvider.TenantId.HasValue &&
            !_tenantProvider.BypassTenantFilter &&
            _tenantProvider.TenantId.Value != authorization.ClientId)
        {
            throw new UnauthorizedException("Unauthorized cross-tenant authorization cancellation attempt.");
        }

        // Facility Permissions Validation
        if (_currentUserContext.FacilityId.HasValue &&
            !_currentUserContext.IsSuperAdmin &&
            _currentUserContext.FacilityId.Value != authorization.OriginFacilityId &&
            _currentUserContext.FacilityId.Value != authorization.DestinationFacilityId)
        {
            throw new ForbiddenException($"User is not authorized for authorization facility '{authorization.DestinationFacilityId}'.");
        }

        // Pre-Transit Cancellation Eligibility Check
        if (authorization.ReturnShipment != null &&
            (authorization.ReturnShipment.CarrierHandoverAtUtc.HasValue ||
             authorization.ReturnShipment.Status == ReturnShipmentStatus.InTransitToWarehouse ||
             authorization.ReturnShipment.Status == ReturnShipmentStatus.DeliveredAtWarehouse))
        {
            throw new InvalidDomainStateException("Cannot cancel return authorization because carrier has already accepted custody of the return shipment.");
        }

        var userId = request.CancelledByUserId ?? _currentUserContext.UserId ?? Guid.Empty;

        // 1. Cancel Authorization Aggregate
        authorization.Cancel(request.Reason, userId);

        // 2. Invalidate Linked ReturnShipment Booking/Label if in ShipmentCreated (Point 2)
        if (authorization.ReturnShipment != null && authorization.ReturnShipment.Status != ReturnShipmentStatus.Cancelled)
        {
            try
            {
                var voidResponse = await _carrierService.VoidShipmentAsync(new CarrierVoidRequest
                {
                    CarrierCode = authorization.ReturnShipment.CarrierCode,
                    TrackingNumber = authorization.ReturnShipment.TrackingNumber,
                    Reason = request.Reason.Trim(),
                    IdempotencyKey = $"VOID_{authorization.ReturnShipment.Id:N}"
                }, cancellationToken);

                if (voidResponse != null && voidResponse.IsSuccess)
                {
                    authorization.ReturnShipment.CarrierCancellationConfirmed = true;
                    authorization.ReturnShipment.CarrierCancellationNotes = $"Carrier confirmed void: {voidResponse.VoidConfirmationNumber}";
                }
                else
                {
                    authorization.ReturnShipment.CarrierCancellationConfirmed = false;
                    authorization.ReturnShipment.CarrierCancellationNotes = $"Carrier void failed: {voidResponse?.ErrorMessage}. Local status set to Cancelled.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Carrier void exception during return shipment cancellation for tracking '{Tracking}'.", authorization.ReturnShipment.TrackingNumber);
                authorization.ReturnShipment.CarrierCancellationConfirmed = false;
                authorization.ReturnShipment.CarrierCancellationNotes = $"Carrier communication error: {ex.Message}. Local status set to Cancelled.";
            }

            authorization.ReturnShipment.Cancel($"Linked return authorization cancelled: {request.Reason.Trim()}", userId);
            _logger.LogInformation("Cancelled linked ReturnShipment '{ShipmentId}' due to authorization cancellation.", authorization.ReturnShipment.Id);
        }

        // 3. Atomically Revert Exception Aggregate (Point 3)
        if (authorization.DeliveryException != null &&
            authorization.DeliveryException.Status == DeliveryExceptionStatus.ReturnToSenderInitiated)
        {
            authorization.DeliveryException.CancelReturnAuthorization(request.Reason, userId);
            _logger.LogInformation("Reverted delivery exception '{ExcId}' to UnderReview following authorization cancellation.", authorization.DeliveryException.Id);
        }

        // 4. Append Immutable Tracking Event
        var carrierCode = authorization.Package?.CarrierCode ?? "SIM";
        var trackingNumber = authorization.Package?.TrackingNumber ?? authorization.AuthorizationNumber;
        var trackingFp = ComputeFingerprint(authorization.ClientId, carrierCode, trackingNumber, $"RTS_CANCEL_{authorization.Id:N}_{DateTime.UtcNow.Ticks}", DateTime.UtcNow);

        _context.ShipmentTrackingEvents.Add(new ShipmentTrackingEvent
        {
            ClientId = authorization.ClientId,
            FacilityId = authorization.OriginFacilityId,
            PackageId = authorization.PackageId,
            ShipmentId = authorization.OriginalShipmentId,
            TrackingNumber = authorization.Package?.TrackingNumber ?? string.Empty,
            CarrierCode = authorization.Package?.CarrierCode ?? string.Empty,
            CarrierEventCode = "RETURN_AUTHORIZATION_CANCELLED",
            MilestoneStatus = TrackingMilestoneStatus.DeliveryException,
            CarrierDescription = $"Return authorization {authorization.AuthorizationNumber} cancelled: {request.Reason.Trim()}",
            OccurredAtUtc = DateTime.UtcNow,
            ReceivedAtUtc = DateTime.UtcNow,
            EventFingerprint = trackingFp
        });

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Return authorization '{AuthNum}' cancelled successfully.", authorization.AuthorizationNumber);

        return BuildResult(authorization, "Return authorization cancelled successfully.");
    }

    public async Task<ReturnAuthorizationResult> ExpireAuthorizationAsync(
        ExpireReturnAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ValidationException(nameof(request), "Expire request cannot be null.");
        }

        if (request.AuthorizationId == Guid.Empty)
        {
            throw new ValidationException(nameof(request.AuthorizationId), "Authorization ID is required.");
        }

        var authorization = await _context.ReturnAuthorizations
            .Include(a => a.DeliveryException)
            .Include(a => a.ReturnShipment)
            .Include(a => a.Package)
            .FirstOrDefaultAsync(a => a.Id == request.AuthorizationId, cancellationToken);

        if (authorization == null)
        {
            throw new EntityNotFoundException(nameof(ReturnAuthorization), request.AuthorizationId);
        }

        // Multi-Tenancy Validation
        if (request.TenantIdOverride.HasValue && request.TenantIdOverride.Value != authorization.ClientId)
        {
            throw new UnauthorizedException("Authorization belongs to a different client tenant. Cross-tenant expiration rejected.");
        }

        if (_tenantProvider.TenantId.HasValue &&
            !_tenantProvider.BypassTenantFilter &&
            _tenantProvider.TenantId.Value != authorization.ClientId)
        {
            throw new UnauthorizedException("Unauthorized cross-tenant authorization expiration attempt.");
        }

        // Pre-Transit Check
        if (authorization.ReturnShipment != null &&
            (authorization.ReturnShipment.CarrierHandoverAtUtc.HasValue ||
             authorization.ReturnShipment.Status == ReturnShipmentStatus.InTransitToWarehouse))
        {
            throw new InvalidDomainStateException("Cannot expire return authorization because carrier custody handover has already occurred.");
        }

        var expireReason = string.IsNullOrWhiteSpace(request.Reason)
            ? "Authorization window elapsed prior to carrier handover."
            : request.Reason.Trim();

        var userId = _currentUserContext.UserId ?? Guid.Empty;

        // 1. Expire Authorization Aggregate (Point 1)
        authorization.Expire();

        // 2. Invalidate Linked ReturnShipment Booking/Label if ShipmentCreated (Point 1)
        if (authorization.ReturnShipment != null && authorization.ReturnShipment.Status != ReturnShipmentStatus.Cancelled)
        {
            try
            {
                var voidResponse = await _carrierService.VoidShipmentAsync(new CarrierVoidRequest
                {
                    CarrierCode = authorization.ReturnShipment.CarrierCode,
                    TrackingNumber = authorization.ReturnShipment.TrackingNumber,
                    Reason = $"Return authorization expired: {expireReason}",
                    IdempotencyKey = $"VOID_EXP_{authorization.ReturnShipment.Id:N}"
                }, cancellationToken);

                if (voidResponse != null && voidResponse.IsSuccess)
                {
                    authorization.ReturnShipment.CarrierCancellationConfirmed = true;
                    authorization.ReturnShipment.CarrierCancellationNotes = $"Carrier confirmed void: {voidResponse.VoidConfirmationNumber}";
                }
                else
                {
                    authorization.ReturnShipment.CarrierCancellationConfirmed = false;
                    authorization.ReturnShipment.CarrierCancellationNotes = $"Carrier void failed: {voidResponse?.ErrorMessage}. Local status set to Cancelled.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Carrier void exception during return shipment expiration for tracking '{Tracking}'.", authorization.ReturnShipment.TrackingNumber);
                authorization.ReturnShipment.CarrierCancellationConfirmed = false;
                authorization.ReturnShipment.CarrierCancellationNotes = $"Carrier communication error: {ex.Message}. Local status set to Cancelled.";
            }

            authorization.ReturnShipment.Cancel($"Return authorization expired prior to carrier handover: {expireReason}", userId);
            _logger.LogInformation("Cancelled linked ReturnShipment '{ShipmentId}' due to authorization expiration.", authorization.ReturnShipment.Id);
        }

        // 3. Atomically Revert Exception Aggregate (Point 3)
        if (authorization.DeliveryException != null &&
            authorization.DeliveryException.Status == DeliveryExceptionStatus.ReturnToSenderInitiated)
        {
            authorization.DeliveryException.ExpireReturnAuthorization(expireReason);
            _logger.LogInformation("Reverted delivery exception '{ExcId}' to UnderReview following authorization expiration.", authorization.DeliveryException.Id);
        }

        // 4. Append Immutable Tracking Event
        var carrierCode = authorization.Package?.CarrierCode ?? "SIM";
        var trackingNumber = authorization.Package?.TrackingNumber ?? authorization.AuthorizationNumber;
        var trackingFp = ComputeFingerprint(authorization.ClientId, carrierCode, trackingNumber, $"RTS_EXPIRE_{authorization.Id:N}_{DateTime.UtcNow.Ticks}", DateTime.UtcNow);

        _context.ShipmentTrackingEvents.Add(new ShipmentTrackingEvent
        {
            ClientId = authorization.ClientId,
            FacilityId = authorization.OriginFacilityId,
            PackageId = authorization.PackageId,
            ShipmentId = authorization.OriginalShipmentId,
            TrackingNumber = authorization.Package?.TrackingNumber ?? string.Empty,
            CarrierCode = authorization.Package?.CarrierCode ?? string.Empty,
            CarrierEventCode = "RETURN_AUTHORIZATION_EXPIRED",
            MilestoneStatus = TrackingMilestoneStatus.DeliveryException,
            CarrierDescription = $"Return authorization {authorization.AuthorizationNumber} expired: {expireReason}",
            OccurredAtUtc = DateTime.UtcNow,
            ReceivedAtUtc = DateTime.UtcNow,
            EventFingerprint = trackingFp
        });

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Return authorization '{AuthNum}' expired successfully.", authorization.AuthorizationNumber);

        return BuildResult(authorization, "Return authorization expired successfully.");
    }

    public async Task<ReturnAuthorization?> GetAuthorizationByIdAsync(
        Guid authorizationId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ReturnAuthorizations
            .Include(a => a.Client)
            .Include(a => a.OriginFacility)
            .Include(a => a.DestinationFacility)
            .Include(a => a.Order)
            .Include(a => a.OriginalShipment)
            .Include(a => a.Package)
            .Include(a => a.DeliveryException)
            .Include(a => a.ReturnShipment)
            .Include(a => a.AuthorizedByUser)
            .Include(a => a.CancelledByUser)
            .FirstOrDefaultAsync(a => a.Id == authorizationId, cancellationToken);
    }

    public async Task<ReturnAuthorization?> GetActiveAuthorizationForPackageAsync(
        Guid packageId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ReturnAuthorizations
            .Include(a => a.DestinationFacility)
            .Include(a => a.DeliveryException)
            .Include(a => a.ReturnShipment)
            .FirstOrDefaultAsync(a => a.PackageId == packageId &&
                                      a.Status != ReturnAuthorizationStatus.Cancelled &&
                                      a.Status != ReturnAuthorizationStatus.Expired, cancellationToken);
    }

    private static ReturnAuthorizationResult BuildResult(ReturnAuthorization auth, string message)
    {
        return new ReturnAuthorizationResult
        {
            IsSuccess = true,
            AuthorizationId = auth.Id,
            AuthorizationNumber = auth.AuthorizationNumber,
            ClientId = auth.ClientId,
            PackageId = auth.PackageId,
            OrderId = auth.OrderId,
            OriginalShipmentId = auth.OriginalShipmentId,
            DeliveryExceptionId = auth.DeliveryExceptionId,
            ReturnShipmentId = auth.ReturnShipmentId,
            Reason = auth.Reason,
            Status = auth.Status,
            AuthorizedAtUtc = auth.AuthorizedAtUtc,
            ExpiresAtUtc = auth.ExpiresAtUtc,
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

