using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Customers;

namespace Warehouse3PL.WebApi.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/customers")]
public class CustomersController : ControllerBase
{
    private readonly ISender _mediator;

    public CustomersController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<CustomerDto>>> GetCustomers([FromQuery] GetCustomersQuery query)
    {
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CustomerDto>> GetCustomerById(Guid id, [FromQuery] Guid? clientId = null)
    {
        var result = await _mediator.Send(new GetCustomerByIdQuery(id, clientId));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<CustomerDto>> CreateCustomer([FromBody] CreateCustomerCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetCustomerById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CustomerDto>> UpdateCustomer(Guid id, [FromBody] UpdateCustomerCommand command)
    {
        if (command.Id != Guid.Empty && command.Id != id)
        {
            return BadRequest("Route ID does not match body ID.");
        }

        var result = await _mediator.Send(command with { Id = id });
        return Ok(result);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<CustomerDto>> SetCustomerStatus(
        Guid id,
        [FromBody] SetPartnerActiveStatusRequest request,
        [FromQuery] Guid? clientId = null)
    {
        var result = await _mediator.Send(new SetCustomerStatusCommand(id, request.IsActive, clientId ?? request.ClientId, request.ExpectedConfigRevision));
        return Ok(result);
    }

    [HttpPost("{id:guid}/addresses")]
    public async Task<ActionResult<CustomerAddressDto>> AddAddress(Guid id, [FromBody] AddCustomerAddressCommand command)
    {
        if (command.CustomerId != Guid.Empty && command.CustomerId != id)
        {
            return BadRequest("Route customer ID does not match body CustomerId.");
        }

        var result = await _mediator.Send(command with { CustomerId = id });
        return Created($"/api/v1/customers/{id}/addresses/{result.Id}", result);
    }

    [HttpPut("{id:guid}/addresses/{addressId:guid}")]
    public async Task<ActionResult<CustomerAddressDto>> UpdateAddress(
        Guid id,
        Guid addressId,
        [FromBody] UpdateCustomerAddressCommand command)
    {
        var result = await _mediator.Send(command with { CustomerId = id, AddressId = addressId });
        return Ok(result);
    }

    [HttpDelete("{id:guid}/addresses/{addressId:guid}")]
    public async Task<IActionResult> DeleteAddress(Guid id, Guid addressId, [FromQuery] Guid? clientId = null)
    {
        await _mediator.Send(new DeleteCustomerAddressCommand(id, addressId, clientId));
        return NoContent();
    }
}

public record SetPartnerActiveStatusRequest(bool IsActive, Guid? ClientId = null, int? ExpectedConfigRevision = null);
