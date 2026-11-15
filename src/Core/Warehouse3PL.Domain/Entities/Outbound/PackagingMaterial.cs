using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Packaging materials master catalog defining carton dimensions, tare weights, and burst capacities.
/// Supports standard facility-wide packaging (ClientId = null) and client-branded overrides (ClientId set).
/// DB-SPEC-3PL-2026 Phase 4.4 Packing & Dispatch Preparation.
/// </summary>
public class PackagingMaterial : AuditableEntity<Guid>, ISoftDeletable
{
    /// <summary>
    /// Optional tenant partition. Null denotes standard facility-wide packaging available to all clients.
    /// Non-null denotes client-branded / custom packaging restricted to that tenant.
    /// </summary>
    public Guid? ClientId { get; set; }

    public Guid FacilityId { get; set; }

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public PackagingMaterialType MaterialType { get; set; } = PackagingMaterialType.CorrugatedBox;

    public decimal LengthCm { get; set; }
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }

    public decimal MaxWeightKg { get; set; }
    public decimal TareWeightKg { get; set; }
    public decimal? CostPerUnit { get; set; }

    public bool IsActive { get; set; } = true;

    public decimal CalculatedVolumeCm3 => LengthCm * WidthCm * HeightCm;

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigations
    public Client? Client { get; set; }
    public Facility Facility { get; set; } = null!;

    public PackagingMaterial()
    {
        Id = Guid.NewGuid();
    }

    /// <summary>
    /// Calculates volumetric weight in kilograms based on a configurable divisor (standard air/courier = 5000).
    /// </summary>
    public decimal CalculateVolumetricWeightKg(decimal divisor = 5000m)
    {
        if (divisor <= 0m) divisor = 5000m;
        return Math.Round(CalculatedVolumeCm3 / divisor, 2);
    }
}

