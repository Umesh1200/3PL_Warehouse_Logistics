using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Tracking.Commands;

public record ReopenDeliveryExceptionCommand(ReopenDeliveryExceptionRequest Request) : IRequest<DeliveryExceptionResult>;

public class ReopenDeliveryExceptionCommandHandler : IRequestHandler<ReopenDeliveryExceptionCommand, DeliveryExceptionResult>
{
    private readonly IDeliveryExceptionService _deliveryExceptionService;

    public ReopenDeliveryExceptionCommandHandler(IDeliveryExceptionService deliveryExceptionService)
    {
        _deliveryExceptionService = deliveryExceptionService;
    }

    public async Task<DeliveryExceptionResult> Handle(ReopenDeliveryExceptionCommand command, CancellationToken cancellationToken)
    {
        return await _deliveryExceptionService.ReopenExceptionAsync(command.Request, cancellationToken);
    }
}

