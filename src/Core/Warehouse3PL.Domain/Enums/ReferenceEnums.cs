namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Structural address format pattern governed by a country's postal standards.
/// </summary>
public enum AddressSchemaType
{
    UsStateZip = 1,
    NepalProvinceDistrictPalika = 2,
    GenericPostal = 3
}

/// <summary>
/// Civil calendar system(s) used operationally in a country profile.
/// </summary>
public enum PrimaryCalendarSystem
{
    Gregorian = 1,
    BikramSambatDual = 2
}

/// <summary>
/// Fiscal year calendar basis used for statutory tax and financial periods.
/// Note: Nepal uses Bikram Sambat month 4 (Shrawan), which is distinct from Gregorian April.
/// </summary>
public enum FiscalCalendarSystem
{
    Gregorian = 1,
    BikramSambat = 2
}

/// <summary>
/// Hierarchy level for authoritative administrative subdivisions.
/// </summary>
public enum AdministrativeDivisionLevel
{
    State = 1,
    Province = 2,
    District = 3,
    LocalLevelPalika = 4
}

/// <summary>
/// Operational enforcement posture declared by an approved country compliance policy.
/// </summary>
public enum ComplianceEnforcementMode
{
    MandatoryFailClosed = 1,
    ConditionalRequired = 2,
    AdvisoryWarning = 3
}

/// <summary>
/// Publication and audit lifecycle state of a CountryCompliancePolicy version.
/// </summary>
public enum CompliancePolicyApprovalStatus
{
    Draft = 1,
    VerifiedApproved = 2,
    Superseded = 3
}

/// <summary>
/// Geographic or operational applicability tier for HolidayCalendar entries.
/// Precedence at evaluation time: Facility &gt; Regional &gt; National.
/// </summary>
public enum HolidayScope
{
    National = 1,
    Regional = 2,
    Facility = 3
}

/// <summary>
/// Monotonic configuration epoch partition scope for DB-authoritative multi-instance cache consistency.
/// </summary>
public enum ConfigurationScopeType
{
    Global = 1,
    Country = 2,
    LegalEntity = 3,
    Facility = 4,
    Client = 5
}
