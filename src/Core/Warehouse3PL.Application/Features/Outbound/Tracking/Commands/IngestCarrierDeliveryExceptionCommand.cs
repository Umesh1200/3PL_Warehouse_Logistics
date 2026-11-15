using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Tracking.Commands;

public record IngestCarrierDeliveryExceptionCommand(IngestCarrierExceptionRequest Request) : IRequest<DeliveryExceptionResult>;

public class IngestCarrierDeliveryExceptionCommandHandler : IRequestHandler<IngestCarrierDeliveryExceptionCommand, DeliveryExceptionResult>
{
    private readonly IDeliveryExceptionService _deliveryExceptionService;

    public IngestCarrierDeliveryExceptionCommandHandler(IDeliveryExceptionService deliveryExceptionService)
    {
        _deliveryExceptionService = deliveryExceptionService;
    }

    public async Task<DeliveryExceptionResult> Handle(IngestCarrierDeliveryExceptionCommand command, CancellationToken cancellationToken)
    {
        return await _deliveryExceptionService.IngestCarrierExceptionAsync(command.Request, cancellationToken);
    }
}

