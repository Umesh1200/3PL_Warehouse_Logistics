using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Immutable audit log of pick task lifecycle state transitions.
/// DB-SPEC-3PL-2026 Phase 4.3 Picking & Warehouse Execution.
/// </summary>
public class PickTaskStatusHistory : BaseEntity<Guid>
{
    public Guid PickTaskId { get; set; }
    public PickTaskStatus FromStatus { get; set; }
    public PickTaskStatus ToStatus { get; set; }
    public string? Reason { get; set; }
    public Guid ChangedByUserId { get; set; }
    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public PickTask PickTask { get; set; } = null!;

    public PickTaskStatusHistory()
    {
        Id = Guid.NewGuid();
    }
}

