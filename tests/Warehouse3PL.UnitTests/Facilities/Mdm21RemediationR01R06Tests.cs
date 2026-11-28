using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Warehouse3PL.Application;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.Facilities.Commands;
using Warehouse3PL.Application.Features.Inbound.DockManagement.DockDoors.Commands.CreateDockDoor;
using Warehouse3PL.Application.Features.Inventory.Commands.TransferInventory;
using Warehouse3PL.Application.Features.Locations.Commands.CreateLocation;
using Warehouse3PL.Application.Features.WarehouseMaster;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.CrossDock;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Authentication;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Seed;
using Warehouse3PL.Infrastructure.Services;
using Warehouse3PL.Infrastructure.Services.Allocation;
using Warehouse3PL.Infrastructure.Services.Reference;
using Warehouse3PL.UnitTests.Common;
using Warehouse3PL.WebApi.Controllers;
using Warehouse3PL.WebApi.Middleware;
using Xunit;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.UnitTests.Facilities;

public sealed class Mdm21RemediationR01R06Tests
{
    private const string JwtSecret = "Nexus3PL-Mdm21-R01-R06-Security-Verification-Key-2026!";
    private const string JwtIssuer = "nexus3pl-mdm21-r01-test";
    private const string JwtAudience = "nexus3pl-mdm21-r01-api";

    private static async Task<(ApplicationDbContext Db, TestCurrentUserContext Admin, Guid FacilityId, Guid ClientA, Guid ClientB)>
        SeedBaseAsync()
    {
        var dbName = $"mdm21_r01_r06_{Guid.NewGuid():N}";
        var facId = Guid.NewGuid();
        var clientA = Guid.NewGuid();
        var clientB = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var tenant = new TestTenantProvider { BypassTenantFilter = true };
        var admin = new TestCurrentUserContext
        {
            UserId = userId,
            UserType = UserType.Staff,
            FacilityId = facId,
            RolesList = [SystemRoles.SuperAdmin],
            PermissionsList = Permissions.GetAllPermissions().ToList()
        };

        var db = TestDbContextFactory.Create(tenant, admin, dbName);
        await ReferenceCatalogSeeder.SeedAsync(db);

        db.Facilities.Add(new Facility
        {
            Id = facId,
            FacilityCode = "FAC-REM1",
            Name = "Remediation Hub",
            CountryCode = "US",
            Timezone = "America/Chicago",
            DefaultCurrencyCode = "USD",
            IsActive = true
        });
        db.Clients.AddRange(
            new Client { Id = clientA, CompanyName = "Client Alpha", AccountCode = "CL-A", Status = ClientStatus.Active },
            new Client { Id = clientB, CompanyName = "Client Beta", AccountCode = "CL-B", Status = ClientStatus.Active });
        db.ClientFacilityAccesses.AddRange(
            new ClientFacilityAccess { ClientId = clientA, FacilityId = facId, Status = FacilityAccessStatus.Active },
            new ClientFacilityAccess { ClientId = clientB, FacilityId = facId, Status = FacilityAccessStatus.Active });

        var role = new Role { RoleName = SystemRoles.SuperAdmin };
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        db.Users.Add(new User
        {
            Id = userId,
            Email = "admin@r01.test",
            PasswordHash = "x",
            FirstName = "Admin",
            LastName = "R01",
            UserType = UserType.Staff,
            Status = UserStatus.Active,
            AssignedFacilityId = facId
        });
        db.UserRoles.Add(new UserRole { UserId = userId, RoleId = role.Id });
        await db.SaveChangesAsync();

        return (db, admin, facId, clientA, clientB);
    }

