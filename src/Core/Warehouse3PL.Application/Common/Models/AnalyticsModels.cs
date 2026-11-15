using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

public record FacilityProductivityDto
{
    public Guid FacilityId { get; init; }
    public DateTime DateUtc { get; init; }
    public TimeGrain TimeGrain { get; init; }

    public int OrdersReceivedCount { get; init; }
    public int OrdersAllocatedCount { get; init; }
    public int LinesPickedCount { get; init; }
    public int UnitsPickedCount { get; init; }
    public int CartonsPackedCount { get; init; }
    public int OrdersDispatchedCount { get; init; }
    public int PackagesDispatchedCount { get; init; }
    public int InboundPalletsReceivedCount { get; init; }
    public int InboundPutawayCompletedCount { get; init; }
    public int ReturnsInspectedCount { get; init; }

    public int ActiveWorkerHeadcount { get; init; }
    public decimal TotalLaborHours { get; init; }
    public decimal TotalPickHours { get; init; }
    public decimal TotalPutawayHours { get; init; }
    public decimal UnitsPerLaborHour { get; init; }
    public decimal PutawayPerLaborHour { get; init; }

    public decimal AverageFulfillmentCycleTimeHours { get; init; }
    public decimal SameDayDispatchAdherencePercent { get; init; }
}

public record HourlyThroughputPacingDto
{
    public Guid FacilityId { get; init; }
    public DateTime HourTimestampUtc { get; init; }

    public int OrdersReceivedCount { get; init; }
    public int OrdersAllocatedCount { get; init; }
    public int LinesPickedCount { get; init; }
    public int UnitsPickedCount { get; init; }
    public int CartonsPackedCount { get; init; }
    public int OrdersDispatchedCount { get; init; }
    public int PackagesDispatchedCount { get; init; }
    public int InboundPalletsReceivedCount { get; init; }
    public int InboundPutawayCompletedCount { get; init; }
    public int ReturnsInspectedCount { get; init; }

    public int ActiveWorkerHeadcount { get; init; }
    public decimal TotalLaborHours { get; init; }
    public decimal PickDurationHours { get; init; }
    public decimal PutawayDurationHours { get; init; }
    public decimal UnitsPerLaborHour { get; init; }
    public decimal PutawayPerLaborHour { get; init; }

    public decimal AverageCycleTimeHours { get; init; }
    public decimal SameDayDispatchAdherencePercent { get; init; }
    public int ProjectionVersion { get; init; } = 1;
}

public record ClientProfitabilityDto
{
    public Guid ClientId { get; init; }
    public Guid? FacilityId { get; init; }
    public DateTime DateUtc { get; init; }
    public TimeGrain TimeGrain { get; init; }
    public string Currency { get; init; } = "USD";

    public decimal BilledStorageRevenue { get; init; }
    public decimal BilledHandlingRevenue { get; init; }
    public decimal BilledFreightRevenue { get; init; }
    public decimal BilledVasRevenue { get; init; }
    public decimal TotalBilledRevenue { get; init; }

    public decimal CarrierFreightCost { get; init; }
    public decimal NetFreightMargin { get; init; }
    public decimal FreightMarginPercent { get; init; }
    public decimal DirectLaborEstimatedCost { get; init; }
    public decimal NetCommercialMargin { get; init; }
    public decimal CommercialMarginPercent { get; init; }

    public int InvoicesIssuedCount { get; init; }
    public decimal TotalInvoicedAmount { get; init; }
    public decimal TotalTaxAmount { get; init; }
    public decimal PaymentsCollectedAmount { get; init; }
    public decimal CreditsAppliedAmount { get; init; }
    public decimal OutstandingArBalance { get; init; }
}

public record InventoryUtilizationDto
{
    public Guid FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public DateTime DateUtc { get; init; }
    public TimeGrain TimeGrain { get; init; }

    public int TotalStorageLocations { get; init; }
    public int OccupiedStorageLocations { get; init; }
    public decimal PalletPositionOccupancyPercent { get; init; }

    public decimal TotalCapacityCubicMeters { get; init; }
    public decimal OccupiedCubicMeters { get; init; }
    public decimal VolumetricCubeFillPercent { get; init; }

    public int TotalActiveSkus { get; init; }
    public int TotalInventoryUnits { get; init; }
    public decimal TotalInventoryValue { get; init; }
    public int DeadStockSkusCount { get; init; }
    public int DeadStockUnitsCount { get; init; }
    public decimal DeadStockValue { get; init; }
    public decimal DeadStockRatioPercent { get; init; }
}

public record CarrierPerformanceDto
{
    public string CarrierCode { get; init; } = string.Empty;
    public CarrierServiceLevel CarrierServiceLevel { get; init; }
    public Guid FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public DateTime DateUtc { get; init; }
    public TimeGrain TimeGrain { get; init; }

    public int DispatchedShipmentsCount { get; init; }
    public int DeliveredShipmentsCount { get; init; }
    public int InTransitShipmentsCount { get; init; }
    public int OverdueInTransitCount { get; init; }
    public int CompletedDeliveryOutcomesCount { get; init; }

    public int OnTimeDeliveredCount { get; init; }
    public int LateDeliveredCount { get; init; }
    public int FirstAttemptDeliveredCount { get; init; }
    public int DeliveryExceptionsCount { get; init; }

    public decimal? OnTimeDeliveryRatePercent { get; init; }
    public decimal? FirstAttemptDeliveryRatePercent { get; init; }
    public decimal? DeliveryExceptionRatePercent { get; init; }
    public decimal? AverageTransitDays { get; init; }
}

public record DockYardOperationalStatusDto
{
    public Guid FacilityId { get; init; }
    public DateTime DateUtc { get; init; }
    public TimeGrain TimeGrain { get; init; } = TimeGrain.Daily;

    public int TotalDockDoorsCount { get; init; }
    public int OccupiedDockDoorsCount { get; init; }
    public decimal DockDoorUtilizationPercent { get; init; }

    public int TotalYardSpotsCount { get; init; }
    public int OccupiedYardSpotsCount { get; init; }
    public decimal YardOccupancyPercent { get; init; }

    public int TrucksCheckedInCount { get; init; }
    public int TrucksDepartedCount { get; init; }
    public int TrucksWaitingInYardCount { get; init; }
    public int TrucksAtDockCount { get; init; }

    public decimal AverageYardWaitTimeMinutes { get; init; }
    public decimal AverageDockServiceTimeMinutes { get; init; }
    public decimal AverageTotalTurnTimeMinutes { get; init; }
}

public record OrderSlaItemDto
{
    public Guid OrderId { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public Guid ClientId { get; init; }
    public Guid FacilityId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public CarrierServiceLevel ServiceLevel { get; init; }
    public string? CarrierCode { get; init; }
    public DateTime OrderDateUtc { get; init; }
    public DateTime? RequiredShipDateUtc { get; init; }
    public double RemainingMinutes { get; init; }
    public FulfillmentSlaStatus SlaStatus { get; init; }
    public OutboundOrderStatus CurrentOrderStatus { get; init; }
}

public record OrderFulfillmentSlaDashboardDto
{
    public Guid? FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public DateTime AsOfUtc { get; init; }

    public int OnTrackCount { get; init; }
    public int ApproachingDeadlineCount { get; init; }
    public int AtRiskCount { get; init; }
    public int BreachedCount { get; init; }
    public int CompletedWithinSlaCount { get; init; }
    public int CompletedOutsideSlaCount { get; init; }

    public decimal SlaAdherencePercent { get; init; }
    public IReadOnlyList<OrderSlaItemDto> UrgentOrders { get; init; } = [];
}

public record BottleneckStageHealthDto
{
    public OperationalBottleneckStage Stage { get; init; }
    public BottleneckSeverity Severity { get; init; } = BottleneckSeverity.Normal;
    public int QueueDepth { get; init; }
    public decimal MetricValue { get; init; }
    public string MetricUnit { get; init; } = string.Empty;
    public string SummaryMessage { get; init; } = string.Empty;
    public string RecommendedAction { get; init; } = string.Empty;
}

public record FacilityBottleneckAnalysisDto
{
    public Guid FacilityId { get; init; }
    public DateTime EvaluatedAtUtc { get; init; }
    public BottleneckStageHealthDto ReceivingCongestion { get; init; } = new();
    public BottleneckStageHealthDto PickingBacklog { get; init; } = new();
    public BottleneckStageHealthDto PackingDelays { get; init; } = new();
    public BottleneckStageHealthDto DispatchDelays { get; init; } = new();
    public int OverallHealthScore { get; init; } = 100; // 0 - 100
}

public record FacilityComparisonItemDto
{
    public Guid FacilityId { get; init; }
    public string FacilityCode { get; init; } = string.Empty;
    public string FacilityName { get; init; } = string.Empty;

    public int OrdersReceivedToday { get; init; }
    public int OrdersDispatchedToday { get; init; }
    public int UnitsPickedToday { get; init; }
    public int CartonsPackedToday { get; init; }

    public decimal UnitsPerLaborHour { get; init; }
    public decimal AverageCycleTimeHours { get; init; }
    public decimal SameDayDispatchAdherencePercent { get; init; }

    public decimal DockTurnTimeMinutes { get; init; }
    public decimal DockDoorUtilizationPercent { get; init; }
    public decimal YardOccupancyPercent { get; init; }

    public decimal PalletPositionOccupancyPercent { get; init; }
    public decimal VolumetricCubeFillPercent { get; init; }

    public int ActiveBottlenecksCount { get; init; }
    public int CriticalSlaBreachesCount { get; init; }
}

public record MultiFacilityComparisonDto
{
    public DateTime AsOfUtc { get; init; }
    public IReadOnlyList<FacilityComparisonItemDto> Facilities { get; init; } = [];

    // Network totals & blended benchmarks
    public int TotalOrdersReceivedToday { get; init; }
    public int TotalOrdersDispatchedToday { get; init; }
    public decimal NetworkSameDayDispatchAdherencePercent { get; init; }
    public decimal NetworkBlendedUnitsPerLaborHour { get; init; }
    public decimal NetworkAverageCubeFillPercent { get; init; }
}

public record MetricFreshnessMetadataDto
{
    public DateTime LastProjectionUpdateUtc { get; init; }
    public DataFreshnessStatus FreshnessStatus { get; init; } = DataFreshnessStatus.Live;
    public DateTime ReportingWindowStartUtc { get; init; }
    public DateTime ReportingWindowEndUtc { get; init; }
    public string FacilityTimezone { get; init; } = "UTC";
    public int ProjectionVersion { get; init; } = 1;
}

public record ControlTowerSummaryDto
{
    public Guid? FacilityId { get; init; }
    public DateTime AsOfUtc { get; init; }

    // Real-time Throughput (Today)
    public int OrdersReceivedToday { get; init; }
    public int OrdersDispatchedToday { get; init; }
    public int PackagesDispatchedToday { get; init; }
    public int UnitsPickedToday { get; init; }
    public int CartonsPackedToday { get; init; }
    public int InboundPalletsReceivedToday { get; init; }
    public int PutawayCompletedToday { get; init; }
    public int ReturnsInspectedToday { get; init; }

    // Rates & Velocity
    public decimal UnitsPerLaborHourToday { get; init; }
    public decimal AverageFulfillmentCycleTimeHours { get; init; }
    public decimal SameDayDispatchAdherencePercent { get; init; }

    // Commercial & Carrier
    public decimal TotalBilledRevenueToday { get; init; }
    public decimal NetFreightMarginToday { get; init; }
    public decimal OnTimeDeliveryRatePercent { get; init; }
    public int ActiveDeliveryExceptionsCount { get; init; }

    // Capacity
    public decimal PalletPositionOccupancyPercent { get; init; }
    public decimal VolumetricCubeFillPercent { get; init; }
}

public record ControlTowerExecutiveOverviewDto
{
    public Guid? FacilityId { get; init; }
    public DateTime AsOfUtc { get; init; }

    public ControlTowerSummaryDto Summary { get; init; } = new();
    public OrderFulfillmentSlaDashboardDto SlaDashboard { get; init; } = new();
    public DockYardOperationalStatusDto DockYardStatus { get; init; } = new();
    public FacilityBottleneckAnalysisDto Bottlenecks { get; init; } = new();
    public MetricFreshnessMetadataDto FreshnessMetadata { get; init; } = new();
}

// Phase 6.2 Workforce, Labor & Productivity Models

public record WorkerProductivityDto
{
    public Guid WorkerUserId { get; init; }
    public string WorkerName { get; init; } = string.Empty;
    public Guid FacilityId { get; init; }
    public string Department { get; init; } = string.Empty;
    public DateTime DateUtc { get; init; }
    public TimeGrain TimeGrain { get; init; } = TimeGrain.Daily;

