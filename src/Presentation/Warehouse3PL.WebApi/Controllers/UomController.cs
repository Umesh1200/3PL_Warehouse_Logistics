using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Reference.Queries;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.WebApi.Controllers;

[Authorize]
[Route("api/v1/uom")]
[Tags("UN/ECE Rec 20 Units of Measure")]
public sealed class UomController : BaseApiController
{
    private readonly IUomConversionService _conversionService;

    public UomController(IUomConversionService conversionService)
    {
        _conversionService = conversionService;
    }

    public sealed record ConvertUomRequest(
        decimal Quantity,
        string FromUomCode,
        string ToUomCode,
        int? Precision = null);

    [HttpGet("definitions")]
    [ProducesResponseType(typeof(IReadOnlyList<UomDefinitionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UomDefinitionDto>>> GetDefinitions(
        [FromQuery] UomDimension? dimension = null,
        [FromQuery] UomSystemOfMeasure? systemOfMeasure = null,
        [FromQuery] bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(
            new GetUomDefinitionsQuery(dimension, systemOfMeasure, activeOnly),
            cancellationToken);
        return Ok(result);
    }

    [HttpGet("conversions")]
    [ProducesResponseType(typeof(IReadOnlyList<UomConversionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UomConversionDto>>> GetConversions(
        [FromQuery] string? fromUomCode = null,
        [FromQuery] string? toUomCode = null,
        [FromQuery] UomDimension? dimension = null,
        [FromQuery] bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(
            new GetUomConversionsQuery(fromUomCode, toUomCode, dimension, activeOnly),
            cancellationToken);
        return Ok(result);
    }

    [HttpPost("convert")]
    [ProducesResponseType(typeof(UomConversionResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<UomConversionResultDto>> Convert(
        [FromBody] ConvertUomRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _conversionService.ConvertAsync(
            request.Quantity,
            request.FromUomCode,
            request.ToUomCode,
            request.Precision,
            cancellationToken);
        return Ok(result);
    }
}
