using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
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
    private readonly ICurrentUserContext _currentUser;

    public LockLocationCommandHandler(IApplicationDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Guid> Handle(LockLocationCommand request, CancellationToken cancellationToken)
    {
        var location = await _context.Locations.FirstOrDefaultAsync(l => l.Id == request.LocationId, cancellationToken);
        if (location == null)
        {
            throw new EntityNotFoundException(nameof(Location), request.LocationId);
        }

        var locationLock = new LocationLock
        {
            LocationId = location.Id,
            LockType = request.LockType,
            ReasonCode = request.ReasonCode,
            Notes = request.Notes,
            LockedByUserId = _currentUser.UserId,
            LockedAt = DateTime.UtcNow
        };

        location.IsLocked = true;
        _context.LocationLocks.Add(locationLock);
        await _context.SaveChangesAsync(cancellationToken);

        return locationLock.Id;
    }
}

