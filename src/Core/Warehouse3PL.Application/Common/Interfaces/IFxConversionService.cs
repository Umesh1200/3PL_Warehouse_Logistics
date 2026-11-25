using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

/// <summary>
/// Resolves effective-dated FX conversions from authoritative DB <c>FxRateRecord</c> rows first
/// (honoring <c>UnitMultiplier</c>, Buy/Sell/Mid rates, and <c>CurrencyMaster.MinorUnits</c> AwayFromZero rounding),
/// falling back to verified central-bank bulletins for exact historical dates and failing closed on missing rates.
/// </summary>
public interface IFxConversionService
{
    Task<FxConversionResultDto> ConvertAmountAsync(
        decimal amount,
        string fromCurrencyCode,
        string toCurrencyCode,
        DateTime effectiveDateUtc,
        FxRateType rateType = FxRateType.Mid,
        DateTime? asOfUtc = null,
        FxRateSourceAuthority? preferredAuthority = null,
        CancellationToken cancellationToken = default);
}
