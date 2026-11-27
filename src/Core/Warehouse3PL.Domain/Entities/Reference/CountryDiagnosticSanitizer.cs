using System.Text.RegularExpressions;

namespace Warehouse3PL.Domain.Entities.Reference;

public sealed record CountryDiagnosticReason(string Code, string Description);

/// <summary>
/// Fail-closed allowlist sanitizer for country normalization diagnostics, exports, and HTTP DTOs.
/// Only verified active reference catalog tokens (ISO alpha-2, ISO alpha-3, catalog country names,
/// and active <see cref="CountryNormalizationAlias"/> keys) or blank values are exposed;
/// all unrecognized arbitrary free-text values (names, confidential notes, emails, phones, addresses)
/// are redacted by default. Diagnostic reasons are mapped strictly to predefined controlled reason
/// codes and fixed descriptions without returning arbitrary suffix text or interpolated input.
/// </summary>
public static partial class CountryDiagnosticSanitizer
{
    public const string RedactedPlaceholder = "[REDACTED_POTENTIAL_PII]";

    public static readonly CountryDiagnosticReason NormalizedCanonical = new(
        "NORMALIZED_CANONICAL",
        "Normalized to canonical ISO 3166-1 country code.");

    public static readonly CountryDiagnosticReason InferredFromParent = new(
        "INFERRED_FROM_PARENT",
        "Deterministically inferred canonical ISO 3166-1 country code from verified parent context.");

    public static readonly CountryDiagnosticReason HistoricalPreserved = new(
        "HISTORICAL_PRESERVED",
        "Immutable historical record preserved untouched.");

    public static readonly CountryDiagnosticReason HistoricalRestored = new(
        "HISTORICAL_RESTORED",
        "Historical country value restored after verifying pre-remediation provenance.");

    public static readonly CountryDiagnosticReason HistoricalBlankInferencePreserved = new(
        "HISTORICAL_BLANK_INFERENCE_PRESERVED",
        "Historical blank country inference preserved without unverified restoration.");

    public static readonly CountryDiagnosticReason HistoricalProvenanceReview = new(
        "HISTORICAL_PROVENANCE_REVIEW",
        "Historical provenance requires steward review; current value preserved.");

    public static readonly CountryDiagnosticReason RuntimeNormalized = new(
        "RUNTIME_NORMALIZED",
        "Runtime write reconciled to canonical ISO 3166-1 country code.");

    public static readonly CountryDiagnosticReason RuntimePending = new(
        "RUNTIME_PENDING",
        "Runtime write quarantined pending country normalization review.");

    public static readonly CountryDiagnosticReason UnrecognizedCountry = new(
        "UNRECOGNIZED_COUNTRY",
        "Unrecognized or unverified country value quarantined for steward review.");

    public static readonly CountryDiagnosticReason BlankCountryUnverified = new(
        "BLANK_COUNTRY_UNVERIFIED",
        "Blank or missing country value could not be deterministically verified.");

    public static readonly CountryDiagnosticReason AmbiguousSubdivision = new(
        "AMBIGUOUS_SUBDIVISION",
        "Ambiguous subdivision requires manual country confirmation.");

    public static readonly CountryDiagnosticReason UnverifiedSubdivision = new(
        "UNVERIFIED_SUBDIVISION",
        "Unverified subdivision requires manual country confirmation.");

    public static readonly CountryDiagnosticReason ConflictingGeographicEvidence = new(
        "CONFLICTING_GEOGRAPHIC_EVIDENCE",
        "Conflicting geographic evidence quarantined for steward review.");

    public static readonly CountryDiagnosticReason ConflictingTimezoneEvidence = new(
        "CONFLICTING_TIMEZONE_EVIDENCE",
        "Conflicting timezone evidence quarantined for steward review.");

    public static readonly CountryDiagnosticReason ConflictingPostalSyntax = new(
        "CONFLICTING_POSTAL_SYNTAX",
        "Unsupported or conflicting postal code syntax quarantined for steward review.");

