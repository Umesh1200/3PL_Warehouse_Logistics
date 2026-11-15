using System.Security.Cryptography;
using System.Text;
using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Append-only audit tracking event for return-to-sender (RTS) shipments.
/// DB-SPEC-3PL-2026 Phase 4.5 Step 3B: Delivery Exception Workflows &amp; Returns Management.
/// </summary>
public class ReturnTrackingEvent : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid ReturnShipmentId { get; set; }

    public string TrackingNumber { get; set; } = string.Empty;
    public string CarrierCode { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;
    public ReturnShipmentStatus Status { get; set; }

    public string? CarrierStatusCode { get; set; }
    public string? CarrierStatusDescription { get; set; }
    public string? Location { get; set; }

    public DateTime OccurredAtUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;

    public string EventFingerprint { get; set; } = string.Empty;

    // Navigation
    public ReturnShipment ReturnShipment { get; set; } = null!;

    public ReturnTrackingEvent()
    {
        Id = Guid.NewGuid();
    }

    /// <summary>
    /// Computes deterministic SHA-256 fingerprint for return tracking event deduplication.
    /// </summary>
    public static string ComputeFingerprint(Guid clientId, string carrierCode, string trackingNumber, string eventType, DateTime occurredAtUtc)
    {
        var raw = $"{clientId:D}:{carrierCode.Trim().ToUpperInvariant()}:{trackingNumber.Trim().ToUpperInvariant()}:{eventType.Trim().ToUpperInvariant()}:{occurredAtUtc.ToUniversalTime():O}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

