namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Operational classification for non-productive shift intervals and operator downtime.
/// DB-SPEC-3PL-2026 Phase 6.2 Labor, Productivity & Workforce Pacing Analytics.
/// </summary>
public enum LaborIdleTimeReason
{
    WaitingForTask = 1,
    WaitingForReplenishment = 2,
    EquipmentUnavailable = 3,
    ScheduledBreak = 4,
    Training = 5,
    SystemDowntime = 6,
    UnexplainedInactivity = 7
}

