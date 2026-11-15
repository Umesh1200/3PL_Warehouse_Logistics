using MediatR;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Outbound.Allocation.Commands;

public record AllocateOutboundOrderCommand(
    Guid OutboundOrderId,
    AllocationStrategyType? StrategyOverride = null,
    string? IdempotencyKey = null) : IRequest<AllocationExecutionResultDto>;

public class AllocateOutboundOrderCommandHandler : IRequestHandler<AllocateOutboundOrderCommand, AllocationExecutionResultDto>
{
    private readonly IAllocationExecutionService _executionService;
    private readonly ICurrentUserContext _currentUser;

    public AllocateOutboundOrderCommandHandler(
        IAllocationExecutionService executionService,
        ICurrentUserContext currentUser)
    {
        _executionService = executionService;
        _currentUser = currentUser;
    }

    public async Task<AllocationExecutionResultDto> Handle(
        AllocateOutboundOrderCommand request,
        CancellationToken cancellationToken)
    {
        return await _executionService.ExecuteOrderAllocationAsync(
            request.OutboundOrderId,
            request.StrategyOverride,
            request.IdempotencyKey,
            _currentUser.UserId,
            cancellationToken);
    }
}

