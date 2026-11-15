using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Tracking.Commands;

public record UpdateDeliveryAddressCommand(UpdateDeliveryAddressRequest Request) : IRequest<DeliveryExceptionResult>;

public class UpdateDeliveryAddressCommandHandler : IRequestHandler<UpdateDeliveryAddressCommand, DeliveryExceptionResult>
{
    private readonly IDeliveryExceptionService _deliveryExceptionService;

    public UpdateDeliveryAddressCommandHandler(IDeliveryExceptionService deliveryExceptionService)
    {
        _deliveryExceptionService = deliveryExceptionService;
    }

    public async Task<DeliveryExceptionResult> Handle(UpdateDeliveryAddressCommand command, CancellationToken cancellationToken)
    {
        return await _deliveryExceptionService.UpdateAddressAsync(command.Request, cancellationToken);
    }
}

