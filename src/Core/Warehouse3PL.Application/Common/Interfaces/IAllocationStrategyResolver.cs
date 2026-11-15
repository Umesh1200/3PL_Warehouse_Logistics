using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

/// <summary>
/// Resolves the concrete strategy implementation for a given AllocationStrategyType.
/// </summary>
public interface IAllocationStrategyResolver
{
    IAllocationStrategy GetStrategy(AllocationStrategyType strategyType);
}

