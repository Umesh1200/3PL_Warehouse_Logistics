using System.Text.RegularExpressions;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.ValueObjects;

namespace Warehouse3PL.Domain.Entities.Reference;

/// <summary>
/// Authoritative, immutable ISO 3166-1 country localization and statutory default profile.
/// Permits zero tenant overrides and zero runtime mutation endpoints.
/// </summary>
public class CountryProfile
{
    public string CountryCode { get; set; } = string.Empty;
    public string IsoAlpha3Code { get; set; } = string.Empty;
    public string NumericCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DefaultCurrencyCode { get; set; } = string.Empty;
    public string PrimaryTimeZoneId { get; set; } = string.Empty;
    public AddressSchemaType AddressSchemaType { get; set; }

    /// <summary>
    /// Syntactic regular expression for current national postal codes (format check only;
    /// matching does NOT certify physical address or delivery-point existence).
    /// </summary>
    public string PostalCodeRegex { get; set; } = string.Empty;

    /// <summary>
    /// Optional syntactic regular expression supporting recognized legacy postal formats
    /// (for example, legacy 5-digit Nepal GPO postal codes alongside 7-digit locality formats).
    /// </summary>
    public string? LegacyPostalCodeRegex { get; set; }

    public PrimaryCalendarSystem PrimaryCalendarSystem { get; set; }
    public FiscalCalendarSystem FiscalCalendarSystem { get; set; }

    /// <summary>
    /// 1-based month number within <see cref="FiscalCalendarSystem"/> marking fiscal year start
    /// (e.g., 4 = Shrawan in BikramSambat for Nepal; 1 = January in Gregorian default for US).
    /// </summary>
    public int DefaultFiscalYearStartMonth { get; set; } = 1;

    public bool IsActive { get; set; } = true;
    public int SeedVersion { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public ICollection<AdministrativeDivision> Divisions { get; set; } = new List<AdministrativeDivision>();
    public ICollection<CountryCompliancePolicy> CompliancePolicies { get; set; } = new List<CountryCompliancePolicy>();

    /// <summary>
    /// Validates postal code syntax only against <see cref="PostalCodeRegex"/> and <see cref="LegacyPostalCodeRegex"/>.
    /// Does not attest physical address existence or carrier serviceability.
    /// </summary>
    public bool IsSyntacticallyValidPostalCode(string? postalCode, out bool matchedLegacyFormat)
    {
        matchedLegacyFormat = false;
        if (string.IsNullOrWhiteSpace(postalCode))
        {
            return false;
        }

        var trimmed = postalCode.Trim();
        if (!string.IsNullOrWhiteSpace(PostalCodeRegex) &&
            Regex.IsMatch(trimmed, PostalCodeRegex, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(LegacyPostalCodeRegex) &&
            Regex.IsMatch(trimmed, LegacyPostalCodeRegex, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
        {
            matchedLegacyFormat = true;
            return true;
        }

        return false;
    }

    public static CountryProfile Create(
        string countryCode,
        string isoAlpha3Code,
        string numericCode,
        string name,
        string defaultCurrencyCode,
        string primaryTimeZoneId,
        AddressSchemaType addressSchemaType,
        string postalCodeRegex,
        string? legacyPostalCodeRegex,
        PrimaryCalendarSystem primaryCalendarSystem,
        FiscalCalendarSystem fiscalCalendarSystem,
        int defaultFiscalYearStartMonth,
        int seedVersion = 1,
        bool isActive = true)
    {
        var code = (countryCode ?? string.Empty).Trim().ToUpperInvariant();
        var alpha3 = (isoAlpha3Code ?? string.Empty).Trim().ToUpperInvariant();
        var num = (numericCode ?? string.Empty).Trim();

        if (code.Length != 2 || !code.All(char.IsAsciiLetterUpper))
            throw new ArgumentException("CountryCode must be a 2-letter uppercase ISO 3166-1 alpha-2 code.", nameof(countryCode));
        if (alpha3.Length != 3 || !alpha3.All(char.IsAsciiLetterUpper))
            throw new ArgumentException("IsoAlpha3Code must be a 3-letter uppercase ISO 3166-1 alpha-3 code.", nameof(isoAlpha3Code));
        if (num.Length != 3 || !num.All(char.IsAsciiDigit))
            throw new ArgumentException("NumericCode must be a 3-digit ISO 3166-1 numeric code.", nameof(numericCode));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(primaryTimeZoneId))
            throw new ArgumentException("PrimaryTimeZoneId is required.", nameof(primaryTimeZoneId));
        if (defaultFiscalYearStartMonth is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(defaultFiscalYearStartMonth), "Fiscal year start month must be between 1 and 12.");

        Iso4217Currencies.Validate(defaultCurrencyCode, nameof(defaultCurrencyCode));

        return new CountryProfile
        {
            CountryCode = code,
            IsoAlpha3Code = alpha3,
            NumericCode = num,
            Name = name.Trim(),
            DefaultCurrencyCode = defaultCurrencyCode.Trim().ToUpperInvariant(),
            PrimaryTimeZoneId = primaryTimeZoneId.Trim(),
            AddressSchemaType = addressSchemaType,
            PostalCodeRegex = postalCodeRegex.Trim(),
            LegacyPostalCodeRegex = string.IsNullOrWhiteSpace(legacyPostalCodeRegex) ? null : legacyPostalCodeRegex.Trim(),
            PrimaryCalendarSystem = primaryCalendarSystem,
            FiscalCalendarSystem = fiscalCalendarSystem,
            DefaultFiscalYearStartMonth = defaultFiscalYearStartMonth,
            SeedVersion = seedVersion,
            IsActive = isActive
        };
    }
}
