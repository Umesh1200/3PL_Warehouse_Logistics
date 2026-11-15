using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Services.Analytics;
using Warehouse3PL.UnitTests.Common;
using Xunit;

namespace Warehouse3PL.UnitTests.Analytics;

public class ProductAffinityAndCoLocationRulesTests
{
    private class AffinityTestFixture
    {
        public string DbName { get; } = $"Affinity_TestDb_{Guid.NewGuid():N}";
        public Guid FacilityId { get; set; } = Guid.NewGuid();
        public Guid Facility2Id { get; set; } = Guid.NewGuid();
        public Guid ClientId { get; set; } = Guid.NewGuid();
        public Guid Client2Id { get; set; } = Guid.NewGuid();

        public Guid PickZoneId { get; set; } = Guid.NewGuid();
        public Guid ReserveZoneId { get; set; } = Guid.NewGuid();

        public Guid LocationAisle1Rack1Id { get; set; } = Guid.NewGuid();
        public Guid LocationAisle1Rack2Id { get; set; } = Guid.NewGuid();
        public Guid LocationAisle1Rack5Id { get; set; } = Guid.NewGuid();
        public Guid LocationAisle5Rack1Id { get; set; } = Guid.NewGuid();
    }

    private static ApplicationDbContext CreateContext(
        string dbName,
        Guid? tenantId = null,
        Guid? facilityId = null,
        bool isStaff = true,
        bool isSuperAdmin = false)
    {
        var currentUser = new TestCurrentUserContext
        {
            TenantId = tenantId,
            FacilityId = facilityId,
            UserType = isStaff ? UserType.Staff : UserType.Client,
            RolesList = isSuperAdmin ? ["SuperAdmin"] : [],
            PermissionsList = [Permissions.Analytics.SlottingView, Permissions.Analytics.SlottingManage]
        };

        return TestDbContextFactory.Create(
            new TestTenantProvider { TenantId = tenantId, BypassTenantFilter = isStaff || isSuperAdmin },
            currentUser,
            dbName);
    }

    private static async Task<AffinityTestFixture> SeedEnvironmentAsync()
    {
        var fix = new AffinityTestFixture();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);

        var facility = new Facility
        {
            Id = fix.FacilityId,
            FacilityCode = "CHI-01",
            Name = "Chicago Logistics Hub",
            TotalAreaSqM = 30000m,
            IsActive = true
        };

        var facility2 = new Facility
        {
            Id = fix.Facility2Id,
            FacilityCode = "DAL-01",
            Name = "Dallas Distribution Center",
            TotalAreaSqM = 25000m,
            IsActive = true
        };

        var client = new Client
        {
            Id = fix.ClientId,
            AccountCode = "CLI-AFF-01",
            CompanyName = "Affinity Retail Client",
            Status = ClientStatus.Active
        };

        var client2 = new Client
        {
            Id = fix.Client2Id,
            AccountCode = "CLI-AFF-02",
            CompanyName = "Secondary Tenant Client",
            Status = ClientStatus.Active
        };

        var pickZone = new WarehouseZone
        {
            Id = fix.PickZoneId,
            FacilityId = fix.FacilityId,
            ZoneCode = "Z-PICK",
            Name = "Forward Pick Zone",
            ZoneType = WarehouseZoneType.PickFace,
            IsActive = true
        };

        var reserveZone = new WarehouseZone
        {
            Id = fix.ReserveZoneId,
            FacilityId = fix.FacilityId,
            ZoneCode = "Z-RES",
            Name = "Reserve High-Bay",
            ZoneType = WarehouseZoneType.BulkStorage,
            IsActive = true
        };

        context.Facilities.AddRange(facility, facility2);
        context.Clients.AddRange(client, client2);
        context.WarehouseZones.AddRange(pickZone, reserveZone);

        // Seed Locations with Aisle/Rack topology
        context.Locations.AddRange(
            new Location
            {
                Id = fix.LocationAisle1Rack1Id,
                FacilityId = fix.FacilityId,
                ZoneId = fix.PickZoneId,
                LocationCode = "LOC-A1-R1",
                AisleCode = "A1",
                RackCode = "1",
                ShelfLevel = 1,
                BinNumber = 1,
                XCoord = 5.0m,
                YCoord = 5.0m,
                ZCoord = 1.0m,
                LengthCm = 120m,
                WidthCm = 100m,
                HeightCm = 150m,
                MaxWeightKg = 1000m,
                MaxVolumeCm3 = 1800000m,
                IsActive = true,
                IsPickable = true
            },
            new Location
            {
                Id = fix.LocationAisle1Rack2Id,
                FacilityId = fix.FacilityId,
                ZoneId = fix.PickZoneId,
                LocationCode = "LOC-A1-R2",
                AisleCode = "A1",
                RackCode = "2",
                ShelfLevel = 1,
                BinNumber = 1,
                XCoord = 5.0m,
                YCoord = 7.0m,
                ZCoord = 1.0m,
                LengthCm = 120m,
                WidthCm = 100m,
                HeightCm = 150m,
                MaxWeightKg = 1000m,
                MaxVolumeCm3 = 1800000m,
                IsActive = true,
                IsPickable = true
            },
            new Location
            {
                Id = fix.LocationAisle1Rack5Id,
                FacilityId = fix.FacilityId,
                ZoneId = fix.PickZoneId,
                LocationCode = "LOC-A1-R5",
                AisleCode = "A1",
                RackCode = "5",
                ShelfLevel = 1,
                BinNumber = 1,
                XCoord = 5.0m,
                YCoord = 15.0m,
                ZCoord = 1.0m,
                LengthCm = 120m,
                WidthCm = 100m,
                HeightCm = 150m,
                MaxWeightKg = 1000m,
                MaxVolumeCm3 = 1800000m,
                IsActive = true,
                IsPickable = true
            },
            new Location
            {
                Id = fix.LocationAisle5Rack1Id,
                FacilityId = fix.FacilityId,
                ZoneId = fix.ReserveZoneId,
                LocationCode = "LOC-A5-R1",
                AisleCode = "A5",
                RackCode = "1",
                ShelfLevel = 3,
                BinNumber = 1,
                XCoord = 40.0m,
                YCoord = 40.0m,
                ZCoord = 5.0m,
                LengthCm = 120m,
                WidthCm = 100m,
                HeightCm = 150m,
                MaxWeightKg = 1000m,
                MaxVolumeCm3 = 1800000m,
                IsActive = true,
                IsPickable = true
            });

