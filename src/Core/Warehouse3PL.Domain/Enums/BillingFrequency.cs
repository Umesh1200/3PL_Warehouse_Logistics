namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Frequency of commercial billing runs and invoicing cycles.
/// DB-SPEC-3PL-2026 Phase 5.0 Billing Foundation.
/// </summary>
public enum BillingFrequency
{
    Weekly = 1,
    BiWeekly = 2,
    Monthly = 3,
    Custom = 4
}

