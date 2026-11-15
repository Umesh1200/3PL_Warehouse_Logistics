namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Lifecycle status of an outbound pick wave.
/// Phase 4.3 Picking & Warehouse Execution.
/// </summary>
public enum PickWaveStatus
{
    Planning = 1,       // Wave created, orders grouped, tasks not yet generated
    Released = 2,       // Tasks generated and sequenced, ready for operators
    InProgress = 3,     // Operators actively executing tasks on floor
    Completed = 4,      // All tasks completed or resolved
    Cancelled = 5       // Wave aborted prior to completion
}

