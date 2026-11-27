using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.WarehouseMaster;

public sealed record WarehouseZoneDto(
    Guid Id,
    Guid FacilityId,
    string ZoneCode,
    string Name,
    WarehouseZoneType ZoneType,
    decimal? TempMinC,
    decimal? TempMaxC,
    Guid? DedicatedClientId,
    string? Remarks,
    bool IsActive,
    long ConfigRevision,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record LocationStorageProfileDto(
    Guid LocationId,
    Guid FacilityId,
    Guid? DedicatedClientId,
    int? MaxHandlingUnits,
    bool AllowMixedSku,
    bool AllowMixedLot,
    bool IsPutawayAllowed,
    long ConfigRevision);

public sealed record LocationOperationalProfileDto(
    Guid LocationId,
    Guid FacilityId,
    WarehouseStationRole StationRole,
    bool SupportsScale,
    bool SupportsLabelPrinter,
    bool SupportsConveyor,
    int MaxConcurrentSessions,
    Guid? DefaultStagingLocationId,
    long ConfigRevision);

public sealed record LocationEquipmentRequirementDto(
    Guid Id,
    Guid LocationId,
    Guid FacilityId,
    WarehouseEquipmentType EquipmentType,
    bool IsMandatory,
    long ConfigRevision);

public sealed record LocationLockDto(
    Guid Id,
    Guid LocationId,
    LocationLockType LockType,
    string ReasonCode,
    string? Notes,
    Guid? LockedByUserId,
    DateTime LockedAt,
    Guid? ReleasedByUserId,
    DateTime? ReleasedAt,
    bool IsActive);

public sealed class LocationDetailDto : LocationDto
{
    public string? ZoneCode { get; set; }
    public string? ParentLocationCode { get; set; }
    public LocationStorageProfileDto? StorageProfile { get; set; }
    public LocationOperationalProfileDto? OperationalProfile { get; set; }
    public List<LocationEquipmentRequirementDto> EquipmentRequirements { get; set; } = [];
    public int ActiveLockCount { get; set; }
}

public sealed record LocationHierarchyRuleDto(
    Guid Id,
    Guid? FacilityId,
    LocationType? ParentLocationType,
    LocationType ChildLocationType,
    int MaxDepth,
    bool IsStorableLeaf,
    bool IsActive,
    long ConfigRevision);

public sealed record WarehouseEquipmentDto(
    Guid Id,
    Guid FacilityId,
    string EquipmentCode,
    string Name,
    WarehouseEquipmentType EquipmentType,
    string? SerialNumber,
    decimal? MaxLiftHeightCm,
    decimal? MaxLoadKg,
    bool RequiresCertification,
    WarehouseEquipmentStatus Status,
    string? Remarks,
    long ConfigRevision,
    DateTime CreatedAt);

public sealed record PackagingMaterialDto(
    Guid Id,
    Guid FacilityId,
    Guid? ClientId,
    string Code,
    string Name,
    PackagingMaterialType MaterialType,
    decimal LengthCm,
    decimal WidthCm,
    decimal HeightCm,
    decimal CalculatedVolumeCm3,
    decimal MaxWeightKg,
    decimal TareWeightKg,
    decimal? CostPerUnit,
    string? CurrencyCode,
    bool IsActive,
    long ConfigRevision,
    DateTime CreatedAt);

internal static class WarehouseMasterMappers
{
    public static WarehouseZoneDto ToDto(WarehouseZone z) => new(
        z.Id,
        z.FacilityId,
        z.ZoneCode,
        z.Name,
        z.ZoneType,
        z.TempMinC,
        z.TempMaxC,
        z.DedicatedClientId,
        z.Remarks,
        z.IsActive,
        z.ConfigRevision,
        z.CreatedAt,
        z.UpdatedAt);

    public static LocationStorageProfileDto ToDto(LocationStorageProfile p) => new(
        p.LocationId,
        p.FacilityId,
        p.DedicatedClientId,
        p.MaxHandlingUnits,
        p.AllowMixedSku,
        p.AllowMixedLot,
        p.IsPutawayAllowed,
        p.ConfigRevision);

    public static LocationOperationalProfileDto ToDto(LocationOperationalProfile p) => new(
        p.LocationId,
        p.FacilityId,
        p.StationRole,
        p.SupportsScale,
        p.SupportsLabelPrinter,
        p.SupportsConveyor,
        p.MaxConcurrentSessions,
        p.DefaultStagingLocationId,
        p.ConfigRevision);

    public static LocationEquipmentRequirementDto ToDto(LocationEquipmentRequirement r) => new(
        r.Id,
        r.LocationId,
        r.FacilityId,
        r.EquipmentType,
        r.IsMandatory,
        r.ConfigRevision);

    public static LocationLockDto ToDto(LocationLock lk) => new(
        lk.Id,
        lk.LocationId,
        lk.LockType,
        lk.ReasonCode,
        lk.Notes,
        lk.LockedByUserId,
        lk.LockedAt,
        lk.ReleasedByUserId,
        lk.ReleasedAt,
        lk.IsActive);

    public static LocationDto ToLocationDto(Location l) => new()
    {
        Id = l.Id,
        FacilityId = l.FacilityId,
        ZoneId = l.ZoneId,
        ParentLocationId = l.ParentLocationId,
        LocationType = l.LocationType,
        LocationCode = l.LocationCode,
        LocationName = l.LocationName,
        Barcode = l.Barcode,
        HierarchyLevel = l.HierarchyLevel,
        HierarchyPath = l.HierarchyPath,
        Remarks = l.Remarks,
        ConfigRevision = l.ConfigRevision,
        AisleCode = l.AisleCode,
        RackCode = l.RackCode,
        ShelfLevel = l.ShelfLevel,
        BinNumber = l.BinNumber,
        PickSequence = l.PickSequence,
        LengthCm = l.LengthCm,
        WidthCm = l.WidthCm,
        HeightCm = l.HeightCm,
        MaxVolumeCm3 = l.MaxVolumeCm3,
        MaxWeightKg = l.MaxWeightKg,
        Status = l.Status,
        IsActive = l.IsActive,
        IsStorable = l.IsStorable,
        IsPickable = l.IsPickable,
        IsReceiving = l.IsReceiving,
        IsDamaged = l.IsDamaged,
        IsLocked = l.IsLocked
    };

    public static LocationHierarchyRuleDto ToDto(LocationHierarchyRule r) => new(
        r.Id,
        r.FacilityId,
        r.ParentLocationType,
        r.ChildLocationType,
        r.MaxDepth,
        r.IsStorableLeaf,
        r.IsActive,
        r.ConfigRevision);

    public static WarehouseEquipmentDto ToDto(WarehouseEquipment e) => new(
        e.Id,
        e.FacilityId,
        e.EquipmentCode,
        e.Name,
        e.EquipmentType,
        e.SerialNumber,
        e.MaxLiftHeightCm,
        e.MaxLoadKg,
        e.RequiresCertification,
        e.Status,
        e.Remarks,
        e.ConfigRevision,
        e.CreatedAt);

    public static PackagingMaterialDto ToDto(PackagingMaterial m) => new(
        m.Id,
        m.FacilityId,
        m.ClientId,
        m.Code,
        m.Name,
        m.MaterialType,
        m.LengthCm,
        m.WidthCm,
        m.HeightCm,
        m.CalculatedVolumeCm3,
        m.MaxWeightKg,
        m.TareWeightKg,
        m.CostPerUnit,
        m.CurrencyCode,
        m.IsActive,
        m.ConfigRevision,
        m.CreatedAt);
}
