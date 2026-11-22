namespace Warehouse3PL.Infrastructure.Configuration;

/// <summary>
/// Configuration binding for carrier webhook HMAC-SHA256 secrets, replay tolerance, and payload guards.
/// Secrets must be supplied at runtime via environment variables, user secrets, or KMS—never committed in source control.
/// </summary>
public sealed class CarrierWebhookOptions
{
    public const string SectionName = "CarrierWebhooks";

    public int TimestampToleranceSeconds { get; set; } = 300;
    public int MaxPayloadBytes { get; set; } = 262_144; // 256 KiB default limit before full buffering
    public int ProcessingLeaseSeconds { get; set; } = 60;

    public Dictionary<string, CarrierWebhookSecretConfig> Carriers { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CarrierWebhookSecretConfig
{
    public string? PrimarySecret { get; set; }
    public string? SecondarySecret { get; set; }
    public DateTimeOffset? SecondarySecretExpiresAtUtc { get; set; }
}
