using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Domain.ValueObjects;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Services.Analytics;
using Warehouse3PL.UnitTests.Common;

namespace Warehouse3PL.UnitTests.Analytics;

/// <summary>
/// Dedicated verification test suite for Phase 6.4.4.1: Buy-Side &amp; Sell-Side Financial Reconciliation.
/// Validates:
/// 1. Canonical buy-side &amp; sell-side separation without double counting.
/// 2. Division-by-zero safety on empty batches or zero denominators.
/// 3. Signed adjustment accounting: Original + Sum(SignedAdjustments).
/// 4. Strict tenant and facility isolation boundaries.
/// 5. Operational ledger invariance (0 mutations to Phase 4 &amp; 5 transactional tables).
/// 6. Duplicate source record prevention (CarrierFreightCostSummary projection is never joined into financial totals).
/// 7. Multiple signed adjustments netting.
/// 8. Reversal adjustments restoring net balances while preserving immutable audit history.
/// 9. Multi-package exact cent cost &amp; revenue conservation with residual cent allocation to the heaviest package.
/// 10. Unauthorized tenant or facility access rejection with ForbiddenException.
/// </summary>
public class FreightReconciliationTests
{
    private readonly Guid _clientAId = Guid.NewGuid();
    private readonly Guid _clientBId = Guid.NewGuid();
    private readonly Guid _facility1Id = Guid.NewGuid();
    private readonly Guid _facility2Id = Guid.NewGuid();
    private readonly Guid _staffUserId = Guid.NewGuid();
    private readonly IFxRateProvider _fxRateProvider = new FxRateProvider();

    private async Task<(ApplicationDbContext Context, string DbName)> CreateSeededContextAsync(
        TestTenantProvider tenantProvider,
        TestCurrentUserContext currentUserContext,
        string? dbName = null)
    {
        var databaseName = dbName ?? Guid.NewGuid().ToString();
        var context = TestDbContextFactory.Create(tenantProvider, currentUserContext, databaseName);

        var clientA = new Client
        {
            Id = _clientAId,
            CompanyName = "Alpha Logistics Client",
            AccountCode = "ALC01",
            Status = ClientStatus.Active
        };

        var clientB = new Client
        {
            Id = _clientBId,
            CompanyName = "Beta Supply Retail",
            AccountCode = "BSR02",
            Status = ClientStatus.Active
        };

        var facility1 = new Facility
        {
            Id = _facility1Id,
            FacilityCode = "FAC-ORD1",
            Name = "Chicago Hub"
        };

        var facility2 = new Facility
        {
            Id = _facility2Id,
            FacilityCode = "FAC-DFW1",
            Name = "Dallas Hub"
        };

        var staffUser = new User
        {
            Id = _staffUserId,
            Email = "finance.auditor@warehouse3pl.test",
            PasswordHash = "hashed",
            FirstName = "Eleanor",
            LastName = "Vance",
            UserType = UserType.Staff,
            Status = UserStatus.Active
        };

        context.Clients.AddRange(clientA, clientB);
        context.Facilities.AddRange(facility1, facility2);
        context.Users.Add(staffUser);
        context.ClientFacilityAccesses.AddRange(
            new ClientFacilityAccess { ClientId = _clientAId, FacilityId = _facility1Id, Status = FacilityAccessStatus.Active, PrimaryOperatingFacility = true },
            new ClientFacilityAccess { ClientId = _clientAId, FacilityId = _facility2Id, Status = FacilityAccessStatus.Active, PrimaryOperatingFacility = false },
            new ClientFacilityAccess { ClientId = _clientBId, FacilityId = _facility2Id, Status = FacilityAccessStatus.Active, PrimaryOperatingFacility = true }
        );
        await context.SaveChangesAsync();

        return (context, databaseName);
    }

    private static Shipment CreateShipment(
        Guid clientId,
        Guid facilityId,
        string shipmentNumber,
        string carrierCode = "FEDEX",
        DateTime? dispatchedAt = null,
        decimal weightKg = 10.0m)
    {
        var shipment = new Shipment
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            FacilityId = facilityId,
            ShipmentNumber = shipmentNumber,
            CarrierCode = carrierCode,
            CarrierServiceLevel = CarrierServiceLevel.StandardGround,
            Status = ShipmentStatus.Dispatched,
            DispatchedAtUtc = dispatchedAt ?? DateTime.UtcNow,
            TotalPackagesCount = 1,
            TotalWeightKg = weightKg
        };

