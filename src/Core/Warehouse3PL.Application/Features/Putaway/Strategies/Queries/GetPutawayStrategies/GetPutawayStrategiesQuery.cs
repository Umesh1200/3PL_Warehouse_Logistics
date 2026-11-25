using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Putaway.Strategies.Queries.GetPutawayStrategies;

public record GetPutawayStrategiesQuery(Guid FacilityId, Guid? ClientId = null) : IRequest<List<PutawayStrategyRuleDto>>;

public class GetPutawayStrategiesQueryHandler : IRequestHandler<GetPutawayStrategiesQuery, List<PutawayStrategyRuleDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly WarehouseOperationalAuthorization _operationalAuth;

    public GetPutawayStrategiesQueryHandler(
        IApplicationDbContext context,
        ICurrentUserContext currentUser,
        WarehouseOperationalAuthorization operationalAuth)
    {
        _context = context;
        _currentUser = currentUser;
        _operationalAuth = operationalAuth;
    }

    public async Task<List<PutawayStrategyRuleDto>> Handle(GetPutawayStrategiesQuery request, CancellationToken cancellationToken)
    {
        var scope = await _operationalAuth.ResolveQueryScopeAsync(request.ClientId, request.FacilityId, cancellationToken);
        var isVerifiedSuperAdmin = _currentUser.IsSuperAdmin && _currentUser.UserType == UserType.Staff;
        if (!isVerifiedSuperAdmin && !_currentUser.HasPermission(Permissions.Inbound.PutawayRules))
        {
            throw new ForbiddenException("Permission 'inbound:putaway:rules' is required to inspect putaway strategy rules.");
        }

        var targetFacilityId = scope.FacilityId ?? request.FacilityId;
        var query = _context.PutawayStrategyRules
            .AsNoTracking()
            .Where(r => r.FacilityId == targetFacilityId && r.IsActive && !r.IsDeleted);

        if (scope.ClientId.HasValue)
        {
            query = query.Where(r => r.ClientId == scope.ClientId.Value || r.ClientId == Guid.Empty);
        }

        var rules = await query
            .OrderBy(r => r.Sequence)
            .ToListAsync(cancellationToken);

        return rules.Select(r => new PutawayStrategyRuleDto
        {
            Id = r.Id,
            ClientId = r.ClientId,
            FacilityId = r.FacilityId,
            Sequence = r.Sequence,
            RuleName = r.RuleName,
            TempClassRequirement = r.TempClassRequirement,
            TargetVelocity = r.TargetVelocity,
            PreferConsolidation = r.PreferConsolidation,
            AllowDynamicOverflow = r.AllowDynamicOverflow,
            IsActive = r.IsActive,
            Version = r.Version
        }).ToList();
    }
}

