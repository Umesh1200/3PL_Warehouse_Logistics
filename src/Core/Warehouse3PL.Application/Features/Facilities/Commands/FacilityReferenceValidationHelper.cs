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
        bool registeredActiveTz = await db.TimeZoneProfiles
            .AsNoTracking()
            .AnyAsync(tz => tz.TimeZoneId == normalizedTz && tz.IsActive, cancellationToken);

        if (!registeredActiveTz || !timeZoneResolver.TryValidateTimeZoneId(normalizedTz))
        {
            throw new DomainException(
                "FACILITY_INVALID_TIMEZONE",
                $"Timezone '{timezone}' is not a registered active IANA TimeZoneProfile.");
        }

        if (!string.IsNullOrWhiteSpace(postalCode) &&
            !countryProfile.IsSyntacticallyValidPostalCode(postalCode, out _))
        {
            throw new DomainException(
                "FACILITY_INVALID_POSTAL_CODE",
                $"PostalCode '{postalCode}' does not match syntactic postal format for '{normalizedCountry}'.");
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
                throw new DomainException("FACILITY_INVALID_CURRENCY", ex.Message);
            }
        }

        if (defaultLegalEntityId.HasValue)
        {
            if (defaultLegalEntityId.Value == Guid.Empty)
            {
                throw new DomainException("FACILITY_INVALID_LEGAL_ENTITY", "DefaultLegalEntityId cannot be empty.");
            }

            var legalEntity = await db.LegalEntities
                .AsNoTracking()
                .FirstOrDefaultAsync(le => le.Id == defaultLegalEntityId.Value && !le.IsDeleted, cancellationToken)
                ?? throw new DomainException(
                    "FACILITY_UNKNOWN_LEGAL_ENTITY",
                    $"LegalEntity '{defaultLegalEntityId.Value}' does not exist.");

            if (!legalEntity.IsActive)
            {
                throw new DomainException(
                    "FACILITY_INACTIVE_LEGAL_ENTITY",
                    $"Cannot bind Facility to inactive LegalEntity '{legalEntity.LegalCode}'.");
            }
        }

        return (normalizedCountry, normalizedTz, normalizedCurrency);
    }
}
