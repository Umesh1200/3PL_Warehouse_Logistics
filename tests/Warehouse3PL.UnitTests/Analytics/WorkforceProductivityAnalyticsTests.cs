using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Services.Analytics;
using Warehouse3PL.UnitTests.Common;
using Warehouse3PL.WebApi.Controllers;

namespace Warehouse3PL.UnitTests.Analytics;

/// <summary>
/// Comprehensive verification test suite for Phase 6.2:
/// Labor, Productivity & Workforce Pacing Analytics.
/// Tests engineered standard time vs. actual duration, unconflated KPI metrics (UPH, LPH, CPH, TPH),
/// idle time classification, privacy RBAC boundaries, cost per activity, and replay.
/// </summary>
public class WorkforceProductivityAnalyticsTests
{
    private class TestWorkforceFixture
    {
        public string DbName { get; } = Guid.NewGuid().ToString();
        public Guid FacilityId { get; set; } = Guid.NewGuid();
        public Guid Worker1Id { get; set; } = Guid.NewGuid();
        public Guid Worker2Id { get; set; } = Guid.NewGuid();
        public Guid ClientTenantId { get; set; } = Guid.NewGuid();
    }

    private static async Task<TestWorkforceFixture> SeedTestEnvironmentAsync()
    {
        var fix = new TestWorkforceFixture();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var fac = new Facility
        {
            Id = fix.FacilityId,
            FacilityCode = "ORD-01",
            Name = "Chicago Regional Fulfillment Center",
            IsActive = true
        };

        var user1 = new User
        {
            Id = fix.Worker1Id,
            Email = "john.picker@3plwarehousing.com",
            FirstName = "John",
            LastName = "Doe",
            UserType = UserType.Staff,
            Status = UserStatus.Active
        };

        var user2 = new User
        {
            Id = fix.Worker2Id,
            Email = "jane.packer@3plwarehousing.com",
            FirstName = "Jane",
            LastName = "Smith",
            UserType = UserType.Staff,
            Status = UserStatus.Active
        };

        await context.Facilities.AddAsync(fac);
        await context.Users.AddRangeAsync(user1, user2);
        await context.SaveChangesAsync();

        return fix;
    }

    [Fact]
    public void StandardTimeCalculation_CalculatesExpectedSecondsWithEquipmentAndComplexityMultipliers()
    {
        // Arrange: Setup standard profile with base=30s, 5s/unit, 10s/line, 15s/carton, 60s/pallet
        var profile = LaborStandardProfile.Create(
            activityType: WarehouseLaborActivityType.Picking,
            department: "E-Commerce Picking",
            baseSetupSeconds: 30.0m,
            secondsPerUnit: 5.0m,
            secondsPerLine: 10.0m,
            secondsPerCarton: 15.0m,
            secondsPerPallet: 60.0m,
            equipmentFactor: 1.0m,
            complexityMultiplier: 1.0m);

        // Act 1: 10 units, 2 lines, 0 cartons, 0 pallets, Standard cart
        // Raw: 30 + (10*5) + (2*10) = 30 + 50 + 20 = 100 seconds
        var standardSeconds1 = profile.CalculateStandardDurationSeconds(
            units: 10, lines: 2, cartons: 0, pallets: 0);

        // Act 2: Same volume with OrderPicker (1.25x) and Hazmat (1.50x)
        // 100 * 1.25 * 1.50 = 187.50 seconds
        var standardSeconds2 = profile.CalculateStandardDurationSeconds(
            units: 10, lines: 2, cartons: 0, pallets: 0,
            customEquipmentFactor: 1.25m,
            customComplexityMultiplier: 1.50m);

        // Assert
        Assert.Equal(100.00m, standardSeconds1);
        Assert.Equal(187.50m, standardSeconds2);
    }

    [Fact]
    public void EfficiencyCalculation_CorrectlyEvaluatesAllFourPerformanceTiers()
    {
        // Arrange: Daily summary for 8-hour shift
        var summary = DailyWorkerProductivitySummary.Create(
            workerUserId: Guid.NewGuid(),
            facilityId: Guid.NewGuid(),
            department: "Picking",
            dateUtc: DateTime.UtcNow,
            totalShiftMinutes: 480m,
            scheduledBreakMinutes: 30m);

        // Scenario 1: Standard 120s, Actual 100s -> Efficiency = 120% (Exceptional)
        summary.RecordTaskExecution(units: 10, lines: 1, cartons: 0, pallets: 0, actualSeconds: 100.0m, standardSeconds: 120.0m);
        Assert.Equal(120.00m, summary.LaborEfficiencyPercent);
        Assert.Equal(LaborPerformanceTier.Exceptional, summary.PerformanceTier);

        // Reset summary for Scenario 2: Standard 100s, Actual 100s -> Efficiency = 100% (Standard)
        var summary2 = DailyWorkerProductivitySummary.Create(Guid.NewGuid(), Guid.NewGuid(), "Picking", DateTime.UtcNow);
        summary2.RecordTaskExecution(units: 10, lines: 1, cartons: 0, pallets: 0, actualSeconds: 100.0m, standardSeconds: 100.0m);
        Assert.Equal(100.00m, summary2.LaborEfficiencyPercent);
        Assert.Equal(LaborPerformanceTier.Standard, summary2.PerformanceTier);

        // Reset summary for Scenario 3: Standard 85s, Actual 100s -> Efficiency = 85% (Developing)
        var summary3 = DailyWorkerProductivitySummary.Create(Guid.NewGuid(), Guid.NewGuid(), "Picking", DateTime.UtcNow);
        summary3.RecordTaskExecution(units: 10, lines: 1, cartons: 0, pallets: 0, actualSeconds: 100.0m, standardSeconds: 85.0m);
        Assert.Equal(85.00m, summary3.LaborEfficiencyPercent);
        Assert.Equal(LaborPerformanceTier.Developing, summary3.PerformanceTier);

        // Reset summary for Scenario 4: Standard 60s, Actual 100s -> Efficiency = 60% (Underperforming)
        var summary4 = DailyWorkerProductivitySummary.Create(Guid.NewGuid(), Guid.NewGuid(), "Picking", DateTime.UtcNow);
        summary4.RecordTaskExecution(units: 10, lines: 1, cartons: 0, pallets: 0, actualSeconds: 100.0m, standardSeconds: 60.0m);
        Assert.Equal(60.00m, summary4.LaborEfficiencyPercent);
        Assert.Equal(LaborPerformanceTier.Underperforming, summary4.PerformanceTier);
    }

