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
        if (_isInsideConfigurationOutboxSave)
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        ReconcileCountryNormalizationLifecycleSync();

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
        if (_isInsideConfigurationOutboxSave)
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        await ReconcileCountryNormalizationLifecycleAsync(cancellationToken);

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

    private void ReconcileCountryNormalizationLifecycleSync()
    {
        var targets = ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified && IsCountryLifecycleTarget(e.Entity))
            .ToList();
        if (targets.Count == 0) return;

        var activeCodes = CountryProfiles.AsNoTracking()
            .Where(c => c.IsActive)
            .Select(c => c.CountryCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var localCp in ChangeTracker.Entries<CountryProfile>().Where(e => e.State != EntityState.Deleted))
        {
            if (localCp.Entity.IsActive) activeCodes.Add(localCp.Entity.CountryCode.Trim());
            else activeCodes.Remove(localCp.Entity.CountryCode.Trim());
        }

        var localPreserved = CountryNormalizationAudits.Local
            .Where(a => a.PreservedHistoricalValue && a.NormalizationStatus == CountryNormalizationStatus.Normalized)
            .GroupBy(a => (a.EntityType, a.EntityId, a.FieldName))
            .ToDictionary(g => g.Key, g => g.First().OriginalCountryValue);

        var modifiedIds = targets
            .Where(t => t.State == EntityState.Modified)
            .Select(t => ((BaseEntity<Guid>)t.Entity).Id)
            .Distinct()
            .ToList();

        if (modifiedIds.Count > 0)
        {
            foreach (var row in CountryNormalizationAudits.AsNoTracking()
                .Where(a => modifiedIds.Contains(a.EntityId) && a.PreservedHistoricalValue && a.NormalizationStatus == CountryNormalizationStatus.Normalized)
                .Select(a => new { a.EntityType, a.EntityId, a.FieldName, a.OriginalCountryValue })
                .ToList())
            {
                localPreserved.TryAdd((row.EntityType, row.EntityId, row.FieldName), row.OriginalCountryValue);
            }
        }

        ReconcileCountryEntries(targets, activeCodes, localPreserved);
    }

    private async Task ReconcileCountryNormalizationLifecycleAsync(CancellationToken cancellationToken)
    {
        var targets = ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified && IsCountryLifecycleTarget(e.Entity))
            .ToList();
        if (targets.Count == 0) return;

        var dbCodes = await CountryProfiles.AsNoTracking()
            .Where(c => c.IsActive)
            .Select(c => c.CountryCode)
            .ToListAsync(cancellationToken);
        var activeCodes = dbCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var localCp in ChangeTracker.Entries<CountryProfile>().Where(e => e.State != EntityState.Deleted))
        {
            if (localCp.Entity.IsActive) activeCodes.Add(localCp.Entity.CountryCode.Trim());
            else activeCodes.Remove(localCp.Entity.CountryCode.Trim());
        }

        var localPreserved = CountryNormalizationAudits.Local
            .Where(a => a.PreservedHistoricalValue && a.NormalizationStatus == CountryNormalizationStatus.Normalized)
            .GroupBy(a => (a.EntityType, a.EntityId, a.FieldName))
            .ToDictionary(g => g.Key, g => g.First().OriginalCountryValue);

        var modifiedIds = targets
            .Where(t => t.State == EntityState.Modified)
            .Select(t => ((BaseEntity<Guid>)t.Entity).Id)
            .Distinct()
            .ToList();

        if (modifiedIds.Count > 0)
        {
            var dbPreserved = await CountryNormalizationAudits.AsNoTracking()
                .Where(a => modifiedIds.Contains(a.EntityId) && a.PreservedHistoricalValue && a.NormalizationStatus == CountryNormalizationStatus.Normalized)
                .Select(a => new { a.EntityType, a.EntityId, a.FieldName, a.OriginalCountryValue })
                .ToListAsync(cancellationToken);
            foreach (var row in dbPreserved)
            {
                localPreserved.TryAdd((row.EntityType, row.EntityId, row.FieldName), row.OriginalCountryValue);
            }
        }

        ReconcileCountryEntries(targets, activeCodes, localPreserved);
    }

    private static bool IsValidCatalogCanonicalCode(string? raw, HashSet<string> activeCodes)
    {
        if (string.IsNullOrWhiteSpace(raw) || activeCodes.Count == 0)
        {
            return false;
        }

        var trimmed = raw.Trim();
        return trimmed.Length == 2
            && trimmed == trimmed.ToUpperInvariant()
            && trimmed.All(char.IsAsciiLetterUpper)
            && activeCodes.Contains(trimmed);
    }

    private static void ReconcileCountryEntries(
        IReadOnlyList<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry> targets,
        HashSet<string> activeCodes,
        IReadOnlyDictionary<(string EntityType, Guid EntityId, string FieldName), string?> preservedHistoricalValues)
    {
        foreach (var entry in targets)
        {
            var entityId = ((BaseEntity<Guid>)entry.Entity).Id;
            var statusProp = entry.Property(nameof(Facility.CountryNormalizationStatus));
            var currentStatus = (CountryNormalizationStatus)(statusProp.CurrentValue ?? CountryNormalizationStatus.PendingNormalization);
            bool statusExplicitlyClaimedNormalized = currentStatus == CountryNormalizationStatus.Normalized
                && (entry.State == EntityState.Added || statusProp.IsModified);

            string entityName = entry.Entity.GetType().Name;
            bool allCanonical = true;
            bool allHistoricalPreserved = true;
            bool anyCountryFieldModified = entry.State == EntityState.Added;
            string? firstNonCanonicalRaw = null;

            void EvaluateField(string fieldName, string? val, bool isFieldModified)
            {
                if (isFieldModified) anyCountryFieldModified = true;
                if (IsValidCatalogCanonicalCode(val, activeCodes)) return;

                allCanonical = false;
                firstNonCanonicalRaw ??= val;
                if (!preservedHistoricalValues.TryGetValue((entityName, entityId, fieldName), out var origHistVal) ||
                    !string.Equals((origHistVal ?? string.Empty).Trim(), (val ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    allHistoricalPreserved = false;
                }
            }

            switch (entry.Entity)
            {
                case Facility f:
                    EvaluateField(nameof(Facility.CountryCode), f.CountryCode, entry.Property(nameof(Facility.CountryCode)).IsModified);
                    break;
                case Customer c:
                    EvaluateField(nameof(Customer.DefaultCountryCode), c.DefaultCountryCode, entry.Property(nameof(Customer.DefaultCountryCode)).IsModified);
                    break;
                case Supplier s:
                    EvaluateField(nameof(Supplier.Country), s.Country, entry.Property(nameof(Supplier.Country)).IsModified);
                    break;
                case ShippingAddress sa:
                    EvaluateField(nameof(ShippingAddress.CountryCode), sa.CountryCode, entry.Property(nameof(ShippingAddress.CountryCode)).IsModified);
                    break;
                case ReturnShipment rs:
                    EvaluateField(nameof(ReturnShipment.OriginCountry), rs.OriginCountry, entry.Property(nameof(ReturnShipment.OriginCountry)).IsModified);
                    EvaluateField(nameof(ReturnShipment.DestinationCountry), rs.DestinationCountry, entry.Property(nameof(ReturnShipment.DestinationCountry)).IsModified);
                    break;
                case CarrierContractualSlaPolicy p:
                    EvaluateField(nameof(CarrierContractualSlaPolicy.DestinationCountry), p.DestinationCountry, entry.Property(nameof(CarrierContractualSlaPolicy.DestinationCountry)).IsModified);
                    break;
                case CarrierSlaComplianceRecord r:
                    EvaluateField(nameof(CarrierSlaComplianceRecord.DestinationCountry), r.DestinationCountry, entry.Property(nameof(CarrierSlaComplianceRecord.DestinationCountry)).IsModified);
                    break;
            }

            if (allCanonical)
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
                    $"{entityName} cannot declare CountryNormalizationStatus='Normalized' for unnormalized, blank, or unverified country value '{firstNonCanonicalRaw ?? "<blank>"}'.");
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
