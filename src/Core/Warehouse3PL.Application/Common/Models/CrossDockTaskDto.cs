using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

public class CrossDockTaskDto
{
    public Guid Id { get; set; }
    public string TaskNumber { get; set; } = string.Empty;
    public Guid CrossDockPlanId { get; set; }
    public Guid CrossDockPlanLineId { get; set; }
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }

    public Guid ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ItemDescription { get; set; } = string.Empty;
    public Guid? LotId { get; set; }
    public string? LotNumber { get; set; }

    public string Lpn { get; set; } = string.Empty;

    public Guid SourceLocationId { get; set; }
    public string SourceLocationCode { get; set; } = string.Empty;

    public Guid DestinationLocationId { get; set; }
    public string DestinationLocationCode { get; set; } = string.Empty;
    public string DestinationLocationBarcode { get; set; } = string.Empty;

    public Guid? ActualDestinationLocationId { get; set; }
    public string? ActualDestinationLocationCode { get; set; }

    public decimal Quantity { get; set; }
    public CrossDockTaskStatus Status { get; set; }
    public CrossDockPriority Priority { get; set; }

    public Guid? AssignedUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? Notes { get; set; }
    public uint Version { get; set; }
}

public class CrossDockExceptionDto
{
    public Guid Id { get; set; }
    public Guid CrossDockPlanId { get; set; }
    public Guid? CrossDockTaskId { get; set; }
    public CrossDockExceptionType Type { get; set; }
    public decimal QuantityDiscrepant { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid? ReportedByUserId { get; set; }
    public DateTime ReportedAtUtc { get; set; }
    public bool IsResolved { get; set; }
    public string? ResolutionNotes { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
}

