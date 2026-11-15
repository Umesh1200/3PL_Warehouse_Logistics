using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Inbound.DockManagement.Appointments.Commands.CancelDockAppointment;
using Warehouse3PL.Application.Features.Inbound.DockManagement.Appointments.Commands.RescheduleDockAppointment;
using Warehouse3PL.Application.Features.Inbound.DockManagement.Appointments.Commands.ScheduleDockAppointment;
using Warehouse3PL.Application.Features.Inbound.DockManagement.Appointments.Queries.GetDockAppointments;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Inbound - Dock Appointments")]
[Route("api/v1/dock-appointments")]
public class DockAppointmentsController : BaseApiController
{
    [HttpGet]
    [HasPermission(Permissions.Inbound.AppointmentsView)]
    [ProducesResponseType(typeof(PagedResult<DockAppointmentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDockAppointments([FromQuery] GetDockAppointmentsQuery query, CancellationToken ct)
    {
        var result = await Mediator.Send(query, ct);
        return Ok(result);
    }

    [HttpPost]
    [HasPermission(Permissions.Inbound.AppointmentsSchedule)]
    [ProducesResponseType(typeof(DockAppointmentDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> ScheduleAppointment([FromBody] ScheduleDockAppointmentCommand command, CancellationToken ct)
    {
        var result = await Mediator.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:guid}/reschedule")]
    [HasPermission(Permissions.Inbound.AppointmentsSchedule)]
    [ProducesResponseType(typeof(DockAppointmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RescheduleAppointment(
        [FromRoute] Guid id,
        [FromBody] RescheduleDockAppointmentRequest request,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new RescheduleDockAppointmentCommand
        {
            Id = id,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            DockDoorId = request.DockDoorId
        }, ct);

        return Ok(result);
    }

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.Inbound.AppointmentsCancel)]
    [ProducesResponseType(typeof(DockAppointmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelAppointment(
        [FromRoute] Guid id,
        [FromBody] CancelDockAppointmentRequest request,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new CancelDockAppointmentCommand
        {
            Id = id,
            Reason = request.Reason
        }, ct);

        return Ok(result);
    }
}

public record RescheduleDockAppointmentRequest
{
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public Guid? DockDoorId { get; init; }
}

public record CancelDockAppointmentRequest
{
    public string Reason { get; init; } = string.Empty;
}

