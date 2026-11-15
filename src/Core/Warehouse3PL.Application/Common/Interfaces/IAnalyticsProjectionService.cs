using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

public interface IAnalyticsProjectionService
{
    // Checkpointing & Deduplication
    Task<ProjectionCheckpoint> GetOrCreateCheckpointAsync(string projectionName, CancellationToken cancellationToken = default);
    Task<ProjectionCheckpoint> GetOrCreateCheckpointAsync(string projectionName, string sourceStreamId, CancellationToken cancellationToken = default);
    Task<bool> HasEventBeenProcessedAsync(string projectionName, Guid eventId, CancellationToken cancellationToken = default);
    Task RecordEventDeduplicationAsync(string projectionName, Guid eventId, string eventName, CancellationToken cancellationToken = default);
    Task UpdateCheckpointWatermarkAsync(string projectionName, Guid eventId, long sequenceNumber, DateTime timestampUtc, CancellationToken cancellationToken = default);
    Task UpdateCheckpointWatermarkAsync(string projectionName, string sourceStreamId, Guid eventId, long sequenceNumber, DateTime timestampUtc, CancellationToken cancellationToken = default);
    Task ResetProjectionCheckpointAsync(string projectionName, ProjectionReplayMode replayMode, DateTime? fromDateUtc = null, CancellationToken cancellationToken = default);
    Task ResetProjectionCheckpointAsync(string projectionName, string sourceStreamId, ProjectionReplayMode replayMode, DateTime? fromDateUtc = null, CancellationToken cancellationToken = default);

    // Fulfillment Operational Events Ingestion
    Task ProcessOrderReceivedAsync(Guid eventId, long sequenceNumber, Guid facilityId, Guid clientId, DateTime timestampUtc, int orderCount = 1, CancellationToken cancellationToken = default);
    Task ProcessOrderAllocatedAsync(Guid eventId, long sequenceNumber, Guid facilityId, Guid clientId, DateTime timestampUtc, int orderCount = 1, CancellationToken cancellationToken = default);
    Task ProcessPickCompletedAsync(Guid eventId, long sequenceNumber, Guid facilityId, Guid clientId, DateTime timestampUtc, int lines, int units, decimal pickDurationHours = 0m, CancellationToken cancellationToken = default);
    Task ProcessPackCompletedAsync(Guid eventId, long sequenceNumber, Guid facilityId, Guid clientId, DateTime timestampUtc, int cartons, CancellationToken cancellationToken = default);
    Task ProcessOrderDispatchedAsync(Guid eventId, long sequenceNumber, Guid facilityId, Guid clientId, DateTime timestampUtc, int packages = 1, decimal cycleTimeHours = 0m, bool eligibleCutoff = false, bool sameDayDispatched = false, CancellationToken cancellationToken = default);
    Task ProcessInboundReceivedAsync(Guid eventId, long sequenceNumber, Guid facilityId, Guid clientId, DateTime timestampUtc, int pallets = 1, CancellationToken cancellationToken = default);
    Task ProcessPutawayCompletedAsync(Guid eventId, long sequenceNumber, Guid facilityId, Guid clientId, DateTime timestampUtc, int pallets = 1, decimal putawayDurationHours = 0m, CancellationToken cancellationToken = default);
    Task ProcessReturnInspectedAsync(Guid eventId, long sequenceNumber, Guid facilityId, Guid clientId, DateTime timestampUtc, int returnItems = 1, CancellationToken cancellationToken = default);

    // Commercial & Billing Events Ingestion
    Task ProcessCommercialActivityRatedAsync(Guid eventId, long sequenceNumber, Guid clientId, Guid? facilityId, DateTime timestampUtc, BillableActivityType activityType, decimal amount, decimal directCost = 0m, CancellationToken cancellationToken = default);
    Task ProcessInvoiceIssuedAsync(Guid eventId, long sequenceNumber, Guid clientId, Guid? facilityId, DateTime timestampUtc, decimal subtotal, decimal tax, CancellationToken cancellationToken = default);
    Task ProcessPaymentCollectedAsync(Guid eventId, long sequenceNumber, Guid clientId, DateTime timestampUtc, decimal paymentAmount, CancellationToken cancellationToken = default);
    Task ProcessCreditAppliedAsync(Guid eventId, long sequenceNumber, Guid clientId, DateTime timestampUtc, decimal creditAmount, CancellationToken cancellationToken = default);

    // Carrier Performance Events Ingestion
    Task ProcessCarrierDispatchedAsync(Guid eventId, long sequenceNumber, string carrierCode, CarrierServiceLevel serviceLevel, Guid facilityId, DateTime timestampUtc, int count = 1, CancellationToken cancellationToken = default);
    Task ProcessCarrierDeliveredAsync(Guid eventId, long sequenceNumber, string carrierCode, CarrierServiceLevel serviceLevel, Guid facilityId, DateTime timestampUtc, bool isOnTime, bool isFirstAttempt, decimal transitDays, CancellationToken cancellationToken = default);
    Task ProcessCarrierExceptionAsync(Guid eventId, long sequenceNumber, string carrierCode, CarrierServiceLevel serviceLevel, Guid facilityId, DateTime timestampUtc, int count = 1, CancellationToken cancellationToken = default);

