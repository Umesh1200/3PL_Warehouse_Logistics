using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Inventory;

/// <summary>
/// Rule configuration defining directed slotting strategy and priority behavior.
/// DB-SPEC-3PL-2026 Phase 3.4.
/// </summary>
public class PutawayStrategyRule : AuditableEntity<Guid>, ITenantEntity, ISoftDeletable
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }

    public int Sequence { get; set; }
    public string RuleName { get; set; } = string.Empty;
    public StorageTempClass TempClassRequirement { get; set; } = StorageTempClass.Ambient;
    public VelocityClass? TargetVelocity { get; set; }
    public bool PreferConsolidation { get; set; } = true;
    public bool AllowDynamicOverflow { get; set; } = false;
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Optimistic concurrency token.
    /// </summary>
    public uint Version { get; set; } = 1;

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility Facility { get; set; } = null!;

    public PutawayStrategyRule()
    {
        Id = Guid.NewGuid();
    }
}
