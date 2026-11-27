using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

public class LocationDto
{
    public Guid Id { get; set; }
    public Guid FacilityId { get; set; }
    public Guid? ZoneId { get; set; }
    public Guid? ParentLocationId { get; set; }
    public LocationType LocationType { get; set; }
    public string LocationCode { get; set; } = string.Empty;
    public string LocationName { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public int HierarchyLevel { get; set; } = 1;
    public string HierarchyPath { get; set; } = string.Empty;
    public string? Remarks { get; set; }
    public long ConfigRevision { get; set; } = 1;

    public string? AisleCode { get; set; }
    public string? RackCode { get; set; }
    public int? ShelfLevel { get; set; }
    public int? BinNumber { get; set; }
    public int? PickSequence { get; set; }

    public decimal? LengthCm { get; set; }
    public decimal? WidthCm { get; set; }
    public decimal? HeightCm { get; set; }
    public decimal? MaxVolumeCm3 { get; set; }
    public decimal? MaxWeightKg { get; set; }

    public LocationStatus Status { get; set; } = LocationStatus.Active;
    public bool IsActive { get; set; }
    public bool IsStorable { get; set; } = true;
    public bool IsPickable { get; set; }
    public bool IsReceiving { get; set; }
    public bool IsDamaged { get; set; }
    public bool IsLocked { get; set; }
}

public class LocationTreeNodeDto : LocationDto
{
    public List<LocationTreeNodeDto> Children { get; set; } = [];
}

