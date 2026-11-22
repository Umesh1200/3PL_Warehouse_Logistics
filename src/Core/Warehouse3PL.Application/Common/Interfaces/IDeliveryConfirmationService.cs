using System.Text.Json.Serialization;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

/// <summary>
/// Authoritative service coordinating the transition from validated Proof of Delivery evidence to confirmed package delivery.
/// Appends immutable POD_CONFIRMED tracking events and triggers multi-package/multi-shipment order delivery rollup.
/// DB-SPEC-3PL-2026 Phase 4.5 Shipment Tracking & Delivery Confirmation (Step 3A Sub-Step 4).
/// </summary>
public interface IDeliveryConfirmationService
{
    /// <summary>
    /// Confirms delivery for a specific package with an active, finalized Proof of Delivery record.
    /// Appends the authoritative tracking event and recalculates shipment and order rollup statuses atomically.
    /// </summary>
    Task<DeliveryConfirmationResult> ConfirmDeliveryAsync(
        ConfirmPackageDeliveryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Convenience overload to confirm delivery using the active non-superseded POD for the package.
    /// </summary>
    Task<DeliveryConfirmationResult> ConfirmDeliveryByPackageIdAsync(
        Guid packageId,
        CancellationToken cancellationToken = default);
}

public class ConfirmPackageDeliveryRequest
{
    public Guid PackageId { get; set; }
    public Guid? ProofOfDeliveryId { get; set; }
    public DateTime? DeliveredAtUtcOverride { get; set; }
    public string? ConfirmationNotes { get; set; }
    public Guid? TenantIdOverride { get; set; }

    [JsonIgnore]
    public Func<CancellationToken, Task>? BeforeCommitCustodyValidationAsync { get; set; }
}

public class DeliveryConfirmationResult
{
    public bool IsSuccess { get; set; }
    public bool IsAlreadyDelivered { get; set; }
    public Guid PackageId { get; set; }
    public string PackageNumber { get; set; } = string.Empty;
    public string TrackingNumber { get; set; } = string.Empty;
    public string CarrierCode { get; set; } = string.Empty;
    public Guid ProofOfDeliveryId { get; set; }
    public DateTime DeliveredAtUtc { get; set; }
    public TrackingMilestoneStatus PackageTrackingStatus { get; set; }
    public OrderDeliveryStatus OrderDeliveryStatus { get; set; }
    public Guid? OutboundOrderId { get; set; }
    public string? OrderNumber { get; set; }
    public Guid? ShipmentId { get; set; }
    public string Message { get; set; } = string.Empty;
}

