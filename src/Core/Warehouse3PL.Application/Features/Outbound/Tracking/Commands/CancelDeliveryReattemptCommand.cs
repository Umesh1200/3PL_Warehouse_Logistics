using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Tracking.Commands;

public record CancelDeliveryReattemptCommand(CancelDeliveryReattemptRequest Request) : IRequest<DeliveryExceptionResult>;

public class CancelDeliveryReattemptCommandHandler : IRequestHandler<CancelDeliveryReattemptCommand, DeliveryExceptionResult>
{
    private readonly IDeliveryExceptionService _deliveryExceptionService;

    public CancelDeliveryReattemptCommandHandler(IDeliveryExceptionService deliveryExceptionService)
    {
        _deliveryExceptionService = deliveryExceptionService;
    }

    public async Task<DeliveryExceptionResult> Handle(CancelDeliveryReattemptCommand command, CancellationToken cancellationToken)
    {
        return await _deliveryExceptionService.CancelReattemptAsync(command.Request, cancellationToken);
    }
}

