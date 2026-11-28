using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Warehouse3PL.Application;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.Clients;
using Warehouse3PL.Application.Features.Clients.Commands.CreateClient;
using Warehouse3PL.Application.Features.Clients.Commands.DeactivateClient;
using Warehouse3PL.Application.Features.Clients.Commands.UpdateClient;
using Warehouse3PL.Application.Features.Clients.Queries.GetClientById;
using Warehouse3PL.Application.Features.Clients.Queries.GetClients;
using Warehouse3PL.Application.Features.Customers;
using Warehouse3PL.Application.Features.Inbound.Asns.Commands.CreateAsn;
using Warehouse3PL.Application.Features.Inbound.PurchaseOrders.Commands.CreatePurchaseOrder;
using Warehouse3PL.Application.Features.Inbound.Suppliers;
using Warehouse3PL.Application.Features.Inbound.Suppliers.Commands.CreateSupplier;
using Warehouse3PL.Application.Features.Inbound.Suppliers.Queries.GetSuppliers;
using Warehouse3PL.Application.Features.Outbound.OrderManagement.Commands.CreateOutboundOrder;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Authentication;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;
using Warehouse3PL.Infrastructure.Persistence.Seed;
using Warehouse3PL.Infrastructure.Services;
using Warehouse3PL.UnitTests.Common;
using Warehouse3PL.WebApi.Controllers;
using Warehouse3PL.WebApi.Middleware;
using Xunit;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.UnitTests.Clients;

public sealed class Mdm22ClientCustomerSupplierTests
{
    private const string JwtSecret = "Nexus3PL-Mdm22-Unit-Security-Verification-Key-2026!";
    private const string JwtIssuer = "nexus3pl-mdm22-test";
    private const string JwtAudience = "nexus3pl-mdm22-api";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private static async Task<(ApplicationDbContext Db, TestCurrentUserContext Admin, Guid FacilityA, Guid FacilityB, Guid ClientA, Guid ClientB)>
        CreateSeededContextAsync()
    {
        var dbName = $"mdm22_{Guid.NewGuid():N}";
        var facA = Guid.NewGuid();
        var facB = Guid.NewGuid();
        var clientA = Guid.NewGuid();
        var clientB = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        var tenant = new TestTenantProvider { BypassTenantFilter = true };
        var admin = new TestCurrentUserContext
        {
            UserId = adminId,
            UserType = UserType.Staff,
            FacilityId = facA,
            RolesList = [SystemRoles.SuperAdmin],
            PermissionsList = Permissions.GetAllPermissions().ToList()
        };

        var db = TestDbContextFactory.Create(tenant, admin, dbName);
        await ReferenceCatalogSeeder.SeedAsync(db);

        db.Facilities.AddRange(
            new Facility { Id = facA, FacilityCode = "FAC-M22-1", Name = "Chicago Hub", CountryCode = "US", Timezone = "America/Chicago", DefaultCurrencyCode = "USD", IsActive = true },
            new Facility { Id = facB, FacilityCode = "FAC-M22-2", Name = "Kathmandu Hub", CountryCode = "NP", Timezone = "Asia/Kathmandu", DefaultCurrencyCode = "NPR", IsActive = true });

        db.Clients.AddRange(
            new Client { Id = clientA, CompanyName = "Client Alpha", AccountCode = "CL-ALPHA", NormalizedAccountCode = "CL-ALPHA", Status = ClientStatus.Active, Currency = "USD" },
            new Client { Id = clientB, CompanyName = "Client Beta", AccountCode = "CL-BETA", NormalizedAccountCode = "CL-BETA", Status = ClientStatus.Active, Currency = "USD" });

        db.ClientFacilityAccesses.AddRange(
            new ClientFacilityAccess { ClientId = clientA, FacilityId = facA, Status = FacilityAccessStatus.Active, PrimaryOperatingFacility = true },
            new ClientFacilityAccess { ClientId = clientB, FacilityId = facB, Status = FacilityAccessStatus.Active, PrimaryOperatingFacility = true });

        var role = new Role { RoleName = SystemRoles.SuperAdmin };
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        db.Users.Add(new User
        {
            Id = adminId,
            Email = "superadmin@mdm22.test",
            PasswordHash = "hash",
            FirstName = "Super",
            LastName = "Admin",
            UserType = UserType.Staff,
            Status = UserStatus.Active,
            AssignedFacilityId = facA
        });
        db.UserRoles.Add(new UserRole { UserId = adminId, RoleId = role.Id });
        await db.SaveChangesAsync();

        return (db, admin, facA, facB, clientA, clientB);
    }

