using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Locations.Commands.LockLocation;

public record LockLocationCommand : IRequest<Guid>
{
    public Guid LocationId { get; init; }
    public LocationLockType LockType { get; init; } = LocationLockType.Quarantine;
    public string ReasonCode { get; init; } = string.Empty;
    public string? Notes { get; init; }
}

public class LockLocationCommandValidator : AbstractValidator<LockLocationCommand>
{
    public LockLocationCommandValidator()
    {
        RuleFor(x => x.LocationId).NotEmpty();
        RuleFor(x => x.ReasonCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LockType).IsInEnum();
    }
}

public class LockLocationCommandHandler : IRequestHandler<LockLocationCommand, Guid>
{
    private readonly IApplicationDbContext _context;
    private readonly WarehouseMasterAuthorization _auth;

    public LockLocationCommandHandler(IApplicationDbContext context, WarehouseMasterAuthorization auth)
    {
        _context = context;
        _auth = auth;
    }

    public async Task<Guid> Handle(LockLocationCommand request, CancellationToken cancellationToken)
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
                .FirstOrDefaultAsync(l => l.Id == request.LocationId && l.FacilityId == facilityId && !l.IsDeleted, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Location), request.LocationId);

            var locationLock = new LocationLock
            {
                LocationId = location.Id,
                LockType = request.LockType,
                ReasonCode = request.ReasonCode.Trim(),
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
                LockedByUserId = userId,
                LockedAt = DateTime.UtcNow
            };

            location.IsLocked = true;
            location.ConfigRevision++;
            location.UpdatedBy = userId;
            _context.LocationLocks.Add(locationLock);
            await _context.SaveChangesAsync(cancellationToken);

            return locationLock.Id;
        }, cancellationToken);
    }
}
