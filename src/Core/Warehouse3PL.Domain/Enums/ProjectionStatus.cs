namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Operational status of an analytics projection worker / checkpoint.
/// DB-SPEC-3PL-2026 Phase 6.0 Analytics Foundation &amp; KPI Engine.
/// </summary>
public enum ProjectionStatus
{
    /// <summary>
    /// Projection is actively processing incoming events in real time.
    /// </summary>
    Active = 1,

    /// <summary>
    /// Projection is running a historical replay or rebuild from historical transaction logs.
    /// </summary>
    Replaying = 2,

    /// <summary>
    /// Projection is administrative paused.
    /// </summary>
    Paused = 3,

    /// <summary>
    /// Projection encountered an unhandled fault and is halted awaiting operator intervention.
    /// </summary>
    Failed = 4
}

