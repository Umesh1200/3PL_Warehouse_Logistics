using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;
using Warehouse3PL.Infrastructure.Services;
using Warehouse3PL.UnitTests.Common;

namespace Warehouse3PL.UnitTests.Analytics;

public class FinancialQueryTranslationAuditTests
{
    [Fact]
    public void ScopedFinancialIncludes_TranslateWithPostgreSqlProviderWithoutConnecting()
    {
        var user = new TestCurrentUserContext();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only;Username=postgres").Options;
        using var db = new ApplicationDbContext(options, new TestTenantProvider { BypassTenantFilter = true }, user,
            new AuditableEntityInterceptor(user, new DateTimeProvider()));

        // These correlated filtered includes protect ledger materialization. Exercise the actual
        // relational translator: InMemory execution cannot validate their SQL translatability.
        var shipmentsSql = db.Shipments.AsNoTracking()
            .Include(s => s.Packages.Where(p => p.ClientId == p.Shipment!.ClientId && p.FacilityId == p.Shipment.FacilityId))
            .ThenInclude(p => p.Lines).ToQueryString();
        var costsSql = db.CarrierCostRecords.AsNoTracking()
            .Include(c => c.Adjustments.Where(a => a.ClientId == a.CarrierCostRecord.ClientId && a.FacilityId == a.CarrierCostRecord.FacilityId))
            .Where(c => c.ClientId == c.Shipment.ClientId && c.FacilityId == c.Shipment.FacilityId).ToQueryString();
        var chargesSql = db.CarrierAccessorialCharges.AsNoTracking().Include(a => a.CarrierCostRecord)
            .Where(a => a.ClientId == a.Shipment.ClientId && a.FacilityId == a.Shipment.FacilityId
                && a.CarrierCostRecord.ShipmentId == a.ShipmentId && a.CarrierCostRecord.ClientId == a.ClientId
                && a.CarrierCostRecord.FacilityId == a.FacilityId).ToQueryString();
        Assert.Contains("client_id", shipmentsSql);
        Assert.Contains("facility_id", shipmentsSql);
        Assert.Contains("client_id", costsSql);
        Assert.Contains("facility_id", costsSql);
        Assert.Contains("shipment_id", chargesSql);
    }
}
