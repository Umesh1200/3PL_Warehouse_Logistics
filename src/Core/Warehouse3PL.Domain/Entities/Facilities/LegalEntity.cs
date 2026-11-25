using System.Text.RegularExpressions;
using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Reference;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Domain.ValueObjects;

namespace Warehouse3PL.Domain.Entities.Facilities;

/// <summary>
/// Issuing corporate / statutory legal entity distinct from physical warehouse facilities.
/// Owns registered seller name, country, tax registration (PAN/VAT/EIN), and CBMS mandate metadata,
/// while physical warehouse Facility records retain authoritative ship-from dock addresses.
/// </summary>
public class LegalEntity : AuditableEntity<Guid>, ISoftDeletable
{
    private static readonly Regex NepalNineDigitTaxRegex = new(@"^\d{9}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex UsEinRegex = new(@"^\d{2}-?\d{7}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex GeneralTaxIdRegex = new(@"^[A-Za-z0-9\-]{4,50}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public string LegalCode { get; set; } = string.Empty;
    public string LegalName { get; set; } = string.Empty;
    public string? RegistrationNumber { get; set; }
    public string CountryCode { get; set; } = string.Empty;
    public TaxIdentifierType TaxIdentifierType { get; set; }
    public string TaxRegistrationNumber { get; set; } = string.Empty;

    // Registered corporate headquarters / tax authority address (distinct from physical Facility dock)
    public string RegisteredAddressLine1 { get; set; } = string.Empty;
    public string? RegisteredAddressLine2 { get; set; }
    public string RegisteredCity { get; set; } = string.Empty;
    public string? RegisteredStateOrProvince { get; set; }
    public string? RegisteredPostalCode { get; set; }

    public bool IsCbmsMandated { get; set; }
    public string? IroSoftwarePermitRef { get; set; }
    public string DefaultCurrencyCode { get; set; } = string.Empty;

    public long ConfigRevision { get; set; } = 1;
    public bool IsActive { get; set; } = true;

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    public CountryProfile CountryProfile { get; set; } = null!;
    public ICollection<Facility> DefaultFacilities { get; set; } = [];

    public LegalEntity()
    {
        Id = Guid.NewGuid();
    }

    public static LegalEntity Create(
        string legalCode,
        string legalName,
        string? registrationNumber,
        string countryCode,
        TaxIdentifierType taxIdentifierType,
        string taxRegistrationNumber,
        string registeredAddressLine1,
        string? registeredAddressLine2,
        string registeredCity,
        string? registeredStateOrProvince,
        string? registeredPostalCode,
        bool isCbmsMandated,
        string? iroSoftwarePermitRef,
        string defaultCurrencyCode,
        Guid? id = null)
    {
        var normalizedCode = NormalizeLegalCode(legalCode);
        var normalizedCountry = NormalizeCountryCode(countryCode);
        var normalizedCurrency = NormalizeAndValidateCurrency(defaultCurrencyCode);
        var normalizedTaxNumber = ValidateTaxRegistration(normalizedCountry, taxIdentifierType, taxRegistrationNumber);
        ValidateRequiredAddress(legalName, registeredAddressLine1, registeredCity);

        var entity = new LegalEntity
        {
            LegalCode = normalizedCode,
            LegalName = legalName.Trim(),
            RegistrationNumber = string.IsNullOrWhiteSpace(registrationNumber) ? null : registrationNumber.Trim(),
            CountryCode = normalizedCountry,
            TaxIdentifierType = taxIdentifierType,
            TaxRegistrationNumber = normalizedTaxNumber,
            RegisteredAddressLine1 = registeredAddressLine1.Trim(),
            RegisteredAddressLine2 = string.IsNullOrWhiteSpace(registeredAddressLine2) ? null : registeredAddressLine2.Trim(),
            RegisteredCity = registeredCity.Trim(),
            RegisteredStateOrProvince = string.IsNullOrWhiteSpace(registeredStateOrProvince) ? null : registeredStateOrProvince.Trim(),
            RegisteredPostalCode = string.IsNullOrWhiteSpace(registeredPostalCode) ? null : registeredPostalCode.Trim(),
            IsCbmsMandated = isCbmsMandated,
            IroSoftwarePermitRef = string.IsNullOrWhiteSpace(iroSoftwarePermitRef) ? null : iroSoftwarePermitRef.Trim(),
            DefaultCurrencyCode = normalizedCurrency,
            ConfigRevision = 1,
            IsActive = true
        };

        if (id.HasValue && id.Value != Guid.Empty)
        {
            entity.Id = id.Value;
        }

        return entity;
    }

    public void UpdateDetails(
        string legalName,
        string? registrationNumber,
        string countryCode,
        TaxIdentifierType taxIdentifierType,
        string taxRegistrationNumber,
        string registeredAddressLine1,
        string? registeredAddressLine2,
        string registeredCity,
        string? registeredStateOrProvince,
        string? registeredPostalCode,
        bool isCbmsMandated,
        string? iroSoftwarePermitRef,
        string defaultCurrencyCode)
    {
        var normalizedCountry = NormalizeCountryCode(countryCode);
        var normalizedCurrency = NormalizeAndValidateCurrency(defaultCurrencyCode);
        var normalizedTaxNumber = ValidateTaxRegistration(normalizedCountry, taxIdentifierType, taxRegistrationNumber);
        ValidateRequiredAddress(legalName, registeredAddressLine1, registeredCity);

        LegalName = legalName.Trim();
        RegistrationNumber = string.IsNullOrWhiteSpace(registrationNumber) ? null : registrationNumber.Trim();
        CountryCode = normalizedCountry;
        TaxIdentifierType = taxIdentifierType;
        TaxRegistrationNumber = normalizedTaxNumber;
        RegisteredAddressLine1 = registeredAddressLine1.Trim();
        RegisteredAddressLine2 = string.IsNullOrWhiteSpace(registeredAddressLine2) ? null : registeredAddressLine2.Trim();
        RegisteredCity = registeredCity.Trim();
        RegisteredStateOrProvince = string.IsNullOrWhiteSpace(registeredStateOrProvince) ? null : registeredStateOrProvince.Trim();
        RegisteredPostalCode = string.IsNullOrWhiteSpace(registeredPostalCode) ? null : registeredPostalCode.Trim();
        IsCbmsMandated = isCbmsMandated;
        IroSoftwarePermitRef = string.IsNullOrWhiteSpace(iroSoftwarePermitRef) ? null : iroSoftwarePermitRef.Trim();
        DefaultCurrencyCode = normalizedCurrency;
        ConfigRevision++;
    }

    public void SetActiveStatus(bool isActive)
    {
        if (IsActive == isActive)
        {
            return;
        }

        IsActive = isActive;
        ConfigRevision++;
    }

    private static string NormalizeLegalCode(string legalCode)
    {
        var trimmed = (legalCode ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 40)
        {
            throw new DomainException("LEGAL_ENTITY_INVALID_CODE", "LegalCode is required and must not exceed 40 characters.");
        }

        return trimmed;
    }

    private static string NormalizeCountryCode(string countryCode)
    {
        var trimmed = (countryCode ?? string.Empty).Trim().ToUpperInvariant();
        if (trimmed.Length != 2 || !trimmed.All(char.IsAsciiLetterUpper))
        {
            throw new DomainException("LEGAL_ENTITY_INVALID_COUNTRY", "CountryCode must be a 2-letter uppercase ISO 3166-1 alpha-2 code.");
        }

        return trimmed;
    }

    private static string NormalizeAndValidateCurrency(string currencyCode)
    {
        try
        {
            Iso4217Currencies.Validate(currencyCode, nameof(currencyCode));
            return currencyCode.Trim().ToUpperInvariant();
        }
        catch (ArgumentException ex)
        {
            throw new DomainException("LEGAL_ENTITY_INVALID_CURRENCY", ex.Message);
        }
    }

    private static string ValidateTaxRegistration(
        string normalizedCountryCode,
        TaxIdentifierType taxType,
        string taxRegistrationNumber)
    {
        var trimmed = (taxRegistrationNumber ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new DomainException("LEGAL_ENTITY_MISSING_TAX_ID", "TaxRegistrationNumber is required.");
        }

        if (normalizedCountryCode == "NP" &&
            (taxType == TaxIdentifierType.PAN || taxType == TaxIdentifierType.VAT))
        {
            if (!NepalNineDigitTaxRegex.IsMatch(trimmed))
            {
                throw new DomainException(
                    "LEGAL_ENTITY_INVALID_NP_TAX_ID",
                    $"Nepal {taxType} registration number '{trimmed}' must consist of exactly 9 digits.");
            }
        }
        else if (normalizedCountryCode == "US" && taxType == TaxIdentifierType.EIN)
        {
            if (!UsEinRegex.IsMatch(trimmed))
            {
                throw new DomainException(
                    "LEGAL_ENTITY_INVALID_US_EIN",
                    $"US EIN '{trimmed}' must follow 9-digit format (XX-XXXXXXX or XXXXXXXXX).");
            }
        }
        else if (!GeneralTaxIdRegex.IsMatch(trimmed))
        {
            throw new DomainException(
                "LEGAL_ENTITY_INVALID_TAX_ID",
                "TaxRegistrationNumber must be 4-50 alphanumeric or hyphen characters.");
        }

        return trimmed.ToUpperInvariant();
    }

    private static void ValidateRequiredAddress(string legalName, string addressLine1, string city)
    {
        if (string.IsNullOrWhiteSpace(legalName) || legalName.Trim().Length > 200)
        {
            throw new DomainException("LEGAL_ENTITY_INVALID_NAME", "LegalName is required and must not exceed 200 characters.");
        }

        if (string.IsNullOrWhiteSpace(addressLine1) || addressLine1.Trim().Length > 255)
        {
            throw new DomainException("LEGAL_ENTITY_INVALID_ADDRESS", "RegisteredAddressLine1 is required.");
        }

        if (string.IsNullOrWhiteSpace(city) || city.Trim().Length > 100)
        {
            throw new DomainException("LEGAL_ENTITY_INVALID_CITY", "RegisteredCity is required.");
        }
    }
}
