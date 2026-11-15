using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Inbound;

/// <summary>
/// Client Purchase Order master record representing expected commercial inbound supply.
/// </summary>
public class PurchaseOrder : AuditableEntity<Guid>, ITenantEntity, ISoftDeletable
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid? SupplierId { get; set; }
    public string PoNumber { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; } = DateTime.UtcNow;
    public DateTime? ExpectedDeliveryDate { get; set; }
    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;
    public string Currency { get; set; } = "USD";
    public decimal TotalEstimatedCost { get; set; }
    public string? Notes { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime? CancelledAt { get; set; }

    /// <summary>
    /// Concurrency token for optimistic locking on status changes and line updates.
    /// </summary>
    public uint Version { get; set; } = 1;

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public Supplier? Supplier { get; set; }
    public ICollection<PurchaseOrderLine> Lines { get; set; } = [];
    public ICollection<AdvanceShippingNotice> Asns { get; set; } = [];

    public PurchaseOrder()
    {
        Id = Guid.NewGuid();
    }

    public void Submit()
    {
        if (Status != PurchaseOrderStatus.Draft)
        {
            throw new DomainException($"Cannot submit Purchase Order '{PoNumber}' because it is in '{Status}' status. Only Draft POs can be submitted.");
        }

        if (Lines.Count == 0)
        {
            throw new DomainException($"Cannot submit Purchase Order '{PoNumber}' without any line items.");
        }

        Status = PurchaseOrderStatus.Submitted;
        Version++;
    }

    public void Cancel(string reason)
    {
        if (Status == PurchaseOrderStatus.Cancelled)
        {
            return;
        }

        if (Status == PurchaseOrderStatus.PartiallyReceived || Status == PurchaseOrderStatus.Received || Status == PurchaseOrderStatus.Closed)
        {
            throw new DomainException($"Cannot cancel Purchase Order '{PoNumber}' in '{Status}' status because physical goods have already been processed.");
        }

        Status = PurchaseOrderStatus.Cancelled;
        CancellationReason = reason;
        CancelledAt = DateTime.UtcNow;

        foreach (var line in Lines.Where(l => l.Status == PurchaseOrderLineStatus.Open))
        {
            line.Status = PurchaseOrderLineStatus.Cancelled;
        }

        Version++;
    }

    public void RecordLineReceipt(Guid lineId, decimal quantityReceived)
    {
        var line = Lines.FirstOrDefault(l => l.Id == lineId);
        if (line == null)
        {
            throw new DomainException($"Purchase Order line '{lineId}' does not exist on PO '{PoNumber}'.");
        }

        if (line.Status == PurchaseOrderLineStatus.Cancelled)
        {
            throw new DomainException($"Cannot receive quantity for cancelled PO line {line.LineNumber}.");
        }

        line.ReceivedQty += quantityReceived;
        if (line.ReceivedQty >= line.ExpectedQty)
        {
            line.Status = PurchaseOrderLineStatus.Completed;
        }
        else if (line.ReceivedQty > 0)
        {
            line.Status = PurchaseOrderLineStatus.PartiallyReceived;
        }

        var allLinesCompleted = Lines.All(l => l.ReceivedQty >= l.ExpectedQty || l.Status == PurchaseOrderLineStatus.Cancelled);
        var anyLineReceived = Lines.Any(l => l.ReceivedQty > 0);

        if (allLinesCompleted)
        {
            Status = PurchaseOrderStatus.Received;
        }
        else if (anyLineReceived)
        {
            Status = PurchaseOrderStatus.PartiallyReceived;
        }

        Version++;
    }

    public void Close()
    {
        if (Status == PurchaseOrderStatus.Cancelled)
        {
            throw new DomainException($"Cannot close a cancelled Purchase Order '{PoNumber}'.");
        }

        Status = PurchaseOrderStatus.Closed;
        Version++;
    }
}

