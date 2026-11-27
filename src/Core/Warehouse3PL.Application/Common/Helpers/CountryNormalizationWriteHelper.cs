using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Reference;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Helpers;

/// <summary>
/// Shared catalog-backed normalization helper for application write/import commands.
/// Ensures newly written country values never default silently to US/NP and never
/// claim <see cref="CountryNormalizationStatus.Normalized"/> without active catalog validation.
/// </summary>
public static class CountryNormalizationWriteHelper
{
    public static async Task<(string? Value, CountryNormalizationStatus Status)> ResolveForWriteAsync(
        IApplicationDbContext db,
        string? rawCountry,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawCountry))
        {
            return (null, CountryNormalizationStatus.PendingNormalization);
        }

        var trimmed = rawCountry.Trim();
        var activeProfiles = await db.CountryProfiles
            .AsNoTracking()
            .Where(c => c.IsActive)
            .Select(c => new { c.CountryCode, c.IsoAlpha3Code, c.Name })
            .ToListAsync(cancellationToken);

        if (activeProfiles.Count == 0)
        {
            return (trimmed, CountryNormalizationStatus.PendingNormalization);
        }

        var key = CountryNormalizationAlias.NormalizeLookupKey(trimmed);
        var exact = activeProfiles.FirstOrDefault(c =>
            string.Equals(c.CountryCode.Trim(), key, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.IsoAlpha3Code.Trim(), key, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(CountryNormalizationAlias.NormalizeLookupKey(c.Name), key, StringComparison.OrdinalIgnoreCase));

        if (exact != null)
        {
            return (exact.CountryCode.Trim().ToUpperInvariant(), CountryNormalizationStatus.Normalized);
        }

        var aliasTarget = await db.CountryNormalizationAliases
            .AsNoTracking()
            .Where(a => a.IsActive && a.AliasKey == key)
            .Join(
                db.CountryProfiles.AsNoTracking().Where(c => c.IsActive),
                a => a.CountryCode,
                c => c.CountryCode,
                (a, c) => c.CountryCode)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(aliasTarget))
        {
            return (aliasTarget.Trim().ToUpperInvariant(), CountryNormalizationStatus.Normalized);
        }

        return (trimmed, CountryNormalizationStatus.PendingNormalization);
    }
}
