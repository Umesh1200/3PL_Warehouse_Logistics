using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Shipping.Commands;

public record AddPackageToShipmentCommand(
    Guid ShipmentId,
    Guid PackageId) : IRequest<AddPackageToShipmentResult>;

public class AddPackageToShipmentCommandHandler : IRequestHandler<AddPackageToShipmentCommand, AddPackageToShipmentResult>
{
    private readonly IShipmentConsolidationService _consolidationService;
    private readonly ICurrentUserContext _currentUser;

    public AddPackageToShipmentCommandHandler(
        IShipmentConsolidationService consolidationService,
        ICurrentUserContext currentUser)
    {
        _consolidationService = consolidationService;
        _currentUser = currentUser;
    }

    public async Task<AddPackageToShipmentResult> Handle(
        AddPackageToShipmentCommand command,
        CancellationToken cancellationToken)
    {
        return await _consolidationService.AddPackageToShipmentAsync(new AddPackageToShipmentRequest
        {
            ShipmentId = command.ShipmentId,
            PackageId = command.PackageId,
            UserId = _currentUser.UserId ?? Guid.Empty
        }, cancellationToken);
    }
}

