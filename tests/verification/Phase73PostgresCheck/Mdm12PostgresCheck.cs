using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Warehouse3PL.Application;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.Facilities.Commands;
using Warehouse3PL.Application.Features.LegalEntities.Commands;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Authentication;
using Warehouse3PL.Infrastructure.Authorization;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;
using Warehouse3PL.Infrastructure.Persistence.Seed;
using Warehouse3PL.Infrastructure.Services;
using Warehouse3PL.Infrastructure.Services.Reference;
using Warehouse3PL.Infrastructure.Services.Shipping;
using Warehouse3PL.WebApi.Controllers;
using Warehouse3PL.WebApi.Middleware;

internal static class Mdm12PostgresCheck
{
    private const string JwtSecret = "Nexus3PL-Mdm12-PostgreSql-Verification-Key-2026!";
    private const string JwtIssuer = "nexus3pl-mdm12-pg-auth";
    private const string JwtAudience = "nexus3pl-mdm12-pg-api";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public static async Task RunAsync(string baseConnectionString)
    {
        Console.WriteLine("\n[MDM-1.2] Running Live PostgreSQL LegalEntity Separation, Facility Binding, Restrict FK & Origin Tests...");

        var adminBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres" };
        var dbName = $"mdm12_pg_{Guid.NewGuid():N}";

        await using (var adminConn = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await adminConn.OpenAsync();
            await using var createCmd = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", adminConn);
            await createCmd.ExecuteNonQueryAsync();
        }

        var targetBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = dbName };
        var targetConn = targetBuilder.ConnectionString;

        try
        {
            var legacyFacId = Guid.NewGuid();

            // 1. Apply migrations up to MDM-1.1, seed legacy Facility via raw SQL ("USA", pre-MDM1.2 schema), then upgrade to MDM-1.2
            await using (var preMigDb = CreatePgDb(targetConn, null))
            {
                var migrator = preMigDb.GetInfrastructure().GetRequiredService<IMigrator>();
                await migrator.MigrateAsync("20261125102002_MDM1_1_GlobalReferencesAndCompliance");
                await ReferenceCatalogSeeder.SeedAsync(preMigDb);
            }

            await using (var preSqlConn = new NpgsqlConnection(targetConn))
            {
                await preSqlConn.OpenAsync();
                await using var insLegacyCmd = new NpgsqlCommand($@"
                    INSERT INTO facilities (facility_id, facility_code, name, address_line1, city, state_or_province, postal_code, country_code, timezone, dock_door_count, is_active, is_deleted, created_at)
                    VALUES ('{legacyFacId}', 'PG-LEGACY-ORD', 'Legacy Pre-MDM12 Hub', '500 Legacy Dr', 'Chicago', 'IL', '60601', 'USA', 'America/Chicago', 4, true, false, NOW());",
                    preSqlConn);
                await insLegacyCmd.ExecuteNonQueryAsync();
            }

            await using (var postMigDb = CreatePgDb(targetConn, null))
            {
                var migrator = postMigDb.GetInfrastructure().GetRequiredService<IMigrator>();
                await migrator.MigrateAsync("20261125134640_MDM1_2_LegalEntitiesAndFacilityBinding");

                var legacyAfter = await postMigDb.Facilities.AsNoTracking().SingleAsync(f => f.Id == legacyFacId);
                if (legacyAfter.CountryCode != "USA" || legacyAfter.DefaultLegalEntityId != null || legacyAfter.DefaultCurrencyCode != null)
                {
                    throw new InvalidOperationException("MDM-1.2 PostgreSQL migration mutated legacy facility defaults unexpectedly.");
                }

                // Upgrade remaining MDM migrations so live HTTP checks run on current full schema
                await migrator.MigrateAsync();
            }

            // 2. Real JWT HTTP checks + LegalEntity creation + Facility binding + Restrict FK + Rollback
            var clientId = Guid.NewGuid();
            var clientUserId = Guid.NewGuid();
            var plainStaffId = Guid.NewGuid();
            var adminStaffId = Guid.NewGuid();

            await using var host = await BuildPgHttpAppAsync(targetConn);
            using var http = host.GetTestClient();

            await using (var setupDb = CreatePgDb(targetConn, null))
            {
                setupDb.Clients.Add(new Client { Id = clientId, AccountCode = "PG-M12", CompanyName = "PG M12 Client", Status = ClientStatus.Active });
                setupDb.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = clientId, FacilityId = legacyFacId, Status = FacilityAccessStatus.Active });

