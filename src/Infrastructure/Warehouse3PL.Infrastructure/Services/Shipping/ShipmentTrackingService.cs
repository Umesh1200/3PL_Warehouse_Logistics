using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services.Shipping;

/// <summary>
/// Authoritative post-dispatch shipment tracking and delivery rollup engine.
/// Coordinates push webhook event ingestion, carrier polling synchronization, monotonic status transitions,
/// fingerprint deduplication, and order-level multi-package delivery rollup.
/// DB-SPEC-3PL-2026 Phase 4.5 Outbound Shipment Tracking & Delivery Confirmation.
/// </summary>
public class ShipmentTrackingService : IShipmentTrackingService
{
    private readonly IApplicationDbContext _context;
    private readonly IShippingCarrierService _carrierService;
    private readonly ILogger<ShipmentTrackingService> _logger;

    public ShipmentTrackingService(
        IApplicationDbContext context,
        IShippingCarrierService carrierService,
        ILogger<ShipmentTrackingService> logger)
    {
        _context = context;
        _carrierService = carrierService;
        _logger = logger;
    }

    public async Task<IngestTrackingEventResult> IngestTrackingEventAsync(
        IngestTrackingEventRequest request,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate Input
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

        var normalizedTracking = request.TrackingNumber.Trim();
        var normalizedCarrier = request.CarrierCode.Trim();

        // 2. Lookup Package by Tracking Number
        var package = await _context.Packages
            .Include(p => p.Shipment)
            .Include(p => p.OutboundOrder)
            .FirstOrDefaultAsync(p => p.TrackingNumber == normalizedTracking && !p.IsDeleted, cancellationToken);

        if (package == null)
        {
            throw new EntityNotFoundException(nameof(Package), normalizedTracking);
        }

        // Cross-Tenant Validation Guard
        if (request.TenantIdOverride.HasValue && request.TenantIdOverride.Value != package.ClientId)
        {
            throw new ValidationException(nameof(request.TrackingNumber),
                $"Tracking number '{normalizedTracking}' belongs to a different client tenant. Cross-tenant tracking ingestion rejected.");
        }

        // 3. Compute Deduplication Fingerprint
        string fingerprint = ComputeEventFingerprint(normalizedCarrier, normalizedTracking, request.CarrierEventId, request.CarrierEventCode, request.OccurredAtUtc);

        // Check if event was already ingested
        var existingEvent = await _context.ShipmentTrackingEvents
            .AnyAsync(e => e.ClientId == package.ClientId &&
                           e.CarrierCode == normalizedCarrier &&
                           e.EventFingerprint == fingerprint, cancellationToken);

        if (existingEvent)
        {
            _logger.LogInformation("Duplicate tracking event detected for Package '{Pkg}', Tracking '{Trk}', Fingerprint '{Fp}'. Skipping.",
                package.PackageNumber, normalizedTracking, fingerprint);

            return new IngestTrackingEventResult
            {
                IsSuccess = true,
                IsDuplicate = true,
                PackageId = package.Id,
                ShipmentId = package.ShipmentId,
                OutboundOrderId = package.OutboundOrderId,
                NormalizedMilestone = package.CurrentTrackingStatus,
                PackageTrackingStatus = package.CurrentTrackingStatus,
                OrderDeliveryStatus = package.OutboundOrder?.DeliveryStatus ?? OrderDeliveryStatus.Dispatched,
                Message = "Duplicate tracking event ignored (Idempotent)."
            };
        }

        // 4. Normalize Carrier Event to 3PL Milestone
        var milestone = NormalizeCarrierMilestone(request.CarrierEventCode, request.CarrierDescription);

        // 5. Create Append-Only Tracking Event
        var trackingEvent = new ShipmentTrackingEvent
        {
            ClientId = package.ClientId,
            FacilityId = package.FacilityId,
            PackageId = package.Id,
            ShipmentId = package.ShipmentId,
            CarrierCode = normalizedCarrier,
            TrackingNumber = normalizedTracking,
            CarrierEventId = request.CarrierEventId?.Trim(),
            MilestoneStatus = milestone,
            CarrierEventCode = request.CarrierEventCode.Trim(),
            CarrierDescription = string.IsNullOrWhiteSpace(request.CarrierDescription) ? milestone.ToString() : request.CarrierDescription.Trim(),
            EventCity = request.EventCity?.Trim(),
            EventStateOrProvince = request.EventStateOrProvince?.Trim(),
            EventPostalCode = request.EventPostalCode?.Trim(),
            EventCountry = request.EventCountry?.Trim(),
            OccurredAtUtc = request.OccurredAtUtc,
            ReceivedAtUtc = DateTime.UtcNow,
            EventFingerprint = fingerprint,
            RawPayloadJson = request.RawPayloadJson
        };

        _context.ShipmentTrackingEvents.Add(trackingEvent);

        // 6. Update Package Status Enforcing Monotonic Progression
        package.UpdateTrackingMilestone(milestone, request.OccurredAtUtc);

        // 7. Recalculate Shipment Delivery Status (if linked)
        if (package.Shipment != null)
        {
            // Load all other packages in this shipment to evaluate accurate status
            var shipmentPackages = await _context.Packages
                .Where(p => p.ShipmentId == package.ShipmentId && !p.IsDeleted)
                .ToListAsync(cancellationToken);

            // Replace or update current package in local collection
            var existingInList = shipmentPackages.FirstOrDefault(p => p.Id == package.Id);
            if (existingInList != null)
            {
                existingInList.CurrentTrackingStatus = package.CurrentTrackingStatus;
                existingInList.DeliveredAtUtc = package.DeliveredAtUtc;
            }

            package.Shipment.RecalculateDeliveryStatus();
        }

        // 8. Recalculate Outbound Order Delivery Status
        var order = await _context.OutboundOrders
            .Include(o => o.Packages)
            .FirstOrDefaultAsync(o => o.Id == package.OutboundOrderId && !o.IsDeleted, cancellationToken);

        if (order != null)
        {
            var existingPkgInOrder = order.Packages.FirstOrDefault(p => p.Id == package.Id);
            if (existingPkgInOrder != null)
            {
                existingPkgInOrder.CurrentTrackingStatus = package.CurrentTrackingStatus;
                existingPkgInOrder.DeliveredAtUtc = package.DeliveredAtUtc;
            }

            order.RecalculateDeliveryStatus();
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("ix_shipment_tracking_events_tenant_fingerprint") == true ||
                                           ex.Message.Contains("ix_shipment_tracking_events_tenant_fingerprint"))
        {
            // Race condition: another concurrent webhook thread inserted the same fingerprint simultaneously
            _logger.LogWarning("Concurrent duplicate tracking event race handled gracefully for Fingerprint '{Fp}'", fingerprint);
            return new IngestTrackingEventResult
            {
                IsSuccess = true,
                IsDuplicate = true,
                PackageId = package.Id,
                ShipmentId = package.ShipmentId,
                OutboundOrderId = package.OutboundOrderId,
                NormalizedMilestone = milestone,
                PackageTrackingStatus = package.CurrentTrackingStatus,
                OrderDeliveryStatus = order?.DeliveryStatus ?? OrderDeliveryStatus.Dispatched,
                Message = "Duplicate tracking event ignored (Concurrent Idempotent Hit)."
            };
        }

        _logger.LogInformation(
            "Ingested Tracking Event for Package '{Pkg}' ({Trk}): Status '{Status}', Order '{Order}' DeliveryStatus '{OrdStatus}'",
            package.PackageNumber, normalizedTracking, package.CurrentTrackingStatus, order?.OrderNumber, order?.DeliveryStatus);

        return new IngestTrackingEventResult
        {
            IsSuccess = true,
            IsDuplicate = false,
            TrackingEventId = trackingEvent.Id,
            PackageId = package.Id,
            ShipmentId = package.ShipmentId,
            OutboundOrderId = package.OutboundOrderId,
            NormalizedMilestone = milestone,
            PackageTrackingStatus = package.CurrentTrackingStatus,
            OrderDeliveryStatus = order?.DeliveryStatus ?? OrderDeliveryStatus.Dispatched,
            Message = "Tracking event ingested and statuses updated successfully."
        };
    }

