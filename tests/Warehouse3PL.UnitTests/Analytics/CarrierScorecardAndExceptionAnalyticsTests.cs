using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Services.Analytics;
using Warehouse3PL.UnitTests.Common;

namespace Warehouse3PL.UnitTests.Analytics;

/// <summary>
/// Unit and integration verification suite for Phase 6.4.3:
/// Carrier Scorecards, Transit-Time Distributions, Delivery Exception Intelligence,
/// Root-Cause Delay Attribution, and Claims Readiness Auditing.
/// </summary>
public class CarrierScorecardAndExceptionAnalyticsTests
{
    private static TransportationAnalyticsAuthorization PlatformReader(Warehouse3PL.Application.Common.Interfaces.IApplicationDbContext db) =>
        new(db, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] });

    private static async Task SeedClaimShipmentsAsync(Warehouse3PL.Infrastructure.Persistence.ApplicationDbContext db)
    {
        var records = await db.CarrierSlaComplianceRecords.AsNoTracking().ToListAsync();
        foreach (var row in records)
            if (!await db.Shipments.AnyAsync(s => s.Id == row.ShipmentId))
                db.Shipments.Add(new Shipment { Id = row.ShipmentId, ClientId = row.ClientId, FacilityId = row.FacilityId,
                    ShipmentNumber = row.TrackingNumber, CarrierCode = row.CarrierCode });
        await db.SaveChangesAsync();
    }

    private readonly Guid _facilityId = Guid.NewGuid();
    private readonly Guid _otherFacilityId = Guid.NewGuid();
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Guid _otherClientId = Guid.NewGuid();

    [Fact]
    public async Task Test_01_ScorecardAggregation_ReconcilesWithShipmentRecords()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        var attributionService = new DelayAttributionService(context, NullLogger<DelayAttributionService>.Instance);
        var scorecardService = new CarrierScorecardService(context, attributionService, NullLogger<CarrierScorecardService>.Instance, PlatformReader(context));

        var startDate = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 11, 10, 23, 59, 59, DateTimeKind.Utc);

        // Seed 10 shipments: 8 on-time, 2 late; 9 first attempt successes, 1 multi-attempt; 1 with exception
        for (int i = 1; i <= 10; i++)
        {
            bool isOnTime = i <= 8;
            bool isFirstAttempt = i <= 9;
            bool hasException = i == 10;
            decimal transitDays = 2.0m + (i * 0.1m); // 2.1 to 3.0 days

            var record = CarrierSlaComplianceRecord.Create(
                shipmentId: Guid.NewGuid(),
                trackingNumber: $"TRK-FEDEX-{i:D3}",
                carrierCode: "FEDEX",
                serviceLevel: CarrierServiceLevel.StandardGround,
                facilityId: _facilityId,
                clientId: _clientId,
                originRegion: "US-EAST",
                destinationCountry: "US",
                destinationStateOrRegion: "CA",
                destinationPostalCodePrefix: "902",
                dispatchedAtUtc: startDate.AddDays(1),
                contractualTransitDays: 3,
                estimatedDeliveryDateUtc: startDate.AddDays(4),
                slaPolicyId: Guid.NewGuid(),
                slaPolicyVersion: "1.0.0");

            var deliveredTime = startDate.AddDays(1).AddDays((double)transitDays);
            record.RecordDeliveryAttempt(deliveredTime, isFirstAttempt);
            record.RecordDelivery(deliveredTime);

            record.IsOnTime = isOnTime;
            record.ActualTransitDays = transitDays;
            record.TransitTimeVarianceDays = Math.Round(transitDays - 3.0m, 2);
            record.HasDeliveryException = hasException;
            record.QualifyingExceptionsCount = hasException ? 1 : 0;

            context.CarrierSlaComplianceRecords.Add(record);
        }

        await context.SaveChangesAsync();

        var scorecard = await scorecardService.GetCarrierScorecardAsync("FEDEX", startDate, endDate, _facilityId, _clientId);

        Assert.NotNull(scorecard);
        Assert.Equal("FEDEX", scorecard.CarrierCode);
        Assert.Equal(10, scorecard.TotalDispatchedCount);
        Assert.Equal(10, scorecard.TotalDeliveredCount);
        Assert.Equal(10, scorecard.CompletedDeliveryOutcomesCount);

        // 8/10 on-time = 80.00%
        Assert.Equal(80.00m, scorecard.OnTimeDeliveryRatePercent);
        // 9/10 first-attempt = 90.00%
        Assert.Equal(90.00m, scorecard.FirstAttemptDeliveryRatePercent);
        // 1/10 exception = 10.00%
        Assert.Equal(10.00m, scorecard.ExceptionRatePercent);

        // Transit times: 2.1 .. 3.0 -> Average = 2.55 days
        Assert.Equal(2.55m, scorecard.AverageTransitDays);
        Assert.Equal(2.1m, scorecard.MinTransitDays);
        Assert.Equal(3.0m, scorecard.MaxTransitDays);
        // TTV: Actual (2.55) - Contractual (3.00) = -0.45
        Assert.Equal(-0.45m, scorecard.TransitTimeVarianceDays);
    }

    [Fact]
    public async Task Test_02_ZeroDenominators_ReturnNullRates()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        var attributionService = new DelayAttributionService(context, NullLogger<DelayAttributionService>.Instance);
        var scorecardService = new CarrierScorecardService(context, attributionService, NullLogger<CarrierScorecardService>.Instance, PlatformReader(context));

        var startDate = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 11, 10, 23, 59, 59, DateTimeKind.Utc);

        // Query when zero shipments exist
        var scorecard = await scorecardService.GetCarrierScorecardAsync("UPS", startDate, endDate, _facilityId, _clientId);

        Assert.NotNull(scorecard);
        Assert.Equal("UPS", scorecard.CarrierCode);
        Assert.Equal(0, scorecard.TotalDispatchedCount);
        Assert.Equal(0, scorecard.TotalDeliveredCount);
        Assert.Equal(0, scorecard.CompletedDeliveryOutcomesCount);

        // Explicit null verification on zero denominators
        Assert.Null(scorecard.OnTimeDeliveryRatePercent);
        Assert.Null(scorecard.FirstAttemptDeliveryRatePercent);
        Assert.Null(scorecard.ExceptionRatePercent);
        Assert.Null(scorecard.AverageTransitDays);
        Assert.Null(scorecard.MedianTransitDays);
        Assert.Null(scorecard.P75TransitDays);
        Assert.Null(scorecard.P90TransitDays);
        Assert.Null(scorecard.P95TransitDays);
        Assert.Null(scorecard.MinTransitDays);
        Assert.Null(scorecard.MaxTransitDays);
        Assert.Null(scorecard.StandardDeviationTransitDays);
        Assert.Null(scorecard.TransitTimeVarianceDays);
        Assert.False(scorecard.IsStatisticallyReliable);
        Assert.Equal(0, scorecard.SampleSize);
    }

    [Fact]
    public async Task Test_03_MultipleExceptions_DistinctShipmentCountInvariance()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        var attributionService = new DelayAttributionService(context, NullLogger<DelayAttributionService>.Instance);
        var scorecardService = new CarrierScorecardService(context, attributionService, NullLogger<CarrierScorecardService>.Instance, PlatformReader(context));

        var startDate = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 11, 10, 23, 59, 59, DateTimeKind.Utc);

        var shipmentId = Guid.NewGuid();
        var packageId = Guid.NewGuid();
        var trackingNumber = "TRK-MULTI-EX-001";

        // Seed 1 shipment compliance record with 3 qualifying exceptions
        var complianceRecord = CarrierSlaComplianceRecord.Create(
            shipmentId: shipmentId,
            trackingNumber: trackingNumber,
            carrierCode: "DHL",
            serviceLevel: CarrierServiceLevel.StandardGround,
            facilityId: _facilityId,
            clientId: _clientId,
            originRegion: "US-EAST",
            destinationCountry: "US",
            destinationStateOrRegion: "FL",
            destinationPostalCodePrefix: "331",
            dispatchedAtUtc: startDate.AddDays(1),
            contractualTransitDays: 3,
            estimatedDeliveryDateUtc: startDate.AddDays(4),
            slaPolicyId: Guid.NewGuid(),
            slaPolicyVersion: "1.0.0");

        complianceRecord.RecordDeliveryAttempt(startDate.AddDays(4), false);
        complianceRecord.RecordDelivery(startDate.AddDays(5));
        complianceRecord.AttemptCount = 2;
        complianceRecord.IsFirstAttemptSuccess = false;
        complianceRecord.IsOnTime = false;
        complianceRecord.ActualTransitDays = 4.0m;
        complianceRecord.TransitTimeVarianceDays = 1.0m;
        complianceRecord.HasDeliveryException = true;
        complianceRecord.QualifyingExceptionsCount = 3;

        context.CarrierSlaComplianceRecords.Add(complianceRecord);

        // Seed 3 separate physical exception scans for this single shipment
        var ex1 = new ShipmentDeliveryException
        {
            Id = Guid.NewGuid(),
            ShipmentId = shipmentId,
            PackageId = packageId,
            TrackingNumber = trackingNumber,
            CarrierCode = "DHL",
            FacilityId = _facilityId,
            ClientId = _clientId,
            ExceptionType = DeliveryExceptionType.WeatherOrNaturalDisaster,
            CarrierExceptionCode = "WX01",
            CarrierDescription = "Severe snow storm delayed linehaul",
            OccurredAtUtc = startDate.AddDays(2),
            ResolvedAtUtc = startDate.AddDays(2).AddHours(6)
        };

        var ex2 = new ShipmentDeliveryException
        {
            Id = Guid.NewGuid(),
            ShipmentId = shipmentId,
            PackageId = packageId,
            TrackingNumber = trackingNumber,
            CarrierCode = "DHL",
            FacilityId = _facilityId,
            ClientId = _clientId,
            ExceptionType = DeliveryExceptionType.CarrierOperationalDelay,
            CarrierExceptionCode = "SRT02",
            CarrierDescription = "Mechanical sorting delay at sorting hub",
            OccurredAtUtc = startDate.AddDays(3),
            ResolvedAtUtc = startDate.AddDays(3).AddHours(4)
        };

        var ex3 = new ShipmentDeliveryException
        {
            Id = Guid.NewGuid(),
            ShipmentId = shipmentId,
            PackageId = packageId,
            TrackingNumber = trackingNumber,
            CarrierCode = "DHL",
            FacilityId = _facilityId,
            ClientId = _clientId,
            ExceptionType = DeliveryExceptionType.RecipientUnavailable,
            CarrierExceptionCode = "CU03",
            CarrierDescription = "Business closed, no recipient available to sign",
            OccurredAtUtc = startDate.AddDays(4),
            ResolvedAtUtc = startDate.AddDays(5)
        };

        context.ShipmentDeliveryExceptions.AddRange(ex1, ex2, ex3);
        await context.SaveChangesAsync();

        var exIntel = await scorecardService.GetDeliveryExceptionIntelligenceAsync("DHL", startDate, endDate, _facilityId, _clientId);

        Assert.NotNull(exIntel);
        // Total physical events = 3
        Assert.Equal(3, exIntel.TotalExceptionEventsCount);
        // Distinct affected shipments = exactly 1
        Assert.Equal(1, exIntel.DistinctAffectedShipmentsCount);
        // Repeat exception shipments count = 1 (since it had >= 2 exceptions)
        Assert.Equal(1, exIntel.RepeatExceptionShipmentsCount);
        // First attempt correlation = 1 (failed first attempt)
        Assert.Equal(1, exIntel.FirstAttemptFailureCorrelationCount);
        // Exception rate = 1 affected / 1 dispatched = 100.00% (NOT 300%)
        Assert.Equal(100.00m, exIntel.ExceptionRatePercent);

        // Verify category breakdown contains expected counts
        Assert.Equal(1, exIntel.CategoryBreakdown[CanonicalDeliveryExceptionCategory.WeatherDisruption]);
        Assert.Equal(1, exIntel.CategoryBreakdown[CanonicalDeliveryExceptionCategory.CarrierOperationalDelay]);
        Assert.Equal(1, exIntel.CategoryBreakdown[CanonicalDeliveryExceptionCategory.RecipientUnavailable]);
    }

    [Fact]
    public async Task Test_04_TransitTimeDistribution_ExactPercentiles()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        var attributionService = new DelayAttributionService(context, NullLogger<DelayAttributionService>.Instance);
        var scorecardService = new CarrierScorecardService(context, attributionService, NullLogger<CarrierScorecardService>.Instance, PlatformReader(context));

        var startDate = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 11, 30, 23, 59, 59, DateTimeKind.Utc);

        // Seed exactly 31 transit duration observations ranging from 1.0 to 7.0 days
        // Sorted sequence: 1.0, 1.2, 1.4, ..., 7.0
        var durations = new List<decimal>();
        for (int i = 0; i < 31; i++)
        {
            decimal duration = 1.0m + (i * 0.2m); // 1.0, 1.2, 1.4, ... 7.0
            durations.Add(duration);

            var record = CarrierSlaComplianceRecord.Create(
                shipmentId: Guid.NewGuid(),
                trackingNumber: $"TRK-DIST-{i:D3}",
                carrierCode: "FEDEX",
                serviceLevel: CarrierServiceLevel.StandardGround,
                facilityId: _facilityId,
                clientId: _clientId,
                originRegion: "US-EAST",
                destinationCountry: "US",
                destinationStateOrRegion: "TX",
                destinationPostalCodePrefix: "750",
                dispatchedAtUtc: startDate.AddDays(1),
                contractualTransitDays: 3,
                estimatedDeliveryDateUtc: startDate.AddDays(4),
                slaPolicyId: Guid.NewGuid(),
                slaPolicyVersion: "1.0.0");

            var deliveredTime = startDate.AddDays(1).AddDays((double)duration);
            record.RecordDeliveryAttempt(deliveredTime, true);
            record.RecordDelivery(deliveredTime);
            record.IsOnTime = duration <= 3.0m;
            record.ActualTransitDays = duration;
            record.TransitTimeVarianceDays = duration - 3.0m;
            record.HasDeliveryException = false;
            record.QualifyingExceptionsCount = 0;

            context.CarrierSlaComplianceRecords.Add(record);
        }

        await context.SaveChangesAsync();

        var dist = await scorecardService.GetTransitTimeDistributionAsync("FEDEX", startDate, endDate, _facilityId);

        Assert.NotNull(dist);
        Assert.Equal(31, dist.SampleSize);
        Assert.True(dist.IsStatisticallyReliable); // N = 31 >= 30

        Assert.Equal(1.0m, dist.MinDays);
        Assert.Equal(7.0m, dist.MaxDays);
        Assert.Equal(4.0m, dist.MeanDays); // (1.0 + 7.0) / 2 = 4.0

        // For N=31:
        // P50 (Median): k = 0.50 * 30 = 15 -> index 15: 1.0 + 15 * 0.2 = 4.00
        Assert.Equal(4.00m, dist.MedianDays);

        // P75: k = 0.75 * 30 = 22.5 -> between index 22 (5.4) and 23 (5.6) -> 5.50
        Assert.Equal(5.50m, dist.P75Days);

        // P90: k = 0.90 * 30 = 27 -> index 27: 1.0 + 27 * 0.2 = 6.40
        Assert.Equal(6.40m, dist.P90Days);

        // P95: k = 0.95 * 30 = 28.5 -> between index 28 (6.6) and 29 (6.8) -> 6.70
        Assert.Equal(6.70m, dist.P95Days);

        // Standard Deviation should be calculated correctly (> 0)
        Assert.NotNull(dist.StandardDeviationDays);
        Assert.True(dist.StandardDeviationDays > 1.7m && dist.StandardDeviationDays < 1.9m);

        // Histograms
        // lessThan1: 0 (none < 1.0)
        Assert.Equal(0, dist.LessThan1DayCount);
        // oneToTwo: 1.0, 1.2, 1.4, 1.6, 1.8 -> 5
        Assert.Equal(5, dist.OneToTwoDaysCount);
        // twoToThree: 2.0, 2.2, 2.4, 2.6, 2.8 -> 5
        Assert.Equal(5, dist.TwoToThreeDaysCount);
        // threeToFive: 3.0, 3.2, 3.4, 3.6, 3.8, 4.0, 4.2, 4.4, 4.6, 4.8, 5.0 -> 11
        Assert.Equal(11, dist.ThreeToFiveDaysCount);
        // moreThanFive: 5.2, 5.4, 5.6, 5.8, 6.0, 6.2, 6.4, 6.6, 6.8, 7.0 -> 10
        Assert.Equal(10, dist.MoreThanFiveDaysCount);
    }

    [Fact]
    public async Task Test_05_SmallSample_StatisticalReliabilityFlag()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        var attributionService = new DelayAttributionService(context, NullLogger<DelayAttributionService>.Instance);
        var scorecardService = new CarrierScorecardService(context, attributionService, NullLogger<CarrierScorecardService>.Instance, PlatformReader(context));

        var startDate = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 11, 30, 23, 59, 59, DateTimeKind.Utc);

        // Case A: Exactly 1 record
        var singleRecord = CarrierSlaComplianceRecord.Create(
            shipmentId: Guid.NewGuid(),
            trackingNumber: "TRK-SINGLE-01",
            carrierCode: "USPS",
            serviceLevel: CarrierServiceLevel.StandardGround,
            facilityId: _facilityId,
            clientId: _clientId,
            originRegion: "US-EAST",
            destinationCountry: "US",
            destinationStateOrRegion: "NY",
            destinationPostalCodePrefix: "100",
            dispatchedAtUtc: startDate.AddDays(1),
            contractualTransitDays: 3,
            estimatedDeliveryDateUtc: startDate.AddDays(4),
            slaPolicyId: Guid.NewGuid(),
            slaPolicyVersion: "1.0.0");

        singleRecord.RecordDeliveryAttempt(startDate.AddDays(4), true);
        singleRecord.RecordDelivery(startDate.AddDays(4));
        singleRecord.IsOnTime = true;
        singleRecord.ActualTransitDays = 3.0m;
        singleRecord.TransitTimeVarianceDays = 0m;
        singleRecord.HasDeliveryException = false;
        singleRecord.QualifyingExceptionsCount = 0;

        context.CarrierSlaComplianceRecords.Add(singleRecord);
        await context.SaveChangesAsync();

        var singleDist = await scorecardService.GetTransitTimeDistributionAsync("USPS", startDate, endDate, _facilityId);
        Assert.NotNull(singleDist);
        Assert.Equal(1, singleDist.SampleSize);
        Assert.False(singleDist.IsStatisticallyReliable); // N < 30
        Assert.Null(singleDist.StandardDeviationDays); // N < 2 -> null

        // Case B: 15 records (N < 30)
        for (int i = 2; i <= 15; i++)
        {
            var r = CarrierSlaComplianceRecord.Create(
                shipmentId: Guid.NewGuid(),
                trackingNumber: $"TRK-SMALL-{i:D2}",
                carrierCode: "USPS",
                serviceLevel: CarrierServiceLevel.StandardGround,
                facilityId: _facilityId,
                clientId: _clientId,
                originRegion: "US-EAST",
                destinationCountry: "US",
                destinationStateOrRegion: "NY",
                destinationPostalCodePrefix: "100",
                dispatchedAtUtc: startDate.AddDays(1),
                contractualTransitDays: 3,
                estimatedDeliveryDateUtc: startDate.AddDays(4),
                slaPolicyId: Guid.NewGuid(),
                slaPolicyVersion: "1.0.0");

            r.RecordDeliveryAttempt(startDate.AddDays(4), true);
            r.RecordDelivery(startDate.AddDays(4));
            r.IsOnTime = true;
            r.ActualTransitDays = 3.0m + (i * 0.1m);
            r.TransitTimeVarianceDays = i * 0.1m;
            r.HasDeliveryException = false;
            r.QualifyingExceptionsCount = 0;

            context.CarrierSlaComplianceRecords.Add(r);
        }
        await context.SaveChangesAsync();

        var fifteenDist = await scorecardService.GetTransitTimeDistributionAsync("USPS", startDate, endDate, _facilityId);
        Assert.NotNull(fifteenDist);
        Assert.Equal(15, fifteenDist.SampleSize);
        Assert.False(fifteenDist.IsStatisticallyReliable); // 15 < 30
        Assert.NotNull(fifteenDist.StandardDeviationDays); // 15 >= 2 -> calculated
    }

    [Fact]
    public async Task Test_06_HistoricalSlaPolicy_VersionRetention()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        var attributionService = new DelayAttributionService(context, NullLogger<DelayAttributionService>.Instance);
        var scorecardService = new CarrierScorecardService(context, attributionService, NullLogger<CarrierScorecardService>.Instance, PlatformReader(context));

        var startDate = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 11, 10, 23, 59, 59, DateTimeKind.Utc);

        var policyVersions = new[] { "1.0.0", "1.1.0", "2.0.0" };

        foreach (var version in policyVersions)
        {
            var record = CarrierSlaComplianceRecord.Create(
                shipmentId: Guid.NewGuid(),
                trackingNumber: $"TRK-POL-{version}",
                carrierCode: "ONTRAC",
                serviceLevel: CarrierServiceLevel.StandardGround,
                facilityId: _facilityId,
                clientId: _clientId,
                originRegion: "US-WEST",
                destinationCountry: "US",
                destinationStateOrRegion: "WA",
                destinationPostalCodePrefix: "981",
                dispatchedAtUtc: startDate.AddDays(1),
                contractualTransitDays: 2,
                estimatedDeliveryDateUtc: startDate.AddDays(3),
                slaPolicyId: Guid.NewGuid(),
                slaPolicyVersion: version);

            record.RecordDeliveryAttempt(startDate.AddDays(3), true);
            record.RecordDelivery(startDate.AddDays(3));
            record.IsOnTime = true;
            record.ActualTransitDays = 2.0m;
            record.TransitTimeVarianceDays = 0m;
            record.HasDeliveryException = false;
            record.QualifyingExceptionsCount = 0;

            context.CarrierSlaComplianceRecords.Add(record);
        }

        await context.SaveChangesAsync();

        var scorecard = await scorecardService.GetCarrierScorecardAsync("ONTRAC", startDate, endDate, _facilityId);

        Assert.NotNull(scorecard);
        Assert.Equal(3, scorecard.SlaPolicyVersions.Count);
        Assert.Contains("1.0.0", scorecard.SlaPolicyVersions);
        Assert.Contains("1.1.0", scorecard.SlaPolicyVersions);
        Assert.Contains("2.0.0", scorecard.SlaPolicyVersions);
    }

    [Fact]
    public void Test_07_DelayAttribution_CategoryAndConfidence()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        var service = new DelayAttributionService(context, NullLogger<DelayAttributionService>.Instance);
        var shipmentId = Guid.NewGuid();

        // 1. Weather disruption -> ExternalDisruption (High, CarrierResponsible = false)
        var weatherAttr = service.AttributeException(
            shipmentId, "TRK-01", "FEDEX", CanonicalDeliveryExceptionCategory.WeatherDisruption, "WX01", "Blizzard");
        Assert.Equal(DelayAttributionCategory.ExternalDisruption, weatherAttr.AttributionCategory);
        Assert.Equal(AttributionConfidence.High, weatherAttr.Confidence);
        Assert.False(weatherAttr.IsCarrierResponsible);

        // 2. Carrier operational delay -> CarrierFault (High, CarrierResponsible = true)
        var carrierDelayAttr = service.AttributeException(
            shipmentId, "TRK-02", "FEDEX", CanonicalDeliveryExceptionCategory.CarrierOperationalDelay, "SORT01", "Sorting jam");
        Assert.Equal(DelayAttributionCategory.CarrierFault, carrierDelayAttr.AttributionCategory);
        Assert.Equal(AttributionConfidence.High, carrierDelayAttr.Confidence);
        Assert.True(carrierDelayAttr.IsCarrierResponsible);

        // 3. Damaged in transit -> CarrierFault (Medium confidence due to packaging inspection requirement, CarrierResponsible = true)
        var damageAttr = service.AttributeException(
            shipmentId, "TRK-03", "FEDEX", CanonicalDeliveryExceptionCategory.DamagedInTransit, "DMG01", "Crushed carton");
        Assert.Equal(DelayAttributionCategory.CarrierFault, damageAttr.AttributionCategory);
        Assert.Equal(AttributionConfidence.Medium, damageAttr.Confidence);
        Assert.True(damageAttr.IsCarrierResponsible);

        // 4. Address issue -> CustomerOrRecipient (High, CarrierResponsible = false)
        var addressAttr = service.AttributeException(
            shipmentId, "TRK-04", "FEDEX", CanonicalDeliveryExceptionCategory.AddressIssue, "ADDR01", "Invalid suite");
        Assert.Equal(DelayAttributionCategory.CustomerOrRecipient, addressAttr.AttributionCategory);
        Assert.Equal(AttributionConfidence.High, addressAttr.Confidence);
        Assert.False(addressAttr.IsCarrierResponsible);

        // 5. Customs hold -> RegulatoryOrCustoms (High, CarrierResponsible = false)
        var customsAttr = service.AttributeException(
            shipmentId, "TRK-05", "FEDEX", CanonicalDeliveryExceptionCategory.CustomsOrRegulatoryHold, "CUST01", "Border inspection");
        Assert.Equal(DelayAttributionCategory.RegulatoryOrCustoms, customsAttr.AttributionCategory);
        Assert.Equal(AttributionConfidence.High, customsAttr.Confidence);
        Assert.False(customsAttr.IsCarrierResponsible);

        // 6. Tendered after cutoff -> FacilityOrigin (Medium, CarrierResponsible = false)
        var cutoffAttr = service.AttributeException(
            shipmentId, "TRK-06", "FEDEX", CanonicalDeliveryExceptionCategory.CarrierOperationalDelay, "CUT01", "Late tender", isDispatchedAfterCutoff: true);
        Assert.Equal(DelayAttributionCategory.FacilityOrigin, cutoffAttr.AttributionCategory);
        Assert.Equal(AttributionConfidence.Medium, cutoffAttr.Confidence);
        Assert.False(cutoffAttr.IsCarrierResponsible);

        // 7. Other / Unclassified -> Unknown (None, CarrierResponsible = false)
        var unknownAttr = service.AttributeException(
            shipmentId, "TRK-07", "FEDEX", CanonicalDeliveryExceptionCategory.Other, "UNK99", "Miscellaneous scan");
        Assert.Equal(DelayAttributionCategory.Unknown, unknownAttr.AttributionCategory);
        Assert.Equal(AttributionConfidence.None, unknownAttr.Confidence);
        Assert.False(unknownAttr.IsCarrierResponsible);
    }

    [Fact]
    public async Task Test_08_ClaimsReadiness_EvidenceCompletenessScoring()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        var service = new ClaimsReadinessService(context, NullLogger<ClaimsReadinessService>.Instance, PlatformReader(context));

        var shipmentIdIncomplete = Guid.NewGuid();
        var shipmentIdComplete = Guid.NewGuid();

        // 1. Incomplete shipment: Has compliance record (handover) only, missing tracking, alert, cost, valuation
        var incCompliance = CarrierSlaComplianceRecord.Create(
            shipmentId: shipmentIdIncomplete,
            trackingNumber: "TRK-INC-01",
            carrierCode: "UPS",
            serviceLevel: CarrierServiceLevel.StandardGround,
            facilityId: _facilityId,
            clientId: _clientId,
            originRegion: "US-EAST",
            destinationCountry: "US",
            destinationStateOrRegion: "IL",
            destinationPostalCodePrefix: "606",
            dispatchedAtUtc: DateTime.UtcNow.AddDays(-10),
            contractualTransitDays: 3,
            estimatedDeliveryDateUtc: DateTime.UtcNow.AddDays(-7),
            slaPolicyId: Guid.NewGuid(),
            slaPolicyVersion: "1.0.0");
        context.CarrierSlaComplianceRecords.Add(incCompliance);

        // 2. Complete package shipment: Handover + Tracking scans + Lost Alert (breach) + Cost Record
        var compCompliance = CarrierSlaComplianceRecord.Create(
            shipmentId: shipmentIdComplete,
            trackingNumber: "TRK-COMP-01",
            carrierCode: "UPS",
            serviceLevel: CarrierServiceLevel.StandardGround,
            facilityId: _facilityId,
            clientId: _clientId,
            originRegion: "US-EAST",
            destinationCountry: "US",
            destinationStateOrRegion: "IL",
            destinationPostalCodePrefix: "606",
            dispatchedAtUtc: DateTime.UtcNow.AddDays(-10),
            contractualTransitDays: 3,
            estimatedDeliveryDateUtc: DateTime.UtcNow.AddDays(-7),
            slaPolicyId: Guid.NewGuid(),
            slaPolicyVersion: "1.0.0");
        context.CarrierSlaComplianceRecords.Add(compCompliance);

        // Add tracking scan
        var scan = new ShipmentTrackingEvent
        {
            Id = Guid.NewGuid(),
            ShipmentId = shipmentIdComplete,
            PackageId = Guid.NewGuid(),
            TrackingNumber = "TRK-COMP-01",
            CarrierCode = "UPS",
            FacilityId = _facilityId,
            ClientId = _clientId,
            MilestoneStatus = TrackingMilestoneStatus.InTransit,
            CarrierEventCode = "IT",
            CarrierDescription = "Arrived at origin sort facility",
            EventCity = "Chicago Hub",
            OccurredAtUtc = DateTime.UtcNow.AddDays(-9)
        };
        context.ShipmentTrackingEvents.Add(scan);

        // Add lost shipment alert with threshold breach
        var alert = CarrierLostShipmentAlert.Create(
            shipmentId: shipmentIdComplete,
            trackingNumber: "TRK-COMP-01",
            carrierCode: "UPS",
            serviceLevel: CarrierServiceLevel.StandardGround,
            facilityId: _facilityId,
            clientId: _clientId,
            dispatchedAtUtc: DateTime.UtcNow.AddDays(-10),
            expectedDeliveryDateUtc: DateTime.UtcNow.AddDays(-7),
            lastTrackingEventAtUtc: DateTime.UtcNow.AddDays(-9),
            lastKnownStatus: "InTransit",
            lastKnownLocation: "Chicago Hub",
            hoursWithoutScanMovement: 120.0m,
            policyThresholdHours: 72.0m,
            triggerReason: "No tracking movement for 120.0h exceeds policy threshold of 72.0h",
            slaPolicyVersion: "1.0.0");
        context.CarrierLostShipmentAlerts.Add(alert);

        // Freight and insurance charges are cost evidence, not a declared goods valuation.
        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientId,
            FacilityId = _facilityId,
            ShipmentId = shipmentIdComplete,
            PackageId = null,
            CarrierCode = "UPS",
            CarrierServiceLevel = CarrierServiceLevel.StandardGround,
            TrackingNumber = "TRK-COMP-01",
            CarrierInvoiceNumber = "INV-UPS-2026-999",
            BaseTransportationCost = 45.00m,
            FuelSurcharge = 5.50m,
            InsuranceCost = 15.00m,
            AccessorialCost = 0m,
            TotalCarrierCost = 65.50m,
            CostAssessedAtUtc = DateTime.UtcNow.AddDays(-10),
            Status = CarrierCostStatus.Invoiced
        };
        context.CarrierCostRecords.Add(cost);

        await context.SaveChangesAsync();

        // Evaluate incomplete shipment
        await SeedClaimShipmentsAsync(context);
        var incAssessment = await service.AssessShipmentClaimsReadinessAsync(shipmentIdIncomplete);
        Assert.NotNull(incAssessment);
        Assert.True(incAssessment.ReadinessScorePercent < 85.00m);
        Assert.Equal(ClaimsReadinessStatus.InsufficientEvidence, incAssessment.ReadinessStatus);
        Assert.NotEmpty(incAssessment.MissingEvidenceItems);

        // Operational evidence is complete, but no authoritative goods valuation is persisted.
        var compAssessment = await service.AssessShipmentClaimsReadinessAsync(shipmentIdComplete);
        Assert.NotNull(compAssessment);
        Assert.Equal(80m, compAssessment.ReadinessScorePercent);
        Assert.Equal(ClaimsReadinessStatus.InsufficientEvidence, compAssessment.ReadinessStatus);
        Assert.True(compAssessment.HasHandoverEvidence);
        Assert.True(compAssessment.HasTransitScanHistory);
        Assert.True(compAssessment.HasObstructionEvidence);
        Assert.True(compAssessment.HasThresholdBreach);
        Assert.True(compAssessment.HasFreightCostEvidence);
        Assert.False(compAssessment.HasValuationEvidence);
        Assert.Single(compAssessment.MissingEvidenceItems);
        Assert.Contains("valuation", compAssessment.MissingEvidenceItems[0]);
    }

    [Fact]
    public async Task Test_09_TenantIsolation_ClientConfinement()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        var attributionService = new DelayAttributionService(context, NullLogger<DelayAttributionService>.Instance);
        var scorecardService = new CarrierScorecardService(context, attributionService, NullLogger<CarrierScorecardService>.Instance, PlatformReader(context));
        var claimsService = new ClaimsReadinessService(context, NullLogger<ClaimsReadinessService>.Instance, PlatformReader(context));

        var startDate = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 11, 10, 23, 59, 59, DateTimeKind.Utc);

        // Seed Client A record
        var clientARecord = CarrierSlaComplianceRecord.Create(
            shipmentId: Guid.NewGuid(),
            trackingNumber: "TRK-CLIENT-A",
            carrierCode: "FEDEX",
            serviceLevel: CarrierServiceLevel.StandardGround,
            facilityId: _facilityId,
            clientId: _clientId,
            originRegion: "US-EAST",
            destinationCountry: "US",
            destinationStateOrRegion: "OH",
            destinationPostalCodePrefix: "432",
            dispatchedAtUtc: startDate.AddDays(1),
            contractualTransitDays: 2,
            estimatedDeliveryDateUtc: startDate.AddDays(3),
            slaPolicyId: Guid.NewGuid(),
            slaPolicyVersion: "1.0.0");
        clientARecord.RecordDeliveryAttempt(startDate.AddDays(3), true);
        clientARecord.RecordDelivery(startDate.AddDays(3));
        clientARecord.IsOnTime = true;
        clientARecord.ActualTransitDays = 2.0m;
        clientARecord.TransitTimeVarianceDays = 0m;
        clientARecord.HasDeliveryException = false;
        clientARecord.QualifyingExceptionsCount = 0;

        // Seed Client B record
        var clientBRecord = CarrierSlaComplianceRecord.Create(
            shipmentId: Guid.NewGuid(),
            trackingNumber: "TRK-CLIENT-B",
            carrierCode: "FEDEX",
            serviceLevel: CarrierServiceLevel.StandardGround,
            facilityId: _facilityId,
            clientId: _otherClientId,
            originRegion: "US-EAST",
            destinationCountry: "US",
            destinationStateOrRegion: "OH",
            destinationPostalCodePrefix: "432",
            dispatchedAtUtc: startDate.AddDays(1),
            contractualTransitDays: 2,
            estimatedDeliveryDateUtc: startDate.AddDays(3),
            slaPolicyId: Guid.NewGuid(),
            slaPolicyVersion: "1.0.0");
        clientBRecord.RecordDeliveryAttempt(startDate.AddDays(5), false);
        clientBRecord.RecordDelivery(startDate.AddDays(5));
        clientBRecord.AttemptCount = 2;
        clientBRecord.IsFirstAttemptSuccess = false;
        clientBRecord.IsOnTime = false;
        clientBRecord.ActualTransitDays = 4.0m;
        clientBRecord.TransitTimeVarianceDays = 2.0m;
        clientBRecord.HasDeliveryException = true;
        clientBRecord.QualifyingExceptionsCount = 1;

        // Seed Lost Alert for Client B only
        var alertB = CarrierLostShipmentAlert.Create(
            shipmentId: clientBRecord.ShipmentId,
            trackingNumber: "TRK-CLIENT-B",
            carrierCode: "FEDEX",
            serviceLevel: CarrierServiceLevel.StandardGround,
            facilityId: _facilityId,
            clientId: _otherClientId,
            dispatchedAtUtc: startDate.AddDays(1),
            expectedDeliveryDateUtc: startDate.AddDays(3),
            lastTrackingEventAtUtc: startDate.AddDays(2),
            lastKnownStatus: "InTransit",
            lastKnownLocation: "Columbus Hub",
            hoursWithoutScanMovement: 100m,
            policyThresholdHours: 72m,
            triggerReason: "Threshold breach",
            slaPolicyVersion: "1.0.0");

        context.CarrierSlaComplianceRecords.AddRange(clientARecord, clientBRecord);
        context.CarrierLostShipmentAlerts.Add(alertB);
        await context.SaveChangesAsync();

        // Query scorecard specifically for Client A
        var scorecardA = await scorecardService.GetCarrierScorecardAsync("FEDEX", startDate, endDate, _facilityId, _clientId);
        Assert.NotNull(scorecardA);
        Assert.Equal(1, scorecardA.TotalDispatchedCount);
        Assert.Equal(1, scorecardA.TotalDeliveredCount);
        Assert.Equal(100.00m, scorecardA.OnTimeDeliveryRatePercent); // 1/1 on-time
        Assert.Equal(0.00m, scorecardA.ExceptionRatePercent); // 0 exceptions

        // Query transit distribution specifically for Client A
        var distA = await scorecardService.GetTransitTimeDistributionAsync("FEDEX", startDate, endDate, _facilityId, _clientId);
        Assert.NotNull(distA);
        Assert.Equal(1, distA.SampleSize);
        Assert.Equal(2.0m, distA.MeanDays);

        // Query claims batch specifically for Client A
        await SeedClaimShipmentsAsync(context);
        var claimsBatchA = await claimsService.GetFacilityClaimsReadinessBatchAsync(_facilityId, _clientId);
        Assert.Empty(claimsBatchA); // Client A has 0 lost alerts
    }

    [Fact]
    public async Task Test_10_FacilityIsolation_LaneSummaries()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        var attributionService = new DelayAttributionService(context, NullLogger<DelayAttributionService>.Instance);
        var scorecardService = new CarrierScorecardService(context, attributionService, NullLogger<CarrierScorecardService>.Instance, PlatformReader(context));

        var startDate = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 11, 10, 23, 59, 59, DateTimeKind.Utc);

        // Facility 1: 5 shipments
        for (int i = 1; i <= 5; i++)
        {
            var r = CarrierSlaComplianceRecord.Create(
                shipmentId: Guid.NewGuid(),
                trackingNumber: $"TRK-FAC1-{i}",
                carrierCode: "UPS",
                serviceLevel: CarrierServiceLevel.StandardGround,
                facilityId: _facilityId,
                clientId: _clientId,
                originRegion: "US-EAST",
                destinationCountry: "US",
                destinationStateOrRegion: "PA",
                destinationPostalCodePrefix: "191",
                dispatchedAtUtc: startDate.AddDays(1),
                contractualTransitDays: 2,
                estimatedDeliveryDateUtc: startDate.AddDays(3),
                slaPolicyId: Guid.NewGuid(),
                slaPolicyVersion: "1.0.0");
            r.RecordDeliveryAttempt(startDate.AddDays(3), true);
            r.RecordDelivery(startDate.AddDays(3));
            r.IsOnTime = true;
            r.ActualTransitDays = 2.0m;
            r.TransitTimeVarianceDays = 0m;
            r.HasDeliveryException = false;
            r.QualifyingExceptionsCount = 0;
            context.CarrierSlaComplianceRecords.Add(r);
        }

        // Facility 2: 7 shipments
        for (int i = 1; i <= 7; i++)
        {
            var r = CarrierSlaComplianceRecord.Create(
                shipmentId: Guid.NewGuid(),
                trackingNumber: $"TRK-FAC2-{i}",
                carrierCode: "UPS",
                serviceLevel: CarrierServiceLevel.StandardGround,
                facilityId: _otherFacilityId,
                clientId: _clientId,
                originRegion: "US-WEST",
                destinationCountry: "US",
                destinationStateOrRegion: "PA",
                destinationPostalCodePrefix: "191",
                dispatchedAtUtc: startDate.AddDays(1),
                contractualTransitDays: 4,
                estimatedDeliveryDateUtc: startDate.AddDays(5),
                slaPolicyId: Guid.NewGuid(),
                slaPolicyVersion: "1.0.0");
            r.RecordDeliveryAttempt(startDate.AddDays(5), true);
            r.RecordDelivery(startDate.AddDays(5));
            r.IsOnTime = true;
            r.ActualTransitDays = 4.0m;
            r.TransitTimeVarianceDays = 0m;
            r.HasDeliveryException = false;
            r.QualifyingExceptionsCount = 0;
            context.CarrierSlaComplianceRecords.Add(r);
        }

        await context.SaveChangesAsync();

        var scorecardFac1 = await scorecardService.GetCarrierScorecardAsync("UPS", startDate, endDate, _facilityId);
        Assert.NotNull(scorecardFac1);
        Assert.Equal(5, scorecardFac1.TotalDispatchedCount);
        Assert.Equal(5, scorecardFac1.TotalDeliveredCount);
        Assert.Equal(2.0m, scorecardFac1.AverageTransitDays);

        var scorecardFac2 = await scorecardService.GetCarrierScorecardAsync("UPS", startDate, endDate, _otherFacilityId);
        Assert.NotNull(scorecardFac2);
        Assert.Equal(7, scorecardFac2.TotalDispatchedCount);
        Assert.Equal(7, scorecardFac2.TotalDeliveredCount);
        Assert.Equal(4.0m, scorecardFac2.AverageTransitDays);
    }

    [Fact]
    public async Task Test_11_OperationalInvariance_ZeroLedgerMutation()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        var attributionService = new DelayAttributionService(context, NullLogger<DelayAttributionService>.Instance);
        var scorecardService = new CarrierScorecardService(context, attributionService, NullLogger<CarrierScorecardService>.Instance, PlatformReader(context));
        var claimsService = new ClaimsReadinessService(context, NullLogger<ClaimsReadinessService>.Instance, PlatformReader(context));

        var shipmentId = Guid.NewGuid();

        // Seed operational records
        var facility = new Facility
        {
            FacilityCode = "WH-01",
            Name = "Main Warehouse",
            IsActive = true
        };
        context.Facilities.Add(facility);

        var client = new Client
        {
            AccountCode = "ACME",
            CompanyName = "ACME Corp",
            Status = ClientStatus.Active
        };
        context.Clients.Add(client);

        var shipment = new Shipment
        {
            Id = shipmentId,
            FacilityId = facility.Id,
            ClientId = client.Id,
            ShipmentNumber = "SHP-OP-01",
            CarrierCode = "FEDEX",
            CarrierServiceLevel = CarrierServiceLevel.StandardGround,
            Status = ShipmentStatus.Dispatched,
            DeliveryStatus = TrackingMilestoneStatus.InTransit,
            DispatchedAtUtc = DateTime.UtcNow.AddDays(-5)
        };
        context.Shipments.Add(shipment);

        var invoice = new Invoice
        {
            ClientId = client.Id,
            FacilityId = facility.Id,
            BillingPeriodId = Guid.NewGuid(),
            InvoiceNumber = "INV-2026-0001",
            InvoiceDate = DateTime.UtcNow.AddDays(-14),
            DueDate = DateTime.UtcNow.AddDays(14),
            Status = InvoiceStatus.Draft
        };
        context.Invoices.Add(invoice);

        var complianceRecord = CarrierSlaComplianceRecord.Create(
            shipmentId: shipment.Id,
            trackingNumber: "TRK-OP-01",
            carrierCode: "FEDEX",
            serviceLevel: CarrierServiceLevel.StandardGround,
            facilityId: facility.Id,
            clientId: client.Id,
            originRegion: "US-EAST",
            destinationCountry: "US",
            destinationStateOrRegion: "OH",
            destinationPostalCodePrefix: "432",
            dispatchedAtUtc: DateTime.UtcNow.AddDays(-5),
            contractualTransitDays: 2,
            estimatedDeliveryDateUtc: DateTime.UtcNow.AddDays(-3),
            slaPolicyId: Guid.NewGuid(),
            slaPolicyVersion: "1.0.0");
        complianceRecord.RecordDeliveryAttempt(DateTime.UtcNow.AddDays(-3), true);
        complianceRecord.RecordDelivery(DateTime.UtcNow.AddDays(-3));
        complianceRecord.IsOnTime = true;
        complianceRecord.ActualTransitDays = 2.0m;
        complianceRecord.TransitTimeVarianceDays = 0m;
        complianceRecord.HasDeliveryException = false;
        complianceRecord.QualifyingExceptionsCount = 0;
        context.CarrierSlaComplianceRecords.Add(complianceRecord);

        await context.SaveChangesAsync();

        // Capture initial row counts
        int initialShipments = await context.Shipments.CountAsync();
        int initialInvoices = await context.Invoices.CountAsync();
        int initialCompliance = await context.CarrierSlaComplianceRecords.CountAsync();

        // Run all analytics queries
        var startDate = DateTime.UtcNow.AddDays(-30);
        var endDate = DateTime.UtcNow;

        _ = await scorecardService.GetCarrierScorecardAsync("FEDEX", startDate, endDate, facility.Id, client.Id);
        _ = await scorecardService.GetTransitTimeDistributionAsync("FEDEX", startDate, endDate, facility.Id, client.Id);
        _ = await scorecardService.GetDeliveryExceptionIntelligenceAsync("FEDEX", startDate, endDate, facility.Id, client.Id);
        _ = await claimsService.AssessShipmentClaimsReadinessAsync(shipment.Id);
        _ = await claimsService.GetFacilityClaimsReadinessBatchAsync(facility.Id, client.Id);

        // Verify zero mutations, additions, or deletions across operational and financial tables
        Assert.Equal(initialShipments, await context.Shipments.CountAsync());
        Assert.Equal(initialInvoices, await context.Invoices.CountAsync());
        Assert.Equal(initialCompliance, await context.CarrierSlaComplianceRecords.CountAsync());

        // Verify Shipment state remains unchanged
        var refreshedShipment = await context.Shipments.FindAsync(shipment.Id);
        Assert.NotNull(refreshedShipment);
        Assert.Equal("SHP-OP-01", refreshedShipment.ShipmentNumber);
        Assert.Equal(ShipmentStatus.Dispatched, refreshedShipment.Status);
    }

    [Theory]
    [InlineData(39.0, ClaimsReadinessStatus.InsufficientEvidence)]
    [InlineData(40.0, ClaimsReadinessStatus.InsufficientEvidence)]
    [InlineData(49.0, ClaimsReadinessStatus.InsufficientEvidence)]
    [InlineData(50.0, ClaimsReadinessStatus.EvidenceCollecting)]
    [InlineData(79.0, ClaimsReadinessStatus.EvidenceCollecting)]
    [InlineData(80.0, ClaimsReadinessStatus.EvidenceCollecting)]
    [InlineData(84.0, ClaimsReadinessStatus.EvidenceCollecting)]
    [InlineData(85.0, ClaimsReadinessStatus.ReadyForReview)]
    [InlineData(100.0, ClaimsReadinessStatus.ReadyForReview)]
    public void Test_12_ClaimsReadiness_GranularCompletenessBoundaries(decimal scorePercent, ClaimsReadinessStatus expectedStatus)
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        var service = new ClaimsReadinessService(context, NullLogger<ClaimsReadinessService>.Instance, PlatformReader(context));

        // When evaluating completeness boundaries, assume all mandatory items and threshold breach are present
        // so that the boundary score itself determines the state transition.
        var assessment = service.EvaluateReadinessCriteria(
            shipmentId: Guid.NewGuid(),
            trackingNumber: "TRK-BOUND-01",
            carrierCode: "FEDEX",
            facilityId: _facilityId,
            clientId: _clientId,
            hasHandover: true,
            hasTracking: true,
            hasDisruption: true,
            hasThresholdBreach: true,
            hasFreightCost: true,
            hasValuation: true,
            hasPod: false,
            explicitScorePercent: scorePercent,
            claimType: ClaimType.Lost);

        Assert.Equal(scorePercent, assessment.ReadinessScorePercent);
        Assert.Equal(expectedStatus, assessment.ReadinessStatus);
    }

    [Fact]
    public void Test_13_ClaimsReadiness_HighScore_MandatoryEvidenceGate_BlocksReview()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        var service = new ClaimsReadinessService(context, NullLogger<ClaimsReadinessService>.Instance, PlatformReader(context));

        // Scenario: Synthetic high score (95.0% or 100.0%), but missing declared valuation
        var assessmentMissingValuation = service.EvaluateReadinessCriteria(
            shipmentId: Guid.NewGuid(),
            trackingNumber: "TRK-GATE-01",
            carrierCode: "UPS",
            facilityId: _facilityId,
            clientId: _clientId,
            hasHandover: true,
            hasTracking: true,
            hasDisruption: true,
            hasThresholdBreach: true,
            hasFreightCost: true,
            hasValuation: false, // Mandatory item absent!
            hasPod: false,
            explicitScorePercent: 95.0m,
            claimType: ClaimType.Lost);

        // Mandatory evidence gate must downgrade status to InsufficientEvidence
        Assert.Equal(ClaimsReadinessStatus.InsufficientEvidence, assessmentMissingValuation.ReadinessStatus);
        Assert.Contains(assessmentMissingValuation.MissingEvidenceItems, m => m.Contains("valuation"));
        Assert.Contains("Mandatory evidence gate breach", assessmentMissingValuation.ReviewEligibilityReason);

        // Scenario: Missing warehouse handover confirmation
        var assessmentMissingHandover = service.EvaluateReadinessCriteria(
            shipmentId: Guid.NewGuid(),
            trackingNumber: "TRK-GATE-02",
            carrierCode: "UPS",
            facilityId: _facilityId,
            clientId: _clientId,
            hasHandover: false, // Mandatory item absent!
            hasTracking: true,
            hasDisruption: true,
            hasThresholdBreach: true,
            hasFreightCost: true,
            hasValuation: true,
            hasPod: false,
            explicitScorePercent: 90.0m,
            claimType: ClaimType.Lost);

        Assert.Equal(ClaimsReadinessStatus.InsufficientEvidence, assessmentMissingHandover.ReadinessStatus);
        Assert.Contains(assessmentMissingHandover.MissingEvidenceItems, m => m.Contains("handover"));
    }

    [Fact]
    public void Test_14_ClaimsReadiness_MissingDisruptionRecord_BlocksReview()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        var service = new ClaimsReadinessService(context, NullLogger<ClaimsReadinessService>.Instance, PlatformReader(context));

        // Case A: Missing disruption evidence altogether (no exception, no alert) -> InsufficientEvidence
        var noDisruption = service.EvaluateReadinessCriteria(
            shipmentId: Guid.NewGuid(),
            trackingNumber: "TRK-DISR-01",
            carrierCode: "DHL",
            facilityId: _facilityId,
            clientId: _clientId,
            hasHandover: true,
            hasTracking: true,
            hasDisruption: false, // Mandatory disruption missing!
            hasThresholdBreach: false,
            hasFreightCost: true,
            hasValuation: true,
            hasPod: false,
            explicitScorePercent: 85.0m,
            claimType: ClaimType.Lost);

        Assert.Equal(ClaimsReadinessStatus.InsufficientEvidence, noDisruption.ReadinessStatus);
        Assert.Contains(noDisruption.MissingEvidenceItems, m => m.Contains("exception or tracking inactivity"));

        // Case B: Disruption exists, but tracking inactivity duration has NOT breached policy threshold -> EvidenceCollecting
        var noThresholdBreach = service.EvaluateReadinessCriteria(
            shipmentId: Guid.NewGuid(),
            trackingNumber: "TRK-DISR-02",
            carrierCode: "DHL",
            facilityId: _facilityId,
            clientId: _clientId,
            hasHandover: true,
            hasTracking: true,
            hasDisruption: true,
            hasThresholdBreach: false, // Threshold not breached yet!
            hasFreightCost: true,
            hasValuation: true,
            hasPod: false,
            explicitScorePercent: 85.0m,
            claimType: ClaimType.Lost);

        Assert.Equal(ClaimsReadinessStatus.EvidenceCollecting, noThresholdBreach.ReadinessStatus);
        Assert.Contains(noThresholdBreach.MissingEvidenceItems, m => m.Contains("threshold"));
        Assert.Contains("inactivity duration has not yet exceeded contractual threshold", noThresholdBreach.ReviewEligibilityReason);
    }

    [Fact]
    public void Test_15_ClaimsReadiness_ConditionalPod_LostVsDamaged()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        var service = new ClaimsReadinessService(context, NullLogger<ClaimsReadinessService>.Instance, PlatformReader(context));

        // 1. Lost Claim with clean POD: Proof of delivery contradicts lost parcel claim -> InsufficientEvidence
        var lostWithCleanPod = service.EvaluateReadinessCriteria(
            shipmentId: Guid.NewGuid(),
            trackingNumber: "TRK-POD-01",
            carrierCode: "FEDEX",
            facilityId: _facilityId,
            clientId: _clientId,
            hasHandover: true,
            hasTracking: true,
            hasDisruption: true,
            hasThresholdBreach: true,
            hasFreightCost: true,
            hasValuation: true,
            hasPod: true, // Clean POD contradicts lost claim!
            explicitScorePercent: 90.0m,
            claimType: ClaimType.Lost);

        Assert.Equal(ClaimsReadinessStatus.InsufficientEvidence, lostWithCleanPod.ReadinessStatus);
        Assert.Contains(lostWithCleanPod.MissingEvidenceItems, m => m.Contains("contradicting lost parcel claim"));
        Assert.Contains("clean Proof of Delivery exists", lostWithCleanPod.ReviewEligibilityReason);

        // 2. Lost Claim without POD, but threshold breached -> ReadyForReview
        var lostWithoutPod = service.EvaluateReadinessCriteria(
            shipmentId: Guid.NewGuid(),
            trackingNumber: "TRK-POD-02",
            carrierCode: "FEDEX",
            facilityId: _facilityId,
            clientId: _clientId,
            hasHandover: true,
            hasTracking: true,
            hasDisruption: true,
            hasThresholdBreach: true,
            hasFreightCost: true,
            hasValuation: true,
            hasPod: false, // Expected for lost claim
            explicitScorePercent: 95.0m,
            claimType: ClaimType.Lost);

        Assert.Equal(ClaimsReadinessStatus.ReadyForReview, lostWithoutPod.ReadinessStatus);

        // 3. Damaged Claim without damage proof / POD -> EvidenceCollecting
        var damageWithoutProof = service.EvaluateReadinessCriteria(
            shipmentId: Guid.NewGuid(),
            trackingNumber: "TRK-POD-03",
            carrierCode: "FEDEX",
            facilityId: _facilityId,
            clientId: _clientId,
            hasHandover: true,
            hasTracking: true,
            hasDisruption: false,
            hasThresholdBreach: false,
            hasFreightCost: true,
            hasValuation: true,
            hasPod: false,
            explicitScorePercent: 85.0m,
            claimType: ClaimType.Damaged);

        Assert.Equal(ClaimsReadinessStatus.InsufficientEvidence, damageWithoutProof.ReadinessStatus);

        // 4. Damaged Claim with POD damage notation and physical damage exception -> ReadyForReview
        var damageWithProof = service.EvaluateReadinessCriteria(
            shipmentId: Guid.NewGuid(),
            trackingNumber: "TRK-POD-04",
            carrierCode: "FEDEX",
            facilityId: _facilityId,
            clientId: _clientId,
            hasHandover: true,
            hasTracking: true,
            hasDisruption: true, // Physical damage exception
            hasThresholdBreach: false,
            hasFreightCost: true,
            hasValuation: true,
            hasPod: true, // POD confirms delivery with damage
            explicitScorePercent: 90.0m,
            claimType: ClaimType.Damaged);

        Assert.Equal(ClaimsReadinessStatus.ReadyForReview, damageWithProof.ReadinessStatus);
    }

    [Fact]
    public void Test_16_ClaimsReadiness_MissingValuationAndInvoice_AuditedInChecklist()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        var service = new ClaimsReadinessService(context, NullLogger<ClaimsReadinessService>.Instance, PlatformReader(context));

        var assessment = service.EvaluateReadinessCriteria(
            shipmentId: Guid.NewGuid(),
            trackingNumber: "TRK-NO-VAL-01",
            carrierCode: "USPS",
            facilityId: _facilityId,
            clientId: _clientId,
            hasHandover: true,
            hasTracking: true,
            hasDisruption: true,
            hasThresholdBreach: true,
            hasFreightCost: false, // Missing freight invoice
            hasValuation: false,   // Missing declared valuation
            hasPod: false,
            explicitScorePercent: null,
            claimType: ClaimType.Lost);

        // Standard weights: Handover (20) + Tracking (15) + Disruption (20) + ThresholdBreach (10) = 65%
        // Missing FreightCost (15) and Valuation (15)
        Assert.Equal(65.0m, assessment.ReadinessScorePercent);
        Assert.Equal(ClaimsReadinessStatus.InsufficientEvidence, assessment.ReadinessStatus);
        Assert.Contains(assessment.MissingEvidenceItems, m => m.Contains("invoice or buy-side freight cost"));
        Assert.Contains(assessment.MissingEvidenceItems, m => m.Contains("Declared goods replacement valuation"));
    }

    [Fact]
    public void Test_17_DelayAttribution_LostOrMissing_ExplicitMappingAndUnknownPreservation()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        var service = new DelayAttributionService(context, NullLogger<DelayAttributionService>.Instance);
        var shipmentId = Guid.NewGuid();

        // 1. Direct Canonical Mapping: LostOrMissing -> CarrierFault, Medium confidence, IsCarrierResponsible = true
        var directMapping = service.AttributeException(
            shipmentId, "TRK-LOST-01", "FEDEX", CanonicalDeliveryExceptionCategory.LostOrMissing, "LOST99", "Package lost in hub");
        Assert.Equal(DelayAttributionCategory.CarrierFault, directMapping.AttributionCategory);
        Assert.Equal(AttributionConfidence.Medium, directMapping.Confidence);
        Assert.True(directMapping.IsCarrierResponsible);

        // 2. Lost Alert without carrier network scans: custody transfer unproven -> Unknown, None confidence, IsCarrierResponsible = false
        var unprovenCustody = service.AttributeLostShipment(
            shipmentId, "TRK-LOST-02", "FEDEX", hasCarrierScanEvidence: false, isThresholdBreached: true);
        Assert.Equal(DelayAttributionCategory.Unknown, unprovenCustody.AttributionCategory);
        Assert.Equal(AttributionConfidence.None, unprovenCustody.Confidence);
        Assert.False(unprovenCustody.IsCarrierResponsible);
        Assert.Contains("Custody handoff is unverified", unprovenCustody.AttributionReason);

        // 3. Carrier has custody scan, but threshold is not yet breached -> Unknown, Low confidence, IsCarrierResponsible = false
        var thresholdNotBreached = service.AttributeLostShipment(
            shipmentId, "TRK-LOST-03", "FEDEX", hasCarrierScanEvidence: true, isThresholdBreached: false);
        Assert.Equal(DelayAttributionCategory.Unknown, thresholdNotBreached.AttributionCategory);
        Assert.Equal(AttributionConfidence.Low, thresholdNotBreached.Confidence);
        Assert.False(thresholdNotBreached.IsCarrierResponsible);
        Assert.Contains("threshold hours not yet breached", thresholdNotBreached.AttributionReason);

        // 4. Carrier has custody scan AND threshold is breached -> CarrierFault, Medium confidence, IsCarrierResponsible = true
        var verifiedLost = service.AttributeLostShipment(
            shipmentId, "TRK-LOST-04", "FEDEX", hasCarrierScanEvidence: true, isThresholdBreached: true);
        Assert.Equal(DelayAttributionCategory.CarrierFault, verifiedLost.AttributionCategory);
        Assert.Equal(AttributionConfidence.Medium, verifiedLost.Confidence);
        Assert.True(verifiedLost.IsCarrierResponsible);
    }

    [Fact]
    public async Task Test_18_TransitPercentile_ZeroBasedFormulaConvention_DocumentationAndAudit()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        var attributionService = new DelayAttributionService(context, NullLogger<DelayAttributionService>.Instance);
        var scorecardService = new CarrierScorecardService(context, attributionService, NullLogger<CarrierScorecardService>.Instance, PlatformReader(context));

        var startDate = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 11, 30, 23, 59, 59, DateTimeKind.Utc);

        // Seed 11 observations: 1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0, 10.0, 11.0
        // N = 11.
        // Zero-based index position formula: k_0 = (P / 100) * (N - 1)
        // 1-based index position formula:    k_1 = (P / 100) * (N - 1) + 1
        // For P50: k_0 = 0.50 * 10 = 5 -> value at index 5 is 6.0 (1-based index 6 is 6.0)
        // For P75: k_0 = 0.75 * 10 = 7.5 -> halfway between index 7 (8.0) and 8 (9.0) -> 8.5
        // For P90: k_0 = 0.90 * 10 = 9 -> value at index 9 is 10.0 (1-based index 10 is 10.0)
        // For P95: k_0 = 0.95 * 10 = 9.5 -> halfway between index 9 (10.0) and 10 (11.0) -> 10.5
        for (int i = 1; i <= 11; i++)
        {
            decimal duration = (decimal)i;
            var record = CarrierSlaComplianceRecord.Create(
                shipmentId: Guid.NewGuid(),
                trackingNumber: $"TRK-FORMULA-{i:D2}",
                carrierCode: "DHL",
                serviceLevel: CarrierServiceLevel.StandardGround,
                facilityId: _facilityId,
                clientId: _clientId,
                originRegion: "US-EAST",
                destinationCountry: "US",
                destinationStateOrRegion: "IL",
                destinationPostalCodePrefix: "606",
                dispatchedAtUtc: startDate.AddDays(1),
                contractualTransitDays: 3,
                estimatedDeliveryDateUtc: startDate.AddDays(4),
                slaPolicyId: Guid.NewGuid(),
                slaPolicyVersion: "1.0.0");

            var deliveredTime = startDate.AddDays(1).AddDays((double)duration);
            record.RecordDeliveryAttempt(deliveredTime, true);
            record.RecordDelivery(deliveredTime);
            record.IsOnTime = duration <= 3.0m;
            record.ActualTransitDays = duration;
            record.TransitTimeVarianceDays = duration - 3.0m;

            context.CarrierSlaComplianceRecords.Add(record);
        }

        await context.SaveChangesAsync();

        var dist = await scorecardService.GetTransitTimeDistributionAsync("DHL", startDate, endDate, _facilityId);

        Assert.NotNull(dist);
        Assert.Equal(11, dist.SampleSize);
        Assert.Equal(6.00m, dist.MedianDays);
        Assert.Equal(8.50m, dist.P75Days);
        Assert.Equal(10.00m, dist.P90Days);
        Assert.Equal(10.50m, dist.P95Days);
    }
}

