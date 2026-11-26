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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Warehouse3PL.Application;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Reference;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Authentication;
using Warehouse3PL.Infrastructure.Authorization;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;
using Warehouse3PL.Infrastructure.Persistence.Seed;
using Warehouse3PL.Infrastructure.Services;
using Warehouse3PL.Infrastructure.Services.Reference;
using Warehouse3PL.WebApi.Controllers;
using Warehouse3PL.WebApi.Middleware;
using Xunit;

namespace Warehouse3PL.UnitTests.Reference;

public sealed class Mdm16LegacyCountryNormalizationTests
{
    private const string TestSecret = "Nexus3PL-Mdm16-CountryNormalization-JwtSecret-2026!";
    private const string TestIssuer = "nexus3pl-mdm16-auth";
    private const string TestAudience = "nexus3pl-mdm16-api";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    [Fact]
    public async Task ResolveCountry_Handles_CanonicalAliases_Whitespace_MultiCountryExtensibility_And_ZeroFuzzyFallback()
    {
        await using var host = await BuildJwtAppAsync(async db =>
        {
            await ReferenceCatalogSeeder.SeedAsync(db);
            // Idempotent re-seed check
            await ReferenceCatalogSeeder.SeedAsync(db);

            // Extensible multi-country addition (CA / Canada) without hardcoding US/NP
            db.CountryProfiles.Add(CountryProfile.Create(
                countryCode: "CA",
                isoAlpha3Code: "CAN",
                numericCode: "124",
                name: "Canada",
                defaultCurrencyCode: "CAD",
                primaryTimeZoneId: "America/Toronto",
                addressSchemaType: AddressSchemaType.UsStateZip,
                postalCodeRegex: @"^[A-Z]\d[A-Z][ -]?\d[A-Z]\d$",
                legacyPostalCodeRegex: null,
                primaryCalendarSystem: PrimaryCalendarSystem.Gregorian,
                fiscalCalendarSystem: FiscalCalendarSystem.Gregorian,
                defaultFiscalYearStartMonth: 4,
                seedVersion: 1));
            db.CountryNormalizationAliases.Add(
                CountryNormalizationAlias.Create("DOMINION OF CANADA", "CA", "ISO-CUSTOM"));
            await db.SaveChangesAsync();
        });

        using var scope = host.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ILegacyCountryNormalizationService>();

        // 1. Canonical US variants + dotted acronyms + case/whitespace tolerance
        foreach (var rawUs in new[] { "US", "usa", "  USA  ", "U.S.A.", " u.s.a. ", "United States", "  united   states  of america " })
        {
            var res = await svc.ResolveCountryAsync(rawUs);
            Assert.True(res.IsResolved, $"Expected '{rawUs}' to resolve.");
            Assert.Equal("US", res.CanonicalCountryCode);
            Assert.Equal(CountryNormalizationStatus.Normalized, res.Status);
            Assert.False(res.WasInferredFromParent);
        }

        // 2. Canonical NP variants
        foreach (var rawNp in new[] { "NP", "NPL", " npl ", "Nepal", "  NEPAL  " })
        {
            var res = await svc.ResolveCountryAsync(rawNp);
            Assert.True(res.IsResolved, $"Expected '{rawNp}' to resolve.");
            Assert.Equal("NP", res.CanonicalCountryCode);
            Assert.Equal(CountryNormalizationStatus.Normalized, res.Status);
        }

        // 3. Extensible 3rd active country without code changes
        foreach (var rawCa in new[] { "CA", "CAN", "Canada", "  dominion of canada " })
        {
            var res = await svc.ResolveCountryAsync(rawCa);
            Assert.True(res.IsResolved, $"Expected extensible CA variant '{rawCa}' to resolve.");
            Assert.Equal("CA", res.CanonicalCountryCode);
        }

        // 4. Reject unknown, misspelled/fuzzy, and blank inputs (zero default US/NP fallback)
        foreach (var bad in new string?[] { null, "", "   ", "ZZZ", "United Statess", "Nepall", "UK", "XX" })
        {
            var res = await svc.ResolveCountryAsync(bad);
            Assert.False(res.IsResolved, $"Expected '{bad ?? "NULL"}' to be quarantined.");
            Assert.Null(res.CanonicalCountryCode);
            Assert.Equal(CountryNormalizationStatus.PendingNormalization, res.Status);
        }
    }

