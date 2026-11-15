using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Packing.Commands;

public record ReleaseToteCommand(
    string ToteLpn,
    Guid FacilityId) : IRequest<ReleaseToteResult>;

public class ReleaseToteCommandHandler : IRequestHandler<ReleaseToteCommand, ReleaseToteResult>
{
    private readonly IPackingExecutionService _executionService;
    private readonly ICurrentUserContext _currentUser;

    public ReleaseToteCommandHandler(
        IPackingExecutionService executionService,
        ICurrentUserContext currentUser)
    {
        _executionService = executionService;
        _currentUser = currentUser;
    }

    public async Task<ReleaseToteResult> Handle(
        ReleaseToteCommand command,
        CancellationToken cancellationToken)
    {
        return await _executionService.ReleaseToteAsync(new ReleaseToteRequest
        {
            ToteLpn = command.ToteLpn,
            FacilityId = command.FacilityId,
            UserId = _currentUser.UserId ?? Guid.Empty
        }, cancellationToken);
    }
}

