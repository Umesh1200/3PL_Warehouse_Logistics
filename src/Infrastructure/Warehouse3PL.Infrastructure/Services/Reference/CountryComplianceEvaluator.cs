using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Reference;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Infrastructure.Services.Reference;

/// <summary>
/// Bitemporal country statutory compliance evaluator distinguishing:
/// 1) Effective-date evaluation (<c>KnownAsOfUtc == null</c>): what currently approved policy specifies for <c>EvaluationUtc</c>.
/// 2) Historical as-known evaluation (<c>KnownAsOfUtc != null</c>): which published policy was active and unsuperseded at <c>KnownAsOfUtc</c>
///    using <c>ApprovedAtUtc</c> and <c>SupersededAtUtc</c>, preventing backdated later publications from rewriting past decisions.
/// Draft policies are always ignored, and missing policies or ambiguous/unspecified inputs always return <c>Indeterminate</c>.
/// </summary>
public sealed class CountryComplianceEvaluator : ICountryComplianceEvaluator
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _dateTime;
    private readonly IConfigurationConsistencyService? _consistencyService;

    public CountryComplianceEvaluator(
        IApplicationDbContext db,
        IDateTimeProvider dateTime,
        IConfigurationConsistencyService? consistencyService = null)
    {
        _db = db;
        _dateTime = dateTime;
        _consistencyService = consistencyService;
    }

    public async Task<ComplianceEvaluationResult> EvaluateAsync(
        ComplianceEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        var countryCode = (request.CountryCode ?? string.Empty).Trim().ToUpperInvariant();
        var policyCode = (request.PolicyCode ?? string.Empty).Trim().ToUpperInvariant();
        var clockNowUtc = _dateTime.UtcNow;

        if (!TryNormalizeUtcInput(request.EvaluationUtc, "EvaluationUtc", clockNowUtc, out var evaluatedAtUtc, out var evalErrCode, out var evalErrMsg))
        {
            return IndeterminateResult(countryCode, policyCode, null, [evalErrCode!], evalErrMsg!, clockNowUtc, request.KnownAsOfUtc);
        }

        DateTime? explicitKnownAsOfUtc = null;
        if (request.KnownAsOfUtc.HasValue)
        {
            if (!TryNormalizeUtcInput(request.KnownAsOfUtc, "KnownAsOfUtc", clockNowUtc, out var normalizedKnown, out var knownErrCode, out var knownErrMsg))
            {
                return IndeterminateResult(countryCode, policyCode, null, [knownErrCode!], knownErrMsg!, evaluatedAtUtc, null);
            }
            explicitKnownAsOfUtc = normalizedKnown;
        }

        var responseKnownAsOfUtc = explicitKnownAsOfUtc ?? clockNowUtc;

        if (countryCode.Length != 2 || string.IsNullOrWhiteSpace(policyCode))
        {
            return IndeterminateResult(
                countryCode, policyCode, null, ["CountryCode", "PolicyCode"],
                "CountryCode and PolicyCode are required to evaluate statutory applicability.",
                evaluatedAtUtc, responseKnownAsOfUtc);
        }

        // Query published policies (VerifiedApproved or Superseded; NEVER Draft)
        // whose [EffectiveFromUtc, EffectiveToUtc) interval covers evaluatedAtUtc,
        // validating against authoritative PostgreSQL scope epoch when consistency service is wired.
        var allPublishedForCode = _consistencyService != null
            ? await _consistencyService.GetOrLoadVerifiedAsync(
                ConfigurationScopeType.Country,
                countryCode,
                $"compliance-policies:{policyCode}",
                (db, ct) => db.CountryCompliancePolicies
                    .AsNoTracking()
                    .Where(p => p.CountryCode == countryCode &&
                                p.PolicyCode == policyCode &&
                                (p.ApprovalStatus == CompliancePolicyApprovalStatus.VerifiedApproved ||
                                 p.ApprovalStatus == CompliancePolicyApprovalStatus.Superseded))
                    .OrderByDescending(p => p.Version)
                    .ToListAsync(ct),
                cancellationToken: cancellationToken)
            : await _db.CountryCompliancePolicies
                .AsNoTracking()
                .Where(p => p.CountryCode == countryCode &&
                            p.PolicyCode == policyCode &&
                            (p.ApprovalStatus == CompliancePolicyApprovalStatus.VerifiedApproved ||
                             p.ApprovalStatus == CompliancePolicyApprovalStatus.Superseded))
                .OrderByDescending(p => p.Version)
                .ToListAsync(cancellationToken);

        var matchingPublished = allPublishedForCode
            .Where(p => p.EffectiveFromUtc <= evaluatedAtUtc &&
                        (p.EffectiveToUtc == null || evaluatedAtUtc < p.EffectiveToUtc))
            .OrderByDescending(p => p.Version)
            .ToList();

        Warehouse3PL.Domain.Entities.Reference.CountryCompliancePolicy? policy;
        bool evaluatedAgainstHistoricalSuperseded = false;

        if (explicitKnownAsOfUtc.HasValue)
        {
            var asKnownMatches = matchingPublished
                .Where(p => p.ApprovedAtUtc.HasValue &&
                            p.ApprovedAtUtc.Value <= explicitKnownAsOfUtc.Value &&
                            (!p.SupersededAtUtc.HasValue || explicitKnownAsOfUtc.Value < p.SupersededAtUtc.Value))
                .ToList();

            if (asKnownMatches.Count > 1)
            {
                return IndeterminateResult(
                    countryCode, policyCode, null, ["AmbiguousAsKnownPolicies"],
                    $"Multiple published policies overlapped both effective instant {evaluatedAtUtc:O} and publication instant {explicitKnownAsOfUtc.Value:O} for '{countryCode}/{policyCode}'.",
                    evaluatedAtUtc, responseKnownAsOfUtc);
            }

            if (asKnownMatches.Count == 0)
            {
                return IndeterminateResult(
                    countryCode, policyCode, null, ["NoPublishedPolicyKnownAsOf"],
                    $"No published policy for '{countryCode}/{policyCode}' covered {evaluatedAtUtc:O} as known at {explicitKnownAsOfUtc.Value:O}.",
                    evaluatedAtUtc, responseKnownAsOfUtc);
            }

            policy = asKnownMatches[0];
            evaluatedAgainstHistoricalSuperseded = policy.ApprovalStatus == CompliancePolicyApprovalStatus.Superseded;
        }
        else
        {
            var matchingActive = matchingPublished
                .Where(p => p.ApprovalStatus == CompliancePolicyApprovalStatus.VerifiedApproved && p.IsActive)
                .ToList();

            if (matchingActive.Count > 1)
            {
                return IndeterminateResult(
                    countryCode, policyCode, null, ["UnambiguousActivePolicy"],
                    $"Multiple active VerifiedApproved policies matched '{countryCode}/{policyCode}' at {evaluatedAtUtc:O}; cannot disambiguate safely.",
                    evaluatedAtUtc, responseKnownAsOfUtc);
            }

            policy = matchingActive.Count == 1 ? matchingActive[0] : null;

            if (policy == null)
            {
                policy = matchingPublished
                    .Where(p => p.ApprovalStatus == CompliancePolicyApprovalStatus.Superseded)
                    .OrderByDescending(p => p.Version)
                    .FirstOrDefault();

                if (policy != null)
                {
                    evaluatedAgainstHistoricalSuperseded = true;
                }
            }

            if (policy == null)
            {
                return IndeterminateResult(
                    countryCode, policyCode, null, ["ActiveVerifiedPolicy"],
                    $"No published policy covers '{countryCode}/{policyCode}' at {evaluatedAtUtc:O}. Draft or uncovered periods do not establish exemption.",
                    evaluatedAtUtc, responseKnownAsOfUtc);
            }
        }

        if (!ReferenceMappings.TryParsePredicate(policy.ApplicabilityPredicateJson, out var predicate, out var parseError) ||
            predicate == null)
        {
            return new ComplianceEvaluationResult(
                ComplianceApplicabilityStatus.Indeterminate,
                countryCode,
                policyCode,
                policy.Version,
                [],
                ["ValidApplicabilityPredicate"],
                policy.StatutorySourceRef,
                policy.OfficialNoticeCitation,
                policy.EnforcementMode,
                ComplianceEvidenceRequirement.None,
                false,
                parseError ?? "Active policy predicate is malformed or incomplete.",
                evaluatedAtUtc,
                responseKnownAsOfUtc,
                policy.ApprovalStatus,
                policy.ApprovedAtUtc);
        }

        var matchedPredicates = new List<string>();
        var missingOrAmbiguous = new List<string>();
        bool anyExplicitExempt = false;
        bool anyExplicitRequired = false;

        EvaluateStringDimension(
            "BusinessClassification",
            request.BusinessClassification,
            predicate.RequiredBusinessClassifications,
            predicate.ExemptBusinessClassifications,
            predicate.TreatUnlistedValuesAsIndeterminate,
            matchedPredicates,
            missingOrAmbiguous,
            ref anyExplicitRequired,
            ref anyExplicitExempt);

        EvaluateStringDimension(
            "OrderCategory",
            request.OrderCategory,
            predicate.RequiredOrderCategories,
            predicate.ExemptOrderCategories,
            predicate.TreatUnlistedValuesAsIndeterminate,
            matchedPredicates,
            missingOrAmbiguous,
            ref anyExplicitRequired,
            ref anyExplicitExempt);

        EvaluateStringDimension(
            "CargoClass",
            request.CargoClass,
            predicate.RequiredCargoClasses,
            predicate.ExemptCargoClasses,
            predicate.TreatUnlistedValuesAsIndeterminate,
            matchedPredicates,
            missingOrAmbiguous,
            ref anyExplicitRequired,
            ref anyExplicitExempt);

        if (predicate.RequiresTaxpayerMandateFlag.HasValue)
        {
            if (!request.TaxpayerMandateFlag.HasValue)
            {
                missingOrAmbiguous.Add("TaxpayerMandateFlag");
            }
            else if (request.TaxpayerMandateFlag.Value == predicate.RequiresTaxpayerMandateFlag.Value)
            {
                anyExplicitRequired = true;
                matchedPredicates.Add($"TaxpayerMandateFlag={request.TaxpayerMandateFlag.Value}");
            }
            else
            {
                anyExplicitExempt = true;
                matchedPredicates.Add($"TaxpayerMandateFlag={request.TaxpayerMandateFlag.Value}(NotMandatedInPolicy)");
            }
        }

        if (missingOrAmbiguous.Count > 0)
        {
            return new ComplianceEvaluationResult(
                ComplianceApplicabilityStatus.Indeterminate,
                countryCode,
                policyCode,
                policy.Version,
                matchedPredicates,
                missingOrAmbiguous,
                policy.StatutorySourceRef,
                policy.OfficialNoticeCitation,
                policy.EnforcementMode,
                ComplianceEvidenceRequirement.None,
                false,
                $"Missing or ambiguous inputs for policy '{policyCode}' v{policy.Version}: {string.Join(", ", missingOrAmbiguous)}.",
                evaluatedAtUtc,
                responseKnownAsOfUtc,
                policy.ApprovalStatus,
                policy.ApprovedAtUtc);
        }

        // If any dimension matched an explicit non-applicable category and no contradictory required-trigger
        // needs override, return NotRequired strictly per the approved policy predicate (never asserting statutory immunity).
        var historicalNote = explicitKnownAsOfUtc.HasValue
            ? $" (as known at {explicitKnownAsOfUtc.Value:O})"
            : evaluatedAgainstHistoricalSuperseded
                ? " (resolved via historical Superseded version for requested EvaluationUtc)"
                : string.Empty;

        if (anyExplicitExempt && !anyExplicitRequired)
        {
            return new ComplianceEvaluationResult(
                ComplianceApplicabilityStatus.NotRequired,
                countryCode,
                policyCode,
                policy.Version,
                matchedPredicates,
                [],
                policy.StatutorySourceRef,
                policy.OfficialNoticeCitation,
                policy.EnforcementMode,
                ComplianceEvidenceRequirement.None,
                false,
                $"Evaluated NotRequired against configured policy '{policyCode}' v{policy.Version}{historicalNote}.",
                evaluatedAtUtc,
                responseKnownAsOfUtc,
                policy.ApprovalStatus,
                policy.ApprovedAtUtc);
        }

        if (anyExplicitExempt && anyExplicitRequired)
        {
            return new ComplianceEvaluationResult(
                ComplianceApplicabilityStatus.Indeterminate,
                countryCode,
                policyCode,
                policy.Version,
                matchedPredicates,
                ["ConflictingPredicateMatch"],
                policy.StatutorySourceRef,
                policy.OfficialNoticeCitation,
                policy.EnforcementMode,
                ComplianceEvidenceRequirement.None,
                false,
                $"Inputs simultaneously matched required and excluded criteria in policy '{policyCode}' v{policy.Version}.",
                evaluatedAtUtc,
                responseKnownAsOfUtc,
                policy.ApprovalStatus,
                policy.ApprovedAtUtc);
        }

        var evidenceReq = policy.EnforcementMode == ComplianceEnforcementMode.AdvisoryWarning
            ? ComplianceEvidenceRequirement.None
            : ComplianceEvidenceRequirement.ManualReferenceRecorded;

        return new ComplianceEvaluationResult(
            ComplianceApplicabilityStatus.Required,
            countryCode,
            policyCode,
            policy.Version,
            matchedPredicates,
            [],
            policy.StatutorySourceRef,
            policy.OfficialNoticeCitation,
            policy.EnforcementMode,
            evidenceReq,
            false, // ManualReferenceRecorded captures documentation only; never asserts external government verification.
            $"Matched applicability predicates for policy '{policyCode}' v{policy.Version}{historicalNote}.",
            evaluatedAtUtc,
            responseKnownAsOfUtc,
            policy.ApprovalStatus,
            policy.ApprovedAtUtc);
    }

    private static bool TryNormalizeUtcInput(
        DateTime? rawTimestamp,
        string fieldName,
        DateTime fallbackUtc,
        out DateTime normalizedUtc,
        out string? errorCode,
        out string? errorMessage)
    {
        errorCode = null;
        errorMessage = null;
        if (!rawTimestamp.HasValue)
        {
            normalizedUtc = fallbackUtc;
            return true;
        }

        switch (rawTimestamp.Value.Kind)
        {
            case DateTimeKind.Utc:
                normalizedUtc = rawTimestamp.Value;
                return true;
            case DateTimeKind.Local:
                normalizedUtc = rawTimestamp.Value.ToUniversalTime();
                return true;
            default:
                normalizedUtc = fallbackUtc;
                errorCode = $"{fieldName}:UnspecifiedKind";
                errorMessage = $"{fieldName} must be explicitly UTC (DateTimeKind.Utc) or timezone-aware (DateTimeKind.Local); naive Unspecified timestamps are rejected fail-closed.";
                return false;
        }
    }

    private static ComplianceEvaluationResult IndeterminateResult(
        string countryCode,
        string policyCode,
        int? policyVersion,
        IReadOnlyList<string> missingOrAmbiguous,
        string reason,
        DateTime evaluatedAtUtc,
        DateTime? knownAsOfUtc) =>
        new(
            ComplianceApplicabilityStatus.Indeterminate,
            countryCode,
            policyCode,
            policyVersion,
            [],
            missingOrAmbiguous,
            null,
            null,
            null,
            ComplianceEvidenceRequirement.None,
            false,
            reason,
            evaluatedAtUtc,
            knownAsOfUtc);

    private static void EvaluateStringDimension(
        string dimensionName,
        string? rawInput,
        IReadOnlyList<string> requiredValues,
        IReadOnlyList<string> excludedValues,
        bool treatUnlistedAsIndeterminate,
        List<string> matchedPredicates,
        List<string> missingOrAmbiguous,
        ref bool anyExplicitRequired,
        ref bool anyExplicitExempt)
    {
        if (requiredValues.Count == 0 && excludedValues.Count == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(rawInput))
        {
            missingOrAmbiguous.Add(dimensionName);
            return;
        }

        var value = rawInput.Trim();
        bool inRequired = requiredValues.Any(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));
        bool inExcluded = excludedValues.Any(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));

        if (inRequired && inExcluded)
        {
            missingOrAmbiguous.Add($"{dimensionName}:AmbiguousRuleDefinition");
            return;
        }

        if (inRequired)
        {
            anyExplicitRequired = true;
            matchedPredicates.Add($"{dimensionName}={value}");
            return;
        }

        if (inExcluded)
        {
            anyExplicitExempt = true;
            matchedPredicates.Add($"{dimensionName}={value}(PolicyExcluded)");
            return;
        }

        if (treatUnlistedAsIndeterminate)
        {
            missingOrAmbiguous.Add($"{dimensionName}:Unrecognized('{value}')");
        }
        else
        {
            anyExplicitExempt = true;
            matchedPredicates.Add($"{dimensionName}={value}(UnlistedNotRequired)");
        }
    }
}
