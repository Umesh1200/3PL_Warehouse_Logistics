using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Analytics;

/// <summary>
/// Container batch representing an optimization run of candidate SKU relocation recommendations.
/// Invariant: Recommendations are advisory and do not mutate stock balances or physical locations
/// until approved and executed through the operational relocation workflow.
/// </summary>
public class SlottingRecommendationBatch : AuditableEntity<Guid>
{
    public Guid FacilityId { get; set; }
    public Guid? ClientId { get; set; }
    public Guid PolicyId { get; set; }
    public int PolicyVersion { get; set; } = 1;
    public string? PolicySnapshotJson { get; set; }

    public string BatchNumber { get; set; } = string.Empty;
    public SlottingBatchStatus Status { get; set; } = SlottingBatchStatus.Draft;
    public DateTime SnapshotDateUtc { get; set; } = DateTime.UtcNow;

    public int TotalProposedMoves { get; set; }
    public int ApprovedMovesCount { get; set; }
    public int RejectedMovesCount { get; set; }
    public int ExecutedMovesCount { get; set; }

    public decimal EstimatedLaborMinutesTotal { get; set; }
    public decimal EstimatedTravelDistanceSavedMetersTotal { get; set; }
    public decimal EstimatedMonthlyNetSavingsUsd { get; set; }
    public decimal AverageSuitabilityScore { get; set; }

    public Guid? ApprovedBy { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? ExecutionStartedAtUtc { get; set; }
    public DateTime? ExecutionCompletedAtUtc { get; set; }
    public string? Notes { get; set; }

    // Navigations
    public SlottingPolicy Policy { get; set; } = null!;
    public ICollection<SlottingRecommendation> Recommendations { get; set; } = [];

    public SlottingRecommendationBatch()
    {
        Id = Guid.NewGuid();
    }

    public static SlottingRecommendationBatch Create(
        Guid facilityId,
        Guid policyId,
        string batchNumber,
        Guid? clientId = null,
        DateTime? snapshotDateUtc = null,
        string? notes = null,
        int policyVersion = 1,
        string? policySnapshotJson = null)
    {
        if (facilityId == Guid.Empty)
            throw new ValidationException(nameof(facilityId), "Facility ID cannot be empty.");

        if (policyId == Guid.Empty)
            throw new ValidationException(nameof(policyId), "Policy ID cannot be empty.");

        if (string.IsNullOrWhiteSpace(batchNumber))
            throw new ValidationException(nameof(batchNumber), "Batch number cannot be empty.");

        return new SlottingRecommendationBatch
        {
            FacilityId = facilityId,
            PolicyId = policyId,
            PolicyVersion = policyVersion > 0 ? policyVersion : 1,
            PolicySnapshotJson = policySnapshotJson,
            BatchNumber = batchNumber.Trim().ToUpperInvariant(),
            ClientId = clientId,
            SnapshotDateUtc = snapshotDateUtc ?? DateTime.UtcNow,
            Status = SlottingBatchStatus.Draft,
            Notes = notes?.Trim()
        };
    }

    public void MarkSimulated(int totalMoves, decimal estLaborMin, decimal estTravelSavedMeters, decimal estMonthlySavingsUsd, decimal avgScore)
    {
        TotalProposedMoves = totalMoves;
        EstimatedLaborMinutesTotal = estLaborMin;
        EstimatedTravelDistanceSavedMetersTotal = estTravelSavedMeters;
        EstimatedMonthlyNetSavingsUsd = estMonthlySavingsUsd;
        AverageSuitabilityScore = avgScore;
        Status = SlottingBatchStatus.Simulated;
    }

    public void SubmitForApproval()
    {
        if (Status != SlottingBatchStatus.Simulated)
            throw new InvalidOperationException($"Cannot submit batch in '{Status}' state for approval.");

        Status = SlottingBatchStatus.PendingApproval;
    }

    public void Approve(Guid approvedByUserId)
    {
        if (Status != SlottingBatchStatus.PendingApproval && Status != SlottingBatchStatus.Simulated)
            throw new InvalidOperationException($"Cannot approve batch in '{Status}' state.");

        ApprovedBy = approvedByUserId;
        ApprovedAtUtc = DateTime.UtcNow;
        Status = SlottingBatchStatus.Approved;
    }

    public void Reject(string reason)
    {
        if (Status == SlottingBatchStatus.Executed)
            throw new InvalidOperationException("Cannot reject an executed batch.");

        Status = SlottingBatchStatus.Rejected;
        Notes = string.IsNullOrWhiteSpace(Notes) ? $"Rejected: {reason}" : $"{Notes} | Rejected: {reason}";
    }

    public void Cancel(string reason)
    {
        if (Status == SlottingBatchStatus.Executed)
            throw new InvalidOperationException("Cannot cancel an executed batch.");

        Status = SlottingBatchStatus.Cancelled;
        Notes = string.IsNullOrWhiteSpace(Notes) ? $"Cancelled: {reason}" : $"{Notes} | Cancelled: {reason}";
    }
}
