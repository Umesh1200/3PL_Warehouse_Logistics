namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Confidence score for delivery exception delay attribution.
/// DB-SPEC-3PL-2026 Phase 6.4 Transportation & Carrier SLA Analytics.
/// </summary>
public enum AttributionConfidence
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3
}
