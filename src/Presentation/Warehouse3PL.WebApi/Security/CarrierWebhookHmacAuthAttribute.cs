using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Configuration;

namespace Warehouse3PL.WebApi.Security;

/// <summary>
/// Enforces carrier-specific HMAC-SHA256 webhook authentication over the canonical message
/// "{timestamp}.{nonce}.{rawBodyBytes}", constant-time verification, timestamp skew tolerance,
/// bounded request body limits, durable per-carrier nonce state tracking with safe retry recovery,
/// secret rotation, and accurate downstream status auditing.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class CarrierWebhookHmacAuthAttribute : TypeFilterAttribute
{
    public CarrierWebhookHmacAuthAttribute() : base(typeof(CarrierWebhookAuthenticationFilter))
    {
    }
}

public sealed class CarrierWebhookAuthenticationFilter : IAsyncResourceFilter
{
    public const string CarrierIdHeader = "X-Carrier-Id";
    public const string TimestampHeader = "X-Webhook-Timestamp";
    public const string NonceHeader = "X-Webhook-Nonce";
    public const string SignatureHeader = "X-Webhook-Signature";

    private const long MinUnixSeconds = -62_135_596_800L;
    private const long MaxUnixSeconds = 253_402_300_799L;

    // Coordinates non-relational EF InMemory unit test concurrency; relational PostgreSQL relies on row locks + unique index 23505.
    private static readonly SemaphoreSlim NonRelationalClaimGate = new(1, 1);

    private readonly IApplicationDbContext _db;
    private readonly IOptionsMonitor<CarrierWebhookOptions> _optionsMonitor;
    private readonly IDateTimeProvider _clock;
    private readonly ITenantProvider _tenantProvider;

    public CarrierWebhookAuthenticationFilter(
        IApplicationDbContext db,
        IOptionsMonitor<CarrierWebhookOptions> optionsMonitor,
        IDateTimeProvider clock,
        ITenantProvider tenantProvider)
    {
        _db = db;
        _optionsMonitor = optionsMonitor;
        _clock = clock;
        _tenantProvider = tenantProvider;
    }

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var httpContext = context.HttpContext;
        var ct = httpContext.RequestAborted;
        var options = _optionsMonitor.CurrentValue;
        var utcNow = _clock.UtcNow;

