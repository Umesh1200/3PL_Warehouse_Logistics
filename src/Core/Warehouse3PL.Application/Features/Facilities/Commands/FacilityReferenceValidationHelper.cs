using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Domain.ValueObjects;

namespace Warehouse3PL.Application.Features.Facilities.Commands;

internal static class FacilityReferenceValidationHelper
{
    public static async Task<(string NormalizedCountryCode, string NormalizedTimeZoneId, string? NormalizedCurrencyCode)>
        ValidateGeographicAndLegalReferencesAsync(
            IApplicationDbContext db,
            ITimeZoneOffsetResolver timeZoneResolver,
            string countryCode,
            string timezone,
            string? postalCode,
            string? defaultCurrencyCode,
            Guid? defaultLegalEntityId,
            CancellationToken cancellationToken)
    {
        var normalizedCountry = (countryCode ?? string.Empty).Trim().ToUpperInvariant();
        var countryProfile = await db.CountryProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CountryCode == normalizedCountry && c.IsActive, cancellationToken)
            ?? throw new DomainException(
                "FACILITY_INVALID_COUNTRY",
                $"CountryCode '{countryCode}' is not a valid active CountryProfile (MDM-1.1).");

        var normalizedTz = (timezone ?? string.Empty).Trim();
        var tzProfile = await db.TimeZoneProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(tz => tz.TimeZoneId == normalizedTz && tz.IsActive, cancellationToken);

        if (tzProfile == null || !timeZoneResolver.TryValidateTimeZoneId(normalizedTz))
        {
            throw new DomainException(
                "FACILITY_INVALID_TIMEZONE",
                $"Timezone '{timezone}' is not a registered active IANA TimeZoneProfile.");
        }

        if (!string.IsNullOrWhiteSpace(tzProfile.CountryCode) &&
            !string.Equals(tzProfile.CountryCode.Trim(), normalizedCountry, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(
                $"Timezone '{normalizedTz}' belongs to country '{tzProfile.CountryCode}' and cannot be bound to Facility country '{normalizedCountry}'.",
                "FACILITY_TIMEZONE_COUNTRY_MISMATCH");
        }

        if (!string.IsNullOrWhiteSpace(postalCode) &&
            !countryProfile.IsSyntacticallyValidPostalCode(postalCode, out _))
        {
            throw new DomainException(
                $"PostalCode '{postalCode}' does not match syntactic postal format for '{normalizedCountry}'.",
                "FACILITY_INVALID_POSTAL_CODE");
        }

        string? normalizedCurrency = null;
        if (!string.IsNullOrWhiteSpace(defaultCurrencyCode))
        {
            try
            {
                Iso4217Currencies.Validate(defaultCurrencyCode, nameof(defaultCurrencyCode));
                normalizedCurrency = defaultCurrencyCode.Trim().ToUpperInvariant();
            }
            catch (ArgumentException ex)
            {
                throw new DomainException(ex.Message, "FACILITY_INVALID_CURRENCY");
            }
        }

        if (defaultLegalEntityId.HasValue)
        {
            if (defaultLegalEntityId.Value == Guid.Empty)
            {
                throw new DomainException("DefaultLegalEntityId cannot be empty.", "FACILITY_INVALID_LEGAL_ENTITY");
            }

            var legalEntity = await db.LegalEntities
                .AsNoTracking()
                .FirstOrDefaultAsync(le => le.Id == defaultLegalEntityId.Value && !le.IsDeleted, cancellationToken)
                ?? throw new DomainException(
                    $"LegalEntity '{defaultLegalEntityId.Value}' does not exist.",
                    "FACILITY_UNKNOWN_LEGAL_ENTITY");

            if (!legalEntity.IsActive)
            {
                throw new DomainException(
                    $"Cannot bind Facility to inactive LegalEntity '{legalEntity.LegalCode}'.",
                    "FACILITY_INACTIVE_LEGAL_ENTITY");
            }

            if (!string.Equals(legalEntity.CountryCode.Trim(), normalizedCountry, StringComparison.OrdinalIgnoreCase))
            {
                throw new DomainException(
                    $"Facility country '{normalizedCountry}' must match issuing LegalEntity '{legalEntity.LegalCode}' country '{legalEntity.CountryCode}'.",
                    "FACILITY_LEGAL_ENTITY_COUNTRY_MISMATCH");
            }
        }

        return (normalizedCountry, normalizedTz, normalizedCurrency);
    }
}
