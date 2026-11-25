using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.Putaway.Tasks.Commands.ConfirmPutawayTask;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Services;
using Warehouse3PL.Infrastructure.Services.Allocation;

internal static class Mdm04PostgresCheck
{
    public static async Task RunAsync(
        Func<ApplicationDbContext> createDb,
        HttpClient client,
        Guid client1Id,
        Guid existingFacilityId,
        Guid foreignFacilityId,
        Func<HttpMethod, string, Guid, Guid?, string, object?, HttpRequestMessage> staffReq,
        Guid staffId,
        Action<List<Claim>?> setCustomClaims)
    {
        var facilityId = Guid.NewGuid();
        var itemAllocId = Guid.NewGuid();
        var itemPutawayId = Guid.NewGuid();
        var binLooseId = Guid.NewGuid();
        var binHuId = Guid.NewGuid();
        var binSoonExpId = Guid.NewGuid();
        var binOldExpId = Guid.NewGuid();
        var stagingLocId = Guid.NewGuid();
        var binOccupiedId = Guid.NewGuid();
        var binEmptyId = Guid.NewGuid();
        var binTightCapId = Guid.NewGuid();
        var coldZoneId = Guid.NewGuid();
        var coldNullZoneId = Guid.NewGuid();
        var freezerZoneId = Guid.NewGuid();
        var tempCtrlZoneId = Guid.NewGuid();
        var binColdId = Guid.NewGuid();
        var binColdNullId = Guid.NewGuid();
        var binFreezerId = Guid.NewGuid();
        var binTempCtrlId = Guid.NewGuid();
        var foreignClientBId = Guid.NewGuid();
        var suspendedClientCId = Guid.NewGuid();

        var huId = Guid.NewGuid();
        var r04HuAId = Guid.NewGuid();
        var r04HuBId = Guid.NewGuid();
        var itemFrozenId = Guid.NewGuid();
        var itemChilledId = Guid.NewGuid();
        var itemTempCtrlId = Guid.NewGuid();
        var itemR04Id = Guid.NewGuid();
        var binR04HuAId = Guid.NewGuid();
        var binR04HuBId = Guid.NewGuid();
        var binR04LooseId = Guid.NewGuid();
        var ordR04Id = Guid.NewGuid();
        var taskFrzInChillId = Guid.NewGuid();
        var taskFrzInNullColdId = Guid.NewGuid();
        var taskFrzOkId = Guid.NewGuid();
        var taskChlInFreezerId = Guid.NewGuid();
        var taskTcInBulkId = Guid.NewGuid();
        var taskTcInColdId = Guid.NewGuid();
        var taskTcOkId = Guid.NewGuid();
        var taskAmbOverflowBlockedId = Guid.NewGuid();
        var ordHuId = Guid.NewGuid();
        var ordOverrideId = Guid.NewGuid();
        var concOrderIds = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        var putawayTask1Id = Guid.NewGuid();
        var putawayTask2Id = Guid.NewGuid();
        var putawayRuleId = Guid.NewGuid();

        var nowUtc = DateTime.UtcNow;

        await using (var seedDb = createDb())
        {
            seedDb.Facilities.Add(new Facility
            {
                Id = facilityId,
                FacilityCode = "M4PG-" + Guid.NewGuid().ToString("N")[..5],
                Name = "MDM04 PG Policy Hub",
                AddressLine1 = "400 Policy Blvd",
                City = "Chicago",
                CountryCode = "US",
                IsActive = true
            });
            seedDb.Clients.AddRange(
                new Warehouse3PL.Domain.Entities.Security.Client { Id = foreignClientBId, CompanyName = "M4PG Foreign Client B", AccountCode = "M4FB-" + Guid.NewGuid().ToString("N")[..4], Status = ClientStatus.Active },
                new Warehouse3PL.Domain.Entities.Security.Client { Id = suspendedClientCId, CompanyName = "M4PG Suspended Client C", AccountCode = "M4SC-" + Guid.NewGuid().ToString("N")[..4], Status = ClientStatus.Active });
            seedDb.ClientFacilityAccesses.AddRange(
                new ClientFacilityAccess { ClientId = client1Id, FacilityId = facilityId, Status = FacilityAccessStatus.Active },
                new ClientFacilityAccess { ClientId = foreignClientBId, FacilityId = foreignFacilityId, Status = FacilityAccessStatus.Active },
                new ClientFacilityAccess { ClientId = suspendedClientCId, FacilityId = facilityId, Status = FacilityAccessStatus.Revoked });

            seedDb.WarehouseZones.AddRange(
                new WarehouseZone { Id = coldZoneId, FacilityId = facilityId, ZoneCode = "M4PG-COLD-" + Guid.NewGuid().ToString("N")[..4], ZoneType = WarehouseZoneType.ColdStorage, TempMinC = 2m, TempMaxC = 4m },
                new WarehouseZone { Id = coldNullZoneId, FacilityId = facilityId, ZoneCode = "M4PG-CNULL-" + Guid.NewGuid().ToString("N")[..4], ZoneType = WarehouseZoneType.ColdStorage },
                new WarehouseZone { Id = freezerZoneId, FacilityId = facilityId, ZoneCode = "M4PG-FRZ-" + Guid.NewGuid().ToString("N")[..4], ZoneType = WarehouseZoneType.ColdStorage, TempMinC = -25m, TempMaxC = -20m },
                new WarehouseZone { Id = tempCtrlZoneId, FacilityId = facilityId, ZoneCode = "M4PG-TC-" + Guid.NewGuid().ToString("N")[..4], ZoneType = WarehouseZoneType.BulkStorage, TempMinC = 15m, TempMaxC = 25m });

            seedDb.Locations.AddRange(
                new Location { Id = binLooseId, FacilityId = facilityId, LocationCode = "M4-LOOSE-" + Guid.NewGuid().ToString("N")[..4], Barcode = "M4-L", LocationType = LocationType.Bin, PickSequence = 1, IsPickable = true, IsActive = true },
                new Location { Id = binHuId, FacilityId = facilityId, LocationCode = "M4-HU-" + Guid.NewGuid().ToString("N")[..4], Barcode = "M4-H", LocationType = LocationType.Bin, PickSequence = 5, IsPickable = true, IsActive = true },
                new Location { Id = binOldExpId, FacilityId = facilityId, LocationCode = "M4-F90-" + Guid.NewGuid().ToString("N")[..4], Barcode = "M4-90", LocationType = LocationType.Bin, PickSequence = 1, IsPickable = true, IsActive = true },
                new Location { Id = binSoonExpId, FacilityId = facilityId, LocationCode = "M4-F10-" + Guid.NewGuid().ToString("N")[..4], Barcode = "M4-10", LocationType = LocationType.Bin, PickSequence = 2, IsPickable = true, IsActive = true },
                new Location { Id = stagingLocId, FacilityId = facilityId, LocationCode = "M4-STG-" + Guid.NewGuid().ToString("N")[..4], Barcode = "M4-STG", LocationType = LocationType.StagingLane, IsReceiving = true, IsActive = true },
                new Location { Id = binOccupiedId, FacilityId = facilityId, LocationCode = "M4-OCC-" + Guid.NewGuid().ToString("N")[..4], Barcode = "M4-OCC", LocationType = LocationType.Bin, MaxWeightKg = 500m, IsActive = true },
                new Location { Id = binEmptyId, FacilityId = facilityId, LocationCode = "M4-A-EMP-" + Guid.NewGuid().ToString("N")[..4], Barcode = "M4-EMP", LocationType = LocationType.Bin, MaxWeightKg = 500m, IsActive = true },
                new Location { Id = binTightCapId, FacilityId = facilityId, LocationCode = "M4-Z-CAP-" + Guid.NewGuid().ToString("N")[..4], Barcode = "M4-CAP", LocationType = LocationType.Bin, MaxWeightKg = 100m, IsActive = true },
                new Location { Id = binColdId, FacilityId = facilityId, ZoneId = coldZoneId, LocationCode = "M4-CLD-" + Guid.NewGuid().ToString("N")[..4], Barcode = "M4-CLD", LocationType = LocationType.Bin, MaxWeightKg = 500m, IsActive = true },
                new Location { Id = binColdNullId, FacilityId = facilityId, ZoneId = coldNullZoneId, LocationCode = "M4-CNL-" + Guid.NewGuid().ToString("N")[..4], Barcode = "M4-CNL", LocationType = LocationType.Bin, MaxWeightKg = 500m, IsActive = true },
                new Location { Id = binFreezerId, FacilityId = facilityId, ZoneId = freezerZoneId, LocationCode = "M4-FRZ-" + Guid.NewGuid().ToString("N")[..4], Barcode = "M4-FRZ", LocationType = LocationType.Bin, MaxWeightKg = 500m, IsActive = true },
                new Location { Id = binTempCtrlId, FacilityId = facilityId, ZoneId = tempCtrlZoneId, LocationCode = "M4-TC-" + Guid.NewGuid().ToString("N")[..4], Barcode = "M4-TC", LocationType = LocationType.Bin, MaxWeightKg = 500m, IsActive = true },
                new Location { Id = binR04HuAId, FacilityId = facilityId, LocationCode = "M4-R04A-" + Guid.NewGuid().ToString("N")[..4], Barcode = "M4-R04A", LocationType = LocationType.Bin, PickSequence = 10, IsPickable = true, IsActive = true },
                new Location { Id = binR04HuBId, FacilityId = facilityId, LocationCode = "M4-R04B-" + Guid.NewGuid().ToString("N")[..4], Barcode = "M4-R04B", LocationType = LocationType.Bin, PickSequence = 1, IsPickable = true, IsActive = true },
                new Location { Id = binR04LooseId, FacilityId = facilityId, LocationCode = "M4-R04L-" + Guid.NewGuid().ToString("N")[..4], Barcode = "M4-R04L", LocationType = LocationType.Bin, PickSequence = 5, IsPickable = true, IsActive = true });

            seedDb.Items.AddRange(
                new Item { Id = itemAllocId, ClientId = client1Id, Sku = "M4PG-ALLOC-" + Guid.NewGuid().ToString("N")[..5], Description = "Alloc SKU", StorageTempClass = StorageTempClass.Ambient, WeightKg = 1m },
                new Item { Id = itemPutawayId, ClientId = client1Id, Sku = "M4PG-PUT-" + Guid.NewGuid().ToString("N")[..5], Description = "Putaway SKU", StorageTempClass = StorageTempClass.Ambient, WeightKg = 2m, VolumeCm3 = 100m },
                new Item { Id = itemFrozenId, ClientId = client1Id, Sku = "M4PG-FRZ-" + Guid.NewGuid().ToString("N")[..5], Description = "Frozen SKU", StorageTempClass = StorageTempClass.Frozen, WeightKg = 1m },
                new Item { Id = itemChilledId, ClientId = client1Id, Sku = "M4PG-CHL-" + Guid.NewGuid().ToString("N")[..5], Description = "Chilled SKU", StorageTempClass = StorageTempClass.Refrigerated, WeightKg = 1m },
                new Item { Id = itemTempCtrlId, ClientId = client1Id, Sku = "M4PG-TC-" + Guid.NewGuid().ToString("N")[..5], Description = "TempCtrl SKU", StorageTempClass = StorageTempClass.TemperatureControlled, WeightKg = 1m },
                new Item { Id = itemR04Id, ClientId = client1Id, Sku = "M4PG-R04-" + Guid.NewGuid().ToString("N")[..5], Description = "R04 HU SKU", StorageTempClass = StorageTempClass.Ambient, WeightKg = 1m });

            seedDb.HandlingUnits.AddRange(
                new HandlingUnit { Id = huId, ClientId = client1Id, FacilityId = facilityId, LpnCode = "M4PG-LPN-40", UnitType = HandlingUnitType.Pallet },
                new HandlingUnit { Id = r04HuAId, ClientId = client1Id, FacilityId = facilityId, LpnCode = "M4PG-R04-80", UnitType = HandlingUnitType.Pallet },
                new HandlingUnit { Id = r04HuBId, ClientId = client1Id, FacilityId = facilityId, LpnCode = "M4PG-R04-50", UnitType = HandlingUnitType.Pallet });

            var lotSoon = new InventoryLot { Id = Guid.NewGuid(), ClientId = client1Id, ItemId = itemAllocId, LotNumber = "M4-SOON-" + Guid.NewGuid().ToString("N")[..4], ExpirationDate = nowUtc.AddDays(10), QaStatus = LotQaStatus.Released };
            var lotFar = new InventoryLot { Id = Guid.NewGuid(), ClientId = client1Id, ItemId = itemAllocId, LotNumber = "M4-FAR-" + Guid.NewGuid().ToString("N")[..4], ExpirationDate = nowUtc.AddDays(90), QaStatus = LotQaStatus.Released };
            seedDb.InventoryLots.AddRange(lotSoon, lotFar);

            // Stock for PreferFullHandlingUnits + FEFO override + 4-way PostgreSQL allocation race:
            // - binHuId (40 units, full HU, Received -1d)
            // - binLooseId (20 units, loose, Received -1d, PickSeq=1)
            // - binOldExpId (lotFar, Received -20d -> FIFO prefers first; FEFO override skips for lotSoon!)
            // - binSoonExpId (lotSoon, Received -2d -> FEFO prefers first!)
            var sameRecUtc = nowUtc.AddDays(-1);
            seedDb.StockOnHand.AddRange(
                new StockOnHand { ClientId = client1Id, FacilityId = facilityId, ItemId = itemAllocId, LocationId = binLooseId, QtyAvailable = 20m, ReceivedAtUtc = sameRecUtc },
                new StockOnHand { ClientId = client1Id, FacilityId = facilityId, ItemId = itemAllocId, LocationId = binHuId, HandlingUnitId = huId, LpnCode = "M4PG-LPN-40", QtyAvailable = 40m, ReceivedAtUtc = sameRecUtc },
                new StockOnHand { ClientId = client1Id, FacilityId = facilityId, ItemId = itemAllocId, LocationId = binOldExpId, LotId = lotFar.Id, QtyAvailable = 15m, ReceivedAtUtc = nowUtc.AddDays(-20) },
                new StockOnHand { ClientId = client1Id, FacilityId = facilityId, ItemId = itemAllocId, LocationId = binSoonExpId, LotId = lotSoon.Id, QtyAvailable = 15m, ReceivedAtUtc = nowUtc.AddDays(-2) },
                new StockOnHand { ClientId = client1Id, FacilityId = facilityId, ItemId = itemPutawayId, LocationId = binOccupiedId, QtyAvailable = 10m },
                new StockOnHand { ClientId = client1Id, FacilityId = facilityId, ItemId = itemPutawayId, LocationId = stagingLocId, QtyAvailable = 60m }, // 60 * 2kg = 120kg > 100kg binTightCap!
                new StockOnHand { ClientId = client1Id, FacilityId = facilityId, ItemId = itemFrozenId, LocationId = stagingLocId, QtyAvailable = 10m },
                new StockOnHand { ClientId = client1Id, FacilityId = facilityId, ItemId = itemChilledId, LocationId = stagingLocId, QtyAvailable = 10m },
                new StockOnHand { ClientId = client1Id, FacilityId = facilityId, ItemId = itemTempCtrlId, LocationId = stagingLocId, QtyAvailable = 10m },
                new StockOnHand { ClientId = client1Id, FacilityId = facilityId, ItemId = itemR04Id, LocationId = binR04HuAId, HandlingUnitId = r04HuAId, LpnCode = "M4PG-R04-80", QtyAvailable = 80m, ReceivedAtUtc = sameRecUtc },
                new StockOnHand { ClientId = client1Id, FacilityId = facilityId, ItemId = itemR04Id, LocationId = binR04HuBId, HandlingUnitId = r04HuBId, LpnCode = "M4PG-R04-50", QtyAvailable = 50m, ReceivedAtUtc = sameRecUtc },
                new StockOnHand { ClientId = client1Id, FacilityId = facilityId, ItemId = itemR04Id, LocationId = binR04LooseId, QtyAvailable = 30m, ReceivedAtUtc = sameRecUtc });

            // AllocationPolicy: PreferFullHandlingUnits = true, DefaultStrategy = FIFO
            seedDb.AllocationPolicies.Add(new AllocationPolicy
            {
                ClientId = client1Id,
                FacilityId = facilityId,
                DefaultStrategy = AllocationStrategyType.FIFO,
                PreferFullHandlingUnits = true,
                AllowPartialAllocation = true,
                IsActive = true
            });

            // PutawayStrategyRule initially PreferConsolidation = true
            var rule = new PutawayStrategyRule
            {
                Id = putawayRuleId,
                ClientId = client1Id,
                FacilityId = facilityId,
                Sequence = 1,
                RuleName = "M4PG Consolidation",
                TempClassRequirement = StorageTempClass.Ambient,
                PreferConsolidation = true,
                AllowDynamicOverflow = false,
                IsActive = true
            };
            seedDb.PutawayStrategyRules.Add(rule);

            // Order 1: tests HTTP strategyOverride = FEFO against FIFO policy
            var ordOverride = new OutboundOrder { Id = ordOverrideId, ClientId = client1Id, FacilityId = facilityId, OrderNumber = "M4PG-OVR-" + Guid.NewGuid().ToString("N")[..4], CustomerName = "PG Ovr", Status = OutboundOrderStatus.Released };
            ordOverride.Lines.Add(new OutboundOrderLine { ItemId = itemAllocId, LineNumber = 1, QuantityOrdered = 15m });
            seedDb.OutboundOrders.Add(ordOverride);

            // Order HU: exact 40-unit demand on binHuId (when isolated before concOrders)
            var ordHu = new OutboundOrder { Id = ordHuId, ClientId = client1Id, FacilityId = facilityId, OrderNumber = "M4PG-HU-" + Guid.NewGuid().ToString("N")[..4], CustomerName = "PG HU", Status = OutboundOrderStatus.Released };
            ordHu.Lines.Add(new OutboundOrderLine { ItemId = itemAllocId, LineNumber = 1, QuantityOrdered = 20m, RequiredLotNumber = null });
            // Actually use concOrders for concurrency after ordOverride takes lotSoon (15)
            foreach (var cid in concOrderIds)
            {
                var co = new OutboundOrder { Id = cid, ClientId = client1Id, FacilityId = facilityId, OrderNumber = "M4PG-CNC-" + cid.ToString("N")[..5], CustomerName = "PG Conc", Status = OutboundOrderStatus.Released };
                co.Lines.Add(new OutboundOrderLine { ItemId = itemAllocId, LineNumber = 1, QuantityOrdered = 25m });
                seedDb.OutboundOrders.Add(co);
            }

            // 2 competing PutawayTasks each moving 30 units (60kg) into 100kg-capped binTightCapId + R01/R02 safety validation tasks + R04 HU order
            var ordR04 = new OutboundOrder { Id = ordR04Id, ClientId = client1Id, FacilityId = facilityId, OrderNumber = "M4PG-R04-" + Guid.NewGuid().ToString("N")[..4], CustomerName = "PG R04", Status = OutboundOrderStatus.Released };
            ordR04.Lines.Add(new OutboundOrderLine { ItemId = itemR04Id, LineNumber = 1, QuantityOrdered = 100m });
            seedDb.OutboundOrders.Add(ordR04);

            seedDb.PutawayTasks.AddRange(
                new PutawayTask { Id = putawayTask1Id, ClientId = client1Id, FacilityId = facilityId, ItemId = itemPutawayId, SourceLocationId = stagingLocId, SuggestedLocationId = binTightCapId, LpnCode = "M4-LPN-1", Quantity = 30m, Status = PutawayTaskStatus.Pending },
                new PutawayTask { Id = putawayTask2Id, ClientId = client1Id, FacilityId = facilityId, ItemId = itemPutawayId, SourceLocationId = stagingLocId, SuggestedLocationId = binTightCapId, LpnCode = "M4-LPN-2", Quantity = 30m, Status = PutawayTaskStatus.Pending },
                new PutawayTask { Id = taskFrzInChillId, ClientId = client1Id, FacilityId = facilityId, ItemId = itemFrozenId, SourceLocationId = stagingLocId, SuggestedLocationId = binColdId, LpnCode = "M4-FRZ-C4", Quantity = 2m, Status = PutawayTaskStatus.Pending },
                new PutawayTask { Id = taskFrzInNullColdId, ClientId = client1Id, FacilityId = facilityId, ItemId = itemFrozenId, SourceLocationId = stagingLocId, SuggestedLocationId = binColdNullId, LpnCode = "M4-FRZ-CNL", Quantity = 2m, Status = PutawayTaskStatus.Pending },
                new PutawayTask { Id = taskFrzOkId, ClientId = client1Id, FacilityId = facilityId, ItemId = itemFrozenId, SourceLocationId = stagingLocId, SuggestedLocationId = binFreezerId, LpnCode = "M4-FRZ-OK", Quantity = 2m, Status = PutawayTaskStatus.Pending },
                new PutawayTask { Id = taskChlInFreezerId, ClientId = client1Id, FacilityId = facilityId, ItemId = itemChilledId, SourceLocationId = stagingLocId, SuggestedLocationId = binFreezerId, LpnCode = "M4-CHL-FRZ", Quantity = 2m, Status = PutawayTaskStatus.Pending },
                new PutawayTask { Id = taskTcInBulkId, ClientId = client1Id, FacilityId = facilityId, ItemId = itemTempCtrlId, SourceLocationId = stagingLocId, SuggestedLocationId = binEmptyId, LpnCode = "M4-TC-BLK", Quantity = 2m, Status = PutawayTaskStatus.Pending },
                new PutawayTask { Id = taskTcInColdId, ClientId = client1Id, FacilityId = facilityId, ItemId = itemTempCtrlId, SourceLocationId = stagingLocId, SuggestedLocationId = binColdId, LpnCode = "M4-TC-CLD", Quantity = 2m, Status = PutawayTaskStatus.Pending },
                new PutawayTask { Id = taskTcOkId, ClientId = client1Id, FacilityId = facilityId, ItemId = itemTempCtrlId, SourceLocationId = stagingLocId, SuggestedLocationId = binTempCtrlId, LpnCode = "M4-TC-OK", Quantity = 2m, Status = PutawayTaskStatus.Pending },
                new PutawayTask { Id = taskAmbOverflowBlockedId, ClientId = client1Id, FacilityId = facilityId, ItemId = itemPutawayId, SourceLocationId = stagingLocId, SuggestedLocationId = binColdId, LpnCode = "M4-AMB-CLD", Quantity = 2m, Status = PutawayTaskStatus.Pending });

            await seedDb.SaveChangesAsync();
        }

        // 1. Authorization HTTP checks on /allocate strategyOverride & /putaway/strategies
        setCustomClaims([
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, SystemRoles.ClientAdmin),
            new Claim("user_type", "Client"),
            new Claim("tenant_id", client1Id.ToString()),
            new Claim("permissions", Permissions.Outbound.OrdersAllocate),
            new Claim("permissions", Permissions.Inbound.PutawayRules)
        ]);
        if ((await client.PostAsJsonAsync($"/api/v1/outbound/orders/{ordOverrideId}/allocate", new { strategyOverride = "FEFO" })).StatusCode != HttpStatusCode.Forbidden ||
            (await client.PostAsJsonAsync("/api/v1/putaway/strategies", new { clientId = client1Id, facilityId, sequence = 9, ruleName = "Client Blocked", preferConsolidation = false })).StatusCode != HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException("MDM-0.4 PostgreSQL Client override/policy mutation expected 403.");
        }