    public static readonly CountryDiagnosticReason QuarantinedCustomerParent = new(
        "QUARANTINED_CUSTOMER_PARENT",
        "Quarantined or unverified customer master cannot verify child address country.");

    public static readonly CountryDiagnosticReason QuarantinedOrderParent = new(
        "QUARANTINED_ORDER_PARENT",
        "Quarantined original order shipping address cannot verify return country.");

    public static readonly CountryDiagnosticReason CrossBorderReturn = new(
        "CROSS_BORDER_RETURN",
        "Cross-border return shipment requires explicit country confirmation.");

    public static readonly CountryDiagnosticReason CrossBorderOrder = new(
        "CROSS_BORDER_ORDER",
        "Cross-border order context requires explicit country confirmation.");

    public static readonly CountryDiagnosticReason EvaluationPending = new(
        "EVALUATION_PENDING",
        "Country normalization evaluation pending.");

    public static string DefaultQuarantineReason => UnrecognizedCountry.Description;

    public static IReadOnlyList<CountryDiagnosticReason> Catalog { get; } =
    [
        NormalizedCanonical,
        InferredFromParent,
        HistoricalPreserved,
        HistoricalRestored,
        HistoricalBlankInferencePreserved,
        HistoricalProvenanceReview,
        RuntimeNormalized,
        RuntimePending,
        UnrecognizedCountry,
        BlankCountryUnverified,
        AmbiguousSubdivision,
        UnverifiedSubdivision,
        ConflictingGeographicEvidence,
        ConflictingTimezoneEvidence,
        ConflictingPostalSyntax,
        QuarantinedCustomerParent,
        QuarantinedOrderParent,
        CrossBorderReturn,
        CrossBorderOrder,
        EvaluationPending
    ];

    private static readonly Dictionary<string, CountryDiagnosticReason> ReasonsByCode =
        Catalog.ToDictionary(r => r.Code, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, CountryDiagnosticReason> ReasonsByDescription =
        Catalog.ToDictionary(r => r.Description, StringComparer.OrdinalIgnoreCase);

    public static HashSet<string> BuildVerifiedCatalogKeySet(
        IEnumerable<CountryProfile> activeProfiles,
        IEnumerable<CountryNormalizationAlias> activeAliases)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var activeCountryCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in activeProfiles)
        {
            if (!profile.IsActive) continue;

            var code = CountryNormalizationAlias.NormalizeLookupKey(profile.CountryCode);
            if (!string.IsNullOrEmpty(code))
            {
                keys.Add(code);
                activeCountryCodes.Add(code);
            }

            var alpha3 = CountryNormalizationAlias.NormalizeLookupKey(profile.IsoAlpha3Code);
            if (!string.IsNullOrEmpty(alpha3))
            {
                keys.Add(alpha3);
            }

            var name = CountryNormalizationAlias.NormalizeLookupKey(profile.Name);
            if (!string.IsNullOrEmpty(name))
            {
                keys.Add(name);
            }
        }

        foreach (var alias in activeAliases)
        {
            if (!alias.IsActive) continue;
            var targetCode = CountryNormalizationAlias.NormalizeLookupKey(alias.CountryCode);
            if (!activeCountryCodes.Contains(targetCode)) continue;

            var aliasKey = CountryNormalizationAlias.NormalizeLookupKey(alias.AliasKey);
            if (!string.IsNullOrEmpty(aliasKey))
            {
                keys.Add(aliasKey);
            }
        }

