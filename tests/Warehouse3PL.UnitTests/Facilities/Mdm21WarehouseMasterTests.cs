using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Warehouse3PL.Application;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.Facilities.Commands;
using Warehouse3PL.Application.Features.Inbound.DockManagement.DockDoors.Commands.CreateDockDoor;
using Warehouse3PL.Application.Features.Inbound.DockManagement.DockDoors.Commands.UpdateDockDoorStatus;
using Warehouse3PL.Application.Features.Locations.Commands.BatchGenerateLocations;
using Warehouse3PL.Application.Features.Locations.Commands.CreateLocation;
using Warehouse3PL.Application.Features.Locations.Commands.LockLocation;
using Warehouse3PL.Application.Features.Putaway;
using Warehouse3PL.Application.Features.WarehouseMaster;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Dock;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Reference;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Authentication;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Seed;
using Warehouse3PL.Infrastructure.Services;
using Warehouse3PL.Infrastructure.Services.Reference;
using Warehouse3PL.UnitTests.Common;
using Warehouse3PL.WebApi.Controllers;
using Warehouse3PL.WebApi.Middleware;
using Xunit;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.UnitTests.Facilities;

public sealed class Mdm21WarehouseMasterTests
{
    private const string JwtSecret = "Nexus3PL-Mdm21-Unit-Security-Verification-Key-2026!";
    private const string JwtIssuer = "nexus3pl-mdm21-test";
    private const string JwtAudience = "nexus3pl-mdm21-api";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private static async Task<(ApplicationDbContext Db, TestCurrentUserContext Admin, Guid FacilityA, Guid FacilityB, Guid ClientA, Guid ClientB)>
        CreateSeededContextAsync()
    {
        var dbName = $"mdm21_{Guid.NewGuid():N}";
        var facA = Guid.NewGuid();
        var facB = Guid.NewGuid();
        var clientA = Guid.NewGuid();
        var clientB = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var tenant = new TestTenantProvider { BypassTenantFilter = true };
        var admin = new TestCurrentUserContext
        {
            UserId = userId,
            UserType = UserType.Staff,
            FacilityId = facA,
            RolesList = [SystemRoles.SuperAdmin],
            PermissionsList = Permissions.GetAllPermissions().ToList()
        };

        var db = TestDbContextFactory.Create(tenant, admin, dbName);
        await ReferenceCatalogSeeder.SeedAsync(db);

        db.Facilities.Add(new Facility { Id = facA, FacilityCode = "FAC-ORD1", Name = "Chicago Hub", CountryCode = "US", Timezone = "America/Chicago", DefaultCurrencyCode = "USD", IsActive = true });
        db.Facilities.Add(new Facility { Id = facB, FacilityCode = "FAC-KTM1", Name = "Kathmandu Hub", CountryCode = "NP", Timezone = "Asia/Kathmandu", DefaultCurrencyCode = "NPR", IsActive = true });

        db.Clients.Add(new Client { Id = clientA, CompanyName = "Client Alpha", AccountCode = "CL-A", Status = ClientStatus.Active });
        db.Clients.Add(new Client { Id = clientB, CompanyName = "Client Beta", AccountCode = "CL-B", Status = ClientStatus.Active });
        db.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = clientA, FacilityId = facA, Status = FacilityAccessStatus.Active });

        var role = new Role { RoleName = SystemRoles.SuperAdmin };
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        db.Users.Add(new User
        {
            Id = userId,
            Email = "superadmin@mdm21.test",
            PasswordHash = "hash",
            FirstName = "Super",
            LastName = "Admin",
            UserType = UserType.Staff,
            Status = UserStatus.Active,
            AssignedFacilityId = facA
        });
        db.UserRoles.Add(new UserRole { UserId = userId, RoleId = role.Id });
        await db.SaveChangesAsync();

        return (db, admin, facA, facB, clientA, clientB);
    }

    [Fact]
    public async Task A01_LocationLifecycleStatus_IsOrthogonalToLocksAndDamage()
    {
        var (db, admin, facA, _, _, _) = await CreateSeededContextAsync();
        await using var _ = db;
        var auth = new WarehouseMasterAuthorization(db, admin);

        var createHandler = new CreateLocationCommandHandler(db, auth);
        var bin = await createHandler.Handle(new CreateLocationCommand
        {
            FacilityId = facA,
            LocationType = LocationType.Bin,
            LocationCode = "BIN-A01",
            Barcode = "BC-BIN-A01"
        }, CancellationToken.None);

        var lockHandler = new LockLocationCommandHandler(db, auth);
        var lock1 = await lockHandler.Handle(new LockLocationCommand
        {
            LocationId = bin.Id,
            LockType = LocationLockType.CycleCount,
            ReasonCode = "CC_01"
        }, CancellationToken.None);
        var lock2 = await lockHandler.Handle(new LockLocationCommand
        {
            LocationId = bin.Id,
            LockType = LocationLockType.Quarantine,
            ReasonCode = "QA_HOLD"
        }, CancellationToken.None);

        var updateHandler = new UpdateLocationCommandHandler(db, auth);
        await updateHandler.Handle(new UpdateLocationCommand { Id = bin.Id, IsDamaged = true }, CancellationToken.None);

        // Deactivate then reactivate -> locks & damage MUST remain intact (A01)
        var statusHandler = new UpdateLocationStatusCommandHandler(db, auth);
        await statusHandler.Handle(new UpdateLocationStatusCommand(bin.Id, LocationStatus.Inactive), CancellationToken.None);
        var reactivated = await statusHandler.Handle(new UpdateLocationStatusCommand(bin.Id, LocationStatus.Active), CancellationToken.None);

        Assert.Equal(LocationStatus.Active, reactivated.Status);
        Assert.True(reactivated.IsLocked);
        Assert.True(reactivated.IsDamaged);

        // Release lock1 -> IsLocked must stay true because lock2 is still active
        var releaseHandler = new ReleaseLocationLockCommandHandler(db, auth);
        await releaseHandler.Handle(new ReleaseLocationLockCommand(bin.Id, lock1), CancellationToken.None);
        var afterFirstRelease = await db.Locations.AsNoTracking().FirstAsync(l => l.Id == bin.Id);
        Assert.True(afterFirstRelease.IsLocked);

        // Release lock2 -> IsLocked becomes false, IsDamaged remains true
        await releaseHandler.Handle(new ReleaseLocationLockCommand(bin.Id, lock2), CancellationToken.None);
        var afterSecondRelease = await db.Locations.AsNoTracking().FirstAsync(l => l.Id == bin.Id);
        Assert.False(afterSecondRelease.IsLocked);
        Assert.True(afterSecondRelease.IsDamaged);
    }

    [Fact]
    public async Task A02_A04_MoveLocation_PreventsCycles_RecalculatesSubtree_AndEnforcesFacilityRulePrecedence()
    {
        var (db, admin, facOrd, facKtm, _, _) = await CreateSeededContextAsync();
        await using var _ = db;
        var auth = new WarehouseMasterAuthorization(db, admin);
        var createLoc = new CreateLocationCommandHandler(db, auth);
        var moveLoc = new MoveLocationCommandHandler(db, auth);
        var upsertRule = new UpsertLocationHierarchyRuleCommandHandler(db, auth);

        // Compact Kathmandu layout: root Bin directly under Facility (level 1)
        var ktmBin = await createLoc.Handle(new CreateLocationCommand
        {
            FacilityId = facKtm,
            LocationType = LocationType.Bin,
            LocationCode = "KTM-B01",
            Barcode = "BC-KTM-B01"
        }, CancellationToken.None);
        Assert.Equal(1, ktmBin.HierarchyLevel);
        Assert.Equal($"/{ktmBin.Id:N}/", ktmBin.HierarchyPath);

        // Deep Chicago layout: Aisle1 -> Rack1 -> Shelf1 -> Bin1, plus Aisle2
        var aisle1 = await createLoc.Handle(new CreateLocationCommand { FacilityId = facOrd, LocationType = LocationType.Aisle, LocationCode = "A01", Barcode = "BC-A01" }, CancellationToken.None);
        var aisle2 = await createLoc.Handle(new CreateLocationCommand { FacilityId = facOrd, LocationType = LocationType.Aisle, LocationCode = "A02", Barcode = "BC-A02" }, CancellationToken.None);
        var rack1 = await createLoc.Handle(new CreateLocationCommand { FacilityId = facOrd, ParentLocationId = aisle1.Id, LocationType = LocationType.Rack, LocationCode = "A01-R01", Barcode = "BC-R01" }, CancellationToken.None);
        var shelf1 = await createLoc.Handle(new CreateLocationCommand { FacilityId = facOrd, ParentLocationId = rack1.Id, LocationType = LocationType.ShelfLevel, LocationCode = "A01-R01-S01", Barcode = "BC-S01" }, CancellationToken.None);
        var bin1 = await createLoc.Handle(new CreateLocationCommand { FacilityId = facOrd, ParentLocationId = shelf1.Id, LocationType = LocationType.Bin, LocationCode = "A01-R01-S01-B01", Barcode = "BC-B01" }, CancellationToken.None);

        Assert.Equal(4, bin1.HierarchyLevel);
        Assert.Equal($"/{aisle1.Id:N}/{rack1.Id:N}/{shelf1.Id:N}/{bin1.Id:N}/", bin1.HierarchyPath);

        // Self-cycle & indirect cycle rejected
        await Assert.ThrowsAsync<DomainException>(() => moveLoc.Handle(new MoveLocationCommand(aisle1.Id, aisle1.Id), CancellationToken.None));
        await Assert.ThrowsAsync<DomainException>(() => moveLoc.Handle(new MoveLocationCommand(aisle1.Id, bin1.Id), CancellationToken.None));

        // Cross-facility parent rejected
        await Assert.ThrowsAsync<ValidationException>(() => moveLoc.Handle(new MoveLocationCommand(rack1.Id, ktmBin.Id), CancellationToken.None));

        // Move Rack1 under Aisle2 -> Rack1, Shelf1, Bin1 paths all updated atomically
        var movedRack = await moveLoc.Handle(new MoveLocationCommand(rack1.Id, aisle2.Id), CancellationToken.None);
        Assert.Equal($"/{aisle2.Id:N}/{rack1.Id:N}/", movedRack.HierarchyPath);
        var movedBin = await db.Locations.AsNoTracking().FirstAsync(l => l.Id == bin1.Id);
        Assert.Equal($"/{aisle2.Id:N}/{rack1.Id:N}/{shelf1.Id:N}/{bin1.Id:N}/", movedBin.HierarchyPath);
        Assert.Equal(4, movedBin.HierarchyLevel);

        // Facility override disabling ROOT -> Bin in Chicago blocks Chicago root Bin, while Kathmandu still succeeds
        await upsertRule.Handle(new UpsertLocationHierarchyRuleCommand
        {
            FacilityId = facOrd,
            ParentLocationType = null,
            ChildLocationType = LocationType.Bin,
            MaxDepth = 6,
            IsStorableLeaf = true,
            IsActive = false
        }, CancellationToken.None);

        await Assert.ThrowsAsync<ValidationException>(() => createLoc.Handle(new CreateLocationCommand
        {
            FacilityId = facOrd,
            LocationType = LocationType.Bin,
            LocationCode = "ORD-ROOT-BIN",
            Barcode = "BC-ORD-ROOT-BIN"
        }, CancellationToken.None));

        var ktmBin2 = await createLoc.Handle(new CreateLocationCommand
        {
            FacilityId = facKtm,
            LocationType = LocationType.Bin,
            LocationCode = "KTM-B02",
            Barcode = "BC-KTM-B02"
        }, CancellationToken.None);
        Assert.Equal(1, ktmBin2.HierarchyLevel);
    }

    [Fact]
    public async Task A03_PermanentNormalizedUniqueness_BlocksReuseAfterSoftDeleteAndEnforcesImmutability()
    {
        var (db, admin, facA, _, _, _) = await CreateSeededContextAsync();
        await using var _ = db;
        var auth = new WarehouseMasterAuthorization(db, admin);
        var createLoc = new CreateLocationCommandHandler(db, auth);
        var updateLoc = new UpdateLocationCommandHandler(db, auth);

        var loc = await createLoc.Handle(new CreateLocationCommand
        {
            FacilityId = facA,
            LocationType = LocationType.Bin,
            LocationCode = "BIN-PERM-01",
            Barcode = "BC-PERM-01"
        }, CancellationToken.None);

        // Code immutability
        await Assert.ThrowsAsync<DomainException>(() => updateLoc.Handle(new UpdateLocationCommand
        {
            Id = loc.Id,
            LocationCode = "BIN-RENAMED"
        }, CancellationToken.None));

        // Soft-delete location
        var entity = await db.Locations.FirstAsync(l => l.Id == loc.Id);
        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Attempting to reuse normalized code (case/whitespace variant) or barcode MUST fail (A03)
        await Assert.ThrowsAsync<ValidationException>(() => createLoc.Handle(new CreateLocationCommand
        {
            FacilityId = facA,
            LocationType = LocationType.Bin,
            LocationCode = "  bin-perm-01  ",
            Barcode = "BC-NEW-99"
        }, CancellationToken.None));

        await Assert.ThrowsAsync<ValidationException>(() => createLoc.Handle(new CreateLocationCommand
        {
            FacilityId = facA,
            LocationType = LocationType.Bin,
            LocationCode = "BIN-NEW-99",
            Barcode = "  bc-perm-01 "
        }, CancellationToken.None));
    }

    [Fact]
    public async Task A05_A06_StorageProfiles_DedicatedClients_Packaging_DockConversion_AndPutawayEnforcement()
    {
        var (db, admin, facA, _, clientA, clientB) = await CreateSeededContextAsync();
        await using var _ = db;
        var auth = new WarehouseMasterAuthorization(db, admin);

        // Client B does NOT have ClientFacilityAccess to facA -> dedicated zone & packaging rejected (A05)
        var createZone = new CreateWarehouseZoneCommandHandler(db, auth);
        await Assert.ThrowsAsync<ValidationException>(() => createZone.Handle(new CreateWarehouseZoneCommand
        {
            FacilityId = facA,
            ZoneCode = "ZN-CLB",
            Name = "Unauthorized Client Zone",
            DedicatedClientId = clientB
        }, CancellationToken.None));

        var zoneA = await createZone.Handle(new CreateWarehouseZoneCommand
        {
            FacilityId = facA,
            ZoneCode = "ZN-AMB",
            Name = "Ambient Storage",
            ZoneType = WarehouseZoneType.BulkStorage,
            TempMinC = 15,
            TempMaxC = 25
        }, CancellationToken.None);

        // Packaging material: facility-wide vs client-specific code isolation (A05)
        var createPkg = new CreatePackagingMaterialCommandHandler(db, auth);
        var facWideBox = await createPkg.Handle(new CreatePackagingMaterialCommand
        {
            FacilityId = facA,
            ClientId = null,
            Code = "BOX-STD",
            Name = "Standard Facility Box",
            LengthCm = 40,
            WidthCm = 30,
            HeightCm = 20,
            MaxWeightKg = 25,
            TareWeightKg = 0.5m,
            CostPerUnit = 1.50m,
            CurrencyCode = "USD"
        }, CancellationToken.None);
        Assert.Equal("USD", facWideBox.CurrencyCode);

        // Same code for ClientA is allowed (distinct scope), duplicate facility-wide is rejected
        var clientBox = await createPkg.Handle(new CreatePackagingMaterialCommand
        {
            FacilityId = facA,
            ClientId = clientA,
            Code = "box-std",
            Name = "Client A Branded Box",
            LengthCm = 40,
            WidthCm = 30,
            HeightCm = 20,
            MaxWeightKg = 25,
            TareWeightKg = 0.5m,
            CurrencyCode = "USD"
        }, CancellationToken.None);
        Assert.Equal(clientA, clientBox.ClientId);

        await Assert.ThrowsAsync<ValidationException>(() => createPkg.Handle(new CreatePackagingMaterialCommand
        {
            FacilityId = facA,
            ClientId = null,
            Code = "  BOX-STD ",
            Name = "Duplicate Facility Box",
            LengthCm = 40,
            WidthCm = 30,
            HeightCm = 20,
            MaxWeightKg = 25,
            TareWeightKg = 0.5m
        }, CancellationToken.None));

        // Dock door canonical metric conversion (cm <-> ft)
        var createDock = new CreateDockDoorCommandHandler(db, auth);
        var dock = await createDock.Handle(new CreateDockDoorCommand
        {
            FacilityId = facA,
            DoorNumber = "D-101",
            MaxTrailerLengthFeet = 53
        }, CancellationToken.None);
        Assert.Equal(1615.44m, dock.MaxTrailerLengthCm);
        Assert.Equal(53, dock.MaxTrailerLengthFeet);

        // A06 Putaway Enforcement: IsStorable, IsPutawayAllowed, AllowMixedSku, DedicatedClientId
        var createLoc = new CreateLocationCommandHandler(db, auth);
        var upsertProfile = new UpsertLocationStorageProfileCommandHandler(db, auth);

        var nonStorable = await createLoc.Handle(new CreateLocationCommand
        {
            FacilityId = facA,
            ZoneId = zoneA.Id,
            LocationType = LocationType.Bin,
            LocationCode = "BIN-NONSTOR",
            Barcode = "BC-NONSTOR",
            IsStorable = false
        }, CancellationToken.None);

        var noPutaway = await createLoc.Handle(new CreateLocationCommand
        {
            FacilityId = facA,
            ZoneId = zoneA.Id,
            LocationType = LocationType.Bin,
            LocationCode = "BIN-NOPUT",
            Barcode = "BC-NOPUT"
        }, CancellationToken.None);
        await upsertProfile.Handle(new UpsertLocationStorageProfileCommand { LocationId = noPutaway.Id, IsPutawayAllowed = false }, CancellationToken.None);

        var singleSkuBin = await createLoc.Handle(new CreateLocationCommand
        {
            FacilityId = facA,
            ZoneId = zoneA.Id,
            LocationType = LocationType.Bin,
            LocationCode = "BIN-SINGLESKU",
            Barcode = "BC-SINGLESKU"
        }, CancellationToken.None);
        await upsertProfile.Handle(new UpsertLocationStorageProfileCommand { LocationId = singleSkuBin.Id, AllowMixedSku = false }, CancellationToken.None);

        var goodBin = await createLoc.Handle(new CreateLocationCommand
        {
            FacilityId = facA,
            ZoneId = zoneA.Id,
            LocationType = LocationType.Bin,
            LocationCode = "BIN-GOOD",
            Barcode = "BC-GOOD"
        }, CancellationToken.None);

        var item1 = new Item { ClientId = clientA, Sku = "SKU-1", Description = "Item 1", WeightKg = 1m, LengthCm = 10, WidthCm = 10, HeightCm = 10, StorageTempClass = StorageTempClass.Ambient };
        var item2 = new Item { ClientId = clientA, Sku = "SKU-2", Description = "Item 2", WeightKg = 1m, LengthCm = 10, WidthCm = 10, HeightCm = 10, StorageTempClass = StorageTempClass.Ambient };
        db.Items.AddRange(item1, item2);
        db.StockOnHand.Add(new StockOnHand { ClientId = clientA, FacilityId = facA, ItemId = item1.Id, LocationId = singleSkuBin.Id, QtyAvailable = 5m });
        await db.SaveChangesAsync();

        var selector = new PutawayLocationSelector(db, NullLogger<PutawayLocationSelector>.Instance);
        var recs = await selector.EvaluateCandidateLocationsAsync(clientA, facA, item2.Id, 2m);
        Assert.Single(recs);
        Assert.Equal(goodBin.Id, recs[0].LocationId);

        // Safety validator also rejects direct confirmation into non-storable / noPutaway / singleSkuBin
        var nonStorableEntity = await db.Locations.Include(l => l.Zone).FirstAsync(l => l.Id == nonStorable.Id);
        var noPutawayEntity = await db.Locations.Include(l => l.Zone).Include(l => l.StorageProfile).FirstAsync(l => l.Id == noPutaway.Id);
        var singleSkuEntity = await db.Locations.Include(l => l.Zone).Include(l => l.StorageProfile).FirstAsync(l => l.Id == singleSkuBin.Id);

        await Assert.ThrowsAsync<ValidationException>(() => PutawaySafetyValidator.ValidateDestinationSafetyAndCapacityAsync(db, nonStorableEntity, item2, 1m, CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => PutawaySafetyValidator.ValidateDestinationSafetyAndCapacityAsync(db, noPutawayEntity, item2, 1m, CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => PutawaySafetyValidator.ValidateDestinationSafetyAndCapacityAsync(db, singleSkuEntity, item2, 1m, CancellationToken.None));

        // A07: Deactivating location with active stock is blocked
        var statusHandler = new UpdateLocationStatusCommandHandler(db, auth);
        await Assert.ThrowsAsync<DomainException>(() => statusHandler.Handle(new UpdateLocationStatusCommand(singleSkuBin.Id, LocationStatus.Inactive), CancellationToken.None));

        // Facility deactivation with active stock is blocked
        var updateFacility = new UpdateFacilityCommandHandler(db, new CompliancePlatformAuthorization(db, admin), new TimeZoneOffsetResolver());
        await Assert.ThrowsAsync<DomainException>(() => updateFacility.Handle(new UpdateFacilityCommand(
            facA, "FAC-ORD1", "Chicago Hub", null, null, null, null, null, "US", "America/Chicago", null, "USD", null, null, null, 10, false), CancellationToken.None));
    }

    [Fact]
    public async Task RealHttpSecurity_EnforcesRbac_FacilityIsolation_HeaderConflict_AndForgedClaimRejection()
    {
        var dbName = $"mdm21_http_{Guid.NewGuid():N}";
        var facA = Guid.NewGuid();
        var facB = Guid.NewGuid();
        var superAdminId = Guid.NewGuid();
        var facMgrId = Guid.NewGuid();
        var noPermStaffId = Guid.NewGuid();
        var clientUserId = Guid.NewGuid();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ITenantProvider, TestTenantProvider>();
        builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
        builder.Services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        builder.Services.AddScoped<Warehouse3PL.Infrastructure.Persistence.Interceptors.AuditableEntityInterceptor>();
        builder.Services.AddDbContext<ApplicationDbContext>(opt => opt.UseInMemoryDatabase(dbName));
        builder.Services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        builder.Services.AddApplicationServices();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = JwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = JwtAudience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)),
                    ValidateLifetime = true
                };
            });
        builder.Services.AddAuthorization();
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(WarehouseZonesController).Assembly)
            .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

        await using var app = builder.Build();
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();

        // Seed facilities & DB users/roles
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Facilities.AddRange(
                new Facility { Id = facA, FacilityCode = "FAC-A", Name = "Facility A", CountryCode = "US", Timezone = "America/Chicago" },
                new Facility { Id = facB, FacilityCode = "FAC-B", Name = "Facility B", CountryCode = "NP", Timezone = "Asia/Kathmandu" });

            var superRole = new Role { RoleName = SystemRoles.SuperAdmin };
            var mgrRole = new Role { RoleName = "FacilityManager" };
            var viewerRole = new Role { RoleName = "BasicStaff" };
            db.Roles.AddRange(superRole, mgrRole, viewerRole);
            await db.SaveChangesAsync();
            db.RolePermissions.AddRange(
                new RolePermission { RoleId = mgrRole.Id, PermissionKey = Permissions.Facilities.ZonesManage },
                new RolePermission { RoleId = mgrRole.Id, PermissionKey = Permissions.Facilities.ZonesView });

            db.Users.AddRange(
                new User { Id = superAdminId, Email = "sa@test", PasswordHash = "x", FirstName = "SA", LastName = "1", UserType = UserType.Staff, Status = UserStatus.Active, AssignedFacilityId = facA },
                new User { Id = facMgrId, Email = "mgr@test", PasswordHash = "x", FirstName = "Mgr", LastName = "A", UserType = UserType.Staff, Status = UserStatus.Active, AssignedFacilityId = facA },
                new User { Id = noPermStaffId, Email = "noperm@test", PasswordHash = "x", FirstName = "No", LastName = "Perm", UserType = UserType.Staff, Status = UserStatus.Active, AssignedFacilityId = facA },
                new User { Id = clientUserId, Email = "client@test", PasswordHash = "x", FirstName = "Cli", LastName = "1", UserType = UserType.Client, Status = UserStatus.Active });

            db.UserRoles.AddRange(
                new UserRole { UserId = superAdminId, RoleId = superRole.Id },
                new UserRole { UserId = facMgrId, RoleId = mgrRole.Id },
                new UserRole { UserId = noPermStaffId, RoleId = viewerRole.Id });
            await db.SaveChangesAsync();
        }

        var client = app.GetTestClient();

        // 1. Anonymous -> 401
        var anonResp = await client.GetAsync($"/api/v1/warehouse-zones?facilityId={facA}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonResp.StatusCode);

        // 2. Client user -> 403
        using (var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/warehouse-zones?facilityId={facA}"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", IssueJwt(clientUserId, UserType.Client, null, [], [Permissions.Facilities.ZonesView]));
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        }

        // 3. Forged SuperAdmin claim (DB user only has BasicStaff) -> 403
        using (var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/warehouse-zones?facilityId={facA}"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", IssueJwt(noPermStaffId, UserType.Staff, facA, [SystemRoles.SuperAdmin], [Permissions.Facilities.ZonesView]));
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        }

        // 4. Cross-facility Staff (assigned facA, requesting facB) -> 403
        using (var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/warehouse-zones?facilityId={facB}"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", IssueJwt(facMgrId, UserType.Staff, facA, ["FacilityManager"], [Permissions.Facilities.ZonesView, Permissions.Facilities.ZonesManage]));
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        }

        // 5. Conflicting X-Facility-Id header vs query FacilityId -> 400
        using (var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/warehouse-zones?facilityId={facA}"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", IssueJwt(superAdminId, UserType.Staff, facA, [SystemRoles.SuperAdmin], Permissions.GetAllPermissions()));
            req.Headers.Add("X-Facility-Id", facB.ToString());
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        // 6. Authorized FacilityManager creating zone in assigned facA -> 201
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/warehouse-zones"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", IssueJwt(facMgrId, UserType.Staff, facA, ["FacilityManager"], [Permissions.Facilities.ZonesView, Permissions.Facilities.ZonesManage]));
            req.Content = JsonContent.Create(new { facilityId = facA, zoneCode = "ZN-HTTP1", name = "HTTP Zone 1", zoneType = "BulkStorage" });
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        }

        // 7. Global hierarchy rule mutation by non-SuperAdmin -> 403; by DB-verified SuperAdmin -> 200
        using (var req = new HttpRequestMessage(HttpMethod.Put, "/api/v1/location-hierarchy-rules"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", IssueJwt(facMgrId, UserType.Staff, facA, ["FacilityManager"], [Permissions.Facilities.LocationsManage, Permissions.Facilities.Manage]));
            req.Content = JsonContent.Create(new { facilityId = (Guid?)null, parentLocationType = "Aisle", childLocationType = "Bin", maxDepth = 6, isStorableLeaf = true, isActive = true });
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        }

        using (var req = new HttpRequestMessage(HttpMethod.Put, "/api/v1/location-hierarchy-rules"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", IssueJwt(superAdminId, UserType.Staff, facA, [SystemRoles.SuperAdmin], Permissions.GetAllPermissions()));
            req.Content = JsonContent.Create(new { facilityId = (Guid?)null, parentLocationType = "Aisle", childLocationType = "Bin", maxDepth = 6, isStorableLeaf = true, isActive = true });
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }
    }

    private static string IssueJwt(Guid userId, UserType userType, Guid? facilityId, IEnumerable<string> roles, IEnumerable<string> permissions)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new("user_type", userType.ToString())
        };
        if (facilityId.HasValue)
            claims.Add(new Claim("facility_id", facilityId.Value.ToString()));
        foreach (var r in roles)
            claims.Add(new Claim(ClaimTypes.Role, r));
        foreach (var p in permissions)
            claims.Add(new Claim("permission", p));

        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(JwtIssuer, JwtAudience, claims, expires: DateTime.UtcNow.AddHours(1), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
