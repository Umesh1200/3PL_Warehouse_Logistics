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
/// Domain service implementing ABC-Based Intelligent Warehouse Slotting Optimization (Phase 6.3.7.1).
/// Enforces physical feasibility, weight limits, temperature and hazard segregation,
/// ergonomic golden zones, and relocation disruption dampening.
/// Invariant: Recommendations are advisory simulation models; physical inventory ledger (StockOnHand)
/// is never mutated until an operational WMS relocation task is executed.
/// </summary>
public class SlottingOptimizationService : ISlottingOptimizationService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<SlottingOptimizationService> _logger;

    public SlottingOptimizationService(
        IApplicationDbContext dbContext,
        ICurrentUserContext currentUser,
        ILogger<SlottingOptimizationService> logger)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<SlottingPolicyDto> CreatePolicyAsync(
        CreateSlottingPolicyRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ValidateTenantAccess(request.ClientId);
        if (request.FacilityId.HasValue)
        {
            ValidateFacilityAccess(request.FacilityId.Value);
        }

        var policy = SlottingPolicy.Create(
            name: request.Name,
            facilityId: request.FacilityId,
            clientId: request.ClientId,
            zoneId: request.ZoneId,
            primaryGoal: request.PrimaryGoal,
            secondaryGoal: request.SecondaryGoal,
            goldenZoneMinZMeters: request.GoldenZoneMinZMeters,
            goldenZoneMaxZMeters: request.GoldenZoneMaxZMeters,
            goldenZoneShelfLevels: request.GoldenZoneShelfLevels,
            targetClassAPickFace: request.TargetClassAPickFace,
            targetClassBIntermediate: request.TargetClassBIntermediate,
            targetClassCReserve: request.TargetClassCReserve,
            protectDeadStock: request.ProtectDeadStock,
            maxRelocationsPerBatch: request.MaxRelocationsPerBatch,
            minLaborSavingsUsd: request.MinLaborSavingsUsd,
            estimatedRelocationLaborMinutes: request.EstimatedRelocationLaborMinutes,
            laborHourlyRateUsd: request.LaborHourlyRateUsd,
            disruptionPenaltyWeight: request.DisruptionPenaltyWeight,
            weightTravelDistance: request.WeightTravelDistance,
            weightErgonomics: request.WeightErgonomics,
            weightPickDensity: request.WeightPickDensity,
            weightAbcAffinity: request.WeightAbcAffinity,
            weightVelocityAffinity: request.WeightVelocityAffinity,
            weightCubeFill: request.WeightCubeFill,
            weightProductAffinity: request.WeightProductAffinity,
            maxProjectionAgeDays: request.MaxProjectionAgeDays,
            version: 1,
            effectiveFromUtc: DateTime.UtcNow,
            description: request.Description);

        _dbContext.SlottingPolicies.Add(policy);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created slotting policy '{PolicyName}' ({PolicyId}) for Facility {FacilityId}",
            policy.Name, policy.Id, policy.FacilityId);

        return MapPolicyToDto(policy);
    }

    public async Task<SlottingPolicyDto?> GetPolicyAsync(
        Guid policyId,
        CancellationToken cancellationToken = default)
    {
        var policy = await _dbContext.SlottingPolicies
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == policyId, cancellationToken);

        if (policy == null) return null;
        ValidateTenantAccess(policy.ClientId);
        if (policy.FacilityId.HasValue)
        {
            ValidateFacilityAccess(policy.FacilityId.Value);
        }

        return MapPolicyToDto(policy);
    }

    public async Task<SlottingPolicyDto> GetActivePolicyForFacilityAsync(
        Guid facilityId,
        Guid? clientId = null,
        Guid? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        ValidateFacilityAccess(facilityId);
        ValidateTenantAccess(clientId);

        var query = _dbContext.SlottingPolicies
            .AsNoTracking()
            .Where(p => p.IsActive && (p.FacilityId == facilityId || p.FacilityId == null));

        if (clientId.HasValue)
        {
            query = query.Where(p => p.ClientId == clientId.Value || p.ClientId == null);
        }
        if (zoneId.HasValue)
        {
            query = query.Where(p => p.ZoneId == zoneId.Value || p.ZoneId == null);
        }

        var policies = await query.ToListAsync(cancellationToken);

        // Precedence: Specific Zone + Client + Facility > Facility + Client > Facility > Global
        var bestPolicy = policies
            .OrderByDescending(p => p.ZoneId.HasValue ? 4 : 0)
            .ThenByDescending(p => p.ClientId.HasValue ? 2 : 0)
            .ThenByDescending(p => p.FacilityId.HasValue ? 1 : 0)
            .ThenByDescending(p => p.Version)
            .FirstOrDefault();

        if (bestPolicy != null)
        {
            return MapPolicyToDto(bestPolicy);
        }

        // Return standard default policy fallback (and persist so foreign key relationships are valid)
        var defaultPolicy = SlottingPolicy.Create(
            name: "Standard Facility Slotting Policy",
            facilityId: facilityId,
            clientId: clientId,
            zoneId: zoneId);

        _dbContext.SlottingPolicies.Add(defaultPolicy);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapPolicyToDto(defaultPolicy);
    }

    public async Task<SlottingCompatibilityResultDto> EvaluateSkuLocationCompatibilityAsync(
        Guid skuId,
        Guid locationId,
        Guid? policyId = null,
        decimal? quantityToSlot = null,
        DateTime? asOfDateUtc = null,
        CancellationToken cancellationToken = default)
    {
        var item = await _dbContext.Items
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == skuId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Item), skuId);

        ValidateTenantAccess(item.ClientId);

        var location = await _dbContext.Locations
            .AsNoTracking()
            .Include(l => l.Zone)
            .FirstOrDefaultAsync(l => l.Id == locationId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Location), locationId);

        ValidateFacilityAccess(location.FacilityId);

        SlottingPolicyDto policy;
        if (policyId.HasValue)
        {
            policy = await GetPolicyAsync(policyId.Value, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(SlottingPolicy), policyId.Value);
        }
        else
        {
            policy = await GetActivePolicyForFacilityAsync(location.FacilityId, item.ClientId, location.ZoneId, cancellationToken);
        }

        // Authoritative physical occupancy calculation:
        // Physical space and structural weight are consumed by ALL physical inventory present in the location,
        // regardless of picking availability: Q_physical = QtyAvailable + QtyAllocated + QtyReserved + QtyQuarantined + QtyDamaged + QtyOnHold.
        var existingStock = await _dbContext.StockOnHand
            .AsNoTracking()
            .Include(s => s.Item)
            .Where(s => s.LocationId == locationId && (s.QtyAvailable + s.QtyAllocated + s.QtyReserved + s.QtyQuarantined + s.QtyDamaged + s.QtyOnHold) > 0)
            .ToListAsync(cancellationToken);

        decimal existingOccupiedWeightKg = existingStock.Sum(s => s.TotalQuantity * (s.Item?.WeightKg ?? 0m));
        decimal existingOccupiedVolumeCm3 = existingStock.Sum(s => s.TotalQuantity *
            ((s.Item?.VolumeCm3 > 0 ? s.Item.VolumeCm3 : (s.Item?.LengthCm ?? 0m) * (s.Item?.WidthCm ?? 0m) * (s.Item?.HeightCm ?? 0m))));

        decimal qty = quantityToSlot.HasValue && quantityToSlot.Value > 0 ? quantityToSlot.Value : 1m;
        decimal proposedWeightKg = item.WeightKg * qty;
        decimal unitVol = item.VolumeCm3 > 0 ? item.VolumeCm3 : (item.LengthCm * item.WidthCm * item.HeightCm);
        decimal proposedVolumeCm3 = unitVol * qty;

        var evaluations = new List<SlottingConstraintEvaluationDto>();
        var violatedHards = new List<string>();
        var violatedSofts = new List<string>();

        // 1. Operational Location Status (Hard)
        bool opStatusPass = location.IsActive && location.IsPickable && !location.IsDamaged && !location.IsLocked;
        string? opViolation = !location.IsActive ? "Location is inactive" :
                              !location.IsPickable ? "Location is not marked pickable" :
                              location.IsDamaged ? "Location is marked damaged" :
                              location.IsLocked ? "Location has an active operational lock" : null;

        evaluations.Add(new SlottingConstraintEvaluationDto
        {
            ConstraintType = SlottingConstraintType.OperationalLocationStatus,
            Severity = ConstraintSeverity.HardRequirement,
            IsSatisfied = opStatusPass,
            Description = "Target storage location must be active, pickable, undamaged, and unlocked.",
            ViolationDetail = opViolation,
            PenaltyScore = opStatusPass ? 0m : 100m
        });
        if (!opStatusPass && opViolation != null) violatedHards.Add(opViolation);

        // 2. Location Type Compatibility (Hard)
        bool typePass = true;
        string? typeViolation = null;
        if (location.LocationType == LocationType.DockDoor ||
            location.LocationType == LocationType.StagingLane ||
            location.LocationType == LocationType.PackingStation ||
            location.LocationType == LocationType.Facility ||
            location.LocationType == LocationType.Zone ||
            location.LocationType == LocationType.Aisle)
        {
            typePass = false;
            typeViolation = $"Location type '{location.LocationType}' is an operational non-storage area or hierarchical parent and cannot receive inventory.";
        }
        else if (location.LocationType == LocationType.Cantilever && item.LengthCm < 150m)
        {
            typePass = false;
            typeViolation = $"Cantilever storage locations are restricted to long cargo (>=150cm); SKU length is {item.LengthCm}cm.";
        }

        evaluations.Add(new SlottingConstraintEvaluationDto
        {
            ConstraintType = SlottingConstraintType.LocationTypeCompatibility,
            Severity = ConstraintSeverity.HardRequirement,
            IsSatisfied = typePass,
            Description = "Storage location type must be compatible with SKU packaging and handling profile.",
            ViolationDetail = typeViolation,
            PenaltyScore = typePass ? 0m : 100m
        });
        if (!typePass && typeViolation != null) violatedHards.Add(typeViolation);

        // 3. Client Tenancy Exclusivity (Hard)
        bool tenancyPass = true;
        string? tenancyViolation = null;
        if (_currentUser.TenantId.HasValue && item.ClientId != _currentUser.TenantId.Value && !_currentUser.IsStaff)
        {
            tenancyPass = false;
            tenancyViolation = "Tenant access violation: SKU does not belong to authorized client.";
        }

        var otherClientStock = existingStock.FirstOrDefault(s => s.ClientId != item.ClientId);
        if (otherClientStock != null)
        {
            tenancyPass = false;
            tenancyViolation = $"Location currently contains stock for client {otherClientStock.ClientId}; co-mingling separate client tenants in the same location is prohibited.";
        }

        evaluations.Add(new SlottingConstraintEvaluationDto
        {
            ConstraintType = SlottingConstraintType.ClientTenancy,
            Severity = ConstraintSeverity.HardRequirement,
            IsSatisfied = tenancyPass,
            Description = "Tenant boundary isolation must be strictly preserved; client co-mingling in a single storage location is prohibited.",
            ViolationDetail = tenancyViolation,
            PenaltyScore = tenancyPass ? 0m : 100m
        });
        if (!tenancyPass && tenancyViolation != null) violatedHards.Add(tenancyViolation);

        // 4. Physical Dimensions Constraint (Hard)
        bool dimPass = true;
        string? dimViolation = null;
        if (location.LengthCm.HasValue && location.WidthCm.HasValue && location.HeightCm.HasValue)
        {
            // Allow orientational rotation (L x W or W x L fit)
            bool orientFit = (item.LengthCm <= location.LengthCm.Value && item.WidthCm <= location.WidthCm.Value) ||
                             (item.LengthCm <= location.WidthCm.Value && item.WidthCm <= location.LengthCm.Value);
            bool heightFit = item.HeightCm <= location.HeightCm.Value;

            if (!orientFit || !heightFit)
            {
                dimPass = false;
                dimViolation = $"Item dimensions ({item.LengthCm}x{item.WidthCm}x{item.HeightCm} cm) exceed location boundaries ({location.LengthCm}x{location.WidthCm}x{location.HeightCm} cm).";
            }
        }
        evaluations.Add(new SlottingConstraintEvaluationDto
        {
            ConstraintType = SlottingConstraintType.PhysicalDimensions,
            Severity = ConstraintSeverity.HardRequirement,
            IsSatisfied = dimPass,
            Description = "Item unit physical dimensions must fit inside location dimensions.",
            ViolationDetail = dimViolation,
            PenaltyScore = dimPass ? 0m : 100m
        });
        if (!dimPass && dimViolation != null) violatedHards.Add(dimViolation);

        // 5. Volumetric Capacity Constraint factoring in Existing Occupancy (Hard)
        bool volPass = true;
        string? volViolation = null;
        decimal locVol = location.CalculateVolumeCm3();
        decimal totalVolumeCm3 = existingOccupiedVolumeCm3 + proposedVolumeCm3;
        if (locVol > 0 && totalVolumeCm3 > locVol)
        {
            volPass = false;
            volViolation = $"Proposed volume ({proposedVolumeCm3:N0} cm³ for qty {qty}) plus existing occupancy ({existingOccupiedVolumeCm3:N0} cm³) total {totalVolumeCm3:N0} cm³, exceeding location capacity ({locVol:N0} cm³).";
        }
        evaluations.Add(new SlottingConstraintEvaluationDto
        {
            ConstraintType = SlottingConstraintType.VolumetricCapacity,
            Severity = ConstraintSeverity.HardRequirement,
            IsSatisfied = volPass,
            Description = "Total volume (existing occupancy + proposed quantity) must not exceed location usable volume.",
            ViolationDetail = volViolation,
            PenaltyScore = volPass ? 0m : 100m
        });
        if (!volPass && volViolation != null) violatedHards.Add(volViolation);

        // 6. Weight Capacity Constraint factoring in Existing Occupancy (Hard)
        bool weightPass = true;
        string? weightViolation = null;
        if (location.MaxWeightKg.HasValue && location.MaxWeightKg.Value > 0)
        {
            decimal totalWeightKg = existingOccupiedWeightKg + proposedWeightKg;
            if (totalWeightKg > location.MaxWeightKg.Value)
            {
                weightPass = false;
                weightViolation = $"Proposed weight ({proposedWeightKg:N1} kg for qty {qty}) plus existing occupancy ({existingOccupiedWeightKg:N1} kg) total {totalWeightKg:N1} kg, which exceeds location maximum weight limit ({location.MaxWeightKg.Value:N1} kg).";
            }
        }
        evaluations.Add(new SlottingConstraintEvaluationDto
        {
            ConstraintType = SlottingConstraintType.WeightCapacity,
            Severity = ConstraintSeverity.HardRequirement,
            IsSatisfied = weightPass,
            Description = "Total weight (existing occupancy + proposed quantity) must not exceed location maximum structural load capacity.",
            ViolationDetail = weightViolation,
            PenaltyScore = weightPass ? 0m : 100m
        });
        if (!weightPass && weightViolation != null) violatedHards.Add(weightViolation);

        // 7. Temperature Class Compatibility (Hard)
        bool tempPass = true;
        string? tempViolation = null;
        if (location.Zone != null)
        {
            if (item.StorageTempClass == StorageTempClass.Frozen && (location.Zone.TempMaxC == null || location.Zone.TempMaxC > -15m))
            {
                tempPass = false;
                tempViolation = $"Frozen item requires sub-zero zone; zone '{location.Zone.ZoneCode}' max temp is {location.Zone.TempMaxC}°C.";
            }
            else if (item.StorageTempClass == StorageTempClass.Refrigerated && (location.Zone.TempMaxC == null || location.Zone.TempMaxC > 8m || location.Zone.TempMinC < 0m))
            {
                tempPass = false;
                tempViolation = $"Refrigerated item requires 2°C - 8°C cold zone; zone '{location.Zone.ZoneCode}' is not certified refrigerated.";
            }
        }
        evaluations.Add(new SlottingConstraintEvaluationDto
        {
            ConstraintType = SlottingConstraintType.TemperatureClass,
            Severity = ConstraintSeverity.HardRequirement,
            IsSatisfied = tempPass,
            Description = "Product storage temperature requirements must match zone temperature capabilities.",
            ViolationDetail = tempViolation,
            PenaltyScore = tempPass ? 0m : 100m
        });
        if (!tempPass && tempViolation != null) violatedHards.Add(tempViolation);

        // 8. Hazard Segregation (Hard)
        bool hazPass = true;
        string? hazViolation = null;
        if (!string.IsNullOrWhiteSpace(item.HazardClass))
        {
            // Hazardous materials must be in dedicated hazard or bulk zones
            if (location.Zone?.ZoneType == WarehouseZoneType.PickFace || location.Zone?.ZoneType == WarehouseZoneType.Staging)
            {
                hazPass = false;
                hazViolation = $"Hazardous item ({item.HazardClass}) cannot be placed in active general pick face zone '{location.Zone.ZoneCode}'.";
            }
        }
        evaluations.Add(new SlottingConstraintEvaluationDto
        {
            ConstraintType = SlottingConstraintType.HazardSegregation,
            Severity = ConstraintSeverity.HardRequirement,
            IsSatisfied = hazPass,
            Description = "Hazardous and flammable items must satisfy safety segregation rules.",
            ViolationDetail = hazViolation,
            PenaltyScore = hazPass ? 0m : 100m
        });
        if (!hazPass && hazViolation != null) violatedHards.Add(hazViolation);

        // 9. Stackability & Planar Footprint Constraint (Hard)
        bool stackPass = true;
        string? stackViolation = null;

        int stackFactor = Math.Max(1, item.StackabilityFactor);
        decimal locL = location.LengthCm ?? 120m;
        decimal locW = location.WidthCm ?? 80m;
        decimal locH = location.HeightCm ?? 150m;
        decimal itemL = item.LengthCm > 0 ? item.LengthCm : 30m;
        decimal itemW = item.WidthCm > 0 ? item.WidthCm : 20m;
        decimal itemH = item.HeightCm > 0 ? item.HeightCm : 20m;

        // Planar units per tier considering both orthogonal orientations
        int tierNormal = (int)(locL / itemL) * (int)(locW / itemW);
        int tierRotated = item.AllowPlanarRotation ? (int)(locL / itemW) * (int)(locW / itemL) : 0;
        int uTier = Math.Max(tierNormal, tierRotated);

        // Vertical tiers based on crush resistance and vertical clearance
        int maxVerticalTiers;
        if (stackFactor <= 1)
        {
            // Crush-sensitive / non-stackable: maximum vertical tier is strictly 1
            maxVerticalTiers = 1;
        }
        else
        {
            int verticalClearanceTiers = (int)(locH / itemH);
            maxVerticalTiers = Math.Min(stackFactor, verticalClearanceTiers);
        }

        int qMax = uTier * Math.Max(1, maxVerticalTiers);

        // Strict physical vertical clearance check: item height cannot exceed location vertical clearance
        if (itemH > locH)
        {
            stackPass = false;
            stackViolation = $"Item height ({itemH} cm) exceeds location vertical clearance ({locH} cm).";
        }
        else if (uTier <= 0)
        {
            stackPass = false;
            stackViolation = $"Item dimensions ({itemL}x{itemW} cm) exceed location planar boundaries ({locL}x{locW} cm) in all permitted orientations.";
        }
        else if (maxVerticalTiers <= 0)
        {
            stackPass = false;
            stackViolation = $"Location vertical clearance ({locH} cm) is insufficient for item height ({itemH} cm).";
        }

        bool existingHasCrushSensitive = existingStock.Any(s => s.Item != null && s.Item.StackabilityFactor <= 1);
        if (stackPass)
        {
            if (stackFactor <= 1 && existingStock.Count > 0)
            {
                stackPass = false;
                stackViolation = $"SKU is crush-sensitive (StackabilityFactor=1) and cannot be co-located with existing stock ({existingStock.Count} items present).";
            }
            else if (existingHasCrushSensitive)
            {
                stackPass = false;
                stackViolation = "Location contains crush-sensitive item (StackabilityFactor=1); co-locating additional inventory is prohibited.";
            }
        }

        if (stackPass)
        {
            // Pack existing inventory into physical placements
            var existingPlacements = new List<VirtualPlacement>();
            bool existingPackFailed = false;

            foreach (var grp in existingStock.GroupBy(s => s.ItemId))
            {
                var eItem = grp.First().Item;
                if (eItem == null) continue;
                decimal eQty = grp.Sum(s => s.TotalQuantity);
                if (!GeometricOccupancyEngine.TryPackItem(locL, locW, locH, existingPlacements, eItem, eQty, out var ePlacements))
                {
                    existingPackFailed = true;
                    break;
                }
                foreach (var ep in ePlacements) ep.IsExistingPhysicalStock = true;
                existingPlacements.AddRange(ePlacements);
            }

            if (existingPackFailed)
            {
                stackPass = false;
                stackViolation = "Existing inventory in location exceeds geometric floor packing capacity.";
            }
            else if (!GeometricOccupancyEngine.TryPackItem(locL, locW, locH, existingPlacements, item, qty, out var candidatePlacements))
            {
                stackPass = false;
                if (stackFactor <= 1)
                {
                    stackViolation = $"Proposed quantity ({qty}) exceeds maximum single-tier planar footprint capacity ({uTier} units; UTier={uTier}, TMax=1) for crush-sensitive SKU.";
                }
                else if (existingStock.Count == 0)
                {
                    stackViolation = $"Proposed quantity ({qty}) exceeds maximum allowable stacking capacity of {qMax} units (UTier={uTier}, MaxTiers={maxVerticalTiers}, StackabilityFactor={stackFactor}).";
                }
                else
                {
                    stackViolation = $"Proposed quantity ({qty}) cannot be geometrically packed into remaining 2D floor coordinates without overlapping existing inventory or exceeding boundaries, exceeding maximum planar footprint capacity.";
                }
            }
        }

        evaluations.Add(new SlottingConstraintEvaluationDto
        {
            ConstraintType = SlottingConstraintType.Stackability,
            Severity = ConstraintSeverity.HardRequirement,
            IsSatisfied = stackPass,
            Description = "Storage tiering must respect product crush resistance and stackability factor limits.",
            ViolationDetail = stackViolation,
            PenaltyScore = stackPass ? 0m : 100m
        });
        if (!stackPass && stackViolation != null) violatedHards.Add(stackViolation);

        // 10. Soft Preferences: Velocity, ABC Tier, Pick Density & Ergonomics
        DateTime evalTime = asOfDateUtc?.ToUniversalTime() ?? DateTime.UtcNow;
        var profile = await GetSkuVelocityProfileAsync(location.FacilityId, item.Id, item, policy, evalTime, cancellationToken);

        decimal ergonomicScore = CalculateErgonomicScore(location, policy, profile.abcClass, profile.trend);
        if ((profile.abcClass == AbcClassificationCategory.ClassA || profile.trend == "Surging") && ergonomicScore < 50m)
        {
            violatedSofts.Add($"High-velocity Class A SKU is placed outside ergonomic golden zone (Z={location.ZCoord?.ToString("F1") ?? "?"}m, Lvl={location.ShelfLevel?.ToString() ?? "?"}).");
        }
        else if ((profile.abcClass == AbcClassificationCategory.ClassC || profile.trend == "Inactive" || profile.trend == "DeadStock") && ergonomicScore < 50m)
        {
            violatedSofts.Add("Slow-moving Class C SKU occupies a high-value ergonomic golden zone pick location.");
        }

        decimal abcScore = CalculateAbcAffinityScore(location, profile.abcClass);
        decimal velScore = CalculateVelocityAffinityScore(location, profile.velocityCat, profile.trend);
        decimal densityScore = CalculatePickDensityScore(location, profile.pickFreqPerDay, profile.totalPicks);

        // Query primary current location of SKU to determine relocation travel distance delta (Option B)
        var primaryStock = await _dbContext.StockOnHand.AsNoTracking()
            .Include(s => s.Location)
            .FirstOrDefaultAsync(s => s.ItemId == skuId && s.FacilityId == location.FacilityId, cancellationToken);
        var currentLoc = primaryStock?.Location;

        decimal candDist = CalculateDistanceToDockMeters(location);
        decimal travelScore;
        if (currentLoc != null)
        {
            decimal currDist = CalculateDistanceToDockMeters(currentLoc);
            decimal deltaD = currDist - candDist;
            travelScore = Math.Round(Math.Clamp(50.0m + (deltaD * 2.0m), 10.0m, 100.0m), 1);
        }
        else
        {
            travelScore = Math.Round(Math.Clamp(100.0m - candDist, 10.0m, 100.0m), 1);
        }

        decimal coLocationAffinityScore = 50.0m;
        if (policy.WeightProductAffinity > 0m)
        {
            coLocationAffinityScore = await CalculateCoLocationAffinityScoreAsync(
                item.Id, item.ClientId, location, cancellationToken);
        }

        // Overall Feasibility & Suitability Calculation
        bool isPhysicallyFeasible = dimPass && volPass && weightPass && tempPass && hazPass && stackPass;
        bool isOperationallyFeasible = opStatusPass && tenancyPass && typePass;

        decimal totalScore = 0m;
        if (isPhysicallyFeasible && isOperationallyFeasible)
        {
            totalScore = CalculateCompositeSuitabilityScore(
                policy, ergonomicScore, abcScore, velScore, densityScore, travelScore, coLocationAffinityScore);
        }

        string? priorityExplanation = null;
        if (profile.isStaleOrMissing && profile.classificationSource.Contains("Stale"))
        {
            priorityExplanation = $"Authoritative velocity projection is stale (calculated {profile.projectionAgeDays} days ago, exceeding {policy.MaxProjectionAgeDays}-day threshold); fell back to catalog Item.AbcVelocity.";
        }
        else if (profile.isStaleOrMissing && profile.classificationSource.Contains("FutureDated"))
        {
            priorityExplanation = $"Authoritative velocity projection has future-dated timestamp ({profile.projectionCalculatedAtUtc:O}), indicating clock skew anomaly; fell back to catalog Item.AbcVelocity.";
        }
        else if (profile.abcClass == AbcClassificationCategory.ClassA && profile.trend == "Declining")
        {
            priorityExplanation = "Priority conflict resolved: SKU has Class A monetary valuation but Declining demand trend. Placement favors intermediate storage to free prime golden zone pick faces.";
        }
        else if (profile.abcClass == AbcClassificationCategory.ClassC && profile.trend == "Surging")
        {
            priorityExplanation = "Priority conflict resolved: SKU has Class C monetary valuation but Surging pick demand. Placement favors forward pick face to reduce manual travel labor.";
        }

        return new SlottingCompatibilityResultDto
        {
            SkuId = item.Id,
            Sku = item.Sku,
            LocationId = location.Id,
            LocationCode = location.LocationCode,
            IsPhysicallyFeasible = isPhysicallyFeasible,
            IsOperationallyFeasible = isOperationallyFeasible,
            CalculatedSuitabilityScore = totalScore,
            ErgonomicScore = ergonomicScore,
            AbcAffinityScore = abcScore,
            VelocityAffinityScore = velScore,
            PickDensityScore = densityScore,
            TravelDistanceSavingsScore = travelScore,
            CoLocationAffinityScore = coLocationAffinityScore,
            EffectiveAbcClass = profile.abcLabel,
            EffectiveVelocityCategory = profile.velocityCat.ToString(),
            VelocityTrend = profile.trend,
            IsProjectionStaleOrMissing = profile.isStaleOrMissing,
            ClassificationSource = profile.classificationSource,
            ProjectionCalculatedAtUtc = profile.projectionCalculatedAtUtc,
            ProjectionAgeDays = profile.projectionAgeDays,
            PriorityResolutionExplanation = priorityExplanation,
            ConstraintEvaluations = evaluations,
            ViolatedHardConstraints = violatedHards,
            ViolatedSoftPreferences = violatedSofts
        };
    }

    public async Task<SlottingSuitabilityMatrixDto> GenerateSuitabilityMatrixAsync(
        GenerateSuitabilityMatrixRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ValidateFacilityAccess(request.FacilityId);
        ValidateTenantAccess(request.ClientId);

        SlottingPolicyDto policy;
        if (request.PolicyId.HasValue)
        {
            policy = await GetPolicyAsync(request.PolicyId.Value, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(SlottingPolicy), request.PolicyId.Value);
        }
        else
        {
            policy = await GetActivePolicyForFacilityAsync(request.FacilityId, request.ClientId, null, cancellationToken);
        }

        var itemQuery = _dbContext.Items.AsNoTracking();
        if (request.ClientId.HasValue)
        {
            itemQuery = itemQuery.Where(i => i.ClientId == request.ClientId.Value);
        }
        if (request.ItemIds != null && request.ItemIds.Count > 0)
        {
            itemQuery = itemQuery.Where(i => request.ItemIds.Contains(i.Id));
        }
        var items = await itemQuery.ToListAsync(cancellationToken);

        var locQuery = _dbContext.Locations.AsNoTracking()
            .Include(l => l.Zone)
            .Where(l => l.FacilityId == request.FacilityId && l.IsActive && l.IsPickable && !l.IsDamaged && !l.IsLocked);
        if (request.LocationIds != null && request.LocationIds.Count > 0)
        {
            locQuery = locQuery.Where(l => request.LocationIds.Contains(l.Id));
        }
        var locations = await locQuery.ToListAsync(cancellationToken);

        var itemIds = items.Select(i => i.Id).ToList();
        var stocks = await _dbContext.StockOnHand.AsNoTracking()
            .Include(s => s.Location)
            .Where(s => s.FacilityId == request.FacilityId && itemIds.Contains(s.ItemId))
            .ToListAsync(cancellationToken);

        var skuProfiles = new List<SkuSuitabilityProfileDto>();

        DateTime evalTime = request.AsOfDateUtc?.ToUniversalTime() ?? DateTime.UtcNow;

        foreach (var item in items)
        {
            var profile = await GetSkuVelocityProfileAsync(request.FacilityId, item.Id, item, policy, evalTime, cancellationToken);
            var primaryStock = stocks.FirstOrDefault(s => s.ItemId == item.Id);
            var currLoc = primaryStock?.Location;

            var candidateScores = new List<SkuLocationSuitabilityScoreDto>();

            foreach (var loc in locations)
            {
                var compat = await EvaluateSkuLocationCompatibilityAsync(item.Id, loc.Id, policy.Id, null, evalTime, cancellationToken);

                decimal currentDist = currLoc != null ? CalculateDistanceToDockMeters(currLoc) : 30m;
                decimal candDist = CalculateDistanceToDockMeters(loc);
                decimal deltaDist = Math.Max(0m, (currentDist - candDist) * 2m);

                decimal monthlyPicks = profile.totalPicks > 0 ? (decimal)profile.totalPicks : (profile.pickFreqPerDay > 0 ? profile.pickFreqPerDay * 30m : 30m);
                var econ = CalculateEconomicSavings(
                    roundTripTravelSavedMeters: deltaDist,
                    monthlyPicks: monthlyPicks,
                    walkingSpeedMps: 1.0m,
                    laborHourlyRateUsd: policy.LaborHourlyRateUsd > 0 ? policy.LaborHourlyRateUsd : 25m,
                    relocationLaborMinutes: policy.EstimatedRelocationLaborMinutes > 0 ? policy.EstimatedRelocationLaborMinutes : 6m,
                    amortizationMonths: 3m,
                    minLaborSavingsUsd: policy.MinLaborSavingsUsd);

                candidateScores.Add(new SkuLocationSuitabilityScoreDto
                {
                    LocationId = loc.Id,
                    LocationCode = loc.LocationCode,
                    ZoneCode = loc.Zone?.ZoneCode,
                    CompositeSuitabilityScore = compat.CalculatedSuitabilityScore,
                    ErgonomicScore = compat.ErgonomicScore,
                    AbcAffinityScore = compat.AbcAffinityScore,
                    VelocityAffinityScore = compat.VelocityAffinityScore,
                    PickDensityScore = compat.PickDensityScore,
                    TravelDistanceSavingsScore = compat.TravelDistanceSavingsScore,
                    CoLocationAffinityScore = compat.CoLocationAffinityScore,
                    IsPhysicallyFeasible = compat.IsPhysicallyFeasible,
                    IsOperationallyFeasible = compat.IsOperationallyFeasible,
                    IsOverallCompatible = compat.IsOverallCompatible,
                    EstimatedRoundTripTravelSavedMeters = deltaDist,
                    EstimatedMonthlyNetSavingsUsd = econ.netMonthlySavingsUsd,
                    ViolatedHardConstraints = compat.ViolatedHardConstraints,
                    ViolatedSoftPreferences = compat.ViolatedSoftPreferences
                });
            }

            skuProfiles.Add(new SkuSuitabilityProfileDto
            {
                ItemId = item.Id,
                Sku = item.Sku,
                Description = item.Description ?? string.Empty,
                CurrentLocationId = currLoc?.Id ?? Guid.Empty,
                CurrentLocationCode = currLoc?.LocationCode ?? "UNASSIGNED",
                AbcClass = profile.abcClass,
                VelocityCategory = profile.velocityCat,
                VelocityTrend = profile.trend,
                DailyDemandVelocity = profile.dailyVelocity,
                PickFrequencyPerDay = profile.pickFreqPerDay,
                IsProjectionStaleOrMissing = profile.isStaleOrMissing,
                ClassificationSource = profile.classificationSource,
                ProjectionCalculatedAtUtc = profile.projectionCalculatedAtUtc,
                ProjectionAgeDays = profile.projectionAgeDays,
                CandidateLocations = candidateScores.OrderByDescending(c => c.CompositeSuitabilityScore).ToList()
            });
        }

        return new SlottingSuitabilityMatrixDto
        {
            FacilityId = request.FacilityId,
            PolicyId = policy.Id,
            PolicyVersion = policy.Version,
            EvaluatedAtUtc = evalTime,
            SkuProfiles = skuProfiles
        };
    }

    public async Task<SlottingRecommendationBatchDto> SimulateSlottingRecommendationsAsync(
        SlottingSimulationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ValidateFacilityAccess(request.FacilityId);
        ValidateTenantAccess(request.ClientId);

        SlottingPolicyDto policy;
        if (request.PolicyId.HasValue)
        {
            policy = await GetPolicyAsync(request.PolicyId.Value, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(SlottingPolicy), request.PolicyId.Value);
        }
        else
        {
            policy = await GetActivePolicyForFacilityAsync(request.FacilityId, request.ClientId, null, cancellationToken);
        }

        DateTime evalTime = request.AsOfDateUtc?.ToUniversalTime() ?? DateTime.UtcNow;
        var asOf = evalTime.Date;
        int maxMoves = request.MaxMoves.HasValue && request.MaxMoves.Value > 0
            ? Math.Min(request.MaxMoves.Value, policy.MaxRelocationsPerBatch)
            : policy.MaxRelocationsPerBatch;

        // Query active inventory across all 6 partitions
        var stockQuery = _dbContext.StockOnHand
            .AsNoTracking()
            .Include(s => s.Item)
            .Include(s => s.Location)
            .ThenInclude(l => l.Zone)
            .Where(s => s.FacilityId == request.FacilityId && (s.QtyAvailable + s.QtyAllocated + s.QtyReserved + s.QtyQuarantined + s.QtyDamaged + s.QtyOnHold) > 0);

        if (request.ClientId.HasValue)
        {
            stockQuery = stockQuery.Where(s => s.ClientId == request.ClientId.Value);
        }

        var currentStocks = await stockQuery.ToListAsync(cancellationToken);

        // Pre-query all candidate locations in facility
        var candidateLocations = await _dbContext.Locations
            .AsNoTracking()
            .Include(l => l.Zone)
            .Where(l => l.FacilityId == request.FacilityId && l.IsActive && l.IsPickable && !l.IsDamaged && !l.IsLocked)
            .ToListAsync(cancellationToken);

        // Pre-query existing occupancy (weight & volume) across all locations summing all 6 partitions
        var existingWeightByLoc = currentStocks
            .GroupBy(s => s.LocationId)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.TotalQuantity * (s.Item?.WeightKg ?? 0m)));

        var existingVolumeByLoc = currentStocks
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

        string batchNumber = $"SLOT-{asOf:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

        var batch = SlottingRecommendationBatch.Create(
            facilityId: request.FacilityId,
            policyId: policy.Id,
            batchNumber: batchNumber,
            clientId: request.ClientId,
            snapshotDateUtc: asOf,
            notes: request.Notes,
            policyVersion: policy.Version,
            policySnapshotJson: JsonSerializer.Serialize(policy));

        decimal hourlyLaborRate = policy.LaborHourlyRateUsd > 0 ? policy.LaborHourlyRateUsd : 25.00m;
        decimal moveLaborMinutes = policy.EstimatedRelocationLaborMinutes > 0 ? policy.EstimatedRelocationLaborMinutes : 6.00m;

        // Collect all individually feasible candidate relocation proposals into candidate pool
        var candidatePool = new List<(
            Item Item,
            decimal Quantity,
            Location ProposedLocation,
            decimal ProposedWeightKg,
            decimal ProposedVolumeCm3,
            decimal SuitabilityScore,
            decimal EstimatedMonthlyNetSavingsUsd,
            string ItemSku,
            string ProposedLocationCode,
            SlottingRecommendation Recommendation
        )>();

        var itemIds = currentStocks.Select(s => s.ItemId).Distinct().ToList();
        var agingSnapshots = await _dbContext.DailyInventoryAgingSnapshots
            .AsNoTracking()
            .Where(s => s.FacilityId == request.FacilityId && itemIds.Contains(s.ItemId))
            .ToListAsync(cancellationToken);

        var latestAgingByItem = agingSnapshots
            .GroupBy(s => s.ItemId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.SnapshotDateUtc).First());

        var itemGroups = currentStocks.GroupBy(s => s.Item).OrderBy(g => g.Key.Sku);

        foreach (var grp in itemGroups)
        {
            var item = grp.Key;
            var primaryStock = grp.First();
            var currLoc = primaryStock.Location;
            if (currLoc == null) continue;

            latestAgingByItem.TryGetValue(item.Id, out var aging);
            bool isDead = await ResolveDeadStockStatusAsync(request.FacilityId, item.Id, item, aging, evalTime, cancellationToken);

            velocityByItem.TryGetValue(item.Id, out var recs);
            var profile = recs != null && recs.Count > 0
                ? GetProfileFromRecords(item, recs, policy, evalTime, isDead)
                : GetFallbackProfile(item, "CatalogFallback (MissingProjection)", null, null, isDead);

            bool currInGolden = IsInGoldenZone(currLoc, policy);

            // Safeguard: Dead stock protection rule
            if (profile.isDeadStock && policy.ProtectDeadStockFromUnnecessaryRelocation && !currInGolden)
            {
                // Dead stock already in reserve/bulk; strictly protect from unnecessary relocation
                continue;
            }

            // Target candidate location selection based on ABC and velocity affinity
            IEnumerable<Location> targetCandidates;

            if ((profile.abcClass == AbcClassificationCategory.ClassA || profile.trend == "Surging") && !currInGolden && policy.TargetClassAPickFace)
            {
                // Promote Class A / Surging to Golden Zone or prime pick face
                targetCandidates = candidateLocations.Where(l => IsInGoldenZone(l, policy) || l.Zone?.ZoneType == WarehouseZoneType.PickFace);
            }
            else if (profile.trend == "Declining" && currInGolden)
            {
                // Demote Declining Class A from Golden Zone to intermediate rack (freeing golden zone)
                targetCandidates = candidateLocations.Where(l => !IsInGoldenZone(l, policy) && (l.ShelfLevel == 2 || l.ShelfLevel == 3 || l.Zone?.ZoneType != WarehouseZoneType.PickFace));
            }
            else if (profile.trend == "SeasonalSpike")
            {
                // Temporary seasonal spike buffer/forward pick
                targetCandidates = candidateLocations.Where(l => l.Zone?.ZoneType == WarehouseZoneType.PickFace || IsInGoldenZone(l, policy));
            }
            else if ((profile.abcClass == AbcClassificationCategory.ClassC || profile.trend == "Inactive" || profile.trend == "DeadStock") && currInGolden && policy.TargetClassCReserve)
            {
                // Demote slow mover occupying golden zone to upper reserve / bulk
                targetCandidates = candidateLocations.Where(l => !IsInGoldenZone(l, policy) && (l.ShelfLevel >= 3 || l.Zone?.ZoneType == WarehouseZoneType.BulkStorage));
            }
            else if (profile.abcClass == AbcClassificationCategory.ClassB && !currInGolden && (currLoc.ShelfLevel >= 4 || currLoc.Zone?.ZoneType == WarehouseZoneType.BulkStorage))
            {
                // Class B intermediate placement
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

                // 1. Dimensions check (allowing orthogonal L x W or W x L planar rotation)
                if (cand.LengthCm.HasValue && cand.WidthCm.HasValue)
                {
                    bool orientFit = (item.LengthCm <= cand.LengthCm.Value && item.WidthCm <= cand.WidthCm.Value) ||
                                     (item.LengthCm <= cand.WidthCm.Value && item.WidthCm <= cand.LengthCm.Value);
                    if (!orientFit) continue;
                }
                else
                {
                    if (cand.LengthCm.HasValue && item.LengthCm > cand.LengthCm.Value) continue;
                    if (cand.WidthCm.HasValue && item.WidthCm > cand.WidthCm.Value) continue;
                }
                if (cand.HeightCm.HasValue && item.HeightCm > cand.HeightCm.Value) continue;

                // 2. Operational status & type checks
                if (!cand.IsActive || !cand.IsPickable || cand.IsDamaged || cand.IsLocked) continue;
                if (cand.LocationType == LocationType.Cantilever && item.LengthCm < 150m) continue;
                if (cand.LocationType == LocationType.DockDoor || cand.LocationType == LocationType.StagingLane) continue;

                // 3. Temperature & hazard compatibility
                if (cand.Zone != null && item.StorageTempClass == StorageTempClass.Frozen && cand.Zone.ZoneType != WarehouseZoneType.ColdStorage) continue;
                if (!string.IsNullOrWhiteSpace(item.HazardClass) && (cand.Zone?.ZoneType == WarehouseZoneType.PickFace || cand.Zone?.ZoneType == WarehouseZoneType.Staging)) continue;

                // 4. Standalone destination physical limits
                decimal candMaxW = cand.MaxWeightKg ?? decimal.MaxValue;
                decimal candMaxV = (cand.LengthCm ?? 100m) * (cand.WidthCm ?? 100m) * (cand.HeightCm ?? 100m) * 0.85m;
                decimal existW = existingWeightByLoc.TryGetValue(cand.Id, out var ew) ? ew : 0m;
                decimal existV = existingVolumeByLoc.TryGetValue(cand.Id, out var ev) ? ev : 0m;

                if (existW + proposedWeightKg > candMaxW) continue;
                if (existV + proposedVolumeCm3 > candMaxV) continue;

                // Check standalone planar footprint capacity
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
                if (cCandFootprint > 1.000001m) continue;

                // 5. Travel distance & economic model
                decimal roundTripTravelSavedMeters;
                decimal netMonthlyGain;
                bool meetsEcon;

                if (currInGolden && (profile.abcClass == AbcClassificationCategory.ClassC || profile.trend == "Declining"))
                {
                    // Freeing up high-value pick face for fast movers provides opportunity gain
                    roundTripTravelSavedMeters = 0.0m;
                    netMonthlyGain = 8.50m;
                    meetsEcon = netMonthlyGain >= policy.MinLaborSavingsUsd;
                }
                else
                {
                    decimal currDist = CalculateDistanceToDockMeters(currLoc);
                    decimal candDist = CalculateDistanceToDockMeters(cand);
                    roundTripTravelSavedMeters = Math.Max(0m, (currDist - candDist) * 2.0m);
                    if (roundTripTravelSavedMeters == 0m) roundTripTravelSavedMeters = 15.0m; // Default golden zone elevation gain

                    decimal monthlyPicks = profile.totalPicks > 0 ? (decimal)profile.totalPicks : (profile.pickFreqPerDay > 0 ? profile.pickFreqPerDay * 30m : 120m);
                    var econ = CalculateEconomicSavings(
                        roundTripTravelSavedMeters: roundTripTravelSavedMeters,
                        monthlyPicks: monthlyPicks,
                        walkingSpeedMps: 1.0m,
                        laborHourlyRateUsd: hourlyLaborRate,
                        relocationLaborMinutes: moveLaborMinutes,
                        amortizationMonths: 3.0m,
                        minLaborSavingsUsd: policy.MinLaborSavingsUsd);

                    netMonthlyGain = econ.netMonthlySavingsUsd;
                    meetsEcon = econ.meetsThreshold;
                }

                if (!meetsEcon) continue;

                // 6. Affinity & Suitability Scores
                decimal ergoScore = CalculateErgonomicScore(cand, policy, profile.abcClass, profile.trend);
                decimal abcScore = CalculateAbcAffinityScore(cand, profile.abcClass);
                decimal velScore = CalculateVelocityAffinityScore(cand, profile.velocityCat, profile.trend);
                decimal densityScore = CalculatePickDensityScore(cand, profile.pickFreqPerDay, profile.totalPicks);
                decimal cDist = CalculateDistanceToDockMeters(currLoc);
                decimal cdDist = CalculateDistanceToDockMeters(cand);
                decimal deltaD = cDist - cdDist;
                decimal travelScore = Math.Round(Math.Clamp(50.0m + (deltaD * 2.0m), 10.0m, 100.0m), 1);

                decimal coLocationScore = 50.0m;
                if (policy.WeightProductAffinity > 0m)
                {
                    coLocationScore = await CalculateCoLocationAffinityScoreAsync(
                        item.Id, item.ClientId, cand, cancellationToken);
                }

                decimal suitabilityScore = CalculateCompositeSuitabilityScore(policy, ergoScore, abcScore, velScore, densityScore, travelScore, coLocationScore);

                // Determine explainable Reason Code
                string reasonCode = profile.trend switch
                {
                    "Surging" when profile.abcClass == AbcClassificationCategory.ClassC => "PromoteSurgingClassCToPickFaceForLaborReduction",
                    "Surging" => "PromoteFastMoverToGoldenZonePickFace",
                    "Declining" => "RebalanceDecliningClassAFromGoldenZoneToIntermediate",
                    "SeasonalSpike" => "StageSeasonalSpikeItemInBufferZone",
                    _ when profile.abcClass == AbcClassificationCategory.ClassA => "PromoteFastMoverToGoldenZonePickFace",
                    _ when profile.abcClass == AbcClassificationCategory.ClassC => "DemoteSlowMoverToFreeGoldenZonePickFace",
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
                    SkuVelocity = profile.vLabel,
                    AbcClass = profile.abcLabel,
                    VelocityTrend = profile.trend,
                    ReasonCode = reasonCode,
                    SuitabilityScore = suitabilityScore,
                    ErgonomicScore = ergoScore,
                    AbcAffinityScore = abcScore,
                    VelocityAffinityScore = velScore,
                    PickDensityScore = densityScore,
                    TravelDistanceDeltaMeters = roundTripTravelSavedMeters,
                    EstimatedMoveLaborMinutes = moveLaborMinutes,
                    EstimatedMonthlyNetSavingsUsd = Math.Round(netMonthlyGain, 2),
                    GrossMonthlySavingsUsd = Math.Round(netMonthlyGain + (moveLaborMinutes / 60.0m * hourlyLaborRate / 3.0m), 2),
                    HandlingCostUsd = Math.Round(qtyToMove * (policy.HandlingCostPerUnitUsd > 0 ? policy.HandlingCostPerUnitUsd : 0.10m) + Math.Max(0m, (item.WeightKg - 20.0m) / 10.0m) * (policy.HeavyHandlingSurchargePerTierUsd > 0 ? policy.HeavyHandlingSurchargePerTierUsd : 5.00m), 2),
                    MovementCostUsd = Math.Round((moveLaborMinutes / 60.0m) * hourlyLaborRate, 2),
                    DisruptionCostUsd = Math.Round((policy.BaseDisruptionCostUsd > 0 ? policy.BaseDisruptionCostUsd : 15.00m) * (cand.Zone?.ZoneType == WarehouseZoneType.PickFace || cand.ShelfLevel == 1 || cand.ShelfLevel == 2 ? 1.5m : 1.0m), 2),
                    TotalRelocationCostUsd = Math.Round(
                        (qtyToMove * (policy.HandlingCostPerUnitUsd > 0 ? policy.HandlingCostPerUnitUsd : 0.10m) + Math.Max(0m, (item.WeightKg - 20.0m) / 10.0m) * (policy.HeavyHandlingSurchargePerTierUsd > 0 ? policy.HeavyHandlingSurchargePerTierUsd : 5.00m)) +
                        ((moveLaborMinutes / 60.0m) * hourlyLaborRate) +
                        ((policy.BaseDisruptionCostUsd > 0 ? policy.BaseDisruptionCostUsd : 15.00m) * (cand.Zone?.ZoneType == WarehouseZoneType.PickFace || cand.ShelfLevel == 1 || cand.ShelfLevel == 2 ? 1.5m : 1.0m)), 2),
                    UrgencyScore = (profile.abcClass == AbcClassificationCategory.ClassA && !currInGolden ? 45.0m : 0.0m) +
                                   (profile.trend == "SeasonalSpike" || profile.trend == "Surging" ? 30.0m : 0.0m) +
                                   (currInGolden && (profile.abcClass == AbcClassificationCategory.ClassC || isDead) ? 40.0m : 0.0m),
                    UrgencyReason = "Multi-factor operational evaluation",
                    PriorityScore = Math.Round(Math.Clamp(
                        (policy.WeightFinancialBenefit * Math.Clamp(netMonthlyGain / (policy.TargetMonthlySavingsUsd > 0 ? policy.TargetMonthlySavingsUsd : 50.0m) * 100.0m, 0.0m, 100.0m)) +
                        (policy.WeightOperationalUrgency * Math.Clamp((profile.abcClass == AbcClassificationCategory.ClassA && !currInGolden ? 45.0m : 0.0m) + (profile.trend == "SeasonalSpike" || profile.trend == "Surging" ? 30.0m : 0.0m) + (currInGolden && (profile.abcClass == AbcClassificationCategory.ClassC || isDead) ? 40.0m : 0.0m), 0.0m, 100.0m)) +
                        (policy.WeightPlacementSuitability * suitabilityScore) -
                        (policy.WeightDisruptionPenalty * Math.Clamp(15.0m, 0.0m, 100.0m)), 0.0m, 100.0m), 1),
                    DisruptionLevel = "Medium",
                    IsOperationallyFeasible = true,
                    IsHardConstraintViolated = false,
                    ViolatedConstraintsSummary = profile.classificationSource.Contains("Stale")
                        ? $"Authoritative velocity projection is stale (calculated {profile.projectionAgeDays} days ago, exceeding {policy.MaxProjectionAgeDays}-day threshold); fell back to catalog Item.AbcVelocity."
                        : profile.classificationSource.Contains("FutureDated")
                            ? "Authoritative velocity projection has future-dated timestamp anomaly; fell back to catalog Item.AbcVelocity."
                            : null,
                    IsProjectionStaleOrMissing = profile.isStaleOrMissing,
                    ClassificationSource = profile.classificationSource,
                    ProjectionCalculatedAtUtc = profile.projectionCalculatedAtUtc,
                    ProjectionAgeDays = profile.projectionAgeDays,
                    Status = SlottingRecommendationStatus.Pending
                };

                candidatePool.Add((
                    Item: item,
                    Quantity: qtyToMove,
                    ProposedLocation: cand,
                    ProposedWeightKg: proposedWeightKg,
                    ProposedVolumeCm3: proposedVolumeCm3,
                    SuitabilityScore: suitabilityScore,
                    EstimatedMonthlyNetSavingsUsd: netMonthlyGain,
                    ItemSku: item.Sku,
                    ProposedLocationCode: cand.LocationCode,
                    Recommendation: rec
                ));
            }
        }

        // 7. Deterministic Ranking & Tie-Breaking
        // Order: NetSavings descending -> SuitabilityScore descending -> SKU ascending -> LocationCode ascending
        var sortedCandidates = candidatePool
            .OrderByDescending(c => c.EstimatedMonthlyNetSavingsUsd)
            .ThenByDescending(c => c.SuitabilityScore)
            .ThenBy(c => c.ItemSku, StringComparer.Ordinal)
            .ThenBy(c => c.ProposedLocationCode, StringComparer.Ordinal)
            .ToList();

        // 8. Multi-SKU Planning with Virtual Destination State Tracking
        var acceptedRecommendations = new List<SlottingRecommendation>();
        var assignedItemIds = new HashSet<Guid>();

        var virtualDestinations = candidateLocations.ToDictionary(
            loc => loc.Id,
            loc =>
            {
                var locStocks = currentStocks.Where(s => s.LocationId == loc.Id).ToList();
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

                var vState = new VirtualDestinationState
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

        foreach (var cand in sortedCandidates)
        {
            if (acceptedRecommendations.Count >= maxMoves) break;

            // One relocation proposal per SKU in a batch
            if (assignedItemIds.Contains(cand.Item.Id)) continue;

            if (!virtualDestinations.TryGetValue(cand.ProposedLocation.Id, out var vDest))
            {
                continue;
            }

            // 1. Client Tenancy Exclusivity (cannot co-mingle proposals of different clients)
            if (vDest.ActiveClientId.HasValue && vDest.ActiveClientId.Value != cand.Item.ClientId)
            {
                // Multi-tenant co-mingling in a single storage location is prohibited
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

            // 4. Stackability & Co-location Compatibility
            if (cand.Item.StackabilityFactor <= 1)
            {
                // Crush-sensitive / non-stackable item cannot co-locate with existing stock OR previous proposals
                if (vDest.HasExistingPhysicalStock || vDest.AcceptedItemIds.Count > 0)
                {
                    continue;
                }
            }
            else if (vDest.HasNonStackableOrCrushSensitiveItem)
            {
                // Destination contains a crush-sensitive item; cannot add any other SKU
                continue;
            }

            // 5. Heterogeneous 2D Physical Geometric Rectangle-Packing Occupancy Check
            decimal candItemL = cand.Item.LengthCm > 0 ? cand.Item.LengthCm : 30m;
            decimal candItemW = cand.Item.WidthCm > 0 ? cand.Item.WidthCm : 20m;
            decimal candItemH = cand.Item.HeightCm > 0 ? cand.Item.HeightCm : 20m;

            // Strict physical clearance and planar fit checks (fail closed without fallback)
            if (candItemH > vDest.LocationHeightCm)
            {
                continue;
            }
            bool fitsNormal = candItemL <= vDest.LocationLengthCm && candItemW <= vDest.LocationWidthCm;
            bool fitsRotated = cand.Item.AllowPlanarRotation && candItemW <= vDest.LocationLengthCm && candItemL <= vDest.LocationWidthCm;
            if (!fitsNormal && !fitsRotated)
            {
                continue;
            }

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
                // 2D Geometric Packing Conflict: cannot place without overlap
                continue;
            }

            // 6. Hazard Segregation
            bool isCandHazardous = !string.IsNullOrWhiteSpace(cand.Item.HazardClass);
            if (isCandHazardous)
            {
                if (vDest.HasExistingPhysicalStock && !vDest.HasHazardousGoods) continue;
                if (vDest.AcceptedItemIds.Count > 0 && !vDest.HasHazardousGoods) continue;
                if (vDest.HasHazardousGoods && !string.Equals(vDest.HazardClass, cand.Item.HazardClass, StringComparison.OrdinalIgnoreCase)) continue;
            }
            else
            {
                if (vDest.HasHazardousGoods) continue;
            }

            // 7. Storage Temperature Compatibility
            if (vDest.RequiredTempClass.HasValue && vDest.RequiredTempClass.Value != cand.Item.StorageTempClass)
            {
                continue;
            }

            // ALL VIRTUAL DESTINATION CHECKS PASSED: Accept proposal and mutate virtual destination state
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

            acceptedRecommendations.Add(cand.Recommendation);
        }

        decimal totalLaborMin = acceptedRecommendations.Sum(r => r.EstimatedMoveLaborMinutes);
        decimal totalTravelSaved = acceptedRecommendations.Sum(r => r.TravelDistanceDeltaMeters);
        decimal totalNetSavings = acceptedRecommendations.Sum(r => r.EstimatedMonthlyNetSavingsUsd);
        decimal avgScore = acceptedRecommendations.Count > 0 ? acceptedRecommendations.Average(r => r.SuitabilityScore) : 0m;

        batch.MarkSimulated(
            totalMoves: acceptedRecommendations.Count,
            estLaborMin: totalLaborMin,
            estTravelSavedMeters: totalTravelSaved,
            estMonthlySavingsUsd: totalNetSavings,
            avgScore: Math.Round(avgScore, 1));

        batch.Recommendations = acceptedRecommendations;

        _dbContext.SlottingRecommendationBatches.Add(batch);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Simulated slotting recommendation batch {BatchNumber} with {MoveCount} candidate relocations for Facility {FacilityId}",
            batch.BatchNumber, batch.TotalProposedMoves, batch.FacilityId);

        return MapBatchToDto(batch, policy.Name);
    }

    public async Task<SlottingRecommendationBatchDto?> GetRecommendationBatchAsync(
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

        return MapBatchToDto(batch, batch.Policy?.Name ?? "Policy");
    }

    public async Task<SlottingRecommendationBatchDto> ApproveRecommendationBatchAsync(
        Guid batchId,
        IReadOnlyList<Guid>? approvedRecommendationIds = null,
        CancellationToken cancellationToken = default)
    {
        var batch = await _dbContext.SlottingRecommendationBatches
            .Include(b => b.Policy)
            .Include(b => b.Recommendations)
            .FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(SlottingRecommendationBatch), batchId);

        ValidateFacilityAccess(batch.FacilityId);
        ValidateTenantAccess(batch.ClientId);

        var approverId = _currentUser.UserId ?? Guid.NewGuid();

        if (approvedRecommendationIds == null || approvedRecommendationIds.Count == 0)
        {
            // Approve all non-violated recommendations
            foreach (var rec in batch.Recommendations.Where(r => !r.IsHardConstraintViolated))
            {
                rec.Status = SlottingRecommendationStatus.Approved;
            }
            batch.ApprovedMovesCount = batch.Recommendations.Count(r => r.Status == SlottingRecommendationStatus.Approved);
            batch.Approve(approverId);
        }
        else
        {
            var approvedSet = approvedRecommendationIds.ToHashSet();
            foreach (var rec in batch.Recommendations)
            {
                if (approvedSet.Contains(rec.Id) && !rec.IsHardConstraintViolated)
                {
                    rec.Status = SlottingRecommendationStatus.Approved;
                }
                else if (approvedSet.Contains(rec.Id) && rec.IsHardConstraintViolated)
                {
                    rec.Status = SlottingRecommendationStatus.Rejected;
                    rec.RejectionReason = "Cannot approve recommendation with hard constraint violations.";
                }
                else
                {
                    rec.Status = SlottingRecommendationStatus.Skipped;
                }
            }

            batch.ApprovedMovesCount = batch.Recommendations.Count(r => r.Status == SlottingRecommendationStatus.Approved);
            batch.RejectedMovesCount = batch.Recommendations.Count(r => r.Status == SlottingRecommendationStatus.Rejected);
            batch.Status = batch.ApprovedMovesCount == batch.TotalProposedMoves
                ? SlottingBatchStatus.Approved
                : SlottingBatchStatus.PartiallyApproved;

            batch.ApprovedBy = approverId;
            batch.ApprovedAtUtc = DateTime.UtcNow;
        }

        // Invariant: Save batch changes only! Zero StockOnHand or Location state mutation!
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Approved slotting recommendation batch {BatchNumber} ({ApprovedCount}/{TotalCount} moves approved)",
            batch.BatchNumber, batch.ApprovedMovesCount, batch.TotalProposedMoves);

        return MapBatchToDto(batch, batch.Policy?.Name ?? "Policy");
    }

    #region Helper & Mapping Methods

    /// <summary>
    /// Calculates monthly economic net savings according to warehouse industrial engineering standards:
    /// TravelSavingsUsd = ((RoundTripMeters * Picks) / (WalkingSpeedMps * 3600)) * HourlyRateUsd
    /// MoveLaborCostUsd = (MoveMinutes / 60) * HourlyRateUsd
    /// NetMonthlySavingsUsd = TravelSavingsUsd - (MoveLaborCostUsd / AmortizationMonths)
    /// </summary>
    public static (decimal travelSavingsUsd, decimal moveLaborCostUsd, decimal netMonthlySavingsUsd, bool meetsThreshold) CalculateEconomicSavings(
        decimal roundTripTravelSavedMeters,
        decimal monthlyPicks,
        decimal walkingSpeedMps = 1.0m,
        decimal laborHourlyRateUsd = 25.00m,
        decimal relocationLaborMinutes = 6.00m,
        decimal amortizationMonths = 3.0m,
        decimal minLaborSavingsUsd = 5.00m)
    {
        decimal speed = walkingSpeedMps > 0 ? walkingSpeedMps : 1.0m;
        decimal rate = laborHourlyRateUsd > 0 ? laborHourlyRateUsd : 25.00m;
        decimal amort = amortizationMonths > 0 ? amortizationMonths : 3.0m;

        // Seconds walking saved per month = (roundTripTravelSavedMeters * monthlyPicks) / walkingSpeed
        decimal secondsSaved = (roundTripTravelSavedMeters * monthlyPicks) / speed;
        decimal hoursSaved = secondsSaved / 3600.0m;
        decimal travelSavingsUsd = Math.Round(hoursSaved * rate, 2);

        // One-time relocation physical labor cost = (minutes / 60) * hourlyRate
        decimal moveHours = relocationLaborMinutes / 60.0m;
        decimal moveLaborCostUsd = Math.Round(moveHours * rate, 2);

        // Monthly amortized relocation cost
        decimal monthlyAmortizedCost = moveLaborCostUsd / amort;
        decimal netMonthlySavingsUsd = Math.Round(travelSavingsUsd - monthlyAmortizedCost, 2);

        bool meetsThreshold = netMonthlySavingsUsd >= minLaborSavingsUsd;

        return (travelSavingsUsd, moveLaborCostUsd, netMonthlySavingsUsd, meetsThreshold);
    }

    private void ValidateFacilityAccess(Guid facilityId)
    {
        if (_currentUser.FacilityId.HasValue && _currentUser.FacilityId.Value != facilityId && !_currentUser.IsSuperAdmin)
        {
            throw new ForbiddenException($"User does not have access to facility {facilityId}.");
        }
    }

    private void ValidateTenantAccess(Guid? clientId)
    {
        if (!_currentUser.IsStaff && _currentUser.TenantId.HasValue)
        {
            if (!clientId.HasValue || clientId.Value != _currentUser.TenantId.Value)
            {
                throw new ForbiddenException($"Tenant boundary violation: Access restricted to client {_currentUser.TenantId.Value}.");
            }
        }
    }

    private static SlottingPolicyDto MapPolicyToDto(SlottingPolicy p) => new()
    {
        Id = p.Id,
        FacilityId = p.FacilityId,
        ClientId = p.ClientId,
        ZoneId = p.ZoneId,
        Name = p.Name,
        Description = p.Description,
        PrimaryGoal = p.PrimaryGoal,
        SecondaryGoal = p.SecondaryGoal,
        GoldenZoneMinZMeters = p.GoldenZoneMinZMeters,
        GoldenZoneMaxZMeters = p.GoldenZoneMaxZMeters,
        GoldenZoneShelfLevels = p.GoldenZoneShelfLevels,
        TargetClassAPickFace = p.TargetClassAPickFace,
        TargetClassBIntermediate = p.TargetClassBIntermediate,
        TargetClassCReserve = p.TargetClassCReserve,
        ProtectDeadStockFromUnnecessaryRelocation = p.ProtectDeadStockFromUnnecessaryRelocation,
        MaxRelocationsPerBatch = p.MaxRelocationsPerBatch,
        MinLaborSavingsUsd = p.MinLaborSavingsUsd,
        EstimatedRelocationLaborMinutes = p.EstimatedRelocationLaborMinutes,
        LaborHourlyRateUsd = p.LaborHourlyRateUsd,
        DisruptionPenaltyWeight = p.DisruptionPenaltyWeight,
        WeightTravelDistance = p.WeightTravelDistance,
        WeightErgonomics = p.WeightErgonomics,
        WeightPickDensity = p.WeightPickDensity,
        WeightAbcAffinity = p.WeightAbcAffinity,
        WeightVelocityAffinity = p.WeightVelocityAffinity,
        WeightCubeFill = p.WeightCubeFill,
        WeightProductAffinity = p.WeightProductAffinity,
        WeightFinancialBenefit = p.WeightFinancialBenefit,
        WeightOperationalUrgency = p.WeightOperationalUrgency,
        WeightPlacementSuitability = p.WeightPlacementSuitability,
        WeightDisruptionPenalty = p.WeightDisruptionPenalty,
        NegativeNetBenefitPolicy = p.NegativeNetBenefitPolicy,
        UrgencyThresholdForNegativeBenefit = p.UrgencyThresholdForNegativeBenefit,
        TargetMonthlySavingsUsd = p.TargetMonthlySavingsUsd,
        MaxAcceptableRelocationCostUsd = p.MaxAcceptableRelocationCostUsd,
        HandlingCostPerUnitUsd = p.HandlingCostPerUnitUsd,
        HeavyHandlingSurchargePerTierUsd = p.HeavyHandlingSurchargePerTierUsd,
        BaseDisruptionCostUsd = p.BaseDisruptionCostUsd,
        GoldenZoneOpportunityGainUsd = p.GoldenZoneOpportunityGainUsd,
        AmortizationMonths = p.AmortizationMonths,
        MaxProjectionAgeDays = p.MaxProjectionAgeDays,
        Version = p.Version,
        EffectiveFromUtc = p.EffectiveFromUtc,
        EffectiveToUtc = p.EffectiveToUtc,
        IsActive = p.IsActive
    };

    private static SlottingRecommendationBatchDto MapBatchToDto(SlottingRecommendationBatch b, string policyName) => new()
    {
        Id = b.Id,
        FacilityId = b.FacilityId,
        ClientId = b.ClientId,
        PolicyId = b.PolicyId,
        PolicyVersion = b.PolicyVersion,
        PolicySnapshotJson = b.PolicySnapshotJson,
        PolicyName = policyName,
        BatchNumber = b.BatchNumber,
        Status = b.Status,
        SnapshotDateUtc = b.SnapshotDateUtc,
        TotalProposedMoves = b.TotalProposedMoves,
        ApprovedMovesCount = b.ApprovedMovesCount,
        RejectedMovesCount = b.RejectedMovesCount,
        ExecutedMovesCount = b.ExecutedMovesCount,
        EstimatedLaborMinutesTotal = b.EstimatedLaborMinutesTotal,
        EstimatedTravelDistanceSavedMetersTotal = b.EstimatedTravelDistanceSavedMetersTotal,
        EstimatedMonthlyNetSavingsUsd = b.EstimatedMonthlyNetSavingsUsd,
        AverageSuitabilityScore = b.AverageSuitabilityScore,
        ApprovedBy = b.ApprovedBy,
        ApprovedAtUtc = b.ApprovedAtUtc,
        Notes = b.Notes,
        Recommendations = b.Recommendations.Select(r => new SlottingRecommendationDto
        {
            Id = r.Id,
            BatchId = r.BatchId,
            ItemId = r.ItemId,
            ItemSku = r.ItemSku,
            ClientId = r.ClientId,
            CurrentLocationId = r.CurrentLocationId,
            CurrentLocationCode = r.CurrentLocationCode,
            ProposedLocationId = r.ProposedLocationId,
            ProposedLocationCode = r.ProposedLocationCode,
            SourceZoneCode = r.SourceZoneCode,
            DestinationZoneCode = r.DestinationZoneCode,
            SkuVelocity = r.SkuVelocity,
            AbcClass = r.AbcClass,
            VelocityTrend = r.VelocityTrend ?? "Stable",
            ReasonCode = r.ReasonCode,
            SuitabilityScore = r.SuitabilityScore,
            ErgonomicScore = r.ErgonomicScore,
            AbcAffinityScore = r.AbcAffinityScore,
            VelocityAffinityScore = r.VelocityAffinityScore,
            PickDensityScore = r.PickDensityScore,
            CumulativeWeightConsumedKg = r.CumulativeWeightConsumedKg,
            CumulativeVolumeConsumedCm3 = r.CumulativeVolumeConsumedCm3,
            TravelDistanceDeltaMeters = r.TravelDistanceDeltaMeters,
            EstimatedMoveLaborMinutes = r.EstimatedMoveLaborMinutes,
            EstimatedMonthlyNetSavingsUsd = r.EstimatedMonthlyNetSavingsUsd,
            GrossMonthlySavingsUsd = r.GrossMonthlySavingsUsd,
            HandlingCostUsd = r.HandlingCostUsd,
            MovementCostUsd = r.MovementCostUsd,
            DisruptionCostUsd = r.DisruptionCostUsd,
            TotalRelocationCostUsd = r.TotalRelocationCostUsd,
            UrgencyScore = r.UrgencyScore,
            UrgencyReason = r.UrgencyReason,
            PriorityScore = r.PriorityScore,
            DisruptionLevel = r.DisruptionLevel,
            IsOperationallyFeasible = r.IsOperationallyFeasible,
            IsHardConstraintViolated = r.IsHardConstraintViolated,
            ViolatedConstraintsSummary = r.ViolatedConstraintsSummary,
            IsProjectionStaleOrMissing = r.IsProjectionStaleOrMissing,
            ClassificationSource = r.ClassificationSource,
            ProjectionCalculatedAtUtc = r.ProjectionCalculatedAtUtc,
            ProjectionAgeDays = r.ProjectionAgeDays,
            Status = r.Status,
            RejectionReason = r.RejectionReason
        }).ToList()
    };

    public static bool IsInGoldenZone(Location loc, SlottingPolicyDto policy)
    {
        if (loc.ZCoord.HasValue)
        {
            return loc.ZCoord.Value >= policy.GoldenZoneMinZMeters &&
                   loc.ZCoord.Value <= policy.GoldenZoneMaxZMeters;
        }
        if (loc.ShelfLevel.HasValue)
        {
            var goldenLevels = policy.GoldenZoneShelfLevels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return goldenLevels.Contains(loc.ShelfLevel.Value.ToString());
        }
        return false;
    }

    public static bool IsInGoldenZone(Location loc, SlottingPolicy policy)
    {
        if (loc.ZCoord.HasValue)
        {
            return loc.ZCoord.Value >= policy.GoldenZoneMinZMeters &&
                   loc.ZCoord.Value <= policy.GoldenZoneMaxZMeters;
        }
        if (loc.ShelfLevel.HasValue)
        {
            var goldenLevels = policy.GoldenZoneShelfLevels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return goldenLevels.Contains(loc.ShelfLevel.Value.ToString());
        }
        return false;
    }

    public static decimal CalculateDistanceToDockMeters(Location loc)
    {
        if (loc.XCoord.HasValue && loc.YCoord.HasValue)
        {
            decimal x = loc.XCoord.Value;
            decimal y = loc.YCoord.Value;
            decimal z = loc.ZCoord ?? 0m;
            return (decimal)Math.Sqrt((double)(x * x + y * y + z * z));
        }

        int aisleNum = 1;
        if (!string.IsNullOrEmpty(loc.AisleCode))
        {
            var digits = new string(loc.AisleCode.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out int a)) aisleNum = a;
        }
        int level = loc.ShelfLevel ?? 1;
        return 10.0m + (aisleNum * 4.0m) + (level * 1.5m);
    }

    public static decimal CalculateErgonomicScore(Location loc, SlottingPolicyDto policy, AbcClassificationCategory abc, string trend)
    {
        bool inGolden = IsInGoldenZone(loc, policy);

        if (abc == AbcClassificationCategory.ClassA || trend == "Surging")
        {
            return inGolden ? 100m : 30m;
        }
        if (abc == AbcClassificationCategory.ClassC || trend == "Inactive" || trend == "DeadStock")
        {
            return inGolden ? 25m : 90m;
        }
        return inGolden ? 85m : 70m;
    }

    public static decimal CalculateErgonomicScore(Location loc, SlottingPolicy policy, AbcClassificationCategory abc, string trend)
    {
        bool inGolden = IsInGoldenZone(loc, policy);

        if (abc == AbcClassificationCategory.ClassA || trend == "Surging")
        {
            return inGolden ? 100m : 30m;
        }
        if (abc == AbcClassificationCategory.ClassC || trend == "Inactive" || trend == "DeadStock")
        {
            return inGolden ? 25m : 90m;
        }
        return inGolden ? 85m : 70m;
    }

    public static decimal CalculateAbcAffinityScore(Location loc, AbcClassificationCategory abc)
    {
        bool isPickFace = loc.Zone?.ZoneType == WarehouseZoneType.PickFace;
        bool isBulk = loc.Zone?.ZoneType == WarehouseZoneType.BulkStorage;
        int level = loc.ShelfLevel ?? 1;

        return abc switch
        {
            AbcClassificationCategory.ClassA => isPickFace || level <= 2 ? 100m : (isBulk || level >= 4 ? 25m : 75m),
            AbcClassificationCategory.ClassB => !isBulk && level <= 3 ? 95m : (isBulk ? 60m : 75m),
            _ => isBulk || level >= 3 ? 100m : (isPickFace && level == 1 ? 20m : 75m)
        };
    }

    public static decimal CalculateVelocityAffinityScore(Location loc, InventoryMovementVelocityCategory velocityCat, string trend)
    {
        bool isPickFace = loc.Zone?.ZoneType == WarehouseZoneType.PickFace;
        int level = loc.ShelfLevel ?? 1;

        return trend switch
        {
            "Surging" => isPickFace || level <= 2 ? 100m : (level >= 3 ? 20m : 70m),
            "Declining" => level >= 2 && level <= 3 ? 95m : (isPickFace && level == 1 ? 30m : 75m),
            "SeasonalSpike" => isPickFace || level <= 2 ? 95m : (level >= 4 ? 25m : 70m),
            "Inactive" => level >= 3 ? 95m : (isPickFace && level == 1 ? 25m : 70m),
            "DeadStock" => level >= 3 ? 100m : (isPickFace && level == 1 ? 10m : 65m),
            _ => 85m
        };
    }

    public static decimal CalculatePickDensityScore(Location loc, decimal pickFreqPerDay, int totalPicks)
    {
        decimal dist = CalculateDistanceToDockMeters(loc);
        decimal proximity = Math.Clamp(100m - dist, 0m, 100m);

        if (pickFreqPerDay >= 4.0m || totalPicks >= 40)
        {
            return Math.Round(50m + (0.5m * proximity), 1);
        }
        if (pickFreqPerDay <= 1.0m && totalPicks <= 10)
        {
            return Math.Round(100m - (0.5m * proximity), 1);
        }
        return 75m;
    }

    public static decimal CalculateCompositeSuitabilityScore(
        SlottingPolicyDto policy,
        decimal ergoScore,
        decimal abcScore,
        decimal velScore,
        decimal densityScore,
        decimal travelScore,
        decimal coLocationScore = 50.0m)
    {
        decimal wE = policy.WeightErgonomics;
        decimal wT = policy.WeightTravelDistance;
        decimal wD = policy.WeightPickDensity;
        decimal wAbc = policy.WeightAbcAffinity;
        decimal wVel = policy.WeightVelocityAffinity;
        decimal wColoc = policy.WeightProductAffinity;

        decimal sum = wE + wT + wD + wAbc + wVel + wColoc;
        if (sum > 0m)
        {
            wE /= sum;
            wT /= sum;
            wD /= sum;
            wAbc /= sum;
            wVel /= sum;
            wColoc /= sum;
        }

        decimal totalScore = (ergoScore * wE) +
                             (travelScore * wT) +
                             (densityScore * wD) +
                             (abcScore * wAbc) +
                             (velScore * wVel) +
                             (coLocationScore * wColoc);

        return Math.Round(Math.Clamp(totalScore, 0m, 100m), 1);
    }

    public static decimal CalculateCompositeSuitabilityScore(
        SlottingPolicy policy,
        decimal ergoScore,
        decimal abcScore,
        decimal velScore,
        decimal densityScore,
        decimal travelScore,
        decimal coLocationScore = 50.0m)
    {
        decimal wE = policy.WeightErgonomics;
        decimal wT = policy.WeightTravelDistance;
        decimal wD = policy.WeightPickDensity;
        decimal wAbc = policy.WeightAbcAffinity;
        decimal wVel = policy.WeightVelocityAffinity;
        decimal wColoc = policy.WeightProductAffinity;

        decimal sum = wE + wT + wD + wAbc + wVel + wColoc;
        if (sum > 0m)
        {
            wE /= sum;
            wT /= sum;
            wD /= sum;
            wAbc /= sum;
            wVel /= sum;
            wColoc /= sum;
        }

        decimal totalScore = (ergoScore * wE) +
                             (travelScore * wT) +
                             (densityScore * wD) +
                             (abcScore * wAbc) +
                             (velScore * wVel) +
                             (coLocationScore * wColoc);

        return Math.Round(Math.Clamp(totalScore, 0m, 100m), 1);
    }

    private async Task<decimal> CalculateCoLocationAffinityScoreAsync(
        Guid itemId,
        Guid clientId,
        Location location,
        CancellationToken cancellationToken)
    {
        // 1. Find if item belongs to an active co-location cluster for this facility & client
        var cluster = await _dbContext.ProductAffinityClusters
            .AsNoTracking()
            .Include(c => c.Members)
            .FirstOrDefaultAsync(c => c.FacilityId == location.FacilityId &&
                                      c.ClientId == clientId &&
                                      c.Members.Any(m => m.ItemId == itemId),
                                 cancellationToken);

        if (cluster == null)
        {
            return 50.0m; // Neutral baseline when item is unclustered
        }

        var partnerItemIds = cluster.Members
            .Where(m => m.ItemId != itemId)
            .Select(m => m.ItemId)
            .ToList();

        if (partnerItemIds.Count == 0)
        {
            return 50.0m;
        }

        // 2. Query locations currently occupied by cluster partners in this facility
        var partnerLocations = await _dbContext.StockOnHand
            .AsNoTracking()
            .Where(s => partnerItemIds.Contains(s.ItemId) && (s.QtyAvailable > 0 || s.QtyAllocated > 0))
            .Select(s => s.Location)
            .Where(l => l.FacilityId == location.FacilityId && !l.IsDeleted)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (partnerLocations.Count == 0)
        {
            return 50.0m; // Neutral baseline when no partners are currently slotted in facility
        }

        // 3. Topological Proximity Scoring
        // In warehouse storage layout, RackCode denotes the longitudinal bay/rack position along an aisle.
        int? lAisle = TryExtractAisleNumber(location.AisleCode);
        int? lRack = TryExtractRackNumber(location.RackCode);

        // Same aisle and same rack/bay -> Prime co-location (100 pts)
        if (partnerLocations.Any(pl => !string.IsNullOrEmpty(pl.AisleCode) && pl.AisleCode == location.AisleCode &&
                                       !string.IsNullOrEmpty(pl.RackCode) && pl.RackCode == location.RackCode))
        {
            return 100.0m;
        }

        // Same aisle, adjacent rack/bay (|Delta Rack| <= 2) -> High co-location (85 pts)
        if (partnerLocations.Any(pl => !string.IsNullOrEmpty(pl.AisleCode) && pl.AisleCode == location.AisleCode &&
                                       TryExtractRackNumber(pl.RackCode) is int pRack && lRack.HasValue &&
                                       Math.Abs(pRack - lRack.Value) <= 2 && Math.Abs(pRack - lRack.Value) > 0))
        {
            return 85.0m;
        }

        // Same aisle -> Moderate co-location (70 pts)
        if (partnerLocations.Any(pl => !string.IsNullOrEmpty(pl.AisleCode) && pl.AisleCode == location.AisleCode))
        {
            return 70.0m;
        }

        // Adjacent aisle (|Delta Aisle| <= 1) -> Low-moderate co-location (55 pts)
        if (partnerLocations.Any(pl => TryExtractAisleNumber(pl.AisleCode) is int pAisle && lAisle.HasValue &&
                                       Math.Abs(pAisle - lAisle.Value) == 1))
        {
            return 55.0m;
        }

        // 4. Continuous Spatial 3D Euclidean Distance Fallback:
        // S_c = round(clamp(100.0 - 2.5 * D, 10.0, 100.0), 1)
        decimal minDistanceMeters = partnerLocations.Min(pl => Calculate3DDistance(location, pl));

        return Math.Round(Math.Clamp(100.0m - (minDistanceMeters * 2.5m), 10.0m, 100.0m), 1);
    }

    public static int? TryExtractAisleNumber(string? aisleCode)
    {
        if (string.IsNullOrWhiteSpace(aisleCode)) return null;
        var digits = new string(aisleCode.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out int a) ? a : null;
    }

    public static int? TryExtractRackNumber(string? rackCode)
    {
        if (string.IsNullOrWhiteSpace(rackCode)) return null;
        var digits = new string(rackCode.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out int r) ? r : null;
    }

    public static decimal Calculate3DDistance(Location loc1, Location loc2)
    {
        if (loc1.XCoord.HasValue && loc1.YCoord.HasValue && loc2.XCoord.HasValue && loc2.YCoord.HasValue)
        {
            decimal dx = loc1.XCoord.Value - loc2.XCoord.Value;
            decimal dy = loc1.YCoord.Value - loc2.YCoord.Value;
            decimal dz = (loc1.ZCoord ?? 0m) - (loc2.ZCoord ?? 0m);
            return (decimal)Math.Sqrt((double)(dx * dx + dy * dy + dz * dz));
        }

        int a1 = TryExtractAisleNumber(loc1.AisleCode) ?? 1;
        int a2 = TryExtractAisleNumber(loc2.AisleCode) ?? 1;
        int r1 = TryExtractRackNumber(loc1.RackCode) ?? 1;
        int r2 = TryExtractRackNumber(loc2.RackCode) ?? 1;
        int l1 = loc1.ShelfLevel ?? 1;
        int l2 = loc2.ShelfLevel ?? 1;

        decimal approxDx = Math.Abs(a1 - a2) * 3.0m;
        decimal approxDy = Math.Abs(r1 - r2) * 1.5m;
        decimal approxDz = Math.Abs(l1 - l2) * 1.2m;

        return (decimal)Math.Sqrt((double)(approxDx * approxDx + approxDy * approxDy + approxDz * approxDz));
    }

    private async Task<(AbcClassificationCategory abcClass, InventoryMovementVelocityCategory velocityCat, string trend, decimal dailyVelocity, decimal pickFreqPerDay, int totalPicks, bool isStaleOrMissing, string vLabel, string abcLabel, string classificationSource, DateTime? projectionCalculatedAtUtc, int? projectionAgeDays, bool isDeadStock)> GetSkuVelocityProfileAsync(
        Guid facilityId,
        Guid itemId,
        Item item,
        SlottingPolicyDto? policy,
        DateTime evaluationTimeUtc,
        CancellationToken cancellationToken)
    {
        var records = await _dbContext.SkuVelocityClassificationRecords
            .AsNoTracking()
            .Where(r => r.FacilityId == facilityId && r.ItemId == itemId)
            .OrderByDescending(r => r.CalculatedAtUtc)
            .ToListAsync(cancellationToken);

        // Check Dead Stock status from aging snapshots or authoritative lifetime evidence
        var aging = await _dbContext.DailyInventoryAgingSnapshots
            .AsNoTracking()
            .Where(s => s.FacilityId == facilityId && s.ItemId == itemId)
            .OrderByDescending(s => s.SnapshotDateUtc)
            .FirstOrDefaultAsync(cancellationToken);

        bool isDeadStock = await ResolveDeadStockStatusAsync(facilityId, itemId, item, aging, evaluationTimeUtc, cancellationToken);

        if (records.Count == 0)
        {
            return GetFallbackProfile(item, "CatalogFallback (MissingProjection)", null, null, isDeadStock);
        }

        return GetProfileFromRecords(item, records, policy, evaluationTimeUtc, isDeadStock);
    }

    private async Task<bool> ResolveDeadStockStatusAsync(
        Guid facilityId,
        Guid itemId,
        Item item,
        DailyInventoryAgingSnapshot? aging,
        DateTime evaluationTimeUtc,
        CancellationToken cancellationToken)
    {
        // 1. Fresh Aging Snapshot Evaluation (Snapshot age <= 30 days)
        if (aging != null)
        {
            TimeSpan snapAge = evaluationTimeUtc - aging.SnapshotDateUtc;
            if (snapAge >= TimeSpan.Zero && snapAge <= TimeSpan.FromDays(30))
            {
                // Rule 1: Confirmed demand history exists in snapshot
                if (aging.HasDemandHistory && aging.DemandInactivityDays.HasValue)
                {
                    // Demand occurred within the last 365 days -> Strictly Not dead stock
                    if (aging.LastDemandAgeDays < 365 || aging.DemandInactivityDays.Value < 365)
                    {
                        return false;
                    }

                    // LastDemandAgeDays >= 365 with confirmed demand history qualifies as dead stock
                    if (aging.LastDemandAgeDays >= 365 || aging.DemandInactivityDays.Value >= 365)
                    {
                        return true;
                    }
                }

                // Rule 2, 3 & 4: DemandInactivityDays == null or !aging.HasDemandHistory
                // A null inactivity duration is treated as UNKNOWN demand evidence, not proof of zero demand.
                // Never-demanded inventory qualifies ONLY when the absence of lifetime demand is positively
                // established by authoritative operational ledger evidence.
                // Unknown or incomplete evidence strictly returns isDeadStock = false.
                if (aging.ReceiptAgeDays >= 365)
                {
                    var (hasHistoricalEvidence, isDeadStock) = await CheckAuthoritativeLifetimeDemandEvidenceAsync(
                        facilityId, itemId, item, evaluationTimeUtc, cancellationToken);

                    if (hasHistoricalEvidence && isDeadStock)
                    {
                        return true;
                    }
                }

                // Unknown or incomplete evidence returns isDeadStock = false
                return false;
            }
        }

        // 2. Missing or Stale Aging Snapshot:
        // Must check independent authoritative lifetime-demand evidence from the operational ledger.
        // A missing velocity record or a short-window (30d/90d) velocity projection cannot prove 365-day dormancy.
        var (hasLedgerEvidence, isDead) = await CheckAuthoritativeLifetimeDemandEvidenceAsync(
            facilityId, itemId, item, evaluationTimeUtc, cancellationToken);

        if (hasLedgerEvidence)
        {
            return isDead;
        }

        // Insufficient historical evidence -> Unverified (strictly not dead stock)
        return false;
    }

    private async Task<(bool hasHistoricalEvidence, bool isDeadStock)> CheckAuthoritativeLifetimeDemandEvidenceAsync(
        Guid facilityId,
        Guid itemId,
        Item item,
        DateTime evaluationTimeUtc,
        CancellationToken cancellationToken)
    {
        bool hasUnreliableDemandTimestamp = false;

        // 1. Query qualifying pick tasks (executed picking demand)
        var pickTasks = await _dbContext.PickTasks
            .AsNoTracking()
            .Where(p => p.ItemId == itemId && (p.FacilityId == facilityId || p.FacilityId == Guid.Empty))
            .Where(p => p.Status == PickTaskStatus.Completed || p.Status == PickTaskStatus.ShortPicked || p.QuantityPicked > 0)
            .ToListAsync(cancellationToken);

        var demandDates = new List<DateTime>();

        foreach (var pt in pickTasks)
        {
            DateTime? execTime = pt.CompletedAtUtc ?? pt.StartedAtUtc;
            if (execTime.HasValue)
            {
                demandDates.Add(execTime.Value);
            }
            else
            {
                // Task has positive picked quantity or completed status, but lacks an authoritative execution timestamp.
                // We must NOT fall back to task CreatedAt, as that does not establish when physical picking occurred.
                hasUnreliableDemandTimestamp = true;
            }
        }

        // 2. Query immutable pick transactions (physical stock ledger picks)
        var txs = await _dbContext.InventoryTransactions
            .AsNoTracking()
            .Where(t => t.ItemId == itemId && (t.FacilityId == facilityId || t.FacilityId == Guid.Empty) && t.TxType == InventoryTxType.Pick && t.Quantity > 0)
            .ToListAsync(cancellationToken);

        foreach (var tx in txs)
        {
            demandDates.Add(tx.TxTimestamp);
        }

        // 3. Query outbound order lines
        var orderLines = await _dbContext.OutboundOrderLines
            .AsNoTracking()
            .Include(l => l.Allocations)
            .Where(l => l.ItemId == itemId && !l.IsDeleted)
            .Where(l => l.Status != OutboundOrderLineStatus.Cancelled && l.Status != OutboundOrderLineStatus.Pending)
            .Where(l => l.QuantityAllocated > 0 || l.QuantityShipped > 0 || l.Status == OutboundOrderLineStatus.Completed || l.Status == OutboundOrderLineStatus.Allocated || l.Status == OutboundOrderLineStatus.PartiallyAllocated)
            .ToListAsync(cancellationToken);

        var orderIds = orderLines.Select(l => l.OutboundOrderId).Distinct().ToList();
        var orders = await _dbContext.OutboundOrders
            .AsNoTracking()
            .Where(o => orderIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, cancellationToken);

        foreach (var line in orderLines)
        {
            if (!orders.TryGetValue(line.OutboundOrderId, out var order) || order == null)
            {
                // Qualifying demand exists on an orphaned order line lacking a parent order:
                // Lacks authoritative lifecycle state and facility isolation context -> Incomplete demand evidence.
                hasUnreliableDemandTimestamp = true;
                continue;
            }

            // Enforce facility isolation
            if (order.FacilityId != facilityId && order.FacilityId != Guid.Empty)
            {
                continue;
            }

            // Voided parent orders (cancelled or unreleased draft) do not constitute executed demand
            if (order.Status == OutboundOrderStatus.Cancelled || order.Status == OutboundOrderStatus.Draft || order.IsDeleted)
            {
                continue;
            }

            DateTime? allocTime = line.Allocations.Count > 0
                ? line.Allocations.Where(a => !a.IsDeleted).Select(a => (DateTime?)a.AllocatedAtUtc).OrderByDescending(d => d).FirstOrDefault()
                : null;

            DateTime? shipTime = order.DeliveredAtUtc ?? order.DeliveryStatusUpdatedAtUtc;

            DateTime? lineExecTime = allocTime ?? shipTime;

            if (lineExecTime.HasValue)
            {
                demandDates.Add(lineExecTime.Value);
            }
            else
            {
                // Qualifying demand exists without reliable allocation/shipment timestamp.
                // We must NOT fall back to OrderDateUtc or CreatedAt.
                hasUnreliableDemandTimestamp = true;
            }
        }

        // 4. Inbound Physical Receipt Scans
        // Authoritative physical receiving intake requires:
        //  1. Non-null, non-deleted ReceivingSession
        //  2. Session in an explicitly permitted active/completed state (Started, InProgress, Completed)
        //  3. Preserved facility isolation (matching facilityId or Guid.Empty)
        //  4. Positive received quantity (QuantityReceived > 0)
        //  5. Valid physical intake timestamp (ScannedAtUtc != default)
        var receiptLines = await _dbContext.ReceiptLines
            .AsNoTracking()
            .Include(r => r.ReceivingSession)
            .Where(r => r.ItemId == itemId && !r.IsDeleted)
            .Where(r => r.ReceivingSession != null && !r.ReceivingSession.IsDeleted)
            .Where(r => r.ReceivingSession.FacilityId == facilityId || r.ReceivingSession.FacilityId == Guid.Empty)
            .Where(r => r.ReceivingSession.Status == ReceivingStatus.Completed ||
                        r.ReceivingSession.Status == ReceivingStatus.InProgress ||
                        r.ReceivingSession.Status == ReceivingStatus.Started)
            .Where(r => r.QuantityReceived > 0)
            .Where(r => r.ScannedAtUtc != default)
            .ToListAsync(cancellationToken);

        DateTime? earliestReceiptUtc = receiptLines.Count > 0
            ? receiptLines.Select(r => (DateTime?)r.ScannedAtUtc).OrderBy(d => d).FirstOrDefault()
            : null;

        // If any demand activity occurred with missing/unreliable execution timestamps,
        // we cannot reliably determine the demand inactivity period -> fail closed as unverified.
        if (hasUnreliableDemandTimestamp)
        {
            return (hasHistoricalEvidence: false, isDeadStock: false);
        }

        bool hasDemandHistory = demandDates.Count > 0;
        DateTime? latestDemandUtc = hasDemandHistory ? demandDates.Max() : null;

        if (hasDemandHistory && latestDemandUtc.HasValue)
        {
            TimeSpan demandInactivity = evaluationTimeUtc - latestDemandUtc.Value;
            if (demandInactivity < TimeSpan.FromDays(365))
            {
                // Recent demand within the 365-day period -> Positively NOT dead stock
                return (hasHistoricalEvidence: true, isDeadStock: false);
            }
            else
            {
                // 365+ days confirmed zero demand dormancy -> Dead stock
                return (hasHistoricalEvidence: true, isDeadStock: true);
            }
        }
        else
        {
            // Zero demand ever recorded in operational ledger.
            // Never-demanded inventory qualifies ONLY when authoritative physical receipt is positively established.
            // If ReceiptLines are absent, physical warehouse arrival is unverified; Item.CreatedAt cannot substitute.
            if (!earliestReceiptUtc.HasValue)
            {
                return (hasHistoricalEvidence: false, isDeadStock: false);
            }

            TimeSpan physicalReceiptAge = evaluationTimeUtc - earliestReceiptUtc.Value;
            if (physicalReceiptAge >= TimeSpan.FromDays(365))
            {
                // Receipt scan confirms 365+ days presence in facility and zero lifetime demand -> Dead stock
                return (hasHistoricalEvidence: true, isDeadStock: true);
            }
            else
            {
                // Inventory received < 365 days ago -> Insufficient historical evidence to prove dormancy
                return (hasHistoricalEvidence: false, isDeadStock: false);
            }
        }
    }

    public static (AbcClassificationCategory abcClass, InventoryMovementVelocityCategory velocityCat, string trend, decimal dailyVelocity, decimal pickFreqPerDay, int totalPicks, bool isStaleOrMissing, string vLabel, string abcLabel, string classificationSource, DateTime? projectionCalculatedAtUtc, int? projectionAgeDays, bool isDeadStock) GetProfileFromRecords(
        Item item,
        List<SkuVelocityClassificationRecord> records,
        SlottingPolicyDto? policy,
        DateTime evaluationTimeUtc,
        bool isDeadStock)
    {
        var rec90 = records.FirstOrDefault(r => r.ObservationWindow == VelocityObservationWindow.Days90) ?? records.First();
        var rec30 = records.FirstOrDefault(r => r.ObservationWindow == VelocityObservationWindow.Days30);

        DateTime calcAt = rec90.CalculatedAtUtc;
        TimeSpan ageSpan = evaluationTimeUtc - calcAt;
        int maxAgeDays = policy?.MaxProjectionAgeDays ?? 30;

        // Clock skew check: future-dated projection (> 5 minutes ahead of evaluationTimeUtc)
        if (calcAt > evaluationTimeUtc.AddMinutes(5))
        {
            return GetFallbackProfile(item, "CatalogFallback (FutureDatedSkewAnomaly)", calcAt, 0, isDeadStock);
        }

        // Exact TimeSpan freshness boundary check
        bool isStale = ageSpan > TimeSpan.FromDays(maxAgeDays);
        int ageDays = Math.Max(0, (int)ageSpan.TotalDays);

        if (isStale)
        {
            return GetFallbackProfile(item, "CatalogFallback (StaleProjection)", calcAt, ageDays, isDeadStock);
        }

        var abc = rec90.AbcClass;
        var cat = rec90.VelocityCategory;
        decimal v90 = rec90.DailyDemandVelocity;
        decimal v30 = rec30?.DailyDemandVelocity ?? v90;
        decimal pickFreq = rec90.PickFrequencyPerDay;
        int totalPicks = rec90.TotalPickTasksCount;

        string trend;
        if (isDeadStock)
        {
            trend = "DeadStock";
            cat = InventoryMovementVelocityCategory.DeadStock;
        }
        else
        {
            if (cat == InventoryMovementVelocityCategory.DeadStock)
            {
                // Without verified 365-day dormancy proof, a 90-day zero-demand projection is Inactive/NonMoving, NOT dead stock
                cat = InventoryMovementVelocityCategory.NonMoving;
            }

            if (rec90.IsSeasonalPattern || (rec30?.IsSeasonalPattern ?? false))
            {
                trend = "SeasonalSpike";
            }
            else if (rec90.IsDemandSpike || (rec30?.IsDemandSpike ?? false))
            {
                trend = "Surging";
            }
            else if (v30 == 0m && v90 == 0m)
            {
                trend = "Inactive";
            }
            else if (v30 > v90 * 1.3m && v30 > 1.0m)
            {
                trend = "Surging";
            }
            else if (v30 < v90 * 0.7m && v30 > 0m)
            {
                trend = "Declining";
            }
            else
            {
                trend = "Stable";
            }
        }

        string vLabel = trend == "Surging" ? "Fast" :
                       (cat == InventoryMovementVelocityCategory.FastMoving ? "Fast" :
                       (cat == InventoryMovementVelocityCategory.SlowMoving || cat == InventoryMovementVelocityCategory.DeadStock ? "Slow" : "Medium"));
        string aLabel = abc == AbcClassificationCategory.ClassA ? "A" : (abc == AbcClassificationCategory.ClassB ? "B" : "C");

        return (abc, cat, trend, v90, pickFreq, totalPicks, false, vLabel, aLabel, "AuthoritativeProjection", calcAt, ageDays, isDeadStock);
    }

    public static (AbcClassificationCategory abcClass, InventoryMovementVelocityCategory velocityCat, string trend, decimal dailyVelocity, decimal pickFreqPerDay, int totalPicks, bool isStaleOrMissing, string vLabel, string abcLabel, string classificationSource, DateTime? projectionCalculatedAtUtc, int? projectionAgeDays, bool isDeadStock) GetFallbackProfile(
        Item item,
        string source,
        DateTime? calculatedAtUtc,
        int? ageDays,
        bool isDeadStock)
    {
        var fallbackAbc = item.AbcVelocity switch
        {
            AbcVelocity.A => AbcClassificationCategory.ClassA,
            AbcVelocity.B => AbcClassificationCategory.ClassB,
            _ => AbcClassificationCategory.ClassC
        };
        var fallbackCat = item.AbcVelocity switch
        {
            AbcVelocity.A => InventoryMovementVelocityCategory.FastMoving,
            AbcVelocity.B => InventoryMovementVelocityCategory.MediumMoving,
            _ => InventoryMovementVelocityCategory.SlowMoving
        };
        if (isDeadStock)
        {
            fallbackCat = InventoryMovementVelocityCategory.DeadStock;
        }
        string vLabel = fallbackCat == InventoryMovementVelocityCategory.FastMoving ? "Fast" :
                       (fallbackCat == InventoryMovementVelocityCategory.SlowMoving || fallbackCat == InventoryMovementVelocityCategory.DeadStock ? "Slow" : "Medium");
        string aLabel = fallbackAbc == AbcClassificationCategory.ClassA ? "A" : (fallbackAbc == AbcClassificationCategory.ClassB ? "B" : "C");

        string trend = isDeadStock ? "DeadStock" : "Stable";

        return (fallbackAbc, fallbackCat, trend, 0m, 0m, 0, true, vLabel, aLabel, source, calculatedAtUtc, ageDays, isDeadStock);
    }

    public class VirtualDestinationState
    {
        public Location Location { get; init; } = null!;
        public decimal LocationLengthCm { get; init; }
        public decimal LocationWidthCm { get; init; }
        public decimal LocationHeightCm { get; init; }
        public decimal InitialPhysicalWeightKg { get; init; }
        public decimal InitialPhysicalVolumeCm3 { get; init; }
        public decimal CumulativeProposedWeightKg { get; set; }
        public decimal CumulativeProposedVolumeCm3 { get; set; }
        public decimal MaxWeightKg { get; init; }
        public decimal UsableVolumeCm3 { get; init; }
        public Guid? ActiveClientId { get; set; }
        public bool HasExistingPhysicalStock { get; init; }
        public bool HasNonStackableOrCrushSensitiveItem { get; set; }
        public decimal CumulativeOccupiedFootprintFraction { get; set; }
        public bool HasHazardousGoods { get; set; }
        public string? HazardClass { get; set; }
        public StorageTempClass? RequiredTempClass { get; set; }
        public List<Guid> AcceptedItemIds { get; } = new();
        public List<(Item Item, decimal Quantity)> AcceptedAllocations { get; } = new();
        public List<VirtualPlacement> ExistingPhysicalPlacements { get; } = new();
        public List<VirtualPlacement> AcceptedVirtualPlacements { get; } = new();
    }

    #endregion
}