    [Theory]
    [InlineData("QtyAvailable")]
    [InlineData("QtyAllocated")]
    [InlineData("QtyReserved")]
    [InlineData("QtyQuarantined")]
    [InlineData("QtyDamaged")]
    [InlineData("QtyOnHold")]
    public async Task R01_EveryStockBucket_IndividuallyBlocksLocationZoneFacilityDeactivationAndNonStorable(string bucket)
    {
        var (db, admin, facId, clientA, _) = await SeedBaseAsync();
        await using var _ = db;
        var auth = new WarehouseMasterAuthorization(db, admin);

        var zone = await new CreateWarehouseZoneCommandHandler(db, auth).Handle(new CreateWarehouseZoneCommand
        {
            FacilityId = facId,
            ZoneCode = "ZN-BKT",
            Name = "Bucket Zone"
        }, CancellationToken.None);

        var bin = await new CreateLocationCommandHandler(db, auth).Handle(new CreateLocationCommand
        {
            FacilityId = facId,
            ZoneId = zone.Id,
            LocationType = LocationType.Bin,
            LocationCode = "BIN-BKT",
            Barcode = "BC-BKT"
        }, CancellationToken.None);

        var item = new Item { ClientId = clientA, Sku = "SKU-BKT", Description = "Bucket SKU", WeightKg = 1m, LengthCm = 10, WidthCm = 10, HeightCm = 10 };
        db.Items.Add(item);

        var stock = new StockOnHand
        {
            ClientId = clientA,
            FacilityId = facId,
            ItemId = item.Id,
            LocationId = bin.Id,
            QtyAvailable = bucket == "QtyAvailable" ? 1m : 0m,
            QtyAllocated = bucket == "QtyAllocated" ? 1m : 0m,
            QtyReserved = bucket == "QtyReserved" ? 1m : 0m,
            QtyQuarantined = bucket == "QtyQuarantined" ? 1m : 0m,
            QtyDamaged = bucket == "QtyDamaged" ? 1m : 0m,
            QtyOnHold = bucket == "QtyOnHold" ? 1m : 0m
        };
        db.StockOnHand.Add(stock);
        await db.SaveChangesAsync();

        // 1. Location deactivation blocked
        await Assert.ThrowsAsync<DomainException>(() =>
            new UpdateLocationStatusCommandHandler(db, auth).Handle(new UpdateLocationStatusCommand(bin.Id, LocationStatus.Inactive), CancellationToken.None));

        // 2. IsStorable = false blocked
        await Assert.ThrowsAsync<DomainException>(() =>
            new UpdateLocationCommandHandler(db, auth).Handle(new UpdateLocationCommand { Id = bin.Id, IsStorable = false }, CancellationToken.None));

        // 3. Facility deactivation blocked
        var updateFacility = new UpdateFacilityCommandHandler(db, new CompliancePlatformAuthorization(db, admin), new TimeZoneOffsetResolver());
        await Assert.ThrowsAsync<DomainException>(() => updateFacility.Handle(new UpdateFacilityCommand(
            facId, "FAC-REM1", "Remediation Hub", null, null, null, null, null, "US", "America/Chicago", null, "USD", null, null, null, 2, false), CancellationToken.None));
    }

