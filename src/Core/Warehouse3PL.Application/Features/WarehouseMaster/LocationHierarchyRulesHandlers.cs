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
            var (_, facId, _) = await _auth.ResolveAuthorizedFacilityAsync(
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
        Guid userId;
        Guid? scopeFacilityId = null;

        if (request.FacilityId.HasValue && request.FacilityId.Value != Guid.Empty)
        {
            var resolved = await _auth.ResolveAuthorizedFacilityAsync(
                request.FacilityId,
                cancellationToken,
                Permissions.Facilities.LocationsManage,
                Permissions.Facilities.Manage);
            userId = resolved.UserId;
            scopeFacilityId = resolved.FacilityId;

            var facilityExists = await _db.Facilities
                .AsNoTracking()
                .AnyAsync(f => f.Id == scopeFacilityId.Value && !f.IsDeleted, cancellationToken);
            if (!facilityExists)
                throw new EntityNotFoundException(nameof(Facility), scopeFacilityId.Value);
        }
        else
        {
            userId = await _auth.RequireGlobalHierarchyRuleAdminAsync(cancellationToken);
        }

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
