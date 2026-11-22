using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Allocation.Commands;

public record DeallocateOutboundOrderCommand(
    Guid OutboundOrderId,
    string Reason) : IRequest<DeallocationExecutionResultDto>;

public class DeallocateOutboundOrderCommandHandler : IRequestHandler<DeallocateOutboundOrderCommand, DeallocationExecutionResultDto>
{
    private readonly IAllocationExecutionService _executionService;
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public DeallocateOutboundOrderCommandHandler(
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

    public async Task<DeallocationExecutionResultDto> Handle(
        DeallocateOutboundOrderCommand request,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(DeallocateOutboundOrderCommand));

        var order = await _context.OutboundOrders
            .AsNoTracking()
            .Select(o => new { o.Id, o.FacilityId })
            .FirstOrDefaultAsync(o => o.Id == request.OutboundOrderId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(OutboundOrder), request.OutboundOrderId);

        _outboundAuth.ValidateStaffMutationFacilityAccess(order.FacilityId, nameof(DeallocateOutboundOrderCommand));

        return await _executionService.ExecuteOrderDeallocationAsync(
            request.OutboundOrderId,
            request.Reason,
            _currentUser.UserId,
            cancellationToken);
    }
}

