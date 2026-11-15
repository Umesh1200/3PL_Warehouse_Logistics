namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Source channel that reported a carrier delivery exception.
/// DB-SPEC-3PL-2026 Phase 4.5 Step 3B: Delivery Exception Workflows &amp; Returns Management.
/// </summary>
public enum DeliveryExceptionSource
{
    /// <summary>Asynchronous carrier push notification via webhook.</summary>
    CarrierWebhook = 1,

    /// <summary>Periodic carrier tracking polling batch synchronization.</summary>
    CarrierPolling = 2,

    /// <summary>Mobile delivery app or handheld scanner used by delivery driver.</summary>
    DriverHandheld = 3,

    /// <summary>Manual entry by warehouse customer service or operations staff.</summary>
    ManualStaffEntry = 4
}

