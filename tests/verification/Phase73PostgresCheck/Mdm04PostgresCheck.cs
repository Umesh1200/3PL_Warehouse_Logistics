using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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
        var binColdId = Guid.NewGuid();

        var huId = Guid.NewGuid();
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
            seedDb.ClientFacilityAccesses.Add(new ClientFacilityAccess
            {
                ClientId = client1Id,
                FacilityId = facilityId,
                Status = FacilityAccessStatus.Active
            });

            seedDb.WarehouseZones.Add(new WarehouseZone
            {
                Id = coldZoneId,
                FacilityId = facilityId,
                ZoneCode = "M4PG-COLD-" + Guid.NewGuid().ToString("N")[..4],
                ZoneType = WarehouseZoneType.ColdStorage,
                TempMaxC = 4m
            });

            seedDb.Locations.AddRange(
                new Location { Id = binLooseId, FacilityId = facilityId, LocationCode = "M4-LOOSE-" + Guid.NewGuid().ToString("N")[..4], Barcode = "M4-L", LocationType = LocationType.Bin, PickSequence = 1, IsPickable = true, IsActive = true },
                new Location { Id = binHuId, FacilityId = facilityId, LocationCode = "M4-HU-" + Guid.NewGuid().ToString("N")[..4], Barcode = "M4-H", LocationType = LocationType.Bin, PickSequence = 5, IsPickable = true, IsActive = true },
                new Location { Id = binOldExpId, FacilityId = facilityId, LocationCode = "M4-F90-" + Guid.NewGuid().ToString("N")[..4], Barcode = "M4-90", LocationType = LocationType.Bin, PickSequence = 1, IsPickable = true, IsActive = true },
                new Location { Id = binSoonExpId, FacilityId = facilityId, LocationCode = "M4-F10-" + Guid.NewGuid().ToString("N")[..4], Barcode = "M4-10", LocationType = LocationType.Bin, PickSequence = 2, IsPickable = true, IsActive = true },
                new Location { Id = stagingLocId, FacilityId = facilityId, LocationCode = "M4-STG-" + Guid.NewGuid().ToString("N")[..4], Barcode = "M4-STG", LocationType = LocationType.StagingLane, IsReceiving = true, IsActive = true },
                new Location { Id = binOccupiedId, FacilityId = facilityId, LocationCode = "M4-OCC-" + Guid.NewGuid().ToString("N")[..4], Barcode = "M4-OCC", LocationType = LocationType.Bin, MaxWeightKg = 500m, IsActive = true },
                new Location { Id = binEmptyId, FacilityId = facilityId, LocationCode = "M4-A-EMP-" + Guid.NewGuid().ToString("N")[..4], Barcode = "M4-EMP", LocationType = LocationType.Bin, MaxWeightKg = 500m, IsActive = true },
                new Location { Id = binTightCapId, FacilityId = facilityId, LocationCode = "M4-Z-CAP-" + Guid.NewGuid().ToString("N")[..4], Barcode = "M4-CAP", LocationType = LocationType.Bin, MaxWeightKg = 100m, IsActive = true },
                new Location { Id = binColdId, FacilityId = facilityId, ZoneId = coldZoneId, LocationCode = "M4-CLD-" + Guid.NewGuid().ToString("N")[..4], Barcode = "M4-CLD", LocationType = LocationType.Bin, MaxWeightKg = 500m, IsActive = true });

            seedDb.Items.AddRange(
                new Item { Id = itemAllocId, ClientId = client1Id, Sku = "M4PG-ALLOC-" + Guid.NewGuid().ToString("N")[..5], Description = "Alloc SKU", StorageTempClass = StorageTempClass.Ambient, WeightKg = 1m },
                new Item { Id = itemPutawayId, ClientId = client1Id, Sku = "M4PG-PUT-" + Guid.NewGuid().ToString("N")[..5], Description = "Putaway SKU", StorageTempClass = StorageTempClass.Ambient, WeightKg = 2m, VolumeCm3 = 100m });

            seedDb.HandlingUnits.Add(new HandlingUnit
            {
                Id = huId,
                ClientId = client1Id,
                FacilityId = facilityId,
                LpnCode = "M4PG-LPN-40",
                UnitType = HandlingUnitType.Pallet
            });

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
                new StockOnHand { ClientId = client1Id, FacilityId = facilityId, ItemId = itemPutawayId, LocationId = stagingLocId, QtyAvailable = 60m }); // 60 * 2kg = 120kg > 100kg binTightCap!

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

            // 2 competing PutawayTasks each moving 30 units (60kg) into 100kg-capped binTightCapId
            seedDb.PutawayTasks.AddRange(
                new PutawayTask { Id = putawayTask1Id, ClientId = client1Id, FacilityId = facilityId, ItemId = itemPutawayId, SourceLocationId = stagingLocId, SuggestedLocationId = binTightCapId, LpnCode = "M4-LPN-1", Quantity = 30m, Status = PutawayTaskStatus.Pending },
                new PutawayTask { Id = putawayTask2Id, ClientId = client1Id, FacilityId = facilityId, ItemId = itemPutawayId, SourceLocationId = stagingLocId, SuggestedLocationId = binTightCapId, LpnCode = "M4-LPN-2", Quantity = 30m, Status = PutawayTaskStatus.Pending });

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
                var idCtx = new VerificationIdentity(facilityId);
                var h = new ConfirmPutawayTaskCommandHandler(db, idCtx, new WarehouseOperationalAuthorization(db, idCtx));
                await h.Handle(new ConfirmPutawayTaskCommand { TaskId = putawayTask1Id, ScannedLpn = "M4-LPN-1", ScannedLocationBarcode = "M4-CAP" }, CancellationToken.None);
            }),
            AttemptAsync(async () =>
            {
                await using var db = createDb();
                var idCtx = new VerificationIdentity(facilityId);
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

        Console.WriteLine("PASS PostgreSQL MDM-0.4 Policy Wiring (PreferFullHandlingUnits, StrategyOverride, PreferConsolidation), Authorization, Safety & Concurrent Stock/Capacity Conservation verified cleanly.");
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
}
