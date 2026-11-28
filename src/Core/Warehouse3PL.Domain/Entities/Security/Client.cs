using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Security;

/// <summary>
/// Root Tenant entity: represents a 3PL logistics client company.
/// Per DB-SPEC-3PL-2026: clients.client_id is the primary multi-tenancy anchor
/// propagating across SKUs, ledger, orders, and rate cards.
/// </summary>
public class Client : AuditableEntity<Guid>, ISoftDeletable
{
    public string CompanyName { get; set; } = string.Empty;
    public string AccountCode { get; set; } = string.Empty;
    public string NormalizedAccountCode { get; set; } = string.Empty;
    public ClientStatus Status { get; set; } = ClientStatus.Onboarding;
    public SlaTier SlaTier { get; set; } = SlaTier.Standard;
    public string Currency { get; set; } = "USD";
    public string? BusinessClassification { get; set; }
    public string? TaxRegistrationNumber { get; set; }

    // Structured billing address (MDM-2.2)
    public string? BillingAddressLine1 { get; set; }
    public string? BillingAddressLine2 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingStateOrProvince { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? BillingCountryCode { get; set; }
    public CountryNormalizationStatus CountryNormalizationStatus { get; set; } = CountryNormalizationStatus.PendingNormalization;

    public int ConfigRevision { get; set; } = 1;

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigations
    public ICollection<ClientContact> Contacts { get; set; } = [];
    public ICollection<User> Users { get; set; } = [];
    public ICollection<ClientFacilityAccess> FacilityAccesses { get; set; } = [];
    public ICollection<ClientOperationalPreference> OperationalPreferences { get; set; } = [];

    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    public Client()
    {
        Id = Guid.NewGuid();
    }
}

