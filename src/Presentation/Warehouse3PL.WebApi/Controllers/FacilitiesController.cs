using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Facilities.Commands;
using Warehouse3PL.Application.Features.Facilities.Queries.GetAuthorizedFacilities;
using Warehouse3PL.Application.Features.Facilities.Queries.GetFacilityById;
using Warehouse3PL.Application.Features.Facilities.Queries.GetFacilityConfiguration;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Facilities & Physical Topography")]
public class FacilitiesController : BaseApiController
{
    /// <summary>
    /// Retrieve authorized facilities. Filtered to caller's tenant unless staff/admin.
    /// Returns reader-safe general metadata only.
    /// </summary>
    [HttpGet]
    [HasPermission(Permissions.Facilities.View)]
    [ProducesResponseType(typeof(List<FacilityDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<FacilityDto>>> GetFacilities(CancellationToken ct)
    {
        var response = await Mediator.Send(new GetAuthorizedFacilitiesQuery(), ct);
        return Ok(response);
    }

    /// <summary>
    /// Retrieve general metadata for a specific authorized facility by ID. Validates tenant access boundary.
    /// </summary>
    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Facilities.View)]
    [ProducesResponseType(typeof(FacilityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FacilityDto>> GetFacilityById(Guid id, CancellationToken ct)
    {
        var response = await Mediator.Send(new GetFacilityByIdQuery(id), ct);
        if (response == null)
        {
            return NotFound(new { message = $"Facility '{id}' not found or access denied." });
        }
        return Ok(response);
    }

    /// <summary>
    /// Retrieve privileged administrative configuration details for a facility.
    /// Strictly restricted to authorized warehouse staff holding <c>facilities:manage</c> or platform SuperAdmin.
    /// </summary>
    [HttpGet("{id:guid}/configuration")]
    [HasPermission(Permissions.Facilities.Manage)]
    [ProducesResponseType(typeof(FacilityConfigurationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FacilityConfigurationDto>> GetFacilityConfiguration(Guid id, CancellationToken ct)
    {
        var response = await Mediator.Send(new GetFacilityConfigurationQuery(id), ct);
        if (response == null)
        {
            return NotFound(new { message = $"Facility '{id}' not found." });
        }
        return Ok(response);
    }

    public sealed record CreateFacilityRequest(
        string FacilityCode,
        string Name,
        string? AddressLine1,
        string? AddressLine2,
        string? City,
        string? StateOrProvince,
        string? PostalCode,
        string CountryCode,
        string Timezone,
        Guid? DefaultLegalEntityId,
        string? DefaultCurrencyCode,
        string? ContactEmail,
        string? ContactPhone,
        decimal? TotalAreaSqM,
        int DockDoorCount = 4);

    public sealed record UpdateFacilityRequest(
        string? FacilityCode,
        string Name,
        string? AddressLine1,
        string? AddressLine2,
        string? City,
        string? StateOrProvince,
        string? PostalCode,
        string CountryCode,
        string Timezone,
        Guid? DefaultLegalEntityId,
        string? DefaultCurrencyCode,
        string? ContactEmail,
        string? ContactPhone,
        decimal? TotalAreaSqM,
        int DockDoorCount = 4,
        bool IsActive = true);

    /// <summary>
    /// Creates a new physical warehouse facility bound to registered reference catalogs and optional active LegalEntity.
    /// </summary>
    [HttpPost]
    [HasPermission(Permissions.Facilities.Manage)]
    [ProducesResponseType(typeof(FacilityDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<FacilityDto>> CreateFacility(
        [FromBody] CreateFacilityRequest request,
        CancellationToken ct)
    {
        var created = await Mediator.Send(
            new CreateFacilityCommand(
                request.FacilityCode,
                request.Name,
                request.AddressLine1,
                request.AddressLine2,
                request.City,
                request.StateOrProvince,
                request.PostalCode,
                request.CountryCode,
                request.Timezone,
                request.DefaultLegalEntityId,
                request.DefaultCurrencyCode,
                request.ContactEmail,
                request.ContactPhone,
                request.TotalAreaSqM,
                request.DockDoorCount),
            ct);

        return CreatedAtAction(nameof(GetFacilityById), new { id = created.Id }, created);
    }

    /// <summary>
    /// Updates a physical warehouse facility, validating CountryCode, Timezone, Currency, and active DefaultLegalEntityId.
    /// </summary>
    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Facilities.Manage)]
    [ProducesResponseType(typeof(FacilityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<FacilityDto>> UpdateFacility(
        Guid id,
        [FromBody] UpdateFacilityRequest request,
        CancellationToken ct)
    {
        var updated = await Mediator.Send(
            new UpdateFacilityCommand(
                id,
                request.FacilityCode,
                request.Name,
                request.AddressLine1,
                request.AddressLine2,
                request.City,
                request.StateOrProvince,
                request.PostalCode,
                request.CountryCode,
                request.Timezone,
                request.DefaultLegalEntityId,
                request.DefaultCurrencyCode,
                request.ContactEmail,
                request.ContactPhone,
                request.TotalAreaSqM,
                request.DockDoorCount,
                request.IsActive),
            ct);

        return Ok(updated);
    }
}
