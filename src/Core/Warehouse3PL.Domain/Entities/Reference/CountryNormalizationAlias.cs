using System.Text.RegularExpressions;

namespace Warehouse3PL.Domain.Entities.Reference;

/// <summary>
/// Verified canonical alias mapping an exact normalized country label/abbreviation to an active
/// <see cref="CountryProfile"/> ISO 3166-1 alpha-2 code. Supports zero fuzzy matching or default fallbacks.
/// </summary>
public partial class CountryNormalizationAlias
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AliasKey { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public string SourceStandard { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public CountryProfile CountryProfile { get; set; } = null!;

    public static string NormalizeLookupKey(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return string.Empty;
        }

        var collapsed = MultiWhitespaceRegex().Replace(rawValue.Trim(), " ").ToUpperInvariant();
        if (DottedAcronymRegex().IsMatch(collapsed.Replace(" ", string.Empty)))
        {
            return collapsed.Replace(" ", string.Empty).Replace(".", string.Empty);
        }

        return collapsed;
    }

    public static CountryNormalizationAlias Create(
        string aliasKey,
        string countryCode,
        string sourceStandard,
        Guid? id = null)
    {
        var normAlias = NormalizeLookupKey(aliasKey);
        var normCode = (countryCode ?? string.Empty).Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(normAlias))
        {
            throw new ArgumentException("AliasKey is required.", nameof(aliasKey));
        }

        if (normCode.Length != 2 || !normCode.All(char.IsAsciiLetterUpper))
        {
            throw new ArgumentException("CountryCode must be a 2-letter uppercase ISO 3166-1 alpha-2 code.", nameof(countryCode));
        }

        return new CountryNormalizationAlias
        {
            Id = id ?? Guid.NewGuid(),
            AliasKey = normAlias,
            CountryCode = normCode,
            SourceStandard = string.IsNullOrWhiteSpace(sourceStandard) ? "ISO-3166-1" : sourceStandard.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex MultiWhitespaceRegex();

    [GeneratedRegex(@"^([A-Z]\.){2,}$", RegexOptions.CultureInvariant)]
    private static partial Regex DottedAcronymRegex();
}