    [Fact]
    public async Task R01_OperationalReferences_DamagedHU_PickDest_CrossDock_Packing_CycleCount_Allocation_BlockDeactivation()
    {
        var (db, admin, facId, clientA, _) = await SeedBaseAsync();
        await using var _ = db;
        var auth = new WarehouseMasterAuthorization(db, admin);
        var createLoc = new CreateLocationCommandHandler(db, auth);
        var setStatus = new UpdateLocationStatusCommandHandler(db, auth);

        var binHu = await createLoc.Handle(new CreateLocationCommand { FacilityId = facId, LocationType = LocationType.Bin, LocationCode = "B-HU", Barcode = "BC-B-HU" }, CancellationToken.None);
        var binPickDest = await createLoc.Handle(new CreateLocationCommand { FacilityId = facId, LocationType = LocationType.Bin, LocationCode = "B-PKD", Barcode = "BC-B-PKD" }, CancellationToken.None);
        var binCd = await createLoc.Handle(new CreateLocationCommand { FacilityId = facId, LocationType = LocationType.StagingLane, LocationCode = "S-CD", Barcode = "BC-S-CD" }, CancellationToken.None);
        var stnPack = await createLoc.Handle(new CreateLocationCommand { FacilityId = facId, LocationType = LocationType.PackingStation, LocationCode = "P-STN", Barcode = "BC-P-STN" }, CancellationToken.None);
        var binCc = await createLoc.Handle(new CreateLocationCommand { FacilityId = facId, LocationType = LocationType.Bin, LocationCode = "B-CC", Barcode = "BC-B-CC" }, CancellationToken.None);
        var binAlloc = await createLoc.Handle(new CreateLocationCommand { FacilityId = facId, LocationType = LocationType.Bin, LocationCode = "B-AL", Barcode = "BC-B-AL" }, CancellationToken.None);

        var item = new Item { ClientId = clientA, Sku = "SKU-OP", Description = "Op SKU", WeightKg = 1m };
        db.Items.Add(item);

        // 1. Physically present DAMAGED HandlingUnit blocks deactivation
        db.HandlingUnits.Add(new HandlingUnit { ClientId = clientA, FacilityId = facId, LpnCode = "LPN-DMG", CurrentLocationId = binHu.Id, Status = HandlingUnitStatus.Damaged });

        // 2. Open PickTask referencing DestinationLocationId blocks deactivation
        db.PickTasks.Add(new PickTask { ClientId = clientA, FacilityId = facId, ItemId = item.Id, SourceLocationId = binHu.Id, DestinationLocationId = binPickDest.Id, QuantityToPick = 1m, Status = PickTaskStatus.Pending });

        // 3. Open CrossDockTask blocks deactivation
        db.CrossDockTasks.Add(new CrossDockTask { ClientId = clientA, FacilityId = facId, ItemId = item.Id, SourceLocationId = binHu.Id, DestinationLocationId = binCd.Id, Quantity = 1m, Status = CrossDockTaskStatus.Pending });

        // 4. Open PackingSession blocks station deactivation
        db.PackingSessions.Add(new PackingSession { ClientId = clientA, FacilityId = facId, PackingStationLocationId = stnPack.Id, OutboundOrderId = Guid.NewGuid(), OperatorUserId = admin.UserId!.Value, Status = PackingSessionStatus.Active });

        // 5. Unreconciled open CycleCountItem blocks deactivation
        var cc = new CycleCount { FacilityId = facId, Status = CycleCountStatus.InProgress };
        db.CycleCounts.Add(cc);
        db.CycleCountItems.Add(new CycleCountItem { CycleCountId = cc.Id, LocationId = binCc.Id, ItemId = item.Id, ExpectedQty = 5m, IsReconciled = false });

        // 6. Active InventoryAllocation blocks deactivation
        db.InventoryAllocations.Add(new InventoryAllocation { ClientId = clientA, FacilityId = facId, ItemId = item.Id, LocationId = binAlloc.Id, OutboundOrderId = Guid.NewGuid(), OutboundOrderLineId = Guid.NewGuid(), StockOnHandId = Guid.NewGuid(), QuantityRequested = 2m, QuantityAllocated = 2m, Status = AllocationStatus.Allocated });

        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<DomainException>(() => setStatus.Handle(new UpdateLocationStatusCommand(binHu.Id, LocationStatus.Inactive), CancellationToken.None));
        await Assert.ThrowsAsync<DomainException>(() => setStatus.Handle(new UpdateLocationStatusCommand(binPickDest.Id, LocationStatus.Inactive), CancellationToken.None));
        await Assert.ThrowsAsync<DomainException>(() => setStatus.Handle(new UpdateLocationStatusCommand(binCd.Id, LocationStatus.Inactive), CancellationToken.None));
        await Assert.ThrowsAsync<DomainException>(() => setStatus.Handle(new UpdateLocationStatusCommand(stnPack.Id, LocationStatus.Inactive), CancellationToken.None));
        await Assert.ThrowsAsync<DomainException>(() => setStatus.Handle(new UpdateLocationStatusCommand(binCc.Id, LocationStatus.Inactive), CancellationToken.None));
        await Assert.ThrowsAsync<DomainException>(() => setStatus.Handle(new UpdateLocationStatusCommand(binAlloc.Id, LocationStatus.Inactive), CancellationToken.None));
    }