    public decimal TotalShiftMinutes { get; init; }
    public decimal ProductiveWorkMinutes { get; init; }
    public decimal IdleMinutes { get; init; }
    public decimal ScheduledBreakMinutes { get; init; }

    public int TasksCompleted { get; init; }
    public int UnitsCompleted { get; init; }
    public int LinesCompleted { get; init; }
    public int CartonsCompleted { get; init; }
    public int PalletsCompleted { get; init; }

    public decimal ActualLaborHours { get; init; }
    public decimal StandardLaborHours { get; init; }

    public decimal LaborEfficiencyPercent { get; init; }
    public decimal LaborUtilizationPercent { get; init; }
    public decimal UnitsPerHour { get; init; }
    public decimal LinesPerHour { get; init; }
    public decimal CartonsPerHour { get; init; }
    public decimal TasksPerHour { get; init; }
    public LaborPerformanceTier PerformanceTier { get; init; }

    public decimal HourlyWageRate { get; init; }
    public decimal TotalLaborCost { get; init; }
    public decimal LaborCostPerUnit { get; init; }
    public decimal LaborCostPerTask { get; init; }
}

public record WorkerTaskExecutionLogDto
{
    public Guid Id { get; init; }
    public Guid TaskId { get; init; }
    public WarehouseLaborActivityType ActivityType { get; init; }
    public Guid WorkerUserId { get; init; }
    public Guid FacilityId { get; init; }
    public Guid? ZoneId { get; init; }
    public string Department { get; init; } = string.Empty;

    public int UnitsProcessed { get; init; }
    public int LinesProcessed { get; init; }
    public int CartonsProcessed { get; init; }
    public int PalletsProcessed { get; init; }
    public decimal TravelDistanceMeters { get; init; }

    public DateTime StartedAtUtc { get; init; }
    public DateTime CompletedAtUtc { get; init; }

    public decimal ActualDurationSeconds { get; init; }
    public decimal StandardDurationSeconds { get; init; }
    public Guid? StandardProfileId { get; init; }
    public int StandardProfileVersion { get; init; } = 1;
    public decimal VarianceSeconds { get; init; }
    public decimal EfficiencyPercent { get; init; }

    public LaborComplexityFactor Complexity { get; init; }
    public WarehouseEquipmentType Equipment { get; init; }

    public decimal HourlyWageRate { get; init; }
    public decimal CalculatedLaborCost { get; init; }
}

public record WorkerIdleIntervalDto
{
    public Guid Id { get; init; }
    public Guid WorkerUserId { get; init; }
    public Guid FacilityId { get; init; }
    public string Department { get; init; } = string.Empty;

    public DateTime StartedAtUtc { get; init; }
    public DateTime EndedAtUtc { get; init; }

    public decimal DurationMinutes { get; init; }
    public decimal DurationSeconds { get; init; }

    public LaborIdleTimeReason ReasonCode { get; init; }
    public bool SupervisorApproved { get; init; }
    public string? Notes { get; init; }
}

public record LaborStandardProfileDto
{
    public Guid Id { get; init; }
    public Guid? FacilityId { get; init; }
    public Guid? ZoneId { get; init; }
    public WarehouseZoneType? ZoneType { get; init; }
    public WarehouseLaborActivityType ActivityType { get; init; }
    public string Department { get; init; } = string.Empty;

    public decimal BaseSetupSeconds { get; init; }
    public decimal SecondsPerUnit { get; init; }
    public decimal SecondsPerLine { get; init; }
    public decimal SecondsPerCarton { get; init; }
    public decimal SecondsPerPallet { get; init; }
    public decimal SecondsPerTravelMeter { get; init; }

    public decimal EquipmentFactor { get; init; }
    public decimal ComplexityMultiplier { get; init; }
    public int Version { get; init; } = 1;

    public DateTime EffectiveFromUtc { get; init; }
    public DateTime? EffectiveToUtc { get; init; }
    public bool IsActive { get; init; }
}

public record DepartmentWorkforceDto
{
    public string Department { get; init; } = string.Empty;
    public int ActiveWorkersCount { get; init; }
    public decimal ProductiveHours { get; init; }
    public decimal StandardHours { get; init; }
    public decimal EfficiencyPercent { get; init; }
    public decimal UtilizationPercent { get; init; }
    public decimal UnitsPerHour { get; init; }
    public decimal LinesPerHour { get; init; }
    public decimal CartonsPerHour { get; init; }
    public decimal TotalLaborCost { get; init; }
}

public record FacilityWorkforceSummaryDto
{
    public Guid FacilityId { get; init; }
    public DateTime DateUtc { get; init; }
    public int ActiveWorkersCount { get; init; }
    public decimal TotalProductiveHours { get; init; }
    public decimal TotalStandardHours { get; init; }
    public decimal TotalIdleMinutes { get; init; }

    public int TotalTasksCompleted { get; init; }
    public int TotalUnitsProcessed { get; init; }
    public int TotalLinesProcessed { get; init; }
    public int TotalCartonsProcessed { get; init; }

    public decimal BlendedLaborEfficiencyPercent { get; init; }
    public decimal BlendedLaborUtilizationPercent { get; init; }
    public decimal BlendedUnitsPerHour { get; init; }
    public decimal BlendedLinesPerHour { get; init; }
    public decimal BlendedCartonsPerHour { get; init; }

    public decimal TotalLaborCost { get; init; }
    public decimal AverageCostPerUnit { get; init; }

    public IReadOnlyList<DepartmentWorkforceDto> DepartmentBreakdowns { get; init; } = [];
}

public record LaborCostAnalysisDto
{
    public Guid FacilityId { get; init; }
    public DateTime ReportingPeriodStartUtc { get; init; }
    public DateTime ReportingPeriodEndUtc { get; init; }

    public decimal TotalLaborCost { get; init; }
    public decimal DirectProductiveCost { get; init; }
    public decimal IdleTimeCost { get; init; }

    public decimal AverageCostPerPickUnit { get; init; }
    public decimal AverageCostPerPackCarton { get; init; }
    public decimal AverageCostPerPutawayPallet { get; init; }
}

public record InventoryAgingPolicyDto
{
    public Guid Id { get; init; }
    public Guid? FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public string Name { get; init; } = string.Empty;
    public int SlowMovingThresholdDays { get; init; }
    public int DormantThresholdDays { get; init; }
    public int DeadStockThresholdDays { get; init; }
    public int NearExpiryThresholdDays { get; init; }
    public int CriticalExpiryThresholdDays { get; init; }
    public InventoryAgingBasis DefaultBasis { get; init; }
    public string Currency { get; init; } = "USD";
    public decimal DailyHoldingCostPerCbm { get; init; }
    public decimal DailyHoldingCostPerPallet { get; init; }
    public int Version { get; init; }
    public DateTime EffectiveFromUtc { get; init; }
    public DateTime? EffectiveToUtc { get; init; }
    public bool IsActive { get; init; }
}

public record DailyInventoryAgingRecordDto
{
    public Guid Id { get; init; }
    public Guid ClientId { get; init; }
    public Guid FacilityId { get; init; }
    public Guid LocationId { get; init; }
    public string LocationCode { get; init; } = string.Empty;
    public Guid ItemId { get; init; }
    public string Sku { get; init; } = string.Empty;
    public Guid? LotId { get; init; }
    public string? LotNumber { get; init; }
    public string? LpnCode { get; init; }
    public DateTime SnapshotDateUtc { get; init; }

    public InventoryHoldingStatus HoldingStatus { get; init; }
    public decimal QtyAvailable { get; init; }
    public decimal QtyAllocated { get; init; }
    public decimal QtyReserved { get; init; }
    public decimal QtyQuarantined { get; init; }
    public decimal QtyDamaged { get; init; }
    public decimal QtyOnHold { get; init; }

    public int ReceiptAgeDays { get; init; }
    public int LastMovementAgeDays { get; init; }
    public int LastDemandAgeDays { get; init; }
    public bool HasDemandHistory { get; init; }
    public int? DemandInactivityDays { get; init; }
    public int? DaysUntilExpiry { get; init; }
    public int HoldingDurationDays { get; init; }

    public InventoryAgingBucket AgingBucket { get; init; }
    public InventoryMovementVelocityCategory VelocityCategory { get; init; }

    public bool IsAged { get; init; }
    public bool IsSlowMoving { get; init; }
    public bool IsNonMoving { get; init; }
    public bool IsDormant { get; init; }
    public bool IsDeadStock { get; init; }
    public bool IsNearExpiry { get; init; }
    public bool IsCriticalExpiry { get; init; }
    public bool IsExpired { get; init; }

    public decimal QuantityOnHand { get; init; }
    public decimal UnitCost { get; init; }
    public decimal TotalInventoryValue { get; init; }
    public string Currency { get; init; } = "USD";
    public decimal DailyHoldingCost { get; init; }
    public decimal AccruedHoldingCost { get; init; }
    public decimal OccupiedCbm { get; init; }
    public decimal PalletFootprint { get; init; }
    public bool HasMissingDimensionsFallback { get; init; }
    public string SourceInventoryReference { get; init; } = string.Empty;
}

public record LotAgingRecordDto
{
    public Guid? LotId { get; init; }
    public string? LotNumber { get; init; }
    public string? LpnCode { get; init; }
    public Guid ItemId { get; init; }
    public string Sku { get; init; } = string.Empty;
    public string LocationCode { get; init; } = string.Empty;
    public decimal QuantityOnHand { get; init; }
    public InventoryHoldingStatus HoldingStatus { get; init; }
    public decimal QtyAvailable { get; init; }
    public decimal QtyAllocated { get; init; }
    public decimal QtyReserved { get; init; }
    public decimal QtyQuarantined { get; init; }
    public decimal QtyDamaged { get; init; }
    public decimal QtyOnHold { get; init; }
    public int ReceiptAgeDays { get; init; }
    public int LastMovementAgeDays { get; init; }
    public int LastDemandAgeDays { get; init; }
    public int? DaysUntilExpiry { get; init; }
    public int HoldingDurationDays { get; init; }
    public InventoryAgingBucket AgingBucket { get; init; }
    public InventoryMovementVelocityCategory VelocityCategory { get; init; }
    public bool IsAged { get; init; }
    public bool IsSlowMoving { get; init; }
    public bool IsNonMoving { get; init; }
    public bool IsDeadStock { get; init; }
    public bool IsExpired { get; init; }
    public bool IsNearExpiry { get; init; }
    public decimal UnitCost { get; init; }
    public decimal TotalInventoryValue { get; init; }
    public string Currency { get; init; } = "USD";
    public decimal AccruedHoldingCost { get; init; }
}

public record InventoryAgingBucketSummaryDto
{
    public InventoryAgingBucket Bucket { get; init; }
    public string BucketName { get; init; } = string.Empty;
    public int DaysMin { get; init; }
    public int? DaysMax { get; init; }
    public int SkuCount { get; init; }
    public decimal TotalUnits { get; init; }
    public decimal TotalValue { get; init; }
    public decimal TotalOccupiedCbm { get; init; }
    public decimal PercentageOfTotalValue { get; init; }
}

public record InventoryHoldingStatusBreakdownDto
{
    public InventoryHoldingStatus HoldingStatus { get; init; }
    public decimal TotalUnits { get; init; }
    public decimal TotalValue { get; init; }
    public decimal TotalOccupiedCbm { get; init; }
    public decimal PercentageOfTotalUnits { get; init; }
}

public record DeadStockItemDto
{
    public Guid ItemId { get; init; }
    public string Sku { get; init; } = string.Empty;
    public Guid ClientId { get; init; }
    public Guid FacilityId { get; init; }
    public decimal QuantityOnHand { get; init; }
    public decimal TotalValue { get; init; }
    public int ReceiptAgeDays { get; init; }
    public int LastMovementAgeDays { get; init; }
    public int LastDemandAgeDays { get; init; }
    public bool HasDemandHistory { get; init; }
    public int? DemandInactivityDays { get; init; }
    public decimal OccupiedCbm { get; init; }
    public decimal AccruedHoldingCost { get; init; }
    public string Currency { get; init; } = "USD";
    public int LotCount { get; init; }
    public string PrimaryLocationCode { get; init; } = string.Empty;
}

public record DeadStockAnalysisDto
{
    public Guid FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public DateTime AsOfDateUtc { get; init; }
    public int TotalDeadStockSkus { get; init; }
    public decimal TotalDeadStockUnits { get; init; }
    public decimal TotalDeadStockValue { get; init; }
    public decimal TotalDeadStockCbm { get; init; }
    public decimal TotalAccruedHoldingCost { get; init; }
    public string Currency { get; init; } = "USD";
    public decimal DeadStockPercentageOfInventoryValue { get; init; }
    public IReadOnlyList<DeadStockItemDto> Items { get; init; } = [];
}

public record InventoryHealthSummaryDto
{
    public Guid FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public DateTime AsOfDateUtc { get; init; }

