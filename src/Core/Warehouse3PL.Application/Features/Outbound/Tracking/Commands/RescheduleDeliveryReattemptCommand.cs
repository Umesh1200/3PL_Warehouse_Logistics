using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Tracking.Commands;

public record RescheduleDeliveryReattemptCommand(RescheduleDeliveryReattemptRequest Request) : IRequest<DeliveryExceptionResult>;

public class RescheduleDeliveryReattemptCommandHandler : IRequestHandler<RescheduleDeliveryReattemptCommand, DeliveryExceptionResult>
{
    private readonly IDeliveryExceptionService _deliveryExceptionService;

    public RescheduleDeliveryReattemptCommandHandler(IDeliveryExceptionService deliveryExceptionService)
    {
        _deliveryExceptionService = deliveryExceptionService;
    }

    public async Task<DeliveryExceptionResult> Handle(RescheduleDeliveryReattemptCommand command, CancellationToken cancellationToken)
    {
        return await _deliveryExceptionService.RescheduleReattemptAsync(command.Request, cancellationToken);
    }
}

