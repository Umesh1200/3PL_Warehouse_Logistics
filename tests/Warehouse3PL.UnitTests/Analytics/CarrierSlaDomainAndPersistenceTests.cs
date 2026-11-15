using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Events.Transportation;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.UnitTests.Common;

namespace Warehouse3PL.UnitTests.Analytics;

/// <summary>
/// Unit and persistence verification suite for Phase 6.4.1:
/// Transportation & Carrier SLA Domain Entities, Contractual Matrices, Mathematical Invariants & Event Contracts.
/// </summary>
public class CarrierSlaDomainAndPersistenceTests
{
    private readonly Guid _facilityId = Guid.NewGuid();
    private readonly Guid _clientId = Guid.NewGuid();

    [Fact]
    public void Test_01_SlaPolicy_VersionRetention_And_CutoffEvaluation()
    {
        // 1. Arrange Policy v1.0 (Effective Jan 1, 2026; 3 business days; 17:00 UTC cutoff)
        var policyV1 = CarrierContractualSlaPolicy.Create(
            carrierCode: "FEDEX",
            serviceLevel: CarrierServiceLevel.StandardGround,
            transitCommitmentDays: 3,
            slaPolicyVersion: "1.0.0",
            calculationType: TransitDayCalculationType.BusinessDays,
            clientId: _clientId,
            originFacilityId: _facilityId,
            dailyCutoffTime: new TimeSpan(17, 0, 0),
            cutoffTimeZone: "UTC",
            effectiveFromUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            effectiveToUtc: new DateTime(2026, 5, 31, 23, 59, 59, DateTimeKind.Utc));

        // 2. Dispatch before cutoff on Tuesday 10:00 UTC -> committed delivery is Friday 23:59:59 UTC
        var dispatchBeforeCutoff = new DateTime(2026, 2, 10, 10, 0, 0, DateTimeKind.Utc); // Tuesday
        var committedEddV1 = policyV1.CalculateCommittedDeliveryDate(dispatchBeforeCutoff);
        Assert.Equal(DayOfWeek.Friday, committedEddV1.DayOfWeek);
        Assert.Equal(new DateTime(2026, 2, 13, 23, 59, 59, DateTimeKind.Utc), committedEddV1);

        // 3. Create compliance evaluation record linking to Policy v1.0
        var shipmentId = Guid.NewGuid();
        var complianceRecord = CarrierSlaComplianceRecord.Create(
            shipmentId: shipmentId,
            trackingNumber: "TRK-FEDEX-001",
            carrierCode: "FEDEX",
            serviceLevel: CarrierServiceLevel.StandardGround,
            facilityId: _facilityId,
            clientId: _clientId,
            dispatchedAtUtc: dispatchBeforeCutoff,
            estimatedDeliveryDateUtc: committedEddV1,
            contractualTransitDays: policyV1.TransitCommitmentDays,
            slaPolicyVersion: policyV1.SlaPolicyVersion,
            slaPolicyId: policyV1.Id);

        // 4. Arrange Policy v2.0 deployed later (Effective June 1, 2026; 2 business days)
        var policyV2 = CarrierContractualSlaPolicy.Create(
            carrierCode: "FEDEX",
            serviceLevel: CarrierServiceLevel.StandardGround,
            transitCommitmentDays: 2,
            slaPolicyVersion: "2.0.0",
            calculationType: TransitDayCalculationType.BusinessDays,
            clientId: _clientId,
            originFacilityId: _facilityId,
            dailyCutoffTime: new TimeSpan(17, 0, 0),
            cutoffTimeZone: "UTC",
            effectiveFromUtc: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        // 5. Invariant Assertion: Historical record strictly preserves Policy v1.0 parameters
        Assert.Equal("1.0.0", complianceRecord.SlaPolicyVersion);
        Assert.Equal(3, complianceRecord.ContractualTransitDays);
        Assert.Equal(policyV1.Id, complianceRecord.SlaPolicyId);
        Assert.NotEqual(policyV2.SlaPolicyVersion, complianceRecord.SlaPolicyVersion);

        // 6. Cutoff rollover test: Dispatch after 17:00 UTC rolls start date to Wednesday -> lands on Monday
        var dispatchAfterCutoff = new DateTime(2026, 2, 10, 18, 30, 0, DateTimeKind.Utc); // Tuesday evening
        var committedEddRollover = policyV1.CalculateCommittedDeliveryDate(dispatchAfterCutoff);
        Assert.Equal(DayOfWeek.Monday, committedEddRollover.DayOfWeek);
        Assert.Equal(new DateTime(2026, 2, 16, 23, 59, 59, DateTimeKind.Utc), committedEddRollover);
    }

    [Fact]
    public void Test_02_MathematicalInvariants_BoundaryConditions_And_DayCalculation()
    {
        // --- 1. Business Days vs Calendar Days Calculation Invariant ---
        var policyBusiness = CarrierContractualSlaPolicy.Create(
            carrierCode: "UPS",
            serviceLevel: CarrierServiceLevel.SecondDayAir,
            transitCommitmentDays: 2,
            calculationType: TransitDayCalculationType.BusinessDays);

        var policyCalendar = CarrierContractualSlaPolicy.Create(
            carrierCode: "UPS",
            serviceLevel: CarrierServiceLevel.SecondDayAir,
            transitCommitmentDays: 2,
            calculationType: TransitDayCalculationType.CalendarDays);

        // Friday 14:00 UTC dispatch
        var fridayDispatch = new DateTime(2026, 3, 6, 14, 0, 0, DateTimeKind.Utc); // Friday
        var businessEdd = policyBusiness.CalculateCommittedDeliveryDate(fridayDispatch);
        var calendarEdd = policyCalendar.CalculateCommittedDeliveryDate(fridayDispatch);

        // Business days: skips Sat & Sun -> lands on Tuesday
        Assert.Equal(DayOfWeek.Tuesday, businessEdd.DayOfWeek);
        Assert.Equal(new DateTime(2026, 3, 10, 23, 59, 59, DateTimeKind.Utc), businessEdd);

        // Calendar days: includes weekend -> lands on Sunday
        Assert.Equal(DayOfWeek.Sunday, calendarEdd.DayOfWeek);
        Assert.Equal(new DateTime(2026, 3, 8, 23, 59, 59, DateTimeKind.Utc), calendarEdd);

        // --- 2. Exact EDD Parity Invariant ---
        var edd = new DateTime(2026, 3, 10, 23, 59, 59, DateTimeKind.Utc);
        var recordExact = CarrierSlaComplianceRecord.Create(
            Guid.NewGuid(), "TRK-001", "UPS", CarrierServiceLevel.SecondDayAir, _facilityId, _clientId,
            fridayDispatch, edd, 2, "1.0.0");
        recordExact.RecordDelivery(edd); // Exact timestamp delivery
        Assert.True(recordExact.IsOnTime, "Delivery exactly on EDD timestamp must be classified as On-Time.");

        var recordLate = CarrierSlaComplianceRecord.Create(
            Guid.NewGuid(), "TRK-002", "UPS", CarrierServiceLevel.SecondDayAir, _facilityId, _clientId,
            fridayDispatch, edd, 2, "1.0.0");
        recordLate.RecordDelivery(edd.AddSeconds(1)); // 1 second late
        Assert.False(recordLate.IsOnTime, "Delivery after EDD timestamp must be classified as Late.");

        // --- 3. Zero Denominator Rate Protection (Nullable Rate Invariant) ---
        var dailyRecord = DailyCarrierPerformanceRecord.Create("UPS", CarrierServiceLevel.SecondDayAir, _facilityId, DateTime.UtcNow.Date, TimeGrain.Daily, _clientId);
        Assert.Null(dailyRecord.OnTimeDeliveryRatePercent);
        Assert.Null(dailyRecord.FirstAttemptDeliveryRatePercent);
        Assert.Null(dailyRecord.DeliveryExceptionRatePercent);
        Assert.Null(dailyRecord.AverageTransitDays);

        // --- 4. Undelivered Shipment Exclusion from OTDR Denominator ---
        dailyRecord.RecordDispatch(5); // 5 shipments dispatched, 0 delivered yet
        Assert.Equal(5, dailyRecord.DispatchedShipmentsCount);
        Assert.Equal(5, dailyRecord.InTransitShipmentsCount);
        Assert.Equal(0, dailyRecord.DeliveredShipmentsCount);
        // Rate remains null because no shipments have been delivered yet (in-transit are excluded from OTDR denominator)
        Assert.Null(dailyRecord.OnTimeDeliveryRatePercent);

        // Deliver 2 shipments (1 on time, 1 late)
        dailyRecord.RecordDelivery(isOnTime: true, isFirstAttempt: true, transitDays: 2.0m);
        dailyRecord.RecordDelivery(isOnTime: false, isFirstAttempt: false, transitDays: 4.0m);

        Assert.Equal(2, dailyRecord.DeliveredShipmentsCount);
        Assert.Equal(3, dailyRecord.InTransitShipmentsCount);
        Assert.Equal(50.00m, dailyRecord.OnTimeDeliveryRatePercent); // 1 / 2 * 100
        Assert.Equal(50.00m, dailyRecord.FirstAttemptDeliveryRatePercent); // 1 / 2 * 100
        Assert.Equal(3.00m, dailyRecord.AverageTransitDays); // (2 + 4) / 2
    }

    [Fact]
    public void Test_03_DeliveryAttempt_Immutability()
    {
        var record = CarrierSlaComplianceRecord.Create(
            Guid.NewGuid(), "TRK-ATTEMPT-01", "DHL", CarrierServiceLevel.NextDayAir, _facilityId, _clientId,
            DateTime.UtcNow.AddDays(-2), DateTime.UtcNow, 1, "1.0.0");

        // Attempt 1: Failed delivery (e.g. Recipient Unavailable)
        var attempt1Time = DateTime.UtcNow.AddHours(-10);
        record.RecordDeliveryAttempt(attempt1Time, wasSuccessful: false);

        Assert.Equal(1, record.AttemptCount);
        Assert.False(record.IsFirstAttemptSuccess);
        Assert.Equal(attempt1Time, record.FirstAttemptAtUtc);

        // Attempt 2: Reattempt succeeds
        var attempt2Time = DateTime.UtcNow.AddHours(-2);
        record.RecordDeliveryAttempt(attempt2Time, wasSuccessful: true);

        // Invariant Assertion: AttemptCount is 2, but FirstAttempt metadata remains immutable
        Assert.Equal(2, record.AttemptCount);
        Assert.False(record.IsFirstAttemptSuccess, "Reattempt success must NEVER overwrite first-attempt failure outcome.");
        Assert.Equal(attempt1Time, record.FirstAttemptAtUtc);
    }

    [Fact]
    public void Test_04_DataQualityFlags_And_IndeterminateStates()
    {
        // Shipment created without contractual EDD (data missing from carrier manifest)
        var record = CarrierSlaComplianceRecord.Create(
            shipmentId: Guid.NewGuid(),
            trackingNumber: "TRK-NO-EDD",
            carrierCode: "USPS",
            serviceLevel: CarrierServiceLevel.StandardGround,
            facilityId: _facilityId,
            clientId: _clientId,
            dispatchedAtUtc: DateTime.UtcNow.AddDays(-3),
            estimatedDeliveryDateUtc: null, // Missing EDD
            contractualTransitDays: 3,
            slaPolicyVersion: "Default");

        Assert.Equal(SlaEvaluationStatus.IndeterminateDueToMissingEdd, record.EvaluationStatus);
        Assert.Null(record.IsOnTime);

        // When delivered, on-time status cannot be deterministically evaluated
        record.RecordDelivery(DateTime.UtcNow);

        Assert.Null(record.IsOnTime);
        Assert.Equal(SlaEvaluationStatus.IndeterminateDueToMissingEdd, record.EvaluationStatus);
        Assert.Contains("Missing Estimated Delivery Date", record.DataQualityNotes);
    }

    [Fact]
    public void Test_05_FreightSpendReconciliation_Variance_And_AccessorialDensity()
    {
        var costSummary = CarrierFreightCostSummary.Create(
            carrierCode: "FEDEX",
            facilityId: _facilityId,
            dateUtc: DateTime.UtcNow.Date,
            timeGrain: TimeGrain.Daily,
            currency: "USD",
            clientId: _clientId);

        // 1. Initial Rate Quote: Base $100.00, Accessorials $20.00 -> Total Quoted $120.00
        costSummary.RecordQuote(baseFreight: 100.00m, accessorials: 20.00m);
        Assert.Equal(1, costSummary.TotalShipmentsQuotedCount);
        Assert.Equal(100.00m, costSummary.TotalQuotedBaseFreightUsd);
        Assert.Equal(20.00m, costSummary.TotalQuotedAccessorialsUsd);
        Assert.Equal(120.00m, costSummary.TotalQuotedAmountUsd);

        // 2. Dispatch Manifest Estimate: Total $125.00 -> Estimated Variance = +$5.00
        costSummary.RecordEstimate(estimatedTotal: 125.00m);
        Assert.Equal(125.00m, costSummary.TotalEstimatedAmountUsd);
        Assert.Equal(5.00m, costSummary.EstimatedVarianceUsd);

        // 3. Authoritative Carrier Invoice: Base $110.00, Accessorials $35.00 (Fuel $15, Res $10, Lift $10) -> Total $145.00
        costSummary.RecordBilledCharge(
            baseFreight: 110.00m,
            accessorials: 35.00m,
            fuelSurcharge: 15.00m,
            residential: 10.00m,
            liftgate: 10.00m);

        Assert.Equal(1, costSummary.TotalShipmentsBilledCount);
        Assert.Equal(110.00m, costSummary.TotalActualBaseFreightUsd);
        Assert.Equal(35.00m, costSummary.TotalActualAccessorialsUsd);
        Assert.Equal(145.00m, costSummary.TotalBilledAmountUsd);
        Assert.Equal(25.00m, costSummary.BilledVarianceUsd); // $145.00 - $120.00 = +$25.00
        Assert.Equal(35.00m, costSummary.AccessorialDensityRatio); // $35.00 / 1 billed = $35.00

        // 4. Invoice Reconciliation Audit
        costSummary.RecordReconciliation(reconciledAmount: 145.00m);
        Assert.Equal(1, costSummary.TotalShipmentsReconciledCount);
        Assert.Equal(25.00m, costSummary.ReconciledVarianceUsd);
    }

    [Fact]
    public async Task Test_06_PersistenceAndMapping_AllEntities_InMemoryDbContext()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        var dateUtc = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);