    public int TotalActiveSkus { get; init; }
    public decimal TotalUnitsOnHand { get; init; }
    public decimal TotalInventoryValue { get; init; }
    public decimal TotalOccupiedCbm { get; init; }
    public decimal TotalPalletFootprint { get; init; }

    // Aging & Dead Stock KPI Segregation
    public decimal DeadStockValue { get; init; }
    public decimal DeadStockRatioPercent { get; init; }
    public int DeadStockSkuCount { get; init; }
    public decimal DeadStockUnitsCount { get; init; }

    public decimal SlowMovingValue { get; init; }
    public int SlowMovingSkuCount { get; init; }

    public decimal ExpiredStockValue { get; init; }
    public int ExpiredLotCount { get; init; }
    public decimal NearExpiryStockValue { get; init; }
    public int NearExpiryLotCount { get; init; }

    // Bucket Breakdown & Holding Status Breakdown
    public IReadOnlyList<InventoryAgingBucketSummaryDto> AgingBuckets { get; init; } = [];
    public IReadOnlyList<InventoryHoldingStatusBreakdownDto> HoldingStatusBreakdown { get; init; } = [];
}

public record LocationSpaceUtilizationDto
{
    public Guid LocationId { get; init; }
    public Guid FacilityId { get; init; }
    public Guid? ZoneId { get; init; }
    public string LocationCode { get; init; } = string.Empty;
    public LocationType LocationType { get; init; }
    public DateTime SnapshotDateUtc { get; init; }

    public string? AisleCode { get; init; }
    public string? RackCode { get; init; }
    public int? ShelfLevel { get; init; }
    public int? BinNumber { get; init; }
    public decimal? XCoord { get; init; }
    public decimal? YCoord { get; init; }
    public decimal? ZCoord { get; init; }

    // Volumetric Fill & Clearances
    public decimal PhysicalCubicMeters { get; init; }
    public decimal UsableCapacityFactor { get; init; }
    public decimal UsableCubicMeters { get; init; }
    public decimal OccupiedCubicMeters { get; init; }
    public decimal AvailableCubicMeters { get; init; }
    public decimal CubeUtilizationPercent { get; init; }

    // Pallet Position Occupancy
    public int PhysicalPalletPositions { get; init; }
    public int UsablePalletPositions { get; init; }
    public int OccupiedPalletPositions { get; init; }
    public int AvailablePalletPositions { get; init; }
    public decimal PalletOccupancyPercent { get; init; }

    // Operational Availability & Blocking
    public bool IsBlocked { get; init; }
    public string? BlockReason { get; init; }

    // Weight Capacity
    public decimal MaxWeightKg { get; init; }
    public decimal OccupiedWeightKg { get; init; }
    public decimal WeightUtilizationPercent { get; init; }
    public bool IsWeightConstrained { get; init; }

    // Over-Capacity Flags
    public bool IsOverCapacity { get; init; }
    public decimal OverCapacityExcessCbm { get; init; }

    public int DistinctSkuCount { get; init; }
    public decimal TotalUnitsStored { get; init; }
    public bool IsMixedSkuLocation { get; init; }
    public bool HasMissingDimensionsFallback { get; init; }

    // Status Partitioned Quantities
    public decimal UnitsAvailable { get; init; }
    public decimal UnitsAllocated { get; init; }
    public decimal UnitsReserved { get; init; }
    public decimal UnitsQuarantined { get; init; }
    public decimal UnitsDamaged { get; init; }
    public decimal UnitsOnHold { get; init; }

    // Status Partitioned Occupied Volume (CBM)
    public decimal OccupiedCbmAvailable { get; init; }
    public decimal OccupiedCbmAllocated { get; init; }
    public decimal OccupiedCbmReserved { get; init; }
    public decimal OccupiedCbmQuarantined { get; init; }
    public decimal OccupiedCbmDamaged { get; init; }
    public decimal OccupiedCbmOnHold { get; init; }

    public LocationSpaceUtilizationClassification SpaceClassification { get; init; }
}

public record AisleSpaceUtilizationDto
{
    public string AisleCode { get; init; } = string.Empty;
    public int LocationsCount { get; init; }
    public int RacksCount { get; init; }
    public decimal UsableCbm { get; init; }
    public decimal OccupiedCbm { get; init; }
    public decimal AvailableCbm { get; init; }
    public decimal CubeUtilizationPercent { get; init; }
    public int UsablePalletPositions { get; init; }
    public int OccupiedPalletPositions { get; init; }
    public decimal PalletOccupancyPercent { get; init; }
    public int OverCapacityLocationsCount { get; init; }
}

public record ZoneSpaceUtilizationDto
{
    public Guid? ZoneId { get; init; }
    public string ZoneCode { get; init; } = string.Empty;
    public string ZoneName { get; init; } = string.Empty;
    public int TotalLocationsCount { get; init; }
    public int ActiveLocationsCount { get; init; }
    public int BlockedLocationsCount { get; init; }
    public decimal UsableCbm { get; init; }
    public decimal OccupiedCbm { get; init; }
    public decimal AvailableCbm { get; init; }
    public decimal CubeUtilizationPercent { get; init; }
    public int UsablePalletPositions { get; init; }
    public int OccupiedPalletPositions { get; init; }
    public int AvailablePalletPositions { get; init; }
    public decimal PalletOccupancyPercent { get; init; }
    public int OverCapacityLocationsCount { get; init; }
    public int UnderutilizedLocationsCount { get; init; }
    public LocationSpaceUtilizationClassification SpaceClassification { get; init; }
    public IReadOnlyList<AisleSpaceUtilizationDto> AisleBreakdowns { get; init; } = [];
}

public record OverCapacityAlertDto
{
    public Guid LocationId { get; init; }
    public string LocationCode { get; init; } = string.Empty;
    public Guid? ZoneId { get; init; }
    public LocationType LocationType { get; init; }
    public decimal CubeUtilizationPercent { get; init; }
    public decimal PalletOccupancyPercent { get; init; }
    public decimal WeightUtilizationPercent { get; init; }
    public decimal UsableCbm { get; init; }
    public decimal OccupiedCbm { get; init; }
    public decimal ExcessCbm { get; init; }
    public bool IsWeightConstrained { get; init; }
    public int DistinctSkuCount { get; init; }
}

public record FacilitySpaceUtilizationSummaryDto
{
    public Guid FacilityId { get; init; }
    public DateTime AsOfDateUtc { get; init; }

    // Location Counts
    public int TotalLocationsCount { get; init; }
    public int ActiveLocationsCount { get; init; }
    public int BlockedLocationsCount { get; init; }
    public int EmptyLocationsCount { get; init; }
    public int UnderutilizedLocationsCount { get; init; }
    public int OptimalLocationsCount { get; init; }
    public int NearCapacityLocationsCount { get; init; }
    public int OverCapacityLocationsCount { get; init; }
    public int MixedSkuLocationsCount { get; init; }

    // Volumetric Aggregations
    public decimal TotalPhysicalCbm { get; init; }
    public decimal TotalUsableCbm { get; init; }
    public decimal TotalOccupiedCbm { get; init; }
    public decimal TotalAvailableCbm { get; init; }
    public decimal CubeFillPercent { get; init; }
    public decimal TotalOverCapacityExcessCbm { get; init; }

    // Pallet Position Aggregations
    public int TotalPhysicalPalletPositions { get; init; }
    public int TotalUsablePalletPositions { get; init; }
    public int TotalOccupiedPalletPositions { get; init; }
    public int TotalAvailablePalletPositions { get; init; }
    public decimal PalletOccupancyPercent { get; init; }

    // Weight Aggregations
    public decimal TotalMaxWeightKg { get; init; }
    public decimal TotalOccupiedWeightKg { get; init; }
    public decimal WeightUtilizationPercent { get; init; }
    public int WeightConstrainedLocationsCount { get; init; }

    // Status Partitioned Occupied Volume (CBM)
    public decimal OccupiedCbmAvailable { get; init; }
    public decimal OccupiedCbmAllocated { get; init; }
    public decimal OccupiedCbmReserved { get; init; }
    public decimal OccupiedCbmQuarantined { get; init; }
    public decimal OccupiedCbmDamaged { get; init; }
    public decimal OccupiedCbmOnHold { get; init; }

    public int MissingDimensionsCount { get; init; }
    public IReadOnlyList<ZoneSpaceUtilizationDto> ZoneBreakdowns { get; init; } = [];
}

public record SkuVelocityClassificationDto
{
    public Guid Id { get; init; }
    public Guid ClientId { get; init; }
    public Guid FacilityId { get; init; }
    public Guid ItemId { get; init; }
    public string Sku { get; init; } = string.Empty;

    public AbcClassificationCategory AbcClass { get; init; }
    public AbcClassificationCriteria CriteriaUsed { get; init; }
    public InventoryMovementVelocityCategory VelocityCategory { get; init; }
    public VelocityObservationWindow ObservationWindow { get; init; }

    public DateTime PeriodStartUtc { get; init; }
    public DateTime PeriodEndUtc { get; init; }
    public int PeriodDurationDays { get; init; }

    // Demand Metrics
    public int TotalPickTasksCount { get; init; }
    public decimal TotalUnitsDemanded { get; init; }
    public decimal TotalDemandValue { get; init; }
    public decimal PeriodDemandValue { get; init; }
    public decimal AnnualizedConsumptionValue { get; init; }
    public SkuClassificationRecordType RecordType { get; init; }
    public Guid? ClassificationRunId { get; init; }
    public int OrderLinesCount { get; init; }
    public int TotalDemandEventsCount { get; init; }

    // Physical Movement & Handling Metrics
    public int TotalMovementEventsCount { get; init; }
    public decimal TotalUnitsMoved { get; init; }
    public int TotalReplenishmentCount { get; init; }
    public int TotalRelocationCount { get; init; }

    // Inventory Turnover & Valuation
    public decimal CostOfGoodsConsumed { get; init; }
    public decimal AverageInventoryValue { get; init; }
    public decimal InventoryTurnoverRatio { get; init; }
    public decimal DaysOfSupply { get; init; }
    public decimal DaysOfSupplyFinancial { get; init; }
    public decimal DaysOfSupplyOperational { get; init; }
    public decimal CurrentOnHandUnits { get; init; }

    // Anomaly & Pattern Flags
    public bool IsDemandSpike { get; init; }
    public bool IsSeasonalPattern { get; init; }

    // Frequency Metrics
    public decimal PickFrequencyPerDay { get; init; }
    public decimal ReplenishmentFrequencyPerDay { get; init; }
    public decimal RelocationFrequencyPerDay { get; init; }

    public decimal CumulativePercentage { get; init; }
    public int RankInPortfolio { get; init; }
    public Guid? PolicyId { get; init; }
    public decimal CompositeScore { get; init; }
    public decimal DailyDemandVelocity { get; init; }

    // Spatial Dimensions
    public Guid? LocationId { get; init; }
    public Guid? ZoneId { get; init; }
    public string? Aisle { get; init; }

    public int Version { get; init; }
    public DateTime CalculatedAtUtc { get; init; }
}

public record SkuVelocityMultiWindowComparisonDto
{
    public Guid ItemId { get; init; }
    public string Sku { get; init; } = string.Empty;
    public Guid FacilityId { get; init; }
    public Guid ClientId { get; init; }

    public decimal DemandVelocity30d { get; init; }
    public decimal DemandVelocity90d { get; init; }
    public decimal DemandVelocity180d { get; init; }
    public decimal DemandVelocity365d { get; init; }

    public decimal TurnoverRatio30d { get; init; }
    public decimal TurnoverRatio90d { get; init; }
    public decimal TurnoverRatio180d { get; init; }
    public decimal TurnoverRatio365d { get; init; }

    public decimal PickFrequency30d { get; init; }
    public decimal PickFrequency90d { get; init; }
    public decimal MovementFrequency30d { get; init; }
    public decimal MovementFrequency90d { get; init; }

    public InventoryMovementVelocityCategory VelocityCategory90d { get; init; }
    public AbcClassificationCategory AbcClass90d { get; init; }

