using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.CrossDock;

/// <summary>
/// Execution exception or discrepancy encountered during a cross-dock operation.
/// </summary>
public class CrossDockException : BaseEntity<Guid>
{
    public Guid CrossDockPlanId { get; set; }
    public Guid? CrossDockTaskId { get; set; }

    public CrossDockExceptionType Type { get; set; } = CrossDockExceptionType.Shortage;
    public decimal QuantityDiscrepant { get; set; }
    public string Reason { get; set; } = string.Empty;

    public Guid? ReportedByUserId { get; set; }
    public DateTime ReportedAtUtc { get; set; } = DateTime.UtcNow;

    public bool IsResolved { get; set; }
    public string? ResolutionNotes { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }

    public uint Version { get; set; } = 1;

    // Navigations
    public CrossDockPlan CrossDockPlan { get; set; } = null!;
    public CrossDockTask? CrossDockTask { get; set; }
    public User? ReportedByUser { get; set; }

    public CrossDockException()
    {
        Id = Guid.NewGuid();
    }

    public void Resolve(string resolutionNotes)
    {
        IsResolved = true;
        ResolutionNotes = resolutionNotes;
        ResolvedAtUtc = DateTime.UtcNow;
    }
}

