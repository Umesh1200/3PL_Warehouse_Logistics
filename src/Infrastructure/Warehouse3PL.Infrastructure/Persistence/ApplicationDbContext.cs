using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
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
using Warehouse3PL.Domain.Entities.Reference;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
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
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> InMemoryComplianceLocks = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> InMemoryLegalEntityLocks = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> InMemoryFxRateLocks = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> InMemoryFacilityCalendarLocks = new();
    private static readonly SemaphoreSlim InMemoryGlobalHierarchyLock = new(1, 1);
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> InMemoryFacilityHierarchyLocks = new();
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> InMemoryLocationLifecycleLocks = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> InMemoryScopeEpochLocks = new(StringComparer.Ordinal);
    private bool _isInsideConfigurationOutboxSave;
    private bool? _hasMdm15Schema;

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
    public bool HasActiveTransaction => Database.CurrentTransaction != null || _currentTransaction != null;
    public bool AllowExplicitAuditTimestamps { get; set; } = false;
    internal bool IsInsideNormalizationBackfill { get; set; } = false;
    private bool _isInsideCountryLifecycleReconciliation = false;
    internal bool IsPrivilegedAnalyticsMaintenance => _currentUser.IsAuthenticated && _currentUser.IsSuperAdmin;
    private void EnforceJournalImmutability()
    {
        ChangeTracker.DetectChanges();
        if(ChangeTracker.Entries<AnalyticsIngestionJournal>().Any(x=>x.State==EntityState.Modified||x.State==EntityState.Deleted))
            throw new InvalidOperationException("Analytics ingestion journal entries are immutable.");
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceJournalImmutability();
        NormalizeWarehouseMasterEntities();
        if (_isInsideConfigurationOutboxSave || _isInsideCountryLifecycleReconciliation)
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        _isInsideCountryLifecycleReconciliation = true;
        try
        {
            ReconcileCountryNormalizationLifecycleSync();
        }
        finally
        {
            _isInsideCountryLifecycleReconciliation = false;
        }

        var pendingScopes = ConfigurationScopeMapper.ExtractImpactedScopes(ChangeTracker);
        if (pendingScopes.Count == 0)
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        if (!Database.IsRelational())
        {
            return SaveWithInMemoryScopeEpochsSync(pendingScopes, acceptAllChangesOnSuccess);
        }

        return SaveWithRelationalScopeEpochsSync(pendingScopes, acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnforceJournalImmutability();
        NormalizeWarehouseMasterEntities();
        if (_isInsideConfigurationOutboxSave || _isInsideCountryLifecycleReconciliation)
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        _isInsideCountryLifecycleReconciliation = true;
        try
        {
            await ReconcileCountryNormalizationLifecycleAsync(cancellationToken);
        }
        finally
        {
            _isInsideCountryLifecycleReconciliation = false;
        }

        var pendingScopes = ConfigurationScopeMapper.ExtractImpactedScopes(ChangeTracker);
        if (pendingScopes.Count == 0)
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        if (!Database.IsRelational())
        {
            return await SaveWithInMemoryScopeEpochsAsync(pendingScopes, acceptAllChangesOnSuccess, cancellationToken);
        }

        return await SaveWithRelationalScopeEpochsAsync(pendingScopes, acceptAllChangesOnSuccess, cancellationToken);
    }

    private static bool IsCountryLifecycleTarget(object entity) =>
        entity is Facility or Customer or Supplier or ShippingAddress or ReturnShipment
            or CarrierContractualSlaPolicy or CarrierSlaComplianceRecord;

    private sealed record RuntimeCatalogContext(
        Dictionary<string, CountryProfile> ProfilesByCode,
        HashSet<string> VerifiedCatalogKeys,
        Dictionary<string, HashSet<string>> DivisionCountriesByToken,
        Dictionary<string, string> TimeZoneCountries);

    private void ReconcileCountryNormalizationLifecycleSync()
    {
        var targets = ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted && IsCountryLifecycleTarget(e.Entity))
            .ToList();
        if (targets.Count == 0) return;

        var dbProfiles = CountryProfiles.AsNoTracking().Where(c => c.IsActive).ToList();
        var dbAliases = CountryNormalizationAliases.AsNoTracking().Where(a => a.IsActive).ToList();
        var dbDivisions = AdministrativeDivisions.AsNoTracking().Where(d => d.IsActive).ToList();
        var dbTimeZones = TimeZoneProfiles.AsNoTracking().Where(t => t.IsActive && t.CountryCode != null).ToList();

        var ctx = BuildRuntimeCatalogContext(dbProfiles, dbAliases, dbDivisions, dbTimeZones);

        var targetIds = targets.Select(t => ((BaseEntity<Guid>)t.Entity).Id).Distinct().ToList();
        var existingAudits = CountryNormalizationAudits
            .Where(a => targetIds.Contains(a.EntityId))
            .ToList();

        var orderIds = targets
            .Select(t => t.Entity)
            .OfType<ShippingAddress>()
            .Select(sa => sa.OutboundOrderId)
            .Distinct()
            .ToList();
        var orderScopes = new Dictionary<Guid, (Guid FacilityId, Guid ClientId)>();
        foreach (var localOrd in OutboundOrders.Local)
        {
            orderScopes[localOrd.Id] = (localOrd.FacilityId, localOrd.ClientId);
        }
        var missingOrderIds = orderIds.Where(id => !orderScopes.ContainsKey(id)).ToList();
        if (missingOrderIds.Count > 0)
        {
            foreach (var o in OutboundOrders.IgnoreQueryFilters().AsNoTracking()
                .Where(o => missingOrderIds.Contains(o.Id))
                .Select(o => new { o.Id, o.FacilityId, o.ClientId })
                .ToList())
            {
                orderScopes[o.Id] = (o.FacilityId, o.ClientId);
            }
        }

        ReconcileCountryEntries(targets, ctx, existingAudits, orderScopes);
    }

    private async Task ReconcileCountryNormalizationLifecycleAsync(CancellationToken cancellationToken)
    {
        var targets = ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted && IsCountryLifecycleTarget(e.Entity))
            .ToList();
        if (targets.Count == 0) return;

        var dbProfiles = await CountryProfiles.AsNoTracking().Where(c => c.IsActive).ToListAsync(cancellationToken);
        var dbAliases = await CountryNormalizationAliases.AsNoTracking().Where(a => a.IsActive).ToListAsync(cancellationToken);
        var dbDivisions = await AdministrativeDivisions.AsNoTracking().Where(d => d.IsActive).ToListAsync(cancellationToken);
        var dbTimeZones = await TimeZoneProfiles.AsNoTracking().Where(t => t.IsActive && t.CountryCode != null).ToListAsync(cancellationToken);

        var ctx = BuildRuntimeCatalogContext(dbProfiles, dbAliases, dbDivisions, dbTimeZones);

        var targetIds = targets.Select(t => ((BaseEntity<Guid>)t.Entity).Id).Distinct().ToList();
        var existingAudits = await CountryNormalizationAudits
            .Where(a => targetIds.Contains(a.EntityId))
            .ToListAsync(cancellationToken);

        var orderIds = targets
            .Select(t => t.Entity)
            .OfType<ShippingAddress>()
            .Select(sa => sa.OutboundOrderId)
            .Distinct()
            .ToList();
        var orderScopes = new Dictionary<Guid, (Guid FacilityId, Guid ClientId)>();
        foreach (var localOrd in OutboundOrders.Local)
        {
            orderScopes[localOrd.Id] = (localOrd.FacilityId, localOrd.ClientId);
        }
        var missingOrderIds = orderIds.Where(id => !orderScopes.ContainsKey(id)).ToList();
        if (missingOrderIds.Count > 0)
        {
            var dbOrders = await OutboundOrders.IgnoreQueryFilters().AsNoTracking()
                .Where(o => missingOrderIds.Contains(o.Id))
                .Select(o => new { o.Id, o.FacilityId, o.ClientId })
                .ToListAsync(cancellationToken);
            foreach (var o in dbOrders)
            {
                orderScopes[o.Id] = (o.FacilityId, o.ClientId);
            }
        }

        ReconcileCountryEntries(targets, ctx, existingAudits, orderScopes);
    }

    private RuntimeCatalogContext BuildRuntimeCatalogContext(
        List<CountryProfile> dbProfiles,
        List<CountryNormalizationAlias> dbAliases,
        List<AdministrativeDivision> dbDivisions,
        List<TimeZoneProfile> dbTimeZones)
    {
        var profilesByCode = new Dictionary<string, CountryProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in dbProfiles)
        {
            if (p.IsActive && !string.IsNullOrWhiteSpace(p.CountryCode))
            {
                profilesByCode[p.CountryCode.Trim().ToUpperInvariant()] = p;
            }
        }
        foreach (var localCp in ChangeTracker.Entries<CountryProfile>().Where(e => e.State != EntityState.Deleted))
        {
            var code = localCp.Entity.CountryCode.Trim().ToUpperInvariant();
            if (localCp.Entity.IsActive) profilesByCode[code] = localCp.Entity;
            else profilesByCode.Remove(code);
        }

        var mergedAliases = dbAliases.ToList();
        foreach (var localAl in ChangeTracker.Entries<CountryNormalizationAlias>().Where(e => e.State != EntityState.Deleted))
        {
            if (localAl.Entity.IsActive) mergedAliases.Add(localAl.Entity);
        }

        var verifiedKeys = CountryDiagnosticSanitizer.BuildVerifiedCatalogKeySet(profilesByCode.Values, mergedAliases);

        var divMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        void AddDivToken(string? token, string cc)
        {
            var norm = CountryNormalizationAlias.NormalizeLookupKey(token);
            if (string.IsNullOrWhiteSpace(norm)) return;
            if (!divMap.TryGetValue(norm, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                divMap[norm] = set;
            }
            set.Add(cc);
        }
        foreach (var div in dbDivisions)
        {
            var cc = div.CountryCode.Trim().ToUpperInvariant();
            if (!profilesByCode.ContainsKey(cc)) continue;
            AddDivToken(div.Code, cc);
            AddDivToken(div.IsoCode, cc);
            AddDivToken(div.Name, cc);
        }

        var tzMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tz in dbTimeZones)
        {
            if (!string.IsNullOrWhiteSpace(tz.CountryCode))
            {
                var cc = tz.CountryCode.Trim().ToUpperInvariant();
                if (profilesByCode.ContainsKey(cc))
                {
                    tzMap[tz.TimeZoneId.Trim()] = cc;
                }
            }
        }

        return new RuntimeCatalogContext(profilesByCode, verifiedKeys, divMap, tzMap);
    }

    private static bool TryCanonicalizeStrictIso2(string? raw, Dictionary<string, CountryProfile> profilesByCode, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrWhiteSpace(raw) || profilesByCode.Count == 0)
        {
            return false;
        }

        var trimmed = raw.Trim();
        if (trimmed.Length != 2 || !trimmed.All(char.IsAsciiLetter))
        {
            return false;
        }

        var upper = trimmed.ToUpperInvariant();
        if (!profilesByCode.ContainsKey(upper))
        {
            return false;
        }

        canonical = upper;
        return true;
    }

    private static bool IsExactCanonicalIso2(string? raw, Dictionary<string, CountryProfile> profilesByCode)
    {
        if (string.IsNullOrEmpty(raw) || raw.Length != 2 || profilesByCode.Count == 0)
        {
            return false;
        }

        return raw.All(char.IsAsciiLetterUpper) && profilesByCode.ContainsKey(raw);
    }

    private static string? CheckGeographicContradiction(
        string canonicalCode,
        string? state,
        string? postalCode,
        string? timeZoneId,
        bool checkTimeZone,
        RuntimeCatalogContext ctx)
    {
        if (checkTimeZone && !string.IsNullOrWhiteSpace(timeZoneId) &&
            ctx.TimeZoneCountries.TryGetValue(timeZoneId.Trim(), out var tzCountry) &&
            !string.Equals(tzCountry, canonicalCode, StringComparison.OrdinalIgnoreCase))
        {
            return $"Conflicting timezone evidence: configured timezone maps to '{tzCountry}' while country indicates '{canonicalCode}'.";
        }

        var divKey = CountryNormalizationAlias.NormalizeLookupKey(state);
        if (!string.IsNullOrWhiteSpace(divKey) &&
            ctx.DivisionCountriesByToken.TryGetValue(divKey, out var divCountries) &&
            divCountries.Count == 1)
        {
            var divCountry = divCountries.First();
            if (!string.Equals(divCountry, canonicalCode, StringComparison.OrdinalIgnoreCase))
            {
                var claimedPostalValid = ctx.ProfilesByCode.TryGetValue(canonicalCode, out var claimedProf) &&
                                         MatchesProfilePostal(claimedProf, postalCode);
                var divPostalValid = ctx.ProfilesByCode.TryGetValue(divCountry, out var divProf) &&
                                     MatchesProfilePostal(divProf, postalCode);
                if (!claimedPostalValid || divPostalValid)
                {
                    return $"Conflicting geographic evidence: subdivision matches '{divCountry}' while country indicates '{canonicalCode}'.";
                }
            }
        }

        return null;
    }

    private static bool MatchesProfilePostal(CountryProfile profile, string? postalCode)
    {
        if (string.IsNullOrWhiteSpace(postalCode)) return false;
        var p = postalCode.Trim();
        try
        {
            if (!string.IsNullOrWhiteSpace(profile.PostalCodeRegex) &&
                System.Text.RegularExpressions.Regex.IsMatch(p, profile.PostalCodeRegex, System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            {
                return true;
            }
            if (!string.IsNullOrWhiteSpace(profile.LegacyPostalCodeRegex) &&
                System.Text.RegularExpressions.Regex.IsMatch(p, profile.LegacyPostalCodeRegex, System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            {
                return true;
            }
        }
        catch
        {
        }
        return false;
    }

    private void ReconcileCountryEntries(
        IReadOnlyList<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry> targets,
        RuntimeCatalogContext ctx,
        List<CountryNormalizationAudit> existingAudits,
        Dictionary<Guid, (Guid FacilityId, Guid ClientId)> orderScopes)
    {
        var auditByKey = new Dictionary<(string EntityType, Guid EntityId, string FieldName), CountryNormalizationAudit>();
        foreach (var a in existingAudits)
        {
            auditByKey[(a.EntityType, a.EntityId, a.FieldName)] = a;
        }
        foreach (var localAudit in CountryNormalizationAudits.Local)
        {
            var localEntry = Entry(localAudit);
            if (localEntry.State != EntityState.Deleted)
            {
                auditByKey[(localAudit.EntityType, localAudit.EntityId, localAudit.FieldName)] = localAudit;
            }
        }

        var utcNow = DateTime.UtcNow;
        const string RuntimeBatchId = "RUNTIME-WRITE";

        foreach (var entry in targets)
        {
            var entityId = ((BaseEntity<Guid>)entry.Entity).Id;
            string entityName = entry.Entity.GetType().Name;
            bool isDeleted = entry.State == EntityState.Deleted ||
                             (entry.Entity is ISoftDeletable sd && sd.IsDeleted);

            if (isDeleted)
            {
                if (!IsInsideNormalizationBackfill)
                {
                    var staleKeys = auditByKey.Keys
                        .Where(k => k.EntityType == entityName && k.EntityId == entityId)
                        .ToList();
                    foreach (var sk in staleKeys)
                    {
                        var stale = auditByKey[sk];
                        CountryNormalizationAudits.Remove(stale);
                        auditByKey.Remove(sk);
                    }
                }
                continue;
            }

            var statusProp = entry.Property(nameof(Facility.CountryNormalizationStatus));
            var currentStatus = (CountryNormalizationStatus)(statusProp.CurrentValue ?? CountryNormalizationStatus.PendingNormalization);
            bool statusExplicitlyClaimedNormalized = currentStatus == CountryNormalizationStatus.Normalized
                && (entry.State == EntityState.Added || statusProp.IsModified);

            bool allValid = true;
            bool allHistoricalPreserved = true;
            bool anyCountryFieldModified = entry.State == EntityState.Added;
            string? firstFailureMessage = null;

            void ProcessField(
                string fieldName,
                string? rawVal,
                Action<string> setCanonicalVal,
                bool isFieldModified,
                string? stateVal,
                string? postalVal,
                string? tzVal,
                bool checkTimeZone,
                Guid? facilityId,
                Guid? clientId)
            {
                if (isFieldModified) anyCountryFieldModified = true;

                var key = (entityName, entityId, fieldName);
                auditByKey.TryGetValue(key, out var existingAudit);

                bool isPreservedHistoricalMatch = existingAudit != null &&
                    existingAudit.PreservedHistoricalValue &&
                    existingAudit.NormalizationStatus == CountryNormalizationStatus.Normalized &&
                    string.Equals((existingAudit.OriginalCountryValue ?? string.Empty).Trim(), (rawVal ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

                bool isExactCanonical = IsExactCanonicalIso2(rawVal, ctx.ProfilesByCode);
                string? canonicalCode = null;
                if (isExactCanonical)
                {
                    canonicalCode = rawVal!;
                }
                else if (!isPreservedHistoricalMatch && TryCanonicalizeStrictIso2(rawVal, ctx.ProfilesByCode, out var trimmedUpper))
                {
                    // F01: Persist exact 2-char uppercase ASCII code with no surrounding whitespace
                    setCanonicalVal(trimmedUpper);
                    canonicalCode = trimmedUpper;
                    isExactCanonical = true;
                    anyCountryFieldModified = true;
                }

                string? contradictionReason = null;
                if (isExactCanonical && canonicalCode != null)
                {
                    contradictionReason = CheckGeographicContradiction(canonicalCode, stateVal, postalVal, tzVal, checkTimeZone, ctx);
                }

                bool fieldValid = isExactCanonical && contradictionReason == null;
                if (fieldValid)
                {
                    // F01: Reconcile any existing diagnostic row atomically on runtime correction
                    if (!IsInsideNormalizationBackfill && ctx.ProfilesByCode.Count > 0 && existingAudit != null)
                    {
                        bool needsReconcile =
                            existingAudit.NormalizationStatus != CountryNormalizationStatus.Normalized ||
                            !string.Equals(existingAudit.NormalizedCountryCode, canonicalCode, StringComparison.Ordinal) ||
                            existingAudit.PreservedHistoricalValue ||
                            existingAudit.FacilityId != facilityId ||
                            existingAudit.ClientId != clientId;
                        if (needsReconcile)
                        {
                            existingAudit.NormalizedCountryCode = canonicalCode;
                            existingAudit.NormalizationStatus = CountryNormalizationStatus.Normalized;
                            existingAudit.PreservedHistoricalValue = false;
                            existingAudit.WasInferredFromParent = false;
                            existingAudit.InferenceSource = "RuntimeWriteCanonical";
                            existingAudit.Reason = CountryDiagnosticSanitizer.SanitizeReason($"Runtime write reconciled to canonical '{canonicalCode}'.");
                            existingAudit.FacilityId = facilityId;
                            existingAudit.ClientId = clientId;
                            existingAudit.CorrelationBatchId = RuntimeBatchId;
                            existingAudit.RecordedAtUtc = utcNow;
                        }
                    }
                    return;
                }

                allValid = false;
                if (!isPreservedHistoricalMatch)
                {
                    allHistoricalPreserved = false;
                }

                string diagnosticReason = contradictionReason ??
                    (string.IsNullOrWhiteSpace(rawVal)
                        ? "Blank/null country value quarantined pending normalization."
                        : "Unrecognized country token not found in active CountryProfile or CountryNormalizationAlias catalog.");

                firstFailureMessage ??= contradictionReason ??
                    $"{entityName} cannot declare CountryNormalizationStatus='Normalized' for unnormalized, blank, or unverified country value '{CountryDiagnosticSanitizer.SanitizeForDiagnostic(rawVal, ctx.VerifiedCatalogKeys) ?? "<blank>"}'.";

                // F01: Atomically create or update PendingNormalization diagnostic when active catalog exists
                if (!IsInsideNormalizationBackfill && ctx.ProfilesByCode.Count > 0 && !isPreservedHistoricalMatch)
                {
                    var sanitizedReason = CountryDiagnosticSanitizer.SanitizeReason(diagnosticReason);
                    if (existingAudit != null)
                    {
                        bool needsUpdate =
                            existingAudit.NormalizationStatus != CountryNormalizationStatus.PendingNormalization ||
                            existingAudit.NormalizedCountryCode != null ||
                            existingAudit.PreservedHistoricalValue ||
                            !string.Equals(existingAudit.Reason, sanitizedReason, StringComparison.Ordinal) ||
                            existingAudit.FacilityId != facilityId ||
                            existingAudit.ClientId != clientId;
                        if (needsUpdate)
                        {
                            // Preserve immutable initial provenance (OriginalCountryValue, InitialCorrelationBatchId, InitialRecordedAtUtc)
                            existingAudit.NormalizedCountryCode = null;
                            existingAudit.NormalizationStatus = CountryNormalizationStatus.PendingNormalization;
                            existingAudit.PreservedHistoricalValue = false;
                            existingAudit.WasInferredFromParent = false;
                            existingAudit.InferenceSource = null;
                            existingAudit.Reason = sanitizedReason;
                            existingAudit.FacilityId = facilityId;
                            existingAudit.ClientId = clientId;
                            existingAudit.CorrelationBatchId = RuntimeBatchId;
                            existingAudit.RecordedAtUtc = utcNow;
                        }
                    }
                    else
                    {
                        var newAudit = new CountryNormalizationAudit
                        {
                            Id = Guid.NewGuid(),
                            EntityType = entityName,
                            EntityId = entityId,
                            FieldName = fieldName,
                            OriginalCountryValue = CountryDiagnosticSanitizer.SanitizeForDiagnostic(rawVal, ctx.VerifiedCatalogKeys),
                            NormalizedCountryCode = null,
                            NormalizationStatus = CountryNormalizationStatus.PendingNormalization,
                            PreservedHistoricalValue = false,
                            WasInferredFromParent = false,
                            InferenceSource = null,
                            Reason = sanitizedReason,
                            FacilityId = facilityId,
                            ClientId = clientId,
                            CorrelationBatchId = RuntimeBatchId,
                            RecordedAtUtc = utcNow,
                            InitialCorrelationBatchId = RuntimeBatchId,
                            InitialRecordedAtUtc = utcNow
                        };
                        auditByKey[key] = newAudit;
                        CountryNormalizationAudits.Add(newAudit);
                    }
                }
            }

            switch (entry.Entity)
            {
                case Facility f:
                    ProcessField(nameof(Facility.CountryCode), f.CountryCode, v => f.CountryCode = v,
                        entry.Property(nameof(Facility.CountryCode)).IsModified || entry.Property(nameof(Facility.Timezone)).IsModified || entry.Property(nameof(Facility.StateOrProvince)).IsModified || entry.Property(nameof(Facility.PostalCode)).IsModified,
                        f.StateOrProvince, f.PostalCode, f.Timezone, checkTimeZone: true, facilityId: f.Id, clientId: null);
                    break;
                case Customer c:
                    ProcessField(nameof(Customer.DefaultCountryCode), c.DefaultCountryCode, v => c.DefaultCountryCode = v,
                        entry.Property(nameof(Customer.DefaultCountryCode)).IsModified || entry.Property(nameof(Customer.DefaultStateOrProvince)).IsModified || entry.Property(nameof(Customer.DefaultPostalCode)).IsModified,
                        c.DefaultStateOrProvince, c.DefaultPostalCode, null, checkTimeZone: false, facilityId: null, clientId: c.ClientId);
                    break;
                case Supplier s:
                    ProcessField(nameof(Supplier.Country), s.Country, v => s.Country = v,
                        entry.Property(nameof(Supplier.Country)).IsModified || entry.Property(nameof(Supplier.State)).IsModified || entry.Property(nameof(Supplier.PostalCode)).IsModified,
                        s.State, s.PostalCode, null, checkTimeZone: false, facilityId: null, clientId: s.ClientId);
                    break;
                case ShippingAddress sa:
                    orderScopes.TryGetValue(sa.OutboundOrderId, out var ordScope);
                    Guid? saFacId = ordScope.FacilityId != Guid.Empty ? ordScope.FacilityId : null;
                    Guid? saCliId = ordScope.ClientId != Guid.Empty ? ordScope.ClientId : null;
                    ProcessField(nameof(ShippingAddress.CountryCode), sa.CountryCode, v => sa.CountryCode = v,
                        entry.Property(nameof(ShippingAddress.CountryCode)).IsModified || entry.Property(nameof(ShippingAddress.StateOrProvince)).IsModified || entry.Property(nameof(ShippingAddress.PostalCode)).IsModified,
                        sa.StateOrProvince, sa.PostalCode, null, checkTimeZone: false, facilityId: saFacId, clientId: saCliId);
                    break;
                case ReturnShipment rs:
                    ProcessField(nameof(ReturnShipment.OriginCountry), rs.OriginCountry, v => rs.OriginCountry = v,
                        entry.Property(nameof(ReturnShipment.OriginCountry)).IsModified || entry.Property(nameof(ReturnShipment.OriginState)).IsModified || entry.Property(nameof(ReturnShipment.OriginPostalCode)).IsModified,
                        rs.OriginState, rs.OriginPostalCode, null, checkTimeZone: false, facilityId: rs.OriginFacilityId, clientId: rs.ClientId);
                    ProcessField(nameof(ReturnShipment.DestinationCountry), rs.DestinationCountry, v => rs.DestinationCountry = v,
                        entry.Property(nameof(ReturnShipment.DestinationCountry)).IsModified || entry.Property(nameof(ReturnShipment.DestinationState)).IsModified || entry.Property(nameof(ReturnShipment.DestinationPostalCode)).IsModified,
                        rs.DestinationState, rs.DestinationPostalCode, null, checkTimeZone: false, facilityId: rs.DestinationFacilityId, clientId: rs.ClientId);
                    break;
                case CarrierContractualSlaPolicy p:
                    ProcessField(nameof(CarrierContractualSlaPolicy.DestinationCountry), p.DestinationCountry, v => p.DestinationCountry = v,
                        entry.Property(nameof(CarrierContractualSlaPolicy.DestinationCountry)).IsModified || entry.Property(nameof(CarrierContractualSlaPolicy.DestinationStateOrRegion)).IsModified || entry.Property(nameof(CarrierContractualSlaPolicy.DestinationPostalCodePrefix)).IsModified,
                        p.DestinationStateOrRegion, p.DestinationPostalCodePrefix, null, checkTimeZone: false, facilityId: p.OriginFacilityId, clientId: p.ClientId);
                    break;
                case CarrierSlaComplianceRecord r:
                    ProcessField(nameof(CarrierSlaComplianceRecord.DestinationCountry), r.DestinationCountry, v => r.DestinationCountry = v,
                        entry.Property(nameof(CarrierSlaComplianceRecord.DestinationCountry)).IsModified || entry.Property(nameof(CarrierSlaComplianceRecord.DestinationStateOrRegion)).IsModified || entry.Property(nameof(CarrierSlaComplianceRecord.DestinationPostalCodePrefix)).IsModified,
                        r.DestinationStateOrRegion, r.DestinationPostalCodePrefix, null, checkTimeZone: false, facilityId: r.FacilityId, clientId: r.ClientId);
                    break;
            }

            if (allValid)
            {
                if (currentStatus != CountryNormalizationStatus.Normalized && anyCountryFieldModified)
                {
                    statusProp.CurrentValue = CountryNormalizationStatus.Normalized;
                }
                continue;
            }

            if (allHistoricalPreserved && entry.State == EntityState.Modified && !anyCountryFieldModified)
            {
                continue;
            }

            if (statusExplicitlyClaimedNormalized)
            {
                throw new ValidationException(
                    nameof(Facility.CountryNormalizationStatus),
                    firstFailureMessage ?? $"{entityName} cannot declare CountryNormalizationStatus='Normalized' for unnormalized, blank, or conflicting country evidence.");
            }

            if (currentStatus != CountryNormalizationStatus.PendingNormalization)
            {
                statusProp.CurrentValue = CountryNormalizationStatus.PendingNormalization;
            }
        }
    }

    private int SaveWithInMemoryScopeEpochsSync(
        IReadOnlyList<PendingConfigurationScopeMutation> pendingScopes,
        bool acceptAllChangesOnSuccess)
    {
        var locks = AcquireInMemoryScopeLocksSync(pendingScopes);
        try
        {
            _isInsideConfigurationOutboxSave = true;
            var utcNow = DateTime.UtcNow;

            foreach (var scope in pendingScopes)
            {
                var epoch = ConfigurationScopeEpochs.FirstOrDefault(e => e.ScopeType == scope.ScopeType && e.ScopeKey == scope.ScopeKey);
                long newEpoch;
                if (epoch == null)
                {
                    epoch = ConfigurationScopeEpoch.Create(scope.ScopeType, scope.ScopeKey, utcNow, 1);
                    ConfigurationScopeEpochs.Add(epoch);
                    newEpoch = 1;
                }
                else
                {
                    newEpoch = epoch.Increment(utcNow);
                }

                ConfigurationInvalidationEvents.Add(ConfigurationInvalidationEvent.Create(
                    scope.ScopeType, scope.ScopeKey, newEpoch, scope.EntityType, scope.EntityId, utcNow));
            }

            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
        finally
        {
            _isInsideConfigurationOutboxSave = false;
            for (int i = locks.Count - 1; i >= 0; i--) locks[i].Release();
        }
    }

    private async Task<int> SaveWithInMemoryScopeEpochsAsync(
        IReadOnlyList<PendingConfigurationScopeMutation> pendingScopes,
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken)
    {
        var locks = await AcquireInMemoryScopeLocksAsync(pendingScopes, cancellationToken);
        try
        {
            _isInsideConfigurationOutboxSave = true;
            var utcNow = DateTime.UtcNow;

            foreach (var scope in pendingScopes)
            {
                var epoch = await ConfigurationScopeEpochs
                    .FirstOrDefaultAsync(e => e.ScopeType == scope.ScopeType && e.ScopeKey == scope.ScopeKey, cancellationToken);
                long newEpoch;
                if (epoch == null)
                {
                    epoch = ConfigurationScopeEpoch.Create(scope.ScopeType, scope.ScopeKey, utcNow, 1);
                    await ConfigurationScopeEpochs.AddAsync(epoch, cancellationToken);
                    newEpoch = 1;
                }
                else
                {
                    newEpoch = epoch.Increment(utcNow);
                }

                await ConfigurationInvalidationEvents.AddAsync(ConfigurationInvalidationEvent.Create(
                    scope.ScopeType, scope.ScopeKey, newEpoch, scope.EntityType, scope.EntityId, utcNow), cancellationToken);
            }

            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        finally
        {
            _isInsideConfigurationOutboxSave = false;
            for (int i = locks.Count - 1; i >= 0; i--) locks[i].Release();
        }
    }

    private int SaveWithRelationalScopeEpochsSync(
        IReadOnlyList<PendingConfigurationScopeMutation> pendingScopes,
        bool acceptAllChangesOnSuccess)
    {
        if (!CheckHasMdm15SchemaSync())
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        if (Database.CurrentTransaction != null || _currentTransaction != null)
        {
            var saved = base.SaveChanges(false);
            ApplyRelationalScopeEpochsSync(pendingScopes);
            if (acceptAllChangesOnSuccess) ChangeTracker.AcceptAllChanges();
            return saved;
        }

        return Database.CreateExecutionStrategy().Execute(() =>
        {
            using var tx = Database.BeginTransaction();
            try
            {
                var saved = base.SaveChanges(false);
                ApplyRelationalScopeEpochsSync(pendingScopes);
                tx.Commit();
                if (acceptAllChangesOnSuccess) ChangeTracker.AcceptAllChanges();
                return saved;
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        });
    }

    private async Task<int> SaveWithRelationalScopeEpochsAsync(
        IReadOnlyList<PendingConfigurationScopeMutation> pendingScopes,
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken)
    {
        if (!await CheckHasMdm15SchemaAsync(cancellationToken))
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        if (Database.CurrentTransaction != null || _currentTransaction != null)
        {
            var saved = await base.SaveChangesAsync(false, cancellationToken);
            await ApplyRelationalScopeEpochsAsync(pendingScopes, cancellationToken);
            if (acceptAllChangesOnSuccess) ChangeTracker.AcceptAllChanges();
            return saved;
        }

        return await Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var saved = await base.SaveChangesAsync(false, cancellationToken);
                await ApplyRelationalScopeEpochsAsync(pendingScopes, cancellationToken);
                await tx.CommitAsync(cancellationToken);
                if (acceptAllChangesOnSuccess) ChangeTracker.AcceptAllChanges();
                return saved;
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    private bool CheckHasMdm15SchemaSync()
    {
        if (_hasMdm15Schema == true) return true;
        var conn = Database.GetDbConnection();
        bool shouldClose = conn.State != ConnectionState.Open;
        if (shouldClose) conn.Open();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = Database.CurrentTransaction?.GetDbTransaction();
            cmd.CommandText = """
                SELECT
                    CASE WHEN to_regclass('configuration_scope_epochs') IS NOT NULL THEN 1 ELSE 0 END AS has_epochs,
                    CASE WHEN to_regclass('configuration_invalidation_events') IS NOT NULL THEN 1 ELSE 0 END AS has_outbox,
                    CASE WHEN to_regclass('"__EFMigrationsHistory"') IS NOT NULL
                              AND EXISTS (SELECT 1 FROM "__EFMigrationsHistory")
                         THEN 1 ELSE 0 END AS has_history,
                    CASE WHEN to_regclass('"__EFMigrationsHistory"') IS NOT NULL
                              AND EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE migration_id >= '20261126083415')
                         THEN 1 ELSE 0 END AS has_mdm15_migration;
                """;
            using var reader = cmd.ExecuteReader();
            reader.Read();
            bool hasEpochs = reader.GetInt32(0) == 1;
            bool hasOutbox = reader.GetInt32(1) == 1;
            bool hasHistory = reader.GetInt32(2) == 1;
            bool hasMdm15Migration = reader.GetInt32(3) == 1;

            if (hasEpochs && hasOutbox)
            {
                _hasMdm15Schema = true;
                return true;
            }

            if (hasEpochs != hasOutbox || !hasHistory || hasMdm15Migration)
            {
                throw new InvalidOperationException(
                    "MDM-1.5 configuration_scope_epochs / configuration_invalidation_events schema is incomplete or missing outside a controlled pre-MDM-1.5 migration state; failing closed rather than bypassing epoch tracking.");
            }

            return false;
        }
        finally
        {
            if (shouldClose && conn.State == ConnectionState.Open) conn.Close();
        }
    }

    private async Task<bool> CheckHasMdm15SchemaAsync(CancellationToken cancellationToken)
    {
        if (_hasMdm15Schema == true) return true;
        var conn = Database.GetDbConnection();
        bool shouldClose = conn.State != ConnectionState.Open;
        if (shouldClose) await conn.OpenAsync(cancellationToken);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = Database.CurrentTransaction?.GetDbTransaction();
            cmd.CommandText = """
                SELECT
                    CASE WHEN to_regclass('configuration_scope_epochs') IS NOT NULL THEN 1 ELSE 0 END AS has_epochs,
                    CASE WHEN to_regclass('configuration_invalidation_events') IS NOT NULL THEN 1 ELSE 0 END AS has_outbox,
                    CASE WHEN to_regclass('"__EFMigrationsHistory"') IS NOT NULL
                              AND EXISTS (SELECT 1 FROM "__EFMigrationsHistory")
                         THEN 1 ELSE 0 END AS has_history,
                    CASE WHEN to_regclass('"__EFMigrationsHistory"') IS NOT NULL
                              AND EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE migration_id >= '20261126083415')
                         THEN 1 ELSE 0 END AS has_mdm15_migration;
                """;
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            bool hasEpochs = reader.GetInt32(0) == 1;
            bool hasOutbox = reader.GetInt32(1) == 1;
            bool hasHistory = reader.GetInt32(2) == 1;
            bool hasMdm15Migration = reader.GetInt32(3) == 1;

            if (hasEpochs && hasOutbox)
            {
                _hasMdm15Schema = true;
                return true;
            }

            if (hasEpochs != hasOutbox || !hasHistory || hasMdm15Migration)
            {
                throw new InvalidOperationException(
                    "MDM-1.5 configuration_scope_epochs / configuration_invalidation_events schema is incomplete or missing outside a controlled pre-MDM-1.5 migration state; failing closed rather than bypassing epoch tracking.");
            }

            return false;
        }
        finally
        {
            if (shouldClose && conn.State == ConnectionState.Open) await conn.CloseAsync();
        }
    }

    private void ApplyRelationalScopeEpochsSync(IReadOnlyList<PendingConfigurationScopeMutation> pendingScopes)
    {
        var conn = Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) conn.Open();
        var dbTx = Database.CurrentTransaction?.GetDbTransaction();
        var utcNow = DateTime.UtcNow;

        foreach (var scope in pendingScopes)
        {
            long newEpoch;
            using (var epochCmd = conn.CreateCommand())
            {
                epochCmd.Transaction = dbTx;
                epochCmd.CommandText = """
                    INSERT INTO configuration_scope_epochs (epoch_id, scope_type, scope_key, current_epoch, updated_at_utc)
                    VALUES (@epochId, @scopeType, @scopeKey, 1, @updatedAtUtc)
                    ON CONFLICT (scope_type, scope_key) DO UPDATE
                    SET current_epoch = configuration_scope_epochs.current_epoch + 1,
                        updated_at_utc = EXCLUDED.updated_at_utc
                    RETURNING current_epoch;
                    """;
                AddDbParam(epochCmd, "@epochId", Guid.NewGuid());
                AddDbParam(epochCmd, "@scopeType", scope.ScopeType.ToString());
                AddDbParam(epochCmd, "@scopeKey", scope.ScopeKey);
                AddDbParam(epochCmd, "@updatedAtUtc", utcNow);
                newEpoch = Convert.ToInt64(epochCmd.ExecuteScalar());
            }

            using var evtCmd = conn.CreateCommand();
            evtCmd.Transaction = dbTx;
            evtCmd.CommandText = """
                INSERT INTO configuration_invalidation_events
                    (scope_type, scope_key, new_epoch, entity_type, entity_id, created_at_utc, dispatched_to_redis_at_utc, dispatch_attempts, last_dispatch_error)
                VALUES
                    (@scopeType, @scopeKey, @newEpoch, @entityType, @entityId, @createdAtUtc, NULL, 0, NULL);
                """;
            AddDbParam(evtCmd, "@scopeType", scope.ScopeType.ToString());
            AddDbParam(evtCmd, "@scopeKey", scope.ScopeKey);
            AddDbParam(evtCmd, "@newEpoch", newEpoch);
            AddDbParam(evtCmd, "@entityType", scope.EntityType);
            AddDbParam(evtCmd, "@entityId", scope.EntityId);
            AddDbParam(evtCmd, "@createdAtUtc", utcNow);
            evtCmd.ExecuteNonQuery();
        }
    }

    private async Task ApplyRelationalScopeEpochsAsync(
        IReadOnlyList<PendingConfigurationScopeMutation> pendingScopes,
        CancellationToken cancellationToken)
    {
        var conn = Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(cancellationToken);
        var dbTx = Database.CurrentTransaction?.GetDbTransaction();
        var utcNow = DateTime.UtcNow;

        foreach (var scope in pendingScopes)
        {
            long newEpoch;
            await using (var epochCmd = conn.CreateCommand())
            {
                epochCmd.Transaction = dbTx;
                epochCmd.CommandText = """
                    INSERT INTO configuration_scope_epochs (epoch_id, scope_type, scope_key, current_epoch, updated_at_utc)
                    VALUES (@epochId, @scopeType, @scopeKey, 1, @updatedAtUtc)
                    ON CONFLICT (scope_type, scope_key) DO UPDATE
                    SET current_epoch = configuration_scope_epochs.current_epoch + 1,
                        updated_at_utc = EXCLUDED.updated_at_utc
                    RETURNING current_epoch;
                    """;
                AddDbParam(epochCmd, "@epochId", Guid.NewGuid());
                AddDbParam(epochCmd, "@scopeType", scope.ScopeType.ToString());
                AddDbParam(epochCmd, "@scopeKey", scope.ScopeKey);
                AddDbParam(epochCmd, "@updatedAtUtc", utcNow);
                newEpoch = Convert.ToInt64(await epochCmd.ExecuteScalarAsync(cancellationToken));
            }

            await using var evtCmd = conn.CreateCommand();
            evtCmd.Transaction = dbTx;
            evtCmd.CommandText = """
                INSERT INTO configuration_invalidation_events
                    (scope_type, scope_key, new_epoch, entity_type, entity_id, created_at_utc, dispatched_to_redis_at_utc, dispatch_attempts, last_dispatch_error)
                VALUES
                    (@scopeType, @scopeKey, @newEpoch, @entityType, @entityId, @createdAtUtc, NULL, 0, NULL);
                """;
            AddDbParam(evtCmd, "@scopeType", scope.ScopeType.ToString());
            AddDbParam(evtCmd, "@scopeKey", scope.ScopeKey);
            AddDbParam(evtCmd, "@newEpoch", newEpoch);
            AddDbParam(evtCmd, "@entityType", scope.EntityType);
            AddDbParam(evtCmd, "@entityId", scope.EntityId);
            AddDbParam(evtCmd, "@createdAtUtc", utcNow);
            await evtCmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static List<SemaphoreSlim> AcquireInMemoryScopeLocksSync(IReadOnlyList<PendingConfigurationScopeMutation> scopes)
    {
        var acquired = new List<SemaphoreSlim>(scopes.Count);
        foreach (var scope in scopes)
        {
            var gate = InMemoryScopeEpochLocks.GetOrAdd($"{scope.ScopeType}:{scope.ScopeKey}", _ => new SemaphoreSlim(1, 1));
            gate.Wait();
            acquired.Add(gate);
        }
        return acquired;
    }

    private static async Task<List<SemaphoreSlim>> AcquireInMemoryScopeLocksAsync(
        IReadOnlyList<PendingConfigurationScopeMutation> scopes,
        CancellationToken cancellationToken)
    {
        var acquired = new List<SemaphoreSlim>(scopes.Count);
        try
        {
            foreach (var scope in scopes)
            {
                var gate = InMemoryScopeEpochLocks.GetOrAdd($"{scope.ScopeType}:{scope.ScopeKey}", _ => new SemaphoreSlim(1, 1));
                await gate.WaitAsync(cancellationToken);
                acquired.Add(gate);
            }
            return acquired;
        }
        catch
        {
            for (int i = acquired.Count - 1; i >= 0; i--) acquired[i].Release();
            throw;
        }
    }

    private static void AddDbParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    public DbSet<Client> Clients => Set<Client>();
    public DbSet<ClientContact> ClientContacts => Set<ClientContact>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RoleReconciliationAuditLog> RoleReconciliationAuditLogs => Set<RoleReconciliationAuditLog>();
    public DbSet<ClientFacilityAccess> ClientFacilityAccesses => Set<ClientFacilityAccess>();
    public DbSet<Facility> Facilities => Set<Facility>();
    public DbSet<LegalEntity> LegalEntities => Set<LegalEntity>();
    public DbSet<WarehouseZone> WarehouseZones => Set<WarehouseZone>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<LocationLock> LocationLocks => Set<LocationLock>();
    public DbSet<LocationHierarchyRule> LocationHierarchyRules => Set<LocationHierarchyRule>();
    public DbSet<LocationStorageProfile> LocationStorageProfiles => Set<LocationStorageProfile>();
    public DbSet<LocationOperationalProfile> LocationOperationalProfiles => Set<LocationOperationalProfile>();
    public DbSet<WarehouseEquipment> WarehouseEquipment => Set<WarehouseEquipment>();
    public DbSet<LocationEquipmentRequirement> LocationEquipmentRequirements => Set<LocationEquipmentRequirement>();

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

    // Global Localization Reference & Statutory Compliance Policies (MDM-1.1 / MDM-1.3)
    public DbSet<CountryProfile> CountryProfiles => Set<CountryProfile>();
    public DbSet<AdministrativeDivision> AdministrativeDivisions => Set<AdministrativeDivision>();
    public DbSet<TimeZoneProfile> TimeZoneProfiles => Set<TimeZoneProfile>();
    public DbSet<CountryCompliancePolicy> CountryCompliancePolicies => Set<CountryCompliancePolicy>();
    public DbSet<CurrencyMaster> Currencies => Set<CurrencyMaster>();
    public DbSet<FxRateRecord> FxRateRecords => Set<FxRateRecord>();
    public DbSet<UomDefinition> UomDefinitions => Set<UomDefinition>();
    public DbSet<UomConversion> UomConversions => Set<UomConversion>();
    public DbSet<BikramSambatCalendarMonth> BikramSambatCalendarMonths => Set<BikramSambatCalendarMonth>();
    public DbSet<HolidayCalendar> HolidayCalendars => Set<HolidayCalendar>();
    public DbSet<FacilityCalendar> FacilityCalendars => Set<FacilityCalendar>();
    public DbSet<ConfigurationScopeEpoch> ConfigurationScopeEpochs => Set<ConfigurationScopeEpoch>();
    public DbSet<ConfigurationInvalidationEvent> ConfigurationInvalidationEvents => Set<ConfigurationInvalidationEvent>();
    public DbSet<CountryNormalizationAlias> CountryNormalizationAliases => Set<CountryNormalizationAlias>();
    public DbSet<CountryNormalizationAudit> CountryNormalizationAudits => Set<CountryNormalizationAudit>();

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
        if (Database.CurrentTransaction != null || _currentTransaction != null)
        {
            return await action();
        }

        if (!Database.IsRelational())
        {
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

    public async Task<IAsyncDisposable> AcquireStockAllocationLocksAsync(
        IReadOnlyCollection<Guid> stockOnHandIds,
        CancellationToken cancellationToken = default)
    {
        var sortedIds = stockOnHandIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .OrderBy(id => id)
            .ToList();

        if (sortedIds.Count == 0)
        {
            return NoOpAsyncDisposable.Instance;
        }

        if (!Database.IsRelational())
        {
            var gates = new List<SemaphoreSlim>(sortedIds.Count);
            try
            {
                foreach (var stockId in sortedIds)
                {
                    var gate = InMemoryBillingLocks.GetOrAdd($"STOCKALLOC:{stockId:N}", _ => new SemaphoreSlim(1, 1));
                    await gate.WaitAsync(cancellationToken);
                    gates.Add(gate);
                }
                return new MultiSemaphoreReleaser(gates);
            }
            catch
            {
                for (int i = gates.Count - 1; i >= 0; i--) gates[i].Release();
                throw;
            }
        }

        if (Database.CurrentTransaction != null)
        {
            foreach (var stockId in sortedIds)
            {
                await Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM stock_on_hand WHERE stock_id = {stockId} FOR UPDATE",
                    cancellationToken);
            }
        }

        return NoOpAsyncDisposable.Instance;
    }

    public async Task<IAsyncDisposable> AcquirePutawayExecutionLocksAsync(
        Guid? sourceStockId,
        Guid destinationLocationId,
        CancellationToken cancellationToken = default)
    {
        if (!Database.IsRelational())
        {
            var gates = new List<SemaphoreSlim>(2);
            try
            {
                if (destinationLocationId != Guid.Empty)
                {
                    var locGate = InMemoryBillingLocks.GetOrAdd($"PUTAWAYLOC:{destinationLocationId:N}", _ => new SemaphoreSlim(1, 1));
                    await locGate.WaitAsync(cancellationToken);
                    gates.Add(locGate);
                }

                if (sourceStockId.HasValue && sourceStockId.Value != Guid.Empty)
                {
                    var stockGate = InMemoryBillingLocks.GetOrAdd($"PUTAWAYSOURCESTK:{sourceStockId.Value:N}", _ => new SemaphoreSlim(1, 1));
                    await stockGate.WaitAsync(cancellationToken);
                    gates.Add(stockGate);
                }

                return new MultiSemaphoreReleaser(gates);
            }
            catch
            {
                for (int i = gates.Count - 1; i >= 0; i--) gates[i].Release();
                throw;
            }
        }

        if (Database.CurrentTransaction != null)
        {
            if (destinationLocationId != Guid.Empty)
            {
                if (Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
                {
                    var locLockKey = $"LOCATION_LIFECYCLE:{destinationLocationId:N}";
                    await Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT pg_advisory_xact_lock(hashtextextended({locLockKey}, 0::bigint))",
                        cancellationToken);
                }

                await Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM locations WHERE location_id = {destinationLocationId} FOR UPDATE",
                    cancellationToken);
            }

            if (sourceStockId.HasValue && sourceStockId.Value != Guid.Empty)
            {
                await Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM stock_on_hand WHERE stock_id = {sourceStockId.Value} FOR UPDATE",
                    cancellationToken);
            }
        }

        return NoOpAsyncDisposable.Instance;
    }

    private sealed class MultiSemaphoreReleaser(List<SemaphoreSlim> semaphores) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                for (int i = semaphores.Count - 1; i >= 0; i--)
                {
                    semaphores[i].Release();
                }
            }

            return ValueTask.CompletedTask;
        }
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

    public async Task<TResult> ExecuteWithCompliancePolicyLockAsync<TResult>(
        string countryCode,
        string policyCode,
        Func<Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        var normalizedCountry = (countryCode ?? string.Empty).Trim().ToUpperInvariant();
        var normalizedPolicy = (policyCode ?? string.Empty).Trim().ToUpperInvariant();
        var lockKey = $"COMPLIANCE:{normalizedCountry}:{normalizedPolicy}";

        if (!Database.IsRelational())
        {
            var gate = InMemoryComplianceLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await action();
            }
            finally
            {
                gate.Release();
            }
        }

        async Task AcquireComplianceLocksAsync()
        {
            if (Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
            {
                await Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0::bigint))",
                    cancellationToken);

                await Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM country_compliance_policies WHERE country_code = {normalizedCountry} AND policy_code = {normalizedPolicy} AND approval_status = 'VerifiedApproved' AND is_active = true FOR UPDATE",
                    cancellationToken);
            }
        }

        async Task<TResult> RunLockedActionAsync()
        {
            try
            {
                await AcquireComplianceLocksAsync();
                return await action();
            }
            catch (DbUpdateException dbEx) when (dbEx.InnerException is Npgsql.PostgresException pgEx &&
                                                 (pgEx.SqlState == "23P01" || pgEx.SqlState == "23505"))
            {
                throw new Domain.Exceptions.ConcurrencyException(
                    "COMPLIANCE_POLICY_CONFLICT",
                    $"Concurrent modification or active window overlap prevented for '{normalizedCountry}/{normalizedPolicy}' ({pgEx.SqlState}).");
            }
        }

        if (Database.CurrentTransaction != null || _currentTransaction != null)
        {
            return await RunLockedActionAsync();
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
                var result = await RunLockedActionAsync();
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

    public async Task<TResult> ExecuteWithLegalEntityLocksAsync<TResult>(
        IReadOnlyCollection<Guid> legalEntityIds,
        Func<Task<TResult>> action,
        CancellationToken cancellationToken = default,
        Guid? lockFacilityId = null)
    {
        var sortedIds = legalEntityIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .OrderBy(id => id)
            .ToList();

        if (!Database.IsRelational())
        {
            var gates = new List<SemaphoreSlim>(sortedIds.Count);
            try
            {
                foreach (var legalEntityId in sortedIds)
                {
                    var gate = InMemoryLegalEntityLocks.GetOrAdd(legalEntityId, _ => new SemaphoreSlim(1, 1));
                    await gate.WaitAsync(cancellationToken);
                    gates.Add(gate);
                }

                return await action();
            }
            finally
            {
                for (int i = gates.Count - 1; i >= 0; i--)
                {
                    gates[i].Release();
                }
            }
        }

        async Task AcquireLegalEntityLocksAsync()
        {
            foreach (var legalEntityId in sortedIds)
            {
                if (Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
                {
                    var lockKey = $"LEGALENTITY:{legalEntityId:N}";
                    await Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0::bigint))",
                        cancellationToken);
                }

                await Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM legal_entities WHERE legal_entity_id = {legalEntityId} FOR UPDATE",
                    cancellationToken);
            }

            if (lockFacilityId.HasValue && lockFacilityId.Value != Guid.Empty)
            {
                await Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM facilities WHERE facility_id = {lockFacilityId.Value} FOR UPDATE",
                    cancellationToken);
            }
        }

        if (Database.CurrentTransaction != null || _currentTransaction != null)
        {
            await AcquireLegalEntityLocksAsync();
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
                await AcquireLegalEntityLocksAsync();
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

    public async Task<TResult> ExecuteWithFxRateLockAsync<TResult>(
        string baseCurrencyCode,
        string quoteCurrencyCode,
        Domain.Enums.FxRateSourceAuthority sourceAuthority,
        Func<Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        var normalizedBase = (baseCurrencyCode ?? string.Empty).Trim().ToUpperInvariant();
        var normalizedQuote = (quoteCurrencyCode ?? string.Empty).Trim().ToUpperInvariant();
        var lockKey = $"FXRATE:{normalizedBase}:{normalizedQuote}:{sourceAuthority}";

        if (!Database.IsRelational())
        {
            var gate = InMemoryFxRateLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await action();
            }
            finally
            {
                gate.Release();
            }
        }

        async Task AcquireFxLocksAsync()
        {
            if (Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
            {
                await Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0::bigint))",
                    cancellationToken);

                var authorityName = sourceAuthority.ToString();
                await Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM fx_rates WHERE base_currency_code = {normalizedBase} AND quote_currency_code = {normalizedQuote} AND source_authority = {authorityName} AND approval_status = 'VerifiedApproved' AND is_active = true FOR UPDATE",
                    cancellationToken);
            }
        }

        async Task<TResult> RunLockedActionAsync()
        {
            try
            {
                await AcquireFxLocksAsync();
                return await action();
            }
            catch (DbUpdateException dbEx) when (dbEx.InnerException is Npgsql.PostgresException pgEx &&
                                                 (pgEx.SqlState == "23P01" || pgEx.SqlState == "23505"))
            {
                throw new Domain.Exceptions.ConcurrencyException(
                    "FX_RATE_CONFLICT",
                    $"Concurrent modification or active window overlap prevented for '{normalizedBase}/{normalizedQuote}' ({sourceAuthority}, {pgEx.SqlState}).");
            }
        }

        if (Database.CurrentTransaction != null || _currentTransaction != null)
        {
            return await RunLockedActionAsync();
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
                var result = await RunLockedActionAsync();
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

    public async Task<TResult> ExecuteWithFacilityCalendarLockAsync<TResult>(
        Guid facilityId,
        Func<Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        if (!Database.IsRelational())
        {
            var gate = InMemoryFacilityCalendarLocks.GetOrAdd(facilityId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await action();
            }
            finally
            {
                gate.Release();
            }
        }

        async Task AcquireCalendarLocksAsync()
        {
            if (Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
            {
                var lockKey = $"FACILITYCALENDAR:{facilityId:N}";
                await Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0::bigint))",
                    cancellationToken);

                await Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM facility_calendars WHERE facility_id = {facilityId} AND is_active = true FOR UPDATE",
                    cancellationToken);
            }
        }

        async Task<TResult> RunLockedActionAsync()
        {
            try
            {
                await AcquireCalendarLocksAsync();
                return await action();
            }
            catch (DbUpdateException dbEx) when (dbEx.InnerException is Npgsql.PostgresException pgEx &&
                                                 (pgEx.SqlState == "23P01" || pgEx.SqlState == "23505"))
            {
                throw new Domain.Exceptions.ConcurrencyException(
                    "FACILITY_CALENDAR_CONFLICT",
                    $"Concurrent modification or active window overlap prevented for facility calendar '{facilityId}' ({pgEx.SqlState}).");
            }
        }

        if (Database.CurrentTransaction != null || _currentTransaction != null)
        {
            return await RunLockedActionAsync();
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
                var result = await RunLockedActionAsync();
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

    private void NormalizeWarehouseMasterEntities()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            switch (entry.Entity)
            {
                case Location loc:
                    if (loc.Id == Guid.Empty) loc.Id = Guid.NewGuid();
                    if (!string.IsNullOrWhiteSpace(loc.LocationCode))
                    {
                        loc.LocationCode = loc.LocationCode.Trim();
                        loc.NormalizedLocationCode = Location.NormalizeCode(loc.LocationCode);
                    }
                    if (!string.IsNullOrWhiteSpace(loc.Barcode))
                    {
                        loc.Barcode = loc.Barcode.Trim();
                        loc.NormalizedBarcode = Location.NormalizeCode(loc.Barcode);
                    }
                    if (string.IsNullOrWhiteSpace(loc.LocationName))
                    {
                        loc.LocationName = loc.LocationCode;
                    }
                    if (loc.HierarchyLevel < 1)
                    {
                        loc.HierarchyLevel = 1;
                    }
                    if (string.IsNullOrWhiteSpace(loc.HierarchyPath))
                    {
                        loc.HierarchyPath = Location.BuildHierarchyPath(null, loc.Id);
                    }
                    if (entry.State == EntityState.Added)
                    {
                        if (!loc.IsActive && loc.Status == LocationStatus.Active)
                        {
                            loc.Status = LocationStatus.Inactive;
                        }
                        else
                        {
                            loc.IsActive = loc.Status == LocationStatus.Active;
                        }
                    }
                    else
                    {
                        var statusProp = entry.Property(nameof(Location.Status));
                        var activeProp = entry.Property(nameof(Location.IsActive));
                        if (statusProp.IsModified)
                        {
                            loc.IsActive = loc.Status == LocationStatus.Active;
                        }
                        else if (activeProp.IsModified)
                        {
                            loc.Status = loc.IsActive ? LocationStatus.Active : LocationStatus.Inactive;
                        }
                    }
                    break;

                case WarehouseZone zone:
                    if (!string.IsNullOrWhiteSpace(zone.ZoneCode))
                    {
                        zone.ZoneCode = zone.ZoneCode.Trim();
                        zone.NormalizedZoneCode = WarehouseZone.NormalizeCode(zone.ZoneCode);
                    }
                    break;

                case DockDoor dock:
                    if (!string.IsNullOrWhiteSpace(dock.DoorNumber))
                    {
                        dock.DoorNumber = dock.DoorNumber.Trim();
                        dock.NormalizedDoorNumber = DockDoor.NormalizeDoorNo(dock.DoorNumber);
                    }
                    if (!dock.MaxTrailerLengthCm.HasValue && dock.MaxTrailerLengthFeet.HasValue && dock.MaxTrailerLengthFeet.Value > 0)
                    {
                        dock.MaxTrailerLengthCm = Math.Round(dock.MaxTrailerLengthFeet.Value * DockDoor.CmPerFoot, 2, MidpointRounding.AwayFromZero);
                    }
                    break;

                case PackagingMaterial pm:
                    if (!string.IsNullOrWhiteSpace(pm.Code))
                    {
                        pm.Code = pm.Code.Trim();
                        pm.NormalizedCode = PackagingMaterial.NormalizeMaterialCode(pm.Code);
                    }
                    if (!string.IsNullOrWhiteSpace(pm.CurrencyCode))
                    {
                        pm.CurrencyCode = pm.CurrencyCode.Trim().ToUpperInvariant();
                    }
                    break;

                case Domain.Entities.Facilities.WarehouseEquipment eq:
                    if (!string.IsNullOrWhiteSpace(eq.EquipmentCode))
                    {
                        eq.EquipmentCode = eq.EquipmentCode.Trim();
                        eq.NormalizedEquipmentCode = Domain.Entities.Facilities.WarehouseEquipment.NormalizeCode(eq.EquipmentCode);
                    }
                    break;
            }
        }
    }

    public async Task<TResult> ExecuteWithGlobalHierarchyLockAsync<TResult>(
        Func<Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        if (!Database.IsRelational())
        {
            await InMemoryGlobalHierarchyLock.WaitAsync(cancellationToken);
            try
            {
                return await action();
            }
            finally
            {
                InMemoryGlobalHierarchyLock.Release();
            }
        }

        async Task AcquireGlobalLockAsync()
        {
            if (Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
            {
                const string globalKey = "GLOBAL_HIERARCHY_RULES";
                await Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({globalKey}, 0::bigint))",
                    cancellationToken);
            }
        }

        if (Database.CurrentTransaction != null || _currentTransaction != null)
        {
            await AcquireGlobalLockAsync();
            return await action();
        }

        bool firstAttempt = true;
        return await Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            if (!firstAttempt) ClearChangeTracker();
            firstAttempt = false;

            await BeginTransactionAsync(cancellationToken);
            try
            {
                await AcquireGlobalLockAsync();
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

    public async Task<TResult> ExecuteWithFacilityHierarchyLockAsync<TResult>(
        Guid facilityId,
        Func<Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        if (!Database.IsRelational())
        {
            var gate = InMemoryFacilityHierarchyLocks.GetOrAdd(facilityId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await action();
            }
            finally
            {
                gate.Release();
            }
        }

        async Task AcquireHierarchyLockAsync()
        {
            if (Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
            {
                const string globalKey = "GLOBAL_HIERARCHY_RULES";
                await Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock_shared(hashtextextended({globalKey}, 0::bigint))",
                    cancellationToken);

                var lockKey = $"FACILITY_HIERARCHY:{facilityId:N}";
                await Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0::bigint))",
                    cancellationToken);
            }
        }

        if (Database.CurrentTransaction != null || _currentTransaction != null)
        {
            await AcquireHierarchyLockAsync();
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
                await AcquireHierarchyLockAsync();
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

    public async Task<TResult> ExecuteWithLocationLifecycleLockAsync<TResult>(
        Guid facilityId,
        Guid locationId,
        Func<Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        if (!Database.IsRelational())
        {
            var gate = InMemoryLocationLifecycleLocks.GetOrAdd(locationId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await action();
            }
            finally
            {
                gate.Release();
            }
        }

        async Task AcquireLocationLifecycleLockAsync()
        {
            if (Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
            {
                var lockKey = $"LOCATION_LIFECYCLE:{locationId:N}";
                await Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0::bigint))",
                    cancellationToken);

                await Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM locations WHERE location_id = {locationId} AND facility_id = {facilityId} FOR UPDATE",
                    cancellationToken);
            }
        }

        if (Database.CurrentTransaction != null || _currentTransaction != null)
        {
            await AcquireLocationLifecycleLockAsync();
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
                await AcquireLocationLifecycleLockAsync();
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

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.AddInterceptors(_auditableEntityInterceptor);

        if (optionsBuilder.Options.Extensions.Any(e =>
                e.GetType().FullName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true))
        {
            optionsBuilder.ReplaceService<Microsoft.EntityFrameworkCore.Migrations.IMigrator, Mdm16GuardedMigrator>();
        }
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
