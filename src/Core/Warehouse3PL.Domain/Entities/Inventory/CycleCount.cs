using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Inventory;

/// <summary>
/// Cycle Count Header entity (DB-SPEC-3PL-2026 Module 6).
/// </summary>
public class CycleCount : AuditableEntity<Guid>
{
    public Guid FacilityId { get; set; }
    public Guid? ZoneId { get; set; }
    public string CountNumber { get; set; } = string.Empty;
    public CycleCountType CountType { get; set; } = CycleCountType.AbcVelocity;
    public CycleCountStatus Status { get; set; } = CycleCountStatus.Scheduled;
    
    public DateTime ScheduledDate { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Notes { get; set; }

    // Navigations
    public Facility Facility { get; set; } = null!;
    public WarehouseZone? Zone { get; set; }
    public ICollection<CycleCountItem> Items { get; set; } = [];

    public CycleCount()
    {
        Id = Guid.NewGuid();
    }
}

