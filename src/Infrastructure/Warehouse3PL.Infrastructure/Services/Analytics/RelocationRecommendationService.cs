using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services.Analytics;

/// <summary>
/// Domain service implementation for Phase 6.3.7.4: Relocation Recommendation Engine & Multi-Factor Prioritization.
/// Synthesizes transparent economics, operational urgency, placement suitability, and disruption risk
/// into an auditable, deterministic multi-factor prioritization engine.
/// Strict Invariant: Recommendations are advisory; zero automated inventory movements or operational ledger mutations.
/// </summary>
public class RelocationRecommendationService : IRelocationRecommendationService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<RelocationRecommendationService> _logger;

    public RelocationRecommendationService(
        IApplicationDbContext dbContext,
        ICurrentUserContext currentUser,
        ILogger<RelocationRecommendationService> logger)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <inheritdoc />
    public (decimal grossSavings, decimal handlingCost, decimal movementCost, decimal disruptionCost, decimal amortizedMonthlyCost, decimal netBenefit)
        CalculateTransparentEconomics(
            decimal roundTripTravelSavedMeters,
            decimal monthlyPicks,
            decimal quantity,
            decimal weightKg,
            Location currentLocation,
            Location destinationLocation,
            SlottingPolicy policy,
            string shiftType = "Standard",
            bool isOpportunityGain = false)
    {
        decimal laborRate = policy.LaborHourlyRateUsd > 0 ? policy.LaborHourlyRateUsd : 25.00m;
        decimal walkingSpeedMps = 1.0m;

        // 1. Gross monthly travel savings ($S_travel)
        decimal secondsSaved = (roundTripTravelSavedMeters * monthlyPicks) / walkingSpeedMps;
        decimal hoursSaved = secondsSaved / 3600.0m;
        decimal travelSavingsUsd = Math.Round(hoursSaved * laborRate, 2);

        // Opportunity gain ($G_opp)
        decimal oppGain = isOpportunityGain
            ? (policy.GoldenZoneOpportunityGainUsd > 0 ? policy.GoldenZoneOpportunityGainUsd : 8.50m)
            : 0.0m;

        decimal grossSavings = Math.Round(travelSavingsUsd + oppGain, 2);

        // 2. Handling cost ($C_handling)
        decimal unitHandlingRate = policy.HandlingCostPerUnitUsd > 0 ? policy.HandlingCostPerUnitUsd : 0.10m;
        decimal heavySurcharge = policy.HeavyHandlingSurchargePerTierUsd > 0 ? policy.HeavyHandlingSurchargePerTierUsd : 5.00m;
        decimal excessTiers = Math.Max(0m, (weightKg - 20.0m) / 10.0m);
        decimal handlingCost = Math.Round((quantity * unitHandlingRate) + (excessTiers * heavySurcharge), 2);

        // 3. Movement transit labor cost ($C_movement)
        decimal transitDistMeters = SlottingOptimizationService.Calculate3DDistance(currentLocation, destinationLocation);
        decimal transitSpeedMps = 2.0m;
        decimal setupMinutes = 2.0m;
        decimal transitMinutes = (transitDistMeters / (transitSpeedMps * 60.0m)) + setupMinutes;
        decimal movementCost = Math.Round((transitMinutes / 60.0m) * laborRate, 2);

        // 4. Disruption cost ($C_disrupt)
        decimal baseDisrupt = policy.BaseDisruptionCostUsd > 0 ? policy.BaseDisruptionCostUsd : 15.00m;
        bool isPickFace = destinationLocation.Zone?.ZoneType == WarehouseZoneType.PickFace ||
                          (destinationLocation.Zone == null && destinationLocation.ShelfLevel.HasValue && (destinationLocation.ShelfLevel.Value == 1 || destinationLocation.ShelfLevel.Value == 2));
        decimal zoneFactor = isPickFace ? 1.5m : 1.0m;

        decimal shiftFactor = shiftType.Trim().ToLowerInvariant() switch
        {
            "peak" => 1.4m,
            "offpeak" or "night" => 0.7m,
            _ => 1.0m
        };

        decimal disruptionCost = Math.Round(baseDisrupt * zoneFactor * shiftFactor, 2);

        // Total one-time relocation cost
        decimal totalRelocCost = Math.Round(handlingCost + movementCost + disruptionCost, 2);

        // Amortized monthly cost ($C_monthly)
        decimal amortMonths = policy.AmortizationMonths > 0 ? policy.AmortizationMonths : 3.0m;
        decimal amortizedMonthlyCost = Math.Round(totalRelocCost / amortMonths, 2);

        // Net monthly benefit ($B_net)
        decimal netBenefit = Math.Round(grossSavings - amortizedMonthlyCost, 2);

        return (grossSavings, handlingCost, movementCost, disruptionCost, amortizedMonthlyCost, netBenefit);
    }

    /// <inheritdoc />
    public (decimal urgencyScore, string urgencyReason) EvaluateOperationalUrgency(
        Item item,
        Location currentLocation,
        SlottingPolicy policy,
        string abcClass,
        string velocityCategory,
        string velocityTrend)
    {
        decimal uVelocity = 0.0m;
        decimal uCongestion = 0.0m;
        decimal uSeasonal = 0.0m;
        decimal uErgo = 0.0m;
        var reasons = new List<string>();

        bool currInGolden = SlottingOptimizationService.IsInGoldenZone(currentLocation, policy);
        int shelfLevel = currentLocation.ShelfLevel ?? 1;

        // 1. Velocity Mismatch Urgency
        bool isFastMover = string.Equals(abcClass, "ClassA", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(velocityTrend, "Surging", StringComparison.OrdinalIgnoreCase);

        if (isFastMover)
        {
            if (shelfLevel >= 3 && !currInGolden)
            {
                uVelocity = 45.0m;
                reasons.Add("Fast mover stored in high rack level (Level >= 3) outside Golden Zone (+45)");
            }
            else if (!currInGolden)
            {
                uVelocity = 25.0m;
                reasons.Add("Fast mover stored outside Golden Zone (+25)");
            }
        }
        else if (string.Equals(abcClass, "ClassB", StringComparison.OrdinalIgnoreCase) && shelfLevel >= 5)
        {
            uVelocity = 15.0m;
            reasons.Add("Class B item stored in top rack (Level >= 5) (+15)");
        }

        // 2. Golden Zone Congestion Urgency
        bool isSlowMover = string.Equals(abcClass, "ClassC", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(velocityTrend, "Inactive", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(velocityTrend, "DeadStock", StringComparison.OrdinalIgnoreCase);

        if (isSlowMover && currInGolden)
        {
            uCongestion = 40.0m;
            reasons.Add("Slow mover blocking prime Golden Zone pick face (+40)");
        }
        else if (string.Equals(velocityTrend, "Declining", StringComparison.OrdinalIgnoreCase) && currInGolden)
        {
            uCongestion = 20.0m;
            reasons.Add("Declining SKU occupying Golden Zone pick face (+20)");
        }

        // 3. Seasonal Demand Window Urgency
        if (string.Equals(velocityTrend, "SeasonalSpike", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(velocityTrend, "Surging", StringComparison.OrdinalIgnoreCase))
        {
            uSeasonal = 30.0m;
            reasons.Add("Seasonal spike / surging demand window active (+30)");
        }

        // 4. Ergonomic & Safety Urgency
        decimal weight = item.WeightKg;
        decimal z = currentLocation.ZCoord ?? (shelfLevel * 1.5m);
        if (weight >= 20.0m && (z > 1.5m || shelfLevel >= 3 || z < 0.4m || shelfLevel == 0))
        {
            uErgo = 30.0m;
            reasons.Add($"Heavy item ({weight:0.##}kg) stored outside waist height (Z={z:0.##}m, Level={shelfLevel}) creating lifting hazard (+30)");
        }

        decimal totalUrgency = Math.Clamp(uVelocity + uCongestion + uSeasonal + uErgo, 0.0m, 100.0m);
        string reasonText = reasons.Count > 0 ? string.Join("; ", reasons) : "Standard operational baseline";

        return (Math.Round(totalUrgency, 1), reasonText);
    }

    /// <inheritdoc />
    public decimal CalculatePriorityScore(
        decimal netMonthlyBenefitUsd,
        decimal urgencyScore,
        decimal suitabilityScore,
        decimal costPenaltyUsd,
        SlottingPolicy policy,
        bool isFeasible = true)
    {
        // Hard-Gate Supremacy: Infeasible proposals strictly score 0.0
        if (!isFeasible)
        {
            return 0.0m;
        }

        decimal wb = policy.WeightFinancialBenefit > 0 ? policy.WeightFinancialBenefit : 0.35m;
        decimal wu = policy.WeightOperationalUrgency > 0 ? policy.WeightOperationalUrgency : 0.30m;
        decimal ws = policy.WeightPlacementSuitability > 0 ? policy.WeightPlacementSuitability : 0.20m;
        decimal wd = policy.WeightDisruptionPenalty > 0 ? policy.WeightDisruptionPenalty : 0.15m;

        decimal targetSavings = policy.TargetMonthlySavingsUsd > 0 ? policy.TargetMonthlySavingsUsd : 50.0m;

        // Cost denominator:
        // By default, pure operational disruption cost (disruptionCostUsd) is evaluated to avoid double-counting
        // handling and transit labor which are already deducted in netMonthlyBenefitUsd.
        // When policy.UseTotalRelocationCostForDisruptionPenalty is enabled, total relocation cost is evaluated
        // as a deliberate conservative capital sensitivity constraint.
        decimal maxCost = policy.UseTotalRelocationCostForDisruptionPenalty
            ? (policy.MaxAcceptableRelocationCostUsd > 0 ? policy.MaxAcceptableRelocationCostUsd : 100.0m)
            : (policy.MaxAcceptableDisruptionCostUsd > 0 ? policy.MaxAcceptableDisruptionCostUsd : 30.0m);

        decimal sBenefit = netMonthlyBenefitUsd > 0
            ? Math.Clamp((netMonthlyBenefitUsd / targetSavings) * 100.0m, 0.0m, 100.0m)
            : 0.0m;

        decimal sSuit = Math.Clamp(suitabilityScore, 0.0m, 100.0m);
        decimal u = Math.Clamp(urgencyScore, 0.0m, 100.0m);
        decimal sDisrupt = Math.Clamp((costPenaltyUsd / maxCost) * 100.0m, 0.0m, 100.0m);
        decimal sMitigation = 100.0m - sDisrupt;

        // Normalized 0.0 to 100.0 convex combination:
        // P = w_b * S_b + w_u * U + w_s * S_suit + w_d * (100 - S_disrupt)
        decimal composite = (wb * sBenefit) + (wu * u) + (ws * sSuit) + (wd * sMitigation);
        return Math.Round(Math.Clamp(composite, 0.0m, 100.0m), 1);
    }

    /// <summary>
    /// Helper to determine qualitative disruption level based on disruption cost.
    /// </summary>
    public static string DetermineDisruptionLevel(decimal disruptionCostUsd)
    {
        if (disruptionCostUsd >= 20.0m) return "High";
        if (disruptionCostUsd >= 12.0m) return "Medium";
        return "Low";
    }

    /// <summary>
    /// Helper to evaluate negative net benefit policy.
    /// </summary>
    public static bool IsNegativeBenefitAllowed(
        decimal netBenefit,
        decimal urgencyScore,
        string policyName,
        decimal urgencyThreshold)
    {
        if (netBenefit >= 0m) return true;

        return policyName switch
        {
            "DisqualifyNegativeBenefit" => false,
            "AllowAll" => true,
            _ => urgencyScore >= urgencyThreshold // "AllowIfHighUrgency" (default)
        };
    }

    /// <inheritdoc />
    public async Task<RelocationPrioritizationResultDto> PrioritizeRelocationsAsync(
        RelocationPrioritizationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ValidateFacilityAccess(request.FacilityId);
        ValidateTenantAccess(request.ClientId);

        SlottingPolicy policy;
        if (request.PolicyId.HasValue)
        {
            policy = await _dbContext.SlottingPolicies
                .FirstOrDefaultAsync(p => p.Id == request.PolicyId.Value, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(SlottingPolicy), request.PolicyId.Value);
        }
        else
        {
            var existingPolicy = await _dbContext.SlottingPolicies
                .FirstOrDefaultAsync(p => p.IsActive && (p.FacilityId == request.FacilityId || p.FacilityId == null), cancellationToken);

            if (existingPolicy == null)
            {
                policy = SlottingPolicy.Create("DefaultRelocationPolicy", request.FacilityId, request.ClientId);
                _dbContext.SlottingPolicies.Add(policy);
            }
            else
            {
                policy = existingPolicy;
            }
        }

        string negPolicy = request.NegativeNetBenefitPolicyOverride ?? policy.NegativeNetBenefitPolicy;
        decimal urgencyThreshold = request.UrgencyThresholdOverride ?? policy.UrgencyThresholdForNegativeBenefit;
        string shiftType = request.ShiftType ?? "Standard";
        decimal laborRate = policy.LaborHourlyRateUsd > 0 ? policy.LaborHourlyRateUsd : 25.00m;
        int maxMoves = request.MaxMoves.HasValue && request.MaxMoves.Value > 0
            ? Math.Min(request.MaxMoves.Value, policy.MaxRelocationsPerBatch)
            : policy.MaxRelocationsPerBatch;

        DateTime evalTime = request.AsOfDateUtc?.ToUniversalTime() ?? DateTime.UtcNow;
        var asOf = evalTime.Date;

        // Query ALL active inventory in facility across all tenants to accurately track physical occupancy and enforce strict tenant isolation boundaries
        var allFacilityStocks = await _dbContext.StockOnHand
            .AsNoTracking()
            .Include(s => s.Item)
            .Include(s => s.Location)
            .ThenInclude(l => l.Zone)
            .Where(s => s.FacilityId == request.FacilityId &&
                        (s.QtyAvailable + s.QtyAllocated + s.QtyReserved + s.QtyQuarantined + s.QtyDamaged + s.QtyOnHold) > 0)
            .ToListAsync(cancellationToken);

        // Candidate stock to relocate is scoped to the requested tenant (or all if unspecified)
        var currentStocks = request.ClientId.HasValue
            ? allFacilityStocks.Where(s => s.ClientId == request.ClientId.Value).ToList()
            : allFacilityStocks;

        // Pre-query candidate locations
        var candidateLocations = await _dbContext.Locations
            .AsNoTracking()
            .Include(l => l.Zone)
            .Where(l => l.FacilityId == request.FacilityId && l.IsActive && l.IsPickable && !l.IsDamaged && !l.IsLocked)
            .ToListAsync(cancellationToken);

        // Pre-query existing occupancy across ALL facility stock
        var existingWeightByLoc = allFacilityStocks
            .GroupBy(s => s.LocationId)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.TotalQuantity * (s.Item?.WeightKg ?? 0m)));

        var existingVolumeByLoc = allFacilityStocks
            .GroupBy(s => s.LocationId)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.TotalQuantity * (s.Item?.VolumeCm3 > 0 ? s.Item.VolumeCm3 : (s.Item?.LengthCm ?? 0m) * (s.Item?.WidthCm ?? 0m) * (s.Item?.HeightCm ?? 0m))));

        // Pre-query authoritative velocity records
        var velocityRecords = await _dbContext.SkuVelocityClassificationRecords
            .AsNoTracking()
            .Where(r => r.FacilityId == request.FacilityId)
            .ToListAsync(cancellationToken);
        var velocityByItem = velocityRecords
            .GroupBy(r => r.ItemId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var itemIds = currentStocks.Select(s => s.ItemId).Distinct().ToList();
        var agingSnapshots = await _dbContext.DailyInventoryAgingSnapshots
            .AsNoTracking()
            .Where(s => s.FacilityId == request.FacilityId && itemIds.Contains(s.ItemId))
            .ToListAsync(cancellationToken);

        var latestAgingByItem = agingSnapshots
            .GroupBy(s => s.ItemId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.SnapshotDateUtc).First());

        string batchNumber = $"RELOC-{asOf:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        var batch = SlottingRecommendationBatch.Create(
            facilityId: request.FacilityId,
            policyId: policy.Id,
            batchNumber: batchNumber,
            clientId: request.ClientId,
            snapshotDateUtc: asOf,
            notes: request.Notes,
            policyVersion: policy.Version,
            policySnapshotJson: JsonSerializer.Serialize(policy));

        int totalEvaluated = 0;
        int disqualifiedNegativeBenefitCount = 0;
        int highUrgencyNegativeBenefitCount = 0;

        var candidatePool = new List<(
            Item Item,
            decimal Quantity,
            Location CurrentLocation,
            Location ProposedLocation,
            decimal ProposedWeightKg,
            decimal ProposedVolumeCm3,
            decimal GrossMonthlySavingsUsd,
            decimal HandlingCostUsd,
            decimal MovementCostUsd,
            decimal DisruptionCostUsd,
            decimal TotalRelocationCostUsd,
            decimal AmortizedMonthlyCostUsd,
            decimal NetMonthlyBenefitUsd,
            decimal UrgencyScore,
            string UrgencyReason,
            decimal SuitabilityScore,
            decimal PriorityScore,
            string DisruptionLevel,
            bool IsOperationallyFeasible,
            string ReasonCode,
            List<string> ViolatedConstraints,
            SlottingRecommendation Recommendation
        )>();

        var itemGroups = currentStocks.GroupBy(s => s.Item).OrderBy(g => g.Key.Sku);

        foreach (var grp in itemGroups)
        {
            var item = grp.Key;
            var primaryStock = grp.First();
            var currLoc = primaryStock.Location;
            if (currLoc == null) continue;

            latestAgingByItem.TryGetValue(item.Id, out var aging);
            bool isDead = aging != null && aging.IsDeadStock;

            velocityByItem.TryGetValue(item.Id, out var recs);
            var profile = recs != null && recs.Count > 0
                ? SlottingOptimizationService.GetProfileFromRecords(item, recs, null, evalTime, isDead)
                : SlottingOptimizationService.GetFallbackProfile(item, "CatalogFallback", null, null, isDead);

            string abcClass = profile.abcClass.ToString();
            string velCat = profile.velocityCat.ToString();
            string velTrend = profile.trend;
            int totalPicks = profile.totalPicks;
            decimal pickFreqPerDay = profile.pickFreqPerDay;

            bool currInGolden = SlottingOptimizationService.IsInGoldenZone(currLoc, policy);

            // Dead stock protection rule
            if (isDead && policy.ProtectDeadStockFromUnnecessaryRelocation && !currInGolden)
            {
                continue;
            }

            // Target candidate locations
            IEnumerable<Location> targetCandidates;
            if ((abcClass.Contains('A') || velTrend == "Surging") && !currInGolden && policy.TargetClassAPickFace)
            {
                targetCandidates = candidateLocations.Where(l => SlottingOptimizationService.IsInGoldenZone(l, policy) || l.Zone?.ZoneType == WarehouseZoneType.PickFace);
            }
            else if (velTrend == "Declining" && currInGolden)
            {
                targetCandidates = candidateLocations.Where(l => !SlottingOptimizationService.IsInGoldenZone(l, policy) && (l.ShelfLevel == 2 || l.ShelfLevel == 3));
            }
            else if (velTrend == "SeasonalSpike")
            {
                targetCandidates = candidateLocations.Where(l => l.Zone?.ZoneType == WarehouseZoneType.PickFace || SlottingOptimizationService.IsInGoldenZone(l, policy));
            }
            else if ((abcClass.Contains('C') || velTrend == "Inactive" || isDead) && currInGolden && policy.TargetClassCReserve)
            {
                targetCandidates = candidateLocations.Where(l => !SlottingOptimizationService.IsInGoldenZone(l, policy) && (l.ShelfLevel >= 3 || l.Zone?.ZoneType == WarehouseZoneType.BulkStorage));
            }
            else if (abcClass.Contains('B') && !currInGolden && (currLoc.ShelfLevel >= 4 || currLoc.Zone?.ZoneType == WarehouseZoneType.BulkStorage))
            {
                targetCandidates = candidateLocations.Where(l => l.ShelfLevel == 2 || l.ShelfLevel == 3);
            }
            else
            {
                targetCandidates = Enumerable.Empty<Location>();
            }

            decimal totalQty = grp.Sum(s => s.TotalQuantity);
            decimal qtyToMove = totalQty > 0 ? totalQty : 1m;
            decimal proposedWeightKg = item.WeightKg * qtyToMove;
            decimal proposedVolumeCm3 = (item.VolumeCm3 > 0 ? item.VolumeCm3 : item.LengthCm * item.WidthCm * item.HeightCm) * qtyToMove;

            foreach (var cand in targetCandidates)
            {
                if (cand.Id == currLoc.Id) continue;
                totalEvaluated++;

                var violations = new List<string>();
                bool feasible = true;

                // Physical hard constraints
                if (cand.LengthCm.HasValue && cand.WidthCm.HasValue)
                {
                    bool orientFit = (item.LengthCm <= cand.LengthCm.Value && item.WidthCm <= cand.WidthCm.Value) ||
                                     (item.LengthCm <= cand.WidthCm.Value && item.WidthCm <= cand.LengthCm.Value);
                    if (!orientFit)
                    {
                        feasible = false;
                        violations.Add("Dimensions exceed location boundary");
                    }
                }
                if (cand.HeightCm.HasValue && item.HeightCm > cand.HeightCm.Value)
                {
                    feasible = false;
                    violations.Add("Height exceeds location shelf clearance");
                }

                if (!cand.IsActive || !cand.IsPickable || cand.IsDamaged || cand.IsLocked)
                {
                    feasible = false;
                    violations.Add("Location is not in active/pickable operational status");
                }
                if (cand.LocationType == LocationType.Cantilever && item.LengthCm < 150m)
                {
                    feasible = false;
                    violations.Add("Cantilever restricted to long cargo (>=150cm)");
                }
                if (cand.LocationType == LocationType.DockDoor || cand.LocationType == LocationType.StagingLane)
                {
                    feasible = false;
                    violations.Add("Operational area cannot be permanent storage");
                }

                if (cand.Zone != null && item.StorageTempClass == StorageTempClass.Frozen && cand.Zone.ZoneType != WarehouseZoneType.ColdStorage)
                {
                    feasible = false;
                    violations.Add("Temperature incompatibility: Frozen cargo in non-cold zone");
                }
                if (!string.IsNullOrWhiteSpace(item.HazardClass) && (cand.Zone?.ZoneType == WarehouseZoneType.PickFace || cand.Zone?.ZoneType == WarehouseZoneType.Staging))
                {
                    feasible = false;
                    violations.Add("Hazard incompatibility in pick face / staging");
                }

                // Strict Tenant Isolation: candidate location must not contain stock of another client
                bool isOccupiedByOtherClient = allFacilityStocks.Any(s => s.LocationId == cand.Id && s.ClientId != item.ClientId);
                if (isOccupiedByOtherClient)
                {
                    feasible = false;
                    violations.Add("Cross-tenant co-mingling prohibited: location occupied by another client");
                }

                // Weight & volume limits
                decimal candMaxW = cand.MaxWeightKg ?? decimal.MaxValue;
                decimal candMaxV = (cand.LengthCm ?? 100m) * (cand.WidthCm ?? 100m) * (cand.HeightCm ?? 100m) * 0.85m;
                decimal existW = existingWeightByLoc.TryGetValue(cand.Id, out var ew) ? ew : 0m;
                decimal existV = existingVolumeByLoc.TryGetValue(cand.Id, out var ev) ? ev : 0m;

                if (existW + proposedWeightKg > candMaxW)
                {
                    feasible = false;
                    violations.Add("Exceeds location maximum weight rating");
                }
                if (existV + proposedVolumeCm3 > candMaxV)
                {
                    feasible = false;
                    violations.Add("Exceeds location volumetric capacity");
                }

                // Planar footprint check
                int candStackFactor = Math.Max(1, item.StackabilityFactor);
                decimal cLocL = cand.LengthCm ?? 120m;
                decimal cLocW = cand.WidthCm ?? 80m;
                decimal cLocH = cand.HeightCm ?? 150m;
                decimal cItemL = item.LengthCm > 0 ? item.LengthCm : 30m;
                decimal cItemW = item.WidthCm > 0 ? item.WidthCm : 20m;
                decimal cItemH = item.HeightCm > 0 ? item.HeightCm : 20m;
                int cUTier = Math.Max((int)(cLocL / cItemL) * (int)(cLocW / cItemW), (int)(cLocL / cItemW) * (int)(cLocW / cItemL));
                if (cUTier <= 0) cUTier = 1;
                int cMaxTiers = candStackFactor <= 1 ? 1 : Math.Min(candStackFactor, Math.Max(1, (int)(cLocH / cItemH)));
                decimal cCandStacks = Math.Ceiling(qtyToMove / (decimal)cMaxTiers);
                decimal cCandFootprint = cCandStacks / (decimal)cUTier;
                if (cCandFootprint > 1.000001m)
                {
                    feasible = false;
                    violations.Add("Planar floor footprint capacity exceeded");
                }

                // Travel & Economics
                decimal currDist = SlottingOptimizationService.CalculateDistanceToDockMeters(currLoc);
                decimal candDist = SlottingOptimizationService.CalculateDistanceToDockMeters(cand);
                decimal roundTripTravelSavedMeters = Math.Max(0m, (currDist - candDist) * 2.0m);
                bool isOppGain = currInGolden && (abcClass.Contains('C') || velTrend == "Declining" || isDead);
                if (roundTripTravelSavedMeters == 0m && !isOppGain && SlottingOptimizationService.IsInGoldenZone(cand, policy))
                {
                    roundTripTravelSavedMeters = 15.0m;
                }

                decimal monthlyPicksEst = totalPicks > 0 ? (decimal)totalPicks : (pickFreqPerDay > 0 ? pickFreqPerDay * 30m : 120m);

                var econ = CalculateTransparentEconomics(
                    roundTripTravelSavedMeters: roundTripTravelSavedMeters,
                    monthlyPicks: monthlyPicksEst,
                    quantity: qtyToMove,
                    weightKg: item.WeightKg,
                    currentLocation: currLoc,
                    destinationLocation: cand,
                    policy: policy,
                    shiftType: shiftType,
                    isOpportunityGain: isOppGain);

                var (urgencyScore, urgencyReason) = EvaluateOperationalUrgency(
                    item: item,
                    currentLocation: currLoc,
                    policy: policy,
                    abcClass: abcClass,
                    velocityCategory: velCat,
                    velocityTrend: velTrend);

                // Negative net benefit policy check
                if (econ.netBenefit < 0m)
                {
                    if (!IsNegativeBenefitAllowed(econ.netBenefit, urgencyScore, negPolicy, urgencyThreshold))
                    {
                        disqualifiedNegativeBenefitCount++;
                        continue;
                    }
                    else
                    {
                        highUrgencyNegativeBenefitCount++;
                    }
                }

                // 6D Placement Suitability
                AbcClassificationCategory abcCatEnum = abcClass.Contains('A') ? AbcClassificationCategory.ClassA :
                                                       abcClass.Contains('B') ? AbcClassificationCategory.ClassB :
                                                       AbcClassificationCategory.ClassC;
                InventoryMovementVelocityCategory velCatEnum = velCat.Contains("High") ? InventoryMovementVelocityCategory.FastMoving :
                                                               velCat.Contains("Low") ? InventoryMovementVelocityCategory.SlowMoving :
                                                               InventoryMovementVelocityCategory.MediumMoving;

                decimal ergoScore = SlottingOptimizationService.CalculateErgonomicScore(cand, policy, abcCatEnum, velTrend);
                decimal abcScore = SlottingOptimizationService.CalculateAbcAffinityScore(cand, abcCatEnum);
                decimal velScore = SlottingOptimizationService.CalculateVelocityAffinityScore(cand, velCatEnum, velTrend);
                decimal densityScore = SlottingOptimizationService.CalculatePickDensityScore(cand, pickFreqPerDay, totalPicks);
                decimal deltaD = currDist - candDist;
                decimal travelScore = Math.Round(Math.Clamp(50.0m + (deltaD * 2.0m), 10.0m, 100.0m), 1);

                decimal suitabilityScore = SlottingOptimizationService.CalculateCompositeSuitabilityScore(
                    policy: policy,
                    ergoScore: ergoScore,
                    abcScore: abcScore,
                    velScore: velScore,
                    densityScore: densityScore,
                    travelScore: travelScore);

                decimal costForPenalty = policy.UseTotalRelocationCostForDisruptionPenalty
                    ? (econ.handlingCost + econ.movementCost + econ.disruptionCost)
                    : econ.disruptionCost;

                decimal priorityScore = CalculatePriorityScore(
                    netMonthlyBenefitUsd: econ.netBenefit,
                    urgencyScore: urgencyScore,
                    suitabilityScore: suitabilityScore,
                    costPenaltyUsd: costForPenalty,
                    policy: policy,
                    isFeasible: feasible);

                string disruptionLevel = DetermineDisruptionLevel(econ.disruptionCost);

                string reasonCode = velTrend switch
                {
                    "Surging" when abcCatEnum == AbcClassificationCategory.ClassC => "PromoteSurgingClassCToPickFaceForLaborReduction",
                    "Surging" => "PromoteFastMoverToGoldenZonePickFace",
                    "Declining" => "RebalanceDecliningClassAFromGoldenZoneToIntermediate",
                    "SeasonalSpike" => "StageSeasonalSpikeItemInBufferZone",
                    _ when abcCatEnum == AbcClassificationCategory.ClassA => "PromoteFastMoverToGoldenZonePickFace",
                    _ when abcCatEnum == AbcClassificationCategory.ClassC => "DemoteSlowMoverToFreeGoldenZonePickFace",
                    _ => "OptimizeStoragePlacement"
                };

                var rec = new SlottingRecommendation
                {
                    BatchId = batch.Id,
                    ItemId = item.Id,
                    ItemSku = item.Sku,
                    ClientId = item.ClientId,
                    CurrentLocationId = currLoc.Id,
                    CurrentLocationCode = currLoc.LocationCode,
                    ProposedLocationId = cand.Id,
                    ProposedLocationCode = cand.LocationCode,
                    SourceZoneCode = currLoc.Zone?.ZoneCode ?? "MAIN",
                    DestinationZoneCode = cand.Zone?.ZoneCode ?? "MAIN",
                    SkuVelocity = velCat,
                    AbcClass = abcClass,
                    VelocityTrend = velTrend,
                    ReasonCode = reasonCode,
                    SuitabilityScore = suitabilityScore,
                    ErgonomicScore = ergoScore,
                    AbcAffinityScore = abcScore,
                    VelocityAffinityScore = velScore,
                    PickDensityScore = densityScore,
                    TravelDistanceDeltaMeters = roundTripTravelSavedMeters,
                    EstimatedMoveLaborMinutes = Math.Round(((econ.movementCost / laborRate) * 60m), 2),
                    EstimatedMonthlyNetSavingsUsd = econ.netBenefit,
                    GrossMonthlySavingsUsd = econ.grossSavings,
                    HandlingCostUsd = econ.handlingCost,
                    MovementCostUsd = econ.movementCost,
                    DisruptionCostUsd = econ.disruptionCost,
                    TotalRelocationCostUsd = econ.handlingCost + econ.movementCost + econ.disruptionCost,
                    UrgencyScore = urgencyScore,
                    UrgencyReason = urgencyReason,
                    PriorityScore = priorityScore,
                    DisruptionLevel = disruptionLevel,
                    IsOperationallyFeasible = feasible,
                    IsHardConstraintViolated = !feasible,
                    ViolatedConstraintsSummary = violations.Count > 0 ? string.Join("; ", violations) : null,
                    ClassificationSource = profile.classificationSource,
                    ProjectionCalculatedAtUtc = profile.projectionCalculatedAtUtc,
                    ProjectionAgeDays = profile.projectionAgeDays,
                    Status = SlottingRecommendationStatus.Pending
                };

                candidatePool.Add((
                    Item: item,
                    Quantity: qtyToMove,
                    CurrentLocation: currLoc,
                    ProposedLocation: cand,
                    ProposedWeightKg: proposedWeightKg,
                    ProposedVolumeCm3: proposedVolumeCm3,
                    GrossMonthlySavingsUsd: econ.grossSavings,
                    HandlingCostUsd: econ.handlingCost,
                    MovementCostUsd: econ.movementCost,
                    DisruptionCostUsd: econ.disruptionCost,
                    TotalRelocationCostUsd: econ.handlingCost + econ.movementCost + econ.disruptionCost,
                    AmortizedMonthlyCostUsd: econ.amortizedMonthlyCost,
                    NetMonthlyBenefitUsd: econ.netBenefit,
                    UrgencyScore: urgencyScore,
                    UrgencyReason: urgencyReason,
                    SuitabilityScore: suitabilityScore,
                    PriorityScore: priorityScore,
                    DisruptionLevel: disruptionLevel,
                    IsOperationallyFeasible: feasible,
                    ReasonCode: reasonCode,
                    ViolatedConstraints: violations,
                    Recommendation: rec
                ));
            }
        }

        // Deterministic Ranking & Tie-Breaking
        // P DESC -> B_net DESC -> U DESC -> ItemSku ASC -> ProposedLocationCode ASC
        var sortedCandidates = candidatePool
            .OrderByDescending(c => c.PriorityScore)
            .ThenByDescending(c => c.NetMonthlyBenefitUsd)
            .ThenByDescending(c => c.UrgencyScore)
            .ThenBy(c => c.Item.Sku, StringComparer.Ordinal)
            .ThenBy(c => c.ProposedLocation.LocationCode, StringComparer.Ordinal)
            .ToList();

        // Multi-SKU Virtual Destination State Tracking & Geometric Non-Overlap Packing
        var acceptedRecommendations = new List<PrioritizedRelocationRecommendationDto>();
        var persistedRecommendations = new List<SlottingRecommendation>();
        var assignedItemIds = new HashSet<Guid>();

        var virtualDestinations = candidateLocations.ToDictionary(
            loc => loc.Id,
            loc =>
            {
                var locStocks = allFacilityStocks.Where(s => s.LocationId == loc.Id).ToList();
                var primaryClient = locStocks.FirstOrDefault()?.ClientId;
                bool hasNonStackable = locStocks.Any(s => s.Item != null && s.Item.StackabilityFactor <= 1);
                var hazStock = locStocks.FirstOrDefault(s => s.Item != null && !string.IsNullOrWhiteSpace(s.Item.HazardClass));
                var tempStock = locStocks.FirstOrDefault(s => s.Item != null);

                decimal locL = loc.LengthCm ?? 120m;
                decimal locW = loc.WidthCm ?? 80m;
                decimal locH = loc.HeightCm ?? 150m;

                var existingPlacements = new List<VirtualPlacement>();
                foreach (var grp in locStocks.GroupBy(s => s.ItemId))
                {
                    var eItem = grp.First().Item;
                    if (eItem == null) continue;
                    decimal eQty = grp.Sum(s => s.TotalQuantity);
                    if (GeometricOccupancyEngine.TryPackItem(locL, locW, locH, existingPlacements, eItem, eQty, out var ePlacements))
                    {
                        foreach (var p in ePlacements) p.IsExistingPhysicalStock = true;
                        existingPlacements.AddRange(ePlacements);
                    }
                }

                var vState = new SlottingOptimizationService.VirtualDestinationState
                {
                    Location = loc,
                    LocationLengthCm = locL,
                    LocationWidthCm = locW,
                    LocationHeightCm = locH,
                    InitialPhysicalWeightKg = existingWeightByLoc.TryGetValue(loc.Id, out var ew) ? ew : 0m,
                    InitialPhysicalVolumeCm3 = existingVolumeByLoc.TryGetValue(loc.Id, out var ev) ? ev : 0m,
                    CumulativeProposedWeightKg = 0m,
                    CumulativeProposedVolumeCm3 = 0m,
                    MaxWeightKg = loc.MaxWeightKg ?? decimal.MaxValue,
                    UsableVolumeCm3 = (loc.LengthCm ?? 100m) * (loc.WidthCm ?? 100m) * (loc.HeightCm ?? 100m) * 0.85m,
                    ActiveClientId = primaryClient,
                    HasExistingPhysicalStock = locStocks.Count > 0,
                    HasNonStackableOrCrushSensitiveItem = hasNonStackable,
                    HasHazardousGoods = hazStock != null,
                    HazardClass = hazStock?.Item?.HazardClass,
                    RequiredTempClass = tempStock?.Item?.StorageTempClass
                };
                vState.ExistingPhysicalPlacements.AddRange(existingPlacements);
                return vState;
            });

        int rank = 1;
        foreach (var cand in sortedCandidates)
        {
            if (acceptedRecommendations.Count >= maxMoves) break;
            if (assignedItemIds.Contains(cand.Item.Id)) continue;
            if (!cand.IsOperationallyFeasible) continue;

            if (!virtualDestinations.TryGetValue(cand.ProposedLocation.Id, out var vDest))
            {
                continue;
            }

            // 1. Client Tenancy Exclusivity
            if (vDest.ActiveClientId.HasValue && vDest.ActiveClientId.Value != cand.Item.ClientId)
            {
                continue;
            }

            // 2. Weight Capacity Check
            if (vDest.InitialPhysicalWeightKg + vDest.CumulativeProposedWeightKg + cand.ProposedWeightKg > vDest.MaxWeightKg)
            {
                continue;
            }

            // 3. Volumetric Capacity Check
            if (vDest.InitialPhysicalVolumeCm3 + vDest.CumulativeProposedVolumeCm3 + cand.ProposedVolumeCm3 > vDest.UsableVolumeCm3)
            {
                continue;
            }

            // 4. Non-Stackable / Crush-Sensitive Check
            if (cand.Item.StackabilityFactor <= 1)
            {
                if (vDest.HasExistingPhysicalStock || vDest.AcceptedItemIds.Count > 0)
                {
                    continue;
                }
            }
            else if (vDest.HasNonStackableOrCrushSensitiveItem)
            {
                continue;
            }

            // 5. 2D Rectangular Geometric Non-Overlap Check
            decimal candItemL = cand.Item.LengthCm > 0 ? cand.Item.LengthCm : 30m;
            decimal candItemW = cand.Item.WidthCm > 0 ? cand.Item.WidthCm : 20m;
            decimal candItemH = cand.Item.HeightCm > 0 ? cand.Item.HeightCm : 20m;

            if (candItemH > vDest.LocationHeightCm) continue;
            bool fitsNormal = candItemL <= vDest.LocationLengthCm && candItemW <= vDest.LocationWidthCm;
            bool fitsRotated = cand.Item.AllowPlanarRotation && candItemW <= vDest.LocationLengthCm && candItemL <= vDest.LocationWidthCm;
            if (!fitsNormal && !fitsRotated) continue;

            var currentPlacements = new List<VirtualPlacement>(vDest.ExistingPhysicalPlacements.Count + vDest.AcceptedVirtualPlacements.Count);
            currentPlacements.AddRange(vDest.ExistingPhysicalPlacements);
            currentPlacements.AddRange(vDest.AcceptedVirtualPlacements);

            if (!GeometricOccupancyEngine.TryPackItem(
                vDest.LocationLengthCm,
                vDest.LocationWidthCm,
                vDest.LocationHeightCm,
                currentPlacements,
                cand.Item,
                cand.Quantity,
                out var candidatePlacements))
            {
                continue;
            }

            // 6. Hazard Compatibility
            bool isCandHazardous = !string.IsNullOrWhiteSpace(cand.Item.HazardClass);
            if (isCandHazardous)
            {
                if (vDest.HasExistingPhysicalStock && !vDest.HasHazardousGoods) continue;
                if (vDest.AcceptedItemIds.Count > 0 && !vDest.HasHazardousGoods) continue;
                if (vDest.HasHazardousGoods && !string.Equals(vDest.HazardClass, cand.Item.HazardClass, StringComparison.OrdinalIgnoreCase)) continue;
            }
            else if (vDest.HasHazardousGoods)
            {
                continue;
            }

            // 7. Temperature Compatibility
            if (vDest.RequiredTempClass.HasValue && vDest.RequiredTempClass.Value != cand.Item.StorageTempClass)
            {
                continue;
            }

            // Accept proposal
            assignedItemIds.Add(cand.Item.Id);
            vDest.CumulativeProposedWeightKg += cand.ProposedWeightKg;
            vDest.CumulativeProposedVolumeCm3 += cand.ProposedVolumeCm3;
            vDest.ActiveClientId = cand.Item.ClientId;
            vDest.AcceptedVirtualPlacements.AddRange(candidatePlacements);
            vDest.AcceptedAllocations.Add((cand.Item, cand.Quantity));
            if (cand.Item.StackabilityFactor <= 1) vDest.HasNonStackableOrCrushSensitiveItem = true;
            if (isCandHazardous)
            {
                vDest.HasHazardousGoods = true;
                vDest.HazardClass = cand.Item.HazardClass;
            }
            vDest.RequiredTempClass = cand.Item.StorageTempClass;
            vDest.AcceptedItemIds.Add(cand.Item.Id);

            cand.Recommendation.CumulativeWeightConsumedKg = vDest.CumulativeProposedWeightKg;
            cand.Recommendation.CumulativeVolumeConsumedCm3 = vDest.CumulativeProposedVolumeCm3;

            persistedRecommendations.Add(cand.Recommendation);

            decimal maxCostForPenalty = policy.UseTotalRelocationCostForDisruptionPenalty
                ? (policy.MaxAcceptableRelocationCostUsd > 0 ? policy.MaxAcceptableRelocationCostUsd : 100.0m)
                : (policy.MaxAcceptableDisruptionCostUsd > 0 ? policy.MaxAcceptableDisruptionCostUsd : 30.0m);

            decimal costForPenaltyDto = policy.UseTotalRelocationCostForDisruptionPenalty
                ? cand.TotalRelocationCostUsd
                : cand.DisruptionCostUsd;

            decimal sDisruptPenalty = Math.Clamp((costForPenaltyDto / maxCostForPenalty) * 100.0m, 0.0m, 100.0m);
            decimal sMitigation = 100.0m - sDisruptPenalty;

            acceptedRecommendations.Add(new PrioritizedRelocationRecommendationDto
            {
                RecommendationId = cand.Recommendation.Id,
                ItemId = cand.Item.Id,
                ItemSku = cand.Item.Sku,
                ClientId = cand.Item.ClientId,
                CurrentLocationId = cand.CurrentLocation.Id,
                CurrentLocationCode = cand.CurrentLocation.LocationCode,
                ProposedLocationId = cand.ProposedLocation.Id,
                ProposedLocationCode = cand.ProposedLocation.LocationCode,
                Quantity = cand.Quantity,
                GrossMonthlySavingsUsd = cand.GrossMonthlySavingsUsd,
                HandlingCostUsd = cand.HandlingCostUsd,
                MovementCostUsd = cand.MovementCostUsd,
                DisruptionCostUsd = cand.DisruptionCostUsd,
                TotalRelocationCostUsd = cand.TotalRelocationCostUsd,
                AmortizedMonthlyCostUsd = cand.AmortizedMonthlyCostUsd,
                NetMonthlyBenefitUsd = cand.NetMonthlyBenefitUsd,
                UrgencyScore = cand.UrgencyScore,
                UrgencyReason = cand.UrgencyReason,
                SuitabilityScore = cand.SuitabilityScore,
                DisruptionPenaltyScore = sDisruptPenalty,
                DisruptionMitigationScore = sMitigation,
                PriorityScore = cand.PriorityScore,
                DisruptionLevel = cand.DisruptionLevel,
                IsOperationallyFeasible = cand.IsOperationallyFeasible,
                RankingRank = rank++,
                ReasonCode = cand.ReasonCode,
                ViolatedConstraints = cand.ViolatedConstraints
            });
        }

        decimal totalLaborMin = persistedRecommendations.Sum(r => r.EstimatedMoveLaborMinutes);
        decimal totalTravelSaved = persistedRecommendations.Sum(r => r.TravelDistanceDeltaMeters);
        decimal totalNetSavings = persistedRecommendations.Sum(r => r.EstimatedMonthlyNetSavingsUsd);
        decimal avgScore = persistedRecommendations.Count > 0 ? persistedRecommendations.Average(r => r.SuitabilityScore) : 0m;

        batch.MarkSimulated(
            totalMoves: persistedRecommendations.Count,
            estLaborMin: totalLaborMin,
            estTravelSavedMeters: totalTravelSaved,
            estMonthlySavingsUsd: totalNetSavings,
            avgScore: Math.Round(avgScore, 1));

        batch.Recommendations = persistedRecommendations;

        _dbContext.SlottingRecommendationBatches.Add(batch);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Prioritized {AcceptedCount} relocations for Facility {FacilityId} in batch {BatchNumber} (DisqualifiedNegativeBenefit={DisqCount}, HighUrgencyNegativeBenefit={HighUrgCount})",
            acceptedRecommendations.Count, request.FacilityId, batch.BatchNumber, disqualifiedNegativeBenefitCount, highUrgencyNegativeBenefitCount);

        return new RelocationPrioritizationResultDto
        {
            BatchId = batch.Id,
            BatchNumber = batch.BatchNumber,
            FacilityId = batch.FacilityId,
            ClientId = batch.ClientId,
            PolicyId = policy.Id,
            PolicyName = policy.Name,
            EvaluatedAtUtc = DateTime.UtcNow,
            TotalCandidatesEvaluated = totalEvaluated,
            FeasibleCandidatesCount = candidatePool.Count(c => c.IsOperationallyFeasible),
            DisqualifiedNegativeBenefitCount = disqualifiedNegativeBenefitCount,
            HighUrgencyNegativeBenefitCount = highUrgencyNegativeBenefitCount,
            TotalProjectedGrossMonthlySavingsUsd = acceptedRecommendations.Sum(r => r.GrossMonthlySavingsUsd),
            TotalProjectedOneTimeRelocationCostUsd = acceptedRecommendations.Sum(r => r.TotalRelocationCostUsd),
            TotalProjectedNetMonthlyBenefitUsd = acceptedRecommendations.Sum(r => r.NetMonthlyBenefitUsd),
            PhysicalInventoryTransfersTriggered = 0,
            LedgerMutationsOccurred = 0,
            PrioritizedRecommendations = acceptedRecommendations
        };
    }

    /// <inheritdoc />
    public async Task<SlottingBatchStatus> SubmitBatchForReviewAsync(
        Guid batchId,
        CancellationToken cancellationToken = default)
    {
        var batch = await _dbContext.SlottingRecommendationBatches
            .FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(SlottingRecommendationBatch), batchId);

        ValidateFacilityAccess(batch.FacilityId);
        ValidateTenantAccess(batch.ClientId);

        if (batch.Status != SlottingBatchStatus.Simulated && batch.Status != SlottingBatchStatus.Draft)
        {
            throw new InvalidOperationException($"Cannot submit batch '{batch.BatchNumber}' in '{batch.Status}' state for review.");
        }

        batch.Status = SlottingBatchStatus.PendingApproval;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Batch {BatchNumber} transitioned to PendingApproval for supervisory review", batch.BatchNumber);
        return batch.Status;
    }

    /// <inheritdoc />
    public async Task<SlottingBatchStatus> CancelBatchAsync(
        Guid batchId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var batch = await _dbContext.SlottingRecommendationBatches
            .FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(SlottingRecommendationBatch), batchId);

        ValidateFacilityAccess(batch.FacilityId);
        ValidateTenantAccess(batch.ClientId);

        if (batch.Status == SlottingBatchStatus.Executed)
        {
            throw new InvalidOperationException($"Cannot cancel batch '{batch.BatchNumber}' because it has already been executed.");
        }

        batch.Cancel(reason);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Batch {BatchNumber} cancelled. Reason: {Reason}", batch.BatchNumber, reason);
        return batch.Status;
    }

    /// <inheritdoc />
    public async Task<RelocationApprovalResultDto> ApproveRelocationPlanAsync(
        RelocationApprovalRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var batch = await _dbContext.SlottingRecommendationBatches
            .Include(b => b.Recommendations)
            .FirstOrDefaultAsync(b => b.Id == request.BatchId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(SlottingRecommendationBatch), request.BatchId);

        ValidateFacilityAccess(batch.FacilityId);
        ValidateTenantAccess(batch.ClientId);

        if (batch.Status == SlottingBatchStatus.Cancelled)
        {
            throw new InvalidOperationException($"Cannot approve batch '{batch.BatchNumber}' because it has been cancelled.");
        }
        if (batch.Status == SlottingBatchStatus.Executed)
        {
            throw new InvalidOperationException($"Cannot approve batch '{batch.BatchNumber}' because it has already been executed.");
        }
        if (batch.Status != SlottingBatchStatus.PendingApproval)
        {
            throw new InvalidOperationException($"Cannot approve batch '{batch.BatchNumber}' in '{batch.Status}' state. Batch must be submitted for review (PendingApproval) before administrative approval or rejection.");
        }

        var approvedIds = new HashSet<Guid>(request.ApprovedRecommendationIds);
        var rejectedIds = new HashSet<Guid>(request.RejectedRecommendationIds);

        int approvedCount = 0;
        int rejectedCount = 0;
        int skippedCount = 0;

        foreach (var rec in batch.Recommendations)
        {
            if (approvedIds.Contains(rec.Id))
            {
                // Hard-gate check: cannot approve infeasible recommendations
                if (rec.IsHardConstraintViolated || !rec.IsOperationallyFeasible)
                {
                    rec.Status = SlottingRecommendationStatus.Rejected;
                    string rejectReason = (request.RejectionReasonsByRecommendationId != null &&
                                          request.RejectionReasonsByRecommendationId.TryGetValue(rec.Id, out var customR1))
                                          ? customR1
                                          : "Cannot approve recommendation with hard constraint violations.";
                    rec.RejectionReason = rejectReason;
                    rejectedCount++;
                }
                else
                {
                    rec.Status = SlottingRecommendationStatus.Approved;
                    approvedCount++;
                }
            }
            else if (rejectedIds.Contains(rec.Id))
            {
                rec.Status = SlottingRecommendationStatus.Rejected;
                string rejectReason = (request.RejectionReasonsByRecommendationId != null &&
                                      request.RejectionReasonsByRecommendationId.TryGetValue(rec.Id, out var customR2))
                                      ? customR2
                                      : "Rejected by reviewer during administrative plan review.";
                rec.RejectionReason = rejectReason;
                rejectedCount++;
            }
            else
            {
                rec.Status = SlottingRecommendationStatus.Skipped;
                skippedCount++;
            }
        }

        batch.ApprovedMovesCount = approvedCount;
        batch.RejectedMovesCount = rejectedCount;
        if (approvedCount == batch.TotalProposedMoves && batch.TotalProposedMoves > 0)
        {
            batch.Status = SlottingBatchStatus.Approved;
        }
        else if (approvedCount > 0)
        {
            batch.Status = SlottingBatchStatus.PartiallyApproved;
        }
        else
        {
            batch.Status = SlottingBatchStatus.Rejected;
        }

        batch.ApprovedBy = _currentUser.UserId;
        batch.ApprovedAtUtc = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(request.ApprovalNotes))
        {
            batch.Notes = string.IsNullOrWhiteSpace(batch.Notes)
                ? $"ApprovalNotes: {request.ApprovalNotes}"
                : $"{batch.Notes}\nApprovalNotes: {request.ApprovalNotes}";
        }

        // READ-ONLY OPERATIONAL INVARIANCE:
        // Strictly save batch and recommendation metadata only!
        // ZERO updates to StockOnHand, InventoryTransaction, PickTasks, Locations.
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Processed administrative approval for relocation batch {BatchNumber}: {Approved} approved, {Rejected} rejected, {Skipped} skipped (Ledger mutations = 0)",
            batch.BatchNumber, approvedCount, rejectedCount, skippedCount);

        return new RelocationApprovalResultDto
        {
            BatchId = batch.Id,
            Status = batch.Status,
            ApprovedCount = approvedCount,
            RejectedCount = rejectedCount,
            SkippedCount = skippedCount,
            PhysicalInventoryTransfersTriggered = 0,
            LedgerMutationsOccurred = 0,
            ProcessedAtUtc = DateTime.UtcNow,
            ApprovedBy = _currentUser.UserId,
            Message = $"Relocation plan processed administratively. {approvedCount} recommendations approved, {rejectedCount} rejected. Operational ledgers remained untouched."
        };
    }

    /// <inheritdoc />
    public async Task<RelocationPrioritizationResultDto?> GetPrioritizedPlanAsync(
        Guid batchId,
        CancellationToken cancellationToken = default)
    {
        var batch = await _dbContext.SlottingRecommendationBatches
            .AsNoTracking()
            .Include(b => b.Policy)
            .Include(b => b.Recommendations)
            .FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken);

        if (batch == null) return null;
        ValidateFacilityAccess(batch.FacilityId);
        ValidateTenantAccess(batch.ClientId);

        var policy = batch.Policy ?? SlottingPolicy.Create("Default", batch.FacilityId, batch.ClientId);
        decimal maxCost = policy.MaxAcceptableRelocationCostUsd > 0 ? policy.MaxAcceptableRelocationCostUsd : 100.0m;

        int rank = 1;
        var dtos = batch.Recommendations
            .OrderByDescending(r => r.PriorityScore)
            .ThenByDescending(r => r.EstimatedMonthlyNetSavingsUsd)
            .ThenByDescending(r => r.UrgencyScore)
            .ThenBy(r => r.ItemSku, StringComparer.Ordinal)
            .ThenBy(r => r.ProposedLocationCode, StringComparer.Ordinal)
            .Select(r => new PrioritizedRelocationRecommendationDto
            {
                RecommendationId = r.Id,
                ItemId = r.ItemId,
                ItemSku = r.ItemSku,
                ClientId = r.ClientId,
                CurrentLocationId = r.CurrentLocationId,
                CurrentLocationCode = r.CurrentLocationCode,
                ProposedLocationId = r.ProposedLocationId,
                ProposedLocationCode = r.ProposedLocationCode,
                Quantity = 1m,
                GrossMonthlySavingsUsd = r.GrossMonthlySavingsUsd,
                HandlingCostUsd = r.HandlingCostUsd,
                MovementCostUsd = r.MovementCostUsd,
                DisruptionCostUsd = r.DisruptionCostUsd,
                TotalRelocationCostUsd = r.TotalRelocationCostUsd,
                AmortizedMonthlyCostUsd = policy.AmortizationMonths > 0 ? Math.Round(r.TotalRelocationCostUsd / policy.AmortizationMonths, 2) : 0m,
                NetMonthlyBenefitUsd = r.EstimatedMonthlyNetSavingsUsd,
                UrgencyScore = r.UrgencyScore,
                UrgencyReason = r.UrgencyReason ?? string.Empty,
                SuitabilityScore = r.SuitabilityScore,
                DisruptionPenaltyScore = Math.Clamp((r.TotalRelocationCostUsd / maxCost) * 100.0m, 0.0m, 100.0m),
                PriorityScore = r.PriorityScore,
                DisruptionLevel = r.DisruptionLevel,
                IsOperationallyFeasible = r.IsOperationallyFeasible,
                RankingRank = rank++,
                ReasonCode = r.ReasonCode,
                ViolatedConstraints = !string.IsNullOrEmpty(r.ViolatedConstraintsSummary) ? r.ViolatedConstraintsSummary.Split(';').Select(s => s.Trim()).ToList() : []
            }).ToList();

        return new RelocationPrioritizationResultDto
        {
            BatchId = batch.Id,
            BatchNumber = batch.BatchNumber,
            FacilityId = batch.FacilityId,
            ClientId = batch.ClientId,
            PolicyId = policy.Id,
            PolicyName = policy.Name,
            EvaluatedAtUtc = batch.SnapshotDateUtc,
            TotalCandidatesEvaluated = batch.TotalProposedMoves,
            FeasibleCandidatesCount = dtos.Count(d => d.IsOperationallyFeasible),
            DisqualifiedNegativeBenefitCount = 0,
            HighUrgencyNegativeBenefitCount = 0,
            TotalProjectedGrossMonthlySavingsUsd = dtos.Sum(d => d.GrossMonthlySavingsUsd),
            TotalProjectedOneTimeRelocationCostUsd = dtos.Sum(d => d.TotalRelocationCostUsd),
            TotalProjectedNetMonthlyBenefitUsd = dtos.Sum(d => d.NetMonthlyBenefitUsd),
            PhysicalInventoryTransfersTriggered = 0,
            LedgerMutationsOccurred = 0,
            PrioritizedRecommendations = dtos
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SlottingBatchSummaryDto>> GetBatchesForFacilityAsync(
        Guid facilityId,
        Guid? clientId = null,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        ValidateFacilityAccess(facilityId);
        ValidateTenantAccess(clientId);

        var query = _dbContext.SlottingRecommendationBatches
            .AsNoTracking()
            .Include(b => b.Policy)
            .Where(b => b.FacilityId == facilityId);

        if (clientId.HasValue)
        {
            query = query.Where(b => b.ClientId == clientId.Value || b.ClientId == null);
        }

        var batches = await query
            .OrderByDescending(b => b.SnapshotDateUtc)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(cancellationToken);

        return batches.Select(b => new SlottingBatchSummaryDto
        {
            BatchId = b.Id,
            BatchNumber = b.BatchNumber,
            FacilityId = b.FacilityId,
            ClientId = b.ClientId,
            PolicyId = b.PolicyId,
            PolicyName = b.Policy?.Name ?? "Default Policy",
            Status = b.Status,
            SnapshotDateUtc = b.SnapshotDateUtc,
            TotalProposedMoves = b.TotalProposedMoves,
            ApprovedMovesCount = b.ApprovedMovesCount,
            RejectedMovesCount = b.RejectedMovesCount,
            EstimatedMonthlyNetSavingsUsd = b.EstimatedMonthlyNetSavingsUsd,
            AverageSuitabilityScore = b.AverageSuitabilityScore,
            ApprovedBy = b.ApprovedBy,
            ApprovedAtUtc = b.ApprovedAtUtc,
            Notes = b.Notes
        }).ToList();
    }

    /// <inheritdoc />
    public async Task<SlottingRecommendationDetailDto?> InspectRecommendationAsync(
        Guid recommendationId,
        CancellationToken cancellationToken = default)
    {
        var rec = await _dbContext.SlottingRecommendations
            .AsNoTracking()
            .Include(r => r.Batch)
            .ThenInclude(b => b.Policy)
            .Include(r => r.Item)
            .Include(r => r.CurrentLocation)
            .ThenInclude(l => l.Zone)
            .Include(r => r.ProposedLocation)
            .ThenInclude(l => l.Zone)
            .FirstOrDefaultAsync(r => r.Id == recommendationId, cancellationToken);

        if (rec == null) return null;

        ValidateFacilityAccess(rec.Batch.FacilityId);
        ValidateTenantAccess(rec.ClientId);

        var policy = rec.Batch.Policy ?? SlottingPolicy.Create("Default", rec.Batch.FacilityId, rec.ClientId);

        // Fetch occupancy for source and dest
        var locIds = new[] { rec.CurrentLocationId, rec.ProposedLocationId };
        var locStocks = await _dbContext.StockOnHand
            .AsNoTracking()
            .Include(s => s.Item)
            .Where(s => locIds.Contains(s.LocationId) &&
                        (s.QtyAvailable + s.QtyAllocated + s.QtyReserved + s.QtyQuarantined + s.QtyDamaged + s.QtyOnHold) > 0)
            .ToListAsync(cancellationToken);

        var currLocStocks = locStocks.Where(s => s.LocationId == rec.CurrentLocationId).ToList();
        var destLocStocks = locStocks.Where(s => s.LocationId == rec.ProposedLocationId).ToList();

        decimal currOccupiedW = currLocStocks.Sum(s => s.TotalQuantity * (s.Item?.WeightKg ?? 0m));
        decimal currOccupiedV = currLocStocks.Sum(s => s.TotalQuantity * (s.Item?.VolumeCm3 > 0 ? s.Item.VolumeCm3 : (s.Item?.LengthCm ?? 0m) * (s.Item?.WidthCm ?? 0m) * (s.Item?.HeightCm ?? 0m)));
        decimal currMaxW = rec.CurrentLocation.MaxWeightKg ?? 1000m;
        decimal currMaxV = (rec.CurrentLocation.LengthCm ?? 120m) * (rec.CurrentLocation.WidthCm ?? 80m) * (rec.CurrentLocation.HeightCm ?? 150m) * 0.85m;

        decimal destOccupiedW = destLocStocks.Sum(s => s.TotalQuantity * (s.Item?.WeightKg ?? 0m));
        decimal destOccupiedV = destLocStocks.Sum(s => s.TotalQuantity * (s.Item?.VolumeCm3 > 0 ? s.Item.VolumeCm3 : (s.Item?.LengthCm ?? 0m) * (s.Item?.WidthCm ?? 0m) * (s.Item?.HeightCm ?? 0m)));
        decimal destMaxW = rec.ProposedLocation.MaxWeightKg ?? 1000m;
        decimal destMaxV = (rec.ProposedLocation.LengthCm ?? 120m) * (rec.ProposedLocation.WidthCm ?? 80m) * (rec.ProposedLocation.HeightCm ?? 150m) * 0.85m;

        var currComp = new LocationComparisonDetailDto
        {
            LocationId = rec.CurrentLocation.Id,
            LocationCode = rec.CurrentLocation.LocationCode,
            ZoneCode = rec.CurrentLocation.Zone?.ZoneCode ?? "MAIN",
            ZoneType = rec.CurrentLocation.Zone?.ZoneType ?? WarehouseZoneType.PickFace,
            ShelfLevel = rec.CurrentLocation.ShelfLevel ?? 1,
            ZCoord = rec.CurrentLocation.ZCoord ?? 1.0m,
            DistanceToDockMeters = SlottingOptimizationService.CalculateDistanceToDockMeters(rec.CurrentLocation),
            IsInGoldenZone = SlottingOptimizationService.IsInGoldenZone(rec.CurrentLocation, policy),
            OccupiedWeightKg = Math.Round(currOccupiedW, 2),
            MaxWeightKg = currMaxW,
            WeightUtilizationPercent = currMaxW > 0 ? Math.Round((currOccupiedW / currMaxW) * 100.0m, 1) : 0m,
            OccupiedVolumeCm3 = Math.Round(currOccupiedV, 0),
            MaxVolumeCm3 = Math.Round(currMaxV, 0),
            VolumeUtilizationPercent = currMaxV > 0 ? Math.Round((currOccupiedV / currMaxV) * 100.0m, 1) : 0m
        };

        var destComp = new LocationComparisonDetailDto
        {
            LocationId = rec.ProposedLocation.Id,
            LocationCode = rec.ProposedLocation.LocationCode,
            ZoneCode = rec.ProposedLocation.Zone?.ZoneCode ?? "MAIN",
            ZoneType = rec.ProposedLocation.Zone?.ZoneType ?? WarehouseZoneType.PickFace,
            ShelfLevel = rec.ProposedLocation.ShelfLevel ?? 1,
            ZCoord = rec.ProposedLocation.ZCoord ?? 1.0m,
            DistanceToDockMeters = SlottingOptimizationService.CalculateDistanceToDockMeters(rec.ProposedLocation),
            IsInGoldenZone = SlottingOptimizationService.IsInGoldenZone(rec.ProposedLocation, policy),
            OccupiedWeightKg = Math.Round(destOccupiedW, 2),
            MaxWeightKg = destMaxW,
            WeightUtilizationPercent = destMaxW > 0 ? Math.Round((destOccupiedW / destMaxW) * 100.0m, 1) : 0m,
            OccupiedVolumeCm3 = Math.Round(destOccupiedV, 0),
            MaxVolumeCm3 = Math.Round(destMaxV, 0),
            VolumeUtilizationPercent = destMaxV > 0 ? Math.Round((destOccupiedV / destMaxV) * 100.0m, 1) : 0m
        };

        // Urgency factor decomposition
        decimal uVel = 0m, uCong = 0m, uSeas = 0m, uErgo = 0m;
        bool isFast = string.Equals(rec.AbcClass, "A", StringComparison.OrdinalIgnoreCase) || string.Equals(rec.VelocityTrend, "Surging", StringComparison.OrdinalIgnoreCase);
        bool inGolden = SlottingOptimizationService.IsInGoldenZone(rec.CurrentLocation, policy);
        if (isFast && (rec.CurrentLocation.ShelfLevel ?? 1) >= 3 && !inGolden) uVel = 45m;
        else if (isFast && !inGolden) uVel = 25m;

        bool isSlow = string.Equals(rec.AbcClass, "C", StringComparison.OrdinalIgnoreCase) || string.Equals(rec.VelocityTrend, "Inactive", StringComparison.OrdinalIgnoreCase) || string.Equals(rec.VelocityTrend, "DeadStock", StringComparison.OrdinalIgnoreCase);
        if (isSlow && inGolden) uCong = 40m;
        else if (string.Equals(rec.VelocityTrend, "Declining", StringComparison.OrdinalIgnoreCase) && inGolden) uCong = 20m;

        if (string.Equals(rec.VelocityTrend, "SeasonalSpike", StringComparison.OrdinalIgnoreCase) || string.Equals(rec.VelocityTrend, "Surging", StringComparison.OrdinalIgnoreCase)) uSeas = 30m;

        decimal weight = rec.Item?.WeightKg ?? 5m;
        decimal z = rec.CurrentLocation.ZCoord ?? 1.0m;
        if (weight >= 20m && (z > 1.5m || (rec.CurrentLocation.ShelfLevel ?? 1) >= 3 || z < 0.4m)) uErgo = 30m;

        decimal maxDisrupt = policy.UseTotalRelocationCostForDisruptionPenalty
            ? (policy.MaxAcceptableRelocationCostUsd > 0 ? policy.MaxAcceptableRelocationCostUsd : 100m)
            : (policy.MaxAcceptableDisruptionCostUsd > 0 ? policy.MaxAcceptableDisruptionCostUsd : 30m);

        decimal costForPenalty = policy.UseTotalRelocationCostForDisruptionPenalty
            ? rec.TotalRelocationCostUsd
            : rec.DisruptionCostUsd;

        decimal sDisruptPenalty = Math.Clamp((costForPenalty / maxDisrupt) * 100.0m, 0.0m, 100.0m);
        decimal sMitigation = 100.0m - sDisruptPenalty;

        return new SlottingRecommendationDetailDto
        {
            RecommendationId = rec.Id,
            BatchId = rec.BatchId,
            BatchNumber = rec.Batch.BatchNumber,
            ItemId = rec.ItemId,
            ItemSku = rec.ItemSku,
            ItemDescription = rec.Item?.Description ?? rec.Item?.ShortName,
            ClientId = rec.ClientId,
            Quantity = 1m,
            Status = rec.Status,
            RejectionReason = rec.RejectionReason,
            ReasonCode = rec.ReasonCode,
            IsOperationallyFeasible = rec.IsOperationallyFeasible,
            IsHardConstraintViolated = rec.IsHardConstraintViolated,
            ViolatedConstraints = !string.IsNullOrEmpty(rec.ViolatedConstraintsSummary) ? rec.ViolatedConstraintsSummary.Split(';').Select(s => s.Trim()).ToList() : [],
            SuitabilityScore = rec.SuitabilityScore,
            ErgonomicScore = rec.ErgonomicScore,
            AbcAffinityScore = rec.AbcAffinityScore,
            VelocityAffinityScore = rec.VelocityAffinityScore,
            PickDensityScore = rec.PickDensityScore,
            TravelDistanceDeltaMeters = rec.TravelDistanceDeltaMeters,
            ProductAffinityScore = 75.0m,
            UrgencyScore = rec.UrgencyScore,
            UrgencyReason = rec.UrgencyReason ?? string.Empty,
            VelocityMismatchScore = uVel,
            GoldenZoneCongestionScore = uCong,
            SeasonalDemandScore = uSeas,
            ErgonomicHazardScore = uErgo,
            GrossMonthlySavingsUsd = rec.GrossMonthlySavingsUsd,
            HandlingCostUsd = rec.HandlingCostUsd,
            MovementCostUsd = rec.MovementCostUsd,
            DisruptionCostUsd = rec.DisruptionCostUsd,
            TotalRelocationCostUsd = rec.TotalRelocationCostUsd,
            AmortizedMonthlyCostUsd = policy.AmortizationMonths > 0 ? Math.Round(rec.TotalRelocationCostUsd / policy.AmortizationMonths, 2) : 0m,
            NetMonthlyBenefitUsd = rec.EstimatedMonthlyNetSavingsUsd,
            DisruptionPenaltyScore = sDisruptPenalty,
            DisruptionMitigationScore = sMitigation,
            PriorityScore = rec.PriorityScore,
            DisruptionLevel = rec.DisruptionLevel,
            CurrentLocation = currComp,
            ProposedLocation = destComp
        };
    }

    private void ValidateFacilityAccess(Guid facilityId)
    {
        if (_currentUser.FacilityId.HasValue && _currentUser.FacilityId.Value != facilityId && !_currentUser.IsSuperAdmin)
        {
            throw new UnauthorizedAccessException($"Current user does not have access to Facility {facilityId}.");
        }
    }

    private void ValidateTenantAccess(Guid? clientId)
    {
        if (clientId.HasValue && _currentUser.TenantId.HasValue && _currentUser.TenantId.Value != clientId.Value && !_currentUser.IsStaff)
        {
            throw new UnauthorizedAccessException($"Current user does not have access to Client {clientId.Value}.");
        }
    }
}
