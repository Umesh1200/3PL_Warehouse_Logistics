using System.Collections.Concurrent;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.CrossDock;
using Warehouse3PL.Domain.Entities.Dock;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;

namespace Warehouse3PL.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    private readonly ITenantProvider _tenantProvider;
    private readonly ICurrentUserContext _currentUser;
    private readonly AuditableEntityInterceptor _auditableEntityInterceptor;
    private IDbContextTransaction? _currentTransaction;
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> InMemoryShipmentLocks = new();
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> InMemoryPackageLocks = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> InMemoryPackIdempotencyLocks = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> InMemoryBillingLocks = new(StringComparer.Ordinal);

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ITenantProvider tenantProvider,
        ICurrentUserContext currentUser,
        AuditableEntityInterceptor auditableEntityInterceptor)
        : base(options)
    {
        _tenantProvider = tenantProvider;
        _currentUser = currentUser;
        _auditableEntityInterceptor = auditableEntityInterceptor;
    }

    public Guid? CurrentTenantId => _tenantProvider.TenantId;
    public bool BypassTenantFilter => _tenantProvider.BypassTenantFilter || _currentUser.IsStaff;
    public bool IsRelationalDatabase => Database.IsRelational();
    public bool AllowExplicitAuditTimestamps { get; set; } = false;
    internal bool IsPrivilegedAnalyticsMaintenance => _currentUser.IsAuthenticated && _currentUser.IsSuperAdmin;
    private void EnforceJournalImmutability()
    {
        ChangeTracker.DetectChanges();
        if(ChangeTracker.Entries<AnalyticsIngestionJournal>().Any(x=>x.State==EntityState.Modified||x.State==EntityState.Deleted))
            throw new InvalidOperationException("Analytics ingestion journal entries are immutable.");
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess){EnforceJournalImmutability();return base.SaveChanges(acceptAllChangesOnSuccess);}
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,CancellationToken cancellationToken=default){EnforceJournalImmutability();return base.SaveChangesAsync(acceptAllChangesOnSuccess,cancellationToken);}

    public DbSet<Client> Clients => Set<Client>();
    public DbSet<ClientContact> ClientContacts => Set<ClientContact>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RoleReconciliationAuditLog> RoleReconciliationAuditLogs => Set<RoleReconciliationAuditLog>();
    public DbSet<ClientFacilityAccess> ClientFacilityAccesses => Set<ClientFacilityAccess>();
    public DbSet<Facility> Facilities => Set<Facility>();
    public DbSet<WarehouseZone> WarehouseZones => Set<WarehouseZone>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<LocationLock> LocationLocks => Set<LocationLock>();

    public DbSet<Item> Items => Set<Item>();
    public DbSet<ItemBarcode> ItemBarcodes => Set<ItemBarcode>();
    public DbSet<PackagingUnit> PackagingUnits => Set<PackagingUnit>();
    public DbSet<ItemCategory> ItemCategories => Set<ItemCategory>();
    public DbSet<ItemCategoryMapping> ItemCategoryMappings => Set<ItemCategoryMapping>();

    public DbSet<StockOnHand> StockOnHand => Set<StockOnHand>();
    public DbSet<InventoryLot> InventoryLots => Set<InventoryLot>();
    public DbSet<InventorySerial> InventorySerials => Set<InventorySerial>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();

    public DbSet<PutawayTask> PutawayTasks => Set<PutawayTask>();
    public DbSet<PutawayStrategyRule> PutawayStrategyRules => Set<PutawayStrategyRule>();
    public DbSet<CycleCount> CycleCounts => Set<CycleCount>();
    public DbSet<CycleCountItem> CycleCountItems => Set<CycleCountItem>();
    public DbSet<InventoryAdjustment> InventoryAdjustments => Set<InventoryAdjustment>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<AdvanceShippingNotice> AdvanceShippingNotices => Set<AdvanceShippingNotice>();
    public DbSet<AsnLineItem> AsnLineItems => Set<AsnLineItem>();

    public DbSet<DockDoor> DockDoors => Set<DockDoor>();
    public DbSet<DockAppointment> DockAppointments => Set<DockAppointment>();
    public DbSet<YardVisit> YardVisits => Set<YardVisit>();
    public DbSet<DockAssignment> DockAssignments => Set<DockAssignment>();

    public DbSet<ReceivingSession> ReceivingSessions => Set<ReceivingSession>();
    public DbSet<ReceiptLine> ReceiptLines => Set<ReceiptLine>();
    public DbSet<ReceivingDiscrepancy> ReceivingDiscrepancies => Set<ReceivingDiscrepancy>();

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<OutboundOrder> OutboundOrders => Set<OutboundOrder>();
    public DbSet<OutboundOrderLine> OutboundOrderLines => Set<OutboundOrderLine>();
    public DbSet<ShippingAddress> ShippingAddresses => Set<ShippingAddress>();
    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();
    public DbSet<OrderImportBatch> OrderImportBatches => Set<OrderImportBatch>();
    public DbSet<OrderImportBatchRow> OrderImportBatchRows => Set<OrderImportBatchRow>();

    public DbSet<InventoryAllocation> InventoryAllocations => Set<InventoryAllocation>();
    public DbSet<AllocationShortage> AllocationShortages => Set<AllocationShortage>();
    public DbSet<AllocationPolicy> AllocationPolicies => Set<AllocationPolicy>();

    public DbSet<HandlingUnit> HandlingUnits => Set<HandlingUnit>();
    public DbSet<PickWave> PickWaves => Set<PickWave>();
    public DbSet<PickWaveOrder> PickWaveOrders => Set<PickWaveOrder>();
    public DbSet<PickTask> PickTasks => Set<PickTask>();
    public DbSet<PickTaskStatusHistory> PickTaskStatusHistories => Set<PickTaskStatusHistory>();
    public DbSet<PickException> PickExceptions => Set<PickException>();

    public DbSet<PackagingMaterial> PackagingMaterials => Set<PackagingMaterial>();
    public DbSet<PackingSession> PackingSessions => Set<PackingSession>();
    public DbSet<Package> Packages => Set<Package>();
    public DbSet<PackageLine> PackageLines => Set<PackageLine>();
    public DbSet<ShippingLabel> ShippingLabels => Set<ShippingLabel>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<BillOfLading> BillsOfLading => Set<BillOfLading>();
    public DbSet<ShipmentCustodyTransfer> ShipmentCustodyTransfers => Set<ShipmentCustodyTransfer>();
    public DbSet<ShipmentAuditLog> ShipmentAuditLogs => Set<ShipmentAuditLog>();
    public DbSet<CarrierOperationLog> CarrierOperationLogs => Set<CarrierOperationLog>();
    public DbSet<CarrierWebhookNonce> CarrierWebhookNonces => Set<CarrierWebhookNonce>();
    public DbSet<CarrierWebhookAuditLog> CarrierWebhookAuditLogs => Set<CarrierWebhookAuditLog>();

    // Outbound Tracking & Delivery Confirmation (Phase 4.5)
    public DbSet<ShipmentTrackingEvent> ShipmentTrackingEvents => Set<ShipmentTrackingEvent>();
    public DbSet<ProofOfDelivery> ProofOfDeliveries => Set<ProofOfDelivery>();
    public DbSet<ShipmentDeliveryException> ShipmentDeliveryExceptions => Set<ShipmentDeliveryException>();
    public DbSet<DeliveryExceptionContactLog> DeliveryExceptionContactLogs => Set<DeliveryExceptionContactLog>();
    public DbSet<ReturnAuthorization> ReturnAuthorizations => Set<ReturnAuthorization>();
    public DbSet<ReturnShipment> ReturnShipments => Set<ReturnShipment>();
    public DbSet<ReturnTrackingEvent> ReturnTrackingEvents => Set<ReturnTrackingEvent>();
    public DbSet<ReturnInboundHandoff> ReturnInboundHandoffs => Set<ReturnInboundHandoff>();
    public DbSet<ReturnReceivingSession> ReturnReceivingSessions => Set<ReturnReceivingSession>();
    public DbSet<ReturnReceivingLine> ReturnReceivingLines => Set<ReturnReceivingLine>();
    public DbSet<ReturnReceivingDiscrepancy> ReturnReceivingDiscrepancies => Set<ReturnReceivingDiscrepancy>();
    public DbSet<PublicTrackingShare> PublicTrackingShares => Set<PublicTrackingShare>();

    // Commercial Domain & Billing (Phase 5)
    public DbSet<BillingPeriod> BillingPeriods => Set<BillingPeriod>();
    public DbSet<BillableActivity> BillableActivities => Set<BillableActivity>();
    public DbSet<RateCard> RateCards => Set<RateCard>();
    public DbSet<RateCardSchedule> RateCardSchedules => Set<RateCardSchedule>();
    public DbSet<RateCardLine> RateCardLines => Set<RateCardLine>();
    public DbSet<RateCardTier> RateCardTiers => Set<RateCardTier>();
    public DbSet<DailyInventorySnapshot> DailyInventorySnapshots => Set<DailyInventorySnapshot>();
    public DbSet<DailyInventorySnapshotRecord> DailyInventorySnapshotRecords => Set<DailyInventorySnapshotRecord>();
    public DbSet<StorageBillingPolicy> StorageBillingPolicies => Set<StorageBillingPolicy>();
    public DbSet<CarrierCostRecord> CarrierCostRecords => Set<CarrierCostRecord>();
    public DbSet<CarrierAccessorialCharge> CarrierAccessorialCharges => Set<CarrierAccessorialCharge>();
    public DbSet<ClientFreightAgreement> ClientFreightAgreements => Set<ClientFreightAgreement>();
    public DbSet<CarrierRateAgreement> CarrierRateAgreements => Set<CarrierRateAgreement>();
    public DbSet<CarrierRateTier> CarrierRateTiers => Set<CarrierRateTier>();
    public DbSet<ClientAccessorialRule> ClientAccessorialRules => Set<ClientAccessorialRule>();
    public DbSet<ShipmentFreightMarginRecord> ShipmentFreightMarginRecords => Set<ShipmentFreightMarginRecord>();
    public DbSet<CarrierCostAdjustment> CarrierCostAdjustments => Set<CarrierCostAdjustment>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<InvoiceSequence> InvoiceSequences => Set<InvoiceSequence>();
    public DbSet<CreditNote> CreditNotes => Set<CreditNote>();
    public DbSet<CreditNoteLine> CreditNoteLines => Set<CreditNoteLine>();
    public DbSet<CustomerPayment> CustomerPayments => Set<CustomerPayment>();
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();
    public DbSet<InvoiceDispute> InvoiceDisputes => Set<InvoiceDispute>();

    // Phase 6 Analytics Projections & Read Models
    public DbSet<ProjectionCheckpoint> ProjectionCheckpoints => Set<ProjectionCheckpoint>();
    public DbSet<AnalyticsEventDeduplicationRecord> AnalyticsEventDeduplicationRecords => Set<AnalyticsEventDeduplicationRecord>();
    public DbSet<DailyFacilityProductivitySummary> DailyFacilityProductivitySummaries => Set<DailyFacilityProductivitySummary>();
    public DbSet<DailyClientProfitabilitySummary> DailyClientProfitabilitySummaries => Set<DailyClientProfitabilitySummary>();
    public DbSet<DailyInventoryUtilizationSnapshot> DailyInventoryUtilizationSnapshots => Set<DailyInventoryUtilizationSnapshot>();
    public DbSet<DailyCarrierPerformanceRecord> DailyCarrierPerformanceRecords => Set<DailyCarrierPerformanceRecord>();
    public DbSet<HourlyFacilityThroughputSnapshot> HourlyFacilityThroughputSnapshots => Set<HourlyFacilityThroughputSnapshot>();
    public DbSet<FacilityDockYardSnapshot> FacilityDockYardSnapshots => Set<FacilityDockYardSnapshot>();
    public DbSet<LaborStandardProfile> LaborStandardProfiles => Set<LaborStandardProfile>();
    public DbSet<WorkerTaskExecutionLog> WorkerTaskExecutionLogs => Set<WorkerTaskExecutionLog>();
    public DbSet<WorkerIdleIntervalRecord> WorkerIdleIntervalRecords => Set<WorkerIdleIntervalRecord>();
    public DbSet<DailyWorkerProductivitySummary> DailyWorkerProductivitySummaries => Set<DailyWorkerProductivitySummary>();

    // Phase 6.3 Inventory Health & Space Utilization Read Models
    public DbSet<InventoryAgingPolicy> InventoryAgingPolicies => Set<InventoryAgingPolicy>();
    public DbSet<AbcClassificationPolicy> AbcClassificationPolicies => Set<AbcClassificationPolicy>();
    public DbSet<AbcClassificationScopeVersion> AbcClassificationScopeVersions => Set<AbcClassificationScopeVersion>();
    public DbSet<DailyInventoryAgingSnapshot> DailyInventoryAgingSnapshots => Set<DailyInventoryAgingSnapshot>();
    public DbSet<DailyLocationSpaceUtilizationRecord> DailyLocationSpaceUtilizationRecords => Set<DailyLocationSpaceUtilizationRecord>();
    public DbSet<SkuVelocityClassificationRecord> SkuVelocityClassificationRecords => Set<SkuVelocityClassificationRecord>();
    public DbSet<InventoryMovementEventLog> InventoryMovementEventLogs => Set<InventoryMovementEventLog>();

    // Phase 6.3.7 ABC-Based Intelligent Warehouse Slotting Optimization
    public DbSet<SlottingPolicy> SlottingPolicies => Set<SlottingPolicy>();
    public DbSet<SlottingRecommendationBatch> SlottingRecommendationBatches => Set<SlottingRecommendationBatch>();
    public DbSet<SlottingRecommendation> SlottingRecommendations => Set<SlottingRecommendation>();

    // Phase 6.3.7.3 Product Affinity & Co-Locational Slotting Rules
    public DbSet<ProductAffinityPolicy> ProductAffinityPolicies => Set<ProductAffinityPolicy>();
    public DbSet<ProductAffinityPair> ProductAffinityPairs => Set<ProductAffinityPair>();
    public DbSet<ProductAffinityCluster> ProductAffinityClusters => Set<ProductAffinityCluster>();
    public DbSet<ProductAffinityClusterMember> ProductAffinityClusterMembers => Set<ProductAffinityClusterMember>();

    public DbSet<MonitoringRule> MonitoringRules => Set<MonitoringRule>();
    public DbSet<MonitoringAlert> MonitoringAlerts => Set<MonitoringAlert>();
    public DbSet<AnalyticsIngestionJournal> AnalyticsIngestionJournals => Set<AnalyticsIngestionJournal>();
    // Phase 6.4 Transportation & Carrier SLA Analytics
    public DbSet<CarrierContractualSlaPolicy> CarrierContractualSlaPolicies => Set<CarrierContractualSlaPolicy>();
    public DbSet<CarrierSlaComplianceRecord> CarrierSlaComplianceRecords => Set<CarrierSlaComplianceRecord>();
    public DbSet<LaneTransitSummary> LaneTransitSummaries => Set<LaneTransitSummary>();
    public DbSet<CarrierExceptionSummary> CarrierExceptionSummaries => Set<CarrierExceptionSummary>();
    public DbSet<CarrierFreightCostSummary> CarrierFreightCostSummaries => Set<CarrierFreightCostSummary>();
    public DbSet<CarrierLostShipmentAlert> CarrierLostShipmentAlerts => Set<CarrierLostShipmentAlert>();

    public DbSet<CrossDockPlan> CrossDockPlans => Set<CrossDockPlan>();
    public DbSet<CrossDockPlanLine> CrossDockPlanLines => Set<CrossDockPlanLine>();
    public DbSet<CrossDockAllocation> CrossDockAllocations => Set<CrossDockAllocation>();
    public DbSet<CrossDockTask> CrossDockTasks => Set<CrossDockTask>();
    public DbSet<CrossDockException> CrossDockExceptions => Set<CrossDockException>();

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction != null)
        {
            return;
        }

        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
        {
            return;
        }

        _currentTransaction = await Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await SaveChangesAsync(cancellationToken);
            if (_currentTransaction != null)
            {
                await _currentTransaction.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            await RollbackTransactionAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (_currentTransaction != null)
            {
                await _currentTransaction.DisposeAsync();
                _currentTransaction = null;
            }
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_currentTransaction != null)
            {
                await _currentTransaction.RollbackAsync(cancellationToken);
            }
        }
        finally
        {
            if (_currentTransaction != null)
            {
                await _currentTransaction.DisposeAsync();
                _currentTransaction = null;
            }
        }
    }

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken = default)
    {
        await BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await action();
            await CommitTransactionAsync(cancellationToken);
            return result;
        }
        catch
        {
            await RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    public Task<IAsyncDisposable> AcquirePackIdempotencyLockAsync(
        Guid facilityId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
        => AcquireScopedIdempotencyLockAsync("PACK", facilityId, idempotencyKey, cancellationToken);

    public async Task<IAsyncDisposable> AcquireScopedIdempotencyLockAsync(
        string lockScope,
        Guid facilityId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var lockKey = $"{lockScope}:{facilityId:N}:{idempotencyKey}";

        if (!Database.IsRelational())
        {
            var gate = InMemoryPackIdempotencyLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            return new SemaphoreReleaser(gate);
        }

        if (Database.CurrentTransaction == null)
        {
            throw new InvalidOperationException($"{lockScope} idempotency lock requires an active database transaction.");
        }

        if (Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            await Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0::bigint))",
                cancellationToken);
        }

        return NoOpAsyncDisposable.Instance;
    }

    private sealed class SemaphoreReleaser(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                semaphore.Release();
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoOpAsyncDisposable : IAsyncDisposable
    {
        public static readonly NoOpAsyncDisposable Instance = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public async Task<TResult> ExecuteWithShipmentLockAsync<TResult>(
        Guid? shipmentId,
        Func<Task<TResult>> action,
        CancellationToken cancellationToken = default)
        => await ExecuteWithPackageLockAsync(packageId: null, shipmentId: shipmentId, action, cancellationToken);

    public async Task<TResult> ExecuteWithPackageLockAsync<TResult>(
        Guid? packageId,
        Guid? shipmentId,
        Func<Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        if (!Database.IsRelational())
        {
            SemaphoreSlim? shipmentGate = null;
            SemaphoreSlim? packageGate = null;

            try
            {
                // Acquire in canonical Shipment -> Package order to prevent deadlocks across callers.
                if (shipmentId.HasValue && shipmentId.Value != Guid.Empty)
                {
                    shipmentGate = InMemoryShipmentLocks.GetOrAdd(shipmentId.Value, _ => new SemaphoreSlim(1, 1));
                    await shipmentGate.WaitAsync(cancellationToken);
                }

                if (packageId.HasValue && packageId.Value != Guid.Empty)
                {
                    packageGate = InMemoryPackageLocks.GetOrAdd(packageId.Value, _ => new SemaphoreSlim(1, 1));
                    await packageGate.WaitAsync(cancellationToken);
                }

                return await action();
            }
            finally
            {
                packageGate?.Release();
                shipmentGate?.Release();
            }
        }

        async Task AcquireRelationalRowLocksAsync()
        {
            // Canonical Shipment -> Package FOR UPDATE ordering prevents deadlocks across dispatch/POD/delivery paths.
            if (shipmentId.HasValue && shipmentId.Value != Guid.Empty)
            {
                await Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM shipments WHERE shipment_id = {shipmentId.Value} FOR UPDATE",
                    cancellationToken);
            }

            if (packageId.HasValue && packageId.Value != Guid.Empty)
            {
                await Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM packages WHERE package_id = {packageId.Value} FOR UPDATE",
                    cancellationToken);
            }
        }

        // Reuse an active caller-owned transaction without invoking nested execution strategy retries.
        if (Database.CurrentTransaction != null || _currentTransaction != null)
        {
            await AcquireRelationalRowLocksAsync();
            return await action();
        }

        return await Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await BeginTransactionAsync(cancellationToken);
            try
            {
                await AcquireRelationalRowLocksAsync();
                var result = await action();
                await CommitTransactionAsync(cancellationToken);
                return result;
            }
            catch
            {
                await RollbackTransactionAsync(cancellationToken);
                throw;
            }
        });
    }

    public async Task<TResult> ExecuteWithInvoiceSequenceLockAsync<TResult>(
        Guid clientId,
        int fiscalYear,
        string prefix,
        Func<Task<TResult>> action,
        CancellationToken cancellationToken = default,
        Guid? billingPeriodId = null,
        Guid? invoiceId = null,
        Guid? disputeId = null)
    {
        var normalizedPrefix = string.IsNullOrWhiteSpace(prefix) ? "INV" : prefix.Trim().ToUpperInvariant();
        var sequenceLockKey = $"INVSEQ:{clientId:N}:{fiscalYear}:{normalizedPrefix}";

        if (!Database.IsRelational())
        {
            var acquired = new List<SemaphoreSlim>(4);
            try
            {
                // Canonical BillingPeriod -> Dispute -> Invoice -> Sequence lock order avoids deadlocks.
                if (billingPeriodId.HasValue && billingPeriodId.Value != Guid.Empty)
                {
                    var gate = InMemoryBillingLocks.GetOrAdd($"PERIOD:{billingPeriodId.Value:N}", _ => new SemaphoreSlim(1, 1));
                    await gate.WaitAsync(cancellationToken);
                    acquired.Add(gate);
                }

                if (disputeId.HasValue && disputeId.Value != Guid.Empty)
                {
                    var gate = InMemoryBillingLocks.GetOrAdd($"DISPUTE:{disputeId.Value:N}", _ => new SemaphoreSlim(1, 1));
                    await gate.WaitAsync(cancellationToken);
                    acquired.Add(gate);
                }

                if (invoiceId.HasValue && invoiceId.Value != Guid.Empty)
                {
                    var gate = InMemoryBillingLocks.GetOrAdd($"INVOICE:{invoiceId.Value:N}", _ => new SemaphoreSlim(1, 1));
                    await gate.WaitAsync(cancellationToken);
                    acquired.Add(gate);
                }

                var seqGate = InMemoryBillingLocks.GetOrAdd(sequenceLockKey, _ => new SemaphoreSlim(1, 1));
                await seqGate.WaitAsync(cancellationToken);
                acquired.Add(seqGate);

                return await action();
            }
            finally
            {
                for (int i = acquired.Count - 1; i >= 0; i--)
                {
                    acquired[i].Release();
                }
            }
        }

        async Task AcquireBillingLocksAsync()
        {
            // Canonical BillingPeriod -> Dispute -> Invoice -> Sequence lock order avoids deadlocks.
            if (billingPeriodId.HasValue && billingPeriodId.Value != Guid.Empty)
            {
                await Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM billing_periods WHERE period_id = {billingPeriodId.Value} FOR UPDATE",
                    cancellationToken);
            }

            if (disputeId.HasValue && disputeId.Value != Guid.Empty)
            {
                await Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM invoice_disputes WHERE dispute_id = {disputeId.Value} FOR UPDATE",
                    cancellationToken);
            }

            if (invoiceId.HasValue && invoiceId.Value != Guid.Empty)
            {
                await Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM invoices WHERE invoice_id = {invoiceId.Value} FOR UPDATE",
                    cancellationToken);
            }

            if (Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
            {
                // Transaction-scoped advisory lock serializes first-row creation and sequence increments
                // per (ClientId, FiscalYear, Prefix) while keeping distinct clients and prefixes unblocked.
                await Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({sequenceLockKey}, 0::bigint))",
                    cancellationToken);
            }
        }

        // Reuse an active caller-owned transaction (caller controls commit/rollback).
        if (Database.CurrentTransaction != null || _currentTransaction != null)
        {
            await AcquireBillingLocksAsync();
            return await action();
        }

        bool firstAttempt = true;
        return await Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            if (!firstAttempt)
            {
                ClearChangeTracker();
            }
            firstAttempt = false;

            await BeginTransactionAsync(cancellationToken);
            try
            {
                await AcquireBillingLocksAsync();
                var result = await action();
                await CommitTransactionAsync(cancellationToken);
                return result;
            }
            catch
            {
                await RollbackTransactionAsync(cancellationToken);
                throw;
            }
        });
    }

    public void ClearChangeTracker()
    {
        ChangeTracker.Clear();
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.AddInterceptors(_auditableEntityInterceptor);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Ignore domain events from EF model mapping
        modelBuilder.Ignore<DomainEvent>();

        // Apply entity configurations from current assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // Apply global query filters for soft deletion and multi-tenancy
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            var isSoftDeletable = typeof(ISoftDeletable).IsAssignableFrom(clrType);
            var isTenantEntity = typeof(ITenantEntity).IsAssignableFrom(clrType);

            if (isSoftDeletable || isTenantEntity)
            {
                var parameter = Expression.Parameter(clrType, "e");
                Expression? combinedFilter = null;

                if (isSoftDeletable)
                {
                    var isDeletedProperty = Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted));
                    var softDeleteFilter = Expression.Equal(isDeletedProperty, Expression.Constant(false));
                    combinedFilter = softDeleteFilter;
                }

                if (isTenantEntity)
                {
                    // Dynamic instance-aware Filter: BypassTenantFilter || e.ClientId == CurrentTenantId
                    var tenantIdProperty = Expression.Property(parameter, nameof(ITenantEntity.ClientId));
                    
                    var contextExpr = Expression.Constant(this);

                    var bypassProp = Expression.Property(contextExpr, nameof(BypassTenantFilter));
                    var currentTenantIdProp = Expression.Property(contextExpr, nameof(CurrentTenantId));

                    var nullableTenantProperty = Expression.Convert(tenantIdProperty, typeof(Guid?));
                    var tenantMatch = Expression.Equal(nullableTenantProperty, currentTenantIdProp);

                    var tenantFilter = Expression.OrElse(bypassProp, tenantMatch);

                    combinedFilter = combinedFilter == null
                        ? tenantFilter
                        : Expression.AndAlso(combinedFilter, tenantFilter);
                }

                if (combinedFilter != null)
                {
                    var lambda = Expression.Lambda(combinedFilter, parameter);
                    modelBuilder.Entity(clrType).HasQueryFilter(lambda);
                }
            }
        }
    }
}
