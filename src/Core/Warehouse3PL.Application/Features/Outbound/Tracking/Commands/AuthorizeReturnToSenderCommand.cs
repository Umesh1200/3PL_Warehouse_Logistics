using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Tracking.Commands;

public record AuthorizeReturnToSenderCommand(AuthorizeReturnToSenderRequest Request) : IRequest<ReturnAuthorizationResult>;

public class AuthorizeReturnToSenderCommandHandler : IRequestHandler<AuthorizeReturnToSenderCommand, ReturnAuthorizationResult>
{
    private readonly IReturnAuthorizationService _returnAuthorizationService;

    public AuthorizeReturnToSenderCommandHandler(IReturnAuthorizationService returnAuthorizationService)
    {
        _returnAuthorizationService = returnAuthorizationService;
    }

    public async Task<ReturnAuthorizationResult> Handle(AuthorizeReturnToSenderCommand command, CancellationToken cancellationToken)
    {
        return await _returnAuthorizationService.AuthorizeReturnToSenderAsync(command.Request, cancellationToken);
    }
}

