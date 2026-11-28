using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.CrossDock.Tasks.Commands.ConfirmCrossDockTask;
using Warehouse3PL.Application.Features.Facilities.Commands;
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
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;
using Warehouse3PL.Infrastructure.Persistence.Seed;
using Warehouse3PL.Infrastructure.Services;
using Warehouse3PL.Infrastructure.Services.Packing;
using Warehouse3PL.Infrastructure.Services.Shipping;
using Xunit;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.UnitTests.Facilities;

/// <summary>
/// Targeted closure regressions for MDM-2.1 Follow-up Findings F01–F03:
/// - F01: Complete aggregate lifecycle guards for zones (incl. legacy inconsistent inactive locations) and facilities
/// - F02-A: Return inventory posting eligibility, no unrestricted fallback, atomic multi-line validation
/// - F02-B: MoveLocationCommandHandler zone dedication parity with UpdateLocationCommandHandler
/// - F02-C: ConfirmCrossDockTaskCommand destination eligibility (planned & override)
/// - F03-B: PackingExecutionService.StartOrResumeSessionAsync active, same-facility, eligible station enforcement
/// </summary>
public class Mdm21ClosureF01F03Tests
{
    private sealed class TestIdentity(Guid userId, Guid? facilityId, bool isSuperAdmin = true) : ITenantProvider, ICurrentUserContext
    {
        public Guid? TenantId { get; private set; }
        public bool HasTenant => TenantId.HasValue;
        public bool BypassTenantFilter { get; set; } = true;
        public void SetTenantId(Guid tenantId) => TenantId = tenantId;
        public Guid? UserId => userId;
        public string? Email => "f01-f03-admin@nexus3pl.test";
        public Guid? FacilityId => facilityId;
        public Guid? RequestedFacilityId => null;
        public UserType? UserType => Warehouse3PL.Domain.Enums.UserType.Staff;
        public IReadOnlyList<string> Roles { get; } = [SystemRoles.SuperAdmin];
        public IReadOnlyList<string> Permissions { get; } = Warehouse3PL.Domain.Constants.Permissions.GetAllPermissions();
        public bool IsAuthenticated => true;
        public bool IsStaff => true;
        public bool IsSuperAdmin => isSuperAdmin;
        public bool HasPermission(string permission) => true;
        public bool IsInRole(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
    }

    private sealed class TestTzResolver : ITimeZoneOffsetResolver
    {
        public int GetUtcOffsetMinutes(string timeZoneId, DateTime utcDateTime) => 0;
        public bool TryValidateTimeZoneId(string timeZoneId) => true;
    }

    private static async Task<(ApplicationDbContext Db, TestIdentity Admin, Guid FacId, Guid FacOtherId, Guid ClientA, Guid ClientB)> SeedBaseAsync()
    {
        var userId = Guid.NewGuid();
        var facId = Guid.NewGuid();
        var facOtherId = Guid.NewGuid();
        var clientA = Guid.NewGuid();
        var clientB = Guid.NewGuid();

        var admin = new TestIdentity(userId, facId);
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mdm21_f01f03_{Guid.NewGuid():N}")
            .Options;
        var audit = new AuditableEntityInterceptor(admin, new DateTimeProvider());
        var db = new ApplicationDbContext(opts, admin, admin, audit);
        await ReferenceCatalogSeeder.SeedAsync(db);

        db.Facilities.AddRange(
            new Facility { Id = facId, FacilityCode = "FAC-F01", Name = "Primary Hub", CountryCode = "US", Timezone = "America/Chicago", DefaultCurrencyCode = "USD", IsActive = true },
            new Facility { Id = facOtherId, FacilityCode = "FAC-F02", Name = "Secondary Hub", CountryCode = "US", Timezone = "America/Chicago", DefaultCurrencyCode = "USD", IsActive = true });
        db.Clients.AddRange(
            new Client { Id = clientA, CompanyName = "Client A", AccountCode = "CL-FA", Status = ClientStatus.Active },
            new Client { Id = clientB, CompanyName = "Client B", AccountCode = "CL-FB", Status = ClientStatus.Active });
        db.ClientFacilityAccesses.AddRange(
            new ClientFacilityAccess { ClientId = clientA, FacilityId = facId, Status = FacilityAccessStatus.Active },
            new ClientFacilityAccess { ClientId = clientB, FacilityId = facId, Status = FacilityAccessStatus.Active },
            new ClientFacilityAccess { ClientId = clientA, FacilityId = facOtherId, Status = FacilityAccessStatus.Active });

        var role = new Role { RoleName = SystemRoles.SuperAdmin };
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        db.Users.Add(new User
        {
            Id = userId,
            Email = "f01-f03-admin@nexus3pl.test",
            PasswordHash = "hash",
            FirstName = "Closure",
            LastName = "Admin",
            UserType = UserType.Staff,
            Status = UserStatus.Active,
            AssignedFacilityId = facId
        });
        db.UserRoles.Add(new UserRole { UserId = userId, RoleId = role.Id });
        await db.SaveChangesAsync();

        return (db, admin, facId, facOtherId, clientA, clientB);
    }

