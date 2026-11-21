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

        // Multi-tenancy isolation: non-staff only see their assigned client; fail-closed without tenant
        if (!_currentUser.IsStaff)
        {
            if (!_currentUser.TenantId.HasValue)
            {
                throw new ForbiddenException("Non-staff users without an assigned tenant context are forbidden from accessing client records.");
            }
            query = query.Where(c => c.Id == _currentUser.TenantId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.Trim().ToLower();
            query = query.Where(c => c.CompanyName.ToLower().Contains(term) ||
                                     c.AccountCode.ToLower().Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        // Server-side sorting with strict validated contract
        if (!string.IsNullOrWhiteSpace(request.SortBy))
        {
            query = (request.SortBy.Trim().ToLowerInvariant()) switch
            {
                "companyname" => request.SortDescending
                    ? query.OrderByDescending(c => c.CompanyName)
                    : query.OrderBy(c => c.CompanyName),
                "accountcode" => request.SortDescending
                    ? query.OrderByDescending(c => c.AccountCode)
                    : query.OrderBy(c => c.AccountCode),
                "slatier" => request.SortDescending
                    ? query.OrderByDescending(c => c.SlaTier)
                    : query.OrderBy(c => c.SlaTier),
                "status" => request.SortDescending
                    ? query.OrderByDescending(c => c.Status)
                    : query.OrderBy(c => c.Status),
                "currency" => request.SortDescending
                    ? query.OrderByDescending(c => c.Currency)
                    : query.OrderBy(c => c.Currency),
                "createdat" => request.SortDescending
                    ? query.OrderByDescending(c => c.CreatedAt)
                    : query.OrderBy(c => c.CreatedAt),
                _ => request.SortDescending
                    ? query.OrderByDescending(c => c.CreatedAt)
                    : query.OrderBy(c => c.CreatedAt)
            };
        }
        else
        {
            query = request.SortDescending
                ? query.OrderByDescending(c => c.CreatedAt)
                : query.OrderByDescending(c => c.CreatedAt);
        }

        var items = await query
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var dtos = items.Adapt<List<ClientDto>>();

        return PagedResult<ClientDto>.Create(dtos, totalCount, request.PageNumber, request.PageSize);
    }
}

