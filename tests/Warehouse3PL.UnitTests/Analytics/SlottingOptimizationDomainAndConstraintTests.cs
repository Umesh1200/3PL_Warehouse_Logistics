using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Authorization;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Services.Analytics;
using Warehouse3PL.UnitTests.Common;
using Warehouse3PL.WebApi.Controllers;

namespace Warehouse3PL.UnitTests.Analytics;

/// <summary>
/// Exhaustive unit and integration test suite for Phase 6.3.7.1:
/// Slotting Optimization Domain, Policy &amp; Constraint Foundation.
/// Verifies:
/// 1. Policy creation, configuration, golden zone elevation parameters, and composite goals.
/// 2. Physical dimension, volumetric, and weight capacity hard constraints.
/// 3. Environmental temperature class and hazardous material segregation constraints.
/// 4. Operational location status (inactive, damaged, locked locations disqualified).
/// 5. Ergonomic golden zone alignment (waist-to-shoulder elevation bonus/penalty).
/// 6. Dead stock protection rule (avoids unnecessary churn for inactive inventory).
/// 7. Read-only ledger invariance: simulations and approvals cause ZERO mutations to StockOnHand.
/// 8. WebAPI controller contracts and permission attributes.
/// </summary>
public class SlottingOptimizationDomainAndConstraintTests
{
    private class SlottingFixture
    {
        public string DbName { get; } = Guid.NewGuid().ToString();
        public Guid FacilityId { get; set; } = Guid.NewGuid();
        public Guid ClientId { get; set; } = Guid.NewGuid();

        public Guid AmbientPickZoneId { get; set; } = Guid.NewGuid();
        public Guid ColdStorageZoneId { get; set; } = Guid.NewGuid();
        public Guid BulkReserveZoneId { get; set; } = Guid.NewGuid();

        public Guid FastMoverItemId { get; set; } = Guid.NewGuid();
        public Guid SlowMoverItemId { get; set; } = Guid.NewGuid();
        public Guid HeavyItemId { get; set; } = Guid.NewGuid();
        public Guid OversizedItemId { get; set; } = Guid.NewGuid();
        public Guid FrozenFoodItemId { get; set; } = Guid.NewGuid();
        public Guid HazardousChemicalItemId { get; set; } = Guid.NewGuid();

        // Locations
        public Guid GoldenZonePickLocationId { get; set; } = Guid.NewGuid(); // Level 1, Z = 1.0m
        public Guid UpperReserveLocationId { get; set; } = Guid.NewGuid();    // Level 4, Z = 7.5m
        public Guid DamagedLocationId { get; set; } = Guid.NewGuid();
        public Guid LockedLocationId { get; set; } = Guid.NewGuid();
        public Guid LowCapacityWeightLocationId { get; set; } = Guid.NewGuid(); // MaxWeight = 10kg
    }

    private static ApplicationDbContext CreateContext(
        string dbName,
        Guid? tenantId = null,
        Guid? facilityId = null,
        bool isStaff = true,
        bool isSuperAdmin = false,
        List<string>? permissions = null)
    {
        var currentUser = new TestCurrentUserContext
        {
            TenantId = tenantId,
            FacilityId = facilityId,
            UserType = isStaff ? UserType.Staff : UserType.Client,
            RolesList = isSuperAdmin ? ["SuperAdmin"] : [],
            PermissionsList = permissions ?? [Permissions.Analytics.SlottingView, Permissions.Analytics.SlottingManage]
        };

        return TestDbContextFactory.Create(
            new TestTenantProvider { TenantId = tenantId, BypassTenantFilter = isStaff || isSuperAdmin },
            currentUser,
            dbName);
    }

    private static async Task<SlottingFixture> SeedSlottingEnvironmentAsync()
    {
        var fix = new SlottingFixture();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);

        var facility = new Facility
        {
            Id = fix.FacilityId,
            FacilityCode = "ORD-01",
            Name = "Chicago Regional Hub",
            TotalAreaSqM = 15000m,
            IsActive = true
        };

        var client = new Client
        {
            Id = fix.ClientId,
            AccountCode = "CLI-ACME",
            CompanyName = "Acme Global",
            Status = ClientStatus.Active
        };

        var ambientZone = new WarehouseZone
        {
            Id = fix.AmbientPickZoneId,
            FacilityId = fix.FacilityId,
            ZoneCode = "Z-PICK",
            Name = "Active Pick Faces",
            ZoneType = WarehouseZoneType.PickFace,
            IsActive = true
        };

        var coldZone = new WarehouseZone
        {
            Id = fix.ColdStorageZoneId,
            FacilityId = fix.FacilityId,
            ZoneCode = "Z-COLD",
            Name = "Cold Storage Vault",
            ZoneType = WarehouseZoneType.ColdStorage,
            TempMinC = -25.0m,
            TempMaxC = -18.0m,
            IsActive = true
        };

        var bulkZone = new WarehouseZone
        {
            Id = fix.BulkReserveZoneId,
            FacilityId = fix.FacilityId,
            ZoneCode = "Z-RESERVE",
            Name = "High-Bay Bulk Reserve",
            ZoneType = WarehouseZoneType.BulkStorage,
            IsActive = true
        };

        context.Facilities.Add(facility);
        context.Clients.Add(client);
        context.WarehouseZones.AddRange(ambientZone, coldZone, bulkZone);

        // Seed Items with various physical and classification profiles
        var fastItem = new Item
        {
            Id = fix.FastMoverItemId,
            ClientId = fix.ClientId,
            Sku = "SKU-FAST-01",
            Description = "Fast-Moving Electronic Widget",
            LengthCm = 30m,
            WidthCm = 20m,
            HeightCm = 15m,
            WeightKg = 2.5m,
            VolumeCm3 = 9000m,
            AbcVelocity = AbcVelocity.A,
            StorageTempClass = StorageTempClass.Ambient
        };

        var slowItem = new Item
        {
            Id = fix.SlowMoverItemId,
            ClientId = fix.ClientId,
            Sku = "SKU-SLOW-01",
            Description = "Slow-Moving Industrial Valve",
            LengthCm = 40m,
            WidthCm = 30m,
            HeightCm = 25m,
            WeightKg = 8.0m,
            VolumeCm3 = 30000m,
            AbcVelocity = AbcVelocity.C,
            StorageTempClass = StorageTempClass.Ambient
        };

        var heavyItem = new Item
        {
            Id = fix.HeavyItemId,
            ClientId = fix.ClientId,
            Sku = "SKU-HEAVY-01",
            Description = "Heavy Cast Iron Anchor",
            LengthCm = 50m,
            WidthCm = 50m,
            HeightCm = 40m,
            WeightKg = 150.0m, // 150kg
            VolumeCm3 = 100000m,
            AbcVelocity = AbcVelocity.B,
            StorageTempClass = StorageTempClass.Ambient
        };

        var oversizedItem = new Item
        {
            Id = fix.OversizedItemId,
            ClientId = fix.ClientId,
            Sku = "SKU-OVERSIZED-01",
            Description = "Oversized Kayak Paddle",
            LengthCm = 220m, // 220cm > typical rack width
            WidthCm = 30m,
            HeightCm = 20m,
            WeightKg = 4.0m,
            VolumeCm3 = 132000m,
            AbcVelocity = AbcVelocity.B,
            StorageTempClass = StorageTempClass.Ambient
        };

        var frozenItem = new Item
        {
            Id = fix.FrozenFoodItemId,
            ClientId = fix.ClientId,
            Sku = "SKU-FROZEN-01",
            Description = "Frozen Salmon Fillet Case",
            LengthCm = 40m,
            WidthCm = 30m,
            HeightCm = 20m,
            WeightKg = 10.0m,
            VolumeCm3 = 24000m,
            AbcVelocity = AbcVelocity.A,
            StorageTempClass = StorageTempClass.Frozen // Requires frozen zone
        };

        var hazItem = new Item
        {
            Id = fix.HazardousChemicalItemId,
            ClientId = fix.ClientId,
            Sku = "SKU-HAZ-01",
            Description = "Flammable Solvent Drums",
            LengthCm = 60m,
            WidthCm = 60m,
            HeightCm = 80m,
            WeightKg = 50.0m,
            VolumeCm3 = 288000m,
            AbcVelocity = AbcVelocity.B,
            StorageTempClass = StorageTempClass.Ambient,
            HazardClass = "Class 3 Flammable Liquid"
        };

        context.Items.AddRange(fastItem, slowItem, heavyItem, oversizedItem, frozenItem, hazItem);

        // Seed Locations
        var goldenLoc = new Location
        {
            Id = fix.GoldenZonePickLocationId,
            FacilityId = fix.FacilityId,
            ZoneId = fix.AmbientPickZoneId,
            LocationCode = "PICK-A01-R01-L1",
            AisleCode = "A-01",
            RackCode = "R-01",
            ShelfLevel = 1,
            LocationType = LocationType.Rack,
            XCoord = 10.0m,
            YCoord = 10.0m,
            ZCoord = 1.1m, // In golden zone (0.8m - 1.5m)
            LengthCm = 120m,
            WidthCm = 100m,
            HeightCm = 100m,
            MaxWeightKg = 500m,
            IsActive = true,
            IsPickable = true
        };

