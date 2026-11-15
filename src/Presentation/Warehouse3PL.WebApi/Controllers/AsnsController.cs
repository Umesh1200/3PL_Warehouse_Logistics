using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Inbound.Asns.Commands.CancelAsn;
using Warehouse3PL.Application.Features.Inbound.Asns.Commands.CreateAsn;
using Warehouse3PL.Application.Features.Inbound.Asns.Commands.MarkAsnArrived;
using Warehouse3PL.Application.Features.Inbound.Asns.Commands.MarkAsnInTransit;
using Warehouse3PL.Application.Features.Inbound.Asns.Queries.GetAsnById;
using Warehouse3PL.Application.Features.Inbound.Asns.Queries.GetAsns;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Inbound - Advance Shipping Notices")]
public class AsnsController : BaseApiController
{
    [HttpGet]
    [HasPermission(Permissions.Inbound.AsnView)]
    [ProducesResponseType(typeof(PagedResult<AdvanceShippingNoticeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAsns([FromQuery] GetAsnsQuery query, CancellationToken ct)
    {
        var result = await Mediator.Send(query, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Inbound.AsnView)]
    [ProducesResponseType(typeof(AdvanceShippingNoticeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsnById([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await Mediator.Send(new GetAsnByIdQuery(id), ct);
        return Ok(result);
    }

    [HttpPost]
    [HasPermission(Permissions.Inbound.AsnCreate)]
    [ProducesResponseType(typeof(AdvanceShippingNoticeDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateAsn([FromBody] CreateAsnCommand command, CancellationToken ct)
    {
        var result = await Mediator.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("{id:guid}/in-transit")]
    [HasPermission(Permissions.Inbound.AsnEdit)]
    [ProducesResponseType(typeof(AdvanceShippingNoticeDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkInTransit([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await Mediator.Send(new MarkAsnInTransitCommand(id), ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/arrived")]
    [HasPermission(Permissions.Inbound.AsnEdit)]
    [ProducesResponseType(typeof(AdvanceShippingNoticeDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkArrived([FromRoute] Guid id, [FromBody] MarkArrivedRequest? request, CancellationToken ct)
    {
        var result = await Mediator.Send(new MarkAsnArrivedCommand(id, request?.ArrivedAt), ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.Inbound.AsnCancel)]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    public async Task<IActionResult> CancelAsn([FromRoute] Guid id, [FromBody] CancelAsnRequest request, CancellationToken ct)
    {
        var result = await Mediator.Send(new CancelAsnCommand(id, request.Reason), ct);
        return Ok(result);
    }
}

public record MarkArrivedRequest(DateTime? ArrivedAt);
public record CancelAsnRequest(string Reason);

