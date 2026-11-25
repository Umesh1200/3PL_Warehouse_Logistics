using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

/// <summary>
/// Full administrative LegalEntity DTO containing statutory tax and CBMS metadata.
/// Restricted exclusively to database-verified platform Staff holding Global:LegalEntities permissions.
/// </summary>
public sealed record LegalEntityDto(
    Guid Id,
    string LegalCode,
    string LegalName,
    string? RegistrationNumber,
    string CountryCode,
    TaxIdentifierType TaxIdentifierType,
    string TaxRegistrationNumber,
    string RegisteredAddressLine1,
    string? RegisteredAddressLine2,
    string RegisteredCity,
    string? RegisteredStateOrProvince,
    string? RegisteredPostalCode,
    bool IsCbmsMandated,
    string? IroSoftwarePermitRef,
    string DefaultCurrencyCode,
    long ConfigRevision,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public static class LegalEntityMappings
{
    public static LegalEntityDto ToDto(this LegalEntity entity) =>
        new(
            entity.Id,
            entity.LegalCode,
            entity.LegalName,
            entity.RegistrationNumber,
            entity.CountryCode,
            entity.TaxIdentifierType,
            entity.TaxRegistrationNumber,
            entity.RegisteredAddressLine1,
            entity.RegisteredAddressLine2,
            entity.RegisteredCity,
            entity.RegisteredStateOrProvince,
            entity.RegisteredPostalCode,
            entity.IsCbmsMandated,
            entity.IroSoftwarePermitRef,
            entity.DefaultCurrencyCode,
            entity.ConfigRevision,
            entity.IsActive,
            entity.CreatedAt,
            entity.UpdatedAt);
}
