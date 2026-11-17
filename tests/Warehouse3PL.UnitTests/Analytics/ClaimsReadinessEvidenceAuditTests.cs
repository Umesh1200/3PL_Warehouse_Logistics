using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Services.Analytics;
using Warehouse3PL.UnitTests.Common;

namespace Warehouse3PL.UnitTests.Analytics;

public class ClaimsReadinessEvidenceAuditTests
{
    private static (ApplicationDbContext Db, ClaimsReadinessService Service, Shipment Shipment) Fixture()
    {
        var user = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var db = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, user, Guid.NewGuid().ToString());
        var shipment = new Shipment { ClientId = Guid.NewGuid(), FacilityId = Guid.NewGuid(), ShipmentNumber = "AUDIT", CarrierCode = "UPS" };
        db.Shipments.Add(shipment);
        return (db, new ClaimsReadinessService(db, NullLogger<ClaimsReadinessService>.Instance, new(db, user)), shipment);
    }

    [Theory]
    [InlineData(50, 0)]
    [InlineData(0, 15)]
    [InlineData(65, 15)]
    public async Task FreightAndInsurancePremiums_AreNotGoodsValuation(decimal freight, decimal insurance)
    {
        var (db, service, shipment) = Fixture();
        using (db)
        {
            db.CarrierCostRecords.Add(new CarrierCostRecord { ShipmentId = shipment.Id, ClientId = shipment.ClientId,
                FacilityId = shipment.FacilityId, CarrierCode = "UPS", TotalCarrierCost = freight, InsuranceCost = insurance });
            await db.SaveChangesAsync();
            var assessment = await service.AssessShipmentClaimsReadinessAsync(shipment.Id);
            Assert.False(assessment.HasValuationEvidence);
            Assert.Equal(ClaimsReadinessStatus.InsufficientEvidence, assessment.ReadinessStatus);
            Assert.Contains(assessment.MissingEvidenceItems, item => item.Contains("valuation"));
        }
    }

    [Theory]
    [InlineData(LostShipmentAlertStatus.Active, 72, true)]
    [InlineData(LostShipmentAlertStatus.ResolvedDelivered, 72, false)]
    [InlineData(LostShipmentAlertStatus.Dismissed, 72, false)]
    [InlineData(LostShipmentAlertStatus.ClaimInitiated, 72, false)]
    [InlineData(LostShipmentAlertStatus.Active, 0, false)]
    [InlineData(LostShipmentAlertStatus.Active, -1, false)]
    public async Task HistoricalAlerts_RemainHistoryButOnlyActiveValidAlertsEstablishCurrentBreach(
        LostShipmentAlertStatus status, decimal threshold, bool expectedBreach)
    {
        var (db, service, shipment) = Fixture();
        using (db)
        {
            db.CarrierLostShipmentAlerts.Add(new CarrierLostShipmentAlert { ShipmentId = shipment.Id, ClientId = shipment.ClientId,
                FacilityId = shipment.FacilityId, CarrierCode = "UPS", TrackingNumber = "TRACK", AlertStatus = status,
                PolicyThresholdHours = threshold, HoursWithoutScanMovement = 100 });
            await db.SaveChangesAsync();
            var assessment = await service.AssessShipmentClaimsReadinessAsync(shipment.Id);
            Assert.True(assessment.HasObstructionEvidence);
            Assert.Equal(expectedBreach, assessment.HasThresholdBreach);
        }
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public async Task SupersededAndUnfinalizedPod_DoNotEstablishCurrentDeliveryEvidence(bool superseded, bool finalized, bool expected)
    {
        var (db, service, shipment) = Fixture();
        using (db)
        {
            db.ProofOfDeliveries.Add(new ProofOfDelivery { ShipmentId = shipment.Id, ClientId = shipment.ClientId,
                FacilityId = shipment.FacilityId, PackageId = Guid.NewGuid(), IsSuperseded = superseded, IsStorageFinalized = finalized });
            await db.SaveChangesAsync();
            Assert.Equal(expected, (await service.AssessShipmentClaimsReadinessAsync(shipment.Id)).HasPodEvidence);
        }
    }

    [Theory]
    [InlineData(DeliveryExceptionType.WeatherOrNaturalDisaster, DeliveryExceptionStatus.Open, false)]
    [InlineData(DeliveryExceptionType.PackageDamagedInTransit, DeliveryExceptionStatus.Open, true)]
    [InlineData(DeliveryExceptionType.PackageDamagedInTransit, DeliveryExceptionStatus.Resolved, true)]
    [InlineData(DeliveryExceptionType.PackageDamagedInTransit, DeliveryExceptionStatus.Voided, false)]
    public async Task DamageClaims_RequireDocumentedNonVoidedDamage(DeliveryExceptionType type, DeliveryExceptionStatus status, bool expected)
    {
        var (db, service, shipment) = Fixture();
        using (db)
        {
            db.ShipmentDeliveryExceptions.Add(new ShipmentDeliveryException { ShipmentId = shipment.Id, ClientId = shipment.ClientId,
                FacilityId = shipment.FacilityId, PackageId = Guid.NewGuid(), ExceptionType = type, Status = status });
            db.CarrierLostShipmentAlerts.Add(new CarrierLostShipmentAlert { ShipmentId = shipment.Id, ClientId = shipment.ClientId,
                FacilityId = shipment.FacilityId, CarrierCode = "UPS", TrackingNumber = "TRACK" });
            await db.SaveChangesAsync();
            Assert.Equal(expected, (await service.AssessShipmentClaimsReadinessAsync(shipment.Id, ClaimType.Damaged)).HasObstructionEvidence);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FreightEvidence_DoesNotDependOnFirstCostRecord(bool positiveFirst)
    {
        var (db, service, shipment) = Fixture();
        using (db)
        {
            foreach (var amount in positiveFirst ? new[] { 50m, 0m } : new[] { 0m, 50m })
                db.CarrierCostRecords.Add(new CarrierCostRecord { ShipmentId = shipment.Id, ClientId = shipment.ClientId,
                    FacilityId = shipment.FacilityId, CarrierCode = "UPS", TotalCarrierCost = amount });
            await db.SaveChangesAsync();
            Assert.True((await service.AssessShipmentClaimsReadinessAsync(shipment.Id)).HasFreightCostEvidence);
        }
    }
}
