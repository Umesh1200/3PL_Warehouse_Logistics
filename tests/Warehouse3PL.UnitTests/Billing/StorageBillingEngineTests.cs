using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Services.Billing;
using Warehouse3PL.UnitTests.Common;

namespace Warehouse3PL.UnitTests.Billing;

/// <summary>
/// Comprehensive verification test suite for Phase 5.2 Recurring Storage &amp; Holding Fee Calculation Engine.
/// Validates historical daily snapshots, pallet-day, carton-day, CBM calculations, missing dimension fallbacks,
/// billable status policies (damaged, quarantined, in-transit, returns awaiting inspection),
/// rate card effective-dating, facility overrides, minimum spend reconciliation, and snapshot immutability.
/// </summary>
public class StorageBillingEngineTests
{
    private class TestEnvFixture
    {
        public string DbName { get; } = Guid.NewGuid().ToString();
        public Guid ClientId { get; set; } = Guid.NewGuid();
        public Guid ForeignClientId { get; set; } = Guid.NewGuid();
        public Guid FacilityId { get; set; } = Guid.NewGuid();
        public Guid SecondaryFacilityId { get; set; } = Guid.NewGuid();
        public Guid LocationId { get; set; } = Guid.NewGuid();
        public Guid SecondaryLocationId { get; set; } = Guid.NewGuid();
        public Guid ItemStandardId { get; set; } = Guid.NewGuid();
        public Guid ItemMissingDimsId { get; set; } = Guid.NewGuid();
        public Guid StaffUserId { get; set; } = Guid.NewGuid();
    }

    private static async Task<TestEnvFixture> SeedTestEnvironmentAsync()
    {
        var fix = new TestEnvFixture();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var client = new Client
        {
            Id = fix.ClientId,
            CompanyName = "Sovereign Retail Partners",
            AccountCode = "SOV01",
            Status = ClientStatus.Active
        };

        var foreignClient = new Client
        {
            Id = fix.ForeignClientId,
            CompanyName = "Vanguard Cargo Group",
            AccountCode = "VAN01",
            Status = ClientStatus.Active
        };

        var fac1 = new Facility
        {
            Id = fix.FacilityId,
            FacilityCode = "FAC-ORD1",
            Name = "Chicago Logistics Hub"
        };

        var fac2 = new Facility
        {
            Id = fix.SecondaryFacilityId,
            FacilityCode = "FAC-DFW1",
            Name = "Dallas Distribution Center"
        };

        var loc1 = new Location
        {
            Id = fix.LocationId,
            FacilityId = fix.FacilityId,
            LocationCode = "A-01-01",
            LocationType = LocationType.Rack,
            IsActive = true
        };

        var loc2 = new Location
        {
            Id = fix.SecondaryLocationId,
            FacilityId = fix.SecondaryFacilityId,
            LocationCode = "B-01-01",
            LocationType = LocationType.Rack,
            IsActive = true
        };

        // Standard Item with explicit dimensions: 50cm x 40cm x 30cm = 60,000 cm3 = 0.0600 CBM per unit
        var itemStandard = new Item
        {
            Id = fix.ItemStandardId,
            ClientId = fix.ClientId,
            Sku = "SKU-STD-01",
            Description = "Standard Widget Box",
            LengthCm = 50m,
            WidthCm = 40m,
            HeightCm = 30m,
            VolumeCm3 = 60000m,
            WeightKg = 5m,
            Status = ItemStatus.Active
        };

        // Pallet Packaging Unit: 500 units per pallet
        var palletPackaging = new PackagingUnit
        {
            Id = Guid.NewGuid(),
            ItemId = fix.ItemStandardId,
            UnitLevel = PackagingUnitLevel.Pallet,
            UnitName = "Standard Pallet",
            ConversionQty = 500m
        };

        // Case Packaging Unit: 25 units per carton
        var casePackaging = new PackagingUnit
        {
            Id = Guid.NewGuid(),
            ItemId = fix.ItemStandardId,
            UnitLevel = PackagingUnitLevel.Case,
            UnitName = "Master Carton",
            ConversionQty = 25m
        };

        itemStandard.PackagingUnits.Add(palletPackaging);
        itemStandard.PackagingUnits.Add(casePackaging);

        // Item with missing dimensions (0cm x 0cm x 0cm)
        var itemMissingDims = new Item
        {
            Id = fix.ItemMissingDimsId,
            ClientId = fix.ClientId,
            Sku = "SKU-NODIMS-01",
            Description = "Dimensionless Part",
            LengthCm = 0m,
            WidthCm = 0m,
            HeightCm = 0m,
            VolumeCm3 = 0m,
            WeightKg = 1m,
            Status = ItemStatus.Active
        };

        var staff = new User
        {
            Id = fix.StaffUserId,
            Email = "storage.billing@warehouse3pl.test",
            PasswordHash = "hashed",
            FirstName = "Eleanor",
            LastName = "Rigby",
            UserType = UserType.Staff,
            Status = UserStatus.Active
        };

        context.Clients.AddRange(client, foreignClient);
        context.Facilities.AddRange(fac1, fac2);
        context.Locations.AddRange(loc1, loc2);
        context.Items.AddRange(itemStandard, itemMissingDims);
        context.PackagingUnits.AddRange(palletPackaging, casePackaging);
        context.Users.Add(staff);
        await context.SaveChangesAsync();

        return fix;
    }

