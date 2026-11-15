using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Inbound;

/// <summary>
/// Advance Shipping Notice (ASN) identifying physical inbound shipment to a warehouse facility.
/// DB-SPEC-3PL-2026 Module 5 (advance_shipping_notices).
/// </summary>
public class AdvanceShippingNotice : AuditableEntity<Guid>, ITenantEntity, ISoftDeletable
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid? PurchaseOrderId { get; set; }
    public string AsnNumber { get; set; } = string.Empty;
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public string? CarrierName { get; set; }
    public string? TrackingReference { get; set; }
    public string? TrailerOrContainerNumber { get; set; }
    public DateTime? EstimatedArrival { get; set; }
    public DateTime? ActualArrival { get; set; }
    public AsnStatus Status { get; set; } = AsnStatus.Draft;
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
    public PurchaseOrder? PurchaseOrder { get; set; }
    public Supplier? Supplier { get; set; }
    public ICollection<AsnLineItem> Lines { get; set; } = [];

    public AdvanceShippingNotice()
    {
        Id = Guid.NewGuid();
    }

    public void MarkInTransit()
    {
        if (Status != AsnStatus.Draft)
        {
            throw new DomainException($"Cannot mark ASN '{AsnNumber}' as InTransit because it is currently '{Status}'. Only Draft ASNs can transition to InTransit.");
        }

        if (Lines.Count == 0)
        {
            throw new DomainException($"Cannot dispatch ASN '{AsnNumber}' without any line items.");
        }

        Status = AsnStatus.InTransit;
        Version++;
    }

    public void MarkArrived(DateTime? arrivalTimestamp = null)
    {
        if (Status != AsnStatus.Draft && Status != AsnStatus.InTransit)
        {
            throw new DomainException($"Cannot mark ASN '{AsnNumber}' as Arrived because it is currently '{Status}'. Only Draft or InTransit ASNs can be marked as Arrived.");
        }

        if (Lines.Count == 0)
        {
            throw new DomainException($"Cannot mark ASN '{AsnNumber}' as Arrived without any line items.");
        }

        Status = AsnStatus.Arrived;
        ActualArrival = arrivalTimestamp ?? DateTime.UtcNow;
        Version++;
    }

    public void StartReceiving()
    {
        if (Status != AsnStatus.Arrived && Status != AsnStatus.InTransit)
        {
            throw new DomainException($"Cannot start receiving ASN '{AsnNumber}' from '{Status}' status. Must be Arrived or InTransit.");
        }

        Status = AsnStatus.Receiving;
        Version++;
    }

    public void RecordLineReceipt(Guid lineId, decimal quantityReceived)
    {
        var line = Lines.FirstOrDefault(l => l.Id == lineId);
        if (line != null)
        {
            line.ReceivedQty += quantityReceived;
        }

        var allLinesReceived = Lines.Count > 0 && Lines.All(l => l.ReceivedQty >= l.ExpectedQty);
        if (allLinesReceived)
        {
            Status = AsnStatus.Received;
        }
        else
        {
            Status = AsnStatus.Receiving;
        }

        Version++;
    }

    public void CompleteReceiving()
    {
        if (Status == AsnStatus.Received)
        {
            return;
        }

        if (Status != AsnStatus.Receiving && Status != AsnStatus.Arrived)
        {
            throw new DomainException($"Cannot complete receiving ASN '{AsnNumber}' from '{Status}' status.");
        }

        Status = AsnStatus.Received;
        Version++;
    }

    public void Cancel(string reason)
    {
        if (Status == AsnStatus.Cancelled)
        {
            return;
        }

        if (Status == AsnStatus.Receiving || Status == AsnStatus.Received)
        {
            throw new DomainException($"Cannot cancel ASN '{AsnNumber}' because receiving execution is already '{Status}'.");
        }

        Status = AsnStatus.Cancelled;
        CancellationReason = reason;
        CancelledAt = DateTime.UtcNow;
        Version++;
    }
}