        setCustomClaims(null);
        // Foreign-facility Staff denied 403 even with OrdersAllocate / PutawayRules + MultiFacilityView
        if ((await client.SendAsync(staffReq(HttpMethod.Post, $"/api/v1/outbound/orders/{ordOverrideId}/allocate", staffId, foreignFacilityId, $"{Permissions.Outbound.OrdersAllocate},{Permissions.Analytics.MultiFacilityView}", new { strategyOverride = "FEFO" }))).StatusCode != HttpStatusCode.Forbidden ||
            (await client.SendAsync(staffReq(HttpMethod.Post, "/api/v1/putaway/strategies", staffId, foreignFacilityId, $"{Permissions.Inbound.PutawayRules},{Permissions.Analytics.MultiFacilityView}", new { clientId = client1Id, facilityId, sequence = 9, ruleName = "Foreign Blocked", preferConsolidation = false }))).StatusCode != HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException("MDM-0.4 PostgreSQL foreign-facility override/policy expected 403.");
        }

        // Same-facility Staff lacking OrdersAllocate permission denied 403 on /allocate
        if ((await client.SendAsync(staffReq(HttpMethod.Post, $"/api/v1/outbound/orders/{ordOverrideId}/allocate", staffId, facilityId, Permissions.Outbound.OrdersView, new { strategyOverride = "FEFO" }))).StatusCode != HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException("MDM-0.4 PostgreSQL permissionless Staff override expected 403.");
        }

