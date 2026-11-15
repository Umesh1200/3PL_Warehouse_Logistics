using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

public class AuthorizeReturnToSenderRequest
{
    public Guid DeliveryExceptionId { get; set; }
    public Guid DestinationFacilityId { get; set; }
    public ReturnReason Reason { get; set; } = ReturnReason.Other;
    public string AuthorizationNotes { get; set; } = string.Empty;
    public string? RmaNumber { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public int? ExpiryDays { get; set; }
    public Guid? AuthorizedByUserId { get; set; }
    public Guid? TenantIdOverride { get; set; }
}

public class CancelReturnAuthorizationRequest
{
    public Guid AuthorizationId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid? CancelledByUserId { get; set; }
    public Guid? TenantIdOverride { get; set; }
}

public class ExpireReturnAuthorizationRequest
{
    public Guid AuthorizationId { get; set; }
    public string? Reason { get; set; }
    public Guid? TenantIdOverride { get; set; }
}

public class ReturnAuthorizationResult
{
    public bool IsSuccess { get; set; }
    public Guid AuthorizationId { get; set; }
    public string AuthorizationNumber { get; set; } = string.Empty;
    public Guid ClientId { get; set; }
    public Guid PackageId { get; set; }
    public Guid OrderId { get; set; }
    public Guid? OriginalShipmentId { get; set; }
    public Guid? DeliveryExceptionId { get; set; }
    public Guid? ReturnShipmentId { get; set; }
    public ReturnReason Reason { get; set; }
    public ReturnAuthorizationStatus Status { get; set; }
    public DateTime AuthorizedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Service coordinating Return-To-Sender (RTS) operational authorizations.
/// Enforces operational eligibility, relationship integrity, duplicate prevention, and atomic exception state transitions.
/// DB-SPEC-3PL-2026 Phase 4.5 Step 3B.4: Return-to-Sender Authorization, Return Shipment &amp; Tracking.
/// </summary>
public interface IReturnAuthorizationService
{
    /// <summary>
    /// Explicitly authorizes a Return-To-Sender workflow for an eligible delivery exception.
    /// Atomically transitions the exception aggregate and creates the ReturnAuthorization record.
    /// </summary>
    Task<ReturnAuthorizationResult> AuthorizeReturnToSenderAsync(
        AuthorizeReturnToSenderRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels an active return authorization prior to reverse transit custody.
    /// Invalidates any linked return shipment booking and resets the delivery exception.
    /// </summary>
    Task<ReturnAuthorizationResult> CancelAuthorizationAsync(
        CancelReturnAuthorizationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Expires a return authorization whose authorization horizon has elapsed prior to carrier handover.
    /// </summary>
    Task<ReturnAuthorizationResult> ExpireAuthorizationAsync(
        ExpireReturnAuthorizationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a return authorization by its unique identifier.
    /// </summary>
    Task<ReturnAuthorization?> GetAuthorizationByIdAsync(
        Guid authorizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the active return authorization for a package, if any exists.
    /// </summary>
    Task<ReturnAuthorization?> GetActiveAuthorizationForPackageAsync(
        Guid packageId,
        CancellationToken cancellationToken = default);
}

