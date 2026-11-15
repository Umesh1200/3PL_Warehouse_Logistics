using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Shipping.Commands;

public record RemovePackageFromShipmentCommand(
    Guid ShipmentId,
    Guid PackageId,
    string Reason) : IRequest<RemovePackageFromShipmentResult>;

public class RemovePackageFromShipmentCommandHandler : IRequestHandler<RemovePackageFromShipmentCommand, RemovePackageFromShipmentResult>
{
    private readonly IShipmentConsolidationService _consolidationService;
    private readonly ICurrentUserContext _currentUser;

    public RemovePackageFromShipmentCommandHandler(
        IShipmentConsolidationService consolidationService,
        ICurrentUserContext currentUser)
    {
        _consolidationService = consolidationService;
        _currentUser = currentUser;
    }

    public async Task<RemovePackageFromShipmentResult> Handle(
        RemovePackageFromShipmentCommand command,
        CancellationToken cancellationToken)
    {
        return await _consolidationService.RemovePackageFromShipmentAsync(new RemovePackageFromShipmentRequest
        {
            ShipmentId = command.ShipmentId,
            PackageId = command.PackageId,
            Reason = command.Reason,
            UserId = _currentUser.UserId ?? Guid.Empty
        }, cancellationToken);
    }
}

