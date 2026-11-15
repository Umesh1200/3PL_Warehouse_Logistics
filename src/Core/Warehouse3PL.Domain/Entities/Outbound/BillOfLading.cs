using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Bill of Lading (BOL) legal freight document and shipping manifest snapshot.
/// Captures an immutable snapshot of shipment metrics, carrier, trailer, and consignee details at dispatch.
/// DB-SPEC-3PL-2026 Phase 4.4 Packing & Dispatch Preparation.
/// </summary>
public class BillOfLading : AuditableEntity<Guid>, ITenantEntity, ISoftDeletable
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid ShipmentId { get; set; }

    public string BolNumber { get; set; } = string.Empty;
    public int RevisionNumber { get; set; } = 1;

    public string CarrierCode { get; set; } = string.Empty;
    public string CarrierName { get; set; } = string.Empty;
    public string ServiceLevel { get; set; } = string.Empty;

    public string? TrailerNumber { get; set; }
    public string? SealNumber { get; set; }

    public int TotalPackageCount { get; set; }
    public decimal TotalGrossWeightKg { get; set; }
    public decimal TotalVolumeCm3 { get; set; }
    public string? FreightClass { get; set; }
    public string? SpecialInstructions { get; set; }

    public string ShipperName { get; set; } = string.Empty;
    public string ShipperAddressSnapshot { get; set; } = string.Empty;

    public string ConsigneeName { get; set; } = string.Empty;
    public string ConsigneeAddressSnapshot { get; set; } = string.Empty;

    public string PackageTrackingNumbers { get; set; } = string.Empty;
    public string DocumentSnapshotJson { get; set; } = string.Empty;

    public Guid PreparedByUserId { get; set; }
    public Guid? AuthorizedByUserId { get; set; }
    public DateTime IssuedAtUtc { get; set; } = DateTime.UtcNow;

    public bool IsVoided { get; set; } = false;
    public string? VoidReason { get; set; }
    public DateTime? VoidedAtUtc { get; set; }

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public Shipment Shipment { get; set; } = null!;

    public BillOfLading()
    {
        Id = Guid.NewGuid();
    }

    public void Void(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException(nameof(reason), "A reason must be provided when voiding a Bill of Lading.");
        }

        if (IsVoided)
        {
            throw new InvalidDomainStateException($"Bill of Lading '{BolNumber}' is already voided.");
        }

        IsVoided = true;
        VoidReason = reason;
        VoidedAtUtc = DateTime.UtcNow;
    }
}

