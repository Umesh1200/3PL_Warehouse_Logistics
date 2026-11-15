using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Analytics;

/// <summary>
/// Checkpoint entity tracking high-water marks and replay status for analytics projections.
/// Enables deterministic rebuilds, at-least-once recovery, and zero data loss without modifying OLTP records.
/// DB-SPEC-3PL-2026 Phase 6.0 Analytics Foundation &amp; KPI Engine.
/// </summary>
public class ProjectionCheckpoint : AuditableEntity<Guid>
{
    public string ProjectionName { get; set; } = string.Empty;
    public string SourceStreamId { get; set; } = "default";
    public Guid? LastProcessedEventId { get; set; }
    public long LastProcessedSequenceNumber { get; set; }
    public DateTime LastProcessedTimestampUtc { get; set; }

    public ProjectionStatus Status { get; set; } = ProjectionStatus.Active;
    public int Version { get; set; } = 1;
    public string? ErrorMessage { get; set; }
    public long ProcessedCount { get; set; }

    public ProjectionCheckpoint()
    {
        Id = Guid.NewGuid();
    }

    public static ProjectionCheckpoint Create(string projectionName, int version = 1)
    {
        return Create(projectionName, "default", version);
    }

    public static ProjectionCheckpoint Create(string projectionName, string sourceStreamId, int version = 1)
    {
        if (string.IsNullOrWhiteSpace(projectionName))
        {
            throw new ValidationException(nameof(projectionName), "Projection name is required.");
        }

        return new ProjectionCheckpoint
        {
            Id = Guid.NewGuid(),
            ProjectionName = projectionName.Trim(),
            SourceStreamId = string.IsNullOrWhiteSpace(sourceStreamId) ? "default" : sourceStreamId.Trim(),
            LastProcessedSequenceNumber = 0,
            LastProcessedTimestampUtc = DateTime.UnixEpoch,
            Status = ProjectionStatus.Active,
            Version = version,
            ProcessedCount = 0
        };
    }

    public void UpdateWatermark(Guid eventId, long sequenceNumber, DateTime timestampUtc)
    {
        if (sequenceNumber < LastProcessedSequenceNumber && Status != ProjectionStatus.Replaying)
        {
            throw new InvalidDomainStateException(
                $"Cannot move watermark backward from sequence {LastProcessedSequenceNumber} to {sequenceNumber} while in '{Status}' status.");
        }

        LastProcessedEventId = eventId;
        LastProcessedSequenceNumber = sequenceNumber;
        LastProcessedTimestampUtc = timestampUtc == default ? DateTime.UtcNow : timestampUtc;
        ProcessedCount++;
        ErrorMessage = null;
    }

    public void MarkReplaying()
    {
        Status = ProjectionStatus.Replaying;
        ErrorMessage = null;
    }

    public void MarkActive()
    {
        Status = ProjectionStatus.Active;
        ErrorMessage = null;
    }

    public void Pause()
    {
        Status = ProjectionStatus.Paused;
    }

    public void Fail(string error)
    {
        Status = ProjectionStatus.Failed;
        ErrorMessage = error?.Trim();
    }

    public void ResetToBeginning()
    {
        LastProcessedEventId = null;
        LastProcessedSequenceNumber = 0;
        LastProcessedTimestampUtc = DateTime.UnixEpoch;
        ProcessedCount = 0;
        Status = ProjectionStatus.Replaying;
        ErrorMessage = null;
    }
}

