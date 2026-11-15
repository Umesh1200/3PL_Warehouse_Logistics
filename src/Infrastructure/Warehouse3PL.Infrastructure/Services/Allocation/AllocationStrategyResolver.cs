using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Infrastructure.Services.Allocation;

/// <summary>
/// Resolves the concrete IAllocationStrategy registered in dependency injection for a given AllocationStrategyType.
/// </summary>
public class AllocationStrategyResolver : IAllocationStrategyResolver
{
    private readonly IEnumerable<IAllocationStrategy> _strategies;

    public AllocationStrategyResolver(IEnumerable<IAllocationStrategy> strategies)
    {
        _strategies = strategies;
    }

    public IAllocationStrategy GetStrategy(AllocationStrategyType strategyType)
    {
        var strategy = _strategies.FirstOrDefault(s => s.StrategyType == strategyType);
        if (strategy == null)
        {
            throw new NotSupportedException($"Allocation strategy '{strategyType}' is not registered or supported.");
        }
        return strategy;
    }
}

