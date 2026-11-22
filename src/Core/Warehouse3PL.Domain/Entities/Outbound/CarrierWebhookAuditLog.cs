using Warehouse3PL.Domain.Common;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Auditable log recording every carrier webhook authentication attempt,
/// failure reason, replay rejection, and successful ingestion outcome.
/// </summary>
public class CarrierWebhookAuditLog : BaseEntity<Guid>
{
    public string? CarrierCode { get; set; }
    public string? TrackingNumber { get; set; }
    public string? Nonce { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public int HttpStatusCode { get; set; }
    public string? FailureReason { get; set; }
    public string? SourceIp { get; set; }
    public string? TraceId { get; set; }
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;

    public CarrierWebhookAuditLog()
    {
        Id = Guid.NewGuid();
    }
}
