using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Shipping.Commands;

public record GenerateBillOfLadingCommand(
    Guid ShipmentId,
    string? TrailerNumber = null,
    string? SealNumber = null,
    string? SpecialInstructions = null) : IRequest<GenerateBolResult>;

public class GenerateBillOfLadingCommandHandler : IRequestHandler<GenerateBillOfLadingCommand, GenerateBolResult>
{
    private readonly IShipmentConsolidationService _consolidationService;
    private readonly ICurrentUserContext _currentUser;

    public GenerateBillOfLadingCommandHandler(
        IShipmentConsolidationService consolidationService,
        ICurrentUserContext currentUser)
    {
        _consolidationService = consolidationService;
        _currentUser = currentUser;
    }

    public async Task<GenerateBolResult> Handle(
        GenerateBillOfLadingCommand command,
        CancellationToken cancellationToken)
    {
        return await _consolidationService.GenerateBillOfLadingAsync(new GenerateBolRequest
        {
            ShipmentId = command.ShipmentId,
            TrailerNumber = command.TrailerNumber,
            SealNumber = command.SealNumber,
            SpecialInstructions = command.SpecialInstructions,
            UserId = _currentUser.UserId ?? Guid.Empty
        }, cancellationToken);
    }
}