    [Theory]
    [InlineData("DamagedHU")]
    [InlineData("PutawayTask")]
    [InlineData("PickTask")]
    [InlineData("CrossDockTask")]
    [InlineData("PackingSession")]
    [InlineData("CycleCountItem")]
    [InlineData("InventoryAllocation")]
    public async Task F01_ZoneDeactivation_BlockedByLegacyInconsistentInactiveLocationDependency(string dependencyType)
    {
        var (db, admin, facId, _, clientA, _) = await SeedBaseAsync();
        await using var _ = db;
        var auth = new WarehouseMasterAuthorization(db, admin);

        var zone = await new CreateWarehouseZoneCommandHandler(db, auth).Handle(new CreateWarehouseZoneCommand
        {
            FacilityId = facId,
            ZoneCode = $"ZN-LEG-{dependencyType[..3].ToUpperInvariant()}",
            Name = "Legacy Zone"
        }, CancellationToken.None);

        // Simulate legacy inconsistent state: location is ALREADY Inactive, yet holds an open operational dependency
        var inactiveLoc = new Location
        {
            FacilityId = facId,
            ZoneId = zone.Id,
            LocationType = dependencyType == "PackingSession" ? LocationType.PackingStation : LocationType.Bin,
            LocationCode = "LEG-INACT-01",
            Barcode = "BC-LEG-INACT-01",
            Status = LocationStatus.Inactive,
            IsActive = false,
            IsStorable = dependencyType != "PackingSession"
        };
        db.Locations.Add(inactiveLoc);

        var item = new Item { ClientId = clientA, Sku = "SKU-LEG", Description = "Legacy SKU", WeightKg = 1m };
        db.Items.Add(item);

        switch (dependencyType)
        {
            case "DamagedHU":
                db.HandlingUnits.Add(new HandlingUnit { ClientId = clientA, FacilityId = facId, LpnCode = "LPN-LEG", CurrentLocationId = inactiveLoc.Id, Status = HandlingUnitStatus.Damaged });
                break;
            case "PutawayTask":
                db.PutawayTasks.Add(new PutawayTask { ClientId = clientA, FacilityId = facId, ItemId = item.Id, SourceLocationId = inactiveLoc.Id, SuggestedLocationId = inactiveLoc.Id, Quantity = 2m, Status = PutawayTaskStatus.Pending });
                break;
            case "PickTask":
                db.PickTasks.Add(new PickTask { ClientId = clientA, FacilityId = facId, ItemId = item.Id, SourceLocationId = inactiveLoc.Id, QuantityToPick = 2m, Status = PickTaskStatus.Pending });
                break;
            case "CrossDockTask":
                db.CrossDockTasks.Add(new CrossDockTask { ClientId = clientA, FacilityId = facId, ItemId = item.Id, SourceLocationId = inactiveLoc.Id, DestinationLocationId = inactiveLoc.Id, Quantity = 2m, Status = CrossDockTaskStatus.Pending });
                break;
            case "PackingSession":
                db.PackingSessions.Add(new PackingSession { ClientId = clientA, FacilityId = facId, PackingStationLocationId = inactiveLoc.Id, OutboundOrderId = Guid.NewGuid(), OperatorUserId = admin.UserId!.Value, Status = PackingSessionStatus.Active });
                break;
            case "CycleCountItem":
                var cc = new CycleCount { FacilityId = facId, Status = CycleCountStatus.InProgress };
                db.CycleCounts.Add(cc);
                db.CycleCountItems.Add(new CycleCountItem { CycleCountId = cc.Id, LocationId = inactiveLoc.Id, ItemId = item.Id, ExpectedQty = 3m, IsReconciled = false });
                break;
            case "InventoryAllocation":
                db.InventoryAllocations.Add(new InventoryAllocation { ClientId = clientA, FacilityId = facId, ItemId = item.Id, LocationId = inactiveLoc.Id, OutboundOrderId = Guid.NewGuid(), OutboundOrderLineId = Guid.NewGuid(), StockOnHandId = Guid.NewGuid(), QuantityRequested = 1m, QuantityAllocated = 1m, Status = AllocationStatus.Allocated });
                break;
        }
        await db.SaveChangesAsync();

        var zoneStatus = new UpdateWarehouseZoneStatusCommandHandler(db, auth);
        await Assert.ThrowsAsync<DomainException>(() =>
            zoneStatus.Handle(new UpdateWarehouseZoneStatusCommand(zone.Id, IsActive: false), CancellationToken.None));
    }

