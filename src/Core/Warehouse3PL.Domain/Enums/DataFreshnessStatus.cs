namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Metric data freshness status exposing projection latency and staleness to dashboards.
/// DB-SPEC-3PL-2026 Phase 6.1 Executive Control Tower &amp; Real-Time Visibility.
/// </summary>
public enum DataFreshnessStatus
{
    /// <summary>Projection updated within the last 2 minutes.</summary>
    Live = 1,

    /// <summary>Projection updated within 2 to 15 minutes.</summary>
    NearRealTime = 2,

    /// <summary>Projection delayed between 15 and 60 minutes.</summary>
    Delayed = 3,

    /// <summary>Projection has not received updates for over 60 minutes (stale data warning).</summary>
    Stale = 4
}

