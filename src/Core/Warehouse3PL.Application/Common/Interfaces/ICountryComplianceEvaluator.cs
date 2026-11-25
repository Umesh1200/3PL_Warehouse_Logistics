using Warehouse3PL.Application.Common.Models;

namespace Warehouse3PL.Application.Common.Interfaces;

/// <summary>
/// Evaluates country statutory compliance applicability strictly from VerifiedApproved, active policies.
/// Returns a typed 3-state outcome (Required, NotRequired, Indeterminate) and never silently coerces
/// missing policies or ambiguous inputs to NotRequired.
/// </summary>
public interface ICountryComplianceEvaluator
{
    Task<ComplianceEvaluationResult> EvaluateAsync(
        ComplianceEvaluationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Computes dynamic UTC offsets from authoritative IANA tzdb identifiers at runtime instants.
/// </summary>
public interface ITimeZoneOffsetResolver
{
    int GetUtcOffsetMinutes(string timeZoneId, DateTime utcInstant);
    bool TryValidateTimeZoneId(string timeZoneId);
}