        await context.SaveChangesAsync();
        return fix;
    }

    #region 1. Mathematical Accuracy Tests (Workstreams 6.3.7.3.2, 6.3.7.3.3, 6.3.7.3.4)

    [Fact]
    public async Task SupportConfidenceLift_KnownDataset_CalculatesExpectedValues()
    {
        // Dataset: 50 qualifying orders total.
        // Item A appears in 20 orders.
        // Item B appears in 25 orders.
        // Both A and B appear together in 15 orders.
        // Expected:
        //   supp(A) = 20 / 50 = 0.4000
        //   supp(B) = 25 / 50 = 0.5000
        //   supp(A, B) = 15 / 50 = 0.3000
        //   conf(A -> B) = 15 / 20 = 0.7500
        //   conf(B -> A) = 15 / 25 = 0.6000
        //   maxConf = 0.7500
        //   kulc = 0.5 * (0.7500 + 0.6000) = 0.6750
        //   lift = (15 * 50) / (20 * 25) = 750 / 500 = 1.5000
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var miningService = new ProductAffinityMiningService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<ProductAffinityMiningService>.Instance);

        var itemA = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-MATH-A", Description = "Item A" };
        var itemB = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-MATH-B", Description = "Item B" };
        var itemC = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-MATH-C", Description = "Filler C" };
        context.Items.AddRange(itemA, itemB, itemC);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        // 50 Orders:
        // 1..15: A and B
        // 16..20: A only (plus C)
        // 21..30: B only (plus C)
        // 31..50: C only
        for (int i = 1; i <= 50; i++)
        {
            var order = new OutboundOrder
            {
                Id = Guid.NewGuid(),
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                OrderNumber = $"ORD-MATH-{i:D3}",
                Status = OutboundOrderStatus.Completed,
                OrderDateUtc = evalTime.AddDays(-10)
            };
            context.OutboundOrders.Add(order);

            if (i <= 15)
            {
                // Both A and B
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = order.Id, ItemId = itemA.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = order.Id, ItemId = itemB.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            }
            else if (i <= 20)
            {
                // A only
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = order.Id, ItemId = itemA.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = order.Id, ItemId = itemC.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            }
            else if (i <= 30)
            {
                // B only
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = order.Id, ItemId = itemB.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = order.Id, ItemId = itemC.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            }
            else
            {
                // C only
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = order.Id, ItemId = itemC.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            }
        }
        await context.SaveChangesAsync();

        var result = await miningService.MineAffinityAsync(new MineProductAffinityRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            AsOfDateUtc = evalTime,
            MinOrderCount = 30,
            MinCoOccurrenceCount = 3,
            MinSupport = 0.01m,
            MinConfidence = 0.10m,
            MinLift = 1.10m
        });

        Assert.NotNull(result);
        Assert.True(result.SampleSizeSufficient);
        Assert.Equal(50, result.TotalQualifyingOrders);

        var pairAB = result.SignificantPairs.FirstOrDefault(p =>
            (p.ItemAId == itemA.Id && p.ItemBId == itemB.Id) ||
            (p.ItemAId == itemB.Id && p.ItemBId == itemA.Id));

        Assert.NotNull(pairAB);
        Assert.Equal(15, pairAB.CoOccurrenceCount);
        Assert.Equal(0.3000m, pairAB.Support);
        Assert.Equal(1.5000m, pairAB.Lift);
        Assert.Equal(0.7500m, pairAB.MaxConfidence);
        Assert.Equal(0.6750m, pairAB.KulczynskiMeasure);
        Assert.True(pairAB.IsStatisticallySignificant);
        Assert.True(pairAB.ChiSquareStatistic.HasValue && pairAB.ChiSquareStatistic.Value > 3.841m);
    }

    #endregion

    #region 2. Canonical Demand Identity & Deduplication (Workstream 6.3.7.3.1)

    [Fact]
    public async Task CanonicalDemand_MultipleLinesForSameSkuInOneOrder_CountsOrderOnce()
    {
        // 35 Orders. In 10 orders, SKU A appears on 3 lines and SKU B appears on 2 lines.
        // Invariant: Canonical deduplication must count the order as 1 occurrence of A, 1 of B, 1 of (A, B).
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var miningService = new ProductAffinityMiningService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<ProductAffinityMiningService>.Instance);

        var itemA = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-DEDUP-A", Description = "Item A" };
        var itemB = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-DEDUP-B", Description = "Item B" };
        var itemC = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-DEDUP-C", Description = "Filler C" };
        context.Items.AddRange(itemA, itemB, itemC);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        for (int i = 1; i <= 35; i++)
        {
            var order = new OutboundOrder
            {
                Id = Guid.NewGuid(),
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                OrderNumber = $"ORD-DEDUP-{i:D3}",
                Status = OutboundOrderStatus.Completed,
                OrderDateUtc = evalTime.AddDays(-5)
            };
            context.OutboundOrders.Add(order);

            if (i <= 10)
            {
                // Multi-line repeat allocations for same SKU
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = order.Id, ItemId = itemA.Id, QuantityOrdered = 2m, QuantityAllocated = 2m });
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = order.Id, ItemId = itemA.Id, QuantityOrdered = 5m, QuantityAllocated = 5m });
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = order.Id, ItemId = itemA.Id, QuantityOrdered = 1m, QuantityAllocated = 1m });

                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = order.Id, ItemId = itemB.Id, QuantityOrdered = 10m, QuantityAllocated = 10m });
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = order.Id, ItemId = itemB.Id, QuantityOrdered = 20m, QuantityAllocated = 20m });
            }
            else if (i <= 20)
            {
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = order.Id, ItemId = itemA.Id, QuantityOrdered = 1m, QuantityAllocated = 1m });
            }
            else
            {
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = order.Id, ItemId = itemC.Id, QuantityOrdered = 1m, QuantityAllocated = 1m });
            }
        }
        await context.SaveChangesAsync();

        var result = await miningService.MineAffinityAsync(new MineProductAffinityRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            AsOfDateUtc = evalTime,
            MinOrderCount = 30,
            MinClusterLift = 1.15m
        });

        Assert.NotNull(result);
        Assert.Equal(35, result.TotalQualifyingOrders);

        var pairAB = result.SignificantPairs.FirstOrDefault(p =>
            (p.ItemAId == itemA.Id && p.ItemBId == itemB.Id) ||
            (p.ItemAId == itemB.Id && p.ItemBId == itemA.Id));

        Assert.NotNull(pairAB);
        // Co-occurrence must be exactly 10, not multiplied by 3 * 2 = 60!
        Assert.Equal(10, pairAB.CoOccurrenceCount);
        int aCount = pairAB.ItemAId == itemA.Id ? pairAB.ItemAOrderCount : pairAB.ItemBOrderCount;
        int bCount = pairAB.ItemAId == itemB.Id ? pairAB.ItemAOrderCount : pairAB.ItemBOrderCount;
        Assert.Equal(20, aCount);
        Assert.Equal(10, bCount);
    }

    [Fact]
    public async Task CanonicalDemand_ZeroQuantityOrCancelledLines_Ignored()
    {
        // Cancelled lines or zero-quantity lines must not establish demand basket inclusion
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var miningService = new ProductAffinityMiningService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<ProductAffinityMiningService>.Instance);

        var itemA = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-CANCEL-A", Description = "Item A" };
        var itemB = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-CANCEL-B", Description = "Item B" };
        context.Items.AddRange(itemA, itemB);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        for (int i = 1; i <= 35; i++)
        {
            var order = new OutboundOrder
            {
                Id = Guid.NewGuid(),
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                OrderNumber = $"ORD-CANCEL-{i:D3}",
                Status = OutboundOrderStatus.Completed,
                OrderDateUtc = evalTime.AddDays(-5)
            };
            context.OutboundOrders.Add(order);

            // Valid line for A
            context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = order.Id, ItemId = itemA.Id, QuantityOrdered = 5m, QuantityShipped = 5m });

            // Cancelled line for B
            context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = order.Id, ItemId = itemB.Id, QuantityOrdered = 5m, QuantityShipped = 0m, Status = OutboundOrderLineStatus.Cancelled });
        }
        await context.SaveChangesAsync();

        var result = await miningService.MineAffinityAsync(new MineProductAffinityRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            AsOfDateUtc = evalTime,
            MinOrderCount = 30,
            MinClusterLift = 1.15m
        });

        Assert.NotNull(result);
        Assert.Equal(35, result.TotalQualifyingOrders);

        // Pair AB should not exist because B had only cancelled lines
        var pairAB = result.SignificantPairs.FirstOrDefault(p =>
            (p.ItemAId == itemA.Id && p.ItemBId == itemB.Id) ||
            (p.ItemAId == itemB.Id && p.ItemBId == itemA.Id));

        Assert.Null(pairAB);
    }

    #endregion

    #region 3. Statistical Reliability & Noise Rejection (Workstreams 6.3.7.3.5, 6.3.7.3.6)

    [Fact]
    public async Task SampleSizeGate_LessThanMinOrders_FailsClosedWithoutMining()
    {
        // Sample size of 15 orders when MinOrderCount = 30 must fail closed
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var miningService = new ProductAffinityMiningService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<ProductAffinityMiningService>.Instance);

        var itemA = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-SAMPLE-A" };
        var itemB = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-SAMPLE-B" };
        context.Items.AddRange(itemA, itemB);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        for (int i = 1; i <= 15; i++)
        {
            var order = new OutboundOrder
            {
                Id = Guid.NewGuid(),
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                OrderNumber = $"ORD-LOWSAMPLE-{i}",
                Status = OutboundOrderStatus.Completed,
                OrderDateUtc = evalTime.AddDays(-2)
            };
            context.OutboundOrders.Add(order);
            context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = order.Id, ItemId = itemA.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = order.Id, ItemId = itemB.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
        }
        await context.SaveChangesAsync();

        var result = await miningService.MineAffinityAsync(new MineProductAffinityRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            AsOfDateUtc = evalTime,
            MinOrderCount = 30 // Threshold is 30, we have 15
        });

        Assert.NotNull(result);
        Assert.False(result.SampleSizeSufficient);
        Assert.Equal(15, result.TotalQualifyingOrders);
        Assert.Empty(result.SignificantPairs);
        Assert.Empty(result.Clusters);
        Assert.Contains("below the required minimum", result.StatusMessage);
    }

    [Fact]
    public async Task RarePairProtection_CoOccurrenceBelowThreshold_MarkedNotSignificant()
    {
        // 40 orders. Pair co-occurs 2 times when MinCoOccurrenceCount = 3.
        // Must be marked IsStatisticallySignificant = false and excluded from clusters.
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var miningService = new ProductAffinityMiningService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<ProductAffinityMiningService>.Instance);

        var itemA = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-RARE-A" };
        var itemB = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-RARE-B" };
        var itemC = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-RARE-C" };
        context.Items.AddRange(itemA, itemB, itemC);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        for (int i = 1; i <= 40; i++)
        {
            var order = new OutboundOrder
            {
                Id = Guid.NewGuid(),
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                OrderNumber = $"ORD-RARE-{i}",
                Status = OutboundOrderStatus.Completed,
                OrderDateUtc = evalTime.AddDays(-5)
            };
            context.OutboundOrders.Add(order);

            if (i <= 2)
            {
                // A and B co-occur only 2 times!
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = order.Id, ItemId = itemA.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = order.Id, ItemId = itemB.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            }
            else
            {
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = order.Id, ItemId = itemC.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            }
        }
        await context.SaveChangesAsync();

        var result = await miningService.MineAffinityAsync(new MineProductAffinityRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            AsOfDateUtc = evalTime,
            MinOrderCount = 30,
            MinCoOccurrenceCount = 3 // Minimum 3 required
        });

        Assert.NotNull(result);
        Assert.True(result.SampleSizeSufficient);

        // The pair (A, B) had count 2 < 3 -> should NOT appear in SignificantPairs
        var pairAB = result.SignificantPairs.FirstOrDefault(p =>
            (p.ItemAId == itemA.Id && p.ItemBId == itemB.Id) ||
            (p.ItemAId == itemB.Id && p.ItemBId == itemA.Id));

        Assert.Null(pairAB);
        Assert.Empty(result.Clusters);
    }

    [Fact]
    public async Task RarePairProtection_SingleOrderCoincidence_Disqualified()
    {
        // Single order coincidence (e.g. 1 order contains SKU X and SKU Y out of 50 orders)
        // Confidence could be 100%, but co-occurrence = 1 fails MinCoOccurrenceCount >= 3.
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var miningService = new ProductAffinityMiningService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<ProductAffinityMiningService>.Instance);

        var itemX = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-COINCIDENCE-X" };
        var itemY = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-COINCIDENCE-Y" };
        var itemZ = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-COINCIDENCE-Z" };
        context.Items.AddRange(itemX, itemY, itemZ);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        for (int i = 1; i <= 50; i++)
        {
            var order = new OutboundOrder
            {
                Id = Guid.NewGuid(),
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                OrderNumber = $"ORD-COINCIDENCE-{i}",
                Status = OutboundOrderStatus.Completed,
                OrderDateUtc = evalTime.AddDays(-3)
            };
            context.OutboundOrders.Add(order);

            if (i == 1)
            {
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = order.Id, ItemId = itemX.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = order.Id, ItemId = itemY.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            }
            else
            {
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = order.Id, ItemId = itemZ.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            }
        }
        await context.SaveChangesAsync();

        var result = await miningService.MineAffinityAsync(new MineProductAffinityRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            AsOfDateUtc = evalTime,
            MinOrderCount = 30,
            MinCoOccurrenceCount = 3
        });

        Assert.NotNull(result);
        Assert.Empty(result.SignificantPairs);
        Assert.Empty(result.Clusters);
    }

    [Fact]
    public async Task IndependentOrNegativeLift_BelowThreshold_Disqualified()
    {
        // If two items appear together with lift <= 1.0 (independence or negative correlation),
        // they must be disqualified from significant affinity pairs.
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var miningService = new ProductAffinityMiningService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<ProductAffinityMiningService>.Instance);

        var itemA = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-INDEP-A" };
        var itemB = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-INDEP-B" };
        context.Items.AddRange(itemA, itemB);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        // 100 orders:
        // A in 50 orders (1..50)
        // B in 50 orders (41..90) -> co-occurrence in orders 41..50 (10 orders)
        // supp(A) = 0.50, supp(B) = 0.50, supp(A, B) = 0.10
        // lift = 0.10 / (0.50 * 0.50) = 0.10 / 0.25 = 0.4000 (Strong negative affinity!)
        for (int i = 1; i <= 100; i++)
        {
            var order = new OutboundOrder
            {
                Id = Guid.NewGuid(),
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                OrderNumber = $"ORD-INDEP-{i}",
                Status = OutboundOrderStatus.Completed,
                OrderDateUtc = evalTime.AddDays(-10)
            };
            context.OutboundOrders.Add(order);

            if (i <= 50)
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = order.Id, ItemId = itemA.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            if (i >= 41 && i <= 90)
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = order.Id, ItemId = itemB.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
        }
        await context.SaveChangesAsync();

        var result = await miningService.MineAffinityAsync(new MineProductAffinityRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            AsOfDateUtc = evalTime,
            MinOrderCount = 30,
            MinCoOccurrenceCount = 3,
            MinLift = 1.10m // Requires positive lift > 1.10
        });

        Assert.NotNull(result);
        var pairAB = result.SignificantPairs.FirstOrDefault(p =>
            (p.ItemAId == itemA.Id && p.ItemBId == itemB.Id) ||
            (p.ItemAId == itemB.Id && p.ItemBId == itemA.Id));

        // Lift = 0.4000 < 1.1000 -> must NOT be in SignificantPairs
        Assert.Null(pairAB);
    }

    #endregion

    #region 4. Multi-Tenant & Facility Isolation (Workstream 6.3.7.3.7)

    [Fact]
    public async Task TenantIsolation_AffinityNeverCombinesAcrossClients()
    {
        // Client 1 has 40 orders containing SKU-T1-A and SKU-T1-B.
        // Client 2 has 40 orders containing SKU-T2-C and SKU-T2-D.
        // Mining for Client 1 must strictly return only Client 1's pairs and clusters.
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var miningService = new ProductAffinityMiningService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<ProductAffinityMiningService>.Instance);

        var itemT1A = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-T1-A" };
        var itemT1B = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-T1-B" };
        var itemT2C = new Item { Id = Guid.NewGuid(), ClientId = fix.Client2Id, Sku = "SKU-T2-C" };
        var itemT2D = new Item { Id = Guid.NewGuid(), ClientId = fix.Client2Id, Sku = "SKU-T2-D" };
        context.Items.AddRange(itemT1A, itemT1B, itemT2C, itemT2D);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        // Client 1 Orders
        for (int i = 1; i <= 40; i++)
        {
            var o1 = new OutboundOrder
            {
                Id = Guid.NewGuid(),
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                OrderNumber = $"ORD-T1-{i}",
                Status = OutboundOrderStatus.Completed,
                OrderDateUtc = evalTime.AddDays(-2)
            };
            context.OutboundOrders.Add(o1);
            context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o1.Id, ItemId = itemT1A.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o1.Id, ItemId = itemT1B.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
        }

        // Client 2 Orders
        for (int i = 1; i <= 40; i++)
        {
            var o2 = new OutboundOrder
            {
                Id = Guid.NewGuid(),
                ClientId = fix.Client2Id,
                FacilityId = fix.FacilityId,
                OrderNumber = $"ORD-T2-{i}",
                Status = OutboundOrderStatus.Completed,
                OrderDateUtc = evalTime.AddDays(-2)
            };
            context.OutboundOrders.Add(o2);
            context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o2.Id, ItemId = itemT2C.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o2.Id, ItemId = itemT2D.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
        }
        await context.SaveChangesAsync();

        var resultClient1 = await miningService.MineAffinityAsync(new MineProductAffinityRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            AsOfDateUtc = evalTime,
            MinOrderCount = 30
        });

        Assert.NotNull(resultClient1);
        Assert.Equal(40, resultClient1.TotalQualifyingOrders);
        // All pairs in Client 1 mining must belong to Client 1
        Assert.All(resultClient1.SignificantPairs, p => Assert.Equal(fix.ClientId, p.ClientId));
        Assert.DoesNotContain(resultClient1.SignificantPairs, p => p.ItemASku.Contains("T2") || p.ItemBSku.Contains("T2"));
    }

    [Fact]
    public async Task FacilityIsolation_AffinityNeverCombinesAcrossFacilities()
    {
        // Facility 1 has 40 orders containing SKU-A and SKU-B.
        // Facility 2 has 40 orders containing SKU-A and SKU-C.
        // Mining Facility 1 must not include Facility 2's demand.
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var miningService = new ProductAffinityMiningService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<ProductAffinityMiningService>.Instance);

        var itemA = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-FAC-A" };
        var itemB = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-FAC-B" };
        var itemC = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-FAC-C" };
        var itemD = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-FAC-D" };
        context.Items.AddRange(itemA, itemB, itemC, itemD);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        // Facility 1 orders: 20 orders A + B, 20 orders D filler
        for (int i = 1; i <= 40; i++)
        {
            var o1 = new OutboundOrder
            {
                Id = Guid.NewGuid(),
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                OrderNumber = $"ORD-F1-{i}",
                Status = OutboundOrderStatus.Completed,
                OrderDateUtc = evalTime.AddDays(-3)
            };
            context.OutboundOrders.Add(o1);
            if (i <= 20)
            {
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o1.Id, ItemId = itemA.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o1.Id, ItemId = itemB.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            }
            else
            {
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o1.Id, ItemId = itemD.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            }
        }

        // Facility 2 orders: A + C
        for (int i = 1; i <= 40; i++)
        {
            var o2 = new OutboundOrder
            {
                Id = Guid.NewGuid(),
                ClientId = fix.ClientId,
                FacilityId = fix.Facility2Id, // DIFFERENT FACILITY
                OrderNumber = $"ORD-F2-{i}",
                Status = OutboundOrderStatus.Completed,
                OrderDateUtc = evalTime.AddDays(-3)
            };
            context.OutboundOrders.Add(o2);
            context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o2.Id, ItemId = itemA.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o2.Id, ItemId = itemC.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
        }
        await context.SaveChangesAsync();

        var resultFac1 = await miningService.MineAffinityAsync(new MineProductAffinityRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            AsOfDateUtc = evalTime,
            MinOrderCount = 30
        });

        Assert.NotNull(resultFac1);
        Assert.Equal(40, resultFac1.TotalQualifyingOrders);
        Assert.Contains(resultFac1.SignificantPairs, p => (p.ItemASku == "SKU-FAC-A" && p.ItemBSku == "SKU-FAC-B") || (p.ItemASku == "SKU-FAC-B" && p.ItemBSku == "SKU-FAC-A"));
        Assert.DoesNotContain(resultFac1.SignificantPairs, p => p.ItemASku == "SKU-FAC-C" || p.ItemBSku == "SKU-FAC-C");
    }

    #endregion

    #region 5. Seasonal Segmentation & Time-Window Segmentation (Workstream 6.3.7.3.9)

    [Fact]
    public async Task SeasonalWindow_ExcludesOrdersOutsideDateRange()
    {
        // 40 orders inside the 60-day window, 20 orders dated 120 days ago.
        // Evaluated qualifying orders must be exactly 40.
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var miningService = new ProductAffinityMiningService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<ProductAffinityMiningService>.Instance);

        var itemA = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-SEASON-A" };
        var itemB = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-SEASON-B" };
        context.Items.AddRange(itemA, itemB);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        // 40 recent orders (within 60 days)
        for (int i = 1; i <= 40; i++)
        {
            var o = new OutboundOrder
            {
                Id = Guid.NewGuid(),
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                OrderNumber = $"ORD-RECENT-{i}",
                Status = OutboundOrderStatus.Completed,
                OrderDateUtc = evalTime.AddDays(-15)
            };
            context.OutboundOrders.Add(o);
            context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemA.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemB.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
        }

        // 20 old orders (120 days ago)
        for (int i = 1; i <= 20; i++)
        {
            var o = new OutboundOrder
            {
                Id = Guid.NewGuid(),
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                OrderNumber = $"ORD-OLD-{i}",
                Status = OutboundOrderStatus.Completed,
                OrderDateUtc = evalTime.AddDays(-120)
            };
            context.OutboundOrders.Add(o);
            context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemA.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemB.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
        }
        await context.SaveChangesAsync();

        var result = await miningService.MineAffinityAsync(new MineProductAffinityRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            AsOfDateUtc = evalTime,
            AnalysisWindowDays = 60 // 60 day window
        });

        Assert.NotNull(result);
        Assert.Equal(40, result.TotalQualifyingOrders);
    }

    #endregion

    #region 6. Deterministic Clustering & Anchor Selection (Workstream 6.3.7.3.8)

    [Fact]
    public async Task DeterministicClustering_IdenticalInputs_ProducesIdenticalClustersAndCodes()
    {
        // Executing mining twice on identical dataset must produce identical cluster count, codes, and member lists
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var miningService = new ProductAffinityMiningService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<ProductAffinityMiningService>.Instance);

        var itemA = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-CLUSTER-A" };
        var itemB = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-CLUSTER-B" };
        var itemC = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-CLUSTER-C" };
        context.Items.AddRange(itemA, itemB, itemC);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        for (int i = 1; i <= 50; i++)
        {
            var o = new OutboundOrder
            {
                Id = Guid.NewGuid(),
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                OrderNumber = $"ORD-CLUST-{i}",
                Status = OutboundOrderStatus.Completed,
                OrderDateUtc = evalTime.AddDays(-5)
            };
            context.OutboundOrders.Add(o);
            context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemA.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemB.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemC.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
        }
        await context.SaveChangesAsync();

        var run1 = await miningService.MineAffinityAsync(new MineProductAffinityRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            AsOfDateUtc = evalTime,
            SaveMinedAffinity = false
        });

        var run2 = await miningService.MineAffinityAsync(new MineProductAffinityRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            AsOfDateUtc = evalTime,
            SaveMinedAffinity = false
        });

        Assert.Equal(run1.Clusters.Count, run2.Clusters.Count);
        for (int i = 0; i < run1.Clusters.Count; i++)
        {
            var c1 = run1.Clusters[i];
            var c2 = run2.Clusters[i];
            Assert.Equal(c1.ClusterCode, c2.ClusterCode);
            Assert.Equal(c1.PrimaryAnchorSku, c2.PrimaryAnchorSku);
            Assert.Equal(c1.ItemCount, c2.ItemCount);
            Assert.Equal(c1.AverageLift, c2.AverageLift);
        }
    }

    [Fact]
    public async Task AnchorSkuSelection_ItemWithHighestWithinClusterLift_SelectedAsAnchor()
    {
        // In cluster {A, B, C}:
        // A co-occurs with B in 30 orders and with C in 30 orders (A is the central connector)
        // B and C co-occur with A, but B and C only co-occur together in 15 orders.
        // A has the highest sum of within-cluster lift and pick frequency -> A must be chosen as PrimaryAnchorSku.
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var miningService = new ProductAffinityMiningService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<ProductAffinityMiningService>.Instance);

        var itemA = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-ANCHOR-A" };
        var itemB = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-ANCHOR-B" };
        var itemC = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-ANCHOR-C" };
        var itemD = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-ANCHOR-D" };
        context.Items.AddRange(itemA, itemB, itemC, itemD);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        for (int i = 1; i <= 60; i++)
        {
            var o = new OutboundOrder
            {
                Id = Guid.NewGuid(),
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                OrderNumber = $"ORD-ANCHOR-{i}",
                Status = OutboundOrderStatus.Completed,
                OrderDateUtc = evalTime.AddDays(-5)
            };
            context.OutboundOrders.Add(o);

            if (i <= 30)
            {
                // A + B
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemA.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemB.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            }
            else if (i <= 50)
            {
                // A + C
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemA.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemC.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            }
            else
            {
                // D filler
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemD.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            }
        }
        await context.SaveChangesAsync();

        var result = await miningService.MineAffinityAsync(new MineProductAffinityRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            AsOfDateUtc = evalTime,
            MinOrderCount = 30,
            MinClusterLift = 1.15m
        });

        Assert.NotNull(result);
        var cluster = result.Clusters.FirstOrDefault(c => c.Members.Any(m => m.Sku == "SKU-ANCHOR-A"));
        Assert.NotNull(cluster);
        Assert.Equal("SKU-ANCHOR-A", cluster.PrimaryAnchorSku);
    }

    [Fact]
    public async Task MaxClusterSize_EnforcedWithoutOverflow()
    {
        // 5 highly affiliated items. If MaxClusterSize = 3, engine must not form a cluster with > 3 items.
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var miningService = new ProductAffinityMiningService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<ProductAffinityMiningService>.Instance);

        var items = new List<Item>();
        for (int k = 1; k <= 5; k++)
        {
            items.Add(new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = $"SKU-SIZE-{k}" });
        }
        context.Items.AddRange(items);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        for (int i = 1; i <= 40; i++)
        {
            var o = new OutboundOrder
            {
                Id = Guid.NewGuid(),
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                OrderNumber = $"ORD-SIZE-{i}",
                Status = OutboundOrderStatus.Completed,
                OrderDateUtc = evalTime.AddDays(-2)
            };
            context.OutboundOrders.Add(o);
            foreach (var item in items)
            {
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = item.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            }
        }
        await context.SaveChangesAsync();

        var result = await miningService.MineAffinityAsync(new MineProductAffinityRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            AsOfDateUtc = evalTime,
            MinOrderCount = 30,
            MaxClusterSize = 3 // Max 3 items per cluster
        });

        Assert.NotNull(result);
        Assert.All(result.Clusters, c => Assert.True(c.ItemCount <= 3));
    }

    #endregion

    #region 7. Slotting Suitability Engine Integration (Workstream 6.3.7.3.10)

    [Fact]
    public async Task CoLocationSuitability_SameAisleAndRack_AwardsMaximumAffinityBonus()
    {
        // When item A belongs to a cluster with item B, and item B is slotted at LOC-A1-R1 (Aisle A1, Rack 1):
        // Evaluating item A at LOC-A1-R1 awards maximum 100.0 pts for CoLocationAffinityScore.
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var slottingService = new SlottingOptimizationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<SlottingOptimizationService>.Instance);

        var itemA = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-COLOC-A", WeightKg = 5m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m, AbcVelocity = AbcVelocity.A };
        itemA.RecalculateVolume();
        var itemB = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-COLOC-B", WeightKg = 5m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m, AbcVelocity = AbcVelocity.A };
        itemB.RecalculateVolume();
        context.Items.AddRange(itemA, itemB);

        // Seed stock for Item B at LOC-A1-R1
        context.StockOnHand.Add(new StockOnHand
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = itemB.Id,
            LocationId = fix.LocationAisle1Rack1Id,
            QtyAvailable = 5m
        });

        // Seed Cluster containing A and B
        var cluster = new ProductAffinityCluster
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ClusterCode = "PAC-001",
            PrimaryAnchorItemId = itemB.Id,
            PrimaryAnchorSku = itemB.Sku,
            ItemCount = 2,
            AverageLift = 2.5m,
            MaxLift = 2.5m
        };
        cluster.Members.Add(new ProductAffinityClusterMember { ClusterId = cluster.Id, ItemId = itemA.Id, Sku = itemA.Sku, IsAnchor = false });
        cluster.Members.Add(new ProductAffinityClusterMember { ClusterId = cluster.Id, ItemId = itemB.Id, Sku = itemB.Sku, IsAnchor = true });
        context.ProductAffinityClusters.Add(cluster);

        // Create policy with WeightProductAffinity = 0.20
        var policy = SlottingPolicy.Create(
            "Affinity Slotting Policy",
            fix.FacilityId,
            primaryGoal: SlottingOptimizationGoal.WeightedComposite,
            weightTravelDistance: 0.20m,
            weightErgonomics: 0.20m,
            weightPickDensity: 0.20m,
            weightAbcAffinity: 0.20m,
            weightVelocityAffinity: 0.0m,
            weightCubeFill: 0.0m,
            weightProductAffinity: 0.20m);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        var result = await slottingService.EvaluateSkuLocationCompatibilityAsync(
            itemA.Id, fix.LocationAisle1Rack1Id, policy.Id, null, evalTime);

        Assert.NotNull(result);
        Assert.True(result.IsOverallCompatible);
        Assert.Equal(100.0m, result.CoLocationAffinityScore);
    }

    [Fact]
    public async Task CoLocationSuitability_AdjacentRack_AwardsHighAffinityBonus()
    {
        // Partner item B is at Rack 1. Candidate location is Rack 2 (adjacent bay).
        // CoLocationAffinityScore should be 85.0 pts.
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var slottingService = new SlottingOptimizationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<SlottingOptimizationService>.Instance);

        var itemA = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-ADJ-A", WeightKg = 5m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m, AbcVelocity = AbcVelocity.A };
        var itemB = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-ADJ-B", WeightKg = 5m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m, AbcVelocity = AbcVelocity.A };
        context.Items.AddRange(itemA, itemB);

        // Seed stock for Item B at LOC-A1-R1
        context.StockOnHand.Add(new StockOnHand
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = itemB.Id,
            LocationId = fix.LocationAisle1Rack1Id, // Aisle 1, Rack 1
            QtyAvailable = 50m
        });

        // Seed Cluster
        var cluster = new ProductAffinityCluster
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ClusterCode = "PAC-002",
            PrimaryAnchorItemId = itemB.Id,
            PrimaryAnchorSku = itemB.Sku,
            ItemCount = 2,
            AverageLift = 2.0m,
            MaxLift = 2.0m
        };
        cluster.Members.Add(new ProductAffinityClusterMember { ClusterId = cluster.Id, ItemId = itemA.Id, Sku = itemA.Sku, IsAnchor = false });
        cluster.Members.Add(new ProductAffinityClusterMember { ClusterId = cluster.Id, ItemId = itemB.Id, Sku = itemB.Sku, IsAnchor = true });
        context.ProductAffinityClusters.Add(cluster);

        var policy = SlottingPolicy.Create("Affinity Policy", fix.FacilityId, weightProductAffinity: 0.20m);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        // Evaluate candidate location LOC-A1-R2 (Aisle 1, Rack 2 - adjacent rack)
        var result = await slottingService.EvaluateSkuLocationCompatibilityAsync(
            itemA.Id, fix.LocationAisle1Rack2Id, policy.Id, null, evalTime);

        Assert.NotNull(result);
        Assert.Equal(85.0m, result.CoLocationAffinityScore);
    }

    [Fact]
    public async Task CoLocationSuitability_RemoteLocation_AwardsDistancePenalizedScore()
    {
        // Partner item B is at Rack 1 (Aisle 1). Candidate location is at Aisle 5, 40 meters away.
        // CoLocationAffinityScore should be lower than adjacent rack.
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var slottingService = new SlottingOptimizationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<SlottingOptimizationService>.Instance);

        var itemA = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-DIST-A", WeightKg = 5m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m };
        var itemB = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-DIST-B", WeightKg = 5m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m };
        context.Items.AddRange(itemA, itemB);

        // Stock at Aisle 1, Rack 1
        context.StockOnHand.Add(new StockOnHand
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = itemB.Id,
            LocationId = fix.LocationAisle1Rack1Id,
            QtyAvailable = 50m
        });

        var cluster = new ProductAffinityCluster
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ClusterCode = "PAC-003",
            PrimaryAnchorItemId = itemB.Id,
            PrimaryAnchorSku = itemB.Sku,
            ItemCount = 2,
            AverageLift = 2.0m,
            MaxLift = 2.0m
        };
        cluster.Members.Add(new ProductAffinityClusterMember { ClusterId = cluster.Id, ItemId = itemA.Id, Sku = itemA.Sku });
        cluster.Members.Add(new ProductAffinityClusterMember { ClusterId = cluster.Id, ItemId = itemB.Id, Sku = itemB.Sku, IsAnchor = true });
        context.ProductAffinityClusters.Add(cluster);

        var policy = SlottingPolicy.Create("Affinity Policy", fix.FacilityId, weightProductAffinity: 0.20m);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        var resultAdjacent = await slottingService.EvaluateSkuLocationCompatibilityAsync(
            itemA.Id, fix.LocationAisle1Rack2Id, policy.Id, null, evalTime);

        var resultRemote = await slottingService.EvaluateSkuLocationCompatibilityAsync(
            itemA.Id, fix.LocationAisle5Rack1Id, policy.Id, null, evalTime);

        Assert.True(resultAdjacent.CoLocationAffinityScore > resultRemote.CoLocationAffinityScore);
    }

    [Fact]
    public async Task CoLocationSuitability_UnclusteredItem_ReceivesNeutralBaselineScore()
    {
        // SKU without cluster membership receives neutral 50.0m score
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var slottingService = new SlottingOptimizationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<SlottingOptimizationService>.Instance);

        var item = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-UNCLUSTERED", WeightKg = 5m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m };
        context.Items.Add(item);

        var policy = SlottingPolicy.Create("Affinity Policy", fix.FacilityId, weightProductAffinity: 0.20m);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        var result = await slottingService.EvaluateSkuLocationCompatibilityAsync(
            item.Id, fix.LocationAisle1Rack1Id, policy.Id, null, evalTime);

        Assert.NotNull(result);
        Assert.Equal(50.0m, result.CoLocationAffinityScore);
    }

    #endregion

    #region 8. Physical Hard-Gate Inviolability (Requirement 5)

    [Fact]
    public async Task HardGateInviolability_HighAffinityNeverOverridesWeightGate()
    {
        // Even with 100.0 co-location score, an overweight SKU violates hard weight gate -> totalScore = 0m.
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var slottingService = new SlottingOptimizationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<SlottingOptimizationService>.Instance);

        // Item weighs 2000kg (Location MaxWeightKg is 1000kg)
        var itemOverweight = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-OVERWEIGHT",
            WeightKg = 2000m,
            LengthCm = 50m,
            WidthCm = 50m,
            HeightCm = 50m
        };
        var itemB = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-OVERWEIGHT-PARTNER", WeightKg = 5m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m };
        context.Items.AddRange(itemOverweight, itemB);

        context.StockOnHand.Add(new StockOnHand
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = itemB.Id,
            LocationId = fix.LocationAisle1Rack1Id,
            QtyAvailable = 10m
        });

        var cluster = new ProductAffinityCluster
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ClusterCode = "PAC-HARD-WT",
            PrimaryAnchorItemId = itemB.Id,
            PrimaryAnchorSku = itemB.Sku,
            ItemCount = 2,
            AverageLift = 3.0m,
            MaxLift = 3.0m
        };
        cluster.Members.Add(new ProductAffinityClusterMember { ClusterId = cluster.Id, ItemId = itemOverweight.Id, Sku = itemOverweight.Sku });
        cluster.Members.Add(new ProductAffinityClusterMember { ClusterId = cluster.Id, ItemId = itemB.Id, Sku = itemB.Sku, IsAnchor = true });
        context.ProductAffinityClusters.Add(cluster);

        var policy = SlottingPolicy.Create("Affinity Policy", fix.FacilityId, weightProductAffinity: 0.30m);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        var result = await slottingService.EvaluateSkuLocationCompatibilityAsync(
            itemOverweight.Id, fix.LocationAisle1Rack1Id, policy.Id, null, evalTime);

        Assert.NotNull(result);
        Assert.False(result.IsPhysicallyFeasible);
        Assert.False(result.IsOverallCompatible);
        Assert.Contains(result.ViolatedHardConstraints, c => c.Contains("weight", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0m, result.CalculatedSuitabilityScore); // Hard gate zeroes score
    }

    [Fact]
    public async Task HardGateInviolability_HighAffinityNeverOverridesDimensionGate()
    {
        // Oversized item (Length 250cm vs Location 120cm) violates hard dimension gate -> totalScore = 0m.
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var slottingService = new SlottingOptimizationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<SlottingOptimizationService>.Instance);

        var itemOversized = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-OVERSIZED",
            LengthCm = 250m, // Exceeds location length (120cm) and width (100cm)
            WidthCm = 250m,
            HeightCm = 50m,
            WeightKg = 10m
        };
        var itemB = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-DIMS-PARTNER", WeightKg = 5m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m };
        context.Items.AddRange(itemOversized, itemB);

        context.StockOnHand.Add(new StockOnHand
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = itemB.Id,
            LocationId = fix.LocationAisle1Rack1Id,
            QtyAvailable = 10m
        });

        var cluster = new ProductAffinityCluster
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ClusterCode = "PAC-HARD-DIMS",
            PrimaryAnchorItemId = itemB.Id,
            PrimaryAnchorSku = itemB.Sku,
            ItemCount = 2,
            AverageLift = 3.0m,
            MaxLift = 3.0m
        };
        cluster.Members.Add(new ProductAffinityClusterMember { ClusterId = cluster.Id, ItemId = itemOversized.Id, Sku = itemOversized.Sku });
        cluster.Members.Add(new ProductAffinityClusterMember { ClusterId = cluster.Id, ItemId = itemB.Id, Sku = itemB.Sku, IsAnchor = true });
        context.ProductAffinityClusters.Add(cluster);

        var policy = SlottingPolicy.Create("Affinity Policy", fix.FacilityId, weightProductAffinity: 0.30m);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        var result = await slottingService.EvaluateSkuLocationCompatibilityAsync(
            itemOversized.Id, fix.LocationAisle1Rack1Id, policy.Id, null, evalTime);

        Assert.NotNull(result);
        Assert.False(result.IsPhysicallyFeasible);
        Assert.False(result.IsOverallCompatible);
        Assert.Contains(result.ViolatedHardConstraints, c => c.Contains("dimension", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0m, result.CalculatedSuitabilityScore);
    }

    #endregion

    #region 9. Read-Only Invariance Verification (Requirement 7 & Operational Table Audit)

    [Fact]
    public async Task ReadOnlyInvariance_AffinityMiningAndSimulation_MutatesZeroOperationalRecords()
    {
        // Audit all 8 operational ledger tables before and after mining and simulation:
        // 1. StockOnHand
        // 2. Items
        // 3. Locations
        // 4. OutboundOrders
        // 5. OutboundOrderLines
        // 6. PickTasks
        // 7. InventoryTransactions
        // 8. ReceiptLines
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var miningService = new ProductAffinityMiningService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<ProductAffinityMiningService>.Instance);
        var slottingService = new SlottingOptimizationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<SlottingOptimizationService>.Instance);

        var itemA = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-INVAR-A", WeightKg = 5m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m };
        var itemB = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-INVAR-B", WeightKg = 5m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m };
        context.Items.AddRange(itemA, itemB);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        for (int i = 1; i <= 35; i++)
        {
            var o = new OutboundOrder
            {
                Id = Guid.NewGuid(),
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                OrderNumber = $"ORD-INVAR-{i}",
                Status = OutboundOrderStatus.Completed,
                OrderDateUtc = evalTime.AddDays(-2)
            };
            context.OutboundOrders.Add(o);
            context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemA.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemB.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
        }

        context.StockOnHand.Add(new StockOnHand
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = itemA.Id,
            LocationId = fix.LocationAisle1Rack1Id,
            QtyAvailable = 100m
        });

        await context.SaveChangesAsync();

        // Capture BEFORE snapshots of all 8 operational tables
        int stockCountBefore = context.StockOnHand.Count();
        decimal totalStockQtyBefore = context.StockOnHand.Sum(s => s.QtyAvailable);
        int itemsCountBefore = context.Items.Count();
        int locationsCountBefore = context.Locations.Count();
        int ordersCountBefore = context.OutboundOrders.Count();
        int orderLinesCountBefore = context.OutboundOrderLines.Count();
        int pickTasksCountBefore = context.PickTasks.Count();
        int txCountBefore = context.InventoryTransactions.Count();
        int receiptLinesCountBefore = context.ReceiptLines.Count();

        // 1. Run Affinity Mining
        var miningResult = await miningService.MineAffinityAsync(new MineProductAffinityRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            AsOfDateUtc = evalTime,
            MinOrderCount = 30,
            SaveMinedAffinity = true
        });

        // 2. Run Slotting Simulation
        var policy = SlottingPolicy.Create("Affinity Policy", fix.FacilityId, weightProductAffinity: 0.20m);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        var simResult = await slottingService.SimulateSlottingRecommendationsAsync(new SlottingSimulationRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            PolicyId = policy.Id,
            AsOfDateUtc = evalTime
        });

        // Capture AFTER snapshots of all 8 operational tables
        int stockCountAfter = context.StockOnHand.Count();
        decimal totalStockQtyAfter = context.StockOnHand.Sum(s => s.QtyAvailable);
        int itemsCountAfter = context.Items.Count();
        int locationsCountAfter = context.Locations.Count();
        int ordersCountAfter = context.OutboundOrders.Count();
        int orderLinesCountAfter = context.OutboundOrderLines.Count();
        int pickTasksCountAfter = context.PickTasks.Count();
        int txCountAfter = context.InventoryTransactions.Count();
        int receiptLinesCountAfter = context.ReceiptLines.Count();

        // Assert strictly zero operational mutations across all 8 tables
        Assert.Equal(stockCountBefore, stockCountAfter);
        Assert.Equal(totalStockQtyBefore, totalStockQtyAfter);
        Assert.Equal(itemsCountBefore, itemsCountAfter);
        Assert.Equal(locationsCountBefore, locationsCountAfter);
        Assert.Equal(ordersCountBefore, ordersCountAfter);
        Assert.Equal(orderLinesCountBefore, orderLinesCountAfter);
        Assert.Equal(pickTasksCountBefore, pickTasksCountAfter);
        Assert.Equal(txCountBefore, txCountAfter);
        Assert.Equal(receiptLinesCountBefore, receiptLinesCountAfter);
    }

    [Fact]
    public async Task ReadOnlyInvariance_PopulatedOperationalLedger_PreservesFullRecordSnapshots()
    {
        // Deep snapshot comparison across all 8 populated operational tables before and after mining and simulation:
        // StockOnHand, Items, Locations, OutboundOrders, OutboundOrderLines, PickTasks, InventoryTransactions, ReceiptLines.
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var miningService = new ProductAffinityMiningService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<ProductAffinityMiningService>.Instance);
        var slottingService = new SlottingOptimizationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<SlottingOptimizationService>.Instance);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        // 1. Seed Items
        var item1 = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-POP-1", WeightKg = 5m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m, AbcVelocity = AbcVelocity.A };
        item1.RecalculateVolume();
        var item2 = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-POP-2", WeightKg = 6m, LengthCm = 35m, WidthCm = 25m, HeightCm = 20m, AbcVelocity = AbcVelocity.A };
        item2.RecalculateVolume();
        var item3 = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-POP-3", WeightKg = 2m, LengthCm = 20m, WidthCm = 20m, HeightCm = 15m, AbcVelocity = AbcVelocity.B };
        item3.RecalculateVolume();
        context.Items.AddRange(item1, item2, item3);

        // 2. Seed StockOnHand
        var stock1 = new StockOnHand { Id = Guid.NewGuid(), ClientId = fix.ClientId, FacilityId = fix.FacilityId, ItemId = item1.Id, LocationId = fix.LocationAisle1Rack1Id, QtyAvailable = 50m, QtyAllocated = 10m };
        var stock2 = new StockOnHand { Id = Guid.NewGuid(), ClientId = fix.ClientId, FacilityId = fix.FacilityId, ItemId = item2.Id, LocationId = fix.LocationAisle1Rack2Id, QtyAvailable = 30m, QtyAllocated = 0m };
        context.StockOnHand.AddRange(stock1, stock2);

        // 3. Seed Outbound Orders & Lines (35 qualifying orders for Item 1 + Item 2)
        var orderIds = new List<Guid>();
        for (int i = 1; i <= 35; i++)
        {
            var ord = new OutboundOrder
            {
                Id = Guid.NewGuid(),
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                OrderNumber = $"ORD-POP-{i:D3}",
                Status = OutboundOrderStatus.Completed,
                OrderDateUtc = evalTime.AddDays(-10)
            };
            context.OutboundOrders.Add(ord);
            orderIds.Add(ord.Id);

            context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = ord.Id, ItemId = item1.Id, QuantityOrdered = 2m, QuantityShipped = 2m });
            context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = ord.Id, ItemId = item2.Id, QuantityOrdered = 2m, QuantityShipped = 2m });
            if (i % 2 == 0)
            {
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = ord.Id, ItemId = item3.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            }
        }

        // 4. Seed PickTasks
        var pt1 = new PickTask { Id = Guid.NewGuid(), FacilityId = fix.FacilityId, OutboundOrderId = orderIds[0], ItemId = item1.Id, SourceLocationId = fix.LocationAisle1Rack1Id, QuantityToPick = 2m, QuantityPicked = 2m, Status = PickTaskStatus.Completed };
        var pt2 = new PickTask { Id = Guid.NewGuid(), FacilityId = fix.FacilityId, OutboundOrderId = orderIds[1], ItemId = item2.Id, SourceLocationId = fix.LocationAisle1Rack2Id, QuantityToPick = 2m, QuantityPicked = 2m, Status = PickTaskStatus.Completed };
        context.PickTasks.AddRange(pt1, pt2);

        // 5. Seed InventoryTransactions
        var tx1 = new InventoryTransaction { Id = 1L, FacilityId = fix.FacilityId, ClientId = fix.ClientId, ItemId = item1.Id, FromLocationId = fix.LocationAisle1Rack1Id, TxType = InventoryTxType.Pick, Quantity = 2m, TxTimestamp = evalTime.AddDays(-5) };
        var tx2 = new InventoryTransaction { Id = 2L, FacilityId = fix.FacilityId, ClientId = fix.ClientId, ItemId = item2.Id, ToLocationId = fix.LocationAisle1Rack2Id, TxType = InventoryTxType.Putaway, Quantity = 30m, TxTimestamp = evalTime.AddDays(-6) };
        context.InventoryTransactions.AddRange(tx1, tx2);

        // 6. Seed ReceiptLines
        var rl1 = new ReceiptLine { Id = Guid.NewGuid(), ReceivingSessionId = Guid.NewGuid(), ItemId = item1.Id, QuantityReceived = 50m, ScannedAtUtc = evalTime.AddDays(-20) };
        var rl2 = new ReceiptLine { Id = Guid.NewGuid(), ReceivingSessionId = Guid.NewGuid(), ItemId = item2.Id, QuantityReceived = 30m, ScannedAtUtc = evalTime.AddDays(-20) };
        context.ReceiptLines.AddRange(rl1, rl2);

        await context.SaveChangesAsync();

        // Capture deep snapshots before mining and simulation
        var stockSnapshotsBefore = context.StockOnHand.AsNoTracking().Select(s => new { s.Id, s.QtyAvailable, s.QtyAllocated, s.LocationId, s.ItemId }).ToList();
        var itemSnapshotsBefore = context.Items.AsNoTracking().Select(i => new { i.Id, i.Sku, i.WeightKg, i.LengthCm, i.WidthCm, i.HeightCm, i.AbcVelocity }).ToList();
        var locationSnapshotsBefore = context.Locations.AsNoTracking().Select(l => new { l.Id, l.LocationCode, l.IsActive, l.IsPickable, l.IsDamaged, l.IsLocked }).ToList();
        var orderSnapshotsBefore = context.OutboundOrders.AsNoTracking().Select(o => new { o.Id, o.OrderNumber, o.Status, o.FacilityId, o.ClientId }).ToList();
        var lineSnapshotsBefore = context.OutboundOrderLines.AsNoTracking().Select(l => new { l.Id, l.OutboundOrderId, l.ItemId, l.QuantityOrdered, l.QuantityShipped }).ToList();
        var pickSnapshotsBefore = context.PickTasks.AsNoTracking().Select(p => new { p.Id, p.Status, p.QuantityToPick, p.QuantityPicked, p.SourceLocationId }).ToList();
        var txSnapshotsBefore = context.InventoryTransactions.AsNoTracking().Select(t => new { t.Id, t.Quantity, t.TxType, t.FromLocationId, t.ToLocationId, t.ItemId }).ToList();
        var receiptSnapshotsBefore = context.ReceiptLines.AsNoTracking().Select(r => new { r.Id, r.ItemId, r.QuantityReceived, r.ScannedAtUtc }).ToList();

        // Execute 1: Mine Affinity
        var miningResult = await miningService.MineAffinityAsync(new MineProductAffinityRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            AsOfDateUtc = evalTime,
            MinOrderCount = 30,
            SaveMinedAffinity = true
        });
        Assert.NotNull(miningResult);

        // Execute 2: Compatibility Evaluation
        var policy = SlottingPolicy.Create("Audit Policy", fix.FacilityId, weightProductAffinity: 0.30m);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        var compatResult = await slottingService.EvaluateSkuLocationCompatibilityAsync(
            item1.Id, fix.LocationAisle1Rack1Id, policy.Id, null, evalTime);
        Assert.NotNull(compatResult);

        // Execute 3: Slotting Simulation
        var simResult = await slottingService.SimulateSlottingRecommendationsAsync(new SlottingSimulationRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            PolicyId = policy.Id,
            AsOfDateUtc = evalTime
        });
        Assert.NotNull(simResult);

        // Capture deep snapshots after
        var stockSnapshotsAfter = context.StockOnHand.AsNoTracking().Select(s => new { s.Id, s.QtyAvailable, s.QtyAllocated, s.LocationId, s.ItemId }).ToList();
        var itemSnapshotsAfter = context.Items.AsNoTracking().Select(i => new { i.Id, i.Sku, i.WeightKg, i.LengthCm, i.WidthCm, i.HeightCm, i.AbcVelocity }).ToList();
        var locationSnapshotsAfter = context.Locations.AsNoTracking().Select(l => new { l.Id, l.LocationCode, l.IsActive, l.IsPickable, l.IsDamaged, l.IsLocked }).ToList();
        var orderSnapshotsAfter = context.OutboundOrders.AsNoTracking().Select(o => new { o.Id, o.OrderNumber, o.Status, o.FacilityId, o.ClientId }).ToList();
        var lineSnapshotsAfter = context.OutboundOrderLines.AsNoTracking().Select(l => new { l.Id, l.OutboundOrderId, l.ItemId, l.QuantityOrdered, l.QuantityShipped }).ToList();
        var pickSnapshotsAfter = context.PickTasks.AsNoTracking().Select(p => new { p.Id, p.Status, p.QuantityToPick, p.QuantityPicked, p.SourceLocationId }).ToList();
        var txSnapshotsAfter = context.InventoryTransactions.AsNoTracking().Select(t => new { t.Id, t.Quantity, t.TxType, t.FromLocationId, t.ToLocationId, t.ItemId }).ToList();
        var receiptSnapshotsAfter = context.ReceiptLines.AsNoTracking().Select(r => new { r.Id, r.ItemId, r.QuantityReceived, r.ScannedAtUtc }).ToList();

        // Assert strictly identical records (count, values, and references) across all 8 operational ledger tables
        Assert.Equal(stockSnapshotsBefore, stockSnapshotsAfter);
        Assert.Equal(itemSnapshotsBefore, itemSnapshotsAfter);
        Assert.Equal(locationSnapshotsBefore, locationSnapshotsAfter);
        Assert.Equal(orderSnapshotsBefore, orderSnapshotsAfter);
        Assert.Equal(lineSnapshotsBefore, lineSnapshotsAfter);
        Assert.Equal(pickSnapshotsBefore, pickSnapshotsAfter);
        Assert.Equal(txSnapshotsBefore, txSnapshotsAfter);
        Assert.Equal(receiptSnapshotsBefore, receiptSnapshotsAfter);
    }

    #endregion

    #region 10. Additional Statistical Reliability, Isolation, and Hard-Gate Coverage (Reviewer Findings)

    [Fact]
    public async Task MinimumSupportThreshold_RejectsLowSupportPairs()
    {
        // 400 total orders. Pair A+B appears in 3 orders: supp(A, B) = 3 / 400 = 0.0075 < 0.0100 (MinSupport).
        // C(A, B) = 3 >= 3 and N = 400 >= 30, but low support rejects the pair.
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var miningService = new ProductAffinityMiningService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<ProductAffinityMiningService>.Instance);

        var itemA = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-SUPP-A" };
        var itemB = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-SUPP-B" };
        var itemFiller = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-SUPP-FILLER" };
        context.Items.AddRange(itemA, itemB, itemFiller);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        for (int i = 1; i <= 400; i++)
        {
            var o = new OutboundOrder
            {
                Id = Guid.NewGuid(),
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                OrderNumber = $"ORD-SUPP-{i}",
                Status = OutboundOrderStatus.Completed,
                OrderDateUtc = evalTime.AddDays(-1)
            };
            context.OutboundOrders.Add(o);

            if (i <= 3)
            {
                // A + B co-occurrence (3 orders)
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemA.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemB.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            }
            else
            {
                // Filler orders
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemFiller.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            }
        }
        await context.SaveChangesAsync();

        var result = await miningService.MineAffinityAsync(new MineProductAffinityRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            AsOfDateUtc = evalTime,
            MinOrderCount = 30,
            MinCoOccurrenceCount = 3,
            MinSupport = 0.0100m // Requires >= 1.0% support (>= 4 orders)
        });

        Assert.NotNull(result);
        Assert.Equal(400, result.TotalQualifyingOrders);
        var pair = result.SignificantPairs.FirstOrDefault(p => (p.ItemASku == "SKU-SUPP-A" && p.ItemBSku == "SKU-SUPP-B") || (p.ItemASku == "SKU-SUPP-B" && p.ItemBSku == "SKU-SUPP-A"));
        Assert.Null(pair); // Successfully rejected due to support < 0.0100
    }

    [Fact]
    public async Task MinimumConfidenceThreshold_RejectsWeakConfidencePairs()
    {
        // 100 orders. Item A in 90 orders, Item B in 90 orders. Co-occur in 4 orders.
        // conf(A -> B) = 4 / 90 = 0.044, conf(B -> A) = 4 / 90 = 0.044.
        // maxConf = 0.044 < 0.100 (MinConfidence threshold).
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var miningService = new ProductAffinityMiningService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<ProductAffinityMiningService>.Instance);

        var itemA = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-CONF-A" };
        var itemB = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-CONF-B" };
        var itemFiller = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-CONF-FILLER" };
        context.Items.AddRange(itemA, itemB, itemFiller);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        for (int i = 1; i <= 100; i++)
        {
            var o = new OutboundOrder
            {
                Id = Guid.NewGuid(),
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                OrderNumber = $"ORD-CONF-{i}",
                Status = OutboundOrderStatus.Completed,
                OrderDateUtc = evalTime.AddDays(-2)
            };
            context.OutboundOrders.Add(o);

            if (i <= 4)
            {
                // Both A and B (4 orders)
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemA.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemB.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            }
            else if (i <= 90)
            {
                // A alone
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemA.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
                // B alone in other orders
                var oB = new OutboundOrder
                {
                    Id = Guid.NewGuid(),
                    ClientId = fix.ClientId,
                    FacilityId = fix.FacilityId,
                    OrderNumber = $"ORD-CONF-B-{i}",
                    Status = OutboundOrderStatus.Completed,
                    OrderDateUtc = evalTime.AddDays(-2)
                };
                context.OutboundOrders.Add(oB);
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = oB.Id, ItemId = itemB.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            }
            else
            {
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemFiller.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            }
        }
        await context.SaveChangesAsync();

        var result = await miningService.MineAffinityAsync(new MineProductAffinityRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            AsOfDateUtc = evalTime,
            MinOrderCount = 30,
            MinCoOccurrenceCount = 3,
            MinConfidence = 0.1000m // Requires >= 10% max confidence
        });

        Assert.NotNull(result);
        var pair = result.SignificantPairs.FirstOrDefault(p => (p.ItemASku == "SKU-CONF-A" && p.ItemBSku == "SKU-CONF-B") || (p.ItemASku == "SKU-CONF-B" && p.ItemBSku == "SKU-CONF-A"));
        Assert.Null(pair); // Rejected because maxConf ~ 4.4% < 10%
    }

    [Fact]
    public async Task SparseContingencyTable_AppliesYatesCorrection_RejectsDegenerateExpectedFrequency()
    {
        // 50 orders. A in 5 orders, B in 5 orders, A+B in 4 orders.
        // Expected frequency e11 = (5 * 5) / 50 = 0.50 < 1.0 (Degenerate sparse cell per Cochran's criteria).
        // Must fail closed or apply Yates' correction.
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var miningService = new ProductAffinityMiningService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<ProductAffinityMiningService>.Instance);

        var itemA = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-SPARSE-A" };
        var itemB = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-SPARSE-B" };
        var itemFiller = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-SPARSE-FILLER" };
        context.Items.AddRange(itemA, itemB, itemFiller);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        for (int i = 1; i <= 50; i++)
        {
            var o = new OutboundOrder
            {
                Id = Guid.NewGuid(),
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                OrderNumber = $"ORD-SPARSE-{i}",
                Status = OutboundOrderStatus.Completed,
                OrderDateUtc = evalTime.AddDays(-1)
            };
            context.OutboundOrders.Add(o);

            if (i <= 4)
            {
                // A + B
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemA.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemB.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            }
            else if (i == 5)
            {
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemA.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            }
            else
            {
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemFiller.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            }
        }
        await context.SaveChangesAsync();

        var result = await miningService.MineAffinityAsync(new MineProductAffinityRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            AsOfDateUtc = evalTime,
            MinOrderCount = 30,
            MinCoOccurrenceCount = 3
        });

        Assert.NotNull(result);
        // The candidate pair has minExpected = (5 * 4) / 50 = 0.40 < 1.0 -> rejected by Cochran sparsity gate
        var pair = result.SignificantPairs.FirstOrDefault(p => (p.ItemASku == "SKU-SPARSE-A" && p.ItemBSku == "SKU-SPARSE-B") || (p.ItemASku == "SKU-SPARSE-B" && p.ItemBSku == "SKU-SPARSE-A"));
        Assert.Null(pair); // Rejected due to degenerate expected cell frequency
    }

    [Fact]
    public async Task FacilityIsolation_GuidEmptyOrdersExcludedByDefault_PreventsCrossFacilityContamination()
    {
        // 20 orders in Facility 1 (Item A + B). 20 orders with Guid.Empty (Item A + C).
        // By default (IncludeUnassignedFacilityOrders == false), Facility 1 mining evaluates ONLY the 20 Facility 1 orders.
        // Item C is not mined for Facility 1.
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var miningService = new ProductAffinityMiningService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<ProductAffinityMiningService>.Instance);

        var itemA = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-ISO-A" };
        var itemB = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-ISO-B" };
        var itemC = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-ISO-C" };
        context.Items.AddRange(itemA, itemB, itemC);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        // 25 orders in Facility 1 with Item A + Item B, and 25 filler orders in Facility 1
        var itemFiller = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-ISO-FILLER" };
        context.Items.Add(itemFiller);

        for (int i = 1; i <= 50; i++)
        {
            var o1 = new OutboundOrder
            {
                Id = Guid.NewGuid(),
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId, // Target facility
                OrderNumber = $"ORD-FAC1-{i}",
                Status = OutboundOrderStatus.Completed,
                OrderDateUtc = evalTime.AddDays(-2)
            };
            context.OutboundOrders.Add(o1);

            if (i <= 25)
            {
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o1.Id, ItemId = itemA.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o1.Id, ItemId = itemB.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            }
            else
            {
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o1.Id, ItemId = itemFiller.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            }
        }

        // 35 unassigned orders (FacilityId == Guid.Empty) with Item A + Item C
        for (int i = 1; i <= 35; i++)
        {
            var oEmpty = new OutboundOrder
            {
                Id = Guid.NewGuid(),
                ClientId = fix.ClientId,
                FacilityId = Guid.Empty, // Unassigned facility
                OrderNumber = $"ORD-EMPTY-{i}",
                Status = OutboundOrderStatus.Completed,
                OrderDateUtc = evalTime.AddDays(-2)
            };
            context.OutboundOrders.Add(oEmpty);
            context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = oEmpty.Id, ItemId = itemA.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = oEmpty.Id, ItemId = itemC.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
        }
        await context.SaveChangesAsync();

        // Mine with default policy (IncludeUnassignedFacilityOrders == false)
        var result = await miningService.MineAffinityAsync(new MineProductAffinityRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            AsOfDateUtc = evalTime,
            MinOrderCount = 30
        });

        Assert.NotNull(result);
        Assert.Equal(50, result.TotalQualifyingOrders); // ONLY the 50 Facility 1 orders scanned!
        Assert.Contains(result.SignificantPairs, p => (p.ItemASku == "SKU-ISO-A" && p.ItemBSku == "SKU-ISO-B") || (p.ItemASku == "SKU-ISO-B" && p.ItemBSku == "SKU-ISO-A"));
        Assert.DoesNotContain(result.SignificantPairs, p => p.ItemASku == "SKU-ISO-C" || p.ItemBSku == "SKU-ISO-C");
    }

    [Fact]
    public async Task FacilityIsolation_IncludeUnassignedOrdersPolicy_AllowsExplicitOptIn()
    {
        // When IncludeUnassignedFacilityOrders = true is explicitly set, Guid.Empty orders are included
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var miningService = new ProductAffinityMiningService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<ProductAffinityMiningService>.Instance);

        var itemA = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-OPT-A" };
        var itemB = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-OPT-B" };
        context.Items.AddRange(itemA, itemB);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        // 20 orders in Facility 1
        for (int i = 1; i <= 20; i++)
        {
            var o1 = new OutboundOrder
            {
                Id = Guid.NewGuid(),
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                OrderNumber = $"ORD-OPT-F1-{i}",
                Status = OutboundOrderStatus.Completed,
                OrderDateUtc = evalTime.AddDays(-2)
            };
            context.OutboundOrders.Add(o1);
            context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o1.Id, ItemId = itemA.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o1.Id, ItemId = itemB.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
        }

        // 20 unassigned orders
        for (int i = 1; i <= 20; i++)
        {
            var oEmpty = new OutboundOrder
            {
                Id = Guid.NewGuid(),
                ClientId = fix.ClientId,
                FacilityId = Guid.Empty,
                OrderNumber = $"ORD-OPT-EMP-{i}",
                Status = OutboundOrderStatus.Completed,
                OrderDateUtc = evalTime.AddDays(-2)
            };
            context.OutboundOrders.Add(oEmpty);
            context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = oEmpty.Id, ItemId = itemA.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = oEmpty.Id, ItemId = itemB.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
        }
        await context.SaveChangesAsync();

        var result = await miningService.MineAffinityAsync(new MineProductAffinityRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            AsOfDateUtc = evalTime,
            MinOrderCount = 30,
            IncludeUnassignedFacilityOrders = true // Explicit opt-in
        });

        Assert.NotNull(result);
        Assert.Equal(40, result.TotalQualifyingOrders); // 20 + 20 = 40 orders evaluated
    }

    [Fact]
    public async Task SpatialFallback_CalculatesContinuousEuclideanScoreAndBoundaryClamping()
    {
        // Tests the approved continuous 3D Euclidean spatial fallback formula:
        // S_c = clamp(100.0 - 2.5 * D, 10.0, 100.0)
        // D = 0m -> 100.0 pts
        // D = 10m -> 75.0 pts
        // D = 36m -> 10.0 pts (exact boundary)
        // D = 50m -> 10.0 pts (clamped at minimum 10.0)
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var slottingService = new SlottingOptimizationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<SlottingOptimizationService>.Instance);

        var itemA = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-SPATIAL-A", WeightKg = 5m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m, AbcVelocity = AbcVelocity.A };
        itemA.RecalculateVolume();
        var itemB = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-SPATIAL-B", WeightKg = 5m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m, AbcVelocity = AbcVelocity.A };
        itemB.RecalculateVolume();
        context.Items.AddRange(itemA, itemB);

        // Partner item B is placed at Partner Location: X=0, Y=0, Z=0 in Aisle "A01"
        var locPartner = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ZoneId = fix.PickZoneId,
            LocationCode = "LOC-PARTNER",
            AisleCode = "A01",
            RackCode = "R01",
            ShelfLevel = 1,
            XCoord = 0m,
            YCoord = 0m,
            ZCoord = 0m,
            IsActive = true,
            IsPickable = true
        };
        // Candidate 1: D = 10m (X=0, Y=10, Z=0) in Aisle "A10" (|Delta Aisle| = 9 > 1 -> spatial fallback)
        var locD10 = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ZoneId = fix.PickZoneId,
            LocationCode = "LOC-D10",
            AisleCode = "A10",
            RackCode = "R01",
            ShelfLevel = 1,
            XCoord = 0m,
            YCoord = 10m,
            ZCoord = 0m,
            IsActive = true,
            IsPickable = true
        };
        // Candidate 2: D = 36m (X=0, Y=36, Z=0) in Aisle "A10"
        var locD36 = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ZoneId = fix.PickZoneId,
            LocationCode = "LOC-D36",
            AisleCode = "A10",
            RackCode = "R01",
            ShelfLevel = 1,
            XCoord = 0m,
            YCoord = 36m,
            ZCoord = 0m,
            IsActive = true,
            IsPickable = true
        };
        // Candidate 3: D = 50m (X=0, Y=50, Z=0) in Aisle "A10"
        var locD50 = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ZoneId = fix.PickZoneId,
            LocationCode = "LOC-D50",
            AisleCode = "A10",
            RackCode = "R01",
            ShelfLevel = 1,
            XCoord = 0m,
            YCoord = 50m,
            ZCoord = 0m,
            IsActive = true,
            IsPickable = true
        };
        context.Locations.AddRange(locPartner, locD10, locD36, locD50);

        context.StockOnHand.Add(new StockOnHand
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = itemB.Id,
            LocationId = locPartner.Id,
            QtyAvailable = 5m
        });

        var cluster = new ProductAffinityCluster
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ClusterCode = "PAC-SPATIAL",
            PrimaryAnchorItemId = itemB.Id,
            PrimaryAnchorSku = itemB.Sku,
            ItemCount = 2,
            AverageLift = 2.0m,
            MaxLift = 2.0m
        };
        cluster.Members.Add(new ProductAffinityClusterMember { ClusterId = cluster.Id, ItemId = itemA.Id, Sku = itemA.Sku });
        cluster.Members.Add(new ProductAffinityClusterMember { ClusterId = cluster.Id, ItemId = itemB.Id, Sku = itemB.Sku, IsAnchor = true });
        context.ProductAffinityClusters.Add(cluster);

        // Policy with WeightProductAffinity = 1.0 (all other weights 0) to directly measure CoLocationAffinityScore
        var policy = SlottingPolicy.Create("Spatial Test Policy", fix.FacilityId,
            weightErgonomics: 0m, weightTravelDistance: 0m, weightPickDensity: 0m,
            weightAbcAffinity: 0m, weightVelocityAffinity: 0m, weightProductAffinity: 1.0m);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        // Test D = 10m: Expect S_c = 100 - 2.5 * 10 = 75.0 pts
        var res10 = await slottingService.EvaluateSkuLocationCompatibilityAsync(itemA.Id, locD10.Id, policy.Id, null, evalTime);
        Assert.NotNull(res10);
        Assert.Equal(75.0m, res10.CalculatedSuitabilityScore);

        // Test D = 36m: Expect S_c = 100 - 2.5 * 36 = 10.0 pts
        var res36 = await slottingService.EvaluateSkuLocationCompatibilityAsync(itemA.Id, locD36.Id, policy.Id, null, evalTime);
        Assert.NotNull(res36);
        Assert.Equal(10.0m, res36.CalculatedSuitabilityScore);

        // Test D = 50m: Expect S_c = clamp(100 - 125, 10.0, 100.0) = 10.0 pts (clamped at minimum)
        var res50 = await slottingService.EvaluateSkuLocationCompatibilityAsync(itemA.Id, locD50.Id, policy.Id, null, evalTime);
        Assert.NotNull(res50);
        Assert.Equal(10.0m, res50.CalculatedSuitabilityScore);
    }

    [Fact]
    public async Task SpatialFallback_MissingCoordinates_GracefullyFallsBackToTopologicalDistance()
    {
        // When coordinates are null on locations, engine gracefully calculates topological distance approximation without throwing
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var slottingService = new SlottingOptimizationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<SlottingOptimizationService>.Instance);

        var itemA = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-NOCOORD-A", WeightKg = 5m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m, AbcVelocity = AbcVelocity.A };
        itemA.RecalculateVolume();
        var itemB = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-NOCOORD-B", WeightKg = 5m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m, AbcVelocity = AbcVelocity.A };
        itemB.RecalculateVolume();
        context.Items.AddRange(itemA, itemB);

        var locPartner = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ZoneId = fix.PickZoneId,
            LocationCode = "LOC-NC-PARTNER",
            AisleCode = "A01",
            RackCode = "R01",
            ShelfLevel = 1,
            XCoord = null,
            YCoord = null,
            IsActive = true,
            IsPickable = true
        };
        var locCandidate = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ZoneId = fix.PickZoneId,
            LocationCode = "LOC-NC-CAND",
            AisleCode = "A05",
            RackCode = "R05",
            ShelfLevel = 2,
            XCoord = null,
            YCoord = null,
            IsActive = true,
            IsPickable = true
        };
        context.Locations.AddRange(locPartner, locCandidate);

        context.StockOnHand.Add(new StockOnHand
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = itemB.Id,
            LocationId = locPartner.Id,
            QtyAvailable = 5m
        });

        var cluster = new ProductAffinityCluster
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ClusterCode = "PAC-NOCOORD",
            PrimaryAnchorItemId = itemB.Id,
            PrimaryAnchorSku = itemB.Sku,
            ItemCount = 2,
            AverageLift = 2.0m,
            MaxLift = 2.0m
        };
        cluster.Members.Add(new ProductAffinityClusterMember { ClusterId = cluster.Id, ItemId = itemA.Id, Sku = itemA.Sku });
        cluster.Members.Add(new ProductAffinityClusterMember { ClusterId = cluster.Id, ItemId = itemB.Id, Sku = itemB.Sku, IsAnchor = true });
        context.ProductAffinityClusters.Add(cluster);

        var policy = SlottingPolicy.Create("No Coord Policy", fix.FacilityId,
            weightErgonomics: 0m, weightTravelDistance: 0m, weightPickDensity: 0m,
            weightAbcAffinity: 0m, weightVelocityAffinity: 0m, weightProductAffinity: 1.0m);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        var res = await slottingService.EvaluateSkuLocationCompatibilityAsync(itemA.Id, locCandidate.Id, policy.Id, null, evalTime);
        Assert.NotNull(res);
        Assert.True(res.CalculatedSuitabilityScore >= 10.0m && res.CalculatedSuitabilityScore <= 100.0m);
    }

    [Fact]
    public async Task HardGateInviolability_HighAffinityNeverOverridesTemperatureZoneGate()
    {
        // Frozen item placed in Ambient zone fails hard temperature gate (totalScore = 0m) despite high affinity
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var slottingService = new SlottingOptimizationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<SlottingOptimizationService>.Instance);

        var itemFrozen = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-FROZEN",
            StorageTempClass = StorageTempClass.Frozen, // Requires sub-zero temperature
            WeightKg = 5m,
            LengthCm = 30m,
            WidthCm = 30m,
            HeightCm = 30m
        };
        itemFrozen.RecalculateVolume();
        var itemB = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-TEMP-PARTNER", WeightKg = 5m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m };
        itemB.RecalculateVolume();
        context.Items.AddRange(itemFrozen, itemB);

        // Ambient zone with Max Temp 25°C
        var ambientZone = new WarehouseZone
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ZoneCode = "AMB-01",
            Name = "Ambient Zone",
            ZoneType = WarehouseZoneType.PickFace,
            TempMinC = 15m,
            TempMaxC = 25m
        };
        context.WarehouseZones.Add(ambientZone);

        var locAmbient = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ZoneId = ambientZone.Id,
            LocationCode = "LOC-AMB-01",
            AisleCode = "A01",
            RackCode = "R01",
            ShelfLevel = 1,
            IsActive = true,
            IsPickable = true,
            LengthCm = 120m,
            WidthCm = 100m,
            HeightCm = 150m,
            MaxWeightKg = 1000m
        };
        context.Locations.Add(locAmbient);

        context.StockOnHand.Add(new StockOnHand
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = itemB.Id,
            LocationId = locAmbient.Id,
            QtyAvailable = 5m
        });

        var cluster = new ProductAffinityCluster
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ClusterCode = "PAC-TEMP",
            PrimaryAnchorItemId = itemB.Id,
            PrimaryAnchorSku = itemB.Sku,
            ItemCount = 2,
            AverageLift = 5.0m,
            MaxLift = 5.0m
        };
        cluster.Members.Add(new ProductAffinityClusterMember { ClusterId = cluster.Id, ItemId = itemFrozen.Id, Sku = itemFrozen.Sku });
        cluster.Members.Add(new ProductAffinityClusterMember { ClusterId = cluster.Id, ItemId = itemB.Id, Sku = itemB.Sku, IsAnchor = true });
        context.ProductAffinityClusters.Add(cluster);

        var policy = SlottingPolicy.Create("Affinity Policy", fix.FacilityId, weightProductAffinity: 0.50m);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        var result = await slottingService.EvaluateSkuLocationCompatibilityAsync(
            itemFrozen.Id, locAmbient.Id, policy.Id, null, evalTime);

        Assert.NotNull(result);
        Assert.False(result.IsPhysicallyFeasible);
        Assert.False(result.IsOverallCompatible);
        Assert.Contains(result.ViolatedHardConstraints, c => c.Contains("temp", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0m, result.CalculatedSuitabilityScore); // Hard gate zeroes score
    }

    [Fact]
    public async Task HardGateInviolability_HighAffinityNeverOverridesHazardousMaterialGate()
    {
        // Flammable/Hazardous item placed in active general pick face zone fails hard hazard gate (totalScore = 0m)
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var slottingService = new SlottingOptimizationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<SlottingOptimizationService>.Instance);

        var itemHaz = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-HAZ",
            HazardClass = "Flammable Liquid", // Hazardous item
            WeightKg = 5m,
            LengthCm = 30m,
            WidthCm = 30m,
            HeightCm = 30m
        };
        itemHaz.RecalculateVolume();
        var itemB = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-HAZ-PARTNER", WeightKg = 5m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m };
        itemB.RecalculateVolume();
        context.Items.AddRange(itemHaz, itemB);

        var pickFaceZone = new WarehouseZone
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ZoneCode = "PF-GENERAL",
            Name = "General Pick Face Zone",
            ZoneType = WarehouseZoneType.PickFace
        };
        context.WarehouseZones.Add(pickFaceZone);

        var locPickFace = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ZoneId = pickFaceZone.Id,
            LocationCode = "LOC-PF-01",
            AisleCode = "A01",
            RackCode = "R01",
            ShelfLevel = 1,
            IsActive = true,
            IsPickable = true,
            LengthCm = 120m,
            WidthCm = 100m,
            HeightCm = 150m,
            MaxWeightKg = 1000m
        };
        context.Locations.Add(locPickFace);

        context.StockOnHand.Add(new StockOnHand
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = itemB.Id,
            LocationId = locPickFace.Id,
            QtyAvailable = 5m
        });

        var cluster = new ProductAffinityCluster
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ClusterCode = "PAC-HAZ",
            PrimaryAnchorItemId = itemB.Id,
            PrimaryAnchorSku = itemB.Sku,
            ItemCount = 2,
            AverageLift = 4.0m,
            MaxLift = 4.0m
        };
        cluster.Members.Add(new ProductAffinityClusterMember { ClusterId = cluster.Id, ItemId = itemHaz.Id, Sku = itemHaz.Sku });
        cluster.Members.Add(new ProductAffinityClusterMember { ClusterId = cluster.Id, ItemId = itemB.Id, Sku = itemB.Sku, IsAnchor = true });
        context.ProductAffinityClusters.Add(cluster);

        var policy = SlottingPolicy.Create("Affinity Policy", fix.FacilityId, weightProductAffinity: 0.50m);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        var result = await slottingService.EvaluateSkuLocationCompatibilityAsync(
            itemHaz.Id, locPickFace.Id, policy.Id, null, evalTime);

        Assert.NotNull(result);
        Assert.False(result.IsPhysicallyFeasible);
        Assert.False(result.IsOverallCompatible);
        Assert.Contains(result.ViolatedHardConstraints, c => c.Contains("hazard", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0m, result.CalculatedSuitabilityScore);
    }

    [Fact]
    public async Task HardGateInviolability_HighAffinityNeverOverridesStackabilityCrushGate()
    {
        // Item height exceeds location vertical clearance -> fails stackability/clearance gate (totalScore = 0m)
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var slottingService = new SlottingOptimizationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<SlottingOptimizationService>.Instance);

        var itemTall = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-TALL",
            HeightCm = 200m, // Exceeds location vertical clearance (150cm)
            LengthCm = 50m,
            WidthCm = 50m,
            WeightKg = 10m
        };
        var itemB = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-CRUSH-PARTNER", WeightKg = 5m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m };
        context.Items.AddRange(itemTall, itemB);

        var locLow = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ZoneId = fix.PickZoneId,
            LocationCode = "LOC-LOW",
            AisleCode = "A01",
            RackCode = "R01",
            ShelfLevel = 1,
            IsActive = true,
            IsPickable = true,
            LengthCm = 120m,
            WidthCm = 100m,
            HeightCm = 150m, // 150cm max height
            MaxWeightKg = 1000m
        };
        context.Locations.Add(locLow);

        context.StockOnHand.Add(new StockOnHand
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = itemB.Id,
            LocationId = locLow.Id,
            QtyAvailable = 5m
        });

        var cluster = new ProductAffinityCluster
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ClusterCode = "PAC-STACK",
            PrimaryAnchorItemId = itemB.Id,
            PrimaryAnchorSku = itemB.Sku,
            ItemCount = 2,
            AverageLift = 3.5m,
            MaxLift = 3.5m
        };
        cluster.Members.Add(new ProductAffinityClusterMember { ClusterId = cluster.Id, ItemId = itemTall.Id, Sku = itemTall.Sku });
        cluster.Members.Add(new ProductAffinityClusterMember { ClusterId = cluster.Id, ItemId = itemB.Id, Sku = itemB.Sku, IsAnchor = true });
        context.ProductAffinityClusters.Add(cluster);

        var policy = SlottingPolicy.Create("Affinity Policy", fix.FacilityId, weightProductAffinity: 0.50m);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        var result = await slottingService.EvaluateSkuLocationCompatibilityAsync(
            itemTall.Id, locLow.Id, policy.Id, null, evalTime);

        Assert.NotNull(result);
        Assert.False(result.IsPhysicallyFeasible);
        Assert.False(result.IsOverallCompatible);
        Assert.Equal(0m, result.CalculatedSuitabilityScore);
    }

    [Fact]
    public async Task HardGateInviolability_HighAffinityNeverOverridesClientTenancyGate()
    {
        // Location currently holds stock for Client 2. Item belongs to Client 1.
        // Fails hard tenancy exclusivity gate (totalScore = 0m)
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var slottingService = new SlottingOptimizationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<SlottingOptimizationService>.Instance);

        var itemClient1 = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-TENANT-1", WeightKg = 5m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m };
        var itemClient2 = new Item { Id = Guid.NewGuid(), ClientId = fix.Client2Id, Sku = "SKU-TENANT-2", WeightKg = 5m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m };
        context.Items.AddRange(itemClient1, itemClient2);

        var locShared = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ZoneId = fix.PickZoneId,
            LocationCode = "LOC-TENANT",
            AisleCode = "A01",
            RackCode = "R01",
            ShelfLevel = 1,
            IsActive = true,
            IsPickable = true,
            LengthCm = 120m,
            WidthCm = 100m,
            HeightCm = 150m,
            MaxWeightKg = 1000m
        };
        context.Locations.Add(locShared);

        // Pre-seed stock belonging to Client 2
        context.StockOnHand.Add(new StockOnHand
        {
            Id = Guid.NewGuid(),
            ClientId = fix.Client2Id,
            FacilityId = fix.FacilityId,
            ItemId = itemClient2.Id,
            LocationId = locShared.Id,
            QtyAvailable = 10m
        });

        var policy = SlottingPolicy.Create("Affinity Policy", fix.FacilityId, weightProductAffinity: 0.50m);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        var result = await slottingService.EvaluateSkuLocationCompatibilityAsync(
            itemClient1.Id, locShared.Id, policy.Id, null, evalTime);

        Assert.NotNull(result);
        Assert.False(result.IsOverallCompatible);
        Assert.Contains(result.ViolatedHardConstraints, c => c.Contains("client", StringComparison.OrdinalIgnoreCase) || c.Contains("tenant", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0m, result.CalculatedSuitabilityScore);
    }

    [Fact]
    public async Task HardGateInviolability_HighAffinityNeverOverridesOperationalStatusGate()
    {
        // Location is marked Inactive/Damaged/Locked -> fails operational status gate (totalScore = 0m)
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var slottingService = new SlottingOptimizationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<SlottingOptimizationService>.Instance);

        var itemA = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-STATUS-A", WeightKg = 5m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m };
        var itemB = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-STATUS-B", WeightKg = 5m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m };
        context.Items.AddRange(itemA, itemB);

        var locDamaged = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ZoneId = fix.PickZoneId,
            LocationCode = "LOC-DAMAGED",
            AisleCode = "A01",
            RackCode = "R01",
            ShelfLevel = 1,
            IsActive = true,
            IsDamaged = true, // Damaged!
            IsPickable = false,
            LengthCm = 120m,
            WidthCm = 100m,
            HeightCm = 150m,
            MaxWeightKg = 1000m
        };
        context.Locations.Add(locDamaged);

        var cluster = new ProductAffinityCluster
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ClusterCode = "PAC-STATUS",
            PrimaryAnchorItemId = itemB.Id,
            PrimaryAnchorSku = itemB.Sku,
            ItemCount = 2,
            AverageLift = 5.0m,
            MaxLift = 5.0m
        };
        cluster.Members.Add(new ProductAffinityClusterMember { ClusterId = cluster.Id, ItemId = itemA.Id, Sku = itemA.Sku });
        cluster.Members.Add(new ProductAffinityClusterMember { ClusterId = cluster.Id, ItemId = itemB.Id, Sku = itemB.Sku, IsAnchor = true });
        context.ProductAffinityClusters.Add(cluster);

        var policy = SlottingPolicy.Create("Affinity Policy", fix.FacilityId, weightProductAffinity: 0.50m);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        var result = await slottingService.EvaluateSkuLocationCompatibilityAsync(
            itemA.Id, locDamaged.Id, policy.Id, null, evalTime);

        Assert.NotNull(result);
        Assert.False(result.IsOverallCompatible);
        Assert.Contains(result.ViolatedHardConstraints, c => c.Contains("damaged", StringComparison.OrdinalIgnoreCase) || c.Contains("pickable", StringComparison.OrdinalIgnoreCase) || c.Contains("inactive", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0m, result.CalculatedSuitabilityScore);
    }

    [Fact]
    public async Task HardGateInviolability_HighAffinityNeverOverridesLocationTypeGate()
    {
        // Staging lane / Dock door location cannot receive permanent storage inventory -> fails location type gate (totalScore = 0m)
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var slottingService = new SlottingOptimizationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<SlottingOptimizationService>.Instance);

        var itemA = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-TYPE-A", WeightKg = 5m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m };
        var itemB = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-TYPE-B", WeightKg = 5m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m };
        context.Items.AddRange(itemA, itemB);

        var locStaging = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ZoneId = fix.PickZoneId,
            LocationCode = "LOC-STAGE-01",
            LocationType = LocationType.StagingLane, // Staging lane: non-storage operational area
            AisleCode = "A01",
            RackCode = "R01",
            ShelfLevel = 1,
            IsActive = true,
            IsPickable = true,
            LengthCm = 120m,
            WidthCm = 100m,
            HeightCm = 150m,
            MaxWeightKg = 1000m
        };
        context.Locations.Add(locStaging);

        var cluster = new ProductAffinityCluster
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ClusterCode = "PAC-LOCTYPE",
            PrimaryAnchorItemId = itemB.Id,
            PrimaryAnchorSku = itemB.Sku,
            ItemCount = 2,
            AverageLift = 5.0m,
            MaxLift = 5.0m
        };
        cluster.Members.Add(new ProductAffinityClusterMember { ClusterId = cluster.Id, ItemId = itemA.Id, Sku = itemA.Sku });
        cluster.Members.Add(new ProductAffinityClusterMember { ClusterId = cluster.Id, ItemId = itemB.Id, Sku = itemB.Sku, IsAnchor = true });
        context.ProductAffinityClusters.Add(cluster);

        var policy = SlottingPolicy.Create("Affinity Policy", fix.FacilityId, weightProductAffinity: 0.50m);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        var result = await slottingService.EvaluateSkuLocationCompatibilityAsync(
            itemA.Id, locStaging.Id, policy.Id, null, evalTime);

        Assert.NotNull(result);
        Assert.False(result.IsOperationallyFeasible);
        Assert.False(result.IsOverallCompatible);
        Assert.Contains(result.ViolatedHardConstraints, c => c.Contains("location type", StringComparison.OrdinalIgnoreCase) || c.Contains("staging", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0m, result.CalculatedSuitabilityScore);
    }

    [Fact]
    public async Task HardGateInviolability_HighAffinityNeverOverridesVolumetricCapacityGate()
    {
        // Total proposed volume + existing occupancy exceeds location cubic volume -> fails volumetric gate (totalScore = 0m)
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var slottingService = new SlottingOptimizationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<SlottingOptimizationService>.Instance);

        // Location volume = 100cm x 100cm x 50cm = 500,000 cm3
        var locLimitedVol = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ZoneId = fix.PickZoneId,
            LocationCode = "LOC-VOL-01",
            AisleCode = "A01",
            RackCode = "R01",
            ShelfLevel = 1,
            IsActive = true,
            IsPickable = true,
            LengthCm = 100m,
            WidthCm = 100m,
            HeightCm = 50m,
            MaxWeightKg = 5000m
        };
        context.Locations.Add(locLimitedVol);

        // Pre-existing item B occupies 450,000 cm3 (9 units x 50,000 cm3)
        var itemB = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-VOL-PARTNER", LengthCm = 50m, WidthCm = 50m, HeightCm = 20m, WeightKg = 5m };
        itemB.RecalculateVolume(); // 50,000 cm3
        // Proposed item A is 100,000 cm3 (50 x 50 x 40 cm)
        var itemA = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-VOL-A", LengthCm = 50m, WidthCm = 50m, HeightCm = 40m, WeightKg = 5m };
        itemA.RecalculateVolume(); // 100,000 cm3
        context.Items.AddRange(itemA, itemB);

        // Existing occupancy: 450,000 cm3
        context.StockOnHand.Add(new StockOnHand
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = itemB.Id,
            LocationId = locLimitedVol.Id,
            QtyAvailable = 9m
        });

        // High affinity cluster
        var cluster = new ProductAffinityCluster
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ClusterCode = "PAC-VOL",
            PrimaryAnchorItemId = itemB.Id,
            PrimaryAnchorSku = itemB.Sku,
            ItemCount = 2,
            AverageLift = 5.0m,
            MaxLift = 5.0m
        };
        cluster.Members.Add(new ProductAffinityClusterMember { ClusterId = cluster.Id, ItemId = itemA.Id, Sku = itemA.Sku });
        cluster.Members.Add(new ProductAffinityClusterMember { ClusterId = cluster.Id, ItemId = itemB.Id, Sku = itemB.Sku, IsAnchor = true });
        context.ProductAffinityClusters.Add(cluster);

        var policy = SlottingPolicy.Create("Affinity Policy", fix.FacilityId, weightProductAffinity: 0.50m);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        // Proposed 100,000 cm3 + existing 450,000 cm3 = 550,000 cm3 > 500,000 cm3 location capacity
        var result = await slottingService.EvaluateSkuLocationCompatibilityAsync(
            itemA.Id, locLimitedVol.Id, policy.Id, null, evalTime);

        Assert.NotNull(result);
        Assert.False(result.IsPhysicallyFeasible);
        Assert.False(result.IsOverallCompatible);
        Assert.Contains(result.ViolatedHardConstraints, c => c.Contains("volume", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0m, result.CalculatedSuitabilityScore); // Volumetric hard gate zeroes score despite affinity
    }

    [Fact]
    public async Task MultipleComparisonFdr_ControlsFalseDiscoveryRateAcrossCandidatePairs()
    {
        // When mining across a family of candidate pairs (M_total), Benjamini-Yekutieli FDR control
        // adjusts significance thresholds (k / (M_total * c(M)) * alpha) under arbitrary dependence.
        // A pair with high co-occurrence passes FDR; candidate pairs with insignificant co-occurrence are rejected.
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var miningService = new ProductAffinityMiningService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<ProductAffinityMiningService>.Instance);

        var itemA = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-FDR-A" };
        var itemB = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-FDR-B" };
        var itemC = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-FDR-C" };
        var itemD = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-FDR-D" };
        var itemE = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-FDR-E" };
        context.Items.AddRange(itemA, itemB, itemC, itemD, itemE);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        // 60 qualifying orders
        for (int i = 1; i <= 60; i++)
        {
            var o = new OutboundOrder
            {
                Id = Guid.NewGuid(),
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                OrderNumber = $"ORD-FDR-{i}",
                Status = OutboundOrderStatus.Completed,
                OrderDateUtc = evalTime.AddDays(-2)
            };
            context.OutboundOrders.Add(o);

            if (i <= 30)
            {
                // A + B co-occurs in 30 orders (Highly significant: p < 0.0001)
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemA.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemB.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            }
            else if (i <= 35)
            {
                // C + D co-occurs in 5 orders out of 60
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemC.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemD.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            }
            else
            {
                // Filler orders with E
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemE.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            }
        }
        await context.SaveChangesAsync();

        var result = await miningService.MineAffinityAsync(new MineProductAffinityRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            AsOfDateUtc = evalTime,
            MinOrderCount = 30,
            MinCoOccurrenceCount = 3
        });

        Assert.NotNull(result);
        Assert.True(result.TotalCandidatePairs >= 2);
        // Pair A+B passes FDR because of extreme significance
        var pairAB = result.SignificantPairs.FirstOrDefault(p => (p.ItemASku == "SKU-FDR-A" && p.ItemBSku == "SKU-FDR-B") || (p.ItemASku == "SKU-FDR-B" && p.ItemBSku == "SKU-FDR-A"));
        Assert.NotNull(pairAB);
        Assert.True(pairAB.IsStatisticallySignificant);
        Assert.True(pairAB.PValue < 0.05m);
        // Insignificant candidate pair C+D is correctly rejected by FDR
        Assert.DoesNotContain(result.SignificantPairs, p => p.ItemASku == "SKU-FDR-C" || p.ItemBSku == "SKU-FDR-C");
    }

    [Fact]
    public async Task CandidateHypothesisFamily_ConstructedFromCatalogUniverse_IncludesZeroCoOccurrencePairsInMTotal()
    {
        // Eligible SKU universe I_0 has 4 items in the catalog -> M_total = 4 * 3 / 2 = 6 candidate hypotheses.
        // Even though only A+B co-occurs in the observation window (1 pair observed, 5 pairs with C=0),
        // M_total is constructed independently of the window outcomes as |P_0| = 6.
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var miningService = new ProductAffinityMiningService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<ProductAffinityMiningService>.Instance);

        var itemA = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-UNIV-A" };
        var itemB = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-UNIV-B" };
        var itemC = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-UNIV-C" };
        var itemD = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-UNIV-D" };
        context.Items.AddRange(itemA, itemB, itemC, itemD);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        // 50 qualifying orders: 1..30 A and B co-occur; 31..50 C only (filler)
        for (int i = 1; i <= 50; i++)
        {
            var o = new OutboundOrder
            {
                Id = Guid.NewGuid(),
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                OrderNumber = $"ORD-UNIV-{i}",
                Status = OutboundOrderStatus.Completed,
                OrderDateUtc = evalTime.AddDays(-2)
            };
            context.OutboundOrders.Add(o);

            if (i <= 30)
            {
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemA.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemB.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            }
            else
            {
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemC.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            }
        }
        await context.SaveChangesAsync();

        var result = await miningService.MineAffinityAsync(new MineProductAffinityRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            AsOfDateUtc = evalTime,
            MinOrderCount = 30
        });

        Assert.NotNull(result);
        // Catalog universe of 4 items defines M_total = 4 * 3 / 2 = 6 candidate pairs
        Assert.Equal(6, result.TotalCandidatePairs);
        // Only pair A+B is accepted
        Assert.Single(result.SignificantPairs);
        var acceptedPair = result.SignificantPairs[0];
        Assert.True((acceptedPair.ItemASku == "SKU-UNIV-A" && acceptedPair.ItemBSku == "SKU-UNIV-B") ||
                    (acceptedPair.ItemASku == "SKU-UNIV-B" && acceptedPair.ItemBSku == "SKU-UNIV-A"));
    }

    [Fact]
    public async Task MultipleComparisonFdr_AllPairsFailThreshold_AcceptsZeroDiscoveries()
    {
        // When all candidate pairs exhibit insignificant co-occurrence (p-values fail BY threshold at all ranks k),
        // k* = 0 and zero discoveries are declared (S_FDR = empty).
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var miningService = new ProductAffinityMiningService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<ProductAffinityMiningService>.Instance);

        var itemA = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-FAIL-A" };
        var itemB = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-FAIL-B" };
        var itemC = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-FAIL-C" };
        context.Items.AddRange(itemA, itemB, itemC);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        // 40 qualifying orders: items appear evenly and almost independently
        for (int i = 1; i <= 40; i++)
        {
            var o = new OutboundOrder
            {
                Id = Guid.NewGuid(),
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                OrderNumber = $"ORD-FAIL-{i}",
                Status = OutboundOrderStatus.Completed,
                OrderDateUtc = evalTime.AddDays(-2)
            };
            context.OutboundOrders.Add(o);

            // A appears in all 40 orders
            context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemA.Id, QuantityOrdered = 1m, QuantityShipped = 1m });

            // B appears in 20 orders randomly (co-occurrence = 20, independence expected = 20, lift = 1.0, chi2 = 0)
            if (i <= 20)
            {
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemB.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            }
        }
        await context.SaveChangesAsync();

        var result = await miningService.MineAffinityAsync(new MineProductAffinityRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            AsOfDateUtc = evalTime,
            MinOrderCount = 30,
            MinCoOccurrenceCount = 3
        });

        Assert.NotNull(result);
        Assert.Equal(0, result.StatisticallySignificantPairsCount);
        Assert.Empty(result.SignificantPairs);
    }

    [Fact]
    public async Task OperationalFiltering_OptionA_AppliesBusinessGatesAsOperationalEligibilityFilterOnFdrDiscoveries()
    {
        // Under Option A, BY FDR control governs the statistical significance discovery set S_FDR.
        // Downstream business gates (e.g. MinLift = 2.50) act as secondary operational eligibility filters.
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var miningService = new ProductAffinityMiningService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<ProductAffinityMiningService>.Instance);

        var itemA = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-OPTA-A" };
        var itemB = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-OPTA-B" };
        var itemC = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-OPTA-C" };
        context.Items.AddRange(itemA, itemB, itemC);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        // 60 orders: 1..30 A and B co-occur (Lift = 2.0, extreme significance chi2 = 60, p < 0.0001); 31..60 C only
        for (int i = 1; i <= 60; i++)
        {
            var o = new OutboundOrder
            {
                Id = Guid.NewGuid(),
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                OrderNumber = $"ORD-OPTA-{i}",
                Status = OutboundOrderStatus.Completed,
                OrderDateUtc = evalTime.AddDays(-2)
            };
            context.OutboundOrders.Add(o);

            if (i <= 30)
            {
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemA.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemB.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            }
            else
            {
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemC.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            }
        }
        await context.SaveChangesAsync();

        // Run with MinLift = 1.10 (Standard): pair passes both BY FDR and business filter
        var resStandard = await miningService.MineAffinityAsync(new MineProductAffinityRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            AsOfDateUtc = evalTime,
            MinOrderCount = 30,
            MinLift = 1.10m
        });
        Assert.Single(resStandard.SignificantPairs);

        // Run with MinLift = 2.50 (Stringent business eligibility): pair passes BY FDR threshold,
        // but is filtered out by operational eligibility gate (Lift 2.00 < 2.50)
        var resFiltered = await miningService.MineAffinityAsync(new MineProductAffinityRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            AsOfDateUtc = evalTime,
            MinOrderCount = 30,
            MinLift = 2.50m
        });
        Assert.Empty(resFiltered.SignificantPairs);
    }

    [Fact]
    public async Task DirectionalAffinity_NegativeAndZeroCoOccurrencePairs_AssignedMaxPValueAndCannotBecomeDiscoveries()
    {
        // Under directional testing H0: OR(A, B) <= 1.0 vs. H1: OR(A, B) > 1.0 (positive affinity):
        // 1. Negative-association pair with both marginal frequencies positive and Lift < 1 receives PValue == 1.0.
        // 2. Zero-co-occurrence pair with both marginal frequencies positive and C(A, C) == 0 receives PValue == 1.0.
        // 3. Complete catalog hypothesis family M_total = 3 * 2 / 2 = 3 from catalog snapshot {A, B, C}.
        // 4. Neither negative nor zero-co-occurrence pairs belong to the BY discovery set, and the BY denominator remains M_total = 3.
        // 5. Direct assertions on evaluated pair statistics (persisted entity & ComputePairStatistics) verify underlying statistical results.
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var miningService = new ProductAffinityMiningService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<ProductAffinityMiningService>.Instance);

        var itemA = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-DIR-A" };
        var itemB = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-DIR-B" };
        var itemC = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-DIR-C" };
        context.Items.AddRange(itemA, itemB, itemC);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        // 60 qualifying orders:
        // A in orders 1..30 -> C(A) = 30 > 0
        // B in orders 26..55 -> C(B) = 30 > 0; co-occurs with A in 5 orders (26..30)
        // supp(A) = 30/60 = 0.50, supp(B) = 30/60 = 0.50, supp(A, B) = 5/60 = 0.0833
        // Lift(A, B) = (5 * 60) / (30 * 30) = 300 / 900 = 0.3333 < 1.0 (Negative association with positive marginals)
        // C in orders 56..60 -> C(C) = 5 > 0 (Positive marginal frequency)
        // C never co-occurs with A or B -> C(A, C) = 0, C(B, C) = 0 (Ordinary zero-co-occurrence with positive marginals)
        for (int i = 1; i <= 60; i++)
        {
            var o = new OutboundOrder
            {
                Id = Guid.NewGuid(),
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                OrderNumber = $"ORD-DIR-{i}",
                Status = OutboundOrderStatus.Completed,
                OrderDateUtc = evalTime.AddDays(-2)
            };
            context.OutboundOrders.Add(o);

            if (i <= 30)
            {
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemA.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            }
            if (i >= 26 && i <= 55)
            {
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemB.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            }
            if (i >= 56 && i <= 60)
            {
                context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = o.Id, ItemId = itemC.Id, QuantityOrdered = 1m, QuantityShipped = 1m });
            }
        }
        await context.SaveChangesAsync();

        var result = await miningService.MineAffinityAsync(new MineProductAffinityRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            AsOfDateUtc = evalTime,
            MinOrderCount = 30,
            MinCoOccurrenceCount = 3
        });

        Assert.NotNull(result);

        // 1. The BY denominator remains the full catalog pair count: 3 items -> M_total = 3 * 2 / 2 = 3
        Assert.Equal(3, result.TotalCandidatePairs);

        // 2. Neither negative-association nor zero-co-occurrence pairs belong to the BY discovery set
        Assert.Empty(result.SignificantPairs);
        Assert.Equal(0, result.StatisticallySignificantPairsCount);
        Assert.DoesNotContain(result.SignificantPairs, p => (p.ItemASku == "SKU-DIR-A" && p.ItemBSku == "SKU-DIR-B") || (p.ItemASku == "SKU-DIR-B" && p.ItemBSku == "SKU-DIR-A"));
        Assert.DoesNotContain(result.SignificantPairs, p => (p.ItemASku == "SKU-DIR-A" && p.ItemBSku == "SKU-DIR-C") || (p.ItemASku == "SKU-DIR-C" && p.ItemBSku == "SKU-DIR-A"));
        Assert.DoesNotContain(result.SignificantPairs, p => (p.ItemASku == "SKU-DIR-B" && p.ItemBSku == "SKU-DIR-C") || (p.ItemASku == "SKU-DIR-C" && p.ItemBSku == "SKU-DIR-B"));

        // 3. Direct assertion on persisted negative-association pair entity in operational database
        var persistedNegPair = await context.ProductAffinityPairs
            .FirstOrDefaultAsync(p => (p.ItemASku == "SKU-DIR-A" && p.ItemBSku == "SKU-DIR-B") ||
                                      (p.ItemASku == "SKU-DIR-B" && p.ItemBSku == "SKU-DIR-A"));
        Assert.NotNull(persistedNegPair);
        Assert.Equal(30, persistedNegPair.ItemAOrderCount);
        Assert.Equal(30, persistedNegPair.ItemBOrderCount);
        Assert.Equal(5, persistedNegPair.CoOccurrenceCount);
        Assert.True(persistedNegPair.Lift < 1.0m);
        Assert.Equal(1.0m, persistedNegPair.PValue); // Direct verification: Negative pair PValue == 1.0
        Assert.Equal(0m, persistedNegPair.ChiSquareStatistic);
        Assert.False(persistedNegPair.IsStatisticallySignificant);

        // 4. Direct assertions against evaluated pair statistics unit (ProductAffinityMiningService.ComputePairStatistics)
        // A. Negative-association pair with both marginal frequencies positive:
        var negStats = ProductAffinityMiningService.ComputePairStatistics(60, 30, 30, 5);
        Assert.Equal(30, negStats.ItemAOrderCount);
        Assert.Equal(30, negStats.ItemBOrderCount);
        Assert.Equal(5, negStats.CoOccurrenceCount);
        Assert.True(negStats.Lift < 1.0m);
        Assert.Equal(1.0m, negStats.PValue); // Direct verification: Negative pair PValue == 1.0
        Assert.Equal(0m, negStats.ChiSquareStatistic);
        Assert.False(negStats.IsDirectionallyPositive);

        // B. Zero-co-occurrence pair (A, C) with both marginal frequencies positive:
        var zeroStatsAC = ProductAffinityMiningService.ComputePairStatistics(60, 30, 5, 0);
        Assert.Equal(30, zeroStatsAC.ItemAOrderCount); // C(A) = 30 > 0
        Assert.Equal(5, zeroStatsAC.ItemBOrderCount);  // C(C) = 5 > 0
        Assert.Equal(0, zeroStatsAC.CoOccurrenceCount); // C(A, C) = 0
        Assert.Equal(0.0m, zeroStatsAC.Lift);
        Assert.Equal(1.0m, zeroStatsAC.PValue); // Direct verification: Zero-co-occurrence pair PValue == 1.0
        Assert.Equal(0m, zeroStatsAC.ChiSquareStatistic);
        Assert.False(zeroStatsAC.IsDirectionallyPositive);

        // C. Zero-co-occurrence pair (B, C) with both marginal frequencies positive:
        var zeroStatsBC = ProductAffinityMiningService.ComputePairStatistics(60, 30, 5, 0);
        Assert.Equal(30, zeroStatsBC.ItemAOrderCount); // C(B) = 30 > 0
        Assert.Equal(5, zeroStatsBC.ItemBOrderCount);  // C(C) = 5 > 0
        Assert.Equal(0, zeroStatsBC.CoOccurrenceCount); // C(B, C) = 0
        Assert.Equal(0.0m, zeroStatsBC.Lift);
        Assert.Equal(1.0m, zeroStatsBC.PValue); // Direct verification: Zero-co-occurrence pair PValue == 1.0
        Assert.Equal(0m, zeroStatsBC.ChiSquareStatistic);
        Assert.False(zeroStatsBC.IsDirectionallyPositive);
    }

    #endregion
}
