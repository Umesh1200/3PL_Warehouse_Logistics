using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.Outbound.Dispatch.DTOs;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Dispatch.Commands;

/// <summary>
/// Assigns, reassigns (pending handover acknowledgment when dispatched), or revokes
/// the courier driver identity bound to an outbound shipment under shipment row locking
/// and Shipment.Version optimistic concurrency.
/// </summary>
public record AssignShipmentDriverCommand(
    Guid ShipmentId,
    Guid? DriverUserId,
    uint? ExpectedVersion = null,
    string? Reason = null) : IRequest<AssignShipmentDriverResult>;

public sealed class AssignShipmentDriverCommandHandler : IRequestHandler<AssignShipmentDriverCommand, AssignShipmentDriverResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public AssignShipmentDriverCommandHandler(
        IApplicationDbContext context,
        ICurrentUserContext currentUser,
        OutboundOperationalAuthorization outboundAuth)
    {
        _context = context;
        _currentUser = currentUser;
        _outboundAuth = outboundAuth;
    }

    public async Task<AssignShipmentDriverResult> Handle(
        AssignShipmentDriverCommand command,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(AssignShipmentDriverCommand));

        return await _context.ExecuteWithShipmentLockAsync(command.ShipmentId, async () =>
        {
            var shipment = await _context.Shipments
                .IgnoreQueryFilters()
                .Include(s => s.Packages)
                .FirstOrDefaultAsync(s => s.Id == command.ShipmentId && !s.IsDeleted, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Shipment), command.ShipmentId);

            _outboundAuth.ValidateStaffMutationFacilityAccess(shipment.FacilityId, nameof(AssignShipmentDriverCommand));

            if (command.ExpectedVersion.HasValue && command.ExpectedVersion.Value != shipment.Version)
            {
                throw new ConcurrencyException(
                    $"Shipment '{shipment.ShipmentNumber}' was modified concurrently (expected version {command.ExpectedVersion.Value}, current version {shipment.Version}).");
            }

            if (shipment.Status == ShipmentStatus.Cancelled)
            {
                throw new InvalidDomainStateException(
                    $"Cannot assign a driver to cancelled shipment '{shipment.ShipmentNumber}'.");
            }

            if (shipment.DeliveryStatus == TrackingMilestoneStatus.Delivered ||
                shipment.Packages.Any(p => !p.IsDeleted && p.CurrentTrackingStatus == TrackingMilestoneStatus.Delivered))
            {
                throw new InvalidDomainStateException(
                    $"Cannot reassign or revoke driver custody after delivery on shipment '{shipment.ShipmentNumber}'.");
            }

            // Disallow un-audited driver custody reassignment or revocation once shipment is already dispatched.
            if (shipment.Status == ShipmentStatus.Dispatched &&
                shipment.AssignedDriverUserId != command.DriverUserId &&
                string.IsNullOrWhiteSpace(command.Reason))
            {
                throw new InvalidDomainStateException(
                    $"An explicit audit reason is required to reassign or revoke driver custody on dispatched shipment '{shipment.ShipmentNumber}'.");
            }

            string? driverName = null;
            string? driverEmail = null;

            if (command.DriverUserId.HasValue)
            {
                var driverUser = await OutboundOperationalAuthorization.ValidateAssignableDriverAsync(
                    _context,
                    command.DriverUserId.Value,
                    shipment.ClientId,
                    shipment.FacilityId,
                    cancellationToken);

                driverName = $"{driverUser.FirstName} {driverUser.LastName}".Trim();
                driverEmail = driverUser.Email;
            }

            var previousDriverId = shipment.AssignedDriverUserId;
            var changed = previousDriverId != command.DriverUserId;

            if (!changed)
            {
                return new AssignShipmentDriverResult
                {
                    IsSuccess = true,
                    ShipmentId = shipment.Id,
                    ShipmentNumber = shipment.ShipmentNumber,
                    ShipmentStatus = shipment.Status,
                    CustodyStatus = shipment.CustodyStatus,
                    AssignedDriverUserId = shipment.AssignedDriverUserId,
                    AssignedDriverName = driverName,
                    AssignedDriverEmail = driverEmail,
                    Version = shipment.Version,
                    Message = "Shipment driver assignment already matches the requested driver (Idempotent)."
                };
            }

            var now = DateTime.UtcNow;
            var actorUserId = _currentUser.UserId ?? Guid.Empty;

            shipment.AssignedDriverUserId = command.DriverUserId;
            shipment.Version++;

            string action;
            if (shipment.Status == ShipmentStatus.Dispatched)
            {
                if (command.DriverUserId.HasValue)
                {
                    // Never synthesize a fake driver signature row; require explicit authenticated driver handover acknowledgment.
                    shipment.CustodyStatus = ShipmentCustodyStatus.PendingHandover;
                    action = "DriverReassignmentPendingHandover";
                }
                else
                {
                    shipment.CustodyStatus = ShipmentCustodyStatus.Revoked;
                    action = "DriverCustodyRevoked";
                }
            }
            else
            {
                shipment.CustodyStatus = ShipmentCustodyStatus.Unassigned;
                action = !command.DriverUserId.HasValue
                    ? "DriverRevoked"
                    : previousDriverId.HasValue
                        ? "DriverReassigned"
                        : "DriverAssigned";
            }

            _context.ShipmentAuditLogs.Add(new ShipmentAuditLog
            {
                ClientId = shipment.ClientId,
                FacilityId = shipment.FacilityId,
                ShipmentId = shipment.Id,
                Action = action,
                Details = $"PreviousDriver={previousDriverId?.ToString() ?? "None"}, NewDriver={command.DriverUserId?.ToString() ?? "None"}, CustodyStatus={shipment.CustodyStatus}, Reason={command.Reason?.Trim() ?? "N/A"}",
                UserId = actorUserId,
                TimestampUtc = now
            });

            await _context.SaveChangesAsync(cancellationToken);

            return new AssignShipmentDriverResult
            {
                IsSuccess = true,
                ShipmentId = shipment.Id,
                ShipmentNumber = shipment.ShipmentNumber,
                ShipmentStatus = shipment.Status,
                CustodyStatus = shipment.CustodyStatus,
                AssignedDriverUserId = shipment.AssignedDriverUserId,
                AssignedDriverName = driverName,
                AssignedDriverEmail = driverEmail,
                Version = shipment.Version,
                Message = command.DriverUserId.HasValue
                    ? (shipment.CustodyStatus == ShipmentCustodyStatus.PendingHandover
                        ? "Driver reassignment recorded; awaiting driver custody handover acknowledgment."
                        : "Driver assigned to shipment successfully.")
                    : "Driver assignment revoked successfully."
            };
        }, cancellationToken);
    }
}
