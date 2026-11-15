using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

public class CycleCountDto
{
    public Guid Id { get; set; }
    public Guid FacilityId { get; set; }
    public Guid? ZoneId { get; set; }
    public string CountNumber { get; set; } = string.Empty;
    public CycleCountType CountType { get; set; }
    public CycleCountStatus Status { get; set; }
    public DateTime ScheduledDate { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Notes { get; set; }
    public int TotalItemsCount { get; set; }
    public int DiscrepancyCount { get; set; }
    public List<CycleCountItemDto> Items { get; set; } = [];
}

public class CycleCountItemDto
{
    public Guid Id { get; set; }
    public Guid LocationId { get; set; }
    public string LocationCode { get; set; } = string.Empty;
    public Guid ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public Guid? LotId { get; set; }
    public string? LotNumber { get; set; }
    public decimal ExpectedQty { get; set; }
    public decimal? CountedQty { get; set; }
    public decimal? VarianceQty { get; set; }
    public bool IsReconciled { get; set; }
    public DateTime? CountedAt { get; set; }
}

