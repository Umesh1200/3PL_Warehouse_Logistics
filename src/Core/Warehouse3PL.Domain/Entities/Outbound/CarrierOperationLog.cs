using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Audit and reconciliation log for carrier API operations.
/// Records pending, successful, and failed carrier calls outside DB transactions
/// to guarantee idempotency and prevent duplicate bookings.
/// DB-SPEC-3PL-2026 Phase 4.4 Packing & Dispatch Preparation.
/// </summary>
public class CarrierOperationLog : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid PackageId { get; set; }

    public string OperationType { get; set; } = "GenerateLabel";
    public string CarrierCode { get; set; } = string.Empty;
    public string ServiceLevel { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;

    public string Status { get; set; } = "Pending"; // Pending, Success, Failed

    public string RequestPayload { get; set; } = string.Empty;
    public string? ResponsePayload { get; set; }
    public string? TrackingNumber { get; set; }
    public string? ErrorMessage { get; set; }

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public Package Package { get; set; } = null!;

    public CarrierOperationLog()
    {
        Id = Guid.NewGuid();
    }

    public void MarkSuccess(string responsePayload, string trackingNumber)
    {
        Status = "Success";
        ResponsePayload = responsePayload;
        TrackingNumber = trackingNumber;
        CompletedAtUtc = DateTime.UtcNow;
    }

    public void MarkFailed(string errorMessage)
    {
        Status = "Failed";
        ErrorMessage = errorMessage;
        CompletedAtUtc = DateTime.UtcNow;
    }
}

