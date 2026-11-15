using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Authorization;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Services.Analytics;
using Warehouse3PL.UnitTests.Common;
using Warehouse3PL.WebApi.Controllers;

namespace Warehouse3PL.UnitTests.Analytics;

/// <summary>
/// Exhaustive unit test suite for Phase 6.3.7.4:
/// Relocation Recommendation Engine &amp; Multi-Factor Prioritization.
/// Verifies all 10 reviewer acceptance criteria:
/// 1. Benefit Ranking
/// 2. Operational Urgency Impact
/// 3. Hard-Gate Supremacy (Physical &amp; Regulatory Inviolability)
/// 4. Negative Net Benefit Policy (Disqualify, AllowIfHighUrgency, AllowAll)
/// 5. Disruption Cost Invariant (Handling, Transit, Disruption Dampening)
/// 6. Deterministic Ranking &amp; Tie-Breaking
/// 7. Tenant and Facility Isolation
/// 8. Geometric Packing Feasibility (2D Planar Non-Overlap)
/// 9. Populated Operational Ledger Invariance (All 8 Tables Audited)
/// 10. Approval Boundary (Advisory Lifecycle &amp; Zero Stock Mutations)
/// </summary>
public class RelocationRecommendationAndPrioritizationTests
{
    private class RelocationTestFixture
    {
        public string DbName { get; } = Guid.NewGuid().ToString();
        public Guid FacilityId { get; set; } = Guid.NewGuid();
        public Guid Facility2Id { get; set; } = Guid.NewGuid();
        public Guid ClientId { get; set; } = Guid.NewGuid();
        public Guid Client2Id { get; set; } = Guid.NewGuid();

        public Guid PickZoneId { get; set; } = Guid.NewGuid();
        public Guid ReserveZoneId { get; set; } = Guid.NewGuid();
        public Guid ColdZoneId { get; set; } = Guid.NewGuid();

        // Locations
        public Guid GoldenZonePickLocation1Id { get; set; } = Guid.NewGuid(); // Level 1, Z = 1.1m (close to dock)
        public Guid GoldenZonePickLocation2Id { get; set; } = Guid.NewGuid(); // Level 1, Z = 1.1m
        public Guid IntermediateRackLocationId { get; set; } = Guid.NewGuid();  // Level 2, Z = 2.2m
        public Guid UpperReserveLocationId { get; set; } = Guid.NewGuid();      // Level 4, Z = 6.0m
        public Guid HighRackLocationId { get; set; } = Guid.NewGuid();          // Level 5, Z = 9.0m
        public Guid CantileverLocationId { get; set; } = Guid.NewGuid();        // Cantilever
        public Guid ColdLocationId { get; set; } = Guid.NewGuid();              // Cold storage
        public Guid DamagedLocationId { get; set; } = Guid.NewGuid();           // Damaged
    }

    private static ApplicationDbContext CreateContext(
        string dbName,
        Guid? tenantId = null,
        Guid? facilityId = null,
        bool isStaff = true,
        bool isSuperAdmin = false)
    {
        var currentUser = new TestCurrentUserContext
        {
            TenantId = tenantId,
            FacilityId = facilityId,
            UserType = isStaff ? UserType.Staff : UserType.Client,
            RolesList = isSuperAdmin ? ["SuperAdmin"] : [],
            PermissionsList = [Permissions.Analytics.SlottingView, Permissions.Analytics.SlottingManage]
        };

        return TestDbContextFactory.Create(
            new TestTenantProvider { TenantId = tenantId, BypassTenantFilter = isStaff || isSuperAdmin },
            currentUser,
            dbName);
    }

    private static async Task<RelocationTestFixture> SeedEnvironmentAsync()
    {
        var fix = new RelocationTestFixture();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);

        var facility = new Facility
        {
            Id = fix.FacilityId,
            FacilityCode = "FAC-RELOC",
            Name = "Relocation Engineering Facility",
            IsActive = true
        };
        var facility2 = new Facility
        {
            Id = fix.Facility2Id,
            FacilityCode = "FAC-ISOLATED",
            Name = "Isolated Facility",
            IsActive = true
        };
        context.Facilities.AddRange(facility, facility2);

        var pickZone = new WarehouseZone
        {
            Id = fix.PickZoneId,
            FacilityId = fix.FacilityId,
            ZoneCode = "PICK-ZONE",
            Name = "Forward Pick Face Zone",
            ZoneType = WarehouseZoneType.PickFace,
            IsActive = true
        };
        var reserveZone = new WarehouseZone
        {
            Id = fix.ReserveZoneId,
            FacilityId = fix.FacilityId,
            ZoneCode = "RES-ZONE",
            Name = "Upper Bulk Reserve Zone",
            ZoneType = WarehouseZoneType.BulkStorage,
            IsActive = true
        };
        var coldZone = new WarehouseZone
        {
            Id = fix.ColdZoneId,
            FacilityId = fix.FacilityId,
            ZoneCode = "COLD-ZONE",
            Name = "Refrigerated / Cold Storage Zone",
            ZoneType = WarehouseZoneType.ColdStorage,
            IsActive = true
        };
        context.WarehouseZones.AddRange(pickZone, reserveZone, coldZone);

        // Golden Zone Pick Location 1 (Dock proximity = 15m)
        var locGolden1 = new Location
        {
            Id = fix.GoldenZonePickLocation1Id,
            FacilityId = fix.FacilityId,
            ZoneId = fix.PickZoneId,
            LocationCode = "LOC-G1",
            AisleCode = "A01",
            RackCode = "R01",
            ShelfLevel = 1,
            XCoord = 10m,
            YCoord = 5m,
            ZCoord = 1.1m,
            LengthCm = 120m,
            WidthCm = 80m,
            HeightCm = 150m,
            MaxWeightKg = 1000m,
            IsActive = true,
            IsPickable = true,
            LocationType = LocationType.Rack
        };

        // Golden Zone Pick Location 2
        var locGolden2 = new Location
        {
            Id = fix.GoldenZonePickLocation2Id,
            FacilityId = fix.FacilityId,
            ZoneId = fix.PickZoneId,
            LocationCode = "LOC-G2",
            AisleCode = "A01",
            RackCode = "R02",
            ShelfLevel = 1,
            XCoord = 12m,
            YCoord = 5m,
            ZCoord = 1.1m,
            LengthCm = 120m,
            WidthCm = 80m,
            HeightCm = 150m,
            MaxWeightKg = 1000m,
            IsActive = true,
            IsPickable = true,
            LocationType = LocationType.Rack
        };

        // Intermediate Rack Location
        var locInter = new Location
        {
            Id = fix.IntermediateRackLocationId,
            FacilityId = fix.FacilityId,
            ZoneId = fix.PickZoneId,
            LocationCode = "LOC-INT",
            AisleCode = "A02",
            RackCode = "R01",
            ShelfLevel = 2,
            XCoord = 20m,
            YCoord = 10m,
            ZCoord = 2.2m,
            LengthCm = 120m,
            WidthCm = 80m,
            HeightCm = 150m,
            MaxWeightKg = 1000m,
            IsActive = true,
            IsPickable = true,
            LocationType = LocationType.Rack
        };

        // Upper Reserve Location (Level 4, Z = 6.0m)
        var locReserve = new Location
        {
            Id = fix.UpperReserveLocationId,
            FacilityId = fix.FacilityId,
            ZoneId = fix.ReserveZoneId,
            LocationCode = "LOC-RES",
            AisleCode = "A05",
            RackCode = "R03",
            ShelfLevel = 4,
            XCoord = 50m,
            YCoord = 30m,
            ZCoord = 6.0m,
            LengthCm = 120m,
            WidthCm = 80m,
            HeightCm = 150m,
            MaxWeightKg = 1000m,
            IsActive = true,
            IsPickable = true,
            LocationType = LocationType.Rack
        };

        // High Rack Location (Level 5, Z = 9.0m)
        var locHigh = new Location
        {
            Id = fix.HighRackLocationId,
            FacilityId = fix.FacilityId,
            ZoneId = fix.ReserveZoneId,
            LocationCode = "LOC-HIGH",
            AisleCode = "A06",
            RackCode = "R04",
            ShelfLevel = 5,
            XCoord = 60m,
            YCoord = 40m,
            ZCoord = 9.0m,
            LengthCm = 120m,
            WidthCm = 80m,
            HeightCm = 150m,
            MaxWeightKg = 1000m,
            IsActive = true,
            IsPickable = true,
            LocationType = LocationType.Rack
        };

