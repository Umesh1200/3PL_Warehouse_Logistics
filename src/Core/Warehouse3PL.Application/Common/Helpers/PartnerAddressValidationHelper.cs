using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Helpers;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Common.Helpers;

/// <summary>
/// Shared MDM-2.2 address validation and country normalization helper.
/// Uses MDM-1 CountryProfile, AdministrativeDivision, and postal regex rules when active catalog data is seeded.
/// </summary>
public static class PartnerAddressValidationHelper
{
    public static async Task<(string? CountryCode, CountryNormalizationStatus Status)> ValidateAndNormalizeAddressAsync(
        IApplicationDbContext db,
        string? rawCountry,
        string? stateOrProvince,
        string? postalCode,
        string fieldPrefix,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawCountry))
        {
            return (null, CountryNormalizationStatus.PendingNormalization);
        }

        var (resolvedCountry, status) = await CountryNormalizationWriteHelper.ResolveForWriteAsync(db, rawCountry, cancellationToken);

        // If CountryProfiles are seeded in DB, validate postal format and active AdministrativeDivision when country resolved
        if (status == CountryNormalizationStatus.Normalized && !string.IsNullOrWhiteSpace(resolvedCountry))
        {
            var profile = await db.CountryProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.CountryCode == resolvedCountry && c.IsActive, cancellationToken);

            if (profile != null)
            {
                if (!string.IsNullOrWhiteSpace(postalCode) && !string.IsNullOrWhiteSpace(profile.PostalCodeRegex))
                {
                    if (!Regex.IsMatch(postalCode.Trim(), profile.PostalCodeRegex, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200)))
                    {
                        throw new ValidationException(
                            $"{fieldPrefix}PostalCode",
                            $"Postal code '{postalCode.Trim()}' does not match the format required for country '{resolvedCountry}'.");
                    }
                }

                if (!string.IsNullOrWhiteSpace(stateOrProvince))
                {
                    var divisions = await db.AdministrativeDivisions
                        .AsNoTracking()
                        .Where(d => d.CountryCode == resolvedCountry && d.IsActive)
                        .Select(d => new { d.Code, d.Name })
                        .ToListAsync(cancellationToken);

                    if (divisions.Count > 0)
                    {
                        var trimmedState = stateOrProvince.Trim();
                        bool matched = divisions.Any(d =>
                            string.Equals(d.Code, trimmedState, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(d.Name, trimmedState, StringComparison.OrdinalIgnoreCase));

                        if (!matched)
                        {
                            throw new ValidationException(
                                $"{fieldPrefix}StateOrProvince",
                                $"Administrative division '{trimmedState}' is not a recognized active division for country '{resolvedCountry}'.");
                        }
                    }
                }
            }
        }

        return (resolvedCountry, status);
    }
}
