namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Resolution status for a logged inbound discrepancy or quality control hold.
/// </summary>
public enum DiscrepancyResolutionStatus
{
    PendingReview = 1,
    Approved = 2,
    Rejected = 3,
    Quarantined = 4
}

