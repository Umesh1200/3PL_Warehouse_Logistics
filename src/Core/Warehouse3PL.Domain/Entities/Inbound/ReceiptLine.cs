using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Inbound;

/// <summary>
/// Individual LPN / package scan intake record within a ReceivingSession.
/// DB-SPEC-3PL-2026 Module 5.
/// </summary>
public class ReceiptLine : BaseEntity<Guid>, ISoftDeletable
{
    public Guid ReceivingSessionId { get; set; }
    public Guid ItemId { get; set; }
    public Guid? PurchaseOrderLineId { get; set; }
    public Guid? AsnLineItemId { get; set; }

    /// <summary>
    /// Unique License Plate Number / Barcode for container/pallet/carton tracking.
    /// </summary>
    public string Lpn { get; set; } = string.Empty;

    public decimal QuantityReceived { get; set; }
    public decimal QuantityExpected { get; set; }

    // Traceability & Weights
    public string? LotNumber { get; set; }
    public string? SerialNumber { get; set; }
    public decimal? CatchWeightKg { get; set; }
    public DateTime? ManufactureDate { get; set; }
    public DateTime? ExpirationDate { get; set; }

    public ReceiptConditionStatus Condition { get; set; } = ReceiptConditionStatus.Sound;
    public Guid? StagingLocationId { get; set; }
    public string? Notes { get; set; }
    public DateTime ScannedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Concurrency token for optimistic locking on receipt line updates.
    /// </summary>
    public uint Version { get; set; } = 1;

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigations
    public ReceivingSession ReceivingSession { get; set; } = null!;
    public Item Item { get; set; } = null!;
    public PurchaseOrderLine? PurchaseOrderLine { get; set; }
    public AsnLineItem? AsnLineItem { get; set; }
    public Location? StagingLocation { get; set; }
    public ICollection<ReceivingDiscrepancy> Discrepancies { get; set; } = [];

    public ReceiptLine()
    {
        Id = Guid.NewGuid();
    }
}

