using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services.Billing;

/// <summary>
/// Authoritative calculation engine for recurring warehouse storage and holding fees.
/// Rates daily inventory snapshots against client rate cards (Pallet, Carton, CBM)
/// and reconciles billing-period contractual minimum monthly spend commitments.
/// DB-SPEC-3PL-2026 Phase 5.2 Recurring Storage &amp; Holding Fee Calculation Engine.
/// </summary>
public class StorageBillingService : IStorageBillingService
{
    private readonly IApplicationDbContext _context;
    private readonly IRateCardService _rateCardService;
    private readonly ITenantProvider _tenantProvider;
    private readonly ILogger<StorageBillingService> _logger;

    public StorageBillingService(
        IApplicationDbContext context,
        IRateCardService rateCardService,
        ITenantProvider tenantProvider,
        ILogger<StorageBillingService> logger)
    {
        _context = context;
        _rateCardService = rateCardService;
        _tenantProvider = tenantProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<StorageBillingRunResult> RateDailyStorageSnapshotAsync(
        Guid snapshotId,
        Guid? ratedByUserId = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _context.DailyInventorySnapshots
            .Include(s => s.Records)
            .FirstOrDefaultAsync(s => s.Id == snapshotId, cancellationToken);

        if (snapshot == null)
        {
            throw new EntityNotFoundException(nameof(DailyInventorySnapshot), snapshotId);
        }

        ValidateTenantAccess(snapshot.ClientId);

        var result = new StorageBillingRunResult
        {
            SnapshotId = snapshot.Id,
            ClientId = snapshot.ClientId,
            FacilityId = snapshot.FacilityId,
            SnapshotDate = snapshot.SnapshotDate
        };

        // Idempotency: If snapshot is already Rated, fetch previously created storage activities
        if (snapshot.Status == StorageSnapshotStatus.Rated)
        {
            _logger.LogInformation("Daily snapshot '{SnapshotId}' is already rated. Returning existing charges idempotently.", snapshotId);
            var existingActivities = await _context.BillableActivities
                .Where(a => a.SourceType == nameof(DailyInventorySnapshot) && a.SourceId == snapshot.Id)
                .ToListAsync(cancellationToken);

            result.CreatedActivities = existingActivities;
            result.TotalStorageAmount = existingActivities.Sum(a => a.TotalAmount ?? 0m);
            result.Currency = existingActivities.FirstOrDefault()?.Currency ?? "USD";
            result.AppliedChargesSummary.Add("Snapshot was previously rated. Returned idempotent existing commercial activities.");
            return result;
        }

        if (snapshot.Status != StorageSnapshotStatus.Finalized)
        {
            throw new InvalidDomainStateException($"Cannot rate daily storage snapshot in status '{snapshot.Status}'. Snapshot must be Finalized first.");
        }

        var snapshotDateUtc = snapshot.SnapshotDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        // 1. Resolve Effective Rate Card Schedule
        var schedule = await _rateCardService.GetEffectiveScheduleAsync(
            snapshot.ClientId,
            snapshot.FacilityId,
            snapshotDateUtc,
            cancellationToken: cancellationToken);

        if (schedule == null)
        {
            throw new InvalidDomainStateException(
                $"No active commercial rate card schedule found for Client '{snapshot.ClientId}', Facility '{snapshot.FacilityId}' on date '{snapshot.SnapshotDate:yyyy-MM-dd}'.");
        }

        result.Currency = schedule.Currency;

        // 2. Discover Applicable Storage Rate Lines
        var palletLine = schedule.FindLine(BillableActivityType.StoragePalletDay);
        var cartonLine = schedule.FindLine(BillableActivityType.StorageCartonDay);
        var cbmLine = schedule.FindLine(BillableActivityType.StorageCbmDay);

        // Fallback to client-wide schedule lines if facility schedule does not define specific storage lines
        if (palletLine == null && cartonLine == null && cbmLine == null && schedule.FacilityId.HasValue)
        {
            var clientWideSched = await _rateCardService.GetEffectiveScheduleAsync(
                snapshot.ClientId, null, snapshotDateUtc, schedule.Currency, cancellationToken);

            if (clientWideSched != null)
            {
                palletLine = clientWideSched.FindLine(BillableActivityType.StoragePalletDay);
                cartonLine = clientWideSched.FindLine(BillableActivityType.StorageCartonDay);
                cbmLine = clientWideSched.FindLine(BillableActivityType.StorageCbmDay);
            }
        }

        if (palletLine == null && cartonLine == null && cbmLine == null)
        {
            throw new InvalidDomainStateException(
                $"No storage rate lines (StoragePalletDay, StorageCartonDay, StorageCbmDay) configured for Client '{snapshot.ClientId}' on date '{snapshot.SnapshotDate:yyyy-MM-dd}'.");
        }

        // 3. Rate Pallet Storage (if configured and billable pallets exist)
        if (palletLine != null && snapshot.TotalBillablePalletCount > 0)
        {
            var rating = palletLine.CalculateRating(snapshot.TotalBillablePalletCount);
            var act = BillableActivity.Create(
                clientId: snapshot.ClientId,
                facilityId: snapshot.FacilityId,
                activityType: BillableActivityType.StoragePalletDay,
                sourceType: nameof(DailyInventorySnapshot),
                sourceId: snapshot.Id,
                sourceReference: $"SNAP-{snapshot.SnapshotDate:yyyyMMdd}-PALLET",
                quantity: snapshot.TotalBillablePalletCount,
                unitOfMeasure: "PALLET-DAY",
                occurredAtUtc: snapshotDateUtc,
                idempotencyKey: $"STORAGE-PALLET-{snapshot.Id}",
                description: $"Daily Pallet Storage for {snapshot.SnapshotDate:yyyy-MM-dd} ({snapshot.TotalBillablePalletCount:F2} pallet-days)");

            act.Rate(
                unitPrice: rating.UnitPrice,
                totalAmount: rating.TotalAmount,
                currency: schedule.Currency,
                rateCardId: schedule.RateCardId,
                rateCardTierId: rating.MatchedTierId,
                rateCardVersion: schedule.Version,
                ratedByUserId: ratedByUserId,
                ratedAtUtc: DateTime.UtcNow,
                rateCardScheduleId: schedule.Id);

            _context.BillableActivities.Add(act);
            result.CreatedActivities.Add(act);
            result.TotalStorageAmount += rating.TotalAmount;
            result.TotalBillablePalletDays = snapshot.TotalBillablePalletCount;
            result.AppliedChargesSummary.Add($"Pallet Storage: {snapshot.TotalBillablePalletCount:F2} pallets @ {rating.UnitPrice:F4} = {rating.TotalAmount:F2} {schedule.Currency}");
        }

        // 4. Rate Carton Storage (if configured and billable cartons exist)
        if (cartonLine != null && snapshot.TotalBillableCartonCount > 0)
        {
            var rating = cartonLine.CalculateRating(snapshot.TotalBillableCartonCount);
            var act = BillableActivity.Create(
                clientId: snapshot.ClientId,
                facilityId: snapshot.FacilityId,
                activityType: BillableActivityType.StorageCartonDay,
                sourceType: nameof(DailyInventorySnapshot),
                sourceId: snapshot.Id,
                sourceReference: $"SNAP-{snapshot.SnapshotDate:yyyyMMdd}-CARTON",
                quantity: snapshot.TotalBillableCartonCount,
                unitOfMeasure: "CARTON-DAY",
                occurredAtUtc: snapshotDateUtc,
                idempotencyKey: $"STORAGE-CARTON-{snapshot.Id}",
                description: $"Daily Carton Storage for {snapshot.SnapshotDate:yyyy-MM-dd} ({snapshot.TotalBillableCartonCount:F2} carton-days)");

            act.Rate(
                unitPrice: rating.UnitPrice,
                totalAmount: rating.TotalAmount,
                currency: schedule.Currency,
                rateCardId: schedule.RateCardId,
                rateCardTierId: rating.MatchedTierId,
                rateCardVersion: schedule.Version,
                ratedByUserId: ratedByUserId,
                ratedAtUtc: DateTime.UtcNow,
                rateCardScheduleId: schedule.Id);

            _context.BillableActivities.Add(act);
            result.CreatedActivities.Add(act);
            result.TotalStorageAmount += rating.TotalAmount;
            result.TotalBillableCartonDays = snapshot.TotalBillableCartonCount;
            result.AppliedChargesSummary.Add($"Carton Storage: {snapshot.TotalBillableCartonCount:F2} cartons @ {rating.UnitPrice:F4} = {rating.TotalAmount:F2} {schedule.Currency}");
        }

        // 5. Rate CBM Storage (if configured and billable CBM exists)
        if (cbmLine != null && snapshot.TotalBillableCbm > 0)
        {
            var rating = cbmLine.CalculateRating(snapshot.TotalBillableCbm);
            var act = BillableActivity.Create(
                clientId: snapshot.ClientId,
                facilityId: snapshot.FacilityId,
                activityType: BillableActivityType.StorageCbmDay,
                sourceType: nameof(DailyInventorySnapshot),
                sourceId: snapshot.Id,
                sourceReference: $"SNAP-{snapshot.SnapshotDate:yyyyMMdd}-CBM",
                quantity: snapshot.TotalBillableCbm,
                unitOfMeasure: "CBM-DAY",
                occurredAtUtc: snapshotDateUtc,
                idempotencyKey: $"STORAGE-CBM-{snapshot.Id}",
                description: $"Daily CBM Volumetric Storage for {snapshot.SnapshotDate:yyyy-MM-dd} ({snapshot.TotalBillableCbm:F4} CBM-days)");

            act.Rate(
                unitPrice: rating.UnitPrice,
                totalAmount: rating.TotalAmount,
                currency: schedule.Currency,
                rateCardId: schedule.RateCardId,
                rateCardTierId: rating.MatchedTierId,
                rateCardVersion: schedule.Version,
                ratedByUserId: ratedByUserId,
                ratedAtUtc: DateTime.UtcNow,
                rateCardScheduleId: schedule.Id);

            _context.BillableActivities.Add(act);
            result.CreatedActivities.Add(act);
            result.TotalStorageAmount += rating.TotalAmount;
            result.TotalBillableCbmDays = snapshot.TotalBillableCbm;
            result.AppliedChargesSummary.Add($"CBM Storage: {snapshot.TotalBillableCbm:F4} CBM @ {rating.UnitPrice:F4} = {rating.TotalAmount:F2} {schedule.Currency}");
        }

        // 6. Seal Snapshot as Rated
        snapshot.MarkRated();
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Rated Daily Storage Snapshot '{SnapshotId}' for Client '{ClientId}'. Total Storage Fees: {Total:F2} {Currency}.",
            snapshot.Id, snapshot.ClientId, result.TotalStorageAmount, result.Currency);

        return result;
    }