    /// <summary>
    /// Trend indicator: "DemandSpike", "SeasonalSpike", "Surging", "Stable", "Declining", "Inactive".
    /// </summary>
    public string DemandTrend { get; init; } = "Stable";
    public bool SeasonalSpikeDetected { get; init; }
    public bool DemandSpikeDetected { get; init; }
    public bool SeasonalPatternDetected { get; init; }
    public decimal DaysOfSupply { get; init; }
    public decimal DaysOfSupplyFinancial { get; init; }
    public decimal DaysOfSupplyOperational { get; init; }
    public decimal CurrentOnHandUnits { get; init; }
}

public record SkuTurnoverLeaderDto
{
    public Guid ItemId { get; init; }
    public string Sku { get; init; } = string.Empty;
    public decimal CogsConsumed { get; init; }
    public decimal AverageInventoryValue { get; init; }
    public decimal TurnoverRatio { get; init; }
    public decimal DaysOfSupply { get; init; }
    public decimal DailyDemandVelocity { get; init; }
    public AbcClassificationCategory AbcClass { get; init; }
}

public record InventoryTurnoverAnalysisDto
{
    public Guid FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public VelocityObservationWindow ObservationWindow { get; init; }
    public DateTime PeriodStartUtc { get; init; }
    public DateTime PeriodEndUtc { get; init; }

    public decimal TotalCogsConsumed { get; init; }
    public decimal TotalAverageInventoryValuation { get; init; }
    public decimal OverallAnnualizedTurnoverRatio { get; init; }
    public decimal AverageDaysOfSupply { get; init; }
    public decimal ZeroTurnoverInventoryValue { get; init; }
    public decimal DeadStockValue { get; init; }

    public IReadOnlyList<SkuTurnoverLeaderDto> FastestTurningSkus { get; init; } = [];
    public IReadOnlyList<SkuTurnoverLeaderDto> SlowestTurningSkus { get; init; } = [];
}

public record ZoneVelocityProfileDto
{
    public Guid ZoneId { get; init; }
    public string ZoneCode { get; init; } = string.Empty;
    public WarehouseZoneType ZoneType { get; init; }
    public int TotalLocations { get; init; }
    public int OccupiedLocations { get; init; }

    public decimal ClassAPercent { get; init; }
    public decimal ClassBPercent { get; init; }
    public decimal ClassCPercent { get; init; }

    public int FastMovingSkuCount { get; init; }
    public int SlowMovingSkuCount { get; init; }
    public int DeadStockSkuCount { get; init; }

    public decimal DailyPickTasksTotal { get; init; }
    public int MisallocatedFastMoverCount { get; init; }
    public int MisallocatedSlowMoverCount { get; init; }
}

public record SkuVelocitySummaryDto
{
    public Guid FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public VelocityObservationWindow ObservationWindow { get; init; }
    public DateTime AsOfDateUtc { get; init; }

    public int TotalTrackedSkus { get; init; }
    public int FastMovingCount { get; init; }
    public int MediumMovingCount { get; init; }
    public int SlowMovingCount { get; init; }
    public int NonMovingCount { get; init; }
    public int DeadStockCount { get; init; }

    public int ClassACount { get; init; }
    public int ClassBCount { get; init; }
    public int ClassCCount { get; init; }

    public decimal TotalDailyDemandUnits { get; init; }
    public decimal TotalDailyPickTasks { get; init; }
    public int TotalMovementEventsInPeriod { get; init; }
    public decimal OverallTurnoverRatio { get; init; }
}

public record ProcessInventoryMovementEventRequest
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public long SequenceNumber { get; init; }
    public Guid FacilityId { get; init; }
    public Guid ClientId { get; init; }
    public Guid ItemId { get; init; }
    public string Sku { get; init; } = string.Empty;
    public InventoryMovementEventType EventType { get; init; }
    public decimal Quantity { get; init; }
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public Guid? SourceLocationId { get; init; }
    public Guid? DestinationLocationId { get; init; }
    public Guid? ZoneId { get; init; }
    public decimal UnitCost { get; init; }
    public string ReferenceNumber { get; init; } = string.Empty;
    public bool? IsCanonicalDemand { get; init; }
}

public record UpsertInventoryAgingPolicyRequest
{
    public Guid? Id { get; init; }
    public Guid? FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public string Name { get; init; } = "Standard Aging Policy";
    public int SlowMovingThresholdDays { get; init; } = 90;
    public int DormantThresholdDays { get; init; } = 180;
    public int DeadStockThresholdDays { get; init; } = 365;
    public int NearExpiryThresholdDays { get; init; } = 60;
    public int CriticalExpiryThresholdDays { get; init; } = 30;
    public InventoryAgingBasis DefaultBasis { get; init; } = InventoryAgingBasis.ReceiptDate;
    public string Currency { get; init; } = "USD";
    public decimal DailyHoldingCostPerCbm { get; init; } = 0.05m;
    public decimal DailyHoldingCostPerPallet { get; init; } = 0.50m;
    public int Version { get; init; } = 1;
    public DateTime? EffectiveFromUtc { get; init; }
    public DateTime? EffectiveToUtc { get; init; }
    public bool IsActive { get; init; } = true;
}

// ==========================================
// Phase 6.3.5 ABC Inventory Classification Engine
// ==========================================

public record AbcClassificationPolicyDto
{
    public Guid Id { get; init; }
    public Guid? FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public AbcClassificationCriteria Criteria { get; init; }
    public decimal ClassACumulativeThresholdPercent { get; init; }
    public decimal ClassBCumulativeThresholdPercent { get; init; }
    public int ObservationWindowDays { get; init; }
    public VelocityObservationWindow ObservationWindow { get; init; }
    public InventoryValuationBasis CostValuationBasis { get; init; }
    public decimal WeightAcv { get; init; }
    public decimal WeightPickFrequency { get; init; }
    public decimal WeightUnitVelocity { get; init; }
    public decimal WeightOrderLines { get; init; }
    public int Version { get; init; }
    public DateTime EffectiveFromUtc { get; init; }
    public DateTime? EffectiveToUtc { get; init; }
    public bool IsActive { get; init; }
}

public record UpsertAbcClassificationPolicyRequest
{
    public Guid? Id { get; init; }
    public Guid? FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public string Name { get; init; } = "Standard ABC Classification Policy";
    public string? Description { get; init; }
    public AbcClassificationCriteria Criteria { get; init; } = AbcClassificationCriteria.AnnualConsumptionValue;
    public decimal ClassACumulativeThresholdPercent { get; init; } = 80.00m;
    public decimal ClassBCumulativeThresholdPercent { get; init; } = 95.00m;
    public int ObservationWindowDays { get; init; } = 365;
    public VelocityObservationWindow ObservationWindow { get; init; } = VelocityObservationWindow.Days365;
    public InventoryValuationBasis CostValuationBasis { get; init; } = InventoryValuationBasis.UnitCost;
    public decimal WeightAcv { get; init; } = 0.40m;
    public decimal WeightPickFrequency { get; init; } = 0.30m;
    public decimal WeightUnitVelocity { get; init; } = 0.20m;
    public decimal WeightOrderLines { get; init; } = 0.10m;
    public int Version { get; init; } = 1;
    public DateTime? EffectiveFromUtc { get; init; }
    public DateTime? EffectiveToUtc { get; init; }
    public bool IsActive { get; init; } = true;
}

public record ExecuteAbcClassificationBatchRequest
{
    public Guid FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public Guid? PolicyId { get; init; }
    public AbcClassificationCriteria? CriteriaOverride { get; init; }
    public VelocityObservationWindow? WindowOverride { get; init; }
    public DateTime? AsOfDateUtc { get; init; }
}

public record AbcRankedSkuDto
{
    public Guid ItemId { get; init; }
    public string Sku { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int Rank { get; init; }
    public AbcClassificationCategory AbcClass { get; init; }
    public decimal AnnualConsumptionValue { get; init; }
    public decimal PeriodDemandValue { get; init; }
    public decimal AnnualizedConsumptionValue { get; init; }
    public decimal TotalUnitsDemanded { get; init; }
    public int TotalPickTasksCount { get; init; }
    public int OrderLinesCount { get; init; }
    public decimal CompositeScore { get; init; }
    public decimal PrimaryMetricValue { get; init; }
    public decimal CumulativeContributionPercent { get; init; }
    public decimal InventoryTurnoverRatio { get; init; }
    public decimal DaysOfSupply { get; init; }
    public decimal CurrentOnHandUnits { get; init; }
    public decimal UnitCost { get; init; }
}

public record AbcClassificationSummaryDto
{
    public Guid FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public Guid? ClassificationRunId { get; init; }
    public Guid? PolicyUsedId { get; init; }
    public string PolicyName { get; init; } = string.Empty;
    public AbcClassificationCriteria CriteriaUsed { get; init; }
    public InventoryValuationBasis CostValuationBasis { get; init; }
    public decimal ClassAThresholdPercent { get; init; }
    public decimal ClassBThresholdPercent { get; init; }
    public int ObservationWindowDays { get; init; }
    public int TotalSkus { get; init; }
    public int ClassACount { get; init; }
    public int ClassBCount { get; init; }
    public int ClassCCount { get; init; }
    public decimal ClassAPercentOfSkus { get; init; }
    public decimal ClassBPercentOfSkus { get; init; }
    public decimal ClassCPercentOfSkus { get; init; }
    public decimal ClassAValue { get; init; }
    public decimal ClassBValue { get; init; }
    public decimal ClassCValue { get; init; }
    public decimal TotalPortfolioValue { get; init; }
    public decimal PeriodPortfolioValue { get; init; }
    public decimal AnnualizedPortfolioValue { get; init; }
    public decimal ClassAValuePercent { get; init; }
    public decimal ClassBValuePercent { get; init; }
    public decimal ClassCValuePercent { get; init; }
    public bool HasPositiveConsumptionValue { get; init; } = true;
    public string PortfolioConsumptionStatus { get; init; } = "PositiveConsumption";
    public DateTime CalculatedAtUtc { get; init; }
    public int Version { get; init; }
}

public record AbcClassificationBatchResultDto
{
    public AbcClassificationSummaryDto Summary { get; init; } = null!;
    public IReadOnlyList<AbcRankedSkuDto> RankedSkus { get; init; } = [];
}

public record AbcSkuMigrationItemDto
{
    public Guid ItemId { get; init; }
    public string Sku { get; init; } = string.Empty;
    public AbcClassificationCategory BaseClass { get; init; }
    public AbcClassificationCategory TargetClass { get; init; }
    public int BaseRank { get; init; }
    public int TargetRank { get; init; }
    public int RankDelta { get; init; }
    public decimal BaseCumulativePercent { get; init; }
    public decimal TargetCumulativePercent { get; init; }
    public string MigrationType { get; init; } = "Unchanged"; // "Promoted", "Demoted", "Unchanged"
    public string ChangeReason { get; init; } = string.Empty;
}

public record AbcMigrationAnalysisDto
{
    public Guid FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public int BaseVersion { get; init; }
    public int TargetVersion { get; init; }
    public int TotalEvaluatedSkus { get; init; }
    public int PromotionsCount { get; init; }
    public int DemotionsCount { get; init; }
    public int UnchangedCount { get; init; }
    public IReadOnlyList<AbcSkuMigrationItemDto> Migrations { get; init; } = [];
}

// Phase 6.3.6 Unified Inventory Health Dashboard Models

public record UnifiedAgingExpirySummaryDto
{
    public decimal TotalInventoryValue { get; init; }
    public decimal ActiveStockValue { get; init; }
    public decimal AgedStockValue { get; init; }
    public decimal SlowMovingStockValue { get; init; }
    public decimal NonMovingStockValue { get; init; }
    public decimal DeadStockValue { get; init; }
    public decimal ExpiredStockValue { get; init; }
    public decimal NearExpiryStockValue { get; init; }
    public decimal DeadStockRatioPercent { get; init; }
    public decimal EstimatedAnnualHoldingCostExposure { get; init; }
    public decimal TotalAgedUnits { get; init; }
    public decimal TotalDeadStockUnits { get; init; }
    public int DeadStockSkuCount { get; init; }
    public int ExpiredLotCount { get; init; }
    public int NearExpiryLotCount { get; init; }
}

public record UnifiedSpaceCapacitySummaryDto
{
    public decimal TotalPhysicalCbm { get; init; }
    public decimal TotalUsableCbm { get; init; }
    public decimal TotalOccupiedCbm { get; init; }
    public decimal AvailableCbm { get; init; }
    public decimal CubeUtilizationPercent { get; init; }
    public int TotalPalletPositions { get; init; }
    public int OccupiedPalletPositions { get; init; }
    public int AvailablePalletPositions { get; init; }
    public decimal PalletOccupancyPercent { get; init; }
    public int OverCapacityLocationsCount { get; init; }
    public int BlockedLocationsCount { get; init; }
    public int WeightConstrainedLocationsCount { get; init; }
    public int TotalLocationsEvaluated { get; init; }
}

public record UnifiedVelocitySummaryDto
{
    public VelocityObservationWindow ObservationWindow { get; init; }
    public decimal TotalUnitsDemanded { get; init; }
    public int TotalPickTasksCount { get; init; }
    public int FastMovingSkusCount { get; init; }
    public int MediumMovingSkusCount { get; init; }
    public int SlowMovingSkusCount { get; init; }
    public int NonMovingSkusCount { get; init; }
    public int DeadStockSkusCount { get; init; }
    public decimal FastMovingSkuRatioPercent { get; init; }
    public decimal PortfolioTurnoverRatio { get; init; }
    public decimal AverageDaysOfSupply { get; init; }
}

public record UnifiedAbcSummaryDto
{
    public AbcClassificationCriteria CriteriaUsed { get; init; }
    public int TotalEvaluatedSkus { get; init; }
    public int ClassACount { get; init; }
    public int ClassBCount { get; init; }
    public int ClassCCount { get; init; }
    public decimal ClassAPercentOfSkus { get; init; }
    public decimal ClassAValuePercent { get; init; }
    public decimal ClassBValuePercent { get; init; }
    public decimal ClassCValuePercent { get; init; }
    public decimal TotalPortfolioAnnualizedValue { get; init; }
    public IReadOnlyList<AbcRankedSkuDto> TopClassASkus { get; init; } = [];
    public int Version { get; init; }
    public Guid? ClassificationRunId { get; init; }
}

public record UnifiedStockRiskSummaryDto
{
    public int TotalAlertsCount { get; init; }
    public int CriticalAlertsCount { get; init; }
    public int HighAlertsCount { get; init; }
    public int MediumAlertsCount { get; init; }
    public int LowAlertsCount { get; init; }
    public int StockoutRiskSkusCount { get; init; }
    public int OverstockRiskSkusCount { get; init; }
    public int DeadStockRiskSkusCount { get; init; }
    public int ExpiryRiskLotsCount { get; init; }
}

public record HealthScoreBreakdownDto
{
    public decimal BaseScore { get; init; } = 100.0m;
    public decimal DeadStockPenalty { get; init; }
    public decimal ExpiredStockPenalty { get; init; }
    public decimal OverCapacityPenalty { get; init; }
    public decimal SpaceCongestionPenalty { get; init; }
    public decimal StockoutRiskPenalty { get; init; }
    public decimal TotalDeductions { get; init; }
    public decimal FinalScore { get; init; }
    public string ScoreStatus { get; init; } = "Scored"; // "Scored", "NoInventory", "InsufficientData"
    public string Explanation { get; init; } = string.Empty;
}

public record InventoryHealthFreshnessMetadataDto
{
    public DateTime? AgingSnapshotDateUtc { get; init; }
    public double? AgingAgeHours { get; init; }
    public bool IsAgingStale { get; init; }

