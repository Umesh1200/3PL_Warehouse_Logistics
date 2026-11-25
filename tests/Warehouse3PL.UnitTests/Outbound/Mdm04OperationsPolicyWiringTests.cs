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
            var foreignClientB = Guid.NewGuid();
            var suspendedClientC = Guid.NewGuid();
            db.Clients.AddRange(
                new Client { Id = clientA, CompanyName = "Auth Client", AccountCode = "ACL", Status = ClientStatus.Active },
                new Client { Id = foreignClientB, CompanyName = "Foreign Client B", AccountCode = "FCB", Status = ClientStatus.Active },
                new Client { Id = suspendedClientC, CompanyName = "Suspended Client C", AccountCode = "SCC", Status = ClientStatus.Active });
            db.Facilities.AddRange(
                new Facility { Id = facA, FacilityCode = "FA", Name = "Hub A", AddressLine1 = "1 Main", City = "Chicago", CountryCode = "US", IsActive = true },
                new Facility { Id = facB, FacilityCode = "FB", Name = "Hub B", AddressLine1 = "2 Main", City = "Dallas", CountryCode = "US", IsActive = true });
            db.ClientFacilityAccesses.AddRange(
                new ClientFacilityAccess { ClientId = clientA, FacilityId = facA, Status = FacilityAccessStatus.Active },
                new ClientFacilityAccess { ClientId = foreignClientB, FacilityId = facB, Status = FacilityAccessStatus.Active },
                new ClientFacilityAccess { ClientId = suspendedClientC, FacilityId = facA, Status = FacilityAccessStatus.Revoked });
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

        // MDM04-R03: Staff at Facility A cannot create rules for foreign client, inactive relationship, arbitrary client, or Guid.Empty global rule
        Guid foreignClientIdFromDb;
        Guid suspendedClientIdFromDb;
        using (var lookupScope = app.Services.CreateScope())
        {
            var db = lookupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            foreignClientIdFromDb = (await db.Clients.SingleAsync(c => c.AccountCode == "FCB")).Id;
            suspendedClientIdFromDb = (await db.Clients.SingleAsync(c => c.AccountCode == "SCC")).Id;
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/putaway/strategies", jwtStaffRulesA, new { clientId = foreignClientIdFromDb, facilityId = facA, sequence = 2, ruleName = "Foreign Client", preferConsolidation = false }))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/putaway/strategies", jwtStaffRulesA, new { clientId = suspendedClientIdFromDb, facilityId = facA, sequence = 3, ruleName = "Suspended Link", preferConsolidation = false }))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/putaway/strategies", jwtStaffRulesA, new { clientId = Guid.NewGuid(), facilityId = facA, sequence = 4, ruleName = "Arbitrary Client", preferConsolidation = false }))).StatusCode);

        // MDM04-R05: Guid.Empty facility-wide rule creation is cleanly rejected (400 ValidationException) for all authorized callers until a schema-approved facility-default model exists
        var staffGlobalResp = await http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/putaway/strategies", jwtStaffRulesA, new { clientId = Guid.Empty, facilityId = facA, sequence = 5, ruleName = "Staff Global Blocked", preferConsolidation = false }));
        Assert.Equal(HttpStatusCode.BadRequest, staffGlobalResp.StatusCode);

        var jwtSuperAdmin = IssueJwt(Guid.NewGuid(), UserType.Staff, null, null, [SystemRoles.SuperAdmin], []);
        var superAdminGlobalResp = await http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/putaway/strategies", jwtSuperAdmin, new { clientId = Guid.Empty, facilityId = facA, sequence = 6, ruleName = "SuperAdmin Global Rejected", preferConsolidation = true }));
        Assert.Equal(HttpStatusCode.BadRequest, superAdminGlobalResp.StatusCode);
        var superAdminBody = await superAdminGlobalResp.Content.ReadAsStringAsync();
        Assert.Contains("Guid.Empty", superAdminBody);

        Assert.Equal(HttpStatusCode.Created, (await http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/putaway/strategies", jwtStaffRulesA, createRulePayload))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(JsonReq(HttpMethod.Get, $"/api/v1/putaway/strategies?facilityId={facB}&clientId={clientA}", jwtStaffRulesA))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(JsonReq(HttpMethod.Get, $"/api/v1/putaway/strategies?facilityId={facA}&clientId={clientA}", jwtStaffRulesA))).StatusCode);
    }

    [Fact]
    public async Task ConcurrentAllocation_TotalBinDepletion_ReplansAndConservesAcrossAllBins()
    {
        var dbName = $"mdm04-conc-alloc-{Guid.NewGuid():N}";
        var clientId = Guid.NewGuid();
        var facId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var bin1Id = Guid.NewGuid();
        var bin2Id = Guid.NewGuid();
        var bin3Id = Guid.NewGuid();
        var huId = Guid.NewGuid();
        var orderIds = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();

        var tenantProvider = new TestTenantProvider { TenantId = clientId, BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), UserType = UserType.Staff, FacilityId = facId };

        await using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Locations.AddRange(
                new Location { Id = bin1Id, FacilityId = facId, LocationCode = "B1", LocationType = LocationType.Bin, PickSequence = 1, IsPickable = true, IsActive = true },
                new Location { Id = bin2Id, FacilityId = facId, LocationCode = "B2", LocationType = LocationType.Bin, PickSequence = 5, IsPickable = true, IsActive = true },
                new Location { Id = bin3Id, FacilityId = facId, LocationCode = "B3", LocationType = LocationType.Bin, PickSequence = 1, IsPickable = true, IsActive = true });

            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-RACE", StorageTempClass = StorageTempClass.Ambient });
            seed.HandlingUnits.Add(new HandlingUnit { Id = huId, ClientId = clientId, FacilityId = facId, LpnCode = "LPN-40", UnitType = HandlingUnitType.Pallet });

            var sameUtc = DateTime.UtcNow.AddDays(-1);
            seed.StockOnHand.AddRange(
                new StockOnHand { ClientId = clientId, FacilityId = facId, ItemId = itemId, LocationId = bin1Id, QtyAvailable = 15m, ReceivedAtUtc = DateTime.UtcNow.AddDays(-20) },
                new StockOnHand { ClientId = clientId, FacilityId = facId, ItemId = itemId, LocationId = bin2Id, HandlingUnitId = huId, LpnCode = "LPN-40", QtyAvailable = 40m, ReceivedAtUtc = sameUtc },
                new StockOnHand { ClientId = clientId, FacilityId = facId, ItemId = itemId, LocationId = bin3Id, QtyAvailable = 20m, ReceivedAtUtc = sameUtc });

            seed.AllocationPolicies.Add(new AllocationPolicy
            {
                ClientId = clientId,
                FacilityId = facId,
                DefaultStrategy = AllocationStrategyType.FIFO,
                PreferFullHandlingUnits = true,
                AllowPartialAllocation = true,
                IsActive = true
            });

            foreach (var oid in orderIds)
            {
                var ord = new OutboundOrder { Id = oid, ClientId = clientId, FacilityId = facId, OrderNumber = $"ORD-{oid:N}"[..12], CustomerName = "C", Status = OutboundOrderStatus.Released };
                ord.Lines.Add(new OutboundOrderLine { ItemId = itemId, LineNumber = 1, QuantityOrdered = 25m });
                seed.OutboundOrders.Add(ord);
            }

            await seed.SaveChangesAsync();
        }

        var results = await Task.WhenAll(orderIds.Select(oid => Task.Run(async () =>
        {
            await using var db = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
            var (_, exec) = CreateAllocationServices(db, tenantProvider);
            return await exec.ExecuteOrderAllocationAsync(oid, userId: currentUser.UserId);
        })));

        Assert.Equal(75m, results.Sum(r => r.TotalQuantityAllocated));

        await using var chk = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var stocks = await chk.StockOnHand.Where(s => s.ItemId == itemId).ToListAsync();
        Assert.All(stocks, s => Assert.True(s.QtyAvailable >= 0m));
        Assert.Equal(0m, stocks.Sum(s => s.QtyAvailable));
        Assert.Equal(75m, stocks.Sum(s => s.QtyAllocated));
    }

    [Fact]
    public async Task PutawaySafety_StrictTemperatureCompatibility_EnforcedAcrossSelectorAndConfirmation()
    {
        var dbName = $"mdm04-temp-{Guid.NewGuid():N}";
        var clientId = Guid.NewGuid();
        var facId = Guid.NewGuid();
        var stagingId = Guid.NewGuid();
        var binBulkUnconfiguredId = Guid.NewGuid();
        var binColdUnspecifiedId = Guid.NewGuid();
        var binChill4CId = Guid.NewGuid();
        var binChillBadMinId = Guid.NewGuid();
        var binChillTooWarmId = Guid.NewGuid();
        var binFreezerId = Guid.NewGuid();
        var binControlled15To25Id = Guid.NewGuid();
        var binControlledBadRangeId = Guid.NewGuid();

        var frozenItemId = Guid.NewGuid();
        var chilledItemId = Guid.NewGuid();
        var controlledItemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId, BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), UserType = UserType.Staff, FacilityId = facId };

        await using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = clientId, FacilityId = facId, Status = FacilityAccessStatus.Active });

            var zBulk = new WarehouseZone { Id = Guid.NewGuid(), FacilityId = facId, ZoneCode = "Z-BULK", ZoneType = WarehouseZoneType.BulkStorage };
            var zColdNull = new WarehouseZone { Id = Guid.NewGuid(), FacilityId = facId, ZoneCode = "Z-CLD-NULL", ZoneType = WarehouseZoneType.ColdStorage };
            var zChill4 = new WarehouseZone { Id = Guid.NewGuid(), FacilityId = facId, ZoneCode = "Z-CHILL-4", ZoneType = WarehouseZoneType.ColdStorage, TempMinC = 2m, TempMaxC = 4m };
            var zChillBadMin = new WarehouseZone { Id = Guid.NewGuid(), FacilityId = facId, ZoneCode = "Z-CHILL-SUB0", ZoneType = WarehouseZoneType.ColdStorage, TempMinC = -5m, TempMaxC = 4m };
            var zChillWarm = new WarehouseZone { Id = Guid.NewGuid(), FacilityId = facId, ZoneCode = "Z-CHILL-12", ZoneType = WarehouseZoneType.ColdStorage, TempMinC = 2m, TempMaxC = 12m };
            var zFreezer = new WarehouseZone { Id = Guid.NewGuid(), FacilityId = facId, ZoneCode = "Z-FRZ-20", ZoneType = WarehouseZoneType.ColdStorage, TempMinC = -25m, TempMaxC = -20m };
            var zCtrlOk = new WarehouseZone { Id = Guid.NewGuid(), FacilityId = facId, ZoneCode = "Z-CTRL-OK", ZoneType = WarehouseZoneType.BulkStorage, TempMinC = 15m, TempMaxC = 25m };
            var zCtrlBad = new WarehouseZone { Id = Guid.NewGuid(), FacilityId = facId, ZoneCode = "Z-CTRL-HOT", ZoneType = WarehouseZoneType.BulkStorage, TempMinC = 18m, TempMaxC = 30m };
            seed.WarehouseZones.AddRange(zBulk, zColdNull, zChill4, zChillBadMin, zChillWarm, zFreezer, zCtrlOk, zCtrlBad);

            seed.Locations.AddRange(
                new Location { Id = stagingId, FacilityId = facId, LocationCode = "STG", Barcode = "STG", LocationType = LocationType.StagingLane, IsReceiving = true },
                new Location { Id = binBulkUnconfiguredId, FacilityId = facId, ZoneId = zBulk.Id, LocationCode = "B-BULK", Barcode = "B-BULK", LocationType = LocationType.Bin },
                new Location { Id = binColdUnspecifiedId, FacilityId = facId, ZoneId = zColdNull.Id, LocationCode = "B-CLD-NULL", Barcode = "B-CLD-NULL", LocationType = LocationType.Bin },
                new Location { Id = binChill4CId, FacilityId = facId, ZoneId = zChill4.Id, LocationCode = "B-CHILL-4", Barcode = "B-CHILL-4", LocationType = LocationType.Bin },
                new Location { Id = binChillBadMinId, FacilityId = facId, ZoneId = zChillBadMin.Id, LocationCode = "B-CHILL-NEG", Barcode = "B-CHILL-NEG", LocationType = LocationType.Bin },
                new Location { Id = binChillTooWarmId, FacilityId = facId, ZoneId = zChillWarm.Id, LocationCode = "B-CHILL-12", Barcode = "B-CHILL-12", LocationType = LocationType.Bin },
                new Location { Id = binFreezerId, FacilityId = facId, ZoneId = zFreezer.Id, LocationCode = "B-FRZ-20", Barcode = "B-FRZ-20", LocationType = LocationType.Bin },
                new Location { Id = binControlled15To25Id, FacilityId = facId, ZoneId = zCtrlOk.Id, LocationCode = "B-CTRL-OK", Barcode = "B-CTRL-OK", LocationType = LocationType.Bin },
                new Location { Id = binControlledBadRangeId, FacilityId = facId, ZoneId = zCtrlBad.Id, LocationCode = "B-CTRL-BAD", Barcode = "B-CTRL-BAD", LocationType = LocationType.Bin });

            seed.Items.AddRange(
                new Item { Id = frozenItemId, ClientId = clientId, Sku = "SKU-FRZ", StorageTempClass = StorageTempClass.Frozen, WeightKg = 1m },
                new Item { Id = chilledItemId, ClientId = clientId, Sku = "SKU-CHL", StorageTempClass = StorageTempClass.Refrigerated, WeightKg = 1m },
                new Item { Id = controlledItemId, ClientId = clientId, Sku = "SKU-TC", StorageTempClass = StorageTempClass.TemperatureControlled, WeightKg = 1m });

            seed.StockOnHand.AddRange(
                new StockOnHand { ClientId = clientId, FacilityId = facId, ItemId = frozenItemId, LocationId = stagingId, QtyAvailable = 10m },
                new StockOnHand { ClientId = clientId, FacilityId = facId, ItemId = chilledItemId, LocationId = stagingId, QtyAvailable = 10m },
                new StockOnHand { ClientId = clientId, FacilityId = facId, ItemId = controlledItemId, LocationId = stagingId, QtyAvailable = 10m });

            await seed.SaveChangesAsync();
        }

        await using var ctx = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var selector = new PutawayLocationSelector(ctx, NullLogger<PutawayLocationSelector>.Instance);

        // 1. Frozen: ONLY -20C freezer eligible (+4C and generic ColdStorage null TempMaxC rejected!)
        var frozenRecs = await selector.EvaluateCandidateLocationsAsync(clientId, facId, frozenItemId, 2m);
        Assert.Single(frozenRecs);
        Assert.Equal(binFreezerId, frozenRecs[0].LocationId);

        // 2. Refrigerated (2-8C): ONLY +2..4C chilled bin eligible (freezer -20C, sub-zero -5..4C, +12C warm, and null TempMaxC ColdStorage rejected!)
        var chilledRecs = await selector.EvaluateCandidateLocationsAsync(clientId, facId, chilledItemId, 2m);
        Assert.Single(chilledRecs);
        Assert.Equal(binChill4CId, chilledRecs[0].LocationId);

        // 3. TemperatureControlled (15-25C): ONLY 15..25C zone eligible (unconfigured Bulk, +4C Cold, 18..30C rejected!)
        var tcRecs = await selector.EvaluateCandidateLocationsAsync(clientId, facId, controlledItemId, 2m);
        Assert.Single(tcRecs);
        Assert.Equal(binControlled15To25Id, tcRecs[0].LocationId);

        // Confirmation rejections & valid confirmations via ConfirmPutawayTaskCommandHandler
        async Task<Guid> AddTaskAsync(Guid item, Guid targetBin, string lpn)
        {
            var t = new PutawayTask { ClientId = clientId, FacilityId = facId, ItemId = item, SourceLocationId = stagingId, SuggestedLocationId = targetBin, LpnCode = lpn, Quantity = 2m, Status = PutawayTaskStatus.Pending };
            ctx.PutawayTasks.Add(t);
            await ctx.SaveChangesAsync();
            return t.Id;
        }

        var handler = new ConfirmPutawayTaskCommandHandler(ctx, currentUser, new WarehouseOperationalAuthorization(ctx, currentUser));
        var tFrzInChill = await AddTaskAsync(frozenItemId, binChill4CId, "L-FRZ-1");
        var tFrzInNullCold = await AddTaskAsync(frozenItemId, binColdUnspecifiedId, "L-FRZ-2");
        var tFrzOk = await AddTaskAsync(frozenItemId, binFreezerId, "L-FRZ-OK");
        var tChlInFreezer = await AddTaskAsync(chilledItemId, binFreezerId, "L-CHL-1");
        var tChlInWarm = await AddTaskAsync(chilledItemId, binChillTooWarmId, "L-CHL-2");
        var tTcInBulk = await AddTaskAsync(controlledItemId, binBulkUnconfiguredId, "L-TC-1");
        var tTcInCold = await AddTaskAsync(controlledItemId, binChill4CId, "L-TC-2");
        var tTcOk = await AddTaskAsync(controlledItemId, binControlled15To25Id, "L-TC-OK");

        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(new ConfirmPutawayTaskCommand { TaskId = tFrzInChill, ScannedLpn = "L-FRZ-1", ScannedLocationBarcode = "B-CHILL-4" }, CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(new ConfirmPutawayTaskCommand { TaskId = tFrzInNullCold, ScannedLpn = "L-FRZ-2", ScannedLocationBarcode = "B-CLD-NULL" }, CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(new ConfirmPutawayTaskCommand { TaskId = tChlInFreezer, ScannedLpn = "L-CHL-1", ScannedLocationBarcode = "B-FRZ-20" }, CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(new ConfirmPutawayTaskCommand { TaskId = tChlInWarm, ScannedLpn = "L-CHL-2", ScannedLocationBarcode = "B-CHILL-12" }, CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(new ConfirmPutawayTaskCommand { TaskId = tTcInBulk, ScannedLpn = "L-TC-1", ScannedLocationBarcode = "B-BULK" }, CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(new ConfirmPutawayTaskCommand { TaskId = tTcInCold, ScannedLpn = "L-TC-2", ScannedLocationBarcode = "B-CHILL-4" }, CancellationToken.None));

        Assert.Equal(PutawayTaskStatus.Completed, (await handler.Handle(new ConfirmPutawayTaskCommand { TaskId = tFrzOk, ScannedLpn = "L-FRZ-OK", ScannedLocationBarcode = "B-FRZ-20" }, CancellationToken.None)).Status);
        Assert.Equal(PutawayTaskStatus.Completed, (await handler.Handle(new ConfirmPutawayTaskCommand { TaskId = tTcOk, ScannedLpn = "L-TC-OK", ScannedLocationBarcode = "B-CTRL-OK" }, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task PutawayRulePrecedence_ClientRestrictionBlocksGlobalOverflow_AndExcludesMismatchedTempClass()
    {
        var dbName = $"mdm04-rule-prec-{Guid.NewGuid():N}";
        var clientId = Guid.NewGuid();
        var facId = Guid.NewGuid();
        var stagingId = Guid.NewGuid();
        var dryBinId = Guid.NewGuid();
        var coldBinId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId, BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), UserType = UserType.Staff, FacilityId = facId };

        await using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = clientId, FacilityId = facId, Status = FacilityAccessStatus.Active });
            var zDry = new WarehouseZone { Id = Guid.NewGuid(), FacilityId = facId, ZoneCode = "Z-DRY", ZoneType = WarehouseZoneType.BulkStorage };
            var zCold = new WarehouseZone { Id = Guid.NewGuid(), FacilityId = facId, ZoneCode = "Z-CLD", ZoneType = WarehouseZoneType.ColdStorage, TempMinC = 2m, TempMaxC = 4m };
            seed.WarehouseZones.AddRange(zDry, zCold);

            seed.Locations.AddRange(
                new Location { Id = stagingId, FacilityId = facId, LocationCode = "STG", Barcode = "STG", LocationType = LocationType.StagingLane, IsReceiving = true },
                new Location { Id = dryBinId, FacilityId = facId, ZoneId = zDry.Id, LocationCode = "B-DRY", Barcode = "B-DRY", LocationType = LocationType.Bin },
                new Location { Id = coldBinId, FacilityId = facId, ZoneId = zCold.Id, LocationCode = "B-CLD", Barcode = "B-CLD", LocationType = LocationType.Bin });

            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-AMB", StorageTempClass = StorageTempClass.Ambient, AbcVelocity = AbcVelocity.A, WeightKg = 1m });
            seed.StockOnHand.Add(new StockOnHand { ClientId = clientId, FacilityId = facId, ItemId = itemId, LocationId = stagingId, QtyAvailable = 10m });

            // Mismatched Frozen rule allowing overflow MUST NOT apply to Ambient item
            seed.PutawayStrategyRules.Add(new PutawayStrategyRule
            {
                ClientId = clientId,
                FacilityId = facId,
                Sequence = 1,
                RuleName = "Frozen Overflow",
                TempClassRequirement = StorageTempClass.Frozen,
                PreferConsolidation = false,
                AllowDynamicOverflow = true,
                IsActive = true
            });

            await seed.SaveChangesAsync();
        }

        await using var ctx = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var selector = new PutawayLocationSelector(ctx, NullLogger<PutawayLocationSelector>.Instance);
        var handler = new ConfirmPutawayTaskCommandHandler(ctx, currentUser, new WarehouseOperationalAuthorization(ctx, currentUser));

        // 1. Only Frozen overflow rule exists -> Ambient item must still reject ColdStorage
        var recs1 = await selector.EvaluateCandidateLocationsAsync(clientId, facId, itemId, 2m);
        Assert.DoesNotContain(recs1, r => r.LocationId == coldBinId);

        // 2. Add global (Guid.Empty) Ambient rule allowing overflow (seq 1) AND client-specific Ambient rule prohibiting overflow (seq 10)
        ctx.PutawayStrategyRules.AddRange(
            new PutawayStrategyRule { ClientId = Guid.Empty, FacilityId = facId, Sequence = 1, RuleName = "Global Allow Overflow", TempClassRequirement = StorageTempClass.Ambient, PreferConsolidation = true, AllowDynamicOverflow = true, IsActive = true },
            new PutawayStrategyRule { ClientId = clientId, FacilityId = facId, Sequence = 10, RuleName = "Client Deny Overflow", TempClassRequirement = StorageTempClass.Ambient, PreferConsolidation = true, AllowDynamicOverflow = false, IsActive = true });
        await ctx.SaveChangesAsync();

        var recs2 = await selector.EvaluateCandidateLocationsAsync(clientId, facId, itemId, 2m);
        Assert.DoesNotContain(recs2, r => r.LocationId == coldBinId);

        var tAmbientInCold = new PutawayTask { ClientId = clientId, FacilityId = facId, ItemId = itemId, SourceLocationId = stagingId, SuggestedLocationId = coldBinId, LpnCode = "L-AMB-CLD", Quantity = 2m, Status = PutawayTaskStatus.Pending };
        ctx.PutawayTasks.Add(tAmbientInCold);
        await ctx.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            handler.Handle(new ConfirmPutawayTaskCommand { TaskId = tAmbientInCold.Id, ScannedLpn = "L-AMB-CLD", ScannedLocationBarcode = "B-CLD" }, CancellationToken.None));
    }

    [Fact]
    public async Task PreferFullHandlingUnits_ResidualDemandAware_AvoidsBreakingPartialPallet()
    {
        var dbName = $"mdm04-r04-hu-{Guid.NewGuid():N}";
        var clientId = Guid.NewGuid();
        var facId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var binHuA = Guid.NewGuid();
        var binHuB = Guid.NewGuid();
        var binLoose = Guid.NewGuid();
        var huA = Guid.NewGuid();
        var huB = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var receivedAt = new DateTime(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc);

        var tenantProvider = new TestTenantProvider { TenantId = clientId, BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), UserType = UserType.Staff, FacilityId = facId };

        await using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Locations.AddRange(
                new Location { Id = binHuA, FacilityId = facId, LocationCode = "BIN-HU-A", LocationType = LocationType.Bin, PickSequence = 10, IsPickable = true, IsActive = true },
                new Location { Id = binHuB, FacilityId = facId, LocationCode = "BIN-HU-B", LocationType = LocationType.Bin, PickSequence = 1, IsPickable = true, IsActive = true },
                new Location { Id = binLoose, FacilityId = facId, LocationCode = "BIN-LOOSE", LocationType = LocationType.Bin, PickSequence = 5, IsPickable = true, IsActive = true });

            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-R04", StorageTempClass = StorageTempClass.Ambient });
            seed.HandlingUnits.AddRange(
                new HandlingUnit { Id = huA, ClientId = clientId, FacilityId = facId, LpnCode = "HU-A-80", UnitType = HandlingUnitType.Pallet },
                new HandlingUnit { Id = huB, ClientId = clientId, FacilityId = facId, LpnCode = "HU-B-50", UnitType = HandlingUnitType.Pallet });

            // Exact MDM04-R04 scenario: 100-unit order, equal FIFO tier -> HU-A (80), HU-B (50), Loose (30)
            seed.StockOnHand.AddRange(
                new StockOnHand { ClientId = clientId, FacilityId = facId, ItemId = itemId, LocationId = binHuA, HandlingUnitId = huA, LpnCode = "HU-A-80", QtyAvailable = 80m, ReceivedAtUtc = receivedAt },
                new StockOnHand { ClientId = clientId, FacilityId = facId, ItemId = itemId, LocationId = binHuB, HandlingUnitId = huB, LpnCode = "HU-B-50", QtyAvailable = 50m, ReceivedAtUtc = receivedAt },
                new StockOnHand { ClientId = clientId, FacilityId = facId, ItemId = itemId, LocationId = binLoose, QtyAvailable = 30m, ReceivedAtUtc = receivedAt });

            seed.AllocationPolicies.Add(new AllocationPolicy
            {
                ClientId = clientId,
                FacilityId = facId,
                DefaultStrategy = AllocationStrategyType.FIFO,
                PreferFullHandlingUnits = true,
                AllowPartialAllocation = true,
                IsActive = true
            });

            var order = new OutboundOrder { Id = orderId, ClientId = clientId, FacilityId = facId, OrderNumber = "ORD-R04", CustomerName = "R04", Status = OutboundOrderStatus.Released };
            order.Lines.Add(new OutboundOrderLine { ItemId = itemId, LineNumber = 1, QuantityOrdered = 100m });
            seed.OutboundOrders.Add(order);
            await seed.SaveChangesAsync();
        }

        await using var ctx = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var (_, exec) = CreateAllocationServices(ctx, tenantProvider);
        var result = await exec.ExecuteOrderAllocationAsync(orderId, userId: currentUser.UserId);

        Assert.Equal(100m, result.TotalQuantityAllocated);
        var huAStock = await ctx.StockOnHand.SingleAsync(s => s.LocationId == binHuA);
        var huBStock = await ctx.StockOnHand.SingleAsync(s => s.LocationId == binHuB);
        var looseStock = await ctx.StockOnHand.SingleAsync(s => s.LocationId == binLoose);

        Assert.Equal(80m, huAStock.QtyAllocated);
        Assert.Equal(20m, looseStock.QtyAllocated);
        Assert.Equal(50m, huBStock.QtyAvailable); // HU-B must remain completely unbroken
        Assert.Equal(0m, huBStock.QtyAllocated);
    }
}
