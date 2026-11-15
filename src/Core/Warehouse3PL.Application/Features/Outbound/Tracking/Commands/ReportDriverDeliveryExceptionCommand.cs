using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Tracking.Commands;

public record ReportDriverDeliveryExceptionCommand(ReportDriverExceptionRequest Request) : IRequest<DeliveryExceptionResult>;

public class ReportDriverDeliveryExceptionCommandHandler : IRequestHandler<ReportDriverDeliveryExceptionCommand, DeliveryExceptionResult>
{
    private readonly IDeliveryExceptionService _deliveryExceptionService;

    public ReportDriverDeliveryExceptionCommandHandler(IDeliveryExceptionService deliveryExceptionService)
    {
        _deliveryExceptionService = deliveryExceptionService;
    }

    public async Task<DeliveryExceptionResult> Handle(ReportDriverDeliveryExceptionCommand command, CancellationToken cancellationToken)
    {
        return await _deliveryExceptionService.ReportDriverExceptionAsync(command.Request, cancellationToken);
    }
}

