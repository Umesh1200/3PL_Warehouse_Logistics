using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Packing.Commands;

public record ClosePackageCommand(
    Guid PackageId,
    decimal ScaleGrossWeightKg) : IRequest<ClosePackageResult>;

public class ClosePackageCommandHandler : IRequestHandler<ClosePackageCommand, ClosePackageResult>
{
    private readonly IPackingExecutionService _executionService;
    private readonly ICurrentUserContext _currentUser;

    public ClosePackageCommandHandler(
        IPackingExecutionService executionService,
        ICurrentUserContext currentUser)
    {
        _executionService = executionService;
        _currentUser = currentUser;
    }

    public async Task<ClosePackageResult> Handle(
        ClosePackageCommand command,
        CancellationToken cancellationToken)
    {
        return await _executionService.ClosePackageAsync(new ClosePackageRequest
        {
            PackageId = command.PackageId,
            ScaleGrossWeightKg = command.ScaleGrossWeightKg,
            UserId = _currentUser.UserId ?? Guid.Empty
        }, cancellationToken);
    }
}

