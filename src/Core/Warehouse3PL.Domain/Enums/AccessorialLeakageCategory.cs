namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Operational and contractual classification of unrecovered carrier accessorial surcharges.
/// Differentiates expected contractual absorption from operational leakage defects.
/// DB-SPEC-3PL-2026 Phase 6.4.4.2 Variance &amp; Accessorial Discrepancy Analytics.
/// </summary>
public enum AccessorialLeakageCategory
{
    /// <summary>
    /// Contract explicitly designates that the 3PL absorbs the surcharge (e.g. residential delivery bundled into base markup).
    /// Expected cost; reported separately from operational revenue leakage.
    /// </summary>
    AbsorbedByContract = 1,

    /// <summary>
    /// Surcharge was contractually billable but omitted from client invoice due to operational or ingestion delay.
    /// Represents true unrecovered revenue leakage.
    /// </summary>
    UnbilledOperationalCharge = 2,

    /// <summary>
    /// Carrier assessed an address correction or bad postal code penalty not passed through to client.
    /// </summary>
    AddressCorrectionPenalty = 3,

    /// <summary>
    /// Carrier weekly fuel index escalated beyond client monthly fuel surcharge baseline.
    /// </summary>
    FuelIndexingDrift = 4,

    /// <summary>
    /// Carrier re-weighed or re-measured parcel, assessing additional dimensional weight surcharges not billed to client.
    /// </summary>
    DimensionalWeightAdjustment = 5,

    /// <summary>
    /// Carrier invoice line disputed as unauthorized or improper (quarantined from client billing).
    /// </summary>
    CarrierOvercharge = 6,

    /// <summary>
    /// Client was billed a fixed contractual fee that fell short of the actual carrier surcharge.
    /// </summary>
    ClientUnderRecovery = 7,

    /// <summary>
    /// Surcharge unbilled and unrecovered without specific root-cause categorization.
    /// </summary>
    UncontestedDiscrepancy = 99
}
