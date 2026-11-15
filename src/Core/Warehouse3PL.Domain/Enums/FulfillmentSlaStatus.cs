namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Granular SLA lifecycle status for outbound order fulfillment.
/// DB-SPEC-3PL-2026 Phase 6.1 Executive Control Tower &amp; Real-Time Visibility.
/// </summary>
public enum FulfillmentSlaStatus
{
    /// <summary>Order is progressing on schedule with ample buffer before cutoff.</summary>
    OnTrack = 1,

    /// <summary>Order is within 2 hours of required ship cutoff and active in workflow.</summary>
    ApproachingDeadline = 2,

    /// <summary>Order is within 1 hour of cutoff with significant pending work remaining.</summary>
    AtRisk = 3,

    /// <summary>Current time exceeds required ship date without dispatch handover.</summary>
    Breached = 4,

    /// <summary>Order was successfully dispatched on or before the contractual SLA cutoff.</summary>
    CompletedWithinSla = 5,

    /// <summary>Order was dispatched after the contractual SLA cutoff.</summary>
    CompletedOutsideSla = 6
}