    public async Task<SyncShipmentTrackingResult> SyncShipmentTrackingAsync(
        SyncShipmentTrackingRequest request,
        CancellationToken cancellationToken = default)
    {
        var shipment = await _context.Shipments
            .Include(s => s.Packages)
            .FirstOrDefaultAsync(s => s.Id == request.ShipmentId && !s.IsDeleted, cancellationToken);

        if (shipment == null)
        {
            throw new EntityNotFoundException(nameof(Shipment), request.ShipmentId);
        }

        // Active eligibility check: Only synchronize active, dispatched shipments
        if (shipment.Status != ShipmentStatus.Dispatched && shipment.Status != ShipmentStatus.ReadyForDispatch)
        {
            return new SyncShipmentTrackingResult
            {
                IsSuccess = false,
                ShipmentId = shipment.Id,
                ShipmentDeliveryStatus = shipment.DeliveryStatus,
                Message = $"Shipment '{shipment.ShipmentNumber}' with status '{shipment.Status}' is not eligible for carrier tracking sync."
            };
        }

        // Idempotent: If shipment is already fully Delivered, skip network calls
        if (shipment.DeliveryStatus == TrackingMilestoneStatus.Delivered)
        {
            return new SyncShipmentTrackingResult
            {
                IsSuccess = true,
                ShipmentId = shipment.Id,
                PackagesEvaluated = shipment.Packages.Count,
                NewEventsIngested = 0,
                DuplicateEventsSkipped = 0,
                ShipmentDeliveryStatus = shipment.DeliveryStatus,
                Message = "Shipment is already fully Delivered (Idempotent sync)."
            };
        }

        int evaluated = 0;
        int newEvents = 0;
        int duplicateEvents = 0;

        foreach (var package in shipment.Packages.Where(p => !string.IsNullOrWhiteSpace(p.TrackingNumber)))
        {
            evaluated++;
            var carrier = package.CarrierCode ?? shipment.CarrierCode;

            try
            {
                // Call external/simulated carrier tracking API
                var trackingResponse = await _carrierService.TrackShipmentAsync(package.TrackingNumber!, carrier, cancellationToken);

                if (!trackingResponse.IsSuccess || trackingResponse.Events.Count == 0)
                {
                    _logger.LogWarning("Carrier returned failure or 0 events for Tracking '{Trk}'. Keeping last valid status.", package.TrackingNumber);
                    continue;
                }

                // Ingest carrier events in chronological order
                foreach (var ev in trackingResponse.Events.OrderBy(e => e.TimestampUtc))
                {
                    var ingestResult = await IngestTrackingEventAsync(new IngestTrackingEventRequest
                    {
                        CarrierCode = carrier,
                        TrackingNumber = package.TrackingNumber!,
                        CarrierEventCode = ev.Status,
                        CarrierDescription = ev.Description,
                        OccurredAtUtc = ev.TimestampUtc,
                        EventCity = ev.Location,
                        TenantIdOverride = package.ClientId
                    }, cancellationToken);

                    if (ingestResult.IsSuccess)
                    {
                        if (ingestResult.IsDuplicate)
                        {
                            duplicateEvents++;
                        }
                        else
                        {
                            newEvents++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Bounded failure isolation: a network/carrier error for 1 package does NOT alter previous valid status
                _logger.LogError(ex, "Failed to sync tracking with carrier '{Carrier}' for Package '{Pkg}' ({Trk})",
                    carrier, package.PackageNumber, package.TrackingNumber);
            }
        }

        // Refresh shipment delivery status after sync
        var refreshedShipment = await _context.Shipments
            .FirstOrDefaultAsync(s => s.Id == shipment.Id, cancellationToken);

        return new SyncShipmentTrackingResult
        {
            IsSuccess = true,
            ShipmentId = shipment.Id,
            PackagesEvaluated = evaluated,
            NewEventsIngested = newEvents,
            DuplicateEventsSkipped = duplicateEvents,
            ShipmentDeliveryStatus = refreshedShipment?.DeliveryStatus ?? shipment.DeliveryStatus,
            Message = $"Synced {evaluated} packages: {newEvents} new events, {duplicateEvents} duplicates skipped."
        };
    }

    public async Task<EvaluateOrderDeliveryRollupResult> EvaluateOrderDeliveryRollupAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await _context.OutboundOrders
            .Include(o => o.Packages)
            .FirstOrDefaultAsync(o => o.Id == orderId && !o.IsDeleted, cancellationToken);

        if (order == null)
        {
            throw new EntityNotFoundException(nameof(OutboundOrder), orderId);
        }

        order.RecalculateDeliveryStatus();
        await _context.SaveChangesAsync(cancellationToken);

        return new EvaluateOrderDeliveryRollupResult
        {
            OutboundOrderId = order.Id,
            DeliveryStatus = order.DeliveryStatus,
            TotalPackages = order.Packages.Count,
            DeliveredPackages = order.Packages.Count(p => p.CurrentTrackingStatus == TrackingMilestoneStatus.Delivered),
            InTransitPackages = order.Packages.Count(p => p.CurrentTrackingStatus == TrackingMilestoneStatus.InTransit ||
                                                         p.CurrentTrackingStatus == TrackingMilestoneStatus.OutForDelivery ||
                                                         p.CurrentTrackingStatus == TrackingMilestoneStatus.Accepted),
            ExceptionPackages = order.Packages.Count(p => p.CurrentTrackingStatus == TrackingMilestoneStatus.DeliveryException),
            DeliveredAtUtc = order.DeliveredAtUtc
        };
    }

    public async Task<PackageTrackingTimelineDto?> GetPackageTrackingTimelineAsync(
        Guid packageId,
        CancellationToken cancellationToken = default)
    {
        var package = await _context.Packages
            .Include(p => p.TrackingEvents)
            .FirstOrDefaultAsync(p => p.Id == packageId && !p.IsDeleted, cancellationToken);

        if (package == null) return null;

        return new PackageTrackingTimelineDto
        {
            PackageId = package.Id,
            PackageNumber = package.PackageNumber,
            TrackingNumber = package.TrackingNumber ?? string.Empty,
            CarrierCode = package.CarrierCode ?? string.Empty,
            CurrentTrackingStatus = package.CurrentTrackingStatus,
            DeliveredAtUtc = package.DeliveredAtUtc,
            Events = package.TrackingEvents
                .OrderBy(e => e.OccurredAtUtc)
                .Select(e => new TrackingTimelineEventDto
                {
                    Id = e.Id,
                    MilestoneStatus = e.MilestoneStatus,
                    CarrierEventCode = e.CarrierEventCode,
                    CarrierDescription = e.CarrierDescription,
                    Location = string.Join(", ", new[] { e.EventCity, e.EventStateOrProvince, e.EventCountry }.Where(s => !string.IsNullOrWhiteSpace(s))),
                    OccurredAtUtc = e.OccurredAtUtc,
                    ReceivedAtUtc = e.ReceivedAtUtc
                }).ToList()
        };
    }

    public async Task<EvaluateOrderDeliveryRollupResult?> GetOrderDeliveryStatusAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await _context.OutboundOrders
            .Include(o => o.Packages)
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orderId && !o.IsDeleted, cancellationToken);

        if (order == null) return null;

        return new EvaluateOrderDeliveryRollupResult
        {
            OutboundOrderId = order.Id,
            DeliveryStatus = order.DeliveryStatus,
            TotalPackages = order.Packages.Count,
            DeliveredPackages = order.Packages.Count(p => p.CurrentTrackingStatus == TrackingMilestoneStatus.Delivered),
            InTransitPackages = order.Packages.Count(p => p.CurrentTrackingStatus == TrackingMilestoneStatus.InTransit ||
                                                         p.CurrentTrackingStatus == TrackingMilestoneStatus.OutForDelivery ||
                                                         p.CurrentTrackingStatus == TrackingMilestoneStatus.Accepted),
            ExceptionPackages = order.Packages.Count(p => p.CurrentTrackingStatus == TrackingMilestoneStatus.DeliveryException),
            DeliveredAtUtc = order.DeliveredAtUtc
        };
    }

