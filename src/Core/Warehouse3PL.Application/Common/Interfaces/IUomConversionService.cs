using Warehouse3PL.Application.Common.Models;

namespace Warehouse3PL.Application.Common.Interfaces;

/// <summary>
/// Authoritative UN/ECE Rec 20 UOM conversion service enforcing dimension safety,
/// AwayFromZero rounding precision, and preservation of canonical WMS metric bases (KG, CM, CM3, EA).
/// </summary>
public interface IUomConversionService
{
    Task<UomConversionResultDto> ConvertAsync(
        decimal quantity,
        string fromUomCode,
        string toUomCode,
        int? precisionOverride = null,
        CancellationToken cancellationToken = default);

    Task<UomConversionResultDto> ConvertToCanonicalMetricAsync(
        decimal quantity,
        string fromUomCode,
        CancellationToken cancellationToken = default);
}
