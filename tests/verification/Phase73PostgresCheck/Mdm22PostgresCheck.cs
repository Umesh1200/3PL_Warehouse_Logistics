using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.Clients;
using Warehouse3PL.Application.Features.Clients.Commands.CreateClient;
using Warehouse3PL.Application.Features.Clients.Commands.DeactivateClient;
using Warehouse3PL.Application.Features.Customers;
using Warehouse3PL.Application.Features.Inbound.Suppliers;
using Warehouse3PL.Application.Features.Inbound.Suppliers.Commands.CreateSupplier;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;
using Warehouse3PL.Infrastructure.Persistence.Seed;
using Warehouse3PL.Infrastructure.Services;

internal static class Mdm22PostgresCheck
{
    private const string PreMdm22Migration = "20261127150334_MDM2_1_R01_R06_ConcurrencyAndLifecycleGuards";
    private const string Mdm22Migration = "20261128061719_MDM2_2_ClientCustomerSupplierAndPreferences";

    public static async Task RunAsync(string baseConnectionString)
    {
        Console.WriteLine("\n[MDM-2.2] Running Live PostgreSQL 16 Client, Customer, Supplier & Preferences Verification (PG-37 to PG-41)...");

        await VerifyMigrationPreflightRollbackAndRetryAsync(baseConnectionString);

        var adminBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres" };
        var dbName = $"mdm22_pg_{Guid.NewGuid():N}";
        await CreateDbAsync(adminBuilder.ConnectionString, dbName);
        var targetConn = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = dbName }.ConnectionString;