    [Fact]
    public async Task Client_CreateAndUpdate_EnforcesPermanentNormalizedCodeUniqueness_Immutability_Currency_AndAddressValidation()
    {
        var (db, admin, _, _, _, _) = await CreateSeededContextAsync();
        var auth = new PartnerMasterAuthorization(db, admin);
        var create = new CreateClientCommandHandler(db, auth);
        var update = new UpdateClientCommandHandler(db, admin, auth);
        var deactivate = new DeactivateClientCommandHandler(db, admin, auth);

        // 1. Valid client with structured billing address & classification
        var created = await create.Handle(new CreateClientCommand
        {
            CompanyName = "Apex Distribution",
            AccountCode = "apex-01",
            Currency = "usd",
            BusinessClassification = "Enterprise",
            TaxRegistrationNumber = "EIN-99-1234567",
            BillingAddressLine1 = "100 Wacker Dr",
            BillingCity = "Chicago",
            BillingStateOrProvince = "IL",
            BillingPostalCode = "60606",
            BillingCountryCode = "US"
        }, default);

        Assert.Equal("APEX-01", created.AccountCode);
        Assert.Equal("USD", created.Currency);
        Assert.Equal("Enterprise", created.BusinessClassification);
        Assert.Equal("US", created.BillingCountryCode);
        Assert.Equal(CountryNormalizationStatus.Normalized, created.CountryNormalizationStatus);

        // 2. Reject invalid CurrencyMaster
        await Assert.ThrowsAsync<ValidationException>(() => create.Handle(new CreateClientCommand
        {
            CompanyName = "Bad Currency",
            AccountCode = "BAD-CUR",
            Currency = "ZZZ"
        }, default));

        // 3. Reject invalid postal code for US
        await Assert.ThrowsAsync<ValidationException>(() => create.Handle(new CreateClientCommand
        {
            CompanyName = "Bad Postal",
            AccountCode = "BAD-ZIP",
            Currency = "USD",
            BillingCountryCode = "US",
            BillingStateOrProvince = "IL",
            BillingPostalCode = "INVALID-ZIP"
        }, default));

        // 4. Reject AccountCode mutation on update
        var immutEx = await Assert.ThrowsAsync<DomainException>(() => update.Handle(new UpdateClientCommand
        {
            Id = created.Id,
            CompanyName = "Apex Distribution Updated",
            AccountCode = "APEX-RENAMED",
            Status = ClientStatus.Active,
            SlaTier = SlaTier.Standard,
            Currency = "USD"
        }, default));
        Assert.Equal("CLIENT_CODE_IMMUTABLE", immutEx.Code);

        // 5. Soft-delete client and verify normalized code CANNOT be reused
        await deactivate.Handle(new DeactivateClientCommand(created.Id), default);
        await Assert.ThrowsAsync<ValidationException>(() => create.Handle(new CreateClientCommand
        {
            CompanyName = "Apex Reincarnated",
            AccountCode = "Apex-01",
            Currency = "USD"
        }, default));
    }

