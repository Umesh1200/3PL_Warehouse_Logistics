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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Warehouse3PL.Application;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
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
using Warehouse3PL.Infrastructure.Authentication;
using Warehouse3PL.Infrastructure.Authorization;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;
using Warehouse3PL.Infrastructure.Services;
using Warehouse3PL.Infrastructure.Services.Allocation;
using Warehouse3PL.UnitTests.Common;
using Warehouse3PL.WebApi.Controllers;
using Warehouse3PL.WebApi.Middleware;
using Xunit;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.UnitTests.Outbound;

public sealed class Mdm04OperationsPolicyWiringTests
{
    private const string JwtSecret = "Nexus3PL-Mdm04-PolicyWiring-TestKey-2026!!";
    private const string JwtIssuer = "nexus3pl-mdm04-auth";
    private const string JwtAudience = "nexus3pl-mdm04-api";

    private static (AllocationEngine Engine, AllocationExecutionService Execution) CreateAllocationServices(
        IApplicationDbContext context,
        ITenantProvider tenantProvider)
    {
        var filter = new AllocationCandidateFilter();
        var resolver = new AllocationStrategyResolver(new IAllocationStrategy[]
        {
            new FifoAllocationStrategy(context, filter),
            new FefoAllocationStrategy(context, filter),
            new ManualAllocationStrategy(context, filter)
        });
        var engine = new AllocationEngine(context, resolver, filter);
        var exec = new AllocationExecutionService(context, engine, filter, tenantProvider);
        return (engine, exec);
    }

    [Fact]
    public async Task PreferFullHandlingUnits_EnabledVsDisabled_AndSafetyFallbackBehavior()
    {
        var dbName = $"mdm04-hu-{Guid.NewGuid():N}";
        var clientId = Guid.NewGuid();
        var facId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var looseBinId = Guid.NewGuid();
        var partialHuBinId = Guid.NewGuid();
        var fullHuBinId = Guid.NewGuid();
        var lockedHuBinId = Guid.NewGuid();
        var quarantinedHuBinId = Guid.NewGuid();
        var fullHuId = Guid.NewGuid();
        var partialHuId = Guid.NewGuid();
        var receivedAt = new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);

        var tenantProvider = new TestTenantProvider { TenantId = clientId, BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, FacilityId = facId };

