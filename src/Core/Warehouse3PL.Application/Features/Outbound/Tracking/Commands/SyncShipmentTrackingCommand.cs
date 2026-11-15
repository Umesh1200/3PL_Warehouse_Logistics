using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Tracking.Commands;

public record SyncShipmentTrackingCommand(Guid ShipmentId) : IRequest<SyncShipmentTrackingResult>;

public class SyncShipmentTrackingCommandHandler : IRequestHandler<SyncShipmentTrackingCommand, SyncShipmentTrackingResult>
{
    private readonly IShipmentTrackingService _trackingService;
    private readonly ICurrentUserContext _currentUser;

    public SyncShipmentTrackingCommandHandler(
        IShipmentTrackingService trackingService,
        ICurrentUserContext currentUser)
    {
        _trackingService = trackingService;
        _currentUser = currentUser;
    }

    public async Task<SyncShipmentTrackingResult> Handle(
        SyncShipmentTrackingCommand command,
        CancellationToken cancellationToken)
    {
        return await _trackingService.SyncShipmentTrackingAsync(new SyncShipmentTrackingRequest
        {
            ShipmentId = command.ShipmentId,
            UserId = _currentUser.UserId
        }, cancellationToken);
    }
}