        var upperReserveLoc = new Location
        {
            Id = fix.UpperReserveLocationId,
            FacilityId = fix.FacilityId,
            ZoneId = fix.BulkReserveZoneId,
            LocationCode = "RES-A05-R08-L4",
            AisleCode = "A-05",
            RackCode = "R-08",
            ShelfLevel = 4,
            LocationType = LocationType.Rack,
            XCoord = 45.0m,
            YCoord = 60.0m,
            ZCoord = 7.5m, // Far outside golden zone
            LengthCm = 150m,
            WidthCm = 120m,
            HeightCm = 150m,
            MaxWeightKg = 1500m,
            IsActive = true,
            IsPickable = true
        };

        var damagedLoc = new Location
        {
            Id = fix.DamagedLocationId,
            FacilityId = fix.FacilityId,
            ZoneId = fix.AmbientPickZoneId,
            LocationCode = "PICK-A01-R02-L1",
            ShelfLevel = 1,
            LocationType = LocationType.Rack,
            IsActive = true,
            IsPickable = true,
            IsDamaged = true // Damaged!
        };

        var lockedLoc = new Location
        {
            Id = fix.LockedLocationId,
            FacilityId = fix.FacilityId,
            ZoneId = fix.AmbientPickZoneId,
            LocationCode = "PICK-A01-R03-L1",
            ShelfLevel = 1,
            LocationType = LocationType.Rack,
            IsActive = true,
            IsPickable = true,
            IsLocked = true // Operational lock!
        };

        var lowWeightLoc = new Location
        {
            Id = fix.LowCapacityWeightLocationId,
            FacilityId = fix.FacilityId,
            ZoneId = fix.AmbientPickZoneId,
            LocationCode = "PICK-LIGHT-01",
            ShelfLevel = 1,
            LocationType = LocationType.Bin,
            LengthCm = 60m,
            WidthCm = 40m,
            HeightCm = 30m,
            MaxWeightKg = 10.0m, // Max 10kg!
            IsActive = true,
            IsPickable = true
        };

        context.Locations.AddRange(goldenLoc, upperReserveLoc, damagedLoc, lockedLoc, lowWeightLoc);

