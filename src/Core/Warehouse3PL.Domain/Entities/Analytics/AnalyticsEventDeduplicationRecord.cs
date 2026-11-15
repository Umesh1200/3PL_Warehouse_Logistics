using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Analytics;

/// <summary>
/// Event deduplication log ensuring exact-once projection processing under at-least-once messaging delivery.
/// Prevents metric double-counting during projection rebuilds, replays, or retries.
/// DB-SPEC-3PL-2026 Phase 6.0 Analytics Foundation &amp; KPI Engine.
/// </summary>
public class AnalyticsEventDeduplicationRecord : AuditableEntity<Guid>
{
    public string ProjectionName { get; set; } = string.Empty;
    public Guid EventId { get; set; }
    public string EventName { get; set; } = string.Empty;
    public DateTime ProcessedAtUtc { get; set; }

    public AnalyticsEventDeduplicationRecord()
    {
        Id = Guid.NewGuid();
    }

    public static AnalyticsEventDeduplicationRecord Create(
        string projectionName,
        Guid eventId,
        string eventName)
    {
        if (string.IsNullOrWhiteSpace(projectionName))
        {
            throw new ValidationException(nameof(projectionName), "Projection name is required.");
        }

        if (eventId == Guid.Empty)
        {
            throw new ValidationException(nameof(eventId), "Event ID is required.");
        }

        if (string.IsNullOrWhiteSpace(eventName))
        {
            throw new ValidationException(nameof(eventName), "Event name is required.");
        }

        return new AnalyticsEventDeduplicationRecord
        {
            Id = Guid.NewGuid(),
            ProjectionName = projectionName.Trim(),
            EventId = eventId,
            EventName = eventName.Trim(),
            ProcessedAtUtc = DateTime.UtcNow
        };
    }
}

