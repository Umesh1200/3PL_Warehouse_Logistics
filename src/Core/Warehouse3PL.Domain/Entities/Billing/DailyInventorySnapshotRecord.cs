using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Billing;

/// <summary>
/// Granular line item within an immutable daily inventory snapshot.
/// Captures SKU, location, LPN, holding status, volumetric measurements, and billable qualification.
/// DB-SPEC-3PL-2026 Phase 5.2 Recurring Storage &amp; Holding Fee Calculation Engine.
/// </summary>
public class DailyInventorySnapshotRecord : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid DailyInventorySnapshotId { get; set; }
    public Guid FacilityId { get; set; }

    public Guid LocationId { get; set; }
    public string LocationCode { get; set; } = string.Empty;

    public Guid ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;

    public Guid? LotId { get; set; }
    public string? LotNumber { get; set; }

    public Guid? HandlingUnitId { get; set; }
    public string? LpnCode { get; set; }

    public InventoryHoldingStatus HoldingStatus { get; set; } = InventoryHoldingStatus.Available;

    /// <summary>
    /// Total inventory unit quantity in base units (e.g. Each/Pieces).
    /// </summary>
    public decimal Quantity { get; set; }

    /// <summary>
    /// Calculated pallet footprint occupied by this line.
    /// </summary>
    public decimal PalletCount { get; set; }

    /// <summary>
    /// Calculated carton/case count.
    /// </summary>
    public decimal CartonCount { get; set; }

    /// <summary>
    /// Calculated cubic meter volume (CBM) = Length(m) * Width(m) * Height(m) * Qty.
    /// </summary>
    public decimal Cbm { get; set; }

    /// <summary>
    /// Indicates whether master product dimensions were missing (triggered fallback volumetric calculation).
    /// </summary>
    public bool HasMissingDimensions { get; set; }

    /// <summary>
    /// Indicates whether this record is commercially billable according to the client storage billing policy.
    /// </summary>
    public bool IsBillable { get; set; } = true;

    /// <summary>
    /// Operational traceability reference (e.g. StockOnHand ID).
    /// </summary>
    public string SourceInventoryReference { get; set; } = string.Empty;

    // Navigations
    public DailyInventorySnapshot DailyInventorySnapshot { get; set; } = null!;
    public Location Location { get; set; } = null!;
    public Item Item { get; set; } = null!;
    public HandlingUnit? HandlingUnit { get; set; }

    public DailyInventorySnapshotRecord()
    {
        Id = Guid.NewGuid();
    }

    public static DailyInventorySnapshotRecord Create(
        Guid clientId,
        Guid dailyInventorySnapshotId,
        Guid facilityId,
        Guid locationId,
        string locationCode,
        Guid itemId,
        string sku,
        InventoryHoldingStatus holdingStatus,
        decimal quantity,
        decimal palletCount,
        decimal cartonCount,
        decimal cbm,
        bool hasMissingDimensions,
        bool isBillable,
        string sourceInventoryReference,
        Guid? lotId = null,
        string? lotNumber = null,
        Guid? handlingUnitId = null,
        string? lpnCode = null)
    {
        if (clientId == Guid.Empty)
        {
            throw new ValidationException(nameof(clientId), "Client ID is required for snapshot record.");
        }

        if (dailyInventorySnapshotId == Guid.Empty)
        {
            throw new ValidationException(nameof(dailyInventorySnapshotId), "Snapshot ID is required.");
        }

        if (quantity < 0)
        {
            throw new ValidationException(nameof(quantity), "Quantity cannot be negative in snapshot record.");
        }

        return new DailyInventorySnapshotRecord
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            DailyInventorySnapshotId = dailyInventorySnapshotId,
            FacilityId = facilityId,
            LocationId = locationId,
            LocationCode = locationCode.Trim(),
            ItemId = itemId,
            Sku = sku.Trim(),
            LotId = lotId,
            LotNumber = lotNumber?.Trim(),
            HandlingUnitId = handlingUnitId,
            LpnCode = lpnCode?.Trim(),
            HoldingStatus = holdingStatus,
            Quantity = decimal.Round(quantity, 4, MidpointRounding.AwayFromZero),
            PalletCount = decimal.Round(palletCount, 4, MidpointRounding.AwayFromZero),
            CartonCount = decimal.Round(cartonCount, 4, MidpointRounding.AwayFromZero),
            Cbm = decimal.Round(cbm, 4, MidpointRounding.AwayFromZero),
            HasMissingDimensions = hasMissingDimensions,
            IsBillable = isBillable,
            SourceInventoryReference = sourceInventoryReference.Trim()
        };
    }
}

