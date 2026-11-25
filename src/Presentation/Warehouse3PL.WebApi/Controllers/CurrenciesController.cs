using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Reference.Queries;

namespace Warehouse3PL.WebApi.Controllers;

[Authorize]
[Route("api/v1/currencies")]
[Tags("ISO 4217 Currency Master")]
public sealed class CurrenciesController : BaseApiController
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CurrencyMasterDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CurrencyMasterDto>>> GetCurrencies(
        [FromQuery] bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetCurrenciesQuery(activeOnly), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{currencyCode}")]
    [ProducesResponseType(typeof(CurrencyMasterDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CurrencyMasterDto>> GetCurrencyByCode(
        string currencyCode,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetCurrencyByCodeQuery(currencyCode), cancellationToken);
        if (result == null)
        {
            return NotFound(new { message = $"Currency '{currencyCode}' not found." });
        }

        return Ok(result);
    }
}
