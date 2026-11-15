using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Carrier shipping label payload generated for an outbound package.
/// DB-SPEC-3PL-2026 Phase 4.4 Packing & Dispatch Preparation.
/// </summary>
public class ShippingLabel : BaseEntity<Guid>
{
    public Guid PackageId { get; set; }

    public string TrackingNumber { get; set; } = string.Empty;
    public string CarrierCode { get; set; } = string.Empty;
    public CarrierServiceLevel ServiceLevel { get; set; } = CarrierServiceLevel.StandardGround;

    public string LabelFormat { get; set; } = "ZPL";
    public string LabelData { get; set; } = string.Empty;
    public string? RoutingCode { get; set; }

    public ShippingLabelStatus Status { get; set; } = ShippingLabelStatus.Generated;
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PrintedAtUtc { get; set; }
    public DateTime? VoidedAtUtc { get; set; }
    public string? VoidReason { get; set; }

    public bool IsVoided => Status == ShippingLabelStatus.Voided;

    // Navigations
    public Package Package { get; set; } = null!;

    public ShippingLabel()
    {
        Id = Guid.NewGuid();
    }

    public void MarkPrinted()
    {
        if (Status == ShippingLabelStatus.Voided)
        {
            throw new InvalidDomainStateException("Cannot print voided shipping label.");
        }

        Status = ShippingLabelStatus.Printed;
        PrintedAtUtc = DateTime.UtcNow;
    }

    public void Void(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException(nameof(reason), "A reason must be provided when voiding a shipping label.");
        }

        Status = ShippingLabelStatus.Voided;
        VoidedAtUtc = DateTime.UtcNow;
        VoidReason = reason;
    }
}

