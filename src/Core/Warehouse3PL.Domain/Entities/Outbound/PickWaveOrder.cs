using Warehouse3PL.Domain.Common;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Associative join entity grouping an OutboundOrder within a PickWave with designated execution sequence.
/// DB-SPEC-3PL-2026 Phase 4.3 Picking & Warehouse Execution.
/// </summary>
public class PickWaveOrder : BaseEntity<Guid>
{
    public Guid PickWaveId { get; set; }
    public Guid OutboundOrderId { get; set; }
    public int SequenceNumber { get; set; }

    // Navigations
    public PickWave PickWave { get; set; } = null!;
    public OutboundOrder OutboundOrder { get; set; } = null!;

    public PickWaveOrder()
    {
        Id = Guid.NewGuid();
    }
}

