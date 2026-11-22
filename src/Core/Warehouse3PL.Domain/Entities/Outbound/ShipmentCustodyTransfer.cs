using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Immutable custody transfer record confirming formal legal handover from warehouse facility to carrier driver.
/// DB-SPEC-3PL-2026 Phase 4.4 Packing & Dispatch Preparation.
/// </summary>
public class ShipmentCustodyTransfer : BaseEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid ShipmentId { get; set; }

    public string CarrierCode { get; set; } = string.Empty;
    public string DriverName { get; set; } = string.Empty;
    public string? DriverLicenseNumber { get; set; }
    public string DriverSignatureAcknowledgment { get; set; } = string.Empty;

    public string? TrailerNumber { get; set; }
    public string? SealNumber { get; set; }
    public string? BillOfLadingNumber { get; set; }

    public int TotalPackagesConfirmed { get; set; }
    public decimal TotalWeightKg { get; set; }

    public Guid AuthorizedByUserId { get; set; }
    public Guid? AssignedDriverUserId { get; set; }
    public ShipmentCustodyStatus CustodyStatus { get; set; } = ShipmentCustodyStatus.Confirmed;
    public DateTime TransferredAtUtc { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public Shipment Shipment { get; set; } = null!;
    public User AuthorizedByUser { get; set; } = null!;
    public User? AssignedDriverUser { get; set; }

    public ShipmentCustodyTransfer()
    {
        Id = Guid.NewGuid();
    }
}

