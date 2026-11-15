using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Services.Billing;
using Warehouse3PL.UnitTests.Common;

namespace Warehouse3PL.UnitTests.Billing;

/// <summary>
/// Comprehensive verification test suite for Phase 5.3: Activity-Based Handling Charges &amp; Operational Ingestion Engine.
/// Validates operational event ingestion across Inbound, Outbound, Reverse-Logistics, and VAS domains,
/// deterministic idempotency deduplication, immediate auto-rating vs deferred rating,
/// volume tier pricing, minimum fee floors, facility precedence, and multi-tenant security isolation.
/// </summary>
public class ActivityBasedHandlingChargesTests
{
    private class TestEnvFixture
    {
        public string DbName { get; } = Guid.NewGuid().ToString();
        public Guid ClientId { get; set; } = Guid.NewGuid();
        public Guid ForeignClientId { get; set; } = Guid.NewGuid();
        public Guid FacilityId { get; set; } = Guid.NewGuid();
        public Guid SecondaryFacilityId { get; set; } = Guid.NewGuid();
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
            CompanyName = "OmniChannel Retail Logistics Corp",
            AccountCode = "OMNI01",
            Status = ClientStatus.Active
        };

        var foreignClient = new Client
        {
            Id = fix.ForeignClientId,
            CompanyName = "CrossBorder Trade Logistics Ltd",
            AccountCode = "CBTL02",
            Status = ClientStatus.Active
        };

        var facility1 = new Facility
        {
            Id = fix.FacilityId,
            FacilityCode = "FAC-ORD1",
            Name = "Chicago Regional Distribution Center"
        };

        var facility2 = new Facility
        {
            Id = fix.SecondaryFacilityId,
            FacilityCode = "FAC-DFW1",
            Name = "Dallas Metro Distribution Hub"
        };

        var staff = new User
        {
            Id = fix.StaffUserId,
            Email = "ops.analyst@warehouse3pl.test",
            PasswordHash = "hashed",
            FirstName = "Eleanor",
            LastName = "Vance",
            UserType = UserType.Staff,
            Status = UserStatus.Active
        };

        context.Clients.AddRange(client, foreignClient);
        context.Facilities.AddRange(facility1, facility2);
        context.Users.Add(staff);
        await context.SaveChangesAsync();

