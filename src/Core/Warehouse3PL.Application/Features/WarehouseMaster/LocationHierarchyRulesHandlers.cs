using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.WarehouseMaster;

public sealed record GetLocationHierarchyRulesQuery(Guid? FacilityId = null, bool IncludeGlobal = true)
    : IRequest<List<LocationHierarchyRuleDto>>;

public sealed class GetLocationHierarchyRulesQueryHandler
    : IRequestHandler<GetLocationHierarchyRulesQuery, List<LocationHierarchyRuleDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly WarehouseMasterAuthorization _auth;

    public GetLocationHierarchyRulesQueryHandler(IApplicationDbContext db, WarehouseMasterAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<List<LocationHierarchyRuleDto>> Handle(GetLocationHierarchyRulesQuery request, CancellationToken cancellationToken)
    {
        Guid? resolvedFacilityId = null;
        if (request.FacilityId.HasValue && request.FacilityId.Value != Guid.Empty)
        {
            var (_, facId, _) = await _auth.ResolveAuthorizedFacilityReadAsync(
                request.FacilityId,
                cancellationToken,
                Permissions.Facilities.LocationsView,
                Permissions.Facilities.View);
            resolvedFacilityId = facId;
        }
        else
        {
            await _auth.VerifyStaffPermissionAsync(
                cancellationToken,
                Permissions.Facilities.LocationsView,
                Permissions.Facilities.View);
        }

        var query = _db.LocationHierarchyRules.AsNoTracking().AsQueryable();

        if (resolvedFacilityId.HasValue)
        {
            query = request.IncludeGlobal
                ? query.Where(r => r.FacilityId == resolvedFacilityId.Value || r.FacilityId == null)
                : query.Where(r => r.FacilityId == resolvedFacilityId.Value);
        }
        else
        {
            query = query.Where(r => r.FacilityId == null);
        }

        var rules = await query
            .OrderBy(r => r.FacilityId == null ? 1 : 0)
            .ThenBy(r => r.ParentLocationType)
            .ThenBy(r => r.ChildLocationType)
            .ToListAsync(cancellationToken);

        return rules.Select(WarehouseMasterMappers.ToDto).ToList();
    }
}