    [Fact]
    public async Task Backfill_DeterministicInference_Quarantine_HistoricalImmutability_Idempotency_And_RbacEndpoint()
    {
        var facUsId = Guid.NewGuid();
        var facNpId = Guid.NewGuid();
        var facOrphanId = Guid.NewGuid();
        var facConflictId = Guid.NewGuid();

        var clientNpId = Guid.NewGuid();
        var clientUsId = Guid.NewGuid();
        var clientMultiId = Guid.NewGuid();

        var custInferNpId = Guid.NewGuid();
        var custUnknownId = Guid.NewGuid();
        var supInferUsId = Guid.NewGuid();
        var supOrphanBlankId = Guid.NewGuid();

        var draftOrderUsId = Guid.NewGuid();
        var completedOrderNpId = Guid.NewGuid();
        var crossBorderOrderId = Guid.NewGuid();

        var draftAddrId = Guid.NewGuid();
        var completedAddrId = Guid.NewGuid();
        var crossBorderAddrId = Guid.NewGuid();

        var authReturnId = Guid.NewGuid();
        var closedReturnId = Guid.NewGuid();

        var activeSlaId = Guid.NewGuid();
        var inactiveSlaId = Guid.NewGuid();
        var slaRecordId = Guid.NewGuid();

        var staffPlatformId = Guid.NewGuid();
        var staffFacNpId = Guid.NewGuid();
        var staffUnprivId = Guid.NewGuid();
        var clientUserId = Guid.NewGuid();
        var driverUserId = Guid.NewGuid();

        await using var host = await BuildJwtAppAsync(async db =>
        {
            await ReferenceCatalogSeeder.SeedAsync(db);

            var facUs = new Facility
            {
                Id = facUsId,
                FacilityCode = "FAC-US-M16",
                Name = "Chicago Legacy US Facility",
                CountryCode = "U.S.A.",
                StateOrProvince = "Illinois",
                PostalCode = "60601",
                Timezone = "America/Chicago",
                IsActive = true
            };
            var facNp = new Facility
            {
                Id = facNpId,
                FacilityCode = "FAC-NP-M16",
                Name = "Kathmandu Legacy NP Facility",
                CountryCode = "Nepal",
                StateOrProvince = "Bagmati",
                PostalCode = "44600",
                Timezone = "Asia/Kathmandu",
                IsActive = true
            };
            var facOrphan = new Facility
            {
                Id = facOrphanId,
                FacilityCode = "FAC-ORPHAN",
                Name = "Orphan Blank Geo Facility",
                CountryCode = "",
                StateOrProvince = "",
                PostalCode = "",
                Timezone = "UTC",
                IsActive = true
            };
            var facConflict = new Facility
            {
                Id = facConflictId,
                FacilityCode = "FAC-CONFLICT",
                Name = "Geo Conflict Facility",
                CountryCode = "US",
                StateOrProvince = "Bagmati",
                PostalCode = "44600",
                Timezone = "Asia/Kathmandu",
                IsActive = true
            };
            db.Facilities.AddRange(facUs, facNp, facOrphan, facConflict);

            db.Clients.AddRange(
                new Client { Id = clientNpId, AccountCode = "CL-NP16", CompanyName = "Nepal Domestic Co", Status = ClientStatus.Active },
                new Client { Id = clientUsId, AccountCode = "CL-US16", CompanyName = "US Domestic Co", Status = ClientStatus.Active },
                new Client { Id = clientMultiId, AccountCode = "CL-MULTI16", CompanyName = "Cross-Border Co", Status = ClientStatus.Active });

            db.ClientFacilityAccesses.AddRange(
                new ClientFacilityAccess { ClientId = clientNpId, FacilityId = facNpId, Status = FacilityAccessStatus.Active },
                new ClientFacilityAccess { ClientId = clientUsId, FacilityId = facUsId, Status = FacilityAccessStatus.Active },
                new ClientFacilityAccess { ClientId = clientMultiId, FacilityId = facNpId, Status = FacilityAccessStatus.Active },
                new ClientFacilityAccess { ClientId = clientMultiId, FacilityId = facUsId, Status = FacilityAccessStatus.Active });

            // Mutable Customer inferred via single Nepal client + Bagmati + 44600
            db.Customers.AddRange(
                new Customer
                {
                    Id = custInferNpId,
                    ClientId = clientNpId,
                    CustomerCode = "CUST-NP-INF",
                    CustomerName = "PII Hidden Customer Name",
                    Email = "secret-cust@example.test",
                    DefaultCountryCode = "",
                    DefaultStateOrProvince = "Bagmati",
                    DefaultPostalCode = "44600"
                },
                new Customer
                {
                    Id = custUnknownId,
                    ClientId = clientUsId,
                    CustomerCode = "CUST-UNK",
                    CustomerName = "PII Unknown Customer",
                    DefaultCountryCode = "ZZZ",
                    DefaultStateOrProvince = "Illinois",
                    DefaultPostalCode = "60601"
                });

            // Mutable Supplier inferred via single US client + IL + 60601 vs ambiguous multi-country client blank supplier
            db.Suppliers.AddRange(
                new Supplier
                {
                    Id = supInferUsId,
                    ClientId = clientUsId,
                    SupplierCode = "SUP-US-INF",
                    SupplierName = "US Supplier",
                    Country = null,
                    State = "Illinois",
                    PostalCode = "60601"
                },
                new Supplier
                {
                    Id = supOrphanBlankId,
                    ClientId = clientMultiId,
                    SupplierCode = "SUP-AMB",
                    SupplierName = "Ambiguous Supplier",
                    Country = "",
                    State = "Illinois",
                    PostalCode = "60601"
                });

            db.OutboundOrders.AddRange(
                new OutboundOrder { Id = draftOrderUsId, ClientId = clientUsId, FacilityId = facUsId, CustomerId = custUnknownId, OrderNumber = "ORD-DRAFT-1", Status = OutboundOrderStatus.Draft, CustomerName = "Hidden Draft Consignee" },
                new OutboundOrder { Id = completedOrderNpId, ClientId = clientNpId, FacilityId = facNpId, CustomerId = custInferNpId, OrderNumber = "ORD-COMP-1", Status = OutboundOrderStatus.Completed, CustomerName = "Hidden Completed Consignee" },
                new OutboundOrder { Id = crossBorderOrderId, ClientId = clientNpId, FacilityId = facUsId, CustomerId = custInferNpId, OrderNumber = "ORD-XBRD-1", Status = OutboundOrderStatus.Draft, CustomerName = "Cross Border" });

            db.ShippingAddresses.AddRange(
                new ShippingAddress
                {
                    Id = draftAddrId,
                    OutboundOrderId = draftOrderUsId,
                    RecipientName = "PII Draft Recipient",
                    AddressLine1 = "123 Secret Street",
                    City = "Chicago",
                    StateOrProvince = "Illinois",
                    PostalCode = "60601",
                    CountryCode = "United States"
                },
                new ShippingAddress
                {
                    Id = completedAddrId,
                    OutboundOrderId = completedOrderNpId,
                    RecipientName = "PII Historical Recipient",
                    AddressLine1 = "456 Durbar Marg",
                    City = "Kathmandu",
                    StateOrProvince = "Bagmati",
                    PostalCode = "44600",
                    CountryCode = "NPL"
                },
                new ShippingAddress
                {
                    Id = crossBorderAddrId,
                    OutboundOrderId = crossBorderOrderId,
                    RecipientName = "PII CrossBorder",
                    AddressLine1 = "789 Border Rd",
                    City = "Chicago",
                    StateOrProvince = "Illinois",
                    PostalCode = "60601",
                    CountryCode = ""
                });

            var dummyOrderPkgId = Guid.NewGuid();
            db.ReturnShipments.AddRange(
                new ReturnShipment
                {
                    Id = authReturnId,
                    ClientId = clientUsId,
                    OriginFacilityId = facUsId,
                    DestinationFacilityId = facUsId,
                    OrderId = draftOrderUsId,
                    PackageId = dummyOrderPkgId,
                    ReturnRmaNumber = "RMA-M16-AUTH",
                    Reason = ReturnReason.DamagedInTransit,
                    AuthorizedByUserId = staffPlatformId,
                    CarrierCode = "UPS",
                    TrackingNumber = "1ZAUTH",
                    Status = ReturnShipmentStatus.Authorized,
                    OriginCountry = "United States of America",
                    OriginState = "Illinois",
                    OriginPostalCode = "60601",
                    DestinationCountry = "",
                    DestinationState = "Illinois",
                    DestinationPostalCode = "60601"
                },
                new ReturnShipment
                {
                    Id = closedReturnId,
                    ClientId = clientNpId,
                    OriginFacilityId = facNpId,
                    DestinationFacilityId = facNpId,
                    OrderId = completedOrderNpId,
                    PackageId = dummyOrderPkgId,
                    ReturnRmaNumber = "RMA-M16-HIST",
                    Reason = ReturnReason.CustomerRefused,
                    AuthorizedByUserId = staffPlatformId,
                    CarrierCode = "NPX",
                    TrackingNumber = "NPXHIST",
                    Status = ReturnShipmentStatus.ReceivedAndInspected,
                    LabelGeneratedAtUtc = DateTime.UtcNow.AddDays(-5),
                    OriginCountry = "Nepal",
                    OriginState = "Bagmati",
                    OriginPostalCode = "44600",
                    DestinationCountry = "NPL",
                    DestinationState = "Bagmati",
                    DestinationPostalCode = "44600"
                });

            db.CarrierContractualSlaPolicies.AddRange(
                new CarrierContractualSlaPolicy
                {
                    Id = activeSlaId,
                    CarrierCode = "UPS",
                    ClientId = clientUsId,
                    OriginFacilityId = facUsId,
                    DestinationCountry = "USA",
                    DestinationStateOrRegion = "Illinois",
                    DestinationPostalCodePrefix = "606",
                    TransitCommitmentDays = 2,
                    IsActive = true
                },
                new CarrierContractualSlaPolicy
                {
                    Id = inactiveSlaId,
                    CarrierCode = "NPX",
                    ClientId = clientNpId,
                    OriginFacilityId = facNpId,
                    DestinationCountry = "Nepal",
                    DestinationStateOrRegion = "Bagmati",
                    DestinationPostalCodePrefix = "446",
                    TransitCommitmentDays = 3,
                    IsActive = false
                });

            db.CarrierSlaComplianceRecords.Add(new CarrierSlaComplianceRecord
            {
                Id = slaRecordId,
                ShipmentId = Guid.NewGuid(),
                TrackingNumber = "TRK-HIST-SLA",
                CarrierCode = "NPX",
                FacilityId = facNpId,
                ClientId = clientNpId,
                DestinationCountry = "NPL",
                DestinationStateOrRegion = "Bagmati",
                DestinationPostalCodePrefix = "44600",
                DispatchedAtUtc = DateTime.UtcNow.AddDays(-10)
            });

            var platformRole = new Role { RoleName = "NormAuditor", Description = "Platform Norm", IsSystemRole = false };
            platformRole.RolePermissions.Add(new RolePermission { Role = platformRole, PermissionKey = Permissions.Global.NormalizationView, AccessLevel = AccessLevel.Read });
            var facMgrRole = new Role { RoleName = "FacMgrM16", Description = "Fac Mgr", IsSystemRole = false };
            facMgrRole.RolePermissions.Add(new RolePermission { Role = facMgrRole, PermissionKey = Permissions.Facilities.Manage, AccessLevel = AccessLevel.Admin });
            var opRole = new Role { RoleName = SystemRoles.WarehouseOperator, Description = "Op", IsSystemRole = true };
            db.Roles.AddRange(platformRole, facMgrRole, opRole);

            var uPlat = new User { Id = staffPlatformId, Email = "plat@m16.test", FirstName = "P", LastName = "A", UserType = UserType.Staff, Status = UserStatus.Active, PasswordHash = "x" };
            var uFacNp = new User { Id = staffFacNpId, Email = "facnp@m16.test", FirstName = "F", LastName = "N", UserType = UserType.Staff, AssignedFacilityId = facNpId, Status = UserStatus.Active, PasswordHash = "x" };
            var uUnpriv = new User { Id = staffUnprivId, Email = "unpriv@m16.test", FirstName = "U", LastName = "P", UserType = UserType.Staff, AssignedFacilityId = facUsId, Status = UserStatus.Active, PasswordHash = "x" };
            var uClient = new User { Id = clientUserId, Email = "client@m16.test", FirstName = "C", LastName = "U", UserType = UserType.Client, ClientId = clientNpId, Status = UserStatus.Active, PasswordHash = "x" };
            var uDriver = new User { Id = driverUserId, Email = "driver@m16.test", FirstName = "D", LastName = "U", UserType = UserType.Driver, Status = UserStatus.Active, PasswordHash = "x" };
            db.Users.AddRange(uPlat, uFacNp, uUnpriv, uClient, uDriver);
            db.UserRoles.AddRange(
                new UserRole { User = uPlat, Role = platformRole },
                new UserRole { User = uFacNp, Role = facMgrRole },
                new UserRole { User = uUnpriv, Role = opRole });

            await db.SaveChangesAsync();
        });

        // Execute BackfillAllAsync run #1
        LegacyCountryBackfillSummary summary1;
        using (var scope = host.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<ILegacyCountryNormalizationService>();
            summary1 = await svc.BackfillAllAsync("BATCH-M16-1");
        }

        Assert.Equal(8, summary1.NormalizedMutatedCount);
        Assert.Equal(5, summary1.PreservedHistoricalCount);
        Assert.Equal(3, summary1.InferredCount);
        Assert.Equal(5, summary1.QuarantinedCount);

        // Execute BackfillAllAsync run #2 -> strictly idempotent (zero mutations, zero duplicate audit rows)
        LegacyCountryBackfillSummary summary2;
        using (var scope = host.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<ILegacyCountryNormalizationService>();
            summary2 = await svc.BackfillAllAsync("BATCH-M16-2");
        }

        Assert.Equal(0, summary2.NormalizedMutatedCount);

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var fUs = await db.Facilities.IgnoreQueryFilters().SingleAsync(f => f.Id == facUsId);
            var fNp = await db.Facilities.IgnoreQueryFilters().SingleAsync(f => f.Id == facNpId);
            var fOrph = await db.Facilities.IgnoreQueryFilters().SingleAsync(f => f.Id == facOrphanId);
            var fConf = await db.Facilities.IgnoreQueryFilters().SingleAsync(f => f.Id == facConflictId);
            Assert.Equal("US", fUs.CountryCode);
            Assert.Equal(CountryNormalizationStatus.Normalized, fUs.CountryNormalizationStatus);
            Assert.Equal("NP", fNp.CountryCode);
            Assert.Equal(CountryNormalizationStatus.Normalized, fNp.CountryNormalizationStatus);
            Assert.Equal("", fOrph.CountryCode);
            Assert.Equal(CountryNormalizationStatus.PendingNormalization, fOrph.CountryNormalizationStatus);
            Assert.Equal("US", fConf.CountryCode);
            Assert.Equal(CountryNormalizationStatus.PendingNormalization, fConf.CountryNormalizationStatus);

            var cInf = await db.Customers.IgnoreQueryFilters().SingleAsync(c => c.Id == custInferNpId);
            var cUnk = await db.Customers.IgnoreQueryFilters().SingleAsync(c => c.Id == custUnknownId);
            Assert.Equal("NP", cInf.DefaultCountryCode);
            Assert.Equal(CountryNormalizationStatus.Normalized, cInf.CountryNormalizationStatus);
            Assert.Equal("ZZZ", cUnk.DefaultCountryCode);
            Assert.Equal(CountryNormalizationStatus.PendingNormalization, cUnk.CountryNormalizationStatus);

            var sInf = await db.Suppliers.IgnoreQueryFilters().SingleAsync(s => s.Id == supInferUsId);
            var sAmb = await db.Suppliers.IgnoreQueryFilters().SingleAsync(s => s.Id == supOrphanBlankId);
            Assert.Equal("US", sInf.Country);
            Assert.Equal(CountryNormalizationStatus.Normalized, sInf.CountryNormalizationStatus);
            Assert.Equal("", sAmb.Country);
            Assert.Equal(CountryNormalizationStatus.PendingNormalization, sAmb.CountryNormalizationStatus);

            // Historical ShippingAddress untouched ("NPL"), Draft mutated ("US"), CrossBorder quarantined ("")
            var aDraft = await db.ShippingAddresses.IgnoreQueryFilters().SingleAsync(a => a.Id == draftAddrId);
            var aComp = await db.ShippingAddresses.IgnoreQueryFilters().SingleAsync(a => a.Id == completedAddrId);
            var aXbrd = await db.ShippingAddresses.IgnoreQueryFilters().SingleAsync(a => a.Id == crossBorderAddrId);
            Assert.Equal("US", aDraft.CountryCode);
            Assert.Equal("NPL", aComp.CountryCode);
            Assert.Equal(CountryNormalizationStatus.Normalized, aComp.CountryNormalizationStatus);
            Assert.Equal("", aXbrd.CountryCode);
            Assert.Equal(CountryNormalizationStatus.PendingNormalization, aXbrd.CountryNormalizationStatus);

            // ReturnShipment: Authorized mutated+inferred, Closed preserved verbatim ("Nepal", "NPL")
            var rAuth = await db.ReturnShipments.IgnoreQueryFilters().SingleAsync(r => r.Id == authReturnId);
            var rHist = await db.ReturnShipments.IgnoreQueryFilters().SingleAsync(r => r.Id == closedReturnId);
            Assert.Equal("US", rAuth.OriginCountry);
            Assert.Equal("US", rAuth.DestinationCountry);
            Assert.Equal("Nepal", rHist.OriginCountry);
            Assert.Equal("NPL", rHist.DestinationCountry);

            // SLA policy & compliance record
            var pAct = await db.CarrierContractualSlaPolicies.IgnoreQueryFilters().SingleAsync(p => p.Id == activeSlaId);
            var pInact = await db.CarrierContractualSlaPolicies.IgnoreQueryFilters().SingleAsync(p => p.Id == inactiveSlaId);
            var rec = await db.CarrierSlaComplianceRecords.IgnoreQueryFilters().SingleAsync(r => r.Id == slaRecordId);
            Assert.Equal("US", pAct.DestinationCountry);
            Assert.Equal("Nepal", pInact.DestinationCountry);
            Assert.Equal("NPL", rec.DestinationCountry);

            // Provenance preserved OriginalCountryValue across batch 1 -> batch 2 (unchanged normalized row stays on BATCH-M16-1)
            var facUsAudit = await db.CountryNormalizationAudits.SingleAsync(a => a.EntityType == nameof(Facility) && a.EntityId == facUsId);
            Assert.Equal("U.S.A.", facUsAudit.OriginalCountryValue);
            Assert.Equal("US", facUsAudit.NormalizedCountryCode);
            Assert.Equal("BATCH-M16-1", facUsAudit.CorrelationBatchId);
            var histRecordAudit = await db.CountryNormalizationAudits.SingleAsync(a => a.EntityType == nameof(CarrierSlaComplianceRecord) && a.EntityId == slaRecordId);
            Assert.True(histRecordAudit.PreservedHistoricalValue);
            Assert.Equal("NPL", histRecordAudit.OriginalCountryValue);
            Assert.Equal("NP", histRecordAudit.NormalizedCountryCode);

            // Zero duplicate audit keys
            var audits = await db.CountryNormalizationAudits.ToListAsync();
            Assert.Equal(audits.Count, audits.Select(a => (a.EntityType, a.EntityId, a.FieldName)).Distinct().Count());
        }

