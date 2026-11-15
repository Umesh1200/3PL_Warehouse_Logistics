using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Outbound.Picking.DTOs;

public class PickWaveDto
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public string WaveNumber { get; set; } = string.Empty;
    public PickWaveType WaveType { get; set; }
    public PickWaveStatus Status { get; set; }
    public int Priority { get; set; }
    public Guid? ZoneId { get; set; }
    public StorageTempClass? TempClass { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public DateTime? ReleasedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime CreatedAt { get; set; }

    public int OrdersCount { get; set; }
    public int TasksCount { get; set; }
    public decimal TotalQuantityToPick { get; set; }
    public decimal TotalQuantityPicked { get; set; }
    public decimal EstimatedTravelMeters { get; set; }

    public IReadOnlyList<PickWaveOrderDto> WaveOrders { get; set; } = [];
    public IReadOnlyList<PickTaskDto> Tasks { get; set; } = [];
}

public class PickWaveOrderDto
{
    public Guid Id { get; set; }
    public Guid OutboundOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public int Priority { get; set; }
    public int SequenceNumber { get; set; }
    public OutboundOrderStatus OrderStatus { get; set; }
}

public class PickTaskDto
{
    public Guid Id { get; set; }
    public Guid PickWaveId { get; set; }
    public Guid OutboundOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid OutboundOrderLineId { get; set; }
    public Guid InventoryAllocationId { get; set; }
    public Guid StockOnHandId { get; set; }
    public Guid ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ItemDescription { get; set; } = string.Empty;
    public Guid SourceLocationId { get; set; }
    public string SourceLocationCode { get; set; } = string.Empty;
    public Guid? DestinationLocationId { get; set; }
    public string? DestinationLocationCode { get; set; }
    public Guid? LotId { get; set; }
    public string? LotNumber { get; set; }
    public string? ExpectedLpnCode { get; set; }
    public Guid? TargetHandlingUnitId { get; set; }
    public string? TargetToteBarcode { get; set; }
    public decimal QuantityToPick { get; set; }
    public decimal QuantityPicked { get; set; }
    public decimal ShortPickQuantity { get; set; }
    public int PickSequence { get; set; }
    public PickTaskStatus Status { get; set; }
    public ShortPickReason? ShortPickReason { get; set; }
    public Guid? AssignedUserId { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}

