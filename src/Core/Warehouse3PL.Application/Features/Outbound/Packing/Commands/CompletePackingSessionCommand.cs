using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Packing.Commands;

public record CompletePackingSessionCommand(
    Guid PackingSessionId) : IRequest<CompletePackingSessionResult>;

public class CompletePackingSessionCommandHandler : IRequestHandler<CompletePackingSessionCommand, CompletePackingSessionResult>
{
    private readonly IPackingExecutionService _executionService;
    private readonly ICurrentUserContext _currentUser;

    public CompletePackingSessionCommandHandler(
        IPackingExecutionService executionService,
        ICurrentUserContext currentUser)
    {
        _executionService = executionService;
        _currentUser = currentUser;
    }

    public async Task<CompletePackingSessionResult> Handle(
        CompletePackingSessionCommand command,
        CancellationToken cancellationToken)
    {
        return await _executionService.CompletePackingSessionAsync(new CompletePackingSessionRequest
        {
            PackingSessionId = command.PackingSessionId,
            UserId = _currentUser.UserId ?? Guid.Empty
        }, cancellationToken);
    }
}

