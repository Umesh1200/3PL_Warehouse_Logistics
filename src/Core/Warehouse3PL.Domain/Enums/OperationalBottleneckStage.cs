namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Operational fulfillment stages evaluated for throughput congestion and queue bottlenecks.
/// DB-SPEC-3PL-2026 Phase 6.1 Executive Control Tower &amp; Real-Time Visibility.
/// </summary>
public enum OperationalBottleneckStage
{
    /// <summary>Inbound trailers queueing in yard exceeding dock unloading capacity.</summary>
    ReceivingCongestion = 1,

    /// <summary>Allocated orders awaiting wave release or active pick execution backlog.</summary>
    PickingBacklog = 2,

    /// <summary>Picked units staged at pack stations exceeding packing station throughput.</summary>
    PackingDelays = 3,

    /// <summary>Packed cartons staged at shipping dock awaiting carrier pickup or manifest.</summary>
    DispatchDelays = 4
}

