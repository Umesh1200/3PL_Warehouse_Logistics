using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Services.Billing;
using Warehouse3PL.UnitTests.Common;

namespace Warehouse3PL.UnitTests.Billing;

/// <summary>
/// Comprehensive verification test suite for Phase 5.4: Shipping Margin, Accessorial Charges &amp; Carrier Markup Engine.
/// Validates carrier buy-side cost ingestion, contractual client markups, accessorial classifications,
/// gross freight margin calculations, carrier invoice reconciliation, and late-arriving surcharge adjustments.
/// </summary>
public class ShippingMarginAndCarrierMarkupTests
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
            CompanyName = "Global Brands Direct Fulfillment",
            AccountCode = "GBD01",
            Status = ClientStatus.Active
        };

        var foreignClient = new Client
        {
            Id = fix.ForeignClientId,
            CompanyName = "Foreign Enterprise Ltd",
            AccountCode = "FEL02",
            Status = ClientStatus.Active
        };

        var facility1 = new Facility
        {
            Id = fix.FacilityId,
            FacilityCode = "FAC-CHI1",
            Name = "Chicago Logistics Center"
        };

        var facility2 = new Facility
        {
            Id = fix.SecondaryFacilityId,
            FacilityCode = "FAC-LAX1",
            Name = "Los Angeles Gateway Hub"
        };

        var staff = new User
        {
            Id = fix.StaffUserId,
            Email = "freight.analyst@warehouse3pl.test",
            PasswordHash = "hashed",
            FirstName = "Julian",
            LastName = "Mercer",
            UserType = UserType.Staff,
            Status = UserStatus.Active
        };

        context.Clients.AddRange(client, foreignClient);
        context.Facilities.AddRange(facility1, facility2);
        context.Users.Add(staff);
        await context.SaveChangesAsync();

        return fix;
    }

    private static (FreightMarginEngineService EngineService, IApplicationDbContext Context) CreateServices(
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

        var engineService = new FreightMarginEngineService(
            context,
            tenantProvider,
            NullLogger<FreightMarginEngineService>.Instance);

        return (engineService, context);
    }

    private static async Task<Shipment> SeedShipmentAsync(
        IApplicationDbContext context,
        Guid clientId,
        Guid facilityId,
        string carrierCode = "FEDEX",
        CarrierServiceLevel serviceLevel = CarrierServiceLevel.StandardGround,
        int packageCount = 1)
    {
        var shipment = new Shipment
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            FacilityId = facilityId,
            ShipmentNumber = $"SHIP-{Guid.NewGuid():N}"[..14].ToUpperInvariant(),
            CarrierCode = carrierCode,
            CarrierServiceLevel = serviceLevel,
            Status = ShipmentStatus.Dispatched,
            TotalPackagesCount = packageCount,
            TotalWeightKg = 5.5m * packageCount,
            DispatchedAtUtc = DateTime.UtcNow
        };

        for (int i = 0; i < packageCount; i++)
        {
            var package = new Package
            {
                Id = Guid.NewGuid(),
                ClientId = clientId,
                FacilityId = facilityId,
                OutboundOrderId = Guid.NewGuid(),
                HandlingUnitId = Guid.NewGuid(),
                ShipmentId = shipment.Id,
                PackageNumber = $"PKG-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
                CartonSequence = i + 1,
                GrossWeightKg = 5.5m,
                CarrierCode = carrierCode,
                TrackingNumber = $"TRK-{Guid.NewGuid():N}"[..16].ToUpperInvariant(),
                Status = PackageStatus.Loaded
            };
            shipment.Packages.Add(package);
        }

        context.Shipments.Add(shipment);
        await context.SaveChangesAsync();
        return shipment;
    }

    [Fact]
    public async Task Test_01_Percentage_Markup_Calculation_Illustrative_Case()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (engine, context) = CreateServices(fix, bypassTenant: true);
        var shipment = await SeedShipmentAsync(context, fix.ClientId, fix.FacilityId, "FEDEX");

        // 1. Setup Client Freight Agreement: 15% markup on carrier cost
        await engine.CreateFreightAgreementAsync(new CreateFreightAgreementRequest
        {
            ClientId = fix.ClientId,
            AgreementCode = "AGR-FEDEX-15",
            Name = "FedEx 15% Standard Markup",
            CarrierCode = "FEDEX",
            PricingModel = FreightPricingModel.CostPlusPercentageMarkup,
            MarkupPercentage = 15.00m,
            EffectiveFromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        // 2. Ingest Carrier Buy-Side Cost:
        // Base: $20.00, Fuel: $3.00, Residential Accessorial: $4.00 -> Total Carrier Cost: $27.00
        var ingestReq = new IngestCarrierCostRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierServiceLevel = CarrierServiceLevel.StandardGround,
            BaseTransportationCost = 20.00m,
            FuelSurcharge = 3.00m,
            InsuranceCost = 0m,
            Currency = "USD",
            CostAssessedAtUtc = DateTime.UtcNow,
            IdempotencyKey = $"COST-{shipment.Id:N}",
            Accessorials = new List<IngestAccessorialChargeDto>
            {
                new()
                {
                    AccessorialType = AccessorialType.ResidentialDelivery,
                    CarrierAccessorialCode = "RES",
                    CarrierAmount = 4.00m
                }
            }
        };

        var costResult = await engine.IngestCarrierCostAsync(ingestReq);
        Assert.Equal(27.00m, costResult.TotalCarrierCost);

        // 3. Act: Rate Shipment Freight
        var rateResult = await engine.RateShipmentFreightAsync(shipment.Id);

        // Assert:
        // Total Carrier Cost: $27.00
        // 15% Markup on cost: $4.05
        // Total Client Freight: $31.05
        // Gross Freight Margin: $4.05
        Assert.True(rateResult.IsSuccess);
        Assert.Equal(27.00m, rateResult.TotalCarrierCost);
        Assert.Equal(31.05m, rateResult.TotalClientFreight);
        Assert.Equal(4.05m, rateResult.GrossMarginAmount);
        Assert.Equal(15.00m, rateResult.MarkupPercentage);
        Assert.Equal(13.04m, rateResult.MarginPercentage); // (4.05 / 31.05) * 100 = 13.04%

        // Verify BillableActivity was emitted
        Assert.NotNull(rateResult.BillableActivityId);
        var billable = await context.BillableActivities.FindAsync(rateResult.BillableActivityId.Value);
        Assert.NotNull(billable);
        Assert.Equal(BillableActivityType.CarrierFreight, billable.ActivityType);
        Assert.Equal(31.05m, billable.TotalAmount);
        Assert.Equal(BillableActivityStatus.Rated, billable.Status);
    }

    [Fact]
    public async Task Test_02_Flat_Shipment_And_Package_Fees()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (engine, context) = CreateServices(fix, bypassTenant: true);

        // Case A: Flat Fee Per Shipment ($5.00 handling fee)
        var shipmentA = await SeedShipmentAsync(context, fix.ClientId, fix.FacilityId, "UPS");
        await engine.CreateFreightAgreementAsync(new CreateFreightAgreementRequest
        {
            ClientId = fix.ClientId,
            AgreementCode = "AGR-UPS-FLAT-SHIP",
            Name = "UPS Flat Fee Per Shipment",
            CarrierCode = "UPS",
            PricingModel = FreightPricingModel.FlatFeePerShipment,
            FlatFeePerShipment = 5.00m,
            EffectiveFromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        await engine.IngestCarrierCostAsync(new IngestCarrierCostRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ShipmentId = shipmentA.Id,
            CarrierCode = "UPS",
            BaseTransportationCost = 25.00m,
            Currency = "USD",
            CostAssessedAtUtc = DateTime.UtcNow,
            IdempotencyKey = $"COST-{shipmentA.Id:N}"
        });

        var resultA = await engine.RateShipmentFreightAsync(shipmentA.Id);
        Assert.Equal(25.00m, resultA.TotalCarrierCost);
        Assert.Equal(30.00m, resultA.TotalClientFreight); // $25 + $5
        Assert.Equal(5.00m, resultA.GrossMarginAmount);

        // Case B: Flat Fee Per Package ($2.00 per package on a 3-package shipment)
        var shipmentB = await SeedShipmentAsync(context, fix.ClientId, fix.FacilityId, "DHL", packageCount: 3);
        await engine.CreateFreightAgreementAsync(new CreateFreightAgreementRequest
        {
            ClientId = fix.ClientId,
            AgreementCode = "AGR-DHL-FLAT-PKG",
            Name = "DHL Flat Fee Per Package",
            CarrierCode = "DHL",
            PricingModel = FreightPricingModel.FlatFeePerPackage,
            FlatFeePerPackage = 2.00m,
            EffectiveFromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        await engine.IngestCarrierCostAsync(new IngestCarrierCostRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ShipmentId = shipmentB.Id,
            CarrierCode = "DHL",
            BaseTransportationCost = 30.00m,
            Currency = "USD",
            CostAssessedAtUtc = DateTime.UtcNow,
            IdempotencyKey = $"COST-{shipmentB.Id:N}"
        });

        var resultB = await engine.RateShipmentFreightAsync(shipmentB.Id);
        // Cost: $30.00, Handling: 3 packages * $2.00 = $6.00 -> Sell: $36.00
        Assert.Equal(30.00m, resultB.TotalCarrierCost);
        Assert.Equal(36.00m, resultB.TotalClientFreight);
        Assert.Equal(6.00m, resultB.GrossMarginAmount);
    }

    [Fact]
    public async Task Test_03_Markup_Versus_Margin_Percentage()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (engine, context) = CreateServices(fix, bypassTenant: true);

        // 1. CostPlusPercentageMarkup: 25% Markup on Cost
        // Sell = Cost * (1 + 0.25)
        // Cost: $100 -> Sell: $125. Markup% = 25%, Margin% = 20%
        var agreementMarkup = ClientFreightAgreement.Create(
            fix.ClientId, "TEST-MARKUP", "Test Markup",
            FreightPricingModel.CostPlusPercentageMarkup, "USD",
            DateTime.UtcNow, markupPercentage: 25.00m);

        var (sellMarkup, markupVal, _) = agreementMarkup.CalculatePricing(100.00m);
        Assert.Equal(125.00m, sellMarkup);
        Assert.Equal(25.00m, markupVal);

        // 2. CostPlusMarginPercentage: Target 25% Gross Margin
        // Sell = Cost / (1 - 0.25) = 100 / 0.75 = $133.3333
        // Gross Margin = $33.3333 -> Margin % = (33.3333 / 133.3333) * 100 = 25.00%
        var agreementMargin = ClientFreightAgreement.Create(
            fix.ClientId, "TEST-MARGIN", "Test Margin",
            FreightPricingModel.CostPlusMarginPercentage, "USD",
            DateTime.UtcNow, markupPercentage: 25.00m);

        var (sellMargin, marginVal, _) = agreementMargin.CalculatePricing(100.00m);
        Assert.Equal(133.3333m, sellMargin);
        Assert.Equal(33.3333m, marginVal);

        var marginRecord = ShipmentFreightMarginRecord.Create(
            fix.ClientId, fix.FacilityId, Guid.NewGuid(), Guid.NewGuid(),
            "CARRIER", CarrierServiceLevel.StandardGround, FreightPricingModel.CostPlusMarginPercentage, 1,
            100m, 0m, 0m, 0m,
            100m, 0m, 0m, 0m, marginVal, 0m,
            "USD", "KEY-MARGIN-TEST", DateTime.UtcNow);

        Assert.Equal(25.00m, marginRecord.MarginPercentage); // Target gross margin achieved!
        Assert.Equal(33.33m, marginRecord.MarkupPercentage); // Corresponding markup percentage
    }

    [Fact]
    public async Task Test_04_Minimum_And_Maximum_Markup_Limits()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (engine, context) = CreateServices(fix, bypassTenant: true);

        // 10% Markup with Min Floor of $5.00 and Max Cap of $50.00
        var agreement = ClientFreightAgreement.Create(
            fix.ClientId, "AGR-LIMITS", "Agreement Limits",
            FreightPricingModel.CostPlusPercentageMarkup, "USD",
            DateTime.UtcNow,
            markupPercentage: 10.00m,
            minimumMarkupAmount: 5.00m,
            maximumMarkupAmount: 50.00m);

        // Case A: Small cost ($10). Raw 10% markup = $1.00 -> Elevated to $5.00 minimum floor
        var (sellSmall, markupSmall, _) = agreement.CalculatePricing(10.00m);
        Assert.Equal(5.00m, markupSmall);
        Assert.Equal(15.00m, sellSmall);

        // Case B: Normal cost ($150). Raw 10% markup = $15.00 -> Within limits
        var (sellMid, markupMid, _) = agreement.CalculatePricing(150.00m);
        Assert.Equal(15.00m, markupMid);
        Assert.Equal(165.00m, sellMid);

        // Case C: Heavy freight ($800). Raw 10% markup = $80.00 -> Capped at $50.00 ceiling
        var (sellLarge, markupLarge, _) = agreement.CalculatePricing(800.00m);
        Assert.Equal(50.00m, markupLarge);
        Assert.Equal(850.00m, sellLarge);
    }

    [Fact]
    public async Task Test_05_Client_Specific_Versus_Facility_Specific_Pricing_Precedence()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (engine, context) = CreateServices(fix, bypassTenant: true);

        // 1. Client-Wide Default Agreement (10% markup)
        await engine.CreateFreightAgreementAsync(new CreateFreightAgreementRequest
        {
            ClientId = fix.ClientId,
            AgreementCode = "AGR-CLIENT-DEFAULT",
            Name = "Client-Wide Default 10%",
            PricingModel = FreightPricingModel.CostPlusPercentageMarkup,
            MarkupPercentage = 10.00m,
            EffectiveFromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        // 2. Client-Wide Carrier-Specific Agreement for FEDEX (15% markup)
        await engine.CreateFreightAgreementAsync(new CreateFreightAgreementRequest
        {
            ClientId = fix.ClientId,
            AgreementCode = "AGR-CLIENT-FEDEX",
            Name = "Client-Wide FedEx 15%",
            CarrierCode = "FEDEX",
            PricingModel = FreightPricingModel.CostPlusPercentageMarkup,
            MarkupPercentage = 15.00m,
            EffectiveFromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        // 3. Facility-Specific Override for FacilityId with FEDEX (20% markup)
        await engine.CreateFreightAgreementAsync(new CreateFreightAgreementRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            AgreementCode = "AGR-FAC-FEDEX",
            Name = "Chicago Hub FedEx 20%",
            CarrierCode = "FEDEX",
            PricingModel = FreightPricingModel.CostPlusPercentageMarkup,
            MarkupPercentage = 20.00m,
            EffectiveFromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        var occurred = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc);

        // Test 1: Shipment at FacilityId on FEDEX -> Resolves Facility-Specific FEDEX (20%)
        var resolvedFacFedex = await engine.GetEffectiveFreightAgreementAsync(
            fix.ClientId, fix.FacilityId, "FEDEX", CarrierServiceLevel.StandardGround, occurred);
        Assert.NotNull(resolvedFacFedex);
        Assert.Equal("AGR-FAC-FEDEX", resolvedFacFedex.AgreementCode);
        Assert.Equal(20.00m, resolvedFacFedex.MarkupPercentage);

        // Test 2: Shipment at SecondaryFacilityId on FEDEX -> Resolves Client-Wide FEDEX (15%)
        var resolvedOtherFedex = await engine.GetEffectiveFreightAgreementAsync(
            fix.ClientId, fix.SecondaryFacilityId, "FEDEX", CarrierServiceLevel.StandardGround, occurred);
        Assert.NotNull(resolvedOtherFedex);
        Assert.Equal("AGR-CLIENT-FEDEX", resolvedOtherFedex.AgreementCode);
        Assert.Equal(15.00m, resolvedOtherFedex.MarkupPercentage);

        // Test 3: Shipment at SecondaryFacilityId on USPS (no carrier agreement) -> Resolves Client-Wide Default (10%)
        var resolvedUsps = await engine.GetEffectiveFreightAgreementAsync(
            fix.ClientId, fix.SecondaryFacilityId, "USPS", CarrierServiceLevel.StandardGround, occurred);
        Assert.NotNull(resolvedUsps);
        Assert.Equal("AGR-CLIENT-DEFAULT", resolvedUsps.AgreementCode);
        Assert.Equal(10.00m, resolvedUsps.MarkupPercentage);
    }

    [Fact]
    public async Task Test_06_Accessorial_Classification_And_Billability()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (engine, context) = CreateServices(fix, bypassTenant: true);
        var shipment = await SeedShipmentAsync(context, fix.ClientId, fix.FacilityId, "FEDEX");

        // Create Agreement with 20% general markup and specific accessorial rules
        var agreement = await engine.CreateFreightAgreementAsync(new CreateFreightAgreementRequest
        {
            ClientId = fix.ClientId,
            AgreementCode = "AGR-ACC-RULES",
            Name = "Accessorial Rules Agreement",
            CarrierCode = "FEDEX",
            PricingModel = FreightPricingModel.CostPlusPercentageMarkup,
            MarkupPercentage = 20.00m,
            EffectiveFromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        // Rule 1: Liftgate -> Fixed Client Fee of $50.00
        await engine.AddAccessorialRuleAsync(agreement.Id, AccessorialType.LiftgateService, AccessorialBillableTreatment.FixedClientFee, fixedFee: 50.00m);

        // Rule 2: Address Correction -> Passed through at cost (1:1)
        await engine.AddAccessorialRuleAsync(agreement.Id, AccessorialType.AddressCorrection, AccessorialBillableTreatment.PassThroughAtCost);

        // Rule 3: ReDelivery -> Absorbed by 3PL warehouse (Client charge $0.00)
        await engine.AddAccessorialRuleAsync(agreement.Id, AccessorialType.ReDelivery, AccessorialBillableTreatment.AbsorbedByWarehouse);

        // Ingest Carrier Cost with 4 accessorials:
        // 1. Liftgate (Cost: $35.00 -> Fixed Sell: $50.00)
        // 2. Address Correction (Cost: $18.00 -> PassThrough Sell: $18.00)
        // 3. ReDelivery (Cost: $15.00 -> Absorbed Sell: $0.00)
        // 4. Residential (Cost: $5.00 -> Billable with default 20% markup -> Sell: $6.00)
        await engine.IngestCarrierCostAsync(new IngestCarrierCostRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            BaseTransportationCost = 50.00m,
            Currency = "USD",
            CostAssessedAtUtc = DateTime.UtcNow,
            IdempotencyKey = $"COST-ACC-{shipment.Id:N}",
            Accessorials = new List<IngestAccessorialChargeDto>
            {
                new() { AccessorialType = AccessorialType.LiftgateService, CarrierAccessorialCode = "LIFT", CarrierAmount = 35.00m },
                new() { AccessorialType = AccessorialType.AddressCorrection, CarrierAccessorialCode = "ADDR", CarrierAmount = 18.00m },
                new() { AccessorialType = AccessorialType.ReDelivery, CarrierAccessorialCode = "REDEL", CarrierAmount = 15.00m },
                new() { AccessorialType = AccessorialType.ResidentialDelivery, CarrierAccessorialCode = "RES", CarrierAmount = 5.00m }
            }
        });

        var result = await engine.RateShipmentFreightAsync(shipment.Id);

        // Assert:
        // Base Cost: $50.00 + 20% markup = $60.00 sell
        // Accessorial Buy: $35 + $18 + $15 + $5 = $73.00
        // Accessorial Sell: $50 (fixed) + $18 (pass) + $0 (absorbed) + $6 (marked up) = $74.00
        // Total Carrier Cost: $50 + $73 = $123.00
        // Total Client Freight: $60 + $74 = $134.00
        // Gross Margin: $134.00 - $123.00 = $11.00
        Assert.Equal(123.00m, result.TotalCarrierCost);
        Assert.Equal(134.00m, result.TotalClientFreight);
        Assert.Equal(11.00m, result.GrossMarginAmount);
    }

    [Fact]
    public async Task Test_07_Duplicate_Carrier_Cost_Ingestion()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (engine, context) = CreateServices(fix, bypassTenant: true);
        var shipment = await SeedShipmentAsync(context, fix.ClientId, fix.FacilityId);

        var req = new IngestCarrierCostRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            BaseTransportationCost = 35.00m,
            Currency = "USD",
            CostAssessedAtUtc = DateTime.UtcNow,
            IdempotencyKey = $"COST-DUP-{shipment.Id:N}"
        };

        var first = await engine.IngestCarrierCostAsync(req);
        Assert.True(first.IsSuccess);
        Assert.False(first.IsDuplicate);

        var second = await engine.IngestCarrierCostAsync(req);
        Assert.True(second.IsSuccess);
        Assert.True(second.IsDuplicate);
        Assert.Equal(first.CarrierCostRecordId, second.CarrierCostRecordId);
        Assert.Equal(first.TotalCarrierCost, second.TotalCarrierCost);

        var count = await context.CarrierCostRecords.CountAsync(c => c.IdempotencyKey == req.IdempotencyKey);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Test_08_Duplicate_Accessorial_Event_Handling()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (engine, context) = CreateServices(fix, bypassTenant: true);
        var shipment = await SeedShipmentAsync(context, fix.ClientId, fix.FacilityId);

        var costResult = await engine.IngestCarrierCostAsync(new IngestCarrierCostRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            BaseTransportationCost = 20.00m,
            Currency = "USD",
            CostAssessedAtUtc = DateTime.UtcNow,
            IdempotencyKey = $"COST-ACC-DUP-{shipment.Id:N}"
        });

        var accKey = $"ACC-KEY-{shipment.Id:N}-HAZMAT";

        var firstAcc = await engine.IngestAccessorialChargeAsync(
            costResult.CarrierCostRecordId,
            AccessorialType.DangerousGoodsHazmat,
            "HAZ",
            25.00m,
            "USD",
            accKey,
            DateTime.UtcNow);

        var secondAcc = await engine.IngestAccessorialChargeAsync(
            costResult.CarrierCostRecordId,
            AccessorialType.DangerousGoodsHazmat,
            "HAZ",
            25.00m,
            "USD",
            accKey,
            DateTime.UtcNow);

        Assert.Equal(firstAcc.Id, secondAcc.Id);
        var totalAccCount = await context.CarrierAccessorialCharges.CountAsync(a => a.IdempotencyKey == accKey);
        Assert.Equal(1, totalAccCount);
    }

    [Fact]
    public async Task Test_09_Late_Arriving_Carrier_Surcharge()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (engine, context) = CreateServices(fix, bypassTenant: true);
        var shipment = await SeedShipmentAsync(context, fix.ClientId, fix.FacilityId, "FEDEX");

        await engine.CreateFreightAgreementAsync(new CreateFreightAgreementRequest
        {
            ClientId = fix.ClientId,
            AgreementCode = "AGR-LATE-ACC",
            Name = "Late Acc Agreement",
            CarrierCode = "FEDEX",
            PricingModel = FreightPricingModel.CostPlusPercentageMarkup,
            MarkupPercentage = 10.00m,
            EffectiveFromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        // 1. Initial Ingestion & Rating (Base: $20.00 -> Sell: $22.00)
        var costResult = await engine.IngestCarrierCostAsync(new IngestCarrierCostRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            BaseTransportationCost = 20.00m,
            Currency = "USD",
            CostAssessedAtUtc = DateTime.UtcNow,
            IdempotencyKey = $"COST-LATE-{shipment.Id:N}"
        });

        var initialRate = await engine.RateShipmentFreightAsync(shipment.Id);
        Assert.Equal(20.00m, initialRate.TotalCarrierCost);
        Assert.Equal(22.00m, initialRate.TotalClientFreight);

        // 2. Late Surcharge arrives after delivery: Address Correction ($15.00 carrier fee)
        var lateAcc = await engine.IngestAccessorialChargeAsync(
            costResult.CarrierCostRecordId,
            AccessorialType.AddressCorrection,
            "ADDR_CORR",
            15.00m,
            "USD",
            $"LATE-ADDR-{shipment.Id:N}",
            DateTime.UtcNow);

        var costRecord = await context.CarrierCostRecords.FindAsync(costResult.CarrierCostRecordId);
        Assert.NotNull(costRecord);
        Assert.Equal(35.00m, costRecord.TotalCarrierCost); // $20 + $15

        // Verify original margin record retains historical rated baseline
        var marginRecord = await context.ShipmentFreightMarginRecords.FirstOrDefaultAsync(m => m.ShipmentId == shipment.Id);
        Assert.NotNull(marginRecord);
        Assert.Equal(20.00m, marginRecord.TotalCarrierCost);
        Assert.Equal(22.00m, marginRecord.TotalClientFreight);
    }

    [Fact]
    public async Task Test_10_Carrier_Invoice_Cost_Reconciliation()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (engine, context) = CreateServices(fix, bypassTenant: true);
        var shipment = await SeedShipmentAsync(context, fix.ClientId, fix.FacilityId, "FEDEX");

        await engine.CreateFreightAgreementAsync(new CreateFreightAgreementRequest
        {
            ClientId = fix.ClientId,
            AgreementCode = "AGR-RECON",
            Name = "Recon Agreement",
            CarrierCode = "FEDEX",
            PricingModel = FreightPricingModel.CostPlusPercentageMarkup,
            MarkupPercentage = 10.00m,
            EffectiveFromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        // 1. Initial Estimated Booking: $30.00 -> Sell: $33.00, Margin: $3.00
        await engine.IngestCarrierCostAsync(new IngestCarrierCostRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            BaseTransportationCost = 30.00m,
            Currency = "USD",
            CostAssessedAtUtc = DateTime.UtcNow,
            IdempotencyKey = $"COST-RECON-{shipment.Id:N}"
        });

        await engine.RateShipmentFreightAsync(shipment.Id);

        // 2. Carrier Invoice arrives at $38.00 (+$8.00 re-weigh discrepancy)
        var reconReq = new ReconcileCarrierInvoiceRequest
        {
            ClientId = fix.ClientId,
            ShipmentId = shipment.Id,
            CarrierInvoiceNumber = "INV-FDX-99812",
            InvoicedTotalCost = 38.00m,
            AdjustmentType = CarrierCostAdjustmentType.WeightDiscrepancy,
            Reason = "Carrier hub re-weigh adjustment from 5kg to 7.2kg",
            ReconciledAtUtc = DateTime.UtcNow,
            BillAdjustmentToClient = true
        };

        var reconResult = await engine.ReconcileCarrierInvoiceCostAsync(reconReq);

        // Assert:
        // Cost Delta: +$8.00
        // Client Delta: +$8.00 * 1.10 = +$8.80
        // Net Adjusted Cost: $38.00
        // Net Adjusted Sell: $33.00 + $8.80 = $41.80
        // Net Adjusted Margin: $41.80 - $38.00 = $3.80
        Assert.True(reconResult.IsSuccess);
        Assert.True(reconResult.HasDiscrepancy);
        Assert.Equal(8.00m, reconResult.CarrierCostDelta);
        Assert.Equal(8.80m, reconResult.ClientFreightDelta);
        Assert.Equal(3.80m, reconResult.NewNetGrossMargin);

        // Verify original baseline margin record was preserved and net adjusted fields updated
        var marginRecord = await context.ShipmentFreightMarginRecords.FirstOrDefaultAsync(m => m.ShipmentId == shipment.Id);
        Assert.NotNull(marginRecord);
        Assert.Equal(30.00m, marginRecord.TotalCarrierCost);       // Original baseline preserved!
        Assert.Equal(33.00m, marginRecord.TotalClientFreight);     // Original baseline preserved!
        Assert.Equal(38.00m, marginRecord.NetAdjustedCarrierCost); // Net adjusted updated!
        Assert.Equal(41.80m, marginRecord.NetAdjustedClientFreight);
        Assert.Equal(3.80m, marginRecord.NetAdjustedMargin);
        Assert.Equal(1, marginRecord.AdjustmentCount);
        Assert.True(marginRecord.IsReconciled);

        // Verify linked adjustment BillableActivity was created
        Assert.NotNull(reconResult.AdjustmentBillableActivityId);
        var adjActivity = await context.BillableActivities.FindAsync(reconResult.AdjustmentBillableActivityId.Value);
        Assert.NotNull(adjActivity);
        Assert.Equal(8.80m, adjActivity.TotalAmount);
    }

    [Fact]
    public async Task Test_11_Buy_Side_And_Sell_Side_Currency_Mismatch()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (engine, context) = CreateServices(fix, bypassTenant: true);
        var shipment = await SeedShipmentAsync(context, fix.ClientId, fix.FacilityId);

        // Agreement is in EUR
        await engine.CreateFreightAgreementAsync(new CreateFreightAgreementRequest
        {
            ClientId = fix.ClientId,
            AgreementCode = "AGR-EUR",
            Name = "Euro Freight Agreement",
            CarrierCode = "FEDEX",
            PricingModel = FreightPricingModel.CostPlusPercentageMarkup,
            MarkupPercentage = 10.00m,
            Currency = "EUR",
            EffectiveFromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        // Carrier cost is in USD
        await engine.IngestCarrierCostAsync(new IngestCarrierCostRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            BaseTransportationCost = 25.00m,
            Currency = "USD",
            CostAssessedAtUtc = DateTime.UtcNow,
            IdempotencyKey = $"COST-CURR-{shipment.Id:N}"
        });

        // Rating should fail with InvalidDomainStateException (no matching EUR agreement for USD cost)
        await Assert.ThrowsAsync<InvalidDomainStateException>(() => engine.RateShipmentFreightAsync(shipment.Id));
    }

    [Fact]
    public async Task Test_12_Concurrent_Freight_Rating_Snapshot_Preservation()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (engine, context) = CreateServices(fix, bypassTenant: true);
        var shipment = await SeedShipmentAsync(context, fix.ClientId, fix.FacilityId, "FEDEX");

        await engine.CreateFreightAgreementAsync(new CreateFreightAgreementRequest
        {
            ClientId = fix.ClientId,
            AgreementCode = "AGR-CONCURRENCY",
            Name = "Concurrency Test Agreement",
            CarrierCode = "FEDEX",
            PricingModel = FreightPricingModel.CostPlusPercentageMarkup,
            MarkupPercentage = 15.00m,
            EffectiveFromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        await engine.IngestCarrierCostAsync(new IngestCarrierCostRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            BaseTransportationCost = 40.00m,
            Currency = "USD",
            CostAssessedAtUtc = DateTime.UtcNow,
            IdempotencyKey = $"COST-CONCUR-{shipment.Id:N}"
        });

        // First Rating
        var first = await engine.RateShipmentFreightAsync(shipment.Id);
        Assert.False(first.IsAlreadyRated);
        Assert.Equal(46.00m, first.TotalClientFreight);

        // Second Rating (Concurrent / Retry)
        var second = await engine.RateShipmentFreightAsync(shipment.Id);
        Assert.True(second.IsAlreadyRated);
        Assert.Equal(first.MarginRecordId, second.MarginRecordId);
        Assert.Equal(first.TotalClientFreight, second.TotalClientFreight);

        var marginCount = await context.ShipmentFreightMarginRecords.CountAsync(m => m.ShipmentId == shipment.Id);
        Assert.Equal(1, marginCount);
    }

    [Fact]
    public async Task Test_13_Historical_Pricing_Snapshot_Preservation()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (engine, context) = CreateServices(fix, bypassTenant: true);
        var shipment = await SeedShipmentAsync(context, fix.ClientId, fix.FacilityId, "FEDEX");

        var agreement = await engine.CreateFreightAgreementAsync(new CreateFreightAgreementRequest
        {
            ClientId = fix.ClientId,
            AgreementCode = "AGR-HISTORICAL",
            Name = "Historical Agreement v1",
            CarrierCode = "FEDEX",
            PricingModel = FreightPricingModel.CostPlusPercentageMarkup,
            MarkupPercentage = 10.00m,
            EffectiveFromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        await engine.IngestCarrierCostAsync(new IngestCarrierCostRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            BaseTransportationCost = 50.00m,
            Currency = "USD",
            CostAssessedAtUtc = DateTime.UtcNow,
            IdempotencyKey = $"COST-HIST-{shipment.Id:N}"
        });

        // Rate with v1 (10% markup -> $55.00)
        var rateResult = await engine.RateShipmentFreightAsync(shipment.Id);
        Assert.Equal(55.00m, rateResult.TotalClientFreight);

        // Agreement is later updated to 25% markup
        agreement.MarkupPercentage = 25.00m;
        await context.SaveChangesAsync();

        // Re-rating must preserve original 10% snapshot
        var recheckResult = await engine.RateShipmentFreightAsync(shipment.Id);
        Assert.True(recheckResult.IsAlreadyRated);
        Assert.Equal(55.00m, recheckResult.TotalClientFreight); // Preserved at $55.00, not $62.50!
    }

    [Fact]
    public async Task Test_14_Multiple_Packages_Under_One_Shipment()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (engine, context) = CreateServices(fix, bypassTenant: true);

        // Shipment with 4 packages
        var shipment = await SeedShipmentAsync(context, fix.ClientId, fix.FacilityId, "UPS", packageCount: 4);

        await engine.CreateFreightAgreementAsync(new CreateFreightAgreementRequest
        {
            ClientId = fix.ClientId,
            AgreementCode = "AGR-MULTI-PKG",
            Name = "Multi Package Agreement",
            CarrierCode = "UPS",
            PricingModel = FreightPricingModel.FlatFeePerPackage,
            FlatFeePerPackage = 3.50m,
            EffectiveFromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        await engine.IngestCarrierCostAsync(new IngestCarrierCostRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ShipmentId = shipment.Id,
            CarrierCode = "UPS",
            BaseTransportationCost = 40.00m,
            Currency = "USD",
            CostAssessedAtUtc = DateTime.UtcNow,
            IdempotencyKey = $"COST-MULTI-{shipment.Id:N}"
        });

        var result = await engine.RateShipmentFreightAsync(shipment.Id);

        // Carrier Cost: $40.00
        // Handling: 4 packages * $3.50 = $14.00
        // Sell: $54.00, Gross Margin: $14.00
        Assert.Equal(40.00m, result.TotalCarrierCost);
        Assert.Equal(54.00m, result.TotalClientFreight);
        Assert.Equal(14.00m, result.GrossMarginAmount);
    }

    [Fact]
    public async Task Test_15_Partial_Shipment_And_Unrated_Cancellation_Handling()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (engine, context) = CreateServices(fix, bypassTenant: true);
        var shipment = await SeedShipmentAsync(context, fix.ClientId, fix.FacilityId);

        // Cancelling shipment before rating
        shipment.Status = ShipmentStatus.Cancelled;
        await context.SaveChangesAsync();

        // Attempting to rate a shipment without carrier cost throws proper exception
        await Assert.ThrowsAsync<InvalidDomainStateException>(() => engine.RateShipmentFreightAsync(shipment.Id));
    }

    [Fact]
    public async Task Test_16_Tenant_Isolation_Guards()
    {
        var fix = await SeedTestEnvironmentAsync();

        // Service scoped strictly to fix.ClientId tenant
        var (engine, context) = CreateServices(fix, tenantOverride: fix.ClientId, bypassTenant: false);
        var foreignShipment = await SeedShipmentAsync(context, fix.ForeignClientId, fix.FacilityId);

        // Cross-tenant carrier cost ingestion attempt
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            engine.IngestCarrierCostAsync(new IngestCarrierCostRequest
            {
                ClientId = fix.ForeignClientId,
                FacilityId = fix.FacilityId,
                ShipmentId = foreignShipment.Id,
                CarrierCode = "FEDEX",
                BaseTransportationCost = 20.00m,
                IdempotencyKey = "CROSS-TENANT-COST"
            }));

        // Cross-tenant agreement creation attempt
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            engine.CreateFreightAgreementAsync(new CreateFreightAgreementRequest
            {
                ClientId = fix.ForeignClientId,
                AgreementCode = "AGR-FOREIGN",
                Name = "Foreign",
                EffectiveFromUtc = DateTime.UtcNow
            }));
    }

    [Fact]
    public async Task Test_17_Zero_Cost_And_Negative_Adjustment_Edge_Cases()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (engine, context) = CreateServices(fix, bypassTenant: true);
        var shipment = await SeedShipmentAsync(context, fix.ClientId, fix.FacilityId, "FEDEX");

        await engine.CreateFreightAgreementAsync(new CreateFreightAgreementRequest
        {
            ClientId = fix.ClientId,
            AgreementCode = "AGR-ZERO-DISPUTE",
            Name = "Dispute Agreement",
            CarrierCode = "FEDEX",
            PricingModel = FreightPricingModel.CostPlusPercentageMarkup,
            MarkupPercentage = 10.00m,
            EffectiveFromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        // 1. Initial Cost: $50.00 -> Sell: $55.00
        await engine.IngestCarrierCostAsync(new IngestCarrierCostRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            BaseTransportationCost = 50.00m,
            Currency = "USD",
            CostAssessedAtUtc = DateTime.UtcNow,
            IdempotencyKey = $"COST-ZERO-{shipment.Id:N}"
        });

        await engine.RateShipmentFreightAsync(shipment.Id);

        // 2. Negative Adjustment: Carrier credit of $10.00 issued following billing dispute
        // Invoiced cost = $40.00 (delta = -$10.00)
        var reconReq = new ReconcileCarrierInvoiceRequest
        {
            ClientId = fix.ClientId,
            ShipmentId = shipment.Id,
            CarrierInvoiceNumber = "CREDIT-FDX-01",
            InvoicedTotalCost = 40.00m,
            AdjustmentType = CarrierCostAdjustmentType.DisputeCredit,
            Reason = "Carrier service failure credit",
            ReconciledAtUtc = DateTime.UtcNow,
            BillAdjustmentToClient = true
        };

        var reconResult = await engine.ReconcileCarrierInvoiceCostAsync(reconReq);

        // Cost Delta: -$10.00
        // Client Delta: -$10.00 * 1.10 = -$11.00 credit
        // Net Adjusted Cost: $40.00
        // Net Adjusted Sell: $55.00 - $11.00 = $44.00
        // Net Adjusted Margin: $44.00 - $40.00 = $4.00
        Assert.Equal(-10.00m, reconResult.CarrierCostDelta);
        Assert.Equal(-11.00m, reconResult.ClientFreightDelta);
        Assert.Equal(4.00m, reconResult.NewNetGrossMargin);

        // Verify credit BillableActivity was generated with credit description
        Assert.NotNull(reconResult.AdjustmentBillableActivityId);
        var creditAct = await context.BillableActivities.FindAsync(reconResult.AdjustmentBillableActivityId.Value);
        Assert.NotNull(creditAct);
        Assert.Contains("[Credit Adjustment]", creditAct.Description);
        Assert.Equal(11.00m, creditAct.TotalAmount);
    }

    [Fact]
    public async Task Test_18_Freight_Margin_Reconciliation_Audit()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (engine, context) = CreateServices(fix, bypassTenant: true);
        var shipment = await SeedShipmentAsync(context, fix.ClientId, fix.FacilityId, "FEDEX");

        await engine.CreateFreightAgreementAsync(new CreateFreightAgreementRequest
        {
            ClientId = fix.ClientId,
            AgreementCode = "AGR-AUDIT",
            Name = "Audit Agreement",
            CarrierCode = "FEDEX",
            PricingModel = FreightPricingModel.CostPlusPercentageMarkup,
            MarkupPercentage = 20.00m,
            EffectiveFromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        await engine.IngestCarrierCostAsync(new IngestCarrierCostRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            BaseTransportationCost = 100.00m,
            FuelSurcharge = 15.00m,
            InsuranceCost = 5.00m,
            Currency = "USD",
            CostAssessedAtUtc = DateTime.UtcNow,
            IdempotencyKey = $"COST-AUDIT-{shipment.Id:N}",
            Accessorials = new List<IngestAccessorialChargeDto>
            {
                new() { AccessorialType = AccessorialType.ResidentialDelivery, CarrierAccessorialCode = "RES", CarrierAmount = 10.00m }
            }
        });

        var result = await engine.RateShipmentFreightAsync(shipment.Id);

        var marginRecord = await context.ShipmentFreightMarginRecords.FindAsync(result.MarginRecordId);
        Assert.NotNull(marginRecord);

        // Reconcile mathematical relationship:
        // TotalCarrierCost = Base + Fuel + Insurance + Accessorial
        Assert.Equal(
            marginRecord.TotalCarrierCost,
            marginRecord.CarrierBaseCost + marginRecord.CarrierFuelSurcharge + marginRecord.CarrierInsuranceCost + marginRecord.CarrierAccessorialCost);

        // GrossMargin = TotalClientFreight - TotalCarrierCost
        Assert.Equal(
            marginRecord.GrossMarginAmount,
            marginRecord.TotalClientFreight - marginRecord.TotalCarrierCost);

        // Markup % = (GrossMargin / TotalCarrierCost) * 100
        var expectedMarkupPct = decimal.Round((marginRecord.GrossMarginAmount / marginRecord.TotalCarrierCost) * 100m, 2);
        Assert.Equal(expectedMarkupPct, marginRecord.MarkupPercentage);

        // Margin % = (GrossMargin / TotalClientFreight) * 100
        var expectedMarginPct = decimal.Round((marginRecord.GrossMarginAmount / marginRecord.TotalClientFreight) * 100m, 2);
        Assert.Equal(expectedMarginPct, marginRecord.MarginPercentage);
    }
}

