using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Detailed line item within a ReturnReceivingSession tracking expected vs received quantities,
/// quality inspection results, and assigned inventory disposition.
/// DB-SPEC-3PL-2026 Phase 4.5 Step 3B.4.5: Return Facility Receiving &amp; Inspection.
/// </summary>
public class ReturnReceivingLine : BaseEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid ReturnReceivingSessionId { get; set; }
    public Guid ItemId { get; set; }
    public Guid? OriginalOrderLineId { get; set; }

    public string Sku { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public decimal ExpectedQuantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal AcceptedQuantity { get; set; }
    public decimal DamagedQuantity { get; set; }
    public decimal QuarantinedQuantity { get; set; }

    public ReturnDisposition? Disposition { get; set; }
    public Guid? TargetLocationId { get; set; }

    public string? InspectionNotes { get; set; }
    public DateTime? InspectedAtUtc { get; set; }

    public bool IsInventoryPosted { get; set; } = false;
    public long? InventoryTransactionId { get; set; }

    // Navigations
    public ReturnReceivingSession ReturnReceivingSession { get; set; } = null!;
    public Item Item { get; set; } = null!;
    public Location? TargetLocation { get; set; }

    public ReturnReceivingLine()
    {
        Id = Guid.NewGuid();
    }

    /// <summary>
    /// Records physically received quantity for this expected line item.
    /// </summary>
    public void RecordReceivedQuantity(decimal quantity)
    {
        if (quantity < 0)
        {
            throw new ValidationException(nameof(quantity), "Received quantity cannot be negative.");
        }

        ReceivedQuantity = quantity;
    }

    /// <summary>
    /// Assigns quality inspection results and disposition.
    /// Enforces inventory quantity conservation: Accepted + Damaged + Quarantined == ReceivedQuantity.
    /// </summary>
    public void AssignDisposition(
        decimal acceptedQty,
        decimal damagedQty,
        decimal quarantinedQty,
        ReturnDisposition disposition,
        Guid? targetLocationId,
        string? notes,
        DateTime inspectedAtUtc)
    {
        if (acceptedQty < 0 || damagedQty < 0 || quarantinedQty < 0)
        {
            throw new ValidationException(nameof(acceptedQty), "Inspected quantities cannot be negative.");
        }

        var totalInspected = acceptedQty + damagedQty + quarantinedQty;
        if (totalInspected != ReceivedQuantity)
        {
            throw new ValidationException(
                nameof(totalInspected),
                $"Sum of inspected quantities ({totalInspected}) must strictly equal received quantity ({ReceivedQuantity}).");
        }

        AcceptedQuantity = acceptedQty;
        DamagedQuantity = damagedQty;
        QuarantinedQuantity = quarantinedQty;
        Disposition = disposition;
        TargetLocationId = targetLocationId;
        InspectionNotes = notes?.Trim();
        InspectedAtUtc = inspectedAtUtc;
    }

    /// <summary>
    /// Marks the line as inventory-posted with reference to the ledger transaction.
    /// </summary>
    public void MarkInventoryPosted(long transactionId)
    {
        if (IsInventoryPosted)
        {
            throw new InvalidDomainStateException("Inventory has already been posted for this line item.");
        }

        IsInventoryPosted = true;
        InventoryTransactionId = transactionId;
    }
}