    [Theory]
    [InlineData("OpenCycleCount")]
    [InlineData("ActiveAllocation")]
    [InlineData("ActiveLocation")]
    public async Task F01_FacilityDeactivation_BlockedByCycleCountAllocationOrActiveLocation(string blocker)
    {
        var (db, admin, facId, _, clientA, _) = await SeedBaseAsync();
        await using var _ = db;

        var item = new Item { ClientId = clientA, Sku = "SKU-FAC", Description = "Fac SKU", WeightKg = 1m };
        db.Items.Add(item);

        var inactiveBin = new Location
        {
            FacilityId = facId,
            LocationType = LocationType.Bin,
            LocationCode = "FAC-BIN-INACT",
            Barcode = "BC-FAC-BIN-INACT",
            Status = blocker == "ActiveLocation" ? LocationStatus.Active : LocationStatus.Inactive,
            IsActive = blocker == "ActiveLocation"
        };
        db.Locations.Add(inactiveBin);

        if (blocker == "OpenCycleCount")
        {
            db.CycleCounts.Add(new CycleCount { FacilityId = facId, Status = CycleCountStatus.InProgress });
        }
        else if (blocker == "ActiveAllocation")
        {
            db.InventoryAllocations.Add(new InventoryAllocation
            {
                ClientId = clientA,
                FacilityId = facId,
                ItemId = item.Id,
                LocationId = inactiveBin.Id,
                OutboundOrderId = Guid.NewGuid(),
                OutboundOrderLineId = Guid.NewGuid(),
                StockOnHandId = Guid.NewGuid(),
                QuantityRequested = 2m,
                QuantityAllocated = 2m,
                Status = AllocationStatus.Allocated
            });
        }
        await db.SaveChangesAsync();

        var updateFacility = new UpdateFacilityCommandHandler(db, new CompliancePlatformAuthorization(db, admin), new TestTzResolver());
        await Assert.ThrowsAsync<DomainException>(() => updateFacility.Handle(new UpdateFacilityCommand(
            facId, null, "Primary Hub", null, null, null, null, null, "US", "America/Chicago", null, "USD", null, null, null, 2, IsActive: false), CancellationToken.None));

        // Verify facility remained Active and subordinate location was NOT silently cascaded
        var facAfter = await db.Facilities.AsNoTracking().FirstAsync(f => f.Id == facId);
        Assert.True(facAfter.IsActive);
        if (blocker == "ActiveLocation")
        {
            var locAfter = await db.Locations.AsNoTracking().FirstAsync(l => l.Id == inactiveBin.Id);
            Assert.Equal(LocationStatus.Active, locAfter.Status);
        }
    }