    public DateTime? SpaceSnapshotDateUtc { get; init; }
    public double? SpaceAgeHours { get; init; }
    public bool IsSpaceStale { get; init; }

    public DateTime? VelocityCalculatedAtUtc { get; init; }
    public double? VelocityAgeHours { get; init; }
    public bool IsVelocityStale { get; init; }

    public DateTime? AbcCalculatedAtUtc { get; init; }
    public double? AbcAgeHours { get; init; }
    public int? AbcVersion { get; init; }
    public bool IsAbcStale { get; init; }

    public bool HasClockSkewDetected { get; init; }
    public bool IsStale { get; init; }
    public string? StalenessReason { get; init; }
}

public record UnifiedInventoryHealthDashboardDto
{
    public Guid FacilityId { get; init; }
    public string FacilityCode { get; init; } = string.Empty;
    public string FacilityName { get; init; } = string.Empty;
    public Guid? ClientId { get; init; }
    public string? ClientCode { get; init; }
    public string? ClientName { get; init; }
    public DateTime GeneratedAtUtc { get; init; }

    // Executive Rollup KPIs
    public decimal TotalInventoryValuation { get; init; }
    public decimal TotalUnitsOnHand { get; init; }
    public int TotalActiveSkusCount { get; init; }
    public decimal SpaceUtilizationPercent { get; init; }
    public decimal PalletOccupancyPercent { get; init; }
    public decimal DeadStockValue { get; init; }
    public decimal DeadStockRatioPercent { get; init; }
    public decimal ExpiredStockValue { get; init; }
    public decimal NearExpiryStockValue { get; init; }
    public int FastMovingSkuCount { get; init; }
    public decimal FastMovingSkuRatioPercent { get; init; }
    public int ClassASkuCount { get; init; }
    public decimal ClassAValuePercent { get; init; }

    // Composite Health Scoring
    public decimal OverallHealthScore { get; init; }
    public string HealthScoreGrade { get; init; } = "A (Pristine)";
    public string ScoreStatus { get; init; } = "Scored";
    public HealthScoreBreakdownDto HealthScoreBreakdown { get; init; } = new();

    // Subsystem Domain Summaries
    public UnifiedAgingExpirySummaryDto AgingAndExpiry { get; init; } = null!;
    public UnifiedSpaceCapacitySummaryDto SpaceCapacity { get; init; } = null!;
    public UnifiedVelocitySummaryDto VelocityAndMovement { get; init; } = null!;
    public UnifiedAbcSummaryDto AbcPareto { get; init; } = null!;
    public UnifiedStockRiskSummaryDto StockRiskAlerts { get; init; } = null!;

    // Freshness & Audit
    public InventoryHealthFreshnessMetadataDto Freshness { get; init; } = null!;
}

#region Phase 6.3.6.2 Dynamic Warehouse Heatmap Domain Models

public record WarehouseCoordinateDto
{
    public decimal? X { get; init; }
    public decimal? Y { get; init; }
    public decimal? Z { get; init; }
    public decimal? WidthCm { get; init; }
    public decimal? LengthCm { get; init; }
    public decimal? HeightCm { get; init; }
    public decimal OrientationDegrees { get; init; } = 0m;
    public bool HasCoordinates => X.HasValue && Y.HasValue && Z.HasValue;
    public HeatmapCoordinateStatus Status { get; init; } = HeatmapCoordinateStatus.Valid;
}

public record WarehouseBoundingBoxDto
{
    public decimal MinX { get; init; }
    public decimal MaxX { get; init; }
    public decimal MinY { get; init; }
    public decimal MaxY { get; init; }
    public decimal MinZ { get; init; }
    public decimal MaxZ { get; init; }
    public decimal Width => MaxX >= MinX ? MaxX - MinX : 0m;
    public decimal Length => MaxY >= MinY ? MaxY - MinY : 0m;
    public decimal Height => MaxZ >= MinZ ? MaxZ - MinZ : 0m;
}

public record WarehouseZoomPanBoundsDto
{
    public decimal MinZoom { get; init; } = 0.1m;
    public decimal MaxZoom { get; init; } = 20.0m;
    public decimal DefaultZoom { get; init; } = 1.0m;
    public decimal PanPaddingMeters { get; init; } = 5.0m;
    public WarehouseBoundingBoxDto BoundingBox { get; init; } = new();
    public decimal AspectRatio => BoundingBox.Width > 0 && BoundingBox.Length > 0
        ? Math.Round(BoundingBox.Width / BoundingBox.Length, 2)
        : 1.0m;
}

public record WarehouseTopologyNodeDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public LocationType Type { get; init; }
    public Guid? ParentId { get; init; }
    public int DepthLevel { get; init; }
    public IReadOnlyList<WarehouseTopologyNodeDto> Children { get; init; } = [];
}

public record HeatmapColorScaleStopDto
{
    public decimal MinValue { get; init; }
    public decimal? MaxValue { get; init; }
    public string ColorHex { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public HeatmapVisualTier Tier { get; init; }
}

public record HeatmapColorScaleDefinitionDto
{
    public HeatmapLayerType LayerType { get; init; }
    public string LayerName { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public bool IsUncapped { get; init; }
    public IReadOnlyList<HeatmapColorScaleStopDto> Stops { get; init; } = [];
}

public record HeatmapLocationCellDto
{
    public Guid LocationId { get; init; }
    public string LocationCode { get; init; } = string.Empty;
    public LocationType LocationType { get; init; }
    public Guid? ZoneId { get; init; }
    public string? ZoneCode { get; init; }
    public string? AisleCode { get; init; }
    public string? RackCode { get; init; }
    public int? ShelfLevel { get; init; }
    public int? BinNumber { get; init; }

    public WarehouseCoordinateDto Coordinate { get; init; } = new();

    // Metric Values (Uncapped, Independent)
    public decimal CubeUtilizationPercent { get; init; }
    public decimal UsableCbm { get; init; }
    public decimal OccupiedCbm { get; init; }

    public decimal PalletOccupancyPercent { get; init; }
    public int UsablePallets { get; init; }
    public int OccupiedPallets { get; init; }

    public InventoryMovementVelocityCategory VelocityCategory { get; init; } = InventoryMovementVelocityCategory.FastMoving;
    public InventoryAgingBucket AgingBucket { get; init; } = InventoryAgingBucket.Current_0_30;

    public bool IsOverCapacity { get; init; }
    public decimal OverCapacityExcessCbm { get; init; }
    public bool IsBlocked { get; init; }
    public string? BlockReason { get; init; }
    public bool IsDeadStock { get; init; }
    public bool IsExpired { get; init; }
    public bool IsNearExpiry { get; init; }

    // Colors Per Layer (Documented Deterministic Scales)
    public string CubeFillColorHex { get; init; } = string.Empty;
    public string PalletOccupancyColorHex { get; init; } = string.Empty;
    public string VelocityColorHex { get; init; } = string.Empty;
    public string AgingColorHex { get; init; } = string.Empty;
    public string OvercapacityColorHex { get; init; } = string.Empty;
    public string BlockedColorHex { get; init; } = string.Empty;