    // Inventory Utilization Snapshot Ingestion
    Task ProcessInventorySnapshotAsync(Guid eventId, long sequenceNumber, Guid facilityId, Guid? clientId, DateTime dateUtc, int totalLocations, int occupiedLocations, decimal totalCbm, decimal occupiedCbm, int activeSkus, int totalUnits, decimal totalValue, int deadSkus, int deadUnits, decimal deadValue, CancellationToken cancellationToken = default);

    // Dock & Yard Operational Events Ingestion
    Task ProcessTruckCheckedInAsync(Guid eventId, long sequenceNumber, Guid facilityId, DateTime timestampUtc, int count = 1, CancellationToken cancellationToken = default);
    Task ProcessTruckSpottedAtDockAsync(Guid eventId, long sequenceNumber, Guid facilityId, DateTime timestampUtc, decimal yardWaitMinutes, int count = 1, CancellationToken cancellationToken = default);
    Task ProcessDockReleasedAsync(Guid eventId, long sequenceNumber, Guid facilityId, DateTime timestampUtc, decimal dockServiceMinutes, int count = 1, CancellationToken cancellationToken = default);
    Task ProcessTruckDepartedAsync(Guid eventId, long sequenceNumber, Guid facilityId, DateTime timestampUtc, decimal totalTurnTimeMinutes, int count = 1, CancellationToken cancellationToken = default);
    Task ProcessDockYardCapacityUpdatedAsync(Guid eventId, long sequenceNumber, Guid facilityId, DateTime timestampUtc, int totalDoors, int occupiedDoors, int totalYardSpots, int occupiedYardSpots, CancellationToken cancellationToken = default);

    // Workforce & Labor Productivity Events Ingestion
    Task ProcessWorkerTaskExecutionAsync(
        Guid eventId,
        long sequenceNumber,
        Guid taskId,
        WarehouseLaborActivityType activityType,
        Guid workerUserId,
        Guid facilityId,
        string department,
        int units,
        int lines,
        int cartons,
        int pallets,
        DateTime startedAtUtc,
        DateTime completedAtUtc,
        decimal? overrideStandardSeconds = null,
        decimal hourlyWageRate = 0m,
        LaborComplexityFactor complexity = LaborComplexityFactor.Standard,
        WarehouseEquipmentType equipment = WarehouseEquipmentType.ManualCart,
        Guid? zoneId = null,
        decimal travelDistanceMeters = 0m,
        CancellationToken cancellationToken = default);

    Task ProcessWorkerIdleIntervalAsync(
        Guid eventId,
        long sequenceNumber,
        Guid workerUserId,
        Guid facilityId,
        string department,
        DateTime startedAtUtc,
        DateTime endedAtUtc,
        LaborIdleTimeReason reasonCode,
        bool supervisorApproved = false,
        string? notes = null,
        CancellationToken cancellationToken = default);

    Task UpsertLaborStandardProfileAsync(LaborStandardProfile profile, CancellationToken cancellationToken = default);

    // Phase 6.3 Inventory Health & Space Utilization Projections
    Task UpsertInventoryAgingPolicyAsync(InventoryAgingPolicy policy, CancellationToken cancellationToken = default);

    Task ProcessDailyInventoryAgingSnapshotAsync(
        Guid eventId,
        long sequenceNumber,
        Guid clientId,
        Guid facilityId,
        Guid locationId,
        string locationCode,
        Guid itemId,
        string sku,
        DateTime snapshotDateUtc,
        decimal quantityOnHand,
        DateTime? receivedAtUtc,
        DateTime? lastMovedAtUtc,
        DateTime? lastDemandedAtUtc,
        DateTime? expirationDateUtc,
        decimal unitCost = 0m,
        decimal occupiedCbm = 0m,
        decimal palletFootprint = 0m,
        bool hasMissingDimensions = false,
        Guid? lotId = null,
        string? lotNumber = null,
        string? lpnCode = null,
        string sourceInventoryReference = "",
        InventoryHoldingStatus holdingStatus = InventoryHoldingStatus.Available,
        decimal qtyAvailable = 0m,
        decimal qtyAllocated = 0m,
        decimal qtyReserved = 0m,
        decimal qtyQuarantined = 0m,
        decimal qtyDamaged = 0m,
        decimal qtyOnHold = 0m,
        string currency = "USD",
        CancellationToken cancellationToken = default);

