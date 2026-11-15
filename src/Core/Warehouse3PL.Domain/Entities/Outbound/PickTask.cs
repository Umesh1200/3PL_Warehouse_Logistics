using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Executable RF directed picking task.
/// Binds directly to an InventoryAllocation record to consume hard reserved stock without double-allocation.
/// DB-SPEC-3PL-2026 Phase 4.3 Picking & Warehouse Execution.
/// </summary>
public class PickTask : AuditableEntity<Guid>, ITenantEntity, ISoftDeletable
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }

    public Guid PickWaveId { get; set; }
    public Guid OutboundOrderId { get; set; }
    public Guid OutboundOrderLineId { get; set; }

    /// <summary>
    /// Direct foreign key link to Phase 4.2 hard inventory reservation.
    /// </summary>
    public Guid InventoryAllocationId { get; set; }

    public Guid StockOnHandId { get; set; }
    public Guid ItemId { get; set; }
    public Guid SourceLocationId { get; set; }
    public Guid? DestinationLocationId { get; set; }
    public Guid? LotId { get; set; }

    public string? ExpectedLpnCode { get; set; }

    /// <summary>
    /// Movable handling unit (tote / cart slot) into which picked items are placed.
    /// </summary>
    public Guid? TargetHandlingUnitId { get; set; }
    public string? TargetToteBarcode { get; set; }

    public decimal QuantityToPick { get; set; }
    public decimal QuantityPicked { get; set; }

    public decimal ShortPickQuantity => Math.Max(0m, QuantityToPick - QuantityPicked);

    public int PickSequence { get; set; }
    public PickTaskStatus Status { get; set; } = PickTaskStatus.Pending;

    public ShortPickReason? ShortPickReason { get; set; }
    public string? ShortPickNotes { get; set; }

    public Guid? AssignedUserId { get; set; }
    public DateTime? AssignedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public uint Version { get; set; } = 1;

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public PickWave PickWave { get; set; } = null!;
    public OutboundOrder OutboundOrder { get; set; } = null!;
    public OutboundOrderLine OutboundOrderLine { get; set; } = null!;
    public InventoryAllocation InventoryAllocation { get; set; } = null!;
    public StockOnHand StockOnHand { get; set; } = null!;
    public Item Item { get; set; } = null!;
    public Location SourceLocation { get; set; } = null!;
    public Location? DestinationLocation { get; set; }
    public InventoryLot? Lot { get; set; }
    public HandlingUnit? TargetHandlingUnit { get; set; }
    public User? AssignedUser { get; set; }

    public ICollection<PickException> Exceptions { get; set; } = [];
    public ICollection<PickTaskStatusHistory> StatusHistories { get; set; } = [];

    public PickTask()
    {
        Id = Guid.NewGuid();
    }

    /// <summary>
    /// Assigns the task to an RF operator.
    /// </summary>
    public void Assign(Guid userId)
    {
        if (Status != PickTaskStatus.Pending && Status != PickTaskStatus.Assigned)
        {
            throw new InvalidDomainStateException(
                $"Cannot assign pick task '{Id}' with status '{Status}'. Only Pending tasks can be assigned.");
        }

        var prev = Status;
        AssignedUserId = userId;
        AssignedAtUtc = DateTime.UtcNow;
        Status = PickTaskStatus.Assigned;

        AddHistory(prev, Status, "Assigned to operator", userId);
    }

    /// <summary>
    /// Marks the task InProgress when the operator arrives at the bin and scans the location barcode.
    /// </summary>
    public void Start(Guid userId)
    {
        if (Status != PickTaskStatus.Assigned && Status != PickTaskStatus.Pending)
        {
            throw new InvalidDomainStateException(
                $"Cannot start pick task '{Id}' with status '{Status}'. Task must be Assigned or Pending.");
        }

        var prev = Status;
        Status = PickTaskStatus.InProgress;
        StartedAtUtc = DateTime.UtcNow;

        AddHistory(prev, Status, "Operator scanned into location", userId);
    }

    /// <summary>
    /// Confirms full or expected quantity pick into target handling unit / tote.
    /// </summary>
    public void ConfirmPick(decimal quantity, string scannedToteBarcode, Guid? handlingUnitId, Guid userId)
    {
        if (quantity <= 0)
        {
            throw new ValidationException(nameof(quantity), "Picked quantity must be greater than zero.");
        }

        if (quantity > QuantityToPick)
        {
            throw new ValidationException(nameof(quantity),
                $"Cannot pick {quantity} units. Task only requested {QuantityToPick} units.");
        }

        if (string.IsNullOrWhiteSpace(scannedToteBarcode))
        {
            throw new ValidationException(nameof(scannedToteBarcode), "Target tote / container barcode is required.");
        }

        var prev = Status;
        QuantityPicked = quantity;
        TargetToteBarcode = scannedToteBarcode;
        TargetHandlingUnitId = handlingUnitId;
        CompletedAtUtc = DateTime.UtcNow;

        if (QuantityPicked >= QuantityToPick)
        {
            Status = PickTaskStatus.Completed;
        }
        else
        {
            Status = PickTaskStatus.ShortPicked;
        }

        AddHistory(prev, Status, $"Picked {quantity}/{QuantityToPick} units into tote '{scannedToteBarcode}'", userId);
    }

    /// <summary>
    /// Reports a short-pick exception where physical inventory could not satisfy demand.
    /// </summary>
    public void ReportShortPick(decimal quantityPicked, ShortPickReason reason, string? notes, Guid userId)
    {
        if (quantityPicked < 0)
        {
            throw new ValidationException(nameof(quantityPicked), "Picked quantity cannot be negative.");
        }

        if (quantityPicked >= QuantityToPick)
        {
            throw new ValidationException(nameof(quantityPicked),
                $"Short-pick quantity {quantityPicked} must be strictly less than task demand {QuantityToPick}.");
        }

        var prev = Status;
        QuantityPicked = quantityPicked;
        ShortPickReason = reason;
        ShortPickNotes = notes;
        Status = PickTaskStatus.ShortPicked;
        CompletedAtUtc = DateTime.UtcNow;

        AddHistory(prev, Status, $"Short pick reported ({reason}): {quantityPicked}/{QuantityToPick} units. {notes}", userId);
    }

    /// <summary>
    /// Cancels the task prior to physical execution.
    /// </summary>
    public void Cancel(string reason)
    {
        if (Status == PickTaskStatus.Completed)
        {
            throw new InvalidDomainStateException($"Cannot cancel completed pick task '{Id}'.");
        }

        var prev = Status;
        Status = PickTaskStatus.Cancelled;
        AddHistory(prev, Status, reason, AssignedUserId ?? Guid.Empty);
    }

    private void AddHistory(PickTaskStatus fromStatus, PickTaskStatus toStatus, string reason, Guid userId)
    {
        StatusHistories.Add(new PickTaskStatusHistory
        {
            PickTaskId = Id,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            Reason = reason,
            ChangedByUserId = userId,
            ChangedAtUtc = DateTime.UtcNow
        });
    }
}