    private static (InventorySnapshotService SnapshotService, StorageBillingService StorageBillingService, RateCardService RateCardService, IApplicationDbContext Context) CreateServices(
        TestEnvFixture fix,
        Guid? tenantOverride = null,
        bool bypassTenant = false)
    {
        var tenantProvider = new TestTenantProvider
        {
            TenantId = tenantOverride ?? fix.ClientId,
            BypassTenantFilter = bypassTenant
        };

        var userContext = new TestCurrentUserContext
        {
            UserId = fix.StaffUserId,
            UserType = UserType.Staff,
            FacilityId = fix.FacilityId
        };

        var context = TestDbContextFactory.Create(tenantProvider, userContext, fix.DbName);

        var rateCardService = new RateCardService(
            context,
            tenantProvider,
            NullLogger<RateCardService>.Instance);

        var snapshotService = new InventorySnapshotService(
            context,
            tenantProvider,
            NullLogger<InventorySnapshotService>.Instance);

        var storageBillingService = new StorageBillingService(
            context,
            rateCardService,
            tenantProvider,
            NullLogger<StorageBillingService>.Instance);

        return (snapshotService, storageBillingService, rateCardService, context);
    }

    [Fact]
    public async Task Test_01_Duplicate_Daily_Snapshot_Prevention_Idempotency()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (snapshotService, _, _, context) = CreateServices(fix, bypassTenant: true);

