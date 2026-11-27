using System.Text.RegularExpressions;

namespace Warehouse3PL.Domain.Entities.Reference;

/// <summary>
/// Fail-closed allowlist sanitizer for country normalization diagnostics, exports, and HTTP DTOs.
/// Only verified active reference catalog tokens (ISO alpha-2, ISO alpha-3, catalog country names,
/// and active <see cref="CountryNormalizationAlias"/> keys) or blank values are exposed;
/// all unrecognized arbitrary free-text values (names, confidential notes, emails, phones, addresses)
/// are redacted by default.
/// </summary>
public static partial class CountryDiagnosticSanitizer
{
    public const string RedactedPlaceholder = "[REDACTED_POTENTIAL_PII]";
    public const string DefaultQuarantineReason = "Unrecognized or unverified country value quarantined for steward review.";

    private static readonly string[] SafeReasonPrefixes =
    [
        "Normalized to canonical ",
        "Deterministically inferred ",
        "Immutable historical record preserved untouched",
        "Unrecognized country token",
        "Unrecognized or unverified country value",
        "Blank/null country",
        "Ambiguous subdivision",
        "Unverified subdivision",
        "Conflicting geographic evidence:",
        "Conflicting timezone evidence:",
        "Conflicting evidence:",
        "Orphan or unverified blank country",
        "Unsupported or conflicting postal code syntax",
        "Quarantined or unverified customer master",
        "Quarantined original order shipping address",
        "Cross-border return shipment",
        "Cross-border order context",
        "Country normalization evaluation pending",
        "Runtime write recorded pending normalization",
        "Runtime write reconciled to canonical",
        "Historical country value restored",
        "Ambiguous historical restoration provenance",
        "Historical blank country inference preserved"
    ];

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
    /// Allowlists predefined safe diagnostic reasons and rejects any untrusted free-text reason
    /// that could echo PII or unverified user input.
    /// </summary>
    public static string SanitizeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return "Country normalization evaluation pending.";
        }

        var collapsed = CollapseWhitespace(reason);
        if (ContainsPotentialPii(collapsed))
        {
            return DefaultQuarantineReason;
        }

        bool isAllowed = SafeReasonPrefixes.Any(p => collapsed.StartsWith(p, StringComparison.Ordinal));
        if (!isAllowed)
        {
            return DefaultQuarantineReason;
        }

        return collapsed.Length > 500 ? collapsed[..500] : collapsed;
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
