namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Lifecycle status of an immutable daily inventory storage snapshot.
/// DB-SPEC-3PL-2026 Phase 5.2 Recurring Storage &amp; Holding Fee Calculation Engine.
/// </summary>
public enum StorageSnapshotStatus
{
    /// <summary>
    /// Snapshot is actively being constructed or reconciled.
    /// </summary>
    Draft = 1,

    /// <summary>
    /// Snapshot is finalized and immutable, serving as the authoritative audit source for billing.
    /// </summary>
    Finalized = 2,

    /// <summary>
    /// Snapshot has been evaluated by the rating engine and posted to the billable activity ledger.
    /// </summary>
    Rated = 3
}

