namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Lifecycle status of an advisory slotting recommendation batch.
/// </summary>
public enum SlottingBatchStatus
{
    /// <summary>
    /// Initial formulation phase.
    /// </summary>
    Draft = 1,

    /// <summary>
    /// Optimization run completed and candidate moves simulated.
    /// </summary>
    Simulated = 2,

    /// <summary>
    /// Submitted to warehouse operations management for review and approval.
    /// </summary>
    PendingApproval = 3,

    /// <summary>
    /// All proposed relocation moves approved by authorized floor manager.
    /// </summary>
    Approved = 4,

    /// <summary>
    /// Some recommendations approved and others rejected or deferred.
    /// </summary>
    PartiallyApproved = 5,

    /// <summary>
    /// Batch rejected by floor manager; no operational moves executed.
    /// </summary>
    Rejected = 6,

    /// <summary>
    /// Approved moves handed off to WMS operational relocation task engine and executed.
    /// </summary>
    Executed = 7,

    /// <summary>
    /// Cancelled prior to execution.
    /// </summary>
    Cancelled = 8
}
