namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Statutory taxpayer registration identifier classification for an issuing LegalEntity.
/// </summary>
public enum TaxIdentifierType
{
    PAN = 1,
    VAT = 2,
    EIN = 3,
    TIN = 4,
    Other = 99
}
