using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Outbound.Picking.Queries;

/// <summary>
/// Minimal active warehouse staff reference DTO exposed to supervisors holding Permissions.Outbound.TasksManage
/// so pick task assignment works without requiring broad users:view directory grants.
/// </summary>
public record EligiblePickOperatorDto
{
    public Guid Id { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public Guid? AssignedFacilityId { get; init; }
}

public record GetEligiblePickOperatorsQuery(
    Guid? FacilityId = null,
    string? SearchTerm = null,
    int PageNumber = 1,
    int PageSize = 100) : IRequest<PagedResult<EligiblePickOperatorDto>>;

public class GetEligiblePickOperatorsQueryHandler
    : IRequestHandler<GetEligiblePickOperatorsQuery, PagedResult<EligiblePickOperatorDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public GetEligiblePickOperatorsQueryHandler(
        IApplicationDbContext context,
        ITenantProvider tenantProvider,
        ICurrentUserContext currentUser,
        OutboundOperationalAuthorization? outboundAuth = null)
    {
        _context = context;
        _outboundAuth = outboundAuth ?? new OutboundOperationalAuthorization(context, currentUser, tenantProvider);
    }

    public async Task<PagedResult<EligiblePickOperatorDto>> Handle(
        GetEligiblePickOperatorsQuery request,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(GetEligiblePickOperatorsQuery));

        var scope = await _outboundAuth.ResolveOutboundQueryScopeAsync(
            requestedClientId: null,
            requestedFacilityId: request.FacilityId == Guid.Empty ? null : request.FacilityId,
            allowCrossFacilityRead: true,
            allowClient: false,
            ct: cancellationToken);

        var dbQuery = _context.Users
            .AsNoTracking()
            .Where(u => !u.IsDeleted &&
                        u.Status == UserStatus.Active &&
                        u.UserType == UserType.Staff);

        if (scope.FacilityId.HasValue)
        {
            var facilityId = scope.FacilityId.Value;
            dbQuery = dbQuery.Where(u => u.AssignedFacilityId == facilityId);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.Trim().ToLower();
            dbQuery = dbQuery.Where(u =>
                u.FirstName.ToLower().Contains(term) ||
                u.LastName.ToLower().Contains(term) ||
                u.Email.ToLower().Contains(term));
        }

        var pageNumber = request.PageNumber > 0 ? request.PageNumber : 1;
        var pageSize = request.PageSize is > 0 and <= 200 ? request.PageSize : 100;

        var totalCount = await dbQuery.CountAsync(cancellationToken);
        var items = await dbQuery
            .OrderBy(u => u.LastName)
            .ThenBy(u => u.FirstName)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new EligiblePickOperatorDto
            {
                Id = u.Id,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Email = u.Email,
                AssignedFacilityId = u.AssignedFacilityId
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<EligiblePickOperatorDto>(items, totalCount, pageNumber, pageSize);
    }
}
