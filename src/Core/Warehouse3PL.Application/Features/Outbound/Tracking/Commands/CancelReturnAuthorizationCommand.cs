using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Tracking.Commands;

public record CancelReturnAuthorizationCommand(CancelReturnAuthorizationRequest Request) : IRequest<ReturnAuthorizationResult>;

public class CancelReturnAuthorizationCommandHandler : IRequestHandler<CancelReturnAuthorizationCommand, ReturnAuthorizationResult>
{
    private readonly IReturnAuthorizationService _returnAuthorizationService;

    public CancelReturnAuthorizationCommandHandler(IReturnAuthorizationService returnAuthorizationService)
    {
        _returnAuthorizationService = returnAuthorizationService;
    }

    public async Task<ReturnAuthorizationResult> Handle(CancelReturnAuthorizationCommand command, CancellationToken cancellationToken)
    {
        return await _returnAuthorizationService.CancelAuthorizationAsync(command.Request, cancellationToken);
    }
}

