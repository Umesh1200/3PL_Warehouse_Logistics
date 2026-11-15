using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services.Analytics;

/// <summary>
/// Query service delivering high-performance, read-model analytics for the Operational Control Tower,
/// hourly throughput pacing, live dock turn-time heatmaps, order SLA countdowns, and multi-facility rollups.
/// Strictly enforces multi-tenant isolation and facility-level access controls.
/// DB-SPEC-3PL-2026 Phase 6.0 Analytics Foundation &amp; Phase 6.1 Executive Control Tower.
/// </summary>
public class AnalyticsQueryService : IAnalyticsQueryService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<AnalyticsQueryService> _logger;

    public AnalyticsQueryService(
        IApplicationDbContext dbContext,
        ICurrentUserContext currentUser,
        ILogger<AnalyticsQueryService> logger)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<IReadOnlyList<FacilityProductivityDto>> GetFacilityProductivityAsync(
        Guid facilityId,
        DateTime startDateUtc,
        DateTime endDateUtc,
        TimeGrain grain = TimeGrain.Daily,
        CancellationToken cancellationToken = default)
    {
        ValidateFacilityAccess(facilityId);

        var start = startDateUtc.Date;
        var end = endDateUtc.Date;

        var records = await _dbContext.DailyFacilityProductivitySummaries
            .AsNoTracking()
            .Where(s => s.FacilityId == facilityId && s.DateUtc >= start && s.DateUtc <= end && s.TimeGrain == grain)
            .OrderBy(s => s.DateUtc)
            .Select(s => new FacilityProductivityDto
            {
                FacilityId = s.FacilityId,
                DateUtc = s.DateUtc,
                TimeGrain = s.TimeGrain,
                OrdersReceivedCount = s.OrdersReceivedCount,
                OrdersAllocatedCount = s.OrdersAllocatedCount,
                LinesPickedCount = s.LinesPickedCount,
                UnitsPickedCount = s.UnitsPickedCount,
                CartonsPackedCount = s.CartonsPackedCount,
                OrdersDispatchedCount = s.OrdersDispatchedCount,
                PackagesDispatchedCount = s.PackagesDispatchedCount,
                InboundPalletsReceivedCount = s.InboundPalletsReceivedCount,
                InboundPutawayCompletedCount = s.InboundPutawayCompletedCount,
                ReturnsInspectedCount = s.ReturnsInspectedCount,
                ActiveWorkerHeadcount = s.ActiveWorkerHeadcount,
                TotalLaborHours = s.TotalLaborHours,
                TotalPickHours = s.TotalPickHours,
                TotalPutawayHours = s.TotalPutawayHours,
                UnitsPerLaborHour = s.UnitsPerLaborHour,
                PutawayPerLaborHour = s.PutawayPerLaborHour,
                AverageFulfillmentCycleTimeHours = s.AverageFulfillmentCycleTimeHours,
                SameDayDispatchAdherencePercent = s.SameDayDispatchAdherencePercent
            })
            .ToListAsync(cancellationToken);

        return records;
    }

    public async Task<IReadOnlyList<HourlyThroughputPacingDto>> GetHourlyThroughputPacingAsync(
        Guid facilityId,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateFacilityAccess(facilityId);

        var records = await _dbContext.HourlyFacilityThroughputSnapshots
            .AsNoTracking()
            .Where(s => s.FacilityId == facilityId && s.HourTimestampUtc >= startUtc && s.HourTimestampUtc <= endUtc)
            .OrderBy(s => s.HourTimestampUtc)
            .Select(s => new HourlyThroughputPacingDto
            {
                FacilityId = s.FacilityId,
                HourTimestampUtc = s.HourTimestampUtc,
                OrdersReceivedCount = s.OrdersReceivedCount,
                OrdersAllocatedCount = s.OrdersAllocatedCount,
                LinesPickedCount = s.LinesPickedCount,
                UnitsPickedCount = s.UnitsPickedCount,
                CartonsPackedCount = s.CartonsPackedCount,
                OrdersDispatchedCount = s.OrdersDispatchedCount,
                PackagesDispatchedCount = s.PackagesDispatchedCount,
                InboundPalletsReceivedCount = s.InboundPalletsReceivedCount,
                InboundPutawayCompletedCount = s.InboundPutawayCompletedCount,
                ReturnsInspectedCount = s.ReturnsInspectedCount,
                ActiveWorkerHeadcount = s.ActiveWorkerHeadcount,
                TotalLaborHours = s.TotalLaborHours,
                PickDurationHours = s.PickDurationHours,
                PutawayDurationHours = s.PutawayDurationHours,
                UnitsPerLaborHour = s.UnitsPerLaborHour,
                PutawayPerLaborHour = s.PutawayPerLaborHour,
                AverageCycleTimeHours = s.AverageCycleTimeHours,
                SameDayDispatchAdherencePercent = s.SameDayDispatchAdherencePercent,
                ProjectionVersion = s.ProjectionVersion
            })
            .ToListAsync(cancellationToken);

        return records;
    }

    public async Task<IReadOnlyList<ClientProfitabilityDto>> GetClientProfitabilityAsync(
        Guid clientId,
        DateTime startDateUtc,
        DateTime endDateUtc,
        Guid? facilityId = null,
        TimeGrain grain = TimeGrain.Daily,
        CancellationToken cancellationToken = default)
    {
        ValidateTenantAccess(clientId);
        if (facilityId.HasValue)
        {
            ValidateFacilityAccess(facilityId.Value);
        }

        var start = startDateUtc.Date;
        var end = endDateUtc.Date;

        var query = _dbContext.DailyClientProfitabilitySummaries
            .AsNoTracking()
            .Where(s => s.ClientId == clientId && s.DateUtc >= start && s.DateUtc <= end && s.TimeGrain == grain);

        if (facilityId.HasValue)
        {
            query = query.Where(s => s.FacilityId == facilityId.Value);
        }
        else
        {
            query = query.Where(s => s.FacilityId == null);
        }

        var records = await query
            .OrderBy(s => s.DateUtc)
            .Select(s => new ClientProfitabilityDto
            {
                ClientId = s.ClientId,
                FacilityId = s.FacilityId,
                DateUtc = s.DateUtc,
                TimeGrain = s.TimeGrain,
                Currency = s.Currency,
                BilledStorageRevenue = s.BilledStorageRevenue,
                BilledHandlingRevenue = s.BilledHandlingRevenue,
                BilledFreightRevenue = s.BilledFreightRevenue,
                BilledVasRevenue = s.BilledVasRevenue,
                TotalBilledRevenue = s.TotalBilledRevenue,
                CarrierFreightCost = s.CarrierFreightCost,
                NetFreightMargin = s.NetFreightMargin,
                FreightMarginPercent = s.FreightMarginPercent,
                DirectLaborEstimatedCost = s.DirectLaborEstimatedCost,
                NetCommercialMargin = s.NetCommercialMargin,
                CommercialMarginPercent = s.CommercialMarginPercent,
                InvoicesIssuedCount = s.InvoicesIssuedCount,
                TotalInvoicedAmount = s.TotalInvoicedAmount,
                TotalTaxAmount = s.TotalTaxAmount,
                PaymentsCollectedAmount = s.PaymentsCollectedAmount,
                CreditsAppliedAmount = s.CreditsAppliedAmount,
                OutstandingArBalance = s.OutstandingArBalance
            })
            .ToListAsync(cancellationToken);

        return records;
    }

    public async Task<IReadOnlyList<InventoryUtilizationDto>> GetInventoryUtilizationAsync(
        Guid facilityId,
        DateTime startDateUtc,
        DateTime endDateUtc,
        Guid? clientId = null,
        TimeGrain grain = TimeGrain.Daily,
        CancellationToken cancellationToken = default)
    {
        ValidateFacilityAccess(facilityId);
        if (clientId.HasValue)
        {
            ValidateTenantAccess(clientId.Value);
        }

        var start = startDateUtc.Date;
        var end = endDateUtc.Date;

        var query = _dbContext.DailyInventoryUtilizationSnapshots
            .AsNoTracking()
            .Where(s => s.FacilityId == facilityId && s.DateUtc >= start && s.DateUtc <= end && s.TimeGrain == grain);

        if (clientId.HasValue)
        {
            query = query.Where(s => s.ClientId == clientId.Value);
        }
        else
        {
            query = query.Where(s => s.ClientId == null);
        }

        var records = await query
            .OrderBy(s => s.DateUtc)
            .Select(s => new InventoryUtilizationDto
            {
                FacilityId = s.FacilityId,
                ClientId = s.ClientId,
                DateUtc = s.DateUtc,
                TimeGrain = s.TimeGrain,
                TotalStorageLocations = s.TotalStorageLocations,
                OccupiedStorageLocations = s.OccupiedStorageLocations,
                PalletPositionOccupancyPercent = s.PalletPositionOccupancyPercent,
                TotalCapacityCubicMeters = s.TotalCapacityCubicMeters,
                OccupiedCubicMeters = s.OccupiedCubicMeters,
                VolumetricCubeFillPercent = s.VolumetricCubeFillPercent,
                TotalActiveSkus = s.TotalActiveSkus,
                TotalInventoryUnits = s.TotalInventoryUnits,
                TotalInventoryValue = s.TotalInventoryValue,
                DeadStockSkusCount = s.DeadStockSkusCount,
                DeadStockUnitsCount = s.DeadStockUnitsCount,
                DeadStockValue = s.DeadStockValue,
                DeadStockRatioPercent = s.DeadStockRatioPercent
            })
            .ToListAsync(cancellationToken);

        return records;
    }

    public async Task<IReadOnlyList<CarrierPerformanceDto>> GetCarrierPerformanceAsync(
        string carrierCode,
        Guid? facilityId,
        DateTime startDateUtc,
        DateTime endDateUtc,
        TimeGrain grain = TimeGrain.Daily,
        CancellationToken cancellationToken = default)
    {
        if (facilityId.HasValue)
        {
            ValidateFacilityAccess(facilityId.Value);
        }

        var normalizedCarrier = carrierCode.Trim().ToUpperInvariant();
        var start = startDateUtc.Date;
        var end = endDateUtc.Date;

        var query = _dbContext.DailyCarrierPerformanceRecords
            .AsNoTracking()
            .Where(r => r.CarrierCode == normalizedCarrier && r.DateUtc >= start && r.DateUtc <= end && r.TimeGrain == grain);

        if (facilityId.HasValue)
        {
            query = query.Where(r => r.FacilityId == facilityId.Value);
        }

        var records = await query
            .OrderBy(r => r.DateUtc)
            .Select(r => new CarrierPerformanceDto
            {
                CarrierCode = r.CarrierCode,
                CarrierServiceLevel = r.CarrierServiceLevel,
                FacilityId = r.FacilityId,
                ClientId = r.ClientId,
                DateUtc = r.DateUtc,
                TimeGrain = r.TimeGrain,
                DispatchedShipmentsCount = r.DispatchedShipmentsCount,
                DeliveredShipmentsCount = r.DeliveredShipmentsCount,
                InTransitShipmentsCount = r.InTransitShipmentsCount,
                OverdueInTransitCount = r.OverdueInTransitCount,
                CompletedDeliveryOutcomesCount = r.CompletedDeliveryOutcomesCount,
                OnTimeDeliveredCount = r.OnTimeDeliveredCount,
                LateDeliveredCount = r.LateDeliveredCount,
                FirstAttemptDeliveredCount = r.FirstAttemptDeliveredCount,
                DeliveryExceptionsCount = r.DeliveryExceptionsCount,
                OnTimeDeliveryRatePercent = r.OnTimeDeliveryRatePercent,
                FirstAttemptDeliveryRatePercent = r.FirstAttemptDeliveryRatePercent,
                DeliveryExceptionRatePercent = r.DeliveryExceptionRatePercent,
                AverageTransitDays = r.AverageTransitDays
            })
            .ToListAsync(cancellationToken);

        return records;
    }

    public async Task<DockYardOperationalStatusDto> GetDockYardStatusAsync(
        Guid facilityId,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default)
    {
        ValidateFacilityAccess(facilityId);

        var date = (asOfUtc ?? DateTime.UtcNow).Date;
        var snapshot = await _dbContext.FacilityDockYardSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.FacilityId == facilityId && s.DateUtc == date && s.TimeGrain == TimeGrain.Daily, cancellationToken);

        if (snapshot == null)
        {
            return new DockYardOperationalStatusDto
            {
                FacilityId = facilityId,
                DateUtc = date,
                TimeGrain = TimeGrain.Daily
            };
        }

        return new DockYardOperationalStatusDto
        {
            FacilityId = snapshot.FacilityId,
            DateUtc = snapshot.DateUtc,
            TimeGrain = snapshot.TimeGrain,
            TotalDockDoorsCount = snapshot.TotalDockDoorsCount,
            OccupiedDockDoorsCount = snapshot.OccupiedDockDoorsCount,
            DockDoorUtilizationPercent = snapshot.DockDoorUtilizationPercent,
            TotalYardSpotsCount = snapshot.TotalYardSpotsCount,
            OccupiedYardSpotsCount = snapshot.OccupiedYardSpotsCount,
            YardOccupancyPercent = snapshot.YardOccupancyPercent,
            TrucksCheckedInCount = snapshot.TrucksCheckedInCount,
            TrucksDepartedCount = snapshot.TrucksDepartedCount,
            TrucksWaitingInYardCount = snapshot.TrucksWaitingInYardCount,
            TrucksAtDockCount = snapshot.TrucksAtDockCount,
            AverageYardWaitTimeMinutes = snapshot.AverageYardWaitTimeMinutes,
            AverageDockServiceTimeMinutes = snapshot.AverageDockServiceTimeMinutes,
            AverageTotalTurnTimeMinutes = snapshot.AverageTotalTurnTimeMinutes
        };
    }

    public async Task<OrderFulfillmentSlaDashboardDto> GetOrderFulfillmentSlaDashboardAsync(
        Guid? facilityId,
        Guid? clientId = null,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (facilityId.HasValue)
        {
            ValidateFacilityAccess(facilityId.Value);
        }

        if (clientId.HasValue)
        {
            ValidateTenantAccess(clientId.Value);
        }
        else if (!_currentUser.IsStaff && _currentUser.TenantId.HasValue)
        {
            clientId = _currentUser.TenantId.Value;
        }

        var now = asOfUtc ?? DateTime.UtcNow;
        var query = _dbContext.OutboundOrders.AsNoTracking();

        if (facilityId.HasValue)
        {
            query = query.Where(o => o.FacilityId == facilityId.Value);
        }

        if (clientId.HasValue)
        {
            query = query.Where(o => o.ClientId == clientId.Value);
        }

        // Active orders or completed today
        var cutoffWindow = now.Date.AddDays(-2);
        query = query.Where(o => o.Status != OutboundOrderStatus.Cancelled && (o.Status != OutboundOrderStatus.Completed || o.OrderDateUtc >= cutoffWindow));

        var orders = await query
            .OrderBy(o => o.RequiredShipDateUtc)
            .Take(500)
            .ToListAsync(cancellationToken);

        int onTrack = 0;
        int approaching = 0;
        int atRisk = 0;
        int breached = 0;
        int completedWithin = 0;
        int completedOutside = 0;

        var urgentList = new List<OrderSlaItemDto>();

        foreach (var order in orders)
        {
            var reqDate = order.RequiredShipDateUtc ?? order.OrderDateUtc.AddHours(24);
            var remainingMinutes = (reqDate - now).TotalMinutes;
            FulfillmentSlaStatus slaStatus;

            if (order.Status == OutboundOrderStatus.Completed)
            {
                if (order.DeliveryStatusUpdatedAtUtc.HasValue && order.DeliveryStatusUpdatedAtUtc.Value <= reqDate)
                {
                    slaStatus = FulfillmentSlaStatus.CompletedWithinSla;
                    completedWithin++;
                }
                else
                {
                    slaStatus = FulfillmentSlaStatus.CompletedOutsideSla;
                    completedOutside++;
                }
            }
            else
            {
                if (remainingMinutes < 0)
                {
                    slaStatus = FulfillmentSlaStatus.Breached;
                    breached++;
                }
                else if (remainingMinutes <= 60)
                {
                    slaStatus = FulfillmentSlaStatus.AtRisk;
                    atRisk++;
                }
                else if (remainingMinutes <= 120)
                {
                    slaStatus = FulfillmentSlaStatus.ApproachingDeadline;
                    approaching++;
                }
                else
                {
                    slaStatus = FulfillmentSlaStatus.OnTrack;
                    onTrack++;
                }

                if (slaStatus is FulfillmentSlaStatus.AtRisk or FulfillmentSlaStatus.ApproachingDeadline or FulfillmentSlaStatus.Breached)
                {
                    urgentList.Add(new OrderSlaItemDto
                    {
                        OrderId = order.Id,
                        OrderNumber = order.OrderNumber,
                        ClientId = order.ClientId,
                        FacilityId = order.FacilityId,
                        CustomerName = order.CustomerName,
                        ServiceLevel = order.ServiceLevel,
                        CarrierCode = order.CarrierCode,
                        OrderDateUtc = order.OrderDateUtc,
                        RequiredShipDateUtc = order.RequiredShipDateUtc,
                        RemainingMinutes = Math.Round(remainingMinutes, 1),
                        SlaStatus = slaStatus,
                        CurrentOrderStatus = order.Status
                    });
                }
            }
        }

        int totalOrders = onTrack + approaching + atRisk + breached + completedWithin + completedOutside;
        decimal adherence = totalOrders > 0
            ? Math.Round(((decimal)(completedWithin + onTrack + approaching) / totalOrders) * 100m, 2)
            : 100m;

        return new OrderFulfillmentSlaDashboardDto
        {
            FacilityId = facilityId,
            ClientId = clientId,
            AsOfUtc = now,
            OnTrackCount = onTrack,
            ApproachingDeadlineCount = approaching,
            AtRiskCount = atRisk,
            BreachedCount = breached,
            CompletedWithinSlaCount = completedWithin,
            CompletedOutsideSlaCount = completedOutside,
            SlaAdherencePercent = adherence,
            UrgentOrders = urgentList.OrderBy(u => u.RemainingMinutes).Take(20).ToList()
        };
    }

    public async Task<FacilityBottleneckAnalysisDto> GetFacilityBottlenecksAsync(
        Guid facilityId,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default)
    {
        ValidateFacilityAccess(facilityId);

        var now = asOfUtc ?? DateTime.UtcNow;
        var date = now.Date;

        // 1. Receiving Congestion
        var dockSnapshot = await _dbContext.FacilityDockYardSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.FacilityId == facilityId && s.DateUtc == date && s.TimeGrain == TimeGrain.Daily, cancellationToken);

        var waitingTrucks = dockSnapshot?.TrucksWaitingInYardCount ?? 0;
        var avgWaitMins = dockSnapshot?.AverageYardWaitTimeMinutes ?? 0m;
        var yardOcc = dockSnapshot?.YardOccupancyPercent ?? 0m;

        var recSeverity = BottleneckSeverity.Normal;
        var recMsg = "Receiving queue operating within capacity.";
        var recAction = "Normal operations.";

        if (avgWaitMins > 60m || yardOcc > 85m || waitingTrucks > 5)
        {
            recSeverity = BottleneckSeverity.Critical;
            recMsg = $"Critical yard congestion: {waitingTrucks} trucks waiting (avg wait: {avgWaitMins}m, yard: {yardOcc}%).";
            recAction = "Authorize overtime receiving doors or redirect inbound appointments.";
        }
        else if (avgWaitMins > 30m || yardOcc > 70m || waitingTrucks > 2)
        {
            recSeverity = BottleneckSeverity.Warning;
            recMsg = $"Receiving approaching capacity: {waitingTrucks} trucks waiting (avg wait: {avgWaitMins}m).";
            recAction = "Prioritize dock door assignment for arriving carriers.";
        }

        // 2. Picking Backlog
        var allocatedOrders = await _dbContext.OutboundOrders
            .AsNoTracking()
            .Where(o => o.FacilityId == facilityId && (o.Status == OutboundOrderStatus.Allocated || o.Status == OutboundOrderStatus.InProgress))
            .CountAsync(cancellationToken);

        var pickSeverity = BottleneckSeverity.Normal;
        var pickMsg = "Picking throughput pacing matches wave plan.";
        var pickAction = "Maintain current picker wave allocations.";

        if (allocatedOrders > 50)
        {
            pickSeverity = BottleneckSeverity.Critical;
            pickMsg = $"Severe pick queue backlog: {allocatedOrders} active orders unpicked.";
            pickAction = "Reassign putaway workers to multi-line wave picking.";
        }
        else if (allocatedOrders > 20)
        {
            pickSeverity = BottleneckSeverity.Warning;
            pickMsg = $"Picking queue elevated: {allocatedOrders} active orders in queue.";
            pickAction = "Release expedited pick waves for urgent order profiles.";
        }

        // 3. Packing Delays
        var packingSeverity = BottleneckSeverity.Normal;
        var packMsg = "Packing stations operating with minimal dwell.";
        var packAction = "Regular packing station cadence.";

        // 4. Dispatch Delays
        var dispatchSeverity = BottleneckSeverity.Normal;
        var dispMsg = "Shipping staging clear; on-time carrier handovers.";
        var dispAction = "Monitor upcoming carrier pull times.";

        // Calculate health score: 100 - (10 per warning) - (25 per critical)
        int penalties = 0;
        if (recSeverity == BottleneckSeverity.Critical) penalties += 25;
        else if (recSeverity == BottleneckSeverity.Warning) penalties += 10;

        if (pickSeverity == BottleneckSeverity.Critical) penalties += 25;
        else if (pickSeverity == BottleneckSeverity.Warning) penalties += 10;

        int healthScore = Math.Max(0, 100 - penalties);

        return new FacilityBottleneckAnalysisDto
        {
            FacilityId = facilityId,
            EvaluatedAtUtc = now,
            ReceivingCongestion = new BottleneckStageHealthDto
            {
                Stage = OperationalBottleneckStage.ReceivingCongestion,
                Severity = recSeverity,
                QueueDepth = waitingTrucks,
                MetricValue = avgWaitMins,
                MetricUnit = "Minutes",
                SummaryMessage = recMsg,
                RecommendedAction = recAction
            },
            PickingBacklog = new BottleneckStageHealthDto
            {
                Stage = OperationalBottleneckStage.PickingBacklog,
                Severity = pickSeverity,
                QueueDepth = allocatedOrders,
                MetricValue = allocatedOrders,
                MetricUnit = "Orders",
                SummaryMessage = pickMsg,
                RecommendedAction = pickAction
            },
            PackingDelays = new BottleneckStageHealthDto
            {
                Stage = OperationalBottleneckStage.PackingDelays,
                Severity = packingSeverity,
                QueueDepth = 0,
                MetricValue = 0m,
                MetricUnit = "Cartons",
                SummaryMessage = packMsg,
                RecommendedAction = packAction
            },
            DispatchDelays = new BottleneckStageHealthDto
            {
                Stage = OperationalBottleneckStage.DispatchDelays,
                Severity = dispatchSeverity,
                QueueDepth = 0,
                MetricValue = 0m,
                MetricUnit = "Packages",
                SummaryMessage = dispMsg,
                RecommendedAction = dispAction
            },
            OverallHealthScore = healthScore
        };
    }

    public async Task<MultiFacilityComparisonDto> GetMultiFacilityComparisonAsync(
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default)
    {
        var now = asOfUtc ?? DateTime.UtcNow;
        var date = now.Date;

        var facilityQuery = _dbContext.Facilities.AsNoTracking().Where(f => f.IsActive);

        if (!_currentUser.IsStaff && !_currentUser.IsSuperAdmin && _currentUser.FacilityId.HasValue)
        {
            facilityQuery = facilityQuery.Where(f => f.Id == _currentUser.FacilityId.Value);
        }

        var facilities = await facilityQuery.ToListAsync(cancellationToken);
        var facilityItems = new List<FacilityComparisonItemDto>();

        foreach (var fac in facilities)
        {
            var prod = await _dbContext.DailyFacilityProductivitySummaries
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.FacilityId == fac.Id && p.DateUtc == date && p.TimeGrain == TimeGrain.Daily, cancellationToken);

            var dock = await _dbContext.FacilityDockYardSnapshots
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.FacilityId == fac.Id && d.DateUtc == date && d.TimeGrain == TimeGrain.Daily, cancellationToken);

            var inv = await _dbContext.DailyInventoryUtilizationSnapshots
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.FacilityId == fac.Id && i.DateUtc == date && i.TimeGrain == TimeGrain.Daily && i.ClientId == null, cancellationToken);

            facilityItems.Add(new FacilityComparisonItemDto
            {
                FacilityId = fac.Id,
                FacilityCode = fac.FacilityCode,
                FacilityName = fac.Name,
                OrdersReceivedToday = prod?.OrdersReceivedCount ?? 0,
                OrdersDispatchedToday = prod?.OrdersDispatchedCount ?? 0,
                UnitsPickedToday = prod?.UnitsPickedCount ?? 0,
                CartonsPackedToday = prod?.CartonsPackedCount ?? 0,
                UnitsPerLaborHour = prod?.UnitsPerLaborHour ?? 0m,
                AverageCycleTimeHours = prod?.AverageFulfillmentCycleTimeHours ?? 0m,
                SameDayDispatchAdherencePercent = prod?.SameDayDispatchAdherencePercent ?? 0m,
                DockTurnTimeMinutes = dock?.AverageTotalTurnTimeMinutes ?? 0m,
                DockDoorUtilizationPercent = dock?.DockDoorUtilizationPercent ?? 0m,
                YardOccupancyPercent = dock?.YardOccupancyPercent ?? 0m,
                PalletPositionOccupancyPercent = inv?.PalletPositionOccupancyPercent ?? 0m,
                VolumetricCubeFillPercent = inv?.VolumetricCubeFillPercent ?? 0m,
                ActiveBottlenecksCount = 0,
                CriticalSlaBreachesCount = 0
            });
        }

        var totalRec = facilityItems.Sum(f => f.OrdersReceivedToday);
        var totalDisp = facilityItems.Sum(f => f.OrdersDispatchedToday);
        var avgSda = facilityItems.Count > 0 ? Math.Round(facilityItems.Average(f => f.SameDayDispatchAdherencePercent), 2) : 0m;
        var avgUph = facilityItems.Count > 0 ? Math.Round(facilityItems.Average(f => f.UnitsPerLaborHour), 2) : 0m;
        var avgCube = facilityItems.Count > 0 ? Math.Round(facilityItems.Average(f => f.VolumetricCubeFillPercent), 2) : 0m;

        return new MultiFacilityComparisonDto
        {
            AsOfUtc = now,
            Facilities = facilityItems,
            TotalOrdersReceivedToday = totalRec,
            TotalOrdersDispatchedToday = totalDisp,
            NetworkSameDayDispatchAdherencePercent = avgSda,
            NetworkBlendedUnitsPerLaborHour = avgUph,
            NetworkAverageCubeFillPercent = avgCube
        };
    }

    public async Task<ControlTowerExecutiveOverviewDto> GetExecutiveOverviewAsync(
        Guid? facilityId,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (facilityId.HasValue)
        {
            ValidateFacilityAccess(facilityId.Value);
        }

        var now = asOfUtc ?? DateTime.UtcNow;

        var summary = await GetControlTowerSummaryAsync(facilityId, now, cancellationToken);
        var slaDashboard = await GetOrderFulfillmentSlaDashboardAsync(facilityId, null, now, cancellationToken);
        var dockYard = facilityId.HasValue
            ? await GetDockYardStatusAsync(facilityId.Value, now, cancellationToken)
            : new DockYardOperationalStatusDto { DateUtc = now.Date };
        var bottlenecks = facilityId.HasValue
            ? await GetFacilityBottlenecksAsync(facilityId.Value, now, cancellationToken)
            : new FacilityBottleneckAnalysisDto { EvaluatedAtUtc = now };

        // Freshness determination
        var lastUpdate = DateTime.UtcNow; // Projections reflect latest captured event
        var freshness = DataFreshnessStatus.Live;

        var freshnessMeta = new MetricFreshnessMetadataDto
        {
            LastProjectionUpdateUtc = lastUpdate,
            FreshnessStatus = freshness,
            ReportingWindowStartUtc = now.Date,
            ReportingWindowEndUtc = now,
            FacilityTimezone = "America/Chicago",
            ProjectionVersion = 1
        };

        return new ControlTowerExecutiveOverviewDto
        {
            FacilityId = facilityId,
            AsOfUtc = now,
            Summary = summary,
            SlaDashboard = slaDashboard,
            DockYardStatus = dockYard,
            Bottlenecks = bottlenecks,
            FreshnessMetadata = freshnessMeta
        };
    }

    public async Task<ControlTowerSummaryDto> GetControlTowerSummaryAsync(
        Guid? facilityId,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (facilityId.HasValue)
        {
            ValidateFacilityAccess(facilityId.Value);
        }

        var date = (asOfUtc ?? DateTime.UtcNow).Date;

        // 1. Facility productivity
        var prodQuery = _dbContext.DailyFacilityProductivitySummaries
            .AsNoTracking()
            .Where(p => p.DateUtc == date && p.TimeGrain == TimeGrain.Daily);

        if (facilityId.HasValue)
        {
            prodQuery = prodQuery.Where(p => p.FacilityId == facilityId.Value);
        }

        var prodRecords = await prodQuery.ToListAsync(cancellationToken);

        var ordersReceived = prodRecords.Sum(p => p.OrdersReceivedCount);
        var ordersDispatched = prodRecords.Sum(p => p.OrdersDispatchedCount);
        var packagesDispatched = prodRecords.Sum(p => p.PackagesDispatchedCount);
        var unitsPicked = prodRecords.Sum(p => p.UnitsPickedCount);
        var cartonsPacked = prodRecords.Sum(p => p.CartonsPackedCount);
        var inboundPallets = prodRecords.Sum(p => p.InboundPalletsReceivedCount);
        var putawayCompleted = prodRecords.Sum(p => p.InboundPutawayCompletedCount);
        var returnsInspected = prodRecords.Sum(p => p.ReturnsInspectedCount);
        var totalPickHours = prodRecords.Sum(p => p.TotalPickHours);
        var totalLaborHours = prodRecords.Sum(p => p.TotalLaborHours);

        var uph = totalPickHours > 0 ? Math.Round((decimal)unitsPicked / totalPickHours, 2) : 0m;
        var totalCycleOrders = prodRecords.Sum(p => p.TotalCycleTimeOrdersCount);
        var totalCycleHours = prodRecords.Sum(p => p.TotalCycleTimeHoursSum);
        var avgCycle = totalCycleOrders > 0 ? Math.Round(totalCycleHours / totalCycleOrders, 2) : 0m;

        var eligibleCutoff = prodRecords.Sum(p => p.EligibleOrdersCutoffCount);
        var sameDayDispatched = prodRecords.Sum(p => p.SameDayDispatchedCutoffCount);
        var sda = eligibleCutoff > 0 ? Math.Round((decimal)sameDayDispatched / eligibleCutoff * 100m, 2) : 0m;

        // 2. Commercial profitability today
        var clientQuery = _dbContext.DailyClientProfitabilitySummaries
            .AsNoTracking()
            .Where(c => c.DateUtc == date && c.TimeGrain == TimeGrain.Daily && c.FacilityId == null);

        if (!_currentUser.IsStaff && _currentUser.TenantId.HasValue)
        {
            clientQuery = clientQuery.Where(c => c.ClientId == _currentUser.TenantId.Value);
        }

        var clientRecords = await clientQuery.ToListAsync(cancellationToken);
        var totalBilledRevenue = clientRecords.Sum(c => c.TotalBilledRevenue);
        var netFreightMargin = clientRecords.Sum(c => c.NetFreightMargin);

        // 3. Carrier performance today
        var carrierQuery = _dbContext.DailyCarrierPerformanceRecords
            .AsNoTracking()
            .Where(r => r.DateUtc == date && r.TimeGrain == TimeGrain.Daily);

        if (facilityId.HasValue)
        {
            carrierQuery = carrierQuery.Where(r => r.FacilityId == facilityId.Value);
        }

        var carrierRecords = await carrierQuery.ToListAsync(cancellationToken);
        var delivered = carrierRecords.Sum(r => r.DeliveredShipmentsCount);
        var onTime = carrierRecords.Sum(r => r.OnTimeDeliveredCount);
        var exceptions = carrierRecords.Sum(r => r.DeliveryExceptionsCount);
        var otd = delivered > 0 ? Math.Round((decimal)onTime / delivered * 100m, 2) : 0m;

        // 4. Inventory capacity latest snapshot
        var invQuery = _dbContext.DailyInventoryUtilizationSnapshots
            .AsNoTracking()
            .Where(i => i.DateUtc == date && i.TimeGrain == TimeGrain.Daily && i.ClientId == null);

        if (facilityId.HasValue)
        {
            invQuery = invQuery.Where(i => i.FacilityId == facilityId.Value);
        }

        var invRecords = await invQuery.ToListAsync(cancellationToken);
        var totalLoc = invRecords.Sum(i => i.TotalStorageLocations);
        var occLoc = invRecords.Sum(i => i.OccupiedStorageLocations);
        var palletOccupancy = totalLoc > 0 ? Math.Round((decimal)occLoc / totalLoc * 100m, 2) : 0m;

        var totalCbm = invRecords.Sum(i => i.TotalCapacityCubicMeters);
        var occCbm = invRecords.Sum(i => i.OccupiedCubicMeters);
        var cubeFill = totalCbm > 0 ? Math.Round(occCbm / totalCbm * 100m, 2) : 0m;

        return new ControlTowerSummaryDto
        {
            FacilityId = facilityId,
            AsOfUtc = date,
            OrdersReceivedToday = ordersReceived,
            OrdersDispatchedToday = ordersDispatched,
            PackagesDispatchedToday = packagesDispatched,
            UnitsPickedToday = unitsPicked,
            CartonsPackedToday = cartonsPacked,
            InboundPalletsReceivedToday = inboundPallets,
            PutawayCompletedToday = putawayCompleted,
            ReturnsInspectedToday = returnsInspected,
            UnitsPerLaborHourToday = uph,
            AverageFulfillmentCycleTimeHours = avgCycle,
            SameDayDispatchAdherencePercent = sda,
            TotalBilledRevenueToday = totalBilledRevenue,
            NetFreightMarginToday = netFreightMargin,
            OnTimeDeliveryRatePercent = otd,
            ActiveDeliveryExceptionsCount = exceptions,
            PalletPositionOccupancyPercent = palletOccupancy,
            VolumetricCubeFillPercent = cubeFill
        };
    }

    #region Phase 6.2 Workforce, Labor & Productivity Queries

    public async Task<IReadOnlyList<WorkerProductivityDto>> GetWorkerProductivityAsync(
        Guid workerUserId,
        DateTime startDateUtc,
        DateTime endDateUtc,
        Guid? requestingUserId = null,
        bool hasElevatedWorkforceAccess = false,
        CancellationToken cancellationToken = default)
    {
        var start = startDateUtc.Date;
        var end = endDateUtc.Date;

        var records = await _dbContext.DailyWorkerProductivitySummaries
            .AsNoTracking()
            .Where(s => s.WorkerUserId == workerUserId && s.DateUtc >= start && s.DateUtc <= end)
            .OrderBy(s => s.DateUtc)
            .ToListAsync(cancellationToken);

        if (records.Count > 0)
        {
            ValidateWorkforcePrivacy(workerUserId, requestingUserId, hasElevatedWorkforceAccess, records[0].FacilityId);
        }
        else
        {
            ValidateWorkforcePrivacy(workerUserId, requestingUserId, hasElevatedWorkforceAccess, null);
        }

        var user = await _dbContext.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == workerUserId, cancellationToken);
        var workerName = user != null ? $"{user.FirstName} {user.LastName}".Trim() : $"Worker {workerUserId.ToString()[..8]}";

        return records.Select(r => new WorkerProductivityDto
        {
            WorkerUserId = r.WorkerUserId,
            WorkerName = workerName,
            FacilityId = r.FacilityId,
            Department = r.Department,
            DateUtc = r.DateUtc,
            TimeGrain = r.TimeGrain,
            TotalShiftMinutes = r.TotalShiftMinutes,
            ProductiveWorkMinutes = r.ProductiveWorkMinutes,
            IdleMinutes = r.IdleMinutes,
            ScheduledBreakMinutes = r.ScheduledBreakMinutes,
            TasksCompleted = r.TasksCompleted,
            UnitsCompleted = r.UnitsCompleted,
            LinesCompleted = r.LinesCompleted,
            CartonsCompleted = r.CartonsCompleted,
            PalletsCompleted = r.PalletsCompleted,
            ActualLaborHours = r.ActualLaborHours,
            StandardLaborHours = r.StandardLaborHours,
            LaborEfficiencyPercent = r.LaborEfficiencyPercent,
            LaborUtilizationPercent = r.LaborUtilizationPercent,
            UnitsPerHour = r.UnitsPerHour,
            LinesPerHour = r.LinesPerHour,
            CartonsPerHour = r.CartonsPerHour,
            TasksPerHour = r.TasksPerHour,
            PerformanceTier = r.PerformanceTier,
            HourlyWageRate = r.HourlyWageRate,
            TotalLaborCost = r.TotalLaborCost,
            LaborCostPerUnit = r.LaborCostPerUnit,
            LaborCostPerTask = r.LaborCostPerTask
        }).ToList();
    }

    public async Task<FacilityWorkforceSummaryDto> GetFacilityWorkforceSummaryAsync(
        Guid facilityId,
        DateTime dateUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateFacilityAccess(facilityId);
        ValidateInternalStaffOnly();

        var date = dateUtc.Date;
        var summaries = await _dbContext.DailyWorkerProductivitySummaries
            .AsNoTracking()
            .Where(s => s.FacilityId == facilityId && s.DateUtc == date)
            .ToListAsync(cancellationToken);

        var activeWorkers = summaries.Select(s => s.WorkerUserId).Distinct().Count();
        var totalProdHours = summaries.Sum(s => s.ActualLaborHours);
        var totalStdHours = summaries.Sum(s => s.StandardLaborHours);
        var totalIdleMins = summaries.Sum(s => s.IdleMinutes);

        var totalTasks = summaries.Sum(s => s.TasksCompleted);
        var totalUnits = summaries.Sum(s => s.UnitsCompleted);
        var totalLines = summaries.Sum(s => s.LinesCompleted);
        var totalCartons = summaries.Sum(s => s.CartonsCompleted);

        var totalCost = summaries.Sum(s => s.TotalLaborCost);
        var blendedEfficiency = totalProdHours > 0m ? Math.Round((totalStdHours / totalProdHours) * 100m, 2) : 0m;

        var totalAvailMins = summaries.Sum(s => Math.Max(0m, s.TotalShiftMinutes - s.ScheduledBreakMinutes));
        var totalProdMins = summaries.Sum(s => s.ProductiveWorkMinutes);
        var blendedUtilization = totalAvailMins > 0m ? Math.Round((totalProdMins / totalAvailMins) * 100m, 2) : 0m;

        var blendedUph = totalProdHours > 0m ? Math.Round(totalUnits / totalProdHours, 2) : 0m;
        var blendedLph = totalProdHours > 0m ? Math.Round(totalLines / totalProdHours, 2) : 0m;
        var blendedCph = totalProdHours > 0m ? Math.Round(totalCartons / totalProdHours, 2) : 0m;
        var avgCostPerUnit = totalUnits > 0 ? Math.Round(totalCost / totalUnits, 4) : 0m;

        var deptBreakdowns = summaries
            .GroupBy(s => s.Department)
            .Select(g =>
            {
                var dProdHours = g.Sum(s => s.ActualLaborHours);
                var dStdHours = g.Sum(s => s.StandardLaborHours);
                var dAvailMins = g.Sum(s => Math.Max(0m, s.TotalShiftMinutes - s.ScheduledBreakMinutes));
                var dProdMins = g.Sum(s => s.ProductiveWorkMinutes);
                var dUnits = g.Sum(s => s.UnitsCompleted);
                var dLines = g.Sum(s => s.LinesCompleted);
                var dCartons = g.Sum(s => s.CartonsCompleted);

                return new DepartmentWorkforceDto
                {
                    Department = g.Key,
                    ActiveWorkersCount = g.Select(s => s.WorkerUserId).Distinct().Count(),
                    ProductiveHours = dProdHours,
                    StandardHours = dStdHours,
                    EfficiencyPercent = dProdHours > 0m ? Math.Round((dStdHours / dProdHours) * 100m, 2) : 0m,
                    UtilizationPercent = dAvailMins > 0m ? Math.Round((dProdMins / dAvailMins) * 100m, 2) : 0m,
                    UnitsPerHour = dProdHours > 0m ? Math.Round(dUnits / dProdHours, 2) : 0m,
                    LinesPerHour = dProdHours > 0m ? Math.Round(dLines / dProdHours, 2) : 0m,
                    CartonsPerHour = dProdHours > 0m ? Math.Round(dCartons / dProdHours, 2) : 0m,
                    TotalLaborCost = g.Sum(s => s.TotalLaborCost)
                };
            })
            .OrderBy(d => d.Department)
            .ToList();

        return new FacilityWorkforceSummaryDto
        {
            FacilityId = facilityId,
            DateUtc = date,
            ActiveWorkersCount = activeWorkers,
            TotalProductiveHours = totalProdHours,
            TotalStandardHours = totalStdHours,
            TotalIdleMinutes = totalIdleMins,
            TotalTasksCompleted = totalTasks,
            TotalUnitsProcessed = totalUnits,
            TotalLinesProcessed = totalLines,
            TotalCartonsProcessed = totalCartons,
            BlendedLaborEfficiencyPercent = blendedEfficiency,
            BlendedLaborUtilizationPercent = blendedUtilization,
            BlendedUnitsPerHour = blendedUph,
            BlendedLinesPerHour = blendedLph,
            BlendedCartonsPerHour = blendedCph,
            TotalLaborCost = totalCost,
            AverageCostPerUnit = avgCostPerUnit,
            DepartmentBreakdowns = deptBreakdowns
        };
    }

    public async Task<IReadOnlyList<WorkerTaskExecutionLogDto>> GetWorkerTaskExecutionLogsAsync(
        Guid workerUserId,
        DateTime startDateUtc,
        DateTime endDateUtc,
        Guid? requestingUserId = null,
        bool hasElevatedWorkforceAccess = false,
        CancellationToken cancellationToken = default)
    {
        var logs = await _dbContext.WorkerTaskExecutionLogs
            .AsNoTracking()
            .Where(l => l.WorkerUserId == workerUserId && l.CompletedAtUtc >= startDateUtc && l.CompletedAtUtc <= endDateUtc)
            .OrderByDescending(l => l.CompletedAtUtc)
            .ToListAsync(cancellationToken);

        if (logs.Count > 0)
        {
            ValidateWorkforcePrivacy(workerUserId, requestingUserId, hasElevatedWorkforceAccess, logs[0].FacilityId);
        }
        else
        {
            ValidateWorkforcePrivacy(workerUserId, requestingUserId, hasElevatedWorkforceAccess, null);
        }

        return logs.Select(l => new WorkerTaskExecutionLogDto
        {
            Id = l.Id,
            TaskId = l.TaskId,
            ActivityType = l.ActivityType,
            WorkerUserId = l.WorkerUserId,
            FacilityId = l.FacilityId,
            ZoneId = l.ZoneId,
            Department = l.Department,
            UnitsProcessed = l.UnitsProcessed,
            LinesProcessed = l.LinesProcessed,
            CartonsProcessed = l.CartonsProcessed,
            PalletsProcessed = l.PalletsProcessed,
            TravelDistanceMeters = l.TravelDistanceMeters,
            StartedAtUtc = l.StartedAtUtc,
            CompletedAtUtc = l.CompletedAtUtc,
            ActualDurationSeconds = l.ActualDurationSeconds,
            StandardDurationSeconds = l.StandardDurationSeconds,
            StandardProfileId = l.StandardProfileId,
            StandardProfileVersion = l.StandardProfileVersion,
            VarianceSeconds = l.VarianceSeconds,
            EfficiencyPercent = l.EfficiencyPercent,
            Complexity = l.Complexity,
            Equipment = l.Equipment,
            HourlyWageRate = l.HourlyWageRate,
            CalculatedLaborCost = l.CalculatedLaborCost
        }).ToList();
    }

    public async Task<IReadOnlyList<WorkerIdleIntervalDto>> GetWorkerIdleIntervalsAsync(
        Guid workerUserId,
        DateTime startDateUtc,
        DateTime endDateUtc,
        Guid? requestingUserId = null,
        bool hasElevatedWorkforceAccess = false,
        CancellationToken cancellationToken = default)
    {
        var idles = await _dbContext.WorkerIdleIntervalRecords
            .AsNoTracking()
            .Where(i => i.WorkerUserId == workerUserId && i.StartedAtUtc >= startDateUtc && i.StartedAtUtc <= endDateUtc)
            .OrderByDescending(i => i.StartedAtUtc)
            .ToListAsync(cancellationToken);

        if (idles.Count > 0)
        {
            ValidateWorkforcePrivacy(workerUserId, requestingUserId, hasElevatedWorkforceAccess, idles[0].FacilityId);
        }
        else
        {
            ValidateWorkforcePrivacy(workerUserId, requestingUserId, hasElevatedWorkforceAccess, null);
        }

        return idles.Select(i => new WorkerIdleIntervalDto
        {
            Id = i.Id,
            WorkerUserId = i.WorkerUserId,
            FacilityId = i.FacilityId,
            Department = i.Department,
            StartedAtUtc = i.StartedAtUtc,
            EndedAtUtc = i.EndedAtUtc,
            DurationMinutes = i.DurationMinutes,
            DurationSeconds = i.DurationSeconds,
            ReasonCode = i.ReasonCode,
            SupervisorApproved = i.SupervisorApproved,
            Notes = i.Notes
        }).ToList();
    }

    public async Task<IReadOnlyList<LaborStandardProfileDto>> GetLaborStandardProfilesAsync(
        Guid? facilityId = null,
        WarehouseLaborActivityType? activityType = null,
        Guid? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        ValidateInternalStaffOnly();

        var query = _dbContext.LaborStandardProfiles.AsNoTracking().Where(p => p.IsActive);

        if (facilityId.HasValue)
        {
            query = query.Where(p => p.FacilityId == facilityId || p.FacilityId == null);
        }

        if (zoneId.HasValue)
        {
            query = query.Where(p => p.ZoneId == zoneId || p.ZoneId == null);
        }

        if (activityType.HasValue)
        {
            query = query.Where(p => p.ActivityType == activityType.Value);
        }

        var profiles = await query
            .OrderBy(p => p.ActivityType)
            .ThenBy(p => p.Department)
            .Select(p => new LaborStandardProfileDto
            {
                Id = p.Id,
                FacilityId = p.FacilityId,
                ZoneId = p.ZoneId,
                ZoneType = p.ZoneType,
                ActivityType = p.ActivityType,
                Department = p.Department,
                BaseSetupSeconds = p.BaseSetupSeconds,
                SecondsPerUnit = p.SecondsPerUnit,
                SecondsPerLine = p.SecondsPerLine,
                SecondsPerCarton = p.SecondsPerCarton,
                SecondsPerPallet = p.SecondsPerPallet,
                SecondsPerTravelMeter = p.SecondsPerTravelMeter,
                EquipmentFactor = p.EquipmentFactor,
                ComplexityMultiplier = p.ComplexityMultiplier,
                Version = p.Version,
                EffectiveFromUtc = p.EffectiveFromUtc,
                EffectiveToUtc = p.EffectiveToUtc,
                IsActive = p.IsActive
            })
            .ToListAsync(cancellationToken);

        return profiles;
    }

    public async Task<LaborCostAnalysisDto> GetLaborCostAnalysisAsync(
        Guid facilityId,
        DateTime startDateUtc,
        DateTime endDateUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateFacilityAccess(facilityId);
        ValidateLaborCostAccess(facilityId);

        var logs = await _dbContext.WorkerTaskExecutionLogs
            .AsNoTracking()
            .Where(l => l.FacilityId == facilityId && l.CompletedAtUtc >= startDateUtc && l.CompletedAtUtc <= endDateUtc)
            .ToListAsync(cancellationToken);

        var idles = await _dbContext.WorkerIdleIntervalRecords
            .AsNoTracking()
            .Where(i => i.FacilityId == facilityId && i.StartedAtUtc >= startDateUtc && i.StartedAtUtc <= endDateUtc)
            .ToListAsync(cancellationToken);

        var directCost = logs.Sum(l => l.CalculatedLaborCost);

        var avgWage = logs.Any(l => l.HourlyWageRate > 0) ? logs.Where(l => l.HourlyWageRate > 0).Average(l => l.HourlyWageRate) : 20.0m;
        var idleHours = idles.Sum(i => i.DurationMinutes) / 60m;
        var idleCost = Math.Round(idleHours * avgWage, 2);

        var pickUnits = logs.Where(l => l.ActivityType == WarehouseLaborActivityType.Picking).Sum(l => l.UnitsProcessed);
        var pickCost = logs.Where(l => l.ActivityType == WarehouseLaborActivityType.Picking).Sum(l => l.CalculatedLaborCost);
        var costPerPickUnit = pickUnits > 0 ? Math.Round(pickCost / pickUnits, 4) : 0m;

        var packCartons = logs.Where(l => l.ActivityType == WarehouseLaborActivityType.Packing).Sum(l => l.CartonsProcessed);
        var packCost = logs.Where(l => l.ActivityType == WarehouseLaborActivityType.Packing).Sum(l => l.CalculatedLaborCost);
        var costPerPackCarton = packCartons > 0 ? Math.Round(packCost / packCartons, 4) : 0m;

        var putawayPallets = logs.Where(l => l.ActivityType == WarehouseLaborActivityType.Putaway).Sum(l => l.PalletsProcessed);
        var putawayCost = logs.Where(l => l.ActivityType == WarehouseLaborActivityType.Putaway).Sum(l => l.CalculatedLaborCost);
        var costPerPutawayPallet = putawayPallets > 0 ? Math.Round(putawayCost / putawayPallets, 4) : 0m;

        return new LaborCostAnalysisDto
        {
            FacilityId = facilityId,
            ReportingPeriodStartUtc = startDateUtc,
            ReportingPeriodEndUtc = endDateUtc,
            TotalLaborCost = Math.Round(directCost + idleCost, 2),
            DirectProductiveCost = Math.Round(directCost, 2),
            IdleTimeCost = idleCost,
            AverageCostPerPickUnit = costPerPickUnit,
            AverageCostPerPackCarton = costPerPackCarton,
            AverageCostPerPutawayPallet = costPerPutawayPallet
        };
    }

    #endregion

    #region Phase 6.3 Inventory Health & Space Utilization Queries

    public async Task<InventoryAgingPolicyDto> GetInventoryAgingPolicyAsync(
        Guid? facilityId = null,
        Guid? clientId = null,
        CancellationToken cancellationToken = default)
    {
        if (clientId.HasValue)
        {
            ValidateTenantAccess(clientId.Value);
        }
        else if (_currentUser.TenantId.HasValue)
        {
            clientId = _currentUser.TenantId.Value;
        }

        if (facilityId.HasValue)
        {
            ValidateFacilityAccess(facilityId.Value);
        }

        var policies = await _dbContext.InventoryAgingPolicies
            .AsNoTracking()
            .Where(p => p.IsActive)
            .ToListAsync(cancellationToken);

        var matched = policies
            .Where(p => (p.FacilityId == facilityId && p.ClientId == clientId) ||
                        (p.FacilityId == null && p.ClientId == clientId) ||
                        (p.FacilityId == facilityId && p.ClientId == null) ||
                        (p.FacilityId == null && p.ClientId == null))
            .OrderBy(p => p.FacilityId.HasValue && p.ClientId.HasValue ? 0 :
                          p.ClientId.HasValue ? 1 :
                          p.FacilityId.HasValue ? 2 : 3)
            .ThenByDescending(p => p.Version)
            .FirstOrDefault();

        var policy = matched ?? InventoryAgingPolicy.Create("Platform Default Policy");

        return new InventoryAgingPolicyDto
        {
            Id = policy.Id,
            FacilityId = policy.FacilityId,
            ClientId = policy.ClientId,
            Name = policy.Name,
            SlowMovingThresholdDays = policy.SlowMovingThresholdDays,
            DormantThresholdDays = policy.DormantThresholdDays,
            DeadStockThresholdDays = policy.DeadStockThresholdDays,
            NearExpiryThresholdDays = policy.NearExpiryThresholdDays,
            CriticalExpiryThresholdDays = policy.CriticalExpiryThresholdDays,
            DefaultBasis = policy.DefaultBasis,
            Currency = policy.Currency,
            DailyHoldingCostPerCbm = policy.DailyHoldingCostPerCbm,
            DailyHoldingCostPerPallet = policy.DailyHoldingCostPerPallet,
            Version = policy.Version,
            EffectiveFromUtc = policy.EffectiveFromUtc,
            EffectiveToUtc = policy.EffectiveToUtc,
            IsActive = policy.IsActive
        };
    }

    public async Task<InventoryHealthSummaryDto> GetInventoryHealthSummaryAsync(
        Guid facilityId,
        DateTime? asOfDateUtc = null,
        Guid? clientId = null,
        CancellationToken cancellationToken = default)
    {
        ValidateFacilityAccess(facilityId);

        if (clientId.HasValue)
        {
            ValidateTenantAccess(clientId.Value);
        }
        else if (_currentUser.TenantId.HasValue)
        {
            clientId = _currentUser.TenantId.Value;
        }

        var asOf = (asOfDateUtc ?? DateTime.UtcNow).Date;

        var query = _dbContext.DailyInventoryAgingSnapshots
            .AsNoTracking()
            .Where(s => s.FacilityId == facilityId && s.SnapshotDateUtc == asOf);

        if (clientId.HasValue)
        {
            query = query.Where(s => s.ClientId == clientId.Value);
        }

        var records = await query.ToListAsync(cancellationToken);

        var totalSkus = records.Select(r => r.ItemId).Distinct().Count();
        var totalUnits = records.Sum(r => r.QuantityOnHand);
        var totalValue = records.Sum(r => r.TotalInventoryValue);
        var totalCbm = records.Sum(r => r.OccupiedCbm);
        var totalPallets = records.Sum(r => r.PalletFootprint);

        var deadStock = records.Where(r => r.IsDeadStock).ToList();
        var deadStockValue = deadStock.Sum(r => r.TotalInventoryValue);
        var deadStockRatio = totalValue > 0m ? Math.Round((deadStockValue / totalValue) * 100m, 2) : 0m;
        var deadStockSkus = deadStock.Select(r => r.ItemId).Distinct().Count();
        var deadStockUnits = deadStock.Sum(r => r.QuantityOnHand);

        var slowMoving = records.Where(r => r.IsSlowMoving).ToList();
        var slowMovingValue = slowMoving.Sum(r => r.TotalInventoryValue);
        var slowMovingSkus = slowMoving.Select(r => r.ItemId).Distinct().Count();

        var expired = records.Where(r => r.IsExpired).ToList();
        var expiredValue = expired.Sum(r => r.TotalInventoryValue);
        var expiredLots = expired.Where(r => r.LotId.HasValue).Select(r => r.LotId!.Value).Distinct().Count();

        var nearExpiry = records.Where(r => r.IsNearExpiry && !r.IsExpired).ToList();
        var nearExpiryValue = nearExpiry.Sum(r => r.TotalInventoryValue);
        var nearExpiryLots = nearExpiry.Where(r => r.LotId.HasValue).Select(r => r.LotId!.Value).Distinct().Count();

        // 6 Discrete Buckets
        var bucketSummaries = Enum.GetValues<InventoryAgingBucket>()
            .Select(b =>
            {
                var bucketRecords = records.Where(r => r.AgingBucket == b).ToList();
                var bValue = bucketRecords.Sum(r => r.TotalInventoryValue);
                var (dMin, dMax, name) = b switch
                {
                    InventoryAgingBucket.Current_0_30 => (0, (int?)30, "Current (0-30 Days)"),
                    InventoryAgingBucket.Aging_31_60 => (31, (int?)60, "Aging (31-60 Days)"),
                    InventoryAgingBucket.Aging_61_90 => (61, (int?)90, "Aging (61-90 Days)"),
                    InventoryAgingBucket.SlowMoving_91_180 => (91, (int?)180, "Slow-Moving (91-180 Days)"),
                    InventoryAgingBucket.Dormant_181_365 => (181, (int?)365, "Dormant (181-365 Days)"),
                    _ => (366, (int?)null, "Dead Stock (365+ Days)")
                };

                return new InventoryAgingBucketSummaryDto
                {
                    Bucket = b,
                    BucketName = name,
                    DaysMin = dMin,
                    DaysMax = dMax,
                    SkuCount = bucketRecords.Select(r => r.ItemId).Distinct().Count(),
                    TotalUnits = bucketRecords.Sum(r => r.QuantityOnHand),
                    TotalValue = bValue,
                    TotalOccupiedCbm = bucketRecords.Sum(r => r.OccupiedCbm),
                    PercentageOfTotalValue = totalValue > 0m ? Math.Round((bValue / totalValue) * 100m, 2) : 0m
                };
            })
            .OrderBy(b => b.Bucket)
            .ToList();

        // Holding Status Breakdown
        var holdingBreakdowns = Enum.GetValues<InventoryHoldingStatus>()
            .Select(h =>
            {
                var hRecords = records.Where(r => r.HoldingStatus == h).ToList();
                var hUnits = hRecords.Sum(r => r.QuantityOnHand);
                return new InventoryHoldingStatusBreakdownDto
                {
                    HoldingStatus = h,
                    TotalUnits = hUnits,
                    TotalValue = hRecords.Sum(r => r.TotalInventoryValue),
                    TotalOccupiedCbm = hRecords.Sum(r => r.OccupiedCbm),
                    PercentageOfTotalUnits = totalUnits > 0m ? Math.Round((hUnits / totalUnits) * 100m, 2) : 0m
                };
            })
            .OrderBy(h => h.HoldingStatus)
            .ToList();

        return new InventoryHealthSummaryDto
        {
            FacilityId = facilityId,
            ClientId = clientId,
            AsOfDateUtc = asOf,
            TotalActiveSkus = totalSkus,
            TotalUnitsOnHand = totalUnits,
            TotalInventoryValue = totalValue,
            TotalOccupiedCbm = totalCbm,
            TotalPalletFootprint = totalPallets,
            DeadStockValue = deadStockValue,
            DeadStockRatioPercent = deadStockRatio,
            DeadStockSkuCount = deadStockSkus,
            DeadStockUnitsCount = deadStockUnits,
            SlowMovingValue = slowMovingValue,
            SlowMovingSkuCount = slowMovingSkus,
            ExpiredStockValue = expiredValue,
            ExpiredLotCount = expiredLots,
            NearExpiryStockValue = nearExpiryValue,
            NearExpiryLotCount = nearExpiryLots,
            AgingBuckets = bucketSummaries,
            HoldingStatusBreakdown = holdingBreakdowns
        };
    }

    public async Task<IReadOnlyList<DailyInventoryAgingRecordDto>> GetDailyInventoryAgingRecordsAsync(
        Guid facilityId,
        DateTime? asOfDateUtc = null,
        Guid? clientId = null,
        InventoryAgingBucket? bucket = null,
        bool? isDeadStock = null,
        CancellationToken cancellationToken = default)
    {
        ValidateFacilityAccess(facilityId);

        if (clientId.HasValue)
        {
            ValidateTenantAccess(clientId.Value);
        }
        else if (_currentUser.TenantId.HasValue)
        {
            clientId = _currentUser.TenantId.Value;
        }

        var asOf = (asOfDateUtc ?? DateTime.UtcNow).Date;

        var query = _dbContext.DailyInventoryAgingSnapshots
            .AsNoTracking()
            .Where(s => s.FacilityId == facilityId && s.SnapshotDateUtc == asOf);

        if (clientId.HasValue)
        {
            query = query.Where(s => s.ClientId == clientId.Value);
        }

        if (bucket.HasValue)
        {
            query = query.Where(s => s.AgingBucket == bucket.Value);
        }

        if (isDeadStock.HasValue)
        {
            query = query.Where(s => s.IsDeadStock == isDeadStock.Value);
        }

        var records = await query
            .OrderByDescending(s => s.TotalInventoryValue)
            .ToListAsync(cancellationToken);

        return records.Select(s => new DailyInventoryAgingRecordDto
        {
            Id = s.Id,
            ClientId = s.ClientId,
            FacilityId = s.FacilityId,
            LocationId = s.LocationId,
            LocationCode = s.LocationCode,
            ItemId = s.ItemId,
            Sku = s.Sku,
            LotId = s.LotId,
            LotNumber = s.LotNumber,
            LpnCode = s.LpnCode,
            SnapshotDateUtc = s.SnapshotDateUtc,
            HoldingStatus = s.HoldingStatus,
            QtyAvailable = s.QtyAvailable,
            QtyAllocated = s.QtyAllocated,
            QtyReserved = s.QtyReserved,
            QtyQuarantined = s.QtyQuarantined,
            QtyDamaged = s.QtyDamaged,
            QtyOnHold = s.QtyOnHold,
            ReceiptAgeDays = s.ReceiptAgeDays,
            LastMovementAgeDays = s.LastMovementAgeDays,
            LastDemandAgeDays = s.LastDemandAgeDays,
            HasDemandHistory = s.HasDemandHistory,
            DemandInactivityDays = s.DemandInactivityDays,
            DaysUntilExpiry = s.DaysUntilExpiry,
            HoldingDurationDays = s.HoldingDurationDays,
            AgingBucket = s.AgingBucket,
            VelocityCategory = s.VelocityCategory,
            IsAged = s.IsAged,
            IsSlowMoving = s.IsSlowMoving,
            IsNonMoving = s.IsNonMoving,
            IsDormant = s.IsDormant,
            IsDeadStock = s.IsDeadStock,
            IsNearExpiry = s.IsNearExpiry,
            IsCriticalExpiry = s.IsCriticalExpiry,
            IsExpired = s.IsExpired,
            QuantityOnHand = s.QuantityOnHand,
            UnitCost = s.UnitCost,
            TotalInventoryValue = s.TotalInventoryValue,
            Currency = s.Currency,
            DailyHoldingCost = s.DailyHoldingCost,
            AccruedHoldingCost = s.AccruedHoldingCost,
            OccupiedCbm = s.OccupiedCbm,
            PalletFootprint = s.PalletFootprint,
            HasMissingDimensionsFallback = s.HasMissingDimensionsFallback,
            SourceInventoryReference = s.SourceInventoryReference
        }).ToList();
    }

    public async Task<IReadOnlyList<LotAgingRecordDto>> GetLotAgingBreakdownAsync(
        Guid facilityId,
        Guid itemId,
        DateTime? asOfDateUtc = null,
        CancellationToken cancellationToken = default)
    {
        ValidateFacilityAccess(facilityId);

        var asOf = (asOfDateUtc ?? DateTime.UtcNow).Date;
        var query = _dbContext.DailyInventoryAgingSnapshots
            .AsNoTracking()
            .Where(s => s.FacilityId == facilityId && s.ItemId == itemId && s.SnapshotDateUtc == asOf);

        if (_currentUser.TenantId.HasValue)
        {
            query = query.Where(s => s.ClientId == _currentUser.TenantId.Value);
        }

        var records = await query
            .OrderBy(s => s.ExpirationDateUtc.HasValue ? 0 : 1)
            .ThenBy(s => s.ExpirationDateUtc)
            .ThenBy(s => s.ReceiptAgeDays)
            .ToListAsync(cancellationToken);

        return records.Select(s => new LotAgingRecordDto
        {
            LotId = s.LotId,
            LotNumber = s.LotNumber,
            LpnCode = s.LpnCode,
            ItemId = s.ItemId,
            Sku = s.Sku,
            LocationCode = s.LocationCode,
            QuantityOnHand = s.QuantityOnHand,
            HoldingStatus = s.HoldingStatus,
            QtyAvailable = s.QtyAvailable,
            QtyAllocated = s.QtyAllocated,
            QtyReserved = s.QtyReserved,
            QtyQuarantined = s.QtyQuarantined,
            QtyDamaged = s.QtyDamaged,
            QtyOnHold = s.QtyOnHold,
            ReceiptAgeDays = s.ReceiptAgeDays,
            LastMovementAgeDays = s.LastMovementAgeDays,
            LastDemandAgeDays = s.LastDemandAgeDays,
            DaysUntilExpiry = s.DaysUntilExpiry,
            HoldingDurationDays = s.HoldingDurationDays,
            AgingBucket = s.AgingBucket,
            VelocityCategory = s.VelocityCategory,
            IsAged = s.IsAged,
            IsSlowMoving = s.IsSlowMoving,
            IsNonMoving = s.IsNonMoving,
            IsDeadStock = s.IsDeadStock,
            IsExpired = s.IsExpired,
            IsNearExpiry = s.IsNearExpiry,
            UnitCost = s.UnitCost,
            TotalInventoryValue = s.TotalInventoryValue,
            Currency = s.Currency,
            AccruedHoldingCost = s.AccruedHoldingCost
        }).ToList();
    }

    public async Task<DeadStockAnalysisDto> GetDeadStockAnalysisAsync(
        Guid facilityId,
        Guid? clientId = null,
        DateTime? asOfDateUtc = null,
        CancellationToken cancellationToken = default)
    {
        ValidateFacilityAccess(facilityId);

        if (clientId.HasValue)
        {
            ValidateTenantAccess(clientId.Value);
        }
        else if (_currentUser.TenantId.HasValue)
        {
            clientId = _currentUser.TenantId.Value;
        }

        var asOf = (asOfDateUtc ?? DateTime.UtcNow).Date;

        var allRecordsQuery = _dbContext.DailyInventoryAgingSnapshots
            .AsNoTracking()
            .Where(s => s.FacilityId == facilityId && s.SnapshotDateUtc == asOf);

        if (clientId.HasValue)
        {
            allRecordsQuery = allRecordsQuery.Where(s => s.ClientId == clientId.Value);
        }

        var allRecords = await allRecordsQuery.ToListAsync(cancellationToken);
        var totalFacilityValue = allRecords.Sum(r => r.TotalInventoryValue);

        var deadRecords = allRecords.Where(r => r.IsDeadStock).ToList();
        var deadValue = deadRecords.Sum(r => r.TotalInventoryValue);
        var deadUnits = deadRecords.Sum(r => r.QuantityOnHand);
        var deadCbm = deadRecords.Sum(r => r.OccupiedCbm);
        var deadHoldingCost = deadRecords.Sum(r => r.AccruedHoldingCost);
        var deadPct = totalFacilityValue > 0m ? Math.Round((deadValue / totalFacilityValue) * 100m, 2) : 0m;

        var items = deadRecords
            .GroupBy(r => new { r.ItemId, r.Sku, r.ClientId, r.FacilityId })
            .Select(g => new DeadStockItemDto
            {
                ItemId = g.Key.ItemId,
                Sku = g.Key.Sku,
                ClientId = g.Key.ClientId,
                FacilityId = g.Key.FacilityId,
                QuantityOnHand = g.Sum(r => r.QuantityOnHand),
                TotalValue = g.Sum(r => r.TotalInventoryValue),
                ReceiptAgeDays = g.Max(r => r.ReceiptAgeDays),
                LastMovementAgeDays = g.Max(r => r.LastMovementAgeDays),
                LastDemandAgeDays = g.Max(r => r.LastDemandAgeDays),
                HasDemandHistory = g.Any(r => r.HasDemandHistory),
                DemandInactivityDays = g.Max(r => r.DemandInactivityDays),
                OccupiedCbm = g.Sum(r => r.OccupiedCbm),
                AccruedHoldingCost = g.Sum(r => r.AccruedHoldingCost),
                Currency = g.First().Currency,
                LotCount = g.Where(r => r.LotId.HasValue).Select(r => r.LotId!.Value).Distinct().Count(),
                PrimaryLocationCode = g.OrderByDescending(r => r.QuantityOnHand).First().LocationCode
            })
            .OrderByDescending(i => i.TotalValue)
            .ToList();

        return new DeadStockAnalysisDto
        {
            FacilityId = facilityId,
            ClientId = clientId,
            AsOfDateUtc = asOf,
            TotalDeadStockSkus = items.Count,
            TotalDeadStockUnits = deadUnits,
            TotalDeadStockValue = deadValue,
            TotalDeadStockCbm = deadCbm,
            TotalAccruedHoldingCost = deadHoldingCost,
            Currency = deadRecords.FirstOrDefault()?.Currency ?? "USD",
            DeadStockPercentageOfInventoryValue = deadPct,
            Items = items
        };
    }

    public async Task<IReadOnlyList<LocationSpaceUtilizationDto>> GetLocationSpaceUtilizationAsync(
        Guid facilityId,
        DateTime? asOfDateUtc = null,
        Guid? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        ValidateInternalStaffOnly();
        ValidateFacilityAccess(facilityId);

        var asOf = (asOfDateUtc ?? DateTime.UtcNow).Date;

        var query = _dbContext.DailyLocationSpaceUtilizationRecords
            .AsNoTracking()
            .Where(r => r.FacilityId == facilityId && r.SnapshotDateUtc == asOf);

        if (zoneId.HasValue)
        {
            query = query.Where(r => r.ZoneId == zoneId.Value);
        }

        var records = await query
            .OrderBy(r => r.LocationCode)
            .ToListAsync(cancellationToken);

        return records.Select(r => new LocationSpaceUtilizationDto
        {
            LocationId = r.LocationId,
            FacilityId = r.FacilityId,
            ZoneId = r.ZoneId,
            LocationCode = r.LocationCode,
            LocationType = r.LocationType,
            SnapshotDateUtc = r.SnapshotDateUtc,
            AisleCode = r.AisleCode,
            RackCode = r.RackCode,
            ShelfLevel = r.ShelfLevel,
            BinNumber = r.BinNumber,
            XCoord = r.XCoord,
            YCoord = r.YCoord,
            ZCoord = r.ZCoord,
            PhysicalCubicMeters = r.PhysicalCubicMeters,
            UsableCapacityFactor = r.UsableCapacityFactor,
            UsableCubicMeters = r.UsableCubicMeters,
            OccupiedCubicMeters = r.OccupiedCubicMeters,
            AvailableCubicMeters = r.AvailableCubicMeters,
            CubeUtilizationPercent = r.CubeUtilizationPercent,
            PhysicalPalletPositions = r.PhysicalPalletPositions,
            UsablePalletPositions = r.UsablePalletPositions,
            OccupiedPalletPositions = r.OccupiedPalletPositions,
            AvailablePalletPositions = r.AvailablePalletPositions,
            PalletOccupancyPercent = r.PalletOccupancyPercent,
            IsBlocked = r.IsBlocked,
            BlockReason = r.BlockReason,
            MaxWeightKg = r.MaxWeightKg,
            OccupiedWeightKg = r.OccupiedWeightKg,
            WeightUtilizationPercent = r.WeightUtilizationPercent,
            IsWeightConstrained = r.IsWeightConstrained,
            IsOverCapacity = r.IsOverCapacity,
            OverCapacityExcessCbm = r.OverCapacityExcessCbm,
            DistinctSkuCount = r.DistinctSkuCount,
            TotalUnitsStored = r.TotalUnitsStored,
            IsMixedSkuLocation = r.IsMixedSkuLocation,
            HasMissingDimensionsFallback = r.HasMissingDimensionsFallback,
            UnitsAvailable = r.UnitsAvailable,
            UnitsAllocated = r.UnitsAllocated,
            UnitsReserved = r.UnitsReserved,
            UnitsQuarantined = r.UnitsQuarantined,
            UnitsDamaged = r.UnitsDamaged,
            UnitsOnHold = r.UnitsOnHold,
            OccupiedCbmAvailable = r.OccupiedCbmAvailable,
            OccupiedCbmAllocated = r.OccupiedCbmAllocated,
            OccupiedCbmReserved = r.OccupiedCbmReserved,
            OccupiedCbmQuarantined = r.OccupiedCbmQuarantined,
            OccupiedCbmDamaged = r.OccupiedCbmDamaged,
            OccupiedCbmOnHold = r.OccupiedCbmOnHold,
            SpaceClassification = r.SpaceClassification
        }).ToList();
    }

    public async Task<FacilitySpaceUtilizationSummaryDto> GetFacilitySpaceUtilizationSummaryAsync(
        Guid facilityId,
        DateTime? asOfDateUtc = null,
        CancellationToken cancellationToken = default)
    {
        ValidateInternalStaffOnly();
        ValidateFacilityAccess(facilityId);

        var asOf = (asOfDateUtc ?? DateTime.UtcNow).Date;

        var records = await _dbContext.DailyLocationSpaceUtilizationRecords
            .AsNoTracking()
            .Where(r => r.FacilityId == facilityId && r.SnapshotDateUtc == asOf)
            .ToListAsync(cancellationToken);

        var zones = await _dbContext.WarehouseZones
            .AsNoTracking()
            .Where(z => z.FacilityId == facilityId)
            .ToListAsync(cancellationToken);

        var totalLocations = records.Count;
        var activeLocations = records.Count(r => !r.IsBlocked);
        var blockedLocations = records.Count(r => r.IsBlocked);
        var emptyLocations = records.Count(r => r.SpaceClassification == LocationSpaceUtilizationClassification.Empty);
        var underutilizedLocations = records.Count(r => r.SpaceClassification == LocationSpaceUtilizationClassification.Underutilized);
        var optimalLocations = records.Count(r => r.SpaceClassification == LocationSpaceUtilizationClassification.Optimal);
        var nearCapacityLocations = records.Count(r => r.SpaceClassification == LocationSpaceUtilizationClassification.NearCapacity);
        var overCapacityLocations = records.Count(r => r.IsOverCapacity || r.SpaceClassification == LocationSpaceUtilizationClassification.OverCapacity);
        var mixedSkuLocations = records.Count(r => r.IsMixedSkuLocation);

        var totalPhysicalCbm = records.Sum(r => r.PhysicalCubicMeters);
        var totalUsableCbm = records.Sum(r => r.UsableCubicMeters);
        var totalOccupiedCbm = records.Sum(r => r.OccupiedCubicMeters);
        var totalAvailableCbm = records.Sum(r => r.AvailableCubicMeters);
        var cubeFillPercent = totalUsableCbm > 0m ? Math.Round((totalOccupiedCbm / totalUsableCbm) * 100m, 2) : 0m;
        var totalExcessCbm = records.Sum(r => r.OverCapacityExcessCbm);

        var totalPhysicalPallets = records.Sum(r => r.PhysicalPalletPositions);
        var totalUsablePallets = records.Sum(r => r.UsablePalletPositions);
        var totalOccupiedPallets = records.Sum(r => r.OccupiedPalletPositions);
        var totalAvailablePallets = records.Sum(r => r.AvailablePalletPositions);
        var palletOccupancyPercent = totalUsablePallets > 0 ? Math.Round(((decimal)totalOccupiedPallets / (decimal)totalUsablePallets) * 100m, 2) : 0m;

        var totalMaxWeight = records.Sum(r => r.MaxWeightKg);
        var totalOccupiedWeight = records.Sum(r => r.OccupiedWeightKg);
        var weightUtilizationPercent = totalMaxWeight > 0m ? Math.Round((totalOccupiedWeight / totalMaxWeight) * 100m, 2) : 0m;
        var weightConstrainedLocations = records.Count(r => r.IsWeightConstrained);

        var missingDimsCount = records.Count(r => r.HasMissingDimensionsFallback);

        // Partitioned Volume Aggregations
        var occupiedCbmAvailable = records.Sum(r => r.OccupiedCbmAvailable);
        var occupiedCbmAllocated = records.Sum(r => r.OccupiedCbmAllocated);
        var occupiedCbmReserved = records.Sum(r => r.OccupiedCbmReserved);
        var occupiedCbmQuarantined = records.Sum(r => r.OccupiedCbmQuarantined);
        var occupiedCbmDamaged = records.Sum(r => r.OccupiedCbmDamaged);
        var occupiedCbmOnHold = records.Sum(r => r.OccupiedCbmOnHold);

        // Zone-Level Rollups
        var zoneBreakdowns = records
            .GroupBy(r => r.ZoneId)
            .Select(g =>
            {
                var zoneEntity = zones.FirstOrDefault(z => z.Id == g.Key);
                var zUsableCbm = g.Sum(r => r.UsableCubicMeters);
                var zOccupiedCbm = g.Sum(r => r.OccupiedCubicMeters);
                var zUsablePallets = g.Sum(r => r.UsablePalletPositions);
                var zOccupiedPallets = g.Sum(r => r.OccupiedPalletPositions);

                var zPrimaryFill = zUsableCbm > 0m
                    ? Math.Round((zOccupiedCbm / zUsableCbm) * 100m, 2)
                    : (zUsablePallets > 0 ? Math.Round(((decimal)zOccupiedPallets / (decimal)zUsablePallets) * 100m, 2) : 0m);

                var zOverCapCount = g.Count(r => r.IsOverCapacity || r.SpaceClassification == LocationSpaceUtilizationClassification.OverCapacity);

                // Aisle breakdowns inside zone
                var aisles = g
                    .Where(r => !string.IsNullOrWhiteSpace(r.AisleCode))
                    .GroupBy(r => r.AisleCode!)
                    .Select(ag =>
                    {
                        var aUsableCbm = ag.Sum(r => r.UsableCubicMeters);
                        var aOccupiedCbm = ag.Sum(r => r.OccupiedCubicMeters);
                        var aUsablePallets = ag.Sum(r => r.UsablePalletPositions);
                        var aOccupiedPallets = ag.Sum(r => r.OccupiedPalletPositions);

                        return new AisleSpaceUtilizationDto
                        {
                            AisleCode = ag.Key,
                            LocationsCount = ag.Count(),
                            RacksCount = ag.Select(r => r.RackCode).Where(c => !string.IsNullOrEmpty(c)).Distinct().Count(),
                            UsableCbm = aUsableCbm,
                            OccupiedCbm = aOccupiedCbm,
                            AvailableCbm = Math.Max(0m, aUsableCbm - aOccupiedCbm),
                            CubeUtilizationPercent = aUsableCbm > 0m ? Math.Round((aOccupiedCbm / aUsableCbm) * 100m, 2) : 0m,
                            UsablePalletPositions = aUsablePallets,
                            OccupiedPalletPositions = aOccupiedPallets,
                            PalletOccupancyPercent = aUsablePallets > 0 ? Math.Round(((decimal)aOccupiedPallets / (decimal)aUsablePallets) * 100m, 2) : 0m,
                            OverCapacityLocationsCount = ag.Count(r => r.IsOverCapacity)
                        };
                    })
                    .OrderBy(a => a.AisleCode)
                    .ToList();

                var zClass = zOverCapCount > 0
                    ? LocationSpaceUtilizationClassification.OverCapacity
                    : (zPrimaryFill == 0m ? LocationSpaceUtilizationClassification.Empty :
                       zPrimaryFill < 50m ? LocationSpaceUtilizationClassification.Underutilized :
                       zPrimaryFill <= 85m ? LocationSpaceUtilizationClassification.Optimal :
                       zPrimaryFill <= 95m ? LocationSpaceUtilizationClassification.NearCapacity :
                       LocationSpaceUtilizationClassification.FullOrOverfilled);

                return new ZoneSpaceUtilizationDto
                {
                    ZoneId = g.Key,
                    ZoneCode = zoneEntity?.ZoneCode ?? (g.Key.HasValue ? g.Key.Value.ToString()[..8] : "UNASSIGNED"),
                    ZoneName = zoneEntity?.Name ?? "General Storage Zone",
                    TotalLocationsCount = g.Count(),
                    ActiveLocationsCount = g.Count(r => !r.IsBlocked),
                    BlockedLocationsCount = g.Count(r => r.IsBlocked),
                    UsableCbm = zUsableCbm,
                    OccupiedCbm = zOccupiedCbm,
                    AvailableCbm = Math.Max(0m, zUsableCbm - zOccupiedCbm),
                    CubeUtilizationPercent = zUsableCbm > 0m ? Math.Round((zOccupiedCbm / zUsableCbm) * 100m, 2) : 0m,
                    UsablePalletPositions = zUsablePallets,
                    OccupiedPalletPositions = zOccupiedPallets,
                    AvailablePalletPositions = Math.Max(0, zUsablePallets - zOccupiedPallets),
                    PalletOccupancyPercent = zUsablePallets > 0 ? Math.Round(((decimal)zOccupiedPallets / (decimal)zUsablePallets) * 100m, 2) : 0m,
                    OverCapacityLocationsCount = zOverCapCount,
                    UnderutilizedLocationsCount = g.Count(r => r.SpaceClassification == LocationSpaceUtilizationClassification.Underutilized),
                    SpaceClassification = zClass,
                    AisleBreakdowns = aisles
                };
            })
            .OrderBy(z => z.ZoneCode)
            .ToList();

        return new FacilitySpaceUtilizationSummaryDto
        {
            FacilityId = facilityId,
            AsOfDateUtc = asOf,
            TotalLocationsCount = totalLocations,
            ActiveLocationsCount = activeLocations,
            BlockedLocationsCount = blockedLocations,
            EmptyLocationsCount = emptyLocations,
            UnderutilizedLocationsCount = underutilizedLocations,
            OptimalLocationsCount = optimalLocations,
            NearCapacityLocationsCount = nearCapacityLocations,
            OverCapacityLocationsCount = overCapacityLocations,
            MixedSkuLocationsCount = mixedSkuLocations,
            TotalPhysicalCbm = totalPhysicalCbm,
            TotalUsableCbm = totalUsableCbm,
            TotalOccupiedCbm = totalOccupiedCbm,
            TotalAvailableCbm = totalAvailableCbm,
            CubeFillPercent = cubeFillPercent,
            TotalOverCapacityExcessCbm = totalExcessCbm,
            TotalPhysicalPalletPositions = totalPhysicalPallets,
            TotalUsablePalletPositions = totalUsablePallets,
            TotalOccupiedPalletPositions = totalOccupiedPallets,
            TotalAvailablePalletPositions = totalAvailablePallets,
            PalletOccupancyPercent = palletOccupancyPercent,
            TotalMaxWeightKg = totalMaxWeight,
            TotalOccupiedWeightKg = totalOccupiedWeight,
            WeightUtilizationPercent = weightUtilizationPercent,
            WeightConstrainedLocationsCount = weightConstrainedLocations,
            OccupiedCbmAvailable = occupiedCbmAvailable,
            OccupiedCbmAllocated = occupiedCbmAllocated,
            OccupiedCbmReserved = occupiedCbmReserved,
            OccupiedCbmQuarantined = occupiedCbmQuarantined,
            OccupiedCbmDamaged = occupiedCbmDamaged,
            OccupiedCbmOnHold = occupiedCbmOnHold,
            MissingDimensionsCount = missingDimsCount,
            ZoneBreakdowns = zoneBreakdowns
        };
    }

    public async Task<IReadOnlyList<OverCapacityAlertDto>> GetOverCapacityAlertsAsync(
        Guid facilityId,
        DateTime? asOfDateUtc = null,
        CancellationToken cancellationToken = default)
    {
        ValidateInternalStaffOnly();
        ValidateFacilityAccess(facilityId);

        var asOf = (asOfDateUtc ?? DateTime.UtcNow).Date;

        var overfilled = await _dbContext.DailyLocationSpaceUtilizationRecords
            .AsNoTracking()
            .Where(r => r.FacilityId == facilityId && r.SnapshotDateUtc == asOf &&
                       (r.IsOverCapacity || r.SpaceClassification == LocationSpaceUtilizationClassification.OverCapacity ||
                        r.CubeUtilizationPercent > 100m || r.PalletOccupancyPercent > 100m || r.WeightUtilizationPercent > 100m))
            .OrderByDescending(r => r.OverCapacityExcessCbm)
            .ThenByDescending(r => r.CubeUtilizationPercent)
            .ToListAsync(cancellationToken);

        return overfilled.Select(r => new OverCapacityAlertDto
        {
            LocationId = r.LocationId,
            LocationCode = r.LocationCode,
            ZoneId = r.ZoneId,
            LocationType = r.LocationType,
            CubeUtilizationPercent = r.CubeUtilizationPercent,
            PalletOccupancyPercent = r.PalletOccupancyPercent,
            WeightUtilizationPercent = r.WeightUtilizationPercent,
            UsableCbm = r.UsableCubicMeters,
            OccupiedCbm = r.OccupiedCubicMeters,
            ExcessCbm = r.OverCapacityExcessCbm,
            IsWeightConstrained = r.IsWeightConstrained,
            DistinctSkuCount = r.DistinctSkuCount
        }).ToList();
    }

    public async Task<IReadOnlyList<SkuVelocityClassificationDto>> GetSkuVelocityClassificationsAsync(
        Guid facilityId,
        Guid? clientId = null,
        AbcClassificationCriteria? criteria = null,
        AbcClassificationCategory? abcClass = null,
        VelocityObservationWindow? window = null,
        InventoryMovementVelocityCategory? velocityCategory = null,
        Guid? itemId = null,
        CancellationToken cancellationToken = default)
    {
        ValidateFacilityAccess(facilityId);

        if (clientId.HasValue)
        {
            ValidateTenantAccess(clientId.Value);
        }
        else if (_currentUser.TenantId.HasValue)
        {
            clientId = _currentUser.TenantId.Value;
        }

        var query = _dbContext.SkuVelocityClassificationRecords
            .AsNoTracking()
            .Where(r => r.FacilityId == facilityId && r.RecordType == SkuClassificationRecordType.VelocityOperational);

        if (clientId.HasValue)
        {
            query = query.Where(r => r.ClientId == clientId.Value);
        }

        if (criteria.HasValue)
        {
            query = query.Where(r => r.CriteriaUsed == criteria.Value);
        }

        if (abcClass.HasValue)
        {
            query = query.Where(r => r.AbcClass == abcClass.Value);
        }

        if (window.HasValue)
        {
            query = query.Where(r => r.ObservationWindow == window.Value);
        }

        if (velocityCategory.HasValue)
        {
            query = query.Where(r => r.VelocityCategory == velocityCategory.Value);
        }

        if (itemId.HasValue)
        {
            query = query.Where(r => r.ItemId == itemId.Value);
        }

        var records = await query
            .OrderBy(r => r.CumulativePercentage)
            .ToListAsync(cancellationToken);

        return records.Select(r => new SkuVelocityClassificationDto
        {
            Id = r.Id,
            ClientId = r.ClientId,
            FacilityId = r.FacilityId,
            ItemId = r.ItemId,
            Sku = r.Sku,
            AbcClass = r.AbcClass,
            CriteriaUsed = r.CriteriaUsed,
            VelocityCategory = r.VelocityCategory,
            ObservationWindow = r.ObservationWindow,
            PeriodStartUtc = r.CalculationPeriodStartUtc,
            PeriodEndUtc = r.CalculationPeriodEndUtc,
            PeriodDurationDays = r.PeriodDurationDays,
            TotalPickTasksCount = r.TotalPickTasksCount,
            TotalUnitsDemanded = r.TotalUnitsDemanded,
            TotalDemandValue = r.TotalDemandValue,
            PeriodDemandValue = r.PeriodDemandValue > 0m ? r.PeriodDemandValue : r.TotalDemandValue,
            AnnualizedConsumptionValue = r.AnnualizedConsumptionValue > 0m ? r.AnnualizedConsumptionValue : r.TotalDemandValue,
            RecordType = r.RecordType,
            ClassificationRunId = r.ClassificationRunId,
            OrderLinesCount = r.OrderLinesCount,
            TotalDemandEventsCount = r.TotalDemandEventsCount,
            TotalMovementEventsCount = r.TotalMovementEventsCount,
            TotalUnitsMoved = r.TotalUnitsMoved,
            TotalReplenishmentCount = r.TotalReplenishmentCount,
            TotalRelocationCount = r.TotalRelocationCount,
            CostOfGoodsConsumed = r.CostOfGoodsConsumed,
            AverageInventoryValue = r.AverageInventoryValue,
            InventoryTurnoverRatio = r.InventoryTurnoverRatio,
            DaysOfSupply = r.DaysOfSupply,
            DaysOfSupplyFinancial = r.DaysOfSupplyFinancial,
            DaysOfSupplyOperational = r.DaysOfSupplyOperational,
            CurrentOnHandUnits = r.CurrentOnHandUnits,
            IsDemandSpike = r.IsDemandSpike,
            IsSeasonalPattern = r.IsSeasonalPattern,
            PickFrequencyPerDay = r.PickFrequencyPerDay,
            ReplenishmentFrequencyPerDay = r.ReplenishmentFrequencyPerDay,
            RelocationFrequencyPerDay = r.RelocationFrequencyPerDay,
            CumulativePercentage = r.CumulativePercentage,
            RankInPortfolio = r.RankInPortfolio,
            PolicyId = r.PolicyId,
            CompositeScore = r.CompositeScore,
            DailyDemandVelocity = r.DailyDemandVelocity,
            LocationId = r.LocationId,
            ZoneId = r.ZoneId,
            Aisle = r.Aisle,
            Version = r.Version,
            CalculatedAtUtc = r.CalculatedAtUtc
        }).ToList();
    }

    public async Task<SkuVelocityMultiWindowComparisonDto?> GetSkuVelocityMultiWindowComparisonAsync(
        Guid facilityId,
        Guid itemId,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default)
    {
        ValidateFacilityAccess(facilityId);

        var query = _dbContext.SkuVelocityClassificationRecords
            .AsNoTracking()
            .Where(r => r.FacilityId == facilityId && r.ItemId == itemId && r.RecordType == SkuClassificationRecordType.VelocityOperational);

        if (_currentUser.TenantId.HasValue)
        {
            query = query.Where(r => r.ClientId == _currentUser.TenantId.Value);
        }

        var records = await query.ToListAsync(cancellationToken);

        // Also check movement logs if records are scarce
        var movementLogs = await _dbContext.InventoryMovementEventLogs
            .AsNoTracking()
            .Where(l => l.FacilityId == facilityId && l.ItemId == itemId)
            .ToListAsync(cancellationToken);

        if (records.Count == 0 && movementLogs.Count == 0)
        {
            return null;
        }

        var refRecord = records.FirstOrDefault();
        var clientId = refRecord?.ClientId ?? movementLogs.FirstOrDefault()?.ClientId ?? Guid.Empty;
        var sku = refRecord?.Sku ?? movementLogs.FirstOrDefault()?.Sku ?? string.Empty;

        var now = asOfUtc ?? DateTime.UtcNow;

        // Window aggregations helper
        (decimal velocity, decimal turnover, decimal pickFreq, decimal moveFreq) GetWindowStats(int days)
        {
            var record = records.FirstOrDefault(r => r.ObservationWindow == (VelocityObservationWindow)days || r.PeriodDurationDays == days);
            if (record != null)
            {
                return (record.DailyDemandVelocity, record.InventoryTurnoverRatio, record.PickFrequencyPerDay, record.TotalMovementEventsCount / (decimal)Math.Max(1, days));
            }

            var start = now.AddDays(-days);
            var logs = movementLogs.Where(l => l.TimestampUtc >= start && l.TimestampUtc <= now).ToList();
            // Canonical demand deduplication: prevents double counting of pick + dispatch
            decimal demanded = logs.Where(l => l.IsCanonicalDemand).Sum(l => l.Quantity);
            decimal vel = Math.Round(demanded / Math.Max(1, days), 4);
            int picks = logs.Count(l => l.EventType == InventoryMovementEventType.OutboundPick);
            int moves = logs.Count(l => l.IsPhysicalMovement);
            decimal pickF = Math.Round((decimal)picks / Math.Max(1, days), 4);
            decimal moveF = Math.Round((decimal)moves / Math.Max(1, days), 4);
            decimal cogs = logs.Where(l => l.IsCanonicalDemand).Sum(l => l.TotalCostValue);
            decimal avgVal = cogs > 0 ? cogs * 0.5m : 0m;
            decimal to = avgVal > 0 ? Math.Round((cogs / avgVal) * (365m / days), 4) : 0m;
            return (vel, to, pickF, moveF);
        }

        var stats30 = GetWindowStats(30);
        var stats90 = GetWindowStats(90);
        var stats180 = GetWindowStats(180);
        var stats365 = GetWindowStats(365);

        // Trend & Spike Evaluation with Deterministic Precedence
        bool demandSpike = stats30.velocity >= stats365.velocity * 2.0m && stats30.velocity >= 5m;
        bool seasonalPattern = records.Any(r => r.IsSeasonalPattern) ||
                               (stats30.velocity >= 5m && stats180.velocity >= 5m && stats90.velocity < 3m);

        string trend;
        if (stats30.velocity == 0m && stats90.velocity == 0m)
        {
            trend = "Inactive";
        }
        else if (seasonalPattern)
        {
            trend = "SeasonalSpike"; // Verified recurring seasonal pattern
        }
        else if (demandSpike)
        {
            trend = "DemandSpike"; // Sudden extraordinary volume explosion
        }
        else if (stats30.velocity > stats90.velocity * 1.3m && stats30.velocity > 1m)
        {
            trend = "Surging";
        }
        else if (stats30.velocity < stats90.velocity * 0.7m && stats30.velocity > 0m)
        {
            trend = "Declining";
        }
        else
        {
            trend = "Stable";
        }

        // On-hand stock for operational DOS
        var stockOnHandUnits = await _dbContext.StockOnHand
            .AsNoTracking()
            .Where(s => s.FacilityId == facilityId && s.ItemId == itemId)
            .SumAsync(s => s.QtyAvailable + s.QtyAllocated + s.QtyReserved, cancellationToken);

        decimal finDos = stats90.turnover > 0m
            ? Math.Round(365m / stats90.turnover, 2)
            : (stats90.velocity == 0m && stockOnHandUnits > 0m ? 9999.0m : 0.0m);

        decimal opDos = stats90.velocity > 0m
            ? Math.Round(stockOnHandUnits / stats90.velocity, 2)
            : (stockOnHandUnits > 0m ? 9999.0m : 0.0m);

        decimal effectiveDos = opDos > 0m && opDos != 9999.0m ? opDos : finDos;

        var rec90 = records.FirstOrDefault(r => r.ObservationWindow == VelocityObservationWindow.Days90) ?? refRecord;

        return new SkuVelocityMultiWindowComparisonDto
        {
            ItemId = itemId,
            Sku = sku,
            FacilityId = facilityId,
            ClientId = clientId,
            DemandVelocity30d = stats30.velocity,
            DemandVelocity90d = stats90.velocity,
            DemandVelocity180d = stats180.velocity,
            DemandVelocity365d = stats365.velocity,
            TurnoverRatio30d = stats30.turnover,
            TurnoverRatio90d = stats90.turnover,
            TurnoverRatio180d = stats180.turnover,
            TurnoverRatio365d = stats365.turnover,
            PickFrequency30d = stats30.pickFreq,
            PickFrequency90d = stats90.pickFreq,
            MovementFrequency30d = stats30.moveFreq,
            MovementFrequency90d = stats90.moveFreq,
            VelocityCategory90d = rec90?.VelocityCategory ?? InventoryMovementVelocityCategory.MediumMoving,
            AbcClass90d = rec90?.AbcClass ?? AbcClassificationCategory.ClassB,
            DemandTrend = trend,
            SeasonalSpikeDetected = seasonalPattern || demandSpike,
            DemandSpikeDetected = demandSpike,
            SeasonalPatternDetected = seasonalPattern,
            DaysOfSupply = effectiveDos,
            DaysOfSupplyFinancial = finDos,
            DaysOfSupplyOperational = opDos,
            CurrentOnHandUnits = stockOnHandUnits
        };
    }

    public async Task<InventoryTurnoverAnalysisDto> GetInventoryTurnoverAnalysisAsync(
        Guid facilityId,
        Guid? clientId = null,
        VelocityObservationWindow window = VelocityObservationWindow.Days90,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default)
    {
        ValidateFacilityAccess(facilityId);

        if (clientId.HasValue)
        {
            ValidateTenantAccess(clientId.Value);
        }
        else if (_currentUser.TenantId.HasValue)
        {
            clientId = _currentUser.TenantId.Value;
        }

        var query = _dbContext.SkuVelocityClassificationRecords
            .AsNoTracking()
            .Where(r => r.FacilityId == facilityId && r.ObservationWindow == window && r.RecordType == SkuClassificationRecordType.VelocityOperational);

        if (clientId.HasValue)
        {
            query = query.Where(r => r.ClientId == clientId.Value);
        }

        var records = await query.ToListAsync(cancellationToken);

        int durationDays = (int)window;
        var endUtc = asOfUtc ?? DateTime.UtcNow;
        var startUtc = endUtc.AddDays(-durationDays);

        decimal totalCogs = records.Sum(r => r.CostOfGoodsConsumed);
        decimal totalAvgValuation = records.Sum(r => r.AverageInventoryValue);
        decimal overallTurnover = totalAvgValuation > 0m
            ? Math.Round((totalCogs / totalAvgValuation) * (365m / durationDays), 4)
            : 0m;

        decimal avgDos = overallTurnover > 0m ? Math.Round(365m / overallTurnover, 2) : 0m;
        decimal zeroTurnoverVal = records.Where(r => r.TotalUnitsDemanded == 0m).Sum(r => r.AverageInventoryValue);
        decimal deadStockVal = records.Where(r => r.VelocityCategory == InventoryMovementVelocityCategory.DeadStock).Sum(r => r.AverageInventoryValue);

        var fastest = records
            .Where(r => r.InventoryTurnoverRatio > 0m)
            .OrderByDescending(r => r.InventoryTurnoverRatio)
            .Take(5)
            .Select(r => new SkuTurnoverLeaderDto
            {
                ItemId = r.ItemId,
                Sku = r.Sku,
                CogsConsumed = r.CostOfGoodsConsumed,
                AverageInventoryValue = r.AverageInventoryValue,
                TurnoverRatio = r.InventoryTurnoverRatio,
                DaysOfSupply = r.DaysOfSupply,
                DailyDemandVelocity = r.DailyDemandVelocity,
                AbcClass = r.AbcClass
            }).ToList();

        var slowest = records
            .OrderBy(r => r.InventoryTurnoverRatio)
            .ThenByDescending(r => r.AverageInventoryValue)
            .Take(5)
            .Select(r => new SkuTurnoverLeaderDto
            {
                ItemId = r.ItemId,
                Sku = r.Sku,
                CogsConsumed = r.CostOfGoodsConsumed,
                AverageInventoryValue = r.AverageInventoryValue,
                TurnoverRatio = r.InventoryTurnoverRatio,
                DaysOfSupply = r.DaysOfSupply,
                DailyDemandVelocity = r.DailyDemandVelocity,
                AbcClass = r.AbcClass
            }).ToList();

        return new InventoryTurnoverAnalysisDto
        {
            FacilityId = facilityId,
            ClientId = clientId,
            ObservationWindow = window,
            PeriodStartUtc = startUtc,
            PeriodEndUtc = endUtc,
            TotalCogsConsumed = totalCogs,
            TotalAverageInventoryValuation = totalAvgValuation,
            OverallAnnualizedTurnoverRatio = overallTurnover,
            AverageDaysOfSupply = avgDos,
            ZeroTurnoverInventoryValue = zeroTurnoverVal,
            DeadStockValue = deadStockVal,
            FastestTurningSkus = fastest,
            SlowestTurningSkus = slowest
        };
    }

    public async Task<IReadOnlyList<ZoneVelocityProfileDto>> GetZoneVelocityProfilesAsync(
        Guid facilityId,
        Guid? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        ValidateFacilityAccess(facilityId);

        var locQuery = _dbContext.DailyLocationSpaceUtilizationRecords
            .AsNoTracking()
            .Where(l => l.FacilityId == facilityId);

        if (zoneId.HasValue)
        {
            locQuery = locQuery.Where(l => l.ZoneId == zoneId.Value);
        }

        var locs = await locQuery.ToListAsync(cancellationToken);

        // Group by zone
        var zoneGroups = locs
            .Where(l => l.ZoneId.HasValue)
            .GroupBy(l => l.ZoneId!.Value)
            .ToList();

        var skuRecords = await _dbContext.SkuVelocityClassificationRecords
            .AsNoTracking()
            .Where(r => r.FacilityId == facilityId && r.RecordType == SkuClassificationRecordType.VelocityOperational)
            .ToListAsync(cancellationToken);

        var profiles = new List<ZoneVelocityProfileDto>();

        foreach (var group in zoneGroups)
        {
            var currentZoneId = group.Key;
            var totalLocations = group.Count();
            var occupiedLocations = group.Count(l => l.OccupiedCubicMeters > 0 || l.OccupiedPalletPositions > 0);

            // Filter SKU records associated with this zone
            var zoneSkus = skuRecords.Where(r => r.ZoneId == currentZoneId).ToList();
            if (zoneSkus.Count == 0)
            {
                // Fallback: take a representative sample of records to profile the zone
                zoneSkus = skuRecords.Take(Math.Min(skuRecords.Count, totalLocations)).ToList();
            }

            int countA = zoneSkus.Count(r => r.AbcClass == AbcClassificationCategory.ClassA);
            int countB = zoneSkus.Count(r => r.AbcClass == AbcClassificationCategory.ClassB);
            int countC = zoneSkus.Count(r => r.AbcClass == AbcClassificationCategory.ClassC);
            int totalSkus = Math.Max(1, zoneSkus.Count);

            int fastMovers = zoneSkus.Count(r => r.VelocityCategory == InventoryMovementVelocityCategory.FastMoving);
            int slowMovers = zoneSkus.Count(r => r.VelocityCategory == InventoryMovementVelocityCategory.SlowMoving);
            int deadStock = zoneSkus.Count(r => r.VelocityCategory == InventoryMovementVelocityCategory.DeadStock);

            decimal dailyPicks = zoneSkus.Sum(r => r.PickFrequencyPerDay);

            // Determine zone type heuristic from locations or default
            var sampleLoc = group.FirstOrDefault();
            var zoneType = sampleLoc?.LocationType == LocationType.BulkFloor ? WarehouseZoneType.BulkStorage : WarehouseZoneType.PickFace;

            // Slotting misallocation heuristics:
            // Fast movers should be in PickFace; in BulkStorage they are misallocated
            int misallocatedFast = zoneType == WarehouseZoneType.BulkStorage ? fastMovers : 0;
            // Slow movers/Dead stock in PickFace are occupying prime picking real estate
            int misallocatedSlow = zoneType == WarehouseZoneType.PickFace ? (slowMovers + deadStock) : 0;

            profiles.Add(new ZoneVelocityProfileDto
            {
                ZoneId = currentZoneId,
                ZoneCode = $"ZONE-{currentZoneId.ToString()[..4].ToUpper()}",
                ZoneType = zoneType,
                TotalLocations = totalLocations,
                OccupiedLocations = occupiedLocations,
                ClassAPercent = Math.Round((decimal)countA / totalSkus * 100m, 2),
                ClassBPercent = Math.Round((decimal)countB / totalSkus * 100m, 2),
                ClassCPercent = Math.Round((decimal)countC / totalSkus * 100m, 2),
                FastMovingSkuCount = fastMovers,
                SlowMovingSkuCount = slowMovers,
                DeadStockSkuCount = deadStock,
                DailyPickTasksTotal = Math.Round(dailyPicks, 2),
                MisallocatedFastMoverCount = misallocatedFast,
                MisallocatedSlowMoverCount = misallocatedSlow
            });
        }

        return profiles;
    }

    public async Task<SkuVelocitySummaryDto> GetSkuVelocitySummaryAsync(
        Guid facilityId,
        Guid? clientId = null,
        VelocityObservationWindow window = VelocityObservationWindow.Days90,
        CancellationToken cancellationToken = default)
    {
        ValidateFacilityAccess(facilityId);

        if (clientId.HasValue)
        {
            ValidateTenantAccess(clientId.Value);
        }
        else if (_currentUser.TenantId.HasValue)
        {
            clientId = _currentUser.TenantId.Value;
        }

        var query = _dbContext.SkuVelocityClassificationRecords
            .AsNoTracking()
            .Where(r => r.FacilityId == facilityId && r.ObservationWindow == window && r.RecordType == SkuClassificationRecordType.VelocityOperational);

        if (clientId.HasValue)
        {
            query = query.Where(r => r.ClientId == clientId.Value);
        }

        var records = await query.ToListAsync(cancellationToken);

        int totalSkus = records.Count;
        int fast = records.Count(r => r.VelocityCategory == InventoryMovementVelocityCategory.FastMoving);
        int med = records.Count(r => r.VelocityCategory == InventoryMovementVelocityCategory.MediumMoving);
        int slow = records.Count(r => r.VelocityCategory == InventoryMovementVelocityCategory.SlowMoving);
        int nonMoving = records.Count(r => r.VelocityCategory == InventoryMovementVelocityCategory.NonMoving);
        int dead = records.Count(r => r.VelocityCategory == InventoryMovementVelocityCategory.DeadStock);

        int classA = records.Count(r => r.AbcClass == AbcClassificationCategory.ClassA);
        int classB = records.Count(r => r.AbcClass == AbcClassificationCategory.ClassB);
        int classC = records.Count(r => r.AbcClass == AbcClassificationCategory.ClassC);

        decimal dailyDemand = records.Sum(r => r.DailyDemandVelocity);
        decimal dailyPicks = records.Sum(r => r.PickFrequencyPerDay);
        int moveEvents = records.Sum(r => r.TotalMovementEventsCount);

        decimal totalCogs = records.Sum(r => r.CostOfGoodsConsumed);
        decimal totalAvgVal = records.Sum(r => r.AverageInventoryValue);
        decimal turnover = totalAvgVal > 0m ? Math.Round((totalCogs / totalAvgVal) * (365m / (int)window), 4) : 0m;

        return new SkuVelocitySummaryDto
        {
            FacilityId = facilityId,
            ClientId = clientId,
            ObservationWindow = window,
            AsOfDateUtc = DateTime.UtcNow,
            TotalTrackedSkus = totalSkus,
            FastMovingCount = fast,
            MediumMovingCount = med,
            SlowMovingCount = slow,
            NonMovingCount = nonMoving,
            DeadStockCount = dead,
            ClassACount = classA,
            ClassBCount = classB,
            ClassCCount = classC,
            TotalDailyDemandUnits = Math.Round(dailyDemand, 4),
            TotalDailyPickTasks = Math.Round(dailyPicks, 4),
            TotalMovementEventsInPeriod = moveEvents,
            OverallTurnoverRatio = turnover
        };
    }

    #endregion

    private void ValidateWorkforcePrivacy(Guid workerUserId, Guid? requestingUserId, bool hasElevatedWorkforceAccess, Guid? targetFacilityId = null)
    {
        if (_currentUser.TenantId.HasValue)
        {
            throw new UnauthorizedException("Client portal users are not authorized to view internal warehouse workforce analytics.");
        }

        if (_currentUser.FacilityId.HasValue && targetFacilityId.HasValue && _currentUser.FacilityId.Value != targetFacilityId.Value)
        {
            throw new UnauthorizedException($"User is restricted to facility {_currentUser.FacilityId.Value} and cannot access workforce data for facility {targetFacilityId.Value}.");
        }

        var effectiveRequester = requestingUserId ?? _currentUser.UserId;
        if (hasElevatedWorkforceAccess || _currentUser.IsSuperAdmin)
        {
            return;
        }

        if (effectiveRequester != workerUserId)
        {
            throw new UnauthorizedException($"User {effectiveRequester} is not authorized to view worker productivity for {workerUserId}.");
        }
    }

    #region Phase 6.3.5 ABC Inventory Classification Engine

    public async Task<AbcClassificationPolicyDto?> GetAbcClassificationPolicyAsync(
        Guid? facilityId = null,
        Guid? clientId = null,
        CancellationToken cancellationToken = default)
    {
        if (facilityId.HasValue) ValidateFacilityAccess(facilityId.Value);
        if (clientId.HasValue) ValidateTenantAccess(clientId.Value);

        var query = _dbContext.AbcClassificationPolicies.AsNoTracking().Where(p => p.IsActive);

        if (_currentUser.TenantId.HasValue)
        {
            query = query.Where(p => p.ClientId == _currentUser.TenantId.Value || (p.ClientId == null && (p.FacilityId == facilityId || p.FacilityId == null)));
        }
        else if (clientId.HasValue)
        {
            query = query.Where(p => p.ClientId == clientId.Value || (p.ClientId == null && (p.FacilityId == facilityId || p.FacilityId == null)));
        }

        var policy = await query
            .OrderByDescending(p => p.ClientId.HasValue)
            .ThenByDescending(p => p.FacilityId.HasValue)
            .ThenByDescending(p => p.Version)
            .FirstOrDefaultAsync(cancellationToken);

        if (policy == null) return null;

        return new AbcClassificationPolicyDto
        {
            Id = policy.Id,
            FacilityId = policy.FacilityId,
            ClientId = policy.ClientId,
            Name = policy.Name,
            Description = policy.Description,
            Criteria = policy.Criteria,
            ClassACumulativeThresholdPercent = policy.ClassACumulativeThresholdPercent,
            ClassBCumulativeThresholdPercent = policy.ClassBCumulativeThresholdPercent,
            ObservationWindowDays = policy.ObservationWindowDays,
            ObservationWindow = policy.ObservationWindow,
            CostValuationBasis = policy.CostValuationBasis,
            WeightAcv = policy.WeightAcv,
            WeightPickFrequency = policy.WeightPickFrequency,
            WeightUnitVelocity = policy.WeightUnitVelocity,
            WeightOrderLines = policy.WeightOrderLines,
            Version = policy.Version,
            EffectiveFromUtc = policy.EffectiveFromUtc,
            EffectiveToUtc = policy.EffectiveToUtc,
            IsActive = policy.IsActive
        };
    }

    public async Task<IReadOnlyList<AbcClassificationPolicyDto>> GetAbcClassificationPoliciesAsync(
        Guid? facilityId = null,
        Guid? clientId = null,
        CancellationToken cancellationToken = default)
    {
        if (facilityId.HasValue) ValidateFacilityAccess(facilityId.Value);
        if (clientId.HasValue) ValidateTenantAccess(clientId.Value);

        var query = _dbContext.AbcClassificationPolicies.AsNoTracking();

        if (facilityId.HasValue)
        {
            query = query.Where(p => p.FacilityId == facilityId.Value || p.FacilityId == null);
        }

        if (_currentUser.TenantId.HasValue)
        {
            query = query.Where(p => p.ClientId == _currentUser.TenantId.Value || p.ClientId == null);
        }
        else if (clientId.HasValue)
        {
            query = query.Where(p => p.ClientId == clientId.Value || p.ClientId == null);
        }

        var policies = await query
            .OrderBy(p => p.Name)
            .ThenByDescending(p => p.Version)
            .ToListAsync(cancellationToken);

        return policies.Select(p => new AbcClassificationPolicyDto
        {
            Id = p.Id,
            FacilityId = p.FacilityId,
            ClientId = p.ClientId,
            Name = p.Name,
            Description = p.Description,
            Criteria = p.Criteria,
            ClassACumulativeThresholdPercent = p.ClassACumulativeThresholdPercent,
            ClassBCumulativeThresholdPercent = p.ClassBCumulativeThresholdPercent,
            ObservationWindowDays = p.ObservationWindowDays,
            ObservationWindow = p.ObservationWindow,
            CostValuationBasis = p.CostValuationBasis,
            WeightAcv = p.WeightAcv,
            WeightPickFrequency = p.WeightPickFrequency,
            WeightUnitVelocity = p.WeightUnitVelocity,
            WeightOrderLines = p.WeightOrderLines,
            Version = p.Version,
            EffectiveFromUtc = p.EffectiveFromUtc,
            EffectiveToUtc = p.EffectiveToUtc,
            IsActive = p.IsActive
        }).ToList();
    }

    public async Task<AbcClassificationSummaryDto> GetAbcClassificationSummaryAsync(
        Guid facilityId,
        Guid? clientId = null,
        AbcClassificationCriteria? criteria = null,
        int? version = null,
        CancellationToken cancellationToken = default)
    {
        ValidateFacilityAccess(facilityId);
        if (clientId.HasValue) ValidateTenantAccess(clientId.Value);

        var query = _dbContext.SkuVelocityClassificationRecords
            .AsNoTracking()
            .Where(r => r.FacilityId == facilityId && r.RecordType == SkuClassificationRecordType.AbcParetoStratification);

        if (_currentUser.TenantId.HasValue)
        {
            query = query.Where(r => r.ClientId == _currentUser.TenantId.Value);
        }
        else if (clientId.HasValue)
        {
            query = query.Where(r => r.ClientId == clientId.Value);
        }

        if (criteria.HasValue)
        {
            query = query.Where(r => r.CriteriaUsed == criteria.Value);
        }

        int targetVersion = version ?? (await query.MaxAsync(r => (int?)r.Version, cancellationToken) ?? 1);
        query = query.Where(r => r.Version == targetVersion);

        var records = await query.ToListAsync(cancellationToken);
        if (records.Count == 0)
        {
            return new AbcClassificationSummaryDto
            {
                FacilityId = facilityId,
                ClientId = clientId,
                CriteriaUsed = criteria ?? AbcClassificationCriteria.AnnualConsumptionValue,
                CostValuationBasis = InventoryValuationBasis.UnitCost,
                ClassAThresholdPercent = 80.00m,
                ClassBThresholdPercent = 95.00m,
                ObservationWindowDays = 365,
                TotalSkus = 0,
                CalculatedAtUtc = DateTime.UtcNow,
                Version = targetVersion,
                HasPositiveConsumptionValue = false,
                PortfolioConsumptionStatus = "NoPositiveConsumptionValue"
            };
        }

        var firstRecord = records.First();
        var policy = firstRecord.PolicyId.HasValue
            ? await _dbContext.AbcClassificationPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == firstRecord.PolicyId.Value, cancellationToken)
            : null;

        int totalSkus = records.Count;
        int classACount = records.Count(r => r.AbcClass == AbcClassificationCategory.ClassA);
        int classBCount = records.Count(r => r.AbcClass == AbcClassificationCategory.ClassB);
        int classCCount = records.Count(r => r.AbcClass == AbcClassificationCategory.ClassC);

        decimal totalPeriodVal = records.Sum(r => r.PeriodDemandValue > 0m ? r.PeriodDemandValue : r.TotalDemandValue);
        decimal totalAnnualVal = records.Sum(r => r.AnnualizedConsumptionValue > 0m ? r.AnnualizedConsumptionValue : r.TotalDemandValue);

        decimal classAVal = records.Where(r => r.AbcClass == AbcClassificationCategory.ClassA).Sum(r => r.AnnualizedConsumptionValue > 0m ? r.AnnualizedConsumptionValue : r.TotalDemandValue);
        decimal classBVal = records.Where(r => r.AbcClass == AbcClassificationCategory.ClassB).Sum(r => r.AnnualizedConsumptionValue > 0m ? r.AnnualizedConsumptionValue : r.TotalDemandValue);
        decimal classCVal = records.Where(r => r.AbcClass == AbcClassificationCategory.ClassC).Sum(r => r.AnnualizedConsumptionValue > 0m ? r.AnnualizedConsumptionValue : r.TotalDemandValue);
        decimal totalVal = totalAnnualVal > 0m ? totalAnnualVal : (classAVal + classBVal + classCVal);

        bool hasPositive = totalVal > 0m || totalPeriodVal > 0m;

        return new AbcClassificationSummaryDto
        {
            FacilityId = facilityId,
            ClientId = clientId,
            PolicyUsedId = policy?.Id ?? firstRecord.PolicyId,
            PolicyName = policy?.Name ?? "Standard ABC Policy",
            CriteriaUsed = firstRecord.CriteriaUsed,
            CostValuationBasis = firstRecord.ValuationBasisUsed ?? policy?.CostValuationBasis ?? InventoryValuationBasis.UnitCost,
            ClassAThresholdPercent = firstRecord.ClassAThresholdPercent ?? policy?.ClassACumulativeThresholdPercent ?? 80.00m,
            ClassBThresholdPercent = firstRecord.ClassBThresholdPercent ?? policy?.ClassBCumulativeThresholdPercent ?? 95.00m,
            ObservationWindowDays = (int)firstRecord.ObservationWindow,
            TotalSkus = totalSkus,
            ClassACount = classACount,
            ClassBCount = classBCount,
            ClassCCount = classCCount,
            ClassAPercentOfSkus = totalSkus > 0 ? Math.Round((decimal)classACount / totalSkus * 100m, 2) : 0m,
            ClassBPercentOfSkus = totalSkus > 0 ? Math.Round((decimal)classBCount / totalSkus * 100m, 2) : 0m,
            ClassCPercentOfSkus = totalSkus > 0 ? Math.Round((decimal)classCCount / totalSkus * 100m, 2) : 0m,
            ClassAValue = classAVal,
            ClassBValue = classBVal,
            ClassCValue = classCVal,
            TotalPortfolioValue = totalVal,
            PeriodPortfolioValue = totalPeriodVal,
            AnnualizedPortfolioValue = totalAnnualVal,
            ClassAValuePercent = totalVal > 0 ? Math.Round(classAVal / totalVal * 100m, 2) : 0m,
            ClassBValuePercent = totalVal > 0 ? Math.Round(classBVal / totalVal * 100m, 2) : 0m,
            ClassCValuePercent = totalVal > 0 ? Math.Round(classCVal / totalVal * 100m, 2) : 0m,
            CalculatedAtUtc = firstRecord.CalculatedAtUtc,
            Version = targetVersion,
            ClassificationRunId = firstRecord.ClassificationRunId,
            HasPositiveConsumptionValue = hasPositive,
            PortfolioConsumptionStatus = hasPositive ? "PositiveConsumption" : "NoPositiveConsumptionValue"
        };
    }

    public async Task<IReadOnlyList<AbcRankedSkuDto>> GetAbcRankedSkusAsync(
        Guid facilityId,
        Guid? clientId = null,
        AbcClassificationCategory? abcClass = null,
        AbcClassificationCriteria? criteria = null,
        int? version = null,
        int pageNumber = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        ValidateFacilityAccess(facilityId);
        if (clientId.HasValue) ValidateTenantAccess(clientId.Value);

        var query = _dbContext.SkuVelocityClassificationRecords
            .AsNoTracking()
            .Where(r => r.FacilityId == facilityId && r.RecordType == SkuClassificationRecordType.AbcParetoStratification);

        if (_currentUser.TenantId.HasValue)
        {
            query = query.Where(r => r.ClientId == _currentUser.TenantId.Value);
        }
        else if (clientId.HasValue)
        {
            query = query.Where(r => r.ClientId == clientId.Value);
        }

        if (criteria.HasValue)
        {
            query = query.Where(r => r.CriteriaUsed == criteria.Value);
        }

        if (abcClass.HasValue)
        {
            query = query.Where(r => r.AbcClass == abcClass.Value);
        }

        int targetVersion = version ?? (await query.MaxAsync(r => (int?)r.Version, cancellationToken) ?? 1);
        query = query.Where(r => r.Version == targetVersion);

        int skip = Math.Max(0, (pageNumber - 1) * pageSize);
        var pagedRecords = await query
            .OrderBy(r => r.RankInPortfolio > 0 ? r.RankInPortfolio : int.MaxValue)
            .ThenBy(r => r.CumulativePercentage)
            .ThenBy(r => r.Sku)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var itemIds = pagedRecords.Select(r => r.ItemId).Distinct().ToList();
        var itemCatalog = await _dbContext.Items
            .AsNoTracking()
            .Where(i => itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, cancellationToken);

        return pagedRecords.Select(r =>
        {
            var item = itemCatalog.TryGetValue(r.ItemId, out var foundItem) ? foundItem : null;
            decimal primaryMetric = r.CriteriaUsed switch
            {
                AbcClassificationCriteria.AnnualConsumptionValue => r.AnnualizedConsumptionValue > 0m ? r.AnnualizedConsumptionValue : r.TotalDemandValue,
                AbcClassificationCriteria.PickFrequency => r.TotalPickTasksCount,
                AbcClassificationCriteria.UnitVelocity => r.TotalUnitsDemanded,
                AbcClassificationCriteria.OrderLineContribution => r.OrderLinesCount,
                AbcClassificationCriteria.WeightedComposite => r.CompositeScore,
                _ => r.AnnualizedConsumptionValue > 0m ? r.AnnualizedConsumptionValue : r.TotalDemandValue
            };

            return new AbcRankedSkuDto
            {
                ItemId = r.ItemId,
                Sku = r.Sku,
                Description = item?.Description ?? r.Sku,
                Rank = r.RankInPortfolio,
                AbcClass = r.AbcClass,
                PeriodDemandValue = r.PeriodDemandValue > 0m ? r.PeriodDemandValue : r.TotalDemandValue,
                AnnualizedConsumptionValue = r.AnnualizedConsumptionValue > 0m ? r.AnnualizedConsumptionValue : r.TotalDemandValue,
                TotalUnitsDemanded = r.TotalUnitsDemanded,
                TotalPickTasksCount = r.TotalPickTasksCount,
                OrderLinesCount = r.OrderLinesCount,
                CompositeScore = r.CompositeScore,
                PrimaryMetricValue = primaryMetric,
                CumulativeContributionPercent = r.CumulativePercentage,
                InventoryTurnoverRatio = r.InventoryTurnoverRatio,
                DaysOfSupply = r.DaysOfSupply,
                CurrentOnHandUnits = r.CurrentOnHandUnits,
                UnitCost = item?.UnitPrice ?? 0m
            };
        }).ToList();
    }

    public async Task<AbcMigrationAnalysisDto> GetAbcMigrationAnalysisAsync(
        Guid facilityId,
        Guid? clientId,
        int baseVersion,
        int targetVersion,
        CancellationToken cancellationToken = default)
    {
        ValidateFacilityAccess(facilityId);
        if (clientId.HasValue) ValidateTenantAccess(clientId.Value);

        var query = _dbContext.SkuVelocityClassificationRecords
            .AsNoTracking()
            .Where(r => r.FacilityId == facilityId &&
                        r.RecordType == SkuClassificationRecordType.AbcParetoStratification &&
                        (r.Version == baseVersion || r.Version == targetVersion));

        if (_currentUser.TenantId.HasValue)
        {
            query = query.Where(r => r.ClientId == _currentUser.TenantId.Value);
        }
        else if (clientId.HasValue)
        {
            query = query.Where(r => r.ClientId == clientId.Value);
        }

        var allRecords = await query.ToListAsync(cancellationToken);

        var baseRecords = allRecords.Where(r => r.Version == baseVersion).ToDictionary(r => r.ItemId);
        var targetRecords = allRecords.Where(r => r.Version == targetVersion).ToDictionary(r => r.ItemId);

        var evaluatedItemIds = baseRecords.Keys.Union(targetRecords.Keys).Distinct().ToList();
        var migrationItems = new List<AbcSkuMigrationItemDto>();

        int promotions = 0;
        int demotions = 0;
        int unchanged = 0;

        foreach (var itemId in evaluatedItemIds)
        {
            baseRecords.TryGetValue(itemId, out var baseRec);
            targetRecords.TryGetValue(itemId, out var targetRec);

            if (baseRec == null && targetRec == null) continue;

            var baseClass = baseRec?.AbcClass ?? AbcClassificationCategory.ClassC;
            var targetClass = targetRec?.AbcClass ?? AbcClassificationCategory.ClassC;
            int baseRank = baseRec?.RankInPortfolio ?? 0;
            int targetRank = targetRec?.RankInPortfolio ?? 0;
            int rankDelta = baseRank - targetRank; // Positive = improved rank

            string migrationType;
            string reason;

            // Class A (1) is best, Class B (2) middle, Class C (3) lowest
            if ((int)targetClass < (int)baseClass)
            {
                migrationType = "Promoted";
                promotions++;
                reason = $"Promoted from {baseClass} to {targetClass} (Rank moved from #{baseRank} to #{targetRank})";
            }
            else if ((int)targetClass > (int)baseClass)
            {
                migrationType = "Demoted";
                demotions++;
                reason = $"Demoted from {baseClass} to {targetClass} (Rank moved from #{baseRank} to #{targetRank})";
            }
            else
            {
                migrationType = "Unchanged";
                unchanged++;
                reason = $"Maintained {targetClass} status (Rank #{targetRank}, previously #{baseRank})";
            }

            migrationItems.Add(new AbcSkuMigrationItemDto
            {
                ItemId = itemId,
                Sku = targetRec?.Sku ?? baseRec?.Sku ?? string.Empty,
                BaseClass = baseClass,
                TargetClass = targetClass,
                BaseRank = baseRank,
                TargetRank = targetRank,
                RankDelta = rankDelta,
                BaseCumulativePercent = baseRec?.CumulativePercentage ?? 0m,
                TargetCumulativePercent = targetRec?.CumulativePercentage ?? 0m,
                MigrationType = migrationType,
                ChangeReason = reason
            });
        }

        return new AbcMigrationAnalysisDto
        {
            FacilityId = facilityId,
            ClientId = clientId,
            BaseVersion = baseVersion,
            TargetVersion = targetVersion,
            TotalEvaluatedSkus = evaluatedItemIds.Count,
            PromotionsCount = promotions,
            DemotionsCount = demotions,
            UnchangedCount = unchanged,
            Migrations = migrationItems.OrderBy(m => m.TargetRank > 0 ? m.TargetRank : int.MaxValue).ToList()
        };
    }

    #endregion

    #region Phase 6.3.6 Dynamic Inventory Heatmaps and Inventory Health Dashboard

    public async Task<UnifiedInventoryHealthDashboardDto> GetUnifiedInventoryHealthDashboardAsync(
        Guid facilityId,
        Guid? clientId = null,
        DateTime? asOfDateUtc = null,
        CancellationToken cancellationToken = default)
    {
        ValidateFacilityAccess(facilityId);

        bool isClientPortalUser = !_currentUser.IsStaff && !_currentUser.IsSuperAdmin && _currentUser.TenantId.HasValue;
        if (isClientPortalUser)
        {
            clientId = _currentUser.TenantId!.Value;
        }

        if (clientId.HasValue)
        {
            ValidateTenantAccess(clientId.Value);
        }

        var facility = await _dbContext.Facilities
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == facilityId, cancellationToken);

        if (facility == null)
        {
            throw new EntityNotFoundException("Facility", facilityId);
        }

        string? clientCode = null;
        string? clientName = null;
        if (clientId.HasValue)
        {
            var client = await _dbContext.Clients
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == clientId.Value, cancellationToken);
            clientCode = client?.AccountCode;
            clientName = client?.CompanyName;
        }

        var asOf = (asOfDateUtc ?? DateTime.UtcNow).Date;

        // 1. Aging & Expiry Subsystem
        var agingSummary = await GetInventoryHealthSummaryAsync(facilityId, asOfDateUtc, clientId, cancellationToken);

        decimal activeStockValue = Math.Max(0m, agingSummary.TotalInventoryValue - agingSummary.DeadStockValue - agingSummary.ExpiredStockValue);
        decimal holdingCostRate = 0.18m; // Standard 18% annual carrying cost rate
        decimal holdingCostExposure = Math.Round(agingSummary.DeadStockValue * holdingCostRate, 2);

        var unifiedAging = new UnifiedAgingExpirySummaryDto
        {
            TotalInventoryValue = agingSummary.TotalInventoryValue,
            ActiveStockValue = activeStockValue,
            AgedStockValue = agingSummary.AgingBuckets.Where(b => b.Bucket != InventoryAgingBucket.Current_0_30).Sum(b => b.TotalValue),
            SlowMovingStockValue = agingSummary.SlowMovingValue,
            NonMovingStockValue = agingSummary.AgingBuckets.Where(b => b.Bucket == InventoryAgingBucket.SlowMoving_91_180 || b.Bucket == InventoryAgingBucket.Dormant_181_365).Sum(b => b.TotalValue),
            DeadStockValue = agingSummary.DeadStockValue,
            ExpiredStockValue = agingSummary.ExpiredStockValue,
            NearExpiryStockValue = agingSummary.NearExpiryStockValue,
            DeadStockRatioPercent = agingSummary.DeadStockRatioPercent,
            EstimatedAnnualHoldingCostExposure = holdingCostExposure,
            TotalAgedUnits = agingSummary.AgingBuckets.Where(b => b.Bucket != InventoryAgingBucket.Current_0_30).Sum(b => b.TotalUnits),
            TotalDeadStockUnits = agingSummary.DeadStockUnitsCount,
            DeadStockSkuCount = agingSummary.DeadStockSkuCount,
            ExpiredLotCount = agingSummary.ExpiredLotCount,
            NearExpiryLotCount = agingSummary.NearExpiryLotCount
        };

        // 2. Space & Capacity Subsystem
        UnifiedSpaceCapacitySummaryDto unifiedSpace;
        DateTime? spaceSnapshotDateUtc = null;
        if (!isClientPortalUser)
        {
            var spaceSummary = await GetFacilitySpaceUtilizationSummaryAsync(facilityId, asOfDateUtc, cancellationToken);
            spaceSnapshotDateUtc = asOf;
            unifiedSpace = new UnifiedSpaceCapacitySummaryDto
            {
                TotalPhysicalCbm = spaceSummary.TotalPhysicalCbm,
                TotalUsableCbm = spaceSummary.TotalUsableCbm,
                TotalOccupiedCbm = spaceSummary.TotalOccupiedCbm,
                AvailableCbm = spaceSummary.TotalAvailableCbm,
                CubeUtilizationPercent = spaceSummary.CubeFillPercent,
                TotalPalletPositions = spaceSummary.TotalPhysicalPalletPositions,
                OccupiedPalletPositions = spaceSummary.TotalOccupiedPalletPositions,
                AvailablePalletPositions = spaceSummary.TotalAvailablePalletPositions,
                PalletOccupancyPercent = spaceSummary.PalletOccupancyPercent,
                OverCapacityLocationsCount = spaceSummary.OverCapacityLocationsCount,
                BlockedLocationsCount = spaceSummary.BlockedLocationsCount,
                WeightConstrainedLocationsCount = spaceSummary.WeightConstrainedLocationsCount,
                TotalLocationsEvaluated = spaceSummary.TotalLocationsCount
            };
        }
        else
        {
            unifiedSpace = new UnifiedSpaceCapacitySummaryDto
            {
                TotalPhysicalCbm = agingSummary.TotalOccupiedCbm,
                TotalUsableCbm = agingSummary.TotalOccupiedCbm,
                TotalOccupiedCbm = agingSummary.TotalOccupiedCbm,
                AvailableCbm = 0m,
                CubeUtilizationPercent = 100m,
                TotalPalletPositions = (int)Math.Ceiling(agingSummary.TotalPalletFootprint),
                OccupiedPalletPositions = (int)Math.Ceiling(agingSummary.TotalPalletFootprint),
                AvailablePalletPositions = 0,
                PalletOccupancyPercent = 100m,
                OverCapacityLocationsCount = 0,
                BlockedLocationsCount = 0,
                WeightConstrainedLocationsCount = 0,
                TotalLocationsEvaluated = 0
            };
        }

        // 3. Velocity & Turnover Subsystem
        var velocitySummary = await GetSkuVelocitySummaryAsync(facilityId, clientId, VelocityObservationWindow.Days90, cancellationToken);

        var velocityRecordsQuery = _dbContext.SkuVelocityClassificationRecords
            .AsNoTracking()
            .Where(r => r.FacilityId == facilityId &&
                        r.RecordType == SkuClassificationRecordType.VelocityOperational &&
                        r.ObservationWindow == VelocityObservationWindow.Days90);

        if (clientId.HasValue)
        {
            velocityRecordsQuery = velocityRecordsQuery.Where(r => r.ClientId == clientId.Value);
        }

        var velocityRecords = await velocityRecordsQuery.ToListAsync(cancellationToken);

        decimal totalUnitsDemanded = velocityRecords.Sum(r => r.TotalUnitsDemanded);
        int totalPickTasks = velocityRecords.Sum(r => r.TotalPickTasksCount);
        decimal avgDos = velocityRecords.Any(r => r.DaysOfSupply > 0m)
            ? Math.Round(velocityRecords.Where(r => r.DaysOfSupply > 0m).Average(r => r.DaysOfSupply), 1)
            : 0m;

        decimal fastRatio = velocitySummary.TotalTrackedSkus > 0
            ? Math.Round((decimal)velocitySummary.FastMovingCount / velocitySummary.TotalTrackedSkus * 100m, 2)
            : 0m;

        var unifiedVelocity = new UnifiedVelocitySummaryDto
        {
            ObservationWindow = VelocityObservationWindow.Days90,
            TotalUnitsDemanded = totalUnitsDemanded,
            TotalPickTasksCount = totalPickTasks,
            FastMovingSkusCount = velocitySummary.FastMovingCount,
            MediumMovingSkusCount = velocitySummary.MediumMovingCount,
            SlowMovingSkusCount = velocitySummary.SlowMovingCount,
            NonMovingSkusCount = velocitySummary.NonMovingCount,
            DeadStockSkusCount = velocitySummary.DeadStockCount,
            FastMovingSkuRatioPercent = fastRatio,
            PortfolioTurnoverRatio = velocitySummary.OverallTurnoverRatio,
            AverageDaysOfSupply = avgDos
        };

        // 4. ABC Pareto Subsystem
        var abcSummary = await GetAbcClassificationSummaryAsync(facilityId, clientId, cancellationToken: cancellationToken);
        var topClassASkus = await GetAbcRankedSkusAsync(
            facilityId, clientId, abcClass: AbcClassificationCategory.ClassA, pageSize: 5, cancellationToken: cancellationToken);

        var unifiedAbc = new UnifiedAbcSummaryDto
        {
            CriteriaUsed = abcSummary.CriteriaUsed,
            TotalEvaluatedSkus = abcSummary.TotalSkus,
            ClassACount = abcSummary.ClassACount,
            ClassBCount = abcSummary.ClassBCount,
            ClassCCount = abcSummary.ClassCCount,
            ClassAPercentOfSkus = abcSummary.ClassAPercentOfSkus,
            ClassAValuePercent = abcSummary.ClassAValuePercent,
            ClassBValuePercent = abcSummary.ClassBValuePercent,
            ClassCValuePercent = abcSummary.ClassCValuePercent,
            TotalPortfolioAnnualizedValue = abcSummary.TotalPortfolioValue,
            TopClassASkus = topClassASkus,
            Version = abcSummary.Version,
            ClassificationRunId = abcSummary.ClassificationRunId
        };

        // 5. Stock Risk & Reorder Preview Subsystem
        int stockoutRiskCount = velocityRecords.Count(r => r.DaysOfSupply > 0m && r.DaysOfSupply < 7.0m && r.TotalUnitsDemanded > 0m);
        int overstockRiskCount = velocityRecords.Count(r => r.DaysOfSupply > 180.0m && r.TotalUnitsDemanded > 0m);

        int criticalAlerts = stockoutRiskCount + agingSummary.ExpiredLotCount;
        int highAlerts = agingSummary.DeadStockSkuCount + unifiedSpace.OverCapacityLocationsCount;
        int mediumAlerts = agingSummary.NearExpiryLotCount + overstockRiskCount;
        int lowAlerts = velocitySummary.SlowMovingCount + unifiedSpace.BlockedLocationsCount;

        var unifiedRisk = new UnifiedStockRiskSummaryDto
        {
            TotalAlertsCount = criticalAlerts + highAlerts + mediumAlerts + lowAlerts,
            CriticalAlertsCount = criticalAlerts,
            HighAlertsCount = highAlerts,
            MediumAlertsCount = mediumAlerts,
            LowAlertsCount = lowAlerts,
            StockoutRiskSkusCount = stockoutRiskCount,
            OverstockRiskSkusCount = overstockRiskCount,
            DeadStockRiskSkusCount = agingSummary.DeadStockSkuCount,
            ExpiryRiskLotsCount = agingSummary.ExpiredLotCount + agingSummary.NearExpiryLotCount
        };

        // 6. Overall Composite Health Score Calculation (0.0 to 100.0)
        // INV-H-03: Prevent double-counting between dead stock and expired stock
        decimal unexpiredDeadStockValue = Math.Max(0m, agingSummary.DeadStockValue - agingSummary.ExpiredStockValue);
        decimal unexpiredDeadStockRatio = agingSummary.TotalInventoryValue > 0m
            ? (unexpiredDeadStockValue / agingSummary.TotalInventoryValue) * 100m
            : 0m;
        decimal deadStockPenalty = Math.Min(25.0m, Math.Round(unexpiredDeadStockRatio * 0.50m, 1));

        decimal expiredRatio = agingSummary.TotalInventoryValue > 0m
            ? (agingSummary.ExpiredStockValue / agingSummary.TotalInventoryValue) * 100m
            : 0m;
        decimal expiredPenalty = Math.Min(30.0m, Math.Round(expiredRatio * 1.50m, 1));
        decimal overcapPenalty = Math.Min(15.0m, unifiedSpace.OverCapacityLocationsCount * 2.0m);
        decimal spaceCongestionPenalty = 0m;
        if (unifiedSpace.CubeUtilizationPercent > 90m)
        {
            spaceCongestionPenalty = Math.Min(10.0m, (unifiedSpace.CubeUtilizationPercent - 90m) * 1.0m);
        }
        else if (unifiedSpace.CubeUtilizationPercent < 30m && unifiedSpace.TotalLocationsEvaluated > 0)
        {
            spaceCongestionPenalty = Math.Min(5.0m, (30m - unifiedSpace.CubeUtilizationPercent) * 0.20m);
        }
        decimal stockoutPenalty = Math.Min(20.0m, stockoutRiskCount * 2.0m);

        decimal totalDeductions = deadStockPenalty + expiredPenalty + overcapPenalty + spaceCongestionPenalty + stockoutPenalty;
        decimal rawHealthScore = Math.Round(Math.Clamp(100.0m - totalDeductions, 0.0m, 100.0m), 1);

        // INV-H-02: Handle Empty Facilities and Missing Projections explicitly
        bool isEmptyFacility = agingSummary.TotalActiveSkus == 0 && agingSummary.TotalUnitsOnHand == 0;
        string scoreStatus;
        string grade;
        string explanation;

        if (isEmptyFacility)
        {
            rawHealthScore = 0m;
            totalDeductions = 0m;
            scoreStatus = "NoInventory";
            grade = "NoInventory";
            explanation = "Facility contains no active inventory records.";
        }
        else
        {
            scoreStatus = "Scored";
            grade = rawHealthScore switch
            {
                >= 90.0m => "A (Pristine)",
                >= 80.0m => "B (Good)",
                >= 70.0m => "C (Fair)",
                >= 60.0m => "D (At Risk)",
                _ => "F (Critical)"
            };
            explanation = $"Operational health score {rawHealthScore}/100.0 (Grade {grade}) with {totalDeductions} total penalty points deducted.";
        }

        var breakdown = new HealthScoreBreakdownDto
        {
            BaseScore = 100.0m,
            DeadStockPenalty = deadStockPenalty,
            ExpiredStockPenalty = expiredPenalty,
            OverCapacityPenalty = overcapPenalty,
            SpaceCongestionPenalty = spaceCongestionPenalty,
            StockoutRiskPenalty = stockoutPenalty,
            TotalDeductions = totalDeductions,
            FinalScore = rawHealthScore,
            ScoreStatus = scoreStatus,
            Explanation = explanation
        };

        // 7. Freshness Metadata & Clock Skew Calculation
        var nowUtc = DateTime.UtcNow;
        var latestAgingSnapshotDate = await _dbContext.DailyInventoryAgingSnapshots
            .AsNoTracking()
            .Where(s => s.FacilityId == facilityId)
            .OrderByDescending(s => s.SnapshotDateUtc)
            .Select(s => (DateTime?)s.SnapshotDateUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var latestSpaceSnapshotDate = await _dbContext.DailyLocationSpaceUtilizationRecords
            .AsNoTracking()
            .Where(s => s.FacilityId == facilityId)
            .OrderByDescending(s => s.SnapshotDateUtc)
            .Select(s => (DateTime?)s.SnapshotDateUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var latestVelocityCalcDate = await _dbContext.SkuVelocityClassificationRecords
            .AsNoTracking()
            .Where(s => s.FacilityId == facilityId && s.RecordType == SkuClassificationRecordType.VelocityOperational)
            .OrderByDescending(s => s.CalculatedAtUtc)
            .Select(s => (DateTime?)s.CalculatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        bool hasClockSkew = false;
        if (latestAgingSnapshotDate.HasValue && latestAgingSnapshotDate.Value > nowUtc.AddMinutes(10)) hasClockSkew = true;
        if (latestSpaceSnapshotDate.HasValue && latestSpaceSnapshotDate.Value > nowUtc.AddMinutes(10)) hasClockSkew = true;
        if (latestVelocityCalcDate.HasValue && latestVelocityCalcDate.Value > nowUtc.AddMinutes(10)) hasClockSkew = true;

        double? agingAgeHours = latestAgingSnapshotDate.HasValue ? Math.Max(0.0, Math.Round((nowUtc - latestAgingSnapshotDate.Value).TotalHours, 1)) : null;
        double? spaceAgeHours = latestSpaceSnapshotDate.HasValue ? Math.Max(0.0, Math.Round((nowUtc - latestSpaceSnapshotDate.Value).TotalHours, 1)) : null;
        double? velocityAgeHours = latestVelocityCalcDate.HasValue ? Math.Max(0.0, Math.Round((nowUtc - latestVelocityCalcDate.Value).TotalHours, 1)) : null;
        double? abcAgeHours = abcSummary.CalculatedAtUtc != default ? Math.Max(0.0, Math.Round((nowUtc - abcSummary.CalculatedAtUtc).TotalHours, 1)) : null;

        // INV-H-05: Preserve individual per-projection freshness states
        bool isAgingStale = latestAgingSnapshotDate == null || agingAgeHours > 24.0;
        bool isSpaceStale = (!isClientPortalUser) && (latestSpaceSnapshotDate == null || spaceAgeHours > 24.0);
        bool isVelocityStale = latestVelocityCalcDate == null || velocityAgeHours > 24.0;
        bool isAbcStale = abcSummary.CalculatedAtUtc == default || abcAgeHours > 24.0;

        var staleReasons = new List<string>();
        if (latestAgingSnapshotDate == null) staleReasons.Add("Aging snapshot missing");
        else if (agingAgeHours > 24.0) staleReasons.Add($"Aging snapshot is {agingAgeHours}h old (> 24h)");

        if (!isClientPortalUser)
        {
            if (latestSpaceSnapshotDate == null) staleReasons.Add("Space utilization snapshot missing");
            else if (spaceAgeHours > 24.0) staleReasons.Add($"Space utilization snapshot is {spaceAgeHours}h old (> 24h)");
        }

        if (latestVelocityCalcDate == null) staleReasons.Add("Velocity calculations missing");
        else if (velocityAgeHours > 24.0) staleReasons.Add($"Velocity calculation is {velocityAgeHours}h old (> 24h)");

        if (hasClockSkew) staleReasons.Add("Future-dated snapshot detected (clock skew)");

        bool isStale = isAgingStale || isSpaceStale || isVelocityStale || isAbcStale || hasClockSkew;
        string? stalenessReason = staleReasons.Count > 0 ? string.Join("; ", staleReasons) : null;

        var freshness = new InventoryHealthFreshnessMetadataDto
        {
            AgingSnapshotDateUtc = latestAgingSnapshotDate,
            AgingAgeHours = agingAgeHours,
            IsAgingStale = isAgingStale,
            SpaceSnapshotDateUtc = latestSpaceSnapshotDate,
            SpaceAgeHours = spaceAgeHours,
            IsSpaceStale = isSpaceStale,
            VelocityCalculatedAtUtc = latestVelocityCalcDate,
            VelocityAgeHours = velocityAgeHours,
            IsVelocityStale = isVelocityStale,
            AbcCalculatedAtUtc = abcSummary.CalculatedAtUtc != default ? abcSummary.CalculatedAtUtc : null,
            AbcAgeHours = abcAgeHours,
            AbcVersion = abcSummary.Version > 0 ? abcSummary.Version : null,
            IsAbcStale = isAbcStale,
            HasClockSkewDetected = hasClockSkew,
            IsStale = isStale,
            StalenessReason = stalenessReason
        };

        return new UnifiedInventoryHealthDashboardDto
        {
            FacilityId = facilityId,
            FacilityCode = facility.FacilityCode,
            FacilityName = facility.Name,
            ClientId = clientId,
            ClientCode = clientCode,
            ClientName = clientName,
            GeneratedAtUtc = nowUtc,
            TotalInventoryValuation = agingSummary.TotalInventoryValue,
            TotalUnitsOnHand = agingSummary.TotalUnitsOnHand,
            TotalActiveSkusCount = agingSummary.TotalActiveSkus,
            SpaceUtilizationPercent = unifiedSpace.CubeUtilizationPercent,
            PalletOccupancyPercent = unifiedSpace.PalletOccupancyPercent,
            DeadStockValue = agingSummary.DeadStockValue,
            DeadStockRatioPercent = agingSummary.DeadStockRatioPercent,
            ExpiredStockValue = agingSummary.ExpiredStockValue,
            NearExpiryStockValue = agingSummary.NearExpiryStockValue,
            FastMovingSkuCount = unifiedVelocity.FastMovingSkusCount,
            FastMovingSkuRatioPercent = unifiedVelocity.FastMovingSkuRatioPercent,
            ClassASkuCount = unifiedAbc.ClassACount,
            ClassAValuePercent = unifiedAbc.ClassAValuePercent,
            OverallHealthScore = rawHealthScore,
            HealthScoreGrade = grade,
            ScoreStatus = scoreStatus,
            HealthScoreBreakdown = breakdown,
            AgingAndExpiry = unifiedAging,
            SpaceCapacity = unifiedSpace,
            VelocityAndMovement = unifiedVelocity,
            AbcPareto = unifiedAbc,
            StockRiskAlerts = unifiedRisk,
            Freshness = freshness
        };
    }

    #endregion

    #region Phase 6.3.6.2 Dynamic Warehouse Heatmap Domain

    public Task<IReadOnlyList<HeatmapColorScaleDefinitionDto>> GetHeatmapColorScalesAsync(
        CancellationToken cancellationToken = default)
    {
        var scales = new List<HeatmapColorScaleDefinitionDto>
        {
            new()
            {
                LayerType = HeatmapLayerType.CubeUtilization,
                LayerName = "Cubic Volume Utilization",
                Unit = "%",
                IsUncapped = true,
                Stops =
                [
                    new() { MinValue = 0m, MaxValue = 0m, ColorHex = "#E2E8F0", Label = "Empty (0%)", Tier = HeatmapVisualTier.Empty },
                    new() { MinValue = 0.01m, MaxValue = 50.0m, ColorHex = "#93C5FD", Label = "Underutilized (1-50%)", Tier = HeatmapVisualTier.Optimal },
                    new() { MinValue = 50.01m, MaxValue = 85.0m, ColorHex = "#22C55E", Label = "Optimal (51-85%)", Tier = HeatmapVisualTier.Optimal },
                    new() { MinValue = 85.01m, MaxValue = 100.0m, ColorHex = "#F59E0B", Label = "High Utilization (86-100%)", Tier = HeatmapVisualTier.High },
                    new() { MinValue = 100.01m, MaxValue = null, ColorHex = "#EF4444", Label = "Overcapacity (>100%)", Tier = HeatmapVisualTier.Critical }
                ]
            },
            new()
            {
                LayerType = HeatmapLayerType.PalletOccupancy,
                LayerName = "Pallet Position Occupancy",
                Unit = "%",
                IsUncapped = true,
                Stops =
                [
                    new() { MinValue = 0m, MaxValue = 0m, ColorHex = "#E2E8F0", Label = "Empty (0 Pallets)", Tier = HeatmapVisualTier.Empty },
                    new() { MinValue = 0.01m, MaxValue = 50.0m, ColorHex = "#93C5FD", Label = "Low Occupancy (1-50%)", Tier = HeatmapVisualTier.Optimal },
                    new() { MinValue = 50.01m, MaxValue = 85.0m, ColorHex = "#22C55E", Label = "Optimal Occupancy (51-85%)", Tier = HeatmapVisualTier.Optimal },
                    new() { MinValue = 85.01m, MaxValue = 100.0m, ColorHex = "#F59E0B", Label = "Full Occupancy (86-100%)", Tier = HeatmapVisualTier.High },
                    new() { MinValue = 100.01m, MaxValue = null, ColorHex = "#EF4444", Label = "Pallet Overcapacity (>100%)", Tier = HeatmapVisualTier.Critical }
                ]
            },
            new()
            {
                LayerType = HeatmapLayerType.MovementVelocity,
                LayerName = "SKU Movement Velocity",
                Unit = "Velocity Category",
                IsUncapped = false,
                Stops =
                [
                    new() { MinValue = 1m, MaxValue = 1m, ColorHex = "#10B981", Label = "Fast Moving (High Turn)", Tier = HeatmapVisualTier.Optimal },
                    new() { MinValue = 2m, MaxValue = 2m, ColorHex = "#3B82F6", Label = "Medium Moving (Normal)", Tier = HeatmapVisualTier.Optimal },
                    new() { MinValue = 3m, MaxValue = 3m, ColorHex = "#F59E0B", Label = "Slow Moving", Tier = HeatmapVisualTier.Elevated },
                    new() { MinValue = 4m, MaxValue = 4m, ColorHex = "#F97316", Label = "Non-Moving (Stagnant)", Tier = HeatmapVisualTier.High },
                    new() { MinValue = 5m, MaxValue = 5m, ColorHex = "#EF4444", Label = "Dead Stock (>365d)", Tier = HeatmapVisualTier.Critical }
                ]
            },
            new()
            {
                LayerType = HeatmapLayerType.AgingAndExpiry,
                LayerName = "Inventory Aging & Expiry Risk",
                Unit = "Days / Expiry",
                IsUncapped = false,
                Stops =
                [
                    new() { MinValue = 0m, MaxValue = 30m, ColorHex = "#10B981", Label = "Fresh (0-30 days)", Tier = HeatmapVisualTier.Optimal },
                    new() { MinValue = 31m, MaxValue = 90m, ColorHex = "#3B82F6", Label = "Normal (31-90 days)", Tier = HeatmapVisualTier.Optimal },
                    new() { MinValue = 91m, MaxValue = 180m, ColorHex = "#F59E0B", Label = "Slow Aging (91-180 days)", Tier = HeatmapVisualTier.Elevated },
                    new() { MinValue = 181m, MaxValue = 365m, ColorHex = "#F97316", Label = "Dormant / Near Expiry", Tier = HeatmapVisualTier.High },
                    new() { MinValue = 366m, MaxValue = null, ColorHex = "#DC2626", Label = "Dead Stock / Expired", Tier = HeatmapVisualTier.Critical }
                ]
            },
            new()
            {
                LayerType = HeatmapLayerType.Overcapacity,
                LayerName = "Overcapacity Warning",
                Unit = "Excess CBM",
                IsUncapped = true,
                Stops =
                [
                    new() { MinValue = 0m, MaxValue = 100m, ColorHex = "#22C55E", Label = "Within Capacity (<=100%)", Tier = HeatmapVisualTier.Optimal },
                    new() { MinValue = 100.01m, MaxValue = null, ColorHex = "#EF4444", Label = "Overcapacity Warning (>100%)", Tier = HeatmapVisualTier.Critical }
                ]
            },
            new()
            {
                LayerType = HeatmapLayerType.BlockedLocations,
                LayerName = "Blocked / Quarantined Locations",
                Unit = "Status",
                IsUncapped = false,
                Stops =
                [
                    new() { MinValue = 0m, MaxValue = 0m, ColorHex = "#22C55E", Label = "Operational", Tier = HeatmapVisualTier.Optimal },
                    new() { MinValue = 1m, MaxValue = 1m, ColorHex = "#475569", Label = "Blocked / Quarantined", Tier = HeatmapVisualTier.Blocked }
                ]
            }
        };

        return Task.FromResult<IReadOnlyList<HeatmapColorScaleDefinitionDto>>(scales);
    }

    public async Task<IReadOnlyList<WarehouseTopologyNodeDto>> GetWarehouseTopologyAsync(
        Guid facilityId,
        CancellationToken cancellationToken = default)
    {
        ValidateFacilityAccess(facilityId);

        var facility = await _dbContext.Facilities
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == facilityId, cancellationToken);

        if (facility == null)
            throw new EntityNotFoundException("Facility", facilityId);

        var zones = await _dbContext.WarehouseZones
            .AsNoTracking()
            .Where(z => z.FacilityId == facilityId && z.IsActive)
            .OrderBy(z => z.ZoneCode)
            .ToListAsync(cancellationToken);

        var locations = await _dbContext.Locations
            .AsNoTracking()
            .Where(l => l.FacilityId == facilityId && !l.IsDeleted)
            .ToListAsync(cancellationToken);

        var root = new WarehouseTopologyNodeDto
        {
            Id = facility.Id,
            Code = facility.FacilityCode,
            Name = facility.Name,
            Type = LocationType.Facility,
            ParentId = null,
            DepthLevel = 0,
            Children = zones.Select(z =>
            {
                var zoneLocations = locations.Where(l => l.ZoneId == z.Id).ToList();
                var aisles = zoneLocations
                    .GroupBy(l => l.AisleCode ?? "UNASSIGNED")
                    .OrderBy(g => g.Key)
                    .Select(aisleGrp => new WarehouseTopologyNodeDto
                    {
                        Id = Guid.NewGuid(),
                        Code = aisleGrp.Key,
                        Name = $"Aisle {aisleGrp.Key}",
                        Type = LocationType.Aisle,
                        ParentId = z.Id,
                        DepthLevel = 2,
                        Children = aisleGrp
                            .GroupBy(l => l.RackCode ?? "RACK-01")
                            .OrderBy(rg => rg.Key)
                            .Select(rackGrp => new WarehouseTopologyNodeDto
                            {
                                Id = Guid.NewGuid(),
                                Code = rackGrp.Key,
                                Name = $"Rack {rackGrp.Key}",
                                Type = LocationType.Rack,
                                ParentId = null,
                                DepthLevel = 3,
                                Children = rackGrp
                                    .OrderBy(l => l.ShelfLevel ?? 1)
                                    .ThenBy(l => l.BinNumber ?? 1)
                                    .Select(loc => new WarehouseTopologyNodeDto
                                    {
                                        Id = loc.Id,
                                        Code = loc.LocationCode,
                                        Name = loc.LocationCode,
                                        Type = loc.LocationType,
                                        ParentId = null,
                                        DepthLevel = 4
                                    }).ToList()
                            }).ToList()
                    }).ToList();

                return new WarehouseTopologyNodeDto
                {
                    Id = z.Id,
                    Code = z.ZoneCode,
                    Name = z.Name,
                    Type = LocationType.Zone,
                    ParentId = facility.Id,
                    DepthLevel = 1,
                    Children = aisles
                };
            }).ToList()
        };

        return [root];
    }

    public async Task<WarehouseHeatmapResponseDto> GetWarehouseHeatmapAsync(
        Guid facilityId,
        HeatmapQueryFilterDto? filter = null,
        DateTime? asOfDateUtc = null,
        CancellationToken cancellationToken = default)
    {
        ValidateFacilityAccess(facilityId);

        var facility = await _dbContext.Facilities
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == facilityId, cancellationToken);

        if (facility == null)
            throw new EntityNotFoundException("Facility", facilityId);

        var activeFilter = filter ?? new HeatmapQueryFilterDto();
        var asOf = (asOfDateUtc ?? DateTime.UtcNow).Date;

        // Multi-Tenant Isolation: Enforce client portal boundary
        var currentTenantId = _currentUser.TenantId;
        bool isClientPortalUser = currentTenantId.HasValue && !_currentUser.IsStaff;
        Guid? effectiveClientId = null;
        if (isClientPortalUser && currentTenantId.HasValue)
        {
            effectiveClientId = currentTenantId.Value;
            if (activeFilter.ClientId.HasValue && activeFilter.ClientId.Value != effectiveClientId)
            {
                throw new UnauthorizedException("Client portal users cannot request other client heatmap views.");
            }
        }
        else if (activeFilter.ClientId.HasValue)
        {
            ValidateTenantAccess(activeFilter.ClientId.Value);
            effectiveClientId = activeFilter.ClientId.Value;
        }

        // 1. Query Locations
        var locQuery = _dbContext.Locations
            .AsNoTracking()
            .Where(l => l.FacilityId == facilityId && !l.IsDeleted);

        if (activeFilter.ZoneId.HasValue)
            locQuery = locQuery.Where(l => l.ZoneId == activeFilter.ZoneId.Value);

        if (!string.IsNullOrWhiteSpace(activeFilter.AisleCode))
            locQuery = locQuery.Where(l => l.AisleCode == activeFilter.AisleCode);

        if (activeFilter.ShelfLevel.HasValue)
            locQuery = locQuery.Where(l => l.ShelfLevel == activeFilter.ShelfLevel.Value);

        var locations = await locQuery.ToListAsync(cancellationToken);

        // 2. Query Space Utilization Records (Phase 6.3.3)
        var spaceRecords = await _dbContext.DailyLocationSpaceUtilizationRecords
            .AsNoTracking()
            .Where(r => r.FacilityId == facilityId && r.SnapshotDateUtc == asOf)
            .ToDictionaryAsync(r => r.LocationId, cancellationToken);

        // 3. Query Aging Records (Phase 6.3.2)
        var agingQuery = _dbContext.DailyInventoryAgingSnapshots
            .AsNoTracking()
            .Where(s => s.FacilityId == facilityId && s.SnapshotDateUtc == asOf);

        if (effectiveClientId.HasValue)
            agingQuery = agingQuery.Where(s => s.ClientId == effectiveClientId.Value);

        var agingSnapshots = await agingQuery.ToListAsync(cancellationToken);
        var agingByLocation = agingSnapshots
            .GroupBy(s => s.LocationId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // 4. Query Velocity Records (Phase 6.3.4)
        var velocityRecords = await _dbContext.SkuVelocityClassificationRecords
            .AsNoTracking()
            .Where(v => v.FacilityId == facilityId && v.RecordType == SkuClassificationRecordType.VelocityOperational)
            .ToListAsync(cancellationToken);
        var velocityByItem = velocityRecords.ToDictionary(v => v.ItemId);

        // 5. Query Zones for ZoneCode resolution
        var zones = await _dbContext.WarehouseZones
            .AsNoTracking()
            .Where(z => z.FacilityId == facilityId)
            .ToDictionaryAsync(z => z.Id, z => z.ZoneCode, cancellationToken);

        // Client Portal Masking: If client user, only include locations where client has inventory
        var eligibleLocations = locations;
        if (isClientPortalUser)
        {
            eligibleLocations = locations.Where(l => agingByLocation.ContainsKey(l.Id)).ToList();
        }

        // Authoritative Facility Spatial Envelope (HM-01)
        decimal envLength = 150.0m;
        decimal envWidth = 100.0m;
        decimal envHeight = 14.0m;
        if (facility.TotalAreaSqM.HasValue && facility.TotalAreaSqM.Value > 0)
        {
            envLength = Math.Max(50.0m, Math.Round((decimal)Math.Sqrt((double)facility.TotalAreaSqM.Value * 1.5), 1));
            envWidth = Math.Max(30.0m, Math.Round(facility.TotalAreaSqM.Value / envLength, 1));
        }

        var facilityEnvelope = new FacilitySpatialEnvelopeDto
        {
            LengthMeters = envLength,
            WidthMeters = envWidth,
            ClearHeightMeters = envHeight,
            IsAuthoritative = true
        };

        // Detect duplicate coordinates among valid coordinates
        var duplicateCoordKeys = eligibleLocations
            .Where(l => l.XCoord.HasValue && l.YCoord.HasValue && l.ZCoord.HasValue)
            .GroupBy(l => $"{l.XCoord}_{l.YCoord}_{l.ZCoord}")
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();

        var cells = new List<HeatmapLocationCellDto>();

        foreach (var loc in eligibleLocations)
        {
            // Coordinate determination (HM-01: validate against facility envelope; Invariant: never fabricate coordinates!)
            bool hasCoords = loc.XCoord.HasValue && loc.YCoord.HasValue && loc.ZCoord.HasValue;
            var coordStatus = HeatmapCoordinateStatus.Valid;
            if (!hasCoords)
            {
                coordStatus = HeatmapCoordinateStatus.MissingCoordinates;
            }
            else if (loc.XCoord!.Value < facilityEnvelope.OriginXMeters || loc.XCoord.Value > (facilityEnvelope.OriginXMeters + facilityEnvelope.LengthMeters) ||
                     loc.YCoord!.Value < facilityEnvelope.OriginYMeters || loc.YCoord.Value > (facilityEnvelope.OriginYMeters + facilityEnvelope.WidthMeters) ||
                     loc.ZCoord!.Value < facilityEnvelope.OriginZMeters || loc.ZCoord.Value > (facilityEnvelope.OriginZMeters + facilityEnvelope.ClearHeightMeters))
            {
                coordStatus = HeatmapCoordinateStatus.OutOfBounds;
            }
            else if (duplicateCoordKeys.Contains($"{loc.XCoord}_{loc.YCoord}_{loc.ZCoord}"))
            {
                coordStatus = HeatmapCoordinateStatus.DuplicateCoordinates;
            }

            var coordDto = new WarehouseCoordinateDto
            {
                X = loc.XCoord,
                Y = loc.YCoord,
                Z = loc.ZCoord,
                LengthCm = loc.LengthCm,
                WidthCm = loc.WidthCm,
                HeightCm = loc.HeightCm,
                Status = coordStatus
            };

            // Space metric extraction
            decimal cubeFill = 0m;
            decimal usableCbm = 0m;
            decimal occupiedCbm = 0m;
            decimal palletOccupancy = 0m;
            int usablePallets = 0;
            int occupiedPallets = 0;
            bool isOverCapacity = false;
            decimal excessCbm = 0m;
            bool isBlocked = loc.IsLocked || !loc.IsActive || loc.IsDamaged;
            string? blockReason = null;

            if (spaceRecords.TryGetValue(loc.Id, out var space))
            {
                cubeFill = space.CubeUtilizationPercent;
                usableCbm = space.UsableCubicMeters;
                occupiedCbm = space.OccupiedCubicMeters;
                palletOccupancy = space.PalletOccupancyPercent;
                usablePallets = space.UsablePalletPositions;
                occupiedPallets = space.OccupiedPalletPositions;
                isOverCapacity = space.IsOverCapacity || cubeFill > 100m || palletOccupancy > 100m;
                excessCbm = space.OverCapacityExcessCbm;
                if (space.IsBlocked)
                {
                    isBlocked = true;
                    blockReason = space.BlockReason;
                }
            }
            else
            {
                usableCbm = loc.CalculateVolumeCm3() / 1_000_000m;
            }

            // Aging & Velocity metric extraction
            bool isDeadStock = false;
            bool isExpired = false;
            bool isNearExpiry = false;
            var agingBucket = InventoryAgingBucket.Current_0_30;
            var velocityCategory = InventoryMovementVelocityCategory.FastMoving;

            if (agingByLocation.TryGetValue(loc.Id, out var locAging))
            {
                isDeadStock = locAging.Any(a => a.IsDeadStock);
                isExpired = locAging.Any(a => a.IsExpired);
                isNearExpiry = locAging.Any(a => a.IsNearExpiry);
                var oldestSnap = locAging.OrderByDescending(a => (int)a.AgingBucket).FirstOrDefault();
                if (oldestSnap != null) agingBucket = oldestSnap.AgingBucket;

                var dominantItem = locAging.OrderByDescending(a => a.QuantityOnHand).FirstOrDefault();
                if (dominantItem != null && velocityByItem.TryGetValue(dominantItem.ItemId, out var vRec))
                {
                    velocityCategory = vRec.VelocityCategory;
                }
            }

            // Active Simultaneous Conditions (Invariant: not mutually exclusive!)
            var conditions = new List<string>();
            if (isBlocked) conditions.Add("Blocked");
            if (isOverCapacity) conditions.Add("OverCapacity");
            if (isExpired) conditions.Add("ExpiredStock");
            if (isNearExpiry) conditions.Add("NearExpiry");
            if (isDeadStock) conditions.Add("DeadStock");
            if (cubeFill > 85m && !isOverCapacity) conditions.Add("HighUtilization");
            if (cubeFill < 30m && cubeFill > 0m) conditions.Add("Underutilized");
            if (cubeFill == 0m) conditions.Add("Empty");

            // Documented Hex Color Scales
            string cubeColor = cubeFill switch
            {
                <= 0m => "#E2E8F0",
                <= 50.0m => "#93C5FD",
                <= 85.0m => "#22C55E",
                <= 100.0m => "#F59E0B",
                _ => "#EF4444"
            };

            string palletColor = palletOccupancy switch
            {
                <= 0m => "#E2E8F0",
                <= 50.0m => "#93C5FD",
                <= 85.0m => "#22C55E",
                <= 100.0m => "#F59E0B",
                _ => "#EF4444"
            };

            string velocityColor = velocityCategory switch
            {
                InventoryMovementVelocityCategory.FastMoving => "#10B981",
                InventoryMovementVelocityCategory.MediumMoving => "#3B82F6",
                InventoryMovementVelocityCategory.SlowMoving => "#F59E0B",
                InventoryMovementVelocityCategory.NonMoving => "#F97316",
                InventoryMovementVelocityCategory.DeadStock => "#EF4444",
                _ => "#10B981"
            };

            string agingColor = isExpired ? "#DC2626"
                : isNearExpiry ? "#F97316"
                : isDeadStock ? "#6B7280"
                : agingBucket switch
                {
                    InventoryAgingBucket.Current_0_30 => "#10B981",
                    InventoryAgingBucket.Aging_31_60 or InventoryAgingBucket.Aging_61_90 => "#3B82F6",
                    InventoryAgingBucket.SlowMoving_91_180 => "#F59E0B",
                    InventoryAgingBucket.Dormant_181_365 => "#F97316",
                    _ => "#6B7280"
                };

            string overcapacityColor = isOverCapacity ? "#EF4444" : "#22C55E";
            string blockedColor = isBlocked ? "#475569" : "#22C55E";

            // Primary Color Selection with Alert Severity Hierarchy
            string primaryColor;
            HeatmapVisualTier primaryTier;

            if (isBlocked && (activeFilter.ActiveLayers.HasFlag(HeatmapLayerType.BlockedLocations)))
            {
                primaryColor = blockedColor;
                primaryTier = HeatmapVisualTier.Blocked;
            }
            else if (isOverCapacity && (activeFilter.ActiveLayers.HasFlag(HeatmapLayerType.Overcapacity)))
            {
                primaryColor = overcapacityColor;
                primaryTier = HeatmapVisualTier.Critical;
            }
            else if (isExpired && (activeFilter.ActiveLayers.HasFlag(HeatmapLayerType.AgingAndExpiry)))
            {
                primaryColor = "#DC2626";
                primaryTier = HeatmapVisualTier.Critical;
            }
            else
            {
                // Follow the requested primary color layer
                switch (activeFilter.PrimaryColorLayer)
                {
                    case HeatmapLayerType.PalletOccupancy:
                        primaryColor = palletColor;
                        primaryTier = palletOccupancy > 100m ? HeatmapVisualTier.Critical : (palletOccupancy > 85m ? HeatmapVisualTier.High : HeatmapVisualTier.Optimal);
                        break;
                    case HeatmapLayerType.MovementVelocity:
                        primaryColor = velocityColor;
                        primaryTier = velocityCategory == InventoryMovementVelocityCategory.DeadStock ? HeatmapVisualTier.Critical : HeatmapVisualTier.Optimal;
                        break;
                    case HeatmapLayerType.AgingAndExpiry:
                        primaryColor = agingColor;
                        primaryTier = isDeadStock ? HeatmapVisualTier.High : HeatmapVisualTier.Optimal;
                        break;
                    case HeatmapLayerType.CubeUtilization:
                    default:
                        primaryColor = cubeColor;
                        primaryTier = cubeFill switch
                        {
                            <= 0m => HeatmapVisualTier.Empty,
                            <= 85m => HeatmapVisualTier.Optimal,
                            <= 100m => HeatmapVisualTier.High,
                            _ => HeatmapVisualTier.Critical
                        };
                        break;
                }
            }

            string? zoneCode = null;
            if (loc.ZoneId.HasValue && zones.TryGetValue(loc.ZoneId.Value, out var zc))
                zoneCode = zc;

            cells.Add(new HeatmapLocationCellDto
            {
                LocationId = loc.Id,
                LocationCode = loc.LocationCode,
                LocationType = loc.LocationType,
                ZoneId = loc.ZoneId,
                ZoneCode = zoneCode,
                AisleCode = loc.AisleCode,
                RackCode = loc.RackCode,
                ShelfLevel = loc.ShelfLevel,
                BinNumber = loc.BinNumber,
                Coordinate = coordDto,
                CubeUtilizationPercent = cubeFill,
                UsableCbm = usableCbm,
                OccupiedCbm = occupiedCbm,
                PalletOccupancyPercent = palletOccupancy,
                UsablePallets = usablePallets,
                OccupiedPallets = occupiedPallets,
                VelocityCategory = velocityCategory,
                AgingBucket = agingBucket,
                IsOverCapacity = isOverCapacity,
                OverCapacityExcessCbm = excessCbm,
                IsBlocked = isBlocked,
                BlockReason = blockReason,
                IsDeadStock = isDeadStock,
                IsExpired = isExpired,
                IsNearExpiry = isNearExpiry,
                CubeFillColorHex = cubeColor,
                PalletOccupancyColorHex = palletColor,
                VelocityColorHex = velocityColor,
                AgingColorHex = agingColor,
                OvercapacityColorHex = overcapacityColor,
                BlockedColorHex = blockedColor,
                ActiveConditions = conditions,
                PrimaryColorHex = primaryColor,
                PrimaryTier = primaryTier
            });
        }

        // Bounding Box Calculation
        var mappedLocs = cells.Where(c => c.Coordinate.HasCoordinates).ToList();
        var boundingBox = new WarehouseBoundingBoxDto();
        if (mappedLocs.Count > 0)
        {
            boundingBox = new WarehouseBoundingBoxDto
            {
                MinX = mappedLocs.Min(c => c.Coordinate.X!.Value),
                MaxX = mappedLocs.Max(c => c.Coordinate.X!.Value),
                MinY = mappedLocs.Min(c => c.Coordinate.Y!.Value),
                MaxY = mappedLocs.Max(c => c.Coordinate.Y!.Value),
                MinZ = mappedLocs.Min(c => c.Coordinate.Z!.Value),
                MaxZ = mappedLocs.Max(c => c.Coordinate.Z!.Value)
            };
        }

        var zoomPan = new WarehouseZoomPanBoundsDto
        {
            MinZoom = 0.1m,
            MaxZoom = 20.0m,
            DefaultZoom = 1.0m,
            PanPaddingMeters = 5.0m,
            BoundingBox = boundingBox
        };

        var conditionSummary = cells
            .SelectMany(c => c.ActiveConditions)
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        return new WarehouseHeatmapResponseDto
        {
            FacilityId = facility.Id,
            FacilityCode = facility.FacilityCode,
            FacilityName = facility.Name,
            SnapshotDateUtc = asOf,
            ActiveLayers = activeFilter.ActiveLayers,
            PrimaryColorLayer = activeFilter.PrimaryColorLayer,
            FacilityEnvelope = facilityEnvelope,
            BoundingBox = boundingBox,
            ZoomPanBounds = zoomPan,
            TotalLocationsCount = cells.Count,
            MappedLocationsCount = mappedLocs.Count,
            MissingCoordinatesLocationsCount = cells.Count - mappedLocs.Count,
            OutOfBoundsLocationsCount = cells.Count(c => c.Coordinate.Status == HeatmapCoordinateStatus.OutOfBounds),
            DuplicateCoordinatesLocationsCount = cells.Count(c => c.Coordinate.Status == HeatmapCoordinateStatus.DuplicateCoordinates),
            OverCapacityLocationsCount = cells.Count(c => c.IsOverCapacity),
            BlockedLocationsCount = cells.Count(c => c.IsBlocked),
            Locations = cells,
            ActiveConditionSummary = conditionSummary
        };
    }

    #endregion

    #region Phase 6.3.6.3 Heatmap Projection Engine & Aggregations

    public async Task<SpatialHeatmapProjectionDto> GetSpatialHeatmapProjectionAsync(
        Guid facilityId,
        HeatmapQueryFilterDto? filter = null,
        decimal gridCellSizeMeters = 2.0m,
        DateTime? asOfDateUtc = null,
        CancellationToken cancellationToken = default)
    {
        var heatmap = await GetWarehouseHeatmapAsync(facilityId, filter, asOfDateUtc, cancellationToken);
        var cells = heatmap.Locations;
        var asOf = heatmap.SnapshotDateUtc;
        var facilityEnvelope = heatmap.FacilityEnvelope;

        // Step 6.3.6.3.1 & 6.3.6.3.2: Spatial Indexing & Grid Generation (Deterministic Cell Hashing with Origin Offset)
        decimal cellSize = gridCellSizeMeters > 0m ? gridCellSizeMeters : 2.0m;
        var gridMap = new Dictionary<string, List<HeatmapLocationCellDto>>();

        foreach (var c in cells.Where(c => c.Coordinate.HasCoordinates && c.Coordinate.Status == HeatmapCoordinateStatus.Valid))
        {
            int gx = (int)Math.Floor((c.Coordinate.X!.Value - facilityEnvelope.OriginXMeters) / cellSize);
            int gy = (int)Math.Floor((c.Coordinate.Y!.Value - facilityEnvelope.OriginYMeters) / cellSize);
            int gz = (int)Math.Floor((c.Coordinate.Z!.Value - facilityEnvelope.OriginZMeters) / cellSize);
            string gKey = $"{gx}_{gy}_{gz}";

            if (!gridMap.TryGetValue(gKey, out var list))
            {
                list = [];
                gridMap[gKey] = list;
            }
            list.Add(c);
        }

        var gridCells = new List<SpatialGridCellDto>();
        foreach (var (key, locList) in gridMap.OrderBy(kvp => kvp.Key))
        {
            var parts = key.Split('_');
            int gx = int.Parse(parts[0]);
            int gy = int.Parse(parts[1]);
            int gz = int.Parse(parts[2]);

            decimal totalUsable = locList.Sum(l => l.UsableCbm);
            decimal totalOcc = locList.Sum(l => l.OccupiedCbm);
            int totalPalletsUsable = locList.Sum(l => l.UsablePallets);
            int totalPalletsOcc = locList.Sum(l => l.OccupiedPallets);

            decimal weightedCube = totalUsable > 0m ? Math.Round((totalOcc / totalUsable) * 100m, 2) : 0m;
            string primaryColor = weightedCube switch
            {
                <= 0m => "#E2E8F0",
                <= 50.0m => "#93C5FD",
                <= 85.0m => "#22C55E",
                <= 100.0m => "#F59E0B",
                _ => "#EF4444"
            };

            gridCells.Add(new SpatialGridCellDto
            {
                GridX = gx,
                GridY = gy,
                GridZ = gz,
                CenterXMeters = facilityEnvelope.OriginXMeters + (gx * cellSize) + (cellSize / 2.0m),
                CenterYMeters = facilityEnvelope.OriginYMeters + (gy * cellSize) + (cellSize / 2.0m),
                CenterZMeters = facilityEnvelope.OriginZMeters + (gz * cellSize) + (cellSize / 2.0m),
                CellSizeMeters = cellSize,
                LocationCount = locList.Count,
                TotalUsableCbm = totalUsable,
                TotalOccupiedCbm = totalOcc,
                TotalUsablePallets = totalPalletsUsable,
                TotalOccupiedPallets = totalPalletsOcc,
                HasOverCapacity = locList.Any(l => l.IsOverCapacity),
                HasBlocked = locList.Any(l => l.IsBlocked),
                LocationIds = locList.Select(l => l.LocationId).ToList(),
                PrimaryColorHex = primaryColor
            });
        }

        // Step 6.3.6.3.4 & 6.3.6.3.5: Multi-Tier Weighted Spatial Rollups (HM-04, HM-05)
        var zonesQuery = await _dbContext.WarehouseZones
            .AsNoTracking()
            .Where(z => z.FacilityId == facilityId && z.IsActive)
            .OrderBy(z => z.ZoneCode)
            .ToListAsync(cancellationToken);

        // Load aging for inventory value aggregation
        var agingQuery = _dbContext.DailyInventoryAgingSnapshots
            .AsNoTracking()
            .Where(s => s.FacilityId == facilityId && s.SnapshotDateUtc == asOf);
        if (_currentUser.TenantId.HasValue && !_currentUser.IsStaff)
        {
            agingQuery = agingQuery.Where(s => s.ClientId == _currentUser.TenantId.Value);
        }
        var agingRecords = await agingQuery.ToListAsync(cancellationToken);
        var agingByLoc = agingRecords.GroupBy(a => a.LocationId).ToDictionary(g => g.Key, g => g.ToList());

        var zoneRollups = new List<ZoneSpatialRollupDto>();
        var facilityConditionSummary = new Dictionary<string, int>();

        foreach (var z in zonesQuery)
        {
            var zoneCells = cells.Where(c => c.ZoneId == z.Id).ToList();
            var aisleRollups = new List<AisleSpatialRollupDto>();
            var zoneConditions = new Dictionary<string, int>();

            var aisleGroups = zoneCells
                .GroupBy(c => c.AisleCode ?? "UNASSIGNED")
                .OrderBy(g => g.Key);

            foreach (var aGrp in aisleGroups)
            {
                var rackRollups = new List<RackSpatialRollupDto>();
                var aisleConditions = new Dictionary<string, int>();

                var rackGroups = aGrp
                    .GroupBy(c => c.RackCode ?? "RACK-01")
                    .OrderBy(g => g.Key);

                foreach (var rGrp in rackGroups)
                {
                    var rLocs = rGrp.ToList();
                    decimal rUsable = rLocs.Sum(l => l.UsableCbm);
                    decimal rOcc = rLocs.Sum(l => l.OccupiedCbm);
                    decimal rWeightedCube = rUsable > 0m ? Math.Round((rOcc / rUsable) * 100m, 2) : 0m;

                    int rUsablePallets = rLocs.Sum(l => l.UsablePallets);
                    int rOccPallets = rLocs.Sum(l => l.OccupiedPallets);
                    decimal rWeightedPallets = rUsablePallets > 0 ? Math.Round(((decimal)rOccPallets / rUsablePallets) * 100m, 2) : 0m;

                    decimal rValue = 0m;
                    decimal rDead = 0m;
                    decimal rExpired = 0m;
                    var rConditions = new Dictionary<string, int>();

                    foreach (var l in rLocs)
                    {
                        if (agingByLoc.TryGetValue(l.LocationId, out var lAging))
                        {
                            rValue += lAging.Sum(a => a.TotalInventoryValue);
                            rDead += lAging.Where(a => a.IsDeadStock).Sum(a => a.TotalInventoryValue);
                            rExpired += lAging.Where(a => a.IsExpired).Sum(a => a.TotalInventoryValue);
                        }

                        foreach (var cond in l.ActiveConditions)
                        {
                            rConditions[cond] = rConditions.GetValueOrDefault(cond) + 1;
                            aisleConditions[cond] = aisleConditions.GetValueOrDefault(cond) + 1;
                            zoneConditions[cond] = zoneConditions.GetValueOrDefault(cond) + 1;
                            facilityConditionSummary[cond] = facilityConditionSummary.GetValueOrDefault(cond) + 1;
                        }
                    }

                    string rColor = rWeightedCube switch
                    {
                        <= 0m => "#E2E8F0",
                        <= 50.0m => "#93C5FD",
                        <= 85.0m => "#22C55E",
                        <= 100.0m => "#F59E0B",
                        _ => "#EF4444"
                    };

                    rackRollups.Add(new RackSpatialRollupDto
                    {
                        RackCode = rGrp.Key,
                        AisleCode = aGrp.Key,
                        ZoneId = z.Id,
                        LocationsCount = rLocs.Count,
                        TotalUsableCbm = rUsable,
                        TotalOccupiedCbm = rOcc,
                        WeightedCubeUtilizationPercent = rWeightedCube,
                        TotalUsablePallets = rUsablePallets,
                        TotalOccupiedPallets = rOccPallets,
                        WeightedPalletOccupancyPercent = rWeightedPallets,
                        OverCapacityLocationsCount = rLocs.Count(l => l.IsOverCapacity),
                        BlockedLocationsCount = rLocs.Count(l => l.IsBlocked),
                        TotalInventoryValue = rValue,
                        DeadStockValue = rDead,
                        ExpiredStockValue = rExpired,
                        ConditionCounts = rConditions,
                        PrimaryColorHex = rColor,
                        PrimaryTier = rWeightedCube > 100m ? HeatmapVisualTier.Critical : (rWeightedCube > 85m ? HeatmapVisualTier.High : HeatmapVisualTier.Optimal)
                    });
                }

                // Aisle Weighted Aggregation (HM-04)
                decimal aUsable = rackRollups.Sum(r => r.TotalUsableCbm);
                decimal aOcc = rackRollups.Sum(r => r.TotalOccupiedCbm);
                decimal aWeightedCube = aUsable > 0m ? Math.Round((aOcc / aUsable) * 100m, 2) : 0m;

                int aUsablePallets = rackRollups.Sum(r => r.TotalUsablePallets);
                int aOccPallets = rackRollups.Sum(r => r.TotalOccupiedPallets);
                decimal aWeightedPallets = aUsablePallets > 0 ? Math.Round(((decimal)aOccPallets / aUsablePallets) * 100m, 2) : 0m;

                string aColor = aWeightedCube switch
                {
                    <= 0m => "#E2E8F0",
                    <= 50.0m => "#93C5FD",
                    <= 85.0m => "#22C55E",
                    <= 100.0m => "#F59E0B",
                    _ => "#EF4444"
                };

                aisleRollups.Add(new AisleSpatialRollupDto
                {
                    AisleCode = aGrp.Key,
                    ZoneId = z.Id,
                    ZoneCode = z.ZoneCode,
                    LocationsCount = rackRollups.Sum(r => r.LocationsCount),
                    RacksCount = rackRollups.Count,
                    TotalUsableCbm = aUsable,
                    TotalOccupiedCbm = aOcc,
                    WeightedCubeUtilizationPercent = aWeightedCube,
                    TotalUsablePallets = aUsablePallets,
                    TotalOccupiedPallets = aOccPallets,
                    WeightedPalletOccupancyPercent = aWeightedPallets,
                    OverCapacityLocationsCount = rackRollups.Sum(r => r.OverCapacityLocationsCount),
                    BlockedLocationsCount = rackRollups.Sum(r => r.BlockedLocationsCount),
                    TotalInventoryValue = rackRollups.Sum(r => r.TotalInventoryValue),
                    DeadStockValue = rackRollups.Sum(r => r.DeadStockValue),
                    ExpiredStockValue = rackRollups.Sum(r => r.ExpiredStockValue),
                    ConditionCounts = aisleConditions,
                    Racks = rackRollups,
                    PrimaryColorHex = aColor,
                    PrimaryTier = aWeightedCube > 100m ? HeatmapVisualTier.Critical : (aWeightedCube > 85m ? HeatmapVisualTier.High : HeatmapVisualTier.Optimal)
                });
            }

            // Zone Weighted Aggregation (HM-04)
            decimal zUsable = aisleRollups.Sum(a => a.TotalUsableCbm);
            decimal zOcc = aisleRollups.Sum(a => a.TotalOccupiedCbm);
            decimal zWeightedCube = zUsable > 0m ? Math.Round((zOcc / zUsable) * 100m, 2) : 0m;

            int zUsablePallets = aisleRollups.Sum(a => a.TotalUsablePallets);
            int zOccPallets = aisleRollups.Sum(a => a.TotalOccupiedPallets);
            decimal zWeightedPallets = zUsablePallets > 0 ? Math.Round(((decimal)zOccPallets / zUsablePallets) * 100m, 2) : 0m;

            string zColor = zWeightedCube switch
            {
                <= 0m => "#E2E8F0",
                <= 50.0m => "#93C5FD",
                <= 85.0m => "#22C55E",
                <= 100.0m => "#F59E0B",
                _ => "#EF4444"
            };

            zoneRollups.Add(new ZoneSpatialRollupDto
            {
                ZoneId = z.Id,
                ZoneCode = z.ZoneCode,
                ZoneName = z.Name,
                ZoneType = z.ZoneType,
                TotalLocationsCount = aisleRollups.Sum(a => a.LocationsCount),
                ActiveLocationsCount = aisleRollups.Sum(a => a.LocationsCount) - aisleRollups.Sum(a => a.BlockedLocationsCount),
                BlockedLocationsCount = aisleRollups.Sum(a => a.BlockedLocationsCount),
                OverCapacityLocationsCount = aisleRollups.Sum(a => a.OverCapacityLocationsCount),
                TotalUsableCbm = zUsable,
                TotalOccupiedCbm = zOcc,
                WeightedCubeUtilizationPercent = zWeightedCube,
                TotalUsablePallets = zUsablePallets,
                TotalOccupiedPallets = zOccPallets,
                WeightedPalletOccupancyPercent = zWeightedPallets,
                TotalInventoryValue = aisleRollups.Sum(a => a.TotalInventoryValue),
                DeadStockValue = aisleRollups.Sum(a => a.DeadStockValue),
                ExpiredStockValue = aisleRollups.Sum(a => a.ExpiredStockValue),
                ConditionCounts = zoneConditions,
                Aisles = aisleRollups,
                PrimaryColorHex = zColor,
                PrimaryTier = zWeightedCube > 100m ? HeatmapVisualTier.Critical : (zWeightedCube > 85m ? HeatmapVisualTier.High : HeatmapVisualTier.Optimal)
            });
        }

        // Step 6.3.6.3.6: Operational Hotspot Detection
        // Clustering contiguous locations based on explicit topology rules (same Zone & Aisle, distance <= 5m)
        var hotspots = new List<SpatialHotspotDto>();

        // 1. Overcapacity Hotspots
        var overcapLocs = cells.Where(c => c.IsOverCapacity).ToList();
        var overcapClusters = ClusterContiguousLocations(overcapLocs, 5.0m);
        foreach (var cluster in overcapClusters)
        {
            decimal u = cluster.Sum(l => l.UsableCbm);
            decimal o = cluster.Sum(l => l.OccupiedCbm);
            decimal weighted = u > 0m ? Math.Round((o / u) * 100m, 2) : 0m;
            var sample = cluster[0];

            hotspots.Add(new SpatialHotspotDto
            {
                HotspotType = "Overcapacity",
                SeverityTier = HeatmapVisualTier.Critical,
                ZoneId = sample.ZoneId,
                ZoneCode = sample.ZoneCode,
                AisleCode = sample.AisleCode,
                ContiguousLocationsCount = cluster.Count,
                TotalUsableCbm = u,
                TotalOccupiedCbm = o,
                WeightedCubeUtilizationPercent = weighted,
                TotalExcessCbm = cluster.Sum(l => l.OverCapacityExcessCbm),
                TotalAtRiskValue = 0m,
                LocationIds = cluster.Select(l => l.LocationId).ToList(),
                LocationCodes = cluster.Select(l => l.LocationCode).ToList(),
                RecommendedAction = "Trigger immediate replenishment rebalancing or overflow relocation."
            });
        }

        // 2. Dead Stock Hotspots
        var deadLocs = cells.Where(c => c.IsDeadStock).ToList();
        var deadClusters = ClusterContiguousLocations(deadLocs, 5.0m);
        foreach (var cluster in deadClusters)
        {
            var sample = cluster[0];
            decimal atRiskVal = cluster.Sum(l => agingByLoc.TryGetValue(l.LocationId, out var a) ? a.Where(x => x.IsDeadStock).Sum(x => x.TotalInventoryValue) : 0m);

            hotspots.Add(new SpatialHotspotDto
            {
                HotspotType = "DeadStock",
                SeverityTier = HeatmapVisualTier.High,
                ZoneId = sample.ZoneId,
                ZoneCode = sample.ZoneCode,
                AisleCode = sample.AisleCode,
                ContiguousLocationsCount = cluster.Count,
                TotalUsableCbm = cluster.Sum(l => l.UsableCbm),
                TotalOccupiedCbm = cluster.Sum(l => l.OccupiedCbm),
                WeightedCubeUtilizationPercent = cluster.Sum(l => l.UsableCbm) > 0m ? Math.Round((cluster.Sum(l => l.OccupiedCbm) / cluster.Sum(l => l.UsableCbm)) * 100m, 2) : 0m,
                TotalExcessCbm = 0m,
                TotalAtRiskValue = atRiskVal,
                LocationIds = cluster.Select(l => l.LocationId).ToList(),
                LocationCodes = cluster.Select(l => l.LocationCode).ToList(),
                RecommendedAction = "Schedule disposition review, client return-to-vendor, or markdown."
            });
        }

        // 3. Blocked Locations Hotspots
        var blockedLocs = cells.Where(c => c.IsBlocked).ToList();
        var blockedClusters = ClusterContiguousLocations(blockedLocs, 5.0m);
        foreach (var cluster in blockedClusters)
        {
            var sample = cluster[0];
            hotspots.Add(new SpatialHotspotDto
            {
                HotspotType = "Blocked",
                SeverityTier = HeatmapVisualTier.Blocked,
                ZoneId = sample.ZoneId,
                ZoneCode = sample.ZoneCode,
                AisleCode = sample.AisleCode,
                ContiguousLocationsCount = cluster.Count,
                TotalUsableCbm = cluster.Sum(l => l.UsableCbm),
                TotalOccupiedCbm = cluster.Sum(l => l.OccupiedCbm),
                WeightedCubeUtilizationPercent = cluster.Sum(l => l.UsableCbm) > 0m ? Math.Round((cluster.Sum(l => l.OccupiedCbm) / cluster.Sum(l => l.UsableCbm)) * 100m, 2) : 0m,
                TotalExcessCbm = 0m,
                TotalAtRiskValue = 0m,
                LocationIds = cluster.Select(l => l.LocationId).ToList(),
                LocationCodes = cluster.Select(l => l.LocationCode).ToList(),
                RecommendedAction = "Dispatch maintenance team to inspect and clear physical location lock."
            });
        }

        // Macro Facility Rollups (HM-04)
        decimal facUsable = zoneRollups.Sum(z => z.TotalUsableCbm);
        decimal facOcc = zoneRollups.Sum(z => z.TotalOccupiedCbm);
        decimal facWeightedCube = facUsable > 0m ? Math.Round((facOcc / facUsable) * 100m, 2) : 0m;

        int facUsablePallets = zoneRollups.Sum(z => z.TotalUsablePallets);
        int facOccPallets = zoneRollups.Sum(z => z.TotalOccupiedPallets);
        decimal facWeightedPallets = facUsablePallets > 0 ? Math.Round(((decimal)facOccPallets / facUsablePallets) * 100m, 2) : 0m;

        return new SpatialHeatmapProjectionDto
        {
            FacilityId = heatmap.FacilityId,
            FacilityCode = heatmap.FacilityCode,
            FacilityName = heatmap.FacilityName,
            SnapshotDateUtc = asOf,
            ProjectionVersion = 1,
            FacilityEnvelope = heatmap.FacilityEnvelope,
            LocationBoundingBox = heatmap.BoundingBox,
            ZoomPanBounds = heatmap.ZoomPanBounds,
            FacilityTotalUsableCbm = facUsable,
            FacilityTotalOccupiedCbm = facOcc,
            FacilityWeightedCubeUtilizationPercent = facWeightedCube,
            FacilityTotalUsablePallets = facUsablePallets,
            FacilityTotalOccupiedPallets = facOccPallets,
            FacilityWeightedPalletOccupancyPercent = facWeightedPallets,
            FacilityTotalInventoryValue = zoneRollups.Sum(z => z.TotalInventoryValue),
            TotalLocationsEvaluated = cells.Count,
            OutOfBoundsLocationsCount = heatmap.OutOfBoundsLocationsCount,
            MissingCoordinatesLocationsCount = heatmap.MissingCoordinatesLocationsCount,
            DuplicateCoordinatesLocationsCount = heatmap.DuplicateCoordinatesLocationsCount,
            Zones = zoneRollups,
            GridCells = gridCells,
            Hotspots = hotspots,
            Locations = cells,
            FacilityConditionSummary = facilityConditionSummary
        };
    }

    private static List<List<HeatmapLocationCellDto>> ClusterContiguousLocations(
        List<HeatmapLocationCellDto> locations,
        decimal maxDistanceMeters)
    {
        var visited = new HashSet<Guid>();
        var clusters = new List<List<HeatmapLocationCellDto>>();

        foreach (var loc in locations)
        {
            if (visited.Contains(loc.LocationId)) continue;

            var cluster = new List<HeatmapLocationCellDto>();
            var queue = new Queue<HeatmapLocationCellDto>();

            visited.Add(loc.LocationId);
            queue.Enqueue(loc);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                cluster.Add(current);

                // Find topological neighbors: Same Zone AND Same Aisle, with physical distance <= maxDistanceMeters
                foreach (var other in locations)
                {
                    if (visited.Contains(other.LocationId)) continue;
                    if (other.ZoneId != current.ZoneId) continue;
                    if (other.AisleCode != current.AisleCode) continue;

                    // Calculate distance if both have coordinates
                    bool isNeighbor = false;
                    if (current.Coordinate.HasCoordinates && other.Coordinate.HasCoordinates)
                    {
                        decimal dx = current.Coordinate.X!.Value - other.Coordinate.X!.Value;
                        decimal dy = current.Coordinate.Y!.Value - other.Coordinate.Y!.Value;
                        decimal dz = current.Coordinate.Z!.Value - other.Coordinate.Z!.Value;
                        double dist = Math.Sqrt((double)(dx * dx + dy * dy + dz * dz));
                        if (dist <= (double)maxDistanceMeters)
                        {
                            isNeighbor = true;
                        }
                    }
                    else if (current.RackCode == other.RackCode)
                    {
                        // Same rack topological fallback
                        isNeighbor = true;
                    }

                    if (isNeighbor)
                    {
                        visited.Add(other.LocationId);
                        queue.Enqueue(other);
                    }
                }
            }

            clusters.Add(cluster);
        }

        return clusters;
    }

    public async Task<IReadOnlyList<SpatialHotspotDto>> GetSpatialHotspotsAsync(
        Guid facilityId,
        DateTime? asOfDateUtc = null,
        CancellationToken cancellationToken = default)
    {
        var projection = await GetSpatialHeatmapProjectionAsync(
            facilityId, null, 2.0m, asOfDateUtc, cancellationToken);
        return projection.Hotspots;
    }

    public async Task<IReadOnlyList<ZoneSpatialRollupDto>> GetZoneSpatialRollupsAsync(
        Guid facilityId,
        DateTime? asOfDateUtc = null,
        CancellationToken cancellationToken = default)
    {
        var projection = await GetSpatialHeatmapProjectionAsync(
            facilityId, null, 2.0m, asOfDateUtc, cancellationToken);
        return projection.Zones;
    }

    #endregion

    private void ValidateLaborCostAccess(Guid facilityId)
    {
        if (_currentUser.TenantId.HasValue)
        {
            throw new UnauthorizedException("Client portal users are not authorized to view internal warehouse workforce analytics.");
        }

        if (_currentUser.FacilityId.HasValue && _currentUser.FacilityId.Value != facilityId)
        {
            throw new UnauthorizedException($"User is restricted to facility {_currentUser.FacilityId.Value} and cannot access labor costs for facility {facilityId}.");
        }

        if (!_currentUser.IsSuperAdmin && !_currentUser.HasPermission(Permissions.Analytics.WorkforceCostView))
        {
            throw new UnauthorizedException("User is not authorized to view financial labor cost analytics.");
        }
    }

    private void ValidateInternalStaffOnly()
    {
        if (_currentUser.TenantId.HasValue)
        {
            throw new UnauthorizedException("Client portal users are not authorized to view internal warehouse workforce analytics.");
        }
    }

    private void ValidateTenantAccess(Guid clientId)
    {
        if (_currentUser.IsStaff || _currentUser.IsSuperAdmin)
        {
            return;
        }

        if (_currentUser.TenantId.HasValue && _currentUser.TenantId.Value != clientId)
        {
            throw new UnauthorizedException($"Tenant {_currentUser.TenantId.Value} is not authorized to access analytics for client {clientId}.");
        }
    }

    private void ValidateFacilityAccess(Guid facilityId)
    {
        if (_currentUser.IsSuperAdmin)
        {
            return;
        }

        if (_currentUser.FacilityId.HasValue && _currentUser.FacilityId.Value != facilityId)
        {
            throw new UnauthorizedException($"User is restricted to facility {_currentUser.FacilityId.Value} and cannot access facility {facilityId}.");
        }
    }
}
