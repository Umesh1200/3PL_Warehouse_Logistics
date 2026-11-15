namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Status of an individual shipment's contractual carrier SLA performance evaluation.
/// DB-SPEC-3PL-2026 Phase 6.4 Transportation & Carrier SLA Analytics.
/// </summary>
public enum SlaEvaluationStatus
{
    /// <summary>
    /// Shipment SLA performance successfully evaluated with full milestone timestamps and committed EDD.
    /// </summary>
    Evaluated = 1,

    /// <summary>
    /// SLA evaluation indeterminate because contractual or carrier Estimated Delivery Date (EDD) is missing.
    /// </summary>
    IndeterminateDueToMissingEdd = 2,

    /// <summary>
    /// SLA evaluation indeterminate because dispatch or final delivery timestamp is missing or malformed.
    /// </summary>
    IndeterminateDueToMissingTimestamps = 3,

    /// <summary>
    /// Shipment explicitly excluded from SLA compliance measurement (e.g. customer-directed hold, cancelled).
    /// </summary>
    ExcludedFromSla = 4,

    /// <summary>
    /// SLA evaluation indeterminate because no active contractual SLA policy was found for carrier, lane, and client.
    /// </summary>
    IndeterminateDueToMissingPolicy = 5
}
