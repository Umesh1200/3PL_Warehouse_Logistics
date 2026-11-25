using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Reference.Queries;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.WebApi.Controllers;

[Authorize]
[Route("api/v1/reference")]
[Tags("Global Localization Reference Catalogs")]
public sealed class ReferenceController : BaseApiController
{
    /// <summary>
    /// Lists immutable ISO 3166-1 country profiles.
    /// </summary>
    [HttpGet("countries")]
    [ProducesResponseType(typeof(IReadOnlyList<CountryProfileDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CountryProfileDto>>> GetCountries(
        [FromQuery] bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetCountriesQuery(activeOnly), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves an immutable ISO 3166-1 country profile by alpha-2 country code.
    /// </summary>
    [HttpGet("countries/{countryCode}")]
    [ProducesResponseType(typeof(CountryProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CountryProfileDto>> GetCountryByCode(
        string countryCode,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetCountryByCodeQuery(countryCode), cancellationToken);
        if (result == null)
        {
            return NotFound(new { message = $"Country profile '{countryCode}' not found." });
        }

        return Ok(result);
    }

    /// <summary>
    /// Lists authoritative administrative divisions (states, provinces, districts, palikas) for a country.
    /// </summary>
    [HttpGet("countries/{countryCode}/divisions")]
    [ProducesResponseType(typeof(IReadOnlyList<AdministrativeDivisionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<AdministrativeDivisionDto>>> GetDivisions(
        string countryCode,
        [FromQuery] AdministrativeDivisionLevel? level = null,
        [FromQuery] Guid? parentId = null,
        [FromQuery] bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(
            new GetCountryDivisionsQuery(countryCode, level, parentId, activeOnly),
            cancellationToken);

        if (result == null)
        {
            return NotFound(new { message = $"Country profile '{countryCode}' not found." });
        }

        return Ok(result);
    }

    /// <summary>
    /// Lists registered IANA tzdb timezones with dynamically computed UTC offsets for the requested instant.
    /// </summary>
    [HttpGet("timezones")]
    [ProducesResponseType(typeof(IReadOnlyList<TimeZoneProfileDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TimeZoneProfileDto>>> GetTimeZones(
        [FromQuery] string? countryCode = null,
        [FromQuery] DateTime? atUtc = null,
        [FromQuery] bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(
            new GetTimeZonesQuery(countryCode, atUtc, activeOnly),
            cancellationToken);

        return Ok(result);
    }
}
