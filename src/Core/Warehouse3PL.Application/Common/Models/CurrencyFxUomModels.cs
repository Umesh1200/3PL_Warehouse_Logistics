using Warehouse3PL.Domain.Entities.Reference;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

public sealed record CurrencyMasterDto(
    string CurrencyCode,
    string NumericCode,
    string Name,
    string Symbol,
    int MinorUnits,
    bool SymbolBeforeAmount,
    bool IsActive,
    int SeedVersion);

public sealed record FxRateRecordDto(
    Guid Id,
    string BaseCurrencyCode,
    string QuoteCurrencyCode,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc,
    int UnitMultiplier,
    decimal BuyRate,
    decimal SellRate,
    decimal MidRate,
    decimal EffectiveMidUnitRate,
    FxRateDirection RateDirection,
    FxRateSourceAuthority SourceAuthority,
    string BulletinVersion,
    string? BenchmarkSeriesId,
    string? IntegrityHash,
    DateTime PublishedAtUtc,
    FxRateApprovalStatus ApprovalStatus,
    Guid? ApprovedByUserId,
    DateTime? ApprovedAtUtc,
    string? VerificationNotes,
    Guid? SupersededByRateId,
    DateTime? SupersededAtUtc,
    string? VoidReason,
    Guid? VoidedByUserId,
    DateTime? VoidedAtUtc,
    long ConfigRevision,
    bool IsActive,
    string? EvidenceReference = null);

public sealed record FxConversionResultDto(
    decimal SourceAmount,
    string FromCurrencyCode,
    decimal ConvertedAmount,
    string ToCurrencyCode,
    int TargetMinorUnits,
    FxRateType RateType,
    int UnitMultiplier,
    decimal QuotedRate,
    decimal EffectiveUnitRate,
    string SourceAuthority,
    string BulletinVersion,
    string? BenchmarkSeriesId,
    string? IntegrityHash,
    DateTime EffectiveDateUtc,
    DateTime PublishedAtUtc,
    bool ResolvedFromDatabase,
    FxValuationMode ValuationMode = FxValuationMode.HistoricalAsKnown,
    DateTime? ApprovedAtUtc = null);

public sealed record UomDefinitionDto(
    string UomCode,
    string UneceRec20Code,
    string Name,
    UomDimension Dimension,
    UomSystemOfMeasure SystemOfMeasure,
    bool IsCanonicalBase,
    int RoundingPrecision,
    bool IsActive);

public sealed record UomConversionDto(
    string FromUomCode,
    string ToUomCode,
    UomDimension Dimension,
    decimal ConversionFactor,
    decimal InverseFactor,
    bool IsExact,
    bool IsActive);

public sealed record UomConversionResultDto(
    decimal SourceQuantity,
    string FromUomCode,
    decimal ConvertedQuantity,
    string ToUomCode,
    UomDimension Dimension,
    decimal EffectiveFactor,
    bool IsExact,
    int RoundingPrecision,
    string ResolutionPath);

public static class CurrencyFxUomMappings
{
    public static CurrencyMasterDto ToDto(this CurrencyMaster c) => new(
        c.CurrencyCode,
        c.NumericCode,
        c.Name,
        c.Symbol,
        c.MinorUnits,
        c.SymbolBeforeAmount,
        c.IsActive,
        c.SeedVersion);

    public static FxRateRecordDto ToDto(this FxRateRecord r) => new(
        r.Id,
        r.BaseCurrencyCode,
        r.QuoteCurrencyCode,
        r.EffectiveFromUtc,
        r.EffectiveToUtc,
        r.UnitMultiplier,
        r.BuyRate,
        r.SellRate,
        r.MidRate,
        r.ComputeUnitRate(FxRateType.Mid),
        r.RateDirection,
        r.SourceAuthority,
        r.BulletinVersion,
        r.BenchmarkSeriesId,
        r.IntegrityHash,
        r.PublishedAtUtc,
        r.ApprovalStatus,
        r.ApprovedByUserId,
        r.ApprovedAtUtc,
        r.VerificationNotes,
        r.SupersededByRateId,
        r.SupersededAtUtc,
        r.VoidReason,
        r.VoidedByUserId,
        r.VoidedAtUtc,
        r.ConfigRevision,
        r.IsActive,
        r.EvidenceReference);

    public static UomDefinitionDto ToDto(this UomDefinition d) => new(
        d.UomCode,
        d.UneceRec20Code,
        d.Name,
        d.Dimension,
        d.SystemOfMeasure,
        d.IsCanonicalBase,
        d.RoundingPrecision,
        d.IsActive);

    public static UomConversionDto ToDto(this UomConversion c) => new(
        c.FromUomCode,
        c.ToUomCode,
        c.Dimension,
        c.ConversionFactor,
        c.InverseFactor,
        c.IsExact,
        c.IsActive);
}