        // MDM04-R03 & MDM04-R05: Authorized Facility Staff denied 403 for foreign client, revoked relationship, and arbitrary client;
        // Guid.Empty facility-wide rule creation rejected cleanly (400 BadRequest, zero DB exception) for both Staff and SuperAdmin; allowed 201 for active paired client
        if ((await client.SendAsync(staffReq(HttpMethod.Post, "/api/v1/putaway/strategies", staffId, facilityId, Permissions.Inbound.PutawayRules, new { clientId = foreignClientBId, facilityId, sequence = 20, ruleName = "Foreign Client Blocked", preferConsolidation = false }))).StatusCode != HttpStatusCode.Forbidden ||
            (await client.SendAsync(staffReq(HttpMethod.Post, "/api/v1/putaway/strategies", staffId, facilityId, Permissions.Inbound.PutawayRules, new { clientId = suspendedClientCId, facilityId, sequence = 21, ruleName = "Suspended Client Blocked", preferConsolidation = false }))).StatusCode != HttpStatusCode.Forbidden ||
            (await client.SendAsync(staffReq(HttpMethod.Post, "/api/v1/putaway/strategies", staffId, facilityId, Permissions.Inbound.PutawayRules, new { clientId = Guid.NewGuid(), facilityId, sequence = 22, ruleName = "Arbitrary Client Blocked", preferConsolidation = false }))).StatusCode != HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException("MDM-0.4 PostgreSQL MDM04-R03 invalid client/facility strategy pairing expected 403.");
        }

