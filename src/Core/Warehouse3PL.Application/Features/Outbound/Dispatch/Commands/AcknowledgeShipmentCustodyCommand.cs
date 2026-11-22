using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.Outbound.Dispatch.DTOs;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Dispatch.Commands;

/// <summary>
/// Completes physical custody handover acknowledgment by the newly assigned authenticated Driver
/// after a supervisor reassignment on a dispatched shipment.
/// </summary>
public record AcknowledgeShipmentCustodyCommand(
    Guid ShipmentId,
    string DriverSignature,
    string? DriverLicenseNumber = null,
    string? TrailerNumber = null,
    string? SealNumber = null,
    uint? ExpectedVersion = null,
    string? Notes = null) : IRequest<AcknowledgeShipmentCustodyResult>;

public sealed class AcknowledgeShipmentCustodyCommandHandler : IRequestHandler<AcknowledgeShipmentCustodyCommand, AcknowledgeShipmentCustodyResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public AcknowledgeShipmentCustodyCommandHandler(
        IApplicationDbContext context,
        ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<AcknowledgeShipmentCustodyResult> Handle(
        AcknowledgeShipmentCustodyCommand command,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw new UnauthorizedException("Authentication is required to acknowledge shipment custody.");
        }

        if (_currentUser.UserType != UserType.Driver || !_currentUser.HasPermission(Permissions.Dispatch.ExecuteDelivery))
        {
            throw new ForbiddenException("Only an authorized Driver with 'dispatch:execute-delivery' may acknowledge custody handover.");
        }

        if (!_currentUser.UserId.HasValue || _currentUser.UserId.Value == Guid.Empty)
        {
            throw new ForbiddenException("Authenticated driver identity is required.");
        }

        var trimmedSignature = command.DriverSignature?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedSignature) || trimmedSignature.Length < 2 ||
            trimmedSignature.StartsWith("SUPERVISOR_CUSTODY_REASSIGNMENT:", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(
                nameof(command.DriverSignature),
                "A valid driver signature acknowledgment is required.");
        }

        var driverUserId = _currentUser.UserId.Value;

        return await _context.ExecuteWithShipmentLockAsync(command.ShipmentId, async () =>
        {
            var shipment = await _context.Shipments
                .IgnoreQueryFilters()
                .Include(s => s.Packages)
                .FirstOrDefaultAsync(s => s.Id == command.ShipmentId && !s.IsDeleted, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Shipment), command.ShipmentId);

            if (shipment.Status != ShipmentStatus.Dispatched)
            {
                throw new InvalidDomainStateException(
                    $"Custody handover acknowledgment requires shipment status 'Dispatched' (current: '{shipment.Status}').");
            }

            if (!shipment.AssignedDriverUserId.HasValue || shipment.AssignedDriverUserId.Value != driverUserId)
            {
                throw new ForbiddenException(
                    $"Driver '{driverUserId}' is not assigned to shipment '{shipment.ShipmentNumber}'.");
            }

            var driverUser = await OutboundOperationalAuthorization.ValidateAssignableDriverAsync(
                _context,
                driverUserId,
                shipment.ClientId,
                shipment.FacilityId,
                cancellationToken);

            if (command.ExpectedVersion.HasValue && command.ExpectedVersion.Value != shipment.Version)
            {
                throw new ConcurrencyException(
                    $"Shipment '{shipment.ShipmentNumber}' was modified concurrently (expected version {command.ExpectedVersion.Value}, current version {shipment.Version}).");
            }

            if (shipment.DeliveryStatus == TrackingMilestoneStatus.Delivered ||
                shipment.Packages.Any(p => !p.IsDeleted && p.CurrentTrackingStatus == TrackingMilestoneStatus.Delivered))
            {
                throw new InvalidDomainStateException(
                    $"Cannot acknowledge custody handover after delivery on shipment '{shipment.ShipmentNumber}'.");
            }

            if (shipment.CustodyStatus != ShipmentCustodyStatus.PendingHandover)
            {
                throw new InvalidDomainStateException(
                    $"Shipment '{shipment.ShipmentNumber}' is not awaiting driver handover acknowledgment (custody status: '{shipment.CustodyStatus}').");
            }

            var now = DateTime.UtcNow;
            var driverFullName = $"{driverUser.FirstName} {driverUser.LastName}".Trim();

            var transfer = new ShipmentCustodyTransfer
            {
                ClientId = shipment.ClientId,
                FacilityId = shipment.FacilityId,
                ShipmentId = shipment.Id,
                CarrierCode = shipment.CarrierCode,
                DriverName = string.IsNullOrWhiteSpace(driverFullName) ? driverUser.Email : driverFullName,
                DriverLicenseNumber = command.DriverLicenseNumber?.Trim(),
                DriverSignatureAcknowledgment = trimmedSignature,
                TrailerNumber = string.IsNullOrWhiteSpace(command.TrailerNumber) ? shipment.TrailerNumber : command.TrailerNumber.Trim(),
                SealNumber = string.IsNullOrWhiteSpace(command.SealNumber) ? shipment.SealNumber : command.SealNumber.Trim(),
                BillOfLadingNumber = shipment.BillOfLadingNumber,
                TotalPackagesConfirmed = shipment.TotalPackagesCount,
                TotalWeightKg = shipment.TotalWeightKg,
                AuthorizedByUserId = driverUserId,
                AssignedDriverUserId = driverUserId,
                CustodyStatus = ShipmentCustodyStatus.Confirmed,
                TransferredAtUtc = now,
                Notes = command.Notes?.Trim()
            };

            _context.ShipmentCustodyTransfers.Add(transfer);

            shipment.CustodyStatus = ShipmentCustodyStatus.Confirmed;
            shipment.TrailerNumber = transfer.TrailerNumber;
            shipment.SealNumber = transfer.SealNumber;
            shipment.Version++;

            _context.ShipmentAuditLogs.Add(new ShipmentAuditLog
            {
                ClientId = shipment.ClientId,
                FacilityId = shipment.FacilityId,
                ShipmentId = shipment.Id,
                Action = "DriverCustodyAcknowledged",
                Details = $"Confirmed handover by Driver '{driverUserId}' ({transfer.DriverName}), TransferId={transfer.Id}",
                UserId = driverUserId,
                TimestampUtc = now
            });

            await _context.SaveChangesAsync(cancellationToken);

            return new AcknowledgeShipmentCustodyResult
            {
                IsSuccess = true,
                ShipmentId = shipment.Id,
                ShipmentNumber = shipment.ShipmentNumber,
                AssignedDriverUserId = driverUserId,
                CustodyStatus = shipment.CustodyStatus,
                CustodyTransferId = transfer.Id,
                TransferredAtUtc = now,
                Version = shipment.Version,
                Message = "Shipment custody handover acknowledged successfully."
            };
        }, cancellationToken);
    }
}