    [Fact]
    public async Task F02A_ReturnInventoryPosting_RejectsStructuralInvalidFallbackAndDedicatedConflict_AndPostsValid()
    {
        var (db, admin, facId, facOtherId, clientA, clientB) = await SeedBaseAsync();
        await using var _ = db;
        var auth = new WarehouseMasterAuthorization(db, admin);
        var createLoc = new CreateLocationCommandHandler(db, auth);

        var zoneB = await new CreateWarehouseZoneCommandHandler(db, auth).Handle(new CreateWarehouseZoneCommand
        {
            FacilityId = facId,
            ZoneCode = "ZN-RET-B",
            Name = "Client B Only",
            DedicatedClientId = clientB
        }, CancellationToken.None);

        var aisle = await createLoc.Handle(new CreateLocationCommand { FacilityId = facId, LocationType = LocationType.Aisle, LocationCode = "RET-AISLE", Barcode = "BC-RET-AISLE" }, CancellationToken.None);
        var binClientB = await createLoc.Handle(new CreateLocationCommand { FacilityId = facId, ZoneId = zoneB.Id, LocationType = LocationType.Bin, LocationCode = "RET-BIN-B", Barcode = "BC-RET-BIN-B" }, CancellationToken.None);
        var otherFacBin = await createLoc.Handle(new CreateLocationCommand { FacilityId = facOtherId, LocationType = LocationType.Bin, LocationCode = "RET-OTHER", Barcode = "BC-RET-OTHER" }, CancellationToken.None);

        var item = new Item { ClientId = clientA, Sku = "SKU-RET-1", Description = "Returned Item", WeightKg = 1m };
        db.Items.Add(item);

        var origOrder = new OutboundOrder { ClientId = clientA, FacilityId = facId, OrderNumber = "ORD-RET-F02A", CustomerName = "Ret Cust", Status = OutboundOrderStatus.Completed };
        var origPkg = new Package { ClientId = clientA, FacilityId = facId, OutboundOrderId = origOrder.Id, PackageNumber = "PKG-RET-F02A", TrackingNumber = "TRK-RET-F02A", Status = PackageStatus.Loaded };
        var retAuth = ReturnAuthorization.Create(clientA, facId, facId, origOrder.Id, null, origPkg.Id, null, "RA-F02A", ReturnReason.DamagedInTransit, "Test", admin.UserId!.Value, "RMA-F02A", DateTime.UtcNow.AddDays(7));
        var retShip = new ReturnShipment { ClientId = clientA, ReturnAuthorizationId = retAuth.Id, OrderId = origOrder.Id, PackageId = origPkg.Id, OriginFacilityId = facId, DestinationFacilityId = facId, CarrierCode = "FEDEX", TrackingNumber = origPkg.TrackingNumber, AuthorizedByUserId = admin.UserId!.Value, Status = ReturnShipmentStatus.DeliveredAtWarehouse, DeliveredAtWarehouseUtc = DateTime.UtcNow.AddHours(-1) };
        var handoff = ReturnInboundHandoff.Create(clientA, facId, retShip.Id, retAuth.Id, origPkg.Id, origOrder.Id, "HND-F02A", retAuth.RmaNumber!, retShip.TrackingNumber, retShip.CarrierCode, DateTime.UtcNow.AddHours(-1), "D-1", "Arrived");
        db.OutboundOrders.Add(origOrder);
        db.Packages.Add(origPkg);
        db.ReturnAuthorizations.Add(retAuth);
        db.ReturnShipments.Add(retShip);
        db.ReturnInboundHandoffs.Add(handoff);

        var session = new ReturnReceivingSession
        {
            ClientId = clientA,
            FacilityId = facId,
            ReturnInboundHandoffId = handoff.Id,
            SessionNumber = "RRS-F02A-001",
            Status = ReturnReceivingSessionStatus.Inspected,
            IsInventoryPosted = false
        };
        var line = new ReturnReceivingLine
        {
            ReturnReceivingSessionId = session.Id,
            ItemId = item.Id,
            Sku = item.Sku,
            ExpectedQuantity = 4m,
            ReceivedQuantity = 4m
        };
        line.AssignDisposition(4m, 0m, 0m, ReturnDisposition.RestockToAvailable, null, "Good", DateTime.UtcNow);
        session.Lines.Add(line);
        db.ReturnReceivingSessions.Add(session);
        await db.SaveChangesAsync();

        var svc = new ReturnReceivingService(db, admin, admin, NullLogger<ReturnReceivingService>.Instance);

        // 1. No eligible receiving/staging fallback exists (only Aisle & Client-B bin exist) -> MUST reject!
        await Assert.ThrowsAsync<DomainException>(() => svc.PostReturnInventoryAsync(new PostReturnInventoryRequest
        {
            SessionId = session.Id,
            OperatorUserId = admin.UserId!.Value
        }));

        // 2. Explicit structural Aisle destination -> MUST reject!
        await Assert.ThrowsAsync<ValidationException>(() => svc.PostReturnInventoryAsync(new PostReturnInventoryRequest
        {
            SessionId = session.Id,
            OperatorUserId = admin.UserId!.Value,
            DefaultLocationId = aisle.Id
        }));

        // 3. Explicit cross-facility destination -> MUST reject!
        await Assert.ThrowsAsync<ValidationException>(() => svc.PostReturnInventoryAsync(new PostReturnInventoryRequest
        {
            SessionId = session.Id,
            OperatorUserId = admin.UserId!.Value,
            DefaultLocationId = otherFacBin.Id
        }));

        // 4. Explicit Client-B dedicated zone destination for Client-A return -> MUST reject!
        await Assert.ThrowsAsync<ValidationException>(() => svc.PostReturnInventoryAsync(new PostReturnInventoryRequest
        {
            SessionId = session.Id,
            OperatorUserId = admin.UserId!.Value,
            DefaultLocationId = binClientB.Id
        }));

        // Verify zero stock or ledger rows persisted after all rejections
        Assert.Equal(0, await db.StockOnHand.CountAsync());
        Assert.Equal(0, await db.InventoryTransactions.CountAsync());
        Assert.False((await db.ReturnReceivingSessions.AsNoTracking().FirstAsync(s => s.Id == session.Id)).IsInventoryPosted);

        // 5. Create valid receiving staging lane and post via safe fallback -> MUST succeed!
        var validStg = await createLoc.Handle(new CreateLocationCommand
        {
            FacilityId = facId,
            LocationType = LocationType.StagingLane,
            LocationCode = "RET-STG-01",
            Barcode = "BC-RET-STG-01",
            IsReceiving = true
        }, CancellationToken.None);

        var result = await svc.PostReturnInventoryAsync(new PostReturnInventoryRequest
        {
            SessionId = session.Id,
            OperatorUserId = admin.UserId!.Value
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(4m, result.TotalAcceptedQuantity);
        var postedStock = await db.StockOnHand.AsNoTracking().SingleAsync(s => s.LocationId == validStg.Id);
        Assert.Equal(4m, postedStock.QtyAvailable);
    }

    [Fact]
    public async Task F02B_MoveLocation_EnforcesZoneDedicationParityForStockHuAndProfile_PreservingSubtree()
    {
        var (db, admin, facId, _, clientA, clientB) = await SeedBaseAsync();
        await using var _ = db;
        var auth = new WarehouseMasterAuthorization(db, admin);
        var createLoc = new CreateLocationCommandHandler(db, auth);
        var moveLoc = new MoveLocationCommandHandler(db, auth);

        var zoneB = await new CreateWarehouseZoneCommandHandler(db, auth).Handle(new CreateWarehouseZoneCommand
        {
            FacilityId = facId,
            ZoneCode = "ZN-MV-B",
            Name = "Dedicated B",
            DedicatedClientId = clientB
        }, CancellationToken.None);

        var binWithStock = await createLoc.Handle(new CreateLocationCommand { FacilityId = facId, LocationType = LocationType.Bin, LocationCode = "MV-STK", Barcode = "BC-MV-STK" }, CancellationToken.None);
        var binWithHu = await createLoc.Handle(new CreateLocationCommand { FacilityId = facId, LocationType = LocationType.Bin, LocationCode = "MV-HU", Barcode = "BC-MV-HU" }, CancellationToken.None);
        var binWithProfile = await createLoc.Handle(new CreateLocationCommand { FacilityId = facId, LocationType = LocationType.Bin, LocationCode = "MV-PR", Barcode = "BC-MV-PR" }, CancellationToken.None);

        var itemA = new Item { ClientId = clientA, Sku = "SKU-MV-A", Description = "Item A", WeightKg = 1m };
        db.Items.Add(itemA);
        db.StockOnHand.Add(new StockOnHand { ClientId = clientA, FacilityId = facId, ItemId = itemA.Id, LocationId = binWithStock.Id, QtyOnHold = 2m });
        db.HandlingUnits.Add(new HandlingUnit { ClientId = clientA, FacilityId = facId, LpnCode = "LPN-MV-A", CurrentLocationId = binWithHu.Id, Status = HandlingUnitStatus.Damaged });
        await db.SaveChangesAsync();

        await new UpsertLocationStorageProfileCommandHandler(db, auth).Handle(new UpsertLocationStorageProfileCommand
        {
            LocationId = binWithProfile.Id,
            DedicatedClientId = clientA
        }, CancellationToken.None);

        await Assert.ThrowsAsync<DomainException>(() => moveLoc.Handle(new MoveLocationCommand(binWithStock.Id, null, zoneB.Id), CancellationToken.None));
        await Assert.ThrowsAsync<DomainException>(() => moveLoc.Handle(new MoveLocationCommand(binWithHu.Id, null, zoneB.Id), CancellationToken.None));
        await Assert.ThrowsAsync<DomainException>(() => moveLoc.Handle(new MoveLocationCommand(binWithProfile.Id, null, zoneB.Id), CancellationToken.None));

        // Verify none of the bins changed ZoneId
        Assert.Null((await db.Locations.AsNoTracking().FirstAsync(l => l.Id == binWithStock.Id)).ZoneId);
        Assert.Null((await db.Locations.AsNoTracking().FirstAsync(l => l.Id == binWithHu.Id)).ZoneId);
        Assert.Null((await db.Locations.AsNoTracking().FirstAsync(l => l.Id == binWithProfile.Id)).ZoneId);
    }

    [Fact]
    public async Task F02C_ConfirmCrossDockTask_RejectsStructuralAndDedicatedConflict_PreservesValidStaging()
    {
        var (db, admin, facId, _, clientA, clientB) = await SeedBaseAsync();
        await using var _ = db;
        var auth = new WarehouseMasterAuthorization(db, admin);
        var createLoc = new CreateLocationCommandHandler(db, auth);

        var zoneB = await new CreateWarehouseZoneCommandHandler(db, auth).Handle(new CreateWarehouseZoneCommand
        {
            FacilityId = facId,
            ZoneCode = "ZN-CD-B",
            Name = "Client B Zone",
            DedicatedClientId = clientB
        }, CancellationToken.None);

        var srcStg = await createLoc.Handle(new CreateLocationCommand { FacilityId = facId, LocationType = LocationType.StagingLane, LocationCode = "CD-SRC", Barcode = "BC-CD-SRC", IsReceiving = true }, CancellationToken.None);
        var destStg = await createLoc.Handle(new CreateLocationCommand { FacilityId = facId, LocationType = LocationType.StagingLane, LocationCode = "CD-DST", Barcode = "BC-CD-DST" }, CancellationToken.None);
        var badAisle = await createLoc.Handle(new CreateLocationCommand { FacilityId = facId, LocationType = LocationType.Aisle, LocationCode = "CD-AISLE", Barcode = "BC-CD-AISLE" }, CancellationToken.None);
        var badDedStg = await createLoc.Handle(new CreateLocationCommand { FacilityId = facId, ZoneId = zoneB.Id, LocationType = LocationType.StagingLane, LocationCode = "CD-DST-B", Barcode = "BC-CD-DST-B" }, CancellationToken.None);

        var item = new Item { ClientId = clientA, Sku = "SKU-CD", Description = "CD Item", WeightKg = 1m };
        db.Items.Add(item);
        db.StockOnHand.Add(new StockOnHand { ClientId = clientA, FacilityId = facId, ItemId = item.Id, LocationId = srcStg.Id, QtyAllocated = 5m });

        var plan = new CrossDockPlan { ClientId = clientA, FacilityId = facId, PlanNumber = "CDP-1", TotalPlannedQuantity = 5m, Status = CrossDockPlanStatus.InProgress };
        var planLine = new CrossDockPlanLine { CrossDockPlanId = plan.Id, ItemId = item.Id, PlannedQuantity = 5m, Status = CrossDockLineStatus.InProgress };
        var task = new CrossDockTask
        {
            CrossDockPlanId = plan.Id,
            CrossDockPlanLineId = planLine.Id,
            ClientId = clientA,
            FacilityId = facId,
            ItemId = item.Id,
            TaskNumber = "CDT-1",
            Lpn = "LPN-CD-1",
            SourceLocationId = srcStg.Id,
            DestinationLocationId = destStg.Id,
            Quantity = 5m,
            Status = CrossDockTaskStatus.Pending
        };
        db.CrossDockPlans.Add(plan);
        db.CrossDockPlanLines.Add(planLine);
        db.CrossDockTasks.Add(task);
        await db.SaveChangesAsync();

        var handler = new ConfirmCrossDockTaskCommandHandler(db, admin);

        // Override to structural Aisle -> MUST reject!
        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(new ConfirmCrossDockTaskCommand
        {
            TaskId = task.Id,
            ScannedLpn = "LPN-CD-1",
            ScannedDestinationBarcode = "BC-CD-AISLE",
            AllowOverride = true
        }, CancellationToken.None));

        // Override to Client-B dedicated zone staging lane -> MUST reject!
        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(new ConfirmCrossDockTaskCommand
        {
            TaskId = task.Id,
            ScannedLpn = "LPN-CD-1",
            ScannedDestinationBarcode = "BC-CD-DST-B",
            AllowOverride = true
        }, CancellationToken.None));

        // Valid planned StagingLane -> MUST succeed!
        var ok = await handler.Handle(new ConfirmCrossDockTaskCommand
        {
            TaskId = task.Id,
            ScannedLpn = "LPN-CD-1",
            ScannedDestinationBarcode = "BC-CD-DST",
            AllowOverride = false
        }, CancellationToken.None);
        Assert.Equal(CrossDockTaskStatus.Completed, ok.Status);
        Assert.Equal(destStg.Id, ok.ActualDestinationLocationId);
    }

