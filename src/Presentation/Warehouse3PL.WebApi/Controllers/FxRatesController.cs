using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Reference.Commands;
using Warehouse3PL.Application.Features.Reference.Queries;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Authorize]
[Route("api/v1/fx-rates")]
[Tags("Effective-Dated Foreign Exchange Rates")]
public sealed class FxRatesController : BaseApiController
{
    private readonly IFxConversionService _fxConversionService;

    public FxRatesController(IFxConversionService fxConversionService)
    {
        _fxConversionService = fxConversionService;
    }

    public sealed record CreateFxRateDraftRequest(
        string BaseCurrencyCode,
        string QuoteCurrencyCode,
        DateTime EffectiveFromUtc,
        DateTime? EffectiveToUtc,
        int UnitMultiplier,
        decimal BuyRate,
        decimal SellRate,
        decimal MidRate,
        FxRateSourceAuthority SourceAuthority,
        string BulletinVersion,
        DateTime PublishedAtUtc,
        string? BenchmarkSeriesId = null,
        string? IntegrityHash = null,
        string? SourceArtifactPayload = null);

    public sealed record ApproveFxRateRequest(
        bool EvidenceVerified,
        string VerificationNotes,
        bool SupersedeOverlapping = false,
        Guid? ExpectedActiveRateId = null,
        long? ExpectedConfigRevision = null,
        string? SourceArtifactPayload = null);

    public sealed record VoidFxRateRequest(
        string VoidReason,
        long? ExpectedConfigRevision = null);

    public sealed record ConvertFxRequest(
        decimal Amount,
        string FromCurrencyCode,
        string ToCurrencyCode,
        DateTime EffectiveDateUtc,
        FxRateType RateType = FxRateType.Mid,
        DateTime? AsOfUtc = null,
        FxRateSourceAuthority? PreferredAuthority = null,
        FxValuationMode ValuationMode = FxValuationMode.HistoricalAsKnown);

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<FxRateRecordDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<FxRateRecordDto>>> GetFxRates(
        [FromQuery] string? baseCurrencyCode = null,
        [FromQuery] string? quoteCurrencyCode = null,
        [FromQuery] FxRateSourceAuthority? sourceAuthority = null,
        [FromQuery] FxRateApprovalStatus? approvalStatus = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] DateTime? effectiveAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(
            new GetFxRatesQuery(baseCurrencyCode, quoteCurrencyCode, sourceAuthority, approvalStatus, isActive, effectiveAtUtc),
            cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(FxRateRecordDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FxRateRecordDto>> GetFxRateById(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetFxRateByIdQuery(id), cancellationToken);
        if (result == null)
        {
            return NotFound(new { message = $"FxRateRecord '{id}' not found." });
        }

        return Ok(result);
    }

    [HttpPost("convert")]
    [ProducesResponseType(typeof(FxConversionResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<FxConversionResultDto>> ConvertAmount(
        [FromBody] ConvertFxRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _fxConversionService.ConvertAmountAsync(
            request.Amount,
            request.FromCurrencyCode,
            request.ToCurrencyCode,
            request.EffectiveDateUtc,
            request.RateType,
            request.AsOfUtc,
            request.PreferredAuthority,
            request.ValuationMode,
            cancellationToken);

        return Ok(result);
    }

    [HttpPost]
    [HasAnyPermission(Permissions.Global.ManageFxRates, Permissions.Billing.ManageRates)]
    [ProducesResponseType(typeof(FxRateRecordDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<FxRateRecordDto>> CreateDraft(
        [FromBody] CreateFxRateDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        var created = await Mediator.Send(
            new CreateFxRateDraftCommand(
                request.BaseCurrencyCode,
                request.QuoteCurrencyCode,
                request.EffectiveFromUtc,
                request.EffectiveToUtc,
                request.UnitMultiplier,
                request.BuyRate,
                request.SellRate,
                request.MidRate,
                request.SourceAuthority,
                request.BulletinVersion,
                request.PublishedAtUtc,
                request.BenchmarkSeriesId,
                request.IntegrityHash,
                request.SourceArtifactPayload),
            cancellationToken);

        return CreatedAtAction(nameof(GetFxRateById), new { id = created.Id }, created);
    }

    [HttpPost("{id:guid}/approve")]
    [HasAnyPermission(Permissions.Global.ManageFxRates, Permissions.Billing.ManageRates)]
    [ProducesResponseType(typeof(FxRateRecordDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<FxRateRecordDto>> Approve(
        Guid id,
        [FromBody] ApproveFxRateRequest request,
        CancellationToken cancellationToken = default)
    {
        var approved = await Mediator.Send(
            new ApproveFxRateCommand(
                id,
                request.EvidenceVerified,
                request.VerificationNotes,
                request.SupersedeOverlapping,
                request.ExpectedActiveRateId,
                request.ExpectedConfigRevision,
                request.SourceArtifactPayload),
            cancellationToken);

        return Ok(approved);
    }

    [HttpPost("{id:guid}/void")]
    [HasAnyPermission(Permissions.Global.ManageFxRates, Permissions.Billing.ManageRates)]
    [ProducesResponseType(typeof(FxRateRecordDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<FxRateRecordDto>> Void(
        Guid id,
        [FromBody] VoidFxRateRequest request,
        CancellationToken cancellationToken = default)
    {
        var voided = await Mediator.Send(
            new VoidFxRateCommand(id, request.VoidReason, request.ExpectedConfigRevision),
            cancellationToken);

        return Ok(voided);
    }

    [HttpPut("{id:guid}")]
    [HasAnyPermission(Permissions.Global.ManageFxRates, Permissions.Billing.ManageRates)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RejectRetroactiveUpdate(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await Mediator.Send(new RejectRetroactiveFxRateMutationCommand(id, "PUT"), cancellationToken);
        return Conflict();
    }

    [HttpDelete("{id:guid}")]
    [HasAnyPermission(Permissions.Global.ManageFxRates, Permissions.Billing.ManageRates)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RejectRetroactiveDelete(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await Mediator.Send(new RejectRetroactiveFxRateMutationCommand(id, "DELETE"), cancellationToken);
        return Conflict();
    }
}
