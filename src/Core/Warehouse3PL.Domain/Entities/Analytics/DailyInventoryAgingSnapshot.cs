using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Analytics;

/// <summary>
/// Granular read-model projection tracking inventory age, velocity, and expiry proximity per SKU, lot, and location.
/// Derived strictly from authoritative inventory state and immutable transaction logs.
/// Invariant: Receipt age, movement age, demand age, and expiry proximity are independent dimensions.
/// DB-SPEC-3PL-2026 Phase 6.3 Inventory Health & Space Utilization.
/// </summary>
public class DailyInventoryAgingSnapshot : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid LocationId { get; set; }
    public string LocationCode { get; set; } = string.Empty;
    public Guid ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public Guid? LotId { get; set; }
    public string? LotNumber { get; set; }
    public string? LpnCode { get; set; }

    public DateTime SnapshotDateUtc { get; set; }

    // Inventory Holding Status & Partitioned Quantities for Exact Ledger Reconciliation
    public InventoryHoldingStatus HoldingStatus { get; set; } = InventoryHoldingStatus.Available;
    public decimal QtyAvailable { get; set; }
    public decimal QtyAllocated { get; set; }
    public decimal QtyReserved { get; set; }
    public decimal QtyQuarantined { get; set; }
    public decimal QtyDamaged { get; set; }
    public decimal QtyOnHold { get; set; }

    // Five Independent Temporal Dimensions
    public DateTime? ReceivedAtUtc { get; set; }
    public int ReceiptAgeDays { get; set; }

    public DateTime? LastMovedAtUtc { get; set; }
    public int LastMovementAgeDays { get; set; }

    public DateTime? LastDemandedAtUtc { get; set; }
    public int LastDemandAgeDays { get; set; }
    public bool HasDemandHistory => LastDemandedAtUtc.HasValue;
    public int? DemandInactivityDays { get; set; }

    public DateTime? ExpirationDateUtc { get; set; }
    public int? DaysUntilExpiry { get; set; }

    public int HoldingDurationDays { get; set; }

    // Categorization
    public InventoryAgingBucket AgingBucket { get; set; } = InventoryAgingBucket.Current_0_30;
    public InventoryMovementVelocityCategory VelocityCategory { get; set; } = InventoryMovementVelocityCategory.FastMoving;

    // Distinct Health Flags (Invariant: Aged, Slow-moving, Non-moving, Dead-stock, and Expired are distinct)
    public bool IsAged { get; set; }
    public bool IsSlowMoving { get; set; }
    public bool IsNonMoving { get; set; }
    public bool IsDormant { get; set; }
    public bool IsDeadStock { get; set; }
    public bool IsNearExpiry { get; set; }
    public bool IsCriticalExpiry { get; set; }
    public bool IsExpired { get; set; }

    // Quantitative Footprint & Financial Valuation
    public decimal QuantityOnHand { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalInventoryValue { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal DailyHoldingCost { get; set; }
    public decimal AccruedHoldingCost { get; set; }

    public decimal OccupiedCbm { get; set; }
    public decimal PalletFootprint { get; set; }
    public bool HasMissingDimensionsFallback { get; set; }

    /// <summary>
    /// Audit link to authoritative source ledger record (e.g. StockOnHand ID).
    /// Read-only reference: the analytics plane never modifies authoritative stock ledgers.
    /// </summary>
    public string SourceInventoryReference { get; set; } = string.Empty;

    public DailyInventoryAgingSnapshot()
    {
        Id = Guid.NewGuid();
    }

    public static DailyInventoryAgingSnapshot Create(
        Guid clientId,
        Guid facilityId,
        Guid locationId,
        string locationCode,
        Guid itemId,
        string sku,
        DateTime snapshotDateUtc,
        decimal quantityOnHand,
        DateTime? receivedAtUtc,
        DateTime? lastMovedAtUtc,
        DateTime? lastDemandedAtUtc,
        DateTime? expirationDateUtc,
        InventoryAgingPolicy policy,
        decimal unitCost = 0m,
        decimal occupiedCbm = 0m,
        decimal palletFootprint = 0m,
        bool hasMissingDimensions = false,
        Guid? lotId = null,
        string? lotNumber = null,
        string? lpnCode = null,
        string sourceInventoryReference = "",
        InventoryHoldingStatus holdingStatus = InventoryHoldingStatus.Available,
        decimal qtyAvailable = 0m,
        decimal qtyAllocated = 0m,
        decimal qtyReserved = 0m,
        decimal qtyQuarantined = 0m,
        decimal qtyDamaged = 0m,
        decimal qtyOnHold = 0m,
        string currency = "USD")
    {
        if (clientId == Guid.Empty) throw new ValidationException(nameof(clientId), "Client ID is required.");
        if (facilityId == Guid.Empty) throw new ValidationException(nameof(facilityId), "Facility ID is required.");
        if (locationId == Guid.Empty) throw new ValidationException(nameof(locationId), "Location ID is required.");
        if (itemId == Guid.Empty) throw new ValidationException(nameof(itemId), "Item ID is required.");
        if (string.IsNullOrWhiteSpace(sku)) throw new ValidationException(nameof(sku), "SKU is required.");

        var totalQty = Math.Max(0m, quantityOnHand);

        // If specific partition quantities were not provided, default to holdingStatus
        if (qtyAvailable == 0 && qtyAllocated == 0 && qtyReserved == 0 && qtyQuarantined == 0 && qtyDamaged == 0 && qtyOnHold == 0)
        {
            switch (holdingStatus)
            {
                case InventoryHoldingStatus.Available: qtyAvailable = totalQty; break;
                case InventoryHoldingStatus.Allocated: qtyAllocated = totalQty; break;
                case InventoryHoldingStatus.Damaged: qtyDamaged = totalQty; break;
                case InventoryHoldingStatus.Quarantined: qtyQuarantined = totalQty; break;
                case InventoryHoldingStatus.OnHold: qtyOnHold = totalQty; break;
                default: qtyAvailable = totalQty; break;
            }
        }

        var snapshot = new DailyInventoryAgingSnapshot
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            FacilityId = facilityId,
            LocationId = locationId,
            LocationCode = locationCode,
            ItemId = itemId,
            Sku = sku,
            LotId = lotId,
            LotNumber = lotNumber,
            LpnCode = lpnCode,
            SnapshotDateUtc = snapshotDateUtc.Date,
            HoldingStatus = holdingStatus,
            QtyAvailable = Math.Max(0m, qtyAvailable),
            QtyAllocated = Math.Max(0m, qtyAllocated),
            QtyReserved = Math.Max(0m, qtyReserved),
            QtyQuarantined = Math.Max(0m, qtyQuarantined),
            QtyDamaged = Math.Max(0m, qtyDamaged),
            QtyOnHold = Math.Max(0m, qtyOnHold),
            QuantityOnHand = totalQty,
            UnitCost = Math.Max(0m, unitCost),
            TotalInventoryValue = totalQty * Math.Max(0m, unitCost),
            Currency = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant(),
            OccupiedCbm = Math.Max(0m, occupiedCbm),
            PalletFootprint = Math.Max(0m, palletFootprint),
            HasMissingDimensionsFallback = hasMissingDimensions,
            SourceInventoryReference = sourceInventoryReference,
            ReceivedAtUtc = receivedAtUtc,
            LastMovedAtUtc = lastMovedAtUtc,
            LastDemandedAtUtc = lastDemandedAtUtc,
            ExpirationDateUtc = expirationDateUtc
        };

        snapshot.CalculateAgesAndClassify(snapshotDateUtc.Date, policy);
        return snapshot;
    }

    /// <summary>
    /// Computes the 5 independent temporal metrics, demand inactivity, and applies the aging policy classification.
    /// </summary>
    public void CalculateAgesAndClassify(DateTime asOfDateUtc, InventoryAgingPolicy policy)
    {
        // 1. Receipt Age (Days since arrival at warehouse)
        ReceiptAgeDays = ReceivedAtUtc.HasValue 
            ? Math.Max(0, (int)(asOfDateUtc - ReceivedAtUtc.Value.Date).TotalDays)
            : 0;

        // 2. Movement Age (Days since last putaway / relocation)
        LastMovementAgeDays = LastMovedAtUtc.HasValue
            ? Math.Max(0, (int)(asOfDateUtc - LastMovedAtUtc.Value.Date).TotalDays)
            : ReceiptAgeDays;

        // 3. Demand Age & Demand Inactivity Days
        if (LastDemandedAtUtc.HasValue)
        {
            LastDemandAgeDays = Math.Max(0, (int)(asOfDateUtc - LastDemandedAtUtc.Value.Date).TotalDays);
            DemandInactivityDays = LastDemandAgeDays;
        }
        else
        {
            LastDemandAgeDays = ReceiptAgeDays;
            DemandInactivityDays = null; // Explicitly indicates no prior demand history
        }

        // 4. Holding Duration
        HoldingDurationDays = ReceiptAgeDays;

        // 5. Expiry Proximity (Days until lot expiration)
        if (ExpirationDateUtc.HasValue)
        {
            DaysUntilExpiry = (int)(ExpirationDateUtc.Value.Date - asOfDateUtc).TotalDays;
            IsExpired = policy.IsExpired(DaysUntilExpiry.Value);
            IsCriticalExpiry = policy.IsCriticalExpiry(DaysUntilExpiry.Value);
            IsNearExpiry = policy.IsNearExpiry(DaysUntilExpiry.Value);
        }
        else
        {
            DaysUntilExpiry = null;
            IsExpired = false;
            IsCriticalExpiry = false;
            IsNearExpiry = false;
        }

        // Determine Primary Aging Metric using Policy Default Basis
        int primaryAgeDays = policy.DefaultBasis switch
        {
            InventoryAgingBasis.LastDemandDate => LastDemandAgeDays,
            InventoryAgingBasis.LastMovementDate => LastMovementAgeDays,
            InventoryAgingBasis.ExpirationProximity => DaysUntilExpiry.HasValue ? Math.Max(0, 365 - DaysUntilExpiry.Value) : ReceiptAgeDays,
            _ => ReceiptAgeDays
        };

        AgingBucket = policy.ClassifyBucket(primaryAgeDays);
        VelocityCategory = policy.ClassifyVelocity(LastDemandAgeDays);

        // Distinct Semantics:
        // Aged: Exceeds receipt age threshold
        IsAged = policy.IsAged(ReceiptAgeDays);

        // Slow-moving: Velocity category is SlowMoving (demand within 91-180 days)
        IsSlowMoving = policy.IsSlowMovingVelocity(VelocityCategory);

        // Non-moving: Velocity category is NonMoving (demand within 181-365 days)
        IsNonMoving = policy.IsNonMovingVelocity(VelocityCategory);

        // Dormant: Exceeds dormant days but not dead stock
        IsDormant = primaryAgeDays >= policy.DormantThresholdDays && !policy.IsDeadStock(primaryAgeDays, HasDemandHistory, DemandInactivityDays);

        // Dead stock: Explicit business policy (e.g. 365+ days with zero demand or never demanded)
        IsDeadStock = policy.IsDeadStock(primaryAgeDays, HasDemandHistory, DemandInactivityDays);

        // Financial Holding Cost Exposure
        DailyHoldingCost = Math.Max(0m, OccupiedCbm * policy.DailyHoldingCostPerCbm);
        if (DailyHoldingCost == 0m && PalletFootprint > 0m)
        {
            DailyHoldingCost = Math.Max(0m, PalletFootprint * policy.DailyHoldingCostPerPallet);
        }
        AccruedHoldingCost = Math.Round(DailyHoldingCost * HoldingDurationDays, 2);
    }
}