        // 1. Policy
        var policy = CarrierContractualSlaPolicy.Create(
            "UPS", CarrierServiceLevel.StandardGround, 3, "v1.2.0",
            TransitDayCalculationType.BusinessDays, _clientId, _facilityId);
        context.CarrierContractualSlaPolicies.Add(policy);

        // 2. Compliance Record
        var shipmentId = Guid.NewGuid();
        var compliance = CarrierSlaComplianceRecord.Create(
            shipmentId, "1Z9999999999999999", "UPS", CarrierServiceLevel.StandardGround,
            _facilityId, _clientId, dateUtc, dateUtc.AddDays(3), 3, "v1.2.0", policy.Id);
        context.CarrierSlaComplianceRecords.Add(compliance);

        // 3. Lane Transit Summary
        var lane = LaneTransitSummary.Create(
            _facilityId, "US", "IL", "UPS", CarrierServiceLevel.StandardGround, dateUtc, TimeGrain.Daily, originRegion: "MIDWEST");
        lane.RecordDelivery(2.5m, isOnTime: true);
        context.LaneTransitSummaries.Add(lane);

        // 4. Exception Summary
        var exSummary = CarrierExceptionSummary.Create(
            "UPS", _facilityId, CanonicalDeliveryExceptionCategory.WeatherDisruption, dateUtc, TimeGrain.Daily, _clientId);
        exSummary.RecordException(isPending: true);
        exSummary.RecordResolution(wasDelivered: true, wasReturned: false);
        context.CarrierExceptionSummaries.Add(exSummary);

