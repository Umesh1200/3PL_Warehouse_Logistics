using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Reference;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Infrastructure.Services.Reference;

/// <summary>
/// Evaluates statutory applicability strictly against active, VerifiedApproved policy versions.
/// Draft and Superseded policies are ignored, and any missing active policy, invalid predicate,
/// or missing/ambiguous input resolves to Indeterminate rather than NotRequired.
/// </summary>
public sealed class CountryComplianceEvaluator : ICountryComplianceEvaluator
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _dateTime;

    public CountryComplianceEvaluator(IApplicationDbContext db, IDateTimeProvider dateTime)
    {
        _db = db;
        _dateTime = dateTime;
    }

    public async Task<ComplianceEvaluationResult> EvaluateAsync(
        ComplianceEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        var evaluatedAtUtc = request.EvaluationUtc.HasValue
            ? (request.EvaluationUtc.Value.Kind == DateTimeKind.Utc
                ? request.EvaluationUtc.Value
                : DateTime.SpecifyKind(request.EvaluationUtc.Value, DateTimeKind.Utc))
            : _dateTime.UtcNow;

        var countryCode = (request.CountryCode ?? string.Empty).Trim().ToUpperInvariant();
        var policyCode = (request.PolicyCode ?? string.Empty).Trim().ToUpperInvariant();

        if (countryCode.Length != 2 || string.IsNullOrWhiteSpace(policyCode))
        {
            return new ComplianceEvaluationResult(
                ComplianceApplicabilityStatus.Indeterminate,
                countryCode,
                policyCode,
                null,
                [],
                ["CountryCode", "PolicyCode"],
                null,
                null,
                null,
                ComplianceEvidenceRequirement.None,
                false,
                "CountryCode and PolicyCode are required to evaluate statutory applicability.",
                evaluatedAtUtc);
        }

        // Only active VerifiedApproved policies covering evaluatedAtUtc participate in evaluation.
        var matchingActive = await _db.CountryCompliancePolicies
            .AsNoTracking()
            .Where(p => p.CountryCode == countryCode &&
                        p.PolicyCode == policyCode &&
                        p.ApprovalStatus == CompliancePolicyApprovalStatus.VerifiedApproved &&
                        p.IsActive &&
                        p.EffectiveFromUtc <= evaluatedAtUtc &&
                        (p.EffectiveToUtc == null || evaluatedAtUtc < p.EffectiveToUtc))
            .OrderByDescending(p => p.Version)
            .ToListAsync(cancellationToken);

        if (matchingActive.Count == 0)
        {
            return new ComplianceEvaluationResult(
                ComplianceApplicabilityStatus.Indeterminate,
                countryCode,
                policyCode,
                null,
                [],
                ["ActiveVerifiedPolicy"],
                null,
                null,
                null,
                ComplianceEvidenceRequirement.None,
                false,
                $"No active VerifiedApproved policy covers '{countryCode}/{policyCode}' at {evaluatedAtUtc:O}. Draft or expired definitions do not establish exemption.",
                evaluatedAtUtc);
        }

        if (matchingActive.Count > 1)
        {
            return new ComplianceEvaluationResult(
                ComplianceApplicabilityStatus.Indeterminate,
                countryCode,
                policyCode,
                null,
                [],
                ["UnambiguousActivePolicy"],
                null,
                null,
                null,
                ComplianceEvidenceRequirement.None,
                false,
                $"Multiple active VerifiedApproved policies matched '{countryCode}/{policyCode}' at {evaluatedAtUtc:O}; cannot disambiguate safely.",
                evaluatedAtUtc);
        }

        var policy = matchingActive[0];
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
                evaluatedAtUtc);
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
                evaluatedAtUtc);
        }

        // If any dimension matched an explicit non-applicable category and no contradictory required-trigger
        // needs override, return NotRequired strictly per the approved policy predicate (never asserting statutory immunity).
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
                $"Evaluated NotRequired against configured policy '{policyCode}' v{policy.Version}.",
                evaluatedAtUtc);
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
                evaluatedAtUtc);
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
            $"Matched applicability predicates for policy '{policyCode}' v{policy.Version}.",
            evaluatedAtUtc);
    }

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
