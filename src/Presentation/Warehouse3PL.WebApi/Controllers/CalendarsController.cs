using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Reference.Commands;
using Warehouse3PL.Application.Features.Reference.Queries;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.WebApi.Controllers;

[Authorize]
[Route("api/v1/calendars")]
[Tags("Facility & Country Working Calendars")]
public sealed class CalendarsController : BaseApiController
{
    public sealed record CreateHolidayRequest(
        string CountryCode,
        HolidayScope HolidayScope,
        DateOnly HolidayDate,
        string Name,
        string? SubdivisionCode = null,
        Guid? FacilityId = null,
        bool IsWorkingDayOverride = false);

    public sealed record UpdateHolidayRequest(
        string Name,
        bool IsWorkingDayOverride,
        bool IsActive,
        long ExpectedConfigRevision);

    public sealed record DeactivateHolidayRequest(long? ExpectedConfigRevision = null);

    public sealed record UpsertFacilityCalendarRequest(
        DateTime EffectiveFromUtc,
        DateTime? EffectiveToUtc,
        string WeeklyRestDaysCsv,
        TimeSpan WorkdayStartLocalTime,
        TimeSpan WorkdayEndLocalTime,
        TimeSpan DefaultCutoffLocalTime,
        bool SupersedeOverlapping = false,
        long? ExpectedSupersedeRevision = null);

    [HttpGet("holidays")]
    [ProducesResponseType(typeof(IReadOnlyList<HolidayCalendarDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<HolidayCalendarDto>>> GetHolidays(
        [FromQuery] string? countryCode = null,
        [FromQuery] string? subdivisionCode = null,
        [FromQuery] Guid? facilityId = null,
        [FromQuery] DateOnly? fromDate = null,
        [FromQuery] DateOnly? toDate = null,
        [FromQuery] bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var res = await Mediator.Send(
            new GetHolidayCalendarsQuery(countryCode, subdivisionCode, facilityId, fromDate, toDate, activeOnly),
            cancellationToken);
        return Ok(res);
    }

    [HttpPost("holidays")]
    [ProducesResponseType(typeof(HolidayCalendarDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<HolidayCalendarDto>> CreateHoliday(
        [FromBody] CreateHolidayRequest request,
        CancellationToken cancellationToken = default)
    {
        var created = await Mediator.Send(
            new CreateHolidayCalendarCommand(
                request.CountryCode,
                request.HolidayScope,
                request.HolidayDate,
                request.Name,
                request.SubdivisionCode,
                request.FacilityId,
                request.IsWorkingDayOverride),
            cancellationToken);
        return Created($"/api/v1/calendars/holidays/{created.Id}", created);
    }

    [HttpPut("holidays/{id:guid}")]
    [ProducesResponseType(typeof(HolidayCalendarDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<HolidayCalendarDto>> UpdateHoliday(
        Guid id,
        [FromBody] UpdateHolidayRequest request,
        CancellationToken cancellationToken = default)
    {
        var updated = await Mediator.Send(
            new UpdateHolidayCalendarCommand(
                id,
                request.Name,
                request.IsWorkingDayOverride,
                request.IsActive,
                request.ExpectedConfigRevision),
            cancellationToken);
        return Ok(updated);
    }

    [HttpPatch("holidays/{id:guid}/deactivate")]
    [ProducesResponseType(typeof(HolidayCalendarDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<HolidayCalendarDto>> DeactivateHoliday(
        Guid id,
        [FromBody] DeactivateHolidayRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var updated = await Mediator.Send(
            new DeactivateHolidayCalendarCommand(id, request?.ExpectedConfigRevision),
            cancellationToken);
        return Ok(updated);
    }

    [HttpGet("facilities/{facilityId:guid}")]
    [ProducesResponseType(typeof(IReadOnlyList<FacilityCalendarDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<FacilityCalendarDto>>> GetFacilityCalendars(
        Guid facilityId,
        [FromQuery] bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var res = await Mediator.Send(new GetFacilityCalendarsQuery(facilityId, activeOnly), cancellationToken);
        return Ok(res);
    }

    [HttpPost("facilities/{facilityId:guid}")]
    [ProducesResponseType(typeof(FacilityCalendarDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<FacilityCalendarDto>> UpsertFacilityCalendar(
        Guid facilityId,
        [FromBody] UpsertFacilityCalendarRequest request,
        CancellationToken cancellationToken = default)
    {
        var created = await Mediator.Send(
            new UpsertFacilityCalendarCommand(
                facilityId,
                request.EffectiveFromUtc,
                request.EffectiveToUtc,
                request.WeeklyRestDaysCsv,
                request.WorkdayStartLocalTime,
                request.WorkdayEndLocalTime,
                request.DefaultCutoffLocalTime,
                request.SupersedeOverlapping,
                request.ExpectedSupersedeRevision),
            cancellationToken);
        return Created($"/api/v1/calendars/facilities/{facilityId}", created);
    }

    [HttpGet("facilities/{facilityId:guid}/working-day")]
    [ProducesResponseType(typeof(WorkingDayEvaluationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkingDayEvaluationResult>> EvaluateWorkingDay(
        Guid facilityId,
        [FromQuery] DateOnly? localDate = null,
        [FromQuery] DateTime? utcInstant = null,
        CancellationToken cancellationToken = default)
    {
        var res = await Mediator.Send(
            new EvaluateFacilityWorkingDayQuery(facilityId, localDate, utcInstant),
            cancellationToken);
        return Ok(res);
    }
}
