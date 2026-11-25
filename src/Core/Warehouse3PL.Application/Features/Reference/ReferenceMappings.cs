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
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private static readonly HashSet<string> PredicateArrayPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(ComplianceApplicabilityPredicate.RequiredBusinessClassifications),
        nameof(ComplianceApplicabilityPredicate.ExemptBusinessClassifications),
        nameof(ComplianceApplicabilityPredicate.RequiredOrderCategories),
        nameof(ComplianceApplicabilityPredicate.ExemptOrderCategories),
        nameof(ComplianceApplicabilityPredicate.RequiredCargoClasses),
        nameof(ComplianceApplicabilityPredicate.ExemptCargoClasses)
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
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "ApplicabilityPredicateJson must be a JSON object.";
                return false;
            }

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (PredicateArrayPropertyNames.Contains(prop.Name))
                {
                    if (prop.Value.ValueKind != JsonValueKind.Array)
                    {
                        error = $"Predicate property '{prop.Name}' must be a non-null JSON array of strings.";
                        return false;
                    }

                    foreach (var elem in prop.Value.EnumerateArray())
                    {
                        if (elem.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(elem.GetString()))
                        {
                            error = $"Predicate array '{prop.Name}' must contain only non-empty string values.";
                            return false;
                        }
                    }
                }
            }

            var parsed = JsonSerializer.Deserialize<ComplianceApplicabilityPredicate>(json, PredicateJsonOptions);
            if (parsed == null)
            {
                error = "ApplicabilityPredicateJson deserialized to null.";
                return false;
            }

            if (parsed.RequiredBusinessClassifications is null ||
                parsed.ExemptBusinessClassifications is null ||
                parsed.RequiredOrderCategories is null ||
                parsed.ExemptOrderCategories is null ||
                parsed.RequiredCargoClasses is null ||
                parsed.ExemptCargoClasses is null)
            {
                error = "Predicate classification/category arrays cannot be null.";
                return false;
            }

            if (!parsed.HasAnyRuleCriterion())
            {
                error = "ApplicabilityPredicateJson must declare at least one applicability criterion (business classification, order category, cargo class, or taxpayer mandate flag).";
                return false;
            }

            if (HasContradiction(parsed.RequiredBusinessClassifications, parsed.ExemptBusinessClassifications, "BusinessClassification", out var contradictionErr) ||
                HasContradiction(parsed.RequiredOrderCategories, parsed.ExemptOrderCategories, "OrderCategory", out contradictionErr) ||
                HasContradiction(parsed.RequiredCargoClasses, parsed.ExemptCargoClasses, "CargoClass", out contradictionErr))
            {
                error = contradictionErr;
                return false;
            }

            predicate = parsed;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"ApplicabilityPredicateJson is invalid JSON: {ex.Message}";
            return false;
        }
    }

    private static bool HasContradiction(
        IReadOnlyList<string> required,
        IReadOnlyList<string> exempt,
        string dimension,
        out string? error)
    {
        var reqSet = required.Select(v => v.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var overlap = exempt.Select(v => v.Trim()).FirstOrDefault(reqSet.Contains);
        if (overlap != null)
        {
            error = $"Predicate '{dimension}' contains contradictory value '{overlap}' in both required and exempt lists.";
            return true;
        }

        error = null;
        return false;
    }

    public static string SerializePredicate(ComplianceApplicabilityPredicate predicate) =>
        JsonSerializer.Serialize(predicate, PredicateJsonOptions);
}
