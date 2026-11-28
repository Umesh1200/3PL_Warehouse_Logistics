using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.Inbound.Suppliers;
using Warehouse3PL.Domain.Constants;

namespace Warehouse3PL.Application.Features.Inbound.Suppliers.Queries.GetSuppliers;

public record GetSuppliersQuery : PagedQuery, IRequest<PagedResult<SupplierDto>>
{
    public Guid? ClientId { get; init; }
    public bool? IsActive { get; init; }
}

public class GetSuppliersQueryHandler : IRequestHandler<GetSuppliersQuery, PagedResult<SupplierDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly PartnerMasterAuthorization _auth;

    public GetSuppliersQueryHandler(
        IApplicationDbContext context,
        ICurrentUserContext currentUser,
        PartnerMasterAuthorization? auth = null)
    {
        _context = context;
        _auth = auth ?? new PartnerMasterAuthorization(context, currentUser);
    }

    public async Task<PagedResult<SupplierDto>> Handle(GetSuppliersQuery request, CancellationToken cancellationToken)
    {
        var (_, targetClientId) = await _auth.ResolveAuthorizedClientScopeAsync(
            request.ClientId,
            allowClientSelf: true,
            cancellationToken,
            allowedStaffPermissions: [Permissions.Inbound.SuppliersView, Permissions.Clients.View]);

        var query = _context.Suppliers
            .IgnoreQueryFilters()
            .Include(s => s.Contacts)
            .AsNoTracking()
            .Where(s => s.ClientId == targetClientId && !s.IsDeleted);

        if (request.IsActive.HasValue)
        {
            query = query.Where(s => s.IsActive == request.IsActive.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.Trim().ToLower();
            query = query.Where(s =>
                s.SupplierCode.ToLower().Contains(term) ||
                s.SupplierName.ToLower().Contains(term) ||
                (s.ContactName != null && s.ContactName.ToLower().Contains(term)));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(s => s.SupplierName)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return PagedResult<SupplierDto>.Create(
            items.Select(SupplierLifecycleAndContactHandlersMapping.ToDto).ToList(),
            totalCount,
            request.PageNumber,
            request.PageSize);
    }
}
