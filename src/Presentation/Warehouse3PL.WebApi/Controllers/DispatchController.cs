using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Outbound.Dispatch.Commands;
using Warehouse3PL.Application.Features.Outbound.Dispatch.DTOs;
using Warehouse3PL.Application.Features.Outbound.Dispatch.Queries;
using Warehouse3PL.Application.Features.Outbound.Tracking.Commands;
using Warehouse3PL.Application.Features.Outbound.Tracking.Queries;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Authorization;
using Warehouse3PL.WebApi.Security;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Outbound - Dispatch, Tracking, POD & Courier")]
[Route("api/v1/outbound/dispatch")]
public class DispatchController : BaseApiController
{
    private const long MaxEvidenceFileBytes = 5 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    // =========================================================================
    // 1. Read-Only Dispatch, Timeline, Order Status, POD & Exceptions
    // =========================================================================

    [HttpGet("board")]
    [HasPermission(Permissions.Dispatch.View)]
    [ProducesResponseType(typeof(PagedResult<DispatchBoardShipmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetDispatchBoard([FromQuery] GetDispatchBoardQuery query, CancellationToken ct)
    {
        var result = await Mediator.Send(query, ct);
        return Ok(result);
    }

    [HttpGet("packages/{packageId:guid}/timeline")]
    [HasPermission(Permissions.Dispatch.View)]
    [ProducesResponseType(typeof(PackageTrackingTimelineDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPackageTimeline([FromRoute] Guid packageId, CancellationToken ct)
    {
        var result = await Mediator.Send(new GetPackageTrackingTimelineQuery(packageId), ct);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpGet("orders/{orderId:guid}/delivery-status")]
    [HasPermission(Permissions.Dispatch.View)]
    [ProducesResponseType(typeof(EvaluateOrderDeliveryRollupResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrderDeliveryStatus([FromRoute] Guid orderId, CancellationToken ct)
    {
        var result = await Mediator.Send(new GetOrderDeliveryStatusQuery(orderId), ct);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpGet("packages/{packageId:guid}/pod")]
    [HasPermission(Permissions.Dispatch.View)]
    [ProducesResponseType(typeof(ProofOfDeliveryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPackagePod([FromRoute] Guid packageId, CancellationToken ct)
    {
        var result = await Mediator.Send(new GetProofOfDeliveryQuery(PackageId: packageId), ct);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpGet("exceptions")]
    [HasPermission(Permissions.Dispatch.View)]
    [ProducesResponseType(typeof(PagedResult<DeliveryExceptionQueueItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetExceptions([FromQuery] GetDeliveryExceptionsQuery query, CancellationToken ct)
    {
        var result = await Mediator.Send(query, ct);
        return Ok(result);
    }

    // =========================================================================
    // 1B. Courier-Scoped Active Custody Runs (UserType.Driver ExecuteDelivery)
    // =========================================================================

    [HttpGet("courier/shipments")]
    [HasPermission(Permissions.Dispatch.ExecuteDelivery)]
    [ProducesResponseType(typeof(IReadOnlyList<DispatchBoardShipmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetCourierAssignedShipments(CancellationToken ct)
    {
        var result = await Mediator.Send(new GetCourierAssignedShipmentsQuery(), ct);
        return Ok(result);
    }

    // =========================================================================
    // 2. Supervisor Dispatch & Exception Management
    // =========================================================================

    [HttpPost("shipments/{id:guid}/assign-driver")]
    [HasPermission(Permissions.Dispatch.ManageRuns)]
    [ProducesResponseType(typeof(AssignShipmentDriverResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AssignDriver(
        [FromRoute] Guid id,
        [FromBody] AssignShipmentDriverApiRequest request,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new AssignShipmentDriverCommand(
            id,
            request.DriverUserId,
            request.ExpectedVersion,
            request.Reason), ct);

        return Ok(result);
    }

    [HttpPost("shipments/{id:guid}/acknowledge-custody")]
    [HasPermission(Permissions.Dispatch.ExecuteDelivery)]
    [ProducesResponseType(typeof(AcknowledgeShipmentCustodyResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AcknowledgeCustody(
        [FromRoute] Guid id,
        [FromBody] AcknowledgeShipmentCustodyApiRequest request,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new AcknowledgeShipmentCustodyCommand(
            id,
            request.DriverSignature,
            request.DriverLicenseNumber,
            request.TrailerNumber,
            request.SealNumber,
            request.ExpectedVersion,
            request.Notes), ct);

        return Ok(result);
    }

    [HttpPost("shipments/{shipmentId:guid}/sync-tracking")]
    [HasPermission(Permissions.Dispatch.ManageRuns)]
    [ProducesResponseType(typeof(SyncShipmentTrackingResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SyncShipmentTracking([FromRoute] Guid shipmentId, CancellationToken ct)
    {
        var result = await Mediator.Send(new SyncShipmentTrackingCommand(shipmentId), ct);
        return Ok(result);
    }

    [HttpPost("pod/{podId:guid}/supersede")]
    [HasPermission(Permissions.Dispatch.ManageRuns)]
    [RequestSizeLimit(10_485_760)]
    [ProducesResponseType(typeof(SubmitProofOfDeliveryResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SupersedePod(
        [FromRoute] Guid podId,
        [FromBody] SupersedeProofOfDeliveryApiRequest request,
        CancellationToken ct)
    {
        await using var streams = new EvidenceStreamScope();
        var replacement = FromJsonPodRequest(Guid.Empty, request.ReplacementSubmission, streams);

        var result = await Mediator.Send(new SupersedeProofOfDeliveryCommand(new SupersedeProofOfDeliveryRequest
        {
            ExistingPodId = podId,
            Reason = request.Reason,
            ReplacementSubmission = replacement
        }), ct);

        return Ok(result);
    }

    [HttpPost("exceptions/{id:guid}/contact")]
    [HasPermission(Permissions.Dispatch.ManageRuns)]
    [ProducesResponseType(typeof(DeliveryExceptionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RecordCustomerContact(
        [FromRoute] Guid id,
        [FromBody] RecordCustomerContactApiRequest request,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new RecordCustomerContactCommand(new RecordCustomerContactRequest
        {
            ExceptionId = id,
            ContactMethod = request.ContactMethod,
            Outcome = request.Outcome,
            Notes = request.Notes,
            ResolutionAction = request.ResolutionAction
        }), ct);
        return Ok(result);
    }

    [HttpPost("exceptions/{id:guid}/reattempt")]
    [HasPermission(Permissions.Dispatch.ManageRuns)]
    [ProducesResponseType(typeof(DeliveryExceptionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ScheduleReattempt(
        [FromRoute] Guid id,
        [FromBody] ScheduleDeliveryReattemptApiRequest request,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new ScheduleDeliveryReattemptCommand(new ScheduleDeliveryReattemptRequest
        {
            ExceptionId = id,
            ScheduledDateUtc = request.ScheduledDateUtc,
            Notes = request.Notes
        }), ct);
        return Ok(result);
    }

    [HttpPost("exceptions/{id:guid}/reschedule")]
    [HasPermission(Permissions.Dispatch.ManageRuns)]
    [ProducesResponseType(typeof(DeliveryExceptionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RescheduleReattempt(
        [FromRoute] Guid id,
        [FromBody] RescheduleDeliveryReattemptApiRequest request,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new RescheduleDeliveryReattemptCommand(new RescheduleDeliveryReattemptRequest
        {
            ExceptionId = id,
            NewScheduledDateUtc = request.NewScheduledDateUtc,
            Reason = request.Reason
        }), ct);
        return Ok(result);
    }

    [HttpPost("exceptions/{id:guid}/cancel-reattempt")]
    [HasPermission(Permissions.Dispatch.ManageRuns)]
    [ProducesResponseType(typeof(DeliveryExceptionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelReattempt(
        [FromRoute] Guid id,
        [FromBody] ExceptionReasonApiRequest request,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new CancelDeliveryReattemptCommand(new CancelDeliveryReattemptRequest
        {
            ExceptionId = id,
            Reason = request.Reason
        }), ct);
        return Ok(result);
    }

    [HttpPost("exceptions/{id:guid}/update-address")]
    [HasPermission(Permissions.Dispatch.ManageRuns)]
    [ProducesResponseType(typeof(DeliveryExceptionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateDeliveryAddress(
        [FromRoute] Guid id,
        [FromBody] UpdateDeliveryAddressApiRequest request,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new UpdateDeliveryAddressCommand(new UpdateDeliveryAddressRequest
        {
            ExceptionId = id,
            CorrectedAddress = request.CorrectedAddress,
            Notes = request.Notes
        }), ct);
        return Ok(result);
    }

    [HttpPost("exceptions/{id:guid}/redirect-depot")]
    [HasPermission(Permissions.Dispatch.ManageRuns)]
    [ProducesResponseType(typeof(DeliveryExceptionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RedirectToDepot(
        [FromRoute] Guid id,
        [FromBody] RedirectToCarrierDepotApiRequest request,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new RedirectToCarrierDepotCommand(new RedirectToCarrierDepotRequest
        {
            ExceptionId = id,
            DepotLocation = request.DepotLocation,
            Notes = request.Notes
        }), ct);
        return Ok(result);
    }

    [HttpPost("exceptions/{id:guid}/resolve")]
    [HasPermission(Permissions.Dispatch.ManageRuns)]
    [ProducesResponseType(typeof(DeliveryExceptionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResolveException(
        [FromRoute] Guid id,
        [FromBody] ResolveDeliveryExceptionApiRequest request,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new ResolveDeliveryExceptionCommand(new ResolveDeliveryExceptionRequest
        {
            ExceptionId = id,
            Resolution = request.Resolution,
            ResolutionNotes = request.ResolutionNotes
        }), ct);
        return Ok(result);
    }

    [HttpPost("exceptions/{id:guid}/reopen")]
    [HasPermission(Permissions.Dispatch.ManageRuns)]
    [ProducesResponseType(typeof(DeliveryExceptionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReopenException(
        [FromRoute] Guid id,
        [FromBody] ExceptionReasonApiRequest request,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new ReopenDeliveryExceptionCommand(new ReopenDeliveryExceptionRequest
        {
            ExceptionId = id,
            Reason = request.Reason
        }), ct);
        return Ok(result);
    }

    [HttpPost("exceptions/{id:guid}/void")]
    [HasPermission(Permissions.Dispatch.ManageRuns)]
    [ProducesResponseType(typeof(DeliveryExceptionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> VoidException(
        [FromRoute] Guid id,
        [FromBody] ExceptionReasonApiRequest request,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new VoidDeliveryExceptionCommand(new VoidDeliveryExceptionRequest
        {
            ExceptionId = id,
            Reason = request.Reason
        }), ct);
        return Ok(result);
    }

    // =========================================================================
    // 3. Courier Execution Endpoints (Driver Custody or Assigned-Facility Staff)
    // =========================================================================

    [HttpPost("packages/{packageId:guid}/pod")]
    [HasPermission(Permissions.Dispatch.ExecuteDelivery)]
    [RequestSizeLimit(10_485_760)]
    [ProducesResponseType(typeof(SubmitProofOfDeliveryResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SubmitProofOfDelivery([FromRoute] Guid packageId, CancellationToken ct)
    {
        await using var streams = new EvidenceStreamScope();
        SubmitProofOfDeliveryRequest podRequest;

        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync(ct);
            podRequest = await FromFormPodRequestAsync(packageId, form, streams, ct);
        }
        else
        {
            var apiRequest = await JsonSerializer.DeserializeAsync<SubmitProofOfDeliveryApiRequest>(Request.Body, JsonOptions, ct)
                ?? throw new ValidationException(nameof(Request.Body), "A valid Proof of Delivery payload is required.");
            podRequest = FromJsonPodRequest(packageId, apiRequest, streams);
        }

        var result = await Mediator.Send(new SubmitProofOfDeliveryCommand(podRequest), ct);
        return Ok(result);
    }

    [HttpPost("packages/{packageId:guid}/confirm-delivery")]
    [HasPermission(Permissions.Dispatch.ExecuteDelivery)]
    [ProducesResponseType(typeof(DeliveryConfirmationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ConfirmDelivery(
        [FromRoute] Guid packageId,
        [FromBody] ConfirmPackageDeliveryApiRequest? request,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new ConfirmPackageDeliveryCommand(new ConfirmPackageDeliveryRequest
        {
            PackageId = packageId,
            ProofOfDeliveryId = request?.ProofOfDeliveryId,
            DeliveredAtUtcOverride = request?.DeliveredAtUtcOverride,
            ConfirmationNotes = request?.ConfirmationNotes
        }), ct);
        return Ok(result);
    }

    [HttpPost("exceptions/driver-report")]
    [HasPermission(Permissions.Dispatch.ExecuteDelivery)]
    [ProducesResponseType(typeof(DeliveryExceptionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReportDriverException(
        [FromBody] ReportDriverExceptionApiRequest request,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new ReportDriverDeliveryExceptionCommand(new ReportDriverExceptionRequest
        {
            PackageId = request.PackageId,
            ExceptionType = request.ExceptionType,
            DriverNotes = request.DriverNotes,
            OccurredAtUtc = request.OccurredAtUtc ?? DateTime.UtcNow,
            Latitude = request.Latitude,
            Longitude = request.Longitude
        }), ct);
        return Ok(result);
    }

    // =========================================================================
    // 4. Signed Carrier Webhooks
    // =========================================================================

    [HttpPost("webhooks/carrier-tracking")]
    [AllowAnonymous]
    [RequestSizeLimit(262_144)]
    [CarrierWebhookHmacAuth]
    [ProducesResponseType(typeof(IngestTrackingEventResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> IngestCarrierTrackingWebhook(
        [FromBody] CarrierTrackingWebhookApiRequest request,
        CancellationToken ct)
    {
        ValidateHeaderCarrierBinding(request.CarrierCode);

        try
        {
            var result = await Mediator.Send(new IngestCarrierTrackingEventCommand(
                request.CarrierCode,
                request.TrackingNumber,
                request.CarrierEventCode,
                request.CarrierDescription,
                request.OccurredAtUtc,
                request.CarrierEventId,
                request.EventCity,
                request.EventStateOrProvince,
                request.EventPostalCode,
                request.EventCountry,
                request.RawPayloadJson), ct);

            return Ok(result);
        }
        catch (UnauthorizedException ex)
        {
            throw new ForbiddenException(ex.Message);
        }
    }

    [HttpPost("webhooks/carrier-exception")]
    [AllowAnonymous]
    [RequestSizeLimit(262_144)]
    [CarrierWebhookHmacAuth]
    [ProducesResponseType(typeof(DeliveryExceptionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> IngestCarrierExceptionWebhook(
        [FromBody] CarrierExceptionWebhookApiRequest request,
        CancellationToken ct)
    {
        ValidateHeaderCarrierBinding(request.CarrierCode);

        try
        {
            var result = await Mediator.Send(new IngestCarrierDeliveryExceptionCommand(new IngestCarrierExceptionRequest
            {
                CarrierCode = request.CarrierCode,
                TrackingNumber = request.TrackingNumber,
                CarrierEventId = request.CarrierEventId,
                CarrierExceptionCode = request.CarrierExceptionCode,
                CarrierDescription = request.CarrierDescription,
                OccurredAtUtc = request.OccurredAtUtc,
                EventCity = request.EventCity,
                EventStateOrProvince = request.EventStateOrProvince,
                RawPayloadJson = request.RawPayloadJson,
                Source = DeliveryExceptionSource.CarrierWebhook
            }), ct);

            return Ok(result);
        }
        catch (UnauthorizedException ex)
        {
            throw new ForbiddenException(ex.Message);
        }
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private void ValidateHeaderCarrierBinding(string payloadCarrierCode)
    {
        var headerCarrierId = Request.Headers[CarrierWebhookAuthenticationFilter.CarrierIdHeader].ToString().Trim();
        if (string.IsNullOrWhiteSpace(headerCarrierId) ||
            !string.Equals(headerCarrierId, payloadCarrierCode?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException(
                $"Authenticated webhook carrier '{headerCarrierId}' does not match payload carrierCode '{payloadCarrierCode}'.");
        }
    }

    private static SubmitProofOfDeliveryRequest FromJsonPodRequest(
        Guid packageId,
        SubmitProofOfDeliveryApiRequest apiRequest,
        EvidenceStreamScope streams)
    {
        ArgumentNullException.ThrowIfNull(apiRequest);

        var sigStream = DecodeBoundedBase64(apiRequest.SignatureBase64, nameof(apiRequest.SignatureBase64), streams);
        var photoStream = DecodeBoundedBase64(apiRequest.PhotoBase64, nameof(apiRequest.PhotoBase64), streams);

        return new SubmitProofOfDeliveryRequest
        {
            PackageId = packageId,
            RecipientName = apiRequest.RecipientName,
            DeliveryLocationDescription = apiRequest.DeliveryLocationDescription,
            IsSigned = apiRequest.IsSigned,
            SignatureStream = sigStream,
            SignatureContentType = apiRequest.SignatureContentType,
            PhotoStream = photoStream,
            PhotoContentType = apiRequest.PhotoContentType,
            Latitude = apiRequest.Latitude,
            Longitude = apiRequest.Longitude,
            CarrierConfirmationNumber = apiRequest.CarrierConfirmationNumber,
            CarrierEventId = apiRequest.CarrierEventId,
            IdempotencyKey = apiRequest.IdempotencyKey,
            EvidenceSource = apiRequest.EvidenceSource,
            DeliveredAtUtc = apiRequest.DeliveredAtUtc ?? DateTime.UtcNow,
            VerificationReason = apiRequest.VerificationReason,
            ConsigneeAcknowledgmentText = apiRequest.ConsigneeAcknowledgmentText
        };
    }

    private static async Task<SubmitProofOfDeliveryRequest> FromFormPodRequestAsync(
        Guid packageId,
        IFormCollection form,
        EvidenceStreamScope streams,
        CancellationToken ct)
    {
        var sigFile = form.Files.GetFile("signature") ?? form.Files.GetFile("signatureFile");
        var photoFile = form.Files.GetFile("photo") ?? form.Files.GetFile("photoFile");

        var sigStream = await CopyBoundedFormFileAsync(sigFile, "signature", streams, ct);
        var photoStream = await CopyBoundedFormFileAsync(photoFile, "photo", streams, ct);

        bool.TryParse(form["isSigned"], out var isSigned);
        decimal? lat = decimal.TryParse(form["latitude"], out var parsedLat) ? parsedLat : null;
        decimal? lng = decimal.TryParse(form["longitude"], out var parsedLng) ? parsedLng : null;
        DateTime deliveredAt = DateTime.TryParse(form["deliveredAtUtc"], out var parsedDeliveredAt)
            ? parsedDeliveredAt.ToUniversalTime()
            : DateTime.UtcNow;

        var source = Enum.TryParse<PodEvidenceSource>(form["evidenceSource"], ignoreCase: true, out var parsedSource)
            ? parsedSource
            : PodEvidenceSource.DriverHandheld;

        return new SubmitProofOfDeliveryRequest
        {
            PackageId = packageId,
            RecipientName = NullIfEmpty(form["recipientName"]),
            DeliveryLocationDescription = NullIfEmpty(form["deliveryLocationDescription"]),
            IsSigned = isSigned || sigStream != null,
            SignatureStream = sigStream,
            SignatureContentType = NullIfEmpty(form["signatureContentType"]) ?? sigFile?.ContentType,
            PhotoStream = photoStream,
            PhotoContentType = NullIfEmpty(form["photoContentType"]) ?? photoFile?.ContentType,
            Latitude = lat,
            Longitude = lng,
            CarrierConfirmationNumber = NullIfEmpty(form["carrierConfirmationNumber"]),
            CarrierEventId = NullIfEmpty(form["carrierEventId"]),
            IdempotencyKey = NullIfEmpty(form["idempotencyKey"]),
            EvidenceSource = source,
            DeliveredAtUtc = deliveredAt,
            VerificationReason = NullIfEmpty(form["verificationReason"]),
            ConsigneeAcknowledgmentText = NullIfEmpty(form["consigneeAcknowledgmentText"])
        };
    }

    private static async Task<MemoryStream?> CopyBoundedFormFileAsync(
        IFormFile? file,
        string fieldName,
        EvidenceStreamScope streams,
        CancellationToken ct)
    {
        if (file == null || file.Length == 0)
        {
            return null;
        }

        if (file.Length > MaxEvidenceFileBytes)
        {
            throw new ValidationException(fieldName, $"Evidence file '{fieldName}' exceeds the maximum allowed size of {MaxEvidenceFileBytes} bytes.");
        }

        var ms = new MemoryStream((int)file.Length);
        streams.Track(ms);
        await file.CopyToAsync(ms, ct);
        ms.Position = 0;
        return ms;
    }

    private static MemoryStream? DecodeBoundedBase64(
        string? base64,
        string fieldName,
        EvidenceStreamScope streams)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return null;
        }

        if (base64.Length > (MaxEvidenceFileBytes * 4 / 3) + 64)
        {
            throw new ValidationException(fieldName, $"Encoded evidence '{fieldName}' exceeds maximum allowed size.");
        }

        try
        {
            var bytes = Convert.FromBase64String(base64);
            if (bytes.Length > MaxEvidenceFileBytes)
            {
                throw new ValidationException(fieldName, $"Decoded evidence '{fieldName}' exceeds maximum allowed size of {MaxEvidenceFileBytes} bytes.");
            }

            var ms = new MemoryStream(bytes, writable: false);
            streams.Track(ms);
            return ms;
        }
        catch (FormatException)
        {
            throw new ValidationException(fieldName, $"'{fieldName}' is not valid base64 data.");
        }
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class EvidenceStreamScope : IAsyncDisposable
    {
        private readonly List<Stream> _streams = [];
        public void Track(Stream stream) => _streams.Add(stream);

        public async ValueTask DisposeAsync()
        {
            foreach (var s in _streams)
            {
                await s.DisposeAsync();
            }
        }
    }
}

public sealed record AssignShipmentDriverApiRequest(
    Guid? DriverUserId,
    uint? ExpectedVersion = null,
    string? Reason = null);

public sealed record AcknowledgeShipmentCustodyApiRequest(
    string DriverSignature,
    string? DriverLicenseNumber = null,
    string? TrailerNumber = null,
    string? SealNumber = null,
    uint? ExpectedVersion = null,
    string? Notes = null);

public sealed record SubmitProofOfDeliveryApiRequest
{
    public string? RecipientName { get; init; }
    public string? DeliveryLocationDescription { get; init; }
    public bool IsSigned { get; init; }
    public string? SignatureBase64 { get; init; }
    public string? SignatureContentType { get; init; }
    public string? PhotoBase64 { get; init; }
    public string? PhotoContentType { get; init; }
    public decimal? Latitude { get; init; }
    public decimal? Longitude { get; init; }
    public string? CarrierConfirmationNumber { get; init; }
    public string? CarrierEventId { get; init; }
    public string? IdempotencyKey { get; init; }
    public PodEvidenceSource EvidenceSource { get; init; } = PodEvidenceSource.DriverHandheld;
    public DateTime? DeliveredAtUtc { get; init; }
    public string? VerificationReason { get; init; }
    public string? ConsigneeAcknowledgmentText { get; init; }
}

public sealed record SupersedeProofOfDeliveryApiRequest(
    string Reason,
    SubmitProofOfDeliveryApiRequest ReplacementSubmission);

public sealed record ConfirmPackageDeliveryApiRequest(
    Guid? ProofOfDeliveryId = null,
    DateTime? DeliveredAtUtcOverride = null,
    string? ConfirmationNotes = null);

public sealed record ReportDriverExceptionApiRequest(
    Guid PackageId,
    DeliveryExceptionType ExceptionType,
    string DriverNotes,
    DateTime? OccurredAtUtc = null,
    decimal? Latitude = null,
    decimal? Longitude = null);

public sealed record RecordCustomerContactApiRequest(
    CustomerContactMethod ContactMethod,
    CustomerContactOutcome Outcome,
    string Notes,
    string? ResolutionAction = null);

public sealed record ScheduleDeliveryReattemptApiRequest(
    DateTime ScheduledDateUtc,
    string Notes);

public sealed record RescheduleDeliveryReattemptApiRequest(
    DateTime NewScheduledDateUtc,
    string Reason);

public sealed record ExceptionReasonApiRequest(string Reason);

public sealed record UpdateDeliveryAddressApiRequest(
    string CorrectedAddress,
    string Notes);

public sealed record RedirectToCarrierDepotApiRequest(
    string DepotLocation,
    string Notes);

public sealed record ResolveDeliveryExceptionApiRequest(
    DeliveryExceptionResolution Resolution,
    string ResolutionNotes);

public sealed record CarrierTrackingWebhookApiRequest(
    string CarrierCode,
    string TrackingNumber,
    string CarrierEventCode,
    string CarrierDescription,
    DateTime OccurredAtUtc,
    string? CarrierEventId = null,
    string? EventCity = null,
    string? EventStateOrProvince = null,
    string? EventPostalCode = null,
    string? EventCountry = null,
    string? RawPayloadJson = null);

public sealed record CarrierExceptionWebhookApiRequest(
    string CarrierCode,
    string TrackingNumber,
    string CarrierExceptionCode,
    string CarrierDescription,
    DateTime OccurredAtUtc,
    string? CarrierEventId = null,
    string? EventCity = null,
    string? EventStateOrProvince = null,
    string? RawPayloadJson = null);
