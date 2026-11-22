using MediatR;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;

namespace Warehouse3PL.Application.Features.Outbound.Packing.Commands;

public record ReleaseToteCommand(
    string ToteLpn,
    Guid FacilityId) : IRequest<ReleaseToteResult>;

public class ReleaseToteCommandHandler : IRequestHandler<ReleaseToteCommand, ReleaseToteResult>
{
    private readonly IPackingExecutionService _executionService;
    private readonly ICurrentUserContext _currentUser;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public ReleaseToteCommandHandler(
        IPackingExecutionService executionService,
        ICurrentUserContext currentUser,
        OutboundOperationalAuthorization outboundAuth)
    {
        _executionService = executionService;
        _currentUser = currentUser;
        _outboundAuth = outboundAuth;
    }

    public async Task<ReleaseToteResult> Handle(
        ReleaseToteCommand command,
        CancellationToken cancellationToken)
    {
        _outboundAuth.ValidateStaffMutationFacilityAccess(command.FacilityId, nameof(ReleaseToteCommand));

        return await _executionService.ReleaseToteAsync(new ReleaseToteRequest
        {
            ToteLpn = command.ToteLpn,
            FacilityId = command.FacilityId,
            UserId = _currentUser.UserId ?? Guid.Empty
        }, cancellationToken);
    }
}

