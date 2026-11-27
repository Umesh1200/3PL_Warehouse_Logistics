using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Reference;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Persistence;

namespace Warehouse3PL.Infrastructure.Services.Reference;

public sealed class LegacyCountryNormalizationService : ILegacyCountryNormalizationService
{
    private static readonly SemaphoreSlim InMemoryBackfillGate = new(1, 1);
    private readonly IApplicationDbContext _db;

    public LegacyCountryNormalizationService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<CountryNormalizationResolution> ResolveCountryAsync(
        string? rawCountry,
        CancellationToken cancellationToken = default)
    {
        var catalog = await CatalogIndex.LoadAsync(_db, cancellationToken);
        return catalog.ResolveExplicit(rawCountry);
    }

    public async Task<string> ExportProvenanceSnapshotJsonAsync(CancellationToken cancellationToken = default)
    {
        var catalog = await CatalogIndex.LoadAsync(_db, cancellationToken);
        var audits = await _db.CountryNormalizationAudits
            .AsNoTracking()
            .OrderBy(a => a.InitialRecordedAtUtc)
            .ThenBy(a => a.EntityType)
            .ThenBy(a => a.EntityId)
            .ToListAsync(cancellationToken);

        var sanitizedRecords = audits.Select(a => new
        {
            a.Id,
            a.EntityType,
            a.EntityId,
            a.FieldName,
            OriginalCountryValue = CountryDiagnosticSanitizer.SanitizeForDiagnostic(a.OriginalCountryValue, catalog.VerifiedCatalogKeys),
            a.NormalizedCountryCode,
            NormalizationStatus = a.NormalizationStatus.ToString(),
            a.PreservedHistoricalValue,
            a.WasInferredFromParent,
            a.InferenceSource,
            Reason = CountryDiagnosticSanitizer.SanitizeReason(a.Reason),
            a.FacilityId,
            a.ClientId,
            a.InitialCorrelationBatchId,
            a.InitialRecordedAtUtc,
            a.CorrelationBatchId,
            a.RecordedAtUtc
        }).ToList();

        return System.Text.Json.JsonSerializer.Serialize(new
        {
            ExportedAtUtc = DateTime.UtcNow,
            SchemaMigration = "20261127051434_MDM1_6_R04_NormalizationRemediationLifecycle",
            TotalAuditRecords = sanitizedRecords.Count,
            Records = sanitizedRecords
        });
    }

