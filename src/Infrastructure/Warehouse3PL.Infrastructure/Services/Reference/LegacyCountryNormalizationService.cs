using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Reference;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Infrastructure.Services.Reference;

public sealed class LegacyCountryNormalizationService : ILegacyCountryNormalizationService
{
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

    public async Task<LegacyCountryBackfillSummary> BackfillAllAsync(
        string? correlationBatchId = null,
        CancellationToken cancellationToken = default)
    {
        var batchId = string.IsNullOrWhiteSpace(correlationBatchId)
            ? $"MDM16-BACKFILL-{DateTime.UtcNow:yyyyMMddHHmmss}"
            : correlationBatchId.Trim();

        var utcNow = DateTime.UtcNow;
        var catalog = await CatalogIndex.LoadAsync(_db, cancellationToken);

        var existingAudits = await _db.CountryNormalizationAudits
            .ToListAsync(cancellationToken);
        var auditByKey = existingAudits.ToDictionary(
            a => (a.EntityType, a.EntityId, a.FieldName),
            a => a);

        var activeAccesses = await _db.ClientFacilityAccesses
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(cfa => cfa.Status == FacilityAccessStatus.Active)
            .Select(cfa => new { cfa.ClientId, cfa.FacilityId })
            .ToListAsync(cancellationToken);

        var singleFacilityByClient = activeAccesses
            .GroupBy(x => x.ClientId)
            .Where(g => g.Select(x => x.FacilityId).Distinct().Count() == 1)
            .ToDictionary(g => g.Key, g => (Guid?)g.First().FacilityId);

        var facilities = await _db.Facilities.IgnoreQueryFilters().Where(f => !f.IsDeleted).ToListAsync(cancellationToken);
        var facilityMap = facilities.ToDictionary(f => f.Id, f => f);

        var clientCountries = activeAccesses
            .Where(x => facilityMap.TryGetValue(x.FacilityId, out var fac) &&
                        catalog.TryGetCanonicalCountry(fac.CountryCode, out _, out _))
            .GroupBy(x => x.ClientId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => catalog.TryGetCanonicalCountry(facilityMap[x.FacilityId].CountryCode, out var c, out _) ? c! : string.Empty)
                    .Where(c => !string.IsNullOrEmpty(c))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList());

        int totalEvaluated = 0, mutated = 0, unchanged = 0, preservedHistorical = 0, inferredCount = 0, quarantined = 0;

        void RecordAudit(
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
            Guid? clientId)
        {
            var key = (entityType, entityId, fieldName);
            if (auditByKey.TryGetValue(key, out var existing))
            {
                // Preserve initial raw pre-normalization value across idempotent re-runs
                existing.NormalizedCountryCode = resolvedCode;
                existing.NormalizationStatus = status;
                existing.PreservedHistoricalValue = preservedHistoricalVal;
                existing.WasInferredFromParent = wasInferred;
                existing.InferenceSource = inferenceSource;
                existing.Reason = reason.Length > 500 ? reason[..500] : reason;
                existing.FacilityId = facilityId;
                existing.ClientId = clientId;
                existing.CorrelationBatchId = batchId;
                existing.RecordedAtUtc = utcNow;
            }
            else
            {
                var audit = new CountryNormalizationAudit
                {
                    Id = Guid.NewGuid(),
                    EntityType = entityType,
                    EntityId = entityId,
                    FieldName = fieldName,
                    OriginalCountryValue = rawValue,
                    NormalizedCountryCode = resolvedCode,
                    NormalizationStatus = status,
                    PreservedHistoricalValue = preservedHistoricalVal,
                    WasInferredFromParent = wasInferred,
                    InferenceSource = inferenceSource,
                    Reason = reason.Length > 500 ? reason[..500] : reason,
                    FacilityId = facilityId,
                    ClientId = clientId,
                    CorrelationBatchId = batchId,
                    RecordedAtUtc = utcNow
                };
                auditByKey[key] = audit;
                _db.CountryNormalizationAudits.Add(audit);
            }
        }

        // Helper for single-field evaluation + audit decision
        (bool IsResolved, string? TargetCode, CountryNormalizationStatus Status, bool WasInferred, bool ShouldWriteAudit) EvaluateField(
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
                    reason = $"Immutable historical record preserved untouched ('{rawValue ?? "NULL"}'); {eval.Reason}";
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

            if (res.ShouldWriteAudit)
            {
                RecordAudit(nameof(Facility), fac.Id, nameof(Facility.CountryCode), origRaw, res.TargetCode,
                    res.Status, false, res.WasInferred, src, reason, fac.Id, null);
            }
        }