    [Fact]
    public async Task F03B_PackingExecutionService_EnforcesActiveSameFacilityEligibleStation()
    {
        var (db, admin, facId, facOtherId, clientA, _) = await SeedBaseAsync();
        await using var _ = db;
        var auth = new WarehouseMasterAuthorization(db, admin);
        var createLoc = new CreateLocationCommandHandler(db, auth);

        var goodStation = await createLoc.Handle(new CreateLocationCommand { FacilityId = facId, LocationType = LocationType.PackingStation, LocationCode = "PK-GOOD", Barcode = "BC-PK-GOOD" }, CancellationToken.None);
        var otherFacStation = await createLoc.Handle(new CreateLocationCommand { FacilityId = facOtherId, LocationType = LocationType.PackingStation, LocationCode = "PK-OTHER", Barcode = "BC-PK-OTHER" }, CancellationToken.None);

        var order = new OutboundOrder { ClientId = clientA, FacilityId = facId, OrderNumber = "ORD-PK-1", Status = OutboundOrderStatus.Allocated };
        db.OutboundOrders.Add(order);
        await db.SaveChangesAsync();

        var svc = new PackingExecutionService(db, NullLogger<PackingExecutionService>.Instance);

        // Cross-facility station -> rejected
        await Assert.ThrowsAsync<ValidationException>(() => svc.StartOrResumeSessionAsync(new StartPackingSessionRequest
        {
            FacilityId = facId,
            PackingStationLocationId = otherFacStation.Id,
            OutboundOrderId = order.Id,
            OperatorUserId = admin.UserId!.Value
        }));

        // Deactivate station -> rejected
        await new UpdateLocationStatusCommandHandler(db, auth).Handle(new UpdateLocationStatusCommand(goodStation.Id, LocationStatus.Inactive), CancellationToken.None);
        await Assert.ThrowsAsync<InvalidDomainStateException>(() => svc.StartOrResumeSessionAsync(new StartPackingSessionRequest
        {
            FacilityId = facId,
            PackingStationLocationId = goodStation.Id,
            OutboundOrderId = order.Id,
            OperatorUserId = admin.UserId!.Value
        }));

        // Reactivate station -> succeeds
        await new UpdateLocationStatusCommandHandler(db, auth).Handle(new UpdateLocationStatusCommand(goodStation.Id, LocationStatus.Active), CancellationToken.None);
        var res = await svc.StartOrResumeSessionAsync(new StartPackingSessionRequest
        {
            FacilityId = facId,
            PackingStationLocationId = goodStation.Id,
            OutboundOrderId = order.Id,
            OperatorUserId = admin.UserId!.Value
        });
        Assert.True(res.IsSuccess);
        Assert.Equal(PackingSessionStatus.Active, res.Status);
    }
}
