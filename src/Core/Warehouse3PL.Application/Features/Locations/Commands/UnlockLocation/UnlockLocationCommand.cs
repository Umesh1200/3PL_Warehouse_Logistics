using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Locations.Commands.UnlockLocation;

public record UnlockLocationCommand(Guid LocationId) : IRequest<bool>;

public class UnlockLocationCommandValidator : AbstractValidator<UnlockLocationCommand>
{
    public UnlockLocationCommandValidator()
    {
        RuleFor(x => x.LocationId).NotEmpty();
    }
}

public class UnlockLocationCommandHandler : IRequestHandler<UnlockLocationCommand, bool>
{
    private readonly IApplicationDbContext _context;
    private readonly WarehouseMasterAuthorization _auth;

    public UnlockLocationCommandHandler(IApplicationDbContext context, WarehouseMasterAuthorization auth)
    {
        _context = context;
        _auth = auth;
    }

    public async Task<bool> Handle(UnlockLocationCommand request, CancellationToken cancellationToken)
    {
        var preRead = await _context.Locations
            .AsNoTracking()
            .Where(l => l.Id == request.LocationId && !l.IsDeleted)
            .Select(l => new { l.FacilityId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Location), request.LocationId);

        var (userId, facilityId, _) = await _auth.ResolveAuthorizedFacilityAsync(
            preRead.FacilityId,
            cancellationToken,
            Permissions.Facilities.LocationsManage,
            Permissions.Facilities.Manage);

        return await _context.ExecuteWithLocationLifecycleLockAsync(facilityId, request.LocationId, async () =>
        {
            var location = await _context.Locations
                .Include(l => l.Locks)
                .FirstOrDefaultAsync(l => l.Id == request.LocationId && l.FacilityId == facilityId && !l.IsDeleted, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Location), request.LocationId);

            var activeLocks = location.Locks.Where(lk => !lk.ReleasedAt.HasValue).ToList();
            var now = DateTime.UtcNow;
            foreach (var lk in activeLocks)
            {
                lk.ReleasedAt = now;
                lk.ReleasedByUserId = userId;
            }

            location.IsLocked = false;
            location.ConfigRevision++;
            location.UpdatedBy = userId;
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }
}
