using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

public class InspectionDecisionDto
{
    public Guid DiscrepancyId { get; set; }
    public DiscrepancyResolutionStatus Status { get; set; }
    public Guid ResolvedByUserId { get; set; }
    public DateTime ResolvedAtUtc { get; set; }
    public string? ResolutionNotes { get; set; }
    public decimal ReleasedQuantity { get; set; }
    public decimal QuarantinedQuantity { get; set; }
}

