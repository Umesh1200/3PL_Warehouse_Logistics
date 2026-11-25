using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Reference.Queries;

public sealed record GetCompliancePoliciesQuery(
    string? CountryCode = null,
    string? PolicyCode = null,
    CompliancePolicyApprovalStatus? ApprovalStatus = null,
    bool? IsActive = null) : IRequest<IReadOnlyList<CountryCompliancePolicyDto>>;

public sealed class GetCompliancePoliciesQueryHandler
    : IRequestHandler<GetCompliancePoliciesQuery, IReadOnlyList<CountryCompliancePolicyDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly CompliancePlatformAuthorization _auth;

    public GetCompliancePoliciesQueryHandler(IApplicationDbContext db, CompliancePlatformAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<IReadOnlyList<CountryCompliancePolicyDto>> Handle(
        GetCompliancePoliciesQuery request,
        CancellationToken cancellationToken)
    {
        await _auth.RequireVerifiedStaffPermissionAsync(
            cancellationToken,
            Permissions.Global.ComplianceView,
            Permissions.Global.ComplianceManage);

        var query = _db.CountryCompliancePolicies.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.CountryCode))
        {
            var cc = request.CountryCode.Trim().ToUpperInvariant();
            query = query.Where(p => p.CountryCode == cc);
        }

        if (!string.IsNullOrWhiteSpace(request.PolicyCode))
        {
            var pc = request.PolicyCode.Trim().ToUpperInvariant();
            query = query.Where(p => p.PolicyCode == pc);
        }

        if (request.ApprovalStatus.HasValue)
        {
            query = query.Where(p => p.ApprovalStatus == request.ApprovalStatus.Value);
        }

        if (request.IsActive.HasValue)
        {
            query = query.Where(p => p.IsActive == request.IsActive.Value);
        }

        var rows = await query
            .OrderBy(p => p.CountryCode)
            .ThenBy(p => p.PolicyCode)
            .ThenByDescending(p => p.Version)
            .ToListAsync(cancellationToken);

        return rows.Select(p => p.ToDto()).ToList();
    }
}

public sealed record GetCompliancePolicyByIdQuery(Guid PolicyId) : IRequest<CountryCompliancePolicyDto?>;

public sealed class GetCompliancePolicyByIdQueryHandler
    : IRequestHandler<GetCompliancePolicyByIdQuery, CountryCompliancePolicyDto?>
{
    private readonly IApplicationDbContext _db;
    private readonly CompliancePlatformAuthorization _auth;

    public GetCompliancePolicyByIdQueryHandler(IApplicationDbContext db, CompliancePlatformAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<CountryCompliancePolicyDto?> Handle(
        GetCompliancePolicyByIdQuery request,
        CancellationToken cancellationToken)
    {
        await _auth.RequireVerifiedStaffPermissionAsync(
            cancellationToken,
            Permissions.Global.ComplianceView,
            Permissions.Global.ComplianceManage);

        var row = await _db.CountryCompliancePolicies
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.PolicyId, cancellationToken);

        return row?.ToDto();
    }
}
