using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

public interface IAnalyticsQueryService
{
    Task<IReadOnlyList<FacilityProductivityDto>> GetFacilityProductivityAsync(
        Guid facilityId,
        DateTime startDateUtc,
        DateTime endDateUtc,
        TimeGrain grain = TimeGrain.Daily,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClientProfitabilityDto>> GetClientProfitabilityAsync(
        Guid clientId,
        DateTime startDateUtc,
        DateTime endDateUtc,
        Guid? facilityId = null,
        TimeGrain grain = TimeGrain.Daily,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryUtilizationDto>> GetInventoryUtilizationAsync(
        Guid facilityId,
        DateTime startDateUtc,
        DateTime endDateUtc,
        Guid? clientId = null,
        TimeGrain grain = TimeGrain.Daily,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CarrierPerformanceDto>> GetCarrierPerformanceAsync(
        string carrierCode,
        Guid? facilityId,
        DateTime startDateUtc,
        DateTime endDateUtc,
        TimeGrain grain = TimeGrain.Daily,
        CancellationToken cancellationToken = default);

    Task<ControlTowerSummaryDto> GetControlTowerSummaryAsync(
        Guid? facilityId,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HourlyThroughputPacingDto>> GetHourlyThroughputPacingAsync(
        Guid facilityId,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken = default);

    Task<DockYardOperationalStatusDto> GetDockYardStatusAsync(
        Guid facilityId,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default);

    Task<OrderFulfillmentSlaDashboardDto> GetOrderFulfillmentSlaDashboardAsync(
        Guid? facilityId,
        Guid? clientId = null,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default);

    Task<FacilityBottleneckAnalysisDto> GetFacilityBottlenecksAsync(
        Guid facilityId,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default);

    Task<MultiFacilityComparisonDto> GetMultiFacilityComparisonAsync(
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default);

    Task<ControlTowerExecutiveOverviewDto> GetExecutiveOverviewAsync(
        Guid? facilityId,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default);

    // Phase 6.2 Workforce, Labor & Productivity Queries
    Task<IReadOnlyList<WorkerProductivityDto>> GetWorkerProductivityAsync(
        Guid workerUserId,
        DateTime startDateUtc,
        DateTime endDateUtc,
        Guid? requestingUserId = null,
        bool hasElevatedWorkforceAccess = false,
        CancellationToken cancellationToken = default);

    Task<FacilityWorkforceSummaryDto> GetFacilityWorkforceSummaryAsync(
        Guid facilityId,
        DateTime dateUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkerTaskExecutionLogDto>> GetWorkerTaskExecutionLogsAsync(
        Guid workerUserId,
        DateTime startDateUtc,
        DateTime endDateUtc,
        Guid? requestingUserId = null,
        bool hasElevatedWorkforceAccess = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkerIdleIntervalDto>> GetWorkerIdleIntervalsAsync(
        Guid workerUserId,
        DateTime startDateUtc,
        DateTime endDateUtc,
        Guid? requestingUserId = null,
        bool hasElevatedWorkforceAccess = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LaborStandardProfileDto>> GetLaborStandardProfilesAsync(
        Guid? facilityId = null,
        WarehouseLaborActivityType? activityType = null,
        Guid? zoneId = null,
        CancellationToken cancellationToken = default);

    Task<LaborCostAnalysisDto> GetLaborCostAnalysisAsync(
        Guid facilityId,
        DateTime startDateUtc,
        DateTime endDateUtc,
        CancellationToken cancellationToken = default);

    // Phase 6.3 Inventory Health & Space Utilization Queries
    Task<InventoryAgingPolicyDto> GetInventoryAgingPolicyAsync(
        Guid? facilityId = null,
        Guid? clientId = null,
        CancellationToken cancellationToken = default);

    Task<InventoryHealthSummaryDto> GetInventoryHealthSummaryAsync(
        Guid facilityId,
        DateTime? asOfDateUtc = null,
        Guid? clientId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DailyInventoryAgingRecordDto>> GetDailyInventoryAgingRecordsAsync(
        Guid facilityId,
        DateTime? asOfDateUtc = null,
        Guid? clientId = null,
        InventoryAgingBucket? bucket = null,
        bool? isDeadStock = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LotAgingRecordDto>> GetLotAgingBreakdownAsync(
        Guid facilityId,
        Guid itemId,
        DateTime? asOfDateUtc = null,
        CancellationToken cancellationToken = default);

    Task<DeadStockAnalysisDto> GetDeadStockAnalysisAsync(
        Guid facilityId,
        Guid? clientId = null,
        DateTime? asOfDateUtc = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LocationSpaceUtilizationDto>> GetLocationSpaceUtilizationAsync(
        Guid facilityId,
        DateTime? asOfDateUtc = null,
        Guid? zoneId = null,
        CancellationToken cancellationToken = default);

    Task<FacilitySpaceUtilizationSummaryDto> GetFacilitySpaceUtilizationSummaryAsync(
        Guid facilityId,
        DateTime? asOfDateUtc = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OverCapacityAlertDto>> GetOverCapacityAlertsAsync(
        Guid facilityId,
        DateTime? asOfDateUtc = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SkuVelocityClassificationDto>> GetSkuVelocityClassificationsAsync(
        Guid facilityId,
        Guid? clientId = null,
        AbcClassificationCriteria? criteria = null,
        AbcClassificationCategory? abcClass = null,
        VelocityObservationWindow? window = null,
        InventoryMovementVelocityCategory? velocityCategory = null,
        Guid? itemId = null,
        CancellationToken cancellationToken = default);

    Task<SkuVelocityMultiWindowComparisonDto?> GetSkuVelocityMultiWindowComparisonAsync(
        Guid facilityId,
        Guid itemId,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default);

    Task<InventoryTurnoverAnalysisDto> GetInventoryTurnoverAnalysisAsync(
        Guid facilityId,
        Guid? clientId = null,
        VelocityObservationWindow window = VelocityObservationWindow.Days90,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ZoneVelocityProfileDto>> GetZoneVelocityProfilesAsync(
        Guid facilityId,
        Guid? zoneId = null,
        CancellationToken cancellationToken = default);

    Task<SkuVelocitySummaryDto> GetSkuVelocitySummaryAsync(
        Guid facilityId,
        Guid? clientId = null,
        VelocityObservationWindow window = VelocityObservationWindow.Days90,
        CancellationToken cancellationToken = default);

    // Phase 6.3.5 ABC Inventory Classification Engine
    Task<AbcClassificationPolicyDto?> GetAbcClassificationPolicyAsync(
        Guid? facilityId = null,
        Guid? clientId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AbcClassificationPolicyDto>> GetAbcClassificationPoliciesAsync(
        Guid? facilityId = null,
        Guid? clientId = null,
        CancellationToken cancellationToken = default);

    Task<AbcClassificationSummaryDto> GetAbcClassificationSummaryAsync(
        Guid facilityId,
        Guid? clientId = null,
        AbcClassificationCriteria? criteria = null,
        int? version = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AbcRankedSkuDto>> GetAbcRankedSkusAsync(
        Guid facilityId,
        Guid? clientId = null,
        AbcClassificationCategory? abcClass = null,
        AbcClassificationCriteria? criteria = null,
        int? version = null,
        int pageNumber = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    Task<AbcMigrationAnalysisDto> GetAbcMigrationAnalysisAsync(
        Guid facilityId,
        Guid? clientId,
        int baseVersion,
        int targetVersion,
        CancellationToken cancellationToken = default);

    // Phase 6.3.6 Dynamic Inventory Heatmaps and Inventory Health Dashboard
    Task<UnifiedInventoryHealthDashboardDto> GetUnifiedInventoryHealthDashboardAsync(
        Guid facilityId,
        Guid? clientId = null,
        DateTime? asOfDateUtc = null,
        CancellationToken cancellationToken = default);

    // Phase 6.3.6.2 Dynamic Warehouse Heatmap Domain
    Task<WarehouseHeatmapResponseDto> GetWarehouseHeatmapAsync(
        Guid facilityId,
        HeatmapQueryFilterDto? filter = null,
        DateTime? asOfDateUtc = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HeatmapColorScaleDefinitionDto>> GetHeatmapColorScalesAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WarehouseTopologyNodeDto>> GetWarehouseTopologyAsync(
        Guid facilityId,
        CancellationToken cancellationToken = default);

    // Phase 6.3.6.3 Heatmap Projection Engine & Aggregations
    Task<SpatialHeatmapProjectionDto> GetSpatialHeatmapProjectionAsync(
        Guid facilityId,
        HeatmapQueryFilterDto? filter = null,
        decimal gridCellSizeMeters = 2.0m,
        DateTime? asOfDateUtc = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SpatialHotspotDto>> GetSpatialHotspotsAsync(
        Guid facilityId,
        DateTime? asOfDateUtc = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ZoneSpatialRollupDto>> GetZoneSpatialRollupsAsync(
        Guid facilityId,
        DateTime? asOfDateUtc = null,
        CancellationToken cancellationToken = default);
}

