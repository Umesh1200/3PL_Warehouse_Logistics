using Mapster;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Users.Commands.AssignUserFacility;

/// <summary>
/// Assigns, reassigns, or revokes (null) the single operational warehouse facility for a Staff account.
/// Client tenant and facility access remain isolated in ClientFacilityAccess.
/// </summary>
public record AssignUserFacilityCommand(Guid UserId, Guid? FacilityId) : IRequest<UserDto>;

public class AssignUserFacilityCommandHandler : IRequestHandler<AssignUserFacilityCommand, UserDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public AssignUserFacilityCommandHandler(
        IApplicationDbContext context,
        ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<UserDto> Handle(AssignUserFacilityCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsSuperAdmin && _currentUser.UserType != UserType.Staff)
        {
            throw new ForbiddenException("Only authorized warehouse staff administrators can manage staff facility assignments.");
        }

        var user = await _context.Users
            .Include(u => u.Client)
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == request.UserId && !u.IsDeleted, cancellationToken);

        if (user == null)
        {
            throw new EntityNotFoundException(nameof(User), request.UserId);
        }

        if (user.UserType != UserType.Staff)
        {
            throw new ValidationException(nameof(request.UserId),
                "Operational facility assignment applies strictly to warehouse Staff accounts.");
        }

        if (request.FacilityId.HasValue)
        {
            if (request.FacilityId.Value == Guid.Empty)
            {
                throw new ValidationException(nameof(request.FacilityId),
                    "FacilityId must be a valid facility identifier.");
            }

            var facility = await _context.Facilities
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == request.FacilityId.Value && !f.IsDeleted, cancellationToken);

            if (facility == null)
            {
                throw new EntityNotFoundException(nameof(Facility), request.FacilityId.Value);
            }

            if (!facility.IsActive)
            {
                throw new ValidationException(nameof(request.FacilityId),
                    $"Facility '{facility.Id}' is inactive and cannot be assigned.");
            }

            user.AssignedFacilityId = facility.Id;
        }
        else
        {
            user.AssignedFacilityId = null;
        }

        await _context.SaveChangesAsync(cancellationToken);

        return user.Adapt<UserDto>();
    }
}
