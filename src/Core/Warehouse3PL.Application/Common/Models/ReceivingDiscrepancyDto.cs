using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

public class ReceivingDiscrepancyDto
{
    public Guid Id { get; set; }
    public Guid ReceivingSessionId { get; set; }
    public Guid? ReceiptLineId { get; set; }
    public string? Lpn { get; set; }
    public DiscrepancyType Type { get; set; }
    public decimal DiscrepantQuantity { get; set; }
    public string Notes { get; set; } = string.Empty;
    public DiscrepancyResolutionStatus Status { get; set; }
    public DateTime ReportedAtUtc { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public string? ResolutionNotes { get; set; }
    public uint Version { get; set; }
}