        return keys;
    }

    public static bool ContainsPotentialPii(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var collapsed = CollapseWhitespace(rawValue);
        if (collapsed.Length > 64)
        {
            return true;
        }

        if (collapsed.Contains('@') ||
            collapsed.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
            collapsed.Contains("https://", StringComparison.OrdinalIgnoreCase) ||
            collapsed.Contains("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (HasControlChars(rawValue))
        {
            return true;
        }

        if (FiveOrMoreDigitsRegex().IsMatch(collapsed) || PhoneLikePatternRegex().IsMatch(collapsed))
        {
            return true;
        }

        if (StreetAddressIndicatorRegex().IsMatch(collapsed))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Fail-closed allowlist sanitization: returns only blank/null or a verified active catalog token;
    /// redacts all unrecognized arbitrary free-text values by default.
    /// </summary>
    public static string? SanitizeForDiagnostic(string? rawValue, IReadOnlySet<string> verifiedCatalogKeys)
    {
        if (rawValue is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return string.Empty;
        }

        var collapsed = CollapseWhitespace(rawValue);
        if (string.Equals(collapsed, RedactedPlaceholder, StringComparison.Ordinal))
        {
            return RedactedPlaceholder;
        }

        if (ContainsPotentialPii(rawValue))
        {
            return RedactedPlaceholder;
        }

        var lookupKey = CountryNormalizationAlias.NormalizeLookupKey(collapsed);
        if (string.IsNullOrEmpty(lookupKey) || !verifiedCatalogKeys.Contains(lookupKey))
        {
            return RedactedPlaceholder;
        }

        return collapsed.Length > 120 ? collapsed[..120] : collapsed;
    }

    /// <summary>
    /// Strict fallback sanitizer when no catalog set is provided: only allows exact 2-letter uppercase
    /// ISO code syntax or blank/null; redacts all other text fail-closed.
    /// </summary>
    public static string? SanitizeRawValue(string? rawValue)
    {
        if (rawValue is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return string.Empty;
        }

        var collapsed = CollapseWhitespace(rawValue);
        if (string.Equals(collapsed, RedactedPlaceholder, StringComparison.Ordinal))
        {
            return RedactedPlaceholder;
        }

        if (collapsed.Length == 2 && collapsed.All(char.IsAsciiLetterUpper))
        {
            return collapsed;
        }

        return RedactedPlaceholder;
    }

    /// <summary>
    /// Conservatively maps any runtime or legacy persisted reason string to a closed, predefined
    /// <see cref="CountryDiagnosticReason"/>. Never returns arbitrary suffix text or interpolated raw input.
    /// </summary>
    public static CountryDiagnosticReason ClassifyReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return EvaluationPending;
        }

        var collapsed = CollapseWhitespace(reason);
        if (ReasonsByCode.TryGetValue(collapsed, out var byCode))
        {
            return byCode;
        }

        if (ReasonsByDescription.TryGetValue(collapsed, out var byDesc))
        {
            return byDesc;
        }

        if (collapsed.StartsWith("Historical blank country inference preserved", StringComparison.OrdinalIgnoreCase))
            return HistoricalBlankInferencePreserved;
        if (collapsed.StartsWith("Historical country value restored", StringComparison.OrdinalIgnoreCase))
            return HistoricalRestored;
        if (collapsed.StartsWith("Ambiguous historical restoration provenance", StringComparison.OrdinalIgnoreCase) ||
            collapsed.StartsWith("Historical provenance requires steward review", StringComparison.OrdinalIgnoreCase))
            return HistoricalProvenanceReview;
        if (collapsed.StartsWith("Immutable historical record preserved untouched", StringComparison.OrdinalIgnoreCase))
            return HistoricalPreserved;
        if (collapsed.StartsWith("Runtime write reconciled to canonical", StringComparison.OrdinalIgnoreCase))
            return RuntimeNormalized;
        if (collapsed.StartsWith("Runtime write recorded pending normalization", StringComparison.OrdinalIgnoreCase) ||
            collapsed.StartsWith("Runtime write quarantined pending", StringComparison.OrdinalIgnoreCase))
            return RuntimePending;
        if (collapsed.StartsWith("Deterministically inferred", StringComparison.OrdinalIgnoreCase))
            return InferredFromParent;
        if (collapsed.StartsWith("Normalized to canonical", StringComparison.OrdinalIgnoreCase) ||
            collapsed.StartsWith("Resolved to ", StringComparison.OrdinalIgnoreCase) ||
            collapsed.StartsWith("Mapped ", StringComparison.OrdinalIgnoreCase))
            return NormalizedCanonical;
        if (collapsed.StartsWith("Conflicting timezone evidence", StringComparison.OrdinalIgnoreCase))
            return ConflictingTimezoneEvidence;
        if (collapsed.StartsWith("Conflicting geographic evidence", StringComparison.OrdinalIgnoreCase) ||
            collapsed.StartsWith("Conflicting evidence", StringComparison.OrdinalIgnoreCase))
            return ConflictingGeographicEvidence;
        if (collapsed.StartsWith("Unsupported or conflicting postal code syntax", StringComparison.OrdinalIgnoreCase))
            return ConflictingPostalSyntax;
        if (collapsed.StartsWith("Ambiguous subdivision", StringComparison.OrdinalIgnoreCase))
            return AmbiguousSubdivision;
        if (collapsed.StartsWith("Unverified subdivision", StringComparison.OrdinalIgnoreCase))
            return UnverifiedSubdivision;
        if (collapsed.StartsWith("Quarantined or unverified customer master", StringComparison.OrdinalIgnoreCase))
            return QuarantinedCustomerParent;
        if (collapsed.StartsWith("Quarantined original order shipping address", StringComparison.OrdinalIgnoreCase))
            return QuarantinedOrderParent;
        if (collapsed.StartsWith("Cross-border return shipment", StringComparison.OrdinalIgnoreCase))
            return CrossBorderReturn;
        if (collapsed.StartsWith("Cross-border order context", StringComparison.OrdinalIgnoreCase))
            return CrossBorderOrder;
        if (collapsed.StartsWith("Blank/null country", StringComparison.OrdinalIgnoreCase) ||
            collapsed.StartsWith("Blank or null country", StringComparison.OrdinalIgnoreCase) ||
            collapsed.StartsWith("Blank or missing country", StringComparison.OrdinalIgnoreCase) ||
            collapsed.StartsWith("Orphan or unverified blank country", StringComparison.OrdinalIgnoreCase))
            return BlankCountryUnverified;
        if (collapsed.StartsWith("Country normalization evaluation pending", StringComparison.OrdinalIgnoreCase))
            return EvaluationPending;

        return UnrecognizedCountry;
    }

    public static string SanitizeReason(string? reason) => ClassifyReason(reason).Description;

    public static string GetReasonCode(string? reason) => ClassifyReason(reason).Code;

    /// <summary>
    /// Returns the exact controlled descriptions from <see cref="Catalog"/> whose predefined code
    /// or controlled description matches <paramref name="searchTerm"/> (case-insensitive).
    /// </summary>
    public static List<string> MatchControlledDescriptions(string? searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return [];
        }

        var term = searchTerm.Trim();
        var matches = new List<string>();
        foreach (var entry in Catalog)
        {
            if (entry.Code.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                entry.Description.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(entry.Description);
            }
        }

        return matches;
    }

    private static string CollapseWhitespace(string input) =>
        MultiWhitespaceRegex().Replace(input.Replace('\r', ' ').Replace('\n', ' ').Trim(), " ");

    private static bool HasControlChars(string input) =>
        input.Any(c => char.IsControl(c) && c != ' ');

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiWhitespaceRegex();

    [GeneratedRegex(@"(?:\D*\d){5,}")]
    private static partial Regex FiveOrMoreDigitsRegex();

    [GeneratedRegex(@"\+?\d[\d\s\-\(\)]{5,}\d")]
    private static partial Regex PhoneLikePatternRegex();

    [GeneratedRegex(@"\b\d+\s+[A-Za-z].*\b(st|street|ave|avenue|rd|road|blvd|boulevard|dr|drive|ln|lane|apt|suite|box)\b", RegexOptions.IgnoreCase)]
    private static partial Regex StreetAddressIndicatorRegex();
}
