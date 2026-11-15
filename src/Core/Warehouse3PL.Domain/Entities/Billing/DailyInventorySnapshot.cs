using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Billing;

/// <summary>
/// Authoritative, immutable historical daily inventory position snapshot for a 3PL Client at a specific Facility.
/// Captures end-of-day/midnight inventory balances for time-based recurring storage calculations.
/// DB-SPEC-3PL-2026 Phase 5.2 Recurring Storage &amp; Holding Fee Calculation Engine.
/// </summary>
public class DailyInventorySnapshot : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }

    /// <summary>
    /// Calendar date representing this daily storage position.
    /// </summary>
    public DateOnly SnapshotDate { get; set; }

    /// <summary>
    /// Exact UTC timestamp when the snapshot was executed (typically 23:59:59 UTC or 00:00:00 UTC).
    /// </summary>
    public DateTime SnapshotTimestampUtc { get; set; }

    public StorageSnapshotStatus Status { get; set; } = StorageSnapshotStatus.Draft;

    // Aggregated Metrics
    public decimal TotalQuantity { get; set; }
    public decimal TotalBillableQuantity { get; set; }

    public decimal TotalPalletCount { get; set; }
    public decimal TotalBillablePalletCount { get; set; }

    public decimal TotalCartonCount { get; set; }
    public decimal TotalBillableCartonCount { get; set; }

    public decimal TotalCbm { get; set; }
    public decimal TotalBillableCbm { get; set; }

    public int RecordCount { get; set; }
    public bool HasMissingDimensionsRecords { get; set; }

    // Finalization & Rating Lifecycle
    public DateTime? FinalizedAtUtc { get; set; }
    public Guid? FinalizedByUserId { get; set; }
    public DateTime? RatedAtUtc { get; set; }
    public string? Notes { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public ICollection<DailyInventorySnapshotRecord> Records { get; set; } = new List<DailyInventorySnapshotRecord>();

    public DailyInventorySnapshot()
    {
        Id = Guid.NewGuid();
    }

    public static DailyInventorySnapshot Create(
        Guid clientId,
        Guid facilityId,
        DateOnly snapshotDate,
        DateTime snapshotTimestampUtc,
        string? notes = null)
    {
        if (clientId == Guid.Empty)
        {
            throw new ValidationException(nameof(clientId), "Client ID is required for daily inventory snapshot.");
        }

        if (facilityId == Guid.Empty)
        {
            throw new ValidationException(nameof(facilityId), "Facility ID is required for daily inventory snapshot.");
        }

        return new DailyInventorySnapshot
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            FacilityId = facilityId,
            SnapshotDate = snapshotDate,
            SnapshotTimestampUtc = snapshotTimestampUtc == default ? DateTime.UtcNow : snapshotTimestampUtc,
            Status = StorageSnapshotStatus.Draft,
            Notes = notes?.Trim()
        };
    }

    /// <summary>
    /// Adds a granular inventory record to the snapshot while in Draft status.
    /// </summary>
    public void AddRecord(DailyInventorySnapshotRecord record)
    {
        if (Status != StorageSnapshotStatus.Draft)
        {
            throw new InvalidDomainStateException("Cannot add records to an already finalized or rated daily inventory snapshot.");
        }

        Records.Add(record);
        RecordCount++;

        TotalQuantity += record.Quantity;
        TotalPalletCount += record.PalletCount;
        TotalCartonCount += record.CartonCount;
        TotalCbm += record.Cbm;

        if (record.IsBillable)
        {
            TotalBillableQuantity += record.Quantity;
            TotalBillablePalletCount += record.PalletCount;
            TotalBillableCartonCount += record.CartonCount;
            TotalBillableCbm += record.Cbm;
        }

        if (record.HasMissingDimensions)
        {
            HasMissingDimensionsRecords = true;
        }
    }

    /// <summary>
    /// Seals and finalizes the snapshot, making it permanently immutable for audit and billing.
    /// </summary>
    public void FinalizeSnapshot(Guid? finalizedByUserId = null, DateTime? finalizedAtUtc = null)
    {
        if (Status == StorageSnapshotStatus.Finalized || Status == StorageSnapshotStatus.Rated)
        {
            throw new InvalidDomainStateException("Daily inventory snapshot is already finalized.");
        }

        Status = StorageSnapshotStatus.Finalized;
        FinalizedAtUtc = finalizedAtUtc ?? DateTime.UtcNow;
        FinalizedByUserId = finalizedByUserId;

        // Round summary metrics
        TotalQuantity = decimal.Round(TotalQuantity, 4, MidpointRounding.AwayFromZero);
        TotalBillableQuantity = decimal.Round(TotalBillableQuantity, 4, MidpointRounding.AwayFromZero);
        TotalPalletCount = decimal.Round(TotalPalletCount, 4, MidpointRounding.AwayFromZero);
        TotalBillablePalletCount = decimal.Round(TotalBillablePalletCount, 4, MidpointRounding.AwayFromZero);
        TotalCartonCount = decimal.Round(TotalCartonCount, 4, MidpointRounding.AwayFromZero);
        TotalBillableCartonCount = decimal.Round(TotalBillableCartonCount, 4, MidpointRounding.AwayFromZero);
        TotalCbm = decimal.Round(TotalCbm, 4, MidpointRounding.AwayFromZero);
        TotalBillableCbm = decimal.Round(TotalBillableCbm, 4, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Flags the snapshot as rated once its fees have been emitted into the billable activity ledger.
    /// </summary>
    public void MarkRated(DateTime? ratedAtUtc = null)
    {
        if (Status != StorageSnapshotStatus.Finalized)
        {
            throw new InvalidDomainStateException($"Cannot rate a snapshot in status '{Status}'. Snapshot must be Finalized first.");
        }

        Status = StorageSnapshotStatus.Rated;
        RatedAtUtc = ratedAtUtc ?? DateTime.UtcNow;
    }
}

