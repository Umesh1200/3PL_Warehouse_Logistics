using Mapster;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Clients.Queries.GetClients;

public record GetClientsQuery : PagedQuery, IRequest<PagedResult<ClientDto>>;

public class GetClientsQueryHandler : IRequestHandler<GetClientsQuery, PagedResult<ClientDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public GetClientsQueryHandler(IApplicationDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<ClientDto>> Handle(GetClientsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Clients
            .Include(c => c.Contacts)
            .Include(c => c.Users)
            .Where(c => !c.IsDeleted)
            .AsNoTracking();

        // Multi-tenancy isolation: non-staff only see their assigned client
        if (!_currentUser.IsStaff && _currentUser.TenantId.HasValue)
        {
            query = query.Where(c => c.Id == _currentUser.TenantId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.Trim().ToLower();
            query = query.Where(c => c.CompanyName.ToLower().Contains(term) ||
                                     c.AccountCode.ToLower().Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var dtos = items.Adapt<List<ClientDto>>();

        return PagedResult<ClientDto>.Create(dtos, totalCount, request.PageNumber, request.PageSize);
    }
}