public sealed record UpsertLocationHierarchyRuleCommand : IRequest<LocationHierarchyRuleDto>
{
    public Guid? FacilityId { get; init; }
    public LocationType? ParentLocationType { get; init; }
    public LocationType ChildLocationType { get; init; }
    public int MaxDepth { get; init; } = 6;
    public bool IsStorableLeaf { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed class UpsertLocationHierarchyRuleCommandValidator : AbstractValidator<UpsertLocationHierarchyRuleCommand>
{
    public UpsertLocationHierarchyRuleCommandValidator()
    {
        RuleFor(x => x.ChildLocationType).IsInEnum();
        RuleFor(x => x.ParentLocationType).IsInEnum().When(x => x.ParentLocationType.HasValue);
        RuleFor(x => x.MaxDepth).InclusiveBetween(1, 10);
    }
}

/// <summary>
/// MDM21-R02 & R04: Serializes facility rule changes via ExecuteWithFacilityHierarchyLockAsync and
/// global rule changes via ExecuteWithGlobalHierarchyLockAsync (exclusive global lock vs shared facility locks),
/// rejecting any rule mutation that would silently invalidate existing non-deleted locations.
/// </summary>
public sealed class UpsertLocationHierarchyRuleCommandHandler
    : IRequestHandler<UpsertLocationHierarchyRuleCommand, LocationHierarchyRuleDto>
{
    private readonly IApplicationDbContext _db;
    private readonly WarehouseMasterAuthorization _auth;

    public UpsertLocationHierarchyRuleCommandHandler(IApplicationDbContext db, WarehouseMasterAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<LocationHierarchyRuleDto> Handle(UpsertLocationHierarchyRuleCommand request, CancellationToken cancellationToken)
    {
        if (request.FacilityId.HasValue && request.FacilityId.Value != Guid.Empty)
        {
            var (userId, scopeFacilityId, _) = await _auth.ResolveAuthorizedFacilityAsync(
                request.FacilityId,
                cancellationToken,
                Permissions.Facilities.LocationsManage,
                Permissions.Facilities.Manage);

            return await _db.ExecuteWithFacilityHierarchyLockAsync(scopeFacilityId, async () =>
            {
                var facilityLocations = await _db.Locations
                    .AsNoTracking()
                    .Where(l => l.FacilityId == scopeFacilityId && !l.IsDeleted)
                    .ToListAsync(cancellationToken);

                LocationHierarchyRuleEvaluator.ValidateExistingLocationsAgainstProposedRule(
                    facilityLocations,
                    facilityLocations.ToDictionary(l => l.Id),
                    request.ParentLocationType,
                    request.ChildLocationType,
                    request.MaxDepth,
                    request.IsStorableLeaf,
                    request.IsActive);

                return await UpsertRuleCoreAsync(scopeFacilityId, userId, request, cancellationToken);
            }, cancellationToken);
        }
        else
        {
            var userId = await _auth.RequireGlobalHierarchyRuleAdminAsync(cancellationToken);

            return await _db.ExecuteWithGlobalHierarchyLockAsync(async () =>
            {
                // Identify facilities that have an explicit facility override for this exact transition;
                // all other facilities inherit the global rule and must remain valid under the proposed change.
                var overriddenFacilityIds = await _db.LocationHierarchyRules
                    .AsNoTracking()
                    .Where(r => r.FacilityId != null &&
                                r.ParentLocationType == request.ParentLocationType &&
                                r.ChildLocationType == request.ChildLocationType)
                    .Select(r => r.FacilityId!.Value)
                    .ToHashSetAsync(cancellationToken);

                var candidateLocations = await _db.Locations
                    .AsNoTracking()
                    .Where(l => !l.IsDeleted && !overriddenFacilityIds.Contains(l.FacilityId))
                    .ToListAsync(cancellationToken);

                LocationHierarchyRuleEvaluator.ValidateExistingLocationsAgainstProposedRule(
                    candidateLocations,
                    candidateLocations.ToDictionary(l => l.Id),
                    request.ParentLocationType,
                    request.ChildLocationType,
                    request.MaxDepth,
                    request.IsStorableLeaf,
                    request.IsActive);

                return await UpsertRuleCoreAsync(null, userId, request, cancellationToken);
            }, cancellationToken);
        }
    }

    private async Task<LocationHierarchyRuleDto> UpsertRuleCoreAsync(
        Guid? scopeFacilityId,
        Guid userId,
        UpsertLocationHierarchyRuleCommand request,
        CancellationToken cancellationToken)
    {
        var existing = await _db.LocationHierarchyRules
            .FirstOrDefaultAsync(r =>
                r.FacilityId == scopeFacilityId &&
                r.ParentLocationType == request.ParentLocationType &&
                r.ChildLocationType == request.ChildLocationType,
                cancellationToken);

        if (existing != null)
        {
            existing.MaxDepth = request.MaxDepth;
            existing.IsStorableLeaf = request.IsStorableLeaf;
            existing.IsActive = request.IsActive;
            existing.ConfigRevision++;
            existing.UpdatedBy = userId;
            await _db.SaveChangesAsync(cancellationToken);
            return WarehouseMasterMappers.ToDto(existing);
        }

        var rule = new LocationHierarchyRule
        {
            FacilityId = scopeFacilityId,
            ParentLocationType = request.ParentLocationType,
            ChildLocationType = request.ChildLocationType,
            MaxDepth = request.MaxDepth,
            IsStorableLeaf = request.IsStorableLeaf,
            IsActive = request.IsActive,
            ConfigRevision = 1,
            CreatedBy = userId
        };

        _db.LocationHierarchyRules.Add(rule);
        await _db.SaveChangesAsync(cancellationToken);
        return WarehouseMasterMappers.ToDto(rule);
    }
}
