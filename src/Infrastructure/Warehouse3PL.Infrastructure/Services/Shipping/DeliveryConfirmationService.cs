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
/// Authoritative delivery confirmation service for outbound packages.
/// Validates finalized Proof of Delivery evidence, transitions package tracking status to Delivered,
/// atomically records immutable POD_CONFIRMED tracking events, and synchronizes multi-package and multi-shipment
/// order delivery status rollups while strictly preserving warehouse inventory isolation (ΔQ = 0).
/// DB-SPEC-3PL-2026 Phase 4.5 Shipment Tracking & Delivery Confirmation (Step 3A Sub-Step 4).
/// </summary>
public class DeliveryConfirmationService : IDeliveryConfirmationService
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ILogger<DeliveryConfirmationService> _logger;

    public DeliveryConfirmationService(
        IApplicationDbContext context,
        ITenantProvider tenantProvider,
        ICurrentUserContext currentUserContext,
        ILogger<DeliveryConfirmationService> logger)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _currentUserContext = currentUserContext;
        _logger = logger;
    }

    public async Task<DeliveryConfirmationResult> ConfirmDeliveryByPackageIdAsync(
        Guid packageId,
        CancellationToken cancellationToken = default)
    {
        return await ConfirmDeliveryAsync(new ConfirmPackageDeliveryRequest { PackageId = packageId }, cancellationToken);
    }

    public async Task<DeliveryConfirmationResult> ConfirmDeliveryAsync(
        ConfirmPackageDeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate Input & Lookup Package
        if (request.PackageId == Guid.Empty)
        {
            throw new ValidationException(nameof(request.PackageId), "Package ID must be provided.");
        }

        var package = await _context.Packages
            .Include(p => p.Shipment)
            .Include(p => p.OutboundOrder)
            .FirstOrDefaultAsync(p => p.Id == request.PackageId && !p.IsDeleted, cancellationToken);

        if (package == null)
        {
            throw new EntityNotFoundException(nameof(Package), request.PackageId);
        }

        // Tenant Isolation Check
        if (request.TenantIdOverride.HasValue && request.TenantIdOverride.Value != package.ClientId)
        {
            throw new ValidationException(nameof(request.PackageId),
                $"Package '{package.PackageNumber}' belongs to a different client tenant. Cross-tenant access rejected.");
        }

        if (_tenantProvider.TenantId.HasValue &&
            !_tenantProvider.BypassTenantFilter &&
            _tenantProvider.TenantId.Value != package.ClientId)
        {
            throw new UnauthorizedException("Unauthorized cross-tenant delivery confirmation attempt.");
        }

        // Facility Authorization Check
        if (_currentUserContext.FacilityId.HasValue &&
            _currentUserContext.FacilityId.Value != package.FacilityId &&
            !_currentUserContext.IsSuperAdmin)
        {
            throw new ForbiddenException($"User is not authorized for package facility '{package.FacilityId}'.");
        }

        // Package State Validation
        if (package.Status == PackageStatus.Cancelled || package.Status == PackageStatus.Open)
        {
            throw new InvalidDomainStateException(
                $"Cannot confirm delivery for package '{package.PackageNumber}' in status '{package.Status}'. Package must be Packed, Staged, or Loaded.");
        }

        // Carrier Dispatch & Custody-Transfer Safeguard
        if (package.Shipment == null || package.Shipment.Status != ShipmentStatus.Dispatched)
        {
            throw new InvalidDomainStateException(
                $"Cannot confirm delivery for package '{package.PackageNumber}'. The associated shipment must complete authorized custody-transfer and dispatch before carrier delivery can be confirmed.");
        }

        if (string.IsNullOrWhiteSpace(package.TrackingNumber) || string.IsNullOrWhiteSpace(package.CarrierCode))
        {
            throw new ValidationException(nameof(request.PackageId),
                $"Package '{package.PackageNumber}' does not have an assigned tracking number or carrier code.");
        }

        // 2. Retrieve & Validate Authoritative Active Proof of Delivery
        ProofOfDelivery? pod;
        if (request.ProofOfDeliveryId.HasValue)
        {
            pod = await _context.ProofOfDeliveries
                .FirstOrDefaultAsync(p => p.Id == request.ProofOfDeliveryId.Value, cancellationToken);

            if (pod == null)
            {
                throw new EntityNotFoundException(nameof(ProofOfDelivery), request.ProofOfDeliveryId.Value);
            }

            if (pod.PackageId != package.Id)
            {
                throw new ValidationException(nameof(request.ProofOfDeliveryId),
                    $"Proof of Delivery '{pod.Id}' does not belong to package '{package.PackageNumber}'.");
            }

            if (pod.ClientId != package.ClientId)
            {
                throw new UnauthorizedException("Proof of Delivery belongs to a different client tenant.");
            }

            if (pod.IsSuperseded)
            {
                throw new InvalidDomainStateException(
                    $"Cannot confirm delivery using superseded Proof of Delivery '{pod.Id}'.");
            }
        }
        else
        {
            pod = await _context.ProofOfDeliveries
                .FirstOrDefaultAsync(p => p.PackageId == package.Id && !p.IsSuperseded, cancellationToken);

            if (pod == null)
            {
                throw new ValidationException(nameof(request.PackageId),
                    $"Cannot confirm delivery for package '{package.PackageNumber}' without an authoritative Proof of Delivery record.");
            }
        }

        // Verify POD storage finalization: Evidence must be finalized before becoming authoritative
        if (!pod.IsStorageFinalized)
        {
            throw new ValidationException(nameof(request.PackageId),
                $"Proof of Delivery evidence for package '{package.PackageNumber}' is still pending storage finalization. Cannot confirm delivery until evidence is finalized.");
        }

        // Delivery Timestamp Evaluation
        var deliveredAtUtc = request.DeliveredAtUtcOverride ?? pod.DeliveredAtUtc;
        if (deliveredAtUtc > DateTime.UtcNow.AddHours(2))
        {
            throw new ValidationException(nameof(deliveredAtUtc), "Delivery timestamp cannot be in the future.");
        }

        // 3. Idempotency Check: Package Already Confirmed Delivered
        if (package.CurrentTrackingStatus == TrackingMilestoneStatus.Delivered)
        {
            _logger.LogInformation("Package '{Pkg}' is already confirmed Delivered. Returning idempotent confirmation.", package.PackageNumber);

            return new DeliveryConfirmationResult
            {
                IsSuccess = true,
                IsAlreadyDelivered = true,
                PackageId = package.Id,
                PackageNumber = package.PackageNumber,
                TrackingNumber = package.TrackingNumber,
                CarrierCode = package.CarrierCode,
                ProofOfDeliveryId = pod.Id,
                DeliveredAtUtc = package.DeliveredAtUtc ?? deliveredAtUtc,
                PackageTrackingStatus = package.CurrentTrackingStatus,
                OrderDeliveryStatus = package.OutboundOrder?.DeliveryStatus ?? OrderDeliveryStatus.Delivered,
                OutboundOrderId = package.OutboundOrderId,
                OrderNumber = package.OutboundOrder?.OrderNumber,
                ShipmentId = package.ShipmentId,
                Message = "Package delivery is already confirmed (Idempotent)."
            };
        }

        // 4. Confirm Package Delivery (Terminal Monotonic State)
        package.ConfirmDelivery(deliveredAtUtc);

        // 5. Append Authoritative POD_CONFIRMED Tracking Event Atomically
        var rawFingerprint = $"{package.CarrierCode.ToUpperInvariant()}:{package.TrackingNumber.ToUpperInvariant()}:POD_CONFIRMED:{deliveredAtUtc:yyyy-MM-ddTHH:mm:ss.fffZ}";
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawFingerprint))).ToLowerInvariant();

        var eventExists = await _context.ShipmentTrackingEvents
            .AnyAsync(e => e.ClientId == package.ClientId &&
                           e.CarrierCode == package.CarrierCode &&
                           e.EventFingerprint == fingerprint, cancellationToken);

        if (!eventExists)
        {
            var trackingEvent = new ShipmentTrackingEvent
            {
                ClientId = package.ClientId,
                FacilityId = package.FacilityId,
                PackageId = package.Id,
                ShipmentId = package.ShipmentId,
                CarrierCode = package.CarrierCode,
                TrackingNumber = package.TrackingNumber,
                MilestoneStatus = TrackingMilestoneStatus.Delivered,
                CarrierEventCode = "POD_CONFIRMED",
                CarrierDescription = $"Proof of Delivery confirmed via {pod.EvidenceSource}. Recipient: {pod.RecipientName ?? "N/A"}. Signed: {pod.IsSigned}.",
                OccurredAtUtc = deliveredAtUtc,
                ReceivedAtUtc = DateTime.UtcNow,
                EventFingerprint = fingerprint
            };
            _context.ShipmentTrackingEvents.Add(trackingEvent);
        }

        // 6. Recalculate Shipment Delivery Status
        if (package.ShipmentId.HasValue)
        {
            var shipmentPackages = await _context.Packages
                .Where(p => p.ShipmentId == package.ShipmentId && !p.IsDeleted)
                .ToListAsync(cancellationToken);

            var localInShipment = shipmentPackages.FirstOrDefault(p => p.Id == package.Id);
            if (localInShipment != null)
            {
                localInShipment.CurrentTrackingStatus = TrackingMilestoneStatus.Delivered;
                localInShipment.DeliveredAtUtc = deliveredAtUtc;
            }

            package.Shipment?.RecalculateDeliveryStatus();
        }

        // 7. Recalculate Outbound Order Delivery Status Rollup Across All Packages & Shipments
        OutboundOrder? order = null;
        if (package.OutboundOrderId != Guid.Empty)
        {
            order = await _context.OutboundOrders
                .Include(o => o.Packages)
                .FirstOrDefaultAsync(o => o.Id == package.OutboundOrderId && !o.IsDeleted, cancellationToken);

            if (order != null)
            {
                var localInOrder = order.Packages.FirstOrDefault(p => p.Id == package.Id);
                if (localInOrder != null)
                {
                    localInOrder.CurrentTrackingStatus = TrackingMilestoneStatus.Delivered;
                    localInOrder.DeliveredAtUtc = deliveredAtUtc;
                }

                order.RecalculateDeliveryStatus();
            }
        }

        // 8. Commit All Changes Atomically
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("ix_shipment_tracking_events_tenant_fingerprint") == true ||
                                           ex.Message.Contains("ix_shipment_tracking_events_tenant_fingerprint"))
        {
            _logger.LogWarning("Concurrent delivery confirmation hit duplicate fingerprint race for package '{Pkg}'", package.PackageNumber);
            return new DeliveryConfirmationResult
            {
                IsSuccess = true,
                IsAlreadyDelivered = true,
                PackageId = package.Id,
                PackageNumber = package.PackageNumber,
                TrackingNumber = package.TrackingNumber,
                CarrierCode = package.CarrierCode,
                ProofOfDeliveryId = pod.Id,
                DeliveredAtUtc = deliveredAtUtc,
                PackageTrackingStatus = package.CurrentTrackingStatus,
                OrderDeliveryStatus = order?.DeliveryStatus ?? OrderDeliveryStatus.Delivered,
                OutboundOrderId = package.OutboundOrderId,
                OrderNumber = order?.OrderNumber,
                ShipmentId = package.ShipmentId,
                Message = "Package delivery confirmed (Concurrent Idempotent Hit)."
            };
        }

        _logger.LogInformation("Confirmed delivery for Package '{Pkg}' (POD: '{PodId}'). Order '{Order}' DeliveryStatus: '{DeliveryStatus}'",
            package.PackageNumber, pod.Id, order?.OrderNumber, order?.DeliveryStatus);

        return new DeliveryConfirmationResult
        {
            IsSuccess = true,
            IsAlreadyDelivered = false,
            PackageId = package.Id,
            PackageNumber = package.PackageNumber,
            TrackingNumber = package.TrackingNumber,
            CarrierCode = package.CarrierCode,
            ProofOfDeliveryId = pod.Id,
            DeliveredAtUtc = deliveredAtUtc,
            PackageTrackingStatus = package.CurrentTrackingStatus,
            OrderDeliveryStatus = order?.DeliveryStatus ?? OrderDeliveryStatus.Delivered,
            OutboundOrderId = package.OutboundOrderId,
            OrderNumber = order?.OrderNumber,
            ShipmentId = package.ShipmentId,
            Message = "Package delivery confirmed successfully."
        };
    }
}

