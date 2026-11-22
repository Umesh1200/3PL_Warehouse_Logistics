using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Outbound.Packing.Commands;
using Warehouse3PL.Application.Features.Outbound.Packing.DTOs;
using Warehouse3PL.Application.Features.Outbound.Packing.Queries;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Outbound - Packing & Cartonization")]
[Route("api/v1/outbound/packing")]
public class PackingController : BaseApiController
{
    /// <summary>
    /// Retrieves paginated packing sessions for workbench resumption and supervisor oversight.
    /// </summary>
    [HttpGet("sessions")]
    [HasPermission(Permissions.Outbound.Pack)]
    [ProducesResponseType(typeof(PagedResult<PackingSessionSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSessions([FromQuery] GetPackingSessionsQuery query, CancellationToken ct)
    {
        var result = await Mediator.Send(query, ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves full packing session detail including line progress, packages, and picked totes.
    /// </summary>
    [HttpGet("sessions/{id:guid}")]
    [HasPermission(Permissions.Outbound.Pack)]
    [ProducesResponseType(typeof(PackingSessionDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSessionById([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await Mediator.Send(new GetPackingSessionByIdQuery(id), ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves paginated packages scoped to packing workbench hydration.
    /// </summary>
    [HttpGet("packages")]
    [HasPermission(Permissions.Outbound.Pack)]
    [ProducesResponseType(typeof(PagedResult<PackageSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPackages([FromQuery] GetPackingPackagesQuery query, CancellationToken ct)
    {
        var result = await Mediator.Send(query, ct);
        return Ok(result);
    }

    /// <summary>
    /// Starts or resumes a packing session at a packing station workbench for an outbound order.
    /// Prevents concurrent conflicting packing sessions on the same order.
    /// </summary>
    [HttpPost("sessions/start")]
    [HasPermission(Permissions.Outbound.Pack)]
    [ProducesResponseType(typeof(PackingSessionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StartSession([FromBody] StartPackingSessionApiRequest request, CancellationToken ct)
    {
        var result = await Mediator.Send(new StartPackingSessionCommand(
            request.FacilityId,
            request.PackingStationLocationId,
            request.OutboundOrderId), ct);

        return Ok(result);
    }

    /// <summary>
    /// Generates algorithmic cartonization recommendations evaluating 3D orthogonal bounding-box rotation,
    /// max weight capacities, and multi-carton partitioning.
    /// </summary>
    [HttpPost("cartonization/recommend")]
    [HasPermission(Permissions.Outbound.Pack)]
    [ProducesResponseType(typeof(CartonizationRecommendation), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RecommendCartonization([FromBody] CartonizationApiRequest request, CancellationToken ct)
    {
        var result = await Mediator.Send(new GetCartonizationRecommendationQuery(
            request.ClientId,
            request.FacilityId,
            request.OutboundOrderId,
            request.Items,
            request.PreferredPackagingMaterialId,
            request.VolumeUtilizationFactor > 0 ? request.VolumeUtilizationFactor : 0.85m), ct);

        return Ok(result);
    }

    /// <summary>
    /// Creates a new shipping package carton aggregate paired 1:1 with a physical HandlingUnit.
    /// </summary>
    [HttpPost("packages/create")]
    [HasPermission(Permissions.Outbound.Pack)]
    [ProducesResponseType(typeof(CreatePackageResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreatePackage([FromBody] CreatePackageApiRequest request, CancellationToken ct)
    {
        var result = await Mediator.Send(new CreatePackageCommand(
            request.PackingSessionId,
            request.PackagingMaterialId,
            request.OverrideReason), ct);

        return Ok(result);
    }

    /// <summary>
    /// Confirms scanning of picked items from a source tote into an active package carton.
    /// Executes atomic inventory transfer (Delta Q_tote = -Q, Delta Q_carton = +Q) and records ledger transaction.
    /// </summary>
    [HttpPost("items/pack")]
    [HasPermission(Permissions.Outbound.Pack)]
    [ProducesResponseType(typeof(PackItemResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PackItem([FromBody] PackItemApiRequest request, CancellationToken ct)
    {
        var result = await Mediator.Send(new PackItemCommand(
            request.PackingSessionId,
            request.PackageId,
            request.ScannedToteLpn,
            request.ScannedItemBarcode,
            request.ScannedLotNumber,
            request.Quantity,
            request.IdempotencyKey), ct);

        return Ok(result);
    }

    /// <summary>
    /// Seals and closes a completed package with scale-verified gross weight.
    /// </summary>
    [HttpPost("packages/{packageId:guid}/close")]
    [HasPermission(Permissions.Outbound.Pack)]
    [ProducesResponseType(typeof(ClosePackageResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ClosePackage([FromRoute] Guid packageId, [FromBody] ClosePackageApiRequest request, CancellationToken ct)
    {
        var result = await Mediator.Send(new ClosePackageCommand(packageId, request.ScaleGrossWeightKg), ct);
        return Ok(result);
    }

    /// <summary>
    /// Releases an empty pick tote back to the clean available pool only when completely empty.
    /// </summary>
    [HttpPost("totes/release")]
    [HasPermission(Permissions.Outbound.Pack)]
    [ProducesResponseType(typeof(ReleaseToteResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ReleaseTote([FromBody] ReleaseToteApiRequest request, CancellationToken ct)
    {
        var result = await Mediator.Send(new ReleaseToteCommand(request.ToteLpn, request.FacilityId), ct);
        return Ok(result);
    }

    /// <summary>
    /// Completes the packing session when all order lines are fully packed and all packages are sealed.
    /// </summary>
    [HttpPost("sessions/{sessionId:guid}/complete")]
    [HasPermission(Permissions.Outbound.Pack)]
    [ProducesResponseType(typeof(CompletePackingSessionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CompleteSession([FromRoute] Guid sessionId, CancellationToken ct)
    {
        var result = await Mediator.Send(new CompletePackingSessionCommand(sessionId), ct);
        return Ok(result);
    }
}

public record StartPackingSessionApiRequest(
    Guid FacilityId,
    Guid PackingStationLocationId,
    Guid OutboundOrderId);

public record CartonizationApiRequest(
    Guid ClientId,
    Guid FacilityId,
    Guid? OutboundOrderId,
    List<CartonizationItemRequest>? Items,
    Guid? PreferredPackagingMaterialId,
    decimal VolumeUtilizationFactor);

public record CreatePackageApiRequest(
    Guid PackingSessionId,
    Guid? PackagingMaterialId,
    string? OverrideReason);

public record PackItemApiRequest(
    Guid PackingSessionId,
    Guid PackageId,
    string ScannedToteLpn,
    string ScannedItemBarcode,
    string? ScannedLotNumber,
    decimal Quantity,
    string? IdempotencyKey);

public record ClosePackageApiRequest(
    decimal ScaleGrossWeightKg);

public record ReleaseToteApiRequest(
    string ToteLpn,
    Guid FacilityId);

