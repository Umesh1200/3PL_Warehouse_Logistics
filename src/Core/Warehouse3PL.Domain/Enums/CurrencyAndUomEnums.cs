namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Authoritative monetary or governance source for an effective-dated FX rate record.
/// </summary>
public enum FxRateSourceAuthority
{
    NRB = 1,
    FRB = 2,
    ECB = 3,
    BOE = 4,
    ManualApproved = 5
}

/// <summary>
/// Lifecycle status of an effective-dated FX rate bulletin record.
/// Seeded or drafted entries remain inactive until explicitly verified and approved.
/// </summary>
public enum FxRateApprovalStatus
{
    Draft = 1,
    VerifiedApproved = 2,
    Superseded = 3,
    Voided = 4
}

/// <summary>
/// Quotation leg selected when converting between base and quote currencies.
/// </summary>
public enum FxRateType
{
    Mid = 1,
    Buy = 2,
    Sell = 3
}

/// <summary>
/// Structural quotation convention for FxRateRecord:
/// QuoteCurrency units per UnitMultiplier of BaseCurrency.
/// </summary>
public enum FxRateDirection
{
    QuotePerUnitBase = 1
}

/// <summary>
/// Physical measurement dimension governed by UN/ECE Rec 20 UOM catalogs.
/// Cross-dimension conversions are strictly prohibited.
/// </summary>
public enum UomDimension
{
    Mass = 1,
    Length = 2,
    Volume = 3,
    Count = 4
}

/// <summary>
/// Measurement system classification for UOM definitions.
/// </summary>
public enum UomSystemOfMeasure
{
    SI = 1,
    Imperial = 2,
    Packaging = 3
}
