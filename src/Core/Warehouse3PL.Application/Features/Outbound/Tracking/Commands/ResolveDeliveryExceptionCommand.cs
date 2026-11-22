using MediatR;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;

namespace Warehouse3PL.Application.Features.Outbound.Tracking.Commands;

public record ResolveDeliveryExceptionCommand(ResolveDeliveryExceptionRequest Request) : IRequest<DeliveryExceptionResult>;

public class ResolveDeliveryExceptionCommandHandler : IRequestHandler<ResolveDeliveryExceptionCommand, DeliveryExceptionResult>
{
    private readonly IDeliveryExceptionService _deliveryExceptionService;
    private readonly ICurrentUserContext _currentUser;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public ResolveDeliveryExceptionCommandHandler(
        IDeliveryExceptionService deliveryExceptionService,
        ICurrentUserContext currentUser,
        OutboundOperationalAuthorization outboundAuth)
    {
        _deliveryExceptionService = deliveryExceptionService;
        _currentUser = currentUser;
        _outboundAuth = outboundAuth;
    }

    public async Task<DeliveryExceptionResult> Handle(ResolveDeliveryExceptionCommand command, CancellationToken cancellationToken)
    {
        await _outboundAuth.ValidateStaffExceptionMutationAsync(
            command.Request.ExceptionId,
            nameof(ResolveDeliveryExceptionCommand),
            cancellationToken);

        command.Request.TenantIdOverride = null;
        if (_currentUser.UserId.HasValue)
        {
            command.Request.ResolvedByUserId = _currentUser.UserId.Value;
        }

        return await _deliveryExceptionService.ResolveExceptionAsync(command.Request, cancellationToken);
    }
}

