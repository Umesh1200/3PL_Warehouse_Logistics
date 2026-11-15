using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Features.Outbound.Shipping.Commands;
using Warehouse3PL.Application.Features.Outbound.Shipping.Queries;
using Warehouse3PL.Application.Features.Outbound.Tracking.Commands;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Outbound - Shipping & Carrier Integration")]
[Route("api/v1/outbound/shipping")]
public class ShippingController : BaseApiController
{
    /// <summary>
    /// Quotes carrier shipping rates for a packed carton across supported service levels.
    /// </summary>
    [HttpPost("rates")]
    [HasPermission(Permissions.Outbound.Ship)]
    [ProducesResponseType(typeof(IReadOnlyList<CarrierRateQuote>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRates([FromBody] GetPackageRatesApiRequest request, CancellationToken ct)
    {
        var quotes = await Mediator.Send(new GetPackageShippingRatesQuery(
            request.PackageId,
            request.PreferredCarrierCode,
            request.PreferredServiceLevel), ct);

        return Ok(quotes);
    }

    /// <summary>
    /// Generates a carrier shipping label (ZPL / PDF) and tracking number for a packed carton.
    /// Decoupled execution: carrier API calls execute outside database transactions.
    /// </summary>
    [HttpPost("labels/generate")]
    [HasPermission(Permissions.Outbound.Ship)]
    [ProducesResponseType(typeof(GenerateShippingLabelResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GenerateLabel([FromBody] GenerateShippingLabelApiRequest request, CancellationToken ct)
    {
        var result = await Mediator.Send(new GenerateShippingLabelCommand(
            request.PackageId,
            request.CarrierCode,
            request.ServiceLevel,
            request.LabelFormat ?? "ZPL",
            request.IdempotencyKey,
            request.SimulateFailure), ct);

        return Ok(result);
    }

    /// <summary>
    /// Voids an active carrier shipping label and reverts package status to Packed.
    /// </summary>
    [HttpPost("labels/{labelId:guid}/void")]
    [HasPermission(Permissions.Outbound.Ship)]
    [ProducesResponseType(typeof(VoidShippingLabelResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> VoidLabel([FromRoute] Guid labelId, [FromBody] VoidShippingLabelApiRequest request, CancellationToken ct)
    {
        var result = await Mediator.Send(new VoidShippingLabelCommand(
            labelId,
            request.Reason,
            request.IdempotencyKey,
            request.SimulateFailure), ct);

        return Ok(result);
    }

    /// <summary>
    /// Retrieves a generated shipping label payload (ZPL / PDF).
    /// </summary>
    [HttpGet("labels/{labelId:guid}")]
    [HasPermission(Permissions.Outbound.Ship)]
    [ProducesResponseType(typeof(ShippingLabelDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLabel([FromRoute] Guid labelId, CancellationToken ct)
    {
        var label = await Mediator.Send(new GetShippingLabelByIdQuery(labelId), ct);
        if (label == null) return NotFound();
        return Ok(label);
    }

    /// <summary>
    /// Retrieves carrier milestone tracking events for an outbound package.
    /// </summary>
    [HttpGet("packages/{packageId:guid}/track")]
    [HasPermission(Permissions.Outbound.Ship)]
    [ProducesResponseType(typeof(CarrierTrackingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TrackPackage([FromRoute] Guid packageId, CancellationToken ct)
    {
        var tracking = await Mediator.Send(new TrackPackageShipmentQuery(packageId), ct);
        return Ok(tracking);
    }

    /// <summary>
    /// Ingests carrier webhook tracking events for Return-To-Sender (RTS) shipments.
    /// Operates idempotently and monotonically. Automatically creates inbound receiving handoff upon carrier dock delivery.
    /// </summary>
    [HttpPost("returns/tracking/webhook")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IngestReturnTrackingResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> IngestReturnTrackingWebhook(
        [FromBody] IngestReturnTrackingRequest request,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new IngestReturnTrackingEventCommand(request), ct);
        return Ok(result);
    }
}

public record GetPackageRatesApiRequest(
    Guid PackageId,
    string? PreferredCarrierCode,
    CarrierServiceLevel? PreferredServiceLevel);

public record GenerateShippingLabelApiRequest(
    Guid PackageId,
    string CarrierCode,
    CarrierServiceLevel ServiceLevel,
    string? LabelFormat,
    string? IdempotencyKey,
    bool SimulateFailure);

public record VoidShippingLabelApiRequest(
    string Reason,
    string? IdempotencyKey,
    bool SimulateFailure);