    Task ProcessLocationSpaceUtilizationAsync(
        Guid eventId,
        long sequenceNumber,
        Guid facilityId,
        Guid locationId,
        string locationCode,
        DateTime snapshotDateUtc,
        decimal usableCbm,
        decimal occupiedCbm,
        int usablePalletPositions,
        int occupiedPalletPositions,
        Guid? zoneId = null,
        LocationType locationType = LocationType.Bin,
        string? aisleCode = null,
        string? rackCode = null,
        int? shelfLevel = null,
        int? binNumber = null,
        decimal? x = null,
        decimal? y = null,
        decimal? z = null,
        decimal maxWeightKg = 0m,
        decimal occupiedWeightKg = 0m,
        int distinctSkuCount = 0,
        decimal totalUnitsStored = 0m,
        bool hasMissingDimensions = false,
        decimal? physicalCbm = null,
        decimal usableCapacityFactor = 0.85m,
        int? physicalPalletPositions = null,
        bool isBlocked = false,
        string? blockReason = null,
        decimal unitsAvailable = 0m,
        decimal unitsAllocated = 0m,
        decimal unitsReserved = 0m,
        decimal unitsQuarantined = 0m,
        decimal unitsDamaged = 0m,
        decimal unitsOnHold = 0m,
        decimal occupiedCbmAvailable = 0m,
        decimal occupiedCbmAllocated = 0m,
        decimal occupiedCbmReserved = 0m,
        decimal occupiedCbmQuarantined = 0m,
        decimal occupiedCbmDamaged = 0m,
        decimal occupiedCbmOnHold = 0m,
        CancellationToken cancellationToken = default);

    Task ProcessInventoryMovementEventAsync(
        Guid eventId,
        long sequenceNumber,
        Guid facilityId,
        Guid clientId,
        Guid itemId,
        string sku,
        InventoryMovementEventType eventType,
        decimal quantity,
        DateTime timestampUtc,
        Guid? sourceLocationId = null,
        Guid? destinationLocationId = null,
        Guid? zoneId = null,
        decimal unitCost = 0m,
        string referenceNumber = "",
        bool? isCanonicalDemand = null,
        CancellationToken cancellationToken = default);

    Task ProcessSkuVelocityClassificationAsync(
        Guid eventId,
        long sequenceNumber,
        Guid clientId,
        Guid facilityId,
        Guid itemId,
        string sku,
        AbcClassificationCategory abcClass,
        AbcClassificationCriteria criteriaUsed,
        InventoryMovementVelocityCategory velocityCategory,
        DateTime periodStartUtc,
        DateTime periodEndUtc,
        int pickTasksCount,
        decimal unitsDemanded,
        decimal demandValue,
        int orderLinesCount,
        decimal cumulativePercentage,
        int version = 1,
        VelocityObservationWindow observationWindow = VelocityObservationWindow.Days90,
        int demandEventsCount = 0,
        int movementEventsCount = 0,
        decimal unitsMoved = 0m,
        int replenishmentCount = 0,
        int relocationCount = 0,
        decimal cogsConsumed = 0m,
        decimal avgInventoryValue = 0m,
        decimal turnoverRatio = 0m,
        decimal daysOfSupply = 0m,
        Guid? locationId = null,
        Guid? zoneId = null,
        string? aisle = null,
        decimal daysOfSupplyFinancial = 0m,
        decimal daysOfSupplyOperational = 0m,
        decimal currentOnHandUnits = 0m,
        bool isDemandSpike = false,
        bool isSeasonalPattern = false,
        CancellationToken cancellationToken = default);

    Task<SkuVelocityClassificationRecord> CalculateSkuVelocityAsync(
        Guid facilityId,
        Guid clientId,
        Guid itemId,
        string sku,
        VelocityObservationWindow window = VelocityObservationWindow.Days90,
        AbcClassificationCriteria criteria = AbcClassificationCriteria.PickFrequency,
        DateTime? asOfUtc = null,
        int version = 1,
        CancellationToken cancellationToken = default);

    // Phase 6.3.5 ABC Inventory Classification Engine
    Task<AbcClassificationPolicyDto> UpsertAbcClassificationPolicyAsync(
        UpsertAbcClassificationPolicyRequest request,
        CancellationToken cancellationToken = default);

    Task<AbcClassificationBatchResultDto> CalculateAbcClassificationBatchAsync(
        Guid facilityId,
        Guid? clientId = null,
        Guid? policyId = null,
        AbcClassificationCriteria? criteriaOverride = null,
        VelocityObservationWindow? windowOverride = null,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default);

    // Projection Replay & Rebuild Engine
    Task<int> ReplayProjectionsAsync(string projectionName, ProjectionReplayMode replayMode, DateTime? fromDateUtc = null, DateTime? toDateUtc = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Production replay worker that queries the durable event stream from InventoryMovementEventLogs
    /// and reconstructs the DailyInventoryAgingSnapshots projection, enforcing checkpoint watermark
    /// updates and event deduplication strictly from the stored sequence numbers and event payloads.
    /// </summary>
    Task<int> ReplayInventoryAgingFromEventStreamAsync(
        Guid facilityId,
        DateTime? asOfDateUtc = null,
        DateTime? fromDateUtc = null,
        DateTime? toDateUtc = null,
        CancellationToken cancellationToken = default);
}

