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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Warehouse3PL.Application;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
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
using Xunit;

namespace Warehouse3PL.UnitTests.Reference;

public sealed class Mdm12LegalEntitiesAndFacilityBindingTests
{
    private const string TestSecret = "Nexus3PL-Mdm12-LegalEntity-JwtSecret-2026-Strong!";
    private const string TestIssuer = "nexus3pl-mdm12-auth";
    private const string TestAudience = "nexus3pl-mdm12-api";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    [Fact]
    public async Task HttpJwtEndpoints_EnforceLegalEntityRbac_TaxValidation_LifecycleRestrict_AndFacilityReferenceValidation()
    {
        var clientId = Guid.NewGuid();
        var legacyFacId = Guid.NewGuid();
        var clientUserId = Guid.NewGuid();
        var driverUserId = Guid.NewGuid();
        var plainStaffId = Guid.NewGuid();
        var viewStaffId = Guid.NewGuid();
        var facOnlyManageStaffId = Guid.NewGuid();
        var multiReadAssignedStaffId = Guid.NewGuid();
        var multiReadUnassignedStaffId = Guid.NewGuid();
        var globalAdminStaffId = Guid.NewGuid();

        await using var host = await BuildJwtAppAsync(async db =>
        {
            await ReferenceCatalogSeeder.SeedAsync(db);

            db.Clients.Add(new Client { Id = clientId, AccountCode = "MDM12-CL", CompanyName = "MDM12 Tenant", Status = ClientStatus.Active });
            // Pre-existing legacy facility with "USA" and null DefaultLegalEntityId must remain untouched!
            db.Facilities.Add(new Facility
            {
                Id = legacyFacId,
                FacilityCode = "FAC-LEG12",
                Name = "Legacy US Hub",
                AddressLine1 = "100 Legacy Way",
                City = "Chicago",
                StateOrProvince = "IL",
                PostalCode = "60601",
                CountryCode = "USA",
                Timezone = "America/Chicago",
                IsActive = true
            });
            db.ClientFacilityAccesses.Add(new ClientFacilityAccess
            {
                ClientId = clientId,
                FacilityId = legacyFacId,
                Status = FacilityAccessStatus.Active
            });

            var opRole = new Role { RoleName = SystemRoles.WarehouseOperator, Description = "Op", IsSystemRole = true };
            var leViewRole = new Role { RoleName = "LegalViewer", Description = "Viewer", IsSystemRole = false };
            leViewRole.RolePermissions.Add(new RolePermission { Role = leViewRole, PermissionKey = Permissions.Global.ViewLegalEntities, AccessLevel = AccessLevel.Read });
            leViewRole.RolePermissions.Add(new RolePermission { Role = leViewRole, PermissionKey = Permissions.Facilities.View, AccessLevel = AccessLevel.Read });

            var facMgrRole = new Role { RoleName = "SingleFacilityMgr", Description = "Single Fac Mgr", IsSystemRole = false };
            facMgrRole.RolePermissions.Add(new RolePermission { Role = facMgrRole, PermissionKey = Permissions.Facilities.View, AccessLevel = AccessLevel.Read });
            facMgrRole.RolePermissions.Add(new RolePermission { Role = facMgrRole, PermissionKey = Permissions.Facilities.Manage, AccessLevel = AccessLevel.Admin });

            var multiReadFacMgrRole = new Role { RoleName = "MultiReadSingleWriteMgr", Description = "Cross-facility read + assigned manage", IsSystemRole = false };
            multiReadFacMgrRole.RolePermissions.Add(new RolePermission { Role = multiReadFacMgrRole, PermissionKey = Permissions.Facilities.View, AccessLevel = AccessLevel.Read });
            multiReadFacMgrRole.RolePermissions.Add(new RolePermission { Role = multiReadFacMgrRole, PermissionKey = Permissions.Facilities.Manage, AccessLevel = AccessLevel.Admin });
            multiReadFacMgrRole.RolePermissions.Add(new RolePermission { Role = multiReadFacMgrRole, PermissionKey = Permissions.Analytics.MultiFacilityView, AccessLevel = AccessLevel.Read });

            var superAdminRole = new Role { RoleName = SystemRoles.SuperAdmin, Description = "SuperAdmin", IsSystemRole = true };
            foreach (var p in Permissions.GetAllPermissions())
            {
                superAdminRole.RolePermissions.Add(new RolePermission { Role = superAdminRole, PermissionKey = p, AccessLevel = AccessLevel.Admin });
            }

            db.Roles.AddRange(opRole, leViewRole, facMgrRole, multiReadFacMgrRole, superAdminRole);

            var uClient = new User { Id = clientUserId, Email = "c@mdm12.test", FirstName = "C", LastName = "U", UserType = UserType.Client, ClientId = clientId, Status = UserStatus.Active, PasswordHash = "x" };
            var uDriver = new User { Id = driverUserId, Email = "d@mdm12.test", FirstName = "D", LastName = "U", UserType = UserType.Driver, Status = UserStatus.Active, PasswordHash = "x" };
            var uPlain = new User { Id = plainStaffId, Email = "p@mdm12.test", FirstName = "P", LastName = "S", UserType = UserType.Staff, AssignedFacilityId = legacyFacId, Status = UserStatus.Active, PasswordHash = "x" };
            var uLeView = new User { Id = viewStaffId, Email = "v@mdm12.test", FirstName = "V", LastName = "S", UserType = UserType.Staff, AssignedFacilityId = legacyFacId, Status = UserStatus.Active, PasswordHash = "x" };
            var uFacMgr = new User { Id = facOnlyManageStaffId, Email = "fm@mdm12.test", FirstName = "F", LastName = "M", UserType = UserType.Staff, AssignedFacilityId = legacyFacId, Status = UserStatus.Active, PasswordHash = "x" };
            var uMultiReadAssigned = new User { Id = multiReadAssignedStaffId, Email = "mra@mdm12.test", FirstName = "M", LastName = "A", UserType = UserType.Staff, AssignedFacilityId = legacyFacId, Status = UserStatus.Active, PasswordHash = "x" };
            var uMultiReadUnassigned = new User { Id = multiReadUnassignedStaffId, Email = "mru@mdm12.test", FirstName = "M", LastName = "U", UserType = UserType.Staff, AssignedFacilityId = null, Status = UserStatus.Active, PasswordHash = "x" };
            var uAdmin = new User { Id = globalAdminStaffId, Email = "sa@mdm12.test", FirstName = "S", LastName = "A", UserType = UserType.Staff, Status = UserStatus.Active, PasswordHash = "x" };

            db.Users.AddRange(uClient, uDriver, uPlain, uLeView, uFacMgr, uMultiReadAssigned, uMultiReadUnassigned, uAdmin);
            db.UserRoles.AddRange(
                new UserRole { User = uPlain, Role = opRole },
                new UserRole { User = uLeView, Role = leViewRole },
                new UserRole { User = uFacMgr, Role = facMgrRole },
                new UserRole { User = uMultiReadAssigned, Role = multiReadFacMgrRole },
                new UserRole { User = uMultiReadUnassigned, Role = multiReadFacMgrRole },
                new UserRole { User = uAdmin, Role = superAdminRole });

            await db.SaveChangesAsync();
        });

        using var http = host.GetTestClient();
        string JwtFor(Guid id, UserType ut, Guid? cid, Guid? fid, string[] roles, string[] perms)
        {
            using var scope = host.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateTokens(new User
            {
                Id = id, Email = $"{id:N}@mdm12.test", FirstName = "T", LastName = "U", UserType = ut, ClientId = cid, AssignedFacilityId = fid
            }, roles, perms).AccessToken;
        }

        var jwtClientLegit = JwtFor(clientUserId, UserType.Client, clientId, null, [], [Permissions.Facilities.View]);
        var jwtClientSpoof = JwtFor(clientUserId, UserType.Client, clientId, null, [SystemRoles.SuperAdmin], [Permissions.Global.ManageLegalEntities]);
        var jwtDriverSpoof = JwtFor(driverUserId, UserType.Driver, null, null, [], [Permissions.Global.ManageLegalEntities, Permissions.Facilities.Manage]);
        var jwtPlainSpoof = JwtFor(plainStaffId, UserType.Staff, null, legacyFacId, [SystemRoles.WarehouseOperator], [Permissions.Global.ViewLegalEntities, Permissions.Global.ManageLegalEntities]);
        var jwtLeViewer = JwtFor(viewStaffId, UserType.Staff, null, legacyFacId, ["LegalViewer"], [Permissions.Global.ViewLegalEntities, Permissions.Facilities.View]);
        var jwtFacMgr = JwtFor(facOnlyManageStaffId, UserType.Staff, null, legacyFacId, ["SingleFacilityMgr"], [Permissions.Facilities.View, Permissions.Facilities.Manage]);
        var jwtMultiReadAssigned = JwtFor(multiReadAssignedStaffId, UserType.Staff, null, legacyFacId, ["MultiReadSingleWriteMgr"], [Permissions.Facilities.View, Permissions.Facilities.Manage, Permissions.Analytics.MultiFacilityView, "Analytics:CrossFacility"]);
        var jwtMultiReadUnassigned = JwtFor(multiReadUnassignedStaffId, UserType.Staff, null, null, ["MultiReadSingleWriteMgr"], [Permissions.Facilities.View, Permissions.Facilities.Manage, Permissions.Analytics.MultiFacilityView, "Analytics:CrossFacility"]);
        var jwtAdmin = JwtFor(globalAdminStaffId, UserType.Staff, null, null, [SystemRoles.SuperAdmin], Permissions.GetAllPermissions().ToArray());

        // 1. Auth checks: anonymous -> 401; spoofed Client/Driver/Staff -> 401/403
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/v1/legal-entities")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/legal-entities", jwtClientSpoof))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/legal-entities", jwtClientLegit))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/legal-entities", jwtDriverSpoof))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/legal-entities", jwtPlainSpoof))).StatusCode);

        // Single-facility Staff cannot create brand new global Facility (403);
        // MDM12-R01: analytics:multi-facility:view / Analytics:CrossFacility MUST NOT elevate write scope either!
        Assert.Equal(HttpStatusCode.Forbidden,
            (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/facilities", jwtFacMgr, new
            {
                facilityCode = "FAC-UNAUTH", name = "No", countryCode = "US", timezone = "America/Chicago"
            }))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/facilities", jwtMultiReadAssigned, new
            {
                facilityCode = "FAC-MRA-UNAUTH", name = "No Create", countryCode = "US", timezone = "America/Chicago"
            }))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/facilities", jwtMultiReadUnassigned, new
            {
                facilityCode = "FAC-MRU-UNAUTH", name = "No Unassigned Create", countryCode = "US", timezone = "America/Chicago"
            }))).StatusCode);

        // 2. LegalEntity validation errors (invalid NP PAN, invalid currency, unknown country, bad postal)
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/legal-entities", jwtAdmin, new
            {
                legalCode = "NP-BAD-PAN", legalName = "Nepal Bad Co", countryCode = "NP",
                taxIdentifierType = TaxIdentifierType.PAN, taxRegistrationNumber = "12345",
                registeredAddressLine1 = "Durbar Marg", registeredCity = "Kathmandu", registeredPostalCode = "44600",
                isCbmsMandated = true, defaultCurrencyCode = "NPR"
            }))).StatusCode);

        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/legal-entities", jwtAdmin, new
            {
                legalCode = "NP-BAD-CC", legalName = "Unknown Country Co", countryCode = "XX",
                taxIdentifierType = TaxIdentifierType.TIN, taxRegistrationNumber = "123456789",
                registeredAddressLine1 = "Main", registeredCity = "City",
                isCbmsMandated = false, defaultCurrencyCode = "USD"
            }))).StatusCode);

        // 3. Create valid Nepal LegalEntity (registered Kathmandu HQ) & US LegalEntity
        var createNpLe = await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/legal-entities", jwtAdmin, new
        {
            legalCode = "le-np-main",
            legalName = "Nexus 3PL Nepal Pvt Ltd",
            registrationNumber = "REG-NP-2082-001",
            countryCode = "NP",
            taxIdentifierType = TaxIdentifierType.PAN,
            taxRegistrationNumber = "601234567",
            registeredAddressLine1 = "Durbar Marg Corporate Tower",
            registeredCity = "Kathmandu",
            registeredStateOrProvince = "Bagmati",
            registeredPostalCode = "44600",
            isCbmsMandated = true,
            iroSoftwarePermitRef = "IRO-SW-2082-99",
            defaultCurrencyCode = "NPR"
        }));
        Assert.Equal(HttpStatusCode.Created, createNpLe.StatusCode);
        var npLe = (await createNpLe.Content.ReadFromJsonAsync<LegalEntityDto>(JsonOptions))!;
        Assert.Equal("LE-NP-MAIN", npLe.LegalCode);
        Assert.Equal("601234567", npLe.TaxRegistrationNumber);
        Assert.True(npLe.IsCbmsMandated);

        // Duplicate LegalCode -> 409 Conflict
        Assert.Equal(HttpStatusCode.Conflict,
            (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/legal-entities", jwtAdmin, new
            {
                legalCode = "LE-NP-MAIN", legalName = "Dup Co", countryCode = "NP",
                taxIdentifierType = TaxIdentifierType.PAN, taxRegistrationNumber = "609876543",
                registeredAddressLine1 = "Path", registeredCity = "Kathmandu",
                isCbmsMandated = false, defaultCurrencyCode = "NPR"
            }))).StatusCode);

        // Viewer can GET legal entity, but gets 403 on PUT/PATCH/DELETE
        Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(Authed(HttpMethod.Get, $"/api/v1/legal-entities/{npLe.Id}", jwtLeViewer))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Authed(HttpMethod.Patch, $"/api/v1/legal-entities/{npLe.Id}/status", jwtLeViewer, new { isActive = false }))).StatusCode);

        // 4. Create physical Nepal Facility in Birgunj (different ship-from address than Kathmandu HQ!)
        //    First verify invalid CountryCode ("USA" free text) & invalid Timezone ("Mars/Utc") fail closed!
        Assert.Equal(HttpStatusCode.BadRequest,
            (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/facilities", jwtAdmin, new
            {
                facilityCode = "FAC-BAD-CC", name = "Bad Country", countryCode = "USA", timezone = "America/Chicago"
            }))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/facilities", jwtAdmin, new
            {
                facilityCode = "FAC-BAD-TZ", name = "Bad TZ", countryCode = "NP", timezone = "Mars/Olympus"
            }))).StatusCode);

        var createBirgunjFac = await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/facilities", jwtAdmin, new
        {
            facilityCode = "fac-brg1",
            name = "Birgunj Dry Port ICD Warehouse",
            addressLine1 = "Sirsiya ICD Yard, Gate 4",
            city = "Birgunj",
            stateOrProvince = "Madhesh",
            postalCode = "44300",
            countryCode = "NP",
            timezone = "Asia/Kathmandu",
            defaultLegalEntityId = npLe.Id,
            defaultCurrencyCode = "NPR",
            dockDoorCount = 10
        }));
        Assert.Equal(HttpStatusCode.Created, createBirgunjFac.StatusCode);
        var brgFac = (await createBirgunjFac.Content.ReadFromJsonAsync<FacilityDto>(JsonOptions))!;
        Assert.Equal("FAC-BRG1", brgFac.FacilityCode);
        Assert.Equal(npLe.Id, brgFac.DefaultLegalEntityId);
        Assert.Equal("NPR", brgFac.DefaultCurrencyCode);

        // FacilityDto exposed to Clients reveals NO tax registration or CBMS secrets!
        var facListResp = await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/facilities", jwtClientLegit));
        var facListBody = await facListResp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("601234567", facListBody, StringComparison.Ordinal);
        Assert.DoesNotContain("iroSoftwarePermitRef", facListBody, StringComparison.OrdinalIgnoreCase);

        // 5. Attempting to deactivate or delete LegalEntity while bound to active Facility -> 409 Conflict!
        Assert.Equal(HttpStatusCode.Conflict,
            (await http.SendAsync(Authed(HttpMethod.Patch, $"/api/v1/legal-entities/{npLe.Id}/status", jwtAdmin, new { isActive = false }))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await http.SendAsync(Authed(HttpMethod.Delete, $"/api/v1/legal-entities/{npLe.Id}", jwtAdmin))).StatusCode);

        // Attempting to change immutable LegalCode -> 422
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await http.SendAsync(Authed(HttpMethod.Put, $"/api/v1/legal-entities/{npLe.Id}", jwtAdmin, new
            {
                legalCode = "RENAMED-CODE", legalName = npLe.LegalName, countryCode = "NP",
                taxIdentifierType = TaxIdentifierType.PAN, taxRegistrationNumber = "601234567",
                registeredAddressLine1 = npLe.RegisteredAddressLine1, registeredCity = npLe.RegisteredCity,
                isCbmsMandated = true, defaultCurrencyCode = "NPR"
            }))).StatusCode);

        // Legacy facility remained untouched with CountryCode="USA" and DefaultLegalEntityId=null
        var legacyRead = await (await http.SendAsync(Authed(HttpMethod.Get, $"/api/v1/facilities/{legacyFacId}", jwtAdmin)))
            .Content.ReadFromJsonAsync<FacilityDto>(JsonOptions);
        Assert.Equal("USA", legacyRead!.CountryCode);
        Assert.Null(legacyRead.DefaultLegalEntityId);

        // Single-facility Staff cannot update another facility (403); analytics:multi-facility:view grants read (200) NOT cross-facility write (403)!
        Assert.Equal(HttpStatusCode.Forbidden,
            (await http.SendAsync(Authed(HttpMethod.Put, $"/api/v1/facilities/{brgFac.Id}", jwtFacMgr, new
            {
                name = "Unauthorized Touch", countryCode = "NP", timezone = "Asia/Kathmandu", isActive = true
            }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await http.SendAsync(Authed(HttpMethod.Get, $"/api/v1/facilities/{brgFac.Id}", jwtMultiReadAssigned))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await http.SendAsync(Authed(HttpMethod.Put, $"/api/v1/facilities/{brgFac.Id}", jwtMultiReadAssigned, new
            {
                name = "Cross Fac Escalation Attempt", countryCode = "NP", timezone = "Asia/Kathmandu", isActive = true
            }))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await http.SendAsync(Authed(HttpMethod.Put, $"/api/v1/facilities/{brgFac.Id}", jwtMultiReadUnassigned, new
            {
                name = "Unassigned Staff Write Attempt", countryCode = "NP", timezone = "Asia/Kathmandu", isActive = true
            }))).StatusCode);

        // Assigned Staff WITH facilities:manage CAN update their own assigned facility cleanly
        Assert.Equal(HttpStatusCode.OK,
            (await http.SendAsync(Authed(HttpMethod.Put, $"/api/v1/facilities/{legacyFacId}", jwtFacMgr, new
            {
                name = "Legacy US Hub (Updated)", addressLine1 = "100 Legacy Way", city = "Chicago",
                stateOrProvince = "IL", postalCode = "60601", countryCode = "US", timezone = "America/Chicago",
                dockDoorCount = 4, isActive = true
            }))).StatusCode);

        // MDM12-R02: Cross-validation of Facility country <-> TimeZoneProfile.CountryCode (neutral UTC allowed) & LegalEntity.CountryCode
        var createUsLe = await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/legal-entities", jwtAdmin, new
        {
            legalCode = "LE-US-MAIN", legalName = "Nexus 3PL USA Inc", countryCode = "US",
            taxIdentifierType = TaxIdentifierType.EIN, taxRegistrationNumber = "12-3456789",
            registeredAddressLine1 = "200 Midwest Blvd", registeredCity = "Chicago",
            registeredStateOrProvince = "IL", registeredPostalCode = "60601",
            isCbmsMandated = false, defaultCurrencyCode = "USD"
        }));
        Assert.Equal(HttpStatusCode.Created, createUsLe.StatusCode);
        var usLe = (await createUsLe.Content.ReadFromJsonAsync<LegalEntityDto>(JsonOptions))!;

        // NP facility + US timezone -> 422
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/facilities", jwtAdmin, new
            {
                facilityCode = "FAC-NP-BADTZ", name = "Bad TZ Country", countryCode = "NP", timezone = "America/Chicago"
            }))).StatusCode);

        // NP facility + US LegalEntity -> 422
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/facilities", jwtAdmin, new
            {
                facilityCode = "FAC-NP-BADLE", name = "Bad LE Country", countryCode = "NP", timezone = "Asia/Kathmandu",
                defaultLegalEntityId = usLe.Id, defaultCurrencyCode = "NPR"
            }))).StatusCode);

        // Country-neutral UTC timezone allowed with NP facility + NP LegalEntity -> 201
        var createUtcFac = await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/facilities", jwtAdmin, new
        {
            facilityCode = "FAC-NP-UTC", name = "Nepal UTC Transit Hub", countryCode = "NP", timezone = "UTC",
            defaultLegalEntityId = npLe.Id, defaultCurrencyCode = "NPR"
        }));
        Assert.Equal(HttpStatusCode.Created, createUtcFac.StatusCode);
        var utcFac = (await createUtcFac.Content.ReadFromJsonAsync<FacilityDto>(JsonOptions))!;

        // PUT updating bound NP facility to mismatched timezone or mismatched US LegalEntity -> 422
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await http.SendAsync(Authed(HttpMethod.Put, $"/api/v1/facilities/{brgFac.Id}", jwtAdmin, new
            {
                name = brgFac.Name, countryCode = "NP", timezone = "America/Chicago",
                defaultLegalEntityId = npLe.Id, defaultCurrencyCode = "NPR", dockDoorCount = 10, isActive = true
            }))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await http.SendAsync(Authed(HttpMethod.Put, $"/api/v1/facilities/{brgFac.Id}", jwtAdmin, new
            {
                name = brgFac.Name, countryCode = "NP", timezone = "Asia/Kathmandu",
                defaultLegalEntityId = usLe.Id, defaultCurrencyCode = "NPR", dockDoorCount = 10, isActive = true
            }))).StatusCode);

        // PUT changing LegalEntity CountryCode while active NP facilities are bound -> 422
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await http.SendAsync(Authed(HttpMethod.Put, $"/api/v1/legal-entities/{npLe.Id}", jwtAdmin, new
            {
                legalCode = npLe.LegalCode, legalName = npLe.LegalName, countryCode = "US",
                taxIdentifierType = TaxIdentifierType.EIN, taxRegistrationNumber = "98-7654321",
                registeredAddressLine1 = "100 Main", registeredCity = "Chicago", registeredPostalCode = "60601",
                isCbmsMandated = true, defaultCurrencyCode = "USD"
            }))).StatusCode);

        // Unbind both NP facilities -> then LegalEntity can be deactivated cleanly, and cannot bind new facility to inactive LegalEntity!
        Assert.Equal(HttpStatusCode.OK,
            (await http.SendAsync(Authed(HttpMethod.Put, $"/api/v1/facilities/{utcFac.Id}", jwtAdmin, new
            {
                name = utcFac.Name, countryCode = "NP", timezone = "UTC",
                defaultLegalEntityId = (Guid?)null, defaultCurrencyCode = "NPR", dockDoorCount = 4, isActive = true
            }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await http.SendAsync(Authed(HttpMethod.Put, $"/api/v1/facilities/{brgFac.Id}", jwtAdmin, new
            {
                name = brgFac.Name,
                addressLine1 = brgFac.AddressLine1,
                city = brgFac.City,
                stateOrProvince = brgFac.StateOrProvince,
                postalCode = brgFac.PostalCode,
                countryCode = "NP",
                timezone = "Asia/Kathmandu",
                defaultLegalEntityId = (Guid?)null,
                defaultCurrencyCode = "NPR",
                dockDoorCount = 10,
                isActive = true
            }))).StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await http.SendAsync(Authed(HttpMethod.Patch, $"/api/v1/legal-entities/{npLe.Id}/status", jwtAdmin, new { isActive = false }))).StatusCode);

        // Binding facility to inactive LegalEntity fails closed 422!
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await http.SendAsync(Authed(HttpMethod.Put, $"/api/v1/facilities/{brgFac.Id}", jwtAdmin, new
            {
                name = brgFac.Name, countryCode = "NP", timezone = "Asia/Kathmandu",
                defaultLegalEntityId = npLe.Id, defaultCurrencyCode = "NPR", dockDoorCount = 10, isActive = true
            }))).StatusCode);
    }

    [Fact]
    public async Task ShippingLabelService_UsesPhysicalFacilityOrigin_EvenWhenBoundLegalEntityHeadquartersDiffers()
    {
        var dbName = $"mdm12-origin-{Guid.NewGuid():N}";
        var clientId = Guid.NewGuid();
        var facId = Guid.NewGuid();
        var leId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var huId = Guid.NewGuid();
        var pkgId = Guid.NewGuid();

        using (var seed = Warehouse3PL.UnitTests.Common.TestDbContextFactory.Create(
                   new Warehouse3PL.UnitTests.Common.TestTenantProvider { BypassTenantFilter = true },
                   new Warehouse3PL.UnitTests.Common.TestCurrentUserContext { UserType = UserType.Staff },
                   dbName))
        {
            await ReferenceCatalogSeeder.SeedAsync(seed);

            var le = LegalEntity.Create(
                "LE-NP-HQ", "Himalayan Logistics Holding Pvt Ltd", "REG-99", "NP",
                TaxIdentifierType.PAN, "601234567",
                "Durbar Marg Corporate HQ", null, "Kathmandu", "Bagmati", "44600",
                true, "IRO-1", "NPR", leId);
            seed.LegalEntities.Add(le);

            seed.Clients.Add(new Client { Id = clientId, AccountCode = "CL-ORGN", CompanyName = "Nepal Trade", Status = ClientStatus.Active });
            seed.Facilities.Add(new Facility
            {
                Id = facId,
                FacilityCode = "FAC-BRG-ICD",
                Name = "Birgunj Physical ICD Dock",
                AddressLine1 = "Sirsiya Dry Port Bay 7",
                City = "Birgunj",
                StateOrProvince = "Madhesh",
                PostalCode = "44300",
                CountryCode = "NP",
                Timezone = "Asia/Kathmandu",
                ContactPhone = "+977-51-5550100",
                DefaultLegalEntityId = leId,
                DefaultCurrencyCode = "NPR",
                IsActive = true
            });

            var order = new OutboundOrder
            {
                Id = orderId,
                ClientId = clientId,
                FacilityId = facId,
                OrderNumber = "SO-BRG-001",
                CustomerName = "Pokhara Wholesaler",
                Status = OutboundOrderStatus.InProgress,
                ShippingAddress = new ShippingAddress
                {
                    OutboundOrderId = orderId,
                    RecipientName = "Sita Gurung",
                    AddressLine1 = "Lakeside Road 12",
                    City = "Pokhara",
                    StateOrProvince = "Gandaki",
                    PostalCode = "33700",
                    CountryCode = "NP"
                }
            };
            seed.OutboundOrders.Add(order);

            seed.HandlingUnits.Add(new HandlingUnit
            {
                Id = huId, ClientId = clientId, FacilityId = facId, LpnCode = "HU-BRG-1",
                UnitType = HandlingUnitType.ShippingCarton, Status = HandlingUnitStatus.InUse, CurrentOutboundOrderId = orderId
            });

            seed.Packages.Add(new Package
            {
                Id = pkgId, ClientId = clientId, FacilityId = facId, OutboundOrderId = orderId,
                HandlingUnitId = huId, PackageNumber = "PKG-BRG-1",
                LengthCm = 25m, WidthCm = 20m, HeightCm = 15m, GrossWeightKg = 3m, TareWeightKg = 0.2m, NetWeightKg = 2.8m,
                Status = PackageStatus.Packed
            });

            await seed.SaveChangesAsync();
        }

        var spy = new OriginCapturingCarrier();
        using var db = Warehouse3PL.UnitTests.Common.TestDbContextFactory.Create(
            new Warehouse3PL.UnitTests.Common.TestTenantProvider { BypassTenantFilter = true },
            new Warehouse3PL.UnitTests.Common.TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        var svc = new ShippingLabelService(db, spy, NullLogger<ShippingLabelService>.Instance);
        await svc.GetRatesForPackageAsync(new GetRatesForPackageRequest { PackageId = pkgId });

        Assert.NotNull(spy.LastRateOrigin);
        Assert.Equal("Birgunj Physical ICD Dock", spy.LastRateOrigin!.Name);
        Assert.Equal("Sirsiya Dry Port Bay 7", spy.LastRateOrigin.StreetLine1);
        Assert.Equal("Birgunj", spy.LastRateOrigin.City);
        Assert.Equal("44300", spy.LastRateOrigin.PostalCode);
        Assert.DoesNotContain("Kathmandu", spy.LastRateOrigin.City, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Durbar Marg", spy.LastRateOrigin.StreetLine1, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class OriginCapturingCarrier : IShippingCarrierService
    {
        public string CarrierIdentifier => "SPY";
        public CarrierAddress? LastRateOrigin { get; private set; }

        public Task<IReadOnlyList<CarrierRateQuote>> GetRatesAsync(CarrierRateRequest request, CancellationToken ct = default)
        {
            LastRateOrigin = request.OriginAddress;
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

    private static async Task<WebApplication> BuildJwtAppAsync(Func<ApplicationDbContext, Task> seedAsync)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Host.UseDefaultServiceProvider(o => o.ValidateOnBuild = false);
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();

        builder.Services.Configure<JwtOptions>(o =>
        {
            o.SecretKey = TestSecret;
            o.Issuer = TestIssuer;
            o.Audience = TestAudience;
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
                    ValidIssuer = TestIssuer,
                    ValidAudience = TestAudience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret)),
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

        var dbName = $"mdm12-http-{Guid.NewGuid():N}";
        builder.Services.AddDbContext<ApplicationDbContext>((sp, opts) =>
            opts.UseInMemoryDatabase(dbName).AddInterceptors(sp.GetRequiredService<AuditableEntityInterceptor>()));
        builder.Services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        var app = builder.Build();
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthenticatedTenantResolution();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await seedAsync(db);
        return app;
    }
}
