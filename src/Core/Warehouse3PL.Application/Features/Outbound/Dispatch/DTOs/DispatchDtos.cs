using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Outbound.Dispatch.DTOs;

public sealed record AssignShipmentDriverResult
{
    public bool IsSuccess { get; init; }
    public Guid ShipmentId { get; init; }
    public string ShipmentNumber { get; init; } = string.Empty;
    public ShipmentStatus ShipmentStatus { get; init; }
    public Guid? AssignedDriverUserId { get; init; }
    public string? AssignedDriverName { get; init; }
    public string? AssignedDriverEmail { get; init; }
    public uint Version { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed record DispatchBoardPackageDto
{
    public Guid PackageId { get; init; }
    public string PackageNumber { get; init; } = string.Empty;
    public string? TrackingNumber { get; init; }
    public string? CarrierCode { get; init; }
    public PackageStatus PackageStatus { get; init; }
    public TrackingMilestoneStatus TrackingStatus { get; init; }
    public DateTime? DeliveredAtUtc { get; init; }
    public Guid OutboundOrderId { get; init; }
    public string? OrderNumber { get; init; }
    public bool HasActivePod { get; init; }
}

public sealed record DispatchBoardShipmentDto
{
    public Guid ShipmentId { get; init; }
    public string ShipmentNumber { get; init; } = string.Empty;
    public Guid ClientId { get; init; }
    public string? ClientName { get; init; }
    public Guid FacilityId { get; init; }
    public string? FacilityName { get; init; }
    public string CarrierCode { get; init; } = string.Empty;
    public CarrierServiceLevel CarrierServiceLevel { get; init; }
    public ShipmentStatus Status { get; init; }
    public TrackingMilestoneStatus DeliveryStatus { get; init; }
    public DateTime? DeliveryStatusUpdatedAtUtc { get; init; }
    public DateTime? DispatchedAtUtc { get; init; }
    public DateTime? DeliveredAtUtc { get; init; }
    public Guid? AssignedDriverUserId { get; init; }
    public string? AssignedDriverName { get; init; }
    public string? AssignedDriverEmail { get; init; }
    public string? TrailerNumber { get; init; }
    public string? SealNumber { get; init; }
    public string? BillOfLadingNumber { get; init; }
    public int TotalPackagesCount { get; init; }
    public int DeliveredPackagesCount { get; init; }
    public int OpenExceptionsCount { get; init; }
    public decimal TotalWeightKg { get; init; }
    public uint Version { get; init; }
    public DateTime? LatestCustodyTransferredAtUtc { get; init; }
    public IReadOnlyList<DispatchBoardPackageDto> Packages { get; init; } = [];
}

public sealed record DeliveryExceptionQueueItemDto
{
    public Guid ExceptionId { get; init; }
    public Guid ClientId { get; init; }
    public Guid FacilityId { get; init; }
    public Guid PackageId { get; init; }
    public string? PackageNumber { get; init; }
    public Guid? ShipmentId { get; init; }
    public string? ShipmentNumber { get; init; }
    public Guid? OrderId { get; init; }
    public string? OrderNumber { get; init; }
    public string TrackingNumber { get; init; } = string.Empty;
    public string CarrierCode { get; init; } = string.Empty;
    public DeliveryExceptionType ExceptionType { get; init; }
    public DeliveryExceptionStatus Status { get; init; }
    public DeliveryExceptionSeverity Severity { get; init; }
    public DeliveryExceptionResolution Resolution { get; init; }
    public DeliveryExceptionSource Source { get; init; }
    public string CarrierExceptionCode { get; init; } = string.Empty;
    public string CarrierDescription { get; init; } = string.Empty;
    public DateTime OccurredAtUtc { get; init; }
    public int AttemptNumber { get; init; }
    public int ReattemptCount { get; init; }
    public DateTime? ScheduledReattemptDateUtc { get; init; }
    public int ContactAttemptsCount { get; init; }
    public string? CorrectedAddress { get; init; }
    public string? HoldDepotLocation { get; init; }
    public Guid? ReportedByUserId { get; init; }
    public DateTime? ResolvedAtUtc { get; init; }
    public Guid? ResolvedByUserId { get; init; }
    public string? ResolutionNotes { get; init; }
    public DateTime CreatedAt { get; init; }
}
