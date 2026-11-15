using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

public class PutawayStrategyRuleDto
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public int Sequence { get; set; }
    public string RuleName { get; set; } = string.Empty;
    public StorageTempClass TempClassRequirement { get; set; }
    public VelocityClass? TargetVelocity { get; set; }
    public bool PreferConsolidation { get; set; }
    public bool AllowDynamicOverflow { get; set; }
    public bool IsActive { get; set; }
    public uint Version { get; set; }
}

