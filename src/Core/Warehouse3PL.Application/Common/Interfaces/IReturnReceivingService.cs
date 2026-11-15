using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

public class InitiateReturnReceivingRequest
{
    public Guid ReturnInboundHandoffId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid OperatorUserId { get; set; }
    public string? DockLocation { get; set; }
    public string? Notes { get; set; }
}

public class ConfirmPhysicalArrivalRequest
{
    public Guid SessionId { get; set; }
    public Guid OperatorUserId { get; set; }
    public string ScannedBarcode { get; set; } = string.Empty;
    public ReturnPackageCondition PackageCondition { get; set; } = ReturnPackageCondition.Intact;
    public string? ConditionNotes { get; set; }
}

public class VerifyReceivedLineItem
{
    public Guid LineId { get; set; }
    public decimal ReceivedQuantity { get; set; }
}

public class UnannouncedReturnItem
{
    public string Sku { get; set; } = string.Empty;
    public decimal ReceivedQuantity { get; set; }
    public string? Notes { get; set; }
}

public class VerifyReceivedQuantitiesRequest
{
    public Guid SessionId { get; set; }
    public Guid OperatorUserId { get; set; }
    public IReadOnlyList<VerifyReceivedLineItem> Lines { get; set; } = [];
    public IReadOnlyList<UnannouncedReturnItem>? UnannouncedItems { get; set; }
}

public class InspectLineDispositionItem
{
    public Guid LineId { get; set; }
    public decimal AcceptedQuantity { get; set; }
    public decimal DamagedQuantity { get; set; }
    public decimal QuarantinedQuantity { get; set; }
    public ReturnDisposition Disposition { get; set; }
    public Guid? TargetLocationId { get; set; }
    public string? InspectionNotes { get; set; }
}

public class InspectAndAssignDispositionRequest
{
    public Guid SessionId { get; set; }
    public Guid OperatorUserId { get; set; }
    public IReadOnlyList<InspectLineDispositionItem> Dispositions { get; set; } = [];
}

public class PostReturnInventoryRequest
{
    public Guid SessionId { get; set; }
    public Guid OperatorUserId { get; set; }
    public Guid? DefaultLocationId { get; set; }
}

public class CompleteReturnReceivingRequest
{
    public Guid SessionId { get; set; }
    public Guid OperatorUserId { get; set; }
    public string? Notes { get; set; }
}

public class ReturnReceivingSessionDto
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid ReturnInboundHandoffId { get; set; }
    public Guid ReturnShipmentId { get; set; }
    public Guid ReturnAuthorizationId { get; set; }
    public Guid OrderId { get; set; }
    public Guid PackageId { get; set; }
    public string SessionNumber { get; set; } = string.Empty;
    public ReturnReceivingSessionStatus Status { get; set; }
    public Guid ReceivedByUserId { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? PhysicalArrivalAtUtc { get; set; }
    public DateTime? InspectedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public ReturnPackageCondition PackageCondition { get; set; }
    public string? PackageConditionNotes { get; set; }
    public bool IsInventoryPosted { get; set; }
    public string? Notes { get; set; }
    public IReadOnlyList<ReturnReceivingLineDto> Lines { get; set; } = [];
    public IReadOnlyList<ReturnReceivingDiscrepancyDto> Discrepancies { get; set; } = [];
}

public class ReturnReceivingLineDto
{
    public Guid Id { get; set; }
    public Guid ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal ExpectedQuantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal AcceptedQuantity { get; set; }
    public decimal DamagedQuantity { get; set; }
    public decimal QuarantinedQuantity { get; set; }
    public ReturnDisposition? Disposition { get; set; }
    public Guid? TargetLocationId { get; set; }
    public string? InspectionNotes { get; set; }
    public bool IsInventoryPosted { get; set; }
    public long? InventoryTransactionId { get; set; }
}

public class ReturnReceivingDiscrepancyDto
{
    public Guid Id { get; set; }
    public Guid? ItemId { get; set; }
    public string? ExpectedSku { get; set; }
    public string? ActualSku { get; set; }
    public ReturnDiscrepancyType DiscrepancyType { get; set; }
    public decimal ExpectedQuantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal DiscrepancyQuantity { get; set; }
    public string Notes { get; set; } = string.Empty;
    public DateTime ReportedAtUtc { get; set; }
}

public class PostReturnInventoryResult
{
    public bool IsSuccess { get; set; }
    public Guid SessionId { get; set; }
    public int TotalItemsPosted { get; set; }
    public decimal TotalAcceptedQuantity { get; set; }
    public decimal TotalDamagedQuantity { get; set; }
    public decimal TotalQuarantinedQuantity { get; set; }
    public IReadOnlyList<long> InventoryTransactionIds { get; set; } = [];
    public string Message { get; set; } = string.Empty;
}

public class CompleteReturnReceivingResult
{
    public bool IsSuccess { get; set; }
    public Guid SessionId { get; set; }
    public ReturnReceivingSessionStatus SessionStatus { get; set; }
    public ReturnInboundHandoffStatus HandoffStatus { get; set; }
    public ReturnShipmentStatus ReturnShipmentStatus { get; set; }
    public ReturnAuthorizationStatus ReturnAuthorizationStatus { get; set; }
    public OutboundOrderStatus OutboundOrderStatus { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Service managing the warehouse inbound dock physical receiving, package scan,
/// quantity verification, quality inspection, inventory disposition, and stock ledger posting for returned parcels.
/// DB-SPEC-3PL-2026 Phase 4.5 Step 3B.4.5: Return Facility Receiving &amp; Inspection.
/// </summary>
public interface IReturnReceivingService
{
    /// <summary>
    /// Initiates a return receiving session for an authorized inbound handoff at a facility dock.
    /// Decoupled Step 1.
    /// </summary>
    Task<ReturnReceivingSessionDto> InitiateReturnReceivingAsync(
        InitiateReturnReceivingRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirms physical package arrival at the dock, validates package barcode/tracking/RMA, and records condition.
    /// Decoupled Step 2.
    /// </summary>
    Task<ReturnReceivingSessionDto> ConfirmPhysicalArrivalAsync(
        ConfirmPhysicalArrivalRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies physically received line quantities against expected manifest, logging shortages, overages, and unannounced items.
    /// Decoupled Step 3.
    /// </summary>
    Task<ReturnReceivingSessionDto> VerifyReceivedQuantitiesAsync(
        VerifyReceivedQuantitiesRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs quality inspection per line item, assigns disposition, and verifies quantity conservation.
    /// Decoupled Step 4.
    /// </summary>
    Task<ReturnReceivingSessionDto> InspectAndAssignDispositionAsync(
        InspectAndAssignDispositionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Posts accepted, damaged, and quarantined quantities into the warehouse inventory ledger and updates stock on hand.
    /// Enforces duplicate-posting protection and quantity conservation.
    /// Decoupled Step 5.
    /// </summary>
    Task<PostReturnInventoryResult> PostReturnInventoryAsync(
        PostReturnInventoryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes the return receiving session and finalizes inbound handoff, return shipment, and authorization status.
    /// Decoupled Step 6.
    /// </summary>
    Task<CompleteReturnReceivingResult> CompleteReturnReceivingAsync(
        CompleteReturnReceivingRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a return receiving session by primary key ID.
    /// </summary>
    Task<ReturnReceivingSessionDto?> GetSessionByIdAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a return receiving session by associated inbound handoff ID.
    /// </summary>
    Task<ReturnReceivingSessionDto?> GetSessionByHandoffIdAsync(
        Guid handoffId,
        CancellationToken cancellationToken = default);
}

