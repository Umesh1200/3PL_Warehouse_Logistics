using Mapster;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;

namespace Warehouse3PL.Application.Features.Roles.Queries.GetRoles;

public record GetRolesQuery : IRequest<IReadOnlyList<RoleDto>>;

public class GetRolesQueryHandler : IRequestHandler<GetRolesQuery, IReadOnlyList<RoleDto>>
{
    private readonly IApplicationDbContext _context;

    public GetRolesQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<RoleDto>> Handle(GetRolesQuery request, CancellationToken cancellationToken)
    {
        var roles = await _context.Roles
            .Include(r => r.RolePermissions)
            .AsNoTracking()
            .OrderBy(r => r.Id)
            .ToListAsync(cancellationToken);

        return roles.Adapt<List<RoleDto>>();
    }
}