    [Fact]
    public async Task Client_DeactivationAndTermination_BlockedByStock_OpenOrders_ActiveFacilityAccess_ThenSucceedsWhenCleared()
    {
        var (db, admin, facA, _, clientA, _) = await CreateSeededContextAsync();
        var auth = new PartnerMasterAuthorization(db, admin);
        var transition = new TransitionClientStatusCommandHandler(db, auth);
        var accessStatus = new UpdateClientFacilityAccessStatusCommandHandler(db, auth);

        // Seed quarantined stock in bucket (QtyQuarantined > 0)
        var item = new Item { Id = Guid.NewGuid(), ClientId = clientA, Sku = "SKU-M22-1", Description = "Widget" };
        var loc = new Location { Id = Guid.NewGuid(), FacilityId = facA, LocationCode = "A-01-01", NormalizedLocationCode = "A-01-01", LocationName = "Bin 1", IsActive = true };
        var stock = new StockOnHand { Id = Guid.NewGuid(), ClientId = clientA, FacilityId = facA, ItemId = item.Id, LocationId = loc.Id, QtyQuarantined = 5m };
        db.Items.Add(item);
        db.Locations.Add(loc);
        db.StockOnHand.Add(stock);
        await db.SaveChangesAsync();

        // Blocked by quarantined stock
        var stockEx = await Assert.ThrowsAsync<DomainException>(() =>
            transition.Handle(new TransitionClientStatusCommand(clientA, ClientStatus.Suspended), default));
        Assert.Equal("CLIENT_DEACTIVATION_BLOCKED_STOCK", stockEx.Code);

        // Facility access revocation also blocked by stock in that facility
        var accessStockEx = await Assert.ThrowsAsync<DomainException>(() =>
            accessStatus.Handle(new UpdateClientFacilityAccessStatusCommand
            {
                ClientId = clientA,
                FacilityId = facA,
                Status = FacilityAccessStatus.Revoked
            }, default));
        Assert.Equal("CLIENT_ACCESS_BLOCKED_STOCK", accessStockEx.Code);

        // Clear stock -> client deactivation still blocked by active facility access
        stock.QtyQuarantined = 0m;
        await db.SaveChangesAsync();

        var facAccessEx = await Assert.ThrowsAsync<DomainException>(() =>
            transition.Handle(new TransitionClientStatusCommand(clientA, ClientStatus.Terminated), default));
        Assert.Equal("CLIENT_DEACTIVATION_BLOCKED_FACILITY_ACCESS", facAccessEx.Code);

        // Revoke facility access -> now client termination succeeds
        var revoked = await accessStatus.Handle(new UpdateClientFacilityAccessStatusCommand
        {
            ClientId = clientA,
            FacilityId = facA,
            Status = FacilityAccessStatus.Revoked,
            Remarks = "Offboarding"
        }, default);
        Assert.Equal(FacilityAccessStatus.Revoked, revoked.Status);
        Assert.False(revoked.PrimaryOperatingFacility);
        Assert.NotNull(revoked.RevokedAtUtc);

        var terminated = await transition.Handle(new TransitionClientStatusCommand(clientA, ClientStatus.Terminated), default);
        Assert.Equal(ClientStatus.Terminated, terminated.Status);

        // Terminated client cannot be reactivated
        var termEx = await Assert.ThrowsAsync<DomainException>(() =>
            transition.Handle(new TransitionClientStatusCommand(clientA, ClientStatus.Active), default));
        Assert.Equal("CLIENT_TERMINATED_IMMUTABLE", termEx.Code);
    }

