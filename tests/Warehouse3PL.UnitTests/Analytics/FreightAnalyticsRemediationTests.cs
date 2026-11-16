using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;
using Warehouse3PL.Infrastructure.Services;
using Warehouse3PL.UnitTests.Common;

namespace Warehouse3PL.UnitTests.Analytics;

public partial class FreightCostVarianceAndAccessorialAnalyticsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImperialContract_UsesCubicInchesPerPound_AndRoundsAfterConversion(bool justAboveIncrement)
    {
        var tenant = new TestTenantProvider { BypassTenantFilter = true };
        var user = new TestCurrentUserContext { UserType = UserType.Staff, PermissionsList = ["Analytics:CrossFacility"] };
        var (db, _) = await CreateSeededContextAsync(tenant, user);
        var date = DateTime.UtcNow.AddDays(-1);
        var shipment = CreateShipment(_clientAId, _facility1Id, "IMPERIAL", dispatchedAt: date, weightKg: 1m);
        var package = shipment.Packages.First();
        package.LengthCm = 25.4m; package.WidthCm = 25.4m; package.HeightCm = 25.4m;
        package.VolumetricWeightKg = 999m; // Stale stored quote must never override contractual dimensions.
        // 10x10x10 inches / 139 = 7.194... lb, rounded to 8 lb.
        decimal expectedLb = 8m;
        if (justAboveIncrement)
        {
            package.LengthCm = package.WidthCm = package.HeightCm = 1m;
            package.GrossWeightKg = 8.00001m / 2.20462262m;
            expectedLb = 9m; // Rounding the converted scale to four decimals first would incorrectly produce 8.
        }
        db.Shipments.Add(shipment);
        db.CarrierRateAgreements.Add(CarrierRateAgreement.Create("FEDEX", "IMPERIAL", "Imperial", 10m, "USD", "LB",
            RateModelType.PerUnit, PricingTierCalculationModel.FlatRate, date.AddDays(-1),
            dimensionalWeightDivisor: 139m, weightRoundingIncrementKg: 1m));
        db.CarrierCostRecords.Add(new CarrierCostRecord { ClientId = _clientAId, FacilityId = _facility1Id, ShipmentId = shipment.Id,
            CarrierCode = "FEDEX", BaseTransportationCost = expectedLb * 10m, TotalCarrierCost = expectedLb * 10m,
            BilledWeightKg = expectedLb / 2.20462262m, CostAssessedAtUtc = date, Status = CarrierCostStatus.Invoiced });
        await db.SaveChangesAsync();
        var (_, service) = CreateServices(db, tenant, user);
        var result = await service.GetRateCardComplianceAnalysisAsync("FEDEX", date.Date, date.Date);
        Assert.Equal(1, result.CompliantShipmentsCount);
        Assert.Equal(0, result.IndeterminateShipmentsCount);
    }

    [Theory]
    [InlineData("mapped", 1)]
    [InlineData("postal", 1)]
    [InlineData("lane", 1)]
    [InlineData("nonmatch", 1)]
    [InlineData("unmapped", 0)]
    [InlineData("missing", 0)]
    [InlineData("mixed", 0)]
    public async Task ContractRouting_RequiresExplicitCompleteMapping(string scenario, int audited)
    {
        var tenant = new TestTenantProvider { BypassTenantFilter = true };
        var user = new TestCurrentUserContext { UserType = UserType.Staff, PermissionsList = ["Analytics:CrossFacility"] };
        var (db, _) = await CreateSeededContextAsync(tenant, user);
        var date = DateTime.UtcNow.AddDays(-1);
        var shipment = CreateShipment(_clientAId, _facility1Id, "ROUTING", dispatchedAt: date);
        var agreement = CarrierRateAgreement.Create("FEDEX", "ZONE8", "Explicit carrier zone", 40m,
            "USD", "SHIPMENT", RateModelType.FlatFee, PricingTierCalculationModel.FlatRate, date.AddDays(-1), zoneOrLane: "Zone 8");
        if (scenario != "unmapped") agreement.DestinationStateOrProvince = "CA";
        if (scenario == "postal") agreement.DestinationPostalCodePrefix = "902";
        if (scenario == "lane") agreement.DestinationFacilityCode = "FAC-DFW1";
        var package = shipment.Packages.First();
        var order = new OutboundOrder { Id = package.OutboundOrderId, ClientId = _clientAId, FacilityId = _facility1Id, OrderNumber = "ROUTE" };
        order.SetShippingAddress(new ShippingAddress { StateOrProvince = scenario == "nonmatch" ? "NY" : "CA", PostalCode = "90210", CountryCode = "USA" });
        if (scenario == "lane") order.DestinationFacility = await db.Facilities.FindAsync(_facility2Id);
        if (scenario != "missing") db.OutboundOrders.Add(order);
        if (scenario == "mixed")
        {
            var otherPackage = CreateShipment(_clientAId, _facility1Id, "OTHER").Packages.First();
            otherPackage.ShipmentId = shipment.Id;
            var otherOrder = new OutboundOrder { Id = otherPackage.OutboundOrderId, ClientId = _clientAId, FacilityId = _facility1Id, OrderNumber = "OTHER" };
            otherOrder.SetShippingAddress(new ShippingAddress { StateOrProvince = "NY" });
            db.OutboundOrders.Add(otherOrder);
            shipment.Packages.Add(otherPackage);
        }
        db.CarrierRateAgreements.AddRange(agreement, CarrierRateAgreement.Create("FEDEX", "GEN", "Generic", 40m,
            "USD", "SHIPMENT", RateModelType.FlatFee, PricingTierCalculationModel.FlatRate, date.AddDays(-1)));
        // A client fixed-rate fallback must not bypass unresolved carrier routing.
        db.ClientFreightAgreements.Add(new ClientFreightAgreement { ClientId = _clientAId, CarrierCode = "FEDEX", FixedFreightRate = 40m,
            EffectiveFromUtc = date.AddDays(-1), IsActive = true });
        db.Shipments.Add(shipment);
        db.CarrierCostRecords.Add(new CarrierCostRecord { ClientId = _clientAId, FacilityId = _facility1Id, ShipmentId = shipment.Id,
            CarrierCode = "FEDEX", BaseTransportationCost = 40m, TotalCarrierCost = 40m, CostAssessedAtUtc = date, Status = CarrierCostStatus.Invoiced });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var (_, service) = CreateServices(db, tenant, user);
        var result = await service.GetRateCardComplianceAnalysisAsync("FEDEX", date.Date, date.Date);
        Assert.Equal(audited, result.TotalAuditedShipmentsCount);
        Assert.Equal(1 - audited, result.IndeterminateShipmentsCount);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData("KG", "dimension")]
    [InlineData("KG", "scale")]
    [InlineData("KG", "package")]
    [InlineData("KG", "manifest")]
    [InlineData("UNIT", "line")]
    [InlineData("PIECE", "line")]
    public async Task IncompletePackageEvidence_CannotRateKnownSubset(string uom, string missing)
    {
        var tenant = new TestTenantProvider { BypassTenantFilter = true };
        var user = new TestCurrentUserContext { UserType = UserType.Staff, PermissionsList = ["Analytics:CrossFacility"] };
        var (db, _) = await CreateSeededContextAsync(tenant, user);
        var date = DateTime.UtcNow.AddDays(-1);
        var shipment = CreateShipment(_clientAId, _facility1Id, "MISSING", dispatchedAt: date);
        shipment.Packages.First().Lines.Add(new PackageLine { Quantity = 3m });
        var incomplete = CreateShipment(_clientAId, _facility1Id, "INCOMPLETE").Packages.First();
        incomplete.ShipmentId = shipment.Id;
        if (missing == "dimension") incomplete.HeightCm = 0m;
        if (missing == "scale") incomplete.GrossWeightKg = 0m;
        shipment.Packages.Add(incomplete);
        shipment.TotalPackagesCount = missing == "manifest" ? 3 : 2;
        if (missing == "package") shipment.Packages.Clear();
        db.Shipments.Add(shipment);
        db.CarrierRateAgreements.Add(CarrierRateAgreement.Create("FEDEX", "EVIDENCE", "Evidence", 10m, "USD", uom,
            RateModelType.PerUnit, PricingTierCalculationModel.FlatRate, date.AddDays(-1)));
        db.CarrierCostRecords.Add(new CarrierCostRecord { ClientId = _clientAId, FacilityId = _facility1Id, ShipmentId = shipment.Id,
            CarrierCode = "FEDEX", BaseTransportationCost = 100m, TotalCarrierCost = 100m, BilledWeightKg = 10m,
            CostAssessedAtUtc = date, Status = CarrierCostStatus.Invoiced });
        await db.SaveChangesAsync();
        var (_, service) = CreateServices(db, tenant, user);
        var result = await service.GetRateCardComplianceAnalysisAsync("FEDEX", date.Date, date.Date);
        Assert.Equal(1, result.IndeterminateShipmentsCount);
        Assert.Equal(0, result.TotalAuditedShipmentsCount);
    }

    [Fact]
    public async Task ClientComplianceQuery_DoesNotMaterializeOtherClientAgreements_WithTenantBypass()
    {
        var tenant = new TestTenantProvider { BypassTenantFilter = true };
        var user = new TestCurrentUserContext { UserType = UserType.Staff, PermissionsList = ["Analytics:CrossFacility"] };
        var guard = new ClientMaterializationGuard(_clientAId);
        var (seed, _) = await CreateSeededContextAsync(tenant, user, queryInterceptor: guard);
        var date = DateTime.UtcNow.AddDays(-1);
        seed.CarrierRateAgreements.Add(CarrierRateAgreement.Create("FEDEX", "PRIVATE", "Other client", 10m, "USD", "SHIPMENT",
            RateModelType.FlatFee, PricingTierCalculationModel.FlatRate, date.AddDays(-1), clientId: _clientBId));
        seed.ClientFreightAgreements.Add(new ClientFreightAgreement { ClientId = _clientBId, CarrierCode = "FEDEX", IsActive = true, EffectiveFromUtc = date.AddDays(-1) });
        seed.Shipments.Add(CreateShipment(_clientBId, _facility2Id, "PRIVATE", dispatchedAt: date));
        await seed.SaveChangesAsync();
        user.UserType = UserType.Client;
        user.TenantId = _clientAId;
        seed.ChangeTracker.Clear();
        var (_, service) = CreateServices(seed, tenant, user);
        var result = await service.GetRateCardComplianceAnalysisAsync("FEDEX", date.Date, date.Date);
        Assert.Equal(0, result.TotalAuditedShipmentsCount);
    }

    private sealed class ClientMaterializationGuard(Guid clientId) : IMaterializationInterceptor
    {
        public object InitializedInstance(MaterializationInterceptionData data, object entity)
        {
            if (entity is CarrierRateAgreement carrier) Assert.True(carrier.ClientId == null || carrier.ClientId == clientId);
            if (entity is ClientFreightAgreement client) Assert.Equal(clientId, client.ClientId);
            if (entity is Shipment shipment) Assert.Equal(clientId, shipment.ClientId);
            return entity;
        }
    }
}