        // HTTP RBAC, Facility Isolation, and Zero-PII checks
        using var http = host.GetTestClient();
        var jwtPlat = TokenFor(host, staffPlatformId, UserType.Staff, null, null, ["NormAuditor"], [Permissions.Global.NormalizationView]);
        var jwtFacNp = TokenFor(host, staffFacNpId, UserType.Staff, null, facNpId, ["FacMgrM16"], [Permissions.Facilities.Manage]);
        var jwtUnprivStaff = TokenFor(host, staffUnprivId, UserType.Staff, null, facUsId, [SystemRoles.WarehouseOperator], [Permissions.Inventory.View]);
        var jwtSpoofSuperAdmin = TokenFor(host, staffUnprivId, UserType.Staff, null, facUsId, [SystemRoles.SuperAdmin], Permissions.GetAllPermissions().ToArray());
        var jwtClientSpoof = TokenFor(host, clientUserId, UserType.Client, clientNpId, null, [], [Permissions.Global.NormalizationView]);
        var jwtDriverSpoof = TokenFor(host, driverUserId, UserType.Driver, null, null, [], [Permissions.Global.NormalizationView]);

        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/v1/reference/normalization-quarantine")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/reference/normalization-quarantine", jwtSpoofSuperAdmin))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/reference/normalization-quarantine", jwtClientSpoof))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/reference/normalization-quarantine", jwtDriverSpoof))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/reference/normalization-quarantine", jwtUnprivStaff))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Authed(HttpMethod.Get, $"/api/v1/reference/normalization-quarantine?facilityId={facUsId}", jwtFacNp))).StatusCode);

        var xFacReq = Authed(HttpMethod.Get, "/api/v1/reference/normalization-quarantine", jwtFacNp);
        xFacReq.Headers.Add("X-Facility-Id", facUsId.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(xFacReq)).StatusCode);

        var facNpResp = await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/reference/normalization-quarantine?includeAllStatuses=true&pageSize=100", jwtFacNp));
        Assert.Equal(HttpStatusCode.OK, facNpResp.StatusCode);
        var facNpPayload = await facNpResp.Content.ReadFromJsonAsync<PagedNormalizationQuarantineResponse>(JsonOptions);
        Assert.NotNull(facNpPayload);
        Assert.NotEmpty(facNpPayload!.Items);
        Assert.DoesNotContain(facNpPayload.Items, i => i.FacilityId == facUsId || i.FacilityId == facOrphanId);
        Assert.Contains(facNpPayload.Items, i => i.FacilityId == facNpId && i.PreservedHistoricalValue);

        var platResp = await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/reference/normalization-quarantine?includeAllStatuses=true&pageSize=100", jwtPlat));
        Assert.Equal(HttpStatusCode.OK, platResp.StatusCode);
        var rawPlatJson = await platResp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("PII ", rawPlatJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-cust@example.test", rawPlatJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("123 Secret Street", rawPlatJson, StringComparison.OrdinalIgnoreCase);

        var platDto = JsonSerializer.Deserialize<PagedNormalizationQuarantineResponse>(rawPlatJson, JsonOptions)!;
        Assert.True(platDto.PendingCount > facNpPayload.PendingCount);
        Assert.Contains(platDto.Items, i => i.EntityId == facOrphanId && i.NormalizationStatus == CountryNormalizationStatus.PendingNormalization);
    }

    private static string TokenFor(WebApplication host, Guid id, UserType ut, Guid? cid, Guid? fid, string[] roles, string[] perms)
    {
        using var scope = host.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateTokens(new User
        {
            Id = id, Email = $"{id:N}@m16.test", FirstName = "T", LastName = "U", UserType = ut, ClientId = cid, AssignedFacilityId = fid
        }, roles, perms).AccessToken;
    }

    private static HttpRequestMessage Authed(HttpMethod method, string path, string jwt, object? body = null)
    {
        var req = new HttpRequestMessage(method, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        if (body != null) req.Content = JsonContent.Create(body);
        return req;
    }

    private static async Task<WebApplication> BuildJwtAppAsync(Func<ApplicationDbContext, Task> seedAsync)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Host.UseDefaultServiceProvider(o => o.ValidateOnBuild = false);
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();

        builder.Services.Configure<JwtOptions>(o =>
        {
            o.SecretKey = TestSecret;
            o.Issuer = TestIssuer;
            o.Audience = TestAudience;
            o.ExpiryMinutes = 60;
        });

        builder.Services.AddControllers()
            .AddApplicationPart(typeof(ReferenceController).Assembly)
            .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
        builder.Services.AddApplicationServices();
        builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
        builder.Services.AddSingleton<ITokenService, JwtTokenService>();
        builder.Services.AddScoped<ILegacyCountryNormalizationService, LegacyCountryNormalizationService>();
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
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret)),
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

        var dbName = $"mdm16-http-{Guid.NewGuid():N}";
        builder.Services.AddDbContext<ApplicationDbContext>((sp, opts) =>
            opts.UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .AddInterceptors(sp.GetRequiredService<AuditableEntityInterceptor>()));
        builder.Services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        var app = builder.Build();
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthenticatedTenantResolution();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await seedAsync(db);
        return app;
    }
}