        // Seed some stock
        var stock = new StockOnHand
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = fix.ItemStandardId,
            LocationId = fix.LocationId,
            QtyAvailable = 500m
        };
        context.StockOnHand.Add(stock);
        await context.SaveChangesAsync();

        var date = new DateOnly(2026, 3, 1);

        // First capture
        var snap1 = await snapshotService.CaptureDailySnapshotAsync(fix.ClientId, fix.FacilityId, date);
        Assert.NotNull(snap1);
        Assert.Equal(StorageSnapshotStatus.Finalized, snap1.Status);
        Assert.Equal(500m, snap1.TotalQuantity);

        // Second capture for same date must return existing snapshot idempotently without duplicating records
        var snap2 = await snapshotService.CaptureDailySnapshotAsync(fix.ClientId, fix.FacilityId, date);
        Assert.Equal(snap1.Id, snap2.Id);
        Assert.Equal(snap1.RecordCount, snap2.RecordCount);

        var totalSnapshots = await context.DailyInventorySnapshots
            .CountAsync(s => s.ClientId == fix.ClientId && s.FacilityId == fix.FacilityId && s.SnapshotDate == date);
        Assert.Equal(1, totalSnapshots);
    }

    [Fact]
    public async Task Test_02_PalletDay_CartonDay_And_CbmDay_Calculations()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (snapshotService, _, _, context) = CreateServices(fix, bypassTenant: true);

        // 1000 units of standard item:
        // Packaging conversions: 500 units/pallet -> 1000 / 500 = 2.0 pallets
        // Case conversions: 25 units/case -> 1000 / 25 = 40.0 cartons
        // CBM: 0.0600 CBM/unit -> 1000 * 0.0600 = 60.0000 CBM
        var stock = new StockOnHand
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = fix.ItemStandardId,
            LocationId = fix.LocationId,
            QtyAvailable = 1000m
        };
        context.StockOnHand.Add(stock);
        await context.SaveChangesAsync();

        var snap = await snapshotService.CaptureDailySnapshotAsync(fix.ClientId, fix.FacilityId, new DateOnly(2026, 3, 5));

        Assert.Equal(1000m, snap.TotalQuantity);
        Assert.Equal(2.0m, snap.TotalPalletCount);
        Assert.Equal(40.0m, snap.TotalCartonCount);
        Assert.Equal(60.0000m, snap.TotalCbm);
        Assert.False(snap.HasMissingDimensionsRecords);
    }

    [Fact]
    public async Task Test_03_Missing_Product_Dimensions_Triggers_Fallback_No_Free_Storage()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (snapshotService, _, _, context) = CreateServices(fix, bypassTenant: true);

        // 200 units of Item with missing dimensions (0 x 0 x 0)
        var stock = new StockOnHand
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = fix.ItemMissingDimsId,
            LocationId = fix.LocationId,
            QtyAvailable = 200m
        };
        context.StockOnHand.Add(stock);
        await context.SaveChangesAsync();

        var snap = await snapshotService.CaptureDailySnapshotAsync(fix.ClientId, fix.FacilityId, new DateOnly(2026, 3, 6));

        // Must flag missing dimensions
        Assert.True(snap.HasMissingDimensionsRecords);

        // Default fallback CBM = 0.0100 m3/unit -> 200 * 0.0100 = 2.0000 CBM (NOT 0 CBM free storage!)
        Assert.Equal(2.0000m, snap.TotalCbm);
        Assert.Equal(2.0000m, snap.TotalBillableCbm);
    }

    [Fact]
    public async Task Test_04_Damaged_And_Quarantined_Inventory_Policy_Evaluation()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (snapshotService, _, _, context) = CreateServices(fix, bypassTenant: true);

        // Custom Policy: Available and Quarantined are Billable, Damaged is Excluded
        var customPolicy = StorageBillingPolicy.CreateDefault(fix.ClientId, fix.FacilityId, "Custom Damaged Excluded Policy");
        customPolicy.DamagedTreatment = StorageBillingTreatment.Excluded;
        context.StorageBillingPolicies.Add(customPolicy);

        // Stock with: 300 Available, 100 Quarantined, 50 Damaged
        var stock = new StockOnHand
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = fix.ItemStandardId,
            LocationId = fix.LocationId,
            QtyAvailable = 300m,
            QtyQuarantined = 100m,
            QtyDamaged = 50m
        };
        context.StockOnHand.Add(stock);
        await context.SaveChangesAsync();

        var snap = await snapshotService.CaptureDailySnapshotAsync(fix.ClientId, fix.FacilityId, new DateOnly(2026, 3, 7));

        // Total physically stored = 450 units
        Assert.Equal(450m, snap.TotalQuantity);

        // Billable = 300 Available + 100 Quarantined = 400 units (50 Damaged excluded)
        Assert.Equal(400m, snap.TotalBillableQuantity);

        // Total pallets = 450 / 500 = 0.90; Billable pallets = 400 / 500 = 0.80
        Assert.Equal(0.90m, snap.TotalPalletCount);
        Assert.Equal(0.80m, snap.TotalBillablePalletCount);
    }

    [Fact]
    public async Task Test_05_Rate_Daily_Storage_Snapshot_Pallet_And_CBM_Lines()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (snapshotService, storageBillingService, rateCardService, context) = CreateServices(fix, bypassTenant: true);

        // Setup Rate Card with Storage lines:
        // StoragePalletDay = $0.50/pallet-day
        // StorageCbmDay = $0.25/CBM-day
        var rateCard = await rateCardService.CreateRateCardAsync(fix.ClientId, "RC-STOR-01", "Storage Agreement", "USD");
        var sched = await rateCardService.AddScheduleAsync(rateCard.Id, null,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));

        await rateCardService.AddLineAsync(sched.Id, BillableActivityType.StoragePalletDay, "PALLET-DAY",
            RateModelType.PerPallet, PricingTierCalculationModel.FlatRate, 0.50m);
        await rateCardService.AddLineAsync(sched.Id, BillableActivityType.StorageCbmDay, "CBM-DAY",
            RateModelType.PerCbmDay, PricingTierCalculationModel.FlatRate, 0.25m);

        await rateCardService.ActivateScheduleAsync(sched.Id);
        await rateCardService.ActivateRateCardAsync(rateCard.Id);

        // Stock: 1000 units -> 2.0 pallets, 60.0 CBM
        var stock = new StockOnHand
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = fix.ItemStandardId,
            LocationId = fix.LocationId,
            QtyAvailable = 1000m
        };
        context.StockOnHand.Add(stock);
        await context.SaveChangesAsync();

        var snap = await snapshotService.CaptureDailySnapshotAsync(fix.ClientId, fix.FacilityId, new DateOnly(2026, 3, 10));

        var runResult = await storageBillingService.RateDailyStorageSnapshotAsync(snap.Id);

        Assert.Equal(2, runResult.CreatedActivities.Count);
        // Pallet storage: 2.0 pallets * $0.50 = $1.00
        // CBM storage: 60.0 CBM * $0.25 = $15.00
        // Total = $16.00
        Assert.Equal(16.00m, runResult.TotalStorageAmount);

        var snapRecheck = await context.DailyInventorySnapshots.FindAsync(snap.Id);
        Assert.Equal(StorageSnapshotStatus.Rated, snapRecheck!.Status);
        Assert.NotNull(snapRecheck.RatedAtUtc);
    }

    [Fact]
    public async Task Test_06_FacilitySpecific_Storage_Pricing_Precedence()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (snapshotService, storageBillingService, rateCardService, context) = CreateServices(fix, bypassTenant: true);

        var rateCard = await rateCardService.CreateRateCardAsync(fix.ClientId, "RC-STOR-PREC", "Storage Precedence", "USD");

        // Client-Wide default: $0.60/pallet-day
        var clientSched = await rateCardService.AddScheduleAsync(rateCard.Id, null,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));
        await rateCardService.AddLineAsync(clientSched.Id, BillableActivityType.StoragePalletDay, "PALLET-DAY",
            RateModelType.PerPallet, PricingTierCalculationModel.FlatRate, 0.60m);
        await rateCardService.ActivateScheduleAsync(clientSched.Id);

        // Chicago facility override: $0.42/pallet-day
        var chicagoSched = await rateCardService.AddScheduleAsync(rateCard.Id, fix.FacilityId,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));
        await rateCardService.AddLineAsync(chicagoSched.Id, BillableActivityType.StoragePalletDay, "PALLET-DAY",
            RateModelType.PerPallet, PricingTierCalculationModel.FlatRate, 0.42m);
        await rateCardService.ActivateScheduleAsync(chicagoSched.Id);

        await rateCardService.ActivateRateCardAsync(rateCard.Id);

        // Stock in Chicago: 500 units = 1.0 pallet
        var stock = new StockOnHand
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = fix.ItemStandardId,
            LocationId = fix.LocationId,
            QtyAvailable = 500m
        };
        context.StockOnHand.Add(stock);
        await context.SaveChangesAsync();

        var snap = await snapshotService.CaptureDailySnapshotAsync(fix.ClientId, fix.FacilityId, new DateOnly(2026, 3, 11));
        var runResult = await storageBillingService.RateDailyStorageSnapshotAsync(snap.Id);

        // Must apply Chicago override rate ($0.42), not client-wide ($0.60)
        Assert.Equal(0.42m, runResult.TotalStorageAmount);
        Assert.Equal(chicagoSched.Id, runResult.CreatedActivities[0].RateCardScheduleId);
    }

    [Fact]
    public async Task Test_07_Inventory_Movement_Between_Facilities_Billed_Under_Respective_Schedules()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (snapshotService, storageBillingService, rateCardService, context) = CreateServices(fix, bypassTenant: true);

        var rateCard = await rateCardService.CreateRateCardAsync(fix.ClientId, "RC-MULTI-FAC", "Multi-Facility Agreement", "USD");

        // Chicago: $0.40/pallet
        var chiSched = await rateCardService.AddScheduleAsync(rateCard.Id, fix.FacilityId,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));
        await rateCardService.AddLineAsync(chiSched.Id, BillableActivityType.StoragePalletDay, "PALLET-DAY",
            RateModelType.PerPallet, PricingTierCalculationModel.FlatRate, 0.40m);
        await rateCardService.ActivateScheduleAsync(chiSched.Id);

        // Dallas: $0.55/pallet
        var dfwSched = await rateCardService.AddScheduleAsync(rateCard.Id, fix.SecondaryFacilityId,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));
        await rateCardService.AddLineAsync(dfwSched.Id, BillableActivityType.StoragePalletDay, "PALLET-DAY",
            RateModelType.PerPallet, PricingTierCalculationModel.FlatRate, 0.55m);
        await rateCardService.ActivateScheduleAsync(dfwSched.Id);

        await rateCardService.ActivateRateCardAsync(rateCard.Id);

        // Day 1: 500 units in Chicago (1 pallet)
        var stockChi = new StockOnHand
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = fix.ItemStandardId,
            LocationId = fix.LocationId,
            QtyAvailable = 500m
        };
        context.StockOnHand.Add(stockChi);
        await context.SaveChangesAsync();

        var snapDay1 = await snapshotService.CaptureDailySnapshotAsync(fix.ClientId, fix.FacilityId, new DateOnly(2026, 4, 1));
        var billDay1 = await storageBillingService.RateDailyStorageSnapshotAsync(snapDay1.Id);
        Assert.Equal(0.40m, billDay1.TotalStorageAmount);

        // Day 2: Transfer to Dallas
        stockChi.QtyAvailable = 0m;
        var stockDfw = new StockOnHand
        {
            ClientId = fix.ClientId,
            FacilityId = fix.SecondaryFacilityId,
            ItemId = fix.ItemStandardId,
            LocationId = fix.SecondaryLocationId,
            QtyAvailable = 500m
        };
        context.StockOnHand.Add(stockDfw);
        await context.SaveChangesAsync();

        var snapDay2 = await snapshotService.CaptureDailySnapshotAsync(fix.ClientId, fix.SecondaryFacilityId, new DateOnly(2026, 4, 2));
        var billDay2 = await storageBillingService.RateDailyStorageSnapshotAsync(snapDay2.Id);
        Assert.Equal(0.55m, billDay2.TotalStorageAmount);
    }

    [Fact]
    public async Task Test_08_Leap_Year_And_Month_End_Date_Continuity()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (snapshotService, _, _, context) = CreateServices(fix, bypassTenant: true);

        var stock = new StockOnHand
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = fix.ItemStandardId,
            LocationId = fix.LocationId,
            QtyAvailable = 500m
        };
        context.StockOnHand.Add(stock);
        await context.SaveChangesAsync();

        // 2028 is a leap year (February has 29 days)
        var feb28 = new DateOnly(2028, 2, 28);
        var feb29 = new DateOnly(2028, 2, 29);
        var mar01 = new DateOnly(2028, 3, 1);

        var snapFeb28 = await snapshotService.CaptureDailySnapshotAsync(fix.ClientId, fix.FacilityId, feb28);
        var snapFeb29 = await snapshotService.CaptureDailySnapshotAsync(fix.ClientId, fix.FacilityId, feb29);
        var snapMar01 = await snapshotService.CaptureDailySnapshotAsync(fix.ClientId, fix.FacilityId, mar01);

        Assert.NotNull(snapFeb28);
        Assert.NotNull(snapFeb29);
        Assert.NotNull(snapMar01);
        Assert.Equal(feb29, snapFeb29.SnapshotDate);
    }

    [Fact]
    public async Task Test_09_Minimum_Monthly_Spend_Reconciliation_Shortfall_Charged()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, storageBillingService, rateCardService, context) = CreateServices(fix, bypassTenant: true);

        // RateCard with $1,000 monthly minimum commitment
        var rateCard = await rateCardService.CreateRateCardAsync(fix.ClientId, "RC-MIN-SPEND", "Spend Agreement", "USD");
        var sched = await rateCardService.AddScheduleAsync(rateCard.Id, null,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc),
            minimumMonthlySpend: 1000.00m);
        await rateCardService.AddLineAsync(sched.Id, BillableActivityType.PickUnit, "UNIT",
            RateModelType.PerUnit, PricingTierCalculationModel.FlatRate, 0.50m);
        await rateCardService.ActivateScheduleAsync(sched.Id);
        await rateCardService.ActivateRateCardAsync(rateCard.Id);

        // Billing period for March 2026
        var period = BillingPeriod.Create(
            fix.ClientId,
            "BP-2026-03",
            "March 2026 Commercial Period",
            new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 3, 31, 23, 59, 59, DateTimeKind.Utc),
            BillingFrequency.Monthly,
            fix.FacilityId);
        context.BillingPeriods.Add(period);

        // Client generated only $350.00 in rated activity during March
        var act1 = BillableActivity.Create(fix.ClientId, fix.FacilityId, BillableActivityType.PickUnit,
            "PickTask", Guid.NewGuid(), "TASK-01", 700, "UNIT",
            new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc), "IDEMP-RECON-01", "March Pick",
            billingPeriodId: period.Id);
        act1.Rate(0.50m, 350.00m, "USD", rateCard.Id, null, 1, null, DateTime.UtcNow, sched.Id);
        context.BillableActivities.Add(act1);
        await context.SaveChangesAsync();

        // Run reconciliation: Commitment = $1000, Actual = $350 -> Shortfall = $650.00
        var reconResult = await storageBillingService.ReconcilePeriodSpendCommitmentAsync(fix.ClientId, period.Id);

        Assert.True(reconResult.ShortfallCharged);
        Assert.Equal(650.00m, reconResult.ShortfallAmount);
        Assert.NotNull(reconResult.ShortfallActivityId);

        var shortfallAct = await context.BillableActivities.FindAsync(reconResult.ShortfallActivityId.Value);
        Assert.Equal(650.00m, shortfallAct!.TotalAmount);
        Assert.Equal(BillableActivityStatus.Rated, shortfallAct.Status);

        // Idempotency: Running reconciliation a second time detects existing charge
        var reconSecond = await storageBillingService.ReconcilePeriodSpendCommitmentAsync(fix.ClientId, period.Id);
        Assert.True(reconSecond.ShortfallCharged);
        Assert.Equal(reconResult.ShortfallActivityId, reconSecond.ShortfallActivityId);
    }

    [Fact]
    public async Task Test_10_Minimum_Monthly_Spend_Met_No_Shortfall_Adjustment()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, storageBillingService, rateCardService, context) = CreateServices(fix, bypassTenant: true);

        var rateCard = await rateCardService.CreateRateCardAsync(fix.ClientId, "RC-MET-SPEND", "Spend Met Agreement", "USD");
        var sched = await rateCardService.AddScheduleAsync(rateCard.Id, null,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc),
            minimumMonthlySpend: 500.00m);
        await rateCardService.AddLineAsync(sched.Id, BillableActivityType.PickUnit, "UNIT",
            RateModelType.PerUnit, PricingTierCalculationModel.FlatRate, 0.50m);
        await rateCardService.ActivateScheduleAsync(sched.Id);
        await rateCardService.ActivateRateCardAsync(rateCard.Id);

        var period = BillingPeriod.Create(
            fix.ClientId,
            "BP-2026-03-MET",
            "March 2026 Met Period",
            new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 3, 31, 23, 59, 59, DateTimeKind.Utc),
            BillingFrequency.Monthly,
            fix.FacilityId);
        context.BillingPeriods.Add(period);

        // Client generated $750.00 (exceeds $500 floor)
        var act1 = BillableActivity.Create(fix.ClientId, fix.FacilityId, BillableActivityType.PickUnit,
            "PickTask", Guid.NewGuid(), "TASK-MET-01", 1500, "UNIT",
            new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc), "IDEMP-MET-01", "March Pick",
            billingPeriodId: period.Id);
        act1.Rate(0.50m, 750.00m, "USD", rateCard.Id, null, 1, null, DateTime.UtcNow, sched.Id);
        context.BillableActivities.Add(act1);
        await context.SaveChangesAsync();

        var reconResult = await storageBillingService.ReconcilePeriodSpendCommitmentAsync(fix.ClientId, period.Id);

        Assert.False(reconResult.ShortfallCharged);
        Assert.Equal(0m, reconResult.ShortfallAmount);
        Assert.Null(reconResult.ShortfallActivityId);
    }

    [Fact]
    public async Task Test_11_Historical_Snapshot_Immutability_When_Live_Inventory_Mutates()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (snapshotService, _, _, context) = CreateServices(fix, bypassTenant: true);

        var stock = new StockOnHand
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = fix.ItemStandardId,
            LocationId = fix.LocationId,
            QtyAvailable = 500m
        };
        context.StockOnHand.Add(stock);
        await context.SaveChangesAsync();

        var snap = await snapshotService.CaptureDailySnapshotAsync(fix.ClientId, fix.FacilityId, new DateOnly(2026, 3, 1));
        Assert.Equal(500m, snap.TotalQuantity);

        // Now mutate live StockOnHand later (e.g. order dispatched, reducing stock to 0)
        stock.QtyAvailable = 0m;
        await context.SaveChangesAsync();

        // The historical snapshot record MUST remain immutable at 500m
        var recheckSnap = await snapshotService.GetSnapshotAsync(fix.ClientId, fix.FacilityId, new DateOnly(2026, 3, 1));
        Assert.Equal(500m, recheckSnap!.TotalQuantity);
        Assert.Equal(500m, recheckSnap.Records.First().Quantity);
    }

    [Fact]
    public async Task Test_12_CrossTenant_Snapshot_Isolation()
    {
        var fix = await SeedTestEnvironmentAsync();

        // Attempting to capture or access snapshot for Client A using Foreign Client tenant context must fail
        var (foreignSnapshotService, _, _, _) = CreateServices(fix, tenantOverride: fix.ForeignClientId, bypassTenant: false);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            foreignSnapshotService.CaptureDailySnapshotAsync(fix.ClientId, fix.FacilityId, new DateOnly(2026, 3, 1)));
    }
}
