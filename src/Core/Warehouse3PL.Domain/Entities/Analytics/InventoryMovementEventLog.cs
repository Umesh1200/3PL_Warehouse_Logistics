using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Analytics;

/// <summary>
/// Fine-grained log of classified warehouse movement and demand events for velocity and turnover analytics.
/// Invariants:
/// 1. Separates qualifying customer demand (OutboundPick, ShipmentDispatch) from operational handling (InternalRelocation, Replenishment, Putaway).
/// 2. Authoritative inventory tables (StockOnHand, Allocations) are never touched by this analytics projection.
/// DB-SPEC-3PL-2026 Phase 6.3 Inventory Health & Space Utilization.
/// </summary>
public class InventoryMovementEventLog : AuditableEntity<Guid>, ITenantEntity
{
    public Guid EventId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid ClientId { get; set; }
    public Guid ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;

    public InventoryMovementEventType EventType { get; set; }
    public bool IsQualifyingDemand { get; set; }
    public bool IsPhysicalMovement { get; set; }

    /// <summary>
    /// Indicates whether this event represents the canonical demand-counting event for velocity calculation.
    /// Invariant: Prevents double-counting customer demand when both OutboundPick and ShipmentDispatch
    /// occur for the same order line or fulfillment lifecycle.
    /// </summary>
    public bool IsCanonicalDemand { get; set; }

    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCostValue { get; set; }
    public DateTime TimestampUtc { get; set; }

    public Guid? SourceLocationId { get; set; }
    public Guid? DestinationLocationId { get; set; }
    public Guid? ZoneId { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;

    /// <summary>
    /// Authoritative monotonically increasing event sequence number across the event log stream.
    /// Used for checkpoint watermark tracking, deterministic replay ordering, and stream recovery.
    /// </summary>
    public long SequenceNumber { get; set; }

    public InventoryMovementEventLog()
    {
        Id = Guid.NewGuid();
    }

    public static InventoryMovementEventLog Create(
        Guid eventId,
        Guid facilityId,
        Guid clientId,
        Guid itemId,
        string sku,
        InventoryMovementEventType eventType,
        decimal quantity,
        DateTime timestampUtc,
        Guid? sourceLocationId = null,
        Guid? destinationLocationId = null,
        Guid? zoneId = null,
        decimal unitCost = 0m,
        string referenceNumber = "",
        bool? isCanonicalDemand = null,
        long sequenceNumber = 0L)
    {
        if (eventId == Guid.Empty) throw new ValidationException(nameof(eventId), "Event ID is required.");
        if (facilityId == Guid.Empty) throw new ValidationException(nameof(facilityId), "Facility ID is required.");
        if (clientId == Guid.Empty) throw new ValidationException(nameof(clientId), "Client ID is required.");
        if (itemId == Guid.Empty) throw new ValidationException(nameof(itemId), "Item ID is required.");
        if (string.IsNullOrWhiteSpace(sku)) throw new ValidationException(nameof(sku), "SKU is required.");
        if (quantity < 0m) throw new ValidationException(nameof(quantity), "Quantity cannot be negative.");

        var normalizedUtc = timestampUtc.Kind == DateTimeKind.Utc ? timestampUtc : DateTime.SpecifyKind(timestampUtc, DateTimeKind.Utc);
        var cost = Math.Max(0m, unitCost);
        var totalCost = Math.Round(quantity * cost, 4);

        bool qualifying = eventType.IsQualifyingDemand();
        bool canonical = isCanonicalDemand ?? (eventType == InventoryMovementEventType.OutboundPick);

        return new InventoryMovementEventLog
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            FacilityId = facilityId,
            ClientId = clientId,
            ItemId = itemId,
            Sku = sku.Trim(),
            EventType = eventType,
            IsQualifyingDemand = qualifying,
            IsPhysicalMovement = eventType.IsPhysicalMovement(),
            IsCanonicalDemand = canonical && qualifying,
            Quantity = quantity,
            UnitCost = cost,
            TotalCostValue = totalCost,
            TimestampUtc = normalizedUtc,
            SourceLocationId = sourceLocationId,
            DestinationLocationId = destinationLocationId,
            ZoneId = zoneId,
            ReferenceNumber = referenceNumber?.Trim() ?? string.Empty,
            SequenceNumber = sequenceNumber > 0L ? sequenceNumber : (long.TryParse(referenceNumber?.Replace("SEQ-", ""), out var parsedSeq) ? parsedSeq : 0L)
        };
    }
}

