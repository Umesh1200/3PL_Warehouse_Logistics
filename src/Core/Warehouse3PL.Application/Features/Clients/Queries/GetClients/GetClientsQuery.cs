using Mapster;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Clients.Queries.GetClients;

public record GetClientsQuery : PagedQuery, IRequest<PagedResult<ClientDto>>
{
    public ClientStatus? Status { get; init; }
}

public class GetClientsQueryHandler : IRequestHandler<GetClientsQuery, PagedResult<ClientDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly PartnerMasterAuthorization _auth;

    public GetClientsQueryHandler(
        IApplicationDbContext context,
        ICurrentUserContext currentUser,
        PartnerMasterAuthorization? auth = null)
    {
        _context = context;
        _auth = auth ?? new PartnerMasterAuthorization(context, currentUser);
    }

    public async Task<PagedResult<ClientDto>> Handle(GetClientsQuery request, CancellationToken cancellationToken)
    {
        var caller = await _auth.VerifyAuthenticatedCallerAsync(cancellationToken);

        var query = _context.Clients
            .Include(c => c.Contacts)
            .Include(c => c.Users)
            .Where(c => !c.IsDeleted)
            .AsNoTracking();

        if (caller.UserType == UserType.Client)
        {
            if (!caller.AssignedClientId.HasValue || caller.AssignedClientId.Value == Guid.Empty)
            {
                throw new ForbiddenException("Client user without a verified tenant context cannot access client records.");
            }
            query = query.Where(c => c.Id == caller.AssignedClientId.Value);
        }
        else if (!caller.IsSuperAdmin)
        {
            if (!caller.Permissions.Contains(Permissions.Clients.View))
            {
                throw new ForbiddenException($"Staff user lacks required database-verified permission ({Permissions.Clients.View}).");
            }
            if (!caller.AssignedFacilityId.HasValue || caller.AssignedFacilityId.Value == Guid.Empty)
            {
                throw new ForbiddenException("Non-SuperAdmin Staff user has no assigned facility scope.");
            }

            var staffFacilityId = caller.AssignedFacilityId.Value;
            var facilityClientIds = _context.ClientFacilityAccesses
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(a => a.FacilityId == staffFacilityId)
                .Select(a => a.ClientId);

            query = query.Where(c => facilityClientIds.Contains(c.Id));
        }

        if (request.Status.HasValue)
        {
            query = query.Where(c => c.Status == request.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.Trim().ToLower();
            query = query.Where(c => c.CompanyName.ToLower().Contains(term) ||
                                     c.AccountCode.ToLower().Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.SortBy))
        {
            query = request.SortBy.Trim().ToLowerInvariant() switch
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
            query = query.OrderByDescending(c => c.CreatedAt);
        }

        var items = await query
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var dtos = items.Adapt<List<ClientDto>>();

        return PagedResult<ClientDto>.Create(dtos, totalCount, request.PageNumber, request.PageSize);
    }
}
