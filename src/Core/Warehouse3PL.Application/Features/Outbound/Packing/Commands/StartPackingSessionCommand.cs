using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Packing.Commands;

public record StartPackingSessionCommand(
    Guid FacilityId,
    Guid PackingStationLocationId,
    Guid OutboundOrderId) : IRequest<PackingSessionResult>;

public class StartPackingSessionCommandHandler : IRequestHandler<StartPackingSessionCommand, PackingSessionResult>
{
    private readonly IPackingExecutionService _executionService;
    private readonly ICurrentUserContext _currentUser;

    public StartPackingSessionCommandHandler(
        IPackingExecutionService executionService,
        ICurrentUserContext currentUser)
    {
        _executionService = executionService;
        _currentUser = currentUser;
    }

    public async Task<PackingSessionResult> Handle(
        StartPackingSessionCommand command,
        CancellationToken cancellationToken)
    {
        return await _executionService.StartOrResumeSessionAsync(new StartPackingSessionRequest
        {
            FacilityId = command.FacilityId,
            PackingStationLocationId = command.PackingStationLocationId,
            OutboundOrderId = command.OutboundOrderId,
            OperatorUserId = _currentUser.UserId ?? Guid.Empty
        }, cancellationToken);
    }
}