    [Fact]
    public async Task R02_DescendantMaxDepth_IsStorableLeaf_AndRuleConflictWithExistingHierarchy()
    {
        var (db, admin, facId, _, _) = await SeedBaseAsync();
        await using var _ = db;
        var auth = new WarehouseMasterAuthorization(db, admin);
        var createLoc = new CreateLocationCommandHandler(db, auth);
        var moveLoc = new MoveLocationCommandHandler(db, auth);
        var upsertRule = new UpsertLocationHierarchyRuleCommandHandler(db, auth);

        // IsStorableLeaf = false on Aisle -> requesting IsStorable = true MUST fail
        await Assert.ThrowsAsync<ValidationException>(() => createLoc.Handle(new CreateLocationCommand
        {
            FacilityId = facId,
            LocationType = LocationType.Aisle,
            LocationCode = "A-STOR-BAD",
            Barcode = "BC-A-STOR-BAD",
            IsStorable = true
        }, CancellationToken.None));

        // Configure facility rule: ShelfLevel -> Bin MaxDepth = 3
        await upsertRule.Handle(new UpsertLocationHierarchyRuleCommand
        {
            FacilityId = facId,
            ParentLocationType = LocationType.ShelfLevel,
            ChildLocationType = LocationType.Bin,
            MaxDepth = 3,
            IsStorableLeaf = true,
            IsActive = true
        }, CancellationToken.None);

        // Build Root Rack (L1) -> Shelf (L2) -> Bin (L3) + Aisle (L1)
        var aisle = await createLoc.Handle(new CreateLocationCommand { FacilityId = facId, LocationType = LocationType.Aisle, LocationCode = "A1", Barcode = "BC-A1" }, CancellationToken.None);
        var rack = await createLoc.Handle(new CreateLocationCommand { FacilityId = facId, LocationType = LocationType.Rack, LocationCode = "R1", Barcode = "BC-R1" }, CancellationToken.None);
        var shelf = await createLoc.Handle(new CreateLocationCommand { FacilityId = facId, ParentLocationId = rack.Id, LocationType = LocationType.ShelfLevel, LocationCode = "S1", Barcode = "BC-S1" }, CancellationToken.None);
        var bin = await createLoc.Handle(new CreateLocationCommand { FacilityId = facId, ParentLocationId = shelf.Id, LocationType = LocationType.Bin, LocationCode = "B1", Barcode = "BC-B1" }, CancellationToken.None);

        // Moving Rack under Aisle would push Bin to depth 4 (> MaxDepth 3) -> MUST reject & leave subtree unchanged!
        await Assert.ThrowsAsync<ValidationException>(() => moveLoc.Handle(new MoveLocationCommand(rack.Id, aisle.Id), CancellationToken.None));
        var unchangedBin = await db.Locations.AsNoTracking().FirstAsync(l => l.Id == bin.Id);
        Assert.Equal(3, unchangedBin.HierarchyLevel);
        Assert.Equal($"/{rack.Id:N}/{shelf.Id:N}/{bin.Id:N}/", unchangedBin.HierarchyPath);

        // Attempting to disable ShelfLevel -> Bin or reduce MaxDepth to 2 while B1 exists at L3 MUST fail
        await Assert.ThrowsAsync<DomainException>(() => upsertRule.Handle(new UpsertLocationHierarchyRuleCommand
        {
            FacilityId = facId,
            ParentLocationType = LocationType.ShelfLevel,
            ChildLocationType = LocationType.Bin,
            MaxDepth = 2,
            IsStorableLeaf = true,
            IsActive = true
        }, CancellationToken.None));

        await Assert.ThrowsAsync<DomainException>(() => upsertRule.Handle(new UpsertLocationHierarchyRuleCommand
        {
            FacilityId = facId,
            ParentLocationType = LocationType.ShelfLevel,
            ChildLocationType = LocationType.Bin,
            MaxDepth = 3,
            IsStorableLeaf = true,
            IsActive = false
        }, CancellationToken.None));
    }

