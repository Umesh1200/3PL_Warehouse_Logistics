using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Inventory;

/// <summary>
/// Putaway task directing warehouse operators to transport received inventory into optimal storage bins.
/// Enhanced for Phase 3.4 Directed Task Workflow and RF Mobile Dual-Verification.
/// </summary>
public class PutawayTask : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid ItemId { get; set; }
    public Guid? LotId { get; set; }
    public decimal Quantity { get; set; }

    /// <summary>
    /// Identifier of the inbound container or ReceiptLine.
    /// </summary>
    public Guid? LpnId { get; set; }

    /// <summary>
    /// License Plate Number (LPN) or pallet barcode tag for physical scan verification.
    /// </summary>
    public string? LpnCode { get; set; }

    public Guid SourceLocationId { get; set; }

    private Guid _suggestedLocationId;
    public Guid SuggestedLocationId
    {
        get => _suggestedLocationId;
        set
        {
            _suggestedLocationId = value;
            _targetLocationId = value;
        }
    }

    private Guid _targetLocationId;
    /// <summary>
    /// Target bin location alias for backwards compatibility.
    /// </summary>
    public Guid TargetLocationId
    {
        get => _targetLocationId != Guid.Empty ? _targetLocationId : _suggestedLocationId;
        set
        {
            _targetLocationId = value;
            if (_suggestedLocationId == Guid.Empty)
                _suggestedLocationId = value;
        }
    }

    public Guid? ActualLocationId { get; set; }

    public PutawayTaskStatus Status { get; set; } = PutawayTaskStatus.Pending;
    public PutawayPriority Priority { get; set; } = PutawayPriority.Normal;

    public Guid? AssignedUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? CompletedAt
    {
        get => CompletedAtUtc;
        set => CompletedAtUtc = value;
    }

    public string? ExceptionReason { get; set; }
    public string? Notes { get; set; }

    /// <summary>
    /// Concurrency token for optimistic locking.
    /// </summary>
    public uint Version { get; set; } = 1;

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public Item Item { get; set; } = null!;
    public InventoryLot? Lot { get; set; }
    public Location SourceLocation { get; set; } = null!;
    public Location SuggestedLocation { get; set; } = null!;
    public Location? TargetLocation
    {
        get => SuggestedLocation;
        set
        {
            if (value != null)
                SuggestedLocation = value;
        }
    }
    public Location? ActualLocation { get; set; }
    public User? AssignedUser { get; set; }
    public ReceiptLine? ReceiptLine { get; set; }

    public PutawayTask()
    {
        Id = Guid.NewGuid();
    }

    /// <summary>
    /// Claims or assigns task to an RF operator.
    /// </summary>
    public void AssignToWorker(Guid userId)
    {
        if (Status != PutawayTaskStatus.Pending && Status != PutawayTaskStatus.ReRouted)
            throw new InvalidDomainStateException($"Task cannot be assigned from '{Status}' state.");

        AssignedUserId = userId;
        Status = PutawayTaskStatus.Assigned;
    }

    /// <summary>
    /// Marks the task in transit after the worker picks up the LPN from staging.
    /// </summary>
    public void StartTransit(Guid userId)
    {
        if (Status != PutawayTaskStatus.Assigned && Status != PutawayTaskStatus.Pending)
            throw new InvalidDomainStateException($"Task cannot begin transit from '{Status}' state.");

        AssignedUserId = userId;
        Status = PutawayTaskStatus.InTransit;
    }

    /// <summary>
    /// Completes the task upon successful dual-scan verification at target bin face.
    /// </summary>
    public void Complete(Guid actualLocationId, Guid completedByUserId)
    {
        if (Status != PutawayTaskStatus.InTransit && Status != PutawayTaskStatus.Assigned && Status != PutawayTaskStatus.Pending)
            throw new InvalidDomainStateException($"Task cannot be completed from '{Status}' state.");

        ActualLocationId = actualLocationId;
        TargetLocationId = actualLocationId;
        AssignedUserId = completedByUserId;
        Status = PutawayTaskStatus.Completed;
        CompletedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Places task on exception hold due to location obstruction, full bin, or damaged rack.
    /// </summary>
    public void ReportException(string reason)
    {
        if (Status == PutawayTaskStatus.Completed || Status == PutawayTaskStatus.Cancelled)
            throw new InvalidDomainStateException($"Cannot report exception on a '{Status}' task.");

        ExceptionReason = reason;
        Status = PutawayTaskStatus.ExceptionHold;
    }

    /// <summary>
    /// Reroutes the task to an alternate target bin after an exception or dynamic recalculation.
    /// </summary>
    public void ReRoute(Guid newSuggestedLocationId, string? notes = null)
    {
        if (Status != PutawayTaskStatus.ExceptionHold && Status != PutawayTaskStatus.Pending && Status != PutawayTaskStatus.Assigned)
            throw new InvalidDomainStateException($"Task cannot be rerouted from '{Status}' state.");

        SuggestedLocationId = newSuggestedLocationId;
        TargetLocationId = newSuggestedLocationId;
        Status = PutawayTaskStatus.ReRouted;
        if (!string.IsNullOrWhiteSpace(notes))
        {
            Notes = string.IsNullOrWhiteSpace(Notes) ? notes : $"{Notes} | {notes}";
        }
    }
}
