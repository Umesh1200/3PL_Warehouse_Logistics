using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Analytics - Workforce & Labor Productivity")]
[Route("api/v1/analytics/workforce")]
public class WorkforceAnalyticsController : BaseApiController
{
    private readonly IAnalyticsQueryService _analyticsQueryService;
    private readonly IAnalyticsProjectionService _analyticsProjectionService;
    private readonly ICurrentUserContext _currentUser;

    public WorkforceAnalyticsController(
        IAnalyticsQueryService analyticsQueryService,
        IAnalyticsProjectionService analyticsProjectionService,
        ICurrentUserContext currentUser)
    {
        _analyticsQueryService = analyticsQueryService;
        _analyticsProjectionService = analyticsProjectionService;
        _currentUser = currentUser;
    }

    [HttpGet("worker/{workerUserId}")]
    [HasPermission(Permissions.Analytics.WorkforceView)]
    [ProducesResponseType(typeof(IReadOnlyList<WorkerProductivityDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWorkerProductivity(
        [FromRoute] Guid workerUserId,
        [FromQuery] DateTime? startDateUtc,
        [FromQuery] DateTime? endDateUtc,
        CancellationToken ct)
    {
        var start = startDateUtc ?? DateTime.UtcNow.Date.AddDays(-7);
        var end = endDateUtc ?? DateTime.UtcNow;
        var hasElevated = _currentUser.IsStaff || _currentUser.IsSuperAdmin;

        var result = await _analyticsQueryService.GetWorkerProductivityAsync(
            workerUserId, start, end, _currentUser.UserId, hasElevated, ct);
        return Ok(result);
    }

    [HttpGet("facility/{facilityId}")]
    [HasPermission(Permissions.Analytics.WorkforceView)]
    [ProducesResponseType(typeof(FacilityWorkforceSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFacilityWorkforceSummary(
        [FromRoute] Guid facilityId,
        [FromQuery] DateTime? dateUtc,
        CancellationToken ct)
    {
        var date = dateUtc ?? DateTime.UtcNow;
        var result = await _analyticsQueryService.GetFacilityWorkforceSummaryAsync(facilityId, date, ct);
        return Ok(result);
    }

    [HttpGet("worker/{workerUserId}/task-logs")]
    [HasPermission(Permissions.Analytics.WorkforceView)]
    [ProducesResponseType(typeof(IReadOnlyList<WorkerTaskExecutionLogDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWorkerTaskExecutionLogs(
        [FromRoute] Guid workerUserId,
        [FromQuery] DateTime? startDateUtc,
        [FromQuery] DateTime? endDateUtc,
        CancellationToken ct)
    {
        var start = startDateUtc ?? DateTime.UtcNow.Date;
        var end = endDateUtc ?? DateTime.UtcNow;
        var hasElevated = _currentUser.IsStaff || _currentUser.IsSuperAdmin;

        var result = await _analyticsQueryService.GetWorkerTaskExecutionLogsAsync(
            workerUserId, start, end, _currentUser.UserId, hasElevated, ct);
        return Ok(result);
    }

    [HttpGet("worker/{workerUserId}/idle-intervals")]
    [HasPermission(Permissions.Analytics.WorkforceView)]
    [ProducesResponseType(typeof(IReadOnlyList<WorkerIdleIntervalDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWorkerIdleIntervals(
        [FromRoute] Guid workerUserId,
        [FromQuery] DateTime? startDateUtc,
        [FromQuery] DateTime? endDateUtc,
        CancellationToken ct)
    {
        var start = startDateUtc ?? DateTime.UtcNow.Date;
        var end = endDateUtc ?? DateTime.UtcNow;
        var hasElevated = _currentUser.IsStaff || _currentUser.IsSuperAdmin;

        var result = await _analyticsQueryService.GetWorkerIdleIntervalsAsync(
            workerUserId, start, end, _currentUser.UserId, hasElevated, ct);
        return Ok(result);
    }

    [HttpGet("standards")]
    [HasPermission(Permissions.Analytics.WorkforceView)]
    [ProducesResponseType(typeof(IReadOnlyList<LaborStandardProfileDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLaborStandardProfiles(
        [FromQuery] Guid? facilityId,
        [FromQuery] WarehouseLaborActivityType? activityType,
        [FromQuery] Guid? zoneId,
        CancellationToken ct)
    {
        var result = await _analyticsQueryService.GetLaborStandardProfilesAsync(facilityId, activityType, zoneId, ct);
        return Ok(result);
    }

    [HttpPost("standards")]
    [HasPermission(Permissions.Analytics.WorkforceManageStandards)]
    [ProducesResponseType(typeof(LaborStandardProfileDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateOrUpdateStandardProfile(
        [FromBody] CreateLaborStandardProfileRequest request,
        CancellationToken ct)
    {
        var profile = LaborStandardProfile.Create(
            request.ActivityType,
            request.Department,
            request.BaseSetupSeconds,
            request.SecondsPerUnit,
            request.SecondsPerLine,
            request.SecondsPerCarton,
            request.SecondsPerPallet,
            request.SecondsPerTravelMeter,
            request.FacilityId,
            request.ZoneId,
            request.ZoneType,
            request.EquipmentFactor,
            request.ComplexityMultiplier,
            request.Version,
            request.EffectiveFromUtc);

        await _analyticsProjectionService.UpsertLaborStandardProfileAsync(profile, ct);

        var dto = new LaborStandardProfileDto
        {
            Id = profile.Id,
            FacilityId = profile.FacilityId,
            ZoneId = profile.ZoneId,
            ZoneType = profile.ZoneType,
            ActivityType = profile.ActivityType,
            Department = profile.Department,
            BaseSetupSeconds = profile.BaseSetupSeconds,
            SecondsPerUnit = profile.SecondsPerUnit,
            SecondsPerLine = profile.SecondsPerLine,
            SecondsPerCarton = profile.SecondsPerCarton,
            SecondsPerPallet = profile.SecondsPerPallet,
            SecondsPerTravelMeter = profile.SecondsPerTravelMeter,
            EquipmentFactor = profile.EquipmentFactor,
            ComplexityMultiplier = profile.ComplexityMultiplier,
            Version = profile.Version,
            EffectiveFromUtc = profile.EffectiveFromUtc,
            EffectiveToUtc = profile.EffectiveToUtc,
            IsActive = profile.IsActive
        };

        return CreatedAtAction(nameof(GetLaborStandardProfiles), new { facilityId = profile.FacilityId }, dto);
    }

    [HttpGet("facility/{facilityId}/cost-analysis")]
    [HasPermission(Permissions.Analytics.WorkforceCostView)]
    [ProducesResponseType(typeof(LaborCostAnalysisDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLaborCostAnalysis(
        [FromRoute] Guid facilityId,
        [FromQuery] DateTime? startDateUtc,
        [FromQuery] DateTime? endDateUtc,
        CancellationToken ct)
    {
        var start = startDateUtc ?? DateTime.UtcNow.Date.AddDays(-30);
        var end = endDateUtc ?? DateTime.UtcNow;
        var result = await _analyticsQueryService.GetLaborCostAnalysisAsync(facilityId, start, end, ct);
        return Ok(result);
    }
}

public record CreateLaborStandardProfileRequest(
    WarehouseLaborActivityType ActivityType,
    string Department,
    decimal BaseSetupSeconds,
    decimal SecondsPerUnit = 0m,
    decimal SecondsPerLine = 0m,
    decimal SecondsPerCarton = 0m,
    decimal SecondsPerPallet = 0m,
    decimal SecondsPerTravelMeter = 0m,
    Guid? FacilityId = null,
    Guid? ZoneId = null,
    WarehouseZoneType? ZoneType = null,
    decimal EquipmentFactor = 1.0m,
    decimal ComplexityMultiplier = 1.0m,
    int Version = 1,
    DateTime? EffectiveFromUtc = null);
