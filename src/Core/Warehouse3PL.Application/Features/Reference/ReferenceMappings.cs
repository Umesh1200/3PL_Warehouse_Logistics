using System.Text.Json;
using System.Text.Json.Serialization;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Reference;

namespace Warehouse3PL.Application.Features.Reference;

public static class ReferenceMappings
{
    private static readonly JsonSerializerOptions PredicateJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static CountryProfileDto ToDto(this CountryProfile entity) =>
        new(
            entity.CountryCode,
            entity.IsoAlpha3Code,
            entity.NumericCode,
            entity.Name,
            entity.DefaultCurrencyCode,
            entity.PrimaryTimeZoneId,
            entity.AddressSchemaType,
            entity.PostalCodeRegex,
            entity.LegacyPostalCodeRegex,
            entity.PrimaryCalendarSystem,
            entity.FiscalCalendarSystem,
            entity.DefaultFiscalYearStartMonth,
            entity.IsActive,
            entity.SeedVersion);

    public static AdministrativeDivisionDto ToDto(this AdministrativeDivision entity) =>
        new(
            entity.Id,
            entity.CountryCode,
            entity.ParentDivisionId,
            entity.DivisionLevel,
            entity.IsoCode,
            entity.Code,
            entity.Name,
            entity.LocalName,
            entity.PostalPrefix,
            entity.IsActive);

    public static CountryCompliancePolicyDto ToDto(this CountryCompliancePolicy entity) =>
        new(
            entity.Id,
            entity.CountryCode,
            entity.PolicyCode,
            entity.Version,
            entity.Title,
            entity.StatutorySourceRef,
            entity.OfficialNoticeCitation,
            entity.ApplicabilityPredicateJson,
            entity.EnforcementMode,
            entity.EffectiveFromUtc,
            entity.EffectiveToUtc,
            entity.ApprovalStatus,
            entity.ApprovedByUserId,
            entity.ApprovedAtUtc,
            entity.VerificationEvidenceNotes,
            entity.SupersededByPolicyId,
            entity.SupersededAtUtc,
            entity.ConfigRevision,
            entity.IsActive,
            entity.CreatedAt,
            entity.UpdatedAt);

    public static bool TryParsePredicate(string? json, out ComplianceApplicabilityPredicate? predicate, out string? error)
    {
        predicate = null;
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "ApplicabilityPredicateJson cannot be empty.";
            return false;
        }

        try
        {
            predicate = JsonSerializer.Deserialize<ComplianceApplicabilityPredicate>(json, PredicateJsonOptions);
            if (predicate == null || !predicate.HasAnyRuleCriterion())
            {
                predicate = null;
                error = "ApplicabilityPredicateJson must declare at least one applicability criterion (business classification, order category, cargo class, or taxpayer mandate flag).";
                return false;
            }

            return true;
        }
        catch (JsonException ex)
        {
            error = $"ApplicabilityPredicateJson is invalid JSON: {ex.Message}";
            return false;
        }
    }

    public static string SerializePredicate(ComplianceApplicabilityPredicate predicate) =>
        JsonSerializer.Serialize(predicate, PredicateJsonOptions);
}