    [Fact]
    public async Task ClientContacts_FacilityAccessPrimary_AndOperationalPreferences_WorkEndToEnd()
    {
        var (db, admin, facA, facB, clientA, _) = await CreateSeededContextAsync();
        var auth = new PartnerMasterAuthorization(db, admin);

        // 1. ClientContacts post-create CRUD
        var createContact = new CreateClientContactCommandHandler(db, auth);
        var updateContact = new UpdateClientContactCommandHandler(db, auth);
        var listContacts = new GetClientContactsQueryHandler(db, auth);
        var deleteContact = new DeleteClientContactCommandHandler(db, auth);

        var c1 = await createContact.Handle(new CreateClientContactCommand
        {
            ClientId = clientA,
            FirstName = "Maya",
            LastName = "Sharma",
            RoleTitle = "Operations Director",
            Email = "maya@alpha.test",
            IsPrimary = true,
            IsBillingContact = true
        }, default);

        var updatedC1 = await updateContact.Handle(new UpdateClientContactCommand
        {
            ClientId = clientA,
            ContactId = c1.Id,
            FirstName = "Maya",
            LastName = "Sharma-Patel",
            RoleTitle = "VP Ops",
            Email = "maya@alpha.test",
            IsPrimary = true,
            IsBillingContact = true,
            IsActive = true,
            ExpectedConfigRevision = c1.ConfigRevision
        }, default);
        Assert.Equal(2, updatedC1.ConfigRevision);

        var contacts = await listContacts.Handle(new GetClientContactsQuery(clientA), default);
        Assert.Single(contacts);

        await deleteContact.Handle(new DeleteClientContactCommand(clientA, c1.Id), default);
        Assert.Empty(await listContacts.Handle(new GetClientContactsQuery(clientA), default));

        // 2. Grant second facility as Primary -> atomically clears previous primary
        var grantAccess = new GrantClientFacilityAccessCommandHandler(db, auth);
        var facBAccess = await grantAccess.Handle(new GrantClientFacilityAccessCommand
        {
            ClientId = clientA,
            FacilityId = facB,
            PrimaryOperatingFacility = true
        }, default);
        Assert.True(facBAccess.PrimaryOperatingFacility);

        var listAccess = new GetClientFacilityAccessesQueryHandler(db, auth);
        var allAccess = await listAccess.Handle(new GetClientFacilityAccessesQuery(clientA), default);
        Assert.Equal(2, allAccess.Count);
        Assert.Single(allAccess, a => a.PrimaryOperatingFacility);
        Assert.Equal(facB, allAccess.Single(a => a.PrimaryOperatingFacility).FacilityId);

        // Duplicate active grant rejected
        await Assert.ThrowsAsync<DomainException>(() => grantAccess.Handle(new GrantClientFacilityAccessCommand
        {
            ClientId = clientA,
            FacilityId = facB
        }, default));

        // 3. ClientOperationalPreferences: ClientDefault + FacilityOverride + effective resolution
        var zoneA = new WarehouseZone { Id = Guid.NewGuid(), FacilityId = facA, ZoneCode = "RCV-A", NormalizedZoneCode = "RCV-A", Name = "Receiving A", ZoneType = WarehouseZoneType.Receiving, IsActive = true };
        db.WarehouseZones.Add(zoneA);
        await db.SaveChangesAsync();

        var upsertPref = new UpsertClientOperationalPreferenceCommandHandler(db, auth);
        var getEffective = new GetEffectiveClientOperationalPreferenceQueryHandler(db, auth);

        // Setting a zone on ClientDefault (FacilityId = null) must fail
        await Assert.ThrowsAsync<ValidationException>(() => upsertPref.Handle(new UpsertClientOperationalPreferenceCommand
        {
            ClientId = clientA,
            FacilityId = null,
            DefaultReceivingZoneId = zoneA.Id
        }, default));

        var defaultPref = await upsertPref.Handle(new UpsertClientOperationalPreferenceCommand
        {
            ClientId = clientA,
            FacilityId = null,
            RequireAsnBeforeReceiving = true,
            AllowPartialShipments = true,
            DefaultOrderPriority = 2
        }, default);
        Assert.Equal("ClientDefault", defaultPref.Scope);

        // Effective for facB falls back to ClientDefault
        var effB = await getEffective.Handle(new GetEffectiveClientOperationalPreferenceQuery(clientA, facB), default);
        Assert.NotNull(effB);
        Assert.Equal("ClientDefault", effB!.Scope);
        Assert.Equal(2, effB.DefaultOrderPriority);

        // Add FacilityOverride for facA with DefaultReceivingZoneId
        var facAPref = await upsertPref.Handle(new UpsertClientOperationalPreferenceCommand
        {
            ClientId = clientA,
            FacilityId = facA,
            DefaultReceivingZoneId = zoneA.Id,
            RequireAsnBeforeReceiving = false,
            AllowPartialShipments = false,
            DefaultOrderPriority = 5
        }, default);
        Assert.Equal("FacilityOverride", facAPref.Scope);

        var effA = await getEffective.Handle(new GetEffectiveClientOperationalPreferenceQuery(clientA, facA), default);
        Assert.NotNull(effA);
        Assert.Equal("FacilityOverride", effA!.Scope);
        Assert.Equal(zoneA.Id, effA.DefaultReceivingZoneId);
        Assert.Equal(5, effA.DefaultOrderPriority);
    }

