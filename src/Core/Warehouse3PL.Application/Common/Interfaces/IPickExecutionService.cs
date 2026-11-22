using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

public class ConfirmPickRequest
{
    public Guid PickTaskId { get; set; }
    public string ScannedLocationBarcode { get; set; } = string.Empty;
    public string ScannedItemBarcode { get; set; } = string.Empty;
    public string? ScannedLotNumber { get; set; }
    public string ScannedToteLpn { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public Guid UserId { get; set; }
    public string? IdempotencyKey { get; set; }
    public bool RequireAssignedOwnership { get; set; } = false;
    public bool AllowSupervisorOverride { get; set; } = false;
    public string? OverrideReason { get; set; }
}

public class PickConfirmationResult
{
    public bool IsSuccess { get; set; }
    public Guid PickTaskId { get; set; }
    public PickTaskStatus TaskStatus { get; set; }
    public decimal QuantityPicked { get; set; }
    public decimal ShortPickQuantity { get; set; }
    public Guid DestinationStockId { get; set; }
    public Guid DestinationHandlingUnitId { get; set; }
    public string DestinationToteBarcode { get; set; } = string.Empty;
    public long InventoryTransactionId { get; set; }
    public bool WaveCompleted { get; set; }
    public bool OrderCompleted { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class ReportShortPickRequest
{
    public Guid PickTaskId { get; set; }
    public decimal QuantityPicked { get; set; }
    public ShortPickReason Reason { get; set; }
    public string? Notes { get; set; }
    public string? ScannedLocationBarcode { get; set; }
    public string? ScannedItemBarcode { get; set; }
    public string? ScannedToteLpn { get; set; }
    public Guid UserId { get; set; }
    public string? IdempotencyKey { get; set; }
    public bool RequireAssignedOwnership { get; set; } = false;
    public bool AllowSupervisorOverride { get; set; } = false;
    public string? OverrideReason { get; set; }
}

public class ShortPickExecutionResult
{
    public bool IsSuccess { get; set; }
    public Guid PickTaskId { get; set; }
    public PickTaskStatus TaskStatus { get; set; }
    public decimal QuantityPicked { get; set; }
    public decimal DiscrepancyQuantity { get; set; }
    public Guid PickExceptionId { get; set; }
    public Guid? GeneratedCycleCountId { get; set; }
    public bool LocationLockApplied { get; set; }
    public string Message { get; set; } = string.Empty;
}

public interface IPickExecutionService
{
    Task<PickConfirmationResult> ConfirmPickAsync(
        ConfirmPickRequest request,
        CancellationToken cancellationToken = default);

    Task<ShortPickExecutionResult> ReportShortPickAsync(
        ReportShortPickRequest request,
        CancellationToken cancellationToken = default);
}