                var opRole = new Role { RoleName = SystemRoles.WarehouseOperator, Description = "Op", IsSystemRole = true };
                var multiReadFacMgrRole = new Role { RoleName = "MultiReadSingleFacMgr", Description = "Read cross-facility + manage assigned", IsSystemRole = false };
                multiReadFacMgrRole.RolePermissions.Add(new RolePermission { Role = multiReadFacMgrRole, PermissionKey = Permissions.Facilities.View, AccessLevel = AccessLevel.Read });
                multiReadFacMgrRole.RolePermissions.Add(new RolePermission { Role = multiReadFacMgrRole, PermissionKey = Permissions.Facilities.Manage, AccessLevel = AccessLevel.Admin });
                multiReadFacMgrRole.RolePermissions.Add(new RolePermission { Role = multiReadFacMgrRole, PermissionKey = Permissions.Analytics.MultiFacilityView, AccessLevel = AccessLevel.Read });
                var saRole = new Role { RoleName = SystemRoles.SuperAdmin, Description = "Admin", IsSystemRole = true };
                foreach (var perm in Permissions.GetAllPermissions())
                {
                    saRole.RolePermissions.Add(new RolePermission { Role = saRole, PermissionKey = perm, AccessLevel = AccessLevel.Admin });
                }
                setupDb.Roles.AddRange(opRole, multiReadFacMgrRole, saRole);