    // Simultaneous Conditions & Composite Tier
    public IReadOnlyList<string> ActiveConditions { get; init; } = [];
    public string PrimaryColorHex { get; init; } = string.Empty;
    public HeatmapVisualTier PrimaryTier { get; init; } = HeatmapVisualTier.Optimal;
}

public record HeatmapQueryFilterDto
{
    public HeatmapLayerType ActiveLayers { get; init; } = HeatmapLayerType.StandardOverview;
    public HeatmapLayerType PrimaryColorLayer { get; init; } = HeatmapLayerType.CubeUtilization;
    public Guid? ZoneId { get; init; }
    public string? AisleCode { get; init; }
    public int? ShelfLevel { get; init; }
    public Guid? ClientId { get; init; }
    public bool IncludeUnmappedLocations { get; init; } = true;
}

public record WarehouseHeatmapResponseDto
{
    public Guid FacilityId { get; init; }
    public string FacilityCode { get; init; } = string.Empty;
    public string FacilityName { get; init; } = string.Empty;
    public DateTime SnapshotDateUtc { get; init; }
    public HeatmapLayerType ActiveLayers { get; init; }
    public HeatmapLayerType PrimaryColorLayer { get; init; }
    public FacilitySpatialEnvelopeDto FacilityEnvelope { get; init; } = new();
    public WarehouseBoundingBoxDto BoundingBox { get; init; } = new();
    public WarehouseZoomPanBoundsDto ZoomPanBounds { get; init; } = new();
    public int TotalLocationsCount { get; init; }
    public int MappedLocationsCount { get; init; }
    public int MissingCoordinatesLocationsCount { get; init; }
    public int OutOfBoundsLocationsCount { get; init; }
    public int DuplicateCoordinatesLocationsCount { get; init; }
    public int OverCapacityLocationsCount { get; init; }
    public int BlockedLocationsCount { get; init; }
    public IReadOnlyList<HeatmapLocationCellDto> Locations { get; init; } = [];
    public IReadOnlyList<string> ActiveConditionSummary { get; init; } = [];
}

#endregion

#region Phase 6.3.6.3 Heatmap Projection Engine & Aggregations Models

public record FacilitySpatialEnvelopeDto
{
    public decimal OriginXMeters { get; init; } = 0.0m;
    public decimal OriginYMeters { get; init; } = 0.0m;
    public decimal OriginZMeters { get; init; } = 0.0m;
    public decimal LengthMeters { get; init; } = 150.0m;
    public decimal WidthMeters { get; init; } = 100.0m;
    public decimal ClearHeightMeters { get; init; } = 14.0m;
    public decimal TotalFloorAreaSqM => LengthMeters * WidthMeters;
    public decimal TotalBuildingVolumeCbm => LengthMeters * WidthMeters * ClearHeightMeters;
    public bool IsAuthoritative { get; init; } = true;
}

public record SpatialGridCellDto
{
    public int GridX { get; init; }
    public int GridY { get; init; }
    public int GridZ { get; init; }
    public decimal CenterXMeters { get; init; }
    public decimal CenterYMeters { get; init; }
    public decimal CenterZMeters { get; init; }
    public decimal CellSizeMeters { get; init; }
    public int LocationCount { get; init; }
    public decimal TotalUsableCbm { get; init; }
    public decimal TotalOccupiedCbm { get; init; }
    public decimal WeightedCubeUtilizationPercent => TotalUsableCbm > 0m
        ? Math.Round((TotalOccupiedCbm / TotalUsableCbm) * 100m, 2)
        : 0m;
    public int TotalUsablePallets { get; init; }
    public int TotalOccupiedPallets { get; init; }
    public decimal WeightedPalletOccupancyPercent => TotalUsablePallets > 0
        ? Math.Round(((decimal)TotalOccupiedPallets / TotalUsablePallets) * 100m, 2)
        : 0m;
    public bool HasOverCapacity { get; init; }
    public bool HasBlocked { get; init; }
    public IReadOnlyList<Guid> LocationIds { get; init; } = [];
    public string PrimaryColorHex { get; init; } = string.Empty;
}

public record SpatialHotspotDto
{
    public Guid HotspotId { get; init; } = Guid.NewGuid();
    public string HotspotType { get; init; } = string.Empty; // "Overcapacity", "DeadStock", "Congestion", "Blocked"
    public HeatmapVisualTier SeverityTier { get; init; } = HeatmapVisualTier.Critical;
    public Guid? ZoneId { get; init; }
    public string? ZoneCode { get; init; }
    public string? AisleCode { get; init; }
    public int ContiguousLocationsCount { get; init; }
    public decimal TotalUsableCbm { get; init; }
    public decimal TotalOccupiedCbm { get; init; }
    public decimal WeightedCubeUtilizationPercent { get; init; }
    public decimal TotalExcessCbm { get; init; }
    public decimal TotalAtRiskValue { get; init; }
    public IReadOnlyList<Guid> LocationIds { get; init; } = [];
    public IReadOnlyList<string> LocationCodes { get; init; } = [];
    public string RecommendedAction { get; init; } = string.Empty;
}

public record RackSpatialRollupDto
{
    public string RackCode { get; init; } = string.Empty;
    public string AisleCode { get; init; } = string.Empty;
    public Guid? ZoneId { get; init; }
    public int LocationsCount { get; init; }
    public decimal TotalUsableCbm { get; init; }
    public decimal TotalOccupiedCbm { get; init; }
    public decimal WeightedCubeUtilizationPercent { get; init; }
    public int TotalUsablePallets { get; init; }
    public int TotalOccupiedPallets { get; init; }
    public decimal WeightedPalletOccupancyPercent { get; init; }
    public int OverCapacityLocationsCount { get; init; }
    public int BlockedLocationsCount { get; init; }
    public decimal TotalInventoryValue { get; init; }
    public decimal DeadStockValue { get; init; }
    public decimal ExpiredStockValue { get; init; }
    public IReadOnlyDictionary<string, int> ConditionCounts { get; init; } = new Dictionary<string, int>();
    public string PrimaryColorHex { get; init; } = string.Empty;
    public HeatmapVisualTier PrimaryTier { get; init; }
}

public record AisleSpatialRollupDto
{
    public string AisleCode { get; init; } = string.Empty;
    public Guid? ZoneId { get; init; }
    public string? ZoneCode { get; init; }
    public int LocationsCount { get; init; }
    public int RacksCount { get; init; }
    public decimal TotalUsableCbm { get; init; }
    public decimal TotalOccupiedCbm { get; init; }
    public decimal WeightedCubeUtilizationPercent { get; init; }
    public int TotalUsablePallets { get; init; }
    public int TotalOccupiedPallets { get; init; }
    public decimal WeightedPalletOccupancyPercent { get; init; }
    public int OverCapacityLocationsCount { get; init; }
    public int BlockedLocationsCount { get; init; }
    public decimal TotalInventoryValue { get; init; }
    public decimal DeadStockValue { get; init; }
    public decimal ExpiredStockValue { get; init; }
    public IReadOnlyDictionary<string, int> ConditionCounts { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<RackSpatialRollupDto> Racks { get; init; } = [];
    public string PrimaryColorHex { get; init; } = string.Empty;
    public HeatmapVisualTier PrimaryTier { get; init; }
}

public record ZoneSpatialRollupDto
{
    public Guid ZoneId { get; init; }
    public string ZoneCode { get; init; } = string.Empty;
    public string ZoneName { get; init; } = string.Empty;
    public WarehouseZoneType ZoneType { get; init; }
    public int TotalLocationsCount { get; init; }
    public int ActiveLocationsCount { get; init; }
    public int BlockedLocationsCount { get; init; }
    public int OverCapacityLocationsCount { get; init; }
    public decimal TotalUsableCbm { get; init; }
    public decimal TotalOccupiedCbm { get; init; }
    public decimal WeightedCubeUtilizationPercent { get; init; }
    public int TotalUsablePallets { get; init; }
    public int TotalOccupiedPallets { get; init; }
    public decimal WeightedPalletOccupancyPercent { get; init; }
    public decimal TotalInventoryValue { get; init; }
    public decimal DeadStockValue { get; init; }
    public decimal ExpiredStockValue { get; init; }
    public IReadOnlyDictionary<string, int> ConditionCounts { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<AisleSpatialRollupDto> Aisles { get; init; } = [];
    public string PrimaryColorHex { get; init; } = string.Empty;
    public HeatmapVisualTier PrimaryTier { get; init; }
}

public record SpatialHeatmapProjectionDto
{
    public Guid FacilityId { get; init; }
    public string FacilityCode { get; init; } = string.Empty;
    public string FacilityName { get; init; } = string.Empty;
    public DateTime SnapshotDateUtc { get; init; }
    public int ProjectionVersion { get; init; } = 1;

    // Spatial bounds
    public FacilitySpatialEnvelopeDto FacilityEnvelope { get; init; } = new();
    public WarehouseBoundingBoxDto LocationBoundingBox { get; init; } = new();
    public WarehouseZoomPanBoundsDto ZoomPanBounds { get; init; } = new();

    // Macro Facility Rollup (Weighted HM-04)
    public decimal FacilityTotalUsableCbm { get; init; }
    public decimal FacilityTotalOccupiedCbm { get; init; }
    public decimal FacilityWeightedCubeUtilizationPercent { get; init; }
    public int FacilityTotalUsablePallets { get; init; }
    public int FacilityTotalOccupiedPallets { get; init; }
    public decimal FacilityWeightedPalletOccupancyPercent { get; init; }
    public decimal FacilityTotalInventoryValue { get; init; }
    public int TotalLocationsEvaluated { get; init; }
    public int OutOfBoundsLocationsCount { get; init; }
    public int MissingCoordinatesLocationsCount { get; init; }
    public int DuplicateCoordinatesLocationsCount { get; init; }

