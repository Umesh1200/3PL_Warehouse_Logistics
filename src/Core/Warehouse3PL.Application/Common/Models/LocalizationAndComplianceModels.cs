using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

/// <summary>
/// Authoritative configuration scope hierarchy for domain-specific resolvers.
/// </summary>
public enum ConfigScope
{
    Global = 1,
    LegalEntity = 2,
    Facility = 3,
    Client = 4,
    ClientFacility = 5,
    OrderOverride = 6
}

/// <summary>
/// Configuration inheritance state (Specification §2.1).
/// Preserved strictly for configuration resolution; never overloaded with statutory exemption states.
/// </summary>
public enum ConfigResolutionState
{
    ExplicitValue = 1,
    Inherited = 2,
    ExplicitNull = 3,
    Disabled = 4,
    Prohibited = 5
}

/// <summary>
/// Formal configuration resolution envelope carrying provenance, version, and effective window.
/// </summary>
public sealed record ResolvedConfigurationResult<T>(
    T? Value,
    ConfigResolutionState State,
    ConfigScope SourceScope,
    Guid? SourceEntityId,
    string? SourceEntityCode,
    long ConfigRevision,
    DateTime? EffectiveFromUtc,
    DateTime? EffectiveToUtc,
    bool AllowsTenantOverride,
    string ProvenanceReason);

/// <summary>
/// Typed statutory compliance applicability outcome (MDM11-D02).
/// Missing or ambiguous inputs MUST return <see cref="Indeterminate"/> and never <see cref="NotRequired"/>.
/// </summary>
public enum ComplianceApplicabilityStatus
{
    Required = 1,
    NotRequired = 2,
    Indeterminate = 3
}

/// <summary>
/// Evidence capture posture for a compliance evaluation outcome.
/// Never conflated with external government authority verification.
/// </summary>
public enum ComplianceEvidenceRequirement
{
    None = 0,
    ManualReferenceRecorded = 1,
    AuthorityApiIntegrationFuture = 2
}

/// <summary>
/// Structured predicate schema serialized into CountryCompliancePolicy.ApplicabilityPredicateJson.
/// </summary>
public sealed record ComplianceApplicabilityPredicate
{
    public IReadOnlyList<string> RequiredBusinessClassifications { get; init; } = [];
    public IReadOnlyList<string> ExemptBusinessClassifications { get; init; } = [];
    public IReadOnlyList<string> RequiredOrderCategories { get; init; } = [];
    public IReadOnlyList<string> ExemptOrderCategories { get; init; } = [];
    public IReadOnlyList<string> RequiredCargoClasses { get; init; } = [];
    public IReadOnlyList<string> ExemptCargoClasses { get; init; } = [];
    public bool? RequiresTaxpayerMandateFlag { get; init; }
    public bool TreatUnlistedValuesAsIndeterminate { get; init; } = true;
    public string? Notes { get; init; }

    public bool HasAnyRuleCriterion() =>
        RequiredBusinessClassifications.Count > 0 ||
        ExemptBusinessClassifications.Count > 0 ||
        RequiredOrderCategories.Count > 0 ||
        ExemptOrderCategories.Count > 0 ||
        RequiredCargoClasses.Count > 0 ||
        ExemptCargoClasses.Count > 0 ||
        RequiresTaxpayerMandateFlag.HasValue;
}

public sealed record ComplianceEvaluationRequest(
    string CountryCode,
    string PolicyCode,
    DateTime? EvaluationUtc = null,
    string? BusinessClassification = null,
    string? OrderCategory = null,
    string? CargoClass = null,
    bool? TaxpayerMandateFlag = null);

public sealed record ComplianceEvaluationResult(
    ComplianceApplicabilityStatus Status,
    string CountryCode,
    string PolicyCode,
    int? EvaluatedPolicyVersion,
    IReadOnlyList<string> MatchedPredicates,
    IReadOnlyList<string> MissingOrAmbiguousInputs,
    string? StatutorySourceRef,
    string? OfficialNoticeCitation,
    ComplianceEnforcementMode? EnforcementMode,
    ComplianceEvidenceRequirement EvidenceRequirement,
    bool AssertsAuthorityVerified,
    string Reason,
    DateTime EvaluatedAtUtc);

public sealed record CountryProfileDto(
    string CountryCode,
    string IsoAlpha3Code,
    string NumericCode,
    string Name,
    string DefaultCurrencyCode,
    string PrimaryTimeZoneId,
    AddressSchemaType AddressSchemaType,
    string PostalCodeRegex,
    string? LegacyPostalCodeRegex,
    PrimaryCalendarSystem PrimaryCalendarSystem,
    FiscalCalendarSystem FiscalCalendarSystem,
    int DefaultFiscalYearStartMonth,
    bool IsActive,
    int SeedVersion);

public sealed record AdministrativeDivisionDto(
    Guid Id,
    string CountryCode,
    Guid? ParentDivisionId,
    AdministrativeDivisionLevel DivisionLevel,
    string? IsoCode,
    string Code,
    string Name,
    string? LocalName,
    string? PostalPrefix,
    bool IsActive);

public sealed record TimeZoneProfileDto(
    string TimeZoneId,
    string DisplayName,
    bool ObservesDst,
    string? CountryCode,
    bool IsActive,
    int CurrentUtcOffsetMinutes,
    DateTime OffsetEvaluatedAtUtc);

public sealed record CountryCompliancePolicyDto(
    Guid Id,
    string CountryCode,
    string PolicyCode,
    int Version,
    string Title,
    string StatutorySourceRef,
    string OfficialNoticeCitation,
    string ApplicabilityPredicateJson,
    ComplianceEnforcementMode EnforcementMode,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc,
    CompliancePolicyApprovalStatus ApprovalStatus,
    Guid? ApprovedByUserId,
    DateTime? ApprovedAtUtc,
    string? VerificationEvidenceNotes,
    Guid? SupersededByPolicyId,
    DateTime? SupersededAtUtc,
    long ConfigRevision,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
