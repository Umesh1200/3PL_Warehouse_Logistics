using Mapster;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Security;

namespace Warehouse3PL.Application.Features.Users.Queries.GetUsers;

public record GetUsersQuery : PagedQuery, IRequest<PagedResult<UserDto>>
{
    public Guid? ClientId { get; init; }
}

public class GetUsersQueryHandler : IRequestHandler<GetUsersQuery, PagedResult<UserDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public GetUsersQueryHandler(IApplicationDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<UserDto>> Handle(GetUsersQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Users
            .Include(u => u.Client)
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .Where(u => !u.IsDeleted)
            .AsNoTracking();

        // Multi-tenancy isolation: non-staff only see users in their assigned client
        if (!_currentUser.IsStaff && _currentUser.TenantId.HasValue)
        {
            query = query.Where(u => u.ClientId == _currentUser.TenantId.Value);
        }
        else if (request.ClientId.HasValue)
        {
            query = query.Where(u => u.ClientId == request.ClientId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.Trim().ToLower();
            query = query.Where(u => u.Email.ToLower().Contains(term) ||
                                     u.FirstName.ToLower().Contains(term) ||
                                     u.LastName.ToLower().Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var dtos = items.Adapt<List<UserDto>>();

        return PagedResult<UserDto>.Create(dtos, totalCount, request.PageNumber, request.PageSize);
    }
}

