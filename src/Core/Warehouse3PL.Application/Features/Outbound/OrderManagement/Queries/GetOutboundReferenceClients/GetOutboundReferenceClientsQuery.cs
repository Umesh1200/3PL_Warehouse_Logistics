using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Outbound.OrderManagement.Queries.GetOutboundReferenceClients;

/// <summary>
/// Minimal active client tenant option for authorized warehouse staff performing outbound order entry or wave planning.
/// Strictly staff-only; omits sensitive contacts, billing settings, and directory metadata.
/// </summary>
public record OutboundReferenceClientDto
{
    public Guid Id { get; init; }
    public string AccountCode { get; init; } = string.Empty;
    public string CompanyName { get; init; } = string.Empty;
    public ClientStatus Status { get; init; }
}

public record GetOutboundReferenceClientsQuery(
    Guid? FacilityId = null,
    string? SearchTerm = null,
    int PageNumber = 1,
    int PageSize = 100) : IRequest<PagedResult<OutboundReferenceClientDto>>;

public class GetOutboundReferenceClientsQueryHandler
    : IRequestHandler<GetOutboundReferenceClientsQuery, PagedResult<OutboundReferenceClientDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public GetOutboundReferenceClientsQueryHandler(
        IApplicationDbContext context,
        ITenantProvider tenantProvider,
        ICurrentUserContext currentUser,
        OutboundOperationalAuthorization? outboundAuth = null)
    {
        _context = context;
        _outboundAuth = outboundAuth ?? new OutboundOperationalAuthorization(context, currentUser, tenantProvider);
    }

    public async Task<PagedResult<OutboundReferenceClientDto>> Handle(
        GetOutboundReferenceClientsQuery request,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(GetOutboundReferenceClientsQuery));

        var scope = await _outboundAuth.ResolveOutboundQueryScopeAsync(
            requestedClientId: null,
            requestedFacilityId: request.FacilityId == Guid.Empty ? null : request.FacilityId,
            allowCrossFacilityRead: true,
            allowClient: false,
            ct: cancellationToken);

        var dbQuery = _context.Clients
            .AsNoTracking()
            .Where(c => !c.IsDeleted && c.Status == ClientStatus.Active);

        if (scope.FacilityId.HasValue)
        {
            var facilityId = scope.FacilityId.Value;
            dbQuery = dbQuery.Where(c => _context.ClientFacilityAccesses.Any(fa =>
                fa.ClientId == c.Id &&
                fa.FacilityId == facilityId &&
                fa.Status == FacilityAccessStatus.Active));
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.Trim().ToLower();
            dbQuery = dbQuery.Where(c =>
                c.CompanyName.ToLower().Contains(term) ||
                c.AccountCode.ToLower().Contains(term));
        }

        var pageNumber = request.PageNumber > 0 ? request.PageNumber : 1;
        var pageSize = request.PageSize is > 0 and <= 200 ? request.PageSize : 100;

        var totalCount = await dbQuery.CountAsync(cancellationToken);
        var items = await dbQuery
            .OrderBy(c => c.CompanyName)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new OutboundReferenceClientDto
            {
                Id = c.Id,
                AccountCode = c.AccountCode,
                CompanyName = c.CompanyName,
                Status = c.Status
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<OutboundReferenceClientDto>(items, totalCount, pageNumber, pageSize);
    }
}
