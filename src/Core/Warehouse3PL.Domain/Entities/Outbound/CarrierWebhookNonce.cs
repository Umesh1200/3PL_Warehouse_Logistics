using Warehouse3PL.Domain.Common;

namespace Warehouse3PL.Domain.Entities.Outbound;

public enum CarrierWebhookNonceStatus
{
    Processing = 1,
    Succeeded = 2,
    Failed = 3
}

/// <summary>
/// Persists per-carrier webhook nonces with a database unique index and durable processing
/// state to block replay attacks while permitting safe recovery after transient failures.
/// </summary>
public class CarrierWebhookNonce : BaseEntity<Guid>
{
    public string CarrierCode { get; set; } = string.Empty;
    public string Nonce { get; set; } = string.Empty;
    public string PayloadSha256 { get; set; } = string.Empty;
    public CarrierWebhookNonceStatus Status { get; set; } = CarrierWebhookNonceStatus.Processing;
    public Guid? ClaimToken { get; set; }
    public int AttemptCount { get; set; } = 1;
    public DateTime WebhookTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastAttemptAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessingLeaseExpiresAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public int? LastHttpStatusCode { get; set; }
    public string? LastFailureReason { get; set; }

    public CarrierWebhookNonce()
    {
        Id = Guid.NewGuid();
    }
}