    #region Helper Methods

    private static string ComputeEventFingerprint(
        string carrierCode,
        string trackingNumber,
        string? carrierEventId,
        string carrierEventCode,
        DateTime occurredAtUtc)
    {
        string raw;
        if (!string.IsNullOrWhiteSpace(carrierEventId))
        {
            // Prefer unique carrier-supplied event ID
            raw = $"{carrierCode.ToUpperInvariant()}:{trackingNumber.ToUpperInvariant()}:ID:{carrierEventId.Trim()}";
        }
        else
        {
            raw = $"{carrierCode.ToUpperInvariant()}:{trackingNumber.ToUpperInvariant()}:{carrierEventCode.ToUpperInvariant()}:{occurredAtUtc:yyyy-MM-ddTHH:mm:ss.fffZ}";
        }

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public static TrackingMilestoneStatus NormalizeCarrierMilestone(string eventCode, string description)
    {
        var code = eventCode?.Trim().ToUpperInvariant() ?? string.Empty;
        var desc = description?.Trim().ToLowerInvariant() ?? string.Empty;

        // 1. Delivered
        if (code is "DL" or "DEL" or "DELIVERED" or "01" or "D" ||
            desc.Contains("delivered") || desc.Contains("delivery complete") || desc.Contains("signed by"))
        {
            return TrackingMilestoneStatus.Delivered;
        }

        // 2. Out for Delivery
        if (code is "OD" or "OFD" or "OUTFORDELIVERY" or "ON_VAN" or "ON_VEHICLE" ||
            desc.Contains("out for delivery") || desc.Contains("on vehicle for delivery") || desc.Contains("loaded on delivery vehicle"))
        {
            return TrackingMilestoneStatus.OutForDelivery;
        }

        // 3. In Transit
        if (code is "IT" or "IN_TRANSIT" or "TRANSIT" or "AR" or "DP" or "DEPARTED" or "ARRIVED" or "SORTING" ||
            desc.Contains("in transit") || desc.Contains("departed") || desc.Contains("arrived at") || desc.Contains("sort facility") || desc.Contains("hub"))
        {
            return TrackingMilestoneStatus.InTransit;
        }

        // 4. Accepted / Origin Pickup
        if (code is "PU" or "PICKUP" or "ACCEPTED" or "OC" or "ORIGIN" or "MANIFESTCREATED" or "TENDERED" ||
            desc.Contains("picked up") || desc.Contains("carrier received") || desc.Contains("acceptance") || desc.Contains("manifest created") || desc.Contains("tendered"))
        {
            return TrackingMilestoneStatus.Accepted;
        }

        // 5. Exception
        if (code is "EX" or "EXCEPTION" or "DELAY" or "D01" or "D02" or "ATTEMPT_FAILED" or "WEATHER" ||
            desc.Contains("exception") || desc.Contains("delay") || desc.Contains("attempted") || desc.Contains("failed") || desc.Contains("closed") || desc.Contains("weather"))
        {
            return TrackingMilestoneStatus.DeliveryException;
        }

        // 6. Return to Sender
        if (code is "RTS" or "RETURNED" or "RETURN_TO_SENDER" ||
            desc.Contains("return to sender") || desc.Contains("returning to origin"))
        {
            return TrackingMilestoneStatus.ReturnedToSender;
        }

        // Fallback default
        return TrackingMilestoneStatus.InTransit;
    }

    #endregion
}

