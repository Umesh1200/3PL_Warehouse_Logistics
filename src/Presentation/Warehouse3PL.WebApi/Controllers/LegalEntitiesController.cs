using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.LegalEntities.Commands;
using Warehouse3PL.Application.Features.LegalEntities.Queries;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Authorize]
[Route("api/v1/legal-entities")]
[Tags("Issuing Legal Entities")]
public sealed class LegalEntitiesController : BaseApiController
{
    public sealed record CreateLegalEntityRequest(
        string LegalCode,
        string LegalName,
        string? RegistrationNumber,
        string CountryCode,
        TaxIdentifierType TaxIdentifierType,
        string TaxRegistrationNumber,
        string RegisteredAddressLine1,
        string? RegisteredAddressLine2,
        string RegisteredCity,
        string? RegisteredStateOrProvince,
        string? RegisteredPostalCode,
        bool IsCbmsMandated,
        string? IroSoftwarePermitRef,
        string DefaultCurrencyCode);

    public sealed record UpdateLegalEntityRequest(
        string? LegalCode,
        string LegalName,
        string? RegistrationNumber,
        string CountryCode,
        TaxIdentifierType TaxIdentifierType,
        string TaxRegistrationNumber,
        string RegisteredAddressLine1,
        string? RegisteredAddressLine2,
        string RegisteredCity,
        string? RegisteredStateOrProvince,
        string? RegisteredPostalCode,
        bool IsCbmsMandated,
        string? IroSoftwarePermitRef,
        string DefaultCurrencyCode,
        long? ExpectedConfigRevision = null);

    public sealed record UpdateLegalEntityStatusRequest(bool IsActive);

    [HttpGet]
    [HasAnyPermission(Permissions.Global.ViewLegalEntities, Permissions.Global.ManageLegalEntities)]
    [ProducesResponseType(typeof(IReadOnlyList<LegalEntityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<LegalEntityDto>>> GetLegalEntities(
        [FromQuery] string? countryCode = null,
        [FromQuery] bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetLegalEntitiesQuery(countryCode, isActive), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [HasAnyPermission(Permissions.Global.ViewLegalEntities, Permissions.Global.ManageLegalEntities)]
    [ProducesResponseType(typeof(LegalEntityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LegalEntityDto>> GetLegalEntityById(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetLegalEntityByIdQuery(id), cancellationToken);
        if (result == null)
        {
            return NotFound(new { message = $"LegalEntity '{id}' not found." });
        }

        return Ok(result);
    }

    [HttpPost]
    [HasPermission(Permissions.Global.ManageLegalEntities)]
    [ProducesResponseType(typeof(LegalEntityDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<LegalEntityDto>> CreateLegalEntity(
        [FromBody] CreateLegalEntityRequest request,
        CancellationToken cancellationToken = default)
    {
        var created = await Mediator.Send(
            new CreateLegalEntityCommand(
                request.LegalCode,
                request.LegalName,
                request.RegistrationNumber,
                request.CountryCode,
                request.TaxIdentifierType,
                request.TaxRegistrationNumber,
                request.RegisteredAddressLine1,
                request.RegisteredAddressLine2,
                request.RegisteredCity,
                request.RegisteredStateOrProvince,
                request.RegisteredPostalCode,
                request.IsCbmsMandated,
                request.IroSoftwarePermitRef,
                request.DefaultCurrencyCode),
            cancellationToken);

        return CreatedAtAction(nameof(GetLegalEntityById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Global.ManageLegalEntities)]
    [ProducesResponseType(typeof(LegalEntityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<LegalEntityDto>> UpdateLegalEntity(
        Guid id,
        [FromBody] UpdateLegalEntityRequest request,
        CancellationToken cancellationToken = default)
    {
        var updated = await Mediator.Send(
            new UpdateLegalEntityCommand(
                id,
                request.LegalCode,
                request.LegalName,
                request.RegistrationNumber,
                request.CountryCode,
                request.TaxIdentifierType,
                request.TaxRegistrationNumber,
                request.RegisteredAddressLine1,
                request.RegisteredAddressLine2,
                request.RegisteredCity,
                request.RegisteredStateOrProvince,
                request.RegisteredPostalCode,
                request.IsCbmsMandated,
                request.IroSoftwarePermitRef,
                request.DefaultCurrencyCode,
                request.ExpectedConfigRevision),
            cancellationToken);

        return Ok(updated);
    }

    [HttpPatch("{id:guid}/status")]
    [HasPermission(Permissions.Global.ManageLegalEntities)]
    [ProducesResponseType(typeof(LegalEntityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LegalEntityDto>> UpdateStatus(
        Guid id,
        [FromBody] UpdateLegalEntityStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var updated = await Mediator.Send(new UpdateLegalEntityStatusCommand(id, request.IsActive), cancellationToken);
        return Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Global.ManageLegalEntities)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteLegalEntity(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await Mediator.Send(new DeleteLegalEntityCommand(id), cancellationToken);
        return NoContent();
    }
}
