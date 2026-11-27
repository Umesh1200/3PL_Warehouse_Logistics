using System.Linq.Expressions;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Facilities.Queries;

internal static class FacilityProjections
{
    public static readonly Expression<Func<Facility, FacilityDto>> ToSummaryDto = f => new FacilityDto
    {
        Id = f.Id,
        FacilityCode = f.FacilityCode,
        Name = f.Name,
        AddressLine1 = f.AddressLine1,
        AddressLine2 = f.AddressLine2,
        City = f.City,
        StateOrProvince = f.StateOrProvince,
        PostalCode = f.PostalCode,
        CountryCode = f.CountryCode,
        CountryNormalizationStatus = f.CountryNormalizationStatus,
        Timezone = f.Timezone,
        DefaultLegalEntityId = f.DefaultLegalEntityId,
        DefaultCurrencyCode = f.DefaultCurrencyCode,
        ContactEmail = f.ContactEmail,
        ContactPhone = f.ContactPhone,
        TotalAreaSqM = f.TotalAreaSqM,
        DockDoorCount = f.DockDoorCount,
        IsActive = f.IsActive
    };

    public static readonly Expression<Func<Facility, FacilityConfigurationDto>> ToConfigurationDto = f => new FacilityConfigurationDto
    {
        Id = f.Id,
        FacilityCode = f.FacilityCode,
        Name = f.Name,
        AddressLine1 = f.AddressLine1,
        AddressLine2 = f.AddressLine2,
        City = f.City,
        StateOrProvince = f.StateOrProvince,
        PostalCode = f.PostalCode,
        CountryCode = f.CountryCode,
        CountryNormalizationStatus = f.CountryNormalizationStatus,
        Timezone = f.Timezone,
        DefaultLegalEntityId = f.DefaultLegalEntityId,
        DefaultCurrencyCode = f.DefaultCurrencyCode,
        ContactEmail = f.ContactEmail,
        ContactPhone = f.ContactPhone,
        TotalAreaSqM = f.TotalAreaSqM,
        DockDoorCount = f.DockDoorCount,
        IsActive = f.IsActive,
        OperatingHoursJson = f.OperatingHoursJson,
        ConfigurationJson = f.ConfigurationJson,
        CreatedAt = f.CreatedAt,
        UpdatedAt = f.UpdatedAt
    };

    public static FacilityDto ToDto(Facility f) => new()
    {
        Id = f.Id,
        FacilityCode = f.FacilityCode,
        Name = f.Name,
        AddressLine1 = f.AddressLine1,
        AddressLine2 = f.AddressLine2,
        City = f.City,
        StateOrProvince = f.StateOrProvince,
        PostalCode = f.PostalCode,
        CountryCode = f.CountryCode,
        CountryNormalizationStatus = f.CountryNormalizationStatus,
        Timezone = f.Timezone,
        DefaultLegalEntityId = f.DefaultLegalEntityId,
        DefaultCurrencyCode = f.DefaultCurrencyCode,
        ContactEmail = f.ContactEmail,
        ContactPhone = f.ContactPhone,
        TotalAreaSqM = f.TotalAreaSqM,
        DockDoorCount = f.DockDoorCount,
        IsActive = f.IsActive
    };

    public static bool IsCrossFacilityStaff(ICurrentUserContext currentUser) =>
        (currentUser.IsSuperAdmin && currentUser.UserType == UserType.Staff) ||
        (currentUser.IsStaff && (
            currentUser.HasPermission(Permissions.Analytics.MultiFacilityView) ||
            currentUser.HasPermission("Analytics:CrossFacility") ||
            currentUser.HasPermission("Shipments:CrossFacility") ||
            currentUser.HasPermission("Financial:CrossFacility") ||
            currentUser.HasPermission("Financials:CrossFacility")));
}