    [Fact]
    public async Task R03_R05_AllocationIsStorable_TransferEnforcement_ProfileConflicts_DeferredMandatory_AndImmutableBarcode()
    {
        var (db, admin, facId, clientA, clientB) = await SeedBaseAsync();
        await using var _ = db;
        var auth = new WarehouseMasterAuthorization(db, admin);
        var createLoc = new CreateLocationCommandHandler(db, auth);
        var updateLoc = new UpdateLocationCommandHandler(db, auth);
        var upsertStorage = new UpsertLocationStorageProfileCommandHandler(db, auth);

        var zoneA = await new CreateWarehouseZoneCommandHandler(db, auth).Handle(new CreateWarehouseZoneCommand
        {
            FacilityId = facId,
            ZoneCode = "ZN-A",
            Name = "Client A Zone",
            DedicatedClientId = clientA
        }, CancellationToken.None);

        var bin1 = await createLoc.Handle(new CreateLocationCommand { FacilityId = facId, ZoneId = zoneA.Id, LocationType = LocationType.Bin, LocationCode = "B-01", Barcode = "BC-IMMUT-01" }, CancellationToken.None);
        var bin2 = await createLoc.Handle(new CreateLocationCommand { FacilityId = facId, LocationType = LocationType.Bin, LocationCode = "B-02", Barcode = "BC-IMMUT-02" }, CancellationToken.None);

        // R05: Barcode replacement rejected; case/whitespace identical allowed
        await Assert.ThrowsAsync<DomainException>(() => updateLoc.Handle(new UpdateLocationCommand { Id = bin1.Id, Barcode = "BC-REPLACED" }, CancellationToken.None));
        var sameBarcodeOk = await updateLoc.Handle(new UpdateLocationCommand { Id = bin1.Id, Barcode = "  bc-immut-01 " }, CancellationToken.None);
        Assert.Equal("BC-IMMUT-01", sameBarcodeOk.Barcode);

        // R03: Zone & location dedication conflict rejected
        await Assert.ThrowsAsync<DomainException>(() => upsertStorage.Handle(new UpsertLocationStorageProfileCommand { LocationId = bin1.Id, DedicatedClientId = clientB }, CancellationToken.None));

        // R03: Unsupported mandatory equipment & session limit > 1 rejected; advisory metadata accepted
        await Assert.ThrowsAsync<ValidationException>(() =>
            new AddLocationEquipmentRequirementCommandHandler(db, auth).Handle(new AddLocationEquipmentRequirementCommand(bin1.Id, WarehouseEquipmentType.ReachTruck, IsMandatory: true), CancellationToken.None));
        var advisoryReq = await new AddLocationEquipmentRequirementCommandHandler(db, auth).Handle(
            new AddLocationEquipmentRequirementCommand(bin1.Id, WarehouseEquipmentType.ReachTruck, IsMandatory: false), CancellationToken.None);
        Assert.False(advisoryReq.IsMandatory);

        await Assert.ThrowsAsync<ValidationException>(() =>
            new UpsertLocationOperationalProfileCommandHandler(db, auth).Handle(new UpsertLocationOperationalProfileCommand
            {
                LocationId = bin1.Id,
                StationRole = WarehouseStationRole.Packing,
                MaxConcurrentSessions = 3
            }, CancellationToken.None));

        // R03: AllocationCandidateFilter rejects IsStorable = false
        var item1 = new Item { ClientId = clientA, Sku = "SKU-1", Description = "SKU 1", WeightKg = 1m };
        var item2 = new Item { ClientId = clientA, Sku = "SKU-2", Description = "SKU 2", WeightKg = 1m };
        db.Items.AddRange(item1, item2);
        var nonStorableLoc = new Location { FacilityId = facId, LocationType = LocationType.Bin, LocationCode = "NS-1", Barcode = "BC-NS-1", IsStorable = false, IsActive = true, IsPickable = true };
        var filter = new AllocationCandidateFilter();
        var elig = filter.Evaluate(
            new StockOnHand { ClientId = clientA, FacilityId = facId, ItemId = item1.Id, LocationId = nonStorableLoc.Id, Location = nonStorableLoc, Item = item1, QtyAvailable = 5m },
            new AllocationRequest { ClientId = clientA, FacilityId = facId, ItemId = item1.Id, QuantityRequested = 1m });
        Assert.False(elig.IsEligible);

        // R03: Profile tightening contradicting existing mixed SKUs rejected
        db.StockOnHand.AddRange(
            new StockOnHand { ClientId = clientA, FacilityId = facId, ItemId = item1.Id, LocationId = bin2.Id, QtyAvailable = 5m },
            new StockOnHand { ClientId = clientA, FacilityId = facId, ItemId = item2.Id, LocationId = bin2.Id, QtyAvailable = 3m });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<DomainException>(() => upsertStorage.Handle(new UpsertLocationStorageProfileCommand { LocationId = bin2.Id, AllowMixedSku = false }, CancellationToken.None));

        // R03: TransferInventoryCommand enforces storage profile (IsPutawayAllowed = false)
        await upsertStorage.Handle(new UpsertLocationStorageProfileCommand { LocationId = bin1.Id, IsPutawayAllowed = false }, CancellationToken.None);
        var transfer = new TransferInventoryCommandHandler(db, admin, new WarehouseOperationalAuthorization(db, admin));
        await Assert.ThrowsAsync<ValidationException>(() => transfer.Handle(new TransferInventoryCommand
        {
            ClientId = clientA,
            FacilityId = facId,
            ItemId = item1.Id,
            FromLocationId = bin2.Id,
            ToLocationId = bin1.Id,
            Quantity = 1m
        }, CancellationToken.None));
    }

