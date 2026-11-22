using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Shipping.Commands;

public record ConfirmDispatchCustodyCommand(
    Guid ShipmentId,
    string DriverName,
    string? DriverLicenseNumber,
    string DriverSignature,
    string? TrailerNumber = null,
    string? SealNumber = null,
    string? Notes = null,
    string? IdempotencyKey = null) : IRequest<ConfirmDispatchCustodyResult>;

public class ConfirmDispatchCustodyCommandHandler : IRequestHandler<ConfirmDispatchCustodyCommand, ConfirmDispatchCustodyResult>
{
    private readonly IShipmentConsolidationService _consolidationService;
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public ConfirmDispatchCustodyCommandHandler(
        IShipmentConsolidationService consolidationService,
        IApplicationDbContext context,
        ICurrentUserContext currentUser,
        OutboundOperationalAuthorization outboundAuth)
    {
        _consolidationService = consolidationService;
        _context = context;
        _currentUser = currentUser;
        _outboundAuth = outboundAuth;
    }

    public async Task<ConfirmDispatchCustodyResult> Handle(
        ConfirmDispatchCustodyCommand command,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(ConfirmDispatchCustodyCommand));

        var shipment = await _context.Shipments
            .AsNoTracking()
            .Select(s => new { s.Id, s.FacilityId })
            .FirstOrDefaultAsync(s => s.Id == command.ShipmentId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Shipment), command.ShipmentId);

        _outboundAuth.ValidateStaffMutationFacilityAccess(shipment.FacilityId, nameof(ConfirmDispatchCustodyCommand));

        return await _consolidationService.ConfirmDispatchAsync(new ConfirmDispatchCustodyRequest
        {
            ShipmentId = command.ShipmentId,
            DriverName = command.DriverName,
            DriverLicenseNumber = command.DriverLicenseNumber,
            DriverSignature = command.DriverSignature,
            TrailerNumber = command.TrailerNumber,
            SealNumber = command.SealNumber,
            AuthorizedUserId = _currentUser.UserId ?? Guid.Empty,
            Notes = command.Notes,
            IdempotencyKey = command.IdempotencyKey
        }, cancellationToken);
    }
}

