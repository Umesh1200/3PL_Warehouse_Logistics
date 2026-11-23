using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Focused live HTTP + PostgreSQL integration check for Phase 7.4.1:
/// Create Order -> Stock Availability -> Allocate -> Generate Wave -> Release Wave.
/// </summary>
public static class Phase741OutboundLifecycleCheck
{
    public static async Task RunAsync(
        string connectionString,
        HttpClient client,
        Guid clientId,
        Guid facilityId,
        Guid staffUserId,
        Func<HttpMethod, string, Guid, Guid?, string, object?, HttpRequestMessage> staffReqFactory,
        Action<Guid> setClientAdminClaims,
        Guid? foreignClientId = null)
    {
        var itemId = Guid.NewGuid();
        var foreignItemId = Guid.NewGuid();
        var facBOnlyClientId = Guid.NewGuid();
        var facBOnlyId = Guid.NewGuid();
        var authDestFacId = Guid.NewGuid();
        var facBOnlyItemId = Guid.NewGuid();
        var binId = Guid.NewGuid();
        var stockId = Guid.NewGuid();

        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        await using (var seedDb = new ApplicationDbContext(
            dbOptions, new VerificationTenant(), new VerificationIdentity(facilityId),
            new AuditableEntityInterceptor(new VerificationIdentity(facilityId), new VerificationClock())))
        {
            seedDb.Items.Add(new Item
            {
                Id = itemId,
                ClientId = clientId,
                Sku = "PG-741-SKU",
                Description = "Phase 7.4.1 PostgreSQL Item",
                WeightKg = 1.5m,
                LengthCm = 10,
                WidthCm = 10,
                HeightCm = 10,
                StorageTempClass = StorageTempClass.Ambient
            });
            if (foreignClientId.HasValue)
            {
                seedDb.Items.Add(new Item
                {
                    Id = foreignItemId,
                    ClientId = foreignClientId.Value,
                    Sku = "PG-741-FOREIGN-SKU",
                    Description = "Foreign Tenant SKU",
                    WeightKg = 1m,
                    LengthCm = 5,
                    WidthCm = 5,
                    HeightCm = 5,
                    StorageTempClass = StorageTempClass.Ambient
                });
            }
            seedDb.Facilities.AddRange(
                new Facility
                {
                    Id = facBOnlyId,
                    FacilityCode = $"PG741B-{facBOnlyId.ToString("N")[..6]}",
                    Name = "PG 741 Isolated Facility B",
                    CountryCode = "USA",
                    IsActive = true
                },
                new Facility
                {
                    Id = authDestFacId,
                    FacilityCode = $"PG741D-{authDestFacId.ToString("N")[..6]}",
                    Name = "PG 741 Authorized Dest Facility",
                    CountryCode = "USA",
                    IsActive = true
                });
            seedDb.Clients.Add(new Warehouse3PL.Domain.Entities.Security.Client
            {
                Id = facBOnlyClientId,
                AccountCode = $"PG741C-{facBOnlyClientId.ToString("N")[..6]}",
                CompanyName = "Facility B Only Client",
                Status = ClientStatus.Active
            });
            seedDb.ClientFacilityAccesses.AddRange(
                new Warehouse3PL.Domain.Entities.Security.ClientFacilityAccess
                {
                    ClientId = facBOnlyClientId,
                    FacilityId = facBOnlyId,
                    Status = FacilityAccessStatus.Active
                },
                new Warehouse3PL.Domain.Entities.Security.ClientFacilityAccess
                {
                    ClientId = clientId,
                    FacilityId = authDestFacId,
                    Status = FacilityAccessStatus.Active
                });
            seedDb.Items.Add(new Item
            {
                Id = facBOnlyItemId,
                ClientId = facBOnlyClientId,
                Sku = "PG-741-FACB-ONLY-SKU",
                Description = "Unauthorized Facility A SKU",
                WeightKg = 1m,
                LengthCm = 5,
                WidthCm = 5,
                HeightCm = 5,
                StorageTempClass = StorageTempClass.Ambient
            });
            seedDb.Locations.Add(new Location
            {
                Id = binId,
                FacilityId = facilityId,
                LocationCode = "PG-741-BIN-01",
                Barcode = "PG-741-BIN-01",
                LocationType = LocationType.Bin,
                IsActive = true,
                IsPickable = true,
                PickSequence = 10
            });
            seedDb.StockOnHand.Add(new StockOnHand
            {
                Id = stockId,
                ClientId = clientId,
                FacilityId = facilityId,
                LocationId = binId,
                ItemId = itemId,
                QtyAvailable = 25m,
                QtyAllocated = 0m
            });
            await seedDb.SaveChangesAsync();
        }

        int initialTxCount;
        decimal initialTotalQty;
        await using (var baselineDb = new ApplicationDbContext(
            dbOptions, new VerificationTenant(), new VerificationIdentity(facilityId),
            new AuditableEntityInterceptor(new VerificationIdentity(facilityId), new VerificationClock())))
        {
            initialTxCount = await baselineDb.InventoryTransactions.CountAsync();
            var stock = await baselineDb.StockOnHand.SingleAsync(s => s.Id == stockId);
            initialTotalQty = stock.QtyAvailable + stock.QtyAllocated;
        }

        // 1. Authorized Order Entry (Staff with X-Tenant-Id)
        var createPayload = new
        {
            facilityId,
            orderType = "B2BSalesOrder",
            priority = 7,
            serviceLevel = "StandardGround",
            carrierCode = "SIM-FEDEX",
            shippingAddress = new
            {
                recipientName = "PG Lifecycle Buyer",
                companyName = "PG Test Corp",
                addressLine1 = "741 Warehouse Way",
                city = "Chicago",
                stateOrProvince = "IL",
                postalCode = "60601",
                countryCode = "USA"
            },
            lines = new[]
            {
                new { itemId, quantityOrdered = 6m }
            }
        };

        var createReq = staffReqFactory(
            HttpMethod.Post, "/api/v1/outbound/orders", staffUserId, facilityId, Permissions.Outbound.OrdersCreate, createPayload);
        createReq.Headers.Add("X-Tenant-Id", clientId.ToString());
        Guid orderId;
        using (var createResp = await client.SendAsync(createReq))
        {
            if (createResp.StatusCode != HttpStatusCode.Created)
                throw new InvalidOperationException($"Phase 7.4.1 CreateOrder failed: {createResp.StatusCode}: {await createResp.Content.ReadAsStringAsync()}");
            using var createDoc = await JsonDocument.ParseAsync(await createResp.Content.ReadAsStreamAsync());
            orderId = createDoc.RootElement.GetProperty("id").GetGuid();
        }

        // 2. Stock Availability Assessment
        using (var availResp = await client.SendAsync(staffReqFactory(
            HttpMethod.Get, $"/api/v1/outbound/orders/{orderId}/stock-availability", staffUserId, facilityId, Permissions.Outbound.OrdersView, null)))
        {
            if (availResp.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException($"Phase 7.4.1 CheckStockAvailability failed: {availResp.StatusCode}");
            using var availDoc = await JsonDocument.ParseAsync(await availResp.Content.ReadAsStreamAsync());
            if (!availDoc.RootElement.GetProperty("isFullyAvailable").GetBoolean())
                throw new InvalidOperationException("Phase 7.4.1 CheckStockAvailability unexpectedly reported unavailable.");
        }

        // Negative & positive ClientAdmin P741-01 checks on PostgreSQL (no items:view, no waves:manage)
        setClientAdminClaims(clientId);
        using (var catResp = await client.GetAsync($"/api/v1/outbound/orders/catalog-items?facilityId={facilityId}"))
        {
            if (catResp.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException($"ClientAdmin catalog-items lookup failed: {catResp.StatusCode}: {await catResp.Content.ReadAsStringAsync()}");
            using var catDoc = await JsonDocument.ParseAsync(await catResp.Content.ReadAsStreamAsync());
            var skus = catDoc.RootElement.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("sku").GetString()).ToList();
            if (!skus.Contains("PG-741-SKU") || skus.Contains("PG-741-FOREIGN-SKU"))
                throw new InvalidOperationException("ClientAdmin catalog-items must return own active SKU and exclude foreign tenant SKUs.");
        }
        if (foreignClientId.HasValue &&
            (await client.GetAsync($"/api/v1/outbound/orders/catalog-items?clientId={foreignClientId.Value}")).StatusCode != HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException("ClientAdmin must receive 403 on foreign-tenant catalog-items query.");
        }
        if ((await client.GetAsync("/api/v1/outbound/orders/reference-clients")).StatusCode != HttpStatusCode.Forbidden ||
            (await client.GetAsync("/api/v1/outbound/pick-waves/eligible-clients")).StatusCode != HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException("ClientAdmin must receive 403 on staff-only outbound client reference endpoints.");
        }
        using (var caOrderResp = await client.PostAsJsonAsync("/api/v1/outbound/orders", new
        {
            facilityId,
            orderType = "B2BSalesOrder",
            priority = 4,
            serviceLevel = "StandardGround",
            shippingAddress = new
            {
                recipientName = "ClientAdmin Self Order",
                addressLine1 = "101 Tenant Ave",
                city = "Chicago",
                stateOrProvince = "IL",
                postalCode = "60601",
                countryCode = "USA"
            },
            lines = new[] { new { itemId, quantityOrdered = 1m } }
        }))
        {
            if (caOrderResp.StatusCode != HttpStatusCode.Created)
                throw new InvalidOperationException($"ClientAdmin order creation with catalog SKU failed: {caOrderResp.StatusCode}: {await caOrderResp.Content.ReadAsStringAsync()}");
        }
        if ((await client.PostAsJsonAsync($"/api/v1/outbound/orders/{orderId}/allocate", new { })).StatusCode != HttpStatusCode.Forbidden ||
            (await client.PostAsJsonAsync("/api/v1/outbound/pick-waves", new { request = new { clientId, facilityId, waveType = "BatchPick" } })).StatusCode != HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException("ClientAdmin must receive 403 on Order Allocation and Wave Generation.");
        }
        setClientAdminClaims(Guid.Empty);

        // Staff permission-consistent client reference lookups (without broad clients:view)
        using (var waveEligibleResp = await client.SendAsync(staffReqFactory(
            HttpMethod.Get, $"/api/v1/outbound/pick-waves/eligible-clients?facilityId={facilityId}", staffUserId, facilityId, Permissions.Outbound.WavesManage, null)))
        {
            if (waveEligibleResp.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException($"Pick wave eligible-clients lookup failed: {waveEligibleResp.StatusCode}");
        }

        // P741-04 Live PostgreSQL checks: Staff facility/client relationship across catalog-items, order create, and bulk import
        using (var staffValidCat = await client.SendAsync(staffReqFactory(
            HttpMethod.Get, $"/api/v1/outbound/orders/catalog-items?clientId={clientId}&facilityId={facilityId}", staffUserId, facilityId, Permissions.Outbound.OrdersCreate, null)))
        {
            if (staffValidCat.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException($"Staff valid facility/client catalog lookup failed: {staffValidCat.StatusCode}");
        }

        using (var staffCrossClientWithFac = await client.SendAsync(staffReqFactory(
            HttpMethod.Get, $"/api/v1/outbound/orders/catalog-items?clientId={facBOnlyClientId}&facilityId={facilityId}", staffUserId, facilityId, Permissions.Outbound.OrdersCreate, null)))
        {
            if (staffCrossClientWithFac.StatusCode != HttpStatusCode.Forbidden)
                throw new InvalidOperationException($"Expected 403 for Staff at Facility A listing Facility-B-only Client SKUs, got {staffCrossClientWithFac.StatusCode}");
        }

        using (var staffOmitFac = await client.SendAsync(staffReqFactory(
            HttpMethod.Get, $"/api/v1/outbound/orders/catalog-items?clientId={facBOnlyClientId}", staffUserId, facilityId, Permissions.Outbound.OrdersCreate, null)))
        {
            if (staffOmitFac.StatusCode != HttpStatusCode.Forbidden)
                throw new InvalidOperationException($"Expected 403 when Staff omits facilityId for unauthorized client, got {staffOmitFac.StatusCode}");
        }

        var arbitraryTenantReq = staffReqFactory(
            HttpMethod.Get, "/api/v1/outbound/orders/catalog-items", staffUserId, facilityId, Permissions.Outbound.OrdersCreate, null);
        arbitraryTenantReq.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());
        using (var staffArbitraryTenant = await client.SendAsync(arbitraryTenantReq))
        {
            if (staffArbitraryTenant.StatusCode != HttpStatusCode.Forbidden)
                throw new InvalidOperationException($"Expected 403 when Staff passes arbitrary X-Tenant-Id, got {staffArbitraryTenant.StatusCode}");
        }

        // Authorized Staff bulk import succeeds for valid client/facility pairing
        var staffValidImportReq = staffReqFactory(
            HttpMethod.Post,
            "/api/v1/outbound/orders/bulk-import",
            staffUserId,
            facilityId,
            Permissions.Outbound.OrdersImport,
            new
            {
                facilityId,
                sourceFileName = "pg-p741-04-valid.json",
                orders = new[]
                {
                    new
                    {
                        facilityId,
                        externalOrderReference = $"PG-IMP-OK-{Guid.NewGuid():N}",
                        orderType = "B2BSalesOrder",
                        priority = 5,
                        serviceLevel = "StandardGround",
                        shippingAddress = createPayload.shippingAddress,
                        lines = new[] { new { itemId, quantityOrdered = 1m } }
                    }
                }
            });
        staffValidImportReq.Headers.Add("X-Tenant-Id", clientId.ToString());
        using (var validImpResp = await client.SendAsync(staffValidImportReq))
        {
            if (validImpResp.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException($"Authorized Staff bulk import failed: {validImpResp.StatusCode}: {await validImpResp.Content.ReadAsStringAsync()}");
        }

        // Unauthorized Staff order create & bulk import for facBOnlyClientId at facilityId must return 403 without DB persistence
        var unauthOrderReq = staffReqFactory(
            HttpMethod.Post,
            "/api/v1/outbound/orders",
            staffUserId,
            facilityId,
            Permissions.Outbound.OrdersCreate,
            new
            {
                facilityId,
                externalOrderReference = "PG-SHOULD-NOT-PERSIST-ORD",
                orderType = "B2BSalesOrder",
                priority = 5,
                serviceLevel = "StandardGround",
                shippingAddress = createPayload.shippingAddress,
                lines = new[] { new { itemId = facBOnlyItemId, quantityOrdered = 1m } }
            });
        unauthOrderReq.Headers.Add("X-Tenant-Id", facBOnlyClientId.ToString());
        using (var unauthOrdResp = await client.SendAsync(unauthOrderReq))
        {
            if (unauthOrdResp.StatusCode != HttpStatusCode.Forbidden)
                throw new InvalidOperationException($"Expected 403 for unauthorized Staff order create, got {unauthOrdResp.StatusCode}");
        }

        var unauthImportReq = staffReqFactory(
            HttpMethod.Post,
            "/api/v1/outbound/orders/bulk-import",
            staffUserId,
            facilityId,
            Permissions.Outbound.OrdersImport,
            new
            {
                facilityId,
                sourceFileName = "pg-unauth-import.json",
                orders = new[]
                {
                    new
                    {
                        facilityId,
                        externalOrderReference = "PG-SHOULD-NOT-PERSIST-IMP",
                        orderType = "B2BSalesOrder",
                        priority = 5,
                        serviceLevel = "StandardGround",
                        shippingAddress = createPayload.shippingAddress,
                        lines = new[] { new { itemId = facBOnlyItemId, quantityOrdered = 1m } }
                    }
                }
            });
        unauthImportReq.Headers.Add("X-Tenant-Id", facBOnlyClientId.ToString());
        using (var unauthImpResp = await client.SendAsync(unauthImportReq))
        {
            if (unauthImpResp.StatusCode != HttpStatusCode.Forbidden)
                throw new InvalidOperationException($"Expected 403 for unauthorized Staff bulk import, got {unauthImpResp.StatusCode}");
        }

        await using (var p74104VerifyDb = new ApplicationDbContext(
            dbOptions, new VerificationTenant(), new VerificationIdentity(facilityId),
            new AuditableEntityInterceptor(new VerificationIdentity(facilityId), new VerificationClock())))
        {
            var leakedOrders = await p74104VerifyDb.OutboundOrders.CountAsync(o => o.ClientId == facBOnlyClientId);
            var leakedBatches = await p74104VerifyDb.OrderImportBatches.CountAsync(b => b.ClientId == facBOnlyClientId);
            if (leakedOrders != 0 || leakedBatches != 0)
                throw new InvalidOperationException($"Unauthorized Staff order/import persisted rows on PostgreSQL: orders={leakedOrders}, batches={leakedBatches}");
        }

        // Bulk stock-transfer destination authorization checks on live PostgreSQL
        var validXferImportReq = staffReqFactory(
            HttpMethod.Post,
            "/api/v1/outbound/orders/bulk-import",
            staffUserId,
            facilityId,
            Permissions.Outbound.OrdersImport,
            new
            {
                facilityId,
                sourceFileName = "pg-p741-04-valid-xfer.json",
                orders = new[]
                {
                    new
                    {
                        facilityId,
                        destinationFacilityId = authDestFacId,
                        externalOrderReference = $"PG-XFER-OK-{Guid.NewGuid():N}",
                        orderType = "StockTransferOrder",
                        priority = 5,
                        serviceLevel = "FreightLTL",
                        shippingAddress = createPayload.shippingAddress,
                        lines = new[] { new { itemId, quantityOrdered = 1m } }
                    }
                }
            });
        validXferImportReq.Headers.Add("X-Tenant-Id", clientId.ToString());
        using (var validXferResp = await client.SendAsync(validXferImportReq))
        {
            if (validXferResp.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException($"Authorized Staff bulk stock-transfer import failed: {validXferResp.StatusCode}: {await validXferResp.Content.ReadAsStringAsync()}");
        }

        var unauthStaffXferReq = staffReqFactory(
            HttpMethod.Post,
            "/api/v1/outbound/orders/bulk-import",
            staffUserId,
            facilityId,
            Permissions.Outbound.OrdersImport,
            new
            {
                facilityId,
                sourceFileName = "pg-p741-04-unauth-xfer.json",
                orders = new[]
                {
                    new
                    {
                        facilityId,
                        destinationFacilityId = facBOnlyId,
                        externalOrderReference = "PG-SHOULD-NOT-PERSIST-XFER-STAFF",
                        orderType = "StockTransferOrder",
                        priority = 5,
                        serviceLevel = "FreightLTL",
                        shippingAddress = createPayload.shippingAddress,
                        lines = new[] { new { itemId, quantityOrdered = 1m } }
                    }
                }
            });
        unauthStaffXferReq.Headers.Add("X-Tenant-Id", clientId.ToString());
        using (var unauthXferResp = await client.SendAsync(unauthStaffXferReq))
        {
            if (unauthXferResp.StatusCode != HttpStatusCode.Forbidden)
                throw new InvalidOperationException($"Expected 403 for unauthorized Staff bulk stock-transfer destination, got {unauthXferResp.StatusCode}");
        }

        setClientAdminClaims(clientId);
        using (var validClientXferResp = await client.PostAsJsonAsync("/api/v1/outbound/orders/bulk-import", new
        {
            facilityId,
            sourceFileName = "pg-p741-04-valid-client-xfer.json",
            orders = new[]
            {
                new
                {
                    facilityId,
                    destinationFacilityId = authDestFacId,
                    externalOrderReference = $"PG-CLIENT-XFER-OK-{Guid.NewGuid():N}",
                    orderType = "StockTransferOrder",
                    priority = 5,
                    serviceLevel = "FreightLTL",
                    shippingAddress = createPayload.shippingAddress,
                    lines = new[] { new { itemId, quantityOrdered = 1m } }
                }
            }
        }))
        {
            if (validClientXferResp.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException($"Authorized ClientAdmin bulk stock-transfer failed: {validClientXferResp.StatusCode}: {await validClientXferResp.Content.ReadAsStringAsync()}");
        }
        using (var unauthClientXferResp = await client.PostAsJsonAsync("/api/v1/outbound/orders/bulk-import", new
        {
            facilityId,
            sourceFileName = "pg-p741-04-unauth-client-xfer.json",
            orders = new[]
            {
                new
                {
                    facilityId,
                    destinationFacilityId = facBOnlyId,
                    externalOrderReference = "PG-SHOULD-NOT-PERSIST-XFER-CLIENT",
                    orderType = "StockTransferOrder",
                    priority = 5,
                    serviceLevel = "FreightLTL",
                    shippingAddress = createPayload.shippingAddress,
                    lines = new[] { new { itemId, quantityOrdered = 1m } }
                }
            }
        }))
        {
            if (unauthClientXferResp.StatusCode != HttpStatusCode.Forbidden)
                throw new InvalidOperationException($"Expected 403 for unauthorized ClientAdmin bulk stock-transfer destination, got {unauthClientXferResp.StatusCode}");
        }
        setClientAdminClaims(Guid.Empty);

        await using (var xferVerifyDb = new ApplicationDbContext(
            dbOptions, new VerificationTenant(), new VerificationIdentity(facilityId),
            new AuditableEntityInterceptor(new VerificationIdentity(facilityId), new VerificationClock())))
        {
            var leakedXferOrders = await xferVerifyDb.OutboundOrders.CountAsync(o =>
                o.DestinationFacilityId == facBOnlyId ||
                o.ExternalOrderReference == "PG-SHOULD-NOT-PERSIST-XFER-STAFF" ||
                o.ExternalOrderReference == "PG-SHOULD-NOT-PERSIST-XFER-CLIENT");
            var leakedXferBatches = await xferVerifyDb.OrderImportBatches.CountAsync(b =>
                b.SourceFileName == "pg-p741-04-unauth-xfer.json" ||
                b.SourceFileName == "pg-p741-04-unauth-client-xfer.json");
            if (leakedXferOrders != 0 || leakedXferBatches != 0)
                throw new InvalidOperationException($"Unauthorized bulk stock-transfer persisted rows on PostgreSQL: orders={leakedXferOrders}, batches={leakedXferBatches}");
        }

        // Ordinary B2BSalesOrder carrying an unused destinationFacilityId succeeds and persists null DestinationFacilityId
        var salesUnusedDestExtRef = $"PG-SALES-UNUSED-DEST-{Guid.NewGuid():N}";
        var salesUnusedDestReq = staffReqFactory(
            HttpMethod.Post,
            "/api/v1/outbound/orders/bulk-import",
            staffUserId,
            facilityId,
            Permissions.Outbound.OrdersImport,
            new
            {
                facilityId,
                sourceFileName = "pg-p741-04-sales-unused-dest.json",
                orders = new[]
                {
                    new
                    {
                        facilityId,
                        destinationFacilityId = facBOnlyId,
                        externalOrderReference = salesUnusedDestExtRef,
                        orderType = "B2BSalesOrder",
                        priority = 5,
                        serviceLevel = "StandardGround",
                        shippingAddress = createPayload.shippingAddress,
                        lines = new[] { new { itemId, quantityOrdered = 1m } }
                    }
                }
            });
        salesUnusedDestReq.Headers.Add("X-Tenant-Id", clientId.ToString());
        using (var salesUnusedDestResp = await client.SendAsync(salesUnusedDestReq))
        {
            if (salesUnusedDestResp.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException($"Ordinary B2B import with unused destinationFacilityId failed: {salesUnusedDestResp.StatusCode}");
        }
        await using (var salesDestDb = new ApplicationDbContext(
            dbOptions, new VerificationTenant(), new VerificationIdentity(facilityId),
            new AuditableEntityInterceptor(new VerificationIdentity(facilityId), new VerificationClock())))
        {
            var salesRow = await salesDestDb.OutboundOrders.SingleAsync(o => o.ExternalOrderReference == salesUnusedDestExtRef);
            if (salesRow.DestinationFacilityId.HasValue)
                throw new InvalidOperationException("Ordinary B2BSalesOrder persisted non-null DestinationFacilityId.");
        }

        // 3. Allocate with Idempotency + duplicate replay check
        var allocKey = $"pg-741-alloc-{Guid.NewGuid():N}";
        using (var allocResp = await client.SendAsync(staffReqFactory(
            HttpMethod.Post, $"/api/v1/outbound/orders/{orderId}/allocate", staffUserId, facilityId, Permissions.Outbound.OrdersAllocate, new { strategyOverride = "FIFO", idempotencyKey = allocKey })))
        {
            if (allocResp.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException($"Phase 7.4.1 Allocate failed: {allocResp.StatusCode}: {await allocResp.Content.ReadAsStringAsync()}");
            using var allocDoc = await JsonDocument.ParseAsync(await allocResp.Content.ReadAsStreamAsync());
            if (allocDoc.RootElement.GetProperty("totalQuantityAllocated").GetDecimal() != 6m)
                throw new InvalidOperationException("Phase 7.4.1 Allocate did not allocate exact 6 units.");
        }

        using (var dupAllocResp = await client.SendAsync(staffReqFactory(
            HttpMethod.Post, $"/api/v1/outbound/orders/{orderId}/allocate", staffUserId, facilityId, Permissions.Outbound.OrdersAllocate, new { strategyOverride = "FIFO", idempotencyKey = allocKey })))
        {
            if (dupAllocResp.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException($"Phase 7.4.1 Idempotent Allocate failed: {dupAllocResp.StatusCode}");
            using var dupDoc = await JsonDocument.ParseAsync(await dupAllocResp.Content.ReadAsStreamAsync());
            if (!dupDoc.RootElement.GetProperty("isDuplicateRequest").GetBoolean())
                throw new InvalidOperationException("Phase 7.4.1 Duplicate idempotency key did not trigger idempotent replay flag.");
        }

        // 4. Generate Pick Wave via nested { request } command payload
        Guid waveId;
        using (var waveResp = await client.SendAsync(staffReqFactory(
            HttpMethod.Post, "/api/v1/outbound/pick-waves", staffUserId, facilityId, Permissions.Outbound.WavesManage,
            new { request = new { clientId, facilityId, waveType = "BatchPick", orderIds = new[] { orderId } } })))
        {
            if (waveResp.StatusCode != HttpStatusCode.Created)
                throw new InvalidOperationException($"Phase 7.4.1 GenerateWave failed: {waveResp.StatusCode}: {await waveResp.Content.ReadAsStringAsync()}");
            using var waveDoc = await JsonDocument.ParseAsync(await waveResp.Content.ReadAsStreamAsync());
            waveId = waveDoc.RootElement.GetProperty("id").GetGuid();
            if (!string.Equals(waveDoc.RootElement.GetProperty("status").GetString(), "Planning", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Newly generated wave must start in Planning status.");
        }

        // 5. Release Wave -> wave Released, tasks remain Pending (no auto-claim), stock ledger unchanged
        using (var relResp = await client.SendAsync(staffReqFactory(
            HttpMethod.Post, $"/api/v1/outbound/pick-waves/{waveId}/release", staffUserId, facilityId, Permissions.Outbound.WavesManage, new { })))
        {
            if (relResp.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException($"Phase 7.4.1 ReleaseWave failed: {relResp.StatusCode}: {await relResp.Content.ReadAsStringAsync()}");
            using var relDoc = await JsonDocument.ParseAsync(await relResp.Content.ReadAsStreamAsync());
            if (!string.Equals(relDoc.RootElement.GetProperty("status").GetString(), "Released", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Released wave status did not transition to Released.");
        }

        await using (var verifyDb = new ApplicationDbContext(
            dbOptions, new VerificationTenant(), new VerificationIdentity(facilityId),
            new AuditableEntityInterceptor(new VerificationIdentity(facilityId), new VerificationClock())))
        {
            var persistedOrder = await verifyDb.OutboundOrders.SingleAsync(o => o.Id == orderId);
            var persistedWave = await verifyDb.PickWaves.Include(w => w.Tasks).SingleAsync(w => w.Id == waveId);
            var persistedStock = await verifyDb.StockOnHand.SingleAsync(s => s.Id == stockId);
            var finalTxCount = await verifyDb.InventoryTransactions.CountAsync();

            if (persistedOrder.Status != OutboundOrderStatus.Allocated)
                throw new InvalidOperationException($"Expected persisted Order status Allocated, got {persistedOrder.Status}");
            if (persistedWave.Status != PickWaveStatus.Released || persistedWave.Tasks.Count == 0 || persistedWave.Tasks.Any(t => t.Status != PickTaskStatus.Pending || t.AssignedUserId != null))
                throw new InvalidOperationException("Released wave tasks must remain Pending and unclaimed in Phase 7.4.1.");
            if (persistedStock.QtyAllocated != 6m || persistedStock.QtyAvailable != 19m || persistedStock.QtyAllocated + persistedStock.QtyAvailable != initialTotalQty)
                throw new InvalidOperationException("Allocation stock reservation balance mismatch on PostgreSQL.");
            if (finalTxCount != initialTxCount)
                throw new InvalidOperationException("Phase 7.4.1 order/allocation/wave release must not write inventory ledger transactions.");
        }

        Console.WriteLine("PASS PostgreSQL Phase 7.4.1 Create Order -> Stock Availability -> Allocate -> Generate Wave -> Release Wave verified cleanly.");
    }
}
