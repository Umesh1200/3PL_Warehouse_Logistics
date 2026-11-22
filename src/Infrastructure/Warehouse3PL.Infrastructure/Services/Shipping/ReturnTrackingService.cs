using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services.Shipping;

public class ReturnTrackingService : IReturnTrackingService
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ILogger<ReturnTrackingService> _logger;

    public ReturnTrackingService(
        IApplicationDbContext context,
        ITenantProvider tenantProvider,
        ICurrentUserContext currentUserContext,
        ILogger<ReturnTrackingService> logger)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _currentUserContext = currentUserContext;
        _logger = logger;
    }

    public async Task<IngestReturnTrackingResult> IngestReturnTrackingEventAsync(
        IngestReturnTrackingRequest request,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate Input Payload
        if (request == null)
        {
            throw new ValidationException(nameof(request), "Ingest return tracking request cannot be null.");
        }

        if (string.IsNullOrWhiteSpace(request.TrackingNumber))
        {
            throw new ValidationException(nameof(request.TrackingNumber), "Tracking number is required.");
        }

        if (string.IsNullOrWhiteSpace(request.CarrierCode))
        {
            throw new ValidationException(nameof(request.CarrierCode), "Carrier code is required.");
        }

        if (string.IsNullOrWhiteSpace(request.CarrierEventCode) && string.IsNullOrWhiteSpace(request.CarrierDescription))
        {
            throw new ValidationException(nameof(request.CarrierEventCode), "Carrier event code or description must be supplied.");
        }

        if (request.OccurredAtUtc == default)
        {
            throw new ValidationException(nameof(request.OccurredAtUtc), "OccurredAtUtc is required.");
        }

        var normalizedTracking = request.TrackingNumber.Trim();
        var normalizedCarrier = request.CarrierCode.Trim().ToUpperInvariant();

        // 2. Lookup Return Shipment by Tracking Number and Carrier
        var shipment = await _context.ReturnShipments
            .Include(s => s.ReturnAuthorization)
            .Include(s => s.DestinationFacility)
            .Include(s => s.OriginFacility)
            .Include(s => s.InboundHandoff)
            .FirstOrDefaultAsync(s => s.TrackingNumber == normalizedTracking &&
                                      s.CarrierCode == normalizedCarrier, cancellationToken);

        if (shipment == null)
        {
            throw new EntityNotFoundException(nameof(ReturnShipment), normalizedTracking);
        }

        // 3. Multi-Tenancy Validation Guards
        if (request.TenantIdOverride.HasValue && request.TenantIdOverride.Value != shipment.ClientId)
        {
            throw new UnauthorizedException($"Tracking number '{normalizedTracking}' belongs to a different client tenant. Cross-tenant reverse tracking rejected.");
        }

        if (_tenantProvider.TenantId.HasValue &&
            !_tenantProvider.BypassTenantFilter &&
            _tenantProvider.TenantId.Value != shipment.ClientId)
        {
            throw new UnauthorizedException($"Unauthorized cross-tenant reverse tracking event attempt for tracking '{normalizedTracking}'.");
        }

        // 4. Custody & State Guards
        if (shipment.Status == ReturnShipmentStatus.Cancelled)
        {
            throw new InvalidDomainStateException($"Cannot process tracking event for cancelled return shipment '{shipment.Id}'.");
        }

        var auth = shipment.ReturnAuthorization ??
            await _context.ReturnAuthorizations.FirstOrDefaultAsync(a => a.Id == shipment.ReturnAuthorizationId, cancellationToken);

        if (auth == null)
        {
            throw new InvalidDomainStateException($"Return shipment '{shipment.Id}' has no linked ReturnAuthorization.");
        }

        // Ensure authorization has the required shipment relationship (cannot be in preliminary Authorized state)
        if (auth.Status == ReturnAuthorizationStatus.Authorized)
        {
            throw new InvalidDomainStateException($"Cannot process tracking event for ReturnAuthorization '{auth.AuthorizationNumber}' in unexecuted Authorized status.");
        }

        // 5. Deduplication Fingerprint (Idempotent Event Ingestion)
        var eventType = !string.IsNullOrWhiteSpace(request.CarrierEventCode)
            ? request.CarrierEventCode.Trim().ToUpperInvariant()
            : "EVENT";

        var fingerprint = ReturnTrackingEvent.ComputeFingerprint(
            shipment.ClientId,
            normalizedCarrier,
            normalizedTracking,
            eventType,
            request.OccurredAtUtc);

        var existingEvent = await _context.ReturnTrackingEvents
            .AnyAsync(e => e.ClientId == shipment.ClientId &&
                           e.CarrierCode == normalizedCarrier &&
                           e.EventFingerprint == fingerprint, cancellationToken);

        if (existingEvent)
        {
            _logger.LogInformation("Duplicate reverse tracking event detected for ReturnShipment '{ShipmentId}', Tracking '{Trk}', Fingerprint '{Fp}'. Skipping idempotently.",
                shipment.Id, normalizedTracking, fingerprint);

            var existingHandoffRef = shipment.InboundHandoff?.HandoffReference;
            if (string.IsNullOrWhiteSpace(existingHandoffRef) && shipment.InboundHandoffId.HasValue)
            {
                var h = await _context.ReturnInboundHandoffs.FindAsync([shipment.InboundHandoffId.Value], cancellationToken);
                existingHandoffRef = h?.HandoffReference;
            }

            return new IngestReturnTrackingResult
            {
                IsSuccess = true,
                IsDuplicate = true,
                ReturnShipmentId = shipment.Id,
                ReturnAuthorizationId = auth.Id,
                TrackingNumber = shipment.TrackingNumber,
                CarrierCode = shipment.CarrierCode,
                ShipmentStatus = shipment.Status,
                AuthorizationStatus = auth.Status,
                CarrierHandoverAtUtc = shipment.CarrierHandoverAtUtc,
                DeliveredAtWarehouseUtc = shipment.DeliveredAtWarehouseUtc,
                InboundHandoffId = shipment.InboundHandoffId,
                InboundHandoffReference = existingHandoffRef,
                Message = "Duplicate reverse tracking event ignored (idempotent)."
            };
        }

        // 6. Normalize Carrier Event Milestone
        var milestone = NormalizeReverseMilestone(request.CarrierEventCode, request.CarrierDescription);

        // 7. Process Lifecycle & Custody Handover Transitions
        Guid? inboundHandoffId = shipment.InboundHandoffId;
        string? inboundHandoffReference = shipment.InboundHandoff?.HandoffReference;

        switch (milestone)
        {
            case ReverseMilestone.CustodyHandover:
            case ReverseMilestone.InTransit:
                // First custody event sets CarrierHandoverAtUtc exactly once
                shipment.RecordCarrierHandover(request.OccurredAtUtc);
                shipment.RecordInTransit(request.OccurredAtUtc);
                auth.RecordInTransit(request.OccurredAtUtc);
                break;

            case ReverseMilestone.OutForDelivery:
                shipment.RecordCarrierHandover(request.OccurredAtUtc);
                shipment.RecordOutForDelivery(request.OccurredAtUtc);
                auth.RecordInTransit(request.OccurredAtUtc);
                break;

            case ReverseMilestone.Delivered:
                // Validate intended destination facility
                ValidateDestinationFacility(request, shipment);

                // Update delivery timestamps and statuses
                shipment.RecordCarrierHandover(request.OccurredAtUtc);
                shipment.RecordDeliveredAtWarehouse(request.OccurredAtUtc);
                auth.RecordDeliveredAtFacility(request.OccurredAtUtc);

                // Create Inbound Receiving Handoff (exactly once, idempotent)
                var handoff = await EnsureInboundHandoffCreatedAsync(shipment, auth, request, cancellationToken);
                inboundHandoffId = handoff.Id;
                inboundHandoffReference = handoff.HandoffReference;
                break;

            case ReverseMilestone.Lost:
                shipment.MarkLost(request.CarrierDescription);
                break;

            case ReverseMilestone.Exception:
                // Operational exception logged in audit timeline; does not regress custody
                _logger.LogWarning("Reverse transit exception recorded for ReturnShipment '{ShipmentId}', Tracking '{Trk}': {Desc}",
                    shipment.Id, normalizedTracking, request.CarrierDescription);
                break;
        }

        // 8. Create Append-Only Return Tracking Event
        var trackingEvent = new ReturnTrackingEvent
        {
            Id = Guid.NewGuid(),
            ClientId = shipment.ClientId,
            ReturnShipmentId = shipment.Id,
            TrackingNumber = normalizedTracking,
            CarrierCode = normalizedCarrier,
            EventType = eventType,
            Status = shipment.Status,
            CarrierStatusCode = request.CarrierEventCode?.Trim(),
            CarrierStatusDescription = string.IsNullOrWhiteSpace(request.CarrierDescription) ? eventType : request.CarrierDescription.Trim(),
            Location = FormatLocation(request.EventCity, request.EventStateOrProvince, request.EventPostalCode, request.EventCountry),
            OccurredAtUtc = request.OccurredAtUtc,
            ReceivedAtUtc = DateTime.UtcNow,
            EventFingerprint = fingerprint
        };

        _context.ReturnTrackingEvents.Add(trackingEvent);

        // 9. Atomic Commit (concurrent duplicate fingerprint on PostgreSQL degrades cleanly to idempotent duplicate result)
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsFingerprintUniqueViolation(ex))
        {
            _context.ClearChangeTracker();
            var committedShipment = await _context.ReturnShipments
                .Include(s => s.ReturnAuthorization)
                .Include(s => s.InboundHandoff)
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == shipment.Id, cancellationToken);

            var handoffRef = committedShipment?.InboundHandoff?.HandoffReference ?? inboundHandoffReference;
            var handoffId = committedShipment?.InboundHandoffId ?? inboundHandoffId;
            return new IngestReturnTrackingResult
            {
                IsSuccess = true,
                IsDuplicate = true,
                ReturnShipmentId = shipment.Id,
                ReturnAuthorizationId = auth.Id,
                TrackingNumber = shipment.TrackingNumber,
                CarrierCode = shipment.CarrierCode,
                ShipmentStatus = committedShipment?.Status ?? shipment.Status,
                AuthorizationStatus = committedShipment?.ReturnAuthorization?.Status ?? auth.Status,
                CarrierHandoverAtUtc = committedShipment?.CarrierHandoverAtUtc ?? shipment.CarrierHandoverAtUtc,
                DeliveredAtWarehouseUtc = committedShipment?.DeliveredAtWarehouseUtc ?? shipment.DeliveredAtWarehouseUtc,
                InboundHandoffId = handoffId,
                InboundHandoffReference = handoffRef,
                Message = "Duplicate reverse tracking event ignored after concurrent commit (idempotent)."
            };
        }

        _logger.LogInformation(
            "Reverse tracking event '{Type}' ingested for ReturnShipment '{ShipmentId}' (Auth '{AuthNum}'). Status: '{Status}', HandoverAt: {Handover}",
            eventType, shipment.Id, auth.AuthorizationNumber, shipment.Status, shipment.CarrierHandoverAtUtc);

        return new IngestReturnTrackingResult
        {
            IsSuccess = true,
            IsDuplicate = false,
            ReturnTrackingEventId = trackingEvent.Id,
            ReturnShipmentId = shipment.Id,
            ReturnAuthorizationId = auth.Id,
            TrackingNumber = shipment.TrackingNumber,
            CarrierCode = shipment.CarrierCode,
            ShipmentStatus = shipment.Status,
            AuthorizationStatus = auth.Status,
            CarrierHandoverAtUtc = shipment.CarrierHandoverAtUtc,
            DeliveredAtWarehouseUtc = shipment.DeliveredAtWarehouseUtc,
            InboundHandoffId = inboundHandoffId,
            InboundHandoffReference = inboundHandoffReference,
            Message = "Reverse tracking event processed successfully."
        };
    }

    public async Task<ReturnInboundHandoffDto?> GetInboundHandoffByReturnShipmentIdAsync(
        Guid returnShipmentId,
        CancellationToken cancellationToken = default)
    {
        var handoff = await _context.ReturnInboundHandoffs
            .Include(h => h.DestinationFacility)
            .FirstOrDefaultAsync(h => h.ReturnShipmentId == returnShipmentId, cancellationToken);

        return handoff == null ? null : MapToHandoffDto(handoff);
    }

    public async Task<ReturnInboundHandoffDto?> GetInboundHandoffByAuthorizationIdAsync(
        Guid returnAuthorizationId,
        CancellationToken cancellationToken = default)
    {
        var handoff = await _context.ReturnInboundHandoffs
            .Include(h => h.DestinationFacility)
            .FirstOrDefaultAsync(h => h.ReturnAuthorizationId == returnAuthorizationId, cancellationToken);

        return handoff == null ? null : MapToHandoffDto(handoff);
    }

    public async Task<IReadOnlyList<ReturnTrackingTimelineEventDto>> GetReturnTrackingHistoryAsync(
        Guid returnShipmentId,
        CancellationToken cancellationToken = default)
    {
        var events = await _context.ReturnTrackingEvents
            .Where(e => e.ReturnShipmentId == returnShipmentId)
            .OrderBy(e => e.OccurredAtUtc)
            .Select(e => new ReturnTrackingTimelineEventDto
            {
                Id = e.Id,
                EventType = e.EventType,
                Status = e.Status,
                CarrierStatusCode = e.CarrierStatusCode,
                CarrierStatusDescription = e.CarrierStatusDescription,
                Location = e.Location,
                OccurredAtUtc = e.OccurredAtUtc,
                ReceivedAtUtc = e.ReceivedAtUtc
            })
            .ToListAsync(cancellationToken);

        return events;
    }

    #region Private Helpers

    private enum ReverseMilestone
    {
        CustodyHandover,
        InTransit,
        OutForDelivery,
        Delivered,
        Exception,
        Lost
    }

    private static ReverseMilestone NormalizeReverseMilestone(string? eventCode, string? description)
    {
        var code = eventCode?.Trim().ToUpperInvariant() ?? string.Empty;
        var desc = description?.Trim().ToLowerInvariant() ?? string.Empty;

        // 1. Delivered
        if (code is "DL" or "DEL" or "DELIVERED" or "01" or "D" ||
            desc.Contains("delivered") || desc.Contains("delivery complete") || desc.Contains("received at dock") || desc.Contains("dock delivery"))
        {
            return ReverseMilestone.Delivered;
        }

        // 2. Out for Delivery
        if (code is "OD" or "OFD" or "OUTFORDELIVERY" or "OUT_FOR_DELIVERY" or "ON_VAN" or "ON_VEHICLE" or "ON_DELIVERY_VEHICLE" ||
            desc.Contains("out for delivery") || desc.Contains("on vehicle") || desc.Contains("delivery vehicle"))
        {
            return ReverseMilestone.OutForDelivery;
        }

        // 3. Custody Handover / Origin Acceptance
        if (code is "PU" or "PICKUP" or "ACCEPTED" or "OC" or "ORIGIN" or "TENDERED" or "MANIFEST_CREATED" ||
            desc.Contains("picked up") || desc.Contains("carrier received") || desc.Contains("acceptance") || desc.Contains("tendered"))
        {
            return ReverseMilestone.CustodyHandover;
        }

        // 4. Lost in Return
        if (code is "LOST" or "DAMAGED_DESTROYED" ||
            desc.Contains("lost in transit") || desc.Contains("parcel destroyed") || desc.Contains("total loss"))
        {
            return ReverseMilestone.Lost;
        }

        // 5. Exception
        if (code is "EX" or "EXCEPTION" or "DELAY" or "ATTEMPT_FAILED" or "WEATHER" ||
            desc.Contains("exception") || desc.Contains("delay") || desc.Contains("failed") || desc.Contains("weather"))
        {
            return ReverseMilestone.Exception;
        }

        // 6. Default: In Transit
        return ReverseMilestone.InTransit;
    }

    private static void ValidateDestinationFacility(IngestReturnTrackingRequest request, ReturnShipment shipment)
    {
        // 1. Validate Facility Code if supplied in webhook
        if (!string.IsNullOrWhiteSpace(request.DestinationFacilityCode))
        {
            var requestedCode = request.DestinationFacilityCode.Trim().ToUpperInvariant();
            var authorizedCode = shipment.DestinationFacility?.FacilityCode?.Trim().ToUpperInvariant();

            if (!string.IsNullOrWhiteSpace(authorizedCode) && !string.Equals(requestedCode, authorizedCode, StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(
                    nameof(request.DestinationFacilityCode),
                    $"Delivery destination facility '{request.DestinationFacilityCode}' does not match authorized return destination facility '{authorizedCode}'.");
            }
        }

        // 2. Validate Postal Code if supplied in webhook
        if (!string.IsNullOrWhiteSpace(request.EventPostalCode) && !string.IsNullOrWhiteSpace(shipment.DestinationPostalCode))
        {
            var eventPostal = request.EventPostalCode.Trim();
            var destPostal = shipment.DestinationPostalCode.Trim();

            // Compare prefix if full match is not exact (e.g. 5-digit vs 5+4 zip)
            if (!eventPostal.StartsWith(destPostal, StringComparison.OrdinalIgnoreCase) &&
                !destPostal.StartsWith(eventPostal, StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(
                    nameof(request.EventPostalCode),
                    $"Delivery postal code '{eventPostal}' does not match authorized destination facility postal code '{destPostal}'.");
            }
        }
    }

    private async Task<ReturnInboundHandoff> EnsureInboundHandoffCreatedAsync(
        ReturnShipment shipment,
        ReturnAuthorization auth,
        IngestReturnTrackingRequest request,
        CancellationToken cancellationToken)
    {
        // Check if handoff already exists for this return shipment
        var existing = shipment.InboundHandoff ??
            await _context.ReturnInboundHandoffs
                .FirstOrDefaultAsync(h => h.ReturnShipmentId == shipment.Id, cancellationToken);

        if (existing != null)
        {
            return existing;
        }

        // Double check by authorization
        existing = await _context.ReturnInboundHandoffs
            .FirstOrDefaultAsync(h => h.ReturnAuthorizationId == auth.Id, cancellationToken);

        if (existing != null)
        {
            shipment.InboundHandoffId = existing.Id;
            return existing;
        }

        var handoffRef = $"HND-{shipment.ReturnRmaNumber}-{Guid.NewGuid():N}"[..24].ToUpperInvariant();
        var handoff = ReturnInboundHandoff.Create(
            clientId: shipment.ClientId,
            destinationFacilityId: shipment.DestinationFacilityId,
            returnShipmentId: shipment.Id,
            returnAuthorizationId: auth.Id,
            originalPackageId: shipment.PackageId,
            originalOrderId: shipment.OrderId,
            handoffReference: handoffRef,
            returnRmaNumber: shipment.ReturnRmaNumber,
            trackingNumber: shipment.TrackingNumber,
            carrierCode: shipment.CarrierCode,
            carrierDeliveredAtUtc: request.OccurredAtUtc,
            dockDoorLocation: request.DockDoorLocation ?? "DOCK-RECEIVING-GEN",
            notes: $"Carrier delivered return parcel to facility '{shipment.DestinationFacility?.Name ?? shipment.DestinationFacilityId.ToString()}'."
        );

        _context.ReturnInboundHandoffs.Add(handoff);
        shipment.InboundHandoffId = handoff.Id;
        auth.InboundHandoffId = handoff.Id;

        return handoff;
    }

    private static string? FormatLocation(string? city, string? state, string? postalCode, string? country)
    {
        var parts = new[] { city, state, postalCode, country }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim())
            .ToList();

        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }

    private static ReturnInboundHandoffDto MapToHandoffDto(ReturnInboundHandoff handoff)
    {
        return new ReturnInboundHandoffDto
        {
            Id = handoff.Id,
            ClientId = handoff.ClientId,
            DestinationFacilityId = handoff.DestinationFacilityId,
            DestinationFacilityName = handoff.DestinationFacility?.Name ?? string.Empty,
            ReturnShipmentId = handoff.ReturnShipmentId,
            ReturnAuthorizationId = handoff.ReturnAuthorizationId,
            OriginalPackageId = handoff.OriginalPackageId,
            OriginalOrderId = handoff.OriginalOrderId,
            HandoffReference = handoff.HandoffReference,
            ReturnRmaNumber = handoff.ReturnRmaNumber,
            TrackingNumber = handoff.TrackingNumber,
            CarrierCode = handoff.CarrierCode,
            Status = handoff.Status,
            CarrierDeliveredAtUtc = handoff.CarrierDeliveredAtUtc,
            HandoffCreatedAtUtc = handoff.HandoffCreatedAtUtc,
            DockDoorLocation = handoff.DockDoorLocation,
            Notes = handoff.Notes
        };
    }

    private static bool IsFingerprintUniqueViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException pgEx &&
           pgEx.SqlState == PostgresErrorCodes.UniqueViolation &&
           pgEx.ConstraintName != null &&
           pgEx.ConstraintName.Contains("ix_return_tracking_events_fingerprint", StringComparison.OrdinalIgnoreCase);

    #endregion
}
