using Warehouse3PL.Domain.Entities.Billing;

namespace Warehouse3PL.Application.Common.Interfaces;

/// <summary>
/// Service contract for capturing and managing immutable daily inventory balance snapshots.
/// DB-SPEC-3PL-2026 Phase 5.2 Recurring Storage &amp; Holding Fee Calculation Engine.
/// </summary>
public interface IInventorySnapshotService
{
    /// <summary>
    /// Captures a frozen end-of-day/midnight inventory snapshot for a client and facility on a specific date.
    /// Evaluates pallet counts, carton counts, cubic meters (CBM), holding statuses, and billable qualifications.
    /// Enforces database-level idempotency and prevents duplicate snapshot captures.
    /// </summary>
    Task<DailyInventorySnapshot> CaptureDailySnapshotAsync(
        Guid clientId,
        Guid facilityId,
        DateOnly snapshotDate,
        DateTime? snapshotTimestampUtc = null,
        Guid? userId = null,
        string? notes = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves an existing daily snapshot with its granular records.
    /// </summary>
    Task<DailyInventorySnapshot?> GetSnapshotAsync(
        Guid clientId,
        Guid facilityId,
        DateOnly snapshotDate,
        CancellationToken cancellationToken = default);
}

