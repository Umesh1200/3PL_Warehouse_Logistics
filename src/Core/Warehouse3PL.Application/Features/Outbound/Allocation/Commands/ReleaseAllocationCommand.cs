using MediatR;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;

namespace Warehouse3PL.Application.Features.Outbound.Allocation.Commands;

public record ReleaseAllocationCommand(
    Guid AllocationId,
    decimal? QuantityToRelease,
    string Reason) : IRequest<ReleaseAllocationResultDto>;

public class ReleaseAllocationCommandHandler : IRequestHandler<ReleaseAllocationCommand, ReleaseAllocationResultDto>
{
    private readonly IAllocationExecutionService _executionService;
    private readonly ICurrentUserContext _currentUser;

    public ReleaseAllocationCommandHandler(
        IAllocationExecutionService executionService,
        ICurrentUserContext currentUser)
    {
        _executionService = executionService;
        _currentUser = currentUser;
    }

    public async Task<ReleaseAllocationResultDto> Handle(
        ReleaseAllocationCommand request,
        CancellationToken cancellationToken)
    {
        return await _executionService.ReleaseAllocationAsync(
            request.AllocationId,
            request.QuantityToRelease,
            request.Reason,
            _currentUser.UserId,
            cancellationToken);
    }
}

