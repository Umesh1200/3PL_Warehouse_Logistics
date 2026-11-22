using MediatR;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;

namespace Warehouse3PL.Application.Features.Outbound.Tracking.Commands;

public record RescheduleDeliveryReattemptCommand(RescheduleDeliveryReattemptRequest Request) : IRequest<DeliveryExceptionResult>;

public class RescheduleDeliveryReattemptCommandHandler : IRequestHandler<RescheduleDeliveryReattemptCommand, DeliveryExceptionResult>
{
    private readonly IDeliveryExceptionService _deliveryExceptionService;
    private readonly ICurrentUserContext _currentUser;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public RescheduleDeliveryReattemptCommandHandler(
        IDeliveryExceptionService deliveryExceptionService,
        ICurrentUserContext currentUser,
        OutboundOperationalAuthorization outboundAuth)
    {
        _deliveryExceptionService = deliveryExceptionService;
        _currentUser = currentUser;
        _outboundAuth = outboundAuth;
    }

    public async Task<DeliveryExceptionResult> Handle(RescheduleDeliveryReattemptCommand command, CancellationToken cancellationToken)
    {
        await _outboundAuth.ValidateStaffExceptionMutationAsync(
            command.Request.ExceptionId,
            nameof(RescheduleDeliveryReattemptCommand),
            cancellationToken);

        command.Request.TenantIdOverride = null;
        if (_currentUser.UserId.HasValue)
        {
            command.Request.UserId = _currentUser.UserId.Value;
        }

        return await _deliveryExceptionService.RescheduleReattemptAsync(command.Request, cancellationToken);
    }
}