        var package = new Package
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            FacilityId = facilityId,
            OutboundOrderId = Guid.NewGuid(),
            HandlingUnitId = Guid.NewGuid(),
            ShipmentId = shipment.Id,
            PackageNumber = $"PKG-{Guid.NewGuid():N}"[..10].ToUpperInvariant(),
            CartonSequence = 1,
            GrossWeightKg = weightKg,
            CarrierCode = carrierCode,
            TrackingNumber = $"TRK-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            Status = PackageStatus.Loaded
        };

        shipment.Packages.Add(package);
        return shipment;
    }

    [Fact]
    public async Task Test_01_QuoteEstimateInvoiceReconciled_AmountSeparation()
    {
        // ARRANGE
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, dbName) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var dispatchedAt = new DateTime(2026, 11, 10, 14, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-RECON-001", "FEDEX", dispatchedAt);
        context.Shipments.Add(shipment);

        // 1. Quoted baseline record: $100.00
        var quotedCost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierServiceLevel = CarrierServiceLevel.StandardGround,
            BaseTransportationCost = 90.00m,
            FuelSurcharge = 10.00m,
            InsuranceCost = 0m,
            AccessorialCost = 0m,
            TotalCarrierCost = 100.00m,
            CostAssessedAtUtc = dispatchedAt.AddDays(-1),
            Status = CarrierCostStatus.Estimated,
            Currency = "USD"
        };

        // 2. Tender estimate record: $105.00
        var estimatedCost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierServiceLevel = CarrierServiceLevel.StandardGround,
            BaseTransportationCost = 95.00m,
            FuelSurcharge = 10.00m,
            InsuranceCost = 0m,
            AccessorialCost = 0m,
            TotalCarrierCost = 105.00m,
            CostAssessedAtUtc = dispatchedAt,
            Status = CarrierCostStatus.ActualConfirmed,
            Currency = "USD"
        };

        // 3. Authoritative Carrier Invoice: Base $100 + Fuel $10 + Insurance $5 = $115.00
        var invoicedCost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierServiceLevel = CarrierServiceLevel.StandardGround,
            CarrierInvoiceNumber = "INV-FEDEX-9001",
            BaseTransportationCost = 100.00m,
            FuelSurcharge = 10.00m,
            InsuranceCost = 5.00m,
            AccessorialCost = 0m,
            TotalCarrierCost = 115.00m,
            CostAssessedAtUtc = dispatchedAt.AddDays(5),
            Status = CarrierCostStatus.Reconciled,
            Currency = "USD"
        };

        // 4. Signed Adjustment: -$3.00 concession
        var adjustment = CarrierCostAdjustment.Create(
            clientId: _clientAId,
            facilityId: _facility1Id,
            carrierCostRecordId: invoicedCost.Id,
            adjustmentType: CarrierCostAdjustmentType.DisputeCredit,
            carrierInvoiceNumber: "ADJ-FEDEX-9001-A",
            originalCarrierCost: 115.00m,
            adjustedCarrierCost: 112.00m,
            clientFreightDelta: 0m,
            currency: "USD",
            reason: "Late delivery concession credit",
            idempotencyKey: Guid.NewGuid().ToString(),
            adjustedAtUtc: dispatchedAt.AddDays(6));
        invoicedCost.Adjustments.Add(adjustment);

        context.CarrierCostRecords.AddRange(quotedCost, estimatedCost, invoicedCost);

        // 5. Sell-Side Invoiced Client Freight: $150.00
        var billableActivity = new BillableActivity
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ActivityType = BillableActivityType.CarrierFreight,
            Status = BillableActivityStatus.Invoiced,
            SourceType = "Shipment",
            SourceId = shipment.Id,
            SourceReference = shipment.ShipmentNumber,
            Quantity = 1,
            UnitPrice = 150.00m,
            TotalAmount = 150.00m,
            Currency = "USD"
        };
        context.BillableActivities.Add(billableActivity);

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            BillingPeriodId = Guid.NewGuid(),
            InvoiceNumber = "INV-CLIENT-2026-001",
            Status = InvoiceStatus.Paid,
            InvoiceDate = dispatchedAt.AddDays(7),
            DueDate = dispatchedAt.AddDays(37),
            SubtotalAmount = 150.00m,
            TotalAmount = 150.00m
        };
        var line = InvoiceLine.Create(
            lineNumber: 1,
            category: InvoiceChargeCategory.Freight,
            description: "Outbound freight service",
            quantity: 1,
            unitOfMeasure: "SHIPMENT",
            unitPrice: 150.00m,
            totalAmount: 150.00m,
            billableActivityId: billableActivity.Id,
            activityType: BillableActivityType.CarrierFreight,
            facilityId: _facility1Id);
        line.BillableActivity = billableActivity;
        invoice.Lines.Add(line);
        context.Invoices.Add(invoice);

        await context.SaveChangesAsync();

        var service = new FreightReconciliationService(
            context,
            tenantProvider,
            currentUser,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        // ACT
        var result = await service.GetShipmentFinancialReconciliationAsync(shipment.Id);

        // ASSERT
        Assert.NotNull(result);
        Assert.Equal(shipment.Id, result.ShipmentId);
        Assert.Equal("SHIP-RECON-001", result.ShipmentNumber);

        // Buy-Side Separation
        Assert.Equal(100.00m, result.QuotedCarrierCostUsd);
        Assert.Equal(105.00m, result.EstimatedCarrierCostUsd);
        Assert.Equal(100.00m, result.InvoicedCarrierBaseCostUsd);
        Assert.Equal(15.00m, result.InvoicedCarrierAccessorialsUsd);
        Assert.Equal(-3.00m, result.TotalCarrierAdjustmentsUsd);
        Assert.Equal(112.00m, result.NetInvoicedCarrierCostUsd);
        Assert.Equal(112.00m, result.ReconciledCarrierCostUsd);
        Assert.Equal(FreightReconciliationStatus.Reconciled, result.BuySideReconciliationStatus);

        // Sell-Side Separation
        Assert.Equal(150.00m, result.EstimatedClientFreightUsd);
        Assert.Equal(150.00m, result.InvoicedClientFreightUsd);
        Assert.Equal(150.00m, result.ReconciledClientFreightUsd);

        // Traceability and Audit
        Assert.Contains("INV-FEDEX-9001", result.CarrierInvoiceNumbers);
        Assert.Contains("ADJ-FEDEX-9001-A", result.CarrierInvoiceNumbers);
        Assert.Contains("INV-CLIENT-2026-001", result.ClientInvoiceNumbers);
        Assert.Single(result.AdjustmentAuditTrail);
        Assert.Equal(-3.00m, result.AdjustmentAuditTrail[0].CostDelta);
        Assert.Equal("Late delivery concession credit", result.AdjustmentAuditTrail[0].Reason);
    }

    [Fact]
    public async Task Test_03_ZeroDenominator_Handling()
    {
        // ARRANGE
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var service = new FreightReconciliationService(
            context,
            tenantProvider,
            currentUser,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        var emptyStart = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var emptyEnd = new DateTime(2025, 1, 31, 23, 59, 59, DateTimeKind.Utc);

        // ACT - Batch query with 0 shipments
        var batchSummary = await service.GetReconciledShipmentFinancialsBatchAsync(emptyStart, emptyEnd);

        // ASSERT - Batch safely returns zero counts/amounts without division by zero
        Assert.NotNull(batchSummary);
        Assert.Equal(0, batchSummary.TotalShipmentsCount);
        Assert.Equal(0m, batchSummary.TotalQuotedAmountUsd);
        Assert.Equal(0m, batchSummary.TotalEstimatedAmountUsd);
        Assert.Equal(0m, batchSummary.TotalInvoicedCarrierCostUsd);
        Assert.Equal(0m, batchSummary.TotalNetInvoicedCarrierCostUsd);
        Assert.Equal(0m, batchSummary.TotalReconciledCarrierCostUsd);
        Assert.Equal(0m, batchSummary.TotalInvoicedClientFreightUsd);
        Assert.Equal(0m, batchSummary.TotalReconciledClientFreightUsd);
        Assert.Equal(0, batchSummary.DisputedShipmentsCount);
        Assert.Equal(0m, batchSummary.DisputedAmountUsd);
        Assert.Empty(batchSummary.Shipments);

        // ACT & ASSERT - Package allocation with empty package list
        var emptyPackages = service.AllocateShipmentCostsToPackages(100.00m, 150.00m, Array.Empty<(Guid, string, decimal)>());
        Assert.Empty(emptyPackages);

        // ACT & ASSERT - Package allocation with zero weight packages (avoids divide-by-zero, allocates evenly)
        var zeroWeightPackages = new[]
        {
            (Guid.NewGuid(), "PKG-Z1", 0.0m),
            (Guid.NewGuid(), "PKG-Z2", 0.0m)
        };
        var zeroWeightAlloc = service.AllocateShipmentCostsToPackages(100.00m, 150.00m, zeroWeightPackages);
        Assert.Equal(2, zeroWeightAlloc.Count);
        Assert.Equal(50.00m, zeroWeightAlloc[0].AllocatedCarrierCostUsd);
        Assert.Equal(50.00m, zeroWeightAlloc[1].AllocatedCarrierCostUsd);
        Assert.Equal(75.00m, zeroWeightAlloc[0].AllocatedClientFreightUsd);
        Assert.Equal(75.00m, zeroWeightAlloc[1].AllocatedClientFreightUsd);
    }

    [Fact]
    public async Task Test_08_OriginalCharge_PlusSignedAdjustment_NetEvaluation()
    {
        // ARRANGE
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-ADJ-008");
        context.Shipments.Add(shipment);

        // Original invoiced carrier charge: $50.00
        var invoicedCost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierServiceLevel = CarrierServiceLevel.StandardGround,
            CarrierInvoiceNumber = "INV-FEDEX-008",
            BaseTransportationCost = 50.00m,
            FuelSurcharge = 0m,
            InsuranceCost = 0m,
            AccessorialCost = 0m,
            TotalCarrierCost = 50.00m,
            CostAssessedAtUtc = DateTime.UtcNow,
            Status = CarrierCostStatus.Invoiced,
            Currency = "USD"
        };

        // Signed adjustment: -$10.00 credit (original $50.00 adjusted to $40.00)
        var adjustment = CarrierCostAdjustment.Create(
            clientId: _clientAId,
            facilityId: _facility1Id,
            carrierCostRecordId: invoicedCost.Id,
            adjustmentType: CarrierCostAdjustmentType.DisputeCredit,
            carrierInvoiceNumber: "ADJ-FEDEX-008-CR",
            originalCarrierCost: 50.00m,
            adjustedCarrierCost: 40.00m,
            clientFreightDelta: 0m,
            currency: "USD",
            reason: "Over-scale dispute credit from carrier",
            idempotencyKey: Guid.NewGuid().ToString(),
            adjustedAtUtc: DateTime.UtcNow);
        invoicedCost.Adjustments.Add(adjustment);

        context.CarrierCostRecords.Add(invoicedCost);
        await context.SaveChangesAsync();

        var service = new FreightReconciliationService(
            context,
            tenantProvider,
            currentUser,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        // ACT
        var result = await service.GetShipmentFinancialReconciliationAsync(shipment.Id);

        // ASSERT
        Assert.NotNull(result);
        Assert.Equal(50.00m, result.InvoicedCarrierBaseCostUsd);
        Assert.Equal(-10.00m, result.TotalCarrierAdjustmentsUsd);
        // Net = Original ($50) + Signed Adjustment (-$10) = $40.00
        Assert.Equal(40.00m, result.NetInvoicedCarrierCostUsd);
        Assert.Single(result.AdjustmentAuditTrail);
        Assert.Equal(-10.00m, result.AdjustmentAuditTrail[0].CostDelta);
    }

    [Fact]
    public async Task Test_11_TenantAndFacilityIsolation()
    {
        // ARRANGE
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);

        // Shipment 1: Client A at Facility 1 ($100.00)
        var ship1 = CreateShipment(_clientAId, _facility1Id, "SHIP-ISO-A1", "FEDEX", date);
        var cost1 = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = ship1.Id,
            CarrierCode = "FEDEX",
            BaseTransportationCost = 100.00m,
            TotalCarrierCost = 100.00m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced
        };

        // Shipment 2: Client B at Facility 1 ($200.00)
        var ship2 = CreateShipment(_clientBId, _facility1Id, "SHIP-ISO-B1", "FEDEX", date);
        var cost2 = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientBId,
            FacilityId = _facility1Id,
            ShipmentId = ship2.Id,
            CarrierCode = "FEDEX",
            BaseTransportationCost = 200.00m,
            TotalCarrierCost = 200.00m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced
        };

        // Shipment 3: Client A at Facility 2 ($300.00)
        var ship3 = CreateShipment(_clientAId, _facility2Id, "SHIP-ISO-A2", "FEDEX", date);
        var cost3 = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility2Id,
            ShipmentId = ship3.Id,
            CarrierCode = "FEDEX",
            BaseTransportationCost = 300.00m,
            TotalCarrierCost = 300.00m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced
        };

        context.Shipments.AddRange(ship1, ship2, ship3);
        context.CarrierCostRecords.AddRange(cost1, cost2, cost3);
        await context.SaveChangesAsync();

        // User authenticated as Client A restricted to Facility 1
        var clientAUserContext = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            TenantId = _clientAId,
            FacilityId = _facility1Id,
            UserType = UserType.Client
        };

        var clientATenantProvider = new TestTenantProvider
        {
            TenantId = _clientAId,
            BypassTenantFilter = true
        };

        var service = new FreightReconciliationService(
            context,
            clientATenantProvider,
            clientAUserContext,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        // ACT
        var batchResult = await service.GetReconciledShipmentFinancialsBatchAsync(date.AddDays(-1), date.AddDays(1));

        // ASSERT - Client A + Facility 1 must isolate strictly to Shipment 1 ($100), excluding B ($200) and Facility 2 ($300)
        Assert.NotNull(batchResult);
        Assert.Equal(1, batchResult.TotalShipmentsCount);
        Assert.Equal(100.00m, batchResult.TotalNetInvoicedCarrierCostUsd);
        Assert.Single(batchResult.Shipments);
        Assert.Equal("SHIP-ISO-A1", batchResult.Shipments[0].ShipmentNumber);
    }

    [Fact]
    public async Task Test_12_OperationalLedgerInvariance_ZeroMutations()
    {
        // ARRANGE
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 14, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-INVAR-012", "UPS", date);
        context.Shipments.Add(shipment);

        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "UPS",
            BaseTransportationCost = 80.00m,
            FuelSurcharge = 8.00m,
            TotalCarrierCost = 88.00m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced
        };
        var adj = CarrierCostAdjustment.Create(
            clientId: _clientAId,
            facilityId: _facility1Id,
            carrierCostRecordId: cost.Id,
            adjustmentType: CarrierCostAdjustmentType.LateAccessorial,
            carrierInvoiceNumber: "ADJ-UPS-012",
            originalCarrierCost: 88.00m,
            adjustedCarrierCost: 95.00m,
            clientFreightDelta: 0m,
            currency: "USD",
            reason: "Suite address correction surcharge",
            idempotencyKey: Guid.NewGuid().ToString(),
            adjustedAtUtc: date.AddDays(1));
        cost.Adjustments.Add(adj);
        context.CarrierCostRecords.Add(cost);
        await context.SaveChangesAsync();

        // Record exact initial row counts
        int initialShipments = await context.Shipments.CountAsync();
        int initialPackages = await context.Packages.CountAsync();
        int initialCosts = await context.CarrierCostRecords.CountAsync();
        int initialAdjustments = await context.CarrierCostAdjustments.CountAsync();
        int initialInvoices = await context.Invoices.CountAsync();
        int initialInvoiceLines = await context.InvoiceLines.CountAsync();
        int initialActivities = await context.BillableActivities.CountAsync();

        var service = new FreightReconciliationService(
            context,
            tenantProvider,
            currentUser,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        // ACT - Execute read operations
        var singleRecon = await service.GetShipmentFinancialReconciliationAsync(shipment.Id);
        var batchRecon = await service.GetReconciledShipmentFinancialsBatchAsync(date.AddDays(-2), date.AddDays(2));
        var packageAllocations = service.AllocateShipmentCostsToPackages(88.00m, 110.00m, new[] { (Guid.NewGuid(), "PKG-1", 10.0m) });
        var normalizedCurrency = service.NormalizeCurrency(88.00m, "USD", "USD", date);

        // ASSERT - Verify strictly 0 rows mutated or created in operational and billing ledger tables
        Assert.NotNull(singleRecon);
        Assert.NotNull(batchRecon);
        Assert.NotEmpty(packageAllocations);
        Assert.NotNull(normalizedCurrency);

        Assert.Equal(initialShipments, await context.Shipments.CountAsync());
        Assert.Equal(initialPackages, await context.Packages.CountAsync());
        Assert.Equal(initialCosts, await context.CarrierCostRecords.CountAsync());
        Assert.Equal(initialAdjustments, await context.CarrierCostAdjustments.CountAsync());
        Assert.Equal(initialInvoices, await context.Invoices.CountAsync());
        Assert.Equal(initialInvoiceLines, await context.InvoiceLines.CountAsync());
        Assert.Equal(initialActivities, await context.BillableActivities.CountAsync());

        // EF Core ChangeTracker state must be clean
        Assert.False(context.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Test_13_DuplicateSourceRecords_SummaryDuplicationPrevention()
    {
        // ARRANGE
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-DUP-013", "FEDEX", date);
        context.Shipments.Add(shipment);

        // Authoritative CarrierCostRecord: $100.00
        var authoritativeCost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            BaseTransportationCost = 100.00m,
            TotalCarrierCost = 100.00m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced
        };
        context.CarrierCostRecords.Add(authoritativeCost);

        // Analytical Read Model CarrierFreightCostSummary: $100.00
        // (This exists in the database for dashboarding, but MUST NEVER be joined into financial source totals)
        var summaryReadModel = new CarrierFreightCostSummary
        {
            Id = Guid.NewGuid(),
            CarrierCode = "FEDEX",
            FacilityId = _facility1Id,
            ClientId = _clientAId,
            DateUtc = date.Date,
            TimeGrain = TimeGrain.Daily,
            TotalShipmentsBilledCount = 1,
            TotalActualBaseFreightUsd = 100.00m,
            TotalBilledAmountUsd = 100.00m
        };
        context.CarrierFreightCostSummaries.Add(summaryReadModel);

        await context.SaveChangesAsync();

        var service = new FreightReconciliationService(
            context,
            tenantProvider,
            currentUser,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        // ACT
        var batchResult = await service.GetReconciledShipmentFinancialsBatchAsync(date.AddDays(-1), date.AddDays(1));

        // ASSERT - Total Net Invoiced must be exactly $100.00, NOT $200.00
        Assert.NotNull(batchResult);
        Assert.Equal(1, batchResult.TotalShipmentsCount);
        Assert.Equal(100.00m, batchResult.TotalNetInvoicedCarrierCostUsd);
    }

    [Fact]
    public async Task Test_14_MultipleSignedAdjustments_NetAccounting()
    {
        // ARRANGE
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-MULTI-014");
        context.Shipments.Add(shipment);

        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-FEDEX-014",
            BaseTransportationCost = 50.00m,
            TotalCarrierCost = 50.00m,
            CostAssessedAtUtc = DateTime.UtcNow,
            Status = CarrierCostStatus.Invoiced
        };

        // Adjustment 1: Credit of -$10.00 ($50 -> $40)
        var adj1 = CarrierCostAdjustment.Create(
            clientId: _clientAId,
            facilityId: _facility1Id,
            carrierCostRecordId: cost.Id,
            adjustmentType: CarrierCostAdjustmentType.DisputeCredit,
            carrierInvoiceNumber: "ADJ-014-1",
            originalCarrierCost: 50.00m,
            adjustedCarrierCost: 40.00m,
            clientFreightDelta: 0m,
            currency: "USD",
            reason: "Reweigh dispute concession",
            idempotencyKey: Guid.NewGuid().ToString(),
            adjustedAtUtc: DateTime.UtcNow.AddHours(1));

        // Adjustment 2: Debit surcharge of +$5.00 ($40 -> $45)
        var adj2 = CarrierCostAdjustment.Create(
            clientId: _clientAId,
            facilityId: _facility1Id,
            carrierCostRecordId: cost.Id,
            adjustmentType: CarrierCostAdjustmentType.LateAccessorial,
            carrierInvoiceNumber: "ADJ-014-2",
            originalCarrierCost: 40.00m,
            adjustedCarrierCost: 45.00m,
            clientFreightDelta: 0m,
            currency: "USD",
            reason: "Driver wait time at delivery dock",
            idempotencyKey: Guid.NewGuid().ToString(),
            adjustedAtUtc: DateTime.UtcNow.AddHours(2));

        cost.Adjustments.Add(adj1);
        cost.Adjustments.Add(adj2);
        context.CarrierCostRecords.Add(cost);
        await context.SaveChangesAsync();

        var service = new FreightReconciliationService(
            context,
            tenantProvider,
            currentUser,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        // ACT
        var result = await service.GetShipmentFinancialReconciliationAsync(shipment.Id);

        // ASSERT
        Assert.NotNull(result);
        Assert.Equal(50.00m, result.InvoicedCarrierBaseCostUsd);
        Assert.Equal(-5.00m, result.TotalCarrierAdjustmentsUsd); // (-10) + (+5) = -5
        Assert.Equal(45.00m, result.NetInvoicedCarrierCostUsd);   // 50 + (-5) = 45
        Assert.Equal(2, result.AdjustmentAuditTrail.Count);
        Assert.Equal(-10.00m, result.AdjustmentAuditTrail[0].CostDelta);
        Assert.Equal(5.00m, result.AdjustmentAuditTrail[1].CostDelta);
    }

    [Fact]
    public async Task Test_15_AdjustmentReversal_RestoresNetBalance_PreservesAuditHistory()
    {
        // ARRANGE
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-REV-015");
        context.Shipments.Add(shipment);

        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-FEDEX-015",
            BaseTransportationCost = 60.00m,
            TotalCarrierCost = 60.00m,
            CostAssessedAtUtc = DateTime.UtcNow,
            Status = CarrierCostStatus.Invoiced
        };

        // 1. Initial Credit Adjustment: -$20.00 ($60 -> $40)
        var creditAdj = CarrierCostAdjustment.Create(
            clientId: _clientAId,
            facilityId: _facility1Id,
            carrierCostRecordId: cost.Id,
            adjustmentType: CarrierCostAdjustmentType.DisputeCredit,
            carrierInvoiceNumber: "ADJ-015-CR",
            originalCarrierCost: 60.00m,
            adjustedCarrierCost: 40.00m,
            clientFreightDelta: 0m,
            currency: "USD",
            reason: "Erroneous weight concession",
            idempotencyKey: Guid.NewGuid().ToString(),
            adjustedAtUtc: DateTime.UtcNow.AddHours(1));

        // 2. Offsetting Reversal Adjustment: +$20.00 ($40 -> $60)
        var reversalAdj = CarrierCostAdjustment.Create(
            clientId: _clientAId,
            facilityId: _facility1Id,
            carrierCostRecordId: cost.Id,
            adjustmentType: CarrierCostAdjustmentType.RateCorrection,
            carrierInvoiceNumber: "ADJ-015-REV",
            originalCarrierCost: 40.00m,
            adjustedCarrierCost: 60.00m,
            clientFreightDelta: 0m,
            currency: "USD",
            reason: "Reversal of erroneous credit following carrier scale calibration proof",
            idempotencyKey: Guid.NewGuid().ToString(),
            adjustedAtUtc: DateTime.UtcNow.AddHours(3));

        cost.Adjustments.Add(creditAdj);
        cost.Adjustments.Add(reversalAdj);
        context.CarrierCostRecords.Add(cost);
        await context.SaveChangesAsync();

        var service = new FreightReconciliationService(
            context,
            tenantProvider,
            currentUser,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        // ACT
        var result = await service.GetShipmentFinancialReconciliationAsync(shipment.Id);

        // ASSERT
        Assert.NotNull(result);
        Assert.Equal(60.00m, result.InvoicedCarrierBaseCostUsd);
        // Total adjustments net to 0.00m
        Assert.Equal(0.00m, result.TotalCarrierAdjustmentsUsd);
        // Net balance restored exactly to original $60.00
        Assert.Equal(60.00m, result.NetInvoicedCarrierCostUsd);
        // Audit lineage preserved: exactly 2 distinct historical entries
        Assert.Equal(2, result.AdjustmentAuditTrail.Count);
        Assert.Equal(-20.00m, result.AdjustmentAuditTrail[0].CostDelta);
        Assert.Equal(20.00m, result.AdjustmentAuditTrail[1].CostDelta);
        Assert.Equal("Reversal of erroneous credit following carrier scale calibration proof", result.AdjustmentAuditTrail[1].Reason);
    }

    [Fact]
    public async Task Test_16_MultiPackageAllocation_ConservesTotalAndResiduals()
    {
        // ARRANGE
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var service = new FreightReconciliationService(
            context,
            tenantProvider,
            currentUser,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        // Odd cent totals to force fractional cent rounding:
        // Total Carrier Cost: $100.01
        // Total Client Freight: $140.02
        decimal totalCarrierCost = 100.01m;
        decimal totalClientFreight = 140.02m;

        // 3 packages with weights 10kg, 20kg, 30kg (Total = 60kg)
        var p1Id = Guid.NewGuid();
        var p2Id = Guid.NewGuid();
        var p3Id = Guid.NewGuid();

        var packages = new List<(Guid PackageId, string PackageNumber, decimal WeightKg)>
        {
            (p1Id, "PKG-01", 10.0m), // 1/6 = ~16.67%
            (p2Id, "PKG-02", 20.0m), // 2/6 = ~33.33%
            (p3Id, "PKG-03", 30.0m)  // 3/6 = ~50.00% (heaviest)
        };

        // ACT
        var allocations = service.AllocateShipmentCostsToPackages(totalCarrierCost, totalClientFreight, packages);

        // ASSERT
        Assert.Equal(3, allocations.Count);

        // Conservation down to the exact cent
        decimal sumCarrierCost = allocations.Sum(a => a.AllocatedCarrierCostUsd);
        decimal sumClientFreight = allocations.Sum(a => a.AllocatedClientFreightUsd);

        Assert.Equal(totalCarrierCost, sumCarrierCost);
        Assert.Equal(totalClientFreight, sumClientFreight);

        // Heaviest package is PKG-03
        var heaviestAlloc = allocations.First(a => a.PackageNumber == "PKG-03");
        Assert.Equal(30.0m, heaviestAlloc.WeightKg);
        Assert.Equal(0.5000m, heaviestAlloc.WeightRatio);

        // Verify each package has non-zero cost and freight
        foreach (var alloc in allocations)
        {
            Assert.True(alloc.AllocatedCarrierCostUsd > 0m);
            Assert.True(alloc.AllocatedClientFreightUsd > 0m);
        }
    }

    [Fact]
    public async Task Test_22_UnauthorizedTenantOrFacilityAccess_ThrowsForbidden()
    {
        // ARRANGE
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var shipmentClientB = CreateShipment(_clientBId, _facility2Id, "SHIP-CLIENT-B");
        context.Shipments.Add(shipmentClientB);
        await context.SaveChangesAsync();

        // User authenticated as Client A at Facility 1
        var clientAUser = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            TenantId = _clientAId,
            FacilityId = _facility1Id,
            UserType = UserType.Client
        };

        var clientATenantProvider = new TestTenantProvider
        {
            TenantId = _clientAId,
            BypassTenantFilter = true
        };

        var service = new FreightReconciliationService(
            context,
            clientATenantProvider,
            clientAUser,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        var now = DateTime.UtcNow;

        // 1. Calling batch with requested clientId = Client B throws ForbiddenException
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetReconciledShipmentFinancialsBatchAsync(now.AddDays(-1), now.AddDays(1), clientId: _clientBId));

        // 2. Calling batch with requested facilityId = Facility 2 throws ForbiddenException
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetReconciledShipmentFinancialsBatchAsync(now.AddDays(-1), now.AddDays(1), facilityId: _facility2Id));

        // 3. Requesting single shipment financial reconciliation for Client B's shipment throws ForbiddenException
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetShipmentFinancialReconciliationAsync(shipmentClientB.Id));
    }

    [Fact]
    public async Task Test_23_SellSideInvoiceLineUniqueness_And_MultiLineConservation()
    {
        // ARRANGE
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-SELL-23", "FEDEX", date);
        context.Shipments.Add(shipment);

        var carrierCost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            BaseTransportationCost = 100.00m,
            TotalCarrierCost = 100.00m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced
        };
        context.CarrierCostRecords.Add(carrierCost);

        // Activity 1: Base Freight
        var act1 = new BillableActivity
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ActivityType = BillableActivityType.CarrierFreight,
            Status = BillableActivityStatus.Invoiced,
            SourceType = "Shipment",
            SourceId = shipment.Id,
            Quantity = 1,
            UnitPrice = 120.00m,
            TotalAmount = 120.00m,
            Currency = "USD"
        };

        // Activity 2: Fuel Surcharge
        var act2 = new BillableActivity
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ActivityType = BillableActivityType.CarrierFreight,
            Status = BillableActivityStatus.Invoiced,
            SourceType = "Shipment",
            SourceId = shipment.Id,
            Quantity = 1,
            UnitPrice = 30.00m,
            TotalAmount = 30.00m,
            Currency = "USD"
        };

        context.BillableActivities.AddRange(act1, act2);

        // Valid Invoice 1 (Status: Issued) with Line 1: Base Freight $120.00
        var activeInvoice1 = new Invoice
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            BillingPeriodId = Guid.NewGuid(),
            InvoiceNumber = "INV-ACTIVE-023-A",
            Status = InvoiceStatus.Issued,
            InvoiceDate = date.AddDays(5),
            DueDate = date.AddDays(35),
            SubtotalAmount = 120.00m,
            TotalAmount = 120.00m
        };
        var line1 = InvoiceLine.Create(
            lineNumber: 1,
            category: InvoiceChargeCategory.Freight,
            description: "Base freight charge",
            quantity: 1,
            unitOfMeasure: "SHIPMENT",
            unitPrice: 120.00m,
            totalAmount: 120.00m,
            billableActivityId: act1.Id,
            activityType: BillableActivityType.CarrierFreight,
            facilityId: _facility1Id);
        line1.BillableActivity = act1;
        activeInvoice1.Lines.Add(line1);

        // Valid Invoice 2 (Status: Paid) with Line 2: Fuel Surcharge $30.00 across multiple valid invoices
        var activeInvoice2 = new Invoice
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            BillingPeriodId = Guid.NewGuid(),
            InvoiceNumber = "INV-ACTIVE-023-B",
            Status = InvoiceStatus.Paid,
            InvoiceDate = date.AddDays(6),
            DueDate = date.AddDays(36),
            SubtotalAmount = 30.00m,
            TotalAmount = 30.00m
        };
        var line2 = InvoiceLine.Create(
            lineNumber: 1,
            category: InvoiceChargeCategory.Freight,
            description: "Carrier fuel surcharge",
            quantity: 1,
            unitOfMeasure: "SHIPMENT",
            unitPrice: 30.00m,
            totalAmount: 30.00m,
            billableActivityId: act2.Id,
            activityType: BillableActivityType.CarrierFreight,
            facilityId: _facility1Id);
        line2.BillableActivity = act2;
        activeInvoice2.Lines.Add(line2);

        // Activity 3: Duplicate reference pointing to the exact same Line 1
        var act3Duplicate = new BillableActivity
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ActivityType = BillableActivityType.CarrierFreight,
            Status = BillableActivityStatus.Invoiced,
            SourceType = "Shipment",
            SourceId = shipment.Id,
            InvoiceLineId = line1.Id,
            Quantity = 1,
            UnitPrice = 120.00m,
            TotalAmount = 120.00m,
            Currency = "USD"
        };
        context.BillableActivities.Add(act3Duplicate);

        // Non-Qualifying Invoice 1: Status = Draft ($40 line) - Must be excluded
        var draftInvoice = new Invoice
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            BillingPeriodId = Guid.NewGuid(),
            InvoiceNumber = "INV-DRAFT-023",
            Status = InvoiceStatus.Draft,
            InvoiceDate = date.AddDays(2),
            DueDate = date.AddDays(32),
            SubtotalAmount = 40.00m,
            TotalAmount = 40.00m
        };
        var draftLine = InvoiceLine.Create(
            lineNumber: 1,
            category: InvoiceChargeCategory.Freight,
            description: "Unapproved draft freight charge",
            quantity: 1,
            unitOfMeasure: "SHIPMENT",
            unitPrice: 40.00m,
            totalAmount: 40.00m,
            facilityId: _facility1Id);
        draftInvoice.Lines.Add(draftLine);

        // Non-Qualifying Invoice 2: Status = Voided ($50 line) - Must be excluded
        var voidedInvoice = new Invoice
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            BillingPeriodId = Guid.NewGuid(),
            InvoiceNumber = "INV-VOIDED-023",
            Status = InvoiceStatus.Voided,
            InvoiceDate = date.AddDays(1),
            DueDate = date.AddDays(31),
            SubtotalAmount = 50.00m,
            TotalAmount = 50.00m
        };
        var voidedLine = InvoiceLine.Create(
            lineNumber: 1,
            category: InvoiceChargeCategory.Freight,
            description: "Cancelled erroneous freight quote",
            quantity: 1,
            unitOfMeasure: "SHIPMENT",
            unitPrice: 50.00m,
            totalAmount: 50.00m,
            facilityId: _facility1Id);
        voidedInvoice.Lines.Add(voidedLine);

        // Non-Qualifying Line 3: Invoice is null ($60 line) - Must be excluded
        var nullInvoiceLine = InvoiceLine.Create(
            lineNumber: 1,
            category: InvoiceChargeCategory.Freight,
            description: "Orphaned unassigned invoice line",
            quantity: 1,
            unitOfMeasure: "SHIPMENT",
            unitPrice: 60.00m,
            totalAmount: 60.00m,
            billableActivityId: act1.Id,
            facilityId: _facility1Id);

        context.Invoices.AddRange(activeInvoice1, activeInvoice2, draftInvoice, voidedInvoice);
        context.InvoiceLines.Add(nullInvoiceLine);
        await context.SaveChangesAsync();

        var service = new FreightReconciliationService(
            context,
            tenantProvider,
            currentUser,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        // ACT
        var result = await service.GetShipmentFinancialReconciliationAsync(shipment.Id);

        // ASSERT
        Assert.NotNull(result);
        // Multi-line conservation across multiple valid invoices: Line 1 ($120) + Line 2 ($30) = $150.00 exactly
        Assert.Equal(150.00m, result.InvoicedClientFreightUsd);
        // Duplicate activity reference to Line 1 did NOT double-count ($120 was not added again)
        // Draft ($40), Voided ($50), and null-invoice ($60) lines are strictly excluded
        Assert.Contains("INV-ACTIVE-023-A", result.ClientInvoiceNumbers);
        Assert.Contains("INV-ACTIVE-023-B", result.ClientInvoiceNumbers);
        Assert.DoesNotContain("INV-DRAFT-023", result.ClientInvoiceNumbers);
        Assert.DoesNotContain("INV-VOIDED-023", result.ClientInvoiceNumbers);
    }

    [Fact]
    public async Task Test_24_EqualWeightPackageAllocation_DeterministicTieBreak()
    {
        // ARRANGE
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var service = new FreightReconciliationService(
            context,
            tenantProvider,
            currentUser,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        // Total Carrier Cost: $100.01 (1 cent residual to allocate)
        // Total Client Freight: $150.01 (1 cent residual to allocate)
        decimal totalCarrierCost = 100.01m;
        decimal totalClientFreight = 150.01m;

        // Two packages with IDENTICAL weights: 10.0kg each
        var pkgAId = Guid.NewGuid();
        var pkgBId = Guid.NewGuid();

        var pkgA = (PackageId: pkgAId, PackageNumber: "PKG-001", WeightKg: 10.0m);
        var pkgB = (PackageId: pkgBId, PackageNumber: "PKG-002", WeightKg: 10.0m);

        // ACT - Run 1: Passed in order [A, B]
        var list1 = new List<(Guid, string, decimal)> { pkgA, pkgB };
        var allocations1 = service.AllocateShipmentCostsToPackages(totalCarrierCost, totalClientFreight, list1);

        // ACT - Run 2: Passed in REVERSE order [B, A]
        var list2 = new List<(Guid, string, decimal)> { pkgB, pkgA };
        var allocations2 = service.AllocateShipmentCostsToPackages(totalCarrierCost, totalClientFreight, list2);

        // ASSERT - Verify deterministic tie-break allocates residual cent identically in BOTH runs
        var alloc1A = allocations1.First(a => a.PackageNumber == "PKG-001");
        var alloc1B = allocations1.First(a => a.PackageNumber == "PKG-002");

        var alloc2A = allocations2.First(a => a.PackageNumber == "PKG-001");
        var alloc2B = allocations2.First(a => a.PackageNumber == "PKG-002");

        // Allocation to PKG-001 must be identical across both runs regardless of input list ordering
        Assert.Equal(alloc1A.AllocatedCarrierCostUsd, alloc2A.AllocatedCarrierCostUsd);
        Assert.Equal(alloc1A.AllocatedClientFreightUsd, alloc2A.AllocatedClientFreightUsd);

        // Allocation to PKG-002 must be identical across both runs
        Assert.Equal(alloc1B.AllocatedCarrierCostUsd, alloc2B.AllocatedCarrierCostUsd);
        Assert.Equal(alloc1B.AllocatedClientFreightUsd, alloc2B.AllocatedClientFreightUsd);

        // Strict conservation down to the exact cent
        Assert.Equal(totalCarrierCost, allocations1.Sum(a => a.AllocatedCarrierCostUsd));
        Assert.Equal(totalCarrierCost, allocations2.Sum(a => a.AllocatedCarrierCostUsd));
        Assert.Equal(totalClientFreight, allocations1.Sum(a => a.AllocatedClientFreightUsd));
        Assert.Equal(totalClientFreight, allocations2.Sum(a => a.AllocatedClientFreightUsd));
    }

    [Fact]
    public void Test_25_ReproducibleCurrencyConversion_PreservesHistoricalRateAndRounding()
    {
        // ARRANGE
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        using var context = TestDbContextFactory.Create(tenantProvider, currentUser);

        var service = new FreightReconciliationService(
            context,
            tenantProvider,
            currentUser,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        var effectiveDate = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
        decimal originalCadAmount = 150.00m;

        // ACT: Resolves historical rate strictly via IFxRateProvider without bypass backdoor
        var evidence = service.NormalizeCurrency(
            originalCadAmount,
            "CAD",
            "USD",
            effectiveDate);

        // ASSERT: Official FRB H.10 fixing on Nov 4, 2026 is 1.3973 CAD/USD -> Reciprocal Multiplier 0.7157
        Assert.NotNull(evidence);
        Assert.Equal("CAD", evidence.OriginalCurrency);
        Assert.Equal(150.00m, evidence.OriginalAmount);
        Assert.Equal("USD", evidence.TargetCurrency);
        Assert.Equal("FROM_CAD_TO_USD", evidence.RateDirection);
        Assert.Equal(0.7157m, evidence.ExchangeRate);
        Assert.Equal(effectiveDate, evidence.EffectiveDateUtc);
        Assert.Equal("FederalReserveBoard_H10", evidence.RateSource);
        Assert.Equal("FRB-20261109", evidence.RateVersion);
        Assert.Equal("H10/H10/RXI_N.B.CA", evidence.BenchmarkSeriesId);
        Assert.StartsWith("SHA256:", evidence.IntegrityHash);
        Assert.Equal(4, evidence.RoundingDecimals);
        Assert.Equal("MidpointRounding.AwayFromZero", evidence.RoundingMode);
        Assert.Contains("MidpointRounding.AwayFromZero, 4 Decimals", evidence.RoundingPolicy);

        // Expected USD = 150.00 * 0.7157 = 107.3550m
        Assert.Equal(107.3550m, evidence.ConvertedAmountUsd);

        // Verify Immutability: An independent dynamic rate lookup does NOT alter historical evidence
        var secondEvidence = service.NormalizeCurrency(
            originalCadAmount,
            "CAD",
            "USD",
            effectiveDate);

        Assert.Equal(107.3550m, secondEvidence.ConvertedAmountUsd);
        Assert.Equal(evidence.IntegrityHash, secondEvidence.IntegrityHash);
        Assert.Equal(evidence.RateVersion, secondEvidence.RateVersion);
    }

    [Fact]
    public async Task Test_26_MixedCurrencyCarrierCharges_NormalizeBeforeAggregation()
    {
        // ARRANGE
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-MIX-26", "FEDEX", date);
        context.Shipments.Add(shipment);

        // Charge 1: 100.00 CAD on Nov 4, 2026 (FRB H.10 quote 1.3973 -> multiplier 0.7157 = 71.57 USD)
        var costCad = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-CAD-01",
            TrackingNumber = "TRK-CAD-01",
            BaseTransportationCost = 100.00m,
            TotalCarrierCost = 100.00m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced,
            Currency = "CAD"
        };

        // Charge 2: 100.00 USD (at 1.0000 = 100.00 USD)
        var costUsd = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-USD-02",
            TrackingNumber = "TRK-USD-02",
            BaseTransportationCost = 100.00m,
            TotalCarrierCost = 100.00m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced,
            Currency = "USD"
        };

        context.CarrierCostRecords.AddRange(costCad, costUsd);
        await context.SaveChangesAsync();

        var service = new FreightReconciliationService(
            context,
            tenantProvider,
            currentUser,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        // ACT
        var result = await service.GetShipmentFinancialReconciliationAsync(shipment.Id);

        // ASSERT: 100 CAD ($71.5700 USD via FRB H.10 reciprocal multiplier) + 100 USD = $171.5700 USD (not $200.00)
        Assert.NotNull(result);
        Assert.Equal(171.5700m, result.NetInvoicedCarrierCostUsd);
        Assert.Equal(171.5700m, result.InvoicedCarrierBaseCostUsd);
    }

    [Fact]
    public async Task Test_27_AdjustmentCurrency_MatchesParentChargeCurrency()
    {
        // ARRANGE
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-ADJ-27", "FEDEX", date);
        context.Shipments.Add(shipment);

        // Parent Carrier Charge: 100.00 CAD on Nov 4 (FRB quote 1.3973 -> multiplier 0.7157 = 71.57 USD)
        var costCad = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-CAD-27",
            TrackingNumber = "TRK-CAD-27",
            BaseTransportationCost = 100.00m,
            TotalCarrierCost = 100.00m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced,
            Currency = "CAD"
        };

        // Child Adjustment: -10.00 CAD on Nov 5 (FRB quote 1.3980 -> multiplier 0.7153 = -7.1530 USD)
        var adj = CarrierCostAdjustment.Create(
            clientId: _clientAId,
            facilityId: _facility1Id,
            carrierCostRecordId: costCad.Id,
            adjustmentType: CarrierCostAdjustmentType.DisputeCredit,
            carrierInvoiceNumber: "ADJ-CAD-27",
            originalCarrierCost: 100.00m,
            adjustedCarrierCost: 90.00m,
            clientFreightDelta: 0m,
            currency: "CAD",
            reason: "CAD billing dispute concession",
            idempotencyKey: Guid.NewGuid().ToString(),
            adjustedAtUtc: date.AddDays(1));
        costCad.Adjustments.Add(adj);

        context.CarrierCostRecords.Add(costCad);
        await context.SaveChangesAsync();

        var service = new FreightReconciliationService(
            context,
            tenantProvider,
            currentUser,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        // ACT
        var result = await service.GetShipmentFinancialReconciliationAsync(shipment.Id);

        // ASSERT: Base on Nov 4 (Rate 0.7157 -> $71.5700 USD) + Adjustment on Nov 5 (Rate 0.7153 -> -$7.1530 USD) = $64.4170 USD
        Assert.NotNull(result);
        Assert.Equal(71.5700m, result.InvoicedCarrierBaseCostUsd);
        Assert.Equal(-7.1530m, result.TotalCarrierAdjustmentsUsd);
        Assert.Equal(64.4170m, result.NetInvoicedCarrierCostUsd);
    }

    [Fact]
    public async Task Test_28_SellSideCurrency_NormalizesInvoiceLines()
    {
        // ARRANGE
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-SELL-28", "FEDEX", date);
        context.Shipments.Add(shipment);

        var act1 = new BillableActivity
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ActivityType = BillableActivityType.CarrierFreight,
            Status = BillableActivityStatus.Invoiced,
            SourceType = "Shipment",
            SourceId = shipment.Id,
            Quantity = 1,
            UnitPrice = 100.00m,
            TotalAmount = 100.00m,
            Currency = "CAD"
        };

        var act2 = new BillableActivity
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ActivityType = BillableActivityType.CarrierFreight,
            Status = BillableActivityStatus.Invoiced,
            SourceType = "Shipment",
            SourceId = shipment.Id,
            Quantity = 1,
            UnitPrice = 50.00m,
            TotalAmount = 50.00m,
            Currency = "EUR"
        };
        context.BillableActivities.AddRange(act1, act2);

        // Invoice 1: 100.00 CAD on 2026-11-06 (Official FRB H.10 quote 1.4106 -> multiplier 0.7089 = 70.8900 USD)
        var invCad = new Invoice
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            BillingPeriodId = Guid.NewGuid(),
            InvoiceNumber = "INV-CAD-28",
            Status = InvoiceStatus.Issued,
            InvoiceDate = new DateTime(2026, 11, 6, 10, 0, 0, DateTimeKind.Utc),
            DueDate = new DateTime(2026, 12, 6, 10, 0, 0, DateTimeKind.Utc),
            Currency = "CAD",
            SubtotalAmount = 100.00m,
            TotalAmount = 100.00m
        };
        var line1 = InvoiceLine.Create(
            lineNumber: 1,
            category: InvoiceChargeCategory.Freight,
            description: "Canadian line haul",
            quantity: 1,
            unitOfMeasure: "SHIPMENT",
            unitPrice: 100.00m,
            totalAmount: 100.00m,
            billableActivityId: act1.Id,
            activityType: BillableActivityType.CarrierFreight,
            facilityId: _facility1Id);
        line1.BillableActivity = act1;
        invCad.Lines.Add(line1);

        // Invoice 2: 50.00 EUR on 2026-11-02 (Official ECB reference rate 1.1216 -> 56.0800 USD)
        var invEur = new Invoice
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            BillingPeriodId = Guid.NewGuid(),
            InvoiceNumber = "INV-EUR-28",
            Status = InvoiceStatus.Paid,
            InvoiceDate = new DateTime(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc),
            DueDate = new DateTime(2026, 12, 2, 10, 0, 0, DateTimeKind.Utc),
            Currency = "EUR",
            SubtotalAmount = 50.00m,
            TotalAmount = 50.00m
        };
        var line2 = InvoiceLine.Create(
            lineNumber: 1,
            category: InvoiceChargeCategory.Freight,
            description: "European connection fee",
            quantity: 1,
            unitOfMeasure: "SHIPMENT",
            unitPrice: 50.00m,
            totalAmount: 50.00m,
            billableActivityId: act2.Id,
            activityType: BillableActivityType.CarrierFreight,
            facilityId: _facility1Id);
        line2.BillableActivity = act2;
        invEur.Lines.Add(line2);

        context.Invoices.AddRange(invCad, invEur);
        await context.SaveChangesAsync();

        var service = new FreightReconciliationService(
            context,
            tenantProvider,
            currentUser,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        // ACT
        var result = await service.GetShipmentFinancialReconciliationAsync(shipment.Id);

        // ASSERT: 100 CAD ($70.8900) + 50 EUR ($56.0800) = $126.9700 USD
        Assert.NotNull(result);
        Assert.Equal(126.9700m, result.InvoicedClientFreightUsd);
    }

    [Fact]
    public async Task Test_29_UnsupportedExchangeRate_FailsSafely()
    {
        // ARRANGE
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-UNSUPP-29", "FEDEX", date);
        context.Shipments.Add(shipment);

        // Charge in JPY without configured rate
        var costJpy = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-JPY-29",
            BaseTransportationCost = 10000m,
            TotalCarrierCost = 10000m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced,
            Currency = "JPY"
        };
        context.CarrierCostRecords.Add(costJpy);
        await context.SaveChangesAsync();

        var service = new FreightReconciliationService(
            context,
            tenantProvider,
            currentUser,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        // ACT & ASSERT: Must throw InvalidOperationException rather than silently using 1.0
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetShipmentFinancialReconciliationAsync(shipment.Id));
    }

    [Fact]
    public async Task Test_30_CarrierInvoiceChargeIdentity_PreventsDuplicateCost()
    {
        // ARRANGE
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-DUP-30", "UPS", date);
        context.Shipments.Add(shipment);

        // Ingest duplicate carrier invoice records for the exact same invoice charge
        var record1 = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "UPS",
            CarrierInvoiceNumber = "INV-UPS-DUP-100",
            TrackingNumber = "TRK-UPS-DUP-1",
            BaseTransportationCost = 100.00m,
            TotalCarrierCost = 100.00m,
            CostAssessedAtUtc = date.AddMinutes(-5),
            Status = CarrierCostStatus.Invoiced,
            Currency = "USD"
        };

        var record2 = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "UPS",
            CarrierInvoiceNumber = "INV-UPS-DUP-100",
            TrackingNumber = "TRK-UPS-DUP-1",
            BaseTransportationCost = 100.00m,
            TotalCarrierCost = 100.00m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced,
            Currency = "USD"
        };

        context.CarrierCostRecords.AddRange(record1, record2);
        await context.SaveChangesAsync();

        var service = new FreightReconciliationService(
            context,
            tenantProvider,
            currentUser,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        // ACT
        var result = await service.GetShipmentFinancialReconciliationAsync(shipment.Id);

        // ASSERT: Must deduplicate to $100.00 (NOT $200.00)
        Assert.NotNull(result);
        Assert.Equal(100.00m, result.NetInvoicedCarrierCostUsd);
        Assert.Equal(100.00m, result.InvoicedCarrierBaseCostUsd);
    }

    [Fact]
    public async Task Test_31_AdjustmentsOnNonInvoicedRecords_Excluded()
    {
        // ARRANGE
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-EST-ADJ-31", "FEDEX", date);
        context.Shipments.Add(shipment);

        // Estimated record (Quote) with adjustment -$10.00
        var quote = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            BaseTransportationCost = 90.00m,
            TotalCarrierCost = 90.00m,
            CostAssessedAtUtc = date.AddDays(-2),
            Status = CarrierCostStatus.Estimated
        };
        var quoteAdj = CarrierCostAdjustment.Create(
            clientId: _clientAId,
            facilityId: _facility1Id,
            carrierCostRecordId: quote.Id,
            adjustmentType: CarrierCostAdjustmentType.RateCorrection,
            carrierInvoiceNumber: "ADJ-QUOTE",
            originalCarrierCost: 90.00m,
            adjustedCarrierCost: 80.00m,
            clientFreightDelta: 0m,
            currency: "USD",
            reason: "Quote discount test",
            idempotencyKey: Guid.NewGuid().ToString(),
            adjustedAtUtc: date.AddDays(-2));
        quote.Adjustments.Add(quoteAdj);

        // Invoiced record with Base = $100.00, Fuel = 0, no adjustments
        var invoiced = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-FEDEX-31",
            BaseTransportationCost = 100.00m,
            TotalCarrierCost = 100.00m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced
        };

        context.CarrierCostRecords.AddRange(quote, invoiced);
        await context.SaveChangesAsync();

        var service = new FreightReconciliationService(
            context,
            tenantProvider,
            currentUser,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        // ACT
        var result = await service.GetShipmentFinancialReconciliationAsync(shipment.Id);

        // ASSERT: Invoiced total is strictly $100.00 (the -$10 quote adjustment is excluded)
        Assert.NotNull(result);
        Assert.Equal(100.00m, result.NetInvoicedCarrierCostUsd);
        Assert.Equal(0.00m, result.TotalCarrierAdjustmentsUsd);
    }

    [Fact]
    public async Task Test_32_MissingTenantContext_DeniesUnscopedAccess()
    {
        // ARRANGE
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var unauthContext = new TestCurrentUserContext { UserId = null }; // IsAuthenticated = false
        var (context, _) = await CreateSeededContextAsync(tenantProvider, unauthContext);

        var date = new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-SEC-32", "FEDEX", date);
        context.Shipments.Add(shipment);
        await context.SaveChangesAsync();

        var service = new FreightReconciliationService(
            context,
            tenantProvider,
            unauthContext,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        // ACT & ASSERT: Unauthenticated call throws UnauthorizedException
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.GetShipmentFinancialReconciliationAsync(shipment.Id));

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.GetReconciledShipmentFinancialsBatchAsync(date.AddDays(-1), date.AddDays(1)));

        // Authenticated client user missing TenantId throws ForbiddenException
        var missingTenantUser = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            TenantId = null,
            UserType = UserType.Client
        };
        var clientTenantProvider = new TestTenantProvider { TenantId = null };

        var clientService = new FreightReconciliationService(
            context,
            clientTenantProvider,
            missingTenantUser,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            clientService.GetReconciledShipmentFinancialsBatchAsync(date.AddDays(-1), date.AddDays(1)));
    }

    [Fact]
    public async Task Test_33_BuySideReconciled_SellSideNotReconciled()
    {
        // ARRANGE
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-SEP-33", "FEDEX", date);
        context.Shipments.Add(shipment);

        // Buy-Side: Reconciled carrier invoice ($100.00)
        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-FEDEX-33",
            BaseTransportationCost = 100.00m,
            TotalCarrierCost = 100.00m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Reconciled
        };
        context.CarrierCostRecords.Add(cost);

        // Sell-Side: Issued client invoice ($150.00), NOT Reconciled/Paid yet
        var act = new BillableActivity
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ActivityType = BillableActivityType.CarrierFreight,
            Status = BillableActivityStatus.Invoiced,
            SourceType = "Shipment",
            SourceId = shipment.Id,
            Quantity = 1,
            UnitPrice = 150.00m,
            TotalAmount = 150.00m
        };
        context.BillableActivities.Add(act);

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            BillingPeriodId = Guid.NewGuid(),
            InvoiceNumber = "INV-CLIENT-33",
            Status = InvoiceStatus.Issued, // Issued, not Paid
            InvoiceDate = date.AddDays(5),
            DueDate = date.AddDays(35),
            SubtotalAmount = 150.00m,
            TotalAmount = 150.00m
        };
        var line = InvoiceLine.Create(
            lineNumber: 1,
            category: InvoiceChargeCategory.Freight,
            description: "Outbound freight",
            quantity: 1,
            unitOfMeasure: "SHIPMENT",
            unitPrice: 150.00m,
            totalAmount: 150.00m,
            billableActivityId: act.Id,
            activityType: BillableActivityType.CarrierFreight,
            facilityId: _facility1Id);
        line.BillableActivity = act;
        invoice.Lines.Add(line);
        context.Invoices.Add(invoice);

        await context.SaveChangesAsync();

        var service = new FreightReconciliationService(
            context,
            tenantProvider,
            currentUser,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        // ACT
        var result = await service.GetShipmentFinancialReconciliationAsync(shipment.Id);

        // ASSERT: Buy-side is Reconciled, sell-side is Invoiced (NOT Reconciled)
        Assert.NotNull(result);
        Assert.Equal(FreightReconciliationStatus.Reconciled, result.BuySideReconciliationStatus);
        Assert.Equal(100.00m, result.ReconciledCarrierCostUsd);

        Assert.Equal(FreightReconciliationStatus.Invoiced, result.SellSideReconciliationStatus);
        Assert.Null(result.ReconciledClientFreightUsd); // ReconciledClientFreightUsd is null because sell-side is not reconciled
        Assert.Equal(150.00m, result.InvoicedClientFreightUsd);
    }

    [Fact]
    public void Test_34_MissingPackageWeight_UsesApprovedPolicy()
    {
        // ARRANGE
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        using var context = TestDbContextFactory.Create(tenantProvider, currentUser);

        var service = new FreightReconciliationService(
            context,
            tenantProvider,
            currentUser,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        decimal totalCarrierCost = 100.01m;
        decimal totalClientFreight = 150.01m;

        // Two packages with MISSING/ZERO weights
        var p1 = (PackageId: Guid.NewGuid(), PackageNumber: "PKG-01", WeightKg: 0m);
        var p2 = (PackageId: Guid.NewGuid(), PackageNumber: "PKG-02", WeightKg: 0m);

        // ACT
        var allocations = service.AllocateShipmentCostsToPackages(
            totalCarrierCost,
            totalClientFreight,
            new[] { p1, p2 });

        // ASSERT: Equal fallback allocation applied with explicit indicator
        Assert.Equal(2, allocations.Count);
        Assert.All(allocations, a => Assert.True(a.IsFallbackEqualAllocationApplied));

        // Total cost conserved down to the exact cent
        decimal sumCarrierCost = allocations.Sum(a => a.AllocatedCarrierCostUsd);
        decimal sumClientFreight = allocations.Sum(a => a.AllocatedClientFreightUsd);

        Assert.Equal(totalCarrierCost, sumCarrierCost);
        Assert.Equal(totalClientFreight, sumClientFreight);

        // Package PKG-01 receives tie-break deduction of 1 cent excess: $50.00, PKG-02 has $50.01
        var alloc1 = allocations.First(a => a.PackageNumber == "PKG-01");
        var alloc2 = allocations.First(a => a.PackageNumber == "PKG-02");

        Assert.Equal(50.00m, alloc1.AllocatedCarrierCostUsd);
        Assert.Equal(50.01m, alloc2.AllocatedCarrierCostUsd);
    }

    [Fact]
    public async Task Test_35_UnInvoicedShipment_TenderEstimateDoesNotEnterInvoicedFields()
    {
        // ARRANGE
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-UNINVOICED-35", "FEDEX", date);
        context.Shipments.Add(shipment);

        // Tender estimate record (ActualConfirmed), $105.00
        var tenderEstimate = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierServiceLevel = CarrierServiceLevel.StandardGround,
            BaseTransportationCost = 95.00m,
            FuelSurcharge = 10.00m,
            TotalCarrierCost = 105.00m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.ActualConfirmed,
            Currency = "USD"
        };
        context.CarrierCostRecords.Add(tenderEstimate);
        await context.SaveChangesAsync();

        var service = new FreightReconciliationService(
            context,
            tenantProvider,
            currentUser,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        // ACT
        var result = await service.GetShipmentFinancialReconciliationAsync(shipment.Id);
        var batchResult = await service.GetReconciledShipmentFinancialsBatchAsync(date.AddDays(-1), date.AddDays(1));

        // ASSERT: Single shipment financials
        Assert.NotNull(result);
        Assert.Equal(105.00m, result.EstimatedCarrierCostUsd);
        Assert.Equal(0.00m, result.InvoicedCarrierBaseCostUsd);
        Assert.Equal(0.00m, result.InvoicedCarrierAccessorialsUsd);
        Assert.Equal(0.00m, result.TotalCarrierAdjustmentsUsd);
        Assert.Equal(0.00m, result.NetInvoicedCarrierCostUsd);
        Assert.Null(result.ReconciledCarrierCostUsd);
        Assert.Equal(FreightReconciliationStatus.Estimated, result.BuySideReconciliationStatus);

        // ASSERT: Batch financials must NOT include un-invoiced estimates in invoiced spend
        Assert.NotNull(batchResult);
        Assert.Equal(1, batchResult.TotalShipmentsCount);
        Assert.Equal(105.00m, batchResult.TotalEstimatedAmountUsd);
        Assert.Equal(0.00m, batchResult.TotalInvoicedCarrierCostUsd);
        Assert.Equal(0.00m, batchResult.TotalNetInvoicedCarrierCostUsd);
    }

    [Fact]
    public async Task Test_36_HistoricalFxRate_ResolvesBasedOnEffectiveDate()
    {
        // ARRANGE
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var fxProvider = new FxRateProvider();
        var service = new FreightReconciliationService(
            context,
            tenantProvider,
            currentUser,
            fxProvider,
            NullLogger<FreightReconciliationService>.Instance);

        // Date 1: 2026-10-26 (Official FRB H.10: Quote 1.4140 CAD/USD -> Reciprocal Multiplier 0.7072 USD/CAD)
        var date1 = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
        var ship1 = CreateShipment(_clientAId, _facility1Id, "SHIP-FX-OCT-26", "FEDEX", date1);
        var cost1 = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = ship1.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-CAD-1",
            BaseTransportationCost = 100.00m,
            TotalCarrierCost = 100.00m,
            CostAssessedAtUtc = date1,
            Status = CarrierCostStatus.Invoiced,
            Currency = "CAD"
        };

        // Date 2: 2026-11-04 (Official FRB H.10: Quote 1.3973 CAD/USD -> Reciprocal Multiplier 0.7157 USD/CAD)
        var date2 = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
        var ship2 = CreateShipment(_clientAId, _facility1Id, "SHIP-FX-NOV-04", "FEDEX", date2);
        var cost2 = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = ship2.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-CAD-2",
            BaseTransportationCost = 100.00m,
            TotalCarrierCost = 100.00m,
            CostAssessedAtUtc = date2,
            Status = CarrierCostStatus.Invoiced,
            Currency = "CAD"
        };

        // Date 3: 2026-11-03 (Official ECB reference rate 1.1179 USD/EUR)
        var date3 = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
        var ship3 = CreateShipment(_clientAId, _facility1Id, "SHIP-FX-EUR-03", "FEDEX", date3);
        var cost3 = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = ship3.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-EUR-3",
            BaseTransportationCost = 100.00m,
            TotalCarrierCost = 100.00m,
            CostAssessedAtUtc = date3,
            Status = CarrierCostStatus.Invoiced,
            Currency = "EUR"
        };

        context.Shipments.AddRange(ship1, ship2, ship3);
        context.CarrierCostRecords.AddRange(cost1, cost2, cost3);
        await context.SaveChangesAsync();

        // ACT
        var res1 = await service.GetShipmentFinancialReconciliationAsync(ship1.Id);
        var res2 = await service.GetShipmentFinancialReconciliationAsync(ship2.Id);
        var res3 = await service.GetShipmentFinancialReconciliationAsync(ship3.Id);

        // ASSERT: Rates and versions match official central bank historical observations exactly
        Assert.Equal(70.72m, res1.InvoicedCarrierBaseCostUsd);
        var audit1 = res1.ConversionEvidenceAuditTrail.First(e => e.OriginalCurrency == "CAD");
        Assert.Equal("FRB-20261102", audit1.RateVersion);
        Assert.Equal("FederalReserveBoard_H10", audit1.RateSource);
        Assert.Equal("FROM_CAD_TO_USD", audit1.RateDirection);
        Assert.Equal("H10/H10/RXI_N.B.CA", audit1.BenchmarkSeriesId);
        Assert.StartsWith("SHA256:", audit1.IntegrityHash);
        Assert.NotEqual(default, audit1.PublishedAtUtc);

        Assert.Equal(71.57m, res2.InvoicedCarrierBaseCostUsd);
        var audit2 = res2.ConversionEvidenceAuditTrail.First(e => e.OriginalCurrency == "CAD");
        Assert.Equal("FRB-20261109", audit2.RateVersion);
        Assert.Equal("H10/H10/RXI_N.B.CA", audit2.BenchmarkSeriesId);

        Assert.Equal(111.79m, res3.InvoicedCarrierBaseCostUsd);
        var audit3 = res3.ConversionEvidenceAuditTrail.First(e => e.OriginalCurrency == "EUR");
        Assert.Equal("ECB-20261103", audit3.RateVersion);
        Assert.Equal("EuropeanCentralBank_Reference", audit3.RateSource);
        Assert.Equal("EXR.D.EUR.USD.SP00.A", audit3.BenchmarkSeriesId);

        // Cryptographic integrity verification via IFxRateProvider
        Assert.True(fxProvider.VerifyBulletinIntegrity("FRB-20261109", out var computedHash));
        Assert.StartsWith("SHA256:", computedHash);

        // Premature CAD conversion for week of Nov 9-13 (exceeds latest published observation end)
        var prematureEx = Assert.Throws<InvalidOperationException>(() =>
            fxProvider.GetHistoricalExchangeRate("CAD", "USD", new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc), out _, out _));
        Assert.Contains("exceeds published bulletin fixing schedule", prematureEx.Message);
        Assert.Contains("Future rates cannot be used before publication", prematureEx.Message);

        // Preceding earliest available central bank observation (< 2026-10-26)
        var preHistoryEx = Assert.Throws<InvalidOperationException>(() =>
            fxProvider.GetHistoricalExchangeRate("CAD", "USD", new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc), out _, out _));
        Assert.Contains("precedes earliest historical observation available", preHistoryEx.Message);

        // Unsupported currency pair throws InvalidOperationException
        var ex = Assert.Throws<InvalidOperationException>(() =>
            fxProvider.GetHistoricalExchangeRate("JPY", "USD", new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc), out _, out _));
        Assert.Contains("Unsupported currency conversion from 'JPY' to 'USD'", ex.Message);
    }

    [Fact]
    public async Task Test_37_StaffWithoutFacilityOrCrossFacilityPermission_ThrowsForbidden()
    {
        // ARRANGE
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        // Staff user with NO FacilityId and NO CrossFacility permission
        var unassignedStaffContext = new TestCurrentUserContext
        {
            UserId = _staffUserId,
            UserType = UserType.Staff,
            FacilityId = null,
            PermissionsList = []
        };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, unassignedStaffContext);

        var date = new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-FORBID-37", "FEDEX", date);
        context.Shipments.Add(shipment);
        await context.SaveChangesAsync();

        var service = new FreightReconciliationService(
            context,
            tenantProvider,
            unassignedStaffContext,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        // ACT & ASSERT: Single query throws ForbiddenException
        var exSingle = await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetShipmentFinancialReconciliationAsync(shipment.Id));
        Assert.Contains("Staff user has no assigned facility and lacks cross-facility permission", exSingle.Message);

        // ACT & ASSERT: Batch query throws ForbiddenException
        var exBatch = await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetReconciledShipmentFinancialsBatchAsync(date.AddDays(-1), date.AddDays(1)));
        Assert.Contains("Staff user has no assigned facility and lacks cross-facility permission", exBatch.Message);
    }

    [Fact]
    public async Task Test_38_ClientFacilityAccess_EnforcesActiveAssignments()
    {
        // ARRANGE
        var tenantProvider = new TestTenantProvider { TenantId = _clientAId, BypassTenantFilter = true };
        // Client A user without a specific FacilityId in token
        var clientUserContext = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            TenantId = _clientAId,
            UserType = UserType.Client,
            FacilityId = null
        };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, clientUserContext);

        var date = new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);
        var shipFac1 = CreateShipment(_clientAId, _facility1Id, "SHIP-CFA-OK", "FEDEX", date);
        var shipFac2 = CreateShipment(_clientAId, _facility2Id, "SHIP-CFA-REVOKED", "FEDEX", date);
        context.Shipments.AddRange(shipFac1, shipFac2);

        // In CreateSeededContextAsync: Client A has Facility 1 Active, Facility 2 can be revoked now
        var cfaFac2 = await context.ClientFacilityAccesses
            .FirstAsync(c => c.ClientId == _clientAId && c.FacilityId == _facility2Id);
        cfaFac2.Status = FacilityAccessStatus.Revoked;
        await context.SaveChangesAsync();

        var service = new FreightReconciliationService(
            context,
            tenantProvider,
            clientUserContext,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        // ACT & ASSERT: Facility 1 is Active -> Access Allowed
        var resOk = await service.GetShipmentFinancialReconciliationAsync(shipFac1.Id);
        Assert.NotNull(resOk);

        // ACT & ASSERT: Facility 2 is Revoked -> Access Denied with ForbiddenException
        var exSingle = await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetShipmentFinancialReconciliationAsync(shipFac2.Id));
        Assert.Contains("does not have active authorized access to facility", exSingle.Message);

        // ACT & ASSERT: Explicit batch query for revoked Facility 2 -> ForbiddenException
        var exBatch = await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetReconciledShipmentFinancialsBatchAsync(date.AddDays(-1), date.AddDays(1), facilityId: _facility2Id));
        Assert.Contains("does not have authorized access to facility", exBatch.Message);
    }

    [Fact]
    public async Task Test_39_AdjustmentsAcrossMultipleParentRecords_DeduplicatedGlobally()
    {
        // ARRANGE
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-GLOBAL-ADJ-39", "FEDEX", date);
        context.Shipments.Add(shipment);

        // Parent Invoiced Record 1 (Base freight $100.00)
        var parent1 = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-PAR-01",
            BaseTransportationCost = 100.00m,
            TotalCarrierCost = 100.00m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced
        };

        // Parent Invoiced Record 2 (Accessorial line $30.00)
        var parent2 = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-PAR-02",
            BaseTransportationCost = 0.00m,
            AccessorialCost = 30.00m,
            TotalCarrierCost = 30.00m,
            CostAssessedAtUtc = date.AddHours(1),
            Status = CarrierCostStatus.Invoiced
        };

        // Same adjustment duplicated under both parent records with identical IdempotencyKey
        var sharedKey = "IDEM-GLOBAL-DUP-001";
        var adj1 = new CarrierCostAdjustment
        {
            Id = Guid.NewGuid(),
            CarrierCostRecordId = parent1.Id,
            AdjustmentType = CarrierCostAdjustmentType.DisputeCredit,
            Reason = "Shared dispute credit",
            OriginalCarrierCost = 100.00m,
            AdjustedCarrierCost = 85.00m,
            CarrierCostDelta = -15.00m,
            AdjustedAtUtc = date.AddDays(1),
            IdempotencyKey = sharedKey
        };
        parent1.Adjustments.Add(adj1);

        var adj2 = new CarrierCostAdjustment
        {
            Id = Guid.NewGuid(),
            CarrierCostRecordId = parent2.Id,
            AdjustmentType = CarrierCostAdjustmentType.DisputeCredit,
            Reason = "Shared dispute credit",
            OriginalCarrierCost = 100.00m,
            AdjustedCarrierCost = 85.00m,
            CarrierCostDelta = -15.00m,
            AdjustedAtUtc = date.AddDays(1),
            IdempotencyKey = sharedKey
        };
        parent2.Adjustments.Add(adj2);

        context.CarrierCostRecords.AddRange(parent1, parent2);
        await context.SaveChangesAsync();

        var service = new FreightReconciliationService(
            context,
            tenantProvider,
            currentUser,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        // ACT
        var result = await service.GetShipmentFinancialReconciliationAsync(shipment.Id);

        // ASSERT: Adjustment of -$15.00 is aggregated exactly ONCE, not -$30.00
        Assert.NotNull(result);
        Assert.Equal(100.00m, result.InvoicedCarrierBaseCostUsd);
        Assert.Equal(30.00m, result.InvoicedCarrierAccessorialsUsd);
        Assert.Equal(-15.00m, result.TotalCarrierAdjustmentsUsd);
        Assert.Equal(115.00m, result.NetInvoicedCarrierCostUsd); // 100 + 30 - 15 = 115
        Assert.Single(result.AdjustmentAuditTrail);
        Assert.Equal(-15.00m, result.AdjustmentAuditTrail[0].CostDelta);
        Assert.Equal("Shared dispute credit", result.AdjustmentAuditTrail[0].Reason);
    }

    [Fact]
    public async Task Test_40_LegitimateZeroEstimate_PreservedWithoutFallback()
    {
        // ARRANGE
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-ZERO-EST-40", "FEDEX", date);
        context.Shipments.Add(shipment);

        // Legitimate zero estimate in ShipmentFreightMarginRecord (e.g. Free shipping promotion)
        var marginRecord = new ShipmentFreightMarginRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCostRecordId = Guid.NewGuid(),
            CarrierCode = "FEDEX",
            CarrierServiceLevel = CarrierServiceLevel.StandardGround,
            CarrierBaseCost = 50.00m,
            TotalCarrierCost = 50.00m,
            ClientBaseFreight = 0.00m,
            TotalClientFreight = 0.00m, // Legitimate $0.00 estimate!
            GrossMarginAmount = -50.00m,
            MarkupPercentage = -100.00m,
            MarginPercentage = 0.00m,
            PackageCount = 1,
            PricingModelApplied = FreightPricingModel.FlatFeePerShipment,
            RatedAtUtc = date
        };
        context.ShipmentFreightMarginRecords.Add(marginRecord);

        // Later, client is invoiced $120.00 (e.g. out-of-policy surcharge or post-delivery billing)
        var act = new BillableActivity
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ActivityType = BillableActivityType.CarrierFreight,
            Status = BillableActivityStatus.Invoiced,
            SourceType = "Shipment",
            SourceId = shipment.Id,
            Quantity = 1,
            UnitPrice = 120.00m,
            TotalAmount = 120.00m
        };
        context.BillableActivities.Add(act);

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            BillingPeriodId = Guid.NewGuid(),
            InvoiceNumber = "INV-PROMO-40",
            Status = InvoiceStatus.Issued,
            InvoiceDate = date.AddDays(5),
            DueDate = date.AddDays(35),
            SubtotalAmount = 120.00m,
            TotalAmount = 120.00m
        };
        var line = InvoiceLine.Create(
            lineNumber: 1,
            category: InvoiceChargeCategory.Freight,
            description: "Late billed freight",
            quantity: 1,
            unitOfMeasure: "SHIPMENT",
            unitPrice: 120.00m,
            totalAmount: 120.00m,
            billableActivityId: act.Id,
            activityType: BillableActivityType.CarrierFreight,
            facilityId: _facility1Id);
        line.BillableActivity = act;
        invoice.Lines.Add(line);
        context.Invoices.Add(invoice);

        await context.SaveChangesAsync();

        var service = new FreightReconciliationService(
            context,
            tenantProvider,
            currentUser,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        // ACT
        var result = await service.GetShipmentFinancialReconciliationAsync(shipment.Id);

        // ASSERT: Legitimate zero estimate must be preserved (0.00m), NOT overwritten by invoiced $120.00
        Assert.NotNull(result);
        Assert.Equal(0.00m, result.EstimatedClientFreightUsd);
        Assert.Equal(120.00m, result.InvoicedClientFreightUsd);
    }

    [Fact]
    public async Task Test_41_MarginRecordExists_NoQualifyingInvoiceLines_InvoicedClientFreightStrictlyZero()
    {
        // ARRANGE
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-NO-INV-41", "FEDEX", date);
        context.Shipments.Add(shipment);

        // Pre-dispatch quote / dispatch estimate margin record exists with estimated sell-side freight $150.00
        var marginRecord = new ShipmentFreightMarginRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCostRecordId = Guid.NewGuid(),
            CarrierCode = "FEDEX",
            CarrierServiceLevel = CarrierServiceLevel.StandardGround,
            CarrierBaseCost = 100.00m,
            TotalCarrierCost = 100.00m,
            ClientBaseFreight = 150.00m,
            TotalClientFreight = 150.00m,
            GrossMarginAmount = 50.00m,
            MarkupPercentage = 50.00m,
            MarginPercentage = 33.33m,
            PackageCount = 1,
            PricingModelApplied = FreightPricingModel.CostPlusPercentageMarkup,
            RatedAtUtc = date
        };
        context.ShipmentFreightMarginRecords.Add(marginRecord);
        // Note: Strictly NO Invoice or InvoiceLine is seeded for this shipment!
        await context.SaveChangesAsync();

        var service = new FreightReconciliationService(
            context,
            tenantProvider,
            currentUser,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        // ACT
        var result = await service.GetShipmentFinancialReconciliationAsync(shipment.Id);

        // ASSERT: InvoicedClientFreightUsd MUST be strictly 0.00m and ReconciledClientFreightUsd null!
        Assert.NotNull(result);
        Assert.Equal(150.00m, result.EstimatedClientFreightUsd);
        Assert.Equal(0.00m, result.InvoicedClientFreightUsd);
        Assert.Null(result.ReconciledClientFreightUsd);
        Assert.Equal(FreightReconciliationStatus.Estimated, result.SellSideReconciliationStatus);
    }

    [Fact]
    public async Task Test_42_ClientUser_WithStaleFacilityClaim_WhenAccessRevoked_ThrowsForbidden()
    {
        // ARRANGE
        var tenantProvider = new TestTenantProvider { TenantId = _clientAId, BypassTenantFilter = true };
        // Client token contains a stale Facility claim for Facility 1
        var clientUserContext = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            TenantId = _clientAId,
            UserType = UserType.Client,
            FacilityId = _facility1Id
        };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, clientUserContext);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-STALE-42", "FEDEX", date);
        context.Shipments.Add(shipment);

        // ClientFacilityAccess was revoked in DB after token was issued
        var cfa = await context.ClientFacilityAccesses
            .FirstAsync(c => c.ClientId == _clientAId && c.FacilityId == _facility1Id);
        cfa.Status = FacilityAccessStatus.Revoked;
        await context.SaveChangesAsync();

        var service = new FreightReconciliationService(
            context,
            tenantProvider,
            clientUserContext,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        // ACT & ASSERT: Single shipment query fails closed
        var exSingle = await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetShipmentFinancialReconciliationAsync(shipment.Id));
        Assert.Contains("does not have active authorized access to facility", exSingle.Message);

        // ACT & ASSERT: Batch query with stale facility filter fails closed
        var exBatch = await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetReconciledShipmentFinancialsBatchAsync(date.AddDays(-1), date.AddDays(1), facilityId: _facility1Id));
        Assert.Contains("is not currently active for client", exBatch.Message);
    }

    [Fact]
    public async Task Test_43_ClientUser_WithNoActiveFacilityAssignments_UnfilteredBatch_ThrowsForbidden()
    {
        // ARRANGE
        var tenantProvider = new TestTenantProvider { TenantId = _clientAId, BypassTenantFilter = true };
        // Client user without facility claim in token
        var clientUserContext = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            TenantId = _clientAId,
            UserType = UserType.Client,
            FacilityId = null
        };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, clientUserContext);

        // Revoke ALL facility assignments for Client A
        var accesses = await context.ClientFacilityAccesses.Where(c => c.ClientId == _clientAId).ToListAsync();
        foreach (var access in accesses)
        {
            access.Status = FacilityAccessStatus.Revoked;
        }
        await context.SaveChangesAsync();

        var service = new FreightReconciliationService(
            context,
            tenantProvider,
            clientUserContext,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);

        // ACT & ASSERT: Unfiltered batch must fail closed with ForbiddenException
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetReconciledShipmentFinancialsBatchAsync(date.AddDays(-1), date.AddDays(1)));
        Assert.Contains("has no active facility access assignments", ex.Message);
    }

    [Fact]
    public async Task Test_44_AdjustmentsSharingIdempotencyKey_WithConflictingFinancialPayloads_ThrowsCollisionException()
    {
        // ARRANGE
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-IDEM-COLLISION-44", "FEDEX", date);
        context.Shipments.Add(shipment);

        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-COLLIDE-44",
            BaseTransportationCost = 100.00m,
            TotalCarrierCost = 100.00m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced
        };

        // Two adjustments share same IdempotencyKey but have conflicting CarrierCostDelta (-$10.00 vs -$25.00)
        var sharedKey = "IDEM-CONFLICT-KEY-001";
        var adj1 = new CarrierCostAdjustment
        {
            Id = Guid.NewGuid(),
            CarrierCostRecordId = cost.Id,
            AdjustmentType = CarrierCostAdjustmentType.DisputeCredit,
            Reason = "Initial credit",
            OriginalCarrierCost = 100.00m,
            AdjustedCarrierCost = 90.00m,
            CarrierCostDelta = -10.00m,
            AdjustedAtUtc = date.AddDays(1),
            IdempotencyKey = sharedKey
        };

        var adj2 = new CarrierCostAdjustment
        {
            Id = Guid.NewGuid(),
            CarrierCostRecordId = cost.Id,
            AdjustmentType = CarrierCostAdjustmentType.DisputeCredit,
            Reason = "Conflicting larger credit",
            OriginalCarrierCost = 100.00m,
            AdjustedCarrierCost = 75.00m,
            CarrierCostDelta = -25.00m, // Conflicting delta!
            AdjustedAtUtc = date.AddDays(1),
            IdempotencyKey = sharedKey
        };

        cost.Adjustments.Add(adj1);
        cost.Adjustments.Add(adj2);
        context.CarrierCostRecords.Add(cost);
        await context.SaveChangesAsync();

        var service = new FreightReconciliationService(
            context,
            tenantProvider,
            currentUser,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        // ACT & ASSERT: Must throw InvalidOperationException due to collision conflict
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetShipmentFinancialReconciliationAsync(shipment.Id));
        Assert.Contains("Idempotency collision detected", ex.Message);
    }

    [Fact]
    public async Task Test_45_FuelAndInsurance_CoexistWithDetailedAccessorialRows_WithoutOmission()
    {
        // ARRANGE
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-FUEL-INS-45", "FEDEX", date);
        context.Shipments.Add(shipment);

        // Cost record with Base $100, Fuel $15, Insurance $5, and detailed AccessorialCharges ($30 + $20 = $50)
        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-FUEL-45",
            BaseTransportationCost = 100.00m,
            FuelSurcharge = 15.00m,
            InsuranceCost = 5.00m,
            AccessorialCost = 50.00m,
            TotalCarrierCost = 170.00m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced
        };

        cost.AccessorialCharges.Add(new CarrierAccessorialCharge
        {
            Id = Guid.NewGuid(),
            CarrierCostRecordId = cost.Id,
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            AccessorialType = AccessorialType.LiftgateService,
            CarrierAccessorialCode = "LIFTGATE",
            CarrierAmount = 30.00m,
            Currency = "USD",
            AssessedAtUtc = date
        });

        cost.AccessorialCharges.Add(new CarrierAccessorialCharge
        {
            Id = Guid.NewGuid(),
            CarrierCostRecordId = cost.Id,
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            AccessorialType = AccessorialType.ResidentialDelivery,
            CarrierAccessorialCode = "RESIDENTIAL",
            CarrierAmount = 20.00m,
            Currency = "USD",
            AssessedAtUtc = date
        });

        context.CarrierCostRecords.Add(cost);
        await context.SaveChangesAsync();

        var service = new FreightReconciliationService(
            context,
            tenantProvider,
            currentUser,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        // ACT
        var result = await service.GetShipmentFinancialReconciliationAsync(shipment.Id);

        // ASSERT: Total accessorials = Fuel ($15) + Insurance ($5) + Detailed ($50) = $70.00m
        Assert.NotNull(result);
        Assert.Equal(100.00m, result.InvoicedCarrierBaseCostUsd);
        Assert.Equal(70.00m, result.InvoicedCarrierAccessorialsUsd);
        Assert.Equal(170.00m, result.NetInvoicedCarrierCostUsd);
    }

    [Fact]
    public void Test_46_InvalidFxRates_And_MalformedCurrency_FailSafely()
    {
        var validDate = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);

        // 1. Zero exchange rate throws ArgumentOutOfRangeException
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CurrencyConversionEvidence("CAD", 100m, 0m, validDate, "FRB", "V1", "USD"));

        // 2. Negative exchange rate throws ArgumentOutOfRangeException
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CurrencyConversionEvidence("CAD", 100m, -0.5m, validDate, "FRB", "V1", "USD"));

        // 3. Null or whitespace original currency throws ArgumentException
        Assert.Throws<ArgumentException>(() =>
            new CurrencyConversionEvidence("", 100m, 0.7157m, validDate, "FRB", "V1", "USD"));

        // 4. Null or whitespace target currency throws ArgumentException
        Assert.Throws<ArgumentException>(() =>
            new CurrencyConversionEvidence("CAD", 100m, 0.7157m, validDate, "FRB", "V1", "   "));

        // 5. Lowercase currency code throws ArgumentException (ISO 4217 uppercase enforcement)
        var exLower = Assert.Throws<ArgumentException>(() =>
            new CurrencyConversionEvidence("cad", 100m, 0.7157m, validDate, "FRB", "V1", "USD"));
        Assert.Contains("exactly 3 uppercase ASCII letters", exLower.Message);

        // 6. Invalid length or non-alpha throws ArgumentException
        Assert.Throws<ArgumentException>(() =>
            new CurrencyConversionEvidence("TOOLONG", 100m, 0.7157m, validDate, "FRB", "V1", "USD"));
        Assert.Throws<ArgumentException>(() =>
            new CurrencyConversionEvidence("123", 100m, 0.7157m, validDate, "FRB", "V1", "USD"));
        Assert.Throws<ArgumentException>(() =>
            new CurrencyConversionEvidence("US", 100m, 0.7157m, validDate, "FRB", "V1", "USD"));

        // 7. Syntactically uppercase but unassigned currency code rejected by ISO 4217 registry
        var exUnassigned = Assert.Throws<ArgumentException>(() =>
            new CurrencyConversionEvidence("XYZ", 100m, 0.7157m, validDate, "FRB", "V1", "USD"));
        Assert.Contains("not an active standard ISO 4217 currency code", exUnassigned.Message);

        // 8. FxRateProvider throws ArgumentException for empty, malformed, or unassigned currency
        var provider = new FxRateProvider();
        Assert.Throws<ArgumentException>(() =>
            provider.GetHistoricalExchangeRate("", "USD", validDate, out _, out _));
        Assert.Throws<ArgumentException>(() =>
            provider.GetHistoricalExchangeRate("CAD", "", validDate, out _, out _));
        Assert.Throws<ArgumentException>(() =>
            provider.GetHistoricalExchangeRate("cad", "USD", validDate, out _, out _));
        Assert.Throws<ArgumentException>(() =>
            provider.GetHistoricalExchangeRate("CAD", "usd", validDate, out _, out _));
        Assert.Throws<ArgumentException>(() =>
            provider.GetHistoricalExchangeRate("XYZ", "USD", validDate, out _, out _));
    }

    [Fact]
    public void Test_47_FxEffectiveDate_UnspecifiedKindRejected_LocalKindNormalizedToUtc()
    {
        var provider = new FxRateProvider();

        // 1. Unspecified DateTimeKind throws ArgumentException
        var unspecifiedDate = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Unspecified);
        Assert.Throws<ArgumentException>(() =>
            provider.GetHistoricalExchangeRate("CAD", "USD", unspecifiedDate, out _, out _));

        // 2. CurrencyConversionEvidence with Unspecified DateTimeKind throws ArgumentException
        Assert.Throws<ArgumentException>(() =>
            new CurrencyConversionEvidence("CAD", 100m, 0.7157m, unspecifiedDate, "FRB", "V1", "USD"));

        // 3. DateTimeKind.Local is normalized to UTC
        var localDate = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Local);
        var rate = provider.GetHistoricalExchangeRate("CAD", "USD", localDate, out var source, out var version);
        Assert.Equal(0.7157m, rate);
        Assert.Equal("FederalReserveBoard_H10", source);
        Assert.Equal("FRB-20261109", version);
    }

    [Fact]
    public void Test_48_FutureFxEffectiveDate_CannotBeUsedBeforePublication()
    {
        var provider = new FxRateProvider();

        // Effective date far beyond published bulletin schedule (e.g. 2026-12-01)
        var futureDate = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            provider.GetHistoricalExchangeRate("CAD", "USD", futureDate, out _, out _));

        Assert.Contains("exceeds published", ex.Message);
        Assert.Contains("Future rates cannot be used before publication", ex.Message);
    }

    [Fact]
    public void Test_49_RawBulletinIntegrityHash_MathematicallyVerifiedAgainstPayload()
    {
        var provider = new FxRateProvider();

        // 1. Federal Reserve H.10 bulletins - independently verify raw embedded bytes against computed SHA-256
        Assert.True(provider.VerifyBulletinIntegrity("FRB-20261102", out var hashFrb1));
        Assert.StartsWith("SHA256:", hashFrb1);
        Assert.Equal(71, hashFrb1.Length); // "SHA256:" (7) + 64 hex characters

        var rawFrb1Bytes = provider.GetRawBulletinBytes("FRB-20261102");
        var directHashFrb1 = $"SHA256:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(rawFrb1Bytes)).ToLowerInvariant()}";
        Assert.Equal(hashFrb1, directHashFrb1);

        Assert.True(provider.VerifyBulletinIntegrity("FRB-20261109", out var hashFrb2));
        Assert.StartsWith("SHA256:", hashFrb2);
        Assert.Equal(71, hashFrb2.Length);
        Assert.NotEqual(hashFrb1, hashFrb2);

        // 2. European Central Bank daily bulletins
        Assert.True(provider.VerifyBulletinIntegrity("ECB-20261030", out var hashEcb1));
        Assert.StartsWith("SHA256:", hashEcb1);

        Assert.True(provider.VerifyBulletinIntegrity("ECB-20261102", out var hashEcb2));
        Assert.StartsWith("SHA256:", hashEcb2);

        Assert.True(provider.VerifyBulletinIntegrity("ECB-20261103", out var hashEcb3));
        Assert.StartsWith("SHA256:", hashEcb3);

        Assert.True(provider.VerifyBulletinIntegrity("ECB-20261113", out var hashEcb13));
        Assert.StartsWith("SHA256:", hashEcb13);

        // 3. Bank of England spot bulletins
        Assert.True(provider.VerifyBulletinIntegrity("BOE-20261030", out var hashBoe1));
        Assert.StartsWith("SHA256:", hashBoe1);

        Assert.True(provider.VerifyBulletinIntegrity("BOE-20261102", out var hashBoe2));
        Assert.StartsWith("SHA256:", hashBoe2);

        Assert.True(provider.VerifyBulletinIntegrity("BOE-20261113", out var hashBoe3));
        Assert.StartsWith("SHA256:", hashBoe3);

        // 4. Non-existent bulletin fails verification cleanly
        Assert.False(provider.VerifyBulletinIntegrity("NONEXISTENT-BULLETIN", out var hashUnknown));
        Assert.Empty(hashUnknown);
    }

    [Fact]
    public async Task Test_50_MissingSourceCurrency_OnShipmentCharges_FailsSafelyWithoutUsdDefault()
    {
        // ARRANGE
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-MISSING-CURR-50", "FEDEX", date);
        context.Shipments.Add(shipment);

        // CarrierCostRecord with MISSING/EMPTY currency
        var badCost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-NOCURR-50",
            BaseTransportationCost = 100.00m,
            TotalCarrierCost = 100.00m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced,
            Currency = "" // Empty currency!
        };
        context.CarrierCostRecords.Add(badCost);
        await context.SaveChangesAsync();

        var service = new FreightReconciliationService(
            context,
            tenantProvider,
            currentUser,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        // ACT & ASSERT: Must throw ArgumentException, NEVER silently defaulting to USD
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.GetShipmentFinancialReconciliationAsync(shipment.Id));

        Assert.Contains("Currency code cannot be null or whitespace", ex.Message);
    }

    [Fact]
    public void Test_51_OfficialCentralBankReferenceFixtures_EveryObservationMatchesSourceData()
    {
        var provider = new FxRateProvider();

        // Independent Official Reference Fixtures matching Central Bank publications:
        // 1. Federal Reserve Board H.10 Series H10/H10/RXI_N.B.CA (CAD per USD noon buying rate)
        var officialFrbExpectations = new (DateOnly Date, decimal OfficialQuote, decimal ExpectedMultiplier)[]
        {
            (new DateOnly(2026, 10, 26), 1.4140m, 0.7072m),
            (new DateOnly(2026, 10, 27), 1.4037m, 0.7124m),
            (new DateOnly(2026, 10, 28), 1.4036m, 0.7125m),
            (new DateOnly(2026, 10, 29), 1.3975m, 0.7156m),
            (new DateOnly(2026, 10, 30), 1.3957m, 0.7165m),
            (new DateOnly(2026, 11, 2), 1.3974m, 0.7156m),
            (new DateOnly(2026, 11, 3), 1.3998m, 0.7144m),
            (new DateOnly(2026, 11, 4), 1.3973m, 0.7157m),
            (new DateOnly(2026, 11, 5), 1.3980m, 0.7153m),
            (new DateOnly(2026, 11, 6), 1.4106m, 0.7089m)
        };

        foreach (var (date, expectedQuote, expectedMultiplier) in officialFrbExpectations)
        {
            var effectiveUtc = date.ToDateTime(new TimeOnly(12, 0, 0), DateTimeKind.Utc);
            var rate = provider.GetHistoricalExchangeRate("CAD", "USD", effectiveUtc, out var src, out var ver, out var series, out _, out _);

            Assert.Equal(expectedMultiplier, rate);
            Assert.Equal("FederalReserveBoard_H10", src);
            Assert.Equal("H10/H10/RXI_N.B.CA", series);
        }

        // 2. European Central Bank Reference Series EXR.D.EUR.USD.SP00.A (USD per 1 EUR)
        var officialEcbExpectations = new (DateOnly Date, decimal ExpectedRate, string ExpectedBulletin)[]
        {
            (new DateOnly(2026, 10, 30), 1.1172m, "ECB-20261030"),
            (new DateOnly(2026, 11, 2), 1.1216m, "ECB-20261102"),
            (new DateOnly(2026, 11, 3), 1.1179m, "ECB-20261103"),
            (new DateOnly(2026, 11, 4), 1.1267m, "ECB-20261104"),
            (new DateOnly(2026, 11, 5), 1.1224m, "ECB-20261105"),
            (new DateOnly(2026, 11, 6), 1.1165m, "ECB-20261106"),
            (new DateOnly(2026, 11, 9), 1.1193m, "ECB-20261109"),
            (new DateOnly(2026, 11, 10), 1.1214m, "ECB-20261110"),
            (new DateOnly(2026, 11, 11), 1.1127m, "ECB-20261111"),
            (new DateOnly(2026, 11, 12), 1.1081m, "ECB-20261112"),
            (new DateOnly(2026, 11, 13), 1.1052m, "ECB-20261113")
        };

        foreach (var (date, expectedRate, expectedBulletin) in officialEcbExpectations)
        {
            var effectiveUtc = date.ToDateTime(new TimeOnly(12, 0, 0), DateTimeKind.Utc);
            var rate = provider.GetHistoricalExchangeRate("EUR", "USD", effectiveUtc, out var src, out var ver, out var series, out _, out _);

            Assert.Equal(expectedRate, rate);
            Assert.Equal("EuropeanCentralBank_Reference", src);
            Assert.Equal(expectedBulletin, ver);
            Assert.Equal("EXR.D.EUR.USD.SP00.A", series);
        }

        // 3. Bank of England Statistical Spot Series XUDLUSS (USD per 1 GBP)
        var officialBoeExpectations = new (DateOnly Date, decimal ExpectedRate, string ExpectedBulletin)[]
        {
            (new DateOnly(2026, 10, 30), 1.3397m, "BOE-20261030"),
            (new DateOnly(2026, 11, 2), 1.3416m, "BOE-20261102"),
            (new DateOnly(2026, 11, 4), 1.3471m, "BOE-20261104"),
            (new DateOnly(2026, 11, 6), 1.3360m, "BOE-20261106"),
            (new DateOnly(2026, 11, 13), 1.3234m, "BOE-20261113")
        };

        foreach (var (date, expectedRate, expectedBulletin) in officialBoeExpectations)
        {
            var effectiveUtc = date.ToDateTime(new TimeOnly(12, 0, 0), DateTimeKind.Utc);
            var rate = provider.GetHistoricalExchangeRate("GBP", "USD", effectiveUtc, out var src, out var ver, out var series, out _, out _);

            Assert.Equal(expectedRate, rate);
            Assert.Equal("BankOfEngland_Statistical", src);
            Assert.Equal(expectedBulletin, ver);
            Assert.Equal("XUDLUSS", series);
        }
    }

    [Fact]
    public void Test_52_TamperedBulletinPayload_FailsCryptographicVerification()
    {
        var provider = new FxRateProvider();

        // 1. Retrieve authentic metadata and raw bytes for FRB-20261109
        var meta = provider.GetBulletinMetadata("FRB-20261109");
        var authenticBytes = provider.GetRawBulletinBytes("FRB-20261109");

        // Compute authentic digest and verify against independently pinned manifest entry
        byte[] authHash = System.Security.Cryptography.SHA256.HashData(authenticBytes);
        var expectedDigest = $"SHA256:{Convert.ToHexString(authHash).ToLowerInvariant()}";
        Assert.Equal(meta.Sha256Digest, expectedDigest);
        Assert.Equal(FxBulletinManifest.Entries["FRB-20261109"].ExpectedSha256Digest, expectedDigest);

        // Verify authentic bytes pass provider verifier
        Assert.True(provider.VerifyBulletinIntegrity("FRB-20261109", authenticBytes, out var authComputed));
        Assert.Equal(expectedDigest, authComputed);

        // 2. Submit tampered bytes directly into provider verifier
        byte[] tamperedBytes = (byte[])authenticBytes.Clone();
        tamperedBytes[50] ^= 0xFF; // Flip all bits of byte 50

        // Provider MUST reject tampered input against the independently pinned trust anchor
        Assert.False(provider.VerifyBulletinIntegrity("FRB-20261109", tamperedBytes, out var tamperedComputed));
        Assert.NotEqual(expectedDigest, tamperedComputed);

        // 3. Verify embedded artifact directly
        Assert.True(provider.VerifyBulletinIntegrity("FRB-20261109", out var embeddedDigest));
        Assert.Equal(expectedDigest, embeddedDigest);
    }

    [Fact]
    public async Task Test_53_AdjustmentCurrencyInheritance_ExplicitVsInherited_DocumentedAndEnforced()
    {
        // ARRANGE
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-ADJ-INHERIT-53", "FEDEX", date);
        context.Shipments.Add(shipment);

        // Parent Cost Record in CAD ($100 CAD on Nov 4 -> $71.5700 USD)
        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-CAD-53",
            BaseTransportationCost = 100.00m,
            TotalCarrierCost = 100.00m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced,
            Currency = "CAD"
        };

        // 1. Explicit Currency Adjustment: Persisted explicitly as CAD ($10 CAD on Nov 4 -> -$7.1570 USD)
        var explicitAdj = CarrierCostAdjustment.Create(
            clientId: _clientAId,
            facilityId: _facility1Id,
            carrierCostRecordId: cost.Id,
            adjustmentType: CarrierCostAdjustmentType.DisputeCredit,
            carrierInvoiceNumber: "ADJ-EXP-53",
            originalCarrierCost: 100.00m,
            adjustedCarrierCost: 90.00m,
            clientFreightDelta: 0m,
            currency: "CAD", // Explicitly CAD
            reason: "Explicit CAD dispute concession",
            idempotencyKey: Guid.NewGuid().ToString(),
            adjustedAtUtc: date);

        Assert.Equal("CAD", explicitAdj.Currency);
        var expCurr = explicitAdj.ResolveEffectiveCurrency(cost.Currency, out var expInherited);
        Assert.Equal("CAD", expCurr);
        Assert.False(expInherited); // Not inherited

        // 2. Inherited Currency Adjustment: Created with NULL currency (must NOT default to USD!)
        var inheritedAdj = CarrierCostAdjustment.Create(
            clientId: _clientAId,
            facilityId: _facility1Id,
            carrierCostRecordId: cost.Id,
            adjustmentType: CarrierCostAdjustmentType.LateAccessorial,
            carrierInvoiceNumber: "ADJ-INH-53",
            originalCarrierCost: 90.00m,
            adjustedCarrierCost: 95.00m,
            clientFreightDelta: 0m,
            currency: null, // Null currency: factory leaves it null!
            reason: "Inherited CAD late accessorial surcharge",
            idempotencyKey: Guid.NewGuid().ToString(),
            adjustedAtUtc: date);

        Assert.Null(inheritedAdj.Currency); // Proves no silent USD fallback in factory!

        var inhCurr = inheritedAdj.ResolveEffectiveCurrency(cost.Currency, out var inhInherited);
        Assert.Equal("CAD", inhCurr);
        Assert.True(inhInherited); // Explicitly verified as inherited!

        // 3. Negative Case: Inheriting with invalid or null parent currency fails closed
        var exNoParent = Assert.Throws<InvalidOperationException>(() =>
            inheritedAdj.ResolveEffectiveCurrency(null, out _));
        Assert.Contains("Cannot inherit currency", exNoParent.Message);
        Assert.Contains("parent charge has no valid currency", exNoParent.Message);

        cost.Adjustments.Add(explicitAdj);
        cost.Adjustments.Add(inheritedAdj);
        context.CarrierCostRecords.Add(cost);
        await context.SaveChangesAsync();

        var service = new FreightReconciliationService(
            context,
            tenantProvider,
            currentUser,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        // ACT
        var result = await service.GetShipmentFinancialReconciliationAsync(shipment.Id);

        // ASSERT: Both adjustments (-$10 CAD and +$5 CAD = -$5 CAD net = -$3.5785 USD)
        // Base = $71.5700 USD, Adjustments = -$3.5785 USD, Net = $67.9915 USD
        Assert.NotNull(result);
        Assert.Equal(71.5700m, result.InvoicedCarrierBaseCostUsd);
        Assert.Equal(-3.5785m, result.TotalCarrierAdjustmentsUsd);
        Assert.Equal(67.9915m, result.NetInvoicedCarrierCostUsd);
        Assert.Equal(2, result.AdjustmentAuditTrail.Count);
    }

    [Fact]
    public async Task Test_54_ValuationTimePrecedingPublication_RejectsRateBeforePublicationTime()
    {
        // 1. Direct provider test: Effective date Nov 4, 2026 was published in FRB-20261109 on Monday Nov 9, 2026 at 21:15 UTC
        var effectiveDate = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
        var prematureValuationTime = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

        var exProv = Assert.Throws<InvalidOperationException>(() =>
            _fxRateProvider.GetHistoricalExchangeRate(
                "CAD", "USD", effectiveDate, prematureValuationTime,
                out _, out _, out _, out _, out _));

        Assert.Contains("which is after the requested valuation time", exProv.Message);
        Assert.Contains("Future fixings cannot be used before publication", exProv.Message);

        // 2. Production FreightReconciliationService.NormalizeCurrency carries valuation cutoff
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var service = new FreightReconciliationService(
            context,
            tenantProvider,
            currentUser,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        var exNorm = Assert.Throws<InvalidOperationException>(() =>
            service.NormalizeCurrency(100m, "CAD", "USD", effectiveDate, asOfUtc: prematureValuationTime));
        Assert.Contains("which is after the requested valuation time", exNorm.Message);

        // 3. Production GetShipmentFinancialReconciliationAsync carries valuation cutoff into financial reconciliation
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-VAL-54", "FEDEX", effectiveDate);
        context.Shipments.Add(shipment);
        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-VAL-54",
            BaseTransportationCost = 100.00m,
            TotalCarrierCost = 100.00m,
            CostAssessedAtUtc = effectiveDate,
            Status = CarrierCostStatus.Invoiced,
            Currency = "CAD"
        };
        context.CarrierCostRecords.Add(cost);
        await context.SaveChangesAsync();

        var exRecon = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetShipmentFinancialReconciliationAsync(shipment.Id, asOfUtc: prematureValuationTime));
        Assert.Contains("which is after the requested valuation time", exRecon.Message);

        // 4. Valid valuation timestamp (after bulletin release: Nov 10, 2026) succeeds and persists ValuationTimeUtc
        var validValuationTime = new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);
        var validResult = await service.GetShipmentFinancialReconciliationAsync(shipment.Id, asOfUtc: validValuationTime);

        Assert.Equal(validValuationTime, validResult.ValuationTimeUtc);
        Assert.NotNull(validResult.ConversionEvidence);
        Assert.Equal(validValuationTime, validResult.ConversionEvidence.ValuationTimeUtc);
        Assert.All(validResult.ConversionEvidenceAuditTrail, ev => Assert.Equal(validValuationTime, ev.ValuationTimeUtc));
    }

    [Fact]
    public void Test_55_UnassignedThreeLetterCodes_RejectedByIso4217MembershipRegistry()
    {
        var validDate = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);

        // Unassigned 3-letter codes that are uppercase ASCII but NOT active ISO 4217 codes
        string[] unassignedCodes = ["XYZ", "ABC", "QQQ", "ZZZ", "AAA", "FOO", "BAR"];

        foreach (var code in unassignedCodes)
        {
            var ex = Assert.Throws<ArgumentException>(() =>
                new CurrencyConversionEvidence(code, 100m, 1.0m, validDate, "Test", "1.0", "USD"));

            Assert.Contains("not an active standard ISO 4217 currency code", ex.Message);
        }

        // Active ISO 4217 codes must pass
        string[] activeCodes = ["USD", "CAD", "EUR", "GBP", "JPY", "CHF", "AUD", "NZD", "CNY", "INR"];
        foreach (var code in activeCodes)
        {
            var evidence = new CurrencyConversionEvidence(code, 100m, 1.0m, validDate, "Test", "1.0", "USD");
            Assert.Equal(code, evidence.OriginalCurrency);
        }
    }

    [Fact]
    public void Test_56_RawArtifactParsingAndScheduleDerivation_CrossValidatedOnStartup()
    {
        var provider = new FxRateProvider();

        // 1. Verify all registered bulletins match independent manifest metadata
        var versions = provider.GetSupportedBulletinVersions();
        Assert.Equal(FxBulletinManifest.Entries.Count, versions.Count);

        foreach (var version in versions)
        {
            var meta = provider.GetBulletinMetadata(version);
            Assert.True(FxBulletinManifest.TryGetEntry(version, out var manifestEntry));

            Assert.Equal(manifestEntry.ArtifactFileName, meta.ArtifactFileName);
            Assert.Equal(manifestEntry.BenchmarkSeriesId, meta.BenchmarkSeriesId);
            Assert.Equal(manifestEntry.ExpectedSha256Digest, meta.Sha256Digest);
            Assert.Equal(manifestEntry.ExpectedByteCount, meta.ByteCount);
            Assert.Equal(manifestEntry.PublishedAtUtc, meta.PublishedAtUtc);
            Assert.Equal(manifestEntry.QuotationConvention, meta.QuotationConvention);

            // Raw byte count matches
            var rawBytes = provider.GetRawBulletinBytes(version);
            Assert.Equal(manifestEntry.ExpectedByteCount, rawBytes.Length);

            // Cryptographic integrity verified against trust anchor
            Assert.True(provider.VerifyBulletinIntegrity(version, out var hash));
            Assert.Equal(manifestEntry.ExpectedSha256Digest, hash);

            // Observations count matches
            Assert.Equal(manifestEntry.ExpectedObservations.Count, meta.Observations.Count);
            foreach (var (date, rate) in manifestEntry.ExpectedObservations)
            {
                Assert.True(meta.Observations.TryGetValue(date, out var obsRate));
                Assert.Equal(rate, obsRate);
            }
        }

        // 2. Cross-verify Bank of England Nov 4 observation is officially 1.3471m
        var boeMeta = provider.GetBulletinMetadata("BOE-20261104");
        Assert.Equal(1.3471m, boeMeta.Observations[new DateOnly(2026, 11, 4)]);

        // 3. Verify schedule returns 1.3471m for GBP/USD on Nov 4, 2026
        var boeRate = provider.GetHistoricalExchangeRate(
            "GBP", "USD", new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc),
            out var src, out var ver, out var series, out _, out _);
        Assert.Equal(1.3471m, boeRate);
        Assert.Equal("BankOfEngland_Statistical", src);
        Assert.Equal("BOE-20261104", ver);
        Assert.Equal("XUDLUSS", series);
    }
}
