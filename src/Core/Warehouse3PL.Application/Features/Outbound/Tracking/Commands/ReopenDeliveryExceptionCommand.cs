using MediatR;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;

namespace Warehouse3PL.Application.Features.Outbound.Tracking.Commands;

public record ReopenDeliveryExceptionCommand(ReopenDeliveryExceptionRequest Request) : IRequest<DeliveryExceptionResult>;

public class ReopenDeliveryExceptionCommandHandler : IRequestHandler<ReopenDeliveryExceptionCommand, DeliveryExceptionResult>
{
    private readonly IDeliveryExceptionService _deliveryExceptionService;
    private readonly ICurrentUserContext _currentUser;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public ReopenDeliveryExceptionCommandHandler(
        IDeliveryExceptionService deliveryExceptionService,
        ICurrentUserContext currentUser,
        OutboundOperationalAuthorization outboundAuth)
    {
        _deliveryExceptionService = deliveryExceptionService;
        _currentUser = currentUser;
        _outboundAuth = outboundAuth;
    }

    public async Task<DeliveryExceptionResult> Handle(ReopenDeliveryExceptionCommand command, CancellationToken cancellationToken)
    {
        await _outboundAuth.ValidateStaffExceptionMutationAsync(
            command.Request.ExceptionId,
            nameof(ReopenDeliveryExceptionCommand),
            cancellationToken);

        command.Request.TenantIdOverride = null;
        if (_currentUser.UserId.HasValue)
        {
            command.Request.UserId = _currentUser.UserId.Value;
        }

        return await _deliveryExceptionService.ReopenExceptionAsync(command.Request, cancellationToken);
    }
}

