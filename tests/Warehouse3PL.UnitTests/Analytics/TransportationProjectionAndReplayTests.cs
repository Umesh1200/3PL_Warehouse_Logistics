using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Events.Transportation;
using Warehouse3PL.Infrastructure.Services.Analytics;
using Warehouse3PL.UnitTests.Common;

namespace Warehouse3PL.UnitTests.Analytics;

/// <summary>
/// Unit and integration verification suite for Phase 6.4.2:
/// Transportation Projection Workers, Stream-Partitioned Checkpoints, Deduplication &amp; Idempotent Replay Pipeline.
/// </summary>
public class TransportationProjectionAndReplayTests
{
    private readonly Guid _facilityId = Guid.NewGuid();
    private readonly Guid _otherFacilityId = Guid.NewGuid();
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Guid _otherClientId = Guid.NewGuid();

    [Fact]
    public async Task Test_01_DuplicateDelivery_Idempotency()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        var service = new TransportationProjectionService(context, NullLogger<TransportationProjectionService>.Instance);
        var eventId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        var dateUtc = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc);

        var dispatchEvent = new ShipmentDispatchedEvent
        {
            EventId = eventId,
            FacilityId = _facilityId,
            ClientId = _clientId,
            ShipmentId = shipmentId,
            TrackingNumber = "TRK-DUP-01",
            CarrierCode = "FEDEX",
            CarrierServiceLevel = CarrierServiceLevel.StandardGround,
            DispatchedAtUtc = dateUtc,
            InitialEstimatedDeliveryDateUtc = dateUtc.AddDays(3),
            SequenceNumber = 101,
            SourceStreamId = $"Tracking:FEDEX:{_facilityId}"
        };

        // First delivery: processes successfully
        await service.ProcessShipmentDispatchedAsync(dispatchEvent);

        var dailyPerf = await context.DailyCarrierPerformanceRecords.FirstOrDefaultAsync();
        Assert.NotNull(dailyPerf);
        Assert.Equal(1, dailyPerf.DispatchedShipmentsCount);

        var dedupCount = await context.AnalyticsEventDeduplicationRecords.CountAsync();
        Assert.Equal(1, dedupCount);

        // Second delivery (duplicate retry with identical EventId)
        await service.ProcessShipmentDispatchedAsync(dispatchEvent);

        // Invariant: Zero duplicate mutation, count remains exactly 1
        dailyPerf = await context.DailyCarrierPerformanceRecords.FirstOrDefaultAsync();
        Assert.NotNull(dailyPerf);
        Assert.Equal(1, dailyPerf.DispatchedShipmentsCount);

        dedupCount = await context.AnalyticsEventDeduplicationRecords.CountAsync();
        Assert.Equal(1, dedupCount);
    }

    [Fact]
    public async Task Test_02_StreamIsolation_IndependentCheckpoints()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        var service = new TransportationProjectionService(context, NullLogger<TransportationProjectionService>.Instance);

        var trackingStream = $"Tracking:FEDEX:{_facilityId}";
        var invoiceStream = $"Invoice:FEDEX:{_facilityId}";

        // 1. Process Tracking Event
        var trackingEvent = new ShipmentDispatchedEvent
        {
            EventId = Guid.NewGuid(),
            FacilityId = _facilityId,
            ClientId = _clientId,
            ShipmentId = Guid.NewGuid(),
            TrackingNumber = "TRK-STREAM-01",
            CarrierCode = "FEDEX",
            CarrierServiceLevel = CarrierServiceLevel.StandardGround,
            DispatchedAtUtc = DateTime.UtcNow,
            SequenceNumber = 150,
            SourceStreamId = trackingStream
        };
        await service.ProcessShipmentDispatchedAsync(trackingEvent);

        // 2. Process Invoice Event
        var invoiceEvent = new CarrierInvoiceReconciledEvent
        {
            EventId = Guid.NewGuid(),
            FacilityId = _facilityId,
            ClientId = _clientId,
            ShipmentId = Guid.NewGuid(),
            TrackingNumber = "TRK-STREAM-01",
            CarrierCode = "FEDEX",
            InvoiceNumber = "INV-FEDEX-001",
            BilledBaseFreightUsd = 50m,
            SequenceNumber = 900,
            SourceStreamId = invoiceStream
        };
        await service.ProcessCarrierInvoiceReconciledAsync(invoiceEvent);

        // Assert independent checkpoints
        var trackingCheckpoint = await service.GetOrCreateStreamCheckpointAsync(
            TransportationProjectionService.SlaComplianceProjection, trackingStream);
        Assert.Equal(150, trackingCheckpoint.LastProcessedSequenceNumber);
        Assert.Equal(trackingStream, trackingCheckpoint.SourceStreamId);

        var invoiceCheckpoint = await service.GetOrCreateStreamCheckpointAsync(
            TransportationProjectionService.CarrierFreightCostProjection, invoiceStream);
        Assert.Equal(900, invoiceCheckpoint.LastProcessedSequenceNumber);
        Assert.Equal(invoiceStream, invoiceCheckpoint.SourceStreamId);

        // Invariant: Streams advance independently without interfering
        Assert.NotEqual(trackingCheckpoint.LastProcessedSequenceNumber, invoiceCheckpoint.LastProcessedSequenceNumber);
    }

    [Fact]
    public async Task Test_03_SequenceOrdering_OutOfOrderHandling()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        var service = new TransportationProjectionService(context, NullLogger<TransportationProjectionService>.Instance);
        var shipmentId = Guid.NewGuid();
        var dispatchDate = new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);
        var deliveryDate = new DateTime(2026, 11, 12, 14, 0, 0, DateTimeKind.Utc);

        // 1. Initial Dispatch
        await service.ProcessShipmentDispatchedAsync(new ShipmentDispatchedEvent
        {
            EventId = Guid.NewGuid(),
            FacilityId = _facilityId,
            ClientId = _clientId,
            ShipmentId = shipmentId,
            TrackingNumber = "TRK-ORD-01",
            CarrierCode = "UPS",
            CarrierServiceLevel = CarrierServiceLevel.StandardGround,
            DispatchedAtUtc = dispatchDate,
            InitialEstimatedDeliveryDateUtc = dispatchDate.AddDays(3),
            SequenceNumber = 10
        });

        // 2. Final Delivery arrives with Sequence 30
        await service.ProcessShipmentDeliveredAsync(new ShipmentDeliveredEvent
        {
            EventId = Guid.NewGuid(),
            FacilityId = _facilityId,
            ClientId = _clientId,
            ShipmentId = shipmentId,
            TrackingNumber = "TRK-ORD-01",
            CarrierCode = "UPS",
            DeliveredAtUtc = deliveryDate,
            SequenceNumber = 30
        });

        var compliance = await context.CarrierSlaComplianceRecords.FirstOrDefaultAsync(r => r.ShipmentId == shipmentId);
        Assert.NotNull(compliance);
        Assert.Equal(deliveryDate, compliance.DeliveredAtUtc);
        Assert.True(compliance.IsOnTime);

        // 3. Late intermediate tracking scan arrives with earlier Sequence 20
        await service.ProcessShipmentTrackingUpdatedAsync(new ShipmentTrackingUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            FacilityId = _facilityId,
            ClientId = _clientId,
            ShipmentId = shipmentId,
            TrackingNumber = "TRK-ORD-01",
            CarrierCode = "UPS",
            MilestoneUtc = dispatchDate.AddDays(1),
            CarrierStatusCode = "IN_TRANSIT",
            SequenceNumber = 20
        });

        // Invariant: Late event does not regress delivery status or overwrite completed outcome
        compliance = await context.CarrierSlaComplianceRecords.FirstOrDefaultAsync(r => r.ShipmentId == shipmentId);
        Assert.NotNull(compliance);
        Assert.Equal(deliveryDate, compliance.DeliveredAtUtc);
        Assert.True(compliance.IsOnTime);
    }

    [Fact]
    public async Task Test_04_CrashRecovery_AtomicRollback()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        var service = new TransportationProjectionService(context, NullLogger<TransportationProjectionService>.Instance);
        var streamId = $"Tracking:DHL:{_facilityId}";

        // Initialize checkpoint
        var checkpoint = await service.GetOrCreateStreamCheckpointAsync(
            TransportationProjectionService.SlaComplianceProjection, streamId);
        Assert.Equal(0, checkpoint.LastProcessedSequenceNumber);

        // Attempt invalid dispatch with negative sequence or cancellation
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Simulate mid-flight crash / cancellation

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ProcessShipmentDispatchedAsync(new ShipmentDispatchedEvent
            {
                EventId = Guid.NewGuid(),
                FacilityId = _facilityId,
                ClientId = _clientId,
                ShipmentId = Guid.NewGuid(),
                TrackingNumber = "TRK-FAIL-01",
                CarrierCode = "DHL",
                DispatchedAtUtc = DateTime.UtcNow,
                SequenceNumber = 999,
                SourceStreamId = streamId
            }, cts.Token));

        // Invariant: Failed transaction rollback; checkpoint watermark does NOT advance
        context.ClearChangeTracker();
        checkpoint = await service.GetOrCreateStreamCheckpointAsync(
            TransportationProjectionService.SlaComplianceProjection, streamId);
        Assert.Equal(0, checkpoint.LastProcessedSequenceNumber);

        var complianceCount = await context.CarrierSlaComplianceRecords.CountAsync();
        Assert.Equal(0, complianceCount);
    }

    [Fact]
    public async Task Test_05_ReplayFromBeginning_DurableStores()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        var shipmentId = Guid.NewGuid();
        var dateUtc = new DateTime(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc);

        // 1. Seed Durable Operational Tables
        var scanAccepted = new ShipmentTrackingEvent
        {
            Id = Guid.NewGuid(),
            FacilityId = _facilityId,
            ClientId = _clientId,
            ShipmentId = shipmentId,
            TrackingNumber = "TRK-REPLAY-01",
            CarrierCode = "FEDEX",
            MilestoneStatus = TrackingMilestoneStatus.Accepted,
            OccurredAtUtc = dateUtc,
            ReceivedAtUtc = dateUtc
        };

        var scanDelivered = new ShipmentTrackingEvent
        {
            Id = Guid.NewGuid(),
            FacilityId = _facilityId,
            ClientId = _clientId,
            ShipmentId = shipmentId,
            TrackingNumber = "TRK-REPLAY-01",
            CarrierCode = "FEDEX",
            MilestoneStatus = TrackingMilestoneStatus.Delivered,
            OccurredAtUtc = dateUtc.AddDays(2),
            ReceivedAtUtc = dateUtc.AddDays(2)
        };

        var costRecord = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            FacilityId = _facilityId,
            ClientId = _clientId,
            ShipmentId = shipmentId,
            TrackingNumber = "TRK-REPLAY-01",
            CarrierCode = "FEDEX",
            BaseTransportationCost = 80m,
            AccessorialCost = 15m,
            TotalCarrierCost = 95m,
            Currency = "USD",
            CostAssessedAtUtc = dateUtc
        };

        context.ShipmentTrackingEvents.AddRange(scanAccepted, scanDelivered);
        context.CarrierCostRecords.Add(costRecord);
        await context.SaveChangesAsync();
        context.ClearChangeTracker();

        // 2. Execute Deterministic Replay
        var service = new TransportationProjectionService(context, NullLogger<TransportationProjectionService>.Instance);
        var replayedCount = await service.ReplayTransportationProjectionsFromDurableStoresAsync();

        // Assert 3 events replayed (Accepted scan, Delivered scan, Cost record)
        Assert.Equal(3, replayedCount);

        // Invariant: Read models accurately reconstructed from durable stores
        var compliance = await context.CarrierSlaComplianceRecords.FirstOrDefaultAsync(r => r.ShipmentId == shipmentId);
        Assert.NotNull(compliance);
        Assert.Equal(scanDelivered.OccurredAtUtc, compliance.DeliveredAtUtc);

        var freightSummary = await context.CarrierFreightCostSummaries.FirstOrDefaultAsync(s => s.CarrierCode == "FEDEX");
        Assert.NotNull(freightSummary);
        Assert.Equal(80m, freightSummary.TotalQuotedBaseFreightUsd);
        Assert.Equal(15m, freightSummary.TotalQuotedAccessorialsUsd);
    }

    [Fact]
    public async Task Test_06_PartialReplay_StreamPartitioning()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        var service = new TransportationProjectionService(context, NullLogger<TransportationProjectionService>.Instance);

        var streamFedex = $"Tracking:FEDEX:{_facilityId}";
        var streamUps = $"Tracking:UPS:{_otherFacilityId}";

        // Set high water marks on both streams
        var cpFedex = await service.GetOrCreateStreamCheckpointAsync(TransportationProjectionService.SlaComplianceProjection, streamFedex);
        cpFedex.UpdateWatermark(Guid.NewGuid(), 100, DateTime.UtcNow);

        var cpUps = await service.GetOrCreateStreamCheckpointAsync(TransportationProjectionService.SlaComplianceProjection, streamUps);
        cpUps.UpdateWatermark(Guid.NewGuid(), 200, DateTime.UtcNow);

        await context.SaveChangesAsync();

        // Reset and partial replay only FEDEX stream
        await service.ResetStreamCheckpointAsync(TransportationProjectionService.SlaComplianceProjection, streamFedex, ProjectionReplayMode.FromBeginning);

        context.ClearChangeTracker();

        // Invariant: FEDEX stream reset to beginning, while UPS stream retains high water mark 200
        var loadedFedex = await service.GetOrCreateStreamCheckpointAsync(TransportationProjectionService.SlaComplianceProjection, streamFedex);
        Assert.Equal(0, loadedFedex.LastProcessedSequenceNumber);
        Assert.Equal(ProjectionStatus.Replaying, loadedFedex.Status);

        var loadedUps = await service.GetOrCreateStreamCheckpointAsync(TransportationProjectionService.SlaComplianceProjection, streamUps);
        Assert.Equal(200, loadedUps.LastProcessedSequenceNumber);
        Assert.Equal(ProjectionStatus.Active, loadedUps.Status);
    }

    [Fact]
    public async Task Test_07_TenantIsolation_ClientConfinement()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        var service = new TransportationProjectionService(context, NullLogger<TransportationProjectionService>.Instance);
        var dateUtc = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);

        // Process dispatch for Client A
        await service.ProcessShipmentDispatchedAsync(new ShipmentDispatchedEvent
        {
            EventId = Guid.NewGuid(),
            FacilityId = _facilityId,
            ClientId = _clientId,
            ShipmentId = Guid.NewGuid(),
            TrackingNumber = "TRK-TENANT-A",
            CarrierCode = "FEDEX",
            DispatchedAtUtc = dateUtc,
            SequenceNumber = 1
        });

        // Process dispatch for Client B
        await service.ProcessShipmentDispatchedAsync(new ShipmentDispatchedEvent
        {
            EventId = Guid.NewGuid(),
            FacilityId = _facilityId,
            ClientId = _otherClientId,
            ShipmentId = Guid.NewGuid(),
            TrackingNumber = "TRK-TENANT-B",
            CarrierCode = "FEDEX",
            DispatchedAtUtc = dateUtc,
            SequenceNumber = 2
        });

        // Invariant: Strict tenant isolation in read models
        var clientARecord = await context.DailyCarrierPerformanceRecords
            .FirstOrDefaultAsync(r => r.ClientId == _clientId);
        Assert.NotNull(clientARecord);
        Assert.Equal(1, clientARecord.DispatchedShipmentsCount);

        var clientBRecord = await context.DailyCarrierPerformanceRecords
            .FirstOrDefaultAsync(r => r.ClientId == _otherClientId);
        Assert.NotNull(clientBRecord);
        Assert.Equal(1, clientBRecord.DispatchedShipmentsCount);

        // Cross-tenant verification: Client A query contains 0 rows of Client B
        var clientAOnly = await context.DailyCarrierPerformanceRecords
            .Where(r => r.ClientId == _clientId)
            .ToListAsync();
        Assert.All(clientAOnly, r => Assert.Equal(_clientId, r.ClientId));
    }

    [Fact]
    public async Task Test_08_FacilityIsolation_LaneSummaries()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        var service = new TransportationProjectionService(context, NullLogger<TransportationProjectionService>.Instance);
        var dateUtc = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);

        var shipment1 = Guid.NewGuid();
        var shipment2 = Guid.NewGuid();

        // Facility 1 dispatch and delivery
        await service.ProcessShipmentDispatchedAsync(new ShipmentDispatchedEvent
        {
            EventId = Guid.NewGuid(),
            FacilityId = _facilityId,
            ClientId = _clientId,
            ShipmentId = shipment1,
            TrackingNumber = "TRK-FAC-1",
            CarrierCode = "UPS",
            DispatchedAtUtc = dateUtc,
            SequenceNumber = 1
        });

        await service.ProcessShipmentDeliveredAsync(new ShipmentDeliveredEvent
        {
            EventId = Guid.NewGuid(),
            FacilityId = _facilityId,
            ClientId = _clientId,
            ShipmentId = shipment1,
            TrackingNumber = "TRK-FAC-1",
            CarrierCode = "UPS",
            DeliveredAtUtc = dateUtc.AddDays(2),
            SequenceNumber = 2
        });

        // Facility 2 dispatch and delivery
        await service.ProcessShipmentDispatchedAsync(new ShipmentDispatchedEvent
        {
            EventId = Guid.NewGuid(),
            FacilityId = _otherFacilityId,
            ClientId = _clientId,
            ShipmentId = shipment2,
            TrackingNumber = "TRK-FAC-2",
            CarrierCode = "UPS",
            DispatchedAtUtc = dateUtc,
            SequenceNumber = 3
        });

        await service.ProcessShipmentDeliveredAsync(new ShipmentDeliveredEvent
        {
            EventId = Guid.NewGuid(),
            FacilityId = _otherFacilityId,
            ClientId = _clientId,
            ShipmentId = shipment2,
            TrackingNumber = "TRK-FAC-2",
            CarrierCode = "UPS",
            DeliveredAtUtc = dateUtc.AddDays(3),
            SequenceNumber = 4
        });

        // Invariant: Facility isolation in lane summaries
        var fac1Lanes = await context.LaneTransitSummaries
            .Where(l => l.OriginFacilityId == _facilityId)
            .ToListAsync();

        Assert.Single(fac1Lanes);
        Assert.Equal(_facilityId, fac1Lanes[0].OriginFacilityId);
        Assert.Equal(1, fac1Lanes[0].DeliveredShipmentsCount);
    }

    [Fact]
    public async Task Test_09_InvoiceCorrections_VarianceAccuracy()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        var service = new TransportationProjectionService(context, NullLogger<TransportationProjectionService>.Instance);
        var shipmentId = Guid.NewGuid();

        // 1. Initial Quoted Amount ($100 base, $20 accessorials = $120)
        await service.ProcessCarrierCostRecordedAsync(new CarrierCostRecordedEvent
        {
            EventId = Guid.NewGuid(),
            FacilityId = _facilityId,
            ClientId = _clientId,
            ShipmentId = shipmentId,
            TrackingNumber = "TRK-INV-CORR",
            CarrierCode = "FEDEX",
            QuotedBaseFreightUsd = 100m,
            QuotedAccessorialsUsd = 20m,
            EstimatedAmountUsd = 120m,
            SequenceNumber = 1
        });

        // 2. Authoritative Reconciled Invoice Line (Billed: $100 base, $35 accessorials [Fuel $20, Res $15] = $135)
        await service.ProcessCarrierInvoiceReconciledAsync(new CarrierInvoiceReconciledEvent
        {
            EventId = Guid.NewGuid(),
            InvoiceLineId = Guid.NewGuid(),
            InvoiceNumber = "INV-FEDEX-99",
            FacilityId = _facilityId,
            ClientId = _clientId,
            ShipmentId = shipmentId,
            TrackingNumber = "TRK-INV-CORR",
            CarrierCode = "FEDEX",
            BilledBaseFreightUsd = 100m,
            BilledAccessorialsUsd = 35m,
            FuelSurchargeUsd = 20m,
            ResidentialSurchargeUsd = 15m,
            SequenceNumber = 2
        });

        var summary = await context.CarrierFreightCostSummaries.FirstOrDefaultAsync();
        Assert.NotNull(summary);
        Assert.Equal(120m, summary.TotalQuotedAmountUsd);
        Assert.Equal(135m, summary.TotalBilledAmountUsd);
        Assert.Equal(15m, summary.BilledVarianceUsd); // $135 - $120 = +$15.00
        Assert.Equal(35m, summary.AccessorialDensityRatio);
    }

    [Fact]
    public async Task Test_10_LateTrackingEvents_BillingIndependence()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        var service = new TransportationProjectionService(context, NullLogger<TransportationProjectionService>.Instance);
        var shipmentId = Guid.NewGuid();

        // 1. Carrier invoice arrives first and reconciles
        await service.ProcessCarrierInvoiceReconciledAsync(new CarrierInvoiceReconciledEvent
        {
            EventId = Guid.NewGuid(),
            InvoiceLineId = Guid.NewGuid(),
            InvoiceNumber = "INV-LATE-01",
            FacilityId = _facilityId,
            ClientId = _clientId,
            ShipmentId = shipmentId,
            TrackingNumber = "TRK-LATE-SCAN",
            CarrierCode = "UPS",
            BilledBaseFreightUsd = 40m,
            BilledAccessorialsUsd = 10m,
            SequenceNumber = 10
        });

        var costBefore = await context.CarrierFreightCostSummaries.FirstOrDefaultAsync();
        Assert.NotNull(costBefore);
        Assert.Equal(50m, costBefore.TotalBilledAmountUsd);

        // 2. Late tracking delivery scan arrives afterwards
        await service.ProcessShipmentDeliveredAsync(new ShipmentDeliveredEvent
        {
            EventId = Guid.NewGuid(),
            FacilityId = _facilityId,
            ClientId = _clientId,
            ShipmentId = shipmentId,
            TrackingNumber = "TRK-LATE-SCAN",
            CarrierCode = "UPS",
            DeliveredAtUtc = DateTime.UtcNow,
            SequenceNumber = 20
        });

        // Invariant: Late tracking event does NOT alter or corrupt billed freight cost metrics
        var costAfter = await context.CarrierFreightCostSummaries.FirstOrDefaultAsync();
        Assert.NotNull(costAfter);
        Assert.Equal(50m, costAfter.TotalBilledAmountUsd);
    }

    [Fact]
    public async Task Test_11_OperationalInvariance_ZeroLedgerMutation()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        var shipmentId = Guid.NewGuid();

        // Seed initial operational shipment
        var operationalShipment = new Shipment
        {
            Id = shipmentId,
            FacilityId = _facilityId,
            ClientId = _clientId,
            ShipmentNumber = "SHP-OP-INVAR",
            CarrierCode = "FEDEX",
            CarrierServiceLevel = CarrierServiceLevel.StandardGround,
            Status = ShipmentStatus.Dispatched,
            DeliveryStatus = TrackingMilestoneStatus.InTransit,
            TotalPackagesCount = 1,
            TotalWeightKg = 5.0m,
            TotalVolumeCm3 = 1000m
        };
        context.Shipments.Add(operationalShipment);
        await context.SaveChangesAsync();
        context.ClearChangeTracker();

        var initialShipmentVersion = operationalShipment.Version;
        var initialShipmentCount = await context.Shipments.CountAsync();
        var initialPackageCount = await context.Packages.CountAsync();

        // Execute full projection workflow
        var service = new TransportationProjectionService(context, NullLogger<TransportationProjectionService>.Instance);

        await service.ProcessShipmentDispatchedAsync(new ShipmentDispatchedEvent
        {
            EventId = Guid.NewGuid(),
            FacilityId = _facilityId,
            ClientId = _clientId,
            ShipmentId = shipmentId,
            TrackingNumber = "TRK-INVAR-01",
            CarrierCode = "FEDEX",
            DispatchedAtUtc = DateTime.UtcNow,
            SequenceNumber = 1
        });

        await service.ProcessShipmentDeliveredAsync(new ShipmentDeliveredEvent
        {
            EventId = Guid.NewGuid(),
            FacilityId = _facilityId,
            ClientId = _clientId,
            ShipmentId = shipmentId,
            TrackingNumber = "TRK-INVAR-01",
            CarrierCode = "FEDEX",
            DeliveredAtUtc = DateTime.UtcNow,
            SequenceNumber = 2
        });

        context.ClearChangeTracker();

        // Invariant: Zero mutations to operational shipment, package, or billing tables
        var finalShipmentCount = await context.Shipments.CountAsync();
        var finalPackageCount = await context.Packages.CountAsync();
        var reloadedShipment = await context.Shipments.FindAsync(shipmentId);

        Assert.NotNull(reloadedShipment);
        Assert.Equal(initialShipmentCount, finalShipmentCount);
        Assert.Equal(initialPackageCount, finalPackageCount);
        Assert.Equal(initialShipmentVersion, reloadedShipment.Version);
        Assert.Equal(ShipmentStatus.Dispatched, reloadedShipment.Status); // Operational status unmutated
    }
}
