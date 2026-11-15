using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Domain aggregate governing the physical intake, package scan, quantity verification,
/// quality inspection, and inventory disposition for returned goods at a receiving facility dock.
/// DB-SPEC-3PL-2026 Phase 4.5 Step 3B.4.5: Return Facility Receiving &amp; Inspection.
/// </summary>
public class ReturnReceivingSession : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid ReturnInboundHandoffId { get; set; }
    public Guid ReturnShipmentId { get; set; }
    public Guid ReturnAuthorizationId { get; set; }
    public Guid OrderId { get; set; }
    public Guid PackageId { get; set; }

    public string SessionNumber { get; set; } = string.Empty;
    public ReturnReceivingSessionStatus Status { get; set; } = ReturnReceivingSessionStatus.Started;

    public Guid ReceivedByUserId { get; set; }
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PhysicalArrivalAtUtc { get; set; }
    public DateTime? InspectedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public ReturnPackageCondition PackageCondition { get; set; } = ReturnPackageCondition.Intact;
    public string? PackageConditionNotes { get; set; }

    public bool IsInventoryPosted { get; set; } = false;
    public string? Notes { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public ReturnInboundHandoff ReturnInboundHandoff { get; set; } = null!;
    public ReturnShipment ReturnShipment { get; set; } = null!;
    public ReturnAuthorization ReturnAuthorization { get; set; } = null!;
    public User ReceivedByUser { get; set; } = null!;

    public ICollection<ReturnReceivingLine> Lines { get; set; } = [];
    public ICollection<ReturnReceivingDiscrepancy> Discrepancies { get; set; } = [];

    public ReturnReceivingSession()
    {
        Id = Guid.NewGuid();
    }

    /// <summary>
    /// Factory method to construct and initialize a valid ReturnReceivingSession.
    /// </summary>
    public static ReturnReceivingSession Create(
        Guid clientId,
        Guid facilityId,
        Guid returnInboundHandoffId,
        Guid returnShipmentId,
        Guid returnAuthorizationId,
        Guid orderId,
        Guid packageId,
        string sessionNumber,
        Guid operatorUserId,
        string? notes = null)
    {
        if (clientId == Guid.Empty)
        {
            throw new ValidationException(nameof(clientId), "Client ID is required.");
        }

        if (facilityId == Guid.Empty)
        {
            throw new ValidationException(nameof(facilityId), "Facility ID is required.");
        }

        if (returnInboundHandoffId == Guid.Empty)
        {
            throw new ValidationException(nameof(returnInboundHandoffId), "Return inbound handoff ID is required.");
        }

        if (returnShipmentId == Guid.Empty)
        {
            throw new ValidationException(nameof(returnShipmentId), "Return shipment ID is required.");
        }

        if (returnAuthorizationId == Guid.Empty)
        {
            throw new ValidationException(nameof(returnAuthorizationId), "Return authorization ID is required.");
        }

        if (orderId == Guid.Empty)
        {
            throw new ValidationException(nameof(orderId), "Order ID is required.");
        }

        if (packageId == Guid.Empty)
        {
            throw new ValidationException(nameof(packageId), "Package ID is required.");
        }

        if (string.IsNullOrWhiteSpace(sessionNumber))
        {
            throw new ValidationException(nameof(sessionNumber), "Session number is required.");
        }

        if (operatorUserId == Guid.Empty)
        {
            throw new ValidationException(nameof(operatorUserId), "Operator user ID is required.");
        }

        return new ReturnReceivingSession
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            FacilityId = facilityId,
            ReturnInboundHandoffId = returnInboundHandoffId,
            ReturnShipmentId = returnShipmentId,
            ReturnAuthorizationId = returnAuthorizationId,
            OrderId = orderId,
            PackageId = packageId,
            SessionNumber = sessionNumber.Trim().ToUpperInvariant(),
            ReceivedByUserId = operatorUserId,
            StartedAtUtc = DateTime.UtcNow,
            Status = ReturnReceivingSessionStatus.Started,
            Notes = notes?.Trim()
        };
    }

    /// <summary>
    /// Confirms physical package arrival at the dock and records carton condition.
    /// </summary>
    public void ConfirmPhysicalArrival(
        Guid operatorUserId,
        DateTime arrivalTimeUtc,
        ReturnPackageCondition condition,
        string? conditionNotes = null)
    {
        if (Status != ReturnReceivingSessionStatus.Started)
        {
            throw new InvalidDomainStateException($"Cannot confirm physical arrival in '{Status}' status.");
        }

        if (operatorUserId == Guid.Empty)
        {
            throw new ValidationException(nameof(operatorUserId), "Operator user ID is required.");
        }

        ReceivedByUserId = operatorUserId;
        PhysicalArrivalAtUtc = arrivalTimeUtc;
        PackageCondition = condition;
        PackageConditionNotes = conditionNotes?.Trim();
        Status = ReturnReceivingSessionStatus.PhysicalReceiptConfirmed;
    }

    /// <summary>
    /// Marks the session as inspected after all received items have undergone quality inspection and disposition.
    /// </summary>
    public void MarkInspected(DateTime inspectedAtUtc)
    {
        if (Status != ReturnReceivingSessionStatus.PhysicalReceiptConfirmed)
        {
            throw new InvalidDomainStateException($"Cannot mark inspected in '{Status}' status.");
        }

        InspectedAtUtc = inspectedAtUtc;
        Status = ReturnReceivingSessionStatus.Inspected;
    }

    /// <summary>
    /// Records that inventory ledger posting has completed for this receiving session.
    /// Prevents duplicate ledger posting.
    /// </summary>
    public void MarkInventoryPosted()
    {
        if (IsInventoryPosted)
        {
            throw new InvalidDomainStateException("Inventory has already been posted for this return receiving session.");
        }

        if (Status != ReturnReceivingSessionStatus.Inspected)
        {
            throw new InvalidDomainStateException("Cannot post inventory before quality inspection and disposition are completed.");
        }

        IsInventoryPosted = true;
    }

    /// <summary>
    /// Completes the return receiving workflow.
    /// </summary>
    public void Complete(DateTime completedAtUtc)
    {
        if (Status == ReturnReceivingSessionStatus.Completed)
        {
            return;
        }

        if (Status != ReturnReceivingSessionStatus.Inspected)
        {
            throw new InvalidDomainStateException($"Cannot complete return receiving in '{Status}' status. Inspection required.");
        }

        CompletedAtUtc = completedAtUtc;
        Status = ReturnReceivingSessionStatus.Completed;
    }

    /// <summary>
    /// Cancels the receiving session prior to completion.
    /// </summary>
    public void Cancel(string reason)
    {
        if (Status == ReturnReceivingSessionStatus.Completed)
        {
            throw new InvalidDomainStateException("Cannot cancel an already completed return receiving session.");
        }

        if (IsInventoryPosted)
        {
            throw new InvalidDomainStateException("Cannot cancel a receiving session whose inventory has already been posted to the warehouse ledger.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException(nameof(reason), "Cancellation reason is required.");
        }

        Status = ReturnReceivingSessionStatus.Cancelled;
        Notes = string.IsNullOrWhiteSpace(Notes) ? $"Cancelled: {reason.Trim()}" : $"{Notes}\nCancelled: {reason.Trim()}";
    }
}

