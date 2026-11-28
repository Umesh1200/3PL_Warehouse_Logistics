using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Inbound.Suppliers;
using Warehouse3PL.Application.Features.Inbound.Suppliers.Commands.CreateSupplier;
using Warehouse3PL.Application.Features.Inbound.Suppliers.Queries.GetSuppliers;

namespace Warehouse3PL.WebApi.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/suppliers")]
public class SuppliersController : ControllerBase
{
    private readonly ISender _mediator;

    public SuppliersController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<SupplierDto>>> GetSuppliers([FromQuery] GetSuppliersQuery query)
    {
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SupplierDto>> GetSupplierById(Guid id, [FromQuery] Guid? clientId = null)
    {
        var result = await _mediator.Send(new GetSupplierByIdQuery(id, clientId));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<SupplierDto>> CreateSupplier([FromBody] CreateSupplierCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetSupplierById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SupplierDto>> UpdateSupplier(Guid id, [FromBody] UpdateSupplierCommand command)
    {
        if (command.Id != Guid.Empty && command.Id != id)
        {
            return BadRequest("Route ID does not match body ID.");
        }

        var result = await _mediator.Send(command with { Id = id });
        return Ok(result);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<SupplierDto>> SetSupplierStatus(
        Guid id,
        [FromBody] SetPartnerActiveStatusRequest request,
        [FromQuery] Guid? clientId = null)
    {
        var result = await _mediator.Send(new SetSupplierStatusCommand(id, request.IsActive, clientId ?? request.ClientId, request.ExpectedConfigRevision));
        return Ok(result);
    }

    [HttpPost("{id:guid}/contacts")]
    public async Task<ActionResult<SupplierContactDto>> AddContact(Guid id, [FromBody] AddSupplierContactCommand command)
    {
        var result = await _mediator.Send(command with { SupplierId = id });
        return Created($"/api/v1/suppliers/{id}/contacts/{result.Id}", result);
    }

    [HttpPut("{id:guid}/contacts/{contactId:guid}")]
    public async Task<ActionResult<SupplierContactDto>> UpdateContact(
        Guid id,
        Guid contactId,
        [FromBody] UpdateSupplierContactCommand command)
    {
        var result = await _mediator.Send(command with { SupplierId = id, ContactId = contactId });
        return Ok(result);
    }

    [HttpDelete("{id:guid}/contacts/{contactId:guid}")]
    public async Task<IActionResult> DeleteContact(Guid id, Guid contactId, [FromQuery] Guid? clientId = null)
    {
        await _mediator.Send(new DeleteSupplierContactCommand(id, contactId, clientId));
        return NoContent();
    }
}
