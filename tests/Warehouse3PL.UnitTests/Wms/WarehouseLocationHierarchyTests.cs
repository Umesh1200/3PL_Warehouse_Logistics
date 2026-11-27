using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.Locations.Commands.BatchGenerateLocations;
using Warehouse3PL.Application.Features.Locations.Commands.CreateLocation;
using Warehouse3PL.Application.Features.Locations.Commands.LockLocation;
using Warehouse3PL.Application.Features.Locations.Commands.UnlockLocation;
using Warehouse3PL.Application.Features.Locations.Queries.GetLocationTree;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.UnitTests.Common;
using Xunit;

namespace Warehouse3PL.UnitTests.Wms;

public class WarehouseLocationHierarchyTests
{
    private static async Task<TestCurrentUserContext> SeedStaffAdminAsync(ApplicationDbContext db, Guid facilityId)
    {
        var role = new Role { RoleName = SystemRoles.SuperAdmin };
        var userId = Guid.NewGuid();
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        db.Users.Add(new User
        {
            Id = userId,
            Email = $"admin-{userId:N}@nexus3pl.test",
            PasswordHash = "hash",
            FirstName = "Test",
            LastName = "Admin",
            UserType = UserType.Staff,
            Status = UserStatus.Active,
            AssignedFacilityId = facilityId
        });
        db.UserRoles.Add(new UserRole { UserId = userId, RoleId = role.Id });
        await db.SaveChangesAsync();

        return new TestCurrentUserContext
        {
            UserId = userId,
            UserType = UserType.Staff,
            FacilityId = facilityId,
            RolesList = [SystemRoles.SuperAdmin],
            PermissionsList = Permissions.GetAllPermissions().ToList()
        };
    }

    [Fact]
    public async Task ConfigurableHierarchy_SupportsArbitraryParentChildTopology()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var facilityId = Guid.NewGuid();
        var zoneId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var seedUser = new TestCurrentUserContext { UserType = UserType.Staff };
        TestCurrentUserContext currentUser;

        using (var seedContext = TestDbContextFactory.Create(tenantProvider, seedUser, dbName))
        {
            var facility = new Facility { Id = facilityId, FacilityCode = "FAC-CHI", Name = "Chicago Hub" };
            var zone = new WarehouseZone { Id = zoneId, FacilityId = facilityId, ZoneCode = "ZN-PICK", Name = "Pick Mezzanine" };
            seedContext.Facilities.Add(facility);
            seedContext.WarehouseZones.Add(zone);
            currentUser = await SeedStaffAdminAsync(seedContext, facilityId);
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var auth = new WarehouseMasterAuthorization(context, currentUser);
        var handler = new CreateLocationCommandHandler(context, auth);

        // 1. Create Aisle (Parent: null)
        var aisle = await handler.Handle(new CreateLocationCommand
        {
            FacilityId = facilityId,
            ZoneId = zoneId,
            LocationType = LocationType.Aisle,
            LocationCode = "ZN-PICK-A01",
            Barcode = "LOC-ZN-PICK-A01",
            AisleCode = "A01"
        }, CancellationToken.None);

        // 2. Create Rack (Parent: Aisle)
        var rack = await handler.Handle(new CreateLocationCommand
        {
            FacilityId = facilityId,
            ZoneId = zoneId,
            ParentLocationId = aisle.Id,
            LocationType = LocationType.Rack,
            LocationCode = "ZN-PICK-A01-R01",
            Barcode = "LOC-ZN-PICK-A01-R01",
            AisleCode = "A01",
            RackCode = "R01"
        }, CancellationToken.None);

        // 3. Create Shelf (Parent: Rack)
        var shelf = await handler.Handle(new CreateLocationCommand
        {
            FacilityId = facilityId,
            ZoneId = zoneId,
            ParentLocationId = rack.Id,
            LocationType = LocationType.ShelfLevel,
            LocationCode = "ZN-PICK-A01-R01-S01",
            Barcode = "LOC-ZN-PICK-A01-R01-S01",
            ShelfLevel = 1
        }, CancellationToken.None);

        // 4. Create Bin (Parent: Shelf)
        var bin = await handler.Handle(new CreateLocationCommand
        {
            FacilityId = facilityId,
            ZoneId = zoneId,
            ParentLocationId = shelf.Id,
            LocationType = LocationType.Bin,
            LocationCode = "ZN-PICK-A01-R01-S01-B01",
            Barcode = "LOC-ZN-PICK-A01-R01-S01-B01",
            BinNumber = 1,
            LengthCm = 100,
            WidthCm = 80,
            HeightCm = 50,
            MaxWeightKg = 500
        }, CancellationToken.None);

        // Query the tree
        var treeHandler = new GetLocationTreeQueryHandler(context, auth);
        var tree = await treeHandler.Handle(new GetLocationTreeQuery(facilityId, zoneId), CancellationToken.None);

        // Assert
        Assert.Single(tree); // Single Root Aisle
        Assert.Equal(LocationType.Aisle, tree[0].LocationType);
        Assert.Single(tree[0].Children); // Single Rack child
        Assert.Equal(LocationType.Rack, tree[0].Children[0].LocationType);
        Assert.Single(tree[0].Children[0].Children); // Single Shelf child
        Assert.Equal(LocationType.ShelfLevel, tree[0].Children[0].Children[0].LocationType);
        Assert.Single(tree[0].Children[0].Children[0].Children); // Single Bin child
        Assert.Equal(LocationType.Bin, tree[0].Children[0].Children[0].Children[0].LocationType);
    }

