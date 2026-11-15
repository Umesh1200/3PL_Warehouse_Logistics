using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;

namespace Warehouse3PL.Application.Features.Putaway.Strategies.Queries.GetPutawayStrategies;

public record GetPutawayStrategiesQuery(Guid FacilityId, Guid? ClientId = null) : IRequest<List<PutawayStrategyRuleDto>>;

public class GetPutawayStrategiesQueryHandler : IRequestHandler<GetPutawayStrategiesQuery, List<PutawayStrategyRuleDto>>
{
    private readonly IApplicationDbContext _context;

    public GetPutawayStrategiesQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<PutawayStrategyRuleDto>> Handle(GetPutawayStrategiesQuery request, CancellationToken cancellationToken)
    {
        var query = _context.PutawayStrategyRules
            .AsNoTracking()
            .Where(r => r.FacilityId == request.FacilityId);

        if (request.ClientId.HasValue)
        {
            query = query.Where(r => r.ClientId == request.ClientId.Value || r.ClientId == Guid.Empty);
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

