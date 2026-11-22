using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Shipping.Commands;

public record RemovePackageFromShipmentCommand(
    Guid ShipmentId,
    Guid PackageId,
    string Reason) : IRequest<RemovePackageFromShipmentResult>;

public class RemovePackageFromShipmentCommandHandler : IRequestHandler<RemovePackageFromShipmentCommand, RemovePackageFromShipmentResult>
{
    private readonly IShipmentConsolidationService _consolidationService;
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public RemovePackageFromShipmentCommandHandler(
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

    public async Task<RemovePackageFromShipmentResult> Handle(
        RemovePackageFromShipmentCommand command,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(RemovePackageFromShipmentCommand));

        var shipment = await _context.Shipments
            .AsNoTracking()
            .Select(s => new { s.Id, s.FacilityId })
            .FirstOrDefaultAsync(s => s.Id == command.ShipmentId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Shipment), command.ShipmentId);

        _outboundAuth.ValidateStaffMutationFacilityAccess(shipment.FacilityId, nameof(RemovePackageFromShipmentCommand));

        return await _consolidationService.RemovePackageFromShipmentAsync(new RemovePackageFromShipmentRequest
        {
            ShipmentId = command.ShipmentId,
            PackageId = command.PackageId,
            Reason = command.Reason,
            UserId = _currentUser.UserId ?? Guid.Empty
        }, cancellationToken);
    }
}

