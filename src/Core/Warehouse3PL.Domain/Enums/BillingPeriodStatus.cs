namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Status of a financial billing cycle period for a client tenant.
/// DB-SPEC-3PL-2026 Phase 5.0 Commercial Domain &amp; Billing Foundation.
/// </summary>
public enum BillingPeriodStatus
{
    /// <summary>Period is open and actively accepting new billable activities.</summary>
    Open = 1,

    /// <summary>Period is locked for snapshot rating and billing run preparation; no new activities accepted.</summary>
    Locked = 2,

    /// <summary>Period has concluded and all rated activities have been locked.</summary>
    Closed = 3,

    /// <summary>Period invoices have been generated and issued.</summary>
    Invoiced = 4
}

