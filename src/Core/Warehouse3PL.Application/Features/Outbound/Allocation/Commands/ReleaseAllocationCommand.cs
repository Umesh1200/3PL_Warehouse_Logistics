using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Allocation.Commands;

public record ReleaseAllocationCommand(
    Guid AllocationId,
    decimal? QuantityToRelease,
    string Reason) : IRequest<ReleaseAllocationResultDto>;

public class ReleaseAllocationCommandHandler : IRequestHandler<ReleaseAllocationCommand, ReleaseAllocationResultDto>
{
    private readonly IAllocationExecutionService _executionService;
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public ReleaseAllocationCommandHandler(
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

    public async Task<ReleaseAllocationResultDto> Handle(
        ReleaseAllocationCommand request,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(ReleaseAllocationCommand));

        var allocation = await _context.InventoryAllocations
            .AsNoTracking()
            .Select(a => new { a.Id, a.FacilityId })
            .FirstOrDefaultAsync(a => a.Id == request.AllocationId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(InventoryAllocation), request.AllocationId);

        _outboundAuth.ValidateStaffMutationFacilityAccess(allocation.FacilityId, nameof(ReleaseAllocationCommand));

        return await _executionService.ReleaseAllocationAsync(
            request.AllocationId,
            request.QuantityToRelease,
            request.Reason,
            _currentUser.UserId,
            cancellationToken);
    }
}