        // Cantilever Location
        var locCantilever = new Location
        {
            Id = fix.CantileverLocationId,
            FacilityId = fix.FacilityId,
            ZoneId = fix.ReserveZoneId,
            LocationCode = "LOC-CANT",
            AisleCode = "A07",
            RackCode = "R01",
            ShelfLevel = 1,
            XCoord = 70m,
            YCoord = 20m,
            ZCoord = 1.0m,
            LengthCm = 300m,
            WidthCm = 100m,
            HeightCm = 200m,
            MaxWeightKg = 2000m,
            IsActive = true,
            IsPickable = true,
            LocationType = LocationType.Cantilever
        };

        // Cold Location
        var locCold = new Location
        {
            Id = fix.ColdLocationId,
            FacilityId = fix.FacilityId,
            ZoneId = fix.ColdZoneId,
            LocationCode = "LOC-COLD",
            AisleCode = "C01",
            RackCode = "R01",
            ShelfLevel = 1,
            XCoord = 30m,
            YCoord = 50m,
            ZCoord = 1.0m,
            LengthCm = 120m,
            WidthCm = 80m,
            HeightCm = 150m,
            MaxWeightKg = 1000m,
            IsActive = true,
            IsPickable = true,
            LocationType = LocationType.Rack
        };

        // Damaged Location
        var locDamaged = new Location
        {
            Id = fix.DamagedLocationId,
            FacilityId = fix.FacilityId,
            ZoneId = fix.PickZoneId,
            LocationCode = "LOC-DMG",
            AisleCode = "A01",
            RackCode = "R03",
            ShelfLevel = 1,
            IsActive = true,
            IsDamaged = true,
            IsPickable = false,
            LengthCm = 120m,
            WidthCm = 80m,
            HeightCm = 150m,
            MaxWeightKg = 1000m
        };

        context.Locations.AddRange(locGolden1, locGolden2, locInter, locReserve, locHigh, locCantilever, locCold, locDamaged);
        await context.SaveChangesAsync();

