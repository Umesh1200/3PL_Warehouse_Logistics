using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Tracking.Commands;

public record ResolveDeliveryExceptionCommand(ResolveDeliveryExceptionRequest Request) : IRequest<DeliveryExceptionResult>;

public class ResolveDeliveryExceptionCommandHandler : IRequestHandler<ResolveDeliveryExceptionCommand, DeliveryExceptionResult>
{
    private readonly IDeliveryExceptionService _deliveryExceptionService;

    public ResolveDeliveryExceptionCommandHandler(IDeliveryExceptionService deliveryExceptionService)
    {
        _deliveryExceptionService = deliveryExceptionService;
    }

    public async Task<DeliveryExceptionResult> Handle(ResolveDeliveryExceptionCommand command, CancellationToken cancellationToken)
    {
        return await _deliveryExceptionService.ResolveExceptionAsync(command.Request, cancellationToken);
    }
}

