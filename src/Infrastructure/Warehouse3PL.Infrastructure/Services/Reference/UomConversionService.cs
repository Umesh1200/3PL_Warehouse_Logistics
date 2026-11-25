using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services.Reference;

public sealed class UomConversionService : IUomConversionService
{
    private readonly IApplicationDbContext _db;

    public UomConversionService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<UomConversionResultDto> ConvertAsync(
        decimal quantity,
        string fromUomCode,
        string toUomCode,
        int? precisionOverride = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fromUomCode) || string.IsNullOrWhiteSpace(toUomCode))
        {
            throw new DomainException("FromUomCode and ToUomCode are required.", "UOM_MISSING_CODE");
        }

        if (precisionOverride.HasValue && (precisionOverride.Value < 0 || precisionOverride.Value > 8))
        {
            throw new DomainException("Precision override must be between 0 and 8.", "UOM_INVALID_PRECISION");
        }

        var fromNorm = fromUomCode.Trim().ToUpperInvariant();
        var toNorm = toUomCode.Trim().ToUpperInvariant();

        var defs = await _db.UomDefinitions
            .AsNoTracking()
            .Where(d => (d.UomCode == fromNorm || d.UomCode == toNorm) && d.IsActive)
            .ToListAsync(cancellationToken);

        var fromDef = defs.FirstOrDefault(d => d.UomCode == fromNorm)
            ?? throw new DomainException($"Unknown or inactive UOM '{fromNorm}'.", "UOM_UNKNOWN_OR_INACTIVE");
        var toDef = defs.FirstOrDefault(d => d.UomCode == toNorm)
            ?? throw new DomainException($"Unknown or inactive UOM '{toNorm}'.", "UOM_UNKNOWN_OR_INACTIVE");

        if (fromDef.Dimension != toDef.Dimension)
        {
            throw new DomainException(
                $"Cannot convert incompatible UOM dimensions '{fromNorm}' ({fromDef.Dimension}) -> '{toNorm}' ({toDef.Dimension}).",
                "UOM_INCOMPATIBLE_DIMENSION");
        }

        int precision = precisionOverride ?? toDef.RoundingPrecision;

        if (string.Equals(fromNorm, toNorm, StringComparison.Ordinal))
        {
            return new UomConversionResultDto(
                quantity,
                fromNorm,
                decimal.Round(quantity, precision, MidpointRounding.AwayFromZero),
                toNorm,
                fromDef.Dimension,
                1m,
                IsExact: true,
                precision,
                $"{fromNorm}->{toNorm}(identity)");
        }

        if (fromDef.Dimension == UomDimension.Count)
        {
            throw new DomainException(
                $"Cross-packaging Count conversion '{fromNorm} -> {toNorm}' requires an explicit item PackagingUnit factor.",
                "UOM_SKU_SPECIFIC_CONVERSION_REQUIRED");
        }

        var direct = await _db.UomConversions
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.IsActive && c.FromUomCode == fromNorm && c.ToUomCode == toNorm, cancellationToken);

        if (direct != null)
        {
            return new UomConversionResultDto(
                quantity,
                fromNorm,
                decimal.Round(quantity * direct.ConversionFactor, precision, MidpointRounding.AwayFromZero),
                toNorm,
                fromDef.Dimension,
                direct.ConversionFactor,
                direct.IsExact,
                precision,
                $"{fromNorm}->{toNorm}");
        }

        var reverse = await _db.UomConversions
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.IsActive && c.FromUomCode == toNorm && c.ToUomCode == fromNorm, cancellationToken);

        if (reverse != null)
        {
            return new UomConversionResultDto(
                quantity,
                fromNorm,
                decimal.Round(quantity * reverse.InverseFactor, precision, MidpointRounding.AwayFromZero),
                toNorm,
                fromDef.Dimension,
                reverse.InverseFactor,
                reverse.IsExact,
                precision,
                $"{fromNorm}->{toNorm}(inverse)");
        }

        // Canonical 2-hop via dimension base unit (KG / CM / CM3) when both legs convert cleanly to canonical base
        var canonicalDef = await _db.UomDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Dimension == fromDef.Dimension && d.IsCanonicalBase && d.IsActive, cancellationToken);

        if (canonicalDef != null &&
            !string.Equals(canonicalDef.UomCode, fromNorm, StringComparison.Ordinal) &&
            !string.Equals(canonicalDef.UomCode, toNorm, StringComparison.Ordinal))
        {
            var leg1 = await ResolveDirectOrInverseFactorAsync(fromNorm, canonicalDef.UomCode, cancellationToken);
            var leg2 = await ResolveDirectOrInverseFactorAsync(canonicalDef.UomCode, toNorm, cancellationToken);
            if (leg1.HasValue && leg2.HasValue)
            {
                var combinedFactor = leg1.Value.Factor * leg2.Value.Factor;
                return new UomConversionResultDto(
                    quantity,
                    fromNorm,
                    decimal.Round(quantity * combinedFactor, precision, MidpointRounding.AwayFromZero),
                    toNorm,
                    fromDef.Dimension,
                    decimal.Round(combinedFactor, 10, MidpointRounding.AwayFromZero),
                    leg1.Value.IsExact && leg2.Value.IsExact,
                    precision,
                    $"{fromNorm}->{canonicalDef.UomCode}->{toNorm}");
            }
        }

        throw new DomainException(
            $"No active UOM conversion path is registered from '{fromNorm}' to '{toNorm}'.",
            "UOM_CONVERSION_NOT_FOUND");
    }

    public async Task<UomConversionResultDto> ConvertToCanonicalMetricAsync(
        decimal quantity,
        string fromUomCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fromUomCode))
        {
            throw new DomainException("FromUomCode is required.", "UOM_MISSING_CODE");
        }

        var fromNorm = fromUomCode.Trim().ToUpperInvariant();
        var fromDef = await _db.UomDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.UomCode == fromNorm && d.IsActive, cancellationToken)
            ?? throw new DomainException($"Unknown or inactive UOM '{fromNorm}'.", "UOM_UNKNOWN_OR_INACTIVE");

        var canonicalDef = await _db.UomDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Dimension == fromDef.Dimension && d.IsCanonicalBase && d.IsActive, cancellationToken)
            ?? throw new DomainException($"No canonical base UOM configured for dimension '{fromDef.Dimension}'.", "UOM_CANONICAL_MISSING");

        return await ConvertAsync(quantity, fromNorm, canonicalDef.UomCode, canonicalDef.RoundingPrecision, cancellationToken);
    }

    private async Task<(decimal Factor, bool IsExact)?> ResolveDirectOrInverseFactorAsync(
        string fromCode,
        string toCode,
        CancellationToken cancellationToken)
    {
        var direct = await _db.UomConversions
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.IsActive && c.FromUomCode == fromCode && c.ToUomCode == toCode, cancellationToken);
        if (direct != null)
        {
            return (direct.ConversionFactor, direct.IsExact);
        }

        var reverse = await _db.UomConversions
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.IsActive && c.FromUomCode == toCode && c.ToUomCode == fromCode, cancellationToken);
        if (reverse != null)
        {
            return (reverse.InverseFactor, reverse.IsExact);
        }

        return null;
    }
}
