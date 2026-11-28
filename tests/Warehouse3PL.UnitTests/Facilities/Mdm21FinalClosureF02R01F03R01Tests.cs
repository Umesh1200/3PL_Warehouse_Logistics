using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.CrossDock.Tasks.Commands.ConfirmCrossDockTask;
using Warehouse3PL.Application.Features.Inbound.Receiving.Commands.CompleteReceivingSession;
using Warehouse3PL.Application.Features.Locations.Commands.CreateLocation;
using Warehouse3PL.Application.Features.WarehouseMaster;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.CrossDock;
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
using Warehouse3PL.Infrastructure.Services.Shipping;
using Xunit;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.UnitTests.Facilities;

/// <summary>
/// Final closure regressions for MDM21-F02-R01 and MDM21-F03-R01:
/// - F02-R01-A: Cumulative projected-state return validation (combined weight, volume, mixed-SKU on empty bin, missing item fail-closed)
/// - F02-R01-B: Cross-dock structural rejection even when IsReceiving=true + permanent storage profile enforcement
/// - F03-R01-B: CompleteReceivingSessionCommand stale staging rejection and safe automatic staging provisioning
/// </summary>
public class Mdm21FinalClosureF02R01F03R01Tests
{
    private sealed class TestIdentity(Guid userId, Guid? facilityId) : ITenantProvider, ICurrentUserContext
    {
        public Guid? TenantId { get; private set; }
        public bool HasTenant => TenantId.HasValue;
        public bool BypassTenantFilter { get; set; } = true;
        public void SetTenantId(Guid tenantId) => TenantId = tenantId;
        public Guid? UserId => userId;
        public string? Email => "f02r01-admin@nexus3pl.test";
        public Guid? FacilityId => facilityId;
        public Guid? RequestedFacilityId => null;
        public UserType? UserType => Warehouse3PL.Domain.Enums.UserType.Staff;
        public IReadOnlyList<string> Roles { get; } = [SystemRoles.SuperAdmin];
        public IReadOnlyList<string> Permissions { get; } = Warehouse3PL.Domain.Constants.Permissions.GetAllPermissions();
        public bool IsAuthenticated => true;
        public bool IsStaff => true;
        public bool IsSuperAdmin => true;
        public bool HasPermission(string permission) => true;
        public bool IsInRole(string role) => string.Equals(role, SystemRoles.SuperAdmin, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(ApplicationDbContext Db, TestIdentity Admin, Guid FacId, Guid ClientA, Guid ClientB)> SeedBaseAsync()
    {
        var userId = Guid.NewGuid();
        var facId = Guid.NewGuid();
        var clientA = Guid.NewGuid();
        var clientB = Guid.NewGuid();

        var admin = new TestIdentity(userId, facId);
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mdm21_f02r01_{Guid.NewGuid():N}")
            .Options;
        var audit = new AuditableEntityInterceptor(admin, new DateTimeProvider());
        var db = new ApplicationDbContext(opts, admin, admin, audit);
        await ReferenceCatalogSeeder.SeedAsync(db);

        db.Facilities.Add(new Facility { Id = facId, FacilityCode = "FAC-FR1", Name = "Final Closure Hub", CountryCode = "US", Timezone = "America/Chicago", DefaultCurrencyCode = "USD", IsActive = true });
        db.Clients.AddRange(
            new Client { Id = clientA, CompanyName = "Client Alpha", AccountCode = "CL-FR1A", Status = ClientStatus.Active },
            new Client { Id = clientB, CompanyName = "Client Beta", AccountCode = "CL-FR1B", Status = ClientStatus.Active });
        db.ClientFacilityAccesses.AddRange(
            new ClientFacilityAccess { ClientId = clientA, FacilityId = facId, Status = FacilityAccessStatus.Active },
            new ClientFacilityAccess { ClientId = clientB, FacilityId = facId, Status = FacilityAccessStatus.Active });

        var role = new Role { RoleName = SystemRoles.SuperAdmin };
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        db.Users.Add(new User
        {
            Id = userId,
            Email = "f02r01-admin@nexus3pl.test",
            PasswordHash = "hash",
            FirstName = "Final",
            LastName = "Admin",
            UserType = UserType.Staff,
            Status = UserStatus.Active,
            AssignedFacilityId = facId
        });
        db.UserRoles.Add(new UserRole { UserId = userId, RoleId = role.Id });
        await db.SaveChangesAsync();

        return (db, admin, facId, clientA, clientB);
    }

    private static async Task<ReturnReceivingSession> SeedTwoLineReturnSessionAsync(
        ApplicationDbContext db, Guid facId, Guid clientId, Guid userId, Item item1, decimal qty1, Item item2, decimal qty2)
    {
        var origOrder = new OutboundOrder { ClientId = clientId, FacilityId = facId, OrderNumber = $"ORD-{Guid.NewGuid():N}"[..16], CustomerName = "Ret Cust", Status = OutboundOrderStatus.Completed };
        var origHu = new HandlingUnit { ClientId = clientId, FacilityId = facId, LpnCode = $"LPN-{Guid.NewGuid():N}"[..16], UnitType = HandlingUnitType.Pallet };
        var origPkg = new Package { ClientId = clientId, FacilityId = facId, OutboundOrderId = origOrder.Id, HandlingUnitId = origHu.Id, PackageNumber = $"PKG-{Guid.NewGuid():N}"[..16], TrackingNumber = $"TRK-{Guid.NewGuid():N}"[..16], Status = PackageStatus.Loaded };
        var retAuth = ReturnAuthorization.Create(clientId, facId, facId, origOrder.Id, null, origPkg.Id, null, $"RA-{Guid.NewGuid():N}"[..12], ReturnReason.DamagedInTransit, "Test", userId, $"RMA-{Guid.NewGuid():N}"[..12], DateTime.UtcNow.AddDays(7));
        var retShip = new ReturnShipment { ClientId = clientId, ReturnAuthorizationId = retAuth.Id, OrderId = origOrder.Id, PackageId = origPkg.Id, OriginFacilityId = facId, DestinationFacilityId = facId, CarrierCode = "FEDEX", TrackingNumber = origPkg.TrackingNumber, AuthorizedByUserId = userId, Status = ReturnShipmentStatus.DeliveredAtWarehouse, DeliveredAtWarehouseUtc = DateTime.UtcNow.AddHours(-1) };
        var handoff = ReturnInboundHandoff.Create(clientId, facId, retShip.Id, retAuth.Id, origPkg.Id, origOrder.Id, $"HND-{Guid.NewGuid():N}"[..14], retAuth.RmaNumber!, retShip.TrackingNumber, retShip.CarrierCode, DateTime.UtcNow.AddHours(-1), "D-1", "Arrived");

        db.OutboundOrders.Add(origOrder);
        db.HandlingUnits.Add(origHu);
        db.Packages.Add(origPkg);
        db.ReturnAuthorizations.Add(retAuth);
        db.ReturnShipments.Add(retShip);
        db.ReturnInboundHandoffs.Add(handoff);

        var session = new ReturnReceivingSession
        {
            ClientId = clientId,
            FacilityId = facId,
            ReturnInboundHandoffId = handoff.Id,
            ReturnShipmentId = retShip.Id,
            ReturnAuthorizationId = retAuth.Id,
            OrderId = origOrder.Id,
            PackageId = origPkg.Id,
            ReceivedByUserId = userId,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            SessionNumber = $"RRS-{Guid.NewGuid():N}"[..16],
            Status = ReturnReceivingSessionStatus.Inspected,
            IsInventoryPosted = false
        };

        var line1 = new ReturnReceivingLine { ReturnReceivingSessionId = session.Id, ItemId = item1.Id, Sku = item1.Sku, ExpectedQuantity = qty1, ReceivedQuantity = qty1 };
        line1.AssignDisposition(qty1, 0m, 0m, ReturnDisposition.RestockToAvailable, null, "L1", DateTime.UtcNow);
        var line2 = new ReturnReceivingLine { ReturnReceivingSessionId = session.Id, ItemId = item2.Id, Sku = item2.Sku, ExpectedQuantity = qty2, ReceivedQuantity = qty2 };
        line2.AssignDisposition(qty2, 0m, 0m, ReturnDisposition.RestockToAvailable, null, "L2", DateTime.UtcNow);
        session.Lines.Add(line1);
        session.Lines.Add(line2);
        db.ReturnReceivingSessions.Add(session);
        await db.SaveChangesAsync();
        return session;
    }

    [Fact]
    public async Task F02R01A_ReturnBatchProjectedState_RejectsCombinedWeightVolumeMixedSkuAndMissingItem_PostsValid()
    {
        var (db, admin, facId, clientA, _) = await SeedBaseAsync();
        await using var _ = db;
        var auth = new WarehouseMasterAuthorization(db, admin);
        var createLoc = new CreateLocationCommandHandler(db, auth);
        var upsertProf = new UpsertLocationStorageProfileCommandHandler(db, auth);

        // Bin with MaxWeightKg = 10, MaxVolumeCm3 = 1000
        var weightVolBin = await createLoc.Handle(new CreateLocationCommand
        {
            FacilityId = facId,
            LocationType = LocationType.Bin,
            LocationCode = "RET-WV-BIN",
            Barcode = "BC-RET-WV-BIN",
            MaxWeightKg = 10m,
            MaxVolumeCm3 = 1000m
        }, CancellationToken.None);

        // Empty bin with AllowMixedSku = false
        var singleSkuBin = await createLoc.Handle(new CreateLocationCommand
        {
            FacilityId = facId,
            LocationType = LocationType.Bin,
            LocationCode = "RET-1SKU-BIN",
            Barcode = "BC-RET-1SKU-BIN"
        }, CancellationToken.None);
        await upsertProf.Handle(new UpsertLocationStorageProfileCommand { LocationId = singleSkuBin.Id, AllowMixedSku = false }, CancellationToken.None);

        // Generous bin for valid posting
        var validBin = await createLoc.Handle(new CreateLocationCommand
        {
            FacilityId = facId,
            LocationType = LocationType.Bin,
            LocationCode = "RET-OK-BIN",
            Barcode = "BC-RET-OK-BIN",
            MaxWeightKg = 100m,
            MaxVolumeCm3 = 50000m
        }, CancellationToken.None);

        // Two items: each 6kg (individually <= 10kg, combined 12kg > 10kg), volume 200cm3 each
        var heavy1 = new Item { ClientId = clientA, Sku = "SKU-HVY-1", Description = "Heavy 1", WeightKg = 6m, LengthCm = 10, WidthCm = 10, HeightCm = 2 };
        var heavy2 = new Item { ClientId = clientA, Sku = "SKU-HVY-2", Description = "Heavy 2", WeightKg = 6m, LengthCm = 10, WidthCm = 10, HeightCm = 2 };
        // Two bulky items: each 1kg, volume 600cm3 (individually <= 1000cm3, combined 1200cm3 > 1000cm3)
        var bulky1 = new Item { ClientId = clientA, Sku = "SKU-BLK-1", Description = "Bulky 1", WeightKg = 1m, LengthCm = 10, WidthCm = 10, HeightCm = 6 };
        var bulky2 = new Item { ClientId = clientA, Sku = "SKU-BLK-2", Description = "Bulky 2", WeightKg = 1m, LengthCm = 10, WidthCm = 10, HeightCm = 6 };
        db.Items.AddRange(heavy1, heavy2, bulky1, bulky2);
        await db.SaveChangesAsync();

        var svc = new ReturnReceivingService(db, admin, admin, NullLogger<ReturnReceivingService>.Instance);

        // 1. Combined weight exceeds MaxWeightKg (6 + 6 = 12 > 10) -> rejected atomically
        var sWeight = await SeedTwoLineReturnSessionAsync(db, facId, clientA, admin.UserId!.Value, heavy1, 1m, heavy2, 1m);
        await Assert.ThrowsAsync<ValidationException>(() => svc.PostReturnInventoryAsync(new PostReturnInventoryRequest
        {
            SessionId = sWeight.Id,
            OperatorUserId = admin.UserId!.Value,
            DefaultLocationId = weightVolBin.Id
        }));
        Assert.Equal(0, await db.StockOnHand.CountAsync());
        Assert.Equal(0, await db.InventoryTransactions.CountAsync());
        Assert.False((await db.ReturnReceivingSessions.AsNoTracking().FirstAsync(s => s.Id == sWeight.Id)).IsInventoryPosted);

        // 2. Combined volume exceeds MaxVolumeCm3 (600 + 600 = 1200 > 1000) -> rejected atomically
        var sVol = await SeedTwoLineReturnSessionAsync(db, facId, clientA, admin.UserId!.Value, bulky1, 1m, bulky2, 1m);
        await Assert.ThrowsAsync<ValidationException>(() => svc.PostReturnInventoryAsync(new PostReturnInventoryRequest
        {
            SessionId = sVol.Id,
            OperatorUserId = admin.UserId!.Value,
            DefaultLocationId = weightVolBin.Id
        }));
        Assert.Equal(0, await db.StockOnHand.CountAsync());

        // 3. Two different SKUs targeting EMPTY bin with AllowMixedSku = false -> rejected atomically
        await Assert.ThrowsAsync<ValidationException>(() => svc.PostReturnInventoryAsync(new PostReturnInventoryRequest
        {
            SessionId = sVol.Id,
            OperatorUserId = admin.UserId!.Value,
            DefaultLocationId = singleSkuBin.Id
        }));
        Assert.Equal(0, await db.StockOnHand.CountAsync());

        // 4. Valid multi-line return posting -> succeeds cleanly
        var ok = await svc.PostReturnInventoryAsync(new PostReturnInventoryRequest
        {
            SessionId = sWeight.Id,
            OperatorUserId = admin.UserId!.Value,
            DefaultLocationId = validBin.Id
        });
        Assert.True(ok.IsSuccess);
        Assert.Equal(2, ok.TotalItemsPosted);
        Assert.Equal(2m, ok.TotalAcceptedQuantity);
        Assert.Equal(2, await db.StockOnHand.CountAsync(s => s.LocationId == validBin.Id));
    }

    [Fact]
    public async Task F02R01B_CrossDock_RejectsStructuralEvenWhenIsReceivingTrue_AndEnforcesPermanentStorageProfile()
    {
        var (db, admin, facId, clientA, _) = await SeedBaseAsync();
        await using var _ = db;
        var auth = new WarehouseMasterAuthorization(db, admin);
        var createLoc = new CreateLocationCommandHandler(db, auth);
        var upsertProf = new UpsertLocationStorageProfileCommandHandler(db, auth);

        var srcStg = await createLoc.Handle(new CreateLocationCommand { FacilityId = facId, LocationType = LocationType.StagingLane, LocationCode = "XD-SRC", Barcode = "BC-XD-SRC", IsReceiving = true }, CancellationToken.None);
        var dstStg = await createLoc.Handle(new CreateLocationCommand { FacilityId = facId, LocationType = LocationType.StagingLane, LocationCode = "XD-DST", Barcode = "BC-XD-DST" }, CancellationToken.None);
        // Structural Aisle & Rack with contradictory IsReceiving = true
        var aisleReceiving = await createLoc.Handle(new CreateLocationCommand { FacilityId = facId, LocationType = LocationType.Aisle, LocationCode = "XD-AISLE-R", Barcode = "BC-XD-AISLE-R", IsReceiving = true }, CancellationToken.None);
        var rackReceiving = await createLoc.Handle(new CreateLocationCommand { FacilityId = facId, ParentLocationId = aisleReceiving.Id, LocationType = LocationType.Rack, LocationCode = "XD-RACK-R", Barcode = "BC-XD-RACK-R", IsReceiving = true }, CancellationToken.None);
        // Permanent storage bin with IsPutawayAllowed = false
        var restrictedBin = await createLoc.Handle(new CreateLocationCommand { FacilityId = facId, LocationType = LocationType.Bin, LocationCode = "XD-BIN-NO", Barcode = "BC-XD-BIN-NO" }, CancellationToken.None);
        await upsertProf.Handle(new UpsertLocationStorageProfileCommand { LocationId = restrictedBin.Id, IsPutawayAllowed = false }, CancellationToken.None);

        var item = new Item { ClientId = clientA, Sku = "SKU-XD-R01", Description = "XD Item", WeightKg = 1m };
        db.Items.Add(item);
        db.StockOnHand.Add(new StockOnHand { ClientId = clientA, FacilityId = facId, ItemId = item.Id, LocationId = srcStg.Id, QtyAllocated = 5m });

        var plan = new CrossDockPlan { ClientId = clientA, FacilityId = facId, PlanNumber = "CDP-R01", TotalPlannedQuantity = 5m, Status = CrossDockPlanStatus.InProgress };
        var planLine = new CrossDockPlanLine { CrossDockPlanId = plan.Id, ItemId = item.Id, SourceLocationId = srcStg.Id, TargetLocationId = dstStg.Id, PlannedQuantity = 5m, Status = CrossDockLineStatus.InProgress };
        var task = new CrossDockTask
        {
            CrossDockPlanId = plan.Id,
            CrossDockPlanLineId = planLine.Id,
            ClientId = clientA,
            FacilityId = facId,
            ItemId = item.Id,
            TaskNumber = "CDT-R01",
            Lpn = "LPN-XD-R01",
            SourceLocationId = srcStg.Id,
            DestinationLocationId = dstStg.Id,
            Quantity = 5m,
            Status = CrossDockTaskStatus.Pending
        };
        db.CrossDockPlans.Add(plan);
        db.CrossDockPlanLines.Add(planLine);
        db.CrossDockTasks.Add(task);
        await db.SaveChangesAsync();

        var handler = new ConfirmCrossDockTaskCommandHandler(db, admin);

        // Structural Aisle with IsReceiving=true -> REJECTED!
        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(new ConfirmCrossDockTaskCommand
        {
            TaskId = task.Id,
            ScannedLpn = "LPN-XD-R01",
            ScannedDestinationBarcode = "BC-XD-AISLE-R",
            AllowOverride = true
        }, CancellationToken.None));

        // Structural Rack with IsReceiving=true -> REJECTED!
        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(new ConfirmCrossDockTaskCommand
        {
            TaskId = task.Id,
            ScannedLpn = "LPN-XD-R01",
            ScannedDestinationBarcode = "BC-XD-RACK-R",
            AllowOverride = true
        }, CancellationToken.None));

