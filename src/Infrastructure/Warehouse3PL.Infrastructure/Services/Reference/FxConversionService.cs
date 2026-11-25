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

    public FxConversionService(
        IApplicationDbContext db,
        IFxRateProvider fxRateProvider,
        IDateTimeProvider dateTime)
    {
        _db = db;
        _fxRateProvider = fxRateProvider;
        _dateTime = dateTime;
    }

    public async Task<FxConversionResultDto> ConvertAmountAsync(
        decimal amount,
        string fromCurrencyCode,
        string toCurrencyCode,
        DateTime effectiveDateUtc,
        FxRateType rateType = FxRateType.Mid,
        DateTime? asOfUtc = null,
        FxRateSourceAuthority? preferredAuthority = null,
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

        var targetCurrency = await _db.Currencies
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CurrencyCode == toNorm && c.IsActive, cancellationToken)
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
                ResolvedFromDatabase: false);
        }

        // 1. Database-authoritative effective-dated FxRateRecord takes precedence (supports NRB 100 INR -> NPR, USD -> NPR, etc.)
        var dbCandidatesQuery = _db.FxRateRecords
            .AsNoTracking()
            .Where(r => r.BaseCurrencyCode == fromNorm &&
                        r.QuoteCurrencyCode == toNorm &&
                        r.ApprovalStatus == FxRateApprovalStatus.VerifiedApproved &&
                        r.IsActive &&
                        r.EffectiveFromUtc <= evalDateUtc &&
                        (r.EffectiveToUtc == null || r.EffectiveToUtc > evalDateUtc));

        if (preferredAuthority.HasValue)
        {
            dbCandidatesQuery = dbCandidatesQuery.Where(r => r.SourceAuthority == preferredAuthority.Value);
        }

        var dbCandidate = await dbCandidatesQuery
            .OrderByDescending(r => r.EffectiveFromUtc)
            .ThenByDescending(r => r.PublishedAtUtc)
            .ThenByDescending(r => r.ApprovedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (dbCandidate != null)
        {
            if (dbCandidate.PublishedAtUtc > evalAsOfUtc)
            {
                throw new DomainException(
                    $"FX bulletin '{dbCandidate.BulletinVersion}' ({fromNorm}/{toNorm}) was published at {dbCandidate.PublishedAtUtc:O}, after valuation timestamp {evalAsOfUtc:O}.",
                    "FX_RATE_FUTURE_PUBLICATION");
            }

            var quotedRate = rateType switch
            {
                FxRateType.Buy => dbCandidate.BuyRate,
                FxRateType.Sell => dbCandidate.SellRate,
                _ => dbCandidate.MidRate
            };

            var effectiveUnitRate = dbCandidate.ComputeUnitRate(rateType);
            var converted = targetCurrency.RoundAmount(amount * quotedRate / dbCandidate.UnitMultiplier);

            return new FxConversionResultDto(
                amount,
                fromNorm,
                converted,
                toNorm,
                targetCurrency.MinorUnits,
                rateType,
                dbCandidate.UnitMultiplier,
                quotedRate,
                effectiveUnitRate,
                dbCandidate.SourceAuthority.ToString(),
                dbCandidate.BulletinVersion,
                dbCandidate.BenchmarkSeriesId,
                dbCandidate.IntegrityHash,
                evalDateUtc,
                dbCandidate.PublishedAtUtc,
                ResolvedFromDatabase: true);
        }

        // 2. Fallback strictly to verified historical central-bank bulletins for their exact published windows (if no authority override excludes them)
        if (!preferredAuthority.HasValue && rateType == FxRateType.Mid)
        {
            try
            {
                var rate = _fxRateProvider.GetHistoricalExchangeRate(
                    fromNorm,
                    toNorm,
                    evalDateUtc,
                    evalAsOfUtc,
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
                    ResolvedFromDatabase: false);
            }
            catch (InvalidOperationException ex)
            {
                throw new DomainException(ex.Message, "FX_RATE_UNAVAILABLE");
            }
        }

        throw new DomainException(
            $"No verified active FX rate is available for '{fromNorm}/{toNorm}' effective {evalDateUtc:O} (RateType={rateType}).",
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
