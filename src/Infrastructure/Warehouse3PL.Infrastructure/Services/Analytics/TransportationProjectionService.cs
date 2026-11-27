using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Events.Transportation;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services.Analytics;

/// <summary>
/// Service managing transportation and carrier SLA read-model projections, stream-partitioned checkpoints,
/// deduplication, and deterministic offline replay from durable operational stores.
/// Decouples operational logistics and billing ledgers from analytical read models.
/// DB-SPEC-3PL-2026 Phase 6.4 Transportation & Carrier SLA Analytics.
/// </summary>
public class TransportationProjectionService : ITransportationProjectionService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ILogger<TransportationProjectionService> _logger;
    private readonly IWorkingCalendarService _workingCalendarService;

    public const string SlaComplianceProjection = "CarrierSlaCompliance";
    public const string CarrierPerformanceProjection = "CarrierPerformance";
    public const string LaneTransitProjection = "LaneTransitSummary";
    public const string CarrierExceptionProjection = "CarrierExceptionSummary";
    public const string CarrierFreightCostProjection = "CarrierFreightCostSummary";
    public const string LostShipmentAlertProjection = "CarrierLostShipmentAlert";

    public TransportationProjectionService(
        IApplicationDbContext dbContext,
        ILogger<TransportationProjectionService> logger,
        IWorkingCalendarService? workingCalendarService = null)
    {
        _dbContext = dbContext;
        _logger = logger;
        _workingCalendarService = workingCalendarService ?? new Reference.WorkingCalendarService(_dbContext);
    }

    #region Stream Checkpointing & Deduplication

    public async Task<ProjectionCheckpoint> GetOrCreateStreamCheckpointAsync(
        string projectionName,
        string sourceStreamId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectionName))
            throw new ValidationException(nameof(projectionName), "Projection name is required.");

        var normalizedName = projectionName.Trim();
        var normalizedStream = string.IsNullOrWhiteSpace(sourceStreamId) ? "default" : sourceStreamId.Trim();

        var checkpoint = await _dbContext.ProjectionCheckpoints
            .FirstOrDefaultAsync(c => c.ProjectionName == normalizedName && c.SourceStreamId == normalizedStream, cancellationToken);

        if (checkpoint == null)
        {
            checkpoint = ProjectionCheckpoint.Create(normalizedName, normalizedStream);
            await _dbContext.ProjectionCheckpoints.AddAsync(checkpoint, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return checkpoint;
    }

    public async Task UpdateStreamCheckpointWatermarkAsync(
        string projectionName,
        string sourceStreamId,
        Guid eventId,
        long sequenceNumber,
        DateTime timestampUtc,
        CancellationToken cancellationToken = default)
    {
        var checkpoint = await GetOrCreateStreamCheckpointAsync(projectionName, sourceStreamId, cancellationToken);
        if (sequenceNumber >= checkpoint.LastProcessedSequenceNumber || checkpoint.Status == ProjectionStatus.Replaying)
        {
            checkpoint.UpdateWatermark(eventId, sequenceNumber, timestampUtc);
        }
        else
        {
            // Out-of-order historical event: increment processed count without regressing high-water mark
            checkpoint.ProcessedCount++;
        }
    }

    public async Task ResetStreamCheckpointAsync(
        string projectionName,
        string sourceStreamId,
        ProjectionReplayMode replayMode,
        DateTime? fromDateUtc = null,
        CancellationToken cancellationToken = default)
    {
        var checkpoint = await GetOrCreateStreamCheckpointAsync(projectionName, sourceStreamId, cancellationToken);

        if (replayMode == ProjectionReplayMode.FromBeginning)
        {
            checkpoint.ResetToBeginning();
        }
        else
        {
            checkpoint.MarkReplaying();
            if (fromDateUtc.HasValue)
            {
                checkpoint.LastProcessedTimestampUtc = fromDateUtc.Value;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> HasEventBeenProcessedAsync(
        string projectionName,
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = projectionName.Trim();
        return await _dbContext.AnalyticsEventDeduplicationRecords
            .AnyAsync(d => d.ProjectionName == normalizedName && d.EventId == eventId, cancellationToken);
    }

    private async Task RecordDeduplicationAsync(
        string projectionName,
        Guid eventId,
        string eventName,
        CancellationToken cancellationToken = default)
    {
        var record = AnalyticsEventDeduplicationRecord.Create(projectionName, eventId, eventName);
        await _dbContext.AnalyticsEventDeduplicationRecords.AddAsync(record, cancellationToken);
    }

    private static string ResolveStreamId(ITransportationAnalyticsEvent ev, string defaultType = "Tracking")
    {
        if (!string.IsNullOrWhiteSpace(ev.SourceStreamId))
            return ev.SourceStreamId.Trim();

        return $"{defaultType}:{ev.CarrierCode.ToUpperInvariant()}:{ev.FacilityId}";
    }

    #endregion

    #region Ingestion Handlers

    public async Task ProcessShipmentDispatchedAsync(
        ShipmentDispatchedEvent @event,
        CancellationToken cancellationToken = default)
    {
        if (await HasEventBeenProcessedAsync(SlaComplianceProjection, @event.EventId, cancellationToken))
            return;

        var streamId = ResolveStreamId(@event, "Tracking");

        // 1. Resolve Contractual SLA Policy with versioning
        var policy = await FindActiveSlaPolicyAsync(
            @event.CarrierCode,
            @event.CarrierServiceLevel,
            @event.FacilityId,
            @event.ClientId,
            @event.DispatchedAtUtc,
            cancellationToken);

        DateTime? committedEdd = null;
        int transitCommitmentDays = 3;
        string policyVersion = "Default";
        Guid? policyId = null;
        var evalStatus = SlaEvaluationStatus.IndeterminateDueToMissingPolicy;

        if (policy != null)
        {
            policyId = policy.Id;
            policyVersion = policy.SlaPolicyVersion;
            transitCommitmentDays = policy.TransitCommitmentDays;
            committedEdd = await _workingCalendarService.CalculateCommittedDeliveryDateUtcAsync(
                policy,
                @event.DispatchedAtUtc,
                cancellationToken);
            evalStatus = SlaEvaluationStatus.Evaluated;
        }
        else if (@event.InitialEstimatedDeliveryDateUtc.HasValue)
        {
            committedEdd = @event.InitialEstimatedDeliveryDateUtc.Value;
            evalStatus = SlaEvaluationStatus.Evaluated;
        }

        // 2. Create or Update CarrierSlaComplianceRecord
        if (@event.ShipmentId.HasValue)
        {
            var existingRecord = await _dbContext.CarrierSlaComplianceRecords
                .FirstOrDefaultAsync(r => r.ShipmentId == @event.ShipmentId.Value, cancellationToken);

            if (existingRecord == null)
            {
                var compliance = CarrierSlaComplianceRecord.Create(
                    shipmentId: @event.ShipmentId.Value,
                    trackingNumber: @event.TrackingNumber ?? string.Empty,
                    carrierCode: @event.CarrierCode,
                    serviceLevel: @event.CarrierServiceLevel,
                    facilityId: @event.FacilityId,
                    clientId: @event.ClientId,
                    dispatchedAtUtc: @event.DispatchedAtUtc,
                    estimatedDeliveryDateUtc: committedEdd,
                    contractualTransitDays: transitCommitmentDays,
                    slaPolicyVersion: policyVersion,
                    slaPolicyId: policyId,
                    destinationCountry: policy?.DestinationCountry ?? string.Empty);

                compliance.EvaluationStatus = evalStatus;
                compliance.CountryNormalizationStatus = policy != null && policy.CountryNormalizationStatus == CountryNormalizationStatus.Normalized
                    ? CountryNormalizationStatus.Normalized
                    : CountryNormalizationStatus.PendingNormalization;
                await _dbContext.CarrierSlaComplianceRecords.AddAsync(compliance, cancellationToken);
            }
        }

        // 3. Update DailyCarrierPerformanceRecord (Dispatched Volume)
        var dailyPerf = await GetOrCreateDailyCarrierRecordAsync(
            @event.CarrierCode,
            @event.CarrierServiceLevel,
            @event.FacilityId,
            @event.ClientId,
            @event.DispatchedAtUtc.Date,
            TimeGrain.Daily,
            cancellationToken);

        dailyPerf.RecordDispatch(1);

        // 4. Atomic Deduplication & Watermark
        await RecordDeduplicationAsync(SlaComplianceProjection, @event.EventId, nameof(ProcessShipmentDispatchedAsync), cancellationToken);
        await UpdateStreamCheckpointWatermarkAsync(SlaComplianceProjection, streamId, @event.EventId, @event.SequenceNumber, @event.OccurredOn, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ProcessShipmentTrackingUpdatedAsync(
        ShipmentTrackingUpdatedEvent @event,
        CancellationToken cancellationToken = default)
    {
        if (await HasEventBeenProcessedAsync(SlaComplianceProjection, @event.EventId, cancellationToken))
            return;

        var streamId = ResolveStreamId(@event, "Tracking");

        // Tracking updates record milestone progression without mutating freight ledgers
        if (@event.ShipmentId.HasValue)
        {
            var compliance = await _dbContext.CarrierSlaComplianceRecords
                .FirstOrDefaultAsync(r => r.ShipmentId == @event.ShipmentId.Value, cancellationToken);

            if (compliance != null && !string.IsNullOrWhiteSpace(@event.LocationState))
            {
                compliance.DestinationStateOrRegion = @event.LocationState.Trim().ToUpperInvariant();
            }
        }

        await RecordDeduplicationAsync(SlaComplianceProjection, @event.EventId, nameof(ProcessShipmentTrackingUpdatedAsync), cancellationToken);
        await UpdateStreamCheckpointWatermarkAsync(SlaComplianceProjection, streamId, @event.EventId, @event.SequenceNumber, @event.OccurredOn, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ProcessDeliveryAttemptRecordedAsync(
        DeliveryAttemptRecordedEvent @event,
        CancellationToken cancellationToken = default)
    {
        if (await HasEventBeenProcessedAsync(SlaComplianceProjection, @event.EventId, cancellationToken))
            return;

        var streamId = ResolveStreamId(@event, "Tracking");

        if (@event.ShipmentId.HasValue)
        {
            var compliance = await _dbContext.CarrierSlaComplianceRecords
                .FirstOrDefaultAsync(r => r.ShipmentId == @event.ShipmentId.Value, cancellationToken);

            if (compliance != null)
            {
                // Preserves first-attempt immutability inside entity
                compliance.RecordDeliveryAttempt(@event.AttemptTimestampUtc, @event.WasSuccessful);
            }
        }

        await RecordDeduplicationAsync(SlaComplianceProjection, @event.EventId, nameof(ProcessDeliveryAttemptRecordedAsync), cancellationToken);
        await UpdateStreamCheckpointWatermarkAsync(SlaComplianceProjection, streamId, @event.EventId, @event.SequenceNumber, @event.OccurredOn, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ProcessShipmentDeliveredAsync(
        ShipmentDeliveredEvent @event,
        CancellationToken cancellationToken = default)
    {
        if (await HasEventBeenProcessedAsync(SlaComplianceProjection, @event.EventId, cancellationToken))
            return;

        var streamId = ResolveStreamId(@event, "Tracking");

        bool isOnTime = true;
        bool isFirstAttempt = true;
        decimal transitDays = 1.0m;

        // 1. Update Shipment-Level Compliance Record
        if (@event.ShipmentId.HasValue)
        {
            var compliance = await _dbContext.CarrierSlaComplianceRecords
                .FirstOrDefaultAsync(r => r.ShipmentId == @event.ShipmentId.Value, cancellationToken);

            if (compliance != null)
            {
                compliance.RecordDelivery(@event.DeliveredAtUtc);
                isOnTime = compliance.IsOnTime ?? false;
                isFirstAttempt = compliance.IsFirstAttemptSuccess;
                transitDays = compliance.ActualTransitDays ?? 1.0m;

                // 2. Update Lane Transit Summary
                var laneSummary = await GetOrCreateLaneTransitSummaryAsync(
                    @event.FacilityId,
                    compliance.DestinationCountry,
                    compliance.DestinationStateOrRegion,
                    @event.CarrierCode,
                    compliance.CarrierServiceLevel,
                    @event.DeliveredAtUtc.Date,
                    TimeGrain.Daily,
                    @event.ClientId,
                    cancellationToken);

                laneSummary.RecordDelivery(transitDays, isOnTime);
            }

            // 3. Check and resolve any active Lost Shipment Alert
            var alert = await _dbContext.CarrierLostShipmentAlerts
                .FirstOrDefaultAsync(a => a.ShipmentId == @event.ShipmentId.Value && a.AlertStatus == LostShipmentAlertStatus.Active, cancellationToken);

            if (alert != null)
            {
                alert.MarkResolvedDelivered($"Delivered at {@event.DeliveredAtUtc:yyyy-MM-dd HH:mm:ss} UTC");
            }
        }

        // 4. Update Daily Carrier Performance Record (Delivery Outcome)
        var dailyPerf = await GetOrCreateDailyCarrierRecordAsync(
            @event.CarrierCode,
            CarrierServiceLevel.StandardGround,
            @event.FacilityId,
            @event.ClientId,
            @event.DeliveredAtUtc.Date,
            TimeGrain.Daily,
            cancellationToken);

        dailyPerf.RecordDelivery(isOnTime, isFirstAttempt, transitDays);

        // 5. Atomic Deduplication & Watermark
        await RecordDeduplicationAsync(SlaComplianceProjection, @event.EventId, nameof(ProcessShipmentDeliveredAsync), cancellationToken);
        await UpdateStreamCheckpointWatermarkAsync(SlaComplianceProjection, streamId, @event.EventId, @event.SequenceNumber, @event.OccurredOn, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ProcessProofOfDeliveryRecordedAsync(
        ProofOfDeliveryRecordedEvent @event,
        CancellationToken cancellationToken = default)
    {
        if (await HasEventBeenProcessedAsync(SlaComplianceProjection, @event.EventId, cancellationToken))
            return;

        var streamId = ResolveStreamId(@event, "Tracking");

        // POD confirms physical delivery; does not alter billing or rate amounts
        if (@event.ShipmentId.HasValue)
        {
            var compliance = await _dbContext.CarrierSlaComplianceRecords
                .FirstOrDefaultAsync(r => r.ShipmentId == @event.ShipmentId.Value, cancellationToken);

            if (compliance != null && !compliance.DeliveredAtUtc.HasValue)
            {
                compliance.RecordDelivery(@event.PodVerifiedAtUtc);
            }
        }

        await RecordDeduplicationAsync(SlaComplianceProjection, @event.EventId, nameof(ProcessProofOfDeliveryRecordedAsync), cancellationToken);
        await UpdateStreamCheckpointWatermarkAsync(SlaComplianceProjection, streamId, @event.EventId, @event.SequenceNumber, @event.OccurredOn, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ProcessDeliveryExceptionRecordedAsync(
        DeliveryExceptionRecordedEvent @event,
        CancellationToken cancellationToken = default)
    {
        if (await HasEventBeenProcessedAsync(CarrierExceptionProjection, @event.EventId, cancellationToken))
            return;

        var streamId = ResolveStreamId(@event, "Tracking");

        // 1. Update Shipment Compliance Record
        if (@event.ShipmentId.HasValue)
        {
            var compliance = await _dbContext.CarrierSlaComplianceRecords
                .FirstOrDefaultAsync(r => r.ShipmentId == @event.ShipmentId.Value, cancellationToken);

            if (compliance != null)
            {
                compliance.RecordException(@event.CanonicalCategory);
            }
        }

        // 2. Update Daily Carrier Performance Record
        var dailyPerf = await GetOrCreateDailyCarrierRecordAsync(
            @event.CarrierCode,
            CarrierServiceLevel.StandardGround,
            @event.FacilityId,
            @event.ClientId,
            @event.ExceptionTimestampUtc.Date,
            TimeGrain.Daily,
            cancellationToken);

        dailyPerf.RecordException(1);

        // 3. Update Carrier Exception Summary
        var exSummary = await GetOrCreateCarrierExceptionSummaryAsync(
            @event.CarrierCode,
            @event.FacilityId,
            @event.CanonicalCategory,
            @event.ExceptionTimestampUtc.Date,
            TimeGrain.Daily,
            @event.ClientId,
            cancellationToken);

        exSummary.RecordException(isPending: true, isLostAlarm: @event.CanonicalCategory == CanonicalDeliveryExceptionCategory.LostOrMissing);

        // 4. Atomic Deduplication & Watermark
        await RecordDeduplicationAsync(CarrierExceptionProjection, @event.EventId, nameof(ProcessDeliveryExceptionRecordedAsync), cancellationToken);
        await UpdateStreamCheckpointWatermarkAsync(CarrierExceptionProjection, streamId, @event.EventId, @event.SequenceNumber, @event.OccurredOn, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ProcessShipmentLostThresholdReachedAsync(
        ShipmentLostThresholdReachedEvent @event,
        CancellationToken cancellationToken = default)
    {
        if (await HasEventBeenProcessedAsync(LostShipmentAlertProjection, @event.EventId, cancellationToken))
            return;

        var streamId = ResolveStreamId(@event, "Tracking");

        if (@event.ShipmentId.HasValue)
        {
            var existingAlert = await _dbContext.CarrierLostShipmentAlerts
                .FirstOrDefaultAsync(a => a.ShipmentId == @event.ShipmentId.Value, cancellationToken);

            if (existingAlert == null)
            {
                var alert = CarrierLostShipmentAlert.Create(
                    shipmentId: @event.ShipmentId.Value,
                    trackingNumber: @event.TrackingNumber ?? "UNKNOWN",
                    carrierCode: @event.CarrierCode,
                    serviceLevel: @event.CarrierServiceLevel,
                    facilityId: @event.FacilityId,
                    clientId: @event.ClientId,
                    dispatchedAtUtc: @event.TriggeredAtUtc.AddHours(-(double)@event.HoursWithoutScanMovement),
                    expectedDeliveryDateUtc: null,
                    lastTrackingEventAtUtc: @event.TriggeredAtUtc.AddHours(-(double)@event.HoursWithoutScanMovement),
                    lastKnownStatus: "InTransit",
                    lastKnownLocation: "Last Known Network Scan Location",
                    hoursWithoutScanMovement: @event.HoursWithoutScanMovement,
                    policyThresholdHours: @event.ThresholdHours,
                    triggerReason: $"No carrier tracking progression detected in {@event.HoursWithoutScanMovement} hours.");

                await _dbContext.CarrierLostShipmentAlerts.AddAsync(alert, cancellationToken);
            }
        }

        await RecordDeduplicationAsync(LostShipmentAlertProjection, @event.EventId, nameof(ProcessShipmentLostThresholdReachedAsync), cancellationToken);
        await UpdateStreamCheckpointWatermarkAsync(LostShipmentAlertProjection, streamId, @event.EventId, @event.SequenceNumber, @event.OccurredOn, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ProcessCarrierCostRecordedAsync(
        CarrierCostRecordedEvent @event,
        CancellationToken cancellationToken = default)
    {
        if (await HasEventBeenProcessedAsync(CarrierFreightCostProjection, @event.EventId, cancellationToken))
            return;

        var streamId = ResolveStreamId(@event, "Invoice");

        var costSummary = await GetOrCreateFreightCostSummaryAsync(
            @event.CarrierCode,
            @event.FacilityId,
            @event.OccurredOn.Date,
            TimeGrain.Daily,
            @event.Currency,
            @event.ClientId,
            cancellationToken);

        if (@event.QuotedBaseFreightUsd > 0m || @event.QuotedAccessorialsUsd > 0m)
        {
            costSummary.RecordQuote(@event.QuotedBaseFreightUsd, @event.QuotedAccessorialsUsd);
        }

        if (@event.EstimatedAmountUsd > 0m)
        {
            costSummary.RecordEstimate(@event.EstimatedAmountUsd);
        }

        await RecordDeduplicationAsync(CarrierFreightCostProjection, @event.EventId, nameof(ProcessCarrierCostRecordedAsync), cancellationToken);
        await UpdateStreamCheckpointWatermarkAsync(CarrierFreightCostProjection, streamId, @event.EventId, @event.SequenceNumber, @event.OccurredOn, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ProcessCarrierInvoiceReconciledAsync(
        CarrierInvoiceReconciledEvent @event,
        CancellationToken cancellationToken = default)
    {
        if (await HasEventBeenProcessedAsync(CarrierFreightCostProjection, @event.EventId, cancellationToken))
            return;

        var streamId = ResolveStreamId(@event, "Invoice");

        var costSummary = await GetOrCreateFreightCostSummaryAsync(
            @event.CarrierCode,
            @event.FacilityId,
            @event.OccurredOn.Date,
            TimeGrain.Daily,
            "USD",
            @event.ClientId,
            cancellationToken);

        costSummary.RecordBilledCharge(
            baseFreight: @event.BilledBaseFreightUsd,
            accessorials: @event.BilledAccessorialsUsd,
            fuelSurcharge: @event.FuelSurchargeUsd,
            residential: @event.ResidentialSurchargeUsd,
            liftgate: @event.LiftgateFeeUsd,
            addressCorrection: @event.AddressCorrectionFeeUsd,
            other: @event.OtherAdjustmentsUsd);

        costSummary.RecordReconciliation(@event.TotalBilledAmountUsd);

        await RecordDeduplicationAsync(CarrierFreightCostProjection, @event.EventId, nameof(ProcessCarrierInvoiceReconciledAsync), cancellationToken);
        await UpdateStreamCheckpointWatermarkAsync(CarrierFreightCostProjection, streamId, @event.EventId, @event.SequenceNumber, @event.OccurredOn, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    #endregion

    #region Deterministic Replay Engine

    public async Task<int> ReplayTransportationProjectionsFromDurableStoresAsync(
        string? sourceStreamId = null,
        DateTime? fromDateUtc = null,
        DateTime? toDateUtc = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting deterministic transportation projections replay. Stream: {StreamId}, From: {From}, To: {To}",
            sourceStreamId ?? "ALL", fromDateUtc, toDateUtc);

        var from = fromDateUtc ?? DateTime.UnixEpoch;
        var to = toDateUtc ?? DateTime.UtcNow.AddDays(1);

        // 1. Reset check point(s) to Replaying
        var projections = new[]
        {
            SlaComplianceProjection,
            CarrierPerformanceProjection,
            LaneTransitProjection,
            CarrierExceptionProjection,
            CarrierFreightCostProjection,
            LostShipmentAlertProjection
        };

        foreach (var proj in projections)
        {
            var checkpoints = await _dbContext.ProjectionCheckpoints
                .Where(c => c.ProjectionName == proj && (sourceStreamId == null || c.SourceStreamId == sourceStreamId))
                .ToListAsync(cancellationToken);

            foreach (var cp in checkpoints)
            {
                cp.ResetToBeginning();
            }
        }

        // 2. Clear deduplication records for replaying stream
        var dedupRecords = await _dbContext.AnalyticsEventDeduplicationRecords
            .Where(d => projections.Contains(d.ProjectionName))
            .ToListAsync(cancellationToken);

        _dbContext.AnalyticsEventDeduplicationRecords.RemoveRange(dedupRecords);

        // 3. Query durable operational stores (ShipmentTrackingEvents, DeliveryExceptions, CarrierCostRecords)
        var trackingQuery = _dbContext.ShipmentTrackingEvents
            .AsNoTracking()
            .Where(t => t.OccurredAtUtc >= from && t.OccurredAtUtc <= to)
            .OrderBy(t => t.OccurredAtUtc);

        var trackingScans = await trackingQuery.ToListAsync(cancellationToken);

        int replayedCount = 0;
        long replaySeq = 1;

        foreach (var scan in trackingScans)
        {
            if (scan.MilestoneStatus == TrackingMilestoneStatus.Accepted || scan.MilestoneStatus == TrackingMilestoneStatus.PendingHandover)
            {
                var dispatchEvent = new ShipmentDispatchedEvent
                {
                    EventId = Guid.NewGuid(),
                    FacilityId = scan.FacilityId,
                    ClientId = scan.ClientId,
                    ShipmentId = scan.ShipmentId,
                    TrackingNumber = scan.TrackingNumber,
                    CarrierCode = scan.CarrierCode,
                    CarrierServiceLevel = CarrierServiceLevel.StandardGround,
                    DispatchedAtUtc = scan.OccurredAtUtc,
                    SequenceNumber = replaySeq++,
                    SourceStreamId = sourceStreamId ?? $"Tracking:{scan.CarrierCode}:{scan.FacilityId}"
                };

                await ProcessShipmentDispatchedAsync(dispatchEvent, cancellationToken);
                replayedCount++;
            }
            else if (scan.MilestoneStatus == TrackingMilestoneStatus.Delivered)
            {
                var deliveredEvent = new ShipmentDeliveredEvent
                {
                    EventId = Guid.NewGuid(),
                    FacilityId = scan.FacilityId,
                    ClientId = scan.ClientId,
                    ShipmentId = scan.ShipmentId,
                    TrackingNumber = scan.TrackingNumber,
                    CarrierCode = scan.CarrierCode,
                    DeliveredAtUtc = scan.OccurredAtUtc,
                    SequenceNumber = replaySeq++,
                    SourceStreamId = sourceStreamId ?? $"Tracking:{scan.CarrierCode}:{scan.FacilityId}"
                };

                await ProcessShipmentDeliveredAsync(deliveredEvent, cancellationToken);
                replayedCount++;
            }
            else if (scan.MilestoneStatus == TrackingMilestoneStatus.DeliveryException)
            {
                var exEvent = new DeliveryExceptionRecordedEvent
                {
                    EventId = Guid.NewGuid(),
                    FacilityId = scan.FacilityId,
                    ClientId = scan.ClientId,
                    ShipmentId = scan.ShipmentId,
                    TrackingNumber = scan.TrackingNumber,
                    CarrierCode = scan.CarrierCode,
                    ExceptionTimestampUtc = scan.OccurredAtUtc,
                    CanonicalCategory = CanonicalDeliveryExceptionCategory.CarrierOperationalDelay,
                    SequenceNumber = replaySeq++,
                    SourceStreamId = sourceStreamId ?? $"Tracking:{scan.CarrierCode}:{scan.FacilityId}"
                };

                await ProcessDeliveryExceptionRecordedAsync(exEvent, cancellationToken);
                replayedCount++;
            }
        }

        // 4. Query durable carrier cost records
        var costQuery = _dbContext.CarrierCostRecords
            .AsNoTracking()
            .Where(c => c.CostAssessedAtUtc >= from && c.CostAssessedAtUtc <= to)
            .OrderBy(c => c.CostAssessedAtUtc);

        var costRecords = await costQuery.ToListAsync(cancellationToken);

        foreach (var cost in costRecords)
        {
            var costEvent = new CarrierCostRecordedEvent
            {
                EventId = Guid.NewGuid(),
                FacilityId = cost.FacilityId,
                ClientId = cost.ClientId,
                ShipmentId = cost.ShipmentId,
                TrackingNumber = cost.TrackingNumber,
                CarrierCode = cost.CarrierCode,
                QuotedBaseFreightUsd = cost.BaseTransportationCost,
                QuotedAccessorialsUsd = cost.AccessorialCost,
                EstimatedAmountUsd = cost.TotalCarrierCost,
                Currency = cost.Currency,
                SequenceNumber = replaySeq++,
                SourceStreamId = sourceStreamId ?? $"Invoice:{cost.CarrierCode}:{cost.FacilityId}"
            };

            await ProcessCarrierCostRecordedAsync(costEvent, cancellationToken);
            replayedCount++;
        }

        _logger.LogInformation("Completed deterministic transportation replay. Processed {Count} events.", replayedCount);
        return replayedCount;
    }

    #endregion

    #region Private Helper Methods

    private async Task<CarrierContractualSlaPolicy?> FindActiveSlaPolicyAsync(
        string carrierCode,
        CarrierServiceLevel serviceLevel,
        Guid facilityId,
        Guid clientId,
        DateTime dateUtc,
        CancellationToken cancellationToken)
    {
        var normalizedCarrier = carrierCode.Trim().ToUpperInvariant();

        // 1. Most specific: Carrier + ServiceLevel + Facility + Client
        var policy = await _dbContext.CarrierContractualSlaPolicies
            .AsNoTracking()
            .Where(p => p.CarrierCode == normalizedCarrier
                     && p.CarrierServiceLevel == serviceLevel
                     && p.OriginFacilityId == facilityId
                     && p.ClientId == clientId
                     && p.IsActive
                     && p.EffectiveFromUtc <= dateUtc
                     && (!p.EffectiveToUtc.HasValue || p.EffectiveToUtc.Value >= dateUtc))
            .OrderByDescending(p => p.EffectiveFromUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (policy != null) return policy;

        // 2. Facility default for client: Carrier + Facility + Client (any service level if configured)
        policy = await _dbContext.CarrierContractualSlaPolicies
            .AsNoTracking()
            .Where(p => p.CarrierCode == normalizedCarrier
                     && p.CarrierServiceLevel == serviceLevel
                     && p.OriginFacilityId == facilityId
                     && p.ClientId == null
                     && p.IsActive
                     && p.EffectiveFromUtc <= dateUtc
                     && (!p.EffectiveToUtc.HasValue || p.EffectiveToUtc.Value >= dateUtc))
            .OrderByDescending(p => p.EffectiveFromUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (policy != null) return policy;

        // 3. Carrier-wide default: Carrier + ServiceLevel (no facility, no client)
        policy = await _dbContext.CarrierContractualSlaPolicies
            .AsNoTracking()
            .Where(p => p.CarrierCode == normalizedCarrier
                     && p.CarrierServiceLevel == serviceLevel
                     && p.OriginFacilityId == null
                     && p.ClientId == null
                     && p.IsActive
                     && p.EffectiveFromUtc <= dateUtc
                     && (!p.EffectiveToUtc.HasValue || p.EffectiveToUtc.Value >= dateUtc))
            .OrderByDescending(p => p.EffectiveFromUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return policy;
    }

    private async Task<DailyCarrierPerformanceRecord> GetOrCreateDailyCarrierRecordAsync(
        string carrierCode,
        CarrierServiceLevel serviceLevel,
        Guid facilityId,
        Guid? clientId,
        DateTime dateUtc,
        TimeGrain grain,
        CancellationToken cancellationToken)
    {
        var normalizedCarrier = carrierCode.Trim().ToUpperInvariant();
        var date = dateUtc.Date;

        var record = await _dbContext.DailyCarrierPerformanceRecords
            .FirstOrDefaultAsync(r => r.CarrierCode == normalizedCarrier
                                   && r.CarrierServiceLevel == serviceLevel
                                   && r.FacilityId == facilityId
                                   && r.ClientId == clientId
                                   && r.DateUtc == date
                                   && r.TimeGrain == grain, cancellationToken);

        if (record == null)
        {
            record = DailyCarrierPerformanceRecord.Create(normalizedCarrier, serviceLevel, facilityId, date, grain, clientId);
            await _dbContext.DailyCarrierPerformanceRecords.AddAsync(record, cancellationToken);
        }

        return record;
    }

    private async Task<LaneTransitSummary> GetOrCreateLaneTransitSummaryAsync(
        Guid facilityId,
        string country,
        string stateOrRegion,
        string carrierCode,
        CarrierServiceLevel serviceLevel,
        DateTime dateUtc,
        TimeGrain grain,
        Guid? clientId,
        CancellationToken cancellationToken)
    {
        var normalizedCarrier = carrierCode.Trim().ToUpperInvariant();
        var normalizedCountry = string.IsNullOrWhiteSpace(country) ? "US" : country.Trim().ToUpperInvariant();
        var normalizedState = stateOrRegion?.Trim().ToUpperInvariant() ?? string.Empty;
        var date = dateUtc.Date;

        var summary = await _dbContext.LaneTransitSummaries
            .FirstOrDefaultAsync(l => l.OriginFacilityId == facilityId
                                   && l.DestinationCountry == normalizedCountry
                                   && l.DestinationStateOrRegion == normalizedState
                                   && l.CarrierCode == normalizedCarrier
                                   && l.CarrierServiceLevel == serviceLevel
                                   && l.DateUtc == date
                                   && l.TimeGrain == grain, cancellationToken);

        if (summary == null)
        {
            summary = LaneTransitSummary.Create(facilityId, normalizedCountry, normalizedState, normalizedCarrier, serviceLevel, date, grain);
            await _dbContext.LaneTransitSummaries.AddAsync(summary, cancellationToken);
        }

        return summary;
    }

    private async Task<CarrierExceptionSummary> GetOrCreateCarrierExceptionSummaryAsync(
        string carrierCode,
        Guid facilityId,
        CanonicalDeliveryExceptionCategory category,
        DateTime dateUtc,
        TimeGrain grain,
        Guid? clientId,
        CancellationToken cancellationToken)
    {
        var normalizedCarrier = carrierCode.Trim().ToUpperInvariant();
        var date = dateUtc.Date;

        var summary = await _dbContext.CarrierExceptionSummaries
            .FirstOrDefaultAsync(s => s.CarrierCode == normalizedCarrier
                                   && s.FacilityId == facilityId
                                   && s.ClientId == clientId
                                   && s.ExceptionCategory == category
                                   && s.DateUtc == date
                                   && s.TimeGrain == grain, cancellationToken);

        if (summary == null)
        {
            summary = CarrierExceptionSummary.Create(normalizedCarrier, facilityId, category, date, grain, clientId);
            await _dbContext.CarrierExceptionSummaries.AddAsync(summary, cancellationToken);
        }

        return summary;
    }

    private async Task<CarrierFreightCostSummary> GetOrCreateFreightCostSummaryAsync(
        string carrierCode,
        Guid facilityId,
        DateTime dateUtc,
        TimeGrain grain,
        string currency,
        Guid? clientId,
        CancellationToken cancellationToken)
    {
        var normalizedCarrier = carrierCode.Trim().ToUpperInvariant();
        var normalizedCurrency = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();
        var date = dateUtc.Date;

        var summary = await _dbContext.CarrierFreightCostSummaries
            .FirstOrDefaultAsync(s => s.CarrierCode == normalizedCarrier
                                   && s.FacilityId == facilityId
                                   && s.ClientId == clientId
                                   && s.DateUtc == date
                                   && s.TimeGrain == grain
                                   && s.Currency == normalizedCurrency, cancellationToken);

        if (summary == null)
        {
            summary = CarrierFreightCostSummary.Create(normalizedCarrier, facilityId, date, grain, normalizedCurrency, clientId);
            await _dbContext.CarrierFreightCostSummaries.AddAsync(summary, cancellationToken);
        }

        return summary;
    }

    #endregion
}
