using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Analytics;

/// <summary>
/// Historical record of SKU velocity, Pareto / ABC classification, and inventory movement frequency.
/// Invariants:
/// 1. Distinguishes qualifying customer demand (picks, shipments) from physical warehouse movements (relocations, replenishments).
/// 2. Distinguishes Daily Demand Velocity (demand units/day) from Inventory Turnover (COGS / Avg Inventory Value) and Movement Frequency (tasks/day).
/// 3. Retains observation window, calculation period, input metrics, criterion used, and version for historical consistency.
/// DB-SPEC-3PL-2026 Phase 6.3 Inventory Health & Space Utilization.
/// </summary>
public class SkuVelocityClassificationRecord : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;

    public AbcClassificationCategory AbcClass { get; set; } = AbcClassificationCategory.ClassC;
    public AbcClassificationCriteria CriteriaUsed { get; set; } = AbcClassificationCriteria.AnnualConsumptionValue;
    public SkuClassificationRecordType RecordType { get; set; } = SkuClassificationRecordType.VelocityOperational;
    public InventoryMovementVelocityCategory VelocityCategory { get; set; } = InventoryMovementVelocityCategory.MediumMoving;
    public VelocityObservationWindow ObservationWindow { get; set; } = VelocityObservationWindow.Days90;

    /// <summary>
    /// Unique identifier for the batch classification run, guaranteeing run isolation and concurrency safety.
    /// </summary>
    public Guid? ClassificationRunId { get; set; }

    // Calculation Period Window
    public DateTime CalculationPeriodStartUtc { get; set; }
    public DateTime CalculationPeriodEndUtc { get; set; }
    public int PeriodDurationDays => Math.Max(1, (int)(CalculationPeriodEndUtc.Date - CalculationPeriodStartUtc.Date).TotalDays);

    // Underlying Pareto Input & Customer Demand Metrics
    public int TotalPickTasksCount { get; set; }
    public decimal TotalUnitsDemanded { get; set; }

    /// <summary>
    /// Monetary consumption demand value within the specific observation window (Period Consumption Value).
    /// </summary>
    public decimal TotalDemandValue { get; set; }
    public decimal PeriodDemandValue { get; set; }

    /// <summary>
    /// Standardized Annualized Consumption Value (ACV) = PeriodDemandValue * (365 / PeriodDurationDays).
    /// Enables direct comparability across different observation horizons (e.g. 30d vs 365d).
    /// </summary>
    public decimal AnnualizedConsumptionValue { get; set; }

    public int OrderLinesCount { get; set; }
    public int TotalDemandEventsCount { get; set; }

    // Physical Movement & Warehouse Work Metrics
    public int TotalMovementEventsCount { get; set; }
    public decimal TotalUnitsMoved { get; set; }
    public int TotalReplenishmentCount { get; set; }
    public int TotalRelocationCount { get; set; }

    // Inventory Turnover & Valuation Metrics
    public decimal CostOfGoodsConsumed { get; set; }
    public decimal AverageInventoryValue { get; set; }
    public decimal InventoryTurnoverRatio { get; set; }

    /// <summary>
    /// Effective days of supply based on operational demand coverage or financial turnover.
    /// Invariant: Safe from NaN and divide-by-zero. Returns 9999.0 when stock is held with zero demand (indefinite/dormant).
    /// </summary>
    public decimal DaysOfSupply { get; set; }

    /// <summary>
    /// Capital/Financial Days of Supply: (365 / AnnualizedTurnover).
    /// </summary>
    public decimal DaysOfSupplyFinancial { get; set; }

    /// <summary>
    /// Operational Physical Unit Coverage: (CurrentOnHandUnits / DailyDemandVelocity).
    /// </summary>
    public decimal DaysOfSupplyOperational { get; set; }

    /// <summary>
    /// Current physical stock on hand quantity.
    /// </summary>
    public decimal CurrentOnHandUnits { get; set; }

    // Demand Anomaly & Classification Flags
    public bool IsDemandSpike { get; set; }
    public bool IsSeasonalPattern { get; set; }

    // Frequency Metrics (Tasks per calendar day)
    public decimal PickFrequencyPerDay => PeriodDurationDays > 0 ? Math.Round((decimal)TotalPickTasksCount / PeriodDurationDays, 4) : 0m;
    public decimal ReplenishmentFrequencyPerDay => PeriodDurationDays > 0 ? Math.Round((decimal)TotalReplenishmentCount / PeriodDurationDays, 4) : 0m;
    public decimal RelocationFrequencyPerDay => PeriodDurationDays > 0 ? Math.Round((decimal)TotalRelocationCount / PeriodDurationDays, 4) : 0m;

    /// <summary>
    /// Cumulative percentage share of total volume/value in the facility/client portfolio (0.00% to 100.00%).
    /// </summary>
    public decimal CumulativePercentage { get; set; }

    /// <summary>
    /// Pareto rank order in portfolio (1 = highest contributor).
    /// </summary>
    public int RankInPortfolio { get; set; }

    /// <summary>
    /// Associated classification policy ID.
    /// </summary>
    public Guid? PolicyId { get; set; }

    /// <summary>
    /// Normalized score when using weighted composite criteria (0.00 to 100.00).
    /// </summary>
    public decimal CompositeScore { get; set; }

    /// <summary>
    /// Cumulative threshold percentage configured for Class A at calculation time.
    /// Preserves exact historical policy cutoffs.
    /// </summary>
    public decimal? ClassAThresholdPercent { get; set; }

    /// <summary>
    /// Cumulative threshold percentage configured for Class B at calculation time.
    /// Preserves exact historical policy cutoffs.
    /// </summary>
    public decimal? ClassBThresholdPercent { get; set; }

    /// <summary>
    /// Inventory cost basis applied during calculation (StandardCost, UnitCost, FIFO, etc.).
    /// </summary>
    public InventoryValuationBasis? ValuationBasisUsed { get; set; }

    /// <summary>
    /// Average qualifying demand units per calendar day over calculation period.
    /// </summary>
    public decimal DailyDemandVelocity => PeriodDurationDays > 0 ? Math.Round(TotalUnitsDemanded / PeriodDurationDays, 4) : 0m;

    // Spatial Dimensions for Slotting and Pick Velocity Profiling
    public Guid? LocationId { get; set; }
    public Guid? ZoneId { get; set; }
    public string? Aisle { get; set; }

    /// <summary>
    /// Classification algorithm run version.
    /// </summary>
    public int Version { get; set; } = 1;
    public DateTime CalculatedAtUtc { get; set; } = DateTime.UtcNow;

    public SkuVelocityClassificationRecord()
    {
        Id = Guid.NewGuid();
    }

    public static SkuVelocityClassificationRecord Create(
        Guid clientId,
        Guid facilityId,
        Guid itemId,
        string sku,
        AbcClassificationCategory abcClass,
        AbcClassificationCriteria criteriaUsed,
        InventoryMovementVelocityCategory velocityCategory,
        DateTime periodStartUtc,
        DateTime periodEndUtc,
        int pickTasksCount,
        decimal unitsDemanded,
        decimal demandValue,
        int orderLinesCount,
        decimal cumulativePercentage,
        int version = 1,
        VelocityObservationWindow observationWindow = VelocityObservationWindow.Days90,
        int demandEventsCount = 0,
        int movementEventsCount = 0,
        decimal unitsMoved = 0m,
        int replenishmentCount = 0,
        int relocationCount = 0,
        decimal cogsConsumed = 0m,
        decimal avgInventoryValue = 0m,
        decimal turnoverRatio = 0m,
        decimal daysOfSupply = 0m,
        Guid? locationId = null,
        Guid? zoneId = null,
        string? aisle = null,
        decimal daysOfSupplyFinancial = 0m,
        decimal daysOfSupplyOperational = 0m,
        decimal currentOnHandUnits = 0m,
        bool isDemandSpike = false,
        bool isSeasonalPattern = false,
        int rankInPortfolio = 0,
        Guid? policyId = null,
        decimal compositeScore = 0m,
        SkuClassificationRecordType recordType = SkuClassificationRecordType.VelocityOperational,
        Guid? classificationRunId = null,
        decimal? annualizedConsumptionValue = null,
        decimal? classAThresholdPercent = null,
        decimal? classBThresholdPercent = null,
        InventoryValuationBasis? valuationBasisUsed = null)
    {
        if (clientId == Guid.Empty) throw new ValidationException(nameof(clientId), "Client ID is required.");
        if (facilityId == Guid.Empty) throw new ValidationException(nameof(facilityId), "Facility ID is required.");
        if (itemId == Guid.Empty) throw new ValidationException(nameof(itemId), "Item ID is required.");
        if (string.IsNullOrWhiteSpace(sku)) throw new ValidationException(nameof(sku), "SKU is required.");
        if (periodEndUtc < periodStartUtc) throw new ValidationException(nameof(periodEndUtc), "Period end cannot precede period start.");

        int durationDays = Math.Max(1, (int)(periodEndUtc.Date - periodStartUtc.Date).TotalDays);

        // Standardized Annualized Consumption Value (ACV)
        decimal annualizedAcv = annualizedConsumptionValue ??
            (durationDays > 0 ? Math.Round(Math.Max(0m, demandValue) * (365m / durationDays), 2) : Math.Max(0m, demandValue));

        // Auto-calculate annualized turnover ratio if not supplied but inputs are present
        decimal calculatedTurnover = turnoverRatio;
        if (calculatedTurnover == 0m && avgInventoryValue > 0m && cogsConsumed > 0m)
        {
            calculatedTurnover = Math.Round((cogsConsumed / avgInventoryValue) * (365m / durationDays), 4);
        }

        // Financial Days of Supply: (365 / AnnualizedTurnover)
        decimal finDos = daysOfSupplyFinancial;
        if (finDos == 0m)
        {
            if (calculatedTurnover > 0m)
            {
                finDos = Math.Round(365m / calculatedTurnover, 2);
            }
            else
            {
                // Explicit safe policy: zero turnover with positive valuation -> 9999 (indefinite), zero valuation -> 0
                finDos = avgInventoryValue > 0m ? 9999.0m : 0.0m;
            }
        }

        // Operational Days of Supply: (CurrentOnHand / DailyDemandVelocity)
        decimal dailyDemand = durationDays > 0 ? unitsDemanded / durationDays : 0m;
        decimal opDos = daysOfSupplyOperational;
        if (opDos == 0m)
        {
            if (dailyDemand > 0m)
            {
                opDos = Math.Round(currentOnHandUnits / dailyDemand, 2);
            }
            else
            {
                // Explicit safe policy: zero demand with positive stock -> 9999 (indefinite/non-moving), zero stock -> 0
                opDos = currentOnHandUnits > 0m ? 9999.0m : 0.0m;
            }
        }

        // Effective DOS: prefers operational unit coverage if valid, falls back to financial or sentinel
        decimal calculatedDos = daysOfSupply;
        if (calculatedDos == 0m)
        {
            calculatedDos = opDos > 0m && opDos != 9999.0m ? opDos : finDos;
        }

        return new SkuVelocityClassificationRecord
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            FacilityId = facilityId,
            ItemId = itemId,
            Sku = sku.Trim(),
            AbcClass = abcClass,
            CriteriaUsed = criteriaUsed,
            VelocityCategory = velocityCategory,
            ObservationWindow = observationWindow,
            CalculationPeriodStartUtc = periodStartUtc.Kind == DateTimeKind.Utc ? periodStartUtc : DateTime.SpecifyKind(periodStartUtc, DateTimeKind.Utc),
            CalculationPeriodEndUtc = periodEndUtc.Kind == DateTimeKind.Utc ? periodEndUtc : DateTime.SpecifyKind(periodEndUtc, DateTimeKind.Utc),
            TotalPickTasksCount = Math.Max(0, pickTasksCount),
            TotalUnitsDemanded = Math.Max(0m, unitsDemanded),
            TotalDemandValue = Math.Max(0m, demandValue),
            PeriodDemandValue = Math.Max(0m, demandValue),
            AnnualizedConsumptionValue = Math.Max(0m, annualizedAcv),
            RecordType = recordType,
            ClassificationRunId = classificationRunId,
            OrderLinesCount = Math.Max(0, orderLinesCount),
            TotalDemandEventsCount = Math.Max(0, demandEventsCount > 0 ? demandEventsCount : pickTasksCount),
            TotalMovementEventsCount = Math.Max(0, movementEventsCount),
            TotalUnitsMoved = Math.Max(0m, unitsMoved),
            TotalReplenishmentCount = Math.Max(0, replenishmentCount),
            TotalRelocationCount = Math.Max(0, relocationCount),
            CostOfGoodsConsumed = Math.Max(0m, cogsConsumed > 0m ? cogsConsumed : demandValue),
            AverageInventoryValue = Math.Max(0m, avgInventoryValue),
            InventoryTurnoverRatio = Math.Max(0m, calculatedTurnover),
            DaysOfSupply = Math.Max(0m, calculatedDos),
            DaysOfSupplyFinancial = Math.Max(0m, finDos),
            DaysOfSupplyOperational = Math.Max(0m, opDos),
            CurrentOnHandUnits = Math.Max(0m, currentOnHandUnits),
            IsDemandSpike = isDemandSpike,
            IsSeasonalPattern = isSeasonalPattern,
            CumulativePercentage = Math.Clamp(cumulativePercentage, 0m, 100m),
            RankInPortfolio = Math.Max(0, rankInPortfolio),
            PolicyId = policyId,
            CompositeScore = Math.Max(0m, compositeScore),
            ClassAThresholdPercent = classAThresholdPercent,
            ClassBThresholdPercent = classBThresholdPercent,
            ValuationBasisUsed = valuationBasisUsed,
            LocationId = locationId,
            ZoneId = zoneId,
            Aisle = string.IsNullOrWhiteSpace(aisle) ? null : aisle.Trim(),
            Version = version,
            CalculatedAtUtc = DateTime.UtcNow
        };
    }
}
