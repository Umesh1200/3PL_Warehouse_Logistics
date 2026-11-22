using MediatR;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;

namespace Warehouse3PL.Application.Features.Outbound.Tracking.Commands;

public record SyncShipmentTrackingCommand(Guid ShipmentId) : IRequest<SyncShipmentTrackingResult>;

public class SyncShipmentTrackingCommandHandler : IRequestHandler<SyncShipmentTrackingCommand, SyncShipmentTrackingResult>
{
    private readonly IShipmentTrackingService _trackingService;
    private readonly ICurrentUserContext _currentUser;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public SyncShipmentTrackingCommandHandler(
        IShipmentTrackingService trackingService,
        ICurrentUserContext currentUser,
        OutboundOperationalAuthorization outboundAuth)
    {
        _trackingService = trackingService;
        _currentUser = currentUser;
        _outboundAuth = outboundAuth;
    }

    public async Task<SyncShipmentTrackingResult> Handle(
        SyncShipmentTrackingCommand command,
        CancellationToken cancellationToken)
    {
        await _outboundAuth.ValidateStaffShipmentMutationAsync(
            command.ShipmentId,
            nameof(SyncShipmentTrackingCommand),
            cancellationToken);

        return await _trackingService.SyncShipmentTrackingAsync(new SyncShipmentTrackingRequest
        {
            ShipmentId = command.ShipmentId,
            UserId = _currentUser.UserId
        }, cancellationToken);
    }
}

