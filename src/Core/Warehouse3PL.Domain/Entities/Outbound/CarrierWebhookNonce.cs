using Warehouse3PL.Domain.Common;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Persists seen per-carrier webhook nonces with a database unique index
/// to prevent signed webhook replay attacks under concurrency.
/// </summary>
public class CarrierWebhookNonce : BaseEntity<Guid>
{
    public string CarrierCode { get; set; } = string.Empty;
    public string Nonce { get; set; } = string.Empty;
    public string PayloadSha256 { get; set; } = string.Empty;
    public DateTime WebhookTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;

    public CarrierWebhookNonce()
    {
        Id = Guid.NewGuid();
    }
}