    [Fact]
    public async Task BatchGenerateLocations_GeneratesCoordinatedGridWithHierarchy()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var facilityId = Guid.NewGuid();
        var zoneId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var seedUser = new TestCurrentUserContext { UserType = UserType.Staff };
        TestCurrentUserContext currentUser;

        using (var seedContext = TestDbContextFactory.Create(tenantProvider, seedUser, dbName))
        {
            seedContext.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-BULK", Name = "Bulk Logistics Center" });
            seedContext.WarehouseZones.Add(new WarehouseZone { Id = zoneId, FacilityId = facilityId, ZoneCode = "ZN-BULK", Name = "Bulk A" });
            currentUser = await SeedStaffAdminAsync(seedContext, facilityId);
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var handler = new BatchGenerateLocationsCommandHandler(context, new WarehouseMasterAuthorization(context, currentUser));

        // Act: Generate 2 Aisles, 2 Racks per Aisle, 2 Shelves per Rack, 2 Bins per Shelf
        var result = await handler.Handle(new BatchGenerateLocationsCommand
        {
            FacilityId = facilityId,
            ZoneId = zoneId,
            AisleCount = 2,
            RacksPerAisle = 2,
            ShelvesPerRack = 2,
            BinsPerShelf = 2
        }, CancellationToken.None);

        // Assert
        // 2 Aisles + (2*2 = 4 Racks) + (2*2*2*2 = 16 Bins) = 22 total
        Assert.Equal(2, result.AislesCreated);
        Assert.Equal(4, result.RacksCreated);
        Assert.Equal(16, result.BinsCreated);
        Assert.Equal(22, result.TotalCreated);

        var binsInDb = await context.Locations.Where(l => l.LocationType == LocationType.Bin).ToListAsync();
        Assert.Equal(16, binsInDb.Count);
        Assert.All(binsInDb, b => Assert.True(b.PickSequence.HasValue && b.PickSequence > 0));
    }

    [Fact]
    public async Task LockAndUnlockLocation_UpdatesOperationalAvailability()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var facilityId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var seedUser = new TestCurrentUserContext { UserType = UserType.Staff };
        TestCurrentUserContext currentUser;

        using (var seedContext = TestDbContextFactory.Create(tenantProvider, seedUser, dbName))
        {
            seedContext.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-MAIN", Name = "Main Hub" });
            seedContext.Locations.Add(new Location
            {
                Id = locationId,
                FacilityId = facilityId,
                LocationCode = "BIN-01-A",
                Barcode = "LOC-BIN-01-A",
                IsLocked = false
            });
            currentUser = await SeedStaffAdminAsync(seedContext, facilityId);
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var auth = new WarehouseMasterAuthorization(context, currentUser);

        // Act 1: Lock Location for Quarantine
        var lockHandler = new LockLocationCommandHandler(context, auth);
        var lockId = await lockHandler.Handle(new LockLocationCommand
        {
            LocationId = locationId,
            LockType = LocationLockType.Quarantine,
            ReasonCode = "DAMAGED_SHELF",
            Notes = "Bent support beam, awaiting maintenance."
        }, CancellationToken.None);

        var lockedLoc = await context.Locations.Include(l => l.Locks).FirstAsync(l => l.Id == locationId);
        Assert.True(lockedLoc.IsLocked);
        Assert.Single(lockedLoc.Locks);
        Assert.True(lockedLoc.Locks.First().IsActive);

        // Act 2: Unlock Location
        var unlockHandler = new UnlockLocationCommandHandler(context, auth);
        await unlockHandler.Handle(new UnlockLocationCommand(locationId), CancellationToken.None);

        var unlockedLoc = await context.Locations.Include(l => l.Locks).FirstAsync(l => l.Id == locationId);
        Assert.False(unlockedLoc.IsLocked);
        Assert.False(unlockedLoc.Locks.First().IsActive);
    }
}

