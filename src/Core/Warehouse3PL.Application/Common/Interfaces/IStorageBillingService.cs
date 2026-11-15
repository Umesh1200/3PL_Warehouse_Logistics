using Warehouse3PL.Domain.Entities.Billing;

namespace Warehouse3PL.Application.Common.Interfaces;

/// <summary>
/// Summary result of a storage fee rating execution for a daily inventory snapshot.
/// </summary>
public class StorageBillingRunResult
{
    public Guid SnapshotId { get; set; }
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public DateOnly SnapshotDate { get; set; }
    public decimal TotalBillablePalletDays { get; set; }
    public decimal TotalBillableCartonDays { get; set; }
    public decimal TotalBillableCbmDays { get; set; }
    public decimal TotalStorageAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public List<BillableActivity> CreatedActivities { get; set; } = new();
    public List<string> AppliedChargesSummary { get; set; } = new();
}

/// <summary>
/// Result of end-of-period contractual minimum monthly spend commitment reconciliation.
/// </summary>
public class PeriodSpendReconciliationResult
{
    public Guid BillingPeriodId { get; set; }
    public Guid ClientId { get; set; }
    public Guid? FacilityId { get; set; }
    public decimal ContractualMinimumSpend { get; set; }
    public decimal ActualPeriodSpend { get; set; }
    public decimal ShortfallAmount { get; set; }
    public bool ShortfallCharged { get; set; }
    public Guid? ShortfallActivityId { get; set; }
    public string Currency { get; set; } = "USD";
    public string SummaryMessage { get; set; } = string.Empty;
}

/// <summary>
/// Service contract for calculating recurring warehouse holding fees, pallet/carton/CBM rating,
/// and period-level contractual minimum spend reconciliations.
/// DB-SPEC-3PL-2026 Phase 5.2 Recurring Storage &amp; Holding Fee Calculation Engine.
/// </summary>
public interface IStorageBillingService
{
    /// <summary>
    /// Evaluates a finalized daily inventory snapshot against active client rate card schedules,
    /// generating and rating billable storage activities (PalletDay, CartonDay, CbmDay).
    /// </summary>
    Task<StorageBillingRunResult> RateDailyStorageSnapshotAsync(
        Guid snapshotId,
        Guid? ratedByUserId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconciles total commercial billing charges against the contractual MinimumMonthlySpend
    /// commitment for a specific billing period. Generates an adjustment charge if spend fell below floor.
    /// </summary>
    Task<PeriodSpendReconciliationResult> ReconcilePeriodSpendCommitmentAsync(
        Guid clientId,
        Guid billingPeriodId,
        Guid? facilityId = null,
        Guid? ratedByUserId = null,
        CancellationToken cancellationToken = default);
}

