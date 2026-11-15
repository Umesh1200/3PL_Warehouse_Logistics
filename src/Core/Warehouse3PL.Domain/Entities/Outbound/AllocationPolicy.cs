using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Configurable allocation strategy rules per Client, Facility, and optional ItemCategory.
/// Governs strategy type (FIFO/FEFO), partial allocation allowance, lot mixing, and expiry horizons.
/// DB-SPEC-3PL-2026 Phase 4.2 Allocation Engine.
/// </summary>
public class AllocationPolicy : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }

    /// <summary>
    /// Optional category specificity. If null, acts as facility-level fallback policy for the client.
    /// </summary>
    public int? ItemCategoryId { get; set; }

    public AllocationStrategyType DefaultStrategy { get; set; } = AllocationStrategyType.FIFO;

    public bool AllowPartialAllocation { get; set; } = true;
    public bool AllowMixedLots { get; set; } = true;
    public int MinDaysBeforeExpiry { get; set; } = 0;
    public bool PreferFullHandlingUnits { get; set; } = false;

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Optimistic concurrency token.
    /// </summary>
    public uint Version { get; set; } = 1;

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public ItemCategory? ItemCategory { get; set; }

    public AllocationPolicy()
    {
        Id = Guid.NewGuid();
    }
}

