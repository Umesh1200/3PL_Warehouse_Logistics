using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;

namespace Warehouse3PL.Application.Features.Inbound.Suppliers.Queries.GetSuppliers;

public record GetSuppliersQuery : PagedQuery, IRequest<PagedResult<SupplierDto>>
{
    public Guid? ClientId { get; init; }
    public bool? IsActive { get; init; }
}

public class GetSuppliersQueryHandler : IRequestHandler<GetSuppliersQuery, PagedResult<SupplierDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public GetSuppliersQueryHandler(IApplicationDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<SupplierDto>> Handle(GetSuppliersQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Suppliers
            .AsNoTracking();

        if (!_currentUser.IsStaff)
        {
            query = query.Where(s => s.ClientId == _currentUser.TenantId);
        }
        else if (request.ClientId.HasValue)
        {
            query = query.Where(s => s.ClientId == request.ClientId.Value);
        }

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
            .Select(s => new SupplierDto
            {
                Id = s.Id,
                ClientId = s.ClientId,
                SupplierCode = s.SupplierCode,
                SupplierName = s.SupplierName,
                ContactName = s.ContactName,
                Email = s.Email,
                Phone = s.Phone,
                AddressLine1 = s.AddressLine1,
                AddressLine2 = s.AddressLine2,
                City = s.City,
                State = s.State,
                PostalCode = s.PostalCode,
                Country = s.Country,
                IsActive = s.IsActive,
                CreatedAt = s.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return PagedResult<SupplierDto>.Create(items, totalCount, request.PageNumber, request.PageSize);
    }
}

