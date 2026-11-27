#pragma warning disable EF1001

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql.EntityFrameworkCore.PostgreSQL.Migrations.Internal;
using Warehouse3PL.Infrastructure.Services.Reference;

namespace Warehouse3PL.Infrastructure.Persistence;

/// <summary>
/// Enforces the MDM-1.6 safe upgrade boundary for all EF Core migration invocations
/// (<c>dotnet ef database update</c>, <c>Database.MigrateAsync()</c>, and <c>IMigrator.MigrateAsync()</c>).
/// Blocks unsafe direct execution of <c>20261127051434_MDM1_6_R04_NormalizationRemediationLifecycle</c>
/// against populated databases already at <c>20261126143014_MDM1_6_LegacyCountryNormalization</c> unless
/// invoked through the supported <see cref="Mdm16UpgradePreflightService.ExecuteSafeUpgradeAsync"/> entry point.
/// </summary>
public sealed class Mdm16GuardedMigrator : NpgsqlMigrator
{
    private readonly IHistoryRepository _historyRepository;
    private readonly IRelationalConnection _connection;

    public Mdm16GuardedMigrator(
        IMigrationsAssembly migrationsAssembly,
        IHistoryRepository historyRepository,
        IDatabaseCreator databaseCreator,
        IMigrationsSqlGenerator migrationsSqlGenerator,
        IRawSqlCommandBuilder rawSqlCommandBuilder,
        IMigrationCommandExecutor migrationCommandExecutor,
        IRelationalConnection connection,
        ISqlGenerationHelper sqlGenerationHelper,
        ICurrentDbContext currentContext,
        IModelRuntimeInitializer modelRuntimeInitializer,
        IDiagnosticsLogger<DbLoggerCategory.Migrations> logger,
        IRelationalCommandDiagnosticsLogger commandLogger,
        IDatabaseProvider databaseProvider,
        IMigrationsModelDiffer migrationsModelDiffer,
        IDesignTimeModel designTimeModel,
        IDbContextOptions contextOptions,
        IExecutionStrategy executionStrategy)
        : base(
            migrationsAssembly,
            historyRepository,
            databaseCreator,
            migrationsSqlGenerator,
            rawSqlCommandBuilder,
            migrationCommandExecutor,
            connection,
            sqlGenerationHelper,
            currentContext,
            modelRuntimeInitializer,
            logger,
            commandLogger,
            databaseProvider,
            migrationsModelDiffer,
            designTimeModel,
            contextOptions,
            executionStrategy)
    {
        _historyRepository = historyRepository;
        _connection = connection;
    }

    public override void Migrate(string? targetMigration = null)
    {
        EnsureSafeUpgradeBoundary(targetMigration);
        base.Migrate(targetMigration);
    }

    public override async Task MigrateAsync(
        string? targetMigration = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureSafeUpgradeBoundaryAsync(targetMigration, cancellationToken);
        await base.MigrateAsync(targetMigration, cancellationToken);
    }

    private void EnsureSafeUpgradeBoundary(string? targetMigration)
    {
        if (Mdm16UpgradePreflightService.IsAuthorizedSafeUpgradeActive)
        {
            return;
        }

        if (!WouldExecuteR04(targetMigration))
        {
            return;
        }

        if (!_historyRepository.Exists())
        {
            return;
        }

        var applied = _historyRepository.GetAppliedMigrations()
            .Select(m => m.MigrationId)
            .ToHashSet(StringComparer.Ordinal);

        if (!applied.Contains(Mdm16UpgradePreflightService.Mdm16InitialMigrationId) ||
            applied.Contains(Mdm16UpgradePreflightService.Mdm16R04MigrationId))
        {
            return;
        }

        if (HasPopulatedAffectedRecordsSync())
        {
            throw new InvalidOperationException(BuildBlockedBypassMessage());
        }
    }

    private async Task EnsureSafeUpgradeBoundaryAsync(string? targetMigration, CancellationToken cancellationToken)
    {
        if (Mdm16UpgradePreflightService.IsAuthorizedSafeUpgradeActive)
        {
            return;
        }

        if (!WouldExecuteR04(targetMigration))
        {
            return;
        }

        if (!await _historyRepository.ExistsAsync(cancellationToken))
        {
            return;
        }

        var applied = (await _historyRepository.GetAppliedMigrationsAsync(cancellationToken))
            .Select(m => m.MigrationId)
            .ToHashSet(StringComparer.Ordinal);

        if (!applied.Contains(Mdm16UpgradePreflightService.Mdm16InitialMigrationId) ||
            applied.Contains(Mdm16UpgradePreflightService.Mdm16R04MigrationId))
        {
            return;
        }

        if (await HasPopulatedAffectedRecordsAsync(cancellationToken))
        {
            throw new InvalidOperationException(BuildBlockedBypassMessage());
        }
    }

    private static bool WouldExecuteR04(string? targetMigration)
    {
        if (string.IsNullOrWhiteSpace(targetMigration))
        {
            return true;
        }

        return string.CompareOrdinal(targetMigration.Trim(), Mdm16UpgradePreflightService.Mdm16R04MigrationId) >= 0;
    }

    private bool HasPopulatedAffectedRecordsSync()
    {
        bool shouldClose = _connection.DbConnection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            _connection.Open();
        }

        try
        {
            using var cmd = _connection.DbConnection.CreateCommand();
            cmd.CommandText = """
                SELECT (SELECT COUNT(*) FROM shipping_addresses) + (SELECT COUNT(*) FROM country_normalization_audits);
                """;
            var count = Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
            return count > 0;
        }
        finally
        {
            if (shouldClose)
            {
                _connection.Close();
            }
        }
    }

    private async Task<bool> HasPopulatedAffectedRecordsAsync(CancellationToken cancellationToken)
    {
        bool shouldClose = _connection.DbConnection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            await _connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var cmd = _connection.DbConnection.CreateCommand();
            cmd.CommandText = """
                SELECT (SELECT COUNT(*) FROM shipping_addresses) + (SELECT COUNT(*) FROM country_normalization_audits);
                """;
            var count = Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken) ?? 0L);
            return count > 0;
        }
        finally
        {
            if (shouldClose)
            {
                await _connection.CloseAsync();
            }
        }
    }

    private static string BuildBlockedBypassMessage() =>
        $"Unsafe MDM-1.6 migration bypass blocked: database is at {Mdm16UpgradePreflightService.Mdm16InitialMigrationId} " +
        $"with populated operational records. Direct MigrateAsync / 'dotnet ef database update' cannot execute " +
        $"{Mdm16UpgradePreflightService.Mdm16R04MigrationId} without the protected preflight boundary. " +
        "Execute the supported migration entry point instead: " +
        "dotnet run --project src/Presentation/Warehouse3PL.WebApi -- --migrate-db";
}
