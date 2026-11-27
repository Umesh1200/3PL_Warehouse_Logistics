using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Reference;
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
        if (!_currentUser.IsAuthenticated ||
            !_currentUser.UserId.HasValue ||
            _currentUser.UserId.Value == Guid.Empty)
        {
            throw new UnauthorizedException("Authentication is required to access normalization quarantine diagnostics.");
        }

        if (_currentUser.UserType != UserType.Staff || !_currentUser.IsStaff)
        {
            throw new ForbiddenException("Normalization quarantine diagnostics are restricted to authorized warehouse Staff.");
        }

        if (request.PageNumber < 1)
        {
            throw new ValidationException(nameof(request.PageNumber), "PageNumber must be at least 1.");
        }

        if (request.PageSize < 1 || request.PageSize > 100)
        {
            throw new ValidationException(nameof(request.PageSize), "PageSize must be between 1 and 100.");
        }

        long skipLong = (long)(request.PageNumber - 1) * request.PageSize;
        if (skipLong > int.MaxValue - request.PageSize)
        {
            throw new ValidationException(nameof(request.PageNumber), "Requested pagination offset exceeds maximum allowed bounds.");
        }

        var userId = _currentUser.UserId.Value;
        var dbUser = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.Id,
                u.IsDeleted,
                u.Status,
                u.UserType,
                u.AssignedFacilityId,
                Roles = u.UserRoles.Select(ur => new
                {
                    ur.Role.RoleName,
                    Permissions = ur.Role.RolePermissions.Select(rp => rp.PermissionKey).ToList()
                }).ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (dbUser == null || dbUser.IsDeleted || dbUser.Status != UserStatus.Active || dbUser.UserType != UserType.Staff)
        {
            throw new ForbiddenException("Caller is not an active database-verified Staff account.");
        }

        var dbRoleNames = dbUser.Roles.Select(r => r.RoleName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_currentUser.IsInRole(SystemRoles.SuperAdmin) && !dbRoleNames.Contains(SystemRoles.SuperAdmin))
        {
            throw new ForbiddenException("Claimed SuperAdmin role is not backed by an authoritative database role assignment.");
        }

        var dbPermissions = dbUser.Roles.SelectMany(r => r.Permissions).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool isVerifiedSuperAdmin = dbRoleNames.Contains(SystemRoles.SuperAdmin);
        bool hasNormalizationPerm = dbPermissions.Contains(Permissions.Global.NormalizationView) &&
                                    _currentUser.HasPermission(Permissions.Global.NormalizationView);
        bool hasFacilityManagePerm = dbPermissions.Contains(Permissions.Facilities.Manage) &&
                                     _currentUser.HasPermission(Permissions.Facilities.Manage);
        bool hasMultiFacilityPerm = dbPermissions.Contains(Permissions.Analytics.MultiFacilityView) &&
                                    _currentUser.HasPermission(Permissions.Analytics.MultiFacilityView);

        if (!isVerifiedSuperAdmin && !hasNormalizationPerm && !hasFacilityManagePerm)
        {
            throw new ForbiddenException("Caller lacks database-verified permission to inspect country normalization quarantine diagnostics.");
        }

        bool isPlatformAuthorized =
            isVerifiedSuperAdmin ||
            (hasNormalizationPerm && (hasMultiFacilityPerm || !dbUser.AssignedFacilityId.HasValue));

        if (request.FacilityId.HasValue &&
            _currentUser.RequestedFacilityId.HasValue &&
            request.FacilityId.Value != _currentUser.RequestedFacilityId.Value)
        {
            throw new ValidationException(
                nameof(request.FacilityId),
                "Conflicting facility scope selectors between query parameter 'facilityId' and 'X-Facility-Id' header.");
        }

        Guid? requestedFacility = request.FacilityId ?? _currentUser.RequestedFacilityId;
        Guid? scopedFacilityId = null;

        if (!isPlatformAuthorized)
        {
            if (!dbUser.AssignedFacilityId.HasValue || dbUser.AssignedFacilityId.Value == Guid.Empty)
            {
                throw new ForbiddenException(
                    "Facility-scoped Staff without platform multi-facility authorization must have an assigned facility.");
            }

            if (requestedFacility.HasValue && requestedFacility.Value != dbUser.AssignedFacilityId.Value)
            {
                throw new ForbiddenException(
                    $"Facility-scoped Staff cannot query normalization diagnostics for facility '{requestedFacility.Value}'.");
            }

            var assignedFacActive = await _db.Facilities
                .AsNoTracking()
                .AnyAsync(f => f.Id == dbUser.AssignedFacilityId.Value && !f.IsDeleted && f.IsActive, cancellationToken);

            if (!assignedFacActive)
            {
                throw new ForbiddenException("Assigned warehouse facility is inactive or deleted.");
            }

            scopedFacilityId = dbUser.AssignedFacilityId.Value;

            // Reject unauthorized explicit ?clientId= filters that lack active ClientFacilityAccess for this facility
            if (request.ClientId.HasValue)
            {
                var clientAuthorized = await _db.ClientFacilityAccesses
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .AnyAsync(cfa =>
                        cfa.ClientId == request.ClientId.Value &&
                        cfa.FacilityId == scopedFacilityId.Value &&
                        cfa.Status == FacilityAccessStatus.Active &&
                        _db.Clients.IgnoreQueryFilters().Any(c => c.Id == cfa.ClientId && !c.IsDeleted && c.Status == ClientStatus.Active),
                        cancellationToken);

                if (!clientAuthorized)
                {
                    throw new ForbiddenException(
                        $"Facility-scoped Staff cannot query diagnostics for client '{request.ClientId.Value}' without active facility access.");
                }
            }
        }

        var pageNumber = request.PageNumber;
        var pageSize = request.PageSize;
        var skip = (int)skipLong;

        var baseQuery = _db.CountryNormalizationAudits.AsNoTracking()
            .Where(a =>
                (a.EntityType != nameof(Facility) || _db.Facilities.IgnoreQueryFilters().Any(f => f.Id == a.EntityId && !f.IsDeleted)) &&
                (a.EntityType != nameof(Customer) || _db.Customers.IgnoreQueryFilters().Any(c => c.Id == a.EntityId && !c.IsDeleted)) &&
                (a.EntityType != nameof(Supplier) || _db.Suppliers.IgnoreQueryFilters().Any(s => s.Id == a.EntityId && !s.IsDeleted)) &&
                (a.EntityType != nameof(ShippingAddress) || _db.ShippingAddresses.IgnoreQueryFilters().Any(sa => sa.Id == a.EntityId && !sa.IsDeleted)));

        if (scopedFacilityId.HasValue)
        {
            var authFacId = scopedFacilityId.Value;
            // R05: Prevent stale audit.FacilityId from granting continued access after ClientFacilityAccess revocation or client deactivation
            baseQuery = baseQuery.Where(a =>
                (!a.ClientId.HasValue && a.FacilityId == authFacId) ||
                (a.ClientId.HasValue &&
                 (a.FacilityId == null || a.FacilityId == authFacId) &&
                 _db.Clients.IgnoreQueryFilters().Any(c => c.Id == a.ClientId.Value && !c.IsDeleted && c.Status == ClientStatus.Active) &&
                 _db.ClientFacilityAccesses.IgnoreQueryFilters().Any(cfa =>
                     cfa.ClientId == a.ClientId.Value &&
                     cfa.FacilityId == authFacId &&
                     cfa.Status == FacilityAccessStatus.Active)));
        }
        else if (requestedFacility.HasValue)
        {
            var reqFac = requestedFacility.Value;
            baseQuery = baseQuery.Where(a =>
                (!a.ClientId.HasValue && a.FacilityId == reqFac) ||
                (a.ClientId.HasValue &&
                 (a.FacilityId == reqFac ||
                  (a.FacilityId == null &&
                   _db.ClientFacilityAccesses.IgnoreQueryFilters().Any(cfa =>
                       cfa.ClientId == a.ClientId.Value &&
                       cfa.FacilityId == reqFac &&
                       cfa.Status == FacilityAccessStatus.Active)))));
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

        var activeProfiles = await _db.CountryProfiles
            .AsNoTracking()
            .Where(c => c.IsActive)
            .ToListAsync(cancellationToken);
        var activeAliases = await _db.CountryNormalizationAliases
            .AsNoTracking()
            .Where(a => a.IsActive)
            .ToListAsync(cancellationToken);
        var verifiedCatalogKeys = CountryDiagnosticSanitizer.BuildVerifiedCatalogKeySet(activeProfiles, activeAliases);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLowerInvariant();
            // F04-R01: Search ONLY safe persisted fields, currently verified & displayable catalog tokens,
            // and exact predefined controlled reason descriptions (NEVER raw Reason.Contains).
            var activeCountryCodes = activeProfiles
                .Select(p => CountryNormalizationAlias.NormalizeLookupKey(p.CountryCode))
                .Where(k => !string.IsNullOrEmpty(k))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var matchingCatalogTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in activeProfiles)
            {
                if (p.CountryCode.ToLowerInvariant().Contains(term)) matchingCatalogTokens.Add(p.CountryCode.ToLowerInvariant());
                if (p.IsoAlpha3Code.ToLowerInvariant().Contains(term)) matchingCatalogTokens.Add(p.IsoAlpha3Code.ToLowerInvariant());
                if (p.Name.ToLowerInvariant().Contains(term)) matchingCatalogTokens.Add(p.Name.ToLowerInvariant());
            }
            foreach (var al in activeAliases)
            {
                var targetCode = CountryNormalizationAlias.NormalizeLookupKey(al.CountryCode);
                if (!activeCountryCodes.Contains(targetCode)) continue;
                if (al.AliasKey.ToLowerInvariant().Contains(term)) matchingCatalogTokens.Add(al.AliasKey.ToLowerInvariant());
            }
            var allowedTokenList = matchingCatalogTokens.ToList();
            var matchingControlledReasons = CountryDiagnosticSanitizer.MatchControlledDescriptions(term);

            baseQuery = baseQuery.Where(a =>
                a.EntityType.ToLower().Contains(term) ||
                a.FieldName.ToLower().Contains(term) ||
                a.CorrelationBatchId.ToLower().Contains(term) ||
                (a.NormalizedCountryCode != null && a.NormalizedCountryCode.ToLower().Contains(term)) ||
                (matchingControlledReasons.Count > 0 && matchingControlledReasons.Contains(a.Reason)) ||
                (allowedTokenList.Count > 0 &&
                 a.OriginalCountryValue != null &&
                 a.OriginalCountryValue != CountryDiagnosticSanitizer.RedactedPlaceholder &&
                 allowedTokenList.Contains(a.OriginalCountryValue.ToLower())));
        }

        // Apply identical security predicates to counts, status filters, results and pagination
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
            .Skip(skip)
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
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(f => facilityIds.Contains(f.Id))
                .ToDictionaryAsync(f => f.Id, f => f.FacilityCode, cancellationToken);

        var items = pageRows.Select(r =>
        {
            var classifiedReason = CountryDiagnosticSanitizer.ClassifyReason(r.Reason);
            return new CountryNormalizationQuarantineItemDto(
                r.Id,
                r.EntityType,
                r.EntityId,
                r.FieldName,
                CountryDiagnosticSanitizer.SanitizeForDiagnostic(r.OriginalCountryValue, verifiedCatalogKeys),
                r.NormalizedCountryCode,
                r.NormalizationStatus,
                r.PreservedHistoricalValue,
                r.WasInferredFromParent,
                r.InferenceSource,
                classifiedReason.Description,
                r.FacilityId,
                r.FacilityId.HasValue && facilityCodeMap.TryGetValue(r.FacilityId.Value, out var code) ? code : null,
                r.ClientId,
                r.CorrelationBatchId,
                r.RecordedAtUtc,
                classifiedReason.Code);
        }).ToList();

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
