using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Outbound shipping package / carton entity.
/// Pairs 1:1 with an authoritative physical HandlingUnit (UnitType = ShippingCarton).
/// Holds shipping details, scale-verified weights, dimensions, and carrier tracking.
/// DB-SPEC-3PL-2026 Phase 4.4 Packing & Dispatch Preparation.
/// </summary>
public class Package : AuditableEntity<Guid>, ITenantEntity, ISoftDeletable
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }

    public Guid OutboundOrderId { get; set; }
    public Guid? ShipmentId { get; set; }

    /// <summary>
    /// Authoritative 1:1 link to physical handling unit container identity.
    /// </summary>
    public Guid HandlingUnitId { get; set; }

    public Guid? PackagingMaterialId { get; set; }

    public string PackageNumber { get; set; } = string.Empty;
    public int CartonSequence { get; set; } = 1;

    // Weight and Dimensions Snapshots
    public decimal GrossWeightKg { get; set; }
    public decimal TareWeightKg { get; set; }
    public decimal NetWeightKg { get; set; }

    public decimal LengthCm { get; set; }
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }

    public decimal VolumetricDivisor { get; set; } = 5000m;
    public decimal VolumetricWeightKg { get; set; }

    public string? TrackingNumber { get; set; }
    public string? CarrierCode { get; set; }
    public string? CarrierServiceCode { get; set; }

    public PackageStatus Status { get; set; } = PackageStatus.Open;

    // Carrier Network Tracking & Delivery (Phase 4.5)
    public TrackingMilestoneStatus CurrentTrackingStatus { get; set; } = TrackingMilestoneStatus.PendingHandover;
    public DateTime? TrackingStatusUpdatedAtUtc { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }

    public Guid? CurrentLocationId { get; set; }

    public Guid? PackedByUserId { get; set; }
    public DateTime? PackedAtUtc { get; set; }

    public uint Version { get; set; } = 1;

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public OutboundOrder OutboundOrder { get; set; } = null!;
    public Shipment? Shipment { get; set; }
    public HandlingUnit HandlingUnit { get; set; } = null!;
    public PackagingMaterial? PackagingMaterial { get; set; }
    public Location? CurrentLocation { get; set; }
    public User? PackedByUser { get; set; }

    public ICollection<PackageLine> Lines { get; set; } = [];
    public ICollection<ShippingLabel> ShippingLabels { get; set; } = [];

    // Tracking & POD Navigations (Phase 4.5)
    public ICollection<ProofOfDelivery> ProofOfDeliveries { get; set; } = [];
    public ProofOfDelivery? ProofOfDelivery => ProofOfDeliveries.FirstOrDefault(p => !p.IsSuperseded);
    public ICollection<ShipmentTrackingEvent> TrackingEvents { get; set; } = [];
    public ICollection<ShipmentDeliveryException> DeliveryExceptions { get; set; } = [];
    public ICollection<ReturnShipment> ReturnShipments { get; set; } = [];
    public ICollection<PublicTrackingShare> PublicTrackingShares { get; set; } = [];

    public Package()
    {
        Id = Guid.NewGuid();
    }

    /// <summary>
    /// Applies packaging material dimensions and tare weight snapshot to the package.
    /// </summary>
    public void ApplyPackagingMaterial(PackagingMaterial material)
    {
        PackagingMaterialId = material.Id;
        PackagingMaterial = material;
        LengthCm = material.LengthCm;
        WidthCm = material.WidthCm;
        HeightCm = material.HeightCm;
        TareWeightKg = material.TareWeightKg;
        RecalculateVolumetricWeight();
    }

    /// <summary>
    /// Adds a packed product line to the package.
    /// </summary>
    public void AddLine(Guid orderLineId, Guid itemId, Guid? lotId, decimal quantity, Guid? sourceHandlingUnitId, Guid allocationId)
    {
        if (Status != PackageStatus.Open)
        {
            throw new InvalidDomainStateException($"Cannot add items to package '{PackageNumber}' with status '{Status}'. Package must be Open.");
        }

        if (quantity <= 0)
        {
            throw new ValidationException(nameof(quantity), "Packed item quantity must be greater than zero.");
        }

        var existingLine = Lines.FirstOrDefault(l => l.OutboundOrderLineId == orderLineId && l.LotId == lotId);
        if (existingLine != null)
        {
            existingLine.Quantity += quantity;
        }
        else
        {
            Lines.Add(new PackageLine
            {
                PackageId = Id,
                OutboundOrderLineId = orderLineId,
                ItemId = itemId,
                LotId = lotId,
                Quantity = quantity,
                SourceHandlingUnitId = sourceHandlingUnitId,
                InventoryAllocationId = allocationId
            });
        }
    }

    /// <summary>
    /// Closes and seals the package with scale-verified gross weight, calculating net and volumetric weights.
    /// </summary>
    public void Close(decimal scaleGrossWeightKg, Guid userId)
    {
        if (Status != PackageStatus.Open)
        {
            throw new InvalidDomainStateException($"Cannot close package '{PackageNumber}' with status '{Status}'. Package must be Open.");
        }

        if (Lines.Count == 0)
        {
            throw new InvalidDomainStateException($"Cannot close empty package '{PackageNumber}'. At least one item line must be packed.");
        }

        if (scaleGrossWeightKg <= 0)
        {
            throw new ValidationException(nameof(scaleGrossWeightKg), "Scale-verified gross weight must be greater than zero.");
        }

        if (scaleGrossWeightKg < TareWeightKg)
        {
            throw new ValidationException(nameof(scaleGrossWeightKg),
                $"Scale gross weight ({scaleGrossWeightKg}kg) cannot be less than packaging tare weight ({TareWeightKg}kg).");
        }

        GrossWeightKg = scaleGrossWeightKg;
        NetWeightKg = scaleGrossWeightKg - TareWeightKg;
        RecalculateVolumetricWeight();

        Status = PackageStatus.Packed;
        PackedByUserId = userId;
        PackedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Attaches generated carrier shipping label details to the package.
    /// </summary>
    public void SetShippingLabel(string trackingNumber, string carrierCode, string? serviceCode)
    {
        if (Status != PackageStatus.Packed && Status != PackageStatus.LabelPrinted)
        {
            throw new InvalidDomainStateException($"Cannot apply shipping label to package '{PackageNumber}' with status '{Status}'. Package must be Packed.");
        }

        if (string.IsNullOrWhiteSpace(trackingNumber))
        {
            throw new ValidationException(nameof(trackingNumber), "Tracking number is required.");
        }

        TrackingNumber = trackingNumber.Trim();
        CarrierCode = carrierCode.Trim();
        CarrierServiceCode = serviceCode?.Trim();
        Status = PackageStatus.LabelPrinted;
    }

    /// <summary>
    /// Clears shipping label assignment after voiding, reverting status back to Packed.
    /// </summary>
    public void ClearShippingLabel()
    {
        if (Status != PackageStatus.LabelPrinted)
        {
            throw new InvalidDomainStateException($"Cannot clear shipping label for package '{PackageNumber}' with status '{Status}'. Package must be LabelPrinted.");
        }

        TrackingNumber = null;
        CarrierCode = null;
        CarrierServiceCode = null;
        Status = PackageStatus.Packed;
    }

    /// <summary>
    /// Moves package to outbound staging lane.
    /// </summary>
    public void Stage(Guid stagingLocationId)
    {
        if (Status != PackageStatus.LabelPrinted && Status != PackageStatus.Packed)
        {
            throw new InvalidDomainStateException($"Cannot stage package '{PackageNumber}' with status '{Status}'. Package must be Packed/LabelPrinted.");
        }

        CurrentLocationId = stagingLocationId;
        Status = PackageStatus.Staged;
        HandlingUnit?.MoveToLocation(stagingLocationId, HandlingUnitStatus.InTransit);
    }

    /// <summary>
    /// Marks package loaded onto vehicle.
    /// </summary>
    public void Load()
    {
        if (Status != PackageStatus.Staged && Status != PackageStatus.LabelPrinted)
        {
            throw new InvalidDomainStateException($"Cannot load package '{PackageNumber}' with status '{Status}'. Package must be Staged or LabelPrinted.");
        }

        Status = PackageStatus.Loaded;
    }

    /// <summary>
    /// Cancels the package prior to carrier loading.
    /// </summary>
    public void Cancel(string reason)
    {
        if (Status == PackageStatus.Loaded)
        {
            throw new InvalidDomainStateException($"Cannot cancel package '{PackageNumber}' because it has already been loaded.");
        }

        Status = PackageStatus.Cancelled;
    }

    public void RecalculateVolumetricWeight()
    {
        var divisor = VolumetricDivisor > 0 ? VolumetricDivisor : 5000m;
        var volumeCm3 = LengthCm * WidthCm * HeightCm;
        VolumetricWeightKg = Math.Round(volumeCm3 / divisor, 2);
    }

    /// <summary>
    /// Updates current package tracking status enforcing monotonic progression.
    /// Older or out-of-order scans do not regress a Delivered package.
    /// </summary>
    public void UpdateTrackingMilestone(TrackingMilestoneStatus newMilestone, DateTime occurredAtUtc)
    {
        // Monotonic guard: Delivered is terminal (cannot revert to earlier transit state)
        if (CurrentTrackingStatus == TrackingMilestoneStatus.Delivered && newMilestone != TrackingMilestoneStatus.Delivered)
        {
            return;
        }

        CurrentTrackingStatus = newMilestone;
        TrackingStatusUpdatedAtUtc = occurredAtUtc;

        if (newMilestone == TrackingMilestoneStatus.Delivered && !DeliveredAtUtc.HasValue)
        {
            DeliveredAtUtc = occurredAtUtc;
        }
    }

    /// <summary>
    /// Confirms delivery of the package with timestamp.
    /// </summary>
    public void ConfirmDelivery(DateTime deliveredAtUtc)
    {
        CurrentTrackingStatus = TrackingMilestoneStatus.Delivered;
        DeliveredAtUtc = deliveredAtUtc;
        TrackingStatusUpdatedAtUtc = deliveredAtUtc;
    }
}

