using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Tracking.Commands;

public record RecordCustomerContactCommand(RecordCustomerContactRequest Request) : IRequest<DeliveryExceptionResult>;

public class RecordCustomerContactCommandHandler : IRequestHandler<RecordCustomerContactCommand, DeliveryExceptionResult>
{
    private readonly IDeliveryExceptionService _deliveryExceptionService;

    public RecordCustomerContactCommandHandler(IDeliveryExceptionService deliveryExceptionService)
    {
        _deliveryExceptionService = deliveryExceptionService;
    }

    public async Task<DeliveryExceptionResult> Handle(RecordCustomerContactCommand command, CancellationToken cancellationToken)
    {
        return await _deliveryExceptionService.RecordCustomerContactAsync(command.Request, cancellationToken);
    }
}

