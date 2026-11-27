using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Facilities;

/// <summary>
/// Material Handling Equipment (MHE) asset master owned by a warehouse facility (MDM-2.1).
/// </summary>
public class WarehouseEquipment : AuditableEntity<Guid>, ISoftDeletable
{
    public Guid FacilityId { get; set; }
    public string EquipmentCode { get; set; } = string.Empty;
    public string NormalizedEquipmentCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public WarehouseEquipmentType EquipmentType { get; set; } = WarehouseEquipmentType.ManualCart;
    public string? SerialNumber { get; set; }
    public decimal? MaxLiftHeightCm { get; set; }
    public decimal? MaxLoadKg { get; set; }
    public bool RequiresCertification { get; set; } = false;
    public WarehouseEquipmentStatus Status { get; set; } = WarehouseEquipmentStatus.Active;
    public string? Remarks { get; set; }
    public long ConfigRevision { get; set; } = 1;

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    public Facility Facility { get; set; } = null!;

    public WarehouseEquipment()
    {
        Id = Guid.NewGuid();
    }

    public static string NormalizeCode(string code) =>
        (code ?? string.Empty).Trim().ToUpperInvariant();
}