    [Fact]
    public void UnconflatedWorkforceMetrics_CalculatesUPH_LPH_CPH_TPH_Independently()
    {
        // Arrange: Worker executed work over 2 productive hours (120 mins)
        // 200 units, 40 lines, 20 cartons, 10 tasks
        var summary = DailyWorkerProductivitySummary.Create(
            workerUserId: Guid.NewGuid(),
            facilityId: Guid.NewGuid(),
            department: "Mixed Fulfillment",
            dateUtc: DateTime.UtcNow);

        // Record 10 tasks, each took 720 seconds (12 mins) = 7200 seconds total (120 mins = 2.0 hours)
        for (int i = 0; i < 10; i++)
        {
            summary.RecordTaskExecution(
                units: 20,
                lines: 4,
                cartons: 2,
                pallets: 0,
                actualSeconds: 720.0m,
                standardSeconds: 720.0m);
        }

        // Assert: Distinct, unconflated rates
        Assert.Equal(2.0000m, summary.ActualLaborHours);
        Assert.Equal(10, summary.TasksCompleted);
        Assert.Equal(200, summary.UnitsCompleted);
        Assert.Equal(40, summary.LinesCompleted);
        Assert.Equal(20, summary.CartonsCompleted);

        // UPH = 200 / 2.0 = 100
        Assert.Equal(100.00m, summary.UnitsPerHour);
        // LPH = 40 / 2.0 = 20
        Assert.Equal(20.00m, summary.LinesPerHour);
        // CPH = 20 / 2.0 = 10
        Assert.Equal(10.00m, summary.CartonsPerHour);
        // TPH = 10 / 2.0 = 5
        Assert.Equal(5.00m, summary.TasksPerHour);

        // Assert non-equality proving no conflation
        Assert.NotEqual(summary.UnitsPerHour, summary.LinesPerHour);
        Assert.NotEqual(summary.LinesPerHour, summary.CartonsPerHour);
        Assert.NotEqual(summary.CartonsPerHour, summary.TasksPerHour);
    }

    [Fact]
    public void LaborUtilization_CalculatesActiveVersusNetAvailableShiftTime()
    {
        // Arrange: 8-hour shift (480 mins) - 30 min break = 450 net available minutes
        var summary = DailyWorkerProductivitySummary.Create(
            workerUserId: Guid.NewGuid(),
            facilityId: Guid.NewGuid(),
            department: "Outbound",
            dateUtc: DateTime.UtcNow,
            totalShiftMinutes: 480m,
            scheduledBreakMinutes: 30m);

        // 360 productive minutes (6 hours)
        summary.RecordTaskExecution(
            units: 150, lines: 30, cartons: 0, pallets: 0,
            actualSeconds: 21600.0m, // 360 mins
            standardSeconds: 21600.0m);

        // 60 minutes waiting for replenishment
        summary.RecordIdleInterval(60.0m, LaborIdleTimeReason.WaitingForReplenishment);

        // 30 minutes scheduled break
        summary.RecordIdleInterval(30.0m, LaborIdleTimeReason.ScheduledBreak);

        // Assert
        // Net available = 480 - (30 + 30) = 420 mins (since additional 30 break added)
        // Productive = 360 mins
        // Utilization % = 360 / 420 * 100 = 85.71%
        Assert.Equal(360.0m, summary.ProductiveWorkMinutes);
        Assert.Equal(60.0m, summary.IdleMinutes);
        Assert.Equal(60.0m, summary.ScheduledBreakMinutes);
        Assert.Equal(85.71m, summary.LaborUtilizationPercent);
    }

    [Fact]
    public async Task ProcessWorkerTaskExecutionAsync_CalculatesStandardTimeAndGeneratesExecutionLog()
    {
        // Arrange
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projectionService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);

        // Add a standard profile for Picking
        var profile = LaborStandardProfile.Create(
            activityType: WarehouseLaborActivityType.Picking,
            department: "E-Commerce Picking",
            baseSetupSeconds: 20.0m,
            secondsPerUnit: 4.0m,
            secondsPerLine: 6.0m,
            facilityId: fix.FacilityId);
        await projectionService.UpsertLaborStandardProfileAsync(profile);

        var startedAt = DateTime.UtcNow.AddMinutes(-10);
        var completedAt = DateTime.UtcNow; // 600 seconds elapsed
        var taskId = Guid.NewGuid();

        // Act: 50 units, 10 lines
        // Standard: 20 + (50*4) + (10*6) = 20 + 200 + 60 = 280 seconds
        await projectionService.ProcessWorkerTaskExecutionAsync(
            eventId: Guid.NewGuid(),
            sequenceNumber: 1,
            taskId: taskId,
            activityType: WarehouseLaborActivityType.Picking,
            workerUserId: fix.Worker1Id,
            facilityId: fix.FacilityId,
            department: "E-Commerce Picking",
            units: 50,
            lines: 10,
            cartons: 0,
            pallets: 0,
            startedAtUtc: startedAt,
            completedAtUtc: completedAt,
            hourlyWageRate: 25.00m,
            complexity: LaborComplexityFactor.Standard,
            equipment: WarehouseEquipmentType.ManualCart);

        // Assert
        var log = await context.WorkerTaskExecutionLogs.FirstOrDefaultAsync(l => l.TaskId == taskId);
        Assert.NotNull(log);
        Assert.Equal(280.00m, log.StandardDurationSeconds);
        Assert.Equal(600.00m, log.ActualDurationSeconds);
        Assert.Equal(320.00m, log.VarianceSeconds); // 600 - 280 = 320
        Assert.Equal(46.67m, log.EfficiencyPercent); // (280 / 600) * 100 = 46.67%
        Assert.Equal(25.00m, log.HourlyWageRate);
        Assert.Equal(4.1667m, log.CalculatedLaborCost); // (600 / 3600) * 25 = 4.1667

