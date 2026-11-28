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
using Warehouse3PL.Application.Features.Inbound.Suppliers.Commands.CreateSupplier;
using Warehouse3PL.Application.Features.Outbound.OrderManagement.Commands.ImportOutboundOrdersBulk;
using Warehouse3PL.Application.Features.Outbound.OrderManagement.Commands.CreateOutboundOrder;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Reference;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
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

public sealed class Mdm16RemediationR01ToR05Tests
{
    private const string TestSecret = "Nexus3PL-Mdm16-Remediation-JwtSecret-2026!";
    private const string TestIssuer = "nexus3pl-mdm16-r-auth";
    private const string TestAudience = "nexus3pl-mdm16-r-api";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    [Fact]
    public async Task R01_DomainDefaults_AmendReset_WriteNormalization_And_SaveChangesLifecycleGuard()
    {
        // 1. All 7 entities default to PendingNormalization without silent "USA"/"US" fallback
        var defaultFac = new Facility();
        var defaultCust = new Customer();
        var defaultSup = new Supplier();
        var defaultAddr = new ShippingAddress();
        var defaultRet = new ReturnShipment();
        var slaPolBlank = CarrierContractualSlaPolicy.Create("UPS", CarrierServiceLevel.StandardGround, 2, "1.0.0", destinationCountry: "");
        var slaRecBlank = CarrierSlaComplianceRecord.Create(Guid.NewGuid(), "1ZTEST", "UPS", CarrierServiceLevel.StandardGround, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, null, 2, "1.0.0");

        Assert.Equal(string.Empty, defaultFac.CountryCode);
        Assert.Equal(CountryNormalizationStatus.PendingNormalization, defaultFac.CountryNormalizationStatus);
        Assert.Null(defaultCust.DefaultCountryCode);
        Assert.Equal(CountryNormalizationStatus.PendingNormalization, defaultCust.CountryNormalizationStatus);
        Assert.Null(defaultSup.Country);
        Assert.Equal(CountryNormalizationStatus.PendingNormalization, defaultSup.CountryNormalizationStatus);
        Assert.Equal(string.Empty, defaultAddr.CountryCode);
        Assert.Equal(CountryNormalizationStatus.PendingNormalization, defaultAddr.CountryNormalizationStatus);
        Assert.Equal(string.Empty, defaultRet.OriginCountry);
        Assert.Equal(string.Empty, defaultRet.DestinationCountry);
        Assert.Equal(CountryNormalizationStatus.PendingNormalization, defaultRet.CountryNormalizationStatus);
        Assert.Equal(string.Empty, slaPolBlank.DestinationCountry);
        Assert.Equal(CountryNormalizationStatus.PendingNormalization, slaPolBlank.CountryNormalizationStatus);
        Assert.Equal(string.Empty, slaRecBlank.DestinationCountry);
        Assert.Equal(CountryNormalizationStatus.PendingNormalization, slaRecBlank.CountryNormalizationStatus);

        var facId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using var host = await BuildJwtAppAsync(async db =>
        {
            await ReferenceCatalogSeeder.SeedAsync(db);
            db.Facilities.Add(new Facility { Id = facId, FacilityCode = "R01-FAC", Name = "R01 Facility", CountryCode = "US", Timezone = "America/Chicago", IsActive = true });
            db.Clients.Add(new Client { Id = clientId, AccountCode = "CL-R01", CompanyName = "R01 Client", Status = ClientStatus.Active });
            db.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = clientId, FacilityId = facId, Status = FacilityAccessStatus.Active });
            db.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-R01", Description = "R01 Item", Status = ItemStatus.Active });
            await db.SaveChangesAsync();
        });

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenantProvider = scope.ServiceProvider.GetRequiredService<ITenantProvider>();
        tenantProvider.SetTenantId(clientId);
        var staffUser = new StubUserContext(userId, UserType.Staff, null, facId, [SystemRoles.SuperAdmin], Permissions.GetAllPermissions(), true);

        // 2. Write handlers normalize aliases via catalog and NEVER inject "USA" on missing/unknown country
        var supHandler = new CreateSupplierCommandHandler(db, staffUser);
        var supUs = await supHandler.Handle(new CreateSupplierCommand { ClientId = clientId, SupplierCode = "SUP-US", SupplierName = "US Alias", Country = "United States" }, CancellationToken.None);
        var supUnk = await supHandler.Handle(new CreateSupplierCommand { ClientId = clientId, SupplierCode = "SUP-UNK", SupplierName = "Unknown", Country = "XYZ" }, CancellationToken.None);
        Assert.Equal("US", supUs.Country);
        Assert.Equal(CountryNormalizationStatus.Normalized, supUs.CountryNormalizationStatus);
        Assert.Equal("XYZ", supUnk.Country);
        Assert.Equal(CountryNormalizationStatus.PendingNormalization, supUnk.CountryNormalizationStatus);

        var importHandler = new ImportOutboundOrdersBulkCommandHandler(db, tenantProvider, staffUser);
        var batchResult = await importHandler.Handle(new ImportOutboundOrdersBulkCommand
        {
            FacilityId = facId,
            SourceFileName = "r01.json",
            Orders =
            [
                new CreateOutboundOrderCommand
                {
                    FacilityId = facId,
                    OrderNumber = "ORD-R01-BLANK",
                    ShippingAddress = new CreateShippingAddressDto { RecipientName = "Blank Recipient", AddressLine1 = "1 Main", City = "Chicago", StateOrProvince = "Illinois", PostalCode = "60601", CountryCode = "" },
                    Lines = [new CreateOutboundOrderLineDto { ItemId = itemId, QuantityOrdered = 1 }]
                }
            ]
        }, CancellationToken.None);
        Assert.Equal(1, batchResult.SuccessfulRows);

        var savedAddr = await db.ShippingAddresses.SingleAsync(a => a.RecipientName == "Blank Recipient");
        Assert.Equal(string.Empty, savedAddr.CountryCode);
        Assert.Equal(CountryNormalizationStatus.PendingNormalization, savedAddr.CountryNormalizationStatus);

        // 3. Amend resets status to PendingNormalization; SaveChanges validates canonical ISO vs uncanonical alias/unknown
        savedAddr.Amend("Amended", null, "2 Main", null, "Kathmandu", "Bagmati", "44600", "NP", null, null, userId, "Valid canonical");
        Assert.Equal(CountryNormalizationStatus.PendingNormalization, savedAddr.CountryNormalizationStatus);
        await db.SaveChangesAsync();
        Assert.Equal("NP", savedAddr.CountryCode);
        Assert.Equal(CountryNormalizationStatus.Normalized, savedAddr.CountryNormalizationStatus);

        savedAddr.Amend("Amended Uncanonical", null, "2b Main", null, "Kathmandu", "Bagmati", "44600", "Nepal", null, null, userId, "Raw alias without helper");
        Assert.Equal(CountryNormalizationStatus.PendingNormalization, savedAddr.CountryNormalizationStatus);
        await db.SaveChangesAsync();
        Assert.Equal("Nepal", savedAddr.CountryCode);
        Assert.Equal(CountryNormalizationStatus.PendingNormalization, savedAddr.CountryNormalizationStatus);

        savedAddr.Amend("Amended Bad", null, "3 Main", null, "Unknown", "XX", "000", "ZZZ", null, null, userId, "Unrecognized");
        await db.SaveChangesAsync();
        Assert.Equal("ZZZ", savedAddr.CountryCode);
        Assert.Equal(CountryNormalizationStatus.PendingNormalization, savedAddr.CountryNormalizationStatus);

        // 4. SaveChanges lifecycle enforcement rejects claiming Normalized on blank/alias/invalid raw country
        var invalidClaimFac = new Facility { FacilityCode = "R01-BAD", Name = "Bad", CountryCode = "ZZZ", Timezone = "UTC", CountryNormalizationStatus = CountryNormalizationStatus.Normalized };
        db.Facilities.Add(invalidClaimFac);
        await Assert.ThrowsAsync<ValidationException>(() => db.SaveChangesAsync());
        db.Entry(invalidClaimFac).State = EntityState.Detached;

        var aliasClaimFac = new Facility { FacilityCode = "R01-ALI", Name = "Alias Claim", CountryCode = "USA", Timezone = "America/Chicago", CountryNormalizationStatus = CountryNormalizationStatus.Normalized };
        db.Facilities.Add(aliasClaimFac);
        await Assert.ThrowsAsync<ValidationException>(() => db.SaveChangesAsync());
        db.Entry(aliasClaimFac).State = EntityState.Detached;

        // 5. Historical preserved record retains Normalized across non-country updates while modifying country requires re-normalization
        var histId = Guid.NewGuid();
        var histPol = new CarrierContractualSlaPolicy
        {
            Id = histId,
            CarrierCode = "UPS",
            DestinationCountry = "USA",
            CountryNormalizationStatus = CountryNormalizationStatus.PendingNormalization,
            TransitCommitmentDays = 2,
            IsActive = false
        };
        db.CarrierContractualSlaPolicies.Add(histPol);
        await db.SaveChangesAsync();

        var histAudit = await db.CountryNormalizationAudits.SingleAsync(
            a => a.EntityType == nameof(CarrierContractualSlaPolicy) && a.EntityId == histId && a.FieldName == nameof(CarrierContractualSlaPolicy.DestinationCountry));
        histAudit.NormalizedCountryCode = "US";
        histAudit.NormalizationStatus = CountryNormalizationStatus.Normalized;
        histAudit.PreservedHistoricalValue = true;
        histAudit.Reason = "Immutable historical record preserved untouched";
        histPol.CountryNormalizationStatus = CountryNormalizationStatus.Normalized;
        histPol.Notes = "Metadata update keeping historical DestinationCountry intact";
        await db.SaveChangesAsync();
        Assert.Equal("USA", histPol.DestinationCountry);
        Assert.Equal(CountryNormalizationStatus.Normalized, histPol.CountryNormalizationStatus);

        // Modifying country field automatically resets Normalized -> PendingNormalization; explicitly claiming Normalized still throws
        histPol.DestinationCountry = "NPL";
        await db.SaveChangesAsync();
        Assert.Equal(CountryNormalizationStatus.PendingNormalization, histPol.CountryNormalizationStatus);

        histPol.CountryNormalizationStatus = CountryNormalizationStatus.Normalized;
        await Assert.ThrowsAsync<ValidationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task R02_StrictInference_MultiFacilityUnresolved_TimezoneConflict_NoOriginFallback_And_HistoricalSnapshotPreservation()
    {
        var facUsId = Guid.NewGuid();
        var facNpId = Guid.NewGuid();
        var facUnresolvedId = Guid.NewGuid();
        var facTzConflictId = Guid.NewGuid();
        var clientMixedId = Guid.NewGuid();
        var clientUsId = Guid.NewGuid();
        var custQuarantinedId = Guid.NewGuid();
        var custMixedBlankId = Guid.NewGuid();
        var ordNoCustBlankAddrId = Guid.NewGuid();
        var transferOrderId = Guid.NewGuid();
        var addrNoCustBlankId = Guid.NewGuid();
        var addrTransferValidId = Guid.NewGuid();
        var closedShipmentId = Guid.NewGuid();
        var closedBolId = Guid.NewGuid();
        var closedOrderId = Guid.NewGuid();
        var closedAddrId = Guid.NewGuid();
        var closedInvoiceId = Guid.NewGuid();

        await using var host = await BuildJwtAppAsync(async db =>
        {
            await ReferenceCatalogSeeder.SeedAsync(db);

            // facUs + facUnresolved for clientMixed: network has 1 Normalized + 1 PendingNormalization -> MUST NOT treat client as domestic US!
            db.Facilities.AddRange(
                new Facility { Id = facUsId, FacilityCode = "R02-US", Name = "US Hub", CountryCode = "US", StateOrProvince = "Illinois", PostalCode = "60601", Timezone = "America/Chicago", IsActive = true },
                new Facility { Id = facNpId, FacilityCode = "R02-NP", Name = "NP Hub", CountryCode = "NP", StateOrProvince = "Bagmati", PostalCode = "44600", Timezone = "Asia/Kathmandu", IsActive = true },
                new Facility { Id = facUnresolvedId, FacilityCode = "R02-UNK", Name = "Unresolved Hub", CountryCode = "ZZZ", Timezone = "UTC", IsActive = true },
                new Facility { Id = facTzConflictId, FacilityCode = "R02-TZC", Name = "TZ Conflict Hub", CountryCode = "US", Timezone = "Asia/Kathmandu", IsActive = true });

            db.Clients.AddRange(
                new Client { Id = clientMixedId, AccountCode = "CL-MIX", CompanyName = "Mixed Resolved+Unresolved", Status = ClientStatus.Active },
                new Client { Id = clientUsId, AccountCode = "CL-US2", CompanyName = "US Only Client", Status = ClientStatus.Active });

            db.ClientFacilityAccesses.AddRange(
                new ClientFacilityAccess { ClientId = clientMixedId, FacilityId = facUsId, Status = FacilityAccessStatus.Active },
                new ClientFacilityAccess { ClientId = clientMixedId, FacilityId = facUnresolvedId, Status = FacilityAccessStatus.Active },
                new ClientFacilityAccess { ClientId = clientUsId, FacilityId = facUsId, Status = FacilityAccessStatus.Active });

            // Customer under unresolved network MUST NOT infer "US" even though State=Illinois/60601!
            db.Customers.AddRange(
                new Customer { Id = custMixedBlankId, ClientId = clientMixedId, CustomerCode = "C-MIX", CustomerName = "Mixed Cust", DefaultCountryCode = "", DefaultStateOrProvince = "Illinois", DefaultPostalCode = "60601" },
                // Quarantined customer (cannot verify child order addresses): invalid country "ZZZ"
                new Customer { Id = custQuarantinedId, ClientId = clientUsId, CustomerCode = "C-QUA", CustomerName = "Quarantined Cust", DefaultCountryCode = "ZZZ", DefaultStateOrProvince = "Illinois", DefaultPostalCode = "60601" });

            var draftOrderQuarCustId = Guid.NewGuid();
            db.OutboundOrders.AddRange(
                new OutboundOrder { Id = ordNoCustBlankAddrId, ClientId = clientUsId, FacilityId = facUsId, CustomerId = null, OrderNumber = "ORD-NOCUST", OrderType = OutboundOrderType.B2BSalesOrder, Status = OutboundOrderStatus.Draft, CustomerName = "No Customer" },
                new OutboundOrder { Id = draftOrderQuarCustId, ClientId = clientUsId, FacilityId = facUsId, CustomerId = custQuarantinedId, OrderNumber = "ORD-QUARCUST", OrderType = OutboundOrderType.B2BSalesOrder, Status = OutboundOrderStatus.Draft, CustomerName = "Quar Cust" },
                new OutboundOrder { Id = transferOrderId, ClientId = clientUsId, FacilityId = facUsId, DestinationFacilityId = facNpId, CustomerId = null, OrderNumber = "ORD-XFER", OrderType = OutboundOrderType.StockTransferOrder, Status = OutboundOrderStatus.Draft, CustomerName = "Transfer Order" },
                new OutboundOrder { Id = closedOrderId, ClientId = clientUsId, FacilityId = facUsId, OrderNumber = "ORD-BOL-CLOSED", Status = OutboundOrderStatus.Draft, CustomerName = "Closed Via BOL" });

            // Closed order evidence via Shipment + BillOfLading + PackageStatus.Packed
            db.Shipments.Add(new Shipment
            {
                Id = closedShipmentId,
                ClientId = clientUsId,
                FacilityId = facUsId,
                ShipmentNumber = "SHP-R02",
                CarrierCode = "UPS",
                Status = ShipmentStatus.Dispatched,
                BillOfLadingNumber = "BOL-R02"
            });
            db.BillsOfLading.Add(new BillOfLading
            {
                Id = closedBolId,
                ClientId = clientUsId,
                FacilityId = facUsId,
                ShipmentId = closedShipmentId,
                BolNumber = "BOL-R02",
                CarrierCode = "UPS",
                CarrierName = "UPS",
                ConsigneeName = "Preserved Consignee",
                ConsigneeAddressSnapshot = "456 Legacy Ln, Chicago, IL 60601, USA",
                DocumentSnapshotJson = "{\"country\":\"USA\"}"
            });
            db.Packages.Add(new Package
            {
                Id = Guid.NewGuid(),
                ClientId = clientUsId,
                FacilityId = facUsId,
                OutboundOrderId = closedOrderId,
                ShipmentId = null,
                PackageNumber = "PKG-R02-PACKED",
                Status = PackageStatus.Packed
            });
            var bpId = Guid.NewGuid();
            db.BillingPeriods.Add(new BillingPeriod { Id = bpId, ClientId = clientUsId, PeriodCode = "2026-11", Name = "Nov 2026", StartDateUtc = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), EndDateUtc = new DateTime(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc) });
            db.Invoices.Add(new Invoice
            {
                Id = closedInvoiceId,
                ClientId = clientUsId,
                FacilityId = facUsId,
                BillingPeriodId = bpId,
                InvoiceNumber = "INV-R02-ISSUED",
                Status = InvoiceStatus.Issued,
                Currency = "USD",
                InvoiceDate = DateTime.UtcNow.AddDays(-2),
                DueDate = DateTime.UtcNow.AddDays(28)
            });

            var addrQuarCustId = Guid.NewGuid();
            db.ShippingAddresses.AddRange(
                // Blank address with NO customer MUST NOT infer destination from origin warehouse facUs
                new ShippingAddress { Id = addrNoCustBlankId, OutboundOrderId = ordNoCustBlankAddrId, RecipientName = "NoCust", AddressLine1 = "1 St", City = "Chicago", StateOrProvince = "Illinois", PostalCode = "60601", CountryCode = "" },
                // Blank address with quarantined customer MUST NOT infer "US"
                new ShippingAddress { Id = addrQuarCustId, OutboundOrderId = draftOrderQuarCustId, RecipientName = "QuarCust", AddressLine1 = "2 St", City = "Chicago", StateOrProvince = "Illinois", PostalCode = "60601", CountryCode = "" },
                // StockTransferOrder blank address uses verified DestinationFacility (facNp -> NP)
                new ShippingAddress { Id = addrTransferValidId, OutboundOrderId = transferOrderId, RecipientName = "NP Hub", AddressLine1 = "Ring Rd", City = "Kathmandu", StateOrProvince = "Bagmati", PostalCode = "44600", CountryCode = "" },
                // Closed via Packed package even while Order.Status == Draft -> MUST preserve "USA" verbatim!
                new ShippingAddress { Id = closedAddrId, OutboundOrderId = closedOrderId, RecipientName = "Closed Consignee", AddressLine1 = "456 Legacy Ln", City = "Chicago", StateOrProvince = "Illinois", PostalCode = "60601", CountryCode = "USA" });

            await db.SaveChangesAsync();
        });

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<ILegacyCountryNormalizationService>();
        await svc.BackfillAllAsync("BATCH-R02");

        var fTz = await db.Facilities.IgnoreQueryFilters().SingleAsync(f => f.Id == facTzConflictId);
        Assert.Equal(CountryNormalizationStatus.PendingNormalization, fTz.CountryNormalizationStatus);

        var cMixed = await db.Customers.IgnoreQueryFilters().SingleAsync(c => c.Id == custMixedBlankId);
        Assert.Equal(string.Empty, cMixed.DefaultCountryCode);
        Assert.Equal(CountryNormalizationStatus.PendingNormalization, cMixed.CountryNormalizationStatus);

        var aNoCust = await db.ShippingAddresses.IgnoreQueryFilters().SingleAsync(a => a.Id == addrNoCustBlankId);
        Assert.Equal(string.Empty, aNoCust.CountryCode);
        Assert.Equal(CountryNormalizationStatus.PendingNormalization, aNoCust.CountryNormalizationStatus);

        var aXfer = await db.ShippingAddresses.IgnoreQueryFilters().SingleAsync(a => a.Id == addrTransferValidId);
        Assert.Equal("NP", aXfer.CountryCode);
        Assert.Equal(CountryNormalizationStatus.Normalized, aXfer.CountryNormalizationStatus);

        // Historical closed order/package/BOL/invoice snapshots untouched!
        var aClosed = await db.ShippingAddresses.IgnoreQueryFilters().SingleAsync(a => a.Id == closedAddrId);
        Assert.Equal("USA", aClosed.CountryCode);
        Assert.Equal(CountryNormalizationStatus.Normalized, aClosed.CountryNormalizationStatus);
        var bol = await db.BillsOfLading.IgnoreQueryFilters().SingleAsync(b => b.Id == closedBolId);
        Assert.Equal("456 Legacy Ln, Chicago, IL 60601, USA", bol.ConsigneeAddressSnapshot);
        Assert.Equal("{\"country\":\"USA\"}", bol.DocumentSnapshotJson);
        var inv = await db.Invoices.IgnoreQueryFilters().SingleAsync(i => i.Id == closedInvoiceId);
        Assert.Equal("USD", inv.Currency);
    }

    [Fact]
    public async Task R03_AuditReconciliation_StateTransitions_DeletedCleanup_InitialProvenance_And_R04FailClosedCheck()
    {
        var facId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var custId = Guid.NewGuid();

        // R04: Fail-closed when populated DB lacks active CountryProfile catalog; succeeds cleanly on empty DB
        await using (var emptyHost = await BuildJwtAppAsync(_ => Task.CompletedTask))
        {
            using var scope = emptyHost.Services.CreateScope();
            var emptySvc = scope.ServiceProvider.GetRequiredService<ILegacyCountryNormalizationService>();
            var emptySummary = await emptySvc.BackfillAllAsync("EMPTY-R04");
            Assert.Equal(0, emptySummary.TotalEvaluatedRecords);
        }

        await using (var noCatHost = await BuildJwtAppAsync(async db =>
        {
            db.Facilities.Add(new Facility { Id = Guid.NewGuid(), FacilityCode = "NOCAT", Name = "No Cat", CountryCode = "USA", Timezone = "America/Chicago" });
            await db.SaveChangesAsync();
        }))
        {
            using var scope = noCatHost.Services.CreateScope();
            var noCatSvc = scope.ServiceProvider.GetRequiredService<ILegacyCountryNormalizationService>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => noCatSvc.BackfillAllAsync("FAIL-R04"));
        }

        // R03 full lifecycle test
        await using var host = await BuildJwtAppAsync(async db =>
        {
            await ReferenceCatalogSeeder.SeedAsync(db);
            db.Facilities.Add(new Facility { Id = facId, FacilityCode = "R03-FAC", Name = "Quarantined Fac Initially", CountryCode = "ZZZ", Timezone = "Asia/Kathmandu", IsActive = true });
            db.Clients.Add(new Client { Id = clientId, AccountCode = "CL-R03", CompanyName = "R03 Client", Status = ClientStatus.Active });
            db.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = clientId, FacilityId = facId, Status = FacilityAccessStatus.Active });
            // Customer already has canonical "NP", Bagmati, 44600, so when its parent or facility fixes -> Normalized
            db.Customers.Add(new Customer { Id = custId, ClientId = clientId, CustomerCode = "C-R03", CustomerName = "R03 Cust", DefaultCountryCode = "ZZZ", DefaultStateOrProvince = "Bagmati", DefaultPostalCode = "44600" });
            await db.SaveChangesAsync();
        });

        DateTime initialRecordedAt;
        string initialBatch;
        using (var s1 = host.Services.CreateScope())
        {
            var svc1 = s1.ServiceProvider.GetRequiredService<ILegacyCountryNormalizationService>();
            var db1 = s1.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await svc1.BackfillAllAsync("R03-B1");
            var facAudit1 = await db1.CountryNormalizationAudits.SingleAsync(a => a.EntityType == nameof(Facility) && a.EntityId == facId);
            Assert.Equal(CountryNormalizationStatus.PendingNormalization, facAudit1.NormalizationStatus);
            // F04: Unrecognized arbitrary tokens ('ZZZ') are redacted fail-closed
            Assert.Equal(CountryDiagnosticSanitizer.RedactedPlaceholder, facAudit1.OriginalCountryValue);
            initialBatch = facAudit1.InitialCorrelationBatchId;
            Assert.False(string.IsNullOrWhiteSpace(initialBatch));
            initialRecordedAt = facAudit1.InitialRecordedAtUtc;
        }

        // Batch 2: Operator externally fixes Facility country to canonical "NP" -> audit transitions PendingNormalization -> Normalized while preserving Initial* provenance!
        using (var s2 = host.Services.CreateScope())
        {
            var db2 = s2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var fac = await db2.Facilities.SingleAsync(f => f.Id == facId);
            fac.CountryCode = "NP";
            await db2.SaveChangesAsync();

            var svc2 = s2.ServiceProvider.GetRequiredService<ILegacyCountryNormalizationService>();
            await svc2.BackfillAllAsync("R03-B2");

            var facAudit2 = await db2.CountryNormalizationAudits.SingleAsync(a => a.EntityType == nameof(Facility) && a.EntityId == facId);
            Assert.Equal(CountryNormalizationStatus.Normalized, facAudit2.NormalizationStatus);
            Assert.Equal("NP", facAudit2.NormalizedCountryCode);
            Assert.Equal(CountryDiagnosticSanitizer.RedactedPlaceholder, facAudit2.OriginalCountryValue);
            Assert.Equal(initialBatch, facAudit2.InitialCorrelationBatchId);
            Assert.Equal(initialRecordedAt, facAudit2.InitialRecordedAtUtc);

            // Reverse transition: Facility later gets conflicting timezone "America/Chicago" while Country="NP" -> Normalized -> PendingNormalization!
            fac.Timezone = "America/Chicago";
            await db2.SaveChangesAsync();
            await svc2.BackfillAllAsync("R03-B3");
            var facAudit3 = await db2.CountryNormalizationAudits.SingleAsync(a => a.EntityType == nameof(Facility) && a.EntityId == facId);
            Assert.Equal(CountryNormalizationStatus.PendingNormalization, facAudit3.NormalizationStatus);
            Assert.Equal(initialBatch, facAudit3.InitialCorrelationBatchId);

            // Soft-delete Customer -> audit removed cleanly!
            var cust = await db2.Customers.IgnoreQueryFilters().SingleAsync(c => c.Id == custId);
            cust.IsDeleted = true;
            await db2.SaveChangesAsync();
            await svc2.BackfillAllAsync("R03-B4");
            Assert.False(await db2.CountryNormalizationAudits.AnyAsync(a => a.EntityType == nameof(Customer) && a.EntityId == custId));

            var snapshotJson = await svc2.ExportProvenanceSnapshotJsonAsync();
            Assert.Contains(initialBatch, snapshotJson);
        }
    }

    [Fact]
    public async Task R05_SecurityHardening_RevokedDbRole_StaleAccessFacilityId_ConflictingHeaders_PaginationOverflow_And_PiiRedaction()
    {
        var facUsId = Guid.NewGuid();
        var facNpId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var custPiiEmailId = Guid.NewGuid();
        var custPiiPhoneId = Guid.NewGuid();
        var custPiiStreetId = Guid.NewGuid();
        var staffFacUsId = Guid.NewGuid();
        var staffPlatId = Guid.NewGuid();
        var revokedPlatId = Guid.NewGuid();

        await using var host = await BuildJwtAppAsync(async db =>
        {
            await ReferenceCatalogSeeder.SeedAsync(db);

            db.Facilities.AddRange(
                new Facility { Id = facUsId, FacilityCode = "R05-US", Name = "US Hub", CountryCode = "US", Timezone = "America/Chicago", IsActive = true },
                new Facility { Id = facNpId, FacilityCode = "R05-NP", Name = "NP Hub", CountryCode = "NP", Timezone = "Asia/Kathmandu", IsActive = true });
            db.Clients.Add(new Client { Id = clientId, AccountCode = "CL-R05", CompanyName = "Tenant Revoke", Status = ClientStatus.Active });
            // Client access initially ONLY facNpId (NOT facUsId!)
            db.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = clientId, FacilityId = facNpId, Status = FacilityAccessStatus.Active });

            db.Customers.AddRange(
                new Customer { Id = custPiiEmailId, ClientId = clientId, CustomerCode = "PII-1", CustomerName = "Alice", DefaultCountryCode = "alice.smith@corp.test", DefaultStateOrProvince = "Bagmati", DefaultPostalCode = "44600" },
                new Customer { Id = custPiiPhoneId, ClientId = clientId, CustomerCode = "PII-2", CustomerName = "Bob", DefaultCountryCode = "+1-312-555-0199", DefaultStateOrProvince = "Bagmati", DefaultPostalCode = "44600" },
                new Customer { Id = custPiiStreetId, ClientId = clientId, CustomerCode = "PII-3", CustomerName = "Carol", DefaultCountryCode = "123 Main Street", DefaultStateOrProvince = "Bagmati", DefaultPostalCode = "44600" });

            // Stale audit referencing facUsId even though ClientFacilityAccess is ONLY for facNpId (or was revoked from facUsId!)
            db.CountryNormalizationAudits.Add(new CountryNormalizationAudit
            {
                Id = Guid.NewGuid(),
                EntityType = nameof(Customer),
                EntityId = custPiiEmailId,
                FieldName = nameof(Customer.DefaultCountryCode),
                OriginalCountryValue = CountryDiagnosticSanitizer.RedactedPlaceholder,
                NormalizationStatus = CountryNormalizationStatus.PendingNormalization,
                Reason = "Stale facilityId test",
                FacilityId = facUsId, // STALE facUsId!
                ClientId = clientId,
                CorrelationBatchId = "STALE-1",
                InitialCorrelationBatchId = "STALE-1",
                RecordedAtUtc = DateTime.UtcNow,
                InitialRecordedAtUtc = DateTime.UtcNow
            });

            var platRole = new Role { RoleName = "R05Plat", Description = "Plat", IsSystemRole = false };
            platRole.RolePermissions.Add(new RolePermission { Role = platRole, PermissionKey = Permissions.Global.NormalizationView, AccessLevel = AccessLevel.Read });
            var facRole = new Role { RoleName = "R05Fac", Description = "Fac", IsSystemRole = false };
            facRole.RolePermissions.Add(new RolePermission { Role = facRole, PermissionKey = Permissions.Facilities.Manage, AccessLevel = AccessLevel.Admin });
            var strippedRole = new Role { RoleName = "R05Stripped", Description = "No Norm Perm", IsSystemRole = false };
            db.Roles.AddRange(platRole, facRole, strippedRole);

            var uPlat = new User { Id = staffPlatId, Email = "plat@r05.test", FirstName = "P", LastName = "S", UserType = UserType.Staff, Status = UserStatus.Active, PasswordHash = "x" };
            var uFacUs = new User { Id = staffFacUsId, Email = "facus@r05.test", FirstName = "F", LastName = "U", UserType = UserType.Staff, AssignedFacilityId = facUsId, Status = UserStatus.Active, PasswordHash = "x" };
            var uRevoked = new User { Id = revokedPlatId, Email = "revoked@r05.test", FirstName = "R", LastName = "S", UserType = UserType.Staff, Status = UserStatus.Active, PasswordHash = "x" };
            db.Users.AddRange(uPlat, uFacUs, uRevoked);
            db.UserRoles.AddRange(
                new UserRole { User = uPlat, Role = platRole },
                new UserRole { User = uFacUs, Role = facRole },
                new UserRole { User = uRevoked, Role = strippedRole });

            await db.SaveChangesAsync();
        });

        // Execute Backfill on PII raw values
        using (var s = host.Services.CreateScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<ILegacyCountryNormalizationService>();
            await svc.BackfillAllAsync("R05-PII");
        }

        using var http = host.GetTestClient();
        var jwtPlat = TokenFor(host, staffPlatId, UserType.Staff, null, null, ["R05Plat"], [Permissions.Global.NormalizationView]);
        var jwtFacUs = TokenFor(host, staffFacUsId, UserType.Staff, null, facUsId, ["R05Fac"], [Permissions.Facilities.Manage]);
        // Token claims R05Plat role when DB has R05Stripped -> 401 (JWT role mismatch);
        // Token claims R05Stripped role + global:normalization:view perm when DB R05Stripped lacks permission -> 403 Forbidden!
        var jwtTamperedRole = TokenFor(host, revokedPlatId, UserType.Staff, null, null, ["R05Plat"], [Permissions.Global.NormalizationView]);
        var jwtStrippedPerm = TokenFor(host, revokedPlatId, UserType.Staff, null, null, ["R05Stripped"], [Permissions.Global.NormalizationView]);

        // 1. DB authoritative permission enforcement blocks tampered role claims (401) and stripped DB role permissions (403)
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/reference/normalization-quarantine", jwtTamperedRole))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/reference/normalization-quarantine", jwtStrippedPerm))).StatusCode);

        // 2. Conflicting query ?facilityId vs X-Facility-Id -> 400
        var conflictReq = Authed(HttpMethod.Get, $"/api/v1/reference/normalization-quarantine?facilityId={facNpId}", jwtPlat);
        conflictReq.Headers.Add("X-Facility-Id", facUsId.ToString());
        Assert.Equal(HttpStatusCode.BadRequest, (await http.SendAsync(conflictReq)).StatusCode);

        // 3. Unauthorized ?clientId on facility-scoped manager when client does NOT have active access to facUsId -> 403
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Authed(HttpMethod.Get, $"/api/v1/reference/normalization-quarantine?clientId={clientId}", jwtFacUs))).StatusCode);

        // 4. Stale audit.FacilityId bypass check: facUs manager queries without ?clientId -> MUST NOT return client's rows because ClientFacilityAccess for facUsId is absent/revoked
        var facUsResp = await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/reference/normalization-quarantine?includeAllStatuses=true", jwtFacUs));
        Assert.Equal(HttpStatusCode.OK, facUsResp.StatusCode);
        var facUsDto = await facUsResp.Content.ReadFromJsonAsync<PagedNormalizationQuarantineResponse>(JsonOptions);
        Assert.NotNull(facUsDto);
        Assert.DoesNotContain(facUsDto!.Items, i => i.ClientId == clientId);

        // 5. Pagination integer overflow -> 400 Bad Request
        Assert.Equal(HttpStatusCode.BadRequest, (await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/reference/normalization-quarantine?pageNumber=2147483647&pageSize=20", jwtPlat))).StatusCode);

        // 6. Zero-PII redaction check across email, phone, street address
        var platResp = await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/reference/normalization-quarantine?includeAllStatuses=true&pageSize=100", jwtPlat));
        Assert.Equal(HttpStatusCode.OK, platResp.StatusCode);
        var platJson = await platResp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("alice.smith@corp.test", platJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("+1-312-555-0199", platJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("123 Main Street", platJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(CountryDiagnosticSanitizer.RedactedPlaceholder, platJson);
    }

    [Fact]
    public async Task F01_RuntimeNormalizationAndDiagnosticConsistency_WithoutBackfill_ReturnShipmentDualFields_CanonicalTrim_And_GeoContradictions()
    {
        var facId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var staffPlatId = Guid.NewGuid();
        var facGeoConflictId = Guid.NewGuid();
        var retId = Guid.NewGuid();

        await using var host = await BuildJwtAppAsync(async db =>
        {
            await ReferenceCatalogSeeder.SeedAsync(db);
            db.Facilities.Add(new Facility { Id = facId, FacilityCode = "F01-US", Name = "F01 US", CountryCode = " us ", Timezone = "America/Chicago", IsActive = true });
            db.Clients.Add(new Client { Id = clientId, AccountCode = "CL-F01", CompanyName = "F01 Client", Status = ClientStatus.Active });
            db.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = clientId, FacilityId = facId, Status = FacilityAccessStatus.Active });
            db.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-F01", Description = "F01 Item", Status = ItemStatus.Active });

            var platRole = new Role { RoleName = "F01Plat", Description = "Plat", IsSystemRole = false };
            platRole.RolePermissions.Add(new RolePermission { Role = platRole, PermissionKey = Permissions.Global.NormalizationView, AccessLevel = AccessLevel.Read });
            var superRole = new Role { RoleName = SystemRoles.SuperAdmin, Description = "SuperAdmin", IsSystemRole = true };
            db.Roles.AddRange(platRole, superRole);
            var uPlat = new User { Id = staffPlatId, Email = "plat@f01.test", FirstName = "P", LastName = "S", UserType = UserType.Staff, Status = UserStatus.Active, PasswordHash = "x" };
            db.Users.Add(uPlat);
            db.UserRoles.Add(new UserRole { User = uPlat, Role = platRole });
            db.UserRoles.Add(new UserRole { User = uPlat, Role = superRole });
            await db.SaveChangesAsync();
        });

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenantProvider = scope.ServiceProvider.GetRequiredService<ITenantProvider>();
        tenantProvider.SetTenantId(clientId);
        var staffUser = new StubUserContext(staffPlatId, UserType.Staff, null, facId, [SystemRoles.SuperAdmin], Permissions.GetAllPermissions(), true);

        // 1. Persisted canonical country code must be exact 2 uppercase ASCII letters with zero whitespace
        var savedFac = await db.Facilities.SingleAsync(f => f.Id == facId);
        Assert.Equal("US", savedFac.CountryCode);
        Assert.Equal(CountryNormalizationStatus.Normalized, savedFac.CountryNormalizationStatus);

        // 2. Ordinary write paths (CreateSupplier + Bulk Import) create visible pending diagnostics WITHOUT calling BackfillAllAsync
        var supHandler = new CreateSupplierCommandHandler(db, staffUser);
        var supRes = await supHandler.Handle(new CreateSupplierCommand { ClientId = clientId, SupplierCode = "SUP-F01-PEND", SupplierName = "Pending Sup", Country = "UnknownRegion" }, CancellationToken.None);
        Assert.Equal(CountryNormalizationStatus.PendingNormalization, supRes.CountryNormalizationStatus);

        var importHandler = new ImportOutboundOrdersBulkCommandHandler(db, tenantProvider, staffUser);
        var batchRes = await importHandler.Handle(new ImportOutboundOrdersBulkCommand
        {
            FacilityId = facId,
            SourceFileName = "f01.json",
            Orders =
            [
                new CreateOutboundOrderCommand
                {
                    FacilityId = facId,
                    OrderNumber = "ORD-F01-1",
                    ShippingAddress = new CreateShippingAddressDto { RecipientName = "Rec", AddressLine1 = "1 St", City = "Chicago", StateOrProvince = "Illinois", PostalCode = "60601", CountryCode = "" },
                    Lines = [new CreateOutboundOrderLineDto { ItemId = itemId, QuantityOrdered = 1 }]
                }
            ]
        }, CancellationToken.None);
        Assert.Equal(1, batchRes.SuccessfulRows);
        var importedAddr = await db.ShippingAddresses.IgnoreQueryFilters().SingleAsync(a => a.RecipientName == "Rec");

        // 3. Geographic/timezone contradiction on Facility is quarantined immediately at runtime (never treated as verified parent)
        db.Facilities.Add(new Facility
        {
            Id = facGeoConflictId,
            FacilityCode = "F01-CONFLICT",
            Name = "Conflicting TZ",
            CountryCode = "US",
            StateOrProvince = "Bagmati",
            PostalCode = "44600",
            Timezone = "Asia/Kathmandu",
            IsActive = true
        });

        // 4. ReturnShipment dual fields handled independently
        db.ReturnShipments.Add(new ReturnShipment
        {
            Id = retId,
            ClientId = clientId,
            OriginFacilityId = facId,
            DestinationFacilityId = facId,
            OrderId = importedAddr.OutboundOrderId,
            PackageId = Guid.NewGuid(),
            ReturnRmaNumber = "RMA-F01-DUAL",
            Reason = ReturnReason.DamagedInTransit,
            AuthorizedByUserId = staffPlatId,
            CarrierCode = "UPS",
            TrackingNumber = "1ZF01",
            Status = ReturnShipmentStatus.Authorized,
            OriginCountry = "InvalidOrigin",
            DestinationCountry = "US"
        });
        await db.SaveChangesAsync();

        var facConflict = await db.Facilities.SingleAsync(f => f.Id == facGeoConflictId);
        Assert.Equal(CountryNormalizationStatus.PendingNormalization, facConflict.CountryNormalizationStatus);

        var retSaved = await db.ReturnShipments.IgnoreQueryFilters().SingleAsync(r => r.Id == retId);
        Assert.Equal(CountryNormalizationStatus.PendingNormalization, retSaved.CountryNormalizationStatus);

        // Verify ALL pending records are visible in authorized HTTP diagnostics WITHOUT ever calling BackfillAllAsync
        using var http = host.GetTestClient();
        var jwtPlat = TokenFor(host, staffPlatId, UserType.Staff, null, null, ["F01Plat"], [Permissions.Global.NormalizationView]);
        var diagResp = await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/reference/normalization-quarantine?pageSize=100", jwtPlat));
        Assert.Equal(HttpStatusCode.OK, diagResp.StatusCode);
        var diagDto = await diagResp.Content.ReadFromJsonAsync<PagedNormalizationQuarantineResponse>(JsonOptions);
        Assert.NotNull(diagDto);
        Assert.Contains(diagDto!.Items, i => i.EntityType == nameof(Supplier) && i.EntityId == supRes.Id);
        Assert.Contains(diagDto.Items, i => i.EntityType == nameof(ShippingAddress) && i.EntityId == importedAddr.Id);
        Assert.Contains(diagDto.Items, i => i.EntityType == nameof(Facility) && i.EntityId == facGeoConflictId);
        Assert.Contains(diagDto.Items, i => i.EntityType == nameof(ReturnShipment) && i.EntityId == retId && i.FieldName == nameof(ReturnShipment.OriginCountry));
        Assert.DoesNotContain(diagDto.Items, i => i.EntityType == nameof(ReturnShipment) && i.EntityId == retId && i.FieldName == nameof(ReturnShipment.DestinationCountry));

        // 5. Runtime corrections reconcile diagnostics atomically while preserving Initial* provenance, and Facility status-only changes emit MDM-1.5 epochs
        var initialSupAudit = await db.CountryNormalizationAudits.SingleAsync(a => a.EntityType == nameof(Supplier) && a.EntityId == supRes.Id);
        var origInitTime = initialSupAudit.InitialRecordedAtUtc;
        var origInitBatch = initialSupAudit.InitialCorrelationBatchId;

        var supEntity = await db.Suppliers.IgnoreQueryFilters().SingleAsync(s => s.Id == supRes.Id);
        supEntity.Country = " np ";
        retSaved.OriginCountry = "US";
        var epochsBefore = await db.ConfigurationScopeEpochs.Where(e => e.ScopeType == ConfigurationScopeType.Facility && e.ScopeKey == facGeoConflictId.ToString("D").ToUpperInvariant()).Select(e => e.CurrentEpoch).FirstOrDefaultAsync();
        facConflict.Timezone = "America/Chicago";
        facConflict.StateOrProvince = "Illinois";
        facConflict.PostalCode = "60601";
        await db.SaveChangesAsync();

        Assert.Equal("NP", supEntity.Country);
        Assert.Equal(CountryNormalizationStatus.Normalized, supEntity.CountryNormalizationStatus);
        Assert.Equal(CountryNormalizationStatus.Normalized, retSaved.CountryNormalizationStatus);
        Assert.Equal(CountryNormalizationStatus.Normalized, facConflict.CountryNormalizationStatus);

        var reconciledSupAudit = await db.CountryNormalizationAudits.SingleAsync(a => a.EntityType == nameof(Supplier) && a.EntityId == supRes.Id);
        Assert.Equal(CountryNormalizationStatus.Normalized, reconciledSupAudit.NormalizationStatus);
        Assert.Equal("NP", reconciledSupAudit.NormalizedCountryCode);
        Assert.Equal(origInitTime, reconciledSupAudit.InitialRecordedAtUtc);
        Assert.Equal(origInitBatch, reconciledSupAudit.InitialCorrelationBatchId);

        var epochsAfter = await db.ConfigurationScopeEpochs.Where(e => e.ScopeType == ConfigurationScopeType.Facility && e.ScopeKey == facGeoConflictId.ToString("D").ToUpperInvariant()).Select(e => e.CurrentEpoch).FirstOrDefaultAsync();
        Assert.True(epochsAfter > epochsBefore);
    }

    [Fact]
    public async Task F04_FailClosedAllowlistedDiagnosticPrivacy_PersonalNames_ConfidentialText_SearchCountInference_And_Export()
    {
        var facUsId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var staffPlatId = Guid.NewGuid();

        await using var host = await BuildJwtAppAsync(async db =>
        {
            await ReferenceCatalogSeeder.SeedAsync(db);
            db.Facilities.Add(new Facility { Id = facUsId, FacilityCode = "F04-US", Name = "F04 Hub", CountryCode = "US", Timezone = "America/Chicago", IsActive = true });
            db.Clients.Add(new Client { Id = clientId, AccountCode = "CL-F04", CompanyName = "F04 Client", Status = ClientStatus.Active });
            db.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = clientId, FacilityId = facUsId, Status = FacilityAccessStatus.Active });

            // Synthetic personal names, confidential free-text, email, phone, street address + verified catalog alias
            db.Customers.AddRange(
                new Customer { ClientId = clientId, CustomerCode = "F04-1", CustomerName = "N1", DefaultCountryCode = "Johnathan Doe" },
                new Customer { ClientId = clientId, CustomerCode = "F04-2", CustomerName = "N2", DefaultCountryCode = "Priya Sharma" },
                new Customer { ClientId = clientId, CustomerCode = "F04-3", CustomerName = "N3", DefaultCountryCode = "VIP Confidential Note" },
                new Customer { ClientId = clientId, CustomerCode = "F04-4", CustomerName = "N4", DefaultCountryCode = "alice.smith@corp.test" },
                new Customer { ClientId = clientId, CustomerCode = "F04-5", CustomerName = "N5", DefaultCountryCode = "+1-312-555-0199" },
                new Customer { ClientId = clientId, CustomerCode = "F04-6", CustomerName = "N6", DefaultCountryCode = "123 Main Street" },
                new Customer { ClientId = clientId, CustomerCode = "F04-7", CustomerName = "N7", DefaultCountryCode = "USA" });

            var platRole = new Role { RoleName = "F04Plat", Description = "Plat", IsSystemRole = false };
            platRole.RolePermissions.Add(new RolePermission { Role = platRole, PermissionKey = Permissions.Global.NormalizationView, AccessLevel = AccessLevel.Read });
            db.Roles.Add(platRole);
            var uPlat = new User { Id = staffPlatId, Email = "plat@f04.test", FirstName = "P", LastName = "S", UserType = UserType.Staff, Status = UserStatus.Active, PasswordHash = "x" };
            db.Users.Add(uPlat);
            db.UserRoles.Add(new UserRole { User = uPlat, Role = platRole });
            await db.SaveChangesAsync();
        });

        using var http = host.GetTestClient();
        var jwtPlat = TokenFor(host, staffPlatId, UserType.Staff, null, null, ["F04Plat"], [Permissions.Global.NormalizationView]);

        var allResp = await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/reference/normalization-quarantine?includeAllStatuses=true&pageSize=100", jwtPlat));
        Assert.Equal(HttpStatusCode.OK, allResp.StatusCode);
        var allJson = await allResp.Content.ReadAsStringAsync();

        // Fail-closed redaction: personal names, confidential notes, emails, phones, streets never echoed; verified catalog token 'USA' allowed
        foreach (var forbidden in new[] { "Johnathan", "Doe", "Priya", "Sharma", "Confidential", "alice.smith", "555-0199", "Main Street" })
        {
            Assert.DoesNotContain(forbidden, allJson, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Contains("\"USA\"", allJson);
        Assert.Contains(CountryDiagnosticSanitizer.RedactedPlaceholder, allJson);

        // Search count inference guard: querying any redacted/unverified text MUST yield 0 items and 0 counts
        foreach (var probe in new[] { "Johnathan", "Priya", "Confidential", "alice", "555-0199", "Main" })
        {
            var searchResp = await http.SendAsync(Authed(HttpMethod.Get, $"/api/v1/reference/normalization-quarantine?includeAllStatuses=true&search={Uri.EscapeDataString(probe)}", jwtPlat));
            Assert.Equal(HttpStatusCode.OK, searchResp.StatusCode);
            var searchDto = await searchResp.Content.ReadFromJsonAsync<PagedNormalizationQuarantineResponse>(JsonOptions);
            Assert.NotNull(searchDto);
            Assert.Equal(0, searchDto!.TotalCount);
            Assert.Equal(0, searchDto.PendingCount);
            Assert.Equal(0, searchDto.NormalizedCount);
            Assert.Empty(searchDto.Items);
        }

        // Verified catalog token search works cleanly
        var usaSearch = await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/reference/normalization-quarantine?includeAllStatuses=true&search=USA", jwtPlat));
        Assert.Equal(HttpStatusCode.OK, usaSearch.StatusCode);
        var usaDto = await usaSearch.Content.ReadFromJsonAsync<PagedNormalizationQuarantineResponse>(JsonOptions);
        Assert.True(usaDto!.TotalCount >= 1);

        // Export provenance snapshot applies identical allowlist redaction
        using var scope = host.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ILegacyCountryNormalizationService>();
        var exportJson = await svc.ExportProvenanceSnapshotJsonAsync();
        foreach (var forbidden in new[] { "Johnathan", "Priya", "Confidential", "alice.smith", "555-0199", "Main Street" })
        {
            Assert.DoesNotContain(forbidden, exportJson, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Contains("\"USA\"", exportJson);
    }

    [Fact]
    public async Task F04R01_StructuredDiagnosticReasons_RejectArbitrarySuffixAfterApprovedPrefix_AndPreventRawReasonSearchLeak()
    {
        var staffPlatId = Guid.NewGuid();
        var facId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var c1 = Guid.NewGuid();
        var c2 = Guid.NewGuid();
        var c3 = Guid.NewGuid();
        var cInactiveAlias = Guid.NewGuid();

        await using var host = await BuildJwtAppAsync(async db =>
        {
            await ReferenceCatalogSeeder.SeedAsync(db);
            // Inactive country profile with active alias -> MUST NOT be searchable/displayable as verified token
            db.CountryProfiles.Add(new CountryProfile
            {
                CountryCode = "XX",
                IsoAlpha3Code = "XXX",
                Name = "Inactive Land",
                IsActive = false
            });
            db.CountryNormalizationAliases.Add(new CountryNormalizationAlias
            {
                AliasKey = "INACTIVEALIAS",
                CountryCode = "XX",
                SourceStandard = "Test",
                IsActive = true
            });

            var fac = new Facility { Id = facId, FacilityCode = "F04R1-US", Name = "F04R1 US", CountryCode = "US", Timezone = "America/Chicago", IsActive = true };
            var client = new Client { Id = clientId, AccountCode = "F04R1-CL", CompanyName = "F04R1 Client", Status = ClientStatus.Active };
            db.Facilities.Add(fac);
            db.Clients.Add(client);
            db.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = clientId, FacilityId = facId, Status = FacilityAccessStatus.Active });
            db.Customers.AddRange(
                new Customer { Id = c1, ClientId = clientId, CustomerCode = "C1", CustomerName = "N1", DefaultCountryCode = "ZZ1" },
                new Customer { Id = c2, ClientId = clientId, CustomerCode = "C2", CustomerName = "N2", DefaultCountryCode = "ZZ2" },
                new Customer { Id = c3, ClientId = clientId, CustomerCode = "C3", CustomerName = "N3", DefaultCountryCode = "US" },
                new Customer { Id = cInactiveAlias, ClientId = clientId, CustomerCode = "C4", CustomerName = "N4", DefaultCountryCode = "INACTIVEALIAS" });

            var platRole = new Role { RoleName = "F04R1Plat", Description = "Plat", IsSystemRole = false };
            platRole.RolePermissions.Add(new RolePermission { Role = platRole, PermissionKey = Permissions.Global.NormalizationView, AccessLevel = AccessLevel.Read });
            var uPlat = new User { Id = staffPlatId, Email = "plat@f04r1.test", FirstName = "P", LastName = "S", UserType = UserType.Staff, Status = UserStatus.Active, PasswordHash = "x" };
            db.Roles.Add(platRole);
            db.Users.Add(uPlat);
            db.UserRoles.Add(new UserRole { User = uPlat, Role = platRole });
            await db.SaveChangesAsync();

            // Inject synthetic legacy persisted reasons containing personal names & confidential text AFTER approved prefixes
            var a1 = await db.CountryNormalizationAudits.SingleAsync(a => a.EntityType == nameof(Customer) && a.EntityId == c1);
            a1.Reason = "Unrecognized country token — Johnathan Vance (Confidential VIP Account)";
            var a2 = await db.CountryNormalizationAudits.SingleAsync(a => a.EntityType == nameof(Customer) && a.EntityId == c2);
            a2.Reason = "Conflicting geographic evidence: Elena Rostova private postal 90210 secret memo";
            var a4 = await db.CountryNormalizationAudits.SingleAsync(a => a.EntityType == nameof(Customer) && a.EntityId == cInactiveAlias);
            a4.OriginalCountryValue = "INACTIVEALIAS";
            a4.Reason = "Normalized to canonical 'US' — Internal Project Orion";

            db.CountryNormalizationAudits.Add(new CountryNormalizationAudit
            {
                EntityType = nameof(Customer),
                EntityId = c3,
                FieldName = nameof(Customer.DefaultCountryCode),
                OriginalCountryValue = "USA",
                NormalizedCountryCode = "US",
                NormalizationStatus = CountryNormalizationStatus.Normalized,
                Reason = CountryDiagnosticSanitizer.ConflictingGeographicEvidence.Description,
                ClientId = clientId,
                FacilityId = facId,
                CorrelationBatchId = "F04R1-CTRL",
                InitialCorrelationBatchId = "F04R1-CTRL",
                RecordedAtUtc = DateTime.UtcNow,
                InitialRecordedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        });

        // 1. Direct sanitizer contract: never echo suffix text after approved prefixes
        Assert.Equal(CountryDiagnosticSanitizer.UnrecognizedCountry.Description,
            CountryDiagnosticSanitizer.SanitizeReason("Unrecognized country token — Johnathan Vance (Confidential VIP Account)"));
        Assert.Equal(CountryDiagnosticSanitizer.ConflictingGeographicEvidence.Code,
            CountryDiagnosticSanitizer.GetReasonCode("Conflicting geographic evidence: Elena Rostova private postal 90210 secret memo"));
        Assert.Equal(CountryDiagnosticSanitizer.NormalizedCanonical.Description,
            CountryDiagnosticSanitizer.SanitizeReason("Normalized to canonical 'US' — Internal Project Orion"));

        using var http = host.GetTestClient();
        var jwtPlat = TokenFor(host, staffPlatId, UserType.Staff, null, null, ["F04R1Plat"], [Permissions.Global.NormalizationView]);

        // 2. HTTP response body must expose only controlled reason codes/descriptions and NEVER leak suffix text or inactive aliases
        var allResp = await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/reference/normalization-quarantine?includeAllStatuses=true&pageSize=100", jwtPlat));
        Assert.Equal(HttpStatusCode.OK, allResp.StatusCode);
        var allJson = await allResp.Content.ReadAsStringAsync();
        foreach (var forbidden in new[] { "Johnathan", "Vance", "Confidential", "VIP Account", "Elena", "Rostova", "90210", "secret memo", "Project Orion", "INACTIVEALIAS" })
        {
            Assert.DoesNotContain(forbidden, allJson, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Contains("UNRECOGNIZED_COUNTRY", allJson);
        Assert.Contains("CONFLICTING_GEOGRAPHIC_EVIDENCE", allJson);

        // 3. Search must NEVER match raw persisted Reason suffixes or inactive-country aliases (0 matches, 0 count leak)
        foreach (var probe in new[] { "Johnathan", "Vance", "Confidential", "Elena", "Rostova", "90210", "Orion", "INACTIVEALIAS" })
        {
            var searchResp = await http.SendAsync(Authed(HttpMethod.Get, $"/api/v1/reference/normalization-quarantine?includeAllStatuses=true&search={Uri.EscapeDataString(probe)}", jwtPlat));
            Assert.Equal(HttpStatusCode.OK, searchResp.StatusCode);
            var searchDto = await searchResp.Content.ReadFromJsonAsync<PagedNormalizationQuarantineResponse>(JsonOptions);
            Assert.NotNull(searchDto);
            Assert.Equal(0, searchDto!.TotalCount);
            Assert.Equal(0, searchDto.PendingCount);
            Assert.Equal(0, searchDto.NormalizedCount);
            Assert.Empty(searchDto.Items);
        }

        // 4. Safe search by predefined ReasonCode matches controlled rows cleanly
        var codeSearch = await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/reference/normalization-quarantine?includeAllStatuses=true&search=CONFLICTING_GEOGRAPHIC_EVIDENCE", jwtPlat));
        Assert.Equal(HttpStatusCode.OK, codeSearch.StatusCode);
        var codeDto = await codeSearch.Content.ReadFromJsonAsync<PagedNormalizationQuarantineResponse>(JsonOptions);
        Assert.True(codeDto!.TotalCount >= 1);
        Assert.All(codeDto.Items, item => Assert.Equal("CONFLICTING_GEOGRAPHIC_EVIDENCE", item.ReasonCode));

        // 5. JSON export must never expose suffix text and must include structured ReasonCode
        using var scope = host.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ILegacyCountryNormalizationService>();
        var exportJson = await svc.ExportProvenanceSnapshotJsonAsync();
        foreach (var forbidden in new[] { "Johnathan", "Vance", "Confidential", "Elena", "Rostova", "90210", "Project Orion", "INACTIVEALIAS" })
        {
            Assert.DoesNotContain(forbidden, exportJson, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Contains("\"ReasonCode\":", exportJson);
    }

    private static string TokenFor(WebApplication host, Guid id, UserType ut, Guid? cid, Guid? fid, string[] roles, string[] perms)
    {
        using var scope = host.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateTokens(new User
        {
            Id = id, Email = $"{id:N}@r05.test", FirstName = "T", LastName = "U", UserType = ut, ClientId = cid, AssignedFacilityId = fid
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

        var dbName = $"mdm16-r01r05-{Guid.NewGuid():N}";
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

    private sealed class StubUserContext(
        Guid userId,
        UserType userType,
        Guid? tenantId,
        Guid? facilityId,
        IReadOnlyList<string> roles,
        IReadOnlyList<string> perms,
        bool isSuperAdmin) : ICurrentUserContext
    {
        public Guid? UserId => userId;
        public string? Email => $"{userId:N}@stub.test";
        public Guid? TenantId => tenantId;
        public Guid? FacilityId => facilityId;
        public Guid? RequestedFacilityId => facilityId;
        public UserType? UserType => userType;
        public IReadOnlyList<string> Roles => roles;
        public IReadOnlyList<string> Permissions => perms;
        public bool IsAuthenticated => true;
        public bool IsStaff => userType == Warehouse3PL.Domain.Enums.UserType.Staff;
        public bool IsSuperAdmin => isSuperAdmin;
        public bool HasPermission(string p) => perms.Contains(p, StringComparer.OrdinalIgnoreCase);
        public bool IsInRole(string r) => roles.Contains(r, StringComparer.OrdinalIgnoreCase);
    }
}