    /// <summary>
    /// Executes deterministic country normalization backfill under strict cross-instance concurrency control.
    /// <list type="bullet">
    /// <item><b>Caller-owned relational transaction:</b> Reuses the active transaction, acquires PostgreSQL
    /// advisory + <c>SHARE ROW EXCLUSIVE</c> table locks in canonical order, and leaves Commit/Rollback to the caller.</item>
    /// <item><b>Service-owned relational transaction:</b> Opens a resilient execution-strategy transaction, acquires
    /// PostgreSQL advisory + <c>SHARE ROW EXCLUSIVE</c> table locks, commits atomically, and never falls back to a process-local semaphore.</item>
    /// <item><b>Non-relational (InMemory test) provider:</b> Uses process-local <see cref="InMemoryBackfillGate"/>.</item>
    /// </list>
    /// </summary>
    public async Task<LegacyCountryBackfillSummary> BackfillAllAsync(
        string? correlationBatchId = null,
        CancellationToken cancellationToken = default)
    {
        var batchId = string.IsNullOrWhiteSpace(correlationBatchId)
            ? $"MDM16-BACKFILL-{DateTime.UtcNow:yyyyMMddHHmmss}"
            : correlationBatchId.Trim();

        if (_db.IsRelationalDatabase)
        {
            if (_db is not DbContext relationalCtx)
            {
                throw new InvalidOperationException("Relational country normalization backfill requires an EF Core DbContext.");
            }

            if (relationalCtx.Database.CurrentTransaction != null)
            {
                await AcquireRelationalBackfillLocksAsync(relationalCtx, cancellationToken);
                return await ExecuteBackfillCoreAsync(batchId, cancellationToken);
            }

            bool firstAttempt = true;
            return await relationalCtx.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                if (!firstAttempt)
                {
                    _db.ClearChangeTracker();
                }
                firstAttempt = false;

                await using var tx = await relationalCtx.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    await AcquireRelationalBackfillLocksAsync(relationalCtx, cancellationToken);
                    var summary = await ExecuteBackfillCoreAsync(batchId, cancellationToken);
                    await tx.CommitAsync(cancellationToken);
                    return summary;
                }
                catch
                {
                    await tx.RollbackAsync(cancellationToken);
                    throw;
                }
            });
        }

        await InMemoryBackfillGate.WaitAsync(cancellationToken);
        try
        {
            return await ExecuteBackfillCoreAsync(batchId, cancellationToken);
        }
        finally
        {
            InMemoryBackfillGate.Release();
        }
    }

    private static async Task AcquireRelationalBackfillLocksAsync(DbContext relationalCtx, CancellationToken cancellationToken)
    {
        if (relationalCtx.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            await relationalCtx.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock(hashtextextended('MDM16_COUNTRY_BACKFILL', 0::bigint));",
                cancellationToken);

            // SHARE ROW EXCLUSIVE allows concurrent SELECT reads while blocking conflicting operational writes
            // (order/package lifecycle mutations, parent country updates, and concurrent backfills) across all instances.
            await relationalCtx.Database.ExecuteSqlRawAsync(
                """
                LOCK TABLE
                    clients,
                    facilities,
                    client_facility_access,
                    customers,
                    suppliers,
                    outbound_orders,
                    packages,
                    shipments,
                    bills_of_lading,
                    shipping_addresses,
                    return_shipments,
                    carrier_contractual_sla_policies,
                    carrier_sla_compliance_records,
                    country_normalization_audits
                IN SHARE ROW EXCLUSIVE MODE;
                """,
                cancellationToken);
        }
    }

    private async Task<LegacyCountryBackfillSummary> ExecuteBackfillCoreAsync(
        string batchId,
        CancellationToken cancellationToken)
    {
        var utcNow = DateTime.UtcNow;
        var catalog = await CatalogIndex.LoadAsync(_db, cancellationToken);

        var allFacilities = await _db.Facilities.IgnoreQueryFilters().ToListAsync(cancellationToken);
        var allCustomers = await _db.Customers.IgnoreQueryFilters().ToListAsync(cancellationToken);
        var allSuppliers = await _db.Suppliers.IgnoreQueryFilters().ToListAsync(cancellationToken);
        var allOrders = await _db.OutboundOrders.IgnoreQueryFilters()
            .Select(o => new { o.Id, o.FacilityId, o.DestinationFacilityId, o.OrderType, o.ClientId, o.CustomerId, o.Status, o.IsDeleted })
            .ToListAsync(cancellationToken);
        var allShippingAddresses = await _db.ShippingAddresses.IgnoreQueryFilters().ToListAsync(cancellationToken);
        var allReturnShipments = await _db.ReturnShipments.IgnoreQueryFilters().ToListAsync(cancellationToken);
        var allSlaPolicies = await _db.CarrierContractualSlaPolicies.IgnoreQueryFilters().ToListAsync(cancellationToken);
        var allSlaRecords = await _db.CarrierSlaComplianceRecords.IgnoreQueryFilters().ToListAsync(cancellationToken);

        int totalRowsAcrossTargets =
            allFacilities.Count(f => !f.IsDeleted) +
            allCustomers.Count(c => !c.IsDeleted) +
            allSuppliers.Count(s => !s.IsDeleted) +
            allShippingAddresses.Count(sa => !sa.IsDeleted) +
            allReturnShipments.Count +
            allSlaPolicies.Count +
            allSlaRecords.Count;

        if (!catalog.HasActiveProfiles)
        {
            if (totalRowsAcrossTargets == 0)
            {
                return new LegacyCountryBackfillSummary(batchId, 0, 0, 0, 0, 0, 0);
            }

            throw new InvalidOperationException(
                "Country normalization backfill blocked: active CountryProfile reference catalog rows are required before normalizing populated records.");
        }

        var existingAudits = await _db.CountryNormalizationAudits.ToListAsync(cancellationToken);

        // R03: Reconcile and remove stale diagnostics for soft-deleted entities
        var deletedFacilityIds = allFacilities.Where(f => f.IsDeleted).Select(f => f.Id).ToHashSet();
        var deletedCustomerIds = allCustomers.Where(c => c.IsDeleted).Select(c => c.Id).ToHashSet();
        var deletedSupplierIds = allSuppliers.Where(s => s.IsDeleted).Select(s => s.Id).ToHashSet();
        var deletedOrderIds = allOrders.Where(o => o.IsDeleted).Select(o => o.Id).ToHashSet();
        var deletedAddrIds = allShippingAddresses
            .Where(sa => sa.IsDeleted || deletedOrderIds.Contains(sa.OutboundOrderId))
            .Select(sa => sa.Id)
            .ToHashSet();

        var auditsToRemove = existingAudits.Where(a =>
            (a.EntityType == nameof(Facility) && deletedFacilityIds.Contains(a.EntityId)) ||
            (a.EntityType == nameof(Customer) && deletedCustomerIds.Contains(a.EntityId)) ||
            (a.EntityType == nameof(Supplier) && deletedSupplierIds.Contains(a.EntityId)) ||
            (a.EntityType == nameof(ShippingAddress) && deletedAddrIds.Contains(a.EntityId)))
            .ToList();

        foreach (var staleAudit in auditsToRemove)
        {
            _db.CountryNormalizationAudits.Remove(staleAudit);
            existingAudits.Remove(staleAudit);
        }

        var auditByKey = existingAudits.ToDictionary(
            a => (a.EntityType, a.EntityId, a.FieldName),
            a => a);

        var facilities = allFacilities.Where(f => !f.IsDeleted).ToList();
        var facilityMap = facilities.ToDictionary(f => f.Id, f => f);

        var activeClientIds = await _db.Clients
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => !c.IsDeleted && c.Status == ClientStatus.Active)
            .Select(c => c.Id)
            .ToHashSetAsync(cancellationToken);

        var activeAccesses = (await _db.ClientFacilityAccesses
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(cfa => cfa.Status == FacilityAccessStatus.Active)
            .Select(cfa => new { cfa.ClientId, cfa.FacilityId })
            .ToListAsync(cancellationToken))
            .Where(x => activeClientIds.Contains(x.ClientId) &&
                        facilityMap.TryGetValue(x.FacilityId, out var fac) &&
                        fac.IsActive)
            .ToList();

        var singleFacilityByClient = activeAccesses
            .GroupBy(x => x.ClientId)
            .Where(g => g.Select(x => x.FacilityId).Distinct().Count() == 1)
            .ToDictionary(g => g.Key, g => (Guid?)g.First().FacilityId);

        int totalEvaluated = 0, mutated = 0, unchanged = 0, preservedHistorical = 0, inferredCount = 0, quarantined = 0;

        void ReconcileAudit(
            string entityType,
            Guid entityId,
            string fieldName,
            string? rawValue,
            string? resolvedCode,
            CountryNormalizationStatus status,
            bool preservedHistoricalVal,
            bool wasInferred,
            string? inferenceSource,
            string reason,
            Guid? facilityId,
            Guid? clientId,
            bool forceAuditWrite)
        {
            var sanitizedReason = CountryDiagnosticSanitizer.SanitizeReason(reason);
            var key = (entityType, entityId, fieldName);
            if (auditByKey.TryGetValue(key, out var existing))
            {
                // Semantic idempotency: when already Normalized with the exact same resolved code, historical flag, and tenant/facility scope,
                // preserve the normalizing run's InferenceSource/WasInferredFromParent/Reason/CorrelationBatchId/RecordedAtUtc.
                if (existing.NormalizationStatus == CountryNormalizationStatus.Normalized &&
                    status == CountryNormalizationStatus.Normalized &&
                    string.Equals(existing.NormalizedCountryCode, resolvedCode, StringComparison.Ordinal) &&
                    existing.PreservedHistoricalValue == preservedHistoricalVal &&
                    existing.FacilityId == facilityId &&
                    existing.ClientId == clientId)
                {
                    return;
                }

                // Semantic idempotency: skip update unless diagnostic state, association, or reason actually changed
                bool stateChanged =
                    !string.Equals(existing.NormalizedCountryCode, resolvedCode, StringComparison.Ordinal) ||
                    existing.NormalizationStatus != status ||
                    existing.PreservedHistoricalValue != preservedHistoricalVal ||
                    existing.WasInferredFromParent != wasInferred ||
                    !string.Equals(existing.InferenceSource, inferenceSource, StringComparison.Ordinal) ||
                    existing.FacilityId != facilityId ||
                    existing.ClientId != clientId ||
                    !string.Equals(existing.Reason, sanitizedReason, StringComparison.Ordinal);

                if (!stateChanged)
                {
                    return;
                }

                // R03: Preserve immutable initial provenance (OriginalCountryValue, InitialRecordedAtUtc, InitialCorrelationBatchId)
                existing.NormalizedCountryCode = resolvedCode;
                existing.NormalizationStatus = status;
                existing.PreservedHistoricalValue = preservedHistoricalVal;
                existing.WasInferredFromParent = wasInferred;
                existing.InferenceSource = inferenceSource;
                existing.Reason = sanitizedReason;
                existing.FacilityId = facilityId;
                existing.ClientId = clientId;
                existing.CorrelationBatchId = batchId;
                existing.RecordedAtUtc = utcNow;
                return;
            }

            if (!forceAuditWrite)
            {
                return;
            }

            var audit = new CountryNormalizationAudit
            {
                Id = Guid.NewGuid(),
                EntityType = entityType,
                EntityId = entityId,
                FieldName = fieldName,
                OriginalCountryValue = CountryDiagnosticSanitizer.SanitizeForDiagnostic(rawValue, catalog.VerifiedCatalogKeys),
                NormalizedCountryCode = resolvedCode,
                NormalizationStatus = status,
                PreservedHistoricalValue = preservedHistoricalVal,
                WasInferredFromParent = wasInferred,
                InferenceSource = inferenceSource,
                Reason = sanitizedReason,
                FacilityId = facilityId,
                ClientId = clientId,
                CorrelationBatchId = batchId,
                RecordedAtUtc = utcNow,
                InitialCorrelationBatchId = batchId,
                InitialRecordedAtUtc = utcNow
            };
            auditByKey[key] = audit;
            _db.CountryNormalizationAudits.Add(audit);
        }

        // Single-field evaluation returning resolution + whether initial write is needed
        (bool IsResolved, string? TargetCode, CountryNormalizationStatus Status, bool WasInferred, bool NeedsInitialAudit) EvaluateField(
            string? rawValue,
            string? state,
            string? postal,
            string? verifiedParentCountry,
            string parentHintSource,
            string? timeZoneHint,
            bool allowTimeZoneInference,
            bool requireVerifiedParentForInference,
            bool isHistoricalImmutable,
            out bool wasChanged,
            out string? inferenceSource,
            out string reason)
        {
            wasChanged = false;
            var eval = catalog.EvaluateWithGeoAndParent(
                rawValue, state, postal, verifiedParentCountry, parentHintSource,
                timeZoneHint, allowTimeZoneInference, requireVerifiedParentForInference);
            inferenceSource = eval.InferenceSource;
            reason = eval.Reason;

            if (!eval.IsResolved)
            {
                return (false, null, CountryNormalizationStatus.PendingNormalization, false, true);
            }

            if (isHistoricalImmutable)
            {
                bool differsFromExactCanonical = !string.Equals(rawValue?.Trim(), eval.CanonicalCountryCode, StringComparison.Ordinal);
                if (differsFromExactCanonical)
                {
                    reason = $"Immutable historical record preserved untouched; {eval.Reason}";
                }
                return (true, eval.CanonicalCountryCode, CountryNormalizationStatus.Normalized, eval.WasInferredFromParent, differsFromExactCanonical);
            }

            wasChanged = !string.Equals(rawValue, eval.CanonicalCountryCode, StringComparison.Ordinal);
            return (true, eval.CanonicalCountryCode, CountryNormalizationStatus.Normalized, eval.WasInferredFromParent, wasChanged || eval.WasInferredFromParent);
        }

        // 1. Facilities (Mutable Master Data)
        foreach (var fac in facilities)
        {
            totalEvaluated++;
            var res = EvaluateField(
                fac.CountryCode, fac.StateOrProvince, fac.PostalCode,
                verifiedParentCountry: null, parentHintSource: "FacilityGeo",
                timeZoneHint: fac.Timezone, allowTimeZoneInference: true, requireVerifiedParentForInference: false,
                isHistoricalImmutable: false, out bool changed, out var src, out var reason);

            var origRaw = fac.CountryCode;
            fac.CountryNormalizationStatus = res.Status;
            if (res.IsResolved && res.TargetCode != null)
            {
                if (changed)
                {
                    fac.CountryCode = res.TargetCode;
                    mutated++;
                }
                else unchanged++;
                if (res.WasInferred) inferredCount++;
            }
            else quarantined++;

            ReconcileAudit(nameof(Facility), fac.Id, nameof(Facility.CountryCode), origRaw, res.TargetCode,
                res.Status, false, res.WasInferred, src, reason, fac.Id, null, res.NeedsInitialAudit);
        }

        // R02: Build verified client network map WITHOUT discarding unresolved facilities.
        // If ANY active linked facility is unresolved or fails canonical verification, clientSingleVerifiedCountry is null.
        var clientVerifiedSingleCountry = new Dictionary<Guid, string?>();
        foreach (var group in activeAccesses.GroupBy(x => x.ClientId))
        {
            var resolvedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool anyUnresolved = false;

            foreach (var link in group)
            {
                if (!facilityMap.TryGetValue(link.FacilityId, out var fac) ||
                    fac.CountryNormalizationStatus != CountryNormalizationStatus.Normalized ||
                    !catalog.TryGetCanonicalCountry(fac.CountryCode, out var facIso, out _) ||
                    string.IsNullOrWhiteSpace(facIso))
                {
                    anyUnresolved = true;
                    break;
                }

                resolvedCodes.Add(facIso);
            }

            clientVerifiedSingleCountry[group.Key] = !anyUnresolved && resolvedCodes.Count == 1
                ? resolvedCodes.First()
                : null;
        }

        // 2. Customers (Mutable Master Data)
        var customers = allCustomers.Where(c => !c.IsDeleted).ToList();
        var customerMap = customers.ToDictionary(c => c.Id, c => c);
        foreach (var cust in customers)
        {
            totalEvaluated++;
            clientVerifiedSingleCountry.TryGetValue(cust.ClientId, out var singleClientCountry);
            var res = EvaluateField(
                cust.DefaultCountryCode, cust.DefaultStateOrProvince, cust.DefaultPostalCode,
                verifiedParentCountry: singleClientCountry, parentHintSource: "ClientDomesticAccess",
                timeZoneHint: null, allowTimeZoneInference: false, requireVerifiedParentForInference: true,
                isHistoricalImmutable: false, out bool changed, out var src, out var reason);

            var origRaw = cust.DefaultCountryCode;
            cust.CountryNormalizationStatus = res.Status;
            if (res.IsResolved && res.TargetCode != null)
            {
                if (changed)
                {
                    cust.DefaultCountryCode = res.TargetCode;
                    mutated++;
                }
                else unchanged++;
                if (res.WasInferred) inferredCount++;
            }
            else quarantined++;

            singleFacilityByClient.TryGetValue(cust.ClientId, out var custFacId);
            ReconcileAudit(nameof(Customer), cust.Id, nameof(Customer.DefaultCountryCode), origRaw, res.TargetCode,
                res.Status, false, res.WasInferred, src, reason, custFacId, cust.ClientId, res.NeedsInitialAudit);
        }

        // 3. Suppliers (Mutable Master Data)
        var suppliers = allSuppliers.Where(s => !s.IsDeleted).ToList();
        foreach (var sup in suppliers)
        {
            totalEvaluated++;
            clientVerifiedSingleCountry.TryGetValue(sup.ClientId, out var singleClientCountry);
            var res = EvaluateField(
                sup.Country, sup.State, sup.PostalCode,
                verifiedParentCountry: singleClientCountry, parentHintSource: "ClientDomesticAccess",
                timeZoneHint: null, allowTimeZoneInference: false, requireVerifiedParentForInference: true,
                isHistoricalImmutable: false, out bool changed, out var src, out var reason);

            var origRaw = sup.Country;
            sup.CountryNormalizationStatus = res.Status;
            if (res.IsResolved && res.TargetCode != null)
            {
                if (changed)
                {
                    sup.Country = res.TargetCode;
                    mutated++;
                }
                else unchanged++;
                if (res.WasInferred) inferredCount++;
            }
            else quarantined++;

            singleFacilityByClient.TryGetValue(sup.ClientId, out var supFacId);
            ReconcileAudit(nameof(Supplier), sup.Id, nameof(Supplier.Country), origRaw, res.TargetCode,
                res.Status, false, res.WasInferred, src, reason, supFacId, sup.ClientId, res.NeedsInitialAudit);
        }

        // 4. ShippingAddresses (Mutable pre-fulfillment vs Immutable historical snapshot)
        var bolShipmentIds = (await _db.BillsOfLading
            .IgnoreQueryFilters()
            .Where(b => !b.IsDeleted)
            .Select(b => b.ShipmentId)
            .Distinct()
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var orderIdsWithFulfillment = (await _db.Packages
            .IgnoreQueryFilters()
            .Where(p => !p.IsDeleted &&
                        (p.ShipmentId != null ||
                         p.Status != PackageStatus.Open ||
                         (p.ShipmentId.HasValue && bolShipmentIds.Contains(p.ShipmentId.Value))))
            .Select(p => p.OutboundOrderId)
            .Distinct()
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var orderMap = allOrders.Where(o => !o.IsDeleted).ToDictionary(o => o.Id);
        var shippingAddresses = allShippingAddresses.Where(sa => !sa.IsDeleted).ToList();

        foreach (var addr in shippingAddresses)
        {
            totalEvaluated++;
            orderMap.TryGetValue(addr.OutboundOrderId, out var ord);
            bool hasFulfillment = ord != null && orderIdsWithFulfillment.Contains(ord.Id);
            bool isHistorical = ord == null ||
                hasFulfillment ||
                ord.Status is OutboundOrderStatus.Allocated or OutboundOrderStatus.PartiallyAllocated or OutboundOrderStatus.InProgress or OutboundOrderStatus.Completed or OutboundOrderStatus.Cancelled;

            // R02: Parent country MUST have Verified Normalized status & active non-deleted parent entity
            Customer? linkedCust = ord?.CustomerId != null && customerMap.TryGetValue(ord.CustomerId.Value, out var cEntity)
                ? cEntity
                : null;
            string? verifiedCustomerCountry = linkedCust != null &&
                                              !linkedCust.IsDeleted &&
                                              linkedCust.IsActive &&
                                              linkedCust.CountryNormalizationStatus == CountryNormalizationStatus.Normalized &&
                                              catalog.TryGetCanonicalCountry(linkedCust.DefaultCountryCode, out var ccNorm, out _)
                ? ccNorm
                : null;

            string? verifiedOriginFacCountry = ord != null &&
                                               facilityMap.TryGetValue(ord.FacilityId, out var oFac) &&
                                               oFac.IsActive &&
                                               oFac.CountryNormalizationStatus == CountryNormalizationStatus.Normalized &&
                                               catalog.TryGetCanonicalCountry(oFac.CountryCode, out var fcNorm, out _)
                ? fcNorm
                : null;

            string? verifiedDestFacCountry = ord?.OrderType == OutboundOrderType.StockTransferOrder &&
                                             ord.DestinationFacilityId.HasValue &&
                                             facilityMap.TryGetValue(ord.DestinationFacilityId.Value, out var dFac) &&
                                             dFac.IsActive &&
                                             dFac.CountryNormalizationStatus == CountryNormalizationStatus.Normalized &&
                                             catalog.TryGetCanonicalCountry(dFac.CountryCode, out var dfNorm, out _)
                ? dfNorm
                : null;

            (bool IsResolved, string? TargetCode, CountryNormalizationStatus Status, bool WasInferred, bool NeedsInitialAudit) res;
            bool changed;
            string? src;
            string reason;

            if (string.IsNullOrWhiteSpace(addr.CountryCode) && linkedCust != null && verifiedCustomerCountry == null)
            {
                changed = false;
                src = "QuarantinedCustomerParent";
                reason = "Quarantined or unverified customer master cannot verify child shipping address country.";
                res = (false, null, CountryNormalizationStatus.PendingNormalization, false, true);
            }
            else
            {
                // Never infer ShippingAddress destination solely from origin warehouse facility;
                // only verified CustomerDefault or verified StockTransfer DestinationFacility may verify parent context.
                string? parentHint = ord?.OrderType == OutboundOrderType.StockTransferOrder
                    ? verifiedDestFacCountry
                    : verifiedCustomerCountry;
                string hintSource = ord?.OrderType == OutboundOrderType.StockTransferOrder
                    ? "TransferDestinationFacility"
                    : "CustomerDefault";

                res = EvaluateField(
                    addr.CountryCode, addr.StateOrProvince, addr.PostalCode,
                    verifiedParentCountry: parentHint, parentHintSource: hintSource,
                    timeZoneHint: null, allowTimeZoneInference: false, requireVerifiedParentForInference: true,
                    isHistoricalImmutable: isHistorical, out changed, out src, out reason);
            }

            var origRaw = addr.CountryCode;
            addr.CountryNormalizationStatus = res.Status;

            if (res.IsResolved && res.TargetCode != null)
            {
                if (isHistorical) preservedHistorical++;
                else if (changed)
                {
                    addr.CountryCode = res.TargetCode;
                    mutated++;
                }
                else unchanged++;

                if (res.WasInferred) inferredCount++;
            }
            else quarantined++;

            ReconcileAudit(nameof(ShippingAddress), addr.Id, nameof(ShippingAddress.CountryCode), origRaw, res.TargetCode,
                res.Status, isHistorical, res.WasInferred, src, reason, ord?.FacilityId, ord?.ClientId, res.NeedsInitialAudit);
        }

        // 5. ReturnShipments (Dual origin & destination; mutable ONLY while Authorized & pre-label/pre-handover)
        var activeAddrByOrder = shippingAddresses
            .GroupBy(sa => sa.OutboundOrderId)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var ret in allReturnShipments)
        {
            bool isHistorical = ret.Status != ReturnShipmentStatus.Authorized ||
                                ret.LabelGeneratedAtUtc.HasValue ||
                                ret.CarrierHandoverAtUtc.HasValue;

            string? origFacCountry = facilityMap.TryGetValue(ret.OriginFacilityId, out var oFac) &&
                                     oFac.IsActive &&
                                     oFac.CountryNormalizationStatus == CountryNormalizationStatus.Normalized &&
                                     catalog.TryGetCanonicalCountry(oFac.CountryCode, out var ofNorm, out _)
                ? ofNorm
                : null;
            string? destFacCountry = facilityMap.TryGetValue(ret.DestinationFacilityId, out var dFac) &&
                                     dFac.IsActive &&
                                     dFac.CountryNormalizationStatus == CountryNormalizationStatus.Normalized &&
                                     catalog.TryGetCanonicalCountry(dFac.CountryCode, out var dfNorm, out _)
                ? dfNorm
                : null;

            string? orderDestCountry = activeAddrByOrder.TryGetValue(ret.OrderId, out var origAddr) &&
                                       !origAddr.IsDeleted &&
                                       origAddr.CountryNormalizationStatus == CountryNormalizationStatus.Normalized &&
                                       catalog.TryGetCanonicalCountry(origAddr.CountryCode, out var oaNorm, out _)
                ? oaNorm
                : null;

            bool retCrossBorder = ret.OriginFacilityId != ret.DestinationFacilityId &&
                                  origFacCountry != null && destFacCountry != null &&
                                  !string.Equals(origFacCountry, destFacCountry, StringComparison.OrdinalIgnoreCase);

            // OriginCountry (never inherit DestinationFacility country!)
            totalEvaluated++;
            (bool IsResolved, string? TargetCode, CountryNormalizationStatus Status, bool WasInferred, bool NeedsInitialAudit) origRes;
            bool origChanged = false;
            string? origSrc;
            string origReason;

            if (retCrossBorder && string.IsNullOrWhiteSpace(ret.OriginCountry))
            {
                origSrc = "CrossBorderReturnContext";
                origReason = "Cross-border return shipment with blank OriginCountry cannot inherit DestinationFacility country.";
                origRes = (false, null, CountryNormalizationStatus.PendingNormalization, false, true);
            }
            else if (string.IsNullOrWhiteSpace(ret.OriginCountry) && activeAddrByOrder.TryGetValue(ret.OrderId, out var unverifiedOrig) &&
                     unverifiedOrig.CountryNormalizationStatus != CountryNormalizationStatus.Normalized)
            {
                origSrc = "QuarantinedOrderAddress";
                origReason = "Quarantined original order shipping address cannot verify return OriginCountry.";
                origRes = (false, null, CountryNormalizationStatus.PendingNormalization, false, true);
            }
            else
            {
                origRes = EvaluateField(
                    ret.OriginCountry, ret.OriginState, ret.OriginPostalCode,
                    verifiedParentCountry: orderDestCountry, parentHintSource: "OriginalOrderShippingAddress",
                    timeZoneHint: null, allowTimeZoneInference: false, requireVerifiedParentForInference: true,
                    isHistoricalImmutable: isHistorical, out origChanged, out origSrc, out origReason);
            }

            var rawOrigin = ret.OriginCountry;
            if (origRes.IsResolved && origRes.TargetCode != null)
            {
                if (isHistorical) preservedHistorical++;
                else if (origChanged)
                {
                    ret.OriginCountry = origRes.TargetCode;
                    mutated++;
                }
                else unchanged++;
                if (origRes.WasInferred) inferredCount++;
            }
            else quarantined++;

            ReconcileAudit(nameof(ReturnShipment), ret.Id, nameof(ReturnShipment.OriginCountry), rawOrigin, origRes.TargetCode,
                origRes.Status, isHistorical, origRes.WasInferred, origSrc, origReason, ret.OriginFacilityId, ret.ClientId, origRes.NeedsInitialAudit);

            // DestinationCountry
            totalEvaluated++;
            var destRes = EvaluateField(
                ret.DestinationCountry, ret.DestinationState, ret.DestinationPostalCode,
                verifiedParentCountry: destFacCountry, parentHintSource: "ReturnDestinationFacility",
                timeZoneHint: null, allowTimeZoneInference: false, requireVerifiedParentForInference: true,
                isHistoricalImmutable: isHistorical, out bool destChanged, out var destSrc, out var destReason);

            var rawDest = ret.DestinationCountry;
            if (destRes.IsResolved && destRes.TargetCode != null)
            {
                if (isHistorical) preservedHistorical++;
                else if (destChanged)
                {
                    ret.DestinationCountry = destRes.TargetCode;
                    mutated++;
                }
                else unchanged++;
                if (destRes.WasInferred) inferredCount++;
            }
            else quarantined++;

            ReconcileAudit(nameof(ReturnShipment), ret.Id, nameof(ReturnShipment.DestinationCountry), rawDest, destRes.TargetCode,
                destRes.Status, isHistorical, destRes.WasInferred, destSrc, destReason, ret.DestinationFacilityId, ret.ClientId, destRes.NeedsInitialAudit);

            ret.CountryNormalizationStatus =
                origRes.Status == CountryNormalizationStatus.Normalized && destRes.Status == CountryNormalizationStatus.Normalized
                    ? CountryNormalizationStatus.Normalized
                    : CountryNormalizationStatus.PendingNormalization;
        }

        // 6. CarrierContractualSlaPolicy (Mutable when IsActive, historical when !IsActive; never infer destination from OriginFacility)
        foreach (var pol in allSlaPolicies)
        {
            totalEvaluated++;
            bool isHistorical = !pol.IsActive;
            var res = EvaluateField(
                pol.DestinationCountry, pol.DestinationStateOrRegion, pol.DestinationPostalCodePrefix,
                verifiedParentCountry: null, parentHintSource: "PolicyDestinationGeo",
                timeZoneHint: null, allowTimeZoneInference: false, requireVerifiedParentForInference: false,
                isHistoricalImmutable: isHistorical, out bool changed, out var src, out var reason);

            var rawPol = pol.DestinationCountry;
            pol.CountryNormalizationStatus = res.Status;
            if (res.IsResolved && res.TargetCode != null)
            {
                if (isHistorical) preservedHistorical++;
                else if (changed)
                {
                    pol.DestinationCountry = res.TargetCode;
                    mutated++;
                }
                else unchanged++;
                if (res.WasInferred) inferredCount++;
            }
            else quarantined++;

            ReconcileAudit(nameof(CarrierContractualSlaPolicy), pol.Id, nameof(CarrierContractualSlaPolicy.DestinationCountry),
                rawPol, res.TargetCode, res.Status, isHistorical, res.WasInferred, src, reason, pol.OriginFacilityId, pol.ClientId, res.NeedsInitialAudit);
        }

        // 7. CarrierSlaComplianceRecord (Always Immutable Historical Shipment Evidence)
        foreach (var rec in allSlaRecords)
        {
            totalEvaluated++;
            var res = EvaluateField(
                rec.DestinationCountry, rec.DestinationStateOrRegion, rec.DestinationPostalCodePrefix,
                verifiedParentCountry: null, parentHintSource: "SlaHistoricalGeo",
                timeZoneHint: null, allowTimeZoneInference: false, requireVerifiedParentForInference: false,
                isHistoricalImmutable: true, out _, out var src, out var reason);

            rec.CountryNormalizationStatus = res.Status;
            if (res.IsResolved && res.TargetCode != null)
            {
                preservedHistorical++;
                if (res.WasInferred) inferredCount++;
            }
            else quarantined++;

            ReconcileAudit(nameof(CarrierSlaComplianceRecord), rec.Id, nameof(CarrierSlaComplianceRecord.DestinationCountry),
                rec.DestinationCountry, res.TargetCode, res.Status, true, res.WasInferred, src, reason, rec.FacilityId, rec.ClientId, res.NeedsInitialAudit);
        }

        if (_db is ApplicationDbContext backfillDb)
        {
            backfillDb.IsInsideNormalizationBackfill = true;
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            finally
            {
                backfillDb.IsInsideNormalizationBackfill = false;
            }
        }
        else
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new LegacyCountryBackfillSummary(
            batchId,
            totalEvaluated,
            mutated,
            unchanged,
            preservedHistorical,
            inferredCount,
            quarantined);
    }

    private sealed class CatalogIndex
    {
        private readonly Dictionary<string, CountryProfile> _profilesByCode;
        private readonly Dictionary<string, CountryProfile> _profilesByAlpha3;
        private readonly Dictionary<string, CountryProfile> _profilesByName;
        private readonly Dictionary<string, CountryNormalizationAlias> _aliasesByKey;
        private readonly Dictionary<string, HashSet<string>> _divisionCountriesByToken;
        private readonly Dictionary<string, string> _timeZoneCountries;

        public bool HasActiveProfiles => _profilesByCode.Count > 0;
        public HashSet<string> VerifiedCatalogKeys { get; }

        private CatalogIndex(
            Dictionary<string, CountryProfile> profilesByCode,
            Dictionary<string, CountryProfile> profilesByAlpha3,
            Dictionary<string, CountryProfile> profilesByName,
            Dictionary<string, CountryNormalizationAlias> aliasesByKey,
            Dictionary<string, HashSet<string>> divisionCountriesByToken,
            Dictionary<string, string> timeZoneCountries,
            HashSet<string> verifiedCatalogKeys)
        {
            _profilesByCode = profilesByCode;
            _profilesByAlpha3 = profilesByAlpha3;
            _profilesByName = profilesByName;
            _aliasesByKey = aliasesByKey;
            _divisionCountriesByToken = divisionCountriesByToken;
            _timeZoneCountries = timeZoneCountries;
            VerifiedCatalogKeys = verifiedCatalogKeys;
        }

        public static async Task<CatalogIndex> LoadAsync(IApplicationDbContext db, CancellationToken ct)
        {
            var profiles = await db.CountryProfiles.AsNoTracking().Where(c => c.IsActive).ToListAsync(ct);
            var aliases = await db.CountryNormalizationAliases.AsNoTracking().Where(a => a.IsActive).ToListAsync(ct);
            var divisions = await db.AdministrativeDivisions.AsNoTracking().Where(d => d.IsActive).ToListAsync(ct);
            var timeZones = await db.TimeZoneProfiles.AsNoTracking().Where(t => t.IsActive && t.CountryCode != null).ToListAsync(ct);

            var byCode = profiles.ToDictionary(p => p.CountryCode.Trim().ToUpperInvariant(), p => p, StringComparer.OrdinalIgnoreCase);
            var byAlpha3 = profiles.ToDictionary(p => p.IsoAlpha3Code.Trim().ToUpperInvariant(), p => p, StringComparer.OrdinalIgnoreCase);
            var byName = profiles.ToDictionary(p => CountryNormalizationAlias.NormalizeLookupKey(p.Name), p => p, StringComparer.OrdinalIgnoreCase);
            var aliasMap = new Dictionary<string, CountryNormalizationAlias>(StringComparer.OrdinalIgnoreCase);
            foreach (var alias in aliases)
            {
                var normCode = alias.CountryCode.Trim().ToUpperInvariant();
                if (byCode.ContainsKey(normCode))
                {
                    aliasMap[CountryNormalizationAlias.NormalizeLookupKey(alias.AliasKey)] = alias;
                }
            }

            var divMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            void AddDivToken(string? token, string countryCode)
            {
                var norm = CountryNormalizationAlias.NormalizeLookupKey(token);
                if (string.IsNullOrWhiteSpace(norm)) return;
                if (!divMap.TryGetValue(norm, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    divMap[norm] = set;
                }
                set.Add(countryCode);
            }

            foreach (var div in divisions)
            {
                var c = div.CountryCode.Trim().ToUpperInvariant();
                if (!byCode.ContainsKey(c)) continue;
                AddDivToken(div.Code, c);
                AddDivToken(div.IsoCode, c);
                AddDivToken(div.Name, c);
            }

            var tzMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tz in timeZones)
            {
                if (!string.IsNullOrWhiteSpace(tz.CountryCode) && byCode.ContainsKey(tz.CountryCode.Trim().ToUpperInvariant()))
                {
                    tzMap[tz.TimeZoneId.Trim()] = tz.CountryCode.Trim().ToUpperInvariant();
                }
            }

            var verifiedKeys = CountryDiagnosticSanitizer.BuildVerifiedCatalogKeySet(profiles, aliases);
            return new CatalogIndex(byCode, byAlpha3, byName, aliasMap, divMap, tzMap, verifiedKeys);
        }

        public bool TryGetCanonicalCountry(string? raw, out string? code, out string source)
        {
            code = null;
            source = string.Empty;
            var key = CountryNormalizationAlias.NormalizeLookupKey(raw);
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            if (_profilesByCode.TryGetValue(key, out var pCode))
            {
                code = pCode.CountryCode;
                source = "CatalogExact";
                return true;
            }

            if (_profilesByAlpha3.TryGetValue(key, out var pAlpha3))
            {
                code = pAlpha3.CountryCode;
                source = "CatalogIsoAlpha3";
                return true;
            }

            if (_profilesByName.TryGetValue(key, out var pName))
            {
                code = pName.CountryCode;
                source = "CatalogName";
                return true;
            }

            if (_aliasesByKey.TryGetValue(key, out var alias))
            {
                code = alias.CountryCode;
                source = $"CatalogAlias:{alias.SourceStandard}";
                return true;
            }

            return false;
        }

        public CountryNormalizationResolution ResolveExplicit(string? rawCountry)
        {
            if (string.IsNullOrWhiteSpace(rawCountry))
            {
                return new CountryNormalizationResolution(false, null, CountryNormalizationStatus.PendingNormalization, false, null,
                    "Blank or null country code cannot be defaulted.");
            }

            if (TryGetCanonicalCountry(rawCountry, out var code, out var source))
            {
                return new CountryNormalizationResolution(true, code, CountryNormalizationStatus.Normalized, false, source,
                    $"Resolved to '{code}' via {source}.");
            }

            return new CountryNormalizationResolution(false, null, CountryNormalizationStatus.PendingNormalization, false, null,
                "Unrecognized country token not found in active CountryProfile or CountryNormalizationAlias catalog.");
        }

        public CountryNormalizationResolution EvaluateWithGeoAndParent(
            string? rawCountry,
            string? stateOrProvince,
            string? postalCode,
            string? verifiedParentCountry,
            string parentHintSource,
            string? timeZoneHint,
            bool allowTimeZoneInference,
            bool requireVerifiedParentForInference)
        {
            string? tzCountry = allowTimeZoneInference && !string.IsNullOrWhiteSpace(timeZoneHint) &&
                                _timeZoneCountries.TryGetValue(timeZoneHint.Trim(), out var tzc)
                ? tzc
                : null;

            // Explicit country value branch
            if (!string.IsNullOrWhiteSpace(rawCountry))
            {
                if (!TryGetCanonicalCountry(rawCountry, out var code, out var source) || code == null)
                {
                    return new CountryNormalizationResolution(false, null, CountryNormalizationStatus.PendingNormalization, false, null,
                        "Unrecognized country token not found in active CountryProfile or CountryNormalizationAlias catalog.");
                }

                // R02: Timezone conflict on Facility when TimeZoneProfile is country-bound
                if (allowTimeZoneInference && tzCountry != null && !string.Equals(tzCountry, code, StringComparison.OrdinalIgnoreCase))
                {
                    return new CountryNormalizationResolution(false, null, CountryNormalizationStatus.PendingNormalization, false, null,
                        $"Conflicting timezone evidence: configured timezone maps to '{tzCountry}' while country indicates '{code}'.");
                }

                // Detect geographic conflict when subdivision exclusively matches another active country AND postal fails claimed country or matches division country
                var divCountry = ResolveUniqueDivisionCountry(stateOrProvince, out _);
                if (divCountry != null && !string.Equals(divCountry, code, StringComparison.OrdinalIgnoreCase))
                {
                    bool claimedPostalValid = _profilesByCode[code].IsSyntacticallyValidPostalCode(postalCode, out _);
                    bool divPostalValid = _profilesByCode.TryGetValue(divCountry, out var divProfile) &&
                                          divProfile.IsSyntacticallyValidPostalCode(postalCode, out _);
                    if (!claimedPostalValid || divPostalValid)
                    {
                        return new CountryNormalizationResolution(false, null, CountryNormalizationStatus.PendingNormalization, false, null,
                            $"Conflicting geographic evidence: subdivision matches '{divCountry}' while country indicates '{code}'.");
                    }
                }

                return new CountryNormalizationResolution(true, code, CountryNormalizationStatus.Normalized, false, source,
                    $"Normalized to canonical '{code}' via {source}.");
            }

            // Blank/null country branch — strictly requires deterministic domestic geo + non-facility verified parent context
            if (string.IsNullOrWhiteSpace(stateOrProvince) || string.IsNullOrWhiteSpace(postalCode))
            {
                return new CountryNormalizationResolution(false, null, CountryNormalizationStatus.PendingNormalization, false, null,
                    "Blank/null country lacks both verified domestic subdivision and postal code evidence.");
            }

            var divResolved = ResolveUniqueDivisionCountry(stateOrProvince, out var divMatches);
            if (divMatches.Count > 1)
            {
                return new CountryNormalizationResolution(false, null, CountryNormalizationStatus.PendingNormalization, false, null,
                    $"Ambiguous subdivision matches multiple active countries ({string.Join(",", divMatches)}).");
            }

            string? candidate = null;
            string? effectiveSource = null;

            if (!string.IsNullOrWhiteSpace(verifiedParentCountry))
            {
                if (divResolved != null && !string.Equals(divResolved, verifiedParentCountry, StringComparison.OrdinalIgnoreCase))
                {
                    return new CountryNormalizationResolution(false, null, CountryNormalizationStatus.PendingNormalization, false, null,
                        $"Conflicting evidence: subdivision belongs to '{divResolved}' while verified parent '{parentHintSource}' indicates '{verifiedParentCountry}'.");
                }

                if (divResolved == null)
                {
                    return new CountryNormalizationResolution(false, null, CountryNormalizationStatus.PendingNormalization, false, null,
                        "Unverified subdivision prevents deterministic parent-context country inference.");
                }

                candidate = divResolved;
                effectiveSource = $"{parentHintSource}+AdministrativeDivision+PostalCode";
            }
            else if (!requireVerifiedParentForInference && divResolved != null)
            {
                if (tzCountry != null && !string.Equals(tzCountry, divResolved, StringComparison.OrdinalIgnoreCase))
                {
                    return new CountryNormalizationResolution(false, null, CountryNormalizationStatus.PendingNormalization, false, null,
                        $"Conflicting timezone evidence: configured timezone maps to '{tzCountry}' while subdivision belongs to '{divResolved}'.");
                }

                candidate = divResolved;
                effectiveSource = tzCountry != null
                    ? "TimeZoneProfile+AdministrativeDivision+PostalCode"
                    : "AdministrativeDivision+PostalCode";
            }

            if (candidate == null || !_profilesByCode.TryGetValue(candidate, out var profile))
            {
                return new CountryNormalizationResolution(false, null, CountryNormalizationStatus.PendingNormalization, false, null,
                    "Orphan or unverified blank country without verified parent and domestic geographic evidence.");
            }

            if (!profile.IsSyntacticallyValidPostalCode(postalCode, out _))
            {
                return new CountryNormalizationResolution(false, null, CountryNormalizationStatus.PendingNormalization, false, null,
                    $"Unsupported or conflicting postal code syntax for inferred country '{candidate}'.");
            }

            return new CountryNormalizationResolution(true, candidate, CountryNormalizationStatus.Normalized, true, effectiveSource,
                $"Deterministically inferred '{candidate}' via {effectiveSource}.");
        }

        private string? ResolveUniqueDivisionCountry(string? stateOrProvince, out List<string> matchingCountries)
        {
            matchingCountries = [];
            var norm = CountryNormalizationAlias.NormalizeLookupKey(stateOrProvince);
            if (string.IsNullOrWhiteSpace(norm) || !_divisionCountriesByToken.TryGetValue(norm, out var set))
            {
                return null;
            }

            matchingCountries = set.OrderBy(c => c, StringComparer.Ordinal).ToList();
            return matchingCountries.Count == 1 ? matchingCountries[0] : null;
        }
    }
}
