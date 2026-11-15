using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Packing.Commands;

public record CreatePackageCommand(
    Guid PackingSessionId,
    Guid? PackagingMaterialId = null,
    string? OverrideReason = null) : IRequest<CreatePackageResult>;

public class CreatePackageCommandHandler : IRequestHandler<CreatePackageCommand, CreatePackageResult>
{
    private readonly IPackingExecutionService _executionService;
    private readonly ICurrentUserContext _currentUser;

    public CreatePackageCommandHandler(
        IPackingExecutionService executionService,
        ICurrentUserContext currentUser)
    {
        _executionService = executionService;
        _currentUser = currentUser;
    }

    public async Task<CreatePackageResult> Handle(
        CreatePackageCommand command,
        CancellationToken cancellationToken)
    {
        return await _executionService.CreatePackageAsync(new CreatePackageRequest
        {
            PackingSessionId = command.PackingSessionId,
            PackagingMaterialId = command.PackagingMaterialId,
            OverrideReason = command.OverrideReason,
            UserId = _currentUser.UserId ?? Guid.Empty
        }, cancellationToken);
    }
}

