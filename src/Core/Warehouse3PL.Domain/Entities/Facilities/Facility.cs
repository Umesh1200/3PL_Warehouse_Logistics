using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Facilities;

/// <summary>
/// Physical warehouse or logistics facility location anchor.
/// </summary>
public class Facility : AuditableEntity<Guid>, ISoftDeletable
{
    public string FacilityCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    
    // Address & Topography
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? StateOrProvince { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "USA";
    public CountryNormalizationStatus CountryNormalizationStatus { get; set; } = CountryNormalizationStatus.Normalized;
    public string Timezone { get; set; } = "America/Chicago";
    public Guid? DefaultLegalEntityId { get; set; }
    public string? DefaultCurrencyCode { get; set; }
    
    // Contacts
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }

    // Specifications & Operating parameters
    public decimal? TotalAreaSqM { get; set; }
    public int DockDoorCount { get; set; } = 4;
    public string? OperatingHoursJson { get; set; }
    public string? ConfigurationJson { get; set; }
    public bool IsActive { get; set; } = true;

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigations
    public LegalEntity? DefaultLegalEntity { get; set; }
    public ICollection<ClientFacilityAccess> ClientAccesses { get; set; } = [];
    public ICollection<WarehouseZone> Zones { get; set; } = [];
    public ICollection<Location> Locations { get; set; } = [];

    public Facility()
    {
        Id = Guid.NewGuid();
    }
}
