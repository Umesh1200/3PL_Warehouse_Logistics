using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Infrastructure.Services.Analytics;

/// <summary>
/// Domain service implementation for market-basket product affinity mining and co-locational slotting rules (Phase 6.3.7.3).
/// Invariants:
/// 1. Canonical Demand: Aggregated at canonical order level. Repeat lines/allocations of a SKU within an order count once.
/// 2. Tenant & Facility Isolation: Strictly partitioned by ClientId and FacilityId.
/// 3. Statistical Reliability: Low-volume and rare-pair noise rejected via sample size, co-occurrence, support, and lift gates.
/// 4. Read-Only Invariance: Recommendations and clusters are advisory; operational inventory balances remain untouched.
/// </summary>
public class ProductAffinityMiningService : IProductAffinityMiningService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ILogger<ProductAffinityMiningService> _logger;

    public ProductAffinityMiningService(
        IApplicationDbContext dbContext,
        ICurrentUserContext currentUserContext,
        ILogger<ProductAffinityMiningService> logger)
    {
        _dbContext = dbContext;
        _currentUserContext = currentUserContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ProductAffinityMiningResultDto> MineAffinityAsync(
        MineProductAffinityRequestDto request,
        CancellationToken cancellationToken = default)
    {
        DateTime evalTime = request.AsOfDateUtc ?? DateTime.UtcNow;
        var miningBatchId = Guid.NewGuid();

        // 1. Resolve Policy or Defaults
        var policy = await ResolvePolicyAsync(request.FacilityId, request.ClientId, cancellationToken);
        int windowDays = request.AnalysisWindowDays ?? policy.AnalysisWindowDays;
        int minOrderCount = request.MinOrderCount ?? policy.MinOrderCount;
        int minCoOccurrenceCount = request.MinCoOccurrenceCount ?? policy.MinCoOccurrenceCount;
        decimal minSupport = request.MinSupport ?? policy.MinSupport;
        decimal minConfidence = request.MinConfidence ?? policy.MinConfidence;
        decimal minLift = request.MinLift ?? policy.MinLift;
        decimal minClusterLift = request.MinClusterLift ?? policy.MinClusterLift;
        int maxClusterSize = request.MaxClusterSize ?? policy.MaxClusterSize;

        DateTime windowEnd = request.WindowEndUtc ?? policy.SeasonalEndDateUtc ?? evalTime;
        DateTime windowStart = request.WindowStartUtc ?? policy.SeasonalStartDateUtc ?? windowEnd.AddDays(-windowDays);

        _logger.LogInformation(
            "Starting product affinity mining for Facility {FacilityId}, Client {ClientId}. Window: {Start} to {End}",
            request.FacilityId, request.ClientId, windowStart, windowEnd);

        bool allowUnassigned = request.IncludeUnassignedFacilityOrders ?? policy.IncludeUnassignedFacilityOrders;

        // 2. Extract Canonical Outbound Orders
        // Strict facility isolation: only include request.FacilityId unless allowUnassigned is explicitly true
        var ordersQuery = _dbContext.OutboundOrders
            .AsNoTracking()
            .Where(o => o.ClientId == request.ClientId)
            .Where(o => o.FacilityId == request.FacilityId || (allowUnassigned && o.FacilityId == Guid.Empty))
            .Where(o => !o.IsDeleted)
            .Where(o => o.Status != OutboundOrderStatus.Draft && o.Status != OutboundOrderStatus.Cancelled)
            .Where(o => o.OrderDateUtc >= windowStart && o.OrderDateUtc <= windowEnd);

        int totalOrdersScanned = await ordersQuery.CountAsync(cancellationToken);
        var orderIds = await ordersQuery.Select(o => o.Id).ToListAsync(cancellationToken);

        // 3. Extract Canonical Order Lines with Positive Demand
        var orderLines = await _dbContext.OutboundOrderLines
            .AsNoTracking()
            .Where(l => orderIds.Contains(l.OutboundOrderId) && !l.IsDeleted && l.Status != OutboundOrderLineStatus.Cancelled)
            .Where(l => l.QuantityOrdered > 0 || l.QuantityAllocated > 0 || l.QuantityShipped > 0)
            .Select(l => new { l.OutboundOrderId, l.ItemId })
            .ToListAsync(cancellationToken);

        // Group into distinct SKU baskets per order (canonical deduplication)
        var baskets = orderLines
            .GroupBy(l => l.OutboundOrderId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ItemId).Distinct().ToList());

        int totalQualifyingOrders = baskets.Count;

        // 4. Sample Size Reliability Gate (Workstream 6.3.7.3.5 / 6.3.7.3.6)
        if (totalQualifyingOrders < minOrderCount)
        {
            _logger.LogWarning(
                "Affinity mining failed closed: Qualifying order count {Count} is below minimum threshold {MinCount}",
                totalQualifyingOrders, minOrderCount);

            return new ProductAffinityMiningResultDto
            {
                MiningBatchId = miningBatchId,
                FacilityId = request.FacilityId,
                ClientId = request.ClientId,
                EvaluatedAtUtc = evalTime,
                WindowStartUtc = windowStart,
                WindowEndUtc = windowEnd,
                TotalOrdersScanned = totalOrdersScanned,
                TotalQualifyingOrders = totalQualifyingOrders,
                TotalUniqueItems = 0,
                TotalCandidatePairs = 0,
                StatisticallySignificantPairsCount = 0,
                ClustersFormedCount = 0,
                SampleSizeSufficient = false,
                StatusMessage = $"Qualifying order sample size ({totalQualifyingOrders}) is below the required minimum ({minOrderCount}). Affinity mining aborted to prevent rare-pair noise."
            };
        }

        // 5. Item Marginal Frequencies
        var itemCounts = new Dictionary<Guid, int>();
        foreach (var basket in baskets.Values)
        {
            foreach (var itemId in basket)
            {
                itemCounts[itemId] = itemCounts.GetValueOrDefault(itemId) + 1;
            }
        }

        int totalUniqueItems = itemCounts.Count;

        // 5. Pre-Defined Eligible SKU Universe (Catalog Snapshot independent of analysis window outcomes)
        var catalogItems = await _dbContext.Items
            .AsNoTracking()
            .Where(i => i.ClientId == request.ClientId && !i.IsDeleted)
            .ToDictionaryAsync(i => i.Id, i => i.Sku, cancellationToken);

        // Include any item that had demand in the window if not already in catalog dictionary
        var items = new Dictionary<Guid, string>(catalogItems);
        foreach (var id in itemCounts.Keys)
        {
            if (!items.ContainsKey(id))
            {
                items[id] = id.ToString();
            }
        }

        // 6. Pair Co-Occurrence Counting (Canonical Pair Ordering: ItemAId < ItemBId)
        var pairCounts = new Dictionary<(Guid, Guid), int>();
        foreach (var basket in baskets.Values)
        {
            if (basket.Count < 2) continue;

            for (int i = 0; i < basket.Count; i++)
            {
                for (int j = i + 1; j < basket.Count; j++)
                {
                    var a = basket[i];
                    var b = basket[j];
                    var pairKey = a.CompareTo(b) < 0 ? (a, b) : (b, a);
                    pairCounts[pairKey] = pairCounts.GetValueOrDefault(pairKey) + 1;
                }
            }
        }

        // Total tested hypothesis family: all distinct unordered pairs in the eligible SKU universe (M_total = |P_0|)
        int eligibleSkuCount = items.Count;
        int mTotal = eligibleSkuCount >= 2
            ? Math.Max((eligibleSkuCount * (eligibleSkuCount - 1)) / 2, pairCounts.Count)
            : Math.Max(pairCounts.Count, 1);
        int totalCandidatePairs = mTotal;

        // 7. Calculate Statistical Metrics (Support, Confidence, Lift, Chi-Square)
        var calculatedPairs = new List<ProductAffinityPair>();

        foreach (var (pairKey, coCount) in pairCounts)
        {
            var itemAId = pairKey.Item1;
            var itemBId = pairKey.Item2;

            int cA = itemCounts[itemAId];
            int cB = itemCounts[itemBId];

            var stats = ComputePairStatistics(totalQualifyingOrders, cA, cB, coCount);

            var pairEntity = new ProductAffinityPair
            {
                ClientId = request.ClientId,
                FacilityId = request.FacilityId,
                MiningBatchId = miningBatchId,
                ItemAId = itemAId,
                ItemASku = items.GetValueOrDefault(itemAId, itemAId.ToString()),
                ItemBId = itemBId,
                ItemBSku = items.GetValueOrDefault(itemBId, itemBId.ToString()),
                TotalOrdersEvaluated = totalQualifyingOrders,
                ItemAOrderCount = stats.ItemAOrderCount,
                ItemBOrderCount = stats.ItemBOrderCount,
                CoOccurrenceCount = stats.CoOccurrenceCount,
                Support = stats.SupportAB,
                ItemASupport = stats.SupportA,
                ItemBSupport = stats.SupportB,
                ConfidenceAtoB = stats.ConfidenceAtoB,
                ConfidenceBtoA = stats.ConfidenceBtoA,
                MaxConfidence = stats.MaxConfidence,
                KulczynskiMeasure = stats.KulczynskiMeasure,
                Lift = stats.Lift,
                ChiSquareStatistic = stats.ChiSquareStatistic,
                MinExpectedCellFrequency = stats.MinExpectedCellFrequency,
                HasYatesCorrection = stats.HasYatesCorrection,
                PValue = stats.PValue,
                IsStatisticallySignificant = false, // Evaluated via FDR and hard gates below
                WindowStartUtc = windowStart,
                WindowEndUtc = windowEnd,
                CalculatedAtUtc = evalTime
            };

            calculatedPairs.Add(pairEntity);
        }

        // 8. Statistical Reliability Gating & Benjamini-Yekutieli False Discovery Rate (FDR) Control
        // Evaluates FDR under arbitrary dependence across the complete family of tested candidate pair hypotheses (M_total)
        var allTestedPairs = calculatedPairs
            .Where(p => p.PValue.HasValue)
            .OrderBy(p => p.PValue!.Value)
            .ToList();

        mTotal = Math.Max(totalCandidatePairs, allTestedPairs.Count);
        decimal alphaFdr = 0.05m;

        // Benjamini-Yekutieli harmonic series factor c(M) for arbitrary dependence
        decimal cM = 0m;
        if (mTotal <= 10000)
        {
            for (int i = 1; i <= mTotal; i++)
            {
                cM += 1.0m / i;
            }
        }
        else
        {
            cM = (decimal)(Math.Log(mTotal) + 0.5772156649015328606 + 1.0 / (2.0 * mTotal));
        }
        if (cM <= 0m) cM = 1.0m;

        int maxK = 0;
        for (int k = 1; k <= allTestedPairs.Count; k++)
        {
            decimal fdrThreshold = (k / ((decimal)mTotal * cM)) * alphaFdr;
            if (allTestedPairs[k - 1].PValue!.Value <= fdrThreshold)
            {
                maxK = k;
            }
        }

        var fdrAccepted = new HashSet<ProductAffinityPair>(allTestedPairs.Take(maxK));
        foreach (var pair in calculatedPairs)
        {
            bool passesBusinessGates = pair.CoOccurrenceCount >= minCoOccurrenceCount &&
                                       pair.Support >= minSupport &&
                                       pair.Lift >= minLift &&
                                       pair.MaxConfidence >= minConfidence &&
                                       (pair.MinExpectedCellFrequency ?? 0m) >= 1.0m;

            pair.IsStatisticallySignificant = fdrAccepted.Contains(pair) &&
                                              passesBusinessGates &&
                                              pair.ChiSquareStatistic.HasValue &&
                                              pair.ChiSquareStatistic.Value >= 3.841m;
        }

        var significantPairs = calculatedPairs
            .Where(p => p.IsStatisticallySignificant)
            .OrderByDescending(p => p.Lift)
            .ThenByDescending(p => p.Support)
            .ThenBy(p => p.ItemASku)
            .ThenBy(p => p.ItemBSku)
            .ToList();

        // 8. Deterministic Product Affinity Clustering (Workstream 6.3.7.3.8)
        var clusterCandidates = significantPairs
            .Where(p => p.Lift >= minClusterLift)
            .ToList();

        var formedClusters = BuildDeterministicClusters(
            clusterCandidates, items, itemCounts, request.FacilityId, request.ClientId,
            miningBatchId, maxClusterSize, windowStart, windowEnd, evalTime);

        // 9. Persist Mined Models if Requested
        if (request.SaveMinedAffinity)
        {
            // Remove previous affinity pairs and clusters for this facility & client
            var oldPairs = await _dbContext.ProductAffinityPairs
                .Where(p => p.FacilityId == request.FacilityId && p.ClientId == request.ClientId)
                .ToListAsync(cancellationToken);
            _dbContext.ProductAffinityPairs.RemoveRange(oldPairs);

            var oldClusters = await _dbContext.ProductAffinityClusters
                .Include(c => c.Members)
                .Where(c => c.FacilityId == request.FacilityId && c.ClientId == request.ClientId)
                .ToListAsync(cancellationToken);
            _dbContext.ProductAffinityClusters.RemoveRange(oldClusters);

            // Add new records
            await _dbContext.ProductAffinityPairs.AddRangeAsync(calculatedPairs, cancellationToken);
            await _dbContext.ProductAffinityClusters.AddRangeAsync(formedClusters, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        // 10. Map to DTOs
        var pairDtos = significantPairs.Select(MapPairToDto).ToList();
        var clusterDtos = formedClusters.Select(MapClusterToDto).ToList();

        return new ProductAffinityMiningResultDto
        {
            MiningBatchId = miningBatchId,
            FacilityId = request.FacilityId,
            ClientId = request.ClientId,
            EvaluatedAtUtc = evalTime,
            WindowStartUtc = windowStart,
            WindowEndUtc = windowEnd,
            TotalOrdersScanned = totalOrdersScanned,
            TotalQualifyingOrders = totalQualifyingOrders,
            TotalUniqueItems = totalUniqueItems,
            TotalCandidatePairs = totalCandidatePairs,
            StatisticallySignificantPairsCount = significantPairs.Count,
            ClustersFormedCount = formedClusters.Count,
            SampleSizeSufficient = true,
            StatusMessage = $"Affinity mining completed successfully: {significantPairs.Count} significant pairs identified, {formedClusters.Count} co-location clusters formed.",
            SignificantPairs = pairDtos,
            Clusters = clusterDtos
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProductAffinityPairDto>> GetSignificantPairsAsync(
        Guid facilityId,
        Guid clientId,
        decimal? minLift = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.ProductAffinityPairs
            .AsNoTracking()
            .Where(p => p.FacilityId == facilityId && p.ClientId == clientId && p.IsStatisticallySignificant);

        if (minLift.HasValue)
        {
            query = query.Where(p => p.Lift >= minLift.Value);
        }

        var pairs = await query
            .OrderByDescending(p => p.Lift)
            .ThenByDescending(p => p.Support)
            .ThenBy(p => p.ItemASku)
            .ThenBy(p => p.ItemBSku)
            .ToListAsync(cancellationToken);

        return pairs.Select(MapPairToDto).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProductAffinityClusterDto>> GetClustersAsync(
        Guid facilityId,
        Guid clientId,
        CancellationToken cancellationToken = default)
    {
        var clusters = await _dbContext.ProductAffinityClusters
            .AsNoTracking()
            .Include(c => c.Members)
            .Where(c => c.FacilityId == facilityId && c.ClientId == clientId)
            .OrderBy(c => c.ClusterCode)
            .ToListAsync(cancellationToken);

        return clusters.Select(MapClusterToDto).ToList();
    }

    /// <inheritdoc />
    public async Task<ProductAffinityClusterDto?> GetClusterForItemAsync(
        Guid facilityId,
        Guid clientId,
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        var cluster = await _dbContext.ProductAffinityClusters
            .AsNoTracking()
            .Include(c => c.Members)
            .FirstOrDefaultAsync(c => c.FacilityId == facilityId &&
                                      c.ClientId == clientId &&
                                      c.Members.Any(m => m.ItemId == itemId),
                                 cancellationToken);

        return cluster != null ? MapClusterToDto(cluster) : null;
    }

    /// <inheritdoc />
    public async Task<ProductAffinityPolicyDto> GetPolicyAsync(
        Guid facilityId,
        Guid? clientId = null,
        CancellationToken cancellationToken = default)
    {
        var policy = await ResolvePolicyAsync(facilityId, clientId, cancellationToken);
        return MapPolicyToDto(policy);
    }

    /// <inheritdoc />
    public async Task<ProductAffinityPolicyDto> SavePolicyAsync(
        ProductAffinityPolicyDto dto,
        CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.ProductAffinityPolicies
            .FirstOrDefaultAsync(p => p.Id == dto.Id, cancellationToken);

        if (existing == null)
        {
            existing = new ProductAffinityPolicy
            {
                Id = dto.Id != Guid.Empty ? dto.Id : Guid.NewGuid(),
                FacilityId = dto.FacilityId,
                ClientId = dto.ClientId,
                Name = dto.Name,
                Description = dto.Description,
                AnalysisWindowDays = dto.AnalysisWindowDays,
                MinOrderCount = dto.MinOrderCount,
                MinCoOccurrenceCount = dto.MinCoOccurrenceCount,
                MinSupport = dto.MinSupport,
                MinConfidence = dto.MinConfidence,
                MinLift = dto.MinLift,
                MinClusterLift = dto.MinClusterLift,
                MaxClusterSize = dto.MaxClusterSize,
                WeightProductAffinity = dto.WeightProductAffinity,
                IncludeUnassignedFacilityOrders = dto.IncludeUnassignedFacilityOrders,
                SeasonalStartDateUtc = dto.SeasonalStartDateUtc,
                SeasonalEndDateUtc = dto.SeasonalEndDateUtc,
                IsActive = dto.IsActive,
                Version = dto.Version
            };
            await _dbContext.ProductAffinityPolicies.AddAsync(existing, cancellationToken);
        }
        else
        {
            existing.Name = dto.Name;
            existing.Description = dto.Description;
            existing.AnalysisWindowDays = dto.AnalysisWindowDays;
            existing.MinOrderCount = dto.MinOrderCount;
            existing.MinCoOccurrenceCount = dto.MinCoOccurrenceCount;
            existing.MinSupport = dto.MinSupport;
            existing.MinConfidence = dto.MinConfidence;
            existing.MinLift = dto.MinLift;
            existing.MinClusterLift = dto.MinClusterLift;
            existing.MaxClusterSize = dto.MaxClusterSize;
            existing.WeightProductAffinity = dto.WeightProductAffinity;
            existing.IncludeUnassignedFacilityOrders = dto.IncludeUnassignedFacilityOrders;
            existing.SeasonalStartDateUtc = dto.SeasonalStartDateUtc;
            existing.SeasonalEndDateUtc = dto.SeasonalEndDateUtc;
            existing.IsActive = dto.IsActive;
            existing.Version += 1;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapPolicyToDto(existing);
    }

    #region Private Helper Methods

    private async Task<ProductAffinityPolicy> ResolvePolicyAsync(
        Guid facilityId,
        Guid? clientId,
        CancellationToken cancellationToken)
    {
        // 1. Specific facility + client
        if (clientId.HasValue)
        {
            var clientPolicy = await _dbContext.ProductAffinityPolicies
                .AsNoTracking()
                .Where(p => p.FacilityId == facilityId && p.ClientId == clientId.Value && p.IsActive)
                .OrderByDescending(p => p.Version)
                .FirstOrDefaultAsync(cancellationToken);

            if (clientPolicy != null) return clientPolicy;
        }

        // 2. Facility default
        var facilityPolicy = await _dbContext.ProductAffinityPolicies
            .AsNoTracking()
            .Where(p => p.FacilityId == facilityId && p.ClientId == null && p.IsActive)
            .OrderByDescending(p => p.Version)
            .FirstOrDefaultAsync(cancellationToken);

        if (facilityPolicy != null) return facilityPolicy;

        // 3. System Default
        return new ProductAffinityPolicy
        {
            FacilityId = facilityId,
            ClientId = clientId,
            Name = "System Default Product Affinity Policy"
        };
    }

    private static List<ProductAffinityCluster> BuildDeterministicClusters(
        IReadOnlyList<ProductAffinityPair> candidatePairs,
        IReadOnlyDictionary<Guid, string> items,
        IReadOnlyDictionary<Guid, int> itemPickCounts,
        Guid facilityId,
        Guid clientId,
        Guid miningBatchId,
        int maxClusterSize,
        DateTime windowStart,
        DateTime windowEnd,
        DateTime evalTime)
    {
        var clusters = new List<List<Guid>>();
        var itemClusterMap = new Dictionary<Guid, int>();

        // Greedily build connected components with size limit
        foreach (var pair in candidatePairs)
        {
            var a = pair.ItemAId;
            var b = pair.ItemBId;

            bool aHasCluster = itemClusterMap.TryGetValue(a, out int cAIdx);
            bool bHasCluster = itemClusterMap.TryGetValue(b, out int cBIdx);

            if (!aHasCluster && !bHasCluster)
            {
                // Create new cluster
                var newCluster = new List<Guid> { a, b };
                int idx = clusters.Count;
                clusters.Add(newCluster);
                itemClusterMap[a] = idx;
                itemClusterMap[b] = idx;
            }
            else if (aHasCluster && !bHasCluster)
            {
                if (clusters[cAIdx].Count < maxClusterSize)
                {
                    clusters[cAIdx].Add(b);
                    itemClusterMap[b] = cAIdx;
                }
            }
            else if (!aHasCluster && bHasCluster)
            {
                if (clusters[cBIdx].Count < maxClusterSize)
                {
                    clusters[cBIdx].Add(a);
                    itemClusterMap[a] = cBIdx;
                }
            }
            else if (aHasCluster && bHasCluster && cAIdx != cBIdx)
            {
                // Both in different clusters: merge if combined size permits
                if (clusters[cAIdx].Count + clusters[cBIdx].Count <= maxClusterSize)
                {
                    var clusterToMerge = clusters[cBIdx];
                    foreach (var item in clusterToMerge)
                    {
                        clusters[cAIdx].Add(item);
                        itemClusterMap[item] = cAIdx;
                    }
                    clusterToMerge.Clear(); // Empty merged cluster
                }
            }
        }

        // Filter non-empty clusters and sort deterministically
        var validClusters = clusters
            .Where(c => c.Count >= 2)
            .OrderByDescending(c => c.Count)
            .ThenBy(c => items.GetValueOrDefault(c.First(), string.Empty))
            .ToList();

        var result = new List<ProductAffinityCluster>();
        int clusterCounter = 1;

        foreach (var memberIds in validClusters)
        {
            var distinctMemberIds = memberIds.Distinct().ToList();

            // Find within-cluster lift connectivity for each member
            var memberWithinLifts = new Dictionary<Guid, List<decimal>>();
            foreach (var m in distinctMemberIds)
            {
                memberWithinLifts[m] = new List<decimal>();
            }

            var clusterPairs = candidatePairs
                .Where(p => distinctMemberIds.Contains(p.ItemAId) && distinctMemberIds.Contains(p.ItemBId))
                .ToList();

            foreach (var cp in clusterPairs)
            {
                memberWithinLifts[cp.ItemAId].Add(cp.Lift);
                memberWithinLifts[cp.ItemBId].Add(cp.Lift);
            }

            // Identify Anchor Item:
            // 1. Highest sum of within-cluster lift
            // 2. Highest total order/pick frequency
            // 3. Lexicographical SKU string
            var anchorItemId = distinctMemberIds
                .OrderByDescending(m => memberWithinLifts[m].Sum())
                .ThenByDescending(m => itemPickCounts.GetValueOrDefault(m, 0))
                .ThenBy(m => items.GetValueOrDefault(m, string.Empty))
                .First();

            decimal avgClusterLift = clusterPairs.Count > 0
                ? Math.Round(clusterPairs.Average(cp => cp.Lift), 4)
                : 1.0m;
            decimal maxClusterLift = clusterPairs.Count > 0
                ? clusterPairs.Max(cp => cp.Lift)
                : 1.0m;

            var clusterEntity = new ProductAffinityCluster
            {
                ClientId = clientId,
                FacilityId = facilityId,
                MiningBatchId = miningBatchId,
                ClusterCode = $"PAC-{clusterCounter:D3}",
                PrimaryAnchorItemId = anchorItemId,
                PrimaryAnchorSku = items.GetValueOrDefault(anchorItemId, anchorItemId.ToString()),
                ItemCount = distinctMemberIds.Count,
                AverageLift = avgClusterLift,
                MaxLift = maxClusterLift,
                WindowStartUtc = windowStart,
                WindowEndUtc = windowEnd,
                CalculatedAtUtc = evalTime
            };

            foreach (var mId in distinctMemberIds)
            {
                var lifts = memberWithinLifts[mId];
                clusterEntity.Members.Add(new ProductAffinityClusterMember
                {
                    ClusterId = clusterEntity.Id,
                    ItemId = mId,
                    Sku = items.GetValueOrDefault(mId, mId.ToString()),
                    IsAnchor = mId == anchorItemId,
                    WithinClusterDegree = lifts.Count,
                    AverageWithinClusterLift = lifts.Count > 0 ? Math.Round(lifts.Average(), 4) : 0m
                });
            }

            result.Add(clusterEntity);
            clusterCounter++;
        }

        return result;
    }

    private static ProductAffinityPairDto MapPairToDto(ProductAffinityPair p) => new()
    {
        Id = p.Id,
        ClientId = p.ClientId,
        FacilityId = p.FacilityId,
        MiningBatchId = p.MiningBatchId,
        ItemAId = p.ItemAId,
        ItemASku = p.ItemASku,
        ItemBId = p.ItemBId,
        ItemBSku = p.ItemBSku,
        TotalOrdersEvaluated = p.TotalOrdersEvaluated,
        ItemAOrderCount = p.ItemAOrderCount,
        ItemBOrderCount = p.ItemBOrderCount,
        CoOccurrenceCount = p.CoOccurrenceCount,
        Support = p.Support,
        ItemASupport = p.ItemASupport,
        ItemBSupport = p.ItemBSupport,
        ConfidenceAtoB = p.ConfidenceAtoB,
        ConfidenceBtoA = p.ConfidenceBtoA,
        MaxConfidence = p.MaxConfidence,
        KulczynskiMeasure = p.KulczynskiMeasure,
        Lift = p.Lift,
        ChiSquareStatistic = p.ChiSquareStatistic,
        MinExpectedCellFrequency = p.MinExpectedCellFrequency,
        HasYatesCorrection = p.HasYatesCorrection,
        PValue = p.PValue,
        IsStatisticallySignificant = p.IsStatisticallySignificant,
        WindowStartUtc = p.WindowStartUtc,
        WindowEndUtc = p.WindowEndUtc,
        CalculatedAtUtc = p.CalculatedAtUtc
    };

    private static ProductAffinityClusterDto MapClusterToDto(ProductAffinityCluster c) => new()
    {
        Id = c.Id,
        ClientId = c.ClientId,
        FacilityId = c.FacilityId,
        MiningBatchId = c.MiningBatchId,
        ClusterCode = c.ClusterCode,
        PrimaryAnchorItemId = c.PrimaryAnchorItemId,
        PrimaryAnchorSku = c.PrimaryAnchorSku,
        ItemCount = c.ItemCount,
        AverageLift = c.AverageLift,
        MaxLift = c.MaxLift,
        WindowStartUtc = c.WindowStartUtc,
        WindowEndUtc = c.WindowEndUtc,
        CalculatedAtUtc = c.CalculatedAtUtc,
        Members = c.Members.Select(m => new ProductAffinityClusterMemberDto
        {
            Id = m.Id,
            ClusterId = m.ClusterId,
            ItemId = m.ItemId,
            Sku = m.Sku,
            IsAnchor = m.IsAnchor,
            WithinClusterDegree = m.WithinClusterDegree,
            AverageWithinClusterLift = m.AverageWithinClusterLift
        }).ToList()
    };

    private static ProductAffinityPolicyDto MapPolicyToDto(ProductAffinityPolicy p) => new()
    {
        Id = p.Id,
        FacilityId = p.FacilityId,
        ClientId = p.ClientId,
        Name = p.Name,
        Description = p.Description,
        AnalysisWindowDays = p.AnalysisWindowDays,
        MinOrderCount = p.MinOrderCount,
        MinCoOccurrenceCount = p.MinCoOccurrenceCount,
        MinSupport = p.MinSupport,
        MinConfidence = p.MinConfidence,
        MinLift = p.MinLift,
        MinClusterLift = p.MinClusterLift,
        MaxClusterSize = p.MaxClusterSize,
        WeightProductAffinity = p.WeightProductAffinity,
        IncludeUnassignedFacilityOrders = p.IncludeUnassignedFacilityOrders,
        SeasonalStartDateUtc = p.SeasonalStartDateUtc,
        SeasonalEndDateUtc = p.SeasonalEndDateUtc,
        IsActive = p.IsActive,
        Version = p.Version
    };

    /// <summary>
    /// Computes contingency table metrics, expected frequencies, chi-square statistic, and directional p-value.
    /// Under directional hypothesis H0: OR &lt;= 1.0 vs H1: OR &gt; 1.0, non-positive association (Lift &lt;= 1.0),
    /// zero co-occurrence (C = 0), or Cochran sparsity (minExpected &lt; 1.0) returns p-value 1.0 and chi-square 0.0.
    /// </summary>
    public static PairContingencyStatistics ComputePairStatistics(
        int totalOrders,
        int itemAOrderCount,
        int itemBOrderCount,
        int coOccurrenceCount)
    {
        decimal n = totalOrders;
        int cA = itemAOrderCount;
        int cB = itemBOrderCount;
        int coCount = coOccurrenceCount;

        decimal suppA = n > 0 ? Math.Round(cA / n, 4) : 0m;
        decimal suppB = n > 0 ? Math.Round(cB / n, 4) : 0m;
        decimal suppAB = n > 0 ? Math.Round(coCount / n, 4) : 0m;

        decimal confAtoB = cA > 0 ? Math.Round((decimal)coCount / cA, 4) : 0m;
        decimal confBtoA = cB > 0 ? Math.Round((decimal)coCount / cB, 4) : 0m;
        decimal maxConf = Math.Max(confAtoB, confBtoA);
        decimal kulc = Math.Round(0.5m * (confAtoB + confBtoA), 4);

        decimal lift = (cA > 0 && cB > 0 && n > 0)
            ? Math.Round((coCount * n) / (decimal)(cA * cB), 4)
            : 0m;

        // 2x2 Contingency Table Counts
        decimal n11 = coCount;
        decimal n10 = cA - coCount;
        decimal n01 = cB - coCount;
        decimal n00 = n - cA - cB + coCount;

        // Expected cell counts under independence H0
        decimal e11 = n > 0 ? (cA * (decimal)cB) / n : 0m;
        decimal e10 = n > 0 ? (cA * (n - cB)) / n : 0m;
        decimal e01 = n > 0 ? ((n - cA) * (decimal)cB) / n : 0m;
        decimal e00 = n > 0 ? ((n - cA) * (n - cB)) / n : 0m;
        decimal minExpected = Math.Min(Math.Min(e11, e10), Math.Min(e01, e00));

        // Directional Hypothesis Testing for Positive Association:
        // H0: OR(A, B) <= 1.0 (Lift <= 1.0) vs. H1: OR(A, B) > 1.0 (Lift > 1.0)
        decimal denominator = (decimal)cA * (n - cA) * cB * (n - cB);
        decimal? chiSquare = null;
        bool hasYates = false;
        decimal pValue = 1.0m;
        bool isPositive = false;

        if (denominator > 0 && minExpected >= 1.0m)
        {
            decimal crossDiff = (n11 * n00) - (n10 * n01);
            if (crossDiff > 0m) // Positive association direction (Lift > 1.0)
            {
                isPositive = true;
                if (minExpected < 5.0m)
                {
                    hasYates = true;
                    decimal correctedDiff = Math.Max(0m, crossDiff - (n / 2.0m));
                    if (correctedDiff > 0m)
                    {
                        decimal numerator = n * correctedDiff * correctedDiff;
                        chiSquare = Math.Round(numerator / denominator, 4);
                    }
                    else
                    {
                        chiSquare = 0m;
                    }
                }
                else
                {
                    decimal numerator = n * crossDiff * crossDiff;
                    chiSquare = Math.Round(numerator / denominator, 4);
                }

                if (chiSquare.HasValue && chiSquare.Value > 0m)
                {
                    double chiVal = (double)chiSquare.Value;
                    double z = Math.Sqrt(chiVal);
                    double p = Erfc(z / Math.Sqrt(2.0));
                    pValue = (decimal)Math.Round(p, 6);
                }
                else
                {
                    pValue = 1.0m;
                }
            }
            else
            {
                // Non-positive association (Lift <= 1.0): H0 cannot be rejected in favor of positive affinity
                chiSquare = 0m;
                pValue = 1.0m;
            }
        }
        else
        {
            // Degenerate table or Cochran sparsity (minExpected < 1.0 or zero margin): fails closed
            chiSquare = 0m;
            pValue = 1.0m;
        }

        return new PairContingencyStatistics
        {
            TotalOrders = totalOrders,
            ItemAOrderCount = cA,
            ItemBOrderCount = cB,
            CoOccurrenceCount = coCount,
            SupportA = suppA,
            SupportB = suppB,
            SupportAB = suppAB,
            ConfidenceAtoB = confAtoB,
            ConfidenceBtoA = confBtoA,
            MaxConfidence = maxConf,
            KulczynskiMeasure = kulc,
            Lift = lift,
            ChiSquareStatistic = chiSquare,
            MinExpectedCellFrequency = Math.Round(minExpected, 4),
            HasYatesCorrection = hasYates,
            PValue = pValue,
            IsDirectionallyPositive = isPositive
        };
    }

    /// <summary>
    /// Complementary error function approximation with maximal error &lt; 1.5e-7 (Abramowitz &amp; Stegun 7.1.26).
    /// Used to compute p-values for Chi-Square distribution with df = 1: p = erfc(sqrt(chi2 / 2)).
    /// </summary>
    private static double Erfc(double x)
    {
        if (x < 0) return 2.0 - Erfc(-x);
        double t = 1.0 / (1.0 + 0.3275911 * x);
        double poly = t * (0.254829592 + t * (-0.284496736 + t * (1.421413741 + t * (-1.453152027 + t * 1.061405429))));
        return poly * Math.Exp(-x * x);
    }

    #endregion
}