        // 5. Freight Cost Summary
        var freight = CarrierFreightCostSummary.Create(
            "UPS", _facilityId, dateUtc, TimeGrain.Daily, "USD", _clientId);
        freight.RecordQuote(50m, 10m);
        context.CarrierFreightCostSummaries.Add(freight);

        // 6. Lost Shipment Alert
        var lostAlert = CarrierLostShipmentAlert.Create(
            shipmentId, "1Z9999999999999999", "UPS", CarrierServiceLevel.StandardGround,
            _facilityId, _clientId, dateUtc, dateUtc.AddDays(3), dateUtc.AddDays(1),
            "InTransit", "Chicago IL Hub", 72.5m, 48.0m, "No tracking scan update in 72 hours", "v1.2.0");
        context.CarrierLostShipmentAlerts.Add(lostAlert);

        // 7. Daily Carrier Performance Record
        var dailyPerf = DailyCarrierPerformanceRecord.Create(
            "UPS", CarrierServiceLevel.StandardGround, _facilityId, dateUtc, TimeGrain.Daily, _clientId);
        dailyPerf.RecordDispatch(1);
        dailyPerf.RecordDelivery(isOnTime: true, isFirstAttempt: true, transitDays: 2.5m);
        context.DailyCarrierPerformanceRecords.Add(dailyPerf);

