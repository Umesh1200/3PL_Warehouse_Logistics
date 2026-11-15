using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Shipping.Commands;

public record LoadPackageCommand(
    Guid PackageId,
    Guid? ShipmentId = null,
    string? TrailerNumber = null,
    Guid? DockLocationId = null) : IRequest<LoadPackageResult>;

public class LoadPackageCommandHandler : IRequestHandler<LoadPackageCommand, LoadPackageResult>
{
    private readonly IShipmentConsolidationService _consolidationService;
    private readonly ICurrentUserContext _currentUser;

    public LoadPackageCommandHandler(
        IShipmentConsolidationService consolidationService,
        ICurrentUserContext currentUser)
    {
        _consolidationService = consolidationService;
        _currentUser = currentUser;
    }

    public async Task<LoadPackageResult> Handle(
        LoadPackageCommand command,
        CancellationToken cancellationToken)
    {
        return await _consolidationService.LoadPackageAsync(new LoadPackageRequest
        {
            PackageId = command.PackageId,
            ShipmentId = command.ShipmentId,
            TrailerNumber = command.TrailerNumber,
            DockLocationId = command.DockLocationId,
            UserId = _currentUser.UserId ?? Guid.Empty
        }, cancellationToken);
    }
}

