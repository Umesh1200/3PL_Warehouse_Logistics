using MediatR;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;

namespace Warehouse3PL.Application.Features.Outbound.Allocation.Commands;

public record DeallocateOutboundOrderCommand(
    Guid OutboundOrderId,
    string Reason) : IRequest<DeallocationExecutionResultDto>;

public class DeallocateOutboundOrderCommandHandler : IRequestHandler<DeallocateOutboundOrderCommand, DeallocationExecutionResultDto>
{
    private readonly IAllocationExecutionService _executionService;
    private readonly ICurrentUserContext _currentUser;

    public DeallocateOutboundOrderCommandHandler(
        IAllocationExecutionService executionService,
        ICurrentUserContext currentUser)
    {
        _executionService = executionService;
        _currentUser = currentUser;
    }

    public async Task<DeallocationExecutionResultDto> Handle(
        DeallocateOutboundOrderCommand request,
        CancellationToken cancellationToken)
    {
        return await _executionService.ExecuteOrderDeallocationAsync(
            request.OutboundOrderId,
            request.Reason,
            _currentUser.UserId,
            cancellationToken);
    }
}

