using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
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
    private readonly ICurrentUserContext _currentUser;

    public UnlockLocationCommandHandler(IApplicationDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<bool> Handle(UnlockLocationCommand request, CancellationToken cancellationToken)
    {
        var location = await _context.Locations
            .Include(l => l.Locks)
            .FirstOrDefaultAsync(l => l.Id == request.LocationId, cancellationToken);

        if (location == null)
        {
            throw new EntityNotFoundException(nameof(Location), request.LocationId);
        }

        var activeLocks = location.Locks.Where(lk => !lk.ReleasedAt.HasValue).ToList();
        foreach (var lk in activeLocks)
        {
            lk.ReleasedAt = DateTime.UtcNow;
            lk.ReleasedByUserId = _currentUser.UserId;
        }

        location.IsLocked = false;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}

