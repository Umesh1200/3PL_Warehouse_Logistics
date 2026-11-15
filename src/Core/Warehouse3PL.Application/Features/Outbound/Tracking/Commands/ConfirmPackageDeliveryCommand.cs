using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Tracking.Commands;

public record ConfirmPackageDeliveryCommand(ConfirmPackageDeliveryRequest Request) : IRequest<DeliveryConfirmationResult>;

public class ConfirmPackageDeliveryCommandHandler : IRequestHandler<ConfirmPackageDeliveryCommand, DeliveryConfirmationResult>
{
    private readonly IDeliveryConfirmationService _deliveryConfirmationService;

    public ConfirmPackageDeliveryCommandHandler(IDeliveryConfirmationService deliveryConfirmationService)
    {
        _deliveryConfirmationService = deliveryConfirmationService;
    }

    public Task<DeliveryConfirmationResult> Handle(ConfirmPackageDeliveryCommand command, CancellationToken cancellationToken)
    {
        return _deliveryConfirmationService.ConfirmDeliveryAsync(command.Request, cancellationToken);
    }
}