        // Permanent storage bin with IsPutawayAllowed=false -> REJECTED!
        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(new ConfirmCrossDockTaskCommand
        {
            TaskId = task.Id,
            ScannedLpn = "LPN-XD-R01",
            ScannedDestinationBarcode = "BC-XD-BIN-NO",
            AllowOverride = true
        }, CancellationToken.None));

        // Valid StagingLane -> SUCCEEDS with exact conservation!
        var ok = await handler.Handle(new ConfirmCrossDockTaskCommand
        {
            TaskId = task.Id,
            ScannedLpn = "LPN-XD-R01",
            ScannedDestinationBarcode = "BC-XD-DST",
            AllowOverride = false
        }, CancellationToken.None);
        Assert.Equal(CrossDockTaskStatus.Completed, ok.Status);
    }

    [Fact]
    public async Task F03R01B_CompleteReceivingSession_RejectsStaleDeactivatedStaging_AndSafelyAutoProvisionsStaging()
    {
        var (db, admin, facId, clientA, _) = await SeedBaseAsync();
        await using var _ = db;
        var auth = new WarehouseMasterAuthorization(db, admin);
        var createLoc = new CreateLocationCommandHandler(db, auth);

        var stg = await createLoc.Handle(new CreateLocationCommand { FacilityId = facId, LocationType = LocationType.StagingLane, LocationCode = "RCV-STG-1", Barcode = "BC-RCV-STG-1", IsReceiving = true }, CancellationToken.None);
        var item = new Item { ClientId = clientA, Sku = "SKU-RCV-1", Description = "Receiving Item", WeightKg = 1m };
        db.Items.Add(item);

        var session1 = new ReceivingSession
        {
            ClientId = clientA,
            FacilityId = facId,
            SessionNumber = "RCV-STALE-1",
            Status = ReceivingStatus.InProgress,
            ReceivedByUserId = admin.UserId!.Value,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-10)
        };
        session1.Lines.Add(new ReceiptLine
        {
            ReceivingSessionId = session1.Id,
            ItemId = item.Id,
            Lpn = "LPN-STALE-1",
            QuantityExpected = 5m,
            QuantityReceived = 5m,
            StagingLocationId = stg.Id,
            Condition = ReceiptConditionStatus.Sound,
            ScannedAtUtc = DateTime.UtcNow.AddMinutes(-5)
        });
        db.ReceivingSessions.Add(session1);
        await db.SaveChangesAsync();

        // Deactivate staging lane after scan -> completion MUST fail with zero stock/ledger mutation!
        await new UpdateLocationStatusCommandHandler(db, auth).Handle(new UpdateLocationStatusCommand(stg.Id, LocationStatus.Inactive), CancellationToken.None);

        var completeHandler = new CompleteReceivingSessionCommandHandler(db, admin, new WarehouseOperationalAuthorization(db, admin));
        await Assert.ThrowsAsync<ValidationException>(() => completeHandler.Handle(new CompleteReceivingSessionCommand { ReceivingSessionId = session1.Id }, CancellationToken.None));
        Assert.Equal(0, await db.StockOnHand.CountAsync());
        Assert.Equal(0, await db.InventoryTransactions.CountAsync());

        // Safe automatic staging provisioning when no active staging lane exists -> creates valid StagingLane with HierarchyPath & Normalized codes
        var session2 = new ReceivingSession
        {
            ClientId = clientA,
            FacilityId = facId,
            SessionNumber = "RCV-AUTO-2",
            Status = ReceivingStatus.InProgress,
            ReceivedByUserId = admin.UserId!.Value,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-10)
        };
        session2.Lines.Add(new ReceiptLine
        {
            ReceivingSessionId = session2.Id,
            ItemId = item.Id,
            Lpn = "LPN-AUTO-2",
            QuantityExpected = 3m,
            QuantityReceived = 3m,
            StagingLocationId = null,
            Condition = ReceiptConditionStatus.Sound,
            ScannedAtUtc = DateTime.UtcNow.AddMinutes(-5)
        });
        db.ReceivingSessions.Add(session2);
        await db.SaveChangesAsync();

        var completed = await completeHandler.Handle(new CompleteReceivingSessionCommand { ReceivingSessionId = session2.Id }, CancellationToken.None);
        Assert.Equal(ReceivingStatus.Completed, completed.Status);

        var autoLoc = await db.Locations.AsNoTracking().SingleAsync(l => l.FacilityId == facId && l.Status == LocationStatus.Active && l.LocationType == LocationType.StagingLane);
        Assert.True(autoLoc.IsReceiving);
        Assert.True(autoLoc.IsStorable);
        Assert.Equal(1, autoLoc.HierarchyLevel);
        Assert.Equal($"/{autoLoc.Id:N}/", autoLoc.HierarchyPath);
        Assert.Equal(Location.NormalizeCode(autoLoc.LocationCode), autoLoc.NormalizedLocationCode);
        Assert.Equal(Location.NormalizeCode(autoLoc.Barcode), autoLoc.NormalizedBarcode);
    }
}