        // Seed stock on hand for fast item (currently sub-optimally in upper reserve)
        var fastStock = new StockOnHand
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = fix.FastMoverItemId,
            LocationId = fix.UpperReserveLocationId, // Sub-optimal placement!
            QtyAvailable = 30m
        };

        // Seed stock on hand for slow item (currently occupying golden zone pick face)
        var slowStock = new StockOnHand
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = fix.SlowMoverItemId,
            LocationId = fix.GoldenZonePickLocationId, // Sub-optimal: blocking pick face!
            QtyAvailable = 3m
        };

        context.StockOnHand.AddRange(fastStock, slowStock);

        var defaultPolicy = SlottingPolicy.Create(
            name: "Standard Facility Slotting Policy",
            facilityId: fix.FacilityId);
        context.SlottingPolicies.Add(defaultPolicy);

        await context.SaveChangesAsync();
        return fix;
    }

    #region Unit & Domain Tests

    /// <summary>
    /// Test 1: Policy creation with valid parameters and business defaults.
    /// </summary>
    [Fact]
    public async Task CreatePolicy_ValidParameters_SuccessfullyPersistsAndReturnsDto()
    {
        var fix = await SeedSlottingEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new SlottingOptimizationService(context, currentUser, NullLogger<SlottingOptimizationService>.Instance);

        var request = new CreateSlottingPolicyRequestDto
        {
            Name = "Q4 Dynamic Slotting Policy",
            FacilityId = fix.FacilityId,
            PrimaryGoal = SlottingOptimizationGoal.ErgonomicGoldenZone,
            GoldenZoneMinZMeters = 0.85m,
            GoldenZoneMaxZMeters = 1.45m,
            GoldenZoneShelfLevels = "1,2",
            MaxRelocationsPerBatch = 40,
            MinLaborSavingsUsd = 7.50m
        };

        var policy = await service.CreatePolicyAsync(request);

        Assert.NotNull(policy);
        Assert.Equal("Q4 Dynamic Slotting Policy", policy.Name);
        Assert.Equal(0.85m, policy.GoldenZoneMinZMeters);
        Assert.Equal(1.45m, policy.GoldenZoneMaxZMeters);
        Assert.Equal(40, policy.MaxRelocationsPerBatch);
        Assert.Equal(7.50m, policy.MinLaborSavingsUsd);
        Assert.True(policy.ProtectDeadStockFromUnnecessaryRelocation);
    }

    /// <summary>
    /// Test 2: Policy validation throws for invalid golden zone boundaries or out-of-range batch moves.
    /// </summary>
    [Fact]
    public void CreatePolicy_InvalidParameters_ThrowsValidationException()
    {
        // 1. Inverted golden zone (MinZ > MaxZ)
        Assert.Throws<ValidationException>(() =>
            SlottingPolicy.Create(
                name: "Invalid Policy",
                goldenZoneMinZMeters: 2.0m,
                goldenZoneMaxZMeters: 1.0m));

        // 2. Empty policy name
        Assert.Throws<ValidationException>(() =>
            SlottingPolicy.Create(name: "   "));

        // 3. Negative relocations
        Assert.Throws<ValidationException>(() =>
            SlottingPolicy.Create(name: "Negative Moves", maxRelocationsPerBatch: 0));
    }

    /// <summary>
    /// Test 3: Physical Dimensions Hard Constraint checking.
    /// Oversized kayak paddle (220cm) must fail against standard 120cm rack location.
    /// </summary>
    [Fact]
    public async Task EvaluateCompatibility_PhysicalDimensionsExceeded_FailsHardRequirement()
    {
        var fix = await SeedSlottingEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new SlottingOptimizationService(context, currentUser, NullLogger<SlottingOptimizationService>.Instance);

        var result = await service.EvaluateSkuLocationCompatibilityAsync(
            fix.OversizedItemId, fix.GoldenZonePickLocationId);

        Assert.NotNull(result);
        Assert.False(result.IsPhysicallyFeasible);
        Assert.False(result.IsOverallCompatible);
        Assert.Equal(0m, result.CalculatedSuitabilityScore);
        Assert.Contains(result.ViolatedHardConstraints, v => v.Contains("exceed location boundaries"));

        var dimEval = result.ConstraintEvaluations.FirstOrDefault(c => c.ConstraintType == SlottingConstraintType.PhysicalDimensions);
        Assert.NotNull(dimEval);
        Assert.False(dimEval.IsSatisfied);
        Assert.Equal(ConstraintSeverity.HardRequirement, dimEval.Severity);
    }

    /// <summary>
    /// Test 4: Weight Capacity Hard Constraint checking.
    /// Heavy anchor (150kg) must fail against 10kg light bin location.
    /// </summary>
    [Fact]
    public async Task EvaluateCompatibility_WeightCapacityExceeded_FailsHardRequirement()
    {
        var fix = await SeedSlottingEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new SlottingOptimizationService(context, currentUser, NullLogger<SlottingOptimizationService>.Instance);

        var result = await service.EvaluateSkuLocationCompatibilityAsync(
            fix.HeavyItemId, fix.LowCapacityWeightLocationId);

        Assert.NotNull(result);
        Assert.False(result.IsPhysicallyFeasible);
        Assert.False(result.IsOverallCompatible);
        Assert.Contains(result.ViolatedHardConstraints, v => v.Contains("exceeds location maximum weight limit"));

        var weightEval = result.ConstraintEvaluations.FirstOrDefault(c => c.ConstraintType == SlottingConstraintType.WeightCapacity);
        Assert.NotNull(weightEval);
        Assert.False(weightEval.IsSatisfied);
        Assert.Equal(ConstraintSeverity.HardRequirement, weightEval.Severity);
    }

    /// <summary>
    /// Test 5: Environmental Temperature Class Hard Constraint checking.
    /// Frozen salmon fillet case evaluated against an ambient zone must fail.
    /// </summary>
    [Fact]
    public async Task EvaluateCompatibility_TemperatureClassMismatch_FailsHardRequirement()
    {
        var fix = await SeedSlottingEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new SlottingOptimizationService(context, currentUser, NullLogger<SlottingOptimizationService>.Instance);

        // Ambient location evaluation
        var ambientResult = await service.EvaluateSkuLocationCompatibilityAsync(
            fix.FrozenFoodItemId, fix.GoldenZonePickLocationId);

        Assert.NotNull(ambientResult);
        Assert.False(ambientResult.IsPhysicallyFeasible);
        Assert.Contains(ambientResult.ViolatedHardConstraints, v => v.Contains("requires sub-zero zone"));
    }

    /// <summary>
    /// Test 6: Hazardous Material Segregation Hard Constraint checking.
    /// Flammable solvents cannot be placed in active general pick face zone.
    /// </summary>
    [Fact]
    public async Task EvaluateCompatibility_HazardSegregationMismatch_FailsHardRequirement()
    {
        var fix = await SeedSlottingEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new SlottingOptimizationService(context, currentUser, NullLogger<SlottingOptimizationService>.Instance);

        var result = await service.EvaluateSkuLocationCompatibilityAsync(
            fix.HazardousChemicalItemId, fix.GoldenZonePickLocationId);

        Assert.NotNull(result);
        Assert.False(result.IsPhysicallyFeasible);
        Assert.Contains(result.ViolatedHardConstraints, v => v.Contains("cannot be placed in active general pick face zone"));
    }

    /// <summary>
    /// Test 7: Operational Location Status (Damaged or Locked locations must be disqualified).
    /// </summary>
    [Fact]
    public async Task EvaluateCompatibility_OperationalStatusDamagedOrLocked_FailsHardRequirement()
    {
        var fix = await SeedSlottingEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new SlottingOptimizationService(context, currentUser, NullLogger<SlottingOptimizationService>.Instance);

        // 1. Damaged location
        var damagedResult = await service.EvaluateSkuLocationCompatibilityAsync(
            fix.FastMoverItemId, fix.DamagedLocationId);
        Assert.False(damagedResult.IsOperationallyFeasible);
        Assert.Contains(damagedResult.ViolatedHardConstraints, v => v.Contains("marked damaged"));

        // 2. Locked location
        var lockedResult = await service.EvaluateSkuLocationCompatibilityAsync(
            fix.FastMoverItemId, fix.LockedLocationId);
        Assert.False(lockedResult.IsOperationallyFeasible);
        Assert.Contains(lockedResult.ViolatedHardConstraints, v => v.Contains("active operational lock"));
    }

    /// <summary>
    /// Test 8: Ergonomic Golden Zone Alignment (Soft Preference).
    /// Class A item in golden zone gets high ergonomic score; outside golden zone gets lower score.
    /// </summary>
    [Fact]
    public async Task EvaluateCompatibility_ErgonomicGoldenZone_AwardsBonusForFastMover()
    {
        var fix = await SeedSlottingEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new SlottingOptimizationService(context, currentUser, NullLogger<SlottingOptimizationService>.Instance);

        // Fast item in Golden Zone (Z = 1.1m)
        var goldenResult = await service.EvaluateSkuLocationCompatibilityAsync(
            fix.FastMoverItemId, fix.GoldenZonePickLocationId);

        Assert.True(goldenResult.IsOverallCompatible);
        Assert.Equal(100m, goldenResult.ErgonomicScore);
        Assert.True(goldenResult.CalculatedSuitabilityScore >= 75m);

        // Fast item in Upper Reserve (Z = 7.5m)
        var upperResult = await service.EvaluateSkuLocationCompatibilityAsync(
            fix.FastMoverItemId, fix.UpperReserveLocationId);

        Assert.True(upperResult.IsOverallCompatible);
        Assert.Equal(30m, upperResult.ErgonomicScore); // Lower ergonomic score
        Assert.True(goldenResult.CalculatedSuitabilityScore > upperResult.CalculatedSuitabilityScore);
    }

    /// <summary>
    /// Test 9: Simulation identifies Fast Mover (Class A) in upper reserve and recommends promotion to golden zone.
    /// </summary>
    [Fact]
    public async Task SimulateSlotting_FastMoverOutsideGoldenZone_RecommendsPromotionToPickFace()
    {
        var fix = await SeedSlottingEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new SlottingOptimizationService(context, currentUser, NullLogger<SlottingOptimizationService>.Instance);

        var simRequest = new SlottingSimulationRequestDto
        {
            FacilityId = fix.FacilityId,
            MaxMoves = 10
        };

        var batch = await service.SimulateSlottingRecommendationsAsync(simRequest);

        Assert.NotNull(batch);
        Assert.Equal(SlottingBatchStatus.Simulated, batch.Status);
        Assert.NotEmpty(batch.Recommendations);

        var fastRec = batch.Recommendations.FirstOrDefault(r => r.ItemSku == "SKU-FAST-01");
        Assert.NotNull(fastRec);
        Assert.Equal("PromoteFastMoverToGoldenZonePickFace", fastRec.ReasonCode);
        Assert.Equal(fix.UpperReserveLocationId, fastRec.CurrentLocationId);
        Assert.Equal(fix.GoldenZonePickLocationId, fastRec.ProposedLocationId);
        Assert.True(fastRec.EstimatedMonthlyNetSavingsUsd > 0);
    }

    /// <summary>
    /// Test 10: Dead Stock Relocation Protection.
    /// Verifies that dead stock in bulk storage is protected from unnecessary relocation churn.
    /// </summary>
    [Fact]
    public async Task SimulateSlotting_ProtectsDeadStockFromUnnecessaryRelocation()
    {
        var fix = await SeedSlottingEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new SlottingOptimizationService(context, currentUser, NullLogger<SlottingOptimizationService>.Instance);

        // Add a dead stock item located already in upper reserve
        var deadItem = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-DEAD-01",
            Description = "Obsolete discontinued bracket",
            AbcVelocity = AbcVelocity.C,
            LengthCm = 30m,
            WidthCm = 30m,
            HeightCm = 20m,
            WeightKg = 5m,
            VolumeCm3 = 18000m
        };

        var deadStock = new StockOnHand
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = deadItem.Id,
            LocationId = fix.UpperReserveLocationId,
            QtyAvailable = 100m
        };

        context.Items.Add(deadItem);
        context.StockOnHand.Add(deadStock);
        await context.SaveChangesAsync();

        var batch = await service.SimulateSlottingRecommendationsAsync(new SlottingSimulationRequestDto
        {
            FacilityId = fix.FacilityId
        });

        // Dead stock already in reserve must NOT have a relocation recommendation
        var deadRec = batch.Recommendations.FirstOrDefault(r => r.ItemSku == "SKU-DEAD-01");
        Assert.Null(deadRec);
    }

    /// <summary>
    /// Test 11: Read-Only Ledger Invariance.
    /// Executing simulation and approving recommendations MUST cause zero mutations to StockOnHand balances or locations.
    /// </summary>
    [Fact]
    public async Task SimulateSlotting_EnforcesReadOnlyInvariance_CausesZeroStockOnHandOrLocationMutations()
    {
        var fix = await SeedSlottingEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new SlottingOptimizationService(context, currentUser, NullLogger<SlottingOptimizationService>.Instance);

        var initialStockBalances = await context.StockOnHand.AsNoTracking().ToListAsync();
        var initialLocations = await context.Locations.AsNoTracking().ToListAsync();

        // 1. Run simulation
        var batch = await service.SimulateSlottingRecommendationsAsync(new SlottingSimulationRequestDto
        {
            FacilityId = fix.FacilityId
        });

        // 2. Approve batch
        await service.ApproveRecommendationBatchAsync(batch.Id);

        var postStockBalances = await context.StockOnHand.AsNoTracking().ToListAsync();
        var postLocations = await context.Locations.AsNoTracking().ToListAsync();

        Assert.Equal(initialStockBalances.Count, postStockBalances.Count);
        for (int i = 0; i < initialStockBalances.Count; i++)
        {
            Assert.Equal(initialStockBalances[i].LocationId, postStockBalances[i].LocationId);
            Assert.Equal(initialStockBalances[i].QtyAvailable, postStockBalances[i].QtyAvailable);
        }

        Assert.Equal(initialLocations.Count, postLocations.Count);
        for (int i = 0; i < initialLocations.Count; i++)
        {
            Assert.Equal(initialLocations[i].IsActive, postLocations[i].IsActive);
            Assert.Equal(initialLocations[i].LocationCode, postLocations[i].LocationCode);
        }
    }

    /// <summary>
    /// Test 12: WebAPI Controller Contract for Phase 6.3.7.1:
    /// Validates routing, HTTP status codes, and HasPermission policy enforcement.
    /// </summary>
    [Fact]
    public async Task SlottingOptimizationController_Endpoints_EnforcePermissionAttributes()
    {
        var fix = await SeedSlottingEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new SlottingOptimizationService(context, currentUser, NullLogger<SlottingOptimizationService>.Instance);
        var controller = new SlottingOptimizationController(service, currentUser);

        // 1. Evaluate Compatibility Endpoint
        var compResult = await controller.EvaluateCompatibility(fix.FastMoverItemId, fix.GoldenZonePickLocationId, null, null, null, CancellationToken.None);
        var compOk = Assert.IsType<OkObjectResult>(compResult);
        var compDto = Assert.IsType<SlottingCompatibilityResultDto>(compOk.Value);
        Assert.Equal(fix.FastMoverItemId, compDto.SkuId);

        // 2. Active Policy Endpoint
        var policyResult = await controller.GetActivePolicy(fix.FacilityId, null, null, CancellationToken.None);
        var policyOk = Assert.IsType<OkObjectResult>(policyResult);
        var policyDto = Assert.IsType<SlottingPolicyDto>(policyOk.Value);
        Assert.NotNull(policyDto);

        // 3. Verify HasPermission attributes on Controller Methods
        var createPolicyMethod = typeof(SlottingOptimizationController).GetMethod(nameof(SlottingOptimizationController.CreatePolicy));
        Assert.NotNull(createPolicyMethod);
        var manageAttr = createPolicyMethod.GetCustomAttribute<HasPermissionAttribute>();
        Assert.NotNull(manageAttr);
        Assert.Equal(Permissions.Analytics.SlottingManage, manageAttr.Policy);

        var evalMethod = typeof(SlottingOptimizationController).GetMethod(nameof(SlottingOptimizationController.EvaluateCompatibility));
        Assert.NotNull(evalMethod);
        var viewAttr = evalMethod.GetCustomAttribute<HasPermissionAttribute>();
        Assert.NotNull(viewAttr);
        Assert.Equal(Permissions.Analytics.SlottingView, viewAttr.Policy);
    }

    /// <summary>
    /// Test 13: Volumetric Capacity Hard Constraint.
    /// Dedicated test proving an SKU whose volume exceeds location capacity is disqualified (Suitability = 0).
    /// </summary>
    [Fact]
    public async Task EvaluateCompatibility_ExceedsVolumetricCapacity_FailsHardConstraint()
    {
        var fix = await SeedSlottingEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new SlottingOptimizationService(context, currentUser, NullLogger<SlottingOptimizationService>.Instance);

        // Huge volume item (2,000,000 cm³) vs standard rack (1,200,000 cm³)
        var hugeItem = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-HUGE-VOL",
            Description = "Inflatable Staging Mat",
            LengthCm = 110m,
            WidthCm = 90m,
            HeightCm = 90m,
            VolumeCm3 = 2000000m, // 2,000,000 cm³ exceeds 1,200,000 cm³ rack capacity
            WeightKg = 10m
        };
        context.Items.Add(hugeItem);
        await context.SaveChangesAsync();

        var result = await service.EvaluateSkuLocationCompatibilityAsync(
            hugeItem.Id, fix.GoldenZonePickLocationId);

        Assert.NotNull(result);
        Assert.False(result.IsPhysicallyFeasible);
        Assert.False(result.IsOverallCompatible);
        Assert.Equal(0m, result.CalculatedSuitabilityScore);
        Assert.Contains(result.ViolatedHardConstraints, v => v.Contains("exceeding location capacity") || v.Contains("exceed"));

        var volEval = result.ConstraintEvaluations.FirstOrDefault(c => c.ConstraintType == SlottingConstraintType.VolumetricCapacity);
        Assert.NotNull(volEval);
        Assert.False(volEval.IsSatisfied);
        Assert.Equal(ConstraintSeverity.HardRequirement, volEval.Severity);
    }

    /// <summary>
    /// Test 14: Client Tenancy Hard Constraint.
    /// Dedicated test proving an SKU belonging to Client A cannot be placed in a location containing stock of Client B.
    /// </summary>
    [Fact]
    public async Task EvaluateCompatibility_ClientTenancyMismatch_FailsHardConstraint()
    {
        var fix = await SeedSlottingEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new SlottingOptimizationService(context, currentUser, NullLogger<SlottingOptimizationService>.Instance);

        var otherClient = new Client
        {
            Id = Guid.NewGuid(),
            AccountCode = "CLI-OTHER",
            CompanyName = "Competitor Corp",
            Status = ClientStatus.Active
        };
        var otherItem = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = otherClient.Id,
            Sku = "SKU-OTHER-01",
            Description = "Third Party Cargo",
            LengthCm = 20m,
            WidthCm = 20m,
            HeightCm = 20m,
            WeightKg = 5m
        };

        // Put Client B stock into a bin
        var occupiedLoc = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            LocationCode = "BIN-SHARED-TEST",
            LocationType = LocationType.Bin,
            LengthCm = 100m,
            WidthCm = 100m,
            HeightCm = 100m,
            MaxWeightKg = 500m,
            IsActive = true,
            IsPickable = true
        };
        var otherStock = new StockOnHand
        {
            FacilityId = fix.FacilityId,
            ClientId = otherClient.Id,
            ItemId = otherItem.Id,
            LocationId = occupiedLoc.Id,
            QtyAvailable = 5m
        };

        context.Clients.Add(otherClient);
        context.Items.Add(otherItem);
        context.Locations.Add(occupiedLoc);
        context.StockOnHand.Add(otherStock);
        await context.SaveChangesAsync();

        // Attempt to slot Client A's item into Client B's bin
        var result = await service.EvaluateSkuLocationCompatibilityAsync(
            fix.FastMoverItemId, occupiedLoc.Id);

        Assert.NotNull(result);
        Assert.False(result.IsOperationallyFeasible);
        Assert.False(result.IsOverallCompatible);
        Assert.Contains(result.ViolatedHardConstraints, v => v.Contains("client") || v.Contains("Tenant"));

        var tenancyEval = result.ConstraintEvaluations.FirstOrDefault(c => c.ConstraintType == SlottingConstraintType.ClientTenancy);
        Assert.NotNull(tenancyEval);
        Assert.False(tenancyEval.IsSatisfied);
        Assert.Equal(ConstraintSeverity.HardRequirement, tenancyEval.Severity);
    }

    /// <summary>
    /// Test 15: Stackability Limits & Crush Protection.
    /// Crush-sensitive / non-stackable items (StackabilityFactor=1) cannot co-locate with existing stock.
    /// </summary>
    [Fact]
    public async Task EvaluateCompatibility_StackabilityExceeded_FailsHardConstraint()
    {
        var fix = await SeedSlottingEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new SlottingOptimizationService(context, currentUser, NullLogger<SlottingOptimizationService>.Instance);

        var fragileItem = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-GLASS-VASE",
            Description = "Fragile Crystal Vase",
            LengthCm = 20m,
            WidthCm = 20m,
            HeightCm = 30m,
            WeightKg = 2m,
            StackabilityFactor = 1 // Crush sensitive!
        };
        context.Items.Add(fragileItem);
        await context.SaveChangesAsync();

        // Evaluate against GoldenZonePickLocationId which ALREADY has stock (slowStock seeded in SeedSlottingEnvironmentAsync)
        var result = await service.EvaluateSkuLocationCompatibilityAsync(
            fragileItem.Id, fix.GoldenZonePickLocationId);

        Assert.NotNull(result);
        Assert.False(result.IsPhysicallyFeasible);
        Assert.False(result.IsOverallCompatible);
        Assert.Contains(result.ViolatedHardConstraints, v => v.Contains("crush-sensitive") || v.Contains("Stackability"));

        var stackEval = result.ConstraintEvaluations.FirstOrDefault(c => c.ConstraintType == SlottingConstraintType.Stackability);
        Assert.NotNull(stackEval);
        Assert.False(stackEval.IsSatisfied);
        Assert.Equal(ConstraintSeverity.HardRequirement, stackEval.Severity);
    }

    /// <summary>
    /// Test 16: Location Type Incompatibility.
    /// Disqualifies operational endpoints (DockDoor, StagingLane) and mismatched cantilever storage.
    /// </summary>
    [Fact]
    public async Task EvaluateCompatibility_LocationTypeIncompatible_FailsHardConstraint()
    {
        var fix = await SeedSlottingEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new SlottingOptimizationService(context, currentUser, NullLogger<SlottingOptimizationService>.Instance);

        // 1. Staging lane cannot receive storage putaway/slotting
        var dockLocation = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            LocationCode = "DOCK-DOOR-01",
            LocationType = LocationType.DockDoor,
            IsActive = true,
            IsPickable = true
        };

        // 2. Cantilever rack for short cargo (<150cm)
        var cantileverLocation = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            LocationCode = "CANT-01-A",
            LocationType = LocationType.Cantilever,
            LengthCm = 300m,
            WidthCm = 100m,
            HeightCm = 150m,
            IsActive = true,
            IsPickable = true
        };

        context.Locations.AddRange(dockLocation, cantileverLocation);
        await context.SaveChangesAsync();

        // Staging / Dock door test
        var dockResult = await service.EvaluateSkuLocationCompatibilityAsync(fix.FastMoverItemId, dockLocation.Id);
        Assert.False(dockResult.IsOperationallyFeasible);
        Assert.Contains(dockResult.ViolatedHardConstraints, v => v.Contains("operational non-storage area"));

        // Cantilever with standard 30cm item (FastMoverItemId Length is 30cm)
        var cantResult = await service.EvaluateSkuLocationCompatibilityAsync(fix.FastMoverItemId, cantileverLocation.Id);
        Assert.False(cantResult.IsOperationallyFeasible);
        Assert.Contains(cantResult.ViolatedHardConstraints, v => v.Contains("Cantilever storage locations are restricted to long cargo"));
    }

    /// <summary>
    /// Test 17: Existing Occupancy Weight Check:
    /// W_existing + W_proposed > W_usable must be rejected even if W_proposed < W_usable individually.
    /// Usable: 1,000 kg. Existing: 700 kg. Proposed: 400 kg. Total: 1,100 kg -> Fails!
    /// </summary>
    [Fact]
    public async Task EvaluateCompatibility_LocationPartiallyOccupied_RemainingWeightExceeded_FailsHardConstraint()
    {
        var fix = await SeedSlottingEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new SlottingOptimizationService(context, currentUser, NullLogger<SlottingOptimizationService>.Instance);

        var rackLocation = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            LocationCode = "RACK-OCC-WEIGHT",
            LocationType = LocationType.Rack,
            LengthCm = 200m,
            WidthCm = 200m,
            HeightCm = 200m,
            MaxWeightKg = 1000m, // Usable: 1,000 kg
            IsActive = true,
            IsPickable = true
        };

        var existingItem = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-OCC-BASE",
            Description = "Base Inventory",
            LengthCm = 50m,
            WidthCm = 50m,
            HeightCm = 50m,
            WeightKg = 700m // Existing 700 kg
        };

        var existingStock = new StockOnHand
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = existingItem.Id,
            LocationId = rackLocation.Id,
            QtyAvailable = 1m
        };

        var proposedItem = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-PROPOSED-400",
            Description = "Proposed 400kg Cargo",
            LengthCm = 50m,
            WidthCm = 50m,
            HeightCm = 50m,
            WeightKg = 400m // Proposed 400 kg < 1000 kg individually, but 700 + 400 = 1100 > 1000
        };

        context.Locations.Add(rackLocation);
        context.Items.AddRange(existingItem, proposedItem);
        context.StockOnHand.Add(existingStock);
        await context.SaveChangesAsync();

        var result = await service.EvaluateSkuLocationCompatibilityAsync(
            proposedItem.Id, rackLocation.Id);

        Assert.NotNull(result);
        Assert.False(result.IsPhysicallyFeasible);
        Assert.False(result.IsOverallCompatible);
        Assert.Contains(result.ViolatedHardConstraints, v => v.Contains("exceeds location maximum weight limit"));

        var weightEval = result.ConstraintEvaluations.FirstOrDefault(c => c.ConstraintType == SlottingConstraintType.WeightCapacity);
        Assert.NotNull(weightEval);
        Assert.False(weightEval.IsSatisfied);
        Assert.Contains("1,100", weightEval.ViolationDetail);
    }

    /// <summary>
    /// Test 18: Existing Occupancy Volume Check:
    /// V_existing + V_proposed > V_usable must be rejected even if V_proposed < V_usable individually.
    /// Usable: 1,000,000 cm³. Existing: 700,000 cm³. Proposed: 400,000 cm³. Total: 1,100,000 cm³ -> Fails!
    /// </summary>
    [Fact]
    public async Task EvaluateCompatibility_LocationPartiallyOccupied_RemainingVolumeExceeded_FailsHardConstraint()
    {
        var fix = await SeedSlottingEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new SlottingOptimizationService(context, currentUser, NullLogger<SlottingOptimizationService>.Instance);

        var binLocation = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            LocationCode = "BIN-OCC-VOL",
            LocationType = LocationType.Bin,
            LengthCm = 100m,
            WidthCm = 100m,
            HeightCm = 100m, // 1,000,000 cm³ total capacity
            MaxWeightKg = 5000m,
            IsActive = true,
            IsPickable = true
        };

        var existingItem = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-OCC-VOL-BASE",
            Description = "Base Vol Inventory",
            VolumeCm3 = 700000m, // 700,000 cm³
            WeightKg = 10m
        };

        var existingStock = new StockOnHand
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = existingItem.Id,
            LocationId = binLocation.Id,
            QtyAvailable = 1m
        };

        var proposedItem = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-PROPOSED-VOL-400",
            Description = "Proposed 400k cm³ Item",
            VolumeCm3 = 400000m, // 400,000 cm³ individually fits, but 700k + 400k = 1,100,000 > 1,000,000
            WeightKg = 10m
        };

        context.Locations.Add(binLocation);
        context.Items.AddRange(existingItem, proposedItem);
        context.StockOnHand.Add(existingStock);
        await context.SaveChangesAsync();

        var result = await service.EvaluateSkuLocationCompatibilityAsync(
            proposedItem.Id, binLocation.Id);

        Assert.NotNull(result);
        Assert.False(result.IsPhysicallyFeasible);
        Assert.Contains(result.ViolatedHardConstraints, v => v.Contains("exceeding location capacity"));

        var volEval = result.ConstraintEvaluations.FirstOrDefault(c => c.ConstraintType == SlottingConstraintType.VolumetricCapacity);
        Assert.NotNull(volEval);
        Assert.False(volEval.IsSatisfied);
        Assert.Contains("1,100,000", volEval.ViolationDetail);
    }

    /// <summary>
    /// Test 19: Proposed Relocation Quantity Factored In:
    /// Relocating 60 units of a 10 kg item (total 600 kg) into a 500 kg max weight location fails.
    /// </summary>
    [Fact]
    public async Task EvaluateCompatibility_ProposedQuantityFactoredIn_FailsWhenBatchQuantityExceedsLimits()
    {
        var fix = await SeedSlottingEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new SlottingOptimizationService(context, currentUser, NullLogger<SlottingOptimizationService>.Instance);

        var loc = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            LocationCode = "BIN-QTY-WEIGHT-TEST",
            LocationType = LocationType.Bin,
            LengthCm = 150m,
            WidthCm = 150m,
            HeightCm = 150m,
            MaxWeightKg = 500m, // 500 kg max limit
            IsActive = true,
            IsPickable = true
        };

        var item = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-UNIT-10KG",
            Description = "10 kg Carton",
            LengthCm = 30m,
            WidthCm = 30m,
            HeightCm = 30m,
            WeightKg = 10m // 1 unit = 10 kg fits easily
        };

        context.Locations.Add(loc);
        context.Items.Add(item);
        await context.SaveChangesAsync();

        // 1. Single unit evaluation (qty = 1) -> Succeeds
        var singleResult = await service.EvaluateSkuLocationCompatibilityAsync(
            item.Id, loc.Id, quantityToSlot: 1m);
        Assert.True(singleResult.IsPhysicallyFeasible);

        // 2. Batch quantity evaluation (qty = 60 -> 600 kg > 500 kg) -> Fails
        var batchResult = await service.EvaluateSkuLocationCompatibilityAsync(
            item.Id, loc.Id, quantityToSlot: 60m);
        Assert.False(batchResult.IsPhysicallyFeasible);
        Assert.Contains(batchResult.ViolatedHardConstraints, v => v.Contains("exceeds location maximum weight limit"));
        Assert.Contains("600.0", batchResult.ViolatedHardConstraints[0]);
    }

    /// <summary>
    /// Test 20: Policy Versioning & Historical Reproducibility.
    /// Verifies that historical simulation batches retain their original PolicyVersion and immutable JSON snapshot
    /// even if the active SlottingPolicy is modified later in the database.
    /// </summary>
    [Fact]
    public async Task SimulateSlotting_PreservesPolicyVersionAndSnapshot_HistoricalBatchImmutableAfterPolicyUpdate()
    {
        var fix = await SeedSlottingEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new SlottingOptimizationService(context, currentUser, NullLogger<SlottingOptimizationService>.Instance);

        // 1. Create Policy v1 with Golden Zone 0.80 - 1.50
        var policyV1 = await service.CreatePolicyAsync(new CreateSlottingPolicyRequestDto
        {
            Name = "Dynamic Golden Zone Policy v1",
            FacilityId = fix.FacilityId,
            GoldenZoneMinZMeters = 0.80m,
            GoldenZoneMaxZMeters = 1.50m
        });

        // 2. Simulate batch under Policy v1
        var batch = await service.SimulateSlottingRecommendationsAsync(new SlottingSimulationRequestDto
        {
            FacilityId = fix.FacilityId,
            PolicyId = policyV1.Id
        });

        Assert.Equal(1, batch.PolicyVersion);
        Assert.NotNull(batch.PolicySnapshotJson);
        Assert.Contains("0.8", batch.PolicySnapshotJson);

        // 3. Update Policy in DB to v2 with altered golden zone
        var policyEntity = await context.SlottingPolicies.FirstAsync(p => p.Id == policyV1.Id);
        policyEntity.GoldenZoneMinZMeters = 1.10m;
        policyEntity.GoldenZoneMaxZMeters = 1.70m;
        policyEntity.Version = 2;
        await context.SaveChangesAsync();

        // 4. Retrieve historical batch and verify PolicyVersion & Snapshot remain completely immutable
        var historicalBatch = await service.GetRecommendationBatchAsync(batch.Id);
        Assert.NotNull(historicalBatch);
        Assert.Equal(1, historicalBatch.PolicyVersion); // Remains v1!
        Assert.Contains("0.8", historicalBatch.PolicySnapshotJson); // Snapshot contains original 0.80m!
    }

    /// <summary>
    /// Test 21: Economic Savings Industrial Engineering Formula & Boundary Tests:
    /// Tests zero savings, negative savings, boundary value exactly $5.00, and above threshold.
    /// </summary>
    [Fact]
    public void CalculateEconomicSavings_MathematicalFormulaAndBoundaryThresholdTests()
    {
        // Case 1: Standard positive case (Round-trip 15m, 120 picks, $25/hr, 6 min move, 3 mo amort)
        // Travel: (15 * 120) / (1 * 3600) * 25 = 0.5 * 25 = $12.50
        // Move: (6 / 60) * 25 = $2.50. Amortized monthly: 2.50 / 3 = $0.83
        // Net: 12.50 - 0.83 = $11.67 >= $5.00 -> True
        var case1 = SlottingOptimizationService.CalculateEconomicSavings(
            roundTripTravelSavedMeters: 15.0m,
            monthlyPicks: 120m,
            walkingSpeedMps: 1.0m,
            laborHourlyRateUsd: 25.00m,
            relocationLaborMinutes: 6.00m,
            amortizationMonths: 3.0m,
            minLaborSavingsUsd: 5.00m);

        Assert.Equal(12.50m, case1.travelSavingsUsd);
        Assert.Equal(2.50m, case1.moveLaborCostUsd);
        Assert.Equal(11.67m, case1.netMonthlySavingsUsd);
        Assert.True(case1.meetsThreshold);

        // Case 2: Zero travel savings -> Negative monthly gain -> Rejected
        var case2 = SlottingOptimizationService.CalculateEconomicSavings(
            roundTripTravelSavedMeters: 0.0m,
            monthlyPicks: 100m,
            walkingSpeedMps: 1.0m,
            laborHourlyRateUsd: 25.00m,
            relocationLaborMinutes: 6.00m,
            amortizationMonths: 3.0m,
            minLaborSavingsUsd: 5.00m);

        Assert.Equal(0.00m, case2.travelSavingsUsd);
        Assert.Equal(-0.83m, case2.netMonthlySavingsUsd);
        Assert.False(case2.meetsThreshold);

        // Case 3: Exact boundary threshold: net monthly savings is exactly $5.00 -> Accepted (>= comparison)
        // If move labor cost is $0, and travel savings is $5.00
        var case3 = SlottingOptimizationService.CalculateEconomicSavings(
            roundTripTravelSavedMeters: 7.2m, // 7.2 * 100 / 3600 = 0.2 hr * $25 = $5.00
            monthlyPicks: 100m,
            walkingSpeedMps: 1.0m,
            laborHourlyRateUsd: 25.00m,
            relocationLaborMinutes: 0.00m,
            amortizationMonths: 3.0m,
            minLaborSavingsUsd: 5.00m);

        Assert.Equal(5.00m, case3.netMonthlySavingsUsd);
        Assert.True(case3.meetsThreshold); // Exactly $5.00 meets threshold!

        // Case 4: Below threshold ($4.99) -> Rejected
        var case4 = SlottingOptimizationService.CalculateEconomicSavings(
            roundTripTravelSavedMeters: 7.18m,
            monthlyPicks: 100m,
            walkingSpeedMps: 1.0m,
            laborHourlyRateUsd: 25.00m,
            relocationLaborMinutes: 0.00m,
            amortizationMonths: 3.0m,
            minLaborSavingsUsd: 5.00m);

        Assert.True(case4.netMonthlySavingsUsd < 5.00m);
        Assert.False(case4.meetsThreshold);
    }

    /// <summary>
    /// Test 22: Object-Level Tenant and Facility Isolation.
    /// Proves that Client A cannot retrieve or approve Client B's batch, and a facility-scoped user cannot access another facility's policies.
    /// </summary>
    [Fact]
    public async Task TenantAndFacilityIsolation_EnforcesStrictObjectLevelBoundaries()
    {
        var fix = await SeedSlottingEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);

        var clientAId = fix.ClientId;
        var clientBId = Guid.NewGuid();

        var clientB = new Client
        {
            Id = clientBId,
            AccountCode = "CLI-B-ISOLATION",
            CompanyName = "Client B Logistics",
            Status = ClientStatus.Active
        };
        context.Clients.Add(clientB);

        var activePolicy = await context.SlottingPolicies.FirstAsync();
        var batchB = SlottingRecommendationBatch.Create(
            facilityId: fix.FacilityId,
            policyId: activePolicy.Id,
            batchNumber: "SLOT-CLIENT-B-001",
            clientId: clientBId);
        batchB.Status = SlottingBatchStatus.Simulated;
        context.SlottingRecommendationBatches.Add(batchB);
        await context.SaveChangesAsync();

        // 1. Client A user attempts to retrieve Client B's batch -> ForbiddenException
        var clientAUser = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            UserType = UserType.Client,
            TenantId = clientAId,
            RolesList = ["ClientAdmin"]
        };
        var clientAService = new SlottingOptimizationService(context, clientAUser, NullLogger<SlottingOptimizationService>.Instance);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            clientAService.GetRecommendationBatchAsync(batchB.Id));

        // 2. Client A user attempts to approve Client B's batch -> ForbiddenException
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            clientAService.ApproveRecommendationBatchAsync(batchB.Id));

        // 3. User scoped to Facility 1 attempts to access Facility 2 policy -> ForbiddenException
        var facility2Id = Guid.NewGuid();
        var fac1User = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            UserType = UserType.Staff,
            FacilityId = fix.FacilityId, // Scoped to ORD-01
            RolesList = ["WarehouseStaff"]
        };
        var fac1Service = new SlottingOptimizationService(context, fac1User, NullLogger<SlottingOptimizationService>.Instance);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            fac1Service.GetActivePolicyForFacilityAsync(facility2Id));
    }

    /// <summary>
    /// Test 23: Complete Operational Boundary Read-Only Invariance.
    /// Verifies that simulating and approving recommendations causes ZERO mutations across:
    /// StockOnHand, Locations, InventoryTransaction, InventoryAllocation, PutawayTask, PickTask, BillableActivity, and Invoice.
    /// Explicitly pre-seeds records in all 8 operational tables, takes exhaustive before-and-after property snapshots,
    /// and guarantees ZERO additions, ZERO modifications, and ZERO deletions across all 8 operational entity tables.
    /// </summary>
    [Fact]
    public async Task SimulateAndApprove_CompleteOperationalBoundaryInvariance_NoMutationsToAnyOperationalLedger()
    {
        var fix = await SeedSlottingEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new SlottingOptimizationService(context, currentUser, NullLogger<SlottingOptimizationService>.Instance);

        // Pre-seed baseline records in all operational tables that don't already have pre-seeded data from fixture
        // 1. StockOnHand & Locations already pre-seeded in fix.
        var existingFastStock = await context.StockOnHand.FirstAsync(s => s.ItemId == fix.FastMoverItemId);

        // 2. Pre-seed BillingPeriod (needed by Invoices & BillableActivities)
        var billingPeriod = BillingPeriod.Create(
            fix.ClientId, "BP-SLOT-TEST-2026-11", "Slotting Test Billing Period",
            new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 30, 23, 59, 59, DateTimeKind.Utc),
            BillingFrequency.Monthly,
            fix.FacilityId);
        context.BillingPeriods.Add(billingPeriod);

        // 3. Pre-seed InventoryTransaction
        var baselineTx = new InventoryTransaction
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = fix.FastMoverItemId,
            FromLocationId = fix.UpperReserveLocationId,
            ToLocationId = fix.GoldenZonePickLocationId,
            TxType = InventoryTxType.Move,
            Quantity = 12m,
            ReferenceType = "BaselineVerification",
            ReferenceId = Guid.NewGuid(),
            Notes = "Operational Baseline Transaction",
            TxTimestamp = new DateTime(2026, 11, 14, 8, 0, 0, DateTimeKind.Utc)
        };
        context.InventoryTransactions.Add(baselineTx);

        // 4. Pre-seed InventoryAllocation
        var baselineAlloc = new InventoryAllocation
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            OutboundOrderId = Guid.NewGuid(),
            OutboundOrderLineId = Guid.NewGuid(),
            StockOnHandId = existingFastStock.Id,
            ItemId = fix.FastMoverItemId,
            LocationId = fix.UpperReserveLocationId,
            QuantityRequested = 15m,
            QuantityAllocated = 15m,
            QuantityReleased = 0m,
            QuantityPicked = 0m,
            Status = AllocationStatus.Allocated,
            StrategyUsed = AllocationStrategyType.FIFO,
            AllocatedAtUtc = new DateTime(2026, 11, 14, 8, 15, 0, DateTimeKind.Utc)
        };
        context.InventoryAllocations.Add(baselineAlloc);

        // 5. Pre-seed PutawayTask
        var baselinePutaway = new PutawayTask
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = fix.FastMoverItemId,
            Quantity = 25m,
            SourceLocationId = fix.UpperReserveLocationId,
            SuggestedLocationId = fix.GoldenZonePickLocationId,
            TargetLocationId = fix.GoldenZonePickLocationId,
            Status = PutawayTaskStatus.Pending
        };
        context.PutawayTasks.Add(baselinePutaway);

        // 6. Pre-seed PickTask
        var baselinePick = new PickTask
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            PickWaveId = Guid.NewGuid(),
            OutboundOrderId = baselineAlloc.OutboundOrderId,
            OutboundOrderLineId = baselineAlloc.OutboundOrderLineId,
            InventoryAllocationId = baselineAlloc.Id,
            StockOnHandId = existingFastStock.Id,
            ItemId = fix.FastMoverItemId,
            SourceLocationId = fix.UpperReserveLocationId,
            QuantityToPick = 15m,
            QuantityPicked = 0m,
            Status = PickTaskStatus.Pending
        };
        context.PickTasks.Add(baselinePick);

        // 7. Pre-seed BillableActivity
        var baselineActivity = BillableActivity.Create(
            fix.ClientId, fix.FacilityId, BillableActivityType.StoragePalletDay,
            "DailyInventorySnapshot", Guid.NewGuid(), "SNAP-SLOT-BASE-01", 100m, "PALLET_DAY",
            new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc), "IDEMP-SLOT-BASE-01",
            "Baseline Operational Billable Activity", billingPeriod.Id);
        baselineActivity.Rate(2.50m, 250.00m, "USD");
        context.BillableActivities.Add(baselineActivity);

        // 8. Pre-seed Invoice
        var baselineInvoice = Invoice.Create(
            fix.ClientId, billingPeriod.Id, "INV-SLOT-BASE-001", new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc));
        baselineInvoice.AddLine(InvoiceLine.Create(1, InvoiceChargeCategory.Storage, "Storage baseline", 1, "PALLET", 250m, 250m));
        context.Invoices.Add(baselineInvoice);

        await context.SaveChangesAsync();

        // Capture authoritative before-state snapshots (all records and properties) across ALL 8 operational tables
        var preStock = await context.StockOnHand.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var preLocations = await context.Locations.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var preTxs = await context.InventoryTransactions.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var preAllocs = await context.InventoryAllocations.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var prePutaways = await context.PutawayTasks.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var prePicks = await context.PickTasks.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var preActivities = await context.BillableActivities.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var preInvoices = await context.Invoices.AsNoTracking().OrderBy(x => x.Id).ToListAsync();

        // Verify non-zero baseline counts across ALL 8 tables before simulation
        Assert.True(preStock.Count > 0, "StockOnHand must contain pre-seeded records");
        Assert.True(preLocations.Count > 0, "Locations must contain pre-seeded records");
        Assert.True(preTxs.Count > 0, "InventoryTransactions must contain pre-seeded records");
        Assert.True(preAllocs.Count > 0, "InventoryAllocations must contain pre-seeded records");
        Assert.True(prePutaways.Count > 0, "PutawayTasks must contain pre-seeded records");
        Assert.True(prePicks.Count > 0, "PickTasks must contain pre-seeded records");
        Assert.True(preActivities.Count > 0, "BillableActivities must contain pre-seeded records");
        Assert.True(preInvoices.Count > 0, "Invoices must contain pre-seeded records");

        // 1. Run simulation
        var batch = await service.SimulateSlottingRecommendationsAsync(new SlottingSimulationRequestDto
        {
            FacilityId = fix.FacilityId
        });
        Assert.NotNull(batch);
        Assert.NotEmpty(batch.Recommendations);

        // 2. Approve batch
        var approvedBatch = await service.ApproveRecommendationBatchAsync(batch.Id);
        Assert.Equal(SlottingBatchStatus.Approved, approvedBatch.Status);

        // 3. Capture authoritative post-state snapshots across ALL 8 operational tables
        var postStock = await context.StockOnHand.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var postLocations = await context.Locations.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var postTxs = await context.InventoryTransactions.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var postAllocs = await context.InventoryAllocations.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var postPutaways = await context.PutawayTasks.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var postPicks = await context.PickTasks.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var postActivities = await context.BillableActivities.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var postInvoices = await context.Invoices.AsNoTracking().OrderBy(x => x.Id).ToListAsync();

        // 4. Exhaustive Invariance Verifications across all 8 tables:
        // A. StockOnHand (Zero additions, zero deletions, zero property mutations)
        Assert.Equal(preStock.Count, postStock.Count);
        for (int i = 0; i < preStock.Count; i++)
        {
            Assert.Equal(preStock[i].Id, postStock[i].Id);
            Assert.Equal(preStock[i].FacilityId, postStock[i].FacilityId);
            Assert.Equal(preStock[i].ClientId, postStock[i].ClientId);
            Assert.Equal(preStock[i].ItemId, postStock[i].ItemId);
            Assert.Equal(preStock[i].LocationId, postStock[i].LocationId);
            Assert.Equal(preStock[i].QtyAvailable, postStock[i].QtyAvailable);
            Assert.Equal(preStock[i].QtyAllocated, postStock[i].QtyAllocated);
            Assert.Equal(preStock[i].QtyReserved, postStock[i].QtyReserved);
            Assert.Equal(preStock[i].QtyQuarantined, postStock[i].QtyQuarantined);
            Assert.Equal(preStock[i].QtyDamaged, postStock[i].QtyDamaged);
            Assert.Equal(preStock[i].QtyOnHold, postStock[i].QtyOnHold);
            Assert.Equal(preStock[i].TotalQuantity, postStock[i].TotalQuantity);
        }

        // B. Locations (Zero additions, zero deletions, zero property mutations)
        Assert.Equal(preLocations.Count, postLocations.Count);
        for (int i = 0; i < preLocations.Count; i++)
        {
            Assert.Equal(preLocations[i].Id, postLocations[i].Id);
            Assert.Equal(preLocations[i].FacilityId, postLocations[i].FacilityId);
            Assert.Equal(preLocations[i].LocationCode, postLocations[i].LocationCode);
            Assert.Equal(preLocations[i].LocationType, postLocations[i].LocationType);
            Assert.Equal(preLocations[i].LengthCm, postLocations[i].LengthCm);
            Assert.Equal(preLocations[i].WidthCm, postLocations[i].WidthCm);
            Assert.Equal(preLocations[i].HeightCm, postLocations[i].HeightCm);
            Assert.Equal(preLocations[i].MaxWeightKg, postLocations[i].MaxWeightKg);
            Assert.Equal(preLocations[i].IsActive, postLocations[i].IsActive);
            Assert.Equal(preLocations[i].IsPickable, postLocations[i].IsPickable);
            Assert.Equal(preLocations[i].IsDamaged, postLocations[i].IsDamaged);
            Assert.Equal(preLocations[i].IsLocked, postLocations[i].IsLocked);
        }

        // C. InventoryTransactions (Zero additions, zero deletions, zero property mutations)
        Assert.Equal(preTxs.Count, postTxs.Count);
        for (int i = 0; i < preTxs.Count; i++)
        {
            Assert.Equal(preTxs[i].Id, postTxs[i].Id);
            Assert.Equal(preTxs[i].ClientId, postTxs[i].ClientId);
            Assert.Equal(preTxs[i].FacilityId, postTxs[i].FacilityId);
            Assert.Equal(preTxs[i].ItemId, postTxs[i].ItemId);
            Assert.Equal(preTxs[i].FromLocationId, postTxs[i].FromLocationId);
            Assert.Equal(preTxs[i].ToLocationId, postTxs[i].ToLocationId);
            Assert.Equal(preTxs[i].TxType, postTxs[i].TxType);
            Assert.Equal(preTxs[i].Quantity, postTxs[i].Quantity);
            Assert.Equal(preTxs[i].ReferenceType, postTxs[i].ReferenceType);
            Assert.Equal(preTxs[i].ReferenceId, postTxs[i].ReferenceId);
            Assert.Equal(preTxs[i].Notes, postTxs[i].Notes);
            Assert.Equal(preTxs[i].TxTimestamp, postTxs[i].TxTimestamp);
        }

        // D. InventoryAllocations (Zero additions, zero deletions, zero property mutations)
        Assert.Equal(preAllocs.Count, postAllocs.Count);
        for (int i = 0; i < preAllocs.Count; i++)
        {
            Assert.Equal(preAllocs[i].Id, postAllocs[i].Id);
            Assert.Equal(preAllocs[i].ClientId, postAllocs[i].ClientId);
            Assert.Equal(preAllocs[i].FacilityId, postAllocs[i].FacilityId);
            Assert.Equal(preAllocs[i].OutboundOrderId, postAllocs[i].OutboundOrderId);
            Assert.Equal(preAllocs[i].OutboundOrderLineId, postAllocs[i].OutboundOrderLineId);
            Assert.Equal(preAllocs[i].StockOnHandId, postAllocs[i].StockOnHandId);
            Assert.Equal(preAllocs[i].ItemId, postAllocs[i].ItemId);
            Assert.Equal(preAllocs[i].LocationId, postAllocs[i].LocationId);
            Assert.Equal(preAllocs[i].QuantityRequested, postAllocs[i].QuantityRequested);
            Assert.Equal(preAllocs[i].QuantityAllocated, postAllocs[i].QuantityAllocated);
            Assert.Equal(preAllocs[i].QuantityReleased, postAllocs[i].QuantityReleased);
            Assert.Equal(preAllocs[i].QuantityPicked, postAllocs[i].QuantityPicked);
            Assert.Equal(preAllocs[i].Status, postAllocs[i].Status);
            Assert.Equal(preAllocs[i].StrategyUsed, postAllocs[i].StrategyUsed);
        }

        // E. PutawayTasks (Zero additions, zero deletions, zero property mutations)
        Assert.Equal(prePutaways.Count, postPutaways.Count);
        for (int i = 0; i < prePutaways.Count; i++)
        {
            Assert.Equal(prePutaways[i].Id, postPutaways[i].Id);
            Assert.Equal(prePutaways[i].ClientId, postPutaways[i].ClientId);
            Assert.Equal(prePutaways[i].FacilityId, postPutaways[i].FacilityId);
            Assert.Equal(prePutaways[i].ItemId, postPutaways[i].ItemId);
            Assert.Equal(prePutaways[i].Quantity, postPutaways[i].Quantity);
            Assert.Equal(prePutaways[i].SourceLocationId, postPutaways[i].SourceLocationId);
            Assert.Equal(prePutaways[i].SuggestedLocationId, postPutaways[i].SuggestedLocationId);
            Assert.Equal(prePutaways[i].TargetLocationId, postPutaways[i].TargetLocationId);
            Assert.Equal(prePutaways[i].Status, postPutaways[i].Status);
        }

        // F. PickTasks (Zero additions, zero deletions, zero property mutations)
        Assert.Equal(prePicks.Count, postPicks.Count);
        for (int i = 0; i < prePicks.Count; i++)
        {
            Assert.Equal(prePicks[i].Id, postPicks[i].Id);
            Assert.Equal(prePicks[i].ClientId, postPicks[i].ClientId);
            Assert.Equal(prePicks[i].FacilityId, postPicks[i].FacilityId);
            Assert.Equal(prePicks[i].PickWaveId, postPicks[i].PickWaveId);
            Assert.Equal(prePicks[i].OutboundOrderId, postPicks[i].OutboundOrderId);
            Assert.Equal(prePicks[i].OutboundOrderLineId, postPicks[i].OutboundOrderLineId);
            Assert.Equal(prePicks[i].InventoryAllocationId, postPicks[i].InventoryAllocationId);
            Assert.Equal(prePicks[i].StockOnHandId, postPicks[i].StockOnHandId);
            Assert.Equal(prePicks[i].ItemId, postPicks[i].ItemId);
            Assert.Equal(prePicks[i].SourceLocationId, postPicks[i].SourceLocationId);
            Assert.Equal(prePicks[i].QuantityToPick, postPicks[i].QuantityToPick);
            Assert.Equal(prePicks[i].QuantityPicked, postPicks[i].QuantityPicked);
            Assert.Equal(prePicks[i].Status, postPicks[i].Status);
        }

        // G. BillableActivities (Zero additions, zero deletions, zero property mutations)
        Assert.Equal(preActivities.Count, postActivities.Count);
        for (int i = 0; i < preActivities.Count; i++)
        {
            Assert.Equal(preActivities[i].Id, postActivities[i].Id);
            Assert.Equal(preActivities[i].ClientId, postActivities[i].ClientId);
            Assert.Equal(preActivities[i].FacilityId, postActivities[i].FacilityId);
            Assert.Equal(preActivities[i].ActivityType, postActivities[i].ActivityType);
            Assert.Equal(preActivities[i].Status, postActivities[i].Status);
            Assert.Equal(preActivities[i].SourceType, postActivities[i].SourceType);
            Assert.Equal(preActivities[i].SourceId, postActivities[i].SourceId);
            Assert.Equal(preActivities[i].SourceReference, postActivities[i].SourceReference);
            Assert.Equal(preActivities[i].Quantity, postActivities[i].Quantity);
            Assert.Equal(preActivities[i].UnitOfMeasure, postActivities[i].UnitOfMeasure);
            Assert.Equal(preActivities[i].UnitPrice, postActivities[i].UnitPrice);
            Assert.Equal(preActivities[i].TotalAmount, postActivities[i].TotalAmount);
            Assert.Equal(preActivities[i].Currency, postActivities[i].Currency);
            Assert.Equal(preActivities[i].IdempotencyKey, postActivities[i].IdempotencyKey);
        }

        // H. Invoices (Zero additions, zero deletions, zero property mutations)
        Assert.Equal(preInvoices.Count, postInvoices.Count);
        for (int i = 0; i < preInvoices.Count; i++)
        {
            Assert.Equal(preInvoices[i].Id, postInvoices[i].Id);
            Assert.Equal(preInvoices[i].ClientId, postInvoices[i].ClientId);
            Assert.Equal(preInvoices[i].FacilityId, postInvoices[i].FacilityId);
            Assert.Equal(preInvoices[i].BillingPeriodId, postInvoices[i].BillingPeriodId);
            Assert.Equal(preInvoices[i].InvoiceNumber, postInvoices[i].InvoiceNumber);
            Assert.Equal(preInvoices[i].Status, postInvoices[i].Status);
            Assert.Equal(preInvoices[i].Currency, postInvoices[i].Currency);
            Assert.Equal(preInvoices[i].SubtotalAmount, postInvoices[i].SubtotalAmount);
            Assert.Equal(preInvoices[i].TaxRate, postInvoices[i].TaxRate);
            Assert.Equal(preInvoices[i].TaxAmount, postInvoices[i].TaxAmount);
            Assert.Equal(preInvoices[i].TotalAmount, postInvoices[i].TotalAmount);
            Assert.Equal(preInvoices[i].PaidAmount, postInvoices[i].PaidAmount);
            Assert.Equal(preInvoices[i].CreditedAmount, postInvoices[i].CreditedAmount);
            Assert.Equal(preInvoices[i].OutstandingBalance, postInvoices[i].OutstandingBalance);
        }
    }

    /// <summary>
    /// Test 24: Authoritative Physical Occupancy Across All Inventory Status Partitions:
    /// Physical destination capacity cannot ignore non-pickable inventory (Damaged, Quarantined, OnHold).
    /// Usable capacity: 1,000 kg.
    /// Existing stock has 0 Available/Allocated/Reserved, but:
    /// 30 Damaged + 25 Quarantined + 20 OnHold = 75 units (750 kg).
    /// Proposed relocation: 35 units (350 kg).
    /// 750 kg + 350 kg = 1,100 kg > 1,000 kg -> Must fail WeightCapacity!
    /// </summary>
    [Fact]
    public async Task EvaluateCompatibility_DamagedQuarantinedAndOnHoldInventory_CannotBeIgnoredWhenEvaluatingCapacity()
    {
        var fix = await SeedSlottingEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new SlottingOptimizationService(context, currentUser, NullLogger<SlottingOptimizationService>.Instance);

        var destinationLoc = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            LocationCode = "RACK-STATUS-OCC-TEST",
            LocationType = LocationType.Rack,
            LengthCm = 200m,
            WidthCm = 200m,
            HeightCm = 200m,
            MaxWeightKg = 1000m, // 1,000 kg max structural capacity
            IsActive = true,
            IsPickable = true
        };

        var existingItem = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-OCC-HOLD-ITEM",
            Description = "Quarantined/Damaged Inventory",
            LengthCm = 20m,
            WidthCm = 20m,
            HeightCm = 20m,
            WeightKg = 10m // 10 kg per unit
        };

        // Seed stock with ZERO available/allocated/reserved, but 75 units in non-pickable statuses
        var partitionedStock = new StockOnHand
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = existingItem.Id,
            LocationId = destinationLoc.Id,
            QtyAvailable = 0m,
            QtyAllocated = 0m,
            QtyReserved = 0m,
            QtyDamaged = 30m,     // 300 kg
            QtyQuarantined = 25m, // 250 kg
            QtyOnHold = 20m       // 200 kg
            // Total physically present: 75 units = 750 kg!
        };

        var candidateItem = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-PROPOSED-CANDIDATE",
            Description = "Candidate Relocation SKU",
            LengthCm = 20m,
            WidthCm = 20m,
            HeightCm = 20m,
            WeightKg = 10m // 10 kg per unit
        };

        context.Locations.Add(destinationLoc);
        context.Items.AddRange(existingItem, candidateItem);
        context.StockOnHand.Add(partitionedStock);
        await context.SaveChangesAsync();

        // Propose relocating 35 units (350 kg)
        // If non-pickable inventory was ignored, existing occupancy would be evaluated as 0 kg -> False Positive
        // With authoritative physical occupancy: 750 kg existing + 350 kg proposed = 1,100 kg > 1,000 kg -> Must FAIL
        var result = await service.EvaluateSkuLocationCompatibilityAsync(
            candidateItem.Id, destinationLoc.Id, quantityToSlot: 35m);

        Assert.NotNull(result);
        Assert.False(result.IsPhysicallyFeasible);
        Assert.False(result.IsOverallCompatible);
        Assert.Contains(result.ViolatedHardConstraints, v => v.Contains("exceeds location maximum weight limit"));

        var weightEval = result.ConstraintEvaluations.FirstOrDefault(c => c.ConstraintType == SlottingConstraintType.WeightCapacity);
        Assert.NotNull(weightEval);
        Assert.False(weightEval.IsSatisfied);
        Assert.Contains("750.0", weightEval.ViolationDetail);  // Confirms 750 kg existing occupancy recognized
        Assert.Contains("350.0", weightEval.ViolationDetail);  // Confirms 350 kg proposed weight recognized
        Assert.Contains("1,100.0", weightEval.ViolationDetail); // Confirms total 1,100 kg exceeds 1,000 kg
    }

    #endregion
}