    /// <inheritdoc />
    public async Task<PeriodSpendReconciliationResult> ReconcilePeriodSpendCommitmentAsync(
        Guid clientId,
        Guid billingPeriodId,
        Guid? facilityId = null,
        Guid? ratedByUserId = null,
        CancellationToken cancellationToken = default)
    {
        ValidateTenantAccess(clientId);

        var period = await _context.BillingPeriods
            .FirstOrDefaultAsync(p => p.Id == billingPeriodId, cancellationToken);

        if (period == null)
        {
            throw new EntityNotFoundException(nameof(BillingPeriod), billingPeriodId);
        }

        if (period.ClientId != clientId)
        {
            throw new UnauthorizedException("Billing period client does not match requested tenant.");
        }

        var result = new PeriodSpendReconciliationResult
        {
            BillingPeriodId = billingPeriodId,
            ClientId = clientId,
            FacilityId = facilityId
        };

        // 1. Resolve Active RateCardSchedule for the period
        var schedule = await _rateCardService.GetEffectiveScheduleAsync(
            clientId,
            facilityId ?? period.FacilityId,
            period.StartDateUtc,
            cancellationToken: cancellationToken);

        if (schedule == null || !schedule.MinimumMonthlySpend.HasValue || schedule.MinimumMonthlySpend.Value <= 0)
        {
            result.SummaryMessage = "No contractual minimum monthly spend commitment configured for this period.";
            return result;
        }

        var commitment = schedule.MinimumMonthlySpend.Value;
        result.ContractualMinimumSpend = commitment;
        result.Currency = schedule.Currency;

        var idempotencyKey = $"MINSPEND-{billingPeriodId}-{(facilityId.HasValue ? facilityId.Value.ToString() : "ALL")}";

        // Idempotency: Check if shortfall was already charged for this period
        var existingAdjustment = await _context.BillableActivities
            .FirstOrDefaultAsync(a => a.ClientId == clientId && a.IdempotencyKey == idempotencyKey, cancellationToken);

        if (existingAdjustment != null)
        {
            result.ShortfallCharged = true;
            result.ShortfallActivityId = existingAdjustment.Id;
            result.ShortfallAmount = existingAdjustment.TotalAmount ?? 0m;
            result.SummaryMessage = $"Shortfall adjustment already recorded ({existingAdjustment.TotalAmount:F2} {result.Currency}) for period '{period.PeriodCode}'.";
            return result;
        }

        // 2. Query all Rated activities within this billing period (excluding any shortfall adjustment)
        var query = _context.BillableActivities
            .Where(a =>
                a.ClientId == clientId &&
                a.BillingPeriodId == billingPeriodId &&
                a.Status == BillableActivityStatus.Rated &&
                a.IdempotencyKey != idempotencyKey);

        if (facilityId.HasValue)
        {
            query = query.Where(a => a.FacilityId == facilityId.Value);
        }

        var actualSpend = await query.SumAsync(a => a.TotalAmount ?? 0m, cancellationToken);
        result.ActualPeriodSpend = actualSpend;

        // 3. Compare Actual Spend against Commitment Floor
        if (actualSpend >= commitment)
        {
            result.ShortfallCharged = false;
            result.ShortfallAmount = 0m;
            result.SummaryMessage = $"Actual period spend ({actualSpend:F2} {result.Currency}) meets or exceeds commitment floor ({commitment:F2} {result.Currency}). No adjustment charged.";
            return result;
        }

        // Shortfall detected
        var shortfall = commitment - actualSpend;
        result.ShortfallAmount = shortfall;

        // Emitting dedicated minimum spend shortfall reconciliation activity
        var shortfallActivity = BillableActivity.Create(
            clientId: clientId,
            facilityId: facilityId ?? period.FacilityId ?? Guid.Empty,
            activityType: BillableActivityType.CustomVas,
            sourceType: nameof(BillingPeriod),
            sourceId: period.Id,
            sourceReference: period.PeriodCode,
            quantity: 1m,
            unitOfMeasure: "ADJUSTMENT",
            occurredAtUtc: period.EndDateUtc,
            idempotencyKey: idempotencyKey,
            description: $"Contractual Minimum Monthly Spend Reconciliation Shortfall (Commitment: {commitment:F2}, Actual: {actualSpend:F2})",
            billingPeriodId: period.Id);

        shortfallActivity.Rate(
            unitPrice: shortfall,
            totalAmount: shortfall,
            currency: schedule.Currency,
            rateCardId: schedule.RateCardId,
            rateCardTierId: null,
            rateCardVersion: schedule.Version,
            ratedByUserId: ratedByUserId,
            ratedAtUtc: DateTime.UtcNow,
            rateCardScheduleId: schedule.Id);

        _context.BillableActivities.Add(shortfallActivity);
        await _context.SaveChangesAsync(cancellationToken);

        result.ShortfallCharged = true;
        result.ShortfallActivityId = shortfallActivity.Id;
        result.SummaryMessage = $"Minimum spend shortfall of {shortfall:F2} {result.Currency} charged to reach contractual commitment floor {commitment:F2} {result.Currency}.";

        _logger.LogInformation(
            "Reconciled minimum monthly spend for Client '{ClientId}', Period '{Period}'. Commitment: {Floor:F2}, Actual: {Actual:F2}, Shortfall Charged: {Shortfall:F2} {Currency}.",
            clientId, period.PeriodCode, commitment, actualSpend, shortfall, result.Currency);

        return result;
    }

    private void ValidateTenantAccess(Guid clientId)
    {
        if (_tenantProvider.TenantId.HasValue &&
            !_tenantProvider.BypassTenantFilter &&
            _tenantProvider.TenantId.Value != clientId)
        {
            throw new UnauthorizedException("Unauthorized cross-tenant storage billing operation.");
        }
    }
}
