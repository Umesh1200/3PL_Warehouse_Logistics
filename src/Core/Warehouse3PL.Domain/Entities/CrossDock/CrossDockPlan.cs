using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.CrossDock;

/// <summary>
/// Cross-dock operation plan directing inbound freight directly to outbound staging, bypassing long-term storage.
/// DB-SPEC-3PL-2026 Phase 3.5.
/// </summary>
public class CrossDockPlan : AuditableEntity<Guid>, ITenantEntity, ISoftDeletable
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid? ReceivingSessionId { get; set; }

    public string PlanNumber { get; set; } = string.Empty;
    public CrossDockPlanStatus Status { get; set; } = CrossDockPlanStatus.Draft;

    public decimal TotalPlannedQuantity { get; set; }
    public decimal TotalExecutedQuantity { get; set; }

    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }

    public uint Version { get; set; } = 1;

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public ReceivingSession? ReceivingSession { get; set; }

    public ICollection<CrossDockPlanLine> Lines { get; set; } = [];
    public ICollection<CrossDockTask> Tasks { get; set; } = [];
    public ICollection<CrossDockException> Exceptions { get; set; } = [];

    public CrossDockPlan()
    {
        Id = Guid.NewGuid();
    }

    public void Approve()
    {
        if (Status != CrossDockPlanStatus.Draft)
            throw new InvalidDomainStateException($"Plan cannot be approved from '{Status}' status.");

        Status = CrossDockPlanStatus.Approved;
    }

    public void StartExecution()
    {
        if (Status != CrossDockPlanStatus.Approved && Status != CrossDockPlanStatus.Draft)
            throw new InvalidDomainStateException($"Execution cannot start from '{Status}' status.");

        Status = CrossDockPlanStatus.InProgress;
    }

    public void Complete()
    {
        if (Status != CrossDockPlanStatus.InProgress && Status != CrossDockPlanStatus.Approved)
            throw new InvalidDomainStateException($"Plan cannot be completed from '{Status}' status.");

        Status = CrossDockPlanStatus.Completed;
        CompletedAtUtc = DateTime.UtcNow;
    }

    public void Cancel(string reason)
    {
        if (Status == CrossDockPlanStatus.Completed)
            throw new InvalidDomainStateException("Completed cross-dock plan cannot be cancelled.");

        Status = CrossDockPlanStatus.Cancelled;
        Notes = string.IsNullOrWhiteSpace(Notes) ? $"Cancelled: {reason}" : $"{Notes} | Cancelled: {reason}";
    }
}