    [Fact]
    public async Task R06_RealHttpJwt_EnforcesActiveFacilityForMutations_PreservesHistoricalReads_AndRejectsRevokedRolesAndBindings()
    {
        var dbName = $"mdm21_r06_http_{Guid.NewGuid():N}";
        var inactiveFacId = Guid.NewGuid();
        var activeFacId = Guid.NewGuid();
        var clientRevokedId = Guid.NewGuid();
        var superAdminId = Guid.NewGuid();
        var revokedStaffId = Guid.NewGuid();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        builder.Services.AddScoped<ITenantProvider, TestTenantProvider>();
        builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
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

        Guid nonStagingBinId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Facilities.AddRange(
                new Facility { Id = activeFacId, FacilityCode = "FAC-ACT", Name = "Active Hub", CountryCode = "US", Timezone = "America/Chicago", IsActive = true },
                new Facility { Id = inactiveFacId, FacilityCode = "FAC-INACT", Name = "Inactive Hub", CountryCode = "US", Timezone = "America/Chicago", IsActive = false });
            db.WarehouseZones.Add(new WarehouseZone { FacilityId = inactiveFacId, ZoneCode = "ZN-HIST", Name = "Historical Zone", IsActive = true });
            var bin = new Location { FacilityId = activeFacId, LocationType = LocationType.Bin, LocationCode = "B-NOT-STG", Barcode = "BC-NOT-STG", IsActive = true, IsReceiving = false };
            db.Locations.Add(bin);
            nonStagingBinId = bin.Id;

            db.Clients.Add(new Client { Id = clientRevokedId, CompanyName = "Revoked Client", AccountCode = "CL-REV", Status = ClientStatus.Active });
            db.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = clientRevokedId, FacilityId = activeFacId, Status = FacilityAccessStatus.Revoked });

            var superRole = new Role { RoleName = SystemRoles.SuperAdmin };
            var emptyRole = new Role { RoleName = "RevokedRole" };
            db.Roles.AddRange(superRole, emptyRole);
            await db.SaveChangesAsync();

            db.Users.AddRange(
                new User { Id = superAdminId, Email = "sa@r06.test", PasswordHash = "x", FirstName = "SA", LastName = "R06", UserType = UserType.Staff, Status = UserStatus.Active, AssignedFacilityId = activeFacId },
                new User { Id = revokedStaffId, Email = "rev@r06.test", PasswordHash = "x", FirstName = "Rev", LastName = "Staff", UserType = UserType.Staff, Status = UserStatus.Active, AssignedFacilityId = activeFacId });
            db.UserRoles.AddRange(
                new UserRole { UserId = superAdminId, RoleId = superRole.Id },
                new UserRole { UserId = revokedStaffId, RoleId = emptyRole.Id });
            await db.SaveChangesAsync();
        }

        var client = app.GetTestClient();
        var saToken = IssueJwt(superAdminId, UserType.Staff, activeFacId, [SystemRoles.SuperAdmin], Permissions.GetAllPermissions());

        // 1. Historical read on INACTIVE facility succeeds (200 OK)
        using (var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/warehouse-zones?facilityId={inactiveFacId}"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", saToken);
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        // 2. Mutation on INACTIVE facility is rejected (422)
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/warehouse-zones"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", saToken);
            req.Content = JsonContent.Create(new { facilityId = inactiveFacId, zoneCode = "ZN-NEW", name = "Blocked Zone", zoneType = "BulkStorage" });
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        }

        // 3. Revoked DB role (JWT still claims ZonesManage) is rejected (403 Forbidden)
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/warehouse-zones"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", IssueJwt(revokedStaffId, UserType.Staff, activeFacId, ["FacilityManager"], [Permissions.Facilities.ZonesManage]));
            req.Content = JsonContent.Create(new { facilityId = activeFacId, zoneCode = "ZN-REV", name = "Revoked", zoneType = "BulkStorage" });
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        }

        // 4. Revoked ClientFacilityAccess rejected (400)
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/warehouse-zones"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", saToken);
            req.Content = JsonContent.Create(new { facilityId = activeFacId, zoneCode = "ZN-CLREV", name = "Revoked Client Zone", zoneType = "BulkStorage", dedicatedClientId = clientRevokedId });
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        // 5. Non-staging Bin rejected as DefaultStagingLocationId (400) & non-DockDoor Bin rejected as DockDoor.LocationId (400)
        using (var req = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/locations/{nonStagingBinId}/operational-profile"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", saToken);
            req.Content = JsonContent.Create(new { stationRole = "Packing", maxConcurrentSessions = 1, defaultStagingLocationId = nonStagingBinId });
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/dock-doors"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", saToken);
            req.Content = JsonContent.Create(new { facilityId = activeFacId, locationId = nonStagingBinId, doorNumber = "D-BADTYPE", doorType = "Inbound" });
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
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
        if (facilityId.HasValue) claims.Add(new Claim("facility_id", facilityId.Value.ToString()));
        foreach (var r in roles) claims.Add(new Claim(ClaimTypes.Role, r));
        foreach (var p in permissions) claims.Add(new Claim("permission", p));

        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(JwtIssuer, JwtAudience, claims, expires: DateTime.UtcNow.AddHours(1), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
