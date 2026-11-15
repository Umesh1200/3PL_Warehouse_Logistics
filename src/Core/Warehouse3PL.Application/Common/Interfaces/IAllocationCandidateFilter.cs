using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Inventory;

namespace Warehouse3PL.Application.Common.Interfaces;

/// <summary>
/// Shared candidate eligibility component enforcing multi-tenancy, location locks, temperature integrity,
/// lot requirements, expiration horizons, and quality inspection statuses across all allocation strategies.
/// </summary>
public interface IAllocationCandidateFilter
{
    /// <summary>
    /// Evaluates whether a StockOnHand record is eligible for allocation against an order line request.
    /// </summary>
    CandidateEligibilityResult Evaluate(StockOnHand stock, AllocationRequest request);
}

