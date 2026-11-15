namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Operational replay modes for rebuilding analytics read models and projections.
/// DB-SPEC-3PL-2026 Phase 6.0 Analytics Foundation &amp; KPI Engine.
/// </summary>
public enum ProjectionReplayMode
{
    /// <summary>
    /// Rebuilds read models from the beginning of recorded event history (t = 0).
    /// </summary>
    FromBeginning = 1,

    /// <summary>
    /// Resumes processing from the last confirmed checkpoint watermark.
    /// </summary>
    FromWatermark = 2,

    /// <summary>
    /// Selectively replays and recalculates aggregations within an explicit date/time window.
    /// </summary>
    DateRange = 3
}

