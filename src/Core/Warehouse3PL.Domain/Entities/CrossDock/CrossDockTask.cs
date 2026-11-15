using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.CrossDock;

/// <summary>
/// Physical movement task directing a warehouse operator from receiving to outbound staging.
/// </summary>
public class CrossDockTask : AuditableEntity<Guid>, ITenantEntity
{
    public Guid CrossDockPlanId { get; set; }
    public Guid CrossDockPlanLineId { get; set; }
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid ItemId { get; set; }
    public Guid? LotId { get; set; }

    public string TaskNumber { get; set; } = string.Empty;
    public string Lpn { get; set; } = string.Empty;

    public Guid SourceLocationId { get; set; }
    public Guid DestinationLocationId { get; set; }
    public Guid? ActualDestinationLocationId { get; set; }

    public decimal Quantity { get; set; }
    public CrossDockTaskStatus Status { get; set; } = CrossDockTaskStatus.Pending;
    public CrossDockPriority Priority { get; set; } = CrossDockPriority.Normal;

    public Guid? AssignedUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }

    public string? ScannedLpn { get; set; }
    public string? ScannedDestinationBarcode { get; set; }
    public string? Notes { get; set; }

    public uint Version { get; set; } = 1;

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public CrossDockPlan CrossDockPlan { get; set; } = null!;
    public CrossDockPlanLine CrossDockPlanLine { get; set; } = null!;
    public Item Item { get; set; } = null!;
    public InventoryLot? Lot { get; set; }
    public Location SourceLocation { get; set; } = null!;
    public Location DestinationLocation { get; set; } = null!;
    public Location? ActualDestinationLocation { get; set; }
    public User? AssignedUser { get; set; }

    public CrossDockTask()
    {
        Id = Guid.NewGuid();
    }

    public void AssignToWorker(Guid userId)
    {
        if (Status != CrossDockTaskStatus.Pending)
            throw new InvalidDomainStateException($"Task cannot be assigned from '{Status}' status.");

        AssignedUserId = userId;
        Status = CrossDockTaskStatus.Assigned;
    }

    public void StartTransit(Guid userId)
    {
        if (Status != CrossDockTaskStatus.Assigned && Status != CrossDockTaskStatus.Pending)
            throw new InvalidDomainStateException($"Task cannot start transit from '{Status}' status.");

        AssignedUserId = userId;
        Status = CrossDockTaskStatus.InTransit;
    }

    public void ConfirmExecution(string scannedLpn, string scannedDestBarcode, Guid actualDestId, Guid workerId)
    {
        if (Status != CrossDockTaskStatus.InTransit && Status != CrossDockTaskStatus.Assigned && Status != CrossDockTaskStatus.Pending)
            throw new InvalidDomainStateException($"Task cannot be confirmed from '{Status}' status.");

        ScannedLpn = scannedLpn;
        ScannedDestinationBarcode = scannedDestBarcode;
        ActualDestinationLocationId = actualDestId;
        AssignedUserId = workerId;
        Status = CrossDockTaskStatus.Completed;
        CompletedAtUtc = DateTime.UtcNow;
    }

    public void ReportException(string reason)
    {
        if (Status == CrossDockTaskStatus.Completed || Status == CrossDockTaskStatus.Cancelled)
            throw new InvalidDomainStateException($"Cannot report exception on a '{Status}' task.");

        Status = CrossDockTaskStatus.ExceptionHold;
        Notes = string.IsNullOrWhiteSpace(Notes) ? reason : $"{Notes} | {reason}";
    }

    public void Cancel(string reason)
    {
        if (Status == CrossDockTaskStatus.Completed)
            throw new InvalidDomainStateException("Completed task cannot be cancelled.");

        Status = CrossDockTaskStatus.Cancelled;
        Notes = string.IsNullOrWhiteSpace(Notes) ? $"Cancelled: {reason}" : $"{Notes} | Cancelled: {reason}";
    }
}