        await context.SaveChangesAsync();
        context.ClearChangeTracker();

        // Verification Queries
        var loadedPolicy = await context.CarrierContractualSlaPolicies.FindAsync(policy.Id);
        Assert.NotNull(loadedPolicy);
        Assert.Equal("UPS", loadedPolicy.CarrierCode);
        Assert.Equal("v1.2.0", loadedPolicy.SlaPolicyVersion);

        var loadedCompliance = await context.CarrierSlaComplianceRecords.FirstOrDefaultAsync(r => r.ShipmentId == shipmentId);
        Assert.NotNull(loadedCompliance);
        Assert.Equal("1Z9999999999999999", loadedCompliance.TrackingNumber);
        Assert.Equal(policy.Id, loadedCompliance.SlaPolicyId);

        var loadedLane = await context.LaneTransitSummaries.FindAsync(lane.Id);
        Assert.NotNull(loadedLane);
        Assert.Equal(1, loadedLane.DeliveredShipmentsCount);
        Assert.Equal(100.00m, loadedLane.OnTimeDeliveryRatePercent);

        var loadedEx = await context.CarrierExceptionSummaries.FindAsync(exSummary.Id);
        Assert.NotNull(loadedEx);
        Assert.Equal(CanonicalDeliveryExceptionCategory.WeatherDisruption, loadedEx.ExceptionCategory);
        Assert.Equal(1, loadedEx.TotalExceptionsCount);
        Assert.Equal(1, loadedEx.ResolvedDeliveredCount);