        return fix;
    }

    private static (HandlingActivityIngestionService IngestionService, RateCardService RateCardService, RatingEngineService RatingEngine, IApplicationDbContext Context) CreateServices(
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

        var ratingEngineService = new RatingEngineService(
            context,
            rateCardService,
            tenantProvider,
            NullLogger<RatingEngineService>.Instance);

        var ingestionService = new HandlingActivityIngestionService(
            context,
            rateCardService,
            tenantProvider,
            NullLogger<HandlingActivityIngestionService>.Instance);

        return (ingestionService, rateCardService, ratingEngineService, context);
    }

    private static async Task<RateCardSchedule> SetupActiveClientRateScheduleAsync(
        RateCardService rateCardService,
        Guid clientId,
        Guid? facilityId = null,
        string currency = "USD")
    {
        var rateCard = await rateCardService.CreateRateCardAsync(
            clientId,
            $"RC-{Guid.NewGuid():N}"[..12],
            "Master Commercial Schedule",
            currency,
            facilityId);

        var schedule = await rateCardService.AddScheduleAsync(
            rateCard.Id,
            facilityId,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));

        // Inbound Lines
        await rateCardService.AddLineAsync(schedule.Id, BillableActivityType.InboundReceiving, "PALLET", RateModelType.PerUnit, PricingTierCalculationModel.FlatRate, 8.50m);
        await rateCardService.AddLineAsync(schedule.Id, BillableActivityType.InboundInspection, "SAMPLE", RateModelType.PerUnit, PricingTierCalculationModel.FlatRate, 1.25m);
        await rateCardService.AddLineAsync(schedule.Id, BillableActivityType.Putaway, "PALLET", RateModelType.PerUnit, PricingTierCalculationModel.FlatRate, 4.00m);
        await rateCardService.AddLineAsync(schedule.Id, BillableActivityType.CrossDock, "PALLET", RateModelType.PerUnit, PricingTierCalculationModel.FlatRate, 6.00m);

        // Outbound Lines
        await rateCardService.AddLineAsync(schedule.Id, BillableActivityType.OrderProcessing, "ORDER", RateModelType.PerUnit, PricingTierCalculationModel.FlatRate, 2.50m);
        await rateCardService.AddLineAsync(schedule.Id, BillableActivityType.PickLine, "LINE", RateModelType.PerUnit, PricingTierCalculationModel.FlatRate, 0.40m);
        await rateCardService.AddLineAsync(schedule.Id, BillableActivityType.PickUnit, "UNIT", RateModelType.PerUnit, PricingTierCalculationModel.FlatRate, 0.15m);
        await rateCardService.AddLineAsync(schedule.Id, BillableActivityType.PackingCarton, "CARTON", RateModelType.PerUnit, PricingTierCalculationModel.FlatRate, 1.75m);
        await rateCardService.AddLineAsync(schedule.Id, BillableActivityType.PackagingMaterial, "UNIT", RateModelType.PerUnit, PricingTierCalculationModel.FlatRate, 0.95m);
        await rateCardService.AddLineAsync(schedule.Id, BillableActivityType.ShippingHandling, "PACKAGE", RateModelType.PerUnit, PricingTierCalculationModel.FlatRate, 0.50m);

        // Reverse Logistics Lines
        await rateCardService.AddLineAsync(schedule.Id, BillableActivityType.ReturnProcessing, "UNIT", RateModelType.PerUnit, PricingTierCalculationModel.FlatRate, 3.00m);
        await rateCardService.AddLineAsync(schedule.Id, BillableActivityType.ReturnInspection, "UNIT", RateModelType.PerUnit, PricingTierCalculationModel.FlatRate, 2.00m);
        await rateCardService.AddLineAsync(schedule.Id, BillableActivityType.Restocking, "UNIT", RateModelType.PerUnit, PricingTierCalculationModel.FlatRate, 1.50m);

        // Value-Added Services (VAS)
        await rateCardService.AddLineAsync(schedule.Id, BillableActivityType.SpecialHandling, "UNIT", RateModelType.PerUnit, PricingTierCalculationModel.FlatRate, 5.00m);
        await rateCardService.AddLineAsync(schedule.Id, BillableActivityType.KittingAndAssembly, "UNIT", RateModelType.PerUnit, PricingTierCalculationModel.FlatRate, 7.50m);
        await rateCardService.AddLineAsync(schedule.Id, BillableActivityType.CustomVas, "UNIT", RateModelType.PerUnit, PricingTierCalculationModel.FlatRate, 10.00m);

        await rateCardService.ActivateScheduleAsync(schedule.Id);
        await rateCardService.ActivateRateCardAsync(rateCard.Id);

        return schedule;
    }

    [Fact]
    public async Task Test_01_Inbound_Receipt_Ingestion_Pallet_Carton_Unit_AutoRates()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (ingestionService, rateCardService, _, context) = CreateServices(fix, bypassTenant: true);
        await SetupActiveClientRateScheduleAsync(rateCardService, fix.ClientId);

        var receiptLineId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 4, 15, 10, 30, 0, DateTimeKind.Utc);

        // Act: Ingest Inbound Receiving Activity for 10 Pallets
        var result = await ingestionService.IngestInboundReceiptAsync(
            fix.ClientId,
            fix.FacilityId,
            receiptLineId,
            "PO-99481",
            10m,
            "PALLET",
            occurredAt,
            autoRate: true);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.IsDuplicate);
        Assert.True(result.IsRated);
        Assert.Equal(BillableActivityStatus.Rated, result.Status);
        Assert.Equal(10m, result.Quantity);
        Assert.Equal("PALLET", result.UnitOfMeasure);
        Assert.Equal(8.50m, result.UnitPrice);
        Assert.Equal(85.00m, result.TotalAmount);
        Assert.Equal($"INB-RCV-{receiptLineId:N}", result.IdempotencyKey);
        Assert.Equal("PO-99481", result.SourceReference);

        // Verify Entity in Database
        var saved = await context.BillableActivities.FirstOrDefaultAsync(a => a.Id == result.BillableActivityId);
        Assert.NotNull(saved);
        Assert.Equal(BillableActivityStatus.Rated, saved.Status);
        Assert.Equal(85.00m, saved.TotalAmount);
        Assert.NotNull(saved.RatedAtUtc);
    }

    [Fact]
    public async Task Test_02_Inbound_QA_Inspection_Ingestion_AutoRates()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (ingestionService, rateCardService, _, _) = CreateServices(fix, bypassTenant: true);
        await SetupActiveClientRateScheduleAsync(rateCardService, fix.ClientId);

        var inspectionId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 4, 15, 11, 0, 0, DateTimeKind.Utc);

        // Act: Ingest QA inspection for 20 sample units
        var result = await ingestionService.IngestInboundInspectionAsync(
            fix.ClientId,
            fix.FacilityId,
            inspectionId,
            "QA-BATCH-LOT4",
            20m,
            occurredAt,
            autoRate: true);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.IsRated);
        Assert.Equal(BillableActivityType.InboundInspection, result.ActivityType);
        Assert.Equal(20m, result.Quantity);
        Assert.Equal("SAMPLE", result.UnitOfMeasure);
        Assert.Equal(1.25m, result.UnitPrice);
        Assert.Equal(25.00m, result.TotalAmount);
        Assert.Equal($"INB-INSP-{inspectionId:N}", result.IdempotencyKey);
    }

    [Fact]
    public async Task Test_03_Putaway_And_CrossDock_Ingestion_AutoRates()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (ingestionService, rateCardService, _, _) = CreateServices(fix, bypassTenant: true);
        await SetupActiveClientRateScheduleAsync(rateCardService, fix.ClientId);

        var putawayTaskId = Guid.NewGuid();
        var crossDockTaskId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc);

        // Act 1: Putaway Task (5 Pallets to high bay)
        var putawayResult = await ingestionService.IngestPutawayTaskAsync(
            fix.ClientId,
            fix.FacilityId,
            putawayTaskId,
            "PUT-LOC-A1",
            5m,
            "PALLET",
            occurredAt,
            autoRate: true);

        // Act 2: Cross Dock Task (8 Pallets direct dock transfer)
        var crossDockResult = await ingestionService.IngestCrossDockExecutionAsync(
            fix.ClientId,
            fix.FacilityId,
            crossDockTaskId,
            "XD-DOOR-04",
            8m,
            "PALLET",
            occurredAt,
            autoRate: true);

        // Assert Putaway: 5 pallets * 4.00 = 20.00
        Assert.True(putawayResult.IsSuccess);
        Assert.True(putawayResult.IsRated);
        Assert.Equal(BillableActivityType.Putaway, putawayResult.ActivityType);
        Assert.Equal(4.00m, putawayResult.UnitPrice);
        Assert.Equal(20.00m, putawayResult.TotalAmount);
        Assert.Equal($"PUTAWAY-{putawayTaskId:N}", putawayResult.IdempotencyKey);

        // Assert CrossDock: 8 pallets * 6.00 = 48.00
        Assert.True(crossDockResult.IsSuccess);
        Assert.True(crossDockResult.IsRated);
        Assert.Equal(BillableActivityType.CrossDock, crossDockResult.ActivityType);
        Assert.Equal(6.00m, crossDockResult.UnitPrice);
        Assert.Equal(48.00m, crossDockResult.TotalAmount);
        Assert.Equal($"CROSSDOCK-{crossDockTaskId:N}", crossDockResult.IdempotencyKey);
    }

    [Fact]
    public async Task Test_04_Outbound_Order_BaseFee_And_PickExecution_Line_And_Unit_Emissions()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (ingestionService, rateCardService, _, _) = CreateServices(fix, bypassTenant: true);
        await SetupActiveClientRateScheduleAsync(rateCardService, fix.ClientId);

        var orderId = Guid.NewGuid();
        var pickTaskId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 4, 16, 9, 0, 0, DateTimeKind.Utc);

        // Act 1: Base Order Fulfillment Fee
        var orderResult = await ingestionService.IngestOrderFulfillmentAsync(
            fix.ClientId,
            fix.FacilityId,
            orderId,
            "ORD-2026-001",
            occurredAt,
            autoRate: true);

        // Assert Order Base Fee: 1 order * 2.50 = 2.50
        Assert.True(orderResult.IsSuccess);
        Assert.True(orderResult.IsRated);
        Assert.Equal(BillableActivityType.OrderProcessing, orderResult.ActivityType);
        Assert.Equal(1m, orderResult.Quantity);
        Assert.Equal(2.50m, orderResult.UnitPrice);
        Assert.Equal(2.50m, orderResult.TotalAmount);
        Assert.Equal($"OUT-ORD-{orderId:N}", orderResult.IdempotencyKey);

        // Act 2: Pick Execution with 15 picked units (emits both PickLine and PickUnit activities)
        var pickResults = await ingestionService.IngestPickExecutionAsync(
            fix.ClientId,
            fix.FacilityId,
            pickTaskId,
            "TASK-PICK-441",
            15m,
            occurredAt,
            autoRate: true);

        // Assert Pick Emissions
        Assert.Equal(2, pickResults.Count);

        var lineActivity = pickResults.First(r => r.ActivityType == BillableActivityType.PickLine);
        var unitActivity = pickResults.First(r => r.ActivityType == BillableActivityType.PickUnit);

        // PickLine: 1 line * 0.40 = 0.40
        Assert.True(lineActivity.IsRated);
        Assert.Equal(1m, lineActivity.Quantity);
        Assert.Equal("LINE", lineActivity.UnitOfMeasure);
        Assert.Equal(0.40m, lineActivity.UnitPrice);
        Assert.Equal(0.40m, lineActivity.TotalAmount);
        Assert.Equal($"PICK-LINE-{pickTaskId:N}", lineActivity.IdempotencyKey);

        // PickUnit: 15 units * 0.15 = 2.25
        Assert.True(unitActivity.IsRated);
        Assert.Equal(15m, unitActivity.Quantity);
        Assert.Equal("UNIT", unitActivity.UnitOfMeasure);
        Assert.Equal(0.15m, unitActivity.UnitPrice);
        Assert.Equal(2.25m, unitActivity.TotalAmount);
        Assert.Equal($"PICK-UNIT-{pickTaskId:N}", unitActivity.IdempotencyKey);
    }

    [Fact]
    public async Task Test_05_Outbound_Packing_Carton_And_PackagingMaterial_Emissions()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (ingestionService, rateCardService, _, _) = CreateServices(fix, bypassTenant: true);
        await SetupActiveClientRateScheduleAsync(rateCardService, fix.ClientId);

        var packageId = Guid.NewGuid();
        var materialId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 4, 16, 10, 0, 0, DateTimeKind.Utc);

        // Act: Packing Session with Carton and 2 units of Corrugated Box Material
        var results = await ingestionService.IngestPackingSessionAsync(
            fix.ClientId,
            fix.FacilityId,
            packageId,
            "PKG-88219",
            materialId,
            "BOX-12X12X12",
            2m,
            occurredAt,
            autoRate: true);

        // Assert: 2 activities emitted
        Assert.Equal(2, results.Count);

        var cartonResult = results.First(r => r.ActivityType == BillableActivityType.PackingCarton);
        var matResult = results.First(r => r.ActivityType == BillableActivityType.PackagingMaterial);

        // PackingCarton: 1 carton * 1.75 = 1.75
        Assert.True(cartonResult.IsRated);
        Assert.Equal(1m, cartonResult.Quantity);
        Assert.Equal("CARTON", cartonResult.UnitOfMeasure);
        Assert.Equal(1.75m, cartonResult.UnitPrice);
        Assert.Equal(1.75m, cartonResult.TotalAmount);
        Assert.Equal($"PACK-BOX-{packageId:N}", cartonResult.IdempotencyKey);

        // PackagingMaterial: 2 boxes * 0.95 = 1.90
        Assert.True(matResult.IsRated);
        Assert.Equal(2m, matResult.Quantity);
        Assert.Equal("UNIT", matResult.UnitOfMeasure);
        Assert.Equal(0.95m, matResult.UnitPrice);
        Assert.Equal(1.90m, matResult.TotalAmount);
        Assert.Equal($"PACK-MAT-{packageId:N}-{materialId:N}", matResult.IdempotencyKey);
    }

    [Fact]
    public async Task Test_06_Outbound_Shipment_Dispatch_Handling_Ingestion()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (ingestionService, rateCardService, _, _) = CreateServices(fix, bypassTenant: true);
        await SetupActiveClientRateScheduleAsync(rateCardService, fix.ClientId);

        var shipmentId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 4, 16, 14, 0, 0, DateTimeKind.Utc);

        // Act: Shipment Dispatch with 6 packages staged
        var result = await ingestionService.IngestShipmentDispatchAsync(
            fix.ClientId,
            fix.FacilityId,
            shipmentId,
            "SHIP-FEDEX-9001",
            6m,
            occurredAt,
            autoRate: true);

        // Assert: 6 packages * 0.50 = 3.00
        Assert.True(result.IsSuccess);
        Assert.True(result.IsRated);
        Assert.Equal(BillableActivityType.ShippingHandling, result.ActivityType);
        Assert.Equal(6m, result.Quantity);
        Assert.Equal("PACKAGE", result.UnitOfMeasure);
        Assert.Equal(0.50m, result.UnitPrice);
        Assert.Equal(3.00m, result.TotalAmount);
        Assert.Equal($"SHIP-DISP-{shipmentId:N}", result.IdempotencyKey);
    }

    [Fact]
    public async Task Test_07_Reverse_Logistics_Return_Receiving_Inspection_Restocking_Lifecycle()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (ingestionService, rateCardService, _, _) = CreateServices(fix, bypassTenant: true);
        await SetupActiveClientRateScheduleAsync(rateCardService, fix.ClientId);

        var returnLineId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 4, 17, 9, 30, 0, DateTimeKind.Utc);

        // Case A: Full inspection & restocking lifecycle
        var fullResults = await ingestionService.IngestReturnProcessingAsync(
            fix.ClientId,
            fix.FacilityId,
            returnLineId,
            "RMA-55102",
            4m,
            isInspected: true,
            isRestocked: true,
            occurredAt,
            autoRate: true);

        // Assert 3 activities emitted:
        // 1. ReturnProcessing: 4 units * 3.00 = 12.00
        // 2. ReturnInspection: 4 units * 2.00 = 8.00
        // 3. Restocking: 4 units * 1.50 = 6.00
        Assert.Equal(3, fullResults.Count);

        var rcv = fullResults.First(r => r.ActivityType == BillableActivityType.ReturnProcessing);
        var insp = fullResults.First(r => r.ActivityType == BillableActivityType.ReturnInspection);
        var restock = fullResults.First(r => r.ActivityType == BillableActivityType.Restocking);

        Assert.Equal(12.00m, rcv.TotalAmount);
        Assert.Equal($"RET-RCV-{returnLineId:N}", rcv.IdempotencyKey);

        Assert.Equal(8.00m, insp.TotalAmount);
        Assert.Equal($"RET-INSP-{returnLineId:N}", insp.IdempotencyKey);

        Assert.Equal(6.00m, restock.TotalAmount);
        Assert.Equal($"RET-RESTOCK-{returnLineId:N}", restock.IdempotencyKey);

        // Case B: Scrap return (no restocking, no inspection)
        var scrapLineId = Guid.NewGuid();
        var scrapResults = await ingestionService.IngestReturnProcessingAsync(
            fix.ClientId,
            fix.FacilityId,
            scrapLineId,
            "RMA-SCRAP-01",
            2m,
            isInspected: false,
            isRestocked: false,
            occurredAt,
            autoRate: true);

        Assert.Single(scrapResults);
        Assert.Equal(BillableActivityType.ReturnProcessing, scrapResults[0].ActivityType);
        Assert.Equal(6.00m, scrapResults[0].TotalAmount);
    }

    [Fact]
    public async Task Test_08_ValueAddedServices_SpecialHandling_Kitting_CustomVas()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (ingestionService, rateCardService, _, _) = CreateServices(fix, bypassTenant: true);
        await SetupActiveClientRateScheduleAsync(rateCardService, fix.ClientId);

        var sourceId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 4, 17, 14, 0, 0, DateTimeKind.Utc);

        // 1. Special Handling: Fragile/Hazardous Scan (3 units * 5.00 = 15.00)
        var specialResult = await ingestionService.IngestVasActivityAsync(
            fix.ClientId,
            fix.FacilityId,
            BillableActivityType.SpecialHandling,
            sourceId,
            "FRAGILE-GLASSWARE",
            3m,
            "UNIT",
            "Special handling and bubble protection for glassware",
            occurredAt,
            autoRate: true);

        Assert.True(specialResult.IsRated);
        Assert.Equal(15.00m, specialResult.TotalAmount);
        Assert.StartsWith("VAS-SpecialHandling-", specialResult.IdempotencyKey);

        // 2. Kitting & Assembly (5 custom bundles * 7.50 = 37.50)
        var kittingResult = await ingestionService.IngestVasActivityAsync(
            fix.ClientId,
            fix.FacilityId,
            BillableActivityType.KittingAndAssembly,
            sourceId,
            "KIT-GIFT-SET-01",
            5m,
            "UNIT",
            "Gift set assembly and ribbon boxing",
            occurredAt,
            autoRate: true);

        Assert.True(kittingResult.IsRated);
        Assert.Equal(37.50m, kittingResult.TotalAmount);
        Assert.StartsWith("VAS-KittingAndAssembly-", kittingResult.IdempotencyKey);

        // 3. Custom VAS (2 custom brand stamps * 10.00 = 20.00)
        var customResult = await ingestionService.IngestVasActivityAsync(
            fix.ClientId,
            fix.FacilityId,
            BillableActivityType.CustomVas,
            sourceId,
            "BRAND-ENGRAVE-22",
            2m,
            "UNIT",
            "Laser custom engraving",
            occurredAt,
            autoRate: true);

        Assert.True(customResult.IsRated);
        Assert.Equal(20.00m, customResult.TotalAmount);
        Assert.StartsWith("VAS-CustomVas-", customResult.IdempotencyKey);

        // 4. Invalid VAS type validation guard
        await Assert.ThrowsAsync<ValidationException>(() =>
            ingestionService.IngestVasActivityAsync(
                fix.ClientId,
                fix.FacilityId,
                BillableActivityType.InboundReceiving, // Invalid for VAS
                sourceId,
                "REF",
                1m,
                "UNIT",
                "Invalid",
                occurredAt));
    }

    [Fact]
    public async Task Test_09_Deterministic_Idempotency_Deduplication_Single_And_Batch()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (ingestionService, rateCardService, _, context) = CreateServices(fix, bypassTenant: true);
        await SetupActiveClientRateScheduleAsync(rateCardService, fix.ClientId);

        var receiptLineId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 4, 18, 8, 0, 0, DateTimeKind.Utc);

        // First Ingestion: Creates and rates
        var firstResult = await ingestionService.IngestInboundReceiptAsync(
            fix.ClientId,
            fix.FacilityId,
            receiptLineId,
            "PO-DUP-TEST",
            4m,
            "PALLET",
            occurredAt,
            autoRate: true);

        Assert.True(firstResult.IsSuccess);
        Assert.False(firstResult.IsDuplicate);

        // Second Ingestion: Retried or re-scanned event with same receiptLineId
        var secondResult = await ingestionService.IngestInboundReceiptAsync(
            fix.ClientId,
            fix.FacilityId,
            receiptLineId,
            "PO-DUP-TEST",
            4m,
            "PALLET",
            occurredAt,
            autoRate: true);

        // Assert Idempotency: Returns same BillableActivityId with IsDuplicate = true
        Assert.True(secondResult.IsSuccess);
        Assert.True(secondResult.IsDuplicate);
        Assert.Equal(firstResult.BillableActivityId, secondResult.BillableActivityId);
        Assert.Equal(firstResult.TotalAmount, secondResult.TotalAmount);

        // Verify only 1 record exists in the database
        var count = await context.BillableActivities.CountAsync(a => a.IdempotencyKey == firstResult.IdempotencyKey);
        Assert.Equal(1, count);

        // Batch Ingestion containing duplicate requests within the same payload
        var orderIdA = Guid.NewGuid();
        var orderIdB = Guid.NewGuid();

        var batchRequests = new List<IngestHandlingActivityRequest>
        {
            new()
            {
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                ActivityType = BillableActivityType.OrderProcessing,
                SourceType = "OutboundOrder",
                SourceId = orderIdA,
                SourceReference = "ORD-A",
                Quantity = 1m,
                UnitOfMeasure = "ORDER",
                OccurredAtUtc = occurredAt,
                IdempotencyKey = $"OUT-ORD-{orderIdA:N}",
                AutoRate = true
            },
            new()
            {
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                ActivityType = BillableActivityType.OrderProcessing,
                SourceType = "OutboundOrder",
                SourceId = orderIdB,
                SourceReference = "ORD-B",
                Quantity = 1m,
                UnitOfMeasure = "ORDER",
                OccurredAtUtc = occurredAt,
                IdempotencyKey = $"OUT-ORD-{orderIdB:N}",
                AutoRate = true
            },
            new() // Duplicate of orderIdA in same batch
            {
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                ActivityType = BillableActivityType.OrderProcessing,
                SourceType = "OutboundOrder",
                SourceId = orderIdA,
                SourceReference = "ORD-A",
                Quantity = 1m,
                UnitOfMeasure = "ORDER",
                OccurredAtUtc = occurredAt,
                IdempotencyKey = $"OUT-ORD-{orderIdA:N}",
                AutoRate = true
            }
        };

        var batchResults = await ingestionService.IngestBatchAsync(batchRequests);

        Assert.Equal(3, batchResults.Count);
        Assert.False(batchResults[0].IsDuplicate);
        Assert.False(batchResults[1].IsDuplicate);
        Assert.True(batchResults[2].IsDuplicate);
        Assert.Equal(batchResults[0].BillableActivityId, batchResults[2].BillableActivityId);
    }

    [Fact]
    public async Task Test_10_Immediate_AutoRating_vs_Deferred_PendingRating()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (ingestionService, rateCardService, ratingEngine, context) = CreateServices(fix, bypassTenant: true);
        await SetupActiveClientRateScheduleAsync(rateCardService, fix.ClientId);

        var putawayTaskA = Guid.NewGuid();
        var putawayTaskB = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 4, 18, 11, 0, 0, DateTimeKind.Utc);

        // Case A: Immediate Auto-Rating (autoRate: true)
        var resultAuto = await ingestionService.IngestPutawayTaskAsync(
            fix.ClientId,
            fix.FacilityId,
            putawayTaskA,
            "PUTAWAY-AUTO",
            10m,
            "PALLET",
            occurredAt,
            autoRate: true);

        Assert.True(resultAuto.IsRated);
        Assert.Equal(BillableActivityStatus.Rated, resultAuto.Status);
        Assert.Equal(40.00m, resultAuto.TotalAmount);

        // Case B: Deferred Rating (autoRate: false)
        var resultDeferred = await ingestionService.IngestPutawayTaskAsync(
            fix.ClientId,
            fix.FacilityId,
            putawayTaskB,
            "PUTAWAY-DEFERRED",
            10m,
            "PALLET",
            occurredAt,
            autoRate: false);

        Assert.False(resultDeferred.IsRated);
        Assert.Equal(BillableActivityStatus.PendingRating, resultDeferred.Status);
        Assert.Null(resultDeferred.UnitPrice);
        Assert.Null(resultDeferred.TotalAmount);

        // Now run batch rating on pending activities
        var batchRatingResult = await ratingEngine.RateActivitiesBatchAsync(fix.ClientId);
        Assert.Equal(1, batchRatingResult.SuccessfullyRatedCount);

        // Verify deferred activity is now rated in database
        var deferredSaved = await context.BillableActivities.FindAsync(resultDeferred.BillableActivityId);
        Assert.NotNull(deferredSaved);
        Assert.Equal(BillableActivityStatus.Rated, deferredSaved.Status);
        Assert.Equal(40.00m, deferredSaved.TotalAmount);
    }

    [Fact]
    public async Task Test_11_Missing_Rate_Card_Line_Graceful_PendingRating_Resilience()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (ingestionService, _, _, context) = CreateServices(fix, bypassTenant: true);

        // Note: No rate card configured for this client tenant
        var receiptLineId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 4, 19, 9, 0, 0, DateTimeKind.Utc);

        // Act: Ingestion with autoRate: true should NOT throw an exception that crashes warehouse ops!
        var result = await ingestionService.IngestInboundReceiptAsync(
            fix.ClientId,
            fix.FacilityId,
            receiptLineId,
            "PO-NO-RATE-CARD",
            5m,
            "PALLET",
            occurredAt,
            autoRate: true);

        // Assert: Activity is safely created and preserved as PendingRating
        Assert.True(result.IsSuccess);
        Assert.False(result.IsRated);
        Assert.Equal(BillableActivityStatus.PendingRating, result.Status);
        Assert.Contains("PendingRating", result.Message);

        var saved = await context.BillableActivities.FindAsync(result.BillableActivityId);
        Assert.NotNull(saved);
        Assert.Equal(BillableActivityStatus.PendingRating, saved.Status);
        Assert.Null(saved.UnitPrice);
        Assert.Null(saved.TotalAmount);
    }

    [Fact]
    public async Task Test_12_Graduated_Volume_Tiers_PickUnit_Pricing()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (ingestionService, rateCardService, _, _) = CreateServices(fix, bypassTenant: true);

        // Setup Rate Card with Graduated Volume Tiers for PickUnit
        var rateCard = await rateCardService.CreateRateCardAsync(fix.ClientId, "RC-GRADUATED", "Graduated Pick Agreement", "USD");
        var schedule = await rateCardService.AddScheduleAsync(
            rateCard.Id,
            null,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));

        var pickLine = await rateCardService.AddLineAsync(
            schedule.Id,
            BillableActivityType.PickUnit,
            "UNIT",
            RateModelType.PerUnit,
            PricingTierCalculationModel.Graduated,
            baseRate: 0.50m,
            minimumCharge: null,
            tiers: new[]
            {
                (1, 1m, (decimal?)100m, 0.50m),      // Tier 1: 1-100 @ $0.50
                (2, 101m, (decimal?)500m, 0.35m),    // Tier 2: 101-500 @ $0.35
                (3, 501m, (decimal?)null, 0.20m)     // Tier 3: 501+ @ $0.20
            });

        // Also add PickLine line so IngestPickExecutionAsync succeeds on both
        await rateCardService.AddLineAsync(schedule.Id, BillableActivityType.PickLine, "LINE", RateModelType.PerUnit, PricingTierCalculationModel.FlatRate, 0.30m);

        await rateCardService.ActivateScheduleAsync(schedule.Id);
        await rateCardService.ActivateRateCardAsync(rateCard.Id);

        var pickTaskId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 4, 19, 14, 0, 0, DateTimeKind.Utc);

        // Act: Ingest pick of 350 units
        // Graduated calculation:
        // Tier 1: 100 units * $0.50 = $50.00
        // Tier 2: 250 units * $0.35 = $87.50
        // Total: $137.50
        // Effective unit price: $137.50 / 350 = 0.3929
        var results = await ingestionService.IngestPickExecutionAsync(
            fix.ClientId,
            fix.FacilityId,
            pickTaskId,
            "TASK-GRADUATED-350",
            350m,
            occurredAt,
            autoRate: true);

        var pickUnitActivity = results.First(r => r.ActivityType == BillableActivityType.PickUnit);

        Assert.True(pickUnitActivity.IsRated);
        Assert.Equal(350m, pickUnitActivity.Quantity);
        Assert.Equal(137.50m, pickUnitActivity.TotalAmount);
        Assert.Equal(0.3929m, pickUnitActivity.UnitPrice);
    }

    [Fact]
    public async Task Test_13_AllUnits_Volume_Tiers_Carton_Packing_Pricing()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (ingestionService, rateCardService, _, _) = CreateServices(fix, bypassTenant: true);

        // Setup Rate Card with All-Units Volume Tiers for PackingCarton
        var rateCard = await rateCardService.CreateRateCardAsync(fix.ClientId, "RC-ALLUNITS", "All Units Agreement", "USD");
        var schedule = await rateCardService.AddScheduleAsync(
            rateCard.Id,
            null,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));

        await rateCardService.AddLineAsync(
            schedule.Id,
            BillableActivityType.PackingCarton,
            "CARTON",
            RateModelType.PerUnit,
            PricingTierCalculationModel.AllUnits,
            baseRate: 2.00m,
            minimumCharge: null,
            tiers: new[]
            {
                (1, 1m, (decimal?)20m, 2.00m),       // 1-20 @ $2.00
                (2, 21m, (decimal?)null, 1.50m)      // 21+ @ $1.50 for all units
            });

        await rateCardService.ActivateScheduleAsync(schedule.Id);
        await rateCardService.ActivateRateCardAsync(rateCard.Id);

        // Ingest a batch packing activity of 30 cartons directly
        var req = new IngestHandlingActivityRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ActivityType = BillableActivityType.PackingCarton,
            SourceType = "PackingBatch",
            SourceId = Guid.NewGuid(),
            SourceReference = "BATCH-CARTONS-30",
            Quantity = 30m,
            UnitOfMeasure = "CARTON",
            OccurredAtUtc = new DateTime(2026, 4, 19, 15, 0, 0, DateTimeKind.Utc),
            IdempotencyKey = "PACK-BATCH-30",
            AutoRate = true
        };

        var result = await ingestionService.IngestActivityAsync(req);

        // Assert AllUnits rating: 30 units achieved tier 2 ($1.50) -> 30 * $1.50 = $45.00
        Assert.True(result.IsRated);
        Assert.Equal(30m, result.Quantity);
        Assert.Equal(1.50m, result.UnitPrice);
        Assert.Equal(45.00m, result.TotalAmount);
    }

    [Fact]
    public async Task Test_14_Minimum_Charge_Floor_Elevation()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (ingestionService, rateCardService, _, _) = CreateServices(fix, bypassTenant: true);

        // Base rate: $0.20/unit, Minimum charge floor: $5.00
        var rateCard = await rateCardService.CreateRateCardAsync(fix.ClientId, "RC-MINCHARGE", "Floor Agreement", "USD");
        var schedule = await rateCardService.AddScheduleAsync(
            rateCard.Id,
            null,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));

        await rateCardService.AddLineAsync(
            schedule.Id,
            BillableActivityType.InboundReceiving,
            "UNIT",
            RateModelType.PerUnit,
            PricingTierCalculationModel.FlatRate,
            baseRate: 0.20m,
            minimumCharge: 5.00m);

        await rateCardService.ActivateScheduleAsync(schedule.Id);
        await rateCardService.ActivateRateCardAsync(rateCard.Id);

        var receiptLineId = Guid.NewGuid();

        // Ingest 6 units: 6 * $0.20 = $1.20, which is below the $5.00 floor
        var result = await ingestionService.IngestInboundReceiptAsync(
            fix.ClientId,
            fix.FacilityId,
            receiptLineId,
            "PO-SMALL-BATCH",
            6m,
            "UNIT",
            new DateTime(2026, 4, 20, 10, 0, 0, DateTimeKind.Utc),
            autoRate: true);

        Assert.True(result.IsRated);
        Assert.Equal(6m, result.Quantity);
        Assert.Equal(5.00m, result.TotalAmount); // Elevated to contractual minimum floor!
        Assert.Equal(0.8333m, result.UnitPrice); // Effective blended unit price ($5.00 / 6)
    }

    [Fact]
    public async Task Test_15_Facility_Override_Precedence_In_Handling_Rating()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (ingestionService, rateCardService, _, _) = CreateServices(fix, bypassTenant: true);

        var rateCard = await rateCardService.CreateRateCardAsync(fix.ClientId, "RC-FAC-PRECEDENCE", "Multi Facility Pricing", "USD");

        // 1. Client-Wide Default Schedule (Putaway @ $4.00/pallet)
        var defaultSchedule = await rateCardService.AddScheduleAsync(
            rateCard.Id,
            null, // Client-wide fallback
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));

        await rateCardService.AddLineAsync(defaultSchedule.Id, BillableActivityType.Putaway, "PALLET", RateModelType.PerUnit, PricingTierCalculationModel.FlatRate, 4.00m);
        await rateCardService.ActivateScheduleAsync(defaultSchedule.Id);

        // 2. Facility-Specific Schedule for FacilityId (Putaway @ $6.50/pallet due to high-density robotics)
        var facilitySchedule = await rateCardService.AddScheduleAsync(
            rateCard.Id,
            fix.FacilityId, // Exact facility override
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));

        await rateCardService.AddLineAsync(facilitySchedule.Id, BillableActivityType.Putaway, "PALLET", RateModelType.PerUnit, PricingTierCalculationModel.FlatRate, 6.50m);
        await rateCardService.ActivateScheduleAsync(facilitySchedule.Id);

        await rateCardService.ActivateRateCardAsync(rateCard.Id);

        var taskFac1 = Guid.NewGuid();
        var taskFac2 = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 4, 20, 11, 0, 0, DateTimeKind.Utc);

        // Act 1: Putaway at FacilityId (should resolve facility override schedule @ $6.50)
        var resultFac1 = await ingestionService.IngestPutawayTaskAsync(
            fix.ClientId,
            fix.FacilityId,
            taskFac1,
            "TASK-ROBOTIC",
            10m,
            "PALLET",
            occurredAt,
            autoRate: true);

        // Act 2: Putaway at SecondaryFacilityId (should fallback to client-wide schedule @ $4.00)
        var resultFac2 = await ingestionService.IngestPutawayTaskAsync(
            fix.ClientId,
            fix.SecondaryFacilityId,
            taskFac2,
            "TASK-STANDARD",
            10m,
            "PALLET",
            occurredAt,
            autoRate: true);

        // Assert
        Assert.Equal(6.50m, resultFac1.UnitPrice);
        Assert.Equal(65.00m, resultFac1.TotalAmount);
        Assert.Equal(facilitySchedule.Id, resultFac1.RateCardScheduleId);

        Assert.Equal(4.00m, resultFac2.UnitPrice);
        Assert.Equal(40.00m, resultFac2.TotalAmount);
        Assert.Equal(defaultSchedule.Id, resultFac2.RateCardScheduleId);
    }

    [Fact]
    public async Task Test_16_MultiTenant_Security_And_BillingPeriod_Isolation()
    {
        var fix = await SeedTestEnvironmentAsync();

        // Service scoped strictly to fix.ClientId tenant
        var (ingestionService, _, _, context) = CreateServices(fix, tenantOverride: fix.ClientId, bypassTenant: false);

        var occurredAt = new DateTime(2026, 4, 21, 9, 0, 0, DateTimeKind.Utc);

        // 1. Cross-Tenant Attempt: Trying to ingest an activity for ForeignClientId
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            ingestionService.IngestInboundReceiptAsync(
                fix.ForeignClientId, // Foreign tenant!
                fix.FacilityId,
                Guid.NewGuid(),
                "PO-CROSS-TENANT",
                5m,
                "PALLET",
                occurredAt));

        // 2. Cross-Tenant Billing Period Attachment Attempt
        var foreignPeriod = BillingPeriod.Create(
            fix.ForeignClientId,
            "FOREIGN-PER-01",
            "Foreign Client Period",
            new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 30, 23, 59, 59, DateTimeKind.Utc));

        using (var setupContext = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext(), fix.DbName))
        {
            setupContext.BillingPeriods.Add(foreignPeriod);
            await setupContext.SaveChangesAsync();
        }

        var req = new IngestHandlingActivityRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            BillingPeriodId = foreignPeriod.Id, // Foreign period belonging to different tenant!
            ActivityType = BillableActivityType.OrderProcessing,
            SourceType = "OutboundOrder",
            SourceId = Guid.NewGuid(),
            SourceReference = "ORD-TEST",
            Quantity = 1m,
            UnitOfMeasure = "ORDER",
            OccurredAtUtc = occurredAt,
            IdempotencyKey = "OUT-ORD-FOREIGN-PERIOD",
            AutoRate = false
        };

        await Assert.ThrowsAsync<UnauthorizedException>(() => ingestionService.IngestActivityAsync(req));
    }
}

