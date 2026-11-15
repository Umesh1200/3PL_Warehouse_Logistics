using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Tracking.Commands;

public record RedirectToCarrierDepotCommand(RedirectToCarrierDepotRequest Request) : IRequest<DeliveryExceptionResult>;

public class RedirectToCarrierDepotCommandHandler : IRequestHandler<RedirectToCarrierDepotCommand, DeliveryExceptionResult>
{
    private readonly IDeliveryExceptionService _deliveryExceptionService;

    public RedirectToCarrierDepotCommandHandler(IDeliveryExceptionService deliveryExceptionService)
    {
        _deliveryExceptionService = deliveryExceptionService;
    }

    public async Task<DeliveryExceptionResult> Handle(RedirectToCarrierDepotCommand command, CancellationToken cancellationToken)
    {
        return await _deliveryExceptionService.RedirectToDepotAsync(command.Request, cancellationToken);
    }
}