    [Fact]
    public async Task Customer_AndSupplier_Lifecycle_PreservesHistoricalSnapshots_AndBlocksNewInactiveOperations()
    {
        var (db, admin, facA, _, clientA, clientB) = await CreateSeededContextAsync();
        var auth = new PartnerMasterAuthorization(db, admin);
        var tenantProvider = new TestTenantProvider { TenantId = clientA, BypassTenantFilter = true };

        var item = new Item { Id = Guid.NewGuid(), ClientId = clientA, Sku = "SKU-OPS-1", Description = "Ops Item" };
        db.Items.Add(item);
        await db.SaveChangesAsync();

        // 1. Supplier case-insensitive uniqueness fix + cross-tenant same code allowed
        var createSup = new CreateSupplierCommandHandler(db, admin, auth);
        var setSupStatus = new SetSupplierStatusCommandHandler(db, auth);

        var supA = await createSup.Handle(new CreateSupplierCommand
        {
            ClientId = clientA,
            SupplierCode = "vnd-001",
            SupplierName = "Global Vendor",
            Country = "US",
            State = "IL",
            PostalCode = "60601",
            Contacts = [new CreateSupplierContactInput { FirstName = "Sam", LastName = "Lee", Email = "sam@vendor.test", IsPrimary = true }]
        }, default);
        Assert.Equal("VND-001", supA.SupplierCode);
        Assert.Single(supA.Contacts);

        // Same client different case -> BLOCKED
        var dupSupEx = await Assert.ThrowsAsync<DomainException>(() => createSup.Handle(new CreateSupplierCommand
        {
            ClientId = clientA,
            SupplierCode = "VND-001",
            SupplierName = "Dup Vendor"
        }, default));
        Assert.Equal("SUPPLIER_CODE_DUPLICATE", dupSupEx.Code);

        // Different client same code -> ALLOWED
        var supB = await createSup.Handle(new CreateSupplierCommand
        {
            ClientId = clientB,
            SupplierCode = "vnd-001",
            SupplierName = "Beta Vendor"
        }, default);
        Assert.Equal(clientB, supB.ClientId);

        // Create historical PO against active supplier
        var createPo = new CreatePurchaseOrderCommandHandler(db, admin);
        var po = await createPo.Handle(new CreatePurchaseOrderCommand
        {
            ClientId = clientA,
            FacilityId = facA,
            SupplierId = supA.Id,
            PoNumber = "PO-M22-001",
            Currency = "USD",
            Lines = [new CreatePoLineItemDto { LineNumber = 1, ItemId = item.Id, ExpectedQty = 10, UnitPrice = 5 }]
        }, default);
        Assert.Equal(supA.Id, po.SupplierId);

        // Deactivate supplier -> historical PO preserved, new PO & ASN blocked
        await setSupStatus.Handle(new SetSupplierStatusCommand(supA.Id, false, clientA), default);
        Assert.True(await db.PurchaseOrders.AnyAsync(p => p.Id == po.Id && p.SupplierId == supA.Id));

        var poBlockedEx = await Assert.ThrowsAsync<DomainException>(() => createPo.Handle(new CreatePurchaseOrderCommand
        {
            ClientId = clientA,
            FacilityId = facA,
            SupplierId = supA.Id,
            PoNumber = "PO-M22-002",
            Currency = "USD",
            Lines = [new CreatePoLineItemDto { LineNumber = 1, ItemId = item.Id, ExpectedQty = 5, UnitPrice = 5 }]
        }, default));
        Assert.Equal("SUPPLIER_INACTIVE", poBlockedEx.Code);

        var createAsn = new CreateAsnCommandHandler(db, admin);
        var asnBlockedEx = await Assert.ThrowsAsync<DomainException>(() => createAsn.Handle(new CreateAsnCommand
        {
            ClientId = clientA,
            FacilityId = facA,
            SupplierId = supA.Id,
            AsnNumber = "ASN-M22-001",
            Lines = [new CreateAsnLineItemDto { ItemId = item.Id, ExpectedQty = 5 }]
        }, default));
        Assert.Equal("SUPPLIER_INACTIVE", asnBlockedEx.Code);

        // 2. Customer lifecycle + historical OutboundOrder snapshot preservation + inactive block
        var createCust = new CreateCustomerCommandHandler(db, auth);
        var updateCust = new UpdateCustomerCommandHandler(db, auth);
        var setCustStatus = new SetCustomerStatusCommandHandler(db, auth);

        var cust = await createCust.Handle(new CreateCustomerCommand
        {
            ClientId = clientA,
            CustomerCode = "cust-ret-01",
            CustomerName = "Original Retail Co",
            DefaultAddressLine1 = "100 Main St",
            DefaultCity = "Chicago",
            DefaultStateOrProvince = "IL",
            DefaultPostalCode = "60601",
            DefaultCountryCode = "US",
            BillingAddressLine1 = "200 Billing Ave",
            BillingCity = "Chicago",
            BillingStateOrProvince = "IL",
            BillingPostalCode = "60602",
            BillingCountryCode = "US"
        }, default);

        var createOrder = new CreateOutboundOrderCommandHandler(db, tenantProvider, admin);
        var order = await createOrder.Handle(new CreateOutboundOrderCommand
        {
            FacilityId = facA,
            CustomerId = cust.Id,
            OrderNumber = "SO-M22-001",
            ShippingAddress = new CreateShippingAddressDto
            {
                RecipientName = "Original Retail Co",
                AddressLine1 = "100 Main St",
                City = "Chicago",
                StateOrProvince = "IL",
                PostalCode = "60601",
                CountryCode = "US"
            },
            Lines = [new CreateOutboundOrderLineDto { ItemId = item.Id, QuantityOrdered = 2 }]
        }, default);

        // Update customer address -> historical order snapshot MUST remain unchanged
        await updateCust.Handle(new UpdateCustomerCommand
        {
            Id = cust.Id,
            ClientId = clientA,
            CustomerName = "Renamed Retail Co",
            DefaultAddressLine1 = "999 New Blvd",
            DefaultCity = "Austin",
            DefaultStateOrProvince = "TX",
            DefaultPostalCode = "73301",
            DefaultCountryCode = "US"
        }, default);

        var savedOrder = await db.OutboundOrders.Include(o => o.ShippingAddress).FirstAsync(o => o.Id == order.Id);
        Assert.Equal("100 Main St", savedOrder.ShippingAddress!.AddressLine1);
        Assert.Equal("Chicago", savedOrder.ShippingAddress.City);

        // Deactivate customer -> new order referencing inactive customer MUST fail
        await setCustStatus.Handle(new SetCustomerStatusCommand(cust.Id, false, clientA), default);
        var ordBlockedEx = await Assert.ThrowsAsync<DomainException>(() => createOrder.Handle(new CreateOutboundOrderCommand
        {
            FacilityId = facA,
            CustomerId = cust.Id,
            OrderNumber = "SO-M22-002",
            ShippingAddress = new CreateShippingAddressDto
            {
                RecipientName = "Renamed Retail Co",
                AddressLine1 = "999 New Blvd",
                City = "Austin",
                StateOrProvince = "TX",
                PostalCode = "73301",
                CountryCode = "US"
            },
            Lines = [new CreateOutboundOrderLineDto { ItemId = item.Id, QuantityOrdered = 1 }]
        }, default));
        Assert.Equal("CUSTOMER_INACTIVE", ordBlockedEx.Code);

        // Verify MDM-1.5 ConfigurationScopeEpoch & ConfigurationInvalidationEvent recorded for Client scope
        var clientScopeKey = clientA.ToString("D").ToUpperInvariant();
        var clientEpoch = await db.ConfigurationScopeEpochs
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.ScopeType == ConfigurationScopeType.Client && e.ScopeKey == clientScopeKey);
        Assert.NotNull(clientEpoch);
        Assert.True(clientEpoch!.CurrentEpoch >= 1);
    }

    [Fact]
    public async Task HttpSecurity_Enforces401_Driver403_TenantIsolation_HeaderConflict400_AndDbAuthoritativeStaffFacilityScope()
    {
        var dbName = $"mdm22_http_{Guid.NewGuid():N}";
        var facA = Guid.NewGuid();
        var facB = Guid.NewGuid();
        var clientA = Guid.NewGuid();
        var clientB = Guid.NewGuid();

        var superAdminId = Guid.NewGuid();
        var staffFacAId = Guid.NewGuid();
        var clientAUserId = Guid.NewGuid();
        var driverUserId = Guid.NewGuid();
        var forgedAdminId = Guid.NewGuid();

        // Build real ASP.NET Core TestServer with JWT auth + ExceptionHandlingMiddleware
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
        builder.Services.AddScoped<ITenantProvider, TenantProvider>();
        builder.Services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        builder.Services.AddScoped<AuditableEntityInterceptor>();
        builder.Services.AddDbContext<ApplicationDbContext>(opts => opts.UseInMemoryDatabase(dbName));
        builder.Services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        builder.Services.AddApplicationServices();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = JwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = JwtAudience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)),
                    ValidateLifetime = true
                };
            });
        builder.Services.AddAuthorization(options =>
        {
            foreach (var perm in Permissions.GetAllPermissions())
            {
                options.AddPolicy(perm, policy => policy.RequireAssertion(ctx =>
                    ctx.User.IsInRole(SystemRoles.SuperAdmin) ||
                    ctx.User.Claims.Any(c => (c.Type == "permissions" || c.Type == "permission") && c.Value == perm)));
            }
        });
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(ClientsController).Assembly);

        await using var app = builder.Build();
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();

        // Seed DB
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await ReferenceCatalogSeeder.SeedAsync(db);

            db.Facilities.AddRange(
                new Facility { Id = facA, FacilityCode = "FAC-H1", Name = "Hub 1", CountryCode = "US", Timezone = "America/Chicago", DefaultCurrencyCode = "USD", IsActive = true },
                new Facility { Id = facB, FacilityCode = "FAC-H2", Name = "Hub 2", CountryCode = "NP", Timezone = "Asia/Kathmandu", DefaultCurrencyCode = "NPR", IsActive = true });

            db.Clients.AddRange(
                new Client { Id = clientA, CompanyName = "Alpha Co", AccountCode = "ALPHA-H", NormalizedAccountCode = "ALPHA-H", Status = ClientStatus.Active, Currency = "USD" },
                new Client { Id = clientB, CompanyName = "Beta Co", AccountCode = "BETA-H", NormalizedAccountCode = "BETA-H", Status = ClientStatus.Active, Currency = "USD" });

            db.ClientFacilityAccesses.AddRange(
                new ClientFacilityAccess { ClientId = clientA, FacilityId = facA, Status = FacilityAccessStatus.Active, PrimaryOperatingFacility = true },
                new ClientFacilityAccess { ClientId = clientB, FacilityId = facB, Status = FacilityAccessStatus.Active, PrimaryOperatingFacility = true });

            var superRole = new Role { RoleName = SystemRoles.SuperAdmin };
            var staffRole = new Role { RoleName = "FacilityStaff" };
            var clientRole = new Role { RoleName = "ClientUser" };
            var driverRole = new Role { RoleName = "Driver" };
            db.Roles.AddRange(superRole, staffRole, clientRole, driverRole);
            await db.SaveChangesAsync();

            db.RolePermissions.AddRange(
                new RolePermission { RoleId = staffRole.Id, PermissionKey = Permissions.Clients.View },
                new RolePermission { RoleId = staffRole.Id, PermissionKey = Permissions.Customers.View },
                new RolePermission { RoleId = staffRole.Id, PermissionKey = Permissions.Inbound.SuppliersView });

            db.Users.AddRange(
                new User { Id = superAdminId, Email = "sa@h.test", PasswordHash = "h", FirstName = "SA", LastName = "User", UserType = UserType.Staff, Status = UserStatus.Active, AssignedFacilityId = facA },
                new User { Id = staffFacAId, Email = "staffA@h.test", PasswordHash = "h", FirstName = "Staff", LastName = "A", UserType = UserType.Staff, Status = UserStatus.Active, AssignedFacilityId = facA },
                new User { Id = clientAUserId, Email = "clientA@h.test", PasswordHash = "h", FirstName = "Client", LastName = "A", UserType = UserType.Client, Status = UserStatus.Active, ClientId = clientA },
                new User { Id = driverUserId, Email = "driver@h.test", PasswordHash = "h", FirstName = "Driver", LastName = "D", UserType = UserType.Driver, Status = UserStatus.Active },
                new User { Id = forgedAdminId, Email = "forged@h.test", PasswordHash = "h", FirstName = "Forged", LastName = "F", UserType = UserType.Staff, Status = UserStatus.Active, AssignedFacilityId = facA });

            db.UserRoles.AddRange(
                new UserRole { UserId = superAdminId, RoleId = superRole.Id },
                new UserRole { UserId = staffFacAId, RoleId = staffRole.Id },
                new UserRole { UserId = clientAUserId, RoleId = clientRole.Id },
                new UserRole { UserId = driverUserId, RoleId = driverRole.Id },
                new UserRole { UserId = forgedAdminId, RoleId = staffRole.Id }); // NOT SuperAdmin in DB!

            await db.SaveChangesAsync();
        }

        var http = app.GetTestClient();

        // 1. Unauthenticated -> 401
        var unauthResp = await http.GetAsync("/api/v1/clients");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthResp.StatusCode);

        // 2. Driver -> 403 on Clients, Customers, Suppliers
        var driverToken = IssueJwt(driverUserId, UserType.Driver, null, null, ["Driver"], []);
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/clients"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", driverToken);
            var resp = await http.SendAsync(req);
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        }
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/customers"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", driverToken);
            var resp = await http.SendAsync(req);
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        }

        // 3. Client A user -> 200 on own client, 403 on Client B (no cross-client enumeration), 400 on conflicting X-Tenant-Id
        var clientAToken = IssueJwt(clientAUserId, UserType.Client, clientA, null, ["ClientUser"], []);
        using (var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/clients/{clientA}"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", clientAToken);
            var resp = await http.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }
        using (var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/clients/{clientB}"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", clientAToken);
            var resp = await http.SendAsync(req);
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        }
        using (var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/customers?clientId={clientA}"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", clientAToken);
            req.Headers.Add("X-Tenant-Id", clientB.ToString());
            var resp = await http.SendAsync(req);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        // 4. Facility-A Staff -> 200 for Client A (assigned to Facility A), 403 for Client B (only in Facility B)
        var staffAToken = IssueJwt(staffFacAId, UserType.Staff, null, facA, ["FacilityStaff"], [Permissions.Clients.View, Permissions.Customers.View]);
        using (var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/clients/{clientA}"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffAToken);
            var resp = await http.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }
        using (var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/clients/{clientB}"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffAToken);
            var resp = await http.SendAsync(req);
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        }

        // 5. Forged SuperAdmin claim (not in DB) -> 403
        var forgedToken = IssueJwt(forgedAdminId, UserType.Staff, null, facA, [SystemRoles.SuperAdmin], Permissions.GetAllPermissions());
        using (var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/clients/{clientB}"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", forgedToken);
            var resp = await http.SendAsync(req);
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        }

        // 6. Verified SuperAdmin -> 200 across both clients
        var saToken = IssueJwt(superAdminId, UserType.Staff, null, facA, [SystemRoles.SuperAdmin], Permissions.GetAllPermissions());
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/clients"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", saToken);
            var resp = await http.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var page = await resp.Content.ReadFromJsonAsync<PagedResult<ClientDto>>(JsonOptions);
            Assert.NotNull(page);
            Assert.Equal(2, page!.TotalCount);
        }
    }

    private static string IssueJwt(
        Guid userId,
        UserType userType,
        Guid? tenantId,
        Guid? facilityId,
        IEnumerable<string> roles,
        IEnumerable<string> permissions)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new("sub", userId.ToString()),
            new("user_type", userType.ToString())
        };
        if (tenantId.HasValue) claims.Add(new Claim("tenant_id", tenantId.Value.ToString()));
        if (facilityId.HasValue) claims.Add(new Claim("facility_id", facilityId.Value.ToString()));
        foreach (var r in roles) claims.Add(new Claim(ClaimTypes.Role, r));
        foreach (var p in permissions) claims.Add(new Claim("permissions", p));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: JwtIssuer,
            audience: JwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
