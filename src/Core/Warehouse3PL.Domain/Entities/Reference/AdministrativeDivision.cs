using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Reference;

/// <summary>
/// Authoritative country administrative division (State, Province, District, or Local Level Palika)
/// supporting hierarchical parent-child geography lookups.
/// </summary>
public class AdministrativeDivision : AuditableEntity
{
    public string CountryCode { get; set; } = string.Empty;
    public Guid? ParentDivisionId { get; set; }
    public AdministrativeDivisionLevel DivisionLevel { get; set; }
    public string? IsoCode { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? LocalName { get; set; }
    public string? PostalPrefix { get; set; }
    public bool IsActive { get; set; } = true;

    public CountryProfile CountryProfile { get; set; } = null!;
    public AdministrativeDivision? ParentDivision { get; set; }
    public ICollection<AdministrativeDivision> ChildDivisions { get; set; } = new List<AdministrativeDivision>();
}