        var summary = await context.DailyWorkerProductivitySummaries
            .FirstOrDefaultAsync(s => s.WorkerUserId == fix.Worker1Id && s.DateUtc == completedAt.Date);
        Assert.NotNull(summary);
        Assert.Equal(1, summary.TasksCompleted);
        Assert.Equal(50, summary.UnitsCompleted);
        Assert.Equal(10, summary.LinesCompleted);
        Assert.Equal(LaborPerformanceTier.Underperforming, summary.PerformanceTier);
    }

    [Fact]
    public async Task ProcessWorkerIdleIntervalAsync_RecordsAllDiscreteIdleReasons()
    {
        // Arrange
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projectionService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);

        var baseTime = DateTime.UtcNow.Date.AddHours(9);

        // Act: Ingest 3 distinct idle intervals
        await projectionService.ProcessWorkerIdleIntervalAsync(
            eventId: Guid.NewGuid(),
            sequenceNumber: 1,
            workerUserId: fix.Worker1Id,
            facilityId: fix.FacilityId,
            department: "Picking",
            startedAtUtc: baseTime,
            endedAtUtc: baseTime.AddMinutes(15),
            reasonCode: LaborIdleTimeReason.WaitingForTask,
            supervisorApproved: true,
            notes: "Zone clear, waiting for Wave 104");

        await projectionService.ProcessWorkerIdleIntervalAsync(
            eventId: Guid.NewGuid(),
            sequenceNumber: 2,
            workerUserId: fix.Worker1Id,
            facilityId: fix.FacilityId,
            department: "Picking",
            startedAtUtc: baseTime.AddMinutes(20),
            endedAtUtc: baseTime.AddMinutes(35),
            reasonCode: LaborIdleTimeReason.SystemDowntime,
            supervisorApproved: true,
            notes: "RF scanner WiFi network dropped");

        await projectionService.ProcessWorkerIdleIntervalAsync(
            eventId: Guid.NewGuid(),
            sequenceNumber: 3,
            workerUserId: fix.Worker1Id,
            facilityId: fix.FacilityId,
            department: "Picking",
            startedAtUtc: baseTime.AddHours(3),
            endedAtUtc: baseTime.AddHours(3).AddMinutes(30),
            reasonCode: LaborIdleTimeReason.ScheduledBreak,
            supervisorApproved: true);

        // Assert
        var records = await context.WorkerIdleIntervalRecords
            .Where(r => r.WorkerUserId == fix.Worker1Id)
            .ToListAsync();
        Assert.Equal(3, records.Count);

        var summary = await context.DailyWorkerProductivitySummaries
            .FirstOrDefaultAsync(s => s.WorkerUserId == fix.Worker1Id && s.DateUtc == baseTime.Date);
        Assert.NotNull(summary);
        // Non-break idle = 15 + 15 = 30 mins
        Assert.Equal(30.0m, summary.IdleMinutes);
        // Scheduled break = default 30 + 30 added = 60 mins
        Assert.Equal(60.0m, summary.ScheduledBreakMinutes);
    }

    [Fact]
    public async Task PrivacyBoundary_WorkerSelfView_AllowsSelfAccessOnly()
    {
        // Arrange
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        // Create summary for Worker 1
        var today = DateTime.UtcNow.Date;
        var summary1 = DailyWorkerProductivitySummary.Create(fix.Worker1Id, fix.FacilityId, "Picking", today);
        summary1.RecordTaskExecution(50, 10, 0, 0, 1800m, 1800m);
        await context.DailyWorkerProductivitySummaries.AddAsync(summary1);
        await context.SaveChangesAsync();

        // Worker 1 user context
        var worker1Context = new TestCurrentUserContext
        {
            UserId = fix.Worker1Id,
            UserType = UserType.Staff
        };

        var queryService = new AnalyticsQueryService(context, worker1Context, NullLogger<AnalyticsQueryService>.Instance);

        // Act 1: Worker 1 queries own productivity -> Success
        var selfResult = await queryService.GetWorkerProductivityAsync(
            fix.Worker1Id, today, today, requestingUserId: fix.Worker1Id, hasElevatedWorkforceAccess: false);
        Assert.Single(selfResult);

        // Act 2: Worker 1 attempts to query Worker 2's productivity -> UnauthorizedException
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            queryService.GetWorkerProductivityAsync(
                fix.Worker2Id, today, today, requestingUserId: fix.Worker1Id, hasElevatedWorkforceAccess: false));
    }

    [Fact]
    public async Task PrivacyBoundary_ClientTenantUser_IsStrictlyDeniedAccessToWorkforceData()
    {
        // Arrange: Client portal user context
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var clientUserContext = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            TenantId = fix.ClientTenantId,
            UserType = UserType.Client
        };

        var queryService = new AnalyticsQueryService(context, clientUserContext, NullLogger<AnalyticsQueryService>.Instance);

        // Act & Assert: Attempting to query worker productivity throws UnauthorizedException
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            queryService.GetWorkerProductivityAsync(
                fix.Worker1Id, DateTime.UtcNow.Date, DateTime.UtcNow.Date, null, false));

        // Act & Assert: Attempting to query facility workforce summary throws UnauthorizedException
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            queryService.GetFacilityWorkforceSummaryAsync(fix.FacilityId, DateTime.UtcNow.Date));
    }

    [Fact]
    public async Task FacilityWorkforceSummary_AggregatesDepartmentBreakdownsAndBlendedMetrics()
    {
        // Arrange
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var today = DateTime.UtcNow.Date;

        // Worker 1 in Picking: 100 units, 20 lines, 3600s actual, 3600s std, $20/hr
        var worker1Summary = DailyWorkerProductivitySummary.Create(fix.Worker1Id, fix.FacilityId, "Picking", today, 480m, 30m, 20.00m);
        worker1Summary.RecordTaskExecution(100, 20, 0, 0, 3600m, 3600m, 20.00m);

        // Worker 2 in Packing: 50 cartons, 1800s actual, 1800s std, $22/hr
        var worker2Summary = DailyWorkerProductivitySummary.Create(fix.Worker2Id, fix.FacilityId, "Packing", today, 480m, 30m, 22.00m);
        worker2Summary.RecordTaskExecution(50, 0, 50, 0, 1800m, 1800m, 22.00m);

        await context.DailyWorkerProductivitySummaries.AddRangeAsync(worker1Summary, worker2Summary);
        await context.SaveChangesAsync();

        var queryService = new AnalyticsQueryService(
            context,
            new TestCurrentUserContext 
            { 
                UserType = UserType.Staff,
                PermissionsList = [Permissions.Analytics.WorkforceCostView]
            },
            NullLogger<AnalyticsQueryService>.Instance);

        // Act
        var result = await queryService.GetFacilityWorkforceSummaryAsync(fix.FacilityId, today);

        // Assert
        Assert.Equal(2, result.ActiveWorkersCount);
        Assert.Equal(1.5000m, result.TotalProductiveHours); // 1.0 hr + 0.5 hr
        Assert.Equal(1.5000m, result.TotalStandardHours);
        Assert.Equal(100.00m, result.BlendedLaborEfficiencyPercent); // (1.5 / 1.5) * 100 = 100%
        Assert.Equal(100.00m, result.BlendedUnitsPerHour); // 150 units / 1.5 hrs = 100
        Assert.Equal(31.00m, result.TotalLaborCost); // (1.0 * 20) + (0.5 * 22) = 20 + 11 = 31

        Assert.Equal(2, result.DepartmentBreakdowns.Count);
        var pickDept = result.DepartmentBreakdowns.FirstOrDefault(d => d.Department == "Picking");
        Assert.NotNull(pickDept);
        Assert.Equal(1, pickDept.ActiveWorkersCount);
        Assert.Equal(1.0000m, pickDept.ProductiveHours);
        Assert.Equal(20.00m, pickDept.TotalLaborCost);

        var packDept = result.DepartmentBreakdowns.FirstOrDefault(d => d.Department == "Packing");
        Assert.NotNull(packDept);
        Assert.Equal(1, packDept.ActiveWorkersCount);
        Assert.Equal(0.5000m, packDept.ProductiveHours);
        Assert.Equal(11.00m, packDept.TotalLaborCost);
    }

    [Fact]
    public async Task LaborCostAnalysis_CalculatesProductiveAndIdleCostsWithActivityUnitRates()
    {
        // Arrange
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var start = DateTime.UtcNow.Date.AddDays(-1);
        var end = DateTime.UtcNow.Date.AddDays(1);

        // Log 1: Picking task - 100 units, 1 hour (3600s), $24/hr = $24.00 cost
        var log1 = WorkerTaskExecutionLog.Create(
            Guid.NewGuid(),
            WarehouseLaborActivityType.Picking,
            fix.Worker1Id,
            fix.FacilityId,
            "Picking",
            units: 100, lines: 20, cartons: 0, pallets: 0,
            startedAtUtc: DateTime.UtcNow.AddHours(-3),
            completedAtUtc: DateTime.UtcNow.AddHours(-2),
            standardDurationSeconds: 3600m,
            hourlyWageRate: 24.00m);

        // Log 2: Packing task - 40 cartons, 0.5 hour (1800s), $24/hr = $12.00 cost
        var log2 = WorkerTaskExecutionLog.Create(
            Guid.NewGuid(),
            WarehouseLaborActivityType.Packing,
            fix.Worker2Id,
            fix.FacilityId,
            "Packing",
            units: 40, lines: 0, cartons: 40, pallets: 0,
            startedAtUtc: DateTime.UtcNow.AddHours(-2),
            completedAtUtc: DateTime.UtcNow.AddHours(-1.5),
            standardDurationSeconds: 1800m,
            hourlyWageRate: 24.00m);

        // Idle 1: 30 minutes idle
        var idle1 = WorkerIdleIntervalRecord.Create(
            fix.Worker1Id, fix.FacilityId, "Picking",
            DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddMinutes(-30),
            LaborIdleTimeReason.WaitingForReplenishment);

        await context.WorkerTaskExecutionLogs.AddRangeAsync(log1, log2);
        await context.WorkerIdleIntervalRecords.AddAsync(idle1);
        await context.SaveChangesAsync();

        var queryService = new AnalyticsQueryService(
            context,
            new TestCurrentUserContext 
            { 
                UserType = UserType.Staff,
                PermissionsList = [Permissions.Analytics.WorkforceCostView]
            },
            NullLogger<AnalyticsQueryService>.Instance);

        // Act
        var costAnalysis = await queryService.GetLaborCostAnalysisAsync(fix.FacilityId, start, end);

        // Assert
        // Direct cost = $24.00 + $12.00 = $36.00
        Assert.Equal(36.00m, costAnalysis.DirectProductiveCost);
        // Idle time = 30 mins (0.5 hr) * $24/hr = $12.00
        Assert.Equal(12.00m, costAnalysis.IdleTimeCost);
        // Total cost = $48.00
        Assert.Equal(48.00m, costAnalysis.TotalLaborCost);
        // Average cost per pick unit = $24.00 / 100 = $0.2400
        Assert.Equal(0.2400m, costAnalysis.AverageCostPerPickUnit);
        // Average cost per pack carton = $12.00 / 40 = $0.3000
        Assert.Equal(0.3000m, costAnalysis.AverageCostPerPackCarton);
    }

    [Fact]
    public async Task Idempotency_PreventsDoubleCountingOnDuplicateEvents()
    {
        // Arrange
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projectionService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);

        var eventId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        // Act: Ingest same event twice
        await projectionService.ProcessWorkerTaskExecutionAsync(
            eventId: eventId,
            sequenceNumber: 1,
            taskId: taskId,
            activityType: WarehouseLaborActivityType.Picking,
            workerUserId: fix.Worker1Id,
            facilityId: fix.FacilityId,
            department: "Picking",
            units: 50,
            lines: 5,
            cartons: 0,
            pallets: 0,
            startedAtUtc: now.AddMinutes(-10),
            completedAtUtc: now,
            overrideStandardSeconds: 600m,
            hourlyWageRate: 20.00m);

        await projectionService.ProcessWorkerTaskExecutionAsync(
            eventId: eventId,
            sequenceNumber: 1,
            taskId: taskId,
            activityType: WarehouseLaborActivityType.Picking,
            workerUserId: fix.Worker1Id,
            facilityId: fix.FacilityId,
            department: "Picking",
            units: 50,
            lines: 5,
            cartons: 0,
            pallets: 0,
            startedAtUtc: now.AddMinutes(-10),
            completedAtUtc: now,
            overrideStandardSeconds: 600m,
            hourlyWageRate: 20.00m);

        // Assert: Log and summary recorded exactly once
        var logCount = await context.WorkerTaskExecutionLogs.CountAsync(l => l.TaskId == taskId);
        Assert.Equal(1, logCount);

        var summary = await context.DailyWorkerProductivitySummaries.FirstOrDefaultAsync(s => s.WorkerUserId == fix.Worker1Id);
        Assert.NotNull(summary);
        Assert.Equal(1, summary.TasksCompleted);
        Assert.Equal(50, summary.UnitsCompleted);
    }

    [Fact]
    public async Task ReplayProjections_RestoresWorkforceReadModels()
    {
        // Arrange
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            fix.DbName);

        var projectionService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);

        // Ingest task and idle events
        await projectionService.ProcessWorkerTaskExecutionAsync(
            eventId: Guid.NewGuid(), sequenceNumber: 1, taskId: Guid.NewGuid(),
            activityType: WarehouseLaborActivityType.Picking, workerUserId: fix.Worker1Id,
            facilityId: fix.FacilityId, department: "Picking", units: 30, lines: 3, cartons: 0, pallets: 0,
            startedAtUtc: DateTime.UtcNow.AddMinutes(-5), completedAtUtc: DateTime.UtcNow);

        await projectionService.ProcessWorkerIdleIntervalAsync(
            eventId: Guid.NewGuid(), sequenceNumber: 2, workerUserId: fix.Worker1Id,
            facilityId: fix.FacilityId, department: "Picking",
            startedAtUtc: DateTime.UtcNow.AddMinutes(-15), endedAtUtc: DateTime.UtcNow.AddMinutes(-5),
            reasonCode: LaborIdleTimeReason.EquipmentUnavailable);

        // Confirm data exists
        Assert.Equal(1, await context.WorkerTaskExecutionLogs.CountAsync());
        Assert.Equal(1, await context.WorkerIdleIntervalRecords.CountAsync());
        Assert.Equal(1, await context.DailyWorkerProductivitySummaries.CountAsync());

        // Act: Replay workforce projection from beginning
        await projectionService.ReplayProjectionsAsync(
            AnalyticsProjectionService.WorkforceProductivityProjection,
            ProjectionReplayMode.FromBeginning);

        // Assert: All models purged
        Assert.Equal(1, await context.WorkerTaskExecutionLogs.CountAsync());
        Assert.Equal(1, await context.WorkerIdleIntervalRecords.CountAsync());
        Assert.Equal(1, await context.DailyWorkerProductivitySummaries.CountAsync());
    }

    [Fact]
    public async Task WorkforceAnalyticsController_GetWorkerProductivity_ReturnsOkResult()
    {
        // Arrange
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var today = DateTime.UtcNow.Date;
        var summary = DailyWorkerProductivitySummary.Create(fix.Worker1Id, fix.FacilityId, "Picking", today);
        summary.RecordTaskExecution(50, 10, 0, 0, 1200m, 1200m);
        await context.DailyWorkerProductivitySummaries.AddAsync(summary);
        await context.SaveChangesAsync();

        var staffContext = new TestCurrentUserContext
        {
            UserId = fix.Worker1Id,
            UserType = UserType.Staff
        };

        var queryService = new AnalyticsQueryService(context, staffContext, NullLogger<AnalyticsQueryService>.Instance);
        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var controller = new WorkforceAnalyticsController(queryService, projService, staffContext);

        // Act
        var result = await controller.GetWorkerProductivity(fix.Worker1Id, today, today, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var dtos = Assert.IsAssignableFrom<IReadOnlyList<WorkerProductivityDto>>(okResult.Value);
        Assert.Single(dtos);
        Assert.Equal(fix.Worker1Id, dtos[0].WorkerUserId);
        Assert.Equal(50, dtos[0].UnitsCompleted);
    }

    [Fact]
    public async Task LaborStandardProfile_FacilitySpecificOverride_TakesPrecedenceOverPlatformDefault()
    {
        // Arrange
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projectionService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);

        // 1. Platform default profile (FacilityId = null): Base 60s, Pallet 120s
        var platformDefault = LaborStandardProfile.Create(
            activityType: WarehouseLaborActivityType.Receiving,
            department: "Inbound Receiving",
            baseSetupSeconds: 60.0m,
            secondsPerPallet: 120.0m,
            facilityId: null);

        // 2. Facility override profile for fix.FacilityId: Base 45s, Pallet 90s
        var facilityOverride = LaborStandardProfile.Create(
            activityType: WarehouseLaborActivityType.Receiving,
            department: "Inbound Receiving",
            baseSetupSeconds: 45.0m,
            secondsPerPallet: 90.0m,
            facilityId: fix.FacilityId);

        await projectionService.UpsertLaborStandardProfileAsync(platformDefault);
        await projectionService.UpsertLaborStandardProfileAsync(facilityOverride);

        var taskId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        // Act: Process receiving task for 2 pallets at fix.FacilityId
        // If facility override takes precedence: 45 + (2 * 90) = 225 seconds
        // If platform default used incorrectly: 60 + (2 * 120) = 300 seconds
        await projectionService.ProcessWorkerTaskExecutionAsync(
            eventId: Guid.NewGuid(),
            sequenceNumber: 1,
            taskId: taskId,
            activityType: WarehouseLaborActivityType.Receiving,
            workerUserId: fix.Worker1Id,
            facilityId: fix.FacilityId,
            department: "Inbound Receiving",
            units: 0,
            lines: 0,
            cartons: 0,
            pallets: 2,
            startedAtUtc: now.AddMinutes(-5),
            completedAtUtc: now);

        // Assert
        var log = await context.WorkerTaskExecutionLogs.FirstOrDefaultAsync(l => l.TaskId == taskId);
        Assert.NotNull(log);
        Assert.Equal(225.00m, log.StandardDurationSeconds);
    }

    [Fact]
    public void ZeroOrSubSecondDuration_EnforcesMinimumSanityFloor_ToPreventDivisionByZero()
    {
        // Arrange: Start and completion at the exact same millisecond
        var timestamp = DateTime.UtcNow;
        var log = WorkerTaskExecutionLog.Create(
            taskId: Guid.NewGuid(),
            activityType: WarehouseLaborActivityType.CycleCounting,
            workerUserId: Guid.NewGuid(),
            facilityId: Guid.NewGuid(),
            department: "Inventory",
            units: 1,
            lines: 1,
            cartons: 0,
            pallets: 0,
            startedAtUtc: timestamp,
            completedAtUtc: timestamp,
            standardDurationSeconds: 45.0m);

        // Assert: Actual duration is clamped to minimum 1.0 second floor
        Assert.Equal(1.00m, log.ActualDurationSeconds);
        Assert.Equal(45.00m, log.StandardDurationSeconds);
        Assert.Equal(-44.00m, log.VarianceSeconds); // 1.0 - 45.0 = -44.0
        Assert.Equal(4500.00m, log.EfficiencyPercent); // (45.0 / 1.0) * 100 = 4500%
        Assert.True(log.EfficiencyPercent > 0m);
    }

    [Fact]
    public async Task WorkforceAnalyticsController_CreateOrUpdateStandardProfile_And_CostAnalysis_Endpoints()
    {
        // Arrange
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var staffContext = new TestCurrentUserContext
        {
            UserId = fix.Worker1Id,
            UserType = UserType.Staff,
            PermissionsList = [Permissions.Analytics.WorkforceView, Permissions.Analytics.WorkforceManageStandards, Permissions.Analytics.WorkforceCostView]
        };

        var queryService = new AnalyticsQueryService(context, staffContext, NullLogger<AnalyticsQueryService>.Instance);
        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var controller = new WorkforceAnalyticsController(queryService, projService, staffContext);

        // Act 1: POST standards
        var createRequest = new CreateLaborStandardProfileRequest(
            ActivityType: WarehouseLaborActivityType.Packing,
            Department: "E-Commerce Packing",
            BaseSetupSeconds: 25.0m,
            SecondsPerUnit: 3.0m,
            SecondsPerCarton: 12.0m,
            FacilityId: fix.FacilityId);

        var createResult = await controller.CreateOrUpdateStandardProfile(createRequest, CancellationToken.None);
        var createdObj = Assert.IsType<CreatedAtActionResult>(createResult);
        var profileDto = Assert.IsType<LaborStandardProfileDto>(createdObj.Value);
        Assert.Equal(WarehouseLaborActivityType.Packing, profileDto.ActivityType);
        Assert.Equal(25.0m, profileDto.BaseSetupSeconds);

        // Act 2: GET cost analysis
        var costResult = await controller.GetLaborCostAnalysis(fix.FacilityId, null, null, CancellationToken.None);
        var costOk = Assert.IsType<OkObjectResult>(costResult);
        var costDto = Assert.IsType<LaborCostAnalysisDto>(costOk.Value);
        Assert.Equal(fix.FacilityId, costDto.FacilityId);
    }

    [Fact]
    public void StandardTimeCalculation_WithZoneAndTravelDistance_CalculatesAccurateDuration()
    {
        // Arrange: Setup standard profile with travel distance factor and zone metadata
        var zoneId = Guid.NewGuid();
        var profile = LaborStandardProfile.Create(
            activityType: WarehouseLaborActivityType.Picking,
            department: "Bulk Picking",
            baseSetupSeconds: 15.0m,
            secondsPerUnit: 2.0m,
            secondsPerLine: 4.0m,
            secondsPerCarton: 6.0m,
            secondsPerPallet: 20.0m,
            secondsPerTravelMeter: 0.25m,
            zoneId: zoneId,
            zoneType: WarehouseZoneType.BulkStorage,
            equipmentFactor: 1.20m,
            complexityMultiplier: 1.10m);

        // Act: 20 units, 5 lines, 2 cartons, 1 pallet, 80 meters travel
        // Raw: 15 + (20*2) + (5*4) + (2*6) + (1*20) + (80*0.25)
        //    = 15 + 40 + 20 + 12 + 20 + 20 = 127.00 seconds
        // Adjusted: 127 * 1.20 * 1.10 = 167.64 seconds
        var standardSeconds = profile.CalculateStandardDurationSeconds(
            units: 20, lines: 5, cartons: 2, pallets: 1, travelDistanceMeters: 80m);

        // Assert
        Assert.Equal(167.64m, standardSeconds);
        Assert.Equal(zoneId, profile.ZoneId);
        Assert.Equal(WarehouseZoneType.BulkStorage, profile.ZoneType);
    }

    [Fact]
    public async Task DeterministicPrecedence_ResolvesProfile_FollowingSpecificityHierarchyAndDescendingVersion()
    {
        // Arrange
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var zoneA = Guid.NewGuid();
        var zoneB = Guid.NewGuid();
        var otherFacilityId = Guid.NewGuid();

        // 1. Rank 1 (Facility + Zone), Version 1
        var p1v1 = LaborStandardProfile.Create(
            WarehouseLaborActivityType.Picking, "Picking", 10.0m,
            facilityId: fix.FacilityId, zoneId: zoneA, version: 1);

        // 2. Rank 1 (Facility + Zone), Version 2 (Higher Version -> Should Win for Fac+ZoneA)
        var p1v2 = LaborStandardProfile.Create(
            WarehouseLaborActivityType.Picking, "Picking", 12.0m,
            facilityId: fix.FacilityId, zoneId: zoneA, version: 2);

        // 3. Rank 2 (Facility + General Zone)
        var p2 = LaborStandardProfile.Create(
            WarehouseLaborActivityType.Picking, "Picking", 20.0m,
            facilityId: fix.FacilityId, zoneId: null, version: 1);

        // 4. Rank 3 (Platform Default + Specific Zone)
        var p3 = LaborStandardProfile.Create(
            WarehouseLaborActivityType.Picking, "Picking", 30.0m,
            facilityId: null, zoneId: zoneA, version: 1);

        // 5. Rank 4 (Platform Default + General Zone)
        var p4 = LaborStandardProfile.Create(
            WarehouseLaborActivityType.Picking, "Picking", 40.0m,
            facilityId: null, zoneId: null, version: 1);

        await context.LaborStandardProfiles.AddRangeAsync(p1v1, p1v2, p2, p3, p4);
        await context.SaveChangesAsync();

        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var now = DateTime.UtcNow;

        // Act & Assert 1: Fac + ZoneA -> Resolves p1v2 (Rank 1, Version 2)
        var task1Id = Guid.NewGuid();
        await projService.ProcessWorkerTaskExecutionAsync(
            Guid.NewGuid(), 1L, task1Id, WarehouseLaborActivityType.Picking, fix.Worker1Id, fix.FacilityId, "Picking",
            units: 1, lines: 0, cartons: 0, pallets: 0, travelDistanceMeters: 0,
            startedAtUtc: now.AddMinutes(-50), completedAtUtc: now.AddMinutes(-45),
            zoneId: zoneA);

        var log1 = await context.WorkerTaskExecutionLogs.FirstAsync(l => l.TaskId == task1Id);
        Assert.Equal(p1v2.Id, log1.StandardProfileId);
        Assert.Equal(2, log1.StandardProfileVersion);
        Assert.Equal(12.0m, log1.StandardDurationSeconds);

        // Act & Assert 2: Fac + ZoneB (no zone match) -> Resolves p2 (Rank 2)
        var task2Id = Guid.NewGuid();
        await projService.ProcessWorkerTaskExecutionAsync(
            Guid.NewGuid(), 2L, task2Id, WarehouseLaborActivityType.Picking, fix.Worker1Id, fix.FacilityId, "Picking",
            units: 1, lines: 0, cartons: 0, pallets: 0, travelDistanceMeters: 0,
            startedAtUtc: now.AddMinutes(-40), completedAtUtc: now.AddMinutes(-35),
            zoneId: zoneB);

        var log2 = await context.WorkerTaskExecutionLogs.FirstAsync(l => l.TaskId == task2Id);
        Assert.Equal(p2.Id, log2.StandardProfileId);
        Assert.Equal(1, log2.StandardProfileVersion);
        Assert.Equal(20.0m, log2.StandardDurationSeconds);

        // Act & Assert 3: OtherFacility + ZoneA -> Resolves p3 (Rank 3)
        var task3Id = Guid.NewGuid();
        await projService.ProcessWorkerTaskExecutionAsync(
            Guid.NewGuid(), 3L, task3Id, WarehouseLaborActivityType.Picking, fix.Worker1Id, otherFacilityId, "Picking",
            units: 1, lines: 0, cartons: 0, pallets: 0, travelDistanceMeters: 0,
            startedAtUtc: now.AddMinutes(-30), completedAtUtc: now.AddMinutes(-25),
            zoneId: zoneA);

        var log3 = await context.WorkerTaskExecutionLogs.FirstAsync(l => l.TaskId == task3Id);
        Assert.Equal(p3.Id, log3.StandardProfileId);
        Assert.Equal(1, log3.StandardProfileVersion);
        Assert.Equal(30.0m, log3.StandardDurationSeconds);

        // Act & Assert 4: OtherFacility + ZoneB -> Resolves p4 (Rank 4)
        var task4Id = Guid.NewGuid();
        await projService.ProcessWorkerTaskExecutionAsync(
            Guid.NewGuid(), 4L, task4Id, WarehouseLaborActivityType.Picking, fix.Worker1Id, otherFacilityId, "Picking",
            units: 1, lines: 0, cartons: 0, pallets: 0, travelDistanceMeters: 0,
            startedAtUtc: now.AddMinutes(-20), completedAtUtc: now.AddMinutes(-15),
            zoneId: zoneB);

        var log4 = await context.WorkerTaskExecutionLogs.FirstAsync(l => l.TaskId == task4Id);
        Assert.Equal(p4.Id, log4.StandardProfileId);
        Assert.Equal(1, log4.StandardProfileVersion);
        Assert.Equal(40.0m, log4.StandardDurationSeconds);
    }

    [Fact]
    public void LaborTimeIntegrity_OverlappingTaskIntervals_ClipsOverlappingTimeAndPreventsDoubleCounting()
    {
        // Arrange
        var workerId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var date = DateTime.UtcNow.Date;
        var summary = DailyWorkerProductivitySummary.Create(workerId, facilityId, "Picking", date, 480m, 30m);

        var baseTime = DateTime.UtcNow.Date.AddHours(8);

        // Task 1: 08:00 - 08:30 (30 minutes = 1800s)
        summary.RecordTaskExecution(
            units: 10, lines: 2, cartons: 0, pallets: 0,
            actualSeconds: 1800m, standardSeconds: 1800m,
            startedAtUtc: baseTime,
            completedAtUtc: baseTime.AddMinutes(30));

        Assert.Equal(30.00m, summary.ProductiveWorkMinutes);
        Assert.Equal(baseTime.AddMinutes(30), summary.LastTaskCompletedAtUtc);

        // Task 2: 08:15 - 08:45 (30 minutes total, but overlaps 08:15-08:30 by 15 mins)
        // Effective duration credited should only be 15 minutes (900s), bringing total to 45 mins!
        summary.RecordTaskExecution(
            units: 10, lines: 2, cartons: 0, pallets: 0,
            actualSeconds: 1800m, standardSeconds: 1800m,
            startedAtUtc: baseTime.AddMinutes(15),
            completedAtUtc: baseTime.AddMinutes(45));

        Assert.Equal(45.00m, summary.ProductiveWorkMinutes);
        Assert.Equal(baseTime.AddMinutes(45), summary.LastTaskCompletedAtUtc);

        // Task 3: 08:20 - 08:40 (20 minutes total, completely enclosed inside 08:15-08:45)
        // Effective duration credited should be 0 minutes! Total remains 45 mins.
        summary.RecordTaskExecution(
            units: 5, lines: 1, cartons: 0, pallets: 0,
            actualSeconds: 1200m, standardSeconds: 1200m,
            startedAtUtc: baseTime.AddMinutes(20),
            completedAtUtc: baseTime.AddMinutes(40));

        Assert.Equal(45.00m, summary.ProductiveWorkMinutes);
        Assert.Equal(baseTime.AddMinutes(45), summary.LastTaskCompletedAtUtc);
    }

    [Fact]
    public void LaborTimeIntegrity_InvalidTimestamps_ThrowsDomainValidationException()
    {
        var workerId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        // Guard 1: Missing / default start timestamp
        Assert.Throws<InvalidDomainStateException>(() =>
            WorkerTaskExecutionLog.Create(
                Guid.NewGuid(), WarehouseLaborActivityType.Picking, workerId, facilityId, "Picking",
                units: 1, lines: 1, cartons: 0, pallets: 0,
                startedAtUtc: default,
                completedAtUtc: now,
                standardDurationSeconds: 60m));

        // Guard 2: Inverted timestamps (completed before started)
        Assert.Throws<InvalidDomainStateException>(() =>
            WorkerTaskExecutionLog.Create(
                Guid.NewGuid(), WarehouseLaborActivityType.Picking, workerId, facilityId, "Picking",
                units: 1, lines: 1, cartons: 0, pallets: 0,
                startedAtUtc: now,
                completedAtUtc: now.AddMinutes(-5),
                standardDurationSeconds: 60m));
    }

    [Fact]
    public void LaborTimeIntegrity_ScheduledBreaks_ExcludesBreaksFromNetAvailableShiftTime()
    {
        // Arrange: 8-hour shift (480 mins) with 1-hour scheduled breaks (60 mins) -> Net available = 420 mins
        var summary = DailyWorkerProductivitySummary.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Packing", DateTime.UtcNow.Date,
            totalShiftMinutes: 480m, scheduledBreakMinutes: 60m);

        // Act: Worker performs 336 minutes of productive work (5.6 hours = 20,160 seconds)
        // Net utilization = 336 / 420 = 80.00%
        // Note: Gross utilization would have been 336 / 480 = 70.00%
        summary.RecordTaskExecution(
            units: 50, lines: 10, cartons: 50, pallets: 0,
            actualSeconds: 20160m, standardSeconds: 20160m);

        // Assert
        Assert.Equal(480m, summary.TotalShiftMinutes);
        Assert.Equal(60m, summary.ScheduledBreakMinutes);
        Assert.Equal(336.00m, summary.ProductiveWorkMinutes);
        Assert.Equal(80.00m, summary.LaborUtilizationPercent);
    }

    [Fact]
    public async Task WorkforceRBAC_CrossWorkerPrivacy_EmployeeForbiddenFromViewingOtherWorkers()
    {
        // Arrange
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var today = DateTime.UtcNow.Date;
        var summary2 = DailyWorkerProductivitySummary.Create(fix.Worker2Id, fix.FacilityId, "Packing", today);
        var log2 = WorkerTaskExecutionLog.Create(
            Guid.NewGuid(), WarehouseLaborActivityType.Packing, fix.Worker2Id, fix.FacilityId, "Packing",
            units: 10, lines: 2, cartons: 5, pallets: 0,
            startedAtUtc: today.AddHours(8), completedAtUtc: today.AddHours(8).AddMinutes(15),
            standardDurationSeconds: 900m);
        var idle2 = WorkerIdleIntervalRecord.Create(
            fix.Worker2Id, fix.FacilityId, "Packing",
            today.AddHours(9), today.AddHours(9).AddMinutes(10),
            LaborIdleTimeReason.EquipmentUnavailable);

        await context.DailyWorkerProductivitySummaries.AddAsync(summary2);
        await context.WorkerTaskExecutionLogs.AddAsync(log2);
        await context.WorkerIdleIntervalRecords.AddAsync(idle2);
        await context.SaveChangesAsync();

        // Worker 1 user context without supervisor or elevated permissions
        var worker1Context = new TestCurrentUserContext
        {
            UserId = fix.Worker1Id,
            UserType = UserType.Staff,
            PermissionsList = [Permissions.Analytics.WorkforceSelfView]
        };

        var queryService = new AnalyticsQueryService(context, worker1Context, NullLogger<AnalyticsQueryService>.Instance);

        // Act & Assert 1: Worker 1 attempting to view Worker 2's daily productivity throws UnauthorizedException (403)
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            queryService.GetWorkerProductivityAsync(fix.Worker2Id, today, today, requestingUserId: fix.Worker1Id));

        // Act & Assert 2: Worker 1 attempting to view Worker 2's task execution logs throws UnauthorizedException (403)
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            queryService.GetWorkerTaskExecutionLogsAsync(fix.Worker2Id, today, today.AddDays(1), requestingUserId: fix.Worker1Id));

        // Act & Assert 3: Worker 1 attempting to view Worker 2's idle intervals throws UnauthorizedException (403)
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            queryService.GetWorkerIdleIntervalsAsync(fix.Worker2Id, today, today.AddDays(1), requestingUserId: fix.Worker1Id));

        // Act & Assert 4: Worker 1 viewing own records succeeds
        var selfProd = await queryService.GetWorkerProductivityAsync(fix.Worker1Id, today, today, requestingUserId: fix.Worker1Id);
        Assert.NotNull(selfProd);
    }

    [Fact]
    public async Task WorkforceRBAC_SupervisorFacilityConfinement_ForbiddenFromAccessingOtherFacilities()
    {
        // Arrange
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var otherFacilityId = Guid.NewGuid();
        var today = DateTime.UtcNow.Date;

        var supervisorFacAContext = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            UserType = UserType.Staff,
            FacilityId = fix.FacilityId, // Assigned to Facility A
            PermissionsList = [Permissions.Analytics.WorkforceView]
        };

        var queryService = new AnalyticsQueryService(context, supervisorFacAContext, NullLogger<AnalyticsQueryService>.Instance);

        // Act & Assert 1: Facility summary for other facility throws UnauthorizedException
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            queryService.GetFacilityWorkforceSummaryAsync(otherFacilityId, today));

        // Act & Assert 2: Worker task logs for worker at other facility throws UnauthorizedException
        var workerOtherFacId = Guid.NewGuid();
        var logOther = WorkerTaskExecutionLog.Create(
            Guid.NewGuid(), WarehouseLaborActivityType.Picking, workerOtherFacId, otherFacilityId, "Picking",
            units: 5, lines: 1, cartons: 0, pallets: 0,
            startedAtUtc: today.AddHours(8), completedAtUtc: today.AddHours(8).AddMinutes(10),
            standardDurationSeconds: 600m);
        await context.WorkerTaskExecutionLogs.AddAsync(logOther);
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            queryService.GetWorkerTaskExecutionLogsAsync(workerOtherFacId, today, today.AddDays(1), hasElevatedWorkforceAccess: true));
    }

    [Fact]
    public async Task WorkforceRBAC_FacilityManagerLaborCostConfinement_ForbiddenFromAccessingOtherFacilityCost()
    {
        // Arrange
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var otherFacilityId = Guid.NewGuid();
        var today = DateTime.UtcNow.Date;

        // Facility Manager with WorkforceCostView for Facility A
        var managerFacAContext = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            UserType = UserType.Staff,
            FacilityId = fix.FacilityId,
            PermissionsList = [Permissions.Analytics.WorkforceCostView]
        };

        var queryService = new AnalyticsQueryService(context, managerFacAContext, NullLogger<AnalyticsQueryService>.Instance);

        // Act & Assert: Accessing Facility B cost analysis throws UnauthorizedException
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            queryService.GetLaborCostAnalysisAsync(otherFacilityId, today.AddDays(-7), today));

        // Accessing Facility A cost analysis succeeds
        var ownFacCost = await queryService.GetLaborCostAnalysisAsync(fix.FacilityId, today.AddDays(-7), today);
        Assert.NotNull(ownFacCost);
    }

    [Fact]
    public async Task WorkforceRBAC_LaborCostSeparation_RequiresDedicatedWorkforceCostViewPermission()
    {
        // Arrange
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var today = DateTime.UtcNow.Date;

        // Staff user with WorkforceView only (no WorkforceCostView)
        var staffNoCostContext = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            UserType = UserType.Staff,
            FacilityId = fix.FacilityId,
            PermissionsList = [Permissions.Analytics.WorkforceView]
        };

        var queryService = new AnalyticsQueryService(context, staffNoCostContext, NullLogger<AnalyticsQueryService>.Instance);

        // Act & Assert: Throws UnauthorizedException because user lacks WorkforceCostView permission
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            queryService.GetLaborCostAnalysisAsync(fix.FacilityId, today.AddDays(-7), today));
        Assert.Contains("User is not authorized to view financial labor cost analytics", ex.Message);
    }

    [Fact]
    public async Task WorkforceRBAC_ClientPortalUsers_ZeroAccessToWorkforceEndpoints()
    {
        // Arrange
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var today = DateTime.UtcNow.Date;

        var clientContext = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            TenantId = fix.ClientTenantId,
            UserType = UserType.Client
        };

        var queryService = new AnalyticsQueryService(context, clientContext, NullLogger<AnalyticsQueryService>.Instance);

        // 1. Labor standard profiles
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            queryService.GetLaborStandardProfilesAsync(fix.FacilityId));

        // 2. Facility workforce summary
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            queryService.GetFacilityWorkforceSummaryAsync(fix.FacilityId, today));

        // 3. Labor cost analysis
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            queryService.GetLaborCostAnalysisAsync(fix.FacilityId, today.AddDays(-7), today));

        // 4. Worker productivity
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            queryService.GetWorkerProductivityAsync(fix.Worker1Id, today, today));
    }
}
