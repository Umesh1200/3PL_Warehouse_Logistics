using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Dock;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Inbound;

/// <summary>
/// Operational physical goods intake session bound to a docked vehicle / door.
/// DB-SPEC-3PL-2026 Module 5.
/// </summary>
public class ReceivingSession : AuditableEntity<Guid>, ISoftDeletable
{
    public Guid FacilityId { get; set; }
    public Guid? DockDoorId { get; set; }
    public Guid? YardVisitId { get; set; }
    public Guid? ClientId { get; set; }
    public Guid? AsnId { get; set; }
    public Guid? PurchaseOrderId { get; set; }

    public string SessionNumber { get; set; } = string.Empty;
    public ReceivingStatus Status { get; set; } = ReceivingStatus.Started;
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public Guid ReceivedByUserId { get; set; }

    // Cold chain seal & temperature monitoring
    public decimal? InitialProbeTemperature { get; set; }
    public string? SealNumber { get; set; }
    public bool? SealIntact { get; set; }
    public string? Notes { get; set; }

    // Blind receiving and quality gating
    public bool IsBlindReceiving { get; set; } = false;
    public bool RequiresSupervisorApproval { get; set; } = false;
    public bool IsSupervisorApproved { get; set; } = false;
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }

    /// <summary>
    /// Concurrency token for optimistic locking on session updates.
    /// </summary>
    public uint Version { get; set; } = 1;

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigations
    public Facility Facility { get; set; } = null!;
    public DockDoor? DockDoor { get; set; }
    public YardVisit? YardVisit { get; set; }
    public Client? Client { get; set; }
    public AdvanceShippingNotice? AdvanceShippingNotice { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }
    public User ReceivedByUser { get; set; } = null!;
    public User? ApprovedByUser { get; set; }

    public ICollection<ReceiptLine> Lines { get; set; } = [];
    public ICollection<ReceivingDiscrepancy> Discrepancies { get; set; } = [];

    public ReceivingSession()
    {
        Id = Guid.NewGuid();
    }

    public void RecordProbeTemperatureAndSeal(decimal? probeTemp, string? sealNumber, bool? sealIntact)
    {
        InitialProbeTemperature = probeTemp ?? InitialProbeTemperature;
        SealNumber = sealNumber ?? SealNumber;
        SealIntact = sealIntact ?? SealIntact;
        Version++;
    }

    public void FlagSession(string reason)
    {
        Status = ReceivingStatus.Flagged;
        RequiresSupervisorApproval = true;
        Notes = string.IsNullOrWhiteSpace(Notes) ? $"[Flagged: {reason}]" : $"{Notes} | [Flagged: {reason}]";
        Version++;
    }

    public void ApproveSupervisor(Guid supervisorUserId)
    {
        if (!RequiresSupervisorApproval)
        {
            return;
        }

        IsSupervisorApproved = true;
        ApprovedByUserId = supervisorUserId;
        ApprovedAtUtc = DateTime.UtcNow;

        if (Status == ReceivingStatus.Flagged)
        {
            Status = Lines.Count > 0 ? ReceivingStatus.InProgress : ReceivingStatus.Started;
        }

        Version++;
    }

    public void Complete(Guid completedByUserId)
    {
        if (Status == ReceivingStatus.Completed)
        {
            return;
        }

        if (Status == ReceivingStatus.Cancelled)
        {
            throw new DomainException($"Cannot complete cancelled receiving session '{SessionNumber}'.");
        }

        if (RequiresSupervisorApproval && !IsSupervisorApproved)
        {
            throw new DomainException($"Cannot complete receiving session '{SessionNumber}' without mandatory supervisor approval.");
        }

        var hasUnresolvedDiscrepancies = Discrepancies.Any(d => d.Status == DiscrepancyResolutionStatus.PendingReview);
        if (hasUnresolvedDiscrepancies)
        {
            throw new DomainException($"Cannot complete receiving session '{SessionNumber}' while discrepancies remain pending review.");
        }

        Status = ReceivingStatus.Completed;
        CompletedAtUtc = DateTime.UtcNow;
        Version++;
    }

    public void Cancel(string reason)
    {
        if (Status == ReceivingStatus.Completed)
        {
            throw new DomainException($"Cannot cancel completed receiving session '{SessionNumber}'.");
        }

        Status = ReceivingStatus.Cancelled;
        Notes = string.IsNullOrWhiteSpace(Notes) ? $"[Cancelled: {reason}]" : $"{Notes} | [Cancelled: {reason}]";
        Version++;
    }
}
