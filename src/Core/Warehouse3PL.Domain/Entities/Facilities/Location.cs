using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Facilities;

/// <summary>
/// Configurable hierarchical warehouse storage location.
/// Models any depth of warehouse typography via parent-child recursion (Facility -> Zone -> Aisle -> Rack -> Shelf -> Bin).
/// </summary>
public class Location : AuditableEntity<Guid>, ISoftDeletable
{
    public Guid FacilityId { get; set; }
    public Guid? ZoneId { get; set; }
    
    /// <summary>
    /// Recursive parent location reference enabling flexible arbitrary-depth layout hierarchies.
    /// </summary>
    public Guid? ParentLocationId { get; set; }
    
    public LocationType LocationType { get; set; } = LocationType.Bin;
    public string LocationCode { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;

    // Coordinate & Path Matrix
    public string? AisleCode { get; set; }
    public string? RackCode { get; set; }
    public int? ShelfLevel { get; set; }
    public int? BinNumber { get; set; }
    public decimal? XCoord { get; set; }
    public decimal? YCoord { get; set; }
    public decimal? ZCoord { get; set; }
    public int? PickSequence { get; set; }

    // Physical Capacity Constraints
    public decimal? LengthCm { get; set; }
    public decimal? WidthCm { get; set; }
    public decimal? HeightCm { get; set; }
    public decimal? MaxVolumeCm3 { get; set; }
    public decimal? MaxWeightKg { get; set; }

    // Operational Status
    public bool IsActive { get; set; } = true;
    public bool IsPickable { get; set; } = true;
    public bool IsReceiving { get; set; } = false;
    public bool IsDamaged { get; set; } = false;
    public bool IsLocked { get; set; } = false;

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigations
    public Facility Facility { get; set; } = null!;
    public WarehouseZone? Zone { get; set; }
    public Location? ParentLocation { get; set; }
    public ICollection<Location> ChildLocations { get; set; } = [];
    public ICollection<LocationLock> Locks { get; set; } = [];

    public Location()
    {
        Id = Guid.NewGuid();
    }

    /// <summary>
    /// Helper to compute cubic volume in cm3 if dimensions are defined.
    /// </summary>
    public decimal CalculateVolumeCm3()
    {
        if (MaxVolumeCm3.HasValue) return MaxVolumeCm3.Value;
        if (LengthCm.HasValue && WidthCm.HasValue && HeightCm.HasValue)
            return LengthCm.Value * WidthCm.Value * HeightCm.Value;
        return 0m;
    }
}

