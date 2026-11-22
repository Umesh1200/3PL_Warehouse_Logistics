using MediatR;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;

namespace Warehouse3PL.Application.Features.Outbound.Tracking.Commands;

public record ScheduleDeliveryReattemptCommand(ScheduleDeliveryReattemptRequest Request) : IRequest<DeliveryExceptionResult>;

public class ScheduleDeliveryReattemptCommandHandler : IRequestHandler<ScheduleDeliveryReattemptCommand, DeliveryExceptionResult>
{
    private readonly IDeliveryExceptionService _deliveryExceptionService;
    private readonly ICurrentUserContext _currentUser;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public ScheduleDeliveryReattemptCommandHandler(
        IDeliveryExceptionService deliveryExceptionService,
        ICurrentUserContext currentUser,
        OutboundOperationalAuthorization outboundAuth)
    {
        _deliveryExceptionService = deliveryExceptionService;
        _currentUser = currentUser;
        _outboundAuth = outboundAuth;
    }

    public async Task<DeliveryExceptionResult> Handle(ScheduleDeliveryReattemptCommand command, CancellationToken cancellationToken)
    {
        await _outboundAuth.ValidateStaffExceptionMutationAsync(
            command.Request.ExceptionId,
            nameof(ScheduleDeliveryReattemptCommand),
            cancellationToken);

        command.Request.TenantIdOverride = null;
        if (_currentUser.UserId.HasValue)
        {
            command.Request.UserId = _currentUser.UserId.Value;
        }

        return await _deliveryExceptionService.ScheduleReattemptAsync(command.Request, cancellationToken);
    }
}

