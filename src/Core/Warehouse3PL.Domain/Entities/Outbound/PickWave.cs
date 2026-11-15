using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Coordinated outbound fulfillment wave aggregate root.
/// Groups allocated orders into executable batches, optimizes travel routing, and coordinates RF tasks.
/// DB-SPEC-3PL-2026 Phase 4.3 Picking & Warehouse Execution.
/// </summary>
public class PickWave : AuditableEntity<Guid>, ITenantEntity, ISoftDeletable
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }

    public string WaveNumber { get; set; } = string.Empty;
    public PickWaveType WaveType { get; set; } = PickWaveType.BatchPick;
    public PickWaveStatus Status { get; set; } = PickWaveStatus.Planning;

    public int Priority { get; set; } = 1; // 1 = Normal, 2 = High, 3 = Urgent

    /// <summary>
    /// Optional specific warehouse zone for zone picking.
    /// </summary>
    public Guid? ZoneId { get; set; }

    /// <summary>
    /// Optional thermal classification (Ambient, Refrigerated, Frozen) preventing mixed-temperature pick paths.
    /// </summary>
    public StorageTempClass? TempClass { get; set; }

    public Guid? AssignedToUserId { get; set; }

    public DateTime? ReleasedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public string? CancellationReason { get; set; }
    public DateTime? CancelledAtUtc { get; set; }

    public uint Version { get; set; } = 1;

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public WarehouseZone? Zone { get; set; }
    public User? AssignedToUser { get; set; }

    public ICollection<PickWaveOrder> WaveOrders { get; set; } = [];
    public ICollection<PickTask> Tasks { get; set; } = [];

    public PickWave()
    {
        Id = Guid.NewGuid();
    }

    /// <summary>
    /// Releases the wave to the warehouse floor, making tasks available for operator assignment.
    /// </summary>
    public void Release(Guid userId)
    {
        if (Status != PickWaveStatus.Planning)
        {
            throw new InvalidDomainStateException(
                $"Cannot release pick wave '{WaveNumber}' with status '{Status}'. Only Planning waves can be released.");
        }

        if (WaveOrders.Count == 0)
        {
            throw new InvalidDomainStateException($"Pick wave '{WaveNumber}' contains no orders.");
        }

        if (Tasks.Count == 0)
        {
            throw new InvalidDomainStateException($"Pick wave '{WaveNumber}' contains no pick tasks.");
        }

        Status = PickWaveStatus.Released;
        ReleasedAtUtc = DateTime.UtcNow;

        foreach (var task in Tasks.Where(t => t.Status == PickTaskStatus.Pending))
        {
            // ready for assignment
        }
    }

    /// <summary>
    /// Marks the wave InProgress when an operator begins picking tasks.
    /// </summary>
    public void Start(Guid userId)
    {
        if (Status != PickWaveStatus.Released && Status != PickWaveStatus.InProgress)
        {
            throw new InvalidDomainStateException(
                $"Cannot start pick wave '{WaveNumber}' with status '{Status}'. Wave must be Released.");
        }

        Status = PickWaveStatus.InProgress;
        StartedAtUtc ??= DateTime.UtcNow;
    }

    /// <summary>
    /// Completes the wave once all tasks are either Completed, ShortPicked, or Cancelled.
    /// </summary>
    public void Complete(Guid userId)
    {
        if (Status != PickWaveStatus.InProgress && Status != PickWaveStatus.Released)
        {
            throw new InvalidDomainStateException(
                $"Cannot complete pick wave '{WaveNumber}' with status '{Status}'.");
        }

        var openTasks = Tasks.Any(t => t.Status == PickTaskStatus.Pending ||
                                       t.Status == PickTaskStatus.Assigned ||
                                       t.Status == PickTaskStatus.InProgress);

        if (openTasks)
        {
            throw new InvalidDomainStateException(
                $"Cannot complete pick wave '{WaveNumber}' because it still contains open uncompleted tasks.");
        }

        Status = PickWaveStatus.Completed;
        CompletedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Cancels the wave and aborts all non-terminal tasks.
    /// </summary>
    public void Cancel(string reason, Guid userId)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException(nameof(reason), "A reason must be provided when cancelling a pick wave.");
        }

        if (Status == PickWaveStatus.Completed)
        {
            throw new InvalidDomainStateException($"Cannot cancel completed pick wave '{WaveNumber}'.");
        }

        Status = PickWaveStatus.Cancelled;
        CancellationReason = reason;
        CancelledAtUtc = DateTime.UtcNow;

        foreach (var task in Tasks.Where(t => t.Status != PickTaskStatus.Completed && t.Status != PickTaskStatus.Cancelled))
        {
            task.Cancel(reason);
        }
    }
}