        return fix;
    }

    private static SkuVelocityClassificationRecord CreateVelocityRecord(
        Guid clientId,
        Guid facilityId,
        Guid itemId,
        string sku,
        AbcClassificationCategory abc,
        InventoryMovementVelocityCategory vel,
        DateTime evalTime,
        int picks = 50,
        decimal units = 100m)
    {
        return new SkuVelocityClassificationRecord
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            FacilityId = facilityId,
            ItemId = itemId,
            Sku = sku,
            AbcClass = abc,
            VelocityCategory = vel,
            ObservationWindow = VelocityObservationWindow.Days90,
            CalculationPeriodStartUtc = evalTime.AddDays(-90),
            CalculationPeriodEndUtc = evalTime,
            CalculatedAtUtc = evalTime,
            TotalPickTasksCount = picks,
            TotalUnitsDemanded = units,
            TotalDemandValue = units * 20m
        };
    }

    #region 1. Benefit Ranking Acceptance Criterion

    [Fact]
    public void BenefitRanking_HigherNetSavingsRanksHigher_WhenOtherFactorsEqual()
    {
        // Criterion 1: A candidate with higher projected net monthly savings ranks above a candidate with lower savings when urgency and suitability are equal.
        var policy = SlottingPolicy.Create("TestPolicy");
        var service = new RelocationRecommendationService(null!, null!, NullLogger<RelocationRecommendationService>.Instance);

        decimal urgency = 30.0m;
        decimal suitability = 75.0m;
        decimal totalRelocCost = 25.00m;

        // Candidate A: Higher net benefit ($45.00)
        decimal netBenefitA = 45.00m;
        decimal priorityScoreA = service.CalculatePriorityScore(netBenefitA, urgency, suitability, totalRelocCost, policy, isFeasible: true);

        // Candidate B: Lower net benefit ($15.00)
        decimal netBenefitB = 15.00m;
        decimal priorityScoreB = service.CalculatePriorityScore(netBenefitB, urgency, suitability, totalRelocCost, policy, isFeasible: true);

        Assert.True(priorityScoreA > priorityScoreB,
            $"Candidate A priority ({priorityScoreA}) should exceed Candidate B priority ({priorityScoreB}) due to higher net financial savings.");
    }

    #endregion

    #region 2. Operational Urgency Impact Acceptance Criterion

    [Fact]
    public void OperationalUrgency_ElevatesPriorityScore_AccordingToPolicyWeights()
    {
        // Criterion 2: A candidate with high operational urgency elevates its ranking according to policy weights.
        var policy = SlottingPolicy.Create("UrgencyTestPolicy");
        var service = new RelocationRecommendationService(null!, null!, NullLogger<RelocationRecommendationService>.Instance);

        decimal netBenefit = 20.00m;
        decimal suitability = 70.0m;
        decimal totalRelocCost = 25.00m;

        // Low Urgency Candidate (U = 10.0)
        decimal priorityScoreLowUrgency = service.CalculatePriorityScore(netBenefit, 10.0m, suitability, totalRelocCost, policy, isFeasible: true);

        // High Urgency Candidate (U = 80.0)
        decimal priorityScoreHighUrgency = service.CalculatePriorityScore(netBenefit, 80.0m, suitability, totalRelocCost, policy, isFeasible: true);

        Assert.True(priorityScoreHighUrgency > priorityScoreLowUrgency,
            $"Candidate with high urgency ({priorityScoreHighUrgency}) must rank higher than low urgency ({priorityScoreLowUrgency}).");
        Assert.True(priorityScoreHighUrgency - priorityScoreLowUrgency >= 20.0m,
            "Urgency delta of 70 points with w_u = 0.30 should elevate composite priority score by ~21 points.");
    }

    [Fact]
    public void OperationalUrgency_EvaluatesVelocityMismatchCongestionSeasonalAndErgoComponents()
    {
        var policy = SlottingPolicy.Create("UrgencyComponentsPolicy");
        var service = new RelocationRecommendationService(null!, null!, NullLogger<RelocationRecommendationService>.Instance);

        // 1. Fast Mover (Class A) stored in upper reserve (Level 4, Z = 6.0m)
        var highLocation = new Location { ShelfLevel = 4, ZCoord = 6.0m };
        var itemA = new Item { WeightKg = 5.0m, Sku = "SKU-A" };
        var (uVel, reasonVel) = service.EvaluateOperationalUrgency(itemA, highLocation, policy, "ClassA", "FastMoving", "Surging");
        Assert.True(uVel >= 75.0m, $"Urgency should combine velocity mismatch (+45) and surging (+30), got {uVel}");
        Assert.Contains("Fast mover stored in high rack", reasonVel);

        // 2. Slow Mover (Class C) occupying prime Golden Zone pick face
        var goldenLocation = new Location { ShelfLevel = 1, ZCoord = 1.1m };
        var itemC = new Item { WeightKg = 5.0m, Sku = "SKU-C" };
        var (uCong, reasonCong) = service.EvaluateOperationalUrgency(itemC, goldenLocation, policy, "ClassC", "SlowMoving", "Inactive");
        Assert.Equal(40.0m, uCong);
        Assert.Contains("Slow mover blocking prime Golden Zone pick face", reasonCong);

        // 3. Ergonomic Safety Hazard: Heavy Item (30kg >= 20kg) above waist height (Z = 2.2m)
        var ergoLocation = new Location { ShelfLevel = 3, ZCoord = 2.2m };
        var itemHeavy = new Item { WeightKg = 30.0m, Sku = "SKU-HEAVY" };
        var (uErgo, reasonErgo) = service.EvaluateOperationalUrgency(itemHeavy, ergoLocation, policy, "ClassB", "MediumMoving", "Stable");
        Assert.Equal(30.0m, uErgo);
        Assert.Contains("lifting hazard", reasonErgo);
    }

    #endregion

    #region 3. Hard-Gate Supremacy Acceptance Criterion

    [Fact]
    public void HardGateSupremacy_InfeasibleProposal_ScoresZeroPriorityAndNotFeasible()
    {
        // Criterion 3: No benefit or urgency can override hard physical or regulatory gates.
        var policy = SlottingPolicy.Create("HardGatePolicy");
        var service = new RelocationRecommendationService(null!, null!, NullLogger<RelocationRecommendationService>.Instance);

        // Massive savings ($500/mo) and maximum urgency (100.0)
        decimal hugeBenefit = 500.00m;
        decimal maxUrgency = 100.0m;
        decimal maxSuitability = 100.0m;
        decimal lowCost = 10.00m;

        decimal scoreFeasible = service.CalculatePriorityScore(hugeBenefit, maxUrgency, maxSuitability, lowCost, policy, isFeasible: true);
        decimal scoreInfeasible = service.CalculatePriorityScore(hugeBenefit, maxUrgency, maxSuitability, lowCost, policy, isFeasible: false);

        Assert.True(scoreFeasible > 80.0m);
        Assert.Equal(0.0m, scoreInfeasible);
    }

    [Fact]
    public async Task HardGateSupremacy_HeightWeightTempAndCantileverViolations_RejectedInPrioritization()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new RelocationRecommendationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<RelocationRecommendationService>.Instance);

        DateTime evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        // 1. Height clearance violation: 160cm item in 150cm location
        var itemTall = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-TALL", HeightCm = 160m, LengthCm = 50m, WidthCm = 50m, WeightKg = 10m };
        // 2. Weight capacity violation: 1200kg item in 1000kg location
        var itemHeavy = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-TOO-HEAVY", HeightCm = 50m, LengthCm = 50m, WidthCm = 50m, WeightKg = 1200m };
        // 3. Temperature incompatibility: Frozen item in non-cold location
        var itemFrozen = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-FROZEN", HeightCm = 50m, LengthCm = 50m, WidthCm = 50m, WeightKg = 10m, StorageTempClass = StorageTempClass.Frozen };
        // 4. Cantilever length restriction: Short item (80cm < 150cm)
        var itemShort = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-SHORT", HeightCm = 50m, LengthCm = 80m, WidthCm = 50m, WeightKg = 10m };

        context.Items.AddRange(itemTall, itemHeavy, itemFrozen, itemShort);

        // Put stock in Upper Reserve to generate promotion proposals
        context.StockOnHand.AddRange(
            new StockOnHand { Id = Guid.NewGuid(), ClientId = fix.ClientId, FacilityId = fix.FacilityId, ItemId = itemTall.Id, LocationId = fix.UpperReserveLocationId, QtyAvailable = 1m },
            new StockOnHand { Id = Guid.NewGuid(), ClientId = fix.ClientId, FacilityId = fix.FacilityId, ItemId = itemHeavy.Id, LocationId = fix.UpperReserveLocationId, QtyAvailable = 1m },
            new StockOnHand { Id = Guid.NewGuid(), ClientId = fix.ClientId, FacilityId = fix.FacilityId, ItemId = itemFrozen.Id, LocationId = fix.UpperReserveLocationId, QtyAvailable = 1m },
            new StockOnHand { Id = Guid.NewGuid(), ClientId = fix.ClientId, FacilityId = fix.FacilityId, ItemId = itemShort.Id, LocationId = fix.UpperReserveLocationId, QtyAvailable = 1m }
        );

        // Add velocity records marking them Class A
        context.SkuVelocityClassificationRecords.AddRange(
            CreateVelocityRecord(fix.ClientId, fix.FacilityId, itemTall.Id, itemTall.Sku, AbcClassificationCategory.ClassA, InventoryMovementVelocityCategory.FastMoving, evalTime),
            CreateVelocityRecord(fix.ClientId, fix.FacilityId, itemHeavy.Id, itemHeavy.Sku, AbcClassificationCategory.ClassA, InventoryMovementVelocityCategory.FastMoving, evalTime),
            CreateVelocityRecord(fix.ClientId, fix.FacilityId, itemFrozen.Id, itemFrozen.Sku, AbcClassificationCategory.ClassA, InventoryMovementVelocityCategory.FastMoving, evalTime),
            CreateVelocityRecord(fix.ClientId, fix.FacilityId, itemShort.Id, itemShort.Sku, AbcClassificationCategory.ClassA, InventoryMovementVelocityCategory.FastMoving, evalTime)
        );

        await context.SaveChangesAsync();

        var result = await service.PrioritizeRelocationsAsync(new RelocationPrioritizationRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            AsOfDateUtc = evalTime
        });

        Assert.NotNull(result);
        foreach (var rec in result.PrioritizedRecommendations)
        {
            Assert.True(rec.IsOperationallyFeasible);
            Assert.True(rec.PriorityScore > 0m);
            Assert.Empty(rec.ViolatedConstraints);
        }
    }

    #endregion

    #region 4. Negative Net Benefit Policy Acceptance Criterion

    [Theory]
    [InlineData("DisqualifyNegativeBenefit", -10.0, 80.0, false)] // Disqualifies negative benefit even if high urgency
    [InlineData("DisqualifyNegativeBenefit", 15.0, 30.0, true)]   // Accepts positive benefit
    [InlineData("AllowIfHighUrgency", -10.0, 75.0, true)]          // Accepts negative benefit when U >= 70.0
    [InlineData("AllowIfHighUrgency", -10.0, 65.0, false)]         // Rejects negative benefit when U < 70.0
    [InlineData("AllowIfHighUrgency", 15.0, 30.0, true)]           // Accepts positive benefit regardless of U
    [InlineData("AllowAll", -10.0, 20.0, true)]                    // Accepts negative benefit unconditionally
    [InlineData("AllowAll", 20.0, 50.0, true)]                     // Accepts positive benefit
    public void NegativeNetBenefitPolicy_EvaluatesAccordingToPolicyDirectives(
        string policyMode,
        decimal netBenefit,
        decimal urgencyScore,
        bool expectedAllowed)
    {
        // Criterion 4: Negative net monthly benefit is gated strictly by policy.
        bool allowed = RelocationRecommendationService.IsNegativeBenefitAllowed(
            netBenefit: netBenefit,
            urgencyScore: urgencyScore,
            policyName: policyMode,
            urgencyThreshold: 70.0m);

        Assert.Equal(expectedAllowed, allowed);
    }

    #endregion

    #region 5. Disruption Cost Invariant Acceptance Criterion

    [Fact]
    public void DisruptionCostInvariant_TracksHandlingMovementAndDisruptionSeparately()
    {
        // Criterion 5: Relocation cost must be transparently decomposed into handling, movement, and disruption.
        // Disruption cost cannot silently disappear.
        var policy = SlottingPolicy.Create("DisruptionPolicy");
        policy.BaseDisruptionCostUsd = 15.00m;
        var service = new RelocationRecommendationService(null!, null!, NullLogger<RelocationRecommendationService>.Instance);

        var currLoc = new Location { XCoord = 10m, YCoord = 10m, ZCoord = 1.0m, ShelfLevel = 1 };
        var destPickFace = new Location
        {
            XCoord = 30m,
            YCoord = 30m,
            ZCoord = 1.0m,
            ShelfLevel = 1,
            Zone = new WarehouseZone { ZoneType = WarehouseZoneType.PickFace }
        };

        // Standard Shift
        var econStd = service.CalculateTransparentEconomics(
            roundTripTravelSavedMeters: 20m,
            monthlyPicks: 100m,
            quantity: 50m,
            weightKg: 25.0m, // Heavy surcharge applies (>20kg)
            currentLocation: currLoc,
            destinationLocation: destPickFace,
            policy: policy,
            shiftType: "Standard");

        // Assert handling cost includes base unit handling ($0.10 * 50 = $5.00) + heavy tier ((25-20)/10 * $5 = $2.50) = $7.50
        Assert.Equal(7.50m, econStd.handlingCost);

        // Assert movement transit cost > 0
        Assert.True(econStd.movementCost > 0m);

        // Assert disruption cost: $15.00 base * 1.5 pick-face factor * 1.0 shift = $22.50
        Assert.Equal(22.50m, econStd.disruptionCost);
        Assert.Equal("High", RelocationRecommendationService.DetermineDisruptionLevel(econStd.disruptionCost));

        // Peak Shift: disruption multiplied by 1.4 -> $15.00 * 1.5 * 1.4 = $31.50
        var econPeak = service.CalculateTransparentEconomics(
            roundTripTravelSavedMeters: 20m,
            monthlyPicks: 100m,
            quantity: 50m,
            weightKg: 25.0m,
            currentLocation: currLoc,
            destinationLocation: destPickFace,
            policy: policy,
            shiftType: "Peak");

        Assert.Equal(31.50m, econPeak.disruptionCost);

        // Night Shift: disruption multiplied by 0.7 -> $15.00 * 1.5 * 0.7 = $15.75
        var econNight = service.CalculateTransparentEconomics(
            roundTripTravelSavedMeters: 20m,
            monthlyPicks: 100m,
            quantity: 50m,
            weightKg: 25.0m,
            currentLocation: currLoc,
            destinationLocation: destPickFace,
            policy: policy,
            shiftType: "Night");

        Assert.Equal(15.75m, econNight.disruptionCost);

        // Total relocation cost is strictly the sum of the three
        Assert.Equal(Math.Round(econStd.handlingCost + econStd.movementCost + econStd.disruptionCost, 2),
                     Math.Round(econStd.amortizedMonthlyCost * policy.AmortizationMonths, 2));
    }

    #endregion

    #region 6. Deterministic Ranking & Tie-Breaking Acceptance Criterion

    [Fact]
    public async Task DeterministicRankingAndTieBreaking_BitForBitIdenticalAcrossRepeatedRuns()
    {
        // Criterion 6: Deterministic tie-breaking sequence:
        // P DESC -> B_net DESC -> U DESC -> SKU ASC -> Location ASC.
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new RelocationRecommendationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<RelocationRecommendationService>.Instance);

        // Seed 4 items with matching properties to trigger tie-breaking rules
        var item1 = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-DELTA", WeightKg = 5m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m };
        var item2 = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-ALPHA", WeightKg = 5m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m };
        var item3 = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-CHARLIE", WeightKg = 5m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m };
        var item4 = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-BRAVO", WeightKg = 5m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m };

        context.Items.AddRange(item1, item2, item3, item4);

        // Seed stock for all 4 in Upper Reserve
        context.StockOnHand.AddRange(
            new StockOnHand { Id = Guid.NewGuid(), ClientId = fix.ClientId, FacilityId = fix.FacilityId, ItemId = item1.Id, LocationId = fix.UpperReserveLocationId, QtyAvailable = 10m },
            new StockOnHand { Id = Guid.NewGuid(), ClientId = fix.ClientId, FacilityId = fix.FacilityId, ItemId = item2.Id, LocationId = fix.UpperReserveLocationId, QtyAvailable = 10m },
            new StockOnHand { Id = Guid.NewGuid(), ClientId = fix.ClientId, FacilityId = fix.FacilityId, ItemId = item3.Id, LocationId = fix.UpperReserveLocationId, QtyAvailable = 10m },
            new StockOnHand { Id = Guid.NewGuid(), ClientId = fix.ClientId, FacilityId = fix.FacilityId, ItemId = item4.Id, LocationId = fix.UpperReserveLocationId, QtyAvailable = 10m }
        );

        DateTime evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        context.SkuVelocityClassificationRecords.AddRange(
            CreateVelocityRecord(fix.ClientId, fix.FacilityId, item1.Id, item1.Sku, AbcClassificationCategory.ClassA, InventoryMovementVelocityCategory.FastMoving, evalTime),
            CreateVelocityRecord(fix.ClientId, fix.FacilityId, item2.Id, item2.Sku, AbcClassificationCategory.ClassA, InventoryMovementVelocityCategory.FastMoving, evalTime),
            CreateVelocityRecord(fix.ClientId, fix.FacilityId, item3.Id, item3.Sku, AbcClassificationCategory.ClassA, InventoryMovementVelocityCategory.FastMoving, evalTime),
            CreateVelocityRecord(fix.ClientId, fix.FacilityId, item4.Id, item4.Sku, AbcClassificationCategory.ClassA, InventoryMovementVelocityCategory.FastMoving, evalTime)
        );

        await context.SaveChangesAsync();

        var run1 = await service.PrioritizeRelocationsAsync(new RelocationPrioritizationRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            AsOfDateUtc = evalTime
        });

        var run2 = await service.PrioritizeRelocationsAsync(new RelocationPrioritizationRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            AsOfDateUtc = evalTime
        });

        Assert.Equal(run1.PrioritizedRecommendations.Count, run2.PrioritizedRecommendations.Count);
        for (int i = 0; i < run1.PrioritizedRecommendations.Count; i++)
        {
            var r1 = run1.PrioritizedRecommendations[i];
            var r2 = run2.PrioritizedRecommendations[i];
            Assert.Equal(r1.ItemSku, r2.ItemSku);
            Assert.Equal(r1.ProposedLocationCode, r2.ProposedLocationCode);
            Assert.Equal(r1.PriorityScore, r2.PriorityScore);
            Assert.Equal(r1.NetMonthlyBenefitUsd, r2.NetMonthlyBenefitUsd);
            Assert.Equal(r1.RankingRank, r2.RankingRank);
        }

        // Check tie-breaking: when scores match, alphabetical SKU order holds
        var skus = run1.PrioritizedRecommendations.Select(r => r.ItemSku).ToList();
        var sortedSkus = skus.OrderBy(s => s, StringComparer.Ordinal).ToList();
        Assert.Equal(sortedSkus, skus);
    }

    #endregion

    #region 7. Tenant & Facility Isolation Acceptance Criterion

    [Fact]
    public async Task TenantAndFacilityIsolation_StrictlyPartitionsRelocationProposals()
    {
        // Criterion 7: Candidate proposals are strictly isolated by Tenant and Facility.
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new RelocationRecommendationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<RelocationRecommendationService>.Instance);

        // Item for Client 1 in Facility 1
        var itemClient1 = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-C1", WeightKg = 5m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m };
        // Item for Client 2 in Facility 1
        var itemClient2 = new Item { Id = Guid.NewGuid(), ClientId = fix.Client2Id, Sku = "SKU-C2", WeightKg = 5m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m };
        context.Items.AddRange(itemClient1, itemClient2);

        // Put Client 2 stock in the golden zone location
        context.StockOnHand.Add(new StockOnHand
        {
            Id = Guid.NewGuid(),
            ClientId = fix.Client2Id,
            FacilityId = fix.FacilityId,
            ItemId = itemClient2.Id,
            LocationId = fix.GoldenZonePickLocation1Id,
            QtyAvailable = 10m
        });

        // Put Client 1 stock in upper reserve
        context.StockOnHand.Add(new StockOnHand
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = itemClient1.Id,
            LocationId = fix.UpperReserveLocationId,
            QtyAvailable = 10m
        });

        DateTime evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        context.SkuVelocityClassificationRecords.Add(
            CreateVelocityRecord(fix.ClientId, fix.FacilityId, itemClient1.Id, itemClient1.Sku, AbcClassificationCategory.ClassA, InventoryMovementVelocityCategory.FastMoving, evalTime, 100, 200m)
        );

        await context.SaveChangesAsync();

        // Run prioritization scoped to Client 1
        var result = await service.PrioritizeRelocationsAsync(new RelocationPrioritizationRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            AsOfDateUtc = evalTime
        });

        Assert.NotNull(result);
        // Client 1 should NEVER be proposed to move into GoldenZonePickLocation1Id because Client 2 stock is there
        foreach (var rec in result.PrioritizedRecommendations)
        {
            Assert.Equal(fix.ClientId, rec.ClientId);
            Assert.NotEqual(fix.GoldenZonePickLocation1Id, rec.ProposedLocationId);
        }
    }

    #endregion

    #region 8. Geometric Packing Feasibility Acceptance Criterion

    [Fact]
    public async Task GeometricPackingFeasibility_TwoCandidatesCannotOverlapPlanarFootprint()
    {
        // Criterion 8: Virtual state geometric packing ensures no 2D bounding-box collisions.
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new RelocationRecommendationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<RelocationRecommendationService>.Instance);

        // Location is 120cm x 80cm.
        // Two large items: 100cm x 70cm each. They cannot both fit on a 120cm x 80cm shelf without overlapping!
        var item1 = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-LARGE-1", LengthCm = 100m, WidthCm = 70m, HeightCm = 40m, WeightKg = 20m, AllowPlanarRotation = true, StackabilityFactor = 1 };
        var item2 = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-LARGE-2", LengthCm = 100m, WidthCm = 70m, HeightCm = 40m, WeightKg = 20m, AllowPlanarRotation = true, StackabilityFactor = 1 };
        context.Items.AddRange(item1, item2);

        // Put both in Upper Reserve
        context.StockOnHand.AddRange(
            new StockOnHand { Id = Guid.NewGuid(), ClientId = fix.ClientId, FacilityId = fix.FacilityId, ItemId = item1.Id, LocationId = fix.UpperReserveLocationId, QtyAvailable = 1m },
            new StockOnHand { Id = Guid.NewGuid(), ClientId = fix.ClientId, FacilityId = fix.FacilityId, ItemId = item2.Id, LocationId = fix.UpperReserveLocationId, QtyAvailable = 1m }
        );

        DateTime evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        context.SkuVelocityClassificationRecords.AddRange(
            CreateVelocityRecord(fix.ClientId, fix.FacilityId, item1.Id, item1.Sku, AbcClassificationCategory.ClassA, InventoryMovementVelocityCategory.FastMoving, evalTime, 100, 200m),
            CreateVelocityRecord(fix.ClientId, fix.FacilityId, item2.Id, item2.Sku, AbcClassificationCategory.ClassA, InventoryMovementVelocityCategory.FastMoving, evalTime, 100, 200m)
        );

        await context.SaveChangesAsync();

        var result = await service.PrioritizeRelocationsAsync(new RelocationPrioritizationRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            AsOfDateUtc = evalTime
        });

        Assert.NotNull(result);
        // Only one of the two large items can occupy GoldenZonePickLocation1Id
        int movesToLoc1 = result.PrioritizedRecommendations.Count(r => r.ProposedLocationId == fix.GoldenZonePickLocation1Id);
        Assert.True(movesToLoc1 <= 1, $"At most one large item could be packed into LOC-G1 without overlap; found {movesToLoc1}.");
    }

    #endregion

    #region 9. Populated Operational Ledger Invariance Acceptance Criterion

    [Fact]
    public async Task PopulatedOperationalLedgerInvariance_AuditAcrossEightTables_ConfirmsZeroMutations()
    {
        // Criterion 9: Audit all 8 operational ledger tables before and after prioritization and approval:
        // StockOnHand, Items, Locations, OutboundOrders, OutboundOrderLines, PickTasks, InventoryTransactions, ReceiptLines.
        // Invariant: Exactly zero mutations occurred.
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new RelocationRecommendationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"], UserId = Guid.NewGuid() },
            NullLogger<RelocationRecommendationService>.Instance);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        // 1. Seed Items
        var item1 = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-LEDGER-1", WeightKg = 5m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m, AbcVelocity = AbcVelocity.A };
        item1.RecalculateVolume();
        var item2 = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-LEDGER-2", WeightKg = 6m, LengthCm = 35m, WidthCm = 25m, HeightCm = 20m, AbcVelocity = AbcVelocity.A };
        item2.RecalculateVolume();
        context.Items.AddRange(item1, item2);

        // 2. Seed StockOnHand across partitions (fitting location physical capacity)
        var stock1 = new StockOnHand { Id = Guid.NewGuid(), ClientId = fix.ClientId, FacilityId = fix.FacilityId, ItemId = item1.Id, LocationId = fix.UpperReserveLocationId, QtyAvailable = 5m, QtyAllocated = 1m, QtyReserved = 1m };
        var stock2 = new StockOnHand { Id = Guid.NewGuid(), ClientId = fix.ClientId, FacilityId = fix.FacilityId, ItemId = item2.Id, LocationId = fix.UpperReserveLocationId, QtyAvailable = 4m, QtyAllocated = 0m, QtyOnHold = 1m };
        context.StockOnHand.AddRange(stock1, stock2);

        // 3. Seed Outbound Orders & Lines
        var ord = new OutboundOrder
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            OrderNumber = "ORD-LEDGER-001",
            Status = OutboundOrderStatus.Completed,
            OrderDateUtc = evalTime.AddDays(-5)
        };
        context.OutboundOrders.Add(ord);
        context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = ord.Id, ItemId = item1.Id, QuantityOrdered = 1m, QuantityShipped = 1m });

        // 4. Seed PickTasks
        var pt = new PickTask { Id = Guid.NewGuid(), FacilityId = fix.FacilityId, OutboundOrderId = ord.Id, ItemId = item1.Id, SourceLocationId = fix.UpperReserveLocationId, QuantityToPick = 1m, QuantityPicked = 1m, Status = PickTaskStatus.Completed };
        context.PickTasks.Add(pt);

        // 5. Seed InventoryTransactions
        var tx = new InventoryTransaction { Id = 1001L, FacilityId = fix.FacilityId, ClientId = fix.ClientId, ItemId = item1.Id, FromLocationId = fix.UpperReserveLocationId, TxType = InventoryTxType.Pick, Quantity = 1m, TxTimestamp = evalTime.AddDays(-2) };
        context.InventoryTransactions.Add(tx);

        // 6. Seed ReceiptLines
        var rl = new ReceiptLine { Id = Guid.NewGuid(), ReceivingSessionId = Guid.NewGuid(), ItemId = item1.Id, QuantityReceived = 10m, ScannedAtUtc = evalTime.AddDays(-15) };
        context.ReceiptLines.Add(rl);

        // Velocity records
        context.SkuVelocityClassificationRecords.AddRange(
            CreateVelocityRecord(fix.ClientId, fix.FacilityId, item1.Id, item1.Sku, AbcClassificationCategory.ClassA, InventoryMovementVelocityCategory.FastMoving, evalTime, 80, 150m),
            CreateVelocityRecord(fix.ClientId, fix.FacilityId, item2.Id, item2.Sku, AbcClassificationCategory.ClassA, InventoryMovementVelocityCategory.FastMoving, evalTime, 80, 150m)
        );

        await context.SaveChangesAsync();

        // CAPTURE DEEP SNAPSHOTS BEFORE PRIORITIZATION & APPROVAL
        var stockBefore = context.StockOnHand.AsNoTracking().Select(s => new { s.Id, s.QtyAvailable, s.QtyAllocated, s.QtyReserved, s.QtyQuarantined, s.QtyDamaged, s.QtyOnHold, s.LocationId, s.ItemId }).ToList();
        var itemsBefore = context.Items.AsNoTracking().Select(i => new { i.Id, i.Sku, i.WeightKg, i.LengthCm, i.WidthCm, i.HeightCm, i.AbcVelocity }).ToList();
        var locationsBefore = context.Locations.AsNoTracking().Select(l => new { l.Id, l.LocationCode, l.IsActive, l.IsPickable, l.IsDamaged, l.IsLocked }).ToList();
        var ordersBefore = context.OutboundOrders.AsNoTracking().Select(o => new { o.Id, o.OrderNumber, o.Status, o.FacilityId, o.ClientId }).ToList();
        var linesBefore = context.OutboundOrderLines.AsNoTracking().Select(l => new { l.Id, l.OutboundOrderId, l.ItemId, l.QuantityOrdered, l.QuantityShipped }).ToList();
        var picksBefore = context.PickTasks.AsNoTracking().Select(p => new { p.Id, p.Status, p.QuantityToPick, p.QuantityPicked, p.SourceLocationId }).ToList();
        var txBefore = context.InventoryTransactions.AsNoTracking().Select(t => new { t.Id, t.Quantity, t.TxType, t.FromLocationId, t.ToLocationId, t.ItemId }).ToList();
        var receiptsBefore = context.ReceiptLines.AsNoTracking().Select(r => new { r.Id, r.ItemId, r.QuantityReceived, r.ScannedAtUtc }).ToList();

        // 1. Execute Prioritization
        var planResult = await service.PrioritizeRelocationsAsync(new RelocationPrioritizationRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            AsOfDateUtc = evalTime
        });

        Assert.NotNull(planResult);
        Assert.NotEmpty(planResult.PrioritizedRecommendations);

        // 2. Submit for Review (Mandatory before approval)
        await service.SubmitBatchForReviewAsync(planResult.BatchId);

        // 3. Execute Approval
        var approveResult = await service.ApproveRelocationPlanAsync(new RelocationApprovalRequestDto
        {
            BatchId = planResult.BatchId,
            ApprovedRecommendationIds = planResult.PrioritizedRecommendations.Select(r => r.RecommendationId).ToList(),
            ApprovalNotes = "Audited and approved by engineering reviewer."
        });

        Assert.Equal(0, approveResult.PhysicalInventoryTransfersTriggered);
        Assert.Equal(0, approveResult.LedgerMutationsOccurred);

        // CAPTURE DEEP SNAPSHOTS AFTER PRIORITIZATION & APPROVAL
        var stockAfter = context.StockOnHand.AsNoTracking().Select(s => new { s.Id, s.QtyAvailable, s.QtyAllocated, s.QtyReserved, s.QtyQuarantined, s.QtyDamaged, s.QtyOnHold, s.LocationId, s.ItemId }).ToList();
        var itemsAfter = context.Items.AsNoTracking().Select(i => new { i.Id, i.Sku, i.WeightKg, i.LengthCm, i.WidthCm, i.HeightCm, i.AbcVelocity }).ToList();
        var locationsAfter = context.Locations.AsNoTracking().Select(l => new { l.Id, l.LocationCode, l.IsActive, l.IsPickable, l.IsDamaged, l.IsLocked }).ToList();
        var ordersAfter = context.OutboundOrders.AsNoTracking().Select(o => new { o.Id, o.OrderNumber, o.Status, o.FacilityId, o.ClientId }).ToList();
        var linesAfter = context.OutboundOrderLines.AsNoTracking().Select(l => new { l.Id, l.OutboundOrderId, l.ItemId, l.QuantityOrdered, l.QuantityShipped }).ToList();
        var picksAfter = context.PickTasks.AsNoTracking().Select(p => new { p.Id, p.Status, p.QuantityToPick, p.QuantityPicked, p.SourceLocationId }).ToList();
        var txAfter = context.InventoryTransactions.AsNoTracking().Select(t => new { t.Id, t.Quantity, t.TxType, t.FromLocationId, t.ToLocationId, t.ItemId }).ToList();
        var receiptsAfter = context.ReceiptLines.AsNoTracking().Select(r => new { r.Id, r.ItemId, r.QuantityReceived, r.ScannedAtUtc }).ToList();

        // STRICT IDENTITY ASSERTION: ZERO MUTATIONS ACROSS ALL 8 TABLES
        Assert.Equal(stockBefore, stockAfter);
        Assert.Equal(itemsBefore, itemsAfter);
        Assert.Equal(locationsBefore, locationsAfter);
        Assert.Equal(ordersBefore, ordersAfter);
        Assert.Equal(linesBefore, linesAfter);
        Assert.Equal(picksBefore, picksAfter);
        Assert.Equal(txBefore, txAfter);
        Assert.Equal(receiptsBefore, receiptsAfter);
    }

    #endregion

    #region 10. Approval Boundary Acceptance Criterion

    [Fact]
    public async Task ApprovalBoundary_ApprovingBatchMarksApproved_WithoutMovingInventoryOrTransactions()
    {
        // Criterion 10: Approving a batch changes recommendation status to Approved without executing stock moves.
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var approverId = Guid.NewGuid();
        var service = new RelocationRecommendationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"], UserId = approverId },
            NullLogger<RelocationRecommendationService>.Instance);

        var item = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-APPROVE", WeightKg = 5m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m };
        context.Items.Add(item);
        context.StockOnHand.Add(new StockOnHand { Id = Guid.NewGuid(), ClientId = fix.ClientId, FacilityId = fix.FacilityId, ItemId = item.Id, LocationId = fix.UpperReserveLocationId, QtyAvailable = 10m });

        DateTime evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        context.SkuVelocityClassificationRecords.Add(
            CreateVelocityRecord(fix.ClientId, fix.FacilityId, item.Id, item.Sku, AbcClassificationCategory.ClassA, InventoryMovementVelocityCategory.FastMoving, evalTime)
        );
        await context.SaveChangesAsync();

        var plan = await service.PrioritizeRelocationsAsync(new RelocationPrioritizationRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            AsOfDateUtc = evalTime
        });

        Assert.NotEmpty(plan.PrioritizedRecommendations);
        var recId = plan.PrioritizedRecommendations.First().RecommendationId;

        // Submit for Review (Mandatory before approval)
        await service.SubmitBatchForReviewAsync(plan.BatchId);

        // Execute Approval
        var approveResult = await service.ApproveRelocationPlanAsync(new RelocationApprovalRequestDto
        {
            BatchId = plan.BatchId,
            ApprovedRecommendationIds = [recId],
            ApprovalNotes = "Administrative approval verified."
        });

        Assert.Equal(1, approveResult.ApprovedCount);
        Assert.Equal(0, approveResult.PhysicalInventoryTransfersTriggered);
        Assert.Equal(0, approveResult.LedgerMutationsOccurred);
        Assert.Equal(SlottingBatchStatus.Approved, approveResult.Status);

        // Verify batch and recommendation state in DB
        var persistedBatch = await context.SlottingRecommendationBatches
            .Include(b => b.Recommendations)
            .FirstOrDefaultAsync(b => b.Id == plan.BatchId);

        Assert.NotNull(persistedBatch);
        Assert.Equal(SlottingBatchStatus.Approved, persistedBatch.Status);
        Assert.Equal(approverId, persistedBatch.ApprovedBy);
        Assert.NotNull(persistedBatch.ApprovedAtUtc);
        Assert.Contains("Administrative approval verified", persistedBatch.Notes);

        var persistedRec = persistedBatch.Recommendations.First(r => r.Id == recId);
        Assert.Equal(SlottingRecommendationStatus.Approved, persistedRec.Status);

        // Verify inventory in DB: STILL in Upper Reserve, NOT moved to proposed location!
        var currentStock = await context.StockOnHand.FirstAsync(s => s.ItemId == item.Id);
        Assert.Equal(fix.UpperReserveLocationId, currentStock.LocationId);
    }

    [Fact]
    public async Task ApprovalBoundary_CannotApproveInfeasibleRecommendation_RejectsInfeasibleProposal()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new RelocationRecommendationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"], UserId = Guid.NewGuid() },
            NullLogger<RelocationRecommendationService>.Instance);

        var batch = SlottingRecommendationBatch.Create(fix.FacilityId, Guid.NewGuid(), "BATCH-TEST-INFEASIBLE");
        var infeasibleRec = new SlottingRecommendation
        {
            BatchId = batch.Id,
            ItemId = Guid.NewGuid(),
            ItemSku = "SKU-INFEASIBLE",
            ClientId = fix.ClientId,
            CurrentLocationId = fix.UpperReserveLocationId,
            ProposedLocationId = fix.GoldenZonePickLocation1Id,
            IsHardConstraintViolated = true, // Infeasible!
            IsOperationallyFeasible = false,
            Status = SlottingRecommendationStatus.Pending
        };
        batch.Recommendations.Add(infeasibleRec);
        batch.Status = SlottingBatchStatus.PendingApproval;
        context.SlottingRecommendationBatches.Add(batch);
        await context.SaveChangesAsync();

        var result = await service.ApproveRelocationPlanAsync(new RelocationApprovalRequestDto
        {
            BatchId = batch.Id,
            ApprovedRecommendationIds = [infeasibleRec.Id]
        });

        Assert.Equal(0, result.ApprovedCount);
        Assert.Equal(1, result.RejectedCount);

        var reloadedRec = await context.SlottingRecommendations.FirstAsync(r => r.Id == infeasibleRec.Id);
        Assert.Equal(SlottingRecommendationStatus.Rejected, reloadedRec.Status);
        Assert.Contains("hard constraint", reloadedRec.RejectionReason, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region 11. WebAPI Controller & Permission Tests

    [Fact]
    public void PriorityScore_BoundaryValues_ReachesExactZeroAndExactOneHundred()
    {
        var policy = SlottingPolicy.Create("BoundaryPolicy");
        var service = new RelocationRecommendationService(null!, null!, NullLogger<RelocationRecommendationService>.Instance);

        // Maximum Boundary:
        // S_b = 100 ($50 net savings / $50 target), U = 100, S_suit = 100, C_disrupt = 0 (S_disrupt = 0 -> Mitigation = 100)
        // P = 0.35(100) + 0.30(100) + 0.20(100) + 0.15(100) = 100.0
        decimal scoreMax = service.CalculatePriorityScore(
            netMonthlyBenefitUsd: 50.00m,
            urgencyScore: 100.0m,
            suitabilityScore: 100.0m,
            costPenaltyUsd: 0.0m,
            policy: policy,
            isFeasible: true);

        Assert.Equal(100.0m, scoreMax);

        // Minimum Boundary:
        // S_b = 0 ($0 net savings), U = 0, S_suit = 0, C_disrupt >= $30 (S_disrupt = 100 -> Mitigation = 0)
        // P = 0.35(0) + 0.30(0) + 0.20(0) + 0.15(0) = 0.0
        decimal scoreMin = service.CalculatePriorityScore(
            netMonthlyBenefitUsd: 0.0m,
            urgencyScore: 0.0m,
            suitabilityScore: 0.0m,
            costPenaltyUsd: 30.00m,
            policy: policy,
            isFeasible: true);

        Assert.Equal(0.0m, scoreMin);

        // Infeasible Boundary:
        // Infeasible candidate strictly scores 0.0 regardless of components
        decimal scoreInfeasible = service.CalculatePriorityScore(
            netMonthlyBenefitUsd: 50.00m,
            urgencyScore: 100.0m,
            suitabilityScore: 100.0m,
            costPenaltyUsd: 0.0m,
            policy: policy,
            isFeasible: false);

        Assert.Equal(0.0m, scoreInfeasible);
    }

    [Fact]
    public void RelocationCost_IndependentEffectsOfHandlingMovementAndDisruptionCosts()
    {
        var policy = SlottingPolicy.Create("CostDecompositionPolicy");
        var service = new RelocationRecommendationService(null!, null!, NullLogger<RelocationRecommendationService>.Instance);

        var locA = new Location { XCoord = 10m, YCoord = 10m, ZCoord = 1.0m, ShelfLevel = 1 };
        var locB = new Location { XCoord = 30m, YCoord = 30m, ZCoord = 1.0m, ShelfLevel = 1, Zone = new WarehouseZone { ZoneType = WarehouseZoneType.PickFace } };
        var locReserve = new Location { XCoord = 30m, YCoord = 30m, ZCoord = 1.0m, ShelfLevel = 1, Zone = new WarehouseZone { ZoneType = WarehouseZoneType.BulkStorage } };

        // 1. Handling Cost Independence:
        // Baseline: 10 units, 5kg -> Handling = 10 * 0.10 = $1.00
        var econH1 = service.CalculateTransparentEconomics(20m, 100m, 10m, 5m, locA, locB, policy, "Standard");
        // Higher quantity: 50 units, 5kg -> Handling = 50 * 0.10 = $5.00
        var econH2 = service.CalculateTransparentEconomics(20m, 100m, 50m, 5m, locA, locB, policy, "Standard");
        // Heavy item: 10 units, 35kg (>20kg surcharge tier 1.5) -> Handling = 1.00 + (35-20)/10 * 5.00 = $8.50
        var econH3 = service.CalculateTransparentEconomics(20m, 100m, 10m, 35m, locA, locB, policy, "Standard");

        Assert.Equal(1.00m, econH1.handlingCost);
        Assert.Equal(5.00m, econH2.handlingCost);
        Assert.Equal(8.50m, econH3.handlingCost);
        // Movement and disruption unchanged
        Assert.Equal(econH1.movementCost, econH2.movementCost);
        Assert.Equal(econH1.disruptionCost, econH2.disruptionCost);

        // 2. Movement Transit Labor Cost Independence:
        // Transit distance increases from 28.28m to 100m
        var locFar = new Location { XCoord = 110m, YCoord = 10m, ZCoord = 1.0m, ShelfLevel = 1, Zone = new WarehouseZone { ZoneType = WarehouseZoneType.PickFace } };
        var econM1 = service.CalculateTransparentEconomics(20m, 100m, 10m, 5m, locA, locB, policy, "Standard");
        var econM2 = service.CalculateTransparentEconomics(20m, 100m, 10m, 5m, locA, locFar, policy, "Standard");

        Assert.True(econM2.movementCost > econM1.movementCost);
        // Handling and disruption unchanged
        Assert.Equal(econM1.handlingCost, econM2.handlingCost);
        Assert.Equal(econM1.disruptionCost, econM2.disruptionCost);

        // 3. Disruption Cost Independence:
        // PickFace (congestion 1.5) vs BulkStorage (congestion 1.0)
        var econDPick = service.CalculateTransparentEconomics(20m, 100m, 10m, 5m, locA, locB, policy, "Standard");
        var econDBulk = service.CalculateTransparentEconomics(20m, 100m, 10m, 5m, locA, locReserve, policy, "Standard");
        Assert.Equal(22.50m, econDPick.disruptionCost); // 15.00 * 1.5 * 1.0
        Assert.Equal(15.00m, econDBulk.disruptionCost); // 15.00 * 1.0 * 1.0

        // Shift variations: Peak (1.4) vs Standard (1.0) vs Night (0.7)
        var econDPeak = service.CalculateTransparentEconomics(20m, 100m, 10m, 5m, locA, locB, policy, "Peak");
        var econDNight = service.CalculateTransparentEconomics(20m, 100m, 10m, 5m, locA, locB, policy, "Night");
        Assert.Equal(31.50m, econDPeak.disruptionCost);  // 15.00 * 1.5 * 1.4
        Assert.Equal(15.75m, econDNight.disruptionCost); // 15.00 * 1.5 * 0.7

        // Handling and movement unchanged
        Assert.Equal(econDPick.handlingCost, econDPeak.handlingCost);
        Assert.Equal(econDPick.movementCost, econDPeak.movementCost);
    }

    [Fact]
    public async Task ApprovalLifecycle_SimulatedToPendingReview_TransitionsSuccessfully()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new RelocationRecommendationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<RelocationRecommendationService>.Instance);

        var batch = SlottingRecommendationBatch.Create(fix.FacilityId, Guid.NewGuid(), "BATCH-REVIEW-01");
        batch.MarkSimulated(1, 5m, 10m, 20m, 80m);
        context.SlottingRecommendationBatches.Add(batch);
        await context.SaveChangesAsync();

        var newStatus = await service.SubmitBatchForReviewAsync(batch.Id);
        Assert.Equal(SlottingBatchStatus.PendingApproval, newStatus);

        var reloaded = await context.SlottingRecommendationBatches.FindAsync(batch.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(SlottingBatchStatus.PendingApproval, reloaded.Status);
    }

    [Fact]
    public async Task ApprovalLifecycle_RejectionWithAuditableReason_RecordsPerRecommendationReasons()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new RelocationRecommendationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<RelocationRecommendationService>.Instance);

        var batch = SlottingRecommendationBatch.Create(fix.FacilityId, Guid.NewGuid(), "BATCH-REJECT-01");
        var rec1 = new SlottingRecommendation
        {
            BatchId = batch.Id,
            ItemId = Guid.NewGuid(),
            ItemSku = "SKU-REJ-1",
            ClientId = fix.ClientId,
            CurrentLocationId = fix.UpperReserveLocationId,
            ProposedLocationId = fix.GoldenZonePickLocation1Id,
            Status = SlottingRecommendationStatus.Pending,
            IsOperationallyFeasible = true
        };
        var rec2 = new SlottingRecommendation
        {
            BatchId = batch.Id,
            ItemId = Guid.NewGuid(),
            ItemSku = "SKU-REJ-2",
            ClientId = fix.ClientId,
            CurrentLocationId = fix.UpperReserveLocationId,
            ProposedLocationId = fix.GoldenZonePickLocation2Id,
            Status = SlottingRecommendationStatus.Pending,
            IsOperationallyFeasible = true
        };
        batch.Recommendations.Add(rec1);
        batch.Recommendations.Add(rec2);
        batch.MarkSimulated(2, 10m, 20m, 40m, 85m);
        batch.Status = SlottingBatchStatus.PendingApproval;
        context.SlottingRecommendationBatches.Add(batch);
        await context.SaveChangesAsync();

        var result = await service.ApproveRelocationPlanAsync(new RelocationApprovalRequestDto
        {
            BatchId = batch.Id,
            ApprovedRecommendationIds = [],
            RejectedRecommendationIds = [rec1.Id, rec2.Id],
            RejectionReasonsByRecommendationId = new Dictionary<Guid, string>
            {
                [rec1.Id] = "Excessive aisle congestion expected during upcoming shift.",
                [rec2.Id] = "SKU undergoing packaging redesign next week."
            },
            ApprovalNotes = "Batch rejected by warehouse operations manager."
        });

        Assert.Equal(SlottingBatchStatus.Rejected, result.Status);
        Assert.Equal(0, result.ApprovedCount);
        Assert.Equal(2, result.RejectedCount);

        var r1 = await context.SlottingRecommendations.FindAsync(rec1.Id);
        var r2 = await context.SlottingRecommendations.FindAsync(rec2.Id);
        Assert.Equal(SlottingRecommendationStatus.Rejected, r1!.Status);
        Assert.Equal("Excessive aisle congestion expected during upcoming shift.", r1.RejectionReason);
        Assert.Equal(SlottingRecommendationStatus.Rejected, r2!.Status);
        Assert.Equal("SKU undergoing packaging redesign next week.", r2.RejectionReason);
    }

    [Fact]
    public async Task ApprovalLifecycle_PartiallyApprovedBatch_RetainsPerRecommendationStatuses()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new RelocationRecommendationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<RelocationRecommendationService>.Instance);

        var batch = SlottingRecommendationBatch.Create(fix.FacilityId, Guid.NewGuid(), "BATCH-PARTIAL-01");
        var rec1 = new SlottingRecommendation
        {
            BatchId = batch.Id,
            ItemId = Guid.NewGuid(),
            ItemSku = "SKU-PART-1",
            ClientId = fix.ClientId,
            CurrentLocationId = fix.UpperReserveLocationId,
            ProposedLocationId = fix.GoldenZonePickLocation1Id,
            Status = SlottingRecommendationStatus.Pending,
            IsOperationallyFeasible = true
        };
        var rec2 = new SlottingRecommendation
        {
            BatchId = batch.Id,
            ItemId = Guid.NewGuid(),
            ItemSku = "SKU-PART-2",
            ClientId = fix.ClientId,
            CurrentLocationId = fix.UpperReserveLocationId,
            ProposedLocationId = fix.GoldenZonePickLocation2Id,
            Status = SlottingRecommendationStatus.Pending,
            IsOperationallyFeasible = true
        };
        var rec3 = new SlottingRecommendation
        {
            BatchId = batch.Id,
            ItemId = Guid.NewGuid(),
            ItemSku = "SKU-PART-3",
            ClientId = fix.ClientId,
            CurrentLocationId = fix.UpperReserveLocationId,
            ProposedLocationId = fix.IntermediateRackLocationId,
            Status = SlottingRecommendationStatus.Pending,
            IsOperationallyFeasible = true
        };
        batch.Recommendations.Add(rec1);
        batch.Recommendations.Add(rec2);
        batch.Recommendations.Add(rec3);
        batch.MarkSimulated(3, 15m, 30m, 60m, 80m);
        batch.Status = SlottingBatchStatus.PendingApproval;
        context.SlottingRecommendationBatches.Add(batch);
        await context.SaveChangesAsync();

        // Approve rec1, reject rec2, leave rec3 skipped
        var result = await service.ApproveRelocationPlanAsync(new RelocationApprovalRequestDto
        {
            BatchId = batch.Id,
            ApprovedRecommendationIds = [rec1.Id],
            RejectedRecommendationIds = [rec2.Id],
            RejectionReasonsByRecommendationId = new Dictionary<Guid, string>
            {
                [rec2.Id] = "Deferred due to high picker traffic."
            }
        });

        Assert.Equal(SlottingBatchStatus.PartiallyApproved, result.Status);
        Assert.Equal(1, result.ApprovedCount);
        Assert.Equal(1, result.RejectedCount);
        Assert.Equal(1, result.SkippedCount);

        var r1 = await context.SlottingRecommendations.FindAsync(rec1.Id);
        var r2 = await context.SlottingRecommendations.FindAsync(rec2.Id);
        var r3 = await context.SlottingRecommendations.FindAsync(rec3.Id);

        Assert.Equal(SlottingRecommendationStatus.Approved, r1!.Status);
        Assert.Equal(SlottingRecommendationStatus.Rejected, r2!.Status);
        Assert.Equal(SlottingRecommendationStatus.Skipped, r3!.Status);
    }

    [Fact]
    public async Task ApprovalLifecycle_CancelledBatch_CannotSubsequentlyBeApproved()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new RelocationRecommendationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<RelocationRecommendationService>.Instance);

        var batch = SlottingRecommendationBatch.Create(fix.FacilityId, Guid.NewGuid(), "BATCH-CANCEL-01");
        batch.MarkSimulated(1, 5m, 10m, 20m, 80m);
        context.SlottingRecommendationBatches.Add(batch);
        await context.SaveChangesAsync();

        // Cancel batch
        var cancelStatus = await service.CancelBatchAsync(batch.Id, "Strategy superseded by seasonal recalculation.");
        Assert.Equal(SlottingBatchStatus.Cancelled, cancelStatus);

        // Attempting to approve cancelled batch must throw InvalidOperationException
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApproveRelocationPlanAsync(new RelocationApprovalRequestDto
            {
                BatchId = batch.Id,
                ApprovedRecommendationIds = []
            }));
    }

    [Fact]
    public async Task ApprovalLifecycle_AllTransitionsPreserveOperationalLedgerInvariance()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new RelocationRecommendationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<RelocationRecommendationService>.Instance);

        var item = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-LIFECYCLE-INVAR", WeightKg = 5m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m };
        context.Items.Add(item);
        context.StockOnHand.Add(new StockOnHand { Id = Guid.NewGuid(), ClientId = fix.ClientId, FacilityId = fix.FacilityId, ItemId = item.Id, LocationId = fix.UpperReserveLocationId, QtyAvailable = 5m });
        await context.SaveChangesAsync();

        // Snapshots before lifecycle transitions
        var stockBefore = context.StockOnHand.AsNoTracking().Select(s => new { s.Id, s.QtyAvailable, s.LocationId }).ToList();
        var locationsBefore = context.Locations.AsNoTracking().Select(l => new { l.Id, l.LocationCode, l.IsActive }).ToList();

        // 1. Create & Simulate
        var batch = SlottingRecommendationBatch.Create(fix.FacilityId, Guid.NewGuid(), "BATCH-INVAR-01");
        var rec = new SlottingRecommendation
        {
            BatchId = batch.Id,
            ItemId = item.Id,
            ItemSku = item.Sku,
            ClientId = fix.ClientId,
            CurrentLocationId = fix.UpperReserveLocationId,
            ProposedLocationId = fix.GoldenZonePickLocation1Id,
            Status = SlottingRecommendationStatus.Pending,
            IsOperationallyFeasible = true
        };
        batch.Recommendations.Add(rec);
        batch.MarkSimulated(1, 5m, 10m, 20m, 80m);
        context.SlottingRecommendationBatches.Add(batch);
        await context.SaveChangesAsync();

        // 2. Submit for Review
        await service.SubmitBatchForReviewAsync(batch.Id);

        // 3. Approve
        await service.ApproveRelocationPlanAsync(new RelocationApprovalRequestDto
        {
            BatchId = batch.Id,
            ApprovedRecommendationIds = [rec.Id]
        });

        // 4. Cancel a second batch
        var batch2 = SlottingRecommendationBatch.Create(fix.FacilityId, Guid.NewGuid(), "BATCH-INVAR-02");
        batch2.MarkSimulated(0, 0, 0, 0, 0);
        context.SlottingRecommendationBatches.Add(batch2);
        await context.SaveChangesAsync();
        await service.CancelBatchAsync(batch2.Id, "Administrative cancellation test");

        // Snapshots after all lifecycle transitions
        var stockAfter = context.StockOnHand.AsNoTracking().Select(s => new { s.Id, s.QtyAvailable, s.LocationId }).ToList();
        var locationsAfter = context.Locations.AsNoTracking().Select(l => new { l.Id, l.LocationCode, l.IsActive }).ToList();

        // Strict Identity: Physical stock and locations completely unchanged
        Assert.Equal(stockBefore, stockAfter);
        Assert.Equal(locationsBefore, locationsAfter);
    }

    [Fact]
    public void SlottingController_EndpointsHaveCorrectPermissionAttributes()
    {
        var controllerType = typeof(SlottingOptimizationController);

        var prioritizeMethod = controllerType.GetMethod(nameof(SlottingOptimizationController.PrioritizeRelocations));
        Assert.NotNull(prioritizeMethod);
        var permPrioritize = prioritizeMethod.GetCustomAttribute<HasPermissionAttribute>();
        Assert.NotNull(permPrioritize);
        Assert.Equal(Permissions.Analytics.SlottingManage, permPrioritize.Policy);

        var approvePlanMethod = controllerType.GetMethod(nameof(SlottingOptimizationController.ApproveRelocationPlan));
        Assert.NotNull(approvePlanMethod);
        var permApprove = approvePlanMethod.GetCustomAttribute<HasPermissionAttribute>();
        Assert.NotNull(permApprove);
        Assert.Equal(Permissions.Analytics.SlottingManage, permApprove.Policy);

        var submitReviewMethod = controllerType.GetMethod(nameof(SlottingOptimizationController.SubmitBatchForReview));
        Assert.NotNull(submitReviewMethod);
        var permSubmit = submitReviewMethod.GetCustomAttribute<HasPermissionAttribute>();
        Assert.NotNull(permSubmit);
        Assert.Equal(Permissions.Analytics.SlottingManage, permSubmit.Policy);

        var cancelMethod = controllerType.GetMethod(nameof(SlottingOptimizationController.CancelBatch));
        Assert.NotNull(cancelMethod);
        var permCancel = cancelMethod.GetCustomAttribute<HasPermissionAttribute>();
        Assert.NotNull(permCancel);
        Assert.Equal(Permissions.Analytics.SlottingManage, permCancel.Policy);

        var getPlanMethod = controllerType.GetMethod(nameof(SlottingOptimizationController.GetPrioritizedPlan));
        Assert.NotNull(getPlanMethod);
        var permGet = getPlanMethod.GetCustomAttribute<HasPermissionAttribute>();
        Assert.NotNull(permGet);
        Assert.Equal(Permissions.Analytics.SlottingView, permGet.Policy);
    }

    [Fact]
    public async Task SlottingController_PrioritizeRelocations_ReturnsOkWithResult()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var slottingService = new SlottingOptimizationService(context, currentUser, NullLogger<SlottingOptimizationService>.Instance);
        var relocationService = new RelocationRecommendationService(context, currentUser, NullLogger<RelocationRecommendationService>.Instance);

        var controller = new SlottingOptimizationController(slottingService, relocationService, currentUser);

        var actionResult = await controller.PrioritizeRelocations(new RelocationPrioritizationRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId
        }, default);

        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var dto = Assert.IsType<RelocationPrioritizationResultDto>(okResult.Value);
        Assert.Equal(fix.FacilityId, dto.FacilityId);
    }

    #endregion
}
