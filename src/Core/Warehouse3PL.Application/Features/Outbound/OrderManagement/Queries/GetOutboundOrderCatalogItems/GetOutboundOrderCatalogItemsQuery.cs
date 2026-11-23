using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.Application.Features.Outbound.OrderManagement.Queries.GetOutboundOrderCatalogItems;

/// <summary>
/// Minimal tenant-scoped SKU reference DTO exposed solely for authorized outbound order creation/import.
/// Avoids granting broad admin catalog directory permissions to client or floor roles.
/// </summary>
public record OutboundCatalogItemDto
{
    public Guid Id { get; init; }
    public Guid ClientId { get; init; }
    public string Sku { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? ShortName { get; init; }
    public StorageTempClass StorageTempClass { get; init; }
    public bool IsLotTracked { get; init; }
    public bool RequiresExpiryDate { get; init; }
}

public record GetOutboundOrderCatalogItemsQuery(
    Guid? ClientId = null,
    Guid? FacilityId = null,
    string? SearchTerm = null,
    int PageNumber = 1,
    int PageSize = 100) : IRequest<PagedResult<OutboundCatalogItemDto>>;

public class GetOutboundOrderCatalogItemsQueryHandler
    : IRequestHandler<GetOutboundOrderCatalogItemsQuery, PagedResult<OutboundCatalogItemDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly ICurrentUserContext _currentUser;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public GetOutboundOrderCatalogItemsQueryHandler(
        IApplicationDbContext context,
        ITenantProvider tenantProvider,
        ICurrentUserContext currentUser,
        OutboundOperationalAuthorization? outboundAuth = null)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _currentUser = currentUser;
        _outboundAuth = outboundAuth ?? new OutboundOperationalAuthorization(context, currentUser, tenantProvider);
    }

    public async Task<PagedResult<OutboundCatalogItemDto>> Handle(
        GetOutboundOrderCatalogItemsQuery request,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureIdentityAllowed(allowClient: true, nameof(GetOutboundOrderCatalogItemsQuery));

        Guid targetClientId;
        if (_currentUser.UserType == UserType.Client && !_currentUser.IsSuperAdmin)
        {
            targetClientId = _outboundAuth.ResolveValidatedClientTenantId(request.ClientId);
            if (request.FacilityId.HasValue && request.FacilityId.Value != Guid.Empty)
            {
                await _outboundAuth.EnsureClientHasActiveFacilityAccessAsync(
                    targetClientId,
                    request.FacilityId.Value,
                    cancellationToken);
            }
            else
            {
                await _outboundAuth.GetActiveClientFacilitiesAsync(targetClientId, cancellationToken);
            }
        }
        else
        {
            _outboundAuth.EnsureStaffOperationalIdentity(nameof(GetOutboundOrderCatalogItemsQuery));
            if (request.FacilityId.HasValue && request.FacilityId.Value != Guid.Empty)
            {
                _outboundAuth.ValidateStaffReadFacilityAccess(
                    request.FacilityId.Value,
                    nameof(GetOutboundOrderCatalogItemsQuery));
            }

            var resolvedClientId = request.ClientId ?? _tenantProvider.TenantId ?? _currentUser.TenantId;
            if (!resolvedClientId.HasValue || resolvedClientId.Value == Guid.Empty)
            {
                throw new ValidationException(
                    nameof(request.ClientId),
                    "A valid clientId query parameter or X-Tenant-Id header is required for staff catalog lookup.");
            }
            targetClientId = resolvedClientId.Value;
        }

        var clientExists = await _context.Clients
            .AsNoTracking()
            .AnyAsync(c => c.Id == targetClientId && !c.IsDeleted && c.Status == ClientStatus.Active, cancellationToken);
        if (!clientExists)
        {
            throw new EntityNotFoundException(nameof(Client), targetClientId);
        }

        var dbQuery = _context.Items
            .AsNoTracking()
            .Where(i => !i.IsDeleted &&
                        i.Status == ItemStatus.Active &&
                        i.ClientId == targetClientId);

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.Trim().ToLower();
            dbQuery = dbQuery.Where(i =>
                i.Sku.ToLower().Contains(term) ||
                i.Description.ToLower().Contains(term) ||
                (i.ShortName != null && i.ShortName.ToLower().Contains(term)));
        }

        var pageNumber = request.PageNumber > 0 ? request.PageNumber : 1;
        var pageSize = request.PageSize is > 0 and <= 200 ? request.PageSize : 100;

        var totalCount = await dbQuery.CountAsync(cancellationToken);
        var items = await dbQuery
            .OrderBy(i => i.Sku)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(i => new OutboundCatalogItemDto
            {
                Id = i.Id,
                ClientId = i.ClientId,
                Sku = i.Sku,
                Description = i.Description,
                ShortName = i.ShortName,
                StorageTempClass = i.StorageTempClass,
                IsLotTracked = i.IsLotTracked,
                RequiresExpiryDate = i.RequiresExpiryDate
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<OutboundCatalogItemDto>(items, totalCount, pageNumber, pageSize);
    }
}