    // Multi-tier rollups
    public IReadOnlyList<ZoneSpatialRollupDto> Zones { get; init; } = [];
    public IReadOnlyList<SpatialGridCellDto> GridCells { get; init; } = [];
    public IReadOnlyList<SpatialHotspotDto> Hotspots { get; init; } = [];
    public IReadOnlyList<HeatmapLocationCellDto> Locations { get; init; } = [];
    public IReadOnlyDictionary<string, int> FacilityConditionSummary { get; init; } = new Dictionary<string, int>();
}

#endregion

#region Phase 6.3.7 ABC-Based Intelligent Warehouse Slotting Optimization Models

public record SlottingPolicyDto
{
    public Guid Id { get; init; }
    public Guid? FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public Guid? ZoneId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public SlottingOptimizationGoal PrimaryGoal { get; init; }
    public SlottingOptimizationGoal? SecondaryGoal { get; init; }
    public decimal GoldenZoneMinZMeters { get; init; }
    public decimal GoldenZoneMaxZMeters { get; init; }
    public string GoldenZoneShelfLevels { get; init; } = "1,2";
    public bool TargetClassAPickFace { get; init; }
    public bool TargetClassBIntermediate { get; init; }
    public bool TargetClassCReserve { get; init; }
    public bool ProtectDeadStockFromUnnecessaryRelocation { get; init; }
    public int MaxRelocationsPerBatch { get; init; }
    public decimal MinLaborSavingsUsd { get; init; }
    public decimal EstimatedRelocationLaborMinutes { get; init; }
    public decimal LaborHourlyRateUsd { get; init; }
    public decimal DisruptionPenaltyWeight { get; init; }
    public decimal WeightTravelDistance { get; init; }
    public decimal WeightErgonomics { get; init; }
    public decimal WeightPickDensity { get; init; }
    public decimal WeightAbcAffinity { get; init; }
    public decimal WeightVelocityAffinity { get; init; }
    public decimal WeightCubeFill { get; init; }
    public decimal WeightProductAffinity { get; init; } = 0.0m;
    public decimal WeightFinancialBenefit { get; init; } = 0.35m;
    public decimal WeightOperationalUrgency { get; init; } = 0.30m;
    public decimal WeightPlacementSuitability { get; init; } = 0.20m;
    public decimal WeightDisruptionPenalty { get; init; } = 0.15m;
    public string NegativeNetBenefitPolicy { get; init; } = "AllowIfHighUrgency";
    public decimal UrgencyThresholdForNegativeBenefit { get; init; } = 70.0m;
    public decimal TargetMonthlySavingsUsd { get; init; } = 50.0m;
    public decimal MaxAcceptableRelocationCostUsd { get; init; } = 100.0m;
    public decimal HandlingCostPerUnitUsd { get; init; } = 0.10m;
    public decimal HeavyHandlingSurchargePerTierUsd { get; init; } = 5.00m;
    public decimal BaseDisruptionCostUsd { get; init; } = 15.00m;
    public decimal GoldenZoneOpportunityGainUsd { get; init; } = 8.50m;
    public decimal AmortizationMonths { get; init; } = 3.0m;
    public int MaxProjectionAgeDays { get; init; } = 30;
    public int Version { get; init; }
    public DateTime EffectiveFromUtc { get; init; }
    public DateTime? EffectiveToUtc { get; init; }
    public bool IsActive { get; init; }
}

public record SlottingConstraintEvaluationDto
{
    public SlottingConstraintType ConstraintType { get; init; }
    public ConstraintSeverity Severity { get; init; }
    public bool IsSatisfied { get; init; }
    public string Description { get; init; } = string.Empty;
    public string? ViolationDetail { get; init; }
    public decimal PenaltyScore { get; init; }
}

public record SlottingCompatibilityResultDto
{
    public Guid SkuId { get; init; }
    public string Sku { get; init; } = string.Empty;
    public Guid LocationId { get; init; }
    public string LocationCode { get; init; } = string.Empty;
    public bool IsPhysicallyFeasible { get; init; }
    public bool IsOperationallyFeasible { get; init; }
    public bool IsOverallCompatible => IsPhysicallyFeasible && IsOperationallyFeasible;
    public decimal CalculatedSuitabilityScore { get; init; }
    public decimal ErgonomicScore { get; init; }
    public decimal AbcAffinityScore { get; init; }
    public decimal VelocityAffinityScore { get; init; }
    public decimal PickDensityScore { get; init; }
    public decimal TravelDistanceSavingsScore { get; init; }
    public decimal CoLocationAffinityScore { get; init; } = 50.0m;
    public string EffectiveAbcClass { get; init; } = string.Empty;
    public string EffectiveVelocityCategory { get; init; } = string.Empty;
    public string VelocityTrend { get; init; } = "Stable";
    public bool IsProjectionStaleOrMissing { get; init; }
    public string ClassificationSource { get; init; } = "AuthoritativeProjection";
    public DateTimeOffset? ProjectionCalculatedAtUtc { get; init; }
    public int? ProjectionAgeDays { get; init; }
    public string? PriorityResolutionExplanation { get; init; }
    public IReadOnlyList<SlottingConstraintEvaluationDto> ConstraintEvaluations { get; init; } = [];
    public IReadOnlyList<string> ViolatedHardConstraints { get; init; } = [];
    public IReadOnlyList<string> ViolatedSoftPreferences { get; init; } = [];
}

public record SlottingRecommendationDto
{
    public Guid Id { get; init; }
    public Guid BatchId { get; init; }
    public Guid ItemId { get; init; }
    public string ItemSku { get; init; } = string.Empty;
    public Guid ClientId { get; init; }
    public Guid CurrentLocationId { get; init; }
    public string CurrentLocationCode { get; init; } = string.Empty;
    public Guid ProposedLocationId { get; init; }
    public string ProposedLocationCode { get; init; } = string.Empty;
    public string SourceZoneCode { get; init; } = string.Empty;
    public string DestinationZoneCode { get; init; } = string.Empty;
    public string SkuVelocity { get; init; } = string.Empty;
    public string AbcClass { get; init; } = string.Empty;
    public string VelocityTrend { get; init; } = string.Empty;
    public string ReasonCode { get; init; } = string.Empty;
    public decimal SuitabilityScore { get; init; }
    public decimal ErgonomicScore { get; init; }
    public decimal AbcAffinityScore { get; init; }
    public decimal VelocityAffinityScore { get; init; }
    public decimal PickDensityScore { get; init; }
    public decimal CumulativeWeightConsumedKg { get; init; }
    public decimal CumulativeVolumeConsumedCm3 { get; init; }
    public decimal TravelDistanceDeltaMeters { get; init; }
    public decimal EstimatedMoveLaborMinutes { get; init; }
    public decimal EstimatedMonthlyNetSavingsUsd { get; init; }
    public decimal GrossMonthlySavingsUsd { get; init; }
    public decimal HandlingCostUsd { get; init; }
    public decimal MovementCostUsd { get; init; }
    public decimal DisruptionCostUsd { get; init; }
    public decimal TotalRelocationCostUsd { get; init; }
    public decimal UrgencyScore { get; init; }
    public string? UrgencyReason { get; init; }
    public decimal PriorityScore { get; init; }
    public string DisruptionLevel { get; init; } = "Low";
    public bool IsOperationallyFeasible { get; init; } = true;
    public bool IsHardConstraintViolated { get; init; }
    public string? ViolatedConstraintsSummary { get; init; }
    public bool IsProjectionStaleOrMissing { get; init; }
    public string ClassificationSource { get; init; } = "AuthoritativeProjection";
    public DateTimeOffset? ProjectionCalculatedAtUtc { get; init; }
    public int? ProjectionAgeDays { get; init; }
    public SlottingRecommendationStatus Status { get; init; }
    public string? RejectionReason { get; init; }
}

public record SlottingRecommendationBatchDto
{
    public Guid Id { get; init; }
    public Guid FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public Guid PolicyId { get; init; }
    public int PolicyVersion { get; init; }
    public string? PolicySnapshotJson { get; init; }
    public string PolicyName { get; init; } = string.Empty;
    public string BatchNumber { get; init; } = string.Empty;
    public SlottingBatchStatus Status { get; init; }
    public DateTime SnapshotDateUtc { get; init; }
    public int TotalProposedMoves { get; init; }
    public int ApprovedMovesCount { get; init; }
    public int RejectedMovesCount { get; init; }
    public int ExecutedMovesCount { get; init; }
    public decimal EstimatedLaborMinutesTotal { get; init; }
    public decimal EstimatedTravelDistanceSavedMetersTotal { get; init; }
    public decimal EstimatedMonthlyNetSavingsUsd { get; init; }
    public decimal AverageSuitabilityScore { get; init; }
    public Guid? ApprovedBy { get; init; }
    public DateTime? ApprovedAtUtc { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<SlottingRecommendationDto> Recommendations { get; init; } = [];
}

public record CreateSlottingPolicyRequestDto
{
    public string Name { get; init; } = string.Empty;
    public Guid? FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public Guid? ZoneId { get; init; }
    public SlottingOptimizationGoal PrimaryGoal { get; init; } = SlottingOptimizationGoal.ErgonomicGoldenZone;
    public SlottingOptimizationGoal? SecondaryGoal { get; init; } = SlottingOptimizationGoal.MinimizeTravelDistance;
    public decimal GoldenZoneMinZMeters { get; init; } = 0.80m;
    public decimal GoldenZoneMaxZMeters { get; init; } = 1.50m;
    public string GoldenZoneShelfLevels { get; init; } = "1,2";
    public bool TargetClassAPickFace { get; init; } = true;
    public bool TargetClassBIntermediate { get; init; } = true;
    public bool TargetClassCReserve { get; init; } = true;
    public bool ProtectDeadStock { get; init; } = true;
    public int MaxRelocationsPerBatch { get; init; } = 50;
    public decimal MinLaborSavingsUsd { get; init; } = 5.00m;
    public decimal EstimatedRelocationLaborMinutes { get; init; } = 6.00m;
    public decimal LaborHourlyRateUsd { get; init; } = 25.00m;
    public decimal DisruptionPenaltyWeight { get; init; } = 0.20m;
    public decimal WeightTravelDistance { get; init; } = 0.25m;
    public decimal WeightErgonomics { get; init; } = 0.25m;
    public decimal WeightPickDensity { get; init; } = 0.15m;
    public decimal WeightAbcAffinity { get; init; } = 0.20m;
    public decimal WeightVelocityAffinity { get; init; } = 0.15m;
    public decimal WeightCubeFill { get; init; } = 0.0m;
    public decimal WeightProductAffinity { get; init; } = 0.0m;
    public int MaxProjectionAgeDays { get; init; } = 30;
    public string? Description { get; init; }
}

public record SlottingSimulationRequestDto
{
    public Guid FacilityId { get; init; }
    public Guid? PolicyId { get; init; }
    public Guid? ClientId { get; init; }
    public int? MaxMoves { get; init; }
    public DateTime? AsOfDateUtc { get; init; }
    public string? Notes { get; init; }
}

public record SkuLocationSuitabilityScoreDto
{
    public Guid LocationId { get; init; }
    public string LocationCode { get; init; } = string.Empty;
    public string? ZoneCode { get; init; }
    public decimal CompositeSuitabilityScore { get; init; }
    public decimal ErgonomicScore { get; init; }
    public decimal AbcAffinityScore { get; init; }
    public decimal VelocityAffinityScore { get; init; }
    public decimal PickDensityScore { get; init; }
    public decimal TravelDistanceSavingsScore { get; init; }
    public decimal CoLocationAffinityScore { get; init; } = 50.0m;
    public bool IsPhysicallyFeasible { get; init; }
    public bool IsOperationallyFeasible { get; init; }
    public bool IsOverallCompatible { get; init; }
    public decimal EstimatedRoundTripTravelSavedMeters { get; init; }
    public decimal EstimatedMonthlyNetSavingsUsd { get; init; }
    public IReadOnlyList<string> ViolatedHardConstraints { get; init; } = [];
    public IReadOnlyList<string> ViolatedSoftPreferences { get; init; } = [];
}

public record SkuSuitabilityProfileDto
{
    public Guid ItemId { get; init; }
    public string Sku { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public Guid CurrentLocationId { get; init; }
    public string CurrentLocationCode { get; init; } = string.Empty;
    public AbcClassificationCategory AbcClass { get; init; }
    public InventoryMovementVelocityCategory VelocityCategory { get; init; }
    public string VelocityTrend { get; init; } = "Stable";
    public decimal DailyDemandVelocity { get; init; }
    public decimal PickFrequencyPerDay { get; init; }
    public bool IsProjectionStaleOrMissing { get; init; }
    public string ClassificationSource { get; init; } = "AuthoritativeProjection";
    public DateTimeOffset? ProjectionCalculatedAtUtc { get; init; }
    public int? ProjectionAgeDays { get; init; }
    public IReadOnlyList<SkuLocationSuitabilityScoreDto> CandidateLocations { get; init; } = [];
}

public record SlottingSuitabilityMatrixDto
{
    public Guid FacilityId { get; init; }
    public Guid PolicyId { get; init; }
    public int PolicyVersion { get; init; }
    public DateTime EvaluatedAtUtc { get; init; }
    public IReadOnlyList<SkuSuitabilityProfileDto> SkuProfiles { get; init; } = [];
}

public record GenerateSuitabilityMatrixRequestDto
{
    public Guid FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public Guid? PolicyId { get; init; }
    public DateTime? AsOfDateUtc { get; init; }
    public IReadOnlyList<Guid>? ItemIds { get; init; }
    public IReadOnlyList<Guid>? LocationIds { get; init; }
}

#endregion

#region Phase 6.3.7.3 Product Affinity & Co-Locational Slotting Rules Models

public record ProductAffinityPolicyDto
{
    public Guid Id { get; init; }
    public Guid? FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int AnalysisWindowDays { get; init; } = 90;
    public int MinOrderCount { get; init; } = 30;
    public int MinCoOccurrenceCount { get; init; } = 3;
    public decimal MinSupport { get; init; } = 0.0100m;
    public decimal MinConfidence { get; init; } = 0.1000m;
    public decimal MinLift { get; init; } = 1.1000m;
    public decimal MinClusterLift { get; init; } = 1.2500m;
    public int MaxClusterSize { get; init; } = 8;
    public decimal WeightProductAffinity { get; init; } = 0.15m;
    public bool IncludeUnassignedFacilityOrders { get; init; } = false;
    public DateTime? SeasonalStartDateUtc { get; init; }
    public DateTime? SeasonalEndDateUtc { get; init; }
    public bool IsActive { get; init; } = true;
    public int Version { get; init; } = 1;
}

public record ProductAffinityPairDto
{
    public Guid Id { get; init; }
    public Guid ClientId { get; init; }
    public Guid FacilityId { get; init; }
    public Guid? MiningBatchId { get; init; }
    public Guid ItemAId { get; init; }
    public string ItemASku { get; init; } = string.Empty;
    public Guid ItemBId { get; init; }
    public string ItemBSku { get; init; } = string.Empty;
    public int TotalOrdersEvaluated { get; init; }
    public int ItemAOrderCount { get; init; }
    public int ItemBOrderCount { get; init; }
    public int CoOccurrenceCount { get; init; }
    public decimal Support { get; init; }
    public decimal ItemASupport { get; init; }
    public decimal ItemBSupport { get; init; }
    public decimal ConfidenceAtoB { get; init; }
    public decimal ConfidenceBtoA { get; init; }
    public decimal MaxConfidence { get; init; }
    public decimal KulczynskiMeasure { get; init; }
    public decimal Lift { get; init; }
    public decimal? ChiSquareStatistic { get; init; }
    public decimal? MinExpectedCellFrequency { get; init; }
    public bool HasYatesCorrection { get; init; }
    public decimal? PValue { get; init; }
    public bool IsStatisticallySignificant { get; init; }
    public DateTime WindowStartUtc { get; init; }
    public DateTime WindowEndUtc { get; init; }
    public DateTime CalculatedAtUtc { get; init; }
}

public record ProductAffinityClusterMemberDto
{
    public Guid Id { get; init; }
    public Guid ClusterId { get; init; }
    public Guid ItemId { get; init; }
    public string Sku { get; init; } = string.Empty;
    public bool IsAnchor { get; init; }
    public int WithinClusterDegree { get; init; }
    public decimal AverageWithinClusterLift { get; init; }
}

public record ProductAffinityClusterDto
{
    public Guid Id { get; init; }
    public Guid ClientId { get; init; }
    public Guid FacilityId { get; init; }
    public Guid? MiningBatchId { get; init; }
    public string ClusterCode { get; init; } = string.Empty;
    public Guid PrimaryAnchorItemId { get; init; }
    public string PrimaryAnchorSku { get; init; } = string.Empty;
    public int ItemCount { get; init; }
    public decimal AverageLift { get; init; }
    public decimal MaxLift { get; init; }
    public DateTime WindowStartUtc { get; init; }
    public DateTime WindowEndUtc { get; init; }
    public DateTime CalculatedAtUtc { get; init; }
    public IReadOnlyList<ProductAffinityClusterMemberDto> Members { get; init; } = [];
}

public record MineProductAffinityRequestDto
{
    public Guid FacilityId { get; init; }
    public Guid ClientId { get; init; }
    public int? AnalysisWindowDays { get; init; }
    public DateTime? AsOfDateUtc { get; init; }
    public DateTime? WindowStartUtc { get; init; }
    public DateTime? WindowEndUtc { get; init; }
    public int? MinOrderCount { get; init; }
    public int? MinCoOccurrenceCount { get; init; }
    public decimal? MinSupport { get; init; }
    public decimal? MinConfidence { get; init; }
    public decimal? MinLift { get; init; }
    public decimal? MinClusterLift { get; init; }
    public int? MaxClusterSize { get; init; }
    public bool? IncludeUnassignedFacilityOrders { get; init; }
    public bool SaveMinedAffinity { get; init; } = true;
}

public record ProductAffinityMiningResultDto
{
    public Guid MiningBatchId { get; init; }
    public Guid FacilityId { get; init; }
    public Guid ClientId { get; init; }
    public DateTime EvaluatedAtUtc { get; init; }
    public DateTime WindowStartUtc { get; init; }
    public DateTime WindowEndUtc { get; init; }
    public int TotalOrdersScanned { get; init; }
    public int TotalQualifyingOrders { get; init; }
    public int TotalUniqueItems { get; init; }
    public int TotalCandidatePairs { get; init; }
    public int StatisticallySignificantPairsCount { get; init; }
    public int ClustersFormedCount { get; init; }
    public bool SampleSizeSufficient { get; init; }
    public string StatusMessage { get; init; } = string.Empty;
    public IReadOnlyList<ProductAffinityPairDto> SignificantPairs { get; init; } = [];
    public IReadOnlyList<ProductAffinityClusterDto> Clusters { get; init; } = [];
}

public record PairContingencyStatistics
{
    public int TotalOrders { get; init; }
    public int ItemAOrderCount { get; init; }
    public int ItemBOrderCount { get; init; }
    public int CoOccurrenceCount { get; init; }
    public decimal SupportA { get; init; }
    public decimal SupportB { get; init; }
    public decimal SupportAB { get; init; }
    public decimal ConfidenceAtoB { get; init; }
    public decimal ConfidenceBtoA { get; init; }
    public decimal MaxConfidence { get; init; }
    public decimal KulczynskiMeasure { get; init; }
    public decimal Lift { get; init; }
    public decimal? ChiSquareStatistic { get; init; }
    public decimal MinExpectedCellFrequency { get; init; }
    public bool HasYatesCorrection { get; init; }
    public decimal PValue { get; init; }
    public bool IsDirectionallyPositive { get; init; }
}

#endregion

#region Phase 6.3.7.4 Relocation Recommendation & Multi-Factor Prioritization Models

public record RelocationPrioritizationRequestDto
{
    public Guid FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public Guid? PolicyId { get; init; }
    public string ShiftType { get; init; } = "Standard";
    public int? MaxMoves { get; init; }
    public string? NegativeNetBenefitPolicyOverride { get; init; }
    public decimal? UrgencyThresholdOverride { get; init; }
    public DateTime? AsOfDateUtc { get; init; }
    public string? Notes { get; init; }
}

public record PrioritizedRelocationRecommendationDto
{
    public Guid RecommendationId { get; init; }
    public Guid ItemId { get; init; }
    public string ItemSku { get; init; } = string.Empty;
    public Guid ClientId { get; init; }
    public Guid CurrentLocationId { get; init; }
    public string CurrentLocationCode { get; init; } = string.Empty;
    public Guid ProposedLocationId { get; init; }
    public string ProposedLocationCode { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public decimal GrossMonthlySavingsUsd { get; init; }
    public decimal HandlingCostUsd { get; init; }
    public decimal MovementCostUsd { get; init; }
    public decimal DisruptionCostUsd { get; init; }
    public decimal TotalRelocationCostUsd { get; init; }
    public decimal AmortizedMonthlyCostUsd { get; init; }
    public decimal NetMonthlyBenefitUsd { get; init; }
    public decimal UrgencyScore { get; init; }
    public string UrgencyReason { get; init; } = string.Empty;
    public decimal SuitabilityScore { get; init; }
    public decimal DisruptionPenaltyScore { get; init; }
    public decimal DisruptionMitigationScore { get; init; }
    public decimal PriorityScore { get; init; }
    public string DisruptionLevel { get; init; } = "Low";
    public bool IsOperationallyFeasible { get; init; } = true;
    public int RankingRank { get; init; }
    public string ReasonCode { get; init; } = string.Empty;
    public IReadOnlyList<string> ViolatedConstraints { get; init; } = [];
}

public record RelocationPrioritizationResultDto
{
    public Guid BatchId { get; init; }
    public string BatchNumber { get; init; } = string.Empty;
    public Guid FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public Guid PolicyId { get; init; }
    public string PolicyName { get; init; } = string.Empty;
    public DateTime EvaluatedAtUtc { get; init; }
    public int TotalCandidatesEvaluated { get; init; }
    public int FeasibleCandidatesCount { get; init; }
    public int DisqualifiedNegativeBenefitCount { get; init; }
    public int HighUrgencyNegativeBenefitCount { get; init; }
    public decimal TotalProjectedGrossMonthlySavingsUsd { get; init; }
    public decimal TotalProjectedOneTimeRelocationCostUsd { get; init; }
    public decimal TotalProjectedNetMonthlyBenefitUsd { get; init; }
    public int PhysicalInventoryTransfersTriggered { get; init; } = 0;
    public int LedgerMutationsOccurred { get; init; } = 0;
    public IReadOnlyList<PrioritizedRelocationRecommendationDto> PrioritizedRecommendations { get; init; } = [];
}

public record RelocationApprovalRequestDto
{
    public Guid BatchId { get; init; }
    public IReadOnlyList<Guid> ApprovedRecommendationIds { get; init; } = [];
    public IReadOnlyList<Guid> RejectedRecommendationIds { get; init; } = [];
    public Dictionary<Guid, string>? RejectionReasonsByRecommendationId { get; init; }
    public string? ApprovalNotes { get; init; }
}

public record CancelBatchRequestDto
{
    public string Reason { get; init; } = string.Empty;
}

public record RelocationApprovalResultDto
{
    public Guid BatchId { get; init; }
    public SlottingBatchStatus Status { get; init; }
    public int ApprovedCount { get; init; }
    public int RejectedCount { get; init; }
    public int SkippedCount { get; init; }
    public int PhysicalInventoryTransfersTriggered { get; init; } = 0;
    public int LedgerMutationsOccurred { get; init; } = 0;
    public DateTime ProcessedAtUtc { get; init; }
    public Guid? ApprovedBy { get; init; }
    public string Message { get; init; } = string.Empty;
}

public static class SlottingInvariantNotificationEvaluator
{
    public static (string Message, bool IsCriticalViolation) EvaluatePrioritization(RelocationPrioritizationResultDto result)
    {
        if (result.LedgerMutationsOccurred > 0 || result.PhysicalInventoryTransfersTriggered > 0)
        {
            return ($"CRITICAL INVARIANT VIOLATION: Server reported {result.LedgerMutationsOccurred} operational ledger mutations and {result.PhysicalInventoryTransfersTriggered} physical transfers during prioritization simulation.", true);
        }
        return ($"Successfully prioritized batch {result.BatchNumber}: {result.FeasibleCandidatesCount} feasible moves ({result.DisqualifiedNegativeBenefitCount} negative net benefit gated). Server-verified operational ledger mutations: {result.LedgerMutationsOccurred}, physical transfers: {result.PhysicalInventoryTransfersTriggered}.", false);
    }

