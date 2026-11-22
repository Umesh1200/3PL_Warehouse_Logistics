using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
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
/// per-carrier DB-backed replay protection, secret rotation, and audit logging.
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

        httpContext.Request.EnableBuffering();
        byte[] rawBodyBytes;
        using (var ms = new MemoryStream())
        {
            await httpContext.Request.Body.CopyToAsync(ms, ct);
            rawBodyBytes = ms.ToArray();
            httpContext.Request.Body.Position = 0;
        }

        var (payloadCarrier, payloadTracking) = TryExtractMetadata(rawBodyBytes);

        var rawCarrier = httpContext.Request.Headers[CarrierIdHeader].FirstOrDefault()?.Trim();
        var rawTimestamp = httpContext.Request.Headers[TimestampHeader].FirstOrDefault()?.Trim();
        var nonce = httpContext.Request.Headers[NonceHeader].FirstOrDefault()?.Trim();
        var rawSignature = httpContext.Request.Headers[SignatureHeader].FirstOrDefault()?.Trim();

        if (string.IsNullOrWhiteSpace(rawCarrier) ||
            string.IsNullOrWhiteSpace(rawTimestamp) ||
            string.IsNullOrWhiteSpace(nonce) ||
            string.IsNullOrWhiteSpace(rawSignature))
        {
            await FailAsync(rawCarrier?.ToUpperInvariant() ?? payloadCarrier, payloadTracking, nonce,
                "MissingHeader", "Required carrier webhook authentication headers are missing.", httpContext, ct);
        }

        var normalizedCarrier = rawCarrier!.ToUpperInvariant();

        if (nonce!.Length > 150)
        {
            await FailAsync(normalizedCarrier, payloadTracking, nonce[..150],
                "InvalidNonce", "X-Webhook-Nonce exceeds maximum allowed length.", httpContext, ct);
        }

        if (!TryParseTimestamp(rawTimestamp!, out var webhookTimestampUtc))
        {
            await FailAsync(normalizedCarrier, payloadTracking, nonce,
                "InvalidTimestamp", "X-Webhook-Timestamp must be Unix epoch seconds or ISO-8601 UTC.", httpContext, ct);
        }

        var tolerance = options.TimestampToleranceSeconds > 0 ? options.TimestampToleranceSeconds : 300;
        if (Math.Abs((utcNow - webhookTimestampUtc).TotalSeconds) > tolerance)
        {
            await FailAsync(normalizedCarrier, payloadTracking, nonce,
                "ExpiredTimestamp", "Carrier webhook timestamp is outside the permitted tolerance window.", httpContext, ct);
        }

        if (!options.Carriers.TryGetValue(normalizedCarrier, out var carrierConfig) &&
            !options.Carriers.TryGetValue(rawCarrier, out carrierConfig))
        {
            await FailAsync(normalizedCarrier, payloadTracking, nonce,
                "UnknownCarrier", $"Carrier '{normalizedCarrier}' is not configured for webhook authentication.", httpContext, ct);
        }

        if (!TryDecodeHexSignature(rawSignature!, out var providedSignatureBytes))
        {
            await FailAsync(normalizedCarrier, payloadTracking, nonce,
                "MalformedSignature", "X-Webhook-Signature is not a valid SHA-256 hex digest.", httpContext, ct);
        }

        var prefixBytes = Encoding.UTF8.GetBytes($"{rawTimestamp}.{nonce}.");
        var canonicalBytes = new byte[prefixBytes.Length + rawBodyBytes.Length];
        Buffer.BlockCopy(prefixBytes, 0, canonicalBytes, 0, prefixBytes.Length);
        Buffer.BlockCopy(rawBodyBytes, 0, canonicalBytes, prefixBytes.Length, rawBodyBytes.Length);

        var signatureValid = VerifySignatureWithRotation(carrierConfig!, canonicalBytes, providedSignatureBytes, utcNow);
        if (!signatureValid)
        {
            await FailAsync(normalizedCarrier, payloadTracking, nonce,
                "InvalidSignature", "Carrier webhook HMAC-SHA256 signature verification failed.", httpContext, ct);
        }

        if (!string.IsNullOrWhiteSpace(payloadCarrier) &&
            !string.Equals(payloadCarrier.Trim(), normalizedCarrier, StringComparison.OrdinalIgnoreCase))
        {
            await FailAsync(normalizedCarrier, payloadTracking, nonce,
                "CarrierMismatch", $"Payload carrierCode '{payloadCarrier}' does not match X-Carrier-Id '{normalizedCarrier}'.", httpContext, ct);
        }

        // Database-enforced per-carrier replay protection
        var alreadySeen = await _db.CarrierWebhookNonces
            .AsNoTracking()
            .AnyAsync(x => x.CarrierCode == normalizedCarrier && x.Nonce == nonce, ct);

        if (alreadySeen)
        {
            await FailAsync(normalizedCarrier, payloadTracking, nonce,
                "ReplayedNonce", $"Carrier webhook nonce '{nonce}' has already been processed.", httpContext, ct);
        }

        var payloadSha256 = Convert.ToHexStringLower(SHA256.HashData(rawBodyBytes));
        _db.CarrierWebhookNonces.Add(new CarrierWebhookNonce
        {
            CarrierCode = normalizedCarrier,
            Nonce = nonce,
            PayloadSha256 = payloadSha256,
            WebhookTimestampUtc = webhookTimestampUtc,
            ReceivedAtUtc = utcNow
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsNonceUniqueViolation(ex))
        {
            _db.ClearChangeTracker();
            await FailAsync(normalizedCarrier, payloadTracking, nonce,
                "ReplayedNonce", $"Concurrent replay detected for carrier webhook nonce '{nonce}'.", httpContext, ct);
        }

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
        if (executed.Exception == null || executed.ExceptionHandled)
        {
            await RecordAuditAsync(normalizedCarrier, payloadTracking, nonce,
                "Success", StatusCodes.Status200OK, null, httpContext, ct);
        }
        else
        {
            var (outcome, statusCode) = executed.Exception switch
            {
                ForbiddenException => ("CrossTenantOrScopeRejected", StatusCodes.Status403Forbidden),
                UnauthorizedException => ("Unauthorized", StatusCodes.Status401Unauthorized),
                EntityNotFoundException => ("NotFound", StatusCodes.Status404NotFound),
                ValidationException => ("ValidationFailed", StatusCodes.Status400BadRequest),
                DomainException => ("DomainRejected", StatusCodes.Status422UnprocessableEntity),
                _ => ("Error", StatusCodes.Status500InternalServerError)
            };

            _db.ClearChangeTracker();
            await RecordAuditAsync(normalizedCarrier, payloadTracking, nonce,
                outcome, statusCode, executed.Exception.Message, httpContext, ct);
        }
    }

    private async Task FailAsync(
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
        if (long.TryParse(raw, out var unixSeconds))
        {
            timestampUtc = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
            return true;
        }

        if (DateTimeOffset.TryParse(raw, out var dto))
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
}
