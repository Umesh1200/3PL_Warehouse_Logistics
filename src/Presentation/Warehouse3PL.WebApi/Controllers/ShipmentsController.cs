using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Outbound.Packing.DTOs;
using Warehouse3PL.Application.Features.Outbound.Shipping.Commands;
using Warehouse3PL.Application.Features.Outbound.Shipping.DTOs;
using Warehouse3PL.Application.Features.Outbound.Shipping.Queries;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Outbound - Shipment Consolidation & Dock Dispatch")]
[Route("api/v1/outbound/shipments")]
public class ShipmentsController : BaseApiController
{
    /// <summary>
    /// Searches and paginates outbound shipments with facility, status, carrier, and date filters.
    /// </summary>
    [HttpGet]
    [HasPermission(Permissions.Outbound.Ship)]
    [ProducesResponseType(typeof(PagedResult<ShipmentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetShipments([FromQuery] GetShipmentsQuery query, CancellationToken ct)
    {
        var result = await Mediator.Send(query, ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves paginated Packed or LabelPrinted packages not yet consolidated into any shipment.
    /// </summary>
    [HttpGet("eligible-packages")]
    [HasPermission(Permissions.Outbound.Ship)]
    [ProducesResponseType(typeof(PagedResult<PackageSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEligiblePackages([FromQuery] GetEligibleShipmentPackagesQuery query, CancellationToken ct)
    {
        var result = await Mediator.Send(query, ct);
        return Ok(result);
    }

    /// <summary>
    /// Creates a new outbound master freight shipment for consolidation.
    /// </summary>
    [HttpPost]
    [HasPermission(Permissions.Outbound.Ship)]
    [ProducesResponseType(typeof(CreateShipmentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateShipment([FromBody] CreateShipmentApiRequest request, CancellationToken ct)
    {
        var result = await Mediator.Send(new CreateShipmentCommand(
            request.ClientId,
            request.FacilityId,
            request.CarrierCode,
            request.ServiceLevel,
            request.StagingLocationId), ct);

        return Ok(result);
    }

    /// <summary>
    /// Retrieves shipment details including consolidated packages and manifest metadata.
    /// </summary>
    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Outbound.Ship)]
    [ProducesResponseType(typeof(ShipmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetShipment([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await Mediator.Send(new GetShipmentByIdQuery(id), ct);
        return Ok(result);
    }

    /// <summary>
    /// Consolidates a verified, labeled package into an active freight shipment.
    /// Enforces tenant, facility, carrier compatibility, and prevents duplicate active membership.
    /// Zero secondary inventory reservation or stock movement.
    /// </summary>
    [HttpPost("{id:guid}/packages")]
    [HasPermission(Permissions.Outbound.Ship)]
    [ProducesResponseType(typeof(AddPackageToShipmentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddPackage([FromRoute] Guid id, [FromBody] AddPackageToShipmentApiRequest request, CancellationToken ct)
    {
        var result = await Mediator.Send(new AddPackageToShipmentCommand(id, request.PackageId), ct);
        return Ok(result);
    }

    /// <summary>
    /// Removes a package from a shipment prior to vehicle loading with mandatory audit reason.
    /// </summary>
    [HttpDelete("{id:guid}/packages/{packageId:guid}")]
    [HasPermission(Permissions.Outbound.Ship)]
    [ProducesResponseType(typeof(RemovePackageFromShipmentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemovePackage(
        [FromRoute] Guid id,
        [FromRoute] Guid packageId,
        [FromBody] RemovePackageFromShipmentApiRequest request,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new RemovePackageFromShipmentCommand(id, packageId, request.Reason), ct);
        return Ok(result);
    }

    /// <summary>
    /// Confirms scanning and staging of a package at a dock staging lane or dock door.
    /// </summary>
    [HttpPost("packages/{packageId:guid}/stage")]
    [HasPermission(Permissions.Outbound.Ship)]
    [ProducesResponseType(typeof(StagePackageResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StagePackage(
        [FromRoute] Guid packageId,
        [FromBody] StagePackageApiRequest request,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new StagePackageCommand(packageId, request.StagingLocationId), ct);
        return Ok(result);
    }

    /// <summary>
    /// Confirms trailer loading scan of a staged package. Reject un-staged packages. Idempotent duplicate scan.
    /// </summary>
    [HttpPost("packages/{packageId:guid}/load")]
    [HasPermission(Permissions.Outbound.Ship)]
    [ProducesResponseType(typeof(LoadPackageResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> LoadPackage(
        [FromRoute] Guid packageId,
        [FromBody] LoadPackageApiRequest request,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new LoadPackageCommand(
            packageId,
            request.ShipmentId,
            request.TrailerNumber,
            request.DockLocationId), ct);

        return Ok(result);
    }

    /// <summary>
    /// Generates an immutable Bill of Lading (BOL) document snapshot for a consolidated shipment.
    /// Subsequent calls return the existing issued snapshot.
    /// </summary>
    [HttpPost("{id:guid}/bol")]
    [HasPermission(Permissions.Outbound.Ship)]
    [ProducesResponseType(typeof(GenerateBolResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GenerateBillOfLading(
        [FromRoute] Guid id,
        [FromBody] GenerateBolApiRequest request,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new GenerateBillOfLadingCommand(
            id,
            request.TrailerNumber,
            request.SealNumber,
            request.SpecialInstructions), ct);

        return Ok(result);
    }

    /// <summary>
    /// Retrieves an issued Bill of Lading snapshot by its unique identifier.
    /// </summary>
    [HttpGet("bol/{id:guid}")]
    [HasPermission(Permissions.Outbound.Ship)]
    [ProducesResponseType(typeof(BillOfLadingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBillOfLading([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await Mediator.Send(new GetBillOfLadingByIdQuery(id), ct);
        return Ok(result);
    }

    /// <summary>
    /// Confirms driver handover and custody transfer, recording driver signature, trailer, and seal.
    /// Verifies all packages are loaded, transitions shipment to Dispatched, and updates completed orders.
    /// Zero inventory quantity mutation (stock balances unchanged). Idempotent on repeated calls.
    /// </summary>
    [HttpPost("{id:guid}/dispatch")]
    [HasPermission(Permissions.Outbound.Ship)]
    [ProducesResponseType(typeof(ConfirmDispatchCustodyResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ConfirmDispatch(
        [FromRoute] Guid id,
        [FromBody] ConfirmDispatchCustodyApiRequest request,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new ConfirmDispatchCustodyCommand(
            id,
            request.DriverName,
            request.DriverLicenseNumber,
            request.DriverSignature,
            request.TrailerNumber,
            request.SealNumber,
            request.Notes,
            request.IdempotencyKey), ct);

        return Ok(result);
    }
}

public record CreateShipmentApiRequest(
    Guid ClientId,
    Guid FacilityId,
    string CarrierCode,
    CarrierServiceLevel ServiceLevel = CarrierServiceLevel.StandardGround,
    Guid? StagingLocationId = null);

public record AddPackageToShipmentApiRequest(Guid PackageId);

public record RemovePackageFromShipmentApiRequest(string Reason);

public record StagePackageApiRequest(Guid StagingLocationId);

public record LoadPackageApiRequest(
    Guid? ShipmentId = null,
    string? TrailerNumber = null,
    Guid? DockLocationId = null);

public record GenerateBolApiRequest(
    string? TrailerNumber = null,
    string? SealNumber = null,
    string? SpecialInstructions = null);

public record ConfirmDispatchCustodyApiRequest(
    string DriverName,
    string? DriverLicenseNumber,
    string DriverSignature,
    string? TrailerNumber = null,
    string? SealNumber = null,
    string? Notes = null,
    string? IdempotencyKey = null);

