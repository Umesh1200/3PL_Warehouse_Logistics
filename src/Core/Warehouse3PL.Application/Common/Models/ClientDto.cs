using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

public record ClientDto
{
    public Guid Id { get; init; }
    public string CompanyName { get; init; } = string.Empty;
    public string AccountCode { get; init; } = string.Empty;
    public ClientStatus Status { get; init; }
    public SlaTier SlaTier { get; init; }
    public string Currency { get; init; } = "USD";
    public string? BusinessClassification { get; init; }
    public string? TaxRegistrationNumber { get; init; }
    public string? BillingAddressLine1 { get; init; }
    public string? BillingAddressLine2 { get; init; }
    public string? BillingCity { get; init; }
    public string? BillingStateOrProvince { get; init; }
    public string? BillingPostalCode { get; init; }
    public string? BillingCountryCode { get; init; }
    public CountryNormalizationStatus CountryNormalizationStatus { get; init; } = CountryNormalizationStatus.PendingNormalization;
    public int ConfigRevision { get; init; } = 1;
    public DateTime CreatedAt { get; init; }
    public int ActiveUsersCount { get; init; }
    public IReadOnlyList<ClientContactDto> Contacts { get; init; } = [];
}

public record ClientContactDto
{
    public Guid Id { get; init; }
    public Guid ClientId { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? RoleTitle { get; init; }
    public string Email { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public bool IsBillingContact { get; init; }
    public bool IsPrimary { get; init; }
    public bool IsActive { get; init; } = true;
    public int ConfigRevision { get; init; } = 1;
}

public record ClientFacilityAccessDto
{
    public int Id { get; init; }
    public Guid ClientId { get; init; }
    public Guid FacilityId { get; init; }
    public string? FacilityCode { get; init; }
    public string? FacilityName { get; init; }
    public DateTime EffectiveDate { get; init; }
    public FacilityAccessStatus Status { get; init; }
    public bool PrimaryOperatingFacility { get; init; }
    public DateTime? RevokedAtUtc { get; init; }
    public string? Remarks { get; init; }
    public int ConfigRevision { get; init; } = 1;
}

public record ClientOperationalPreferenceDto
{
    public Guid Id { get; init; }
    public Guid ClientId { get; init; }
    public Guid? FacilityId { get; init; }
    public string Scope { get; init; } = "ClientDefault";
    public Guid? DefaultReceivingZoneId { get; init; }
    public Guid? DefaultShippingZoneId { get; init; }
    public bool RequireAsnBeforeReceiving { get; init; }
    public bool AllowPartialShipments { get; init; } = true;
    public bool RequireQcInspectionOnReturns { get; init; }
    public bool AllowLotMixingInLocation { get; init; }
    public int DefaultOrderPriority { get; init; } = 1;
    public string? Notes { get; init; }
    public bool IsActive { get; init; } = true;
    public int ConfigRevision { get; init; } = 1;
}
