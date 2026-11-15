using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Secure tokenized public tracking access share for end-customers.
/// Protects warehouse internal PII and storage keys while providing real-time transit visibility.
/// DB-SPEC-3PL-2026 Phase 4.5 Outbound Shipment Tracking & Delivery Confirmation.
/// </summary>
public class PublicTrackingShare : BaseEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid PackageId { get; set; }
    public Guid? ShipmentId { get; set; }

    /// <summary>
    /// Cryptographically secure random tracking access token (URL-safe).
    /// </summary>
    public string PublicToken { get; set; } = string.Empty;

    public string TrackingNumber { get; set; } = string.Empty;
    public string CarrierCode { get; set; } = string.Empty;

    // Masked Destination Snapshot for Public Presentation (Zero PII leakage)
    public string MaskedRecipientName { get; set; } = string.Empty;
    public string DestinationCity { get; set; } = string.Empty;
    public string DestinationState { get; set; } = string.Empty;
    public string MaskedPostalCode { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }

    public int AccessCount { get; set; } = 0;
    public int MaxAccessCount { get; set; } = 500;

    public bool IsActive => !RevokedAtUtc.HasValue && DateTime.UtcNow <= ExpiresAtUtc && AccessCount < MaxAccessCount;

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public Package Package { get; set; } = null!;
    public Shipment? Shipment { get; set; }

    public PublicTrackingShare()
    {
        Id = Guid.NewGuid();
    }

    public void RecordAccess()
    {
        if (!IsActive)
        {
            throw new InvalidDomainStateException("Public tracking share token is expired, revoked, or has exceeded access limit.");
        }

        AccessCount++;
    }

    public void Revoke()
    {
        if (RevokedAtUtc.HasValue)
        {
            throw new InvalidDomainStateException("Public tracking share token is already revoked.");
        }

        RevokedAtUtc = DateTime.UtcNow;
    }
}

