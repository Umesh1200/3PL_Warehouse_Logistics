using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Domain.ValueObjects;

namespace Warehouse3PL.Infrastructure.Services.Reference;

public sealed class FxConversionService : IFxConversionService
{
    private readonly IApplicationDbContext _db;
    private readonly IFxRateProvider _fxRateProvider;
    private readonly IDateTimeProvider _dateTime;
    private readonly IConfigurationConsistencyService? _consistencyService;

    public FxConversionService(
        IApplicationDbContext db,
        IFxRateProvider fxRateProvider,
        IDateTimeProvider dateTime,
        IConfigurationConsistencyService? consistencyService = null)
    {
        _db = db;
        _fxRateProvider = fxRateProvider;
        _dateTime = dateTime;
        _consistencyService = consistencyService;
    }

    public async Task<FxConversionResultDto> ConvertAmountAsync(
        decimal amount,
        string fromCurrencyCode,
        string toCurrencyCode,
        DateTime effectiveDateUtc,
        FxRateType rateType = FxRateType.Mid,
        DateTime? asOfUtc = null,
        FxRateSourceAuthority? preferredAuthority = null,
        FxValuationMode valuationMode = FxValuationMode.HistoricalAsKnown,
        CancellationToken cancellationToken = default)
    {
        var fromNorm = NormalizeCurrency(fromCurrencyCode, nameof(fromCurrencyCode));
        var toNorm = NormalizeCurrency(toCurrencyCode, nameof(toCurrencyCode));

        if (effectiveDateUtc.Kind == DateTimeKind.Unspecified)
        {
            throw new DomainException(
                "EffectiveDateUtc must explicitly specify DateTimeKind.Utc or Local.",
                "FX_INVALID_EFFECTIVE_DATE_KIND");
        }

        var evalDateUtc = effectiveDateUtc.Kind == DateTimeKind.Local
            ? effectiveDateUtc.ToUniversalTime()
            : effectiveDateUtc;

        var evalAsOfUtc = asOfUtc.HasValue
            ? (asOfUtc.Value.Kind switch
            {
                DateTimeKind.Utc => asOfUtc.Value,
                DateTimeKind.Local => asOfUtc.Value.ToUniversalTime(),
                _ => throw new DomainException("AsOfUtc must explicitly specify DateTimeKind.Utc or Local.", "FX_INVALID_ASOF_DATE_KIND")
            })
            : _dateTime.UtcNow;

        var targetCurrency = (_consistencyService != null
            ? await _consistencyService.GetOrLoadVerifiedAsync(
                ConfigurationScopeType.Global,
                "GLOBAL",
                $"currency:{toNorm}",
                (db, ct) => db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.CurrencyCode == toNorm && c.IsActive, ct),
                cancellationToken: cancellationToken)
            : await _db.Currencies
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.CurrencyCode == toNorm && c.IsActive, cancellationToken))
            ?? throw new DomainException(
                $"Target currency '{toNorm}' is not registered as an active CurrencyMaster entry.",
                "FX_UNKNOWN_TARGET_CURRENCY");

        if (string.Equals(fromNorm, toNorm, StringComparison.Ordinal))
        {
            return new FxConversionResultDto(
                amount,
                fromNorm,
                targetCurrency.RoundAmount(amount),
                toNorm,
                targetCurrency.MinorUnits,
                rateType,
                UnitMultiplier: 1,
                QuotedRate: 1.0m,
                EffectiveUnitRate: 1.0m,
                SourceAuthority: "IdentityRate",
                BulletinVersion: "Identity-1.0",
                BenchmarkSeriesId: "IDENTITY",
                IntegrityHash: "IDENTITY",
                EffectiveDateUtc: evalDateUtc,
                PublishedAtUtc: evalDateUtc,
                ResolvedFromDatabase: false,
                ValuationMode: valuationMode,
                ApprovedAtUtc: evalDateUtc);
        }

        // 1. Unified bitemporal database FX resolution via shared FxDatabaseRateResolver + effective-date-safe epoch cache
        var dbMatch = _consistencyService != null
            ? FxDatabaseRateResolver.SelectBestCandidate(
                (await _consistencyService.GetOrLoadVerifiedAsync(
                    ConfigurationScopeType.Global,
                    "GLOBAL",
                    $"fx-pair-history:{fromNorm}:{toNorm}:{preferredAuthority?.ToString() ?? "ANY"}",
                    (db, ct) => FxDatabaseRateResolver
                        .QueryAllPublishedForPair(db, fromNorm, toNorm, preferredAuthority)
                        .ToListAsync(ct),
                    cancellationToken: cancellationToken))
                .Where(r => r.EffectiveFromUtc <= evalDateUtc &&
                            (r.EffectiveToUtc == null || evalDateUtc < r.EffectiveToUtc)),
                evalAsOfUtc,
                valuationMode)
            : await FxDatabaseRateResolver.ResolveAsync(
                _db,
                fromNorm,
                toNorm,
                evalDateUtc,
                evalAsOfUtc,
                valuationMode,
                preferredAuthority,
                cancellationToken);

        if (dbMatch != null)
        {
            var quotedRate = rateType switch
            {
                FxRateType.Buy => dbMatch.BuyRate,
                FxRateType.Sell => dbMatch.SellRate,
                _ => dbMatch.MidRate
            };

            var effectiveUnitRate = dbMatch.ComputeUnitRate(rateType);
            var converted = targetCurrency.RoundAmount(amount * quotedRate / dbMatch.UnitMultiplier);

            return new FxConversionResultDto(
                amount,
                fromNorm,
                converted,
                toNorm,
                targetCurrency.MinorUnits,
                rateType,
                dbMatch.UnitMultiplier,
                quotedRate,
                effectiveUnitRate,
                dbMatch.SourceAuthority.ToString(),
                dbMatch.BulletinVersion,
                dbMatch.BenchmarkSeriesId,
                dbMatch.IntegrityHash,
                evalDateUtc,
                dbMatch.PublishedAtUtc,
                ResolvedFromDatabase: true,
                ValuationMode: valuationMode,
                ApprovedAtUtc: dbMatch.ApprovedAtUtc);
        }

        // 2. Fallback strictly to verified historical central-bank bulletins for their exact published windows
        if (rateType == FxRateType.Mid &&
            (!preferredAuthority.HasValue ||
             preferredAuthority.Value is FxRateSourceAuthority.FRB or FxRateSourceAuthority.ECB or FxRateSourceAuthority.BOE))
        {
            try
            {
                var rate = _fxRateProvider.GetHistoricalExchangeRate(
                    fromNorm,
                    toNorm,
                    evalDateUtc,
                    evalAsOfUtc,
                    valuationMode,
                    preferredAuthority,
                    out var rateSource,
                    out var rateVersion,
                    out var benchmarkSeriesId,
                    out var integrityHash,
                    out var publishedAtUtc);

                var converted = targetCurrency.RoundAmount(amount * rate);
                return new FxConversionResultDto(
                    amount,
                    fromNorm,
                    converted,
                    toNorm,
                    targetCurrency.MinorUnits,
                    rateType,
                    UnitMultiplier: 1,
                    QuotedRate: rate,
                    EffectiveUnitRate: rate,
                    SourceAuthority: rateSource,
                    BulletinVersion: rateVersion,
                    BenchmarkSeriesId: benchmarkSeriesId,
                    IntegrityHash: integrityHash,
                    EffectiveDateUtc: evalDateUtc,
                    PublishedAtUtc: publishedAtUtc,
                    ResolvedFromDatabase: false,
                    ValuationMode: valuationMode,
                    ApprovedAtUtc: null);
            }
            catch (InvalidOperationException ex)
            {
                throw new DomainException(ex.Message, "FX_RATE_UNAVAILABLE");
            }
        }

        throw new DomainException(
            $"No verified active FX rate is available for '{fromNorm}/{toNorm}' effective {evalDateUtc:O} as-of {evalAsOfUtc:O} (Mode={valuationMode}, RateType={rateType}).",
            "FX_RATE_UNAVAILABLE");
    }

    private static string NormalizeCurrency(string code, string paramName)
    {
        try
        {
            Iso4217Currencies.Validate(code, paramName);
            return code.Trim().ToUpperInvariant();
        }
        catch (ArgumentException ex)
        {
            throw new DomainException(ex.Message, "FX_INVALID_CURRENCY");
        }
    }
}
