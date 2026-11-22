using System.Text.Json.Serialization;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

public class IngestCarrierExceptionRequest
{
    public string CarrierCode { get; set; } = string.Empty;
    public string TrackingNumber { get; set; } = string.Empty;
    public string? CarrierEventId { get; set; }
    public string CarrierExceptionCode { get; set; } = string.Empty;
    public string CarrierDescription { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public string? EventCity { get; set; }
    public string? EventStateOrProvince { get; set; }
    public string? RawPayloadJson { get; set; }
    public Guid? TenantIdOverride { get; set; }
    public DeliveryExceptionSource Source { get; set; } = DeliveryExceptionSource.CarrierWebhook;
}

public class ReportDriverExceptionRequest
{
    public Guid PackageId { get; set; }
    public DeliveryExceptionType ExceptionType { get; set; }
    public string DriverNotes { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public Guid? DriverUserId { get; set; }
    public Guid? TenantIdOverride { get; set; }

    [JsonIgnore]
    public Func<CancellationToken, Task>? BeforeCommitCustodyValidationAsync { get; set; }
}

public class ResolveDeliveryExceptionRequest
{
    public Guid ExceptionId { get; set; }
    public DeliveryExceptionResolution Resolution { get; set; } = DeliveryExceptionResolution.Resolved;
    public string ResolutionNotes { get; set; } = string.Empty;
    public Guid? ResolvedByUserId { get; set; }
    public Guid? TenantIdOverride { get; set; }
}

public class ReopenDeliveryExceptionRequest
{
    public Guid ExceptionId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public Guid? TenantIdOverride { get; set; }
}

public class VoidDeliveryExceptionRequest
{
    public Guid ExceptionId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public Guid? TenantIdOverride { get; set; }
}

public class ScheduleDeliveryReattemptRequest
{
    public Guid ExceptionId { get; set; }
    public DateTime ScheduledDateUtc { get; set; }
    public string Notes { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public Guid? TenantIdOverride { get; set; }
}

public class CancelDeliveryReattemptRequest
{
    public Guid ExceptionId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public Guid? TenantIdOverride { get; set; }
}

public class RescheduleDeliveryReattemptRequest
{
    public Guid ExceptionId { get; set; }
    public DateTime NewScheduledDateUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public Guid? TenantIdOverride { get; set; }
}

public class RecordCustomerContactRequest
{
    public Guid ExceptionId { get; set; }
    public CustomerContactMethod ContactMethod { get; set; }
    public CustomerContactOutcome Outcome { get; set; }
    public string Notes { get; set; } = string.Empty;
    public string? ResolutionAction { get; set; }
    public Guid? StaffUserId { get; set; }
    public Guid? TenantIdOverride { get; set; }
}

public class UpdateDeliveryAddressRequest
{
    public Guid ExceptionId { get; set; }
    public string CorrectedAddress { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public Guid? TenantIdOverride { get; set; }
}

public class RedirectToCarrierDepotRequest
{
    public Guid ExceptionId { get; set; }
    public string DepotLocation { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public Guid? TenantIdOverride { get; set; }
}

public class DeliveryExceptionResult
{
    public bool IsSuccess { get; set; }
    public bool IsDuplicate { get; set; }
    public Guid ExceptionId { get; set; }
    public Guid PackageId { get; set; }
    public Guid? ShipmentId { get; set; }
    public Guid OrderId { get; set; }
    public string TrackingNumber { get; set; } = string.Empty;
    public string CarrierCode { get; set; } = string.Empty;
    public DeliveryExceptionType ExceptionType { get; set; }
    public DeliveryExceptionStatus Status { get; set; }
    public DeliveryExceptionSeverity Severity { get; set; }
    public TrackingMilestoneStatus PackageTrackingStatus { get; set; }
    public OrderDeliveryStatus OrderDeliveryStatus { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Service coordinating delivery exception ingestion, driver exception reporting,
/// classification, and tracking status synchronization.
/// DB-SPEC-3PL-2026 Phase 4.5 Step 3B: Delivery Exception Workflows &amp; Returns Management.
/// </summary>
public interface IDeliveryExceptionService
{
    /// <summary>
    /// Ingests and normalizes an asynchronous carrier delivery failure webhook or polling event.
    /// Deduplicates repeated events and handles concurrent webhooks idempotently.
    /// </summary>
    Task<DeliveryExceptionResult> IngestCarrierExceptionAsync(
        IngestCarrierExceptionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ingests a delivery exception reported directly by an authenticated courier or driver via mobile handheld.
    /// Captures driver notes, GPS coordinates, and verifies carrier/driver shipment custody.
    /// </summary>
    Task<DeliveryExceptionResult> ReportDriverExceptionAsync(
        ReportDriverExceptionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves an open delivery exception with an authorized operational resolution.
    /// </summary>
    Task<DeliveryExceptionResult> ResolveExceptionAsync(
        ResolveDeliveryExceptionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reopens an existing resolved or voided delivery exception for renewed operational handling.
    /// </summary>
    Task<DeliveryExceptionResult> ReopenExceptionAsync(
        ReopenDeliveryExceptionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Voids or rescinds an erroneously created delivery exception.
    /// </summary>
    Task<DeliveryExceptionResult> VoidExceptionAsync(
        VoidDeliveryExceptionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedules a carrier delivery reattempt for an eligible delivery exception.
    /// </summary>
    Task<DeliveryExceptionResult> ScheduleReattemptAsync(
        ScheduleDeliveryReattemptRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels an actively scheduled delivery reattempt.
    /// </summary>
    Task<DeliveryExceptionResult> CancelReattemptAsync(
        CancelDeliveryReattemptRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reschedules an existing delivery reattempt with an updated date.
    /// </summary>
    Task<DeliveryExceptionResult> RescheduleReattemptAsync(
        RescheduleDeliveryReattemptRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a structured customer/consignee contact attempt and updates exception status.
    /// </summary>
    Task<DeliveryExceptionResult> RecordCustomerContactAsync(
        RecordCustomerContactRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates consignee delivery address for retransmission to carrier.
    /// </summary>
    Task<DeliveryExceptionResult> UpdateAddressAsync(
        UpdateDeliveryAddressRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Redirects package to carrier depot or access point for customer pickup.
    /// </summary>
    Task<DeliveryExceptionResult> RedirectToDepotAsync(
        RedirectToCarrierDepotRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a delivery exception by its identifier.
    /// </summary>
    Task<ShipmentDeliveryException?> GetExceptionByIdAsync(
        Guid exceptionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all active and historical delivery exceptions for a package.
    /// </summary>
    Task<List<ShipmentDeliveryException>> GetExceptionsForPackageAsync(
        Guid packageId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the customer contact log history for an exception.
    /// </summary>
    Task<List<DeliveryExceptionContactLog>> GetContactLogsForExceptionAsync(
        Guid exceptionId,
        CancellationToken cancellationToken = default);
}
