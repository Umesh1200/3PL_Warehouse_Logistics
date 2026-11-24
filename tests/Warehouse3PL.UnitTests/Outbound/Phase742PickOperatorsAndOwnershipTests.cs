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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Warehouse3PL.Application;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Authentication;
using Warehouse3PL.Infrastructure.Authorization;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;
using Warehouse3PL.Infrastructure.Services;
using Warehouse3PL.Infrastructure.Services.Picking;
using Warehouse3PL.WebApi.Controllers;
using Warehouse3PL.WebApi.Middleware;
using Warehouse3PL.WebApi.Security;
using Xunit;

namespace Warehouse3PL.UnitTests.Outbound;

public sealed class Phase742PickOperatorsAndOwnershipTests : IAsyncLifetime
{
    private const string TestJwtSecret = "Nexus3PL-Phase742-PickOperators-SigningKey-2026!";
    private const string TestIssuer = "nexus3pl-742-auth";
    private const string TestAudience = "nexus3pl-742-api";

    private static readonly Guid ClientA = Guid.Parse("742a0001-0000-0000-0000-000000000001");
    private static readonly Guid FacilityA = Guid.Parse("742a0002-0000-0000-0000-000000000001");
    private static readonly Guid FacilityB = Guid.Parse("742a0002-0000-0000-0000-000000000002");

    private static readonly Guid SupervisorFacAId = Guid.Parse("742a0003-0000-0000-0000-000000000001");
    private static readonly Guid PickerFacAId = Guid.Parse("742a0003-0000-0000-0000-000000000002");
    private static readonly Guid StaffFacBId = Guid.Parse("742a0003-0000-0000-0000-000000000003");
    private static readonly Guid InactiveStaffFacAId = Guid.Parse("742a0003-0000-0000-0000-000000000004");
    private static readonly Guid ClientUserId = Guid.Parse("742a0003-0000-0000-0000-000000000005");
    private static readonly Guid DriverUserId = Guid.Parse("742a0003-0000-0000-0000-000000000006");

    private WebApplication _app = null!;
    private HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Host.UseDefaultServiceProvider(o => o.ValidateOnBuild = false);
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();

        builder.Services.Configure<JwtOptions>(o =>
        {
            o.SecretKey = TestJwtSecret;
            o.Issuer = TestIssuer;
            o.Audience = TestAudience;
            o.ExpiryMinutes = 60;
        });

        builder.Services.AddControllers()
            .AddApplicationPart(typeof(PickTasksController).Assembly)
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
        builder.Services.AddApplicationServices();

        builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
        builder.Services.AddSingleton<ITokenService, JwtTokenService>();
        builder.Services.AddScoped<IPickExecutionService, PickExecutionService>();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = TestIssuer,
                    ValidAudience = TestAudience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSecret)),
                    ClockSkew = TimeSpan.Zero
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = JwtStaffFacilityValidator.ValidateAsync
                };
            });

        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
        builder.Services.AddScoped<ITenantProvider, TenantProvider>();
        builder.Services.AddScoped<IDateTimeProvider, DateTimeProvider>();
        builder.Services.AddScoped<AuditableEntityInterceptor>();

        var dbName = $"phase742-ops-{Guid.NewGuid():N}";
        builder.Services.AddDbContext<ApplicationDbContext>((sp, options) => options
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(sp.GetRequiredService<AuditableEntityInterceptor>()));
        builder.Services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        _app = builder.Build();
        _app.UseMiddleware<ExceptionHandlingMiddleware>();
        _app.UseRouting();
        _app.UseAuthentication();
        _app.UseAuthenticatedTenantResolution();
        _app.UseAuthorization();
        _app.MapControllers();
        await _app.StartAsync();
        _http = _app.GetTestClient();

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        await SeedAsync(db, hasher);
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static async Task SeedAsync(ApplicationDbContext db, IPasswordHasher hasher)
    {
        db.Facilities.AddRange(
            new Facility { Id = FacilityA, FacilityCode = "FAC-742-A", Name = "Chicago Hub", CountryCode = "USA", IsActive = true },
            new Facility { Id = FacilityB, FacilityCode = "FAC-742-B", Name = "Dallas Hub", CountryCode = "USA", IsActive = true });

        db.Clients.Add(new Client { Id = ClientA, AccountCode = "CL-742", CompanyName = "Delta Corp", Status = ClientStatus.Active });

        var supervisorRole = new Role { RoleName = "PickSupervisor", Description = "Supervisor without users:view", IsSystemRole = false };
        supervisorRole.RolePermissions.Add(new RolePermission { Role = supervisorRole, PermissionKey = Permissions.Outbound.TasksView, AccessLevel = AccessLevel.Read });
        supervisorRole.RolePermissions.Add(new RolePermission { Role = supervisorRole, PermissionKey = Permissions.Outbound.TasksManage, AccessLevel = AccessLevel.Write });

        var pickerRole = new Role { RoleName = "RfPickerOnly", Description = "Pick-only operator", IsSystemRole = false };
        pickerRole.RolePermissions.Add(new RolePermission { Role = pickerRole, PermissionKey = Permissions.Outbound.Pick, AccessLevel = AccessLevel.Execute });

        var legacyClientRole = new Role { RoleName = SystemRoles.ClientAdmin, Description = "Client with stale claim", IsSystemRole = true };
        legacyClientRole.RolePermissions.Add(new RolePermission { Role = legacyClientRole, PermissionKey = Permissions.Outbound.TasksManage, AccessLevel = AccessLevel.Write });

        var driverRole = new Role { RoleName = SystemRoles.Driver, Description = "Driver", IsSystemRole = true };
        driverRole.RolePermissions.Add(new RolePermission { Role = driverRole, PermissionKey = Permissions.Dispatch.ExecuteDelivery, AccessLevel = AccessLevel.Execute });

        db.Roles.AddRange(supervisorRole, pickerRole, legacyClientRole, driverRole);

        var pw = hasher.HashPassword("StrongPass#742");
        db.Users.AddRange(
            new User
            {
                Id = SupervisorFacAId,
                Email = "sup.a@test.local",
                FirstName = "Sam",
                LastName = "Supervisor",
                PasswordHash = pw,
                UserType = UserType.Staff,
                AssignedFacilityId = FacilityA,
                Status = UserStatus.Active,
                UserRoles = { new UserRole { UserId = SupervisorFacAId, Role = supervisorRole } }
            },
            new User
            {
                Id = PickerFacAId,
                Email = "picker.a@test.local",
                FirstName = "Pat",
                LastName = "Picker",
                PasswordHash = pw,
                UserType = UserType.Staff,
                AssignedFacilityId = FacilityA,
                Status = UserStatus.Active,
                UserRoles = { new UserRole { UserId = PickerFacAId, Role = pickerRole } }
            },
            new User
            {
                Id = StaffFacBId,
                Email = "picker.b@test.local",
                FirstName = "Bob",
                LastName = "DallasStaff",
                PasswordHash = pw,
                UserType = UserType.Staff,
                AssignedFacilityId = FacilityB,
                Status = UserStatus.Active,
                UserRoles = { new UserRole { UserId = StaffFacBId, Role = pickerRole } }
            },
            new User
            {
                Id = InactiveStaffFacAId,
                Email = "inactive.a@test.local",
                FirstName = "Ina",
                LastName = "Inactive",
                PasswordHash = pw,
                UserType = UserType.Staff,
                AssignedFacilityId = FacilityA,
                Status = UserStatus.Inactive
            },
            new User
            {
                Id = ClientUserId,
                Email = "client@test.local",
                FirstName = "Cli",
                LastName = "User",
                PasswordHash = pw,
                UserType = UserType.Client,
                ClientId = ClientA,
                Status = UserStatus.Active,
                UserRoles = { new UserRole { UserId = ClientUserId, Role = legacyClientRole } }
            },
            new User
            {
                Id = DriverUserId,
                Email = "driver@test.local",
                FirstName = "Dan",
                LastName = "Driver",
                PasswordHash = pw,
                UserType = UserType.Driver,
                Status = UserStatus.Active,
                UserRoles = { new UserRole { UserId = DriverUserId, Role = driverRole } }
            });

        await db.SaveChangesAsync();
    }

    private string MintToken(Guid userId)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var user = db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role).ThenInclude(r => r.RolePermissions)
            .Single(u => u.Id == userId);
        var roles = user.UserRoles.Select(ur => ur.Role.RoleName).ToList();
        var perms = user.UserRoles.SelectMany(ur => ur.Role.RolePermissions).Select(rp => rp.PermissionKey).Distinct().ToList();
        return tokenService.GenerateTokens(user, roles, perms).AccessToken;
    }

    private HttpRequestMessage AuthedReq(HttpMethod method, string url, Guid userId)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintToken(userId));
        return req;
    }

    [Fact]
    public async Task SupervisorWithoutUsersView_CanQueryFacilityScopedEligiblePickOperators()
    {
        // Confirm Supervisor lacks broad users:view directory access
        using (var usersResp = await _http.SendAsync(AuthedReq(HttpMethod.Get, "/api/v1/users", SupervisorFacAId)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, usersResp.StatusCode);
        }

        // Eligible operators query returns ONLY active UserType.Staff assigned to FacilityA
        using (var opsResp = await _http.SendAsync(AuthedReq(
            HttpMethod.Get, $"/api/v1/outbound/pick-tasks/eligible-operators?facilityId={FacilityA}", SupervisorFacAId)))
        {
            Assert.Equal(HttpStatusCode.OK, opsResp.StatusCode);
            using var doc = await JsonDocument.ParseAsync(await opsResp.Content.ReadAsStreamAsync());
            var ids = doc.RootElement.GetProperty("items").EnumerateArray()
                .Select(i => i.GetProperty("id").GetGuid())
                .ToList();

            Assert.Contains(SupervisorFacAId, ids);
            Assert.Contains(PickerFacAId, ids);
            Assert.DoesNotContain(StaffFacBId, ids);
            Assert.DoesNotContain(InactiveStaffFacAId, ids);
            Assert.DoesNotContain(ClientUserId, ids);
            Assert.DoesNotContain(DriverUserId, ids);
        }
    }

    [Fact]
    public async Task CrossFacilitySupervisor_PickOnly_Client_And_Driver_AreForbiddenFromEligibleOperators()
    {
        using (var crossFac = await _http.SendAsync(AuthedReq(
            HttpMethod.Get, $"/api/v1/outbound/pick-tasks/eligible-operators?facilityId={FacilityB}", SupervisorFacAId)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, crossFac.StatusCode);
        }

        using (var pickOnly = await _http.SendAsync(AuthedReq(
            HttpMethod.Get, $"/api/v1/outbound/pick-tasks/eligible-operators?facilityId={FacilityA}", PickerFacAId)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, pickOnly.StatusCode);
        }

        using (var clientForbidden = await _http.SendAsync(AuthedReq(
            HttpMethod.Get, $"/api/v1/outbound/pick-tasks/eligible-operators?facilityId={FacilityA}", ClientUserId)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, clientForbidden.StatusCode);
        }

        using (var driverForbidden = await _http.SendAsync(AuthedReq(
            HttpMethod.Get, "/api/v1/outbound/pick-tasks/eligible-operators", DriverUserId)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, driverForbidden.StatusCode);
        }
    }

    [Fact]
    public async Task SupervisorWithMultiFacilityView_StillCannotQueryCrossFacilityEligiblePickOperators()
    {
        var multiSupId = Guid.NewGuid();
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var role = new Role { RoleName = "PickSupervisorMultiFac", Description = "Supervisor with Analytics.MultiFacilityView", IsSystemRole = false };
            role.RolePermissions.Add(new RolePermission { Role = role, PermissionKey = Permissions.Outbound.TasksView, AccessLevel = AccessLevel.Read });
            role.RolePermissions.Add(new RolePermission { Role = role, PermissionKey = Permissions.Outbound.TasksManage, AccessLevel = AccessLevel.Write });
            role.RolePermissions.Add(new RolePermission { Role = role, PermissionKey = Permissions.Analytics.MultiFacilityView, AccessLevel = AccessLevel.Read });
            db.Roles.Add(role);

            db.Users.Add(new User
            {
                Id = multiSupId,
                Email = "sup.multi@test.local",
                FirstName = "Morgan",
                LastName = "Multi",
                PasswordHash = hasher.HashPassword("StrongPass#742"),
                UserType = UserType.Staff,
                AssignedFacilityId = FacilityA,
                Status = UserStatus.Active,
                UserRoles = { new UserRole { UserId = multiSupId, Role = role } }
            });
            await db.SaveChangesAsync();
        }

        // Combined TasksManage + Analytics.MultiFacilityView must remain strictly scoped to assigned FacilityA
        using (var crossFacResp = await _http.SendAsync(AuthedReq(
            HttpMethod.Get, $"/api/v1/outbound/pick-tasks/eligible-operators?facilityId={FacilityB}", multiSupId)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, crossFacResp.StatusCode);
        }

        using (var ownFacResp = await _http.SendAsync(AuthedReq(
            HttpMethod.Get, $"/api/v1/outbound/pick-tasks/eligible-operators?facilityId={FacilityA}", multiSupId)))
        {
            Assert.Equal(HttpStatusCode.OK, ownFacResp.StatusCode);
            using var doc = await JsonDocument.ParseAsync(await ownFacResp.Content.ReadAsStreamAsync());
            var ids = doc.RootElement.GetProperty("items").EnumerateArray()
                .Select(i => i.GetProperty("id").GetGuid())
                .ToList();
            Assert.Contains(PickerFacAId, ids);
            Assert.DoesNotContain(StaffFacBId, ids);
        }
    }

    [Fact]
    public async Task PositiveShortPick_RequiresExplicitNonEmptyAndMatchingScans_WhileZeroQuantityPreservesExceptionBehavior()
    {
        var binId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var linePosId = Guid.NewGuid();
        var lineZeroId = Guid.NewGuid();
        var allocPosId = Guid.NewGuid();
        var allocZeroId = Guid.NewGuid();
        var toteId = Guid.NewGuid();
        var waveId = Guid.NewGuid();
        var posTaskId = Guid.NewGuid();
        var zeroTaskId = Guid.NewGuid();

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Locations.Add(new Location
            {
                Id = binId,
                FacilityId = FacilityA,
                LocationCode = "BIN-742-A",
                Barcode = "BIN-742-A",
                AisleCode = "01",
                RackCode = "01",
                ShelfLevel = 1,
                IsActive = true,
                IsPickable = true
            });
            var item = new Item
            {
                Id = itemId,
                ClientId = ClientA,
                Sku = "SKU-742-A",
                Description = "Phase 7.4.2 Short Pick Test Item",
                IsLotTracked = false
            };
            db.Items.Add(item);
            db.StockOnHand.Add(new StockOnHand
            {
                Id = stockId,
                ClientId = ClientA,
                FacilityId = FacilityA,
                ItemId = itemId,
                LocationId = binId,
                QtyAvailable = 20m,
                QtyAllocated = 9m,
                ReceivedAtUtc = DateTime.UtcNow.AddDays(-1),
                Version = 1
            });

            var order = new OutboundOrder
            {
                Id = orderId,
                ClientId = ClientA,
                FacilityId = FacilityA,
                OrderNumber = "SO-742-SHORT-1",
                CustomerName = "Short Pick Verify Buyer",
                Status = OutboundOrderStatus.Allocated
            };
            order.Lines.Add(new OutboundOrderLine
            {
                Id = linePosId,
                OutboundOrderId = orderId,
                ItemId = itemId,
                LineNumber = 1,
                QuantityOrdered = 5m,
                QuantityAllocated = 5m,
                Status = OutboundOrderLineStatus.Allocated
            });
            order.Lines.Add(new OutboundOrderLine
            {
                Id = lineZeroId,
                OutboundOrderId = orderId,
                ItemId = itemId,
                LineNumber = 2,
                QuantityOrdered = 4m,
                QuantityAllocated = 4m,
                Status = OutboundOrderLineStatus.Allocated
            });
            db.OutboundOrders.Add(order);

            db.InventoryAllocations.AddRange(
                new InventoryAllocation
                {
                    Id = allocPosId,
                    ClientId = ClientA,
                    FacilityId = FacilityA,
                    OutboundOrderId = orderId,
                    OutboundOrderLineId = linePosId,
                    StockOnHandId = stockId,
                    ItemId = itemId,
                    LocationId = binId,
                    QuantityRequested = 5m,
                    QuantityAllocated = 5m,
                    Status = AllocationStatus.Allocated
                },
                new InventoryAllocation
                {
                    Id = allocZeroId,
                    ClientId = ClientA,
                    FacilityId = FacilityA,
                    OutboundOrderId = orderId,
                    OutboundOrderLineId = lineZeroId,
                    StockOnHandId = stockId,
                    ItemId = itemId,
                    LocationId = binId,
                    QuantityRequested = 4m,
                    QuantityAllocated = 4m,
                    Status = AllocationStatus.Allocated
                });

            db.HandlingUnits.Add(new HandlingUnit
            {
                Id = toteId,
                ClientId = ClientA,
                FacilityId = FacilityA,
                LpnCode = "TOTE-742-A",
                UnitType = HandlingUnitType.Tote,
                Status = HandlingUnitStatus.Available
            });

            var wave = new PickWave
            {
                Id = waveId,
                ClientId = ClientA,
                FacilityId = FacilityA,
                WaveNumber = "WAVE-742-SHORT",
                Status = PickWaveStatus.Released
            };
            wave.WaveOrders.Add(new PickWaveOrder { PickWaveId = waveId, OutboundOrderId = orderId, SequenceNumber = 1 });
            wave.Tasks.Add(new PickTask
            {
                Id = posTaskId,
                ClientId = ClientA,
                FacilityId = FacilityA,
                PickWaveId = waveId,
                OutboundOrderId = orderId,
                OutboundOrderLineId = linePosId,
                InventoryAllocationId = allocPosId,
                StockOnHandId = stockId,
                ItemId = itemId,
                SourceLocationId = binId,
                QuantityToPick = 5m,
                QuantityPicked = 0m,
                PickSequence = 1,
                Status = PickTaskStatus.Assigned,
                AssignedUserId = PickerFacAId,
                Version = 1
            });
            wave.Tasks.Add(new PickTask
            {
                Id = zeroTaskId,
                ClientId = ClientA,
                FacilityId = FacilityA,
                PickWaveId = waveId,
                OutboundOrderId = orderId,
                OutboundOrderLineId = lineZeroId,
                InventoryAllocationId = allocZeroId,
                StockOnHandId = stockId,
                ItemId = itemId,
                SourceLocationId = binId,
                QuantityToPick = 4m,
                QuantityPicked = 0m,
                PickSequence = 2,
                Status = PickTaskStatus.Assigned,
                AssignedUserId = PickerFacAId,
                Version = 1
            });
            db.PickWaves.Add(wave);
            await db.SaveChangesAsync();
        }

        // Negative cases: missing, blank, or mismatched scans for positive-quantity short picks must all fail 400
        var invalidPayloads = new object[]
        {
            new { quantityPicked = 2m, reason = ShortPickReason.DamagedStock, scannedItemBarcode = "SKU-742-A", scannedToteLpn = "TOTE-742-A" },
            new { quantityPicked = 2m, reason = ShortPickReason.DamagedStock, scannedLocationBarcode = "BIN-742-A", scannedToteLpn = "TOTE-742-A" },
            new { quantityPicked = 2m, reason = ShortPickReason.DamagedStock, scannedLocationBarcode = "BIN-742-A", scannedItemBarcode = "SKU-742-A" },
            new { quantityPicked = 2m, reason = ShortPickReason.DamagedStock, scannedLocationBarcode = "   ", scannedItemBarcode = "SKU-742-A", scannedToteLpn = "TOTE-742-A" },
            new { quantityPicked = 2m, reason = ShortPickReason.DamagedStock, scannedLocationBarcode = "WRONG-BIN", scannedItemBarcode = "SKU-742-A", scannedToteLpn = "TOTE-742-A" },
            new { quantityPicked = 2m, reason = ShortPickReason.DamagedStock, scannedLocationBarcode = "BIN-742-A", scannedItemBarcode = "WRONG-SKU", scannedToteLpn = "TOTE-742-A" }
        };

        foreach (var payload in invalidPayloads)
        {
            using var req = AuthedReq(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{posTaskId}/short-pick", PickerFacAId);
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var resp = await _http.SendAsync(req);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        using (var verifyNoMoveScope = _app.Services.CreateScope())
        {
            var db = verifyNoMoveScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var src = await db.StockOnHand.IgnoreQueryFilters().SingleAsync(s => s.Id == stockId);
            var posTask = await db.PickTasks.IgnoreQueryFilters().SingleAsync(t => t.Id == posTaskId);
            Assert.Equal(9m, src.QtyAllocated);
            Assert.Equal(PickTaskStatus.Assigned, posTask.Status);
            Assert.Empty(await db.InventoryTransactions.IgnoreQueryFilters().Where(t => t.ReferenceId == posTaskId).ToListAsync());
            Assert.Empty(await db.PickExceptions.IgnoreQueryFilters().Where(e => e.PickTaskId == posTaskId).ToListAsync());
        }

        // Valid explicit scans succeed for positive-quantity short-pick
        using (var okPosReq = AuthedReq(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{posTaskId}/short-pick", PickerFacAId))
        {
            okPosReq.Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    quantityPicked = 2m,
                    reason = ShortPickReason.DamagedStock,
                    notes = "2 good units, 3 damaged",
                    scannedLocationBarcode = "BIN-742-A",
                    scannedItemBarcode = "SKU-742-A",
                    scannedToteLpn = "TOTE-742-A"
                }),
                Encoding.UTF8,
                "application/json");
            using var okPosResp = await _http.SendAsync(okPosReq);
            Assert.Equal(HttpStatusCode.OK, okPosResp.StatusCode);
        }

        // Zero-quantity exception succeeds without scan fields
        using (var okZeroReq = AuthedReq(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{zeroTaskId}/short-pick", PickerFacAId))
        {
            okZeroReq.Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    quantityPicked = 0m,
                    reason = ShortPickReason.BinEmpty,
                    notes = "Empty bin zero-qty exception"
                }),
                Encoding.UTF8,
                "application/json");
            using var okZeroResp = await _http.SendAsync(okZeroReq);
            Assert.Equal(HttpStatusCode.OK, okZeroResp.StatusCode);
        }

        using (var finalScope = _app.Services.CreateScope())
        {
            var db = finalScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var src = await db.StockOnHand.IgnoreQueryFilters().SingleAsync(s => s.Id == stockId);
            var toteStock = await db.StockOnHand.IgnoreQueryFilters().SingleAsync(s => s.HandlingUnitId == toteId);
            var zeroTask = await db.PickTasks.IgnoreQueryFilters().SingleAsync(t => t.Id == zeroTaskId);
            Assert.Equal(0m, src.QtyAllocated);
            Assert.Equal(2m, toteStock.QtyAllocated);
            Assert.Equal(PickTaskStatus.ShortPicked, zeroTask.Status);
            Assert.Single(await db.InventoryTransactions.IgnoreQueryFilters().Where(t => t.ReferenceId == posTaskId && t.TxType == InventoryTxType.Pick).ToListAsync());
        }
    }
}
