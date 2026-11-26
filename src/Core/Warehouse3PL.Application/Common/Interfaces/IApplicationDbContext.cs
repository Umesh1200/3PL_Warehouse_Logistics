using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.CrossDock;
using Warehouse3PL.Domain.Entities.Dock;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Reference;
using Warehouse3PL.Domain.Entities.Security;

namespace Warehouse3PL.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Client> Clients { get; }
    DbSet<ClientContact> ClientContacts { get; }
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<RoleReconciliationAuditLog> RoleReconciliationAuditLogs { get; }
    DbSet<ClientFacilityAccess> ClientFacilityAccesses { get; }
    DbSet<Facility> Facilities { get; }
    DbSet<LegalEntity> LegalEntities { get; }
    DbSet<WarehouseZone> WarehouseZones { get; }
    DbSet<Location> Locations { get; }
    DbSet<LocationLock> LocationLocks { get; }

    DbSet<Item> Items { get; }
    DbSet<ItemBarcode> ItemBarcodes { get; }
    DbSet<PackagingUnit> PackagingUnits { get; }
    DbSet<ItemCategory> ItemCategories { get; }
    DbSet<ItemCategoryMapping> ItemCategoryMappings { get; }

    DbSet<StockOnHand> StockOnHand { get; }
    DbSet<InventoryLot> InventoryLots { get; }
    DbSet<InventorySerial> InventorySerials { get; }
    DbSet<InventoryTransaction> InventoryTransactions { get; }

    DbSet<PutawayTask> PutawayTasks { get; }
    DbSet<PutawayStrategyRule> PutawayStrategyRules { get; }
    DbSet<CycleCount> CycleCounts { get; }
    DbSet<CycleCountItem> CycleCountItems { get; }
    DbSet<InventoryAdjustment> InventoryAdjustments { get; }

    DbSet<Supplier> Suppliers { get; }
    DbSet<PurchaseOrder> PurchaseOrders { get; }
    DbSet<PurchaseOrderLine> PurchaseOrderLines { get; }
    DbSet<AdvanceShippingNotice> AdvanceShippingNotices { get; }
    DbSet<AsnLineItem> AsnLineItems { get; }

    DbSet<DockDoor> DockDoors { get; }
    DbSet<DockAppointment> DockAppointments { get; }
    DbSet<YardVisit> YardVisits { get; }
    DbSet<DockAssignment> DockAssignments { get; }

    DbSet<ReceivingSession> ReceivingSessions { get; }
    DbSet<ReceiptLine> ReceiptLines { get; }
    DbSet<ReceivingDiscrepancy> ReceivingDiscrepancies { get; }

    DbSet<Customer> Customers { get; }
    DbSet<OutboundOrder> OutboundOrders { get; }
    DbSet<OutboundOrderLine> OutboundOrderLines { get; }
    DbSet<ShippingAddress> ShippingAddresses { get; }
    DbSet<OrderStatusHistory> OrderStatusHistories { get; }
    DbSet<OrderImportBatch> OrderImportBatches { get; }
    DbSet<OrderImportBatchRow> OrderImportBatchRows { get; }

    DbSet<InventoryAllocation> InventoryAllocations { get; }
    DbSet<AllocationShortage> AllocationShortages { get; }
    DbSet<AllocationPolicy> AllocationPolicies { get; }

    DbSet<HandlingUnit> HandlingUnits { get; }
    DbSet<PickWave> PickWaves { get; }
    DbSet<PickWaveOrder> PickWaveOrders { get; }
    DbSet<PickTask> PickTasks { get; }
    DbSet<PickTaskStatusHistory> PickTaskStatusHistories { get; }
    DbSet<PickException> PickExceptions { get; }

    DbSet<PackagingMaterial> PackagingMaterials { get; }
    DbSet<PackingSession> PackingSessions { get; }
    DbSet<Package> Packages { get; }
    DbSet<PackageLine> PackageLines { get; }
    DbSet<ShippingLabel> ShippingLabels { get; }
    DbSet<Shipment> Shipments { get; }
    DbSet<BillOfLading> BillsOfLading { get; }
    DbSet<ShipmentCustodyTransfer> ShipmentCustodyTransfers { get; }
    DbSet<ShipmentAuditLog> ShipmentAuditLogs { get; }
    DbSet<CarrierOperationLog> CarrierOperationLogs { get; }
    DbSet<CarrierWebhookNonce> CarrierWebhookNonces { get; }
    DbSet<CarrierWebhookAuditLog> CarrierWebhookAuditLogs { get; }

    // Outbound Tracking & Delivery Confirmation (Phase 4.5)
    DbSet<ShipmentTrackingEvent> ShipmentTrackingEvents { get; }
    DbSet<ProofOfDelivery> ProofOfDeliveries { get; }
    DbSet<ShipmentDeliveryException> ShipmentDeliveryExceptions { get; }
    DbSet<DeliveryExceptionContactLog> DeliveryExceptionContactLogs { get; }
    DbSet<ReturnAuthorization> ReturnAuthorizations { get; }
    DbSet<ReturnShipment> ReturnShipments { get; }
    DbSet<ReturnTrackingEvent> ReturnTrackingEvents { get; }
    DbSet<ReturnInboundHandoff> ReturnInboundHandoffs { get; }
    DbSet<ReturnReceivingSession> ReturnReceivingSessions { get; }
    DbSet<ReturnReceivingLine> ReturnReceivingLines { get; }
    DbSet<ReturnReceivingDiscrepancy> ReturnReceivingDiscrepancies { get; }
    DbSet<PublicTrackingShare> PublicTrackingShares { get; }

    // Commercial Domain & Billing (Phase 5)
    DbSet<BillingPeriod> BillingPeriods { get; }
    DbSet<BillableActivity> BillableActivities { get; }
    DbSet<RateCard> RateCards { get; }
    DbSet<RateCardSchedule> RateCardSchedules { get; }
    DbSet<RateCardLine> RateCardLines { get; }
    DbSet<RateCardTier> RateCardTiers { get; }
    DbSet<DailyInventorySnapshot> DailyInventorySnapshots { get; }
    DbSet<DailyInventorySnapshotRecord> DailyInventorySnapshotRecords { get; }
    DbSet<StorageBillingPolicy> StorageBillingPolicies { get; }
    DbSet<CarrierCostRecord> CarrierCostRecords { get; }
    DbSet<CarrierAccessorialCharge> CarrierAccessorialCharges { get; }
    DbSet<ClientFreightAgreement> ClientFreightAgreements { get; }
    DbSet<CarrierRateAgreement> CarrierRateAgreements { get; }
    DbSet<CarrierRateTier> CarrierRateTiers { get; }
    DbSet<ClientAccessorialRule> ClientAccessorialRules { get; }
    DbSet<ShipmentFreightMarginRecord> ShipmentFreightMarginRecords { get; }
    DbSet<CarrierCostAdjustment> CarrierCostAdjustments { get; }
    DbSet<Invoice> Invoices { get; }
    DbSet<InvoiceLine> InvoiceLines { get; }
    DbSet<InvoiceSequence> InvoiceSequences { get; }
    DbSet<CreditNote> CreditNotes { get; }
    DbSet<CreditNoteLine> CreditNoteLines { get; }
    DbSet<CustomerPayment> CustomerPayments { get; }
    DbSet<PaymentAllocation> PaymentAllocations { get; }
    DbSet<InvoiceDispute> InvoiceDisputes { get; }

    // Phase 6 Analytics Projections & Read Models
    DbSet<ProjectionCheckpoint> ProjectionCheckpoints { get; }
    DbSet<AnalyticsEventDeduplicationRecord> AnalyticsEventDeduplicationRecords { get; }
    DbSet<DailyFacilityProductivitySummary> DailyFacilityProductivitySummaries { get; }
    DbSet<DailyClientProfitabilitySummary> DailyClientProfitabilitySummaries { get; }
    DbSet<DailyInventoryUtilizationSnapshot> DailyInventoryUtilizationSnapshots { get; }
    DbSet<DailyCarrierPerformanceRecord> DailyCarrierPerformanceRecords { get; }
    DbSet<HourlyFacilityThroughputSnapshot> HourlyFacilityThroughputSnapshots { get; }
    DbSet<FacilityDockYardSnapshot> FacilityDockYardSnapshots { get; }
    DbSet<LaborStandardProfile> LaborStandardProfiles { get; }
    DbSet<WorkerTaskExecutionLog> WorkerTaskExecutionLogs { get; }
    DbSet<WorkerIdleIntervalRecord> WorkerIdleIntervalRecords { get; }
    DbSet<DailyWorkerProductivitySummary> DailyWorkerProductivitySummaries { get; }

    // Phase 6.3 Inventory Health & Space Utilization Read Models
    DbSet<InventoryAgingPolicy> InventoryAgingPolicies { get; }
    DbSet<AbcClassificationPolicy> AbcClassificationPolicies { get; }
    DbSet<AbcClassificationScopeVersion> AbcClassificationScopeVersions { get; }
    DbSet<DailyInventoryAgingSnapshot> DailyInventoryAgingSnapshots { get; }
    DbSet<DailyLocationSpaceUtilizationRecord> DailyLocationSpaceUtilizationRecords { get; }
    DbSet<SkuVelocityClassificationRecord> SkuVelocityClassificationRecords { get; }
    DbSet<InventoryMovementEventLog> InventoryMovementEventLogs { get; }

    // Phase 6.3.7 ABC-Based Intelligent Warehouse Slotting Optimization
    DbSet<SlottingPolicy> SlottingPolicies { get; }
    DbSet<SlottingRecommendationBatch> SlottingRecommendationBatches { get; }
    DbSet<SlottingRecommendation> SlottingRecommendations { get; }

    // Phase 6.3.7.3 Product Affinity & Co-Locational Slotting Rules
    DbSet<ProductAffinityPolicy> ProductAffinityPolicies { get; }
    DbSet<ProductAffinityPair> ProductAffinityPairs { get; }
    DbSet<ProductAffinityCluster> ProductAffinityClusters { get; }
    DbSet<ProductAffinityClusterMember> ProductAffinityClusterMembers { get; }

    DbSet<MonitoringRule> MonitoringRules { get; }
    DbSet<MonitoringAlert> MonitoringAlerts { get; }
    DbSet<AnalyticsIngestionJournal> AnalyticsIngestionJournals { get; }
    // Phase 6.4 Transportation & Carrier SLA Analytics
    DbSet<CarrierContractualSlaPolicy> CarrierContractualSlaPolicies { get; }
    DbSet<CarrierSlaComplianceRecord> CarrierSlaComplianceRecords { get; }
    DbSet<LaneTransitSummary> LaneTransitSummaries { get; }
    DbSet<CarrierExceptionSummary> CarrierExceptionSummaries { get; }
    DbSet<CarrierFreightCostSummary> CarrierFreightCostSummaries { get; }
    DbSet<CarrierLostShipmentAlert> CarrierLostShipmentAlerts { get; }

    DbSet<CrossDockPlan> CrossDockPlans { get; }
    DbSet<CrossDockPlanLine> CrossDockPlanLines { get; }
    DbSet<CrossDockAllocation> CrossDockAllocations { get; }
    DbSet<CrossDockTask> CrossDockTasks { get; }
    DbSet<CrossDockException> CrossDockExceptions { get; }

    // Global Localization Reference & Statutory Compliance Policies (MDM-1.1 / MDM-1.3)
    DbSet<CountryProfile> CountryProfiles { get; }
    DbSet<AdministrativeDivision> AdministrativeDivisions { get; }
    DbSet<TimeZoneProfile> TimeZoneProfiles { get; }
    DbSet<CountryCompliancePolicy> CountryCompliancePolicies { get; }
    DbSet<CurrencyMaster> Currencies { get; }
    DbSet<FxRateRecord> FxRateRecords { get; }
    DbSet<UomDefinition> UomDefinitions { get; }
    DbSet<UomConversion> UomConversions { get; }
    DbSet<BikramSambatCalendarMonth> BikramSambatCalendarMonths { get; }
    DbSet<HolidayCalendar> HolidayCalendars { get; }
    DbSet<FacilityCalendar> FacilityCalendars { get; }
    DbSet<ConfigurationScopeEpoch> ConfigurationScopeEpochs { get; }
    DbSet<ConfigurationInvalidationEvent> ConfigurationInvalidationEvents { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
    Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken = default);
    Task<TResult> ExecuteWithShipmentLockAsync<TResult>(Guid? shipmentId, Func<Task<TResult>> action, CancellationToken cancellationToken = default);
    Task<TResult> ExecuteWithPackageLockAsync<TResult>(Guid? packageId, Guid? shipmentId, Func<Task<TResult>> action, CancellationToken cancellationToken = default);
    Task<TResult> ExecuteWithInvoiceSequenceLockAsync<TResult>(
        Guid clientId,
        int fiscalYear,
        string prefix,
        Func<Task<TResult>> action,
        CancellationToken cancellationToken = default,
        Guid? billingPeriodId = null,
        Guid? invoiceId = null,
        Guid? disputeId = null);
    Task<IAsyncDisposable> AcquirePackIdempotencyLockAsync(Guid facilityId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<IAsyncDisposable> AcquireScopedIdempotencyLockAsync(string lockScope, Guid facilityId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<IAsyncDisposable> AcquireStockAllocationLocksAsync(IReadOnlyCollection<Guid> stockOnHandIds, CancellationToken cancellationToken = default);
    Task<IAsyncDisposable> AcquirePutawayExecutionLocksAsync(Guid? sourceStockId, Guid destinationLocationId, CancellationToken cancellationToken = default);
    Task<TResult> ExecuteWithCompliancePolicyLockAsync<TResult>(
        string countryCode,
        string policyCode,
        Func<Task<TResult>> action,
        CancellationToken cancellationToken = default);
    Task<TResult> ExecuteWithLegalEntityLocksAsync<TResult>(
        IReadOnlyCollection<Guid> legalEntityIds,
        Func<Task<TResult>> action,
        CancellationToken cancellationToken = default,
        Guid? lockFacilityId = null);
    Task<TResult> ExecuteWithFxRateLockAsync<TResult>(
        string baseCurrencyCode,
        string quoteCurrencyCode,
        Domain.Enums.FxRateSourceAuthority sourceAuthority,
        Func<Task<TResult>> action,
        CancellationToken cancellationToken = default);
    Task<TResult> ExecuteWithFacilityCalendarLockAsync<TResult>(
        Guid facilityId,
        Func<Task<TResult>> action,
        CancellationToken cancellationToken = default);
    bool IsRelationalDatabase { get; }
    void ClearChangeTracker();
    bool AllowExplicitAuditTimestamps { get; set; }
}
