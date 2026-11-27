using System.Text.RegularExpressions;

namespace Warehouse3PL.Domain.Entities.Reference;

/// <summary>
/// Sanitizes and redacts potential PII or untrusted control characters from legacy free-text
/// country fields and diagnostic reasons prior to audit persistence and HTTP DTO serialization.
/// </summary>
public static partial class CountryDiagnosticSanitizer
{
    public const string RedactedPlaceholder = "[REDACTED_POTENTIAL_PII]";

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

        // Reject strings with 5+ digits (phones, SSNs, postal+street combinations) while allowing 3-digit ISO numeric codes (e.g., 840, 524).
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

        if (ContainsPotentialPii(rawValue))
        {
            return RedactedPlaceholder;
        }

        var collapsed = CollapseWhitespace(rawValue);
        return collapsed.Length > 120 ? collapsed[..120] : collapsed;
    }

    public static string SanitizeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return "Country normalization evaluation pending.";
        }

        var collapsed = CollapseWhitespace(reason);
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
