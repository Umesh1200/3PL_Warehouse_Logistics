namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Severity level of a slotting constraint.
/// Hard requirements immediately disqualify candidate locations; soft preferences apply a score penalty.
/// </summary>
public enum ConstraintSeverity
{
    /// <summary>
    /// Strict inviolable requirement (e.g. dimensions, weight, temperature, hazard, active status).
    /// Disqualifies the candidate location if violated.
    /// </summary>
    HardRequirement = 1,

    /// <summary>
    /// Optimization preference (e.g. golden zone preference, aisle balance preference).
    /// Reduces suitability score if violated but does not disqualify the location.
    /// </summary>
    SoftPreference = 2
}
