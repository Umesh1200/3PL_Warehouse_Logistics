using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Tracking.Commands;

public record ExpireReturnAuthorizationCommand(ExpireReturnAuthorizationRequest Request) : IRequest<ReturnAuthorizationResult>;

public class ExpireReturnAuthorizationCommandHandler : IRequestHandler<ExpireReturnAuthorizationCommand, ReturnAuthorizationResult>
{
    private readonly IReturnAuthorizationService _returnAuthorizationService;

    public ExpireReturnAuthorizationCommandHandler(IReturnAuthorizationService returnAuthorizationService)
    {
        _returnAuthorizationService = returnAuthorizationService;
    }

    public async Task<ReturnAuthorizationResult> Handle(ExpireReturnAuthorizationCommand command, CancellationToken cancellationToken)
    {
        return await _returnAuthorizationService.ExpireAuthorizationAsync(command.Request, cancellationToken);
    }
}

