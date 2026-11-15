using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Tracking.Commands;

public record CreateReturnShipmentCommand(CreateReturnShipmentRequest Request) : IRequest<ReturnShipmentResult>;

public class CreateReturnShipmentCommandHandler : IRequestHandler<CreateReturnShipmentCommand, ReturnShipmentResult>
{
    private readonly IReturnShipmentService _returnShipmentService;

    public CreateReturnShipmentCommandHandler(IReturnShipmentService returnShipmentService)
    {
        _returnShipmentService = returnShipmentService;
    }

    public async Task<ReturnShipmentResult> Handle(CreateReturnShipmentCommand command, CancellationToken cancellationToken)
    {
        return await _returnShipmentService.CreateReturnShipmentAsync(command.Request, cancellationToken);
    }
}

