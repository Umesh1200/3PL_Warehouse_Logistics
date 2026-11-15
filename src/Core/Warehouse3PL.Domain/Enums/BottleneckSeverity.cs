namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Alert severity classification for operational fulfillment stage bottlenecks.
/// DB-SPEC-3PL-2026 Phase 6.1 Executive Control Tower &amp; Real-Time Visibility.
/// </summary>
public enum BottleneckSeverity
{
    /// <summary>Queue depths and dwell times are within normal operating bounds.</summary>
    Normal = 1,

    /// <summary>Stage queue exceeds warning threshold; potential SLA risk within 1-2 hours.</summary>
    Warning = 2,

    /// <summary>Stage throughput severely congested; active SLA breaches or dock gridlock occurring.</summary>
    Critical = 3
}