                var multiReadStaffId = Guid.NewGuid();
                var unassignedReadStaffId = Guid.NewGuid();
                var uClient = new User { Id = clientUserId, Email = "pgc@m12.test", FirstName = "C", LastName = "U", UserType = UserType.Client, ClientId = clientId, Status = UserStatus.Active, PasswordHash = "x" };
                var uPlain = new User { Id = plainStaffId, Email = "pgp@m12.test", FirstName = "P", LastName = "S", UserType = UserType.Staff, AssignedFacilityId = legacyFacId, Status = UserStatus.Active, PasswordHash = "x" };
                var uMultiRead = new User { Id = multiReadStaffId, Email = "pgmra@m12.test", FirstName = "M", LastName = "A", UserType = UserType.Staff, AssignedFacilityId = legacyFacId, Status = UserStatus.Active, PasswordHash = "x" };
                var uUnassignedRead = new User { Id = unassignedReadStaffId, Email = "pgmru@m12.test", FirstName = "M", LastName = "U", UserType = UserType.Staff, AssignedFacilityId = null, Status = UserStatus.Active, PasswordHash = "x" };
                var uAdmin = new User { Id = adminStaffId, Email = "pga@m12.test", FirstName = "A", LastName = "S", UserType = UserType.Staff, Status = UserStatus.Active, PasswordHash = "x" };
                setupDb.Users.AddRange(uClient, uPlain, uMultiRead, uUnassignedRead, uAdmin);
                setupDb.UserRoles.AddRange(
                    new UserRole { User = uPlain, Role = opRole },
                    new UserRole { User = uMultiRead, Role = multiReadFacMgrRole },
                    new UserRole { User = uUnassignedRead, Role = multiReadFacMgrRole },
                    new UserRole { User = uAdmin, Role = saRole });
                await setupDb.SaveChangesAsync();
            }

            string TokenFor(Guid uid, UserType ut, Guid? cid, Guid? fid, string[] roles, string[] perms)
            {
                using var scope = host.Services.CreateScope();
                return scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateTokens(new User
                {
                    Id = uid, Email = $"{uid:N}@m12.test", FirstName = "T", LastName = "U", UserType = ut, ClientId = cid, AssignedFacilityId = fid
                }, roles, perms).AccessToken;
            }

            Guid lookupMultiReadId;
            Guid lookupUnassignedId;
            await using (var idDb = CreatePgDb(targetConn, null))
            {
                lookupMultiReadId = await idDb.Users.Where(u => u.Email == "pgmra@m12.test").Select(u => u.Id).SingleAsync();
                lookupUnassignedId = await idDb.Users.Where(u => u.Email == "pgmru@m12.test").Select(u => u.Id).SingleAsync();
            }

            var jwtClient = TokenFor(clientUserId, UserType.Client, clientId, null, [], [Permissions.Facilities.View]);
            var jwtPlainSpoof = TokenFor(plainStaffId, UserType.Staff, null, legacyFacId, [SystemRoles.WarehouseOperator], [Permissions.Global.ManageLegalEntities, Permissions.Facilities.Manage]);
            var jwtMultiReadAssigned = TokenFor(lookupMultiReadId, UserType.Staff, null, legacyFacId, ["MultiReadSingleFacMgr"], [Permissions.Facilities.View, Permissions.Facilities.Manage, Permissions.Analytics.MultiFacilityView, "Analytics:CrossFacility"]);
            var jwtMultiReadUnassigned = TokenFor(lookupUnassignedId, UserType.Staff, null, null, ["MultiReadSingleFacMgr"], [Permissions.Facilities.View, Permissions.Facilities.Manage, Permissions.Analytics.MultiFacilityView, "Analytics:CrossFacility"]);
            var jwtAdmin = TokenFor(adminStaffId, UserType.Staff, null, null, [SystemRoles.SuperAdmin], Permissions.GetAllPermissions().ToArray());

            if ((await http.GetAsync("/api/v1/legal-entities")).StatusCode != HttpStatusCode.Unauthorized ||
                (await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/legal-entities", jwtClient))).StatusCode != HttpStatusCode.Forbidden ||
                (await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/legal-entities", jwtPlainSpoof))).StatusCode != HttpStatusCode.Forbidden)
            {
                throw new InvalidOperationException("MDM-1.2 PostgreSQL RBAC check failed.");
            }

            // Create Kathmandu LegalEntity HQ (PAN 601234567) & Birgunj physical Facility bound to it
            var leResp = await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/legal-entities", jwtAdmin, new
            {
                legalCode = "PG-LE-NP",
                legalName = "Nexus Nepal Corporate HQ Pvt Ltd",
                registrationNumber = "PG-REG-01",
                countryCode = "NP",
                taxIdentifierType = TaxIdentifierType.PAN,
                taxRegistrationNumber = "601234567",
                registeredAddressLine1 = "Durbar Marg HQ Tower",
                registeredCity = "Kathmandu",
                registeredStateOrProvince = "Bagmati",
                registeredPostalCode = "44600",
                isCbmsMandated = true,
                iroSoftwarePermitRef = "PG-IRO-2082",
                defaultCurrencyCode = "NPR"
            }));
            if (leResp.StatusCode != HttpStatusCode.Created)
                throw new InvalidOperationException($"MDM-1.2 PG LegalEntity create failed: {leResp.StatusCode}");
            var npLe = (await leResp.Content.ReadFromJsonAsync<LegalEntityDto>(JsonOptions))!;

            var facResp = await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/facilities", jwtAdmin, new
            {
                facilityCode = "PG-BRG-1",
                name = "Birgunj Physical ICD Warehouse",
                addressLine1 = "Sirsiya Dry Port Bay 9",
                city = "Birgunj",
                stateOrProvince = "Madhesh",
                postalCode = "44300",
                countryCode = "NP",
                timezone = "Asia/Kathmandu",
                defaultLegalEntityId = npLe.Id,
                defaultCurrencyCode = "NPR",
                contactPhone = "+977-51-5550199",
                dockDoorCount = 6
            }));
            if (facResp.StatusCode != HttpStatusCode.Created)
                throw new InvalidOperationException($"MDM-1.2 PG Facility create failed: {facResp.StatusCode}");
            var brgFac = (await facResp.Content.ReadFromJsonAsync<FacilityDto>(JsonOptions))!;

            // Verify raw PostgreSQL OnDelete(Restrict) blocks deleting referenced LegalEntity (SQLSTATE 23503)
            await using (var fkConn = new NpgsqlConnection(targetConn))
            {
                await fkConn.OpenAsync();
                bool fkBlocked = false;
                try
                {
                    await using var delCmd = new NpgsqlCommand($"DELETE FROM legal_entities WHERE legal_entity_id = '{npLe.Id}';", fkConn);
                    await delCmd.ExecuteNonQueryAsync();
                }
                catch (PostgresException pgEx) when (pgEx.SqlState == "23503")
                {
                    fkBlocked = true;
                }

                if (!fkBlocked)
                    throw new InvalidOperationException("MDM-1.2 PostgreSQL FK Restrict did not block deleting referenced LegalEntity.");
            }

            // API deactivate/delete while bound -> 409 Conflict
            if ((await http.SendAsync(Authed(HttpMethod.Patch, $"/api/v1/legal-entities/{npLe.Id}/status", jwtAdmin, new { isActive = false }))).StatusCode != HttpStatusCode.Conflict ||
                (await http.SendAsync(Authed(HttpMethod.Delete, $"/api/v1/legal-entities/{npLe.Id}", jwtAdmin))).StatusCode != HttpStatusCode.Conflict)
            {
                throw new InvalidOperationException("MDM-1.2 PostgreSQL LegalEntity in-use conflict guard failed.");
            }

            // Rollback atomicity check on UpdateFacilityCommandHandler via ThrowAfterSaveInterceptor
            var adminContext = new PgSuperAdminUserContext(adminStaffId);
            await using (var rbDb = CreatePgDb(targetConn, new ThrowAfterSaveInterceptor(), adminContext))
            {
                var h = new UpdateFacilityCommandHandler(rbDb, new CompliancePlatformAuthorization(rbDb, adminContext), new TimeZoneOffsetResolver());
                try
                {
                    await h.Handle(new UpdateFacilityCommand(
                        brgFac.Id, null, "Rolled Back Name", brgFac.AddressLine1, null, brgFac.City, brgFac.StateOrProvince,
                        brgFac.PostalCode, "NP", "Asia/Kathmandu", brgFac.DefaultLegalEntityId, "NPR", null, brgFac.ContactPhone, null, 6, true), CancellationToken.None);
                    throw new InvalidOperationException("Expected ForcedRollbackException did not throw.");
                }
                catch (ForcedRollbackException)
                {
                    // Expected
                }
            }

            await using (var verifyRb = CreatePgDb(targetConn, null))
            {
                var facCheck = await verifyRb.Facilities.AsNoTracking().SingleAsync(f => f.Id == brgFac.Id);
                if (facCheck.Name != "Birgunj Physical ICD Warehouse")
                    throw new InvalidOperationException("MDM-1.2 PostgreSQL UpdateFacility rollback leaked state.");
            }

            // 3. Verify ShippingLabelService uses Birgunj physical Facility origin, NOT Kathmandu LegalEntity HQ!
            var pkgId = Guid.NewGuid();
            await using (var shipDb = CreatePgDb(targetConn, null))
            {
                var orderId = Guid.NewGuid();
                var huId = Guid.NewGuid();
                shipDb.OutboundOrders.Add(new OutboundOrder
                {
                    Id = orderId,
                    ClientId = clientId,
                    FacilityId = brgFac.Id,
                    OrderNumber = "PG-M12-SO-1",
                    CustomerName = "Pokhara Store",
                    Status = OutboundOrderStatus.InProgress,
                    ShippingAddress = new ShippingAddress
                    {
                        OutboundOrderId = orderId,
                        RecipientName = "Ram Thapa",
                        AddressLine1 = "New Road 9",
                        City = "Pokhara",
                        StateOrProvince = "Gandaki",
                        PostalCode = "33700",
                        CountryCode = "NP"
                    }
                });
                shipDb.HandlingUnits.Add(new HandlingUnit
                {
                    Id = huId, ClientId = clientId, FacilityId = brgFac.Id, LpnCode = "PG-M12-HU",
                    UnitType = HandlingUnitType.ShippingCarton, Status = HandlingUnitStatus.InUse, CurrentOutboundOrderId = orderId
                });
                shipDb.Packages.Add(new Package
                {
                    Id = pkgId, ClientId = clientId, FacilityId = brgFac.Id, OutboundOrderId = orderId,
                    HandlingUnitId = huId, PackageNumber = "PG-M12-PKG",
                    LengthCm = 30m, WidthCm = 20m, HeightCm = 15m, GrossWeightKg = 2.5m, TareWeightKg = 0.3m, NetWeightKg = 2.2m,
                    Status = PackageStatus.Packed
                });
                await shipDb.SaveChangesAsync();

                var spyCarrier = new PgOriginSpyCarrier();
                var labelSvc = new ShippingLabelService(shipDb, spyCarrier, NullLogger<ShippingLabelService>.Instance);
                await labelSvc.GetRatesForPackageAsync(new GetRatesForPackageRequest { PackageId = pkgId });

                if (spyCarrier.LastOrigin == null ||
                    spyCarrier.LastOrigin.City != "Birgunj" ||
                    spyCarrier.LastOrigin.PostalCode != "44300" ||
                    spyCarrier.LastOrigin.StreetLine1.Contains("Durbar", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"MDM-1.2 PostgreSQL shipping origin leaked LegalEntity HQ instead of physical Facility: {spyCarrier.LastOrigin?.City}/{spyCarrier.LastOrigin?.PostalCode}");
                }
            }

            // 4. MDM12-R01: analytics:multi-facility:view MUST NOT grant cross-facility PUT or facility POST
            if ((await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/facilities", jwtMultiReadAssigned, new
                {
                    facilityCode = "PG-MRA-NO", name = "No Create", countryCode = "NP", timezone = "Asia/Kathmandu"
                }))).StatusCode != HttpStatusCode.Forbidden ||
                (await http.SendAsync(Authed(HttpMethod.Put, $"/api/v1/facilities/{brgFac.Id}", jwtMultiReadAssigned, new
                {
                    name = "No Cross Fac Write", countryCode = "NP", timezone = "Asia/Kathmandu", dockDoorCount = 6, isActive = true
                }))).StatusCode != HttpStatusCode.Forbidden ||
                (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/facilities", jwtMultiReadUnassigned, new
                {
                    facilityCode = "PG-MRU-NO", name = "No Create Unassigned", countryCode = "NP", timezone = "Asia/Kathmandu"
                }))).StatusCode != HttpStatusCode.Forbidden ||
                (await http.SendAsync(Authed(HttpMethod.Put, $"/api/v1/facilities/{brgFac.Id}", jwtMultiReadUnassigned, new
                {
                    name = "No Cross Fac Write Unassigned", countryCode = "NP", timezone = "Asia/Kathmandu", dockDoorCount = 6, isActive = true
                }))).StatusCode != HttpStatusCode.Forbidden)
            {
                throw new InvalidOperationException("MDM12-R01 PostgreSQL multi-facility read permission escalated into facility write scope!");
            }

            // 5. MDM12-R02: Cross-validation of CountryCode <-> Timezone (neutral UTC allowed) & CountryCode <-> LegalEntity CountryCode
            var usLeResp = await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/legal-entities", jwtAdmin, new
            {
                legalCode = "PG-LE-US",
                legalName = "Nexus US Holdings LLC",
                countryCode = "US",
                taxIdentifierType = TaxIdentifierType.EIN,
                taxRegistrationNumber = "12-3456789",
                registeredAddressLine1 = "100 Commerce Way",
                registeredCity = "Chicago",
                registeredStateOrProvince = "IL",
                registeredPostalCode = "60601",
                isCbmsMandated = false,
                defaultCurrencyCode = "USD"
            }));
            if (usLeResp.StatusCode != HttpStatusCode.Created)
                throw new InvalidOperationException($"MDM12-R02 PG US LegalEntity create failed: {usLeResp.StatusCode}");
            var usLe = (await usLeResp.Content.ReadFromJsonAsync<LegalEntityDto>(JsonOptions))!;

            // NP facility with US timezone -> 422; NP facility bound to US LegalEntity -> 422; LegalEntity country switch while bound -> 422; neutral UTC on NP -> 201
            if ((await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/facilities", jwtAdmin, new
                {
                    facilityCode = "PG-BAD-TZC", name = "Wrong TZ Country", countryCode = "NP", timezone = "America/Chicago"
                }))).StatusCode != HttpStatusCode.UnprocessableEntity ||
                (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/facilities", jwtAdmin, new
                {
                    facilityCode = "PG-BAD-LEC", name = "Wrong LE Country", countryCode = "NP", timezone = "Asia/Kathmandu", defaultLegalEntityId = usLe.Id
                }))).StatusCode != HttpStatusCode.UnprocessableEntity ||
                (await http.SendAsync(Authed(HttpMethod.Put, $"/api/v1/legal-entities/{npLe.Id}", jwtAdmin, new
                {
                    legalCode = npLe.LegalCode, legalName = npLe.LegalName, countryCode = "US",
                    taxIdentifierType = TaxIdentifierType.EIN, taxRegistrationNumber = "98-7654321",
                    registeredAddressLine1 = npLe.RegisteredAddressLine1, registeredCity = npLe.RegisteredCity,
                    registeredPostalCode = "60601", isCbmsMandated = true, defaultCurrencyCode = "USD"
                }))).StatusCode != HttpStatusCode.UnprocessableEntity)
            {
                throw new InvalidOperationException("MDM12-R02 PostgreSQL cross-validation check failed.");
            }

            if ((await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/facilities", jwtAdmin, new
                {
                    facilityCode = "PG-NP-UTC", name = "Nepal UTC Allowed Hub", countryCode = "NP", timezone = "UTC", defaultLegalEntityId = npLe.Id
                }))).StatusCode != HttpStatusCode.Created)
            {
                throw new InvalidOperationException("MDM12-R02 PostgreSQL country-neutral UTC timezone should be accepted.");
            }

            // 6. MDM12-R03: Two-context live PostgreSQL concurrency serialization between Facility binding and LegalEntity deactivation
            var raceLeAResp = await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/legal-entities", jwtAdmin, new
            {
                legalCode = "PG-RACE-A", legalName = "Race Entity A", countryCode = "NP",
                taxIdentifierType = TaxIdentifierType.PAN, taxRegistrationNumber = "601112223",
                registeredAddressLine1 = "Race Rd A", registeredCity = "Kathmandu", registeredPostalCode = "44600",
                isCbmsMandated = false, defaultCurrencyCode = "NPR"
            }));
            var raceLeBResp = await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/legal-entities", jwtAdmin, new
            {
                legalCode = "PG-RACE-B", legalName = "Race Entity B", countryCode = "NP",
                taxIdentifierType = TaxIdentifierType.PAN, taxRegistrationNumber = "601112224",
                registeredAddressLine1 = "Race Rd B", registeredCity = "Kathmandu", registeredPostalCode = "44600",
                isCbmsMandated = false, defaultCurrencyCode = "NPR"
            }));
            var raceLeA = (await raceLeAResp.Content.ReadFromJsonAsync<LegalEntityDto>(JsonOptions))!;
            var raceLeB = (await raceLeBResp.Content.ReadFromJsonAsync<LegalEntityDto>(JsonOptions))!;

            // 6a. Bind holds LegalEntity lock -> Deactivate blocks until commit -> Deactivate rejected 409
            var pauseBind = new PgSavePauseInterceptor();
            var bindCtxA = CreatePgDb(targetConn, pauseBind, adminContext);
            var deactCtxA = CreatePgDb(targetConn, null, adminContext);
            try
            {
                var bindTask = Task.Run(() => new CreateFacilityCommandHandler(
                    bindCtxA, new CompliancePlatformAuthorization(bindCtxA, adminContext), new TimeZoneOffsetResolver())
                    .Handle(new CreateFacilityCommand(
                        "PG-RACE-FAC-A", "Race Facility A", "Bay 1", null, "Birgunj", "Madhesh", "44300",
                        "NP", "Asia/Kathmandu", raceLeA.Id, "NPR", null, null, null, 2), CancellationToken.None));

                await pauseBind.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

                var deactTask = Task.Run(() => new UpdateLegalEntityStatusCommandHandler(
                    deactCtxA, new CompliancePlatformAuthorization(deactCtxA, adminContext))
                    .Handle(new UpdateLegalEntityStatusCommand(raceLeA.Id, false), CancellationToken.None));

                await Task.Delay(250);
                if (deactTask.IsCompleted)
                    throw new InvalidOperationException("MDM12-R03 PostgreSQL lock failed: concurrent deactivation did not wait for active binding transaction!");

                pauseBind.Release.TrySetResult(true);
                await bindTask;

                bool deactBlocked = false;
                try
                {
                    await deactTask;
                }
                catch (ConcurrencyException)
                {
                    deactBlocked = true;
                }

                if (!deactBlocked)
                    throw new InvalidOperationException("MDM12-R03 expected ConcurrencyException when deactivating newly bound LegalEntity.");
            }
            finally
            {
                await bindCtxA.DisposeAsync();
                await deactCtxA.DisposeAsync();
            }

            // 6b. Deactivate holds LegalEntity lock -> Bind blocks until commit -> Bind rejected with FACILITY_INACTIVE_LEGAL_ENTITY
            var pauseDeact = new PgSavePauseInterceptor();
            var deactCtxB = CreatePgDb(targetConn, pauseDeact, adminContext);
            var bindCtxB = CreatePgDb(targetConn, null, adminContext);
            try
            {
                var deactTask2 = Task.Run(() => new UpdateLegalEntityStatusCommandHandler(
                    deactCtxB, new CompliancePlatformAuthorization(deactCtxB, adminContext))
                    .Handle(new UpdateLegalEntityStatusCommand(raceLeB.Id, false), CancellationToken.None));

                await pauseDeact.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

                var bindTask2 = Task.Run(() => new CreateFacilityCommandHandler(
                    bindCtxB, new CompliancePlatformAuthorization(bindCtxB, adminContext), new TimeZoneOffsetResolver())
                    .Handle(new CreateFacilityCommand(
                        "PG-RACE-FAC-B", "Race Facility B", "Bay 2", null, "Birgunj", "Madhesh", "44300",
                        "NP", "Asia/Kathmandu", raceLeB.Id, "NPR", null, null, null, 2), CancellationToken.None));

                await Task.Delay(250);
                if (bindTask2.IsCompleted)
                    throw new InvalidOperationException("MDM12-R03 PostgreSQL lock failed: concurrent facility binding did not wait for deactivation transaction!");

                pauseDeact.Release.TrySetResult(true);
                await deactTask2;

                bool bindBlocked = false;
                try
                {
                    await bindTask2;
                }
                catch (DomainException ex) when (ex.Code == "FACILITY_INACTIVE_LEGAL_ENTITY")
                {
                    bindBlocked = true;
                }

                if (!bindBlocked)
                    throw new InvalidOperationException("MDM12-R03 expected FACILITY_INACTIVE_LEGAL_ENTITY when binding after concurrent deactivation.");
            }
            finally
            {
                await deactCtxB.DisposeAsync();
                await bindCtxB.DisposeAsync();
            }

            await using (var invDb = CreatePgDb(targetConn, null))
            {
                var leA = await invDb.LegalEntities.AsNoTracking().SingleAsync(l => l.Id == raceLeA.Id);
                var facACount = await invDb.Facilities.AsNoTracking().CountAsync(f => !f.IsDeleted && f.IsActive && f.DefaultLegalEntityId == raceLeA.Id);
                var leB = await invDb.LegalEntities.AsNoTracking().SingleAsync(l => l.Id == raceLeB.Id);
                var facBCount = await invDb.Facilities.AsNoTracking().CountAsync(f => !f.IsDeleted && f.DefaultLegalEntityId == raceLeB.Id);
                if (!leA.IsActive || facACount != 1 || leB.IsActive || facBCount != 0)
                    throw new InvalidOperationException("MDM12-R03 PostgreSQL invariant check failed after concurrent transactions.");
            }

            Console.WriteLine("PASS PostgreSQL MDM-1.2 Migration, LegalEntity Separation, RBAC R01, Cross-Validation R02, Concurrent Lock R03 & Physical Origin verified cleanly.");
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var dropConn = new NpgsqlConnection(adminBuilder.ConnectionString);
            await dropConn.OpenAsync();
            await using var dropCmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE)", dropConn);
            await dropCmd.ExecuteNonQueryAsync();
        }
    }

    private sealed class PgSavePauseInterceptor : SaveChangesInterceptor
    {
        private int _armed = 1;
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _armed, 0) == 1)
            {
                Entered.TrySetResult(true);
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class PgOriginSpyCarrier : IShippingCarrierService
    {
        public string CarrierIdentifier => "PG-SPY";
        public CarrierAddress? LastOrigin { get; private set; }
        public Task<IReadOnlyList<CarrierRateQuote>> GetRatesAsync(CarrierRateRequest request, CancellationToken ct = default)
        {
            LastOrigin = request.OriginAddress;
            return Task.FromResult<IReadOnlyList<CarrierRateQuote>>([
                new CarrierRateQuote { CarrierCode = "SIM", CarrierName = "Sim", ServiceLevel = CarrierServiceLevel.StandardGround, TotalAmount = 10m, Currency = "NPR", EstimatedDeliveryDays = 2 }
            ]);
        }
        public Task<CarrierShipmentResponse> CreateShipmentAsync(CarrierShipmentRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<CarrierVoidResponse> VoidShipmentAsync(CarrierVoidRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<CarrierTrackingResponse> TrackShipmentAsync(string trackingNumber, string carrierCode, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private static HttpRequestMessage Authed(HttpMethod method, string path, string jwt, object? body = null)
    {
        var req = new HttpRequestMessage(method, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        if (body != null) req.Content = JsonContent.Create(body);
        return req;
    }

    private static ApplicationDbContext CreatePgDb(string connStr, IInterceptor? interceptor, ICurrentUserContext? userOverride = null)
    {
        var user = userOverride ?? new PgSuperAdminUserContext(Guid.NewGuid());
        var audit = new AuditableEntityInterceptor(user, new DateTimeProvider());
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connStr, b => b.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(audit);
        if (interceptor != null) builder.AddInterceptors(interceptor);
        return new ApplicationDbContext(builder.Options, new PgAdminTenantProvider(), user, audit);
    }

    private static async Task<WebApplication> BuildPgHttpAppAsync(string connStr)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Host.UseDefaultServiceProvider(o => o.ValidateOnBuild = false);
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();

        builder.Services.Configure<JwtOptions>(o =>
        {
            o.SecretKey = JwtSecret;
            o.Issuer = JwtIssuer;
            o.Audience = JwtAudience;
            o.ExpiryMinutes = 60;
        });

        builder.Services.AddControllers()
            .AddApplicationPart(typeof(LegalEntitiesController).Assembly)
            .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
        builder.Services.AddApplicationServices();
        builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
        builder.Services.AddSingleton<ITokenService, JwtTokenService>();
        builder.Services.AddSingleton<ITimeZoneOffsetResolver, TimeZoneOffsetResolver>();
        builder.Services.AddScoped<ICountryComplianceEvaluator, CountryComplianceEvaluator>();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = JwtIssuer,
                    ValidAudience = JwtAudience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)),
                    ClockSkew = TimeSpan.Zero
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = JwtStaffFacilityValidator.ValidateAsync
                };
            });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
        builder.Services.AddScoped<ITenantProvider, TenantProvider>();
        builder.Services.AddScoped<IDateTimeProvider, DateTimeProvider>();
        builder.Services.AddScoped<AuditableEntityInterceptor>();
        builder.Services.AddDbContext<ApplicationDbContext>(opts =>
            opts.UseNpgsql(connStr).UseSnakeCaseNamingConvention());
        builder.Services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        var app = builder.Build();
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthenticatedTenantResolution();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }

    private sealed class PgAdminTenantProvider : ITenantProvider
    {
        public Guid? TenantId => null;
        public bool HasTenant => false;
        public bool BypassTenantFilter { get; set; } = true;
        public void SetTenantId(Guid tenantId) { }
    }

    private sealed class PgSuperAdminUserContext(Guid userId) : ICurrentUserContext
    {
        public Guid? UserId => userId;
        public string? Email => "pgadmin@m12.test";
        public Guid? TenantId => null;
        public Guid? FacilityId => null;
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
