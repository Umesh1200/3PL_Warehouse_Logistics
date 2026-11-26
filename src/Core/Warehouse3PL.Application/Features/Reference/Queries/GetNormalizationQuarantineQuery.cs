using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Reference.Queries;

public sealed record GetNormalizationQuarantineQuery(
    CountryNormalizationStatus? Status = CountryNormalizationStatus.PendingNormalization,
    bool IncludeAllStatuses = false,
    string? EntityType = null,
    Guid? FacilityId = null,
    Guid? ClientId = null,
    string? Search = null,
    int PageNumber = 1,
    int PageSize = 25) : IRequest<PagedNormalizationQuarantineResponse>;

public sealed class GetNormalizationQuarantineQueryHandler
    : IRequestHandler<GetNormalizationQuarantineQuery, PagedNormalizationQuarantineResponse>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public GetNormalizationQuarantineQueryHandler(
        IApplicationDbContext db,
        ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<PagedNormalizationQuarantineResponse> Handle(
        GetNormalizationQuarantineQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw new UnauthorizedException("Authentication is required to access normalization quarantine diagnostics.");
        }

        if (_currentUser.UserType != UserType.Staff)
        {
            throw new ForbiddenException("Normalization quarantine diagnostics are restricted to authorized warehouse Staff.");
        }

        bool isVerifiedSuperAdmin = _currentUser.IsSuperAdmin;
        bool hasAllowedPermission = isVerifiedSuperAdmin ||
            _currentUser.HasPermission(Permissions.Global.NormalizationView) ||
            _currentUser.HasPermission(Permissions.Global.ComplianceView) ||
            _currentUser.HasPermission(Permissions.Global.ComplianceManage) ||
            _currentUser.HasPermission(Permissions.Global.ReferenceView) ||
            _currentUser.HasPermission(Permissions.Facilities.Manage);

        if (!hasAllowedPermission)
        {
            throw new ForbiddenException("Caller lacks permission to inspect country normalization quarantine diagnostics.");
        }

        bool hasGlobalScopePerm =
            _currentUser.HasPermission(Permissions.Global.NormalizationView) ||
            _currentUser.HasPermission(Permissions.Global.ComplianceView) ||
            _currentUser.HasPermission(Permissions.Global.ComplianceManage);

        bool isPlatformAuthorized =
            isVerifiedSuperAdmin ||
            _currentUser.HasPermission(Permissions.Analytics.MultiFacilityView) ||
            (hasGlobalScopePerm && !_currentUser.FacilityId.HasValue);

        Guid? requestedFacility = request.FacilityId ?? _currentUser.RequestedFacilityId;
        Guid? scopedFacilityId = null;

        if (!isPlatformAuthorized)
        {
            if (!_currentUser.FacilityId.HasValue || _currentUser.FacilityId.Value == Guid.Empty)
            {
                throw new ForbiddenException(
                    "Facility-scoped Staff without platform multi-facility authorization must have an assigned facility.");
            }

            if (requestedFacility.HasValue && requestedFacility.Value != _currentUser.FacilityId.Value)
            {
                throw new ForbiddenException(
                    $"Facility-scoped Staff cannot query normalization diagnostics for facility '{requestedFacility.Value}'.");
            }

            scopedFacilityId = _currentUser.FacilityId.Value;
        }

        var pageNumber = Math.Max(1, request.PageNumber);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var baseQuery = _db.CountryNormalizationAudits.AsNoTracking();

        if (scopedFacilityId.HasValue)
        {
            var authFacId = scopedFacilityId.Value;
            baseQuery = baseQuery.Where(a =>
                a.FacilityId == authFacId ||
                (a.FacilityId == null &&
                 a.ClientId.HasValue &&
                 _db.ClientFacilityAccesses.Any(cfa =>
                     cfa.ClientId == a.ClientId.Value &&
                     cfa.FacilityId == authFacId &&
                     cfa.Status == FacilityAccessStatus.Active)));
        }
        else if (requestedFacility.HasValue)
        {
            var reqFac = requestedFacility.Value;
            baseQuery = baseQuery.Where(a => a.FacilityId == reqFac);
        }

        if (request.ClientId.HasValue)
        {
            var cid = request.ClientId.Value;
            baseQuery = baseQuery.Where(a => a.ClientId == cid);
        }

        if (!string.IsNullOrWhiteSpace(request.EntityType))
        {
            var et = request.EntityType.Trim();
            baseQuery = baseQuery.Where(a => a.EntityType == et);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLowerInvariant();
            baseQuery = baseQuery.Where(a =>
                a.EntityType.ToLower().Contains(term) ||
                a.FieldName.ToLower().Contains(term) ||
                (a.OriginalCountryValue != null && a.OriginalCountryValue.ToLower().Contains(term)) ||
                (a.NormalizedCountryCode != null && a.NormalizedCountryCode.ToLower().Contains(term)) ||
                a.Reason.ToLower().Contains(term));
        }

        // Compute scoped counts before status filter (no cross-tenant/cross-facility leakage)
        var pendingCount = await baseQuery.CountAsync(
            a => a.NormalizationStatus == CountryNormalizationStatus.PendingNormalization,
            cancellationToken);
        var normalizedCount = await baseQuery.CountAsync(
            a => a.NormalizationStatus == CountryNormalizationStatus.Normalized,
            cancellationToken);

        var filteredQuery = baseQuery;
        if (!request.IncludeAllStatuses)
        {
            var targetStatus = request.Status ?? CountryNormalizationStatus.PendingNormalization;
            filteredQuery = filteredQuery.Where(a => a.NormalizationStatus == targetStatus);
        }

        var totalCount = await filteredQuery.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

        var pageRows = await filteredQuery
            .OrderByDescending(a => a.RecordedAtUtc)
            .ThenBy(a => a.EntityType)
            .ThenBy(a => a.EntityId)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var facilityIds = pageRows
            .Where(r => r.FacilityId.HasValue)
            .Select(r => r.FacilityId!.Value)
            .Distinct()
            .ToList();

        var facilityCodeMap = facilityIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Facilities
                .AsNoTracking()
                .Where(f => facilityIds.Contains(f.Id))
                .ToDictionaryAsync(f => f.Id, f => f.FacilityCode, cancellationToken);

        var items = pageRows.Select(r => new CountryNormalizationQuarantineItemDto(
            r.Id,
            r.EntityType,
            r.EntityId,
            r.FieldName,
            r.OriginalCountryValue,
            r.NormalizedCountryCode,
            r.NormalizationStatus,
            r.PreservedHistoricalValue,
            r.WasInferredFromParent,
            r.InferenceSource,
            r.Reason,
            r.FacilityId,
            r.FacilityId.HasValue && facilityCodeMap.TryGetValue(r.FacilityId.Value, out var code) ? code : null,
            r.ClientId,
            r.CorrelationBatchId,
            r.RecordedAtUtc)).ToList();

        return new PagedNormalizationQuarantineResponse(
            items,
            totalCount,
            pageNumber,
            pageSize,
            totalPages,
            pendingCount,
            normalizedCount);
    }
}