        var loadedFreight = await context.CarrierFreightCostSummaries.FindAsync(freight.Id);
        Assert.NotNull(loadedFreight);
        Assert.Equal(60.00m, loadedFreight.TotalQuotedAmountUsd);

        var loadedAlert = await context.CarrierLostShipmentAlerts.FirstOrDefaultAsync(a => a.ShipmentId == shipmentId);
        Assert.NotNull(loadedAlert);
        Assert.Equal(LostShipmentAlertStatus.Active, loadedAlert.AlertStatus);
        Assert.Equal(72.5m, loadedAlert.HoursWithoutScanMovement);

        var loadedDaily = await context.DailyCarrierPerformanceRecords.FindAsync(dailyPerf.Id);
        Assert.NotNull(loadedDaily);
        Assert.Equal(1, loadedDaily.DeliveredShipmentsCount);
        Assert.Equal(100.00m, loadedDaily.OnTimeDeliveryRatePercent);
    }

    [Fact]
    public void Test_07_EventContracts_PayloadIntegrity_And_MonotonicSequencing()
    {
        var shipmentId = Guid.NewGuid();
        var dispatchedEvent = new ShipmentDispatchedEvent
        {
            FacilityId = _facilityId,
            ClientId = _clientId,
            ShipmentId = shipmentId,
            TrackingNumber = "TRK-EV-01",
            CarrierCode = "DHL",
            CarrierServiceLevel = CarrierServiceLevel.NextDayAir,
            DispatchedAtUtc = DateTime.UtcNow,
            InitialEstimatedDeliveryDateUtc = DateTime.UtcNow.AddDays(1),
            SequenceNumber = 101
        };

        var trackingEvent = new ShipmentTrackingUpdatedEvent
        {
            FacilityId = _facilityId,
            ClientId = _clientId,
            ShipmentId = shipmentId,
            TrackingNumber = "TRK-EV-01",
            CarrierCode = "DHL",
            MilestoneUtc = DateTime.UtcNow,
            CarrierStatusCode = "IN_TRANSIT",
            CarrierRawDescription = "Departed Sort Facility",
            LocationCity = "Cincinnati",
            LocationState = "OH",
            LocationCountry = "US",
            SequenceNumber = 102
        };

        var attemptEvent = new DeliveryAttemptRecordedEvent
        {
            FacilityId = _facilityId,
            ClientId = _clientId,
            ShipmentId = shipmentId,
            TrackingNumber = "TRK-EV-01",
            CarrierCode = "DHL",
            AttemptNumber = 1,
            AttemptTimestampUtc = DateTime.UtcNow,
            WasSuccessful = false,
            CarrierReasonCode = "BUSINESS_CLOSED",
            SequenceNumber = 103
        };

        var deliveredEvent = new ShipmentDeliveredEvent
        {
            FacilityId = _facilityId,
            ClientId = _clientId,
            ShipmentId = shipmentId,
            TrackingNumber = "TRK-EV-01",
            CarrierCode = "DHL",
            DeliveredAtUtc = DateTime.UtcNow,
            RecipientSignatureName = "J. Smith",
            DeliveryLocationType = "Front Desk",
            SequenceNumber = 104
        };

        var invoiceEvent = new CarrierInvoiceReconciledEvent
        {
            FacilityId = _facilityId,
            ClientId = _clientId,
            ShipmentId = shipmentId,
            TrackingNumber = "TRK-EV-01",
            CarrierCode = "DHL",
            InvoiceNumber = "INV-DHL-2026-99",
            BilledBaseFreightUsd = 45.00m,
            BilledAccessorialsUsd = 12.50m,
            FuelSurchargeUsd = 8.50m,
            ResidentialSurchargeUsd = 4.00m,
            LiftgateFeeUsd = 0m,
            AddressCorrectionFeeUsd = 0m,
            OtherAdjustmentsUsd = 0m,
            ReconciliationStatus = FreightVarianceReconciliationStatus.Reconciled,
            SequenceNumber = 105
        };

        // Assert contract conformance and monotonic sequencing
        Assert.True(dispatchedEvent is ITransportationAnalyticsEvent);
        Assert.True(trackingEvent is ITransportationAnalyticsEvent);
        Assert.True(attemptEvent is ITransportationAnalyticsEvent);
        Assert.True(deliveredEvent is ITransportationAnalyticsEvent);
        Assert.True(invoiceEvent is ITransportationAnalyticsEvent);

        Assert.True(dispatchedEvent.SequenceNumber < trackingEvent.SequenceNumber);
        Assert.True(trackingEvent.SequenceNumber < attemptEvent.SequenceNumber);
        Assert.True(attemptEvent.SequenceNumber < deliveredEvent.SequenceNumber);
        Assert.True(deliveredEvent.SequenceNumber < invoiceEvent.SequenceNumber);

        Assert.Equal("INV-DHL-2026-99", invoiceEvent.InvoiceNumber);
        Assert.Equal(FreightVarianceReconciliationStatus.Reconciled, invoiceEvent.ReconciliationStatus);
    }

    [Fact]
    public void Test_08_LostShipmentAlert_AdvisoryLifecycle()
    {
        var shipmentId = Guid.NewGuid();
        var alert = CarrierLostShipmentAlert.Create(
            shipmentId: shipmentId,
            trackingNumber: "TRK-LOST-01",
            carrierCode: "FEDEX",
            serviceLevel: CarrierServiceLevel.StandardGround,
            facilityId: _facilityId,
            clientId: _clientId,
            dispatchedAtUtc: DateTime.UtcNow.AddDays(-5),
            expectedDeliveryDateUtc: DateTime.UtcNow.AddDays(-2),
            lastTrackingEventAtUtc: DateTime.UtcNow.AddDays(-4),
            lastKnownStatus: "InTransit",
            lastKnownLocation: "Memphis Hub",
            hoursWithoutScanMovement: 96.0m,
            policyThresholdHours: 48.0m,
            triggerReason: "Exceeded 48 hours without scan progression");

        Assert.Equal(LostShipmentAlertStatus.Active, alert.AlertStatus);
        Assert.Equal(96.0m, alert.HoursWithoutScanMovement);

        // Advisor initiates carrier inquiry/claim
        alert.MarkClaimInitiated("CLM-FEDEX-2026-0042");
        Assert.Equal(LostShipmentAlertStatus.ClaimInitiated, alert.AlertStatus);
        Assert.Contains("CLM-FEDEX-2026-0042", alert.ResolutionNotes);

        // Subsequent scan finds parcel and delivers
        alert.MarkResolvedDelivered("Delivered to customer after delay");
        Assert.Equal(LostShipmentAlertStatus.ResolvedDelivered, alert.AlertStatus);
        Assert.Equal("Delivered to customer after delay", alert.ResolutionNotes);
    }
}