    public static (string Message, bool IsCriticalViolation) EvaluateApproval(RelocationApprovalResultDto result)
    {
        if (result.LedgerMutationsOccurred > 0 || result.PhysicalInventoryTransfersTriggered > 0)
        {
            return ($"CRITICAL INVARIANT VIOLATION: Server reported {result.LedgerMutationsOccurred} operational ledger mutations and {result.PhysicalInventoryTransfersTriggered} physical transfers during batch approval.", true);
        }
        return ($"Batch approved: {result.ApprovedCount} moves approved ({result.SkippedCount} skipped). Status: {result.Status}. Server-verified operational ledger mutations: {result.LedgerMutationsOccurred}, physical transfers: {result.PhysicalInventoryTransfersTriggered}.", false);
    }

    public static (string Message, bool IsCriticalViolation) EvaluateRejection(RelocationApprovalResultDto result)
    {
        if (result.LedgerMutationsOccurred > 0 || result.PhysicalInventoryTransfersTriggered > 0)
        {
            return ($"CRITICAL INVARIANT VIOLATION: Server reported {result.LedgerMutationsOccurred} operational ledger mutations and {result.PhysicalInventoryTransfersTriggered} physical transfers during batch rejection.", true);
        }
        return ($"Batch updated: {result.RejectedCount} moves rejected with justification ({result.SkippedCount} skipped). Status: {result.Status}. Server-verified operational ledger mutations: {result.LedgerMutationsOccurred}, physical transfers: {result.PhysicalInventoryTransfersTriggered}.", false);
    }
}

public record LocationComparisonDetailDto
{
    public Guid LocationId { get; init; }
    public string LocationCode { get; init; } = string.Empty;
    public string ZoneCode { get; init; } = string.Empty;
    public WarehouseZoneType ZoneType { get; init; }
    public int ShelfLevel { get; init; }
    public decimal ZCoord { get; init; }
    public decimal DistanceToDockMeters { get; init; }
    public bool IsInGoldenZone { get; init; }
    public decimal OccupiedWeightKg { get; init; }
    public decimal MaxWeightKg { get; init; }
    public decimal WeightUtilizationPercent { get; init; }
    public decimal OccupiedVolumeCm3 { get; init; }
    public decimal MaxVolumeCm3 { get; init; }
    public decimal VolumeUtilizationPercent { get; init; }
}

public record SlottingRecommendationDetailDto
{
    public Guid RecommendationId { get; init; }
    public Guid BatchId { get; init; }
    public string BatchNumber { get; init; } = string.Empty;
    public Guid ItemId { get; init; }
    public string ItemSku { get; init; } = string.Empty;
    public string? ItemDescription { get; init; }
    public Guid ClientId { get; init; }
    public decimal Quantity { get; init; }
    public SlottingRecommendationStatus Status { get; init; }
    public string? RejectionReason { get; init; }
    public string ReasonCode { get; init; } = string.Empty;
    public bool IsOperationallyFeasible { get; init; }
    public bool IsHardConstraintViolated { get; init; }
    public IReadOnlyList<string> ViolatedConstraints { get; init; } = [];

    // 6D Placement Suitability
    public decimal SuitabilityScore { get; init; }
    public decimal ErgonomicScore { get; init; }
    public decimal AbcAffinityScore { get; init; }
    public decimal VelocityAffinityScore { get; init; }
    public decimal PickDensityScore { get; init; }
    public decimal TravelDistanceDeltaMeters { get; init; }
    public decimal ProductAffinityScore { get; init; }

    // 4-Factor Operational Urgency
    public decimal UrgencyScore { get; init; }
    public string UrgencyReason { get; init; } = string.Empty;
    public decimal VelocityMismatchScore { get; init; }
    public decimal GoldenZoneCongestionScore { get; init; }
    public decimal SeasonalDemandScore { get; init; }
    public decimal ErgonomicHazardScore { get; init; }

    // Transparent Economics Breakdown
    public decimal GrossMonthlySavingsUsd { get; init; }
    public decimal HandlingCostUsd { get; init; }
    public decimal MovementCostUsd { get; init; }
    public decimal DisruptionCostUsd { get; init; }
    public decimal TotalRelocationCostUsd { get; init; }
    public decimal AmortizedMonthlyCostUsd { get; init; }
    public decimal NetMonthlyBenefitUsd { get; init; }
    public decimal DisruptionPenaltyScore { get; init; }
    public decimal DisruptionMitigationScore { get; init; }
    public decimal PriorityScore { get; init; }
    public string DisruptionLevel { get; init; } = "Low";

    // Destination Comparison
    public LocationComparisonDetailDto CurrentLocation { get; init; } = null!;
    public LocationComparisonDetailDto ProposedLocation { get; init; } = null!;
}

public record SlottingBatchSummaryDto
{
    public Guid BatchId { get; init; }
    public string BatchNumber { get; init; } = string.Empty;
    public Guid FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public Guid PolicyId { get; init; }
    public string PolicyName { get; init; } = string.Empty;
    public SlottingBatchStatus Status { get; init; }
    public DateTime SnapshotDateUtc { get; init; }
    public int TotalProposedMoves { get; init; }
    public int ApprovedMovesCount { get; init; }
    public int RejectedMovesCount { get; init; }
    public decimal EstimatedMonthlyNetSavingsUsd { get; init; }
    public decimal AverageSuitabilityScore { get; init; }
    public Guid? ApprovedBy { get; init; }
    public DateTime? ApprovedAtUtc { get; init; }
    public string? Notes { get; init; }
}

#endregion
