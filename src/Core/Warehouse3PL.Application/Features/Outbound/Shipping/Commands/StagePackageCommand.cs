using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Shipping.Commands;

public record StagePackageCommand(
    Guid PackageId,
    Guid StagingLocationId) : IRequest<StagePackageResult>;

public class StagePackageCommandHandler : IRequestHandler<StagePackageCommand, StagePackageResult>
{
    private readonly IShipmentConsolidationService _consolidationService;
    private readonly ICurrentUserContext _currentUser;

    public StagePackageCommandHandler(
        IShipmentConsolidationService consolidationService,
        ICurrentUserContext currentUser)
    {
        _consolidationService = consolidationService;
        _currentUser = currentUser;
    }

    public async Task<StagePackageResult> Handle(
        StagePackageCommand command,
        CancellationToken cancellationToken)
    {
        return await _consolidationService.StagePackageAsync(new StagePackageRequest
        {
            PackageId = command.PackageId,
            StagingLocationId = command.StagingLocationId,
            UserId = _currentUser.UserId ?? Guid.Empty
        }, cancellationToken);
    }
}

