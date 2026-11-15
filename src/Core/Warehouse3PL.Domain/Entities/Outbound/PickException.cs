using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Audit exception log capturing short-picks, damaged items, and missing inventory during pick execution.
/// Seamlessly integrates with the existing CycleCount module and LocationLock mechanism based on discrepancy severity.
/// DB-SPEC-3PL-2026 Phase 4.3 Picking & Warehouse Execution.
/// </summary>
public class PickException : BaseEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }

    public Guid PickTaskId { get; set; }
    public Guid OutboundOrderId { get; set; }
    public Guid OutboundOrderLineId { get; set; }

    public Guid LocationId { get; set; }
    public Guid ItemId { get; set; }
    public Guid? LotId { get; set; }

    public decimal ExpectedQuantity { get; set; }
    public decimal ActualQuantityPicked { get; set; }
    public decimal DiscrepancyQuantity => Math.Max(0m, ExpectedQuantity - ActualQuantityPicked);

    public ShortPickReason Reason { get; set; } = ShortPickReason.BinEmpty;
    public string? Notes { get; set; }

    public Guid ReportedByUserId { get; set; }
    public DateTime ReportedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Direct link to the automatically generated CycleCount investigation task.
    /// </summary>
    public Guid? GeneratedCycleCountId { get; set; }

    /// <summary>
    /// Indicates whether an operational LocationLock was applied to the bin based on discrepancy severity.
    /// </summary>
    public bool LocationLockApplied { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public PickTask PickTask { get; set; } = null!;
    public OutboundOrder OutboundOrder { get; set; } = null!;
    public OutboundOrderLine OutboundOrderLine { get; set; } = null!;
    public Location Location { get; set; } = null!;
    public Item Item { get; set; } = null!;
    public InventoryLot? Lot { get; set; }
    public CycleCount? GeneratedCycleCount { get; set; }

    public PickException()
    {
        Id = Guid.NewGuid();
    }
}

