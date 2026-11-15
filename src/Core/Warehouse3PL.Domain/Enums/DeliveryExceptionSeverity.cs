namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Operational severity classification for delivery exceptions.
/// DB-SPEC-3PL-2026 Phase 4.5 Step 3B: Delivery Exception Workflows &amp; Returns Management.
/// </summary>
public enum DeliveryExceptionSeverity
{
    /// <summary>Minor or informational delay (e.g., weather delay warning, minor sort delay).</summary>
    Low = 1,

    /// <summary>Standard delivery failure requiring carrier re-attempt or customer contact (e.g., business closed, recipient absent).</summary>
    Medium = 2,

    /// <summary>Action required to prevent return (e.g., customer refused, access restricted, incorrect address).</summary>
    High = 3,

    /// <summary>Critical anomaly requiring immediate escalation (e.g., package crushed/damaged in transit, lost freight).</summary>
    Critical = 4
}