        using (var staffGlobalResp = await client.SendAsync(staffReq(HttpMethod.Post, "/api/v1/putaway/strategies", staffId, facilityId, Permissions.Inbound.PutawayRules, new { clientId = Guid.Empty, facilityId, sequence = 23, ruleName = "Global Staff Rejected", preferConsolidation = false })))
        using (var superAdminGlobalResp = await client.PostAsJsonAsync("/api/v1/putaway/strategies", new { clientId = Guid.Empty, facilityId, sequence = 24, ruleName = "Global SuperAdmin Rejected", preferConsolidation = false }))
        {
            if (staffGlobalResp.StatusCode != HttpStatusCode.BadRequest || superAdminGlobalResp.StatusCode != HttpStatusCode.BadRequest)
            {
                throw new InvalidOperationException($"MDM-0.4 PostgreSQL MDM04-R05 expected 400 BadRequest for Guid.Empty strategy creation, got Staff={staffGlobalResp.StatusCode}, SuperAdmin={superAdminGlobalResp.StatusCode}.");
            }
        }

        await using (var r05VerifyDb = createDb())
        {
            if (await r05VerifyDb.PutawayStrategyRules.AsNoTracking().AnyAsync(r => r.ClientId == Guid.Empty))
                throw new InvalidOperationException("MDM-0.4 PostgreSQL MDM04-R05 unexpected Guid.Empty PutawayStrategyRule persisted.");
        }

