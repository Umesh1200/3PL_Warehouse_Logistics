using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Consolidated outbound shipment header grouping one or more shipping packages for carrier dispatch.
/// Maintains distinct lifecycle from individual packages.
/// DB-SPEC-3PL-2026 Phase 4.4 Packing & Dispatch Preparation.
/// </summary>
public class Shipment : AuditableEntity<Guid>, ITenantEntity, ISoftDeletable
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }

    public string ShipmentNumber { get; set; } = string.Empty;
    public string CarrierCode { get; set; } = string.Empty;
    public CarrierServiceLevel CarrierServiceLevel { get; set; } = CarrierServiceLevel.StandardGround;

    public ShipmentStatus Status { get; set; } = ShipmentStatus.Draft;

    // Carrier Network Tracking & Delivery (Phase 4.5)
    public TrackingMilestoneStatus DeliveryStatus { get; set; } = TrackingMilestoneStatus.PendingHandover;
    public DateTime? DeliveryStatusUpdatedAtUtc { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }

    public int TotalPackagesCount { get; set; }
    public decimal TotalWeightKg { get; set; }
    public decimal TotalVolumeCm3 { get; set; }

    public Guid? StagingLocationId { get; set; }

    public string? TrailerNumber { get; set; }
    public string? SealNumber { get; set; }
    public string? BillOfLadingNumber { get; set; }
    public DateTime? DispatchedAtUtc { get; set; }

    public uint Version { get; set; } = 1;

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public Location? StagingLocation { get; set; }

    public ICollection<Package> Packages { get; set; } = [];
    public ICollection<ShipmentTrackingEvent> TrackingEvents { get; set; } = [];
    public ICollection<ShipmentDeliveryException> DeliveryExceptions { get; set; } = [];
    public ICollection<ReturnShipment> ReturnShipments { get; set; } = [];

    public Shipment()
    {
        Id = Guid.NewGuid();
    }

    /// <summary>
    /// Consolidates an outbound package into this shipment and recalculates weight and volume metrics.
    /// </summary>
    public void AddPackage(Package package)
    {
        if (Status != ShipmentStatus.Draft)
        {
            throw new InvalidDomainStateException($"Cannot add package to shipment '{ShipmentNumber}' with status '{Status}'. Shipment must be in Draft.");
        }

        if (package.ClientId != ClientId)
        {
            throw new DomainException($"Cannot add cross-tenant package '{package.PackageNumber}' to shipment '{ShipmentNumber}'.");
        }

        if (package.FacilityId != FacilityId)
        {
            throw new DomainException($"Cannot add cross-facility package '{package.PackageNumber}' to shipment '{ShipmentNumber}'.");
        }

        if (!string.IsNullOrWhiteSpace(CarrierCode) && !string.IsNullOrWhiteSpace(package.CarrierCode) &&
            !string.Equals(CarrierCode, package.CarrierCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(nameof(package),
                $"Package carrier '{package.CarrierCode}' is incompatible with shipment carrier '{CarrierCode}'.");
        }

        if (package.ShipmentId.HasValue && package.ShipmentId.Value != Id)
        {
            throw new InvalidDomainStateException(
                $"Package '{package.PackageNumber}' is already assigned to active shipment '{package.ShipmentId.Value}'.");
        }

        if (package.Status == PackageStatus.Open || package.Status == PackageStatus.Cancelled)
        {
            throw new InvalidDomainStateException(
                $"Cannot add package '{package.PackageNumber}' with status '{package.Status}' to shipment. Package must be Packed or Labeled.");
        }

        package.ShipmentId = Id;
        package.Shipment = this;
        Packages.Add(package);

        RecalculateTotals();
    }

    /// <summary>
    /// Removes a package from the draft or ready shipment prior to dispatch.
    /// </summary>
    public void RemovePackage(Guid packageId)
    {
        if (Status == ShipmentStatus.Dispatched)
        {
            throw new InvalidDomainStateException($"Cannot remove package from already dispatched shipment '{ShipmentNumber}'.");
        }

        var package = Packages.FirstOrDefault(p => p.Id == packageId);
        if (package == null)
        {
            throw new EntityNotFoundException(nameof(Package), packageId);
        }

        package.ShipmentId = null;
        package.Shipment = null;
        Packages.Remove(package);

        RecalculateTotals();

        if (Packages.Count == 0 && Status == ShipmentStatus.ReadyForDispatch)
        {
            Status = ShipmentStatus.Draft;
        }
    }

    /// <summary>
    /// Marks the shipment ready for trailer loading / carrier dispatch once all packages are packed and labeled.
    /// </summary>
    public void MarkReadyForDispatch()
    {
        if (Status != ShipmentStatus.Draft)
        {
            throw new InvalidDomainStateException($"Cannot mark shipment '{ShipmentNumber}' ready for dispatch with status '{Status}'.");
        }

        if (Packages.Count == 0)
        {
            throw new InvalidDomainStateException($"Shipment '{ShipmentNumber}' contains no packages.");
        }

        var unready = Packages.Any(p => p.Status != PackageStatus.Packed &&
                                       p.Status != PackageStatus.LabelPrinted &&
                                       p.Status != PackageStatus.Staged &&
                                       p.Status != PackageStatus.Loaded);

        if (unready)
        {
            throw new InvalidDomainStateException($"Cannot mark shipment '{ShipmentNumber}' ready for dispatch because it contains open/incomplete packages.");
        }

        Status = ShipmentStatus.ReadyForDispatch;
        RecalculateTotals();
    }

    /// <summary>
    /// Confirms carrier custody transfer and records Bill of Lading details.
    /// </summary>
    public void Dispatch(string? trailerNumber, string? sealNumber, string? bolNumber, Guid userId)
    {
        if (Status != ShipmentStatus.ReadyForDispatch)
        {
            throw new InvalidDomainStateException($"Cannot dispatch shipment '{ShipmentNumber}' with status '{Status}'. Shipment must be ReadyForDispatch.");
        }

        Status = ShipmentStatus.Dispatched;
        TrailerNumber = trailerNumber;
        SealNumber = sealNumber;
        BillOfLadingNumber = bolNumber;
        DispatchedAtUtc = DateTime.UtcNow;

        foreach (var package in Packages)
        {
            package.Load();
        }
    }

    /// <summary>
    /// Cancels shipment.
    /// </summary>
    public void Cancel(string reason)
    {
        if (Status == ShipmentStatus.Dispatched)
        {
            throw new InvalidDomainStateException($"Cannot cancel already dispatched shipment '{ShipmentNumber}'.");
        }

        Status = ShipmentStatus.Cancelled;
        foreach (var package in Packages)
        {
            package.ShipmentId = null;
        }
    }

    public void RecalculateTotals()
    {
        TotalPackagesCount = Packages.Count;
        TotalWeightKg = Packages.Sum(p => p.GrossWeightKg);
        TotalVolumeCm3 = Packages.Sum(p => p.LengthCm * p.WidthCm * p.HeightCm);
    }

    /// <summary>
    /// Derives and updates the shipment's overall delivery status from its consolidated packages.
    /// </summary>
    public void RecalculateDeliveryStatus()
    {
        if (Packages.Count == 0) return;

        var allDelivered = Packages.All(p => p.CurrentTrackingStatus == TrackingMilestoneStatus.Delivered);
        var anyException = Packages.Any(p => p.CurrentTrackingStatus == TrackingMilestoneStatus.DeliveryException ||
                                              p.CurrentTrackingStatus == TrackingMilestoneStatus.ReturnedToSender);
        var anyInTransit = Packages.Any(p => p.CurrentTrackingStatus == TrackingMilestoneStatus.InTransit ||
                                             p.CurrentTrackingStatus == TrackingMilestoneStatus.OutForDelivery ||
                                             p.CurrentTrackingStatus == TrackingMilestoneStatus.Accepted);

        if (allDelivered)
        {
            DeliveryStatus = TrackingMilestoneStatus.Delivered;
            DeliveredAtUtc = Packages.Max(p => p.DeliveredAtUtc) ?? DateTime.UtcNow;
            DeliveryStatusUpdatedAtUtc = DeliveredAtUtc;
        }
        else if (anyException)
        {
            DeliveryStatus = TrackingMilestoneStatus.DeliveryException;
            DeliveryStatusUpdatedAtUtc = DateTime.UtcNow;
        }
        else if (anyInTransit)
        {
            var anyOutForDelivery = Packages.Any(p => p.CurrentTrackingStatus == TrackingMilestoneStatus.OutForDelivery);
            DeliveryStatus = anyOutForDelivery ? TrackingMilestoneStatus.OutForDelivery : TrackingMilestoneStatus.InTransit;
            DeliveryStatusUpdatedAtUtc = DateTime.UtcNow;
        }
    }
}