        await using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "HU Corp", AccountCode = "HUC" });
            seed.Facilities.Add(new Facility { Id = facId, FacilityCode = "FAC-HU", Name = "HU Hub", IsActive = true });
            seed.Locations.AddRange(
                new Location { Id = looseBinId, FacilityId = facId, LocationCode = "BIN-LOOSE", LocationType = LocationType.Bin, PickSequence = 1, IsPickable = true, IsActive = true },
                new Location { Id = partialHuBinId, FacilityId = facId, LocationCode = "BIN-HU-BIG", LocationType = LocationType.Bin, PickSequence = 2, IsPickable = true, IsActive = true },
                new Location { Id = fullHuBinId, FacilityId = facId, LocationCode = "BIN-HU-EXACT", LocationType = LocationType.Bin, PickSequence = 10, IsPickable = true, IsActive = true },
                new Location { Id = lockedHuBinId, FacilityId = facId, LocationCode = "BIN-HU-LOCKED", LocationType = LocationType.Bin, PickSequence = 0, IsPickable = true, IsActive = true, IsLocked = true },
                new Location { Id = quarantinedHuBinId, FacilityId = facId, LocationCode = "BIN-HU-QA", LocationType = LocationType.Bin, PickSequence = 0, IsPickable = true, IsActive = true });

            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-PALLET", StorageTempClass = StorageTempClass.Ambient });
            var badLot = new InventoryLot { Id = Guid.NewGuid(), ClientId = clientId, ItemId = itemId, LotNumber = "LOT-HOLD", QaStatus = LotQaStatus.Quarantine };
            seed.InventoryLots.Add(badLot);

            seed.HandlingUnits.AddRange(
                new HandlingUnit { Id = fullHuId, ClientId = clientId, FacilityId = facId, LpnCode = "LPN-FULL-50", UnitType = HandlingUnitType.Pallet },
                new HandlingUnit { Id = partialHuId, ClientId = clientId, FacilityId = facId, LpnCode = "LPN-BIG-80", UnitType = HandlingUnitType.Pallet });

            // Eligible StockOnHand with identical ReceivedAtUtc:
            // - loose stock (PickSeq=1)
            // - oversized HU (PickSeq=2, Qty=80 > demand 50 -> would break HU)
            // - exact intact HU (PickSeq=10, Qty=50 == demand 50 -> full HU sweep!)
            // Plus ineligible locked & quarantined full HUs (must NEVER be picked!)
            seed.StockOnHand.AddRange(
                new StockOnHand { ClientId = clientId, FacilityId = facId, ItemId = itemId, LocationId = looseBinId, QtyAvailable = 50m, ReceivedAtUtc = receivedAt },
                new StockOnHand { ClientId = clientId, FacilityId = facId, ItemId = itemId, LocationId = partialHuBinId, HandlingUnitId = partialHuId, LpnCode = "LPN-BIG-80", QtyAvailable = 80m, ReceivedAtUtc = receivedAt },
                new StockOnHand { ClientId = clientId, FacilityId = facId, ItemId = itemId, LocationId = fullHuBinId, HandlingUnitId = fullHuId, LpnCode = "LPN-FULL-50", QtyAvailable = 50m, ReceivedAtUtc = receivedAt },
                new StockOnHand { ClientId = clientId, FacilityId = facId, ItemId = itemId, LocationId = lockedHuBinId, LpnCode = "LPN-LOCKED-50", QtyAvailable = 50m, ReceivedAtUtc = receivedAt },
                new StockOnHand { ClientId = clientId, FacilityId = facId, ItemId = itemId, LocationId = quarantinedHuBinId, LotId = badLot.Id, LpnCode = "LPN-QA-50", QtyAvailable = 50m, ReceivedAtUtc = receivedAt });

            await seed.SaveChangesAsync();
        }

        await using var ctx = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var (engine, _) = CreateAllocationServices(ctx, tenantProvider);

        // 1. Disabled -> picks lowest PickSequence (BIN-LOOSE, PickSeq=1)
        var disabledPlan = await engine.PlanLineAllocationAsync(new AllocationRequest
        {
            ClientId = clientId,
            FacilityId = facId,
            ItemId = itemId,
            QuantityRequested = 50m,
            Policy = new AllocationPolicy { DefaultStrategy = AllocationStrategyType.FIFO, PreferFullHandlingUnits = false }
        });
        Assert.Equal(looseBinId, disabledPlan.CandidateAllocations.Single().LocationId);

        // 2. Enabled -> prefers intact full HU (LPN-FULL-50 at PickSeq=10), never breaking LPN-BIG-80 and ignoring locked/QA HUs
        var enabledPlan = await engine.PlanLineAllocationAsync(new AllocationRequest
        {
            ClientId = clientId,
            FacilityId = facId,
            ItemId = itemId,
            QuantityRequested = 50m,
            Policy = new AllocationPolicy { DefaultStrategy = AllocationStrategyType.FIFO, PreferFullHandlingUnits = true }
        });
        Assert.Equal(fullHuBinId, enabledPlan.CandidateAllocations.Single().LocationId);
        Assert.Equal("LPN-FULL-50", enabledPlan.CandidateAllocations.Single().LpnCode);

        // 3. Deterministic fallback when demand (140) spans multiple HUs + loose stock (picks LPN-BIG-80 then LPN-FULL-50, then BIN-LOOSE)
        var fallbackPlan = await engine.PlanLineAllocationAsync(new AllocationRequest
        {
            ClientId = clientId,
            FacilityId = facId,
            ItemId = itemId,
            QuantityRequested = 140m,
            Policy = new AllocationPolicy { DefaultStrategy = AllocationStrategyType.FIFO, PreferFullHandlingUnits = true }
        });
        Assert.Equal(140m, fallbackPlan.QuantityPlanned);
        Assert.Equal(partialHuBinId, fallbackPlan.CandidateAllocations[0].LocationId);
        Assert.Equal(fullHuBinId, fallbackPlan.CandidateAllocations[1].LocationId);
        Assert.Equal(looseBinId, fallbackPlan.CandidateAllocations[2].LocationId);
    }

    [Fact]
    public async Task ExecuteOrderAllocation_StrategyOverride_AppliesValidatedOverrideInsteadOfIgnoring()
    {
        var dbName = $"mdm04-override-{Guid.NewGuid():N}";
        var clientId = Guid.NewGuid();
        var facId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var oldReceivedBinId = Guid.NewGuid();
        var soonExpiringBinId = Guid.NewGuid();
        var orderDefaultId = Guid.NewGuid();
        var orderOverrideId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId, BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, FacilityId = facId };

        await using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Override Corp", AccountCode = "OVR" });
            seed.Facilities.Add(new Facility { Id = facId, FacilityCode = "FAC-OV", Name = "Override Hub", IsActive = true });
            seed.Locations.AddRange(
                new Location { Id = oldReceivedBinId, FacilityId = facId, LocationCode = "BIN-FIFO-OLD", LocationType = LocationType.Bin, PickSequence = 1, IsPickable = true, IsActive = true },
                new Location { Id = soonExpiringBinId, FacilityId = facId, LocationCode = "BIN-FEFO-SOON", LocationType = LocationType.Bin, PickSequence = 2, IsPickable = true, IsActive = true });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-LOT", StorageTempClass = StorageTempClass.Ambient });

            // Client default policy is FIFO
            seed.AllocationPolicies.Add(new AllocationPolicy
            {
                ClientId = clientId,
                FacilityId = facId,
                DefaultStrategy = AllocationStrategyType.FIFO,
                AllowPartialAllocation = true,
                IsActive = true
            });

            var lotFar = new InventoryLot { Id = Guid.NewGuid(), ClientId = clientId, ItemId = itemId, LotNumber = "LOT-90D", ExpirationDate = DateTime.UtcNow.AddDays(90), QaStatus = LotQaStatus.Released };
            var lotSoon = new InventoryLot { Id = Guid.NewGuid(), ClientId = clientId, ItemId = itemId, LotNumber = "LOT-10D", ExpirationDate = DateTime.UtcNow.AddDays(10), QaStatus = LotQaStatus.Released };
            seed.InventoryLots.AddRange(lotFar, lotSoon);

            seed.StockOnHand.AddRange(
                // Oldest received -> FIFO picks first
                new StockOnHand { ClientId = clientId, FacilityId = facId, ItemId = itemId, LocationId = oldReceivedBinId, LotId = lotFar.Id, QtyAvailable = 30m, ReceivedAtUtc = DateTime.UtcNow.AddDays(-20) },
                // Newer received but earliest expiry -> FEFO picks first
                new StockOnHand { ClientId = clientId, FacilityId = facId, ItemId = itemId, LocationId = soonExpiringBinId, LotId = lotSoon.Id, QtyAvailable = 30m, ReceivedAtUtc = DateTime.UtcNow.AddDays(-2) });

            var ordDefault = new OutboundOrder { Id = orderDefaultId, ClientId = clientId, FacilityId = facId, OrderNumber = "ORD-DEF", CustomerName = "C1", Status = OutboundOrderStatus.Released };
            ordDefault.Lines.Add(new OutboundOrderLine { ItemId = itemId, LineNumber = 1, QuantityOrdered = 10m });

            var ordOverride = new OutboundOrder { Id = orderOverrideId, ClientId = clientId, FacilityId = facId, OrderNumber = "ORD-OVR", CustomerName = "C2", Status = OutboundOrderStatus.Released };
            ordOverride.Lines.Add(new OutboundOrderLine { ItemId = itemId, LineNumber = 1, QuantityOrdered = 10m });

            seed.OutboundOrders.AddRange(ordDefault, ordOverride);
            await seed.SaveChangesAsync();
        }

        await using var ctx = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var (_, exec) = CreateAllocationServices(ctx, tenantProvider);

        // Invalid enum override must fail closed without allocating
        await Assert.ThrowsAsync<ValidationException>(() =>
            exec.ExecuteOrderAllocationAsync(orderDefaultId, strategyOverride: (AllocationStrategyType)999));

        // 1. Default policy -> FIFO picks BIN-FIFO-OLD
        await exec.ExecuteOrderAllocationAsync(orderDefaultId, strategyOverride: null);
        var defaultAlloc = await ctx.InventoryAllocations.SingleAsync(a => a.OutboundOrderId == orderDefaultId);
        Assert.Equal(oldReceivedBinId, defaultAlloc.LocationId);
        Assert.Equal(AllocationStrategyType.FIFO, defaultAlloc.StrategyUsed);

        // 2. Explicit StrategyOverride = FEFO -> overrides FIFO policy and picks BIN-FEFO-SOON
        await exec.ExecuteOrderAllocationAsync(orderOverrideId, strategyOverride: AllocationStrategyType.FEFO);
        var overrideAlloc = await ctx.InventoryAllocations.SingleAsync(a => a.OutboundOrderId == orderOverrideId);
        Assert.Equal(soonExpiringBinId, overrideAlloc.LocationId);
        Assert.Equal(AllocationStrategyType.FEFO, overrideAlloc.StrategyUsed);
    }

    [Fact]
    public async Task PutawayPreferConsolidation_TrueVsFalse_DeterministicFallback_AndSafetyConstraints()
    {
        var dbName = $"mdm04-putaway-{Guid.NewGuid():N}";
        var clientId = Guid.NewGuid();
        var facId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var stagingId = Guid.NewGuid();
        var occupiedBinId = Guid.NewGuid();
        var emptyBinId = Guid.NewGuid();
        var coldZoneId = Guid.NewGuid();
        var coldBinId = Guid.NewGuid();
        var tightCapBinId = Guid.NewGuid();
        Guid ruleId;

        var tenantProvider = new TestTenantProvider { TenantId = clientId, BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), UserType = UserType.Staff, FacilityId = facId };

        await using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.WarehouseZones.AddRange(
                new WarehouseZone { Id = Guid.NewGuid(), FacilityId = facId, ZoneCode = "AMB", ZoneType = WarehouseZoneType.BulkStorage },
                new WarehouseZone { Id = coldZoneId, FacilityId = facId, ZoneCode = "COLD", ZoneType = WarehouseZoneType.ColdStorage, TempMaxC = 4m });

            seed.Locations.AddRange(
                new Location { Id = stagingId, FacilityId = facId, LocationCode = "STG-1", Barcode = "B-STG", LocationType = LocationType.StagingLane, IsReceiving = true },
                new Location { Id = occupiedBinId, FacilityId = facId, LocationCode = "BIN-OCC", Barcode = "B-OCC", LocationType = LocationType.Bin, MaxWeightKg = 100m },
                new Location { Id = emptyBinId, FacilityId = facId, LocationCode = "BIN-EMP", Barcode = "B-EMP", LocationType = LocationType.Bin, MaxWeightKg = 100m },
                new Location { Id = coldBinId, FacilityId = facId, ZoneId = coldZoneId, LocationCode = "BIN-COLD", Barcode = "B-CLD", LocationType = LocationType.Bin, MaxWeightKg = 500m },
                new Location { Id = tightCapBinId, FacilityId = facId, LocationCode = "BIN-TIGHT", Barcode = "B-TGT", LocationType = LocationType.Bin, MaxWeightKg = 15m });

            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-AMB", StorageTempClass = StorageTempClass.Ambient, WeightKg = 2m, VolumeCm3 = 1000m });

            // Existing stock in BIN-OCC (same SKU, 20 kg) + staging stock (100 kg)
            seed.StockOnHand.AddRange(
                new StockOnHand { ClientId = clientId, FacilityId = facId, ItemId = itemId, LocationId = occupiedBinId, QtyAvailable = 10m },
                new StockOnHand { ClientId = clientId, FacilityId = facId, ItemId = itemId, LocationId = stagingId, QtyAvailable = 50m });

            var rule = new PutawayStrategyRule
            {
                ClientId = clientId,
                FacilityId = facId,
                Sequence = 1,
                RuleName = "Ambient Consolidation Control",
                TempClassRequirement = StorageTempClass.Ambient,
                PreferConsolidation = true,
                AllowDynamicOverflow = false,
                IsActive = true
            };
            ruleId = rule.Id;
            seed.PutawayStrategyRules.Add(rule);
            await seed.SaveChangesAsync();
        }

        await using var ctx = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var selector = new PutawayLocationSelector(ctx, NullLogger<PutawayLocationSelector>.Instance);

        // 1. PreferConsolidation = true -> consolidates into BIN-OCC
        var withConsolidation = await selector.SelectOptimalLocationAsync(clientId, facId, itemId, quantity: 10m);
        Assert.NotNull(withConsolidation);
        Assert.Equal(occupiedBinId, withConsolidation!.Id);

        // 2. PreferConsolidation = false -> directs to empty BIN-EMP instead of BIN-OCC (and never picks COLD bin!)
        var ruleToUpdate = await ctx.PutawayStrategyRules.SingleAsync(r => r.Id == ruleId);
        ruleToUpdate.PreferConsolidation = false;
        await ctx.SaveChangesAsync();

        var withoutConsolidation = await selector.SelectOptimalLocationAsync(clientId, facId, itemId, quantity: 10m);
        Assert.NotNull(withoutConsolidation);
        Assert.Equal(emptyBinId, withoutConsolidation!.Id);

        // 3. Deterministic fallback when BIN-EMP is excluded and only BIN-OCC fits
        var fallbackOnlyOccupied = await selector.SelectOptimalLocationAsync(
            clientId, facId, itemId, quantity: 10m, excludedLocationIds: new[] { emptyBinId });
        Assert.NotNull(fallbackOnlyOccupied);
        Assert.Equal(occupiedBinId, fallbackOnlyOccupied!.Id);

        // 4. Safety check during ConfirmPutawayTask: capacity breach (20kg > 15kg MaxWeightKg) and temp breach (coldBin) rejected!
        var capTask = new PutawayTask
        {
            ClientId = clientId, FacilityId = facId, ItemId = itemId, SourceLocationId = stagingId,
            SuggestedLocationId = tightCapBinId, Quantity = 10m, Status = PutawayTaskStatus.Pending
        };
        var tempTask = new PutawayTask
        {
            ClientId = clientId, FacilityId = facId, ItemId = itemId, SourceLocationId = stagingId,
            SuggestedLocationId = coldBinId, Quantity = 5m, Status = PutawayTaskStatus.Pending
        };
        ctx.PutawayTasks.AddRange(capTask, tempTask);
        await ctx.SaveChangesAsync();

        var confirmHandler = new ConfirmPutawayTaskCommandHandler(ctx, currentUser, new WarehouseOperationalAuthorization(ctx, currentUser));
        await Assert.ThrowsAsync<ValidationException>(() => confirmHandler.Handle(
            new ConfirmPutawayTaskCommand { TaskId = capTask.Id, ScannedLpn = "", ScannedLocationBarcode = "B-TGT" }, CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => confirmHandler.Handle(
            new ConfirmPutawayTaskCommand { TaskId = tempTask.Id, ScannedLpn = "", ScannedLocationBarcode = "B-CLD" }, CancellationToken.None));
    }

    [Fact]
    public async Task Authorization_RejectsUnauthorizedOverrides_CrossFacilityPolicyManagement_AndOutOfScopeExecution()
    {
        var clientA = Guid.NewGuid();
        var facA = Guid.NewGuid();
        var facB = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var binId = Guid.NewGuid();
        var orderFacAId = Guid.NewGuid();

        var staffAllocFacA = Guid.NewGuid();
        var staffNoAllocPerm = Guid.NewGuid();
        var staffMultiFacWrongHome = Guid.NewGuid();
        var staffRulesFacA = Guid.NewGuid();
        var staffRulesMultiFacB = Guid.NewGuid();
        var clientAdminId = Guid.NewGuid();
        var driverId = Guid.NewGuid();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.Host.UseDefaultServiceProvider(o => o.ValidateOnBuild = false);
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();

        builder.Services.Configure<JwtOptions>(o =>
        {
            o.SecretKey = JwtSecret;
            o.Issuer = JwtIssuer;
            o.Audience = JwtAudience;
            o.ExpiryMinutes = 60;
        });

        builder.Services.AddControllers()
            .AddApplicationPart(typeof(OutboundOrdersController).Assembly)
            .AddApplicationPart(typeof(PutawayStrategyController).Assembly)
            .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

        builder.Services.AddApplicationServices();
        builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
        builder.Services.AddSingleton<ITokenService, JwtTokenService>();
        builder.Services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = JwtIssuer,
                    ValidAudience = JwtAudience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)),
                    ClockSkew = TimeSpan.Zero
                };
            });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
        builder.Services.AddScoped<ITenantProvider, TenantProvider>();
        builder.Services.AddScoped<AuditableEntityInterceptor>();

        builder.Services.AddScoped<IAllocationCandidateFilter, AllocationCandidateFilter>();
        builder.Services.AddScoped<IAllocationStrategy, FifoAllocationStrategy>();
        builder.Services.AddScoped<IAllocationStrategy, FefoAllocationStrategy>();
        builder.Services.AddScoped<IAllocationStrategy, ManualAllocationStrategy>();
        builder.Services.AddScoped<IAllocationStrategyResolver, AllocationStrategyResolver>();
        builder.Services.AddScoped<IAllocationEngine, AllocationEngine>();
        builder.Services.AddScoped<IAllocationExecutionService, AllocationExecutionService>();

        var dbName = $"mdm04-http-auth-{Guid.NewGuid():N}";
        builder.Services.AddDbContext<ApplicationDbContext>((sp, opts) =>
            opts.UseInMemoryDatabase(dbName).AddInterceptors(sp.GetRequiredService<AuditableEntityInterceptor>()));
        builder.Services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        await using var app = builder.Build();
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthenticatedTenantResolution();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        using var http = app.GetTestClient();

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Clients.Add(new Client { Id = clientA, CompanyName = "Auth Client", AccountCode = "ACL", Status = ClientStatus.Active });
            db.Facilities.AddRange(
                new Facility { Id = facA, FacilityCode = "FA", Name = "Hub A", AddressLine1 = "1 Main", City = "Chicago", CountryCode = "US", IsActive = true },
                new Facility { Id = facB, FacilityCode = "FB", Name = "Hub B", AddressLine1 = "2 Main", City = "Dallas", CountryCode = "US", IsActive = true });
            db.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = clientA, FacilityId = facA, Status = FacilityAccessStatus.Active });
            db.Locations.Add(new Location { Id = binId, FacilityId = facA, LocationCode = "BIN-A", LocationType = LocationType.Bin, IsPickable = true, IsActive = true });
            db.Items.Add(new Item { Id = itemId, ClientId = clientA, Sku = "SKU-AUTH", StorageTempClass = StorageTempClass.Ambient });
            db.StockOnHand.Add(new StockOnHand { ClientId = clientA, FacilityId = facA, ItemId = itemId, LocationId = binId, QtyAvailable = 50m });

            var ord = new OutboundOrder { Id = orderFacAId, ClientId = clientA, FacilityId = facA, OrderNumber = "ORD-AUTH", CustomerName = "C", Status = OutboundOrderStatus.Released };
            ord.Lines.Add(new OutboundOrderLine { ItemId = itemId, LineNumber = 1, QuantityOrdered = 10m });
            db.OutboundOrders.Add(ord);
            await db.SaveChangesAsync();
        }

        string IssueJwt(Guid uid, UserType userType, Guid? tenantId, Guid? facId, IEnumerable<string> roles, IEnumerable<string> perms)
        {
            using var scope = app.Services.CreateScope();
            var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
            var user = new User { Id = uid, Email = $"{uid:N}@test.local", FirstName = "T", LastName = "U", UserType = userType, ClientId = tenantId, AssignedFacilityId = facId };
            return tokens.GenerateTokens(user, roles, perms).AccessToken;
        }

        HttpRequestMessage JsonReq(HttpMethod method, string uri, string? jwt, object? body = null)
        {
            var req = new HttpRequestMessage(method, uri);
            if (jwt != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            if (body != null) req.Content = JsonContent.Create(body);
            return req;
        }

        var jwtStaffAllocA = IssueJwt(staffAllocFacA, UserType.Staff, null, facA, ["Dispatcher"], [Permissions.Outbound.OrdersAllocate]);
        var jwtStaffNoAlloc = IssueJwt(staffNoAllocPerm, UserType.Staff, null, facA, ["Viewer"], [Permissions.Outbound.OrdersView]);
        var jwtStaffMultiFacB = IssueJwt(staffMultiFacWrongHome, UserType.Staff, null, facB, ["Ops"], [Permissions.Outbound.OrdersAllocate, Permissions.Analytics.MultiFacilityView]);
        var jwtStaffRulesA = IssueJwt(staffRulesFacA, UserType.Staff, null, facA, ["InboundLead"], [Permissions.Inbound.PutawayRules]);
        var jwtStaffRulesMultiFacB = IssueJwt(staffRulesMultiFacB, UserType.Staff, null, facB, ["InboundLead"], [Permissions.Inbound.PutawayRules, Permissions.Analytics.MultiFacilityView]);
        var jwtClient = IssueJwt(clientAdminId, UserType.Client, clientA, facA, [SystemRoles.ClientAdmin], [Permissions.Outbound.OrdersAllocate, Permissions.Inbound.PutawayRules]);
        var jwtDriver = IssueJwt(driverId, UserType.Driver, null, facA, [SystemRoles.Driver], [Permissions.Outbound.OrdersAllocate, Permissions.Inbound.PutawayRules]);

        // Allocate with strategyOverride: unauthorized roles/facilities/permissions rejected
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/orders/{orderFacAId}/allocate", null, new { strategyOverride = "FEFO" }))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/orders/{orderFacAId}/allocate", jwtClient, new { strategyOverride = "FEFO" }))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/orders/{orderFacAId}/allocate", jwtDriver, new { strategyOverride = "FEFO" }))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/orders/{orderFacAId}/allocate", jwtStaffNoAlloc, new { strategyOverride = "FEFO" }))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/orders/{orderFacAId}/allocate", jwtStaffMultiFacB, new { strategyOverride = "FEFO" }))).StatusCode);

        // Authorized facility staff succeeds and applies FEFO
        using (var okAlloc = await http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/orders/{orderFacAId}/allocate", jwtStaffAllocA, new { strategyOverride = "FEFO" })))
        {
            Assert.Equal(HttpStatusCode.OK, okAlloc.StatusCode);
        }
        using (var verifyScope = app.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var alloc = await db.InventoryAllocations.IgnoreQueryFilters().SingleAsync(a => a.OutboundOrderId == orderFacAId);
            Assert.Equal(AllocationStrategyType.FEFO, alloc.StrategyUsed);
        }

        // Putaway strategy rule policy management & reads authorization
        var createRulePayload = new { clientId = clientA, facilityId = facA, sequence = 1, ruleName = "Rule A", preferConsolidation = false };
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/putaway/strategies", jwtClient, createRulePayload))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/putaway/strategies", jwtDriver, createRulePayload))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/putaway/strategies", jwtStaffNoAlloc, createRulePayload))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/putaway/strategies", jwtStaffRulesMultiFacB, createRulePayload))).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/putaway/strategies", jwtStaffRulesA, createRulePayload))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(JsonReq(HttpMethod.Get, $"/api/v1/putaway/strategies?facilityId={facB}&clientId={clientA}", jwtStaffRulesA))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(JsonReq(HttpMethod.Get, $"/api/v1/putaway/strategies?facilityId={facA}&clientId={clientA}", jwtStaffRulesA))).StatusCode);
    }
}