        try
        {
            await using (var initDb = CreatePgDb(targetConn))
            {
                await initDb.Database.MigrateAsync();
                await ReferenceCatalogSeeder.SeedAsync(initDb);
            }

            var facA = Guid.NewGuid();
            var facB = Guid.NewGuid();
            var adminId = Guid.NewGuid();

            await using (var seedDb = CreatePgDb(targetConn))
            {
                seedDb.Facilities.AddRange(
                    new Facility { Id = facA, FacilityCode = "FAC-M22-A", Name = "MDM22 Hub A", CountryCode = "US", Timezone = "America/Chicago", DefaultCurrencyCode = "USD", IsActive = true },
                    new Facility { Id = facB, FacilityCode = "FAC-M22-B", Name = "MDM22 Hub B", CountryCode = "NP", Timezone = "Asia/Kathmandu", DefaultCurrencyCode = "NPR", IsActive = true });

                var role = new Role { RoleName = SystemRoles.SuperAdmin };
                seedDb.Roles.Add(role);
                await seedDb.SaveChangesAsync();

                seedDb.Users.Add(new User
                {
                    Id = adminId,
                    Email = "mdm22-pg-admin@nexus3pl.test",
                    PasswordHash = "hash",
                    FirstName = "MDM22",
                    LastName = "Admin",
                    UserType = UserType.Staff,
                    Status = UserStatus.Active,
                    AssignedFacilityId = facA
                });
                seedDb.UserRoles.Add(new UserRole { UserId = adminId, RoleId = role.Id });
                await seedDb.SaveChangesAsync();
            }

            var adminUser = new PgSuperAdminUser(adminId, facA);

            // PG-38: Permanent DB-level normalized uniqueness indexes & partial primary index
            Guid clientId;
            await using (var db = CreatePgDb(targetConn, adminUser))
            {
                var auth = new PartnerMasterAuthorization(db, adminUser);
                var createClient = new CreateClientCommandHandler(db, auth);
                var clientDto = await createClient.Handle(new CreateClientCommand
                {
                    CompanyName = "Acme Global Logistics",
                    AccountCode = "acme-01",
                    Currency = "USD",
                    BusinessClassification = "Enterprise",
                    BillingCountryCode = "US",
                    BillingStateOrProvince = "IL",
                    BillingPostalCode = "60601"
                }, default);
                clientId = clientDto.Id;

                // Soft-delete client2 and verify DB rejects normalized duplicate account_code
                var client2 = await createClient.Handle(new CreateClientCommand
                {
                    CompanyName = "Archived Tenant",
                    AccountCode = "arch-01",
                    Currency = "USD"
                }, default);
                var deact = new DeactivateClientCommandHandler(db, adminUser, auth);
                await deact.Handle(new DeactivateClientCommand(client2.Id), default);

                bool dupClientRejected = false;
                try
                {
                    await createClient.Handle(new CreateClientCommand
                    {
                        CompanyName = "Reuse Attempt",
                        AccountCode = "ARCH-01",
                        Currency = "USD"
                    }, default);
                }
                catch (ValidationException)
                {
                    dupClientRejected = true;
                }
                if (!dupClientRejected) throw new Exception("PG-38 failed: soft-deleted account code reuse was not blocked.");

                // Direct DB check for supplier case-insensitive uniqueness
                var createSup = new CreateSupplierCommandHandler(db, adminUser, auth);
                await createSup.Handle(new CreateSupplierCommand
                {
                    ClientId = clientId,
                    SupplierCode = "sup-case-01",
                    SupplierName = "Case Supplier",
                    Country = "US",
                    State = "IL",
                    PostalCode = "60601"
                }, default);

                bool dupSupRejected = false;
                try
                {
                    await createSup.Handle(new CreateSupplierCommand
                    {
                        ClientId = clientId,
                        SupplierCode = "SUP-CASE-01",
                        SupplierName = "Duplicate Case Supplier"
                    }, default);
                }
                catch (DomainException ex) when (ex.Code == "SUPPLIER_CODE_DUPLICATE")
                {
                    dupSupRejected = true;
                }
                if (!dupSupRejected) throw new Exception("PG-38 failed: case-insensitive supplier_code duplicate was not blocked.");

                // Direct DB check for customer normalized uniqueness
                var createCust = new CreateCustomerCommandHandler(db, auth);
                await createCust.Handle(new CreateCustomerCommand
                {
                    ClientId = clientId,
                    CustomerCode = "cust-01",
                    CustomerName = "Primary Customer",
                    DefaultCountryCode = "US",
                    DefaultStateOrProvince = "IL",
                    DefaultPostalCode = "60601"
                }, default);

                bool dupCustRejected = false;
                try
                {
                    await createCust.Handle(new CreateCustomerCommand
                    {
                        ClientId = clientId,
                        CustomerCode = "CUST-01",
                        CustomerName = "Dup Customer"
                    }, default);
                }
                catch (DomainException ex) when (ex.Code == "CUSTOMER_CODE_DUPLICATE")
                {
                    dupCustRejected = true;
                }
                if (!dupCustRejected) throw new Exception("PG-38 failed: case-insensitive customer_code duplicate was not blocked.");
            }
            Console.WriteLine("  [PASS] PG-38: Permanent normalized uniqueness indexes verified on live PostgreSQL 16.");

            // PG-39: Concurrent primary operating facility switching under advisory locks
            await using (var grantDb = CreatePgDb(targetConn, adminUser))
            {
                var grant = new GrantClientFacilityAccessCommandHandler(grantDb, new PartnerMasterAuthorization(grantDb, adminUser));
                await grant.Handle(new GrantClientFacilityAccessCommand { ClientId = clientId, FacilityId = facA, PrimaryOperatingFacility = true }, default);
                await grant.Handle(new GrantClientFacilityAccessCommand { ClientId = clientId, FacilityId = facB, PrimaryOperatingFacility = false }, default);
            }

            var t1 = Task.Run(async () =>
            {
                await using var db1 = CreatePgDb(targetConn, adminUser);
                var h = new UpdateClientFacilityAccessStatusCommandHandler(db1, new PartnerMasterAuthorization(db1, adminUser));
                await h.Handle(new UpdateClientFacilityAccessStatusCommand { ClientId = clientId, FacilityId = facB, Status = FacilityAccessStatus.Active, PrimaryOperatingFacility = true }, default);
            });
            var t2 = Task.Run(async () =>
            {
                await using var db2 = CreatePgDb(targetConn, adminUser);
                var h = new UpdateClientFacilityAccessStatusCommandHandler(db2, new PartnerMasterAuthorization(db2, adminUser));
                await h.Handle(new UpdateClientFacilityAccessStatusCommand { ClientId = clientId, FacilityId = facA, Status = FacilityAccessStatus.Active, PrimaryOperatingFacility = true }, default);
            });
            await Task.WhenAll(t1, t2);

            await using (var verifyDb = CreatePgDb(targetConn, adminUser))
            {
                var primaryCount = await verifyDb.ClientFacilityAccesses
                    .IgnoreQueryFilters()
                    .CountAsync(a => a.ClientId == clientId && a.PrimaryOperatingFacility && a.Status == FacilityAccessStatus.Active);
                if (primaryCount != 1)
                {
                    throw new Exception($"PG-39 failed: expected exactly 1 active primary operating facility after concurrent switch, found {primaryCount}.");
                }
            }
            Console.WriteLine("  [PASS] PG-39: Concurrent primary operating facility switching serialized cleanly with exactly 1 primary.");

            // PG-40: MDM-1.5 multi-context epoch & outbox invalidation across Client, Preferences, Customer, Supplier
            await using (var db = CreatePgDb(targetConn, adminUser))
            {
                var clientScopeKey = clientId.ToString("D").ToUpperInvariant();
                var epochBefore = await db.ConfigurationScopeEpochs
                    .AsNoTracking()
                    .Where(e => e.ScopeType == ConfigurationScopeType.Client && e.ScopeKey == clientScopeKey)
                    .Select(e => e.CurrentEpoch)
                    .FirstOrDefaultAsync();

                var upsertPref = new UpsertClientOperationalPreferenceCommandHandler(db, new PartnerMasterAuthorization(db, adminUser));
                await upsertPref.Handle(new UpsertClientOperationalPreferenceCommand
                {
                    ClientId = clientId,
                    FacilityId = facA,
                    RequireAsnBeforeReceiving = true,
                    AllowPartialShipments = false,
                    DefaultOrderPriority = 3
                }, default);

                var epochAfter = await db.ConfigurationScopeEpochs
                    .AsNoTracking()
                    .Where(e => e.ScopeType == ConfigurationScopeType.Client && e.ScopeKey == clientScopeKey)
                    .Select(e => e.CurrentEpoch)
                    .FirstOrDefaultAsync();

                if (epochAfter <= epochBefore)
                {
                    throw new Exception($"PG-40 failed: Client ConfigurationScopeEpoch did not increment ({epochBefore} -> {epochAfter}).");
                }

                var outboxExists = await db.ConfigurationInvalidationEvents
                    .AsNoTracking()
                    .AnyAsync(e => e.ScopeType == ConfigurationScopeType.Client &&
                                   e.ScopeKey == clientScopeKey &&
                                   e.EntityType == nameof(ClientOperationalPreference));
                if (!outboxExists)
                {
                    throw new Exception("PG-40 failed: ConfigurationInvalidationEvent outbox entry missing for ClientOperationalPreference.");
                }
            }
            Console.WriteLine("  [PASS] PG-40: MDM-1.5 scope epoch & transactional outbox invalidation verified on live PostgreSQL 16.");

            // PG-41: Reversible Down() -> re-Up() migration verification
            await using (var migDb = CreatePgDb(targetConn))
            {
                var migrator = migDb.GetService<IMigrator>();
                await migrator.MigrateAsync(PreMdm22Migration);
                await migrator.MigrateAsync(Mdm22Migration);

                var countAfterReUp = await migDb.Clients.IgnoreQueryFilters().CountAsync();
                if (countAfterReUp < 2)
                {
                    throw new Exception($"PG-41 failed: expected >= 2 preserved clients after Down->Up, found {countAfterReUp}.");
                }
            }
            Console.WriteLine("  [PASS] PG-41: Reversible Down() -> re-Up() migration verified with data preservation.");
        }
        finally
        {
            await DropDbAsync(adminBuilder.ConnectionString, dbName);
        }
    }

    private static async Task VerifyMigrationPreflightRollbackAndRetryAsync(string baseConnectionString)
    {
        var adminBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres" };
        var dbName = $"mdm22_preflight_{Guid.NewGuid():N}";
        await CreateDbAsync(adminBuilder.ConnectionString, dbName);
        var targetConn = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = dbName }.ConnectionString;

        try
        {
            await using (var db = CreatePgDb(targetConn))
            {
                var migrator = db.GetService<IMigrator>();
                await migrator.MigrateAsync(PreMdm22Migration);
            }

            // Seed a pre-MDM-2.2 populated database with case-insensitive supplier_code collision
            var clientId = Guid.NewGuid();
            var sup1 = Guid.NewGuid();
            var sup2 = Guid.NewGuid();
            await using (var conn = new NpgsqlConnection(targetConn))
            {
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
INSERT INTO clients (client_id, company_name, account_code, status, sla_tier, currency, is_deleted, created_at)
VALUES (@cid, 'Preflight Client', 'PF-CL-1', 'Active', 'Standard', 'USD', FALSE, NOW());

INSERT INTO suppliers (supplier_id, client_id, supplier_code, supplier_name, country_normalization_status, is_active, is_deleted, created_at)
VALUES (@s1, @cid, 'dup-sup', 'Supplier Lower', 'PendingNormalization', TRUE, FALSE, NOW()),
       (@s2, @cid, 'DUP-SUP', 'Supplier Upper', 'PendingNormalization', TRUE, FALSE, NOW());
";
                cmd.Parameters.AddWithValue("cid", clientId);
                cmd.Parameters.AddWithValue("s1", sup1);
                cmd.Parameters.AddWithValue("s2", sup2);
                await cmd.ExecuteNonQueryAsync();
            }

            // Attempt upgrade -> MUST fail closed with preflight collision error
            bool failedClosed = false;
            await using (var dbFail = CreatePgDb(targetConn))
            {
                try
                {
                    await dbFail.Database.MigrateAsync();
                }
                catch (PostgresException ex) when (ex.MessageText.Contains("MDM-2.2 preflight failed", StringComparison.OrdinalIgnoreCase))
                {
                    failedClosed = true;
                }
            }
            if (!failedClosed)
            {
                throw new Exception("PG-37 failed: MDM-2.2 migration did not fail closed on case-insensitive supplier_code collision.");
            }

            // Resolve the collision and retry -> MUST succeed cleanly and backfill normalized codes
            await using (var conn = new NpgsqlConnection(targetConn))
            {
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE suppliers SET supplier_code = 'DUP-SUP-2' WHERE supplier_id = @s2;";
                cmd.Parameters.AddWithValue("s2", sup2);
                await cmd.ExecuteNonQueryAsync();
            }

            await using (var dbRetry = CreatePgDb(targetConn))
            {
                await dbRetry.Database.MigrateAsync();
                var backfilled = await dbRetry.Suppliers
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(s => s.Id == sup1)
                    .Select(s => s.NormalizedSupplierCode)
                    .FirstAsync();
                if (backfilled != "DUP-SUP")
                {
                    throw new Exception($"PG-37 failed: expected normalized_supplier_code 'DUP-SUP', got '{backfilled}'.");
                }
            }

            Console.WriteLine("  [PASS] PG-37: Pre-MDM-2.2 populated database preflight fail-closed, rollback, remediation, and backfill verified.");
        }
        finally
        {
            await DropDbAsync(adminBuilder.ConnectionString, dbName);
        }
    }

    private static ApplicationDbContext CreatePgDb(string connectionString, ICurrentUserContext? user = null)
    {
        var activeUser = user ?? new PgSuperAdminUser(Guid.NewGuid(), Guid.NewGuid());
        var interceptor = new AuditableEntityInterceptor(activeUser, new DateTimeProvider());
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString, b => b.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(interceptor)
            .Options;
        var tenant = new PgTenantProvider();
        return new ApplicationDbContext(opts, tenant, activeUser, interceptor);
    }

    private static async Task CreateDbAsync(string adminConn, string dbName)
    {
        await using var conn = new NpgsqlConnection(adminConn);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($@"CREATE DATABASE ""{dbName}""", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task DropDbAsync(string adminConn, string dbName)
    {
        try
        {
            await using var conn = new NpgsqlConnection(adminConn);
            await conn.OpenAsync();
            await using var term = new NpgsqlCommand(
                $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{dbName}' AND pid <> pg_backend_pid();", conn);
            await term.ExecuteNonQueryAsync();
            await using var drop = new NpgsqlCommand($@"DROP DATABASE IF EXISTS ""{dbName}""", conn);
            await drop.ExecuteNonQueryAsync();
        }
        catch { }
    }

    private sealed class PgTenantProvider : ITenantProvider
    {
        public Guid? TenantId => null;
        public bool HasTenant => false;
        public bool BypassTenantFilter { get; set; } = true;
        public void SetTenantId(Guid tenantId) { }
    }

    private sealed class PgSuperAdminUser(Guid userId, Guid facilityId) : ICurrentUserContext
    {
        public Guid? UserId => userId;
        public string? Email => "mdm22-pg@nexus3pl.test";
        public Guid? TenantId => null;
        public Guid? RequestedTenantId => null;
        public Guid? FacilityId => facilityId;
        public Guid? RequestedFacilityId => null;
        public UserType? UserType => Warehouse3PL.Domain.Enums.UserType.Staff;
        public IReadOnlyList<string> Roles => [SystemRoles.SuperAdmin];
        public IReadOnlyList<string> Permissions => Warehouse3PL.Domain.Constants.Permissions.GetAllPermissions();
        public bool IsAuthenticated => true;
        public bool IsStaff => true;
        public bool IsSuperAdmin => true;
        public bool HasPermission(string permission) => true;
        public bool IsInRole(string role) => string.Equals(role, SystemRoles.SuperAdmin, StringComparison.OrdinalIgnoreCase);
    }
}
