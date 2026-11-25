using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Allocation.Commands;

public record AllocateOutboundOrderCommand(
    Guid OutboundOrderId,
    AllocationStrategyType? StrategyOverride = null,
    string? IdempotencyKey = null) : IRequest<AllocationExecutionResultDto>;

public class AllocateOutboundOrderCommandHandler : IRequestHandler<AllocateOutboundOrderCommand, AllocationExecutionResultDto>
{
    private readonly IAllocationExecutionService _executionService;
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public AllocateOutboundOrderCommandHandler(
        IAllocationExecutionService executionService,
        IApplicationDbContext context,
        ICurrentUserContext currentUser,
        OutboundOperationalAuthorization outboundAuth)
    {
        _executionService = executionService;
        _context = context;
        _currentUser = currentUser;
        _outboundAuth = outboundAuth;
    }

    public async Task<AllocationExecutionResultDto> Handle(
        AllocateOutboundOrderCommand request,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(AllocateOutboundOrderCommand));

        if (!_currentUser.IsSuperAdmin && !_currentUser.HasPermission(Permissions.Outbound.OrdersAllocate))
        {
            throw new ForbiddenException("Permission 'outbound:orders:allocate' is required to allocate outbound orders or apply allocation strategy overrides.");
        }

        if (request.StrategyOverride.HasValue && !Enum.IsDefined(typeof(AllocationStrategyType), request.StrategyOverride.Value))
        {
            throw new ValidationException(nameof(request.StrategyOverride), $"Invalid allocation strategy override '{request.StrategyOverride.Value}'.");
        }

        var order = await _context.OutboundOrders
            .AsNoTracking()
            .Select(o => new { o.Id, o.FacilityId })
            .FirstOrDefaultAsync(o => o.Id == request.OutboundOrderId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(OutboundOrder), request.OutboundOrderId);

        _outboundAuth.ValidateStaffMutationFacilityAccess(order.FacilityId, nameof(AllocateOutboundOrderCommand));

        return await _executionService.ExecuteOrderAllocationAsync(
            request.OutboundOrderId,
            request.StrategyOverride,
            request.IdempotencyKey,
            _currentUser.UserId,
            cancellationToken);
    }
}