        using (var okRuleResp = await client.SendAsync(staffReq(HttpMethod.Post, "/api/v1/putaway/strategies", staffId, facilityId, Permissions.Inbound.PutawayRules, new { clientId = client1Id, facilityId, sequence = 25, ruleName = "Active Paired TempCtrl Rule", tempClassRequirement = StorageTempClass.TemperatureControlled, preferConsolidation = true, allowDynamicOverflow = false })))
        {
            if (okRuleResp.StatusCode != HttpStatusCode.Created)
                throw new InvalidOperationException($"MDM-0.4 PostgreSQL authorized active client/facility strategy creation failed: {okRuleResp.StatusCode}");
        }

        // Authorized Staff allocates ordOverrideId with strategyOverride="FEFO" -> must consume binSoonExpId (15 units) with FEFO!
        using (var okOvr = await client.SendAsync(staffReq(HttpMethod.Post, $"/api/v1/outbound/orders/{ordOverrideId}/allocate", staffId, facilityId, Permissions.Outbound.OrdersAllocate, new { strategyOverride = "FEFO" })))
        {
            if (okOvr.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException($"MDM-0.4 PostgreSQL authorized FEFO strategyOverride failed: {okOvr.StatusCode}");
        }

        await using (var chkDb = createDb())
        {
            var allocOvr = await chkDb.InventoryAllocations.AsNoTracking().SingleAsync(a => a.OutboundOrderId == ordOverrideId);
            if (allocOvr.LocationId != binSoonExpId || allocOvr.StrategyUsed != AllocationStrategyType.FEFO)
                throw new InvalidOperationException("MDM-0.4 PostgreSQL strategyOverride=FEFO was ignored or picked wrong bin.");
        }

        // 2. PutawayStrategyRule.PreferConsolidation enabled vs disabled on live PostgreSQL (and temperature safety)
        await using (var putSelDb = createDb())
        {
            var sel = new PutawayLocationSelector(putSelDb, NullLogger<PutawayLocationSelector>.Instance);
            var recTrue = await sel.EvaluateCandidateLocationsAsync(client1Id, facilityId, itemPutawayId, 5m);
            if (recTrue.First().LocationId != binOccupiedId)
                throw new InvalidOperationException("MDM-0.4 PostgreSQL PreferConsolidation=true did not select occupied SKU bin.");

            var rule = await putSelDb.PutawayStrategyRules.SingleAsync(r => r.Id == putawayRuleId);
            rule.PreferConsolidation = false;
            await putSelDb.SaveChangesAsync();

            var recFalse = await sel.EvaluateCandidateLocationsAsync(client1Id, facilityId, itemPutawayId, 5m);
            if (recFalse.First().LocationId != binEmptyId || recFalse.Any(r => r.LocationId == binColdId))
                throw new InvalidOperationException("MDM-0.4 PostgreSQL PreferConsolidation=false failed or breached cold zone isolation.");
        }

        // 3. PostgreSQL Concurrent Allocation Race (4 concurrent orders x 25 = 100 demand vs remaining 75 units available across bins, PreferFullHandlingUnits=true)
        // Remaining stock on itemAllocId: binOldExp (15, older) + binHu (40, HU) + binLoose (20, loose) = 75 total available.
        var allocRaceTasks = concOrderIds.Select(oid => Task.Run(async () =>
        {
            await using var db = createDb();
            var filter = new AllocationCandidateFilter();
            var resolver = new AllocationStrategyResolver([
                new FifoAllocationStrategy(db, filter),
                new FefoAllocationStrategy(db, filter),
                new ManualAllocationStrategy(db, filter)
            ]);
            var engine = new AllocationEngine(db, resolver, filter);
            var exec = new AllocationExecutionService(db, engine, filter, new VerificationTenant());
            return await exec.ExecuteOrderAllocationAsync(oid, userId: staffId);
        })).ToArray();

        var allocRaceRes = await Task.WhenAll(allocRaceTasks);
        var totalReserved = allocRaceRes.Sum(r => r.TotalQuantityAllocated);
        await using (var verifyAllocDb = createDb())
        {
            var stocks = await verifyAllocDb.StockOnHand.AsNoTracking()
                .Where(s => s.ClientId == client1Id && s.FacilityId == facilityId && s.ItemId == itemAllocId)
                .ToListAsync();

            // 15 (ordOverride) + 75 (concurrent) = 90 total across all 4 bins
            if (totalReserved != 75m ||
                stocks.Any(s => s.QtyAvailable < 0m) ||
                stocks.Sum(s => s.QtyAvailable) != 0m ||
                stocks.Sum(s => s.QtyAllocated) != 90m ||
                stocks.Sum(s => s.TotalQuantity) != 90m)
            {
                throw new InvalidOperationException(
                    $"MDM-0.4 PostgreSQL concurrent allocation ledger conservation failed: Reserved={totalReserved}, AvailSum={stocks.Sum(s => s.QtyAvailable)}, AllocSum={stocks.Sum(s => s.QtyAllocated)}");
            }
        }

        // 4. PostgreSQL Concurrent Putaway Capacity Race (2 tasks x 60kg targeting 100kg-max binTightCapId -> 1 succeeds, 1 fails ValidationException)
        var putawayOutcomes = await Task.WhenAll(
            AttemptAsync(async () =>
            {
                await using var db = createDb();
                var idCtx = new Mdm04StaffIdentity(staffId, facilityId);
                var h = new ConfirmPutawayTaskCommandHandler(db, idCtx, new WarehouseOperationalAuthorization(db, idCtx));
                await h.Handle(new ConfirmPutawayTaskCommand { TaskId = putawayTask1Id, ScannedLpn = "M4-LPN-1", ScannedLocationBarcode = "M4-CAP" }, CancellationToken.None);
            }),
            AttemptAsync(async () =>
            {
                await using var db = createDb();
                var idCtx = new Mdm04StaffIdentity(staffId, facilityId);
                var h = new ConfirmPutawayTaskCommandHandler(db, idCtx, new WarehouseOperationalAuthorization(db, idCtx));
                await h.Handle(new ConfirmPutawayTaskCommand { TaskId = putawayTask2Id, ScannedLpn = "M4-LPN-2", ScannedLocationBarcode = "M4-CAP" }, CancellationToken.None);
            }));

        if (putawayOutcomes.Count(e => e == null) != 1 || putawayOutcomes.Count(e => e is ValidationException) != 1)
        {
            throw new InvalidOperationException(
                $"MDM-0.4 PostgreSQL concurrent putaway capacity race expected 1x success & 1x ValidationException, got: {string.Join(", ", putawayOutcomes.Select(e => e?.GetType().Name ?? "OK"))}");
        }

        await using (var verifyPutDb = createDb())
        {
            var binCapStock = await verifyPutDb.StockOnHand.AsNoTracking()
                .SingleAsync(s => s.LocationId == binTightCapId && s.ItemId == itemPutawayId);
            var stgStock = await verifyPutDb.StockOnHand.AsNoTracking()
                .SingleAsync(s => s.LocationId == stagingLocId && s.ItemId == itemPutawayId);

            if (binCapStock.QtyAvailable != 30m || stgStock.QtyAvailable != 30m)
            {
                throw new InvalidOperationException(
                    $"MDM-0.4 PostgreSQL putaway conservation failed: BinCap={binCapStock.QtyAvailable}, Staging={stgStock.QtyAvailable}");
            }
        }

        // 5. MDM04-R01 & MDM04-R02 Live PostgreSQL Temperature Safety + Rule Precedence Verification
        await using (var safetyDb = createDb())
        {
            var sel = new PutawayLocationSelector(safetyDb, NullLogger<PutawayLocationSelector>.Instance);
            var frzRecs = await sel.EvaluateCandidateLocationsAsync(client1Id, facilityId, itemFrozenId, 2m);
            if (frzRecs.Count != 1 || frzRecs[0].LocationId != binFreezerId)
                throw new InvalidOperationException("MDM-0.4 PostgreSQL MDM04-R01 frozen selector admitted non-freezer or +4C/null ColdStorage bin.");

            var chlRecs = await sel.EvaluateCandidateLocationsAsync(client1Id, facilityId, itemChilledId, 2m);
            if (chlRecs.Count != 1 || chlRecs[0].LocationId != binColdId)
                throw new InvalidOperationException("MDM-0.4 PostgreSQL MDM04-R01 refrigerated selector admitted freezer or unconfigured ColdStorage bin.");

            var tcRecs = await sel.EvaluateCandidateLocationsAsync(client1Id, facilityId, itemTempCtrlId, 2m);
            if (tcRecs.Count != 1 || tcRecs[0].LocationId != binTempCtrlId)
                throw new InvalidOperationException("MDM-0.4 PostgreSQL MDM04-R01 temperature-controlled selector admitted unsuitable zone.");

            // Deactivate baseline Ambient rule first, then verify:
            // 1) Mismatched Frozen overflow rule must NOT allow Ambient cold overflow
            // 2) Exact-velocity restrictive Ambient rule (Velocity B, Seq 5, AllowDynamicOverflow=false) beats wildcard Ambient rule (Seq 1, AllowDynamicOverflow=true)
            var baseAmbientRule = await safetyDb.PutawayStrategyRules.SingleAsync(r => r.Id == putawayRuleId);
            baseAmbientRule.IsActive = false;
            safetyDb.PutawayStrategyRules.Add(new PutawayStrategyRule { ClientId = client1Id, FacilityId = facilityId, Sequence = 1, RuleName = "Mismatched Frozen Overflow", TempClassRequirement = StorageTempClass.Frozen, PreferConsolidation = false, AllowDynamicOverflow = true, IsActive = true });
            await safetyDb.SaveChangesAsync();

            var ambRecsNoAmbRule = await sel.EvaluateCandidateLocationsAsync(client1Id, facilityId, itemPutawayId, 2m);
            if (ambRecsNoAmbRule.Any(r => r.LocationId == binColdId || r.LocationId == binColdNullId || r.LocationId == binFreezerId))
                throw new InvalidOperationException("MDM-0.4 PostgreSQL MDM04-R02 mismatched Frozen rule allowed Ambient cold overflow.");

            safetyDb.PutawayStrategyRules.AddRange(
                new PutawayStrategyRule { ClientId = client1Id, FacilityId = facilityId, Sequence = 1, RuleName = "Ambient Wildcard Allow Overflow", TempClassRequirement = StorageTempClass.Ambient, TargetVelocity = null, PreferConsolidation = true, AllowDynamicOverflow = true, IsActive = true },
                new PutawayStrategyRule { ClientId = client1Id, FacilityId = facilityId, Sequence = 5, RuleName = "Ambient Exact Velocity Block Overflow", TempClassRequirement = StorageTempClass.Ambient, TargetVelocity = VelocityClass.B, PreferConsolidation = true, AllowDynamicOverflow = false, IsActive = true });
            await safetyDb.SaveChangesAsync();

            var ambRecs = await sel.EvaluateCandidateLocationsAsync(client1Id, facilityId, itemPutawayId, 2m);
            if (ambRecs.Any(r => r.LocationId == binColdId || r.LocationId == binColdNullId || r.LocationId == binFreezerId))
                throw new InvalidOperationException("MDM-0.4 PostgreSQL MDM04-R02 rule precedence allowed unintended cold overflow for Ambient SKU.");
        }

        async Task<Exception?> ConfirmTaskAsync(Guid taskId, string lpn, string barcode)
        {
            return await AttemptAsync(async () =>
            {
                await using var db = createDb();
                var idCtx = new Mdm04StaffIdentity(staffId, facilityId);
                var h = new ConfirmPutawayTaskCommandHandler(db, idCtx, new WarehouseOperationalAuthorization(db, idCtx));
                await h.Handle(new ConfirmPutawayTaskCommand { TaskId = taskId, ScannedLpn = lpn, ScannedLocationBarcode = barcode }, CancellationToken.None);
            });
        }

        if (await ConfirmTaskAsync(taskFrzInChillId, "M4-FRZ-C4", "M4-CLD") is not ValidationException ||
            await ConfirmTaskAsync(taskFrzInNullColdId, "M4-FRZ-CNL", "M4-CNL") is not ValidationException ||
            await ConfirmTaskAsync(taskChlInFreezerId, "M4-CHL-FRZ", "M4-FRZ") is not ValidationException ||
            await ConfirmTaskAsync(taskTcInBulkId, "M4-TC-BLK", "M4-EMP") is not ValidationException ||
            await ConfirmTaskAsync(taskTcInColdId, "M4-TC-CLD", "M4-CLD") is not ValidationException ||
            await ConfirmTaskAsync(taskAmbOverflowBlockedId, "M4-AMB-CLD", "M4-CLD") is not ValidationException)
        {
            throw new InvalidOperationException("MDM-0.4 PostgreSQL MDM04-R01/R02 incompatible putaway confirmation was not rejected with ValidationException.");
        }

        if (await ConfirmTaskAsync(taskFrzOkId, "M4-FRZ-OK", "M4-FRZ") != null ||
            await ConfirmTaskAsync(taskTcOkId, "M4-TC-OK", "M4-TC") != null)
        {
            throw new InvalidOperationException("MDM-0.4 PostgreSQL valid freezer/temperature-controlled putaway confirmation failed.");
        }

        // 6. MDM04-R04 Live PostgreSQL Residual-Demand-Aware Full HU Allocation (100 demand -> HU-A 80 + Loose 20, HU-B 50 untouched)
        await using (var r04Db = createDb())
        {
            var filter = new AllocationCandidateFilter();
            var resolver = new AllocationStrategyResolver([
                new FifoAllocationStrategy(r04Db, filter),
                new FefoAllocationStrategy(r04Db, filter),
                new ManualAllocationStrategy(r04Db, filter)
            ]);
            var engine = new AllocationEngine(r04Db, resolver, filter);
            var exec = new AllocationExecutionService(r04Db, engine, filter, new VerificationTenant());
            var r04Res = await exec.ExecuteOrderAllocationAsync(ordR04Id, userId: staffId);

            var r04Stocks = await r04Db.StockOnHand.AsNoTracking()
                .Where(s => s.ItemId == itemR04Id)
                .ToListAsync();
            var sHuA = r04Stocks.Single(s => s.LocationId == binR04HuAId);
            var sHuB = r04Stocks.Single(s => s.LocationId == binR04HuBId);
            var sLoose = r04Stocks.Single(s => s.LocationId == binR04LooseId);

            if (r04Res.TotalQuantityAllocated != 100m || sHuA.QtyAllocated != 80m || sLoose.QtyAllocated != 20m || sHuB.QtyAvailable != 50m || sHuB.QtyAllocated != 0m)
            {
                throw new InvalidOperationException(
                    $"MDM-0.4 PostgreSQL MDM04-R04 HU residual test failed: Total={r04Res.TotalQuantityAllocated}, HuA={sHuA.QtyAllocated}, Loose={sLoose.QtyAllocated}, HuB_Avail={sHuB.QtyAvailable}");
            }
        }

        Console.WriteLine("PASS PostgreSQL MDM-0.4 Policy Wiring (PreferFullHandlingUnits R04, StrategyOverride, PreferConsolidation, Temperature Safety R01, Precedence R02, Client-Facility Isolation R03) & Concurrent Stock/Capacity Conservation verified cleanly.");
    }

    private static async Task<Exception?> AttemptAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private sealed class Mdm04StaffIdentity(Guid userId, Guid facilityId) : ICurrentUserContext
    {
        public Guid? UserId => userId;
        public string? Email => "mdm04-pg@local";
        public Guid? TenantId => null;
        public Guid? FacilityId => facilityId;
        public Guid? RequestedFacilityId => null;
        public UserType? UserType => Warehouse3PL.Domain.Enums.UserType.Staff;
        public IReadOnlyList<string> Roles => ["SuperAdmin"];
        public IReadOnlyList<string> Permissions => [];
        public bool IsAuthenticated => true;
        public bool IsStaff => true;
        public bool IsSuperAdmin => true;
        public bool HasPermission(string permission) => true;
        public bool IsInRole(string role) => true;
    }
}