        // Refresh client countries map after Facilities pass
        clientCountries = activeAccesses
            .Where(x => facilityMap.TryGetValue(x.FacilityId, out var fac) &&
                        catalog.TryGetCanonicalCountry(fac.CountryCode, out _, out _))
            .GroupBy(x => x.ClientId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => catalog.TryGetCanonicalCountry(facilityMap[x.FacilityId].CountryCode, out var c, out _) ? c! : string.Empty)
                    .Where(c => !string.IsNullOrEmpty(c))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList());

        // 2. Customers (Mutable Master Data)
        var customers = await _db.Customers.IgnoreQueryFilters().Where(c => !c.IsDeleted).ToListAsync(cancellationToken);
        var customerMap = customers.ToDictionary(c => c.Id, c => c);
        foreach (var cust in customers)
        {
            totalEvaluated++;
            string? singleClientCountry = clientCountries.TryGetValue(cust.ClientId, out var cc) && cc.Count == 1 ? cc[0] : null;
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

            if (res.ShouldWriteAudit)
            {
                singleFacilityByClient.TryGetValue(cust.ClientId, out var custFacId);
                RecordAudit(nameof(Customer), cust.Id, nameof(Customer.DefaultCountryCode), origRaw, res.TargetCode,
                    res.Status, false, res.WasInferred, src, reason, custFacId, cust.ClientId);
            }
        }

        // 3. Suppliers (Mutable Master Data)
        var suppliers = await _db.Suppliers.IgnoreQueryFilters().Where(s => !s.IsDeleted).ToListAsync(cancellationToken);
        foreach (var sup in suppliers)
        {
            totalEvaluated++;
            string? singleClientCountry = clientCountries.TryGetValue(sup.ClientId, out var sc) && sc.Count == 1 ? sc[0] : null;
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

            if (res.ShouldWriteAudit)
            {
                singleFacilityByClient.TryGetValue(sup.ClientId, out var supFacId);
                RecordAudit(nameof(Supplier), sup.Id, nameof(Supplier.Country), origRaw, res.TargetCode,
                    res.Status, false, res.WasInferred, src, reason, supFacId, sup.ClientId);
            }
        }

        // 4. ShippingAddresses (Mutable pre-fulfillment vs Immutable historical snapshot)
        var orderIdsWithShipments = (await _db.Packages
            .IgnoreQueryFilters()
            .Where(p => p.ShipmentId != null)
            .Select(p => p.OutboundOrderId)
            .Distinct()
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var orders = await _db.OutboundOrders
            .IgnoreQueryFilters()
            .Where(o => !o.IsDeleted)
            .Select(o => new
            {
                o.Id,
                o.FacilityId,
                o.ClientId,
                o.CustomerId,
                o.Status
            })
            .ToDictionaryAsync(o => o.Id, cancellationToken);

        var shippingAddresses = await _db.ShippingAddresses.IgnoreQueryFilters().Where(s => !s.IsDeleted).ToListAsync(cancellationToken);
        foreach (var addr in shippingAddresses)
        {
            totalEvaluated++;
            orders.TryGetValue(addr.OutboundOrderId, out var ord);
            bool hasShipment = ord != null && orderIdsWithShipments.Contains(ord.Id);
            bool isHistorical = ord == null ||
                hasShipment ||
                ord.Status is OutboundOrderStatus.Allocated or OutboundOrderStatus.PartiallyAllocated or OutboundOrderStatus.InProgress or OutboundOrderStatus.Completed or OutboundOrderStatus.Cancelled;

            string? customerCountry = ord?.CustomerId != null && customerMap.TryGetValue(ord.CustomerId.Value, out var cust) &&
                                      catalog.TryGetCanonicalCountry(cust.DefaultCountryCode, out var ccNorm, out _)
                ? ccNorm
                : null;
            string? facilityCountry = ord != null && facilityMap.TryGetValue(ord.FacilityId, out var fac) &&
                                      catalog.TryGetCanonicalCountry(fac.CountryCode, out var fcNorm, out _)
                ? fcNorm
                : null;

            // Cross-border conflict if verified Customer default differs from Facility country when ShippingAddress is blank
            string? parentHint;
            string hintSource;
            bool crossBorderConflict = !string.IsNullOrEmpty(customerCountry) &&
                                       !string.IsNullOrEmpty(facilityCountry) &&
                                       !string.Equals(customerCountry, facilityCountry, StringComparison.OrdinalIgnoreCase);

            if (crossBorderConflict && string.IsNullOrWhiteSpace(addr.CountryCode))
            {
                parentHint = "__CONFLICT__";
                hintSource = "CrossBorderConflict";
            }
            else if (!string.IsNullOrEmpty(customerCountry))
            {
                parentHint = customerCountry;
                hintSource = "CustomerDefault";
            }
            else
            {
                parentHint = facilityCountry;
                hintSource = "OrderFacility";
            }

            (bool IsResolved, string? TargetCode, CountryNormalizationStatus Status, bool WasInferred, bool ShouldWriteAudit) res;
            bool changed;
            string? src;
            string reason;

            if (parentHint == "__CONFLICT__")
            {
                changed = false;
                src = "CrossBorderOrderContext";
                reason = $"Cross-border order context (Customer='{customerCountry}', Facility='{facilityCountry}') with blank shipping country cannot inherit a country safely.";
                res = (false, null, CountryNormalizationStatus.PendingNormalization, false, true);
            }
            else
            {
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
                if (isHistorical)
                {
                    preservedHistorical++;
                }
                else if (changed)
                {
                    addr.CountryCode = res.TargetCode;
                    mutated++;
                }
                else unchanged++;

                if (res.WasInferred) inferredCount++;
            }
            else quarantined++;

            if (res.ShouldWriteAudit)
            {
                RecordAudit(nameof(ShippingAddress), addr.Id, nameof(ShippingAddress.CountryCode), origRaw, res.TargetCode,
                    res.Status, isHistorical, res.WasInferred, src, reason, ord?.FacilityId, ord?.ClientId);
            }
        }

        // 5. ReturnShipments (Dual origin & destination; mutable ONLY while Authorized & pre-label/pre-handover)
        var returnShipments = await _db.ReturnShipments.IgnoreQueryFilters().ToListAsync(cancellationToken);
        var orderAddrMap = shippingAddresses
            .GroupBy(sa => sa.OutboundOrderId)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var ret in returnShipments)
        {
            bool isHistorical = ret.Status != ReturnShipmentStatus.Authorized ||
                                ret.LabelGeneratedAtUtc.HasValue ||
                                ret.CarrierHandoverAtUtc.HasValue;

            string? origFacCountry = facilityMap.TryGetValue(ret.OriginFacilityId, out var oFac) &&
                                     catalog.TryGetCanonicalCountry(oFac.CountryCode, out var ofNorm, out _)
                ? ofNorm
                : null;
            string? destFacCountry = facilityMap.TryGetValue(ret.DestinationFacilityId, out var dFac) &&
                                     catalog.TryGetCanonicalCountry(dFac.CountryCode, out var dfNorm, out _)
                ? dfNorm
                : null;

            string? orderDestCountry = orderAddrMap.TryGetValue(ret.OrderId, out var origAddr) &&
                                       catalog.TryGetCanonicalCountry(origAddr.CountryCode, out var oaNorm, out _)
                ? oaNorm
                : null;

            bool retCrossFacility = ret.OriginFacilityId != ret.DestinationFacilityId &&
                                    !string.Equals(origFacCountry, destFacCountry, StringComparison.OrdinalIgnoreCase);

            // OriginCountry (never inherit DestinationFacility country!)
            totalEvaluated++;
            string? originParentHint = retCrossFacility && string.IsNullOrWhiteSpace(ret.OriginCountry) ? "__CROSS_BORDER__" : orderDestCountry;
            (bool IsResolved, string? TargetCode, CountryNormalizationStatus Status, bool WasInferred, bool ShouldWriteAudit) origRes;
            bool origChanged = false;
            string? origSrc = null;
            string origReason;

            if (originParentHint == "__CROSS_BORDER__")
            {
                origReason = "Cross-border return shipment with blank OriginCountry cannot inherit DestinationFacility country.";
                origRes = (false, null, CountryNormalizationStatus.PendingNormalization, false, true);
            }
            else
            {
                origRes = EvaluateField(
                    ret.OriginCountry, ret.OriginState, ret.OriginPostalCode,
                    verifiedParentCountry: originParentHint, parentHintSource: "OriginalOrderShippingAddress",
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

            if (origRes.ShouldWriteAudit)
            {
                RecordAudit(nameof(ReturnShipment), ret.Id, nameof(ReturnShipment.OriginCountry), rawOrigin, origRes.TargetCode,
                    origRes.Status, isHistorical, origRes.WasInferred, origSrc, origReason, ret.OriginFacilityId, ret.ClientId);
            }

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

            if (destRes.ShouldWriteAudit)
            {
                RecordAudit(nameof(ReturnShipment), ret.Id, nameof(ReturnShipment.DestinationCountry), rawDest, destRes.TargetCode,
                    destRes.Status, isHistorical, destRes.WasInferred, destSrc, destReason, ret.DestinationFacilityId, ret.ClientId);
            }

            ret.CountryNormalizationStatus =
                origRes.Status == CountryNormalizationStatus.Normalized && destRes.Status == CountryNormalizationStatus.Normalized
                    ? CountryNormalizationStatus.Normalized
                    : CountryNormalizationStatus.PendingNormalization;
        }

        // 6. CarrierContractualSlaPolicy (Mutable when IsActive, historical when !IsActive; never infer destination from OriginFacility)
        var slaPolicies = await _db.CarrierContractualSlaPolicies.IgnoreQueryFilters().ToListAsync(cancellationToken);
        foreach (var pol in slaPolicies)
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

            if (res.ShouldWriteAudit)
            {
                RecordAudit(nameof(CarrierContractualSlaPolicy), pol.Id, nameof(CarrierContractualSlaPolicy.DestinationCountry),
                    rawPol, res.TargetCode, res.Status, isHistorical, res.WasInferred, src, reason, pol.OriginFacilityId, pol.ClientId);
            }
        }

        // 7. CarrierSlaComplianceRecord (Always Immutable Historical Shipment Evidence)
        var slaRecords = await _db.CarrierSlaComplianceRecords.IgnoreQueryFilters().ToListAsync(cancellationToken);
        foreach (var rec in slaRecords)
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

            if (res.ShouldWriteAudit)
            {
                RecordAudit(nameof(CarrierSlaComplianceRecord), rec.Id, nameof(CarrierSlaComplianceRecord.DestinationCountry),
                    rec.DestinationCountry, res.TargetCode, res.Status, true, res.WasInferred, src, reason, rec.FacilityId, rec.ClientId);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

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

        private CatalogIndex(
            Dictionary<string, CountryProfile> profilesByCode,
            Dictionary<string, CountryProfile> profilesByAlpha3,
            Dictionary<string, CountryProfile> profilesByName,
            Dictionary<string, CountryNormalizationAlias> aliasesByKey,
            Dictionary<string, HashSet<string>> divisionCountriesByToken,
            Dictionary<string, string> timeZoneCountries)
        {
            _profilesByCode = profilesByCode;
            _profilesByAlpha3 = profilesByAlpha3;
            _profilesByName = profilesByName;
            _aliasesByKey = aliasesByKey;
            _divisionCountriesByToken = divisionCountriesByToken;
            _timeZoneCountries = timeZoneCountries;
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

            return new CatalogIndex(byCode, byAlpha3, byName, aliasMap, divMap, tzMap);
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
                    $"Resolved '{rawCountry.Trim()}' -> '{code}' via {source}.");
            }

            return new CountryNormalizationResolution(false, null, CountryNormalizationStatus.PendingNormalization, false, null,
                $"Unrecognized country value '{rawCountry.Trim()}' not found in active CountryProfile or CountryNormalizationAlias catalog.");
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
            // Explicit country value branch
            if (!string.IsNullOrWhiteSpace(rawCountry))
            {
                if (!TryGetCanonicalCountry(rawCountry, out var code, out var source) || code == null)
                {
                    return new CountryNormalizationResolution(false, null, CountryNormalizationStatus.PendingNormalization, false, null,
                        $"Unrecognized country value '{rawCountry.Trim()}' not found in active CountryProfile or CountryNormalizationAlias catalog.");
                }

                // Detect direct geographic conflict when subdivision exclusively matches another active country AND postal fails claimed country
                var divCountry = ResolveUniqueDivisionCountry(stateOrProvince, out _);
                if (divCountry != null && !string.Equals(divCountry, code, StringComparison.OrdinalIgnoreCase))
                {
                    bool claimedPostalValid = _profilesByCode[code].IsSyntacticallyValidPostalCode(postalCode, out _);
                    bool divPostalValid = _profilesByCode.TryGetValue(divCountry, out var divProfile) &&
                                          divProfile.IsSyntacticallyValidPostalCode(postalCode, out _);
                    if (!claimedPostalValid || divPostalValid)
                    {
                        return new CountryNormalizationResolution(false, null, CountryNormalizationStatus.PendingNormalization, false, null,
                            $"Conflicting geographic evidence: state/province '{stateOrProvince?.Trim()}' matches '{divCountry}' while country indicates '{code}'.");
                    }
                }

                return new CountryNormalizationResolution(true, code, CountryNormalizationStatus.Normalized, false, source,
                    $"Mapped '{rawCountry.Trim()}' -> '{code}' via {source}.");
            }

            // Blank/null country branch — strictly requires deterministic domestic geo + non-facility parent context
            if (string.IsNullOrWhiteSpace(stateOrProvince) || string.IsNullOrWhiteSpace(postalCode))
            {
                return new CountryNormalizationResolution(false, null, CountryNormalizationStatus.PendingNormalization, false, null,
                    "Blank/null country lacks both domestic subdivision and postal code evidence; parent warehouse facility is never inherited blindly.");
            }

            var divResolved = ResolveUniqueDivisionCountry(stateOrProvince, out var divMatches);
            if (divMatches.Count > 1)
            {
                return new CountryNormalizationResolution(false, null, CountryNormalizationStatus.PendingNormalization, false, null,
                    $"Ambiguous subdivision '{stateOrProvince.Trim()}' matches multiple countries ({string.Join(",", divMatches)}).");
            }

            string? tzCountry = allowTimeZoneInference && !string.IsNullOrWhiteSpace(timeZoneHint) &&
                                _timeZoneCountries.TryGetValue(timeZoneHint.Trim(), out var tzc)
                ? tzc
                : null;

            string? candidate = null;
            string? effectiveSource = null;

            if (!string.IsNullOrWhiteSpace(verifiedParentCountry))
            {
                if (divResolved != null && !string.Equals(divResolved, verifiedParentCountry, StringComparison.OrdinalIgnoreCase))
                {
                    return new CountryNormalizationResolution(false, null, CountryNormalizationStatus.PendingNormalization, false, null,
                        $"Conflicting evidence: subdivision '{stateOrProvince.Trim()}' belongs to '{divResolved}' while parent context '{parentHintSource}' indicates '{verifiedParentCountry}'.");
                }

                if (divResolved == null)
                {
                    return new CountryNormalizationResolution(false, null, CountryNormalizationStatus.PendingNormalization, false, null,
                        $"Unverified subdivision '{stateOrProvince.Trim()}' prevents safe parent-context country inference.");
                }

                candidate = divResolved;
                effectiveSource = $"{parentHintSource}+AdministrativeDivision+PostalCode";
            }
            else if (!requireVerifiedParentForInference && divResolved != null)
            {
                if (tzCountry != null && !string.Equals(tzCountry, divResolved, StringComparison.OrdinalIgnoreCase))
                {
                    return new CountryNormalizationResolution(false, null, CountryNormalizationStatus.PendingNormalization, false, null,
                        $"Conflicting evidence: timezone '{timeZoneHint}' indicates '{tzCountry}' while subdivision '{stateOrProvince.Trim()}' belongs to '{divResolved}'.");
                }

                candidate = divResolved;
                effectiveSource = tzCountry != null
                    ? "TimeZoneProfile+AdministrativeDivision+PostalCode"
                    : "AdministrativeDivision+PostalCode";
            }

            if (candidate == null || !_profilesByCode.TryGetValue(candidate, out var profile))
            {
                return new CountryNormalizationResolution(false, null, CountryNormalizationStatus.PendingNormalization, false, null,
                    "Orphan or unverified blank country without deterministic parent and domestic geographic evidence.");
            }

            if (!profile.IsSyntacticallyValidPostalCode(postalCode, out _))
            {
                return new CountryNormalizationResolution(false, null, CountryNormalizationStatus.PendingNormalization, false, null,
                    $"Unsupported or conflicting postal code '{postalCode.Trim()}' for inferred country '{candidate}'.");
            }

            // Reject postal codes matching a different active country when candidate is ambiguous vs postal format
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