        var maxBytes = options.MaxPayloadBytes > 0 ? options.MaxPayloadBytes : 262_144;
        var bodySizeFeature = httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySizeFeature != null && !bodySizeFeature.IsReadOnly)
        {
            bodySizeFeature.MaxRequestBodySize = maxBytes;
        }

        var rawCarrierHeader = httpContext.Request.Headers[CarrierIdHeader].FirstOrDefault()?.Trim();
        var rawTimestamp = httpContext.Request.Headers[TimestampHeader].FirstOrDefault()?.Trim();
        var nonce = httpContext.Request.Headers[NonceHeader].FirstOrDefault()?.Trim();
        var rawSignature = httpContext.Request.Headers[SignatureHeader].FirstOrDefault()?.Trim();

        if (httpContext.Request.ContentLength.HasValue && httpContext.Request.ContentLength.Value > maxBytes)
        {
            await RejectWithStatusAsync(
                context,
                rawCarrierHeader?.ToUpperInvariant(),
                null,
                Truncate(nonce, 150),
                "PayloadTooLarge",
                StatusCodes.Status413PayloadTooLarge,
                "PAYLOAD_TOO_LARGE",
                $"Carrier webhook payload exceeds the maximum permitted limit of {maxBytes} bytes.",
                ct);
            return;
        }

        byte[] rawBodyBytes;
        try
        {
            var (readOk, bytes) = await TryReadBoundedBodyAsync(httpContext.Request.Body, maxBytes, ct);
            if (!readOk)
            {
                await RejectWithStatusAsync(
                    context,
                    rawCarrierHeader?.ToUpperInvariant(),
                    null,
                    Truncate(nonce, 150),
                    "PayloadTooLarge",
                    StatusCodes.Status413PayloadTooLarge,
                    "PAYLOAD_TOO_LARGE",
                    $"Carrier webhook payload exceeds the maximum permitted limit of {maxBytes} bytes.",
                    ct);
                return;
            }
            rawBodyBytes = bytes;
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            await RejectWithStatusAsync(
                context,
                rawCarrierHeader?.ToUpperInvariant(),
                null,
                Truncate(nonce, 150),
                "PayloadTooLarge",
                StatusCodes.Status413PayloadTooLarge,
                "PAYLOAD_TOO_LARGE",
                $"Carrier webhook payload exceeds the maximum permitted limit of {maxBytes} bytes.",
                ct);
            return;
        }

        // Replace request stream with the bounded buffer for model binding (avoids unbounded temp buffering).
        httpContext.Request.Body = new MemoryStream(rawBodyBytes, writable: false);

        var (payloadCarrier, payloadTracking) = TryExtractMetadata(rawBodyBytes);

        if (string.IsNullOrWhiteSpace(rawCarrierHeader) ||
            string.IsNullOrWhiteSpace(rawTimestamp) ||
            string.IsNullOrWhiteSpace(nonce) ||
            string.IsNullOrWhiteSpace(rawSignature))
        {
            await FailUnauthorizedAsync(rawCarrierHeader?.ToUpperInvariant() ?? payloadCarrier, payloadTracking, nonce,
                "MissingHeader", "Required carrier webhook authentication headers are missing.", httpContext, ct);
        }

        var normalizedCarrier = rawCarrierHeader!.ToUpperInvariant();

        if (nonce!.Length > 150)
        {
            await FailUnauthorizedAsync(normalizedCarrier, payloadTracking, nonce[..150],
                "InvalidNonce", "X-Webhook-Nonce exceeds maximum allowed length.", httpContext, ct);
        }

        if (!TryParseTimestamp(rawTimestamp!, out var webhookTimestampUtc))
        {
            await FailUnauthorizedAsync(normalizedCarrier, payloadTracking, nonce,
                "InvalidTimestamp", "X-Webhook-Timestamp must be a valid Unix epoch seconds or ISO-8601 UTC timestamp.", httpContext, ct);
        }

        var toleranceSeconds = Math.Clamp(options.TimestampToleranceSeconds > 0 ? options.TimestampToleranceSeconds : 300, 1, 86_400);
        if (webhookTimestampUtc < utcNow.AddSeconds(-toleranceSeconds) || webhookTimestampUtc > utcNow.AddSeconds(toleranceSeconds))
        {
            await FailUnauthorizedAsync(normalizedCarrier, payloadTracking, nonce,
                "ExpiredTimestamp", "Carrier webhook timestamp is outside the permitted tolerance window.", httpContext, ct);
        }

        if (!options.Carriers.TryGetValue(normalizedCarrier, out var carrierConfig) &&
            !options.Carriers.TryGetValue(rawCarrierHeader, out carrierConfig))
        {
            await FailUnauthorizedAsync(normalizedCarrier, payloadTracking, nonce,
                "UnknownCarrier", $"Carrier '{normalizedCarrier}' is not configured for webhook authentication.", httpContext, ct);
        }

        if (!TryDecodeHexSignature(rawSignature!, out var providedSignatureBytes))
        {
            await FailUnauthorizedAsync(normalizedCarrier, payloadTracking, nonce,
                "MalformedSignature", "X-Webhook-Signature is not a valid SHA-256 hex digest.", httpContext, ct);
        }

        var prefixBytes = Encoding.UTF8.GetBytes($"{rawTimestamp}.{nonce}.");
        var canonicalBytes = new byte[prefixBytes.Length + rawBodyBytes.Length];
        Buffer.BlockCopy(prefixBytes, 0, canonicalBytes, 0, prefixBytes.Length);
        Buffer.BlockCopy(rawBodyBytes, 0, canonicalBytes, prefixBytes.Length, rawBodyBytes.Length);

        var signatureValid = VerifySignatureWithRotation(carrierConfig!, canonicalBytes, providedSignatureBytes, utcNow);
        if (!signatureValid)
        {
            await FailUnauthorizedAsync(normalizedCarrier, payloadTracking, nonce,
                "InvalidSignature", "Carrier webhook HMAC-SHA256 signature verification failed.", httpContext, ct);
        }

        if (!string.IsNullOrWhiteSpace(payloadCarrier) &&
            !string.Equals(payloadCarrier.Trim(), normalizedCarrier, StringComparison.OrdinalIgnoreCase))
        {
            await FailUnauthorizedAsync(normalizedCarrier, payloadTracking, nonce,
                "CarrierMismatch", $"Payload carrierCode '{payloadCarrier}' does not match X-Carrier-Id '{normalizedCarrier}'.", httpContext, ct);
        }

        var payloadSha256 = Convert.ToHexStringLower(SHA256.HashData(rawBodyBytes));
        var leaseSeconds = Math.Clamp(options.ProcessingLeaseSeconds > 0 ? options.ProcessingLeaseSeconds : 60, 5, 600);

        var claim = await AcquireOrReclaimNonceAsync(
            normalizedCarrier,
            nonce,
            payloadSha256,
            webhookTimestampUtc,
            utcNow,
            leaseSeconds,
            ct);

        if (claim.Outcome == ClaimOutcome.ReplayRejected)
        {
            await FailUnauthorizedAsync(normalizedCarrier, payloadTracking, nonce,
                "ReplayRejected", claim.Reason!, httpContext, ct);
        }

        if (claim.Outcome == ClaimOutcome.ConcurrentInFlight)
        {
            await RejectWithStatusAsync(
                context,
                normalizedCarrier,
                payloadTracking,
                nonce,
                "ConcurrentDeliveryRejected",
                StatusCodes.Status409Conflict,
                "WEBHOOK_IN_FLIGHT",
                claim.Reason!,
                ct);
            return;
        }

        await RecordAuditAsync(
            normalizedCarrier,
            payloadTracking,
            nonce,
            "Received",
            0,
            null,
            httpContext,
            ct);

        var previousBypass = _tenantProvider.BypassTenantFilter;
        _tenantProvider.BypassTenantFilter = true;
        ResourceExecutedContext executed;
        try
        {
            executed = await next();
        }
        finally
        {
            _tenantProvider.BypassTenantFilter = previousBypass;
        }

        await FinalizeNonceAndAuditAsync(
            claim.NonceId!.Value,
            claim.ClaimToken!.Value,
            normalizedCarrier,
            payloadTracking,
            nonce,
            executed,
            httpContext);
    }

    private async Task<(ClaimOutcome Outcome, Guid? NonceId, Guid? ClaimToken, string? Reason)> AcquireOrReclaimNonceAsync(
        string normalizedCarrier,
        string nonce,
        string payloadSha256,
        DateTime webhookTimestampUtc,
        DateTime utcNow,
        int leaseSeconds,
        CancellationToken ct)
    {
        var isRelational = _db is DbContext dbCtx && dbCtx.Database.IsRelational();
        if (!isRelational)
        {
            await NonRelationalClaimGate.WaitAsync(ct);
        }

        try
        {
            var leaseUntil = utcNow.AddSeconds(leaseSeconds);
            var claimToken = Guid.NewGuid();
            var existing = await _db.CarrierWebhookNonces
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.CarrierCode == normalizedCarrier && x.Nonce == nonce, ct);

            if (existing == null)
            {
                var created = new CarrierWebhookNonce
                {
                    CarrierCode = normalizedCarrier,
                    Nonce = nonce,
                    PayloadSha256 = payloadSha256,
                    Status = CarrierWebhookNonceStatus.Processing,
                    ClaimToken = claimToken,
                    AttemptCount = 1,
                    WebhookTimestampUtc = webhookTimestampUtc,
                    ReceivedAtUtc = utcNow,
                    LastAttemptAtUtc = utcNow,
                    ProcessingLeaseExpiresAtUtc = leaseUntil
                };

                _db.CarrierWebhookNonces.Add(created);
                try
                {
                    await _db.SaveChangesAsync(ct);
                    return (ClaimOutcome.Acquired, created.Id, claimToken, null);
                }
                catch (DbUpdateException ex) when (IsNonceUniqueViolation(ex))
                {
                    _db.ClearChangeTracker();
                    existing = await _db.CarrierWebhookNonces
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x => x.CarrierCode == normalizedCarrier && x.Nonce == nonce, ct);

                    if (existing == null)
                    {
                        return (ClaimOutcome.ConcurrentInFlight, null, null,
                            $"Concurrent claim conflict for carrier webhook nonce '{nonce}'.");
                    }
                }
            }

            if (!string.Equals(existing.PayloadSha256, payloadSha256, StringComparison.OrdinalIgnoreCase))
            {
                return (ClaimOutcome.ReplayRejected, null, null,
                    $"Carrier webhook nonce '{nonce}' was previously bound to a different payload.");
            }

            if (existing.Status == CarrierWebhookNonceStatus.Succeeded)
            {
                return (ClaimOutcome.ReplayRejected, null, null,
                    $"Carrier webhook nonce '{nonce}' has already been processed successfully.");
            }

            if (existing.Status == CarrierWebhookNonceStatus.Processing &&
                existing.ProcessingLeaseExpiresAtUtc.HasValue &&
                existing.ProcessingLeaseExpiresAtUtc.Value > utcNow)
            {
                return (ClaimOutcome.ConcurrentInFlight, null, null,
                    $"Carrier webhook nonce '{nonce}' is currently being processed by another worker.");
            }

            // Failed attempt or expired Processing lease -> safe retry reclaim fenced with a new ClaimToken + AttemptCount check
            if (isRelational && _db is DbContext relCtx)
            {
                var updated = await relCtx.Set<CarrierWebhookNonce>()
                    .Where(x => x.Id == existing.Id &&
                                x.AttemptCount == existing.AttemptCount &&
                                x.Status == existing.Status &&
                                (x.Status == CarrierWebhookNonceStatus.Failed ||
                                 (x.Status == CarrierWebhookNonceStatus.Processing &&
                                  (!x.ProcessingLeaseExpiresAtUtc.HasValue || x.ProcessingLeaseExpiresAtUtc.Value <= utcNow))))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.Status, CarrierWebhookNonceStatus.Processing)
                        .SetProperty(x => x.ClaimToken, claimToken)
                        .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1)
                        .SetProperty(x => x.LastAttemptAtUtc, utcNow)
                        .SetProperty(x => x.ProcessingLeaseExpiresAtUtc, leaseUntil)
                        .SetProperty(x => x.WebhookTimestampUtc, webhookTimestampUtc),
                        ct);

                if (updated == 0)
                {
                    var latest = await _db.CarrierWebhookNonces
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x => x.Id == existing.Id, ct);

                    if (latest?.Status == CarrierWebhookNonceStatus.Succeeded)
                    {
                        return (ClaimOutcome.ReplayRejected, null, null,
                            $"Carrier webhook nonce '{nonce}' has already been processed successfully.");
                    }

                    return (ClaimOutcome.ConcurrentInFlight, null, null,
                        $"Carrier webhook nonce '{nonce}' was claimed concurrently by another attempt.");
                }

                return (ClaimOutcome.Acquired, existing.Id, claimToken, null);
            }

            var tracked = await _db.CarrierWebhookNonces.FirstAsync(x => x.Id == existing.Id, ct);
            if (tracked.AttemptCount != existing.AttemptCount ||
                tracked.Status == CarrierWebhookNonceStatus.Succeeded ||
                (tracked.Status == CarrierWebhookNonceStatus.Processing &&
                 tracked.ProcessingLeaseExpiresAtUtc.HasValue &&
                 tracked.ProcessingLeaseExpiresAtUtc.Value > utcNow))
            {
                return (tracked.Status == CarrierWebhookNonceStatus.Succeeded
                        ? ClaimOutcome.ReplayRejected
                        : ClaimOutcome.ConcurrentInFlight,
                    null,
                    null,
                    $"Carrier webhook nonce '{nonce}' state changed concurrently before reclamation.");
            }

            tracked.Status = CarrierWebhookNonceStatus.Processing;
            tracked.ClaimToken = claimToken;
            tracked.AttemptCount += 1;
            tracked.LastAttemptAtUtc = utcNow;
            tracked.ProcessingLeaseExpiresAtUtc = leaseUntil;
            tracked.WebhookTimestampUtc = webhookTimestampUtc;
            await _db.SaveChangesAsync(ct);

            return (ClaimOutcome.Acquired, tracked.Id, claimToken, null);
        }
        finally
        {
            if (!isRelational)
            {
                NonRelationalClaimGate.Release();
            }
        }
    }

    private async Task FinalizeNonceAndAuditAsync(
        Guid nonceId,
        Guid claimToken,
        string normalizedCarrier,
        string? trackingNumber,
        string nonce,
        ResourceExecutedContext executed,
        HttpContext httpContext)
    {
        var completedAt = _clock.UtcNow;

        if (executed.Exception != null && !executed.ExceptionHandled)
        {
            var statusCode = executed.Exception switch
            {
                ForbiddenException => StatusCodes.Status403Forbidden,
                UnauthorizedException => StatusCodes.Status401Unauthorized,
                EntityNotFoundException => StatusCodes.Status404NotFound,
                ValidationException => StatusCodes.Status400BadRequest,
                DomainException => StatusCodes.Status422UnprocessableEntity,
                DbUpdateConcurrencyException => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status500InternalServerError
            };

            var reason = executed.Exception is ForbiddenException
                ? $"CrossTenantOrScopeRejected: {executed.Exception.Message}"
                : executed.Exception.Message;

            _db.ClearChangeTracker();
            var updated = await UpdateNonceOutcomeAsync(
                nonceId,
                claimToken,
                CarrierWebhookNonceStatus.Failed,
                statusCode,
                Truncate(reason, 500),
                processedAtUtc: null);

            await RecordAuditAsync(
                normalizedCarrier,
                trackingNumber,
                nonce,
                updated ? "Failed" : "StaleAttemptIgnored",
                statusCode,
                updated
                    ? reason
                    : $"Stale attempt ignored after lease reclamation (claim '{claimToken}'). Failure: {reason}",
                httpContext,
                CancellationToken.None);
            return;
        }

        var actualStatusCode = (executed.Result as IStatusCodeActionResult)?.StatusCode
            ?? (httpContext.Response.StatusCode != 0 ? httpContext.Response.StatusCode : StatusCodes.Status200OK);

        if (actualStatusCode is >= 200 and < 300)
        {
            var updated = await UpdateNonceOutcomeAsync(
                nonceId,
                claimToken,
                CarrierWebhookNonceStatus.Succeeded,
                actualStatusCode,
                failureReason: null,
                processedAtUtc: completedAt);

            await RecordAuditAsync(
                normalizedCarrier,
                trackingNumber,
                nonce,
                updated ? "Processed" : "StaleAttemptIgnored",
                actualStatusCode,
                updated
                    ? null
                    : $"Stale attempt completed with HTTP {actualStatusCode} after lease reclamation (claim '{claimToken}'); newer nonce outcome preserved.",
                httpContext,
                CancellationToken.None);
        }
        else
        {
            var failureReason = $"Downstream endpoint returned non-success HTTP {actualStatusCode}.";
            var updated = await UpdateNonceOutcomeAsync(
                nonceId,
                claimToken,
                CarrierWebhookNonceStatus.Failed,
                actualStatusCode,
                failureReason,
                processedAtUtc: null);

            await RecordAuditAsync(
                normalizedCarrier,
                trackingNumber,
                nonce,
                updated ? "Failed" : "StaleAttemptIgnored",
                actualStatusCode,
                updated
                    ? failureReason
                    : $"Stale attempt returned HTTP {actualStatusCode} after lease reclamation (claim '{claimToken}'); newer nonce outcome preserved.",
                httpContext,
                CancellationToken.None);
        }
    }

    private async Task<bool> UpdateNonceOutcomeAsync(
        Guid nonceId,
        Guid claimToken,
        CarrierWebhookNonceStatus status,
        int httpStatusCode,
        string? failureReason,
        DateTime? processedAtUtc)
    {
        if (_db is DbContext dbCtx && dbCtx.Database.IsRelational())
        {
            var affected = await dbCtx.Set<CarrierWebhookNonce>()
                .Where(x => x.Id == nonceId &&
                            x.Status == CarrierWebhookNonceStatus.Processing &&
                            x.ClaimToken == claimToken)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, status)
                    .SetProperty(x => x.ClaimToken, (Guid?)null)
                    .SetProperty(x => x.LastHttpStatusCode, httpStatusCode)
                    .SetProperty(x => x.LastFailureReason, failureReason)
                    .SetProperty(x => x.ProcessedAtUtc, processedAtUtc)
                    .SetProperty(x => x.ProcessingLeaseExpiresAtUtc, (DateTime?)null),
                    CancellationToken.None);
            return affected == 1;
        }

        await NonRelationalClaimGate.WaitAsync(CancellationToken.None);
        try
        {
            _db.ClearChangeTracker();
            var entity = await _db.CarrierWebhookNonces.FirstOrDefaultAsync(x => x.Id == nonceId, CancellationToken.None);
            if (entity == null ||
                entity.Status != CarrierWebhookNonceStatus.Processing ||
                entity.ClaimToken != claimToken)
            {
                return false;
            }

            entity.Status = status;
            entity.ClaimToken = null;
            entity.LastHttpStatusCode = httpStatusCode;
            entity.LastFailureReason = failureReason;
            entity.ProcessedAtUtc = processedAtUtc;
            entity.ProcessingLeaseExpiresAtUtc = null;
            await _db.SaveChangesAsync(CancellationToken.None);
            return true;
        }
        finally
        {
            NonRelationalClaimGate.Release();
        }
    }

    private async Task FailUnauthorizedAsync(
        string? carrierCode,
        string? trackingNumber,
        string? nonce,
        string outcome,
        string reason,
        HttpContext httpContext,
        CancellationToken ct)
    {
        await RecordAuditAsync(carrierCode, trackingNumber, nonce,
            outcome, StatusCodes.Status401Unauthorized, reason, httpContext, ct);

        throw new UnauthorizedException(reason);
    }

    private async Task RejectWithStatusAsync(
        ResourceExecutingContext context,
        string? carrierCode,
        string? trackingNumber,
        string? nonce,
        string outcome,
        int statusCode,
        string errorCode,
        string message,
        CancellationToken ct)
    {
        await RecordAuditAsync(carrierCode, trackingNumber, nonce,
            outcome, statusCode, message, context.HttpContext, ct);

        context.Result = new JsonResult(new
        {
            status = statusCode,
            code = errorCode,
            message
        })
        {
            StatusCode = statusCode
        };
    }

    private async Task RecordAuditAsync(
        string? carrierCode,
        string? trackingNumber,
        string? nonce,
        string outcome,
        int httpStatusCode,
        string? failureReason,
        HttpContext httpContext,
        CancellationToken ct)
    {
        _db.CarrierWebhookAuditLogs.Add(new CarrierWebhookAuditLog
        {
            CarrierCode = Truncate(carrierCode, 50),
            TrackingNumber = Truncate(trackingNumber, 100),
            Nonce = Truncate(nonce, 150),
            Outcome = outcome,
            HttpStatusCode = httpStatusCode,
            FailureReason = Truncate(failureReason, 500),
            SourceIp = Truncate(httpContext.Connection.RemoteIpAddress?.ToString(), 64),
            TraceId = Truncate(httpContext.TraceIdentifier, 128),
            RecordedAtUtc = _clock.UtcNow
        });

        await _db.SaveChangesAsync(ct);
    }

    private static async Task<(bool Ok, byte[] Bytes)> TryReadBoundedBodyAsync(
        Stream bodyStream,
        int maxBytes,
        CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[16_384];
        long totalRead = 0;

        while (true)
        {
            var read = await bodyStream.ReadAsync(buffer, ct);
            if (read == 0)
            {
                return (true, ms.ToArray());
            }

            totalRead += read;
            if (totalRead > maxBytes)
            {
                return (false, []);
            }

            ms.Write(buffer, 0, read);
        }
    }

    private static bool VerifySignatureWithRotation(
        CarrierWebhookSecretConfig config,
        byte[] canonicalBytes,
        byte[] providedSignatureBytes,
        DateTime utcNow)
    {
        if (!string.IsNullOrWhiteSpace(config.PrimarySecret) &&
            VerifySingleSecret(config.PrimarySecret, canonicalBytes, providedSignatureBytes))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(config.SecondarySecret) &&
            (!config.SecondarySecretExpiresAtUtc.HasValue || config.SecondarySecretExpiresAtUtc.Value.UtcDateTime > utcNow) &&
            VerifySingleSecret(config.SecondarySecret, canonicalBytes, providedSignatureBytes))
        {
            return true;
        }

        return false;
    }

    private static bool VerifySingleSecret(string secret, byte[] canonicalBytes, byte[] providedSignatureBytes)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computed = hmac.ComputeHash(canonicalBytes);
        return CryptographicOperations.FixedTimeEquals(computed, providedSignatureBytes);
    }

    private static bool TryParseTimestamp(string raw, out DateTime timestampUtc)
    {
        if (long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var unixSeconds))
        {
            if (unixSeconds < MinUnixSeconds || unixSeconds > MaxUnixSeconds)
            {
                timestampUtc = default;
                return false;
            }

            try
            {
                timestampUtc = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                timestampUtc = default;
                return false;
            }
        }

        if (DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dto))
        {
            timestampUtc = dto.UtcDateTime;
            return true;
        }

        timestampUtc = default;
        return false;
    }

    private static bool TryDecodeHexSignature(string raw, out byte[] digestBytes)
    {
        var hex = raw.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)
            ? raw["sha256=".Length..]
            : raw;

        if (hex.Length != 64)
        {
            digestBytes = [];
            return false;
        }

        try
        {
            digestBytes = Convert.FromHexString(hex);
            return digestBytes.Length == 32;
        }
        catch (FormatException)
        {
            digestBytes = [];
            return false;
        }
    }

    private static (string? CarrierCode, string? TrackingNumber) TryExtractMetadata(byte[] rawBodyBytes)
    {
        if (rawBodyBytes.Length == 0) return (null, null);

        try
        {
            using var doc = JsonDocument.Parse(rawBodyBytes);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, null);

            string? carrier = null;
            string? tracking = null;
            foreach (var prop in root.EnumerateObject())
            {
                if (string.Equals(prop.Name, "carrierCode", StringComparison.OrdinalIgnoreCase) &&
                    prop.Value.ValueKind == JsonValueKind.String)
                {
                    carrier = prop.Value.GetString();
                }
                else if (string.Equals(prop.Name, "trackingNumber", StringComparison.OrdinalIgnoreCase) &&
                         prop.Value.ValueKind == JsonValueKind.String)
                {
                    tracking = prop.Value.GetString();
                }
            }

            return (carrier, tracking);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static bool IsNonceUniqueViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException pgEx &&
               pgEx.SqlState == PostgresErrorCodes.UniqueViolation &&
               pgEx.ConstraintName != null &&
               pgEx.ConstraintName.Contains("carrier_webhook_nonces", StringComparison.OrdinalIgnoreCase);
    }

    private static string? Truncate(string? value, int maxLen)
        => value != null && value.Length > maxLen ? value[..maxLen] : value;

    private enum ClaimOutcome
    {
        Acquired,
        ReplayRejected,
        ConcurrentInFlight
    }
}
