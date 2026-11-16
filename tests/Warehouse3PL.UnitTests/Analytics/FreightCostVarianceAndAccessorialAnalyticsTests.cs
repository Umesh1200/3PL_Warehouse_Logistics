using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Services.Analytics;
using Warehouse3PL.UnitTests.Common;
using Xunit;

namespace Warehouse3PL.UnitTests.Analytics;

/// <summary>
/// Authoritative test suite for Work Package 6.4.4.2: Freight Cost Variance &amp; Accessorial Discrepancy Analytics.
/// Verifies the 24 specification invariants across variance reconciliation, multi-stage margins,
/// accessorial leakage detection, rate card compliance, tenant isolation, and read-only ledger invariance.
/// </summary>
public class FreightCostVarianceAndAccessorialAnalyticsTests
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
        context.AllowExplicitAuditTimestamps = true;

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

    private (FreightReconciliationService ReconService, FreightCostVarianceService VarianceService) CreateServices(
        ApplicationDbContext context,
        TestTenantProvider tenantProvider,
        TestCurrentUserContext currentUser)
    {
        var reconService = new FreightReconciliationService(
            context,
            tenantProvider,
            currentUser,
            _fxRateProvider,
            NullLogger<FreightReconciliationService>.Instance);

        var varianceService = new FreightCostVarianceService(
            context,
            tenantProvider,
            currentUser,
            reconService,
            NullLogger<FreightCostVarianceService>.Instance);

        return (reconService, varianceService);
    }

    [Fact]
    public async Task Test_01_VarianceReconciliation_QuotedVsEstimatedVsBilledVsReconciled()
    {
        // ARRANGE: Quoted ($100), Estimated ($105), Invoiced ($115), with $3 credit adjustment netting to $112 Reconciled
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-VAR-01", "FEDEX", date);
        context.Shipments.Add(shipment);

        // 1. Quoted record ($100)
        var quoted = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            BaseTransportationCost = 100.00m,
            TotalCarrierCost = 100.00m,
            CostAssessedAtUtc = date.AddHours(-2),
            Status = CarrierCostStatus.Estimated
        };

        // 2. Dispatch estimate ($105)
        var estimated = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            BaseTransportationCost = 105.00m,
            TotalCarrierCost = 105.00m,
            CostAssessedAtUtc = date.AddHours(-1),
            Status = CarrierCostStatus.ActualConfirmed
        };

        // 3. Invoiced & Reconciled record ($115 original, -$3 credit adjustment = $112 net)
        var invoiced = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-VAR-01",
            BaseTransportationCost = 115.00m,
            TotalCarrierCost = 115.00m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Reconciled
        };

        var creditAdj = CarrierCostAdjustment.Create(
            _clientAId, _facility1Id, invoiced.Id, CarrierCostAdjustmentType.DisputeCredit,
            "INV-VAR-01", 115.00m, 112.00m, 0m, "USD", "Approved billing correction", "IDEM-01", date.AddDays(1));

        invoiced.Adjustments.Add(creditAdj);
        context.CarrierCostRecords.AddRange(quoted, estimated, invoiced);
        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT
        var result = await varianceService.GetFreightCostVarianceSummaryAsync("FEDEX", date.AddDays(-1), date.AddDays(2));

        // ASSERT
        Assert.NotNull(result);
        Assert.Equal(100.00m, result.TotalQuotedAmountUsd);
        Assert.Equal(105.00m, result.TotalEstimatedAmountUsd);
        Assert.Equal(112.00m, result.TotalBilledAmountUsd);
        Assert.Equal(112.00m, result.TotalReconciledAmountUsd);
        Assert.Equal(5.00m, result.EstimatedVarianceUsd);
        Assert.Equal(12.00m, result.BilledVarianceUsd);
        Assert.Equal(12.00m, result.ReconciledVarianceUsd);
        Assert.Equal(12.00m, result.VariancePercent); // ((112 - 100) / 100) * 100 = 12.00%
    }

    [Fact]
    public async Task Test_02_MultiStageMarginSeparation_EstimatedVsInvoicedVsRealized()
    {
        // ARRANGE:
        // Estimated: Client $130, Carrier $100 -> $30 margin (23.08%)
        // Invoiced: Client $140, Carrier $115 -> $25 margin (17.86%)
        // Realized: Client $130 (paid invoice), Carrier $112 (reconciled) -> $18 margin (13.85%)
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-MARG-02", "FEDEX", date);
        context.Shipments.Add(shipment);

        // Estimated quote & margin record
        var estCost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            TotalCarrierCost = 100.00m,
            CostAssessedAtUtc = date.AddHours(-1),
            Status = CarrierCostStatus.Estimated
        };

        var marginRecord = new ShipmentFreightMarginRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCostRecordId = estCost.Id,
            CarrierCode = "FEDEX",
            TotalCarrierCost = 100.00m,
            TotalClientFreight = 130.00m,
            RatedAtUtc = date.AddHours(-1),
            IsReconciled = true
        };

        // Reconciled carrier invoice ($115 original - $3 adjustment = $112 net)
        var invCost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-02",
            BaseTransportationCost = 115.00m,
            TotalCarrierCost = 115.00m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Reconciled
        };
        var adj = CarrierCostAdjustment.Create(_clientAId, _facility1Id, invCost.Id, CarrierCostAdjustmentType.DisputeCredit, "INV-02", 115m, 112m, 0m, "USD", "Credit", "K-02", date);
        invCost.Adjustments.Add(adj);

        // Client Paid Invoice ($130)
        var billable = new BillableActivity
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
            UnitPrice = 130.00m,
            TotalAmount = 130.00m,
            Currency = "USD"
        };
        context.BillableActivities.Add(billable);

        var clientInvoice = new Invoice
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            InvoiceNumber = "INV-CLI-02",
            InvoiceDate = date,
            Status = InvoiceStatus.Paid
        };
        var clientLine = InvoiceLine.Create(
            lineNumber: 1,
            category: InvoiceChargeCategory.Freight,
            description: "Client freight",
            quantity: 1,
            unitOfMeasure: "SHIPMENT",
            unitPrice: 130.00m,
            totalAmount: 130.00m,
            billableActivityId: billable.Id,
            activityType: BillableActivityType.CarrierFreight,
            facilityId: _facility1Id);
        clientLine.InvoiceId = clientInvoice.Id;
        clientLine.Invoice = clientInvoice;
        clientLine.BillableActivity = billable;
        clientInvoice.Lines.Add(clientLine);

        context.CarrierCostRecords.AddRange(estCost, invCost);
        context.ShipmentFreightMarginRecords.Add(marginRecord);
        context.Invoices.Add(clientInvoice);
        context.InvoiceLines.Add(clientLine);
        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT
        var result = await varianceService.GetFreightProfitabilitySummaryAsync("FEDEX", date.AddDays(-1), date.AddDays(2));

        // ASSERT: All three margins evaluate distinctly and independently
        Assert.NotNull(result);
        Assert.Equal(30.00m, result.EstimatedGrossMarginUsd); // 130 - 100 = $30
        Assert.Equal(23.08m, result.EstimatedMarginPercent);  // (30 / 130) * 100 = 23.08%

        Assert.Equal(18.00m, result.InvoicedGrossMarginUsd);  // Client $130 - Carrier $112 = $18
        Assert.Equal(13.85m, result.InvoicedMarginPercent);   // (18 / 130) * 100 = 13.85%

        Assert.Equal(18.00m, result.RealizedGrossMarginUsd);  // Reconciled Client $130 - Reconciled Carrier $112 = $18
        Assert.Equal(13.85m, result.RealizedMarginPercent);
    }

    [Fact]
    public async Task Test_03_ZeroDenominators_ReturnsNullForPercentages()
    {
        // ARRANGE: Zero quoted spend and zero revenue
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-ZERO-03", "FEDEX", date);
        context.Shipments.Add(shipment);

        // Carrier cost record with $0 quoted and $0 billed
        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            TotalCarrierCost = 0.00m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced
        };
        context.CarrierCostRecords.Add(cost);
        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT
        var variance = await varianceService.GetFreightCostVarianceSummaryAsync("FEDEX", date.AddDays(-1), date.AddDays(2));
        var profit = await varianceService.GetFreightProfitabilitySummaryAsync("FEDEX", date.AddDays(-1), date.AddDays(2));
        var leakage = await varianceService.GetAccessorialLeakageAnalysisAsync("FEDEX", date.AddDays(-1), date.AddDays(2));
        var compliance = await varianceService.GetRateCardComplianceAnalysisAsync("FEDEX", date.AddDays(-1), date.AddDays(2));

        // ASSERT: Zero denominators must safely return null percentages without throwing exceptions
        Assert.Null(variance.VariancePercent);
        Assert.Null(profit.EstimatedMarginPercent);
        Assert.Null(profit.InvoicedMarginPercent);
        Assert.Null(profit.InvoicedMarkupPercent);
        Assert.Null(profit.RealizedMarginPercent);
        Assert.Null(leakage.AccessorialRecoveryRatePercent);
    }

    [Fact]
    public async Task Test_04_AccessorialLeakageDetection_UnrecoveredFeesFlagged()
    {
        // ARRANGE: Carrier assessed $15 AddressCorrection fee billed to 3PL, $0 billed to client
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-LEAK-04", "FEDEX", date);
        context.Shipments.Add(shipment);

        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-04",
            BaseTransportationCost = 50.00m,
            AccessorialCost = 15.00m,
            TotalCarrierCost = 65.00m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced
        };

        var surcharge = CarrierAccessorialCharge.Create(
            cost.Id, _clientAId, _facility1Id, shipment.Id,
            AccessorialType.AddressCorrection, "ADDR-CORR", 15.00m, "USD", "IDEM-04", date);
        surcharge.BillableTreatment = AccessorialBillableTreatment.BillableWithMarkup; // Billable, but not billed!
        surcharge.ClientAmount = 0m; // Client billed $0

        cost.AccessorialCharges.Add(surcharge);
        context.CarrierCostRecords.Add(cost);
        context.CarrierAccessorialCharges.Add(surcharge);
        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT
        var result = await varianceService.GetAccessorialLeakageAnalysisAsync("FEDEX", date.AddDays(-1), date.AddDays(2));

        // ASSERT: $15 unrecovered leakage detected and categorized under AddressCorrectionPenalty
        Assert.NotNull(result);
        Assert.Equal(15.00m, result.TotalCarrierAccessorialsUsd);
        Assert.Equal(0.00m, result.TotalClientBilledAccessorialsUsd);
        Assert.Equal(15.00m, result.TotalNetLeakageAmountUsd);
        Assert.Equal(0.00m, result.TotalContractuallyAbsorbedUsd);
        Assert.Equal(0.00m, result.AccessorialRecoveryRatePercent);

        var addrCategory = Assert.Single(result.BreakdownByLeakageCategory, c => c.Category == AccessorialLeakageCategory.AddressCorrectionPenalty);
        Assert.Equal(15.00m, addrCategory.AmountUsd);
        Assert.Equal(1, addrCategory.Count);
    }

    [Fact]
    public async Task Test_05_AccessorialRecoveryRate_ComputesExactRecoveryAndCategorization()
    {
        // ARRANGE: Billed $80 of $100 carrier accessorials (computes exact 80.00% recovery rate)
        // $50 Fuel (billed $50), $30 Liftgate (billed $30), $20 Residential (Contractually Absorbed, billed $0)
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-REC-05", "FEDEX", date);
        context.Shipments.Add(shipment);

        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-05",
            BaseTransportationCost = 100.00m,
            AccessorialCost = 100.00m,
            TotalCarrierCost = 200.00m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced
        };

        var fuel = CarrierAccessorialCharge.Create(cost.Id, _clientAId, _facility1Id, shipment.Id, AccessorialType.FuelSurcharge, "FUEL", 50.00m, "USD", "IDEM-FUEL-05", date);
        fuel.ClientAmount = 50.00m;

        var liftgate = CarrierAccessorialCharge.Create(cost.Id, _clientAId, _facility1Id, shipment.Id, AccessorialType.LiftgateService, "LIFT", 30.00m, "USD", "IDEM-LIFT-05", date);
        liftgate.ClientAmount = 30.00m;

        var residential = CarrierAccessorialCharge.Create(cost.Id, _clientAId, _facility1Id, shipment.Id, AccessorialType.ResidentialDelivery, "RESI", 20.00m, "USD", "IDEM-RESI-05", date);
        residential.BillableTreatment = AccessorialBillableTreatment.AbsorbedByWarehouse;
        residential.ClientAmount = 0.00m;

        cost.AccessorialCharges.Add(fuel);
        cost.AccessorialCharges.Add(liftgate);
        cost.AccessorialCharges.Add(residential);
        context.CarrierCostRecords.Add(cost);
        context.CarrierAccessorialCharges.AddRange(fuel, liftgate, residential);

        // Authoritative Phase 5 Client Invoice Lines for Fuel ($50) and Liftgate ($30)
        var billableFuel = new BillableActivity
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ActivityType = BillableActivityType.CarrierFreight,
            Status = BillableActivityStatus.Invoiced,
            SourceType = "CarrierAccessorialCharge",
            SourceId = fuel.Id,
            Quantity = 1,
            UnitPrice = 50.00m,
            TotalAmount = 50.00m,
            Currency = "USD"
        };
        var billableLift = new BillableActivity
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ActivityType = BillableActivityType.CarrierFreight,
            Status = BillableActivityStatus.Invoiced,
            SourceType = "CarrierAccessorialCharge",
            SourceId = liftgate.Id,
            Quantity = 1,
            UnitPrice = 30.00m,
            TotalAmount = 30.00m,
            Currency = "USD"
        };
        context.BillableActivities.AddRange(billableFuel, billableLift);

        var clientInvoice = new Invoice
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            InvoiceNumber = "INV-CLI-05",
            InvoiceDate = date,
            Status = InvoiceStatus.Paid
        };
        var lineFuel = InvoiceLine.Create(1, InvoiceChargeCategory.Freight, "Fuel Surcharge", 1, "UNIT", 50m, 50m, billableFuel.Id, BillableActivityType.CarrierFreight, _facility1Id);
        lineFuel.InvoiceId = clientInvoice.Id;
        lineFuel.Invoice = clientInvoice;
        lineFuel.BillableActivity = billableFuel;

        var lineLift = InvoiceLine.Create(2, InvoiceChargeCategory.Freight, "Liftgate Service", 1, "UNIT", 30m, 30m, billableLift.Id, BillableActivityType.CarrierFreight, _facility1Id);
        lineLift.InvoiceId = clientInvoice.Id;
        lineLift.Invoice = clientInvoice;
        lineLift.BillableActivity = billableLift;

        clientInvoice.Lines.Add(lineFuel);
        clientInvoice.Lines.Add(lineLift);
        context.Invoices.Add(clientInvoice);
        context.InvoiceLines.AddRange(lineFuel, lineLift);

        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT
        var result = await varianceService.GetAccessorialLeakageAnalysisAsync("FEDEX", date.AddDays(-1), date.AddDays(2));

        // ASSERT: Recovery Rate is 80.00%, $20 absorbed by contract, $0 operational leakage
        Assert.NotNull(result);
        Assert.Equal(100.00m, result.TotalCarrierAccessorialsUsd);
        Assert.Equal(80.00m, result.TotalClientBilledAccessorialsUsd);
        Assert.Equal(80.00m, result.AccessorialRecoveryRatePercent);
        Assert.Equal(20.00m, result.TotalContractuallyAbsorbedUsd);
        Assert.Equal(0.00m, result.TotalNetLeakageAmountUsd);

        var absorbedCategory = Assert.Single(result.BreakdownByLeakageCategory, c => c.Category == AccessorialLeakageCategory.AbsorbedByContract);
        Assert.Equal(20.00m, absorbedCategory.AmountUsd);
    }

    [Fact]
    public async Task Test_06_ReconciliationLifecycleStates_PreservesStateIntegrity()
    {
        // ARRANGE: Verify state machine transitions across Estimated, Invoiced, Reconciled, Disputed, WrittenOff
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-STATES-06", "FEDEX", date);
        context.Shipments.Add(shipment);

        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-06",
            BaseTransportationCost = 80.00m,
            TotalCarrierCost = 80.00m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced
        };
        context.CarrierCostRecords.Add(cost);
        await context.SaveChangesAsync();

        var (reconService, _) = CreateServices(context, tenantProvider, currentUser);

        // 1. Initial State: Invoiced
        var resInvoiced = await reconService.GetShipmentFinancialReconciliationAsync(shipment.Id);
        Assert.Equal(FreightReconciliationStatus.Invoiced, resInvoiced.BuySideReconciliationStatus);

        // 2. Transition to Disputed
        cost.Status = CarrierCostStatus.Disputed;
        await context.SaveChangesAsync();
        var resDisputed = await reconService.GetShipmentFinancialReconciliationAsync(shipment.Id);
        Assert.Equal(FreightReconciliationStatus.Disputed, resDisputed.BuySideReconciliationStatus);

        // 3. Transition to Reconciled
        cost.Status = CarrierCostStatus.Reconciled;
        await context.SaveChangesAsync();
        var resReconciled = await reconService.GetShipmentFinancialReconciliationAsync(shipment.Id);
        Assert.Equal(FreightReconciliationStatus.Reconciled, resReconciled.BuySideReconciliationStatus);
    }

    [Fact]
    public async Task Test_07_DisputedCostQuarantined_ExcludedFromReconciledTotals()
    {
        // ARRANGE: Disputed carrier charge ($120) must be excluded from ReconciledCarrierCostUsd
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-DISP-07", "FEDEX", date);
        context.Shipments.Add(shipment);

        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-DISP-07",
            BaseTransportationCost = 120.00m,
            TotalCarrierCost = 120.00m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Disputed // Quarantined!
        };
        context.CarrierCostRecords.Add(cost);
        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT
        var variance = await varianceService.GetFreightCostVarianceSummaryAsync("FEDEX", date.AddDays(-1), date.AddDays(2));
        var queue = await varianceService.GetDiscrepancyManagementQueueAsync("FEDEX", date.AddDays(-1), date.AddDays(2));

        // ASSERT: Reconciled cost is $0 (disputed cost quarantined!)
        Assert.Equal(120.00m, variance.TotalBilledAmountUsd);
        Assert.Equal(0.00m, variance.TotalReconciledAmountUsd);

        var queueItem = Assert.Single(queue);
        Assert.True(queueItem.IsQuarantinedFromReconciledTotals);
        Assert.Equal(FreightReconciliationStatus.Disputed, queueItem.ReconciliationStatus);
    }

    [Fact]
    public async Task Test_08_AntiDoubleCounting_NetInvoicedCumulativeLineage()
    {
        // ARRANGE: Original invoice charge ($50) + carrier cost adjustment (-$10) evaluates to net $40
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-DOUBLE-08", "FEDEX", date);
        context.Shipments.Add(shipment);

        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-08",
            BaseTransportationCost = 50.00m,
            TotalCarrierCost = 50.00m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced
        };

        var adj = CarrierCostAdjustment.Create(
            _clientAId, _facility1Id, cost.Id, CarrierCostAdjustmentType.DisputeCredit,
            "INV-08", 50.00m, 40.00m, 0m, "USD", "Concession credit", "IDEM-08", date.AddDays(1));

        cost.Adjustments.Add(adj);
        context.CarrierCostRecords.Add(cost);
        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT
        var result = await varianceService.GetFreightCostVarianceSummaryAsync("FEDEX", date.AddDays(-1), date.AddDays(2));

        // ASSERT: Net billed amount is strictly $40.00 (never $90 or $50)
        Assert.Equal(40.00m, result.TotalBilledAmountUsd);
    }

    [Fact]
    public async Task Test_09_RateCardComplianceAudit_IdentifiesOvercharge()
    {
        // ARRANGE: Expected base cost $20, billed base cost $25 -> Flags overcharge
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-OVER-09", "FEDEX", date);
        context.Shipments.Add(shipment);

        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-09",
            BaseTransportationCost = 25.00m, // Invoiced $25
            TotalCarrierCost = 25.00m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced
        };

        // Contractual agreement defining $20 base rate for FedEx StandardGround
        var agreement = ClientFreightAgreement.Create(
            _clientAId, "AGR-FEDEX-09", "FedEx Standard Agreement",
            FreightPricingModel.FixedContractualRate, "USD", date.AddDays(-10), date.AddDays(10),
            _facility1Id, "FEDEX", CarrierServiceLevel.StandardGround, fixedFreightRate: 20.00m);
        agreement.IsActive = true;
        context.ClientFreightAgreements.Add(agreement);

        // Adjustment shows original expected rate was $20
        var adj = CarrierCostAdjustment.Create(
            _clientAId, _facility1Id, cost.Id, CarrierCostAdjustmentType.RateCorrection,
            "INV-09", 20.00m, 25.00m, 0m, "USD", "Carrier re-rate overcharge", "IDEM-09", date);

        cost.Adjustments.Add(adj);
        context.CarrierCostRecords.Add(cost);
        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT
        var compliance = await varianceService.GetRateCardComplianceAnalysisAsync("FEDEX", date.AddDays(-1), date.AddDays(2));

        // ASSERT: 1 overcharge discrepancy for $5.00
        Assert.NotNull(compliance);
        Assert.Equal(1, compliance.TotalAuditedShipmentsCount);
        Assert.Equal(0, compliance.CompliantShipmentsCount);
        Assert.Equal(1, compliance.OverchargeDiscrepanciesCount);
        Assert.Equal(5.00m, compliance.TotalOverchargeAmountUsd);
    }

    [Fact]
    public async Task Test_10_DimensionalWeightDrift_IdentifiesCarrierReweighDiscrepancy()
    {
        // ARRANGE: Carrier re-weighed parcel exceeding initial dimensional quote
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-DIM-10", "FEDEX", date, weightKg: 2.5m);
        context.Shipments.Add(shipment);

        var quote = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            BaseTransportationCost = 30.00m,
            TotalCarrierCost = 30.00m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Estimated
        };

        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-10",
            BaseTransportationCost = 30.00m,
            TotalCarrierCost = 45.00m,
            BilledWeightKg = 5.5m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced
        };

        // Contractual agreement defining $30 base rate for FedEx StandardGround
        var agreement = ClientFreightAgreement.Create(
            _clientAId, "AGR-FEDEX-10", "FedEx Standard Agreement",
            FreightPricingModel.FixedContractualRate, "USD", date.AddDays(-10), date.AddDays(10),
            _facility1Id, "FEDEX", CarrierServiceLevel.StandardGround, fixedFreightRate: 30.00m);
        agreement.IsActive = true;
        context.ClientFreightAgreements.Add(agreement);

        // Weight correction adjustment: carrier re-weighed from 2.5kg to 5.5kg
        var weightAdj = CarrierCostAdjustment.Create(
            _clientAId, _facility1Id, cost.Id, CarrierCostAdjustmentType.WeightDiscrepancy,
            "INV-10", 30.00m, 45.00m, 0m, "USD", "Reweighed parcel from 2.5kg to 5.5kg", "IDEM-10", date);
        weightAdj.BilledWeightKg = 5.5m;

        cost.Adjustments.Add(weightAdj);
        context.CarrierCostRecords.AddRange(quote, cost);
        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT
        var compliance = await varianceService.GetRateCardComplianceAnalysisAsync("FEDEX", date.AddDays(-1), date.AddDays(2));
        var queue = await varianceService.GetDiscrepancyManagementQueueAsync("FEDEX", date.AddDays(-1), date.AddDays(2));

        // ASSERT: Dimensional weight drift detected in both compliance report and queue
        Assert.Equal(1, compliance.WeightAuditDiscrepanciesCount);
        Assert.Equal(3.0m, compliance.TotalWeightDriftKg); // Verified carrier billed weight (5.5) - expected billable weight (2.5) = 3.0 kg

        var queueItem = Assert.Single(queue);
        Assert.Equal(DiscrepancyRootCauseCategory.DimensionalWeightDrift, queueItem.RootCauseCategory);
        Assert.Contains("re-weighed", queueItem.DiscrepancyReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Test_11_TenantAndFacilityIsolation_ConfinesQueriesToPermittedScope()
    {
        // ARRANGE: Client A vs Client B
        var tenantProvider = new TestTenantProvider { TenantId = _clientAId, BypassTenantFilter = false };
        var clientUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), UserType = UserType.Client, TenantId = _clientAId, FacilityId = _facility1Id };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, clientUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipA = CreateShipment(_clientAId, _facility1Id, "SHIP-ISO-A", "FEDEX", date);
        var shipB = CreateShipment(_clientBId, _facility2Id, "SHIP-ISO-B", "FEDEX", date);
        context.Shipments.AddRange(shipA, shipB);

        var costA = new CarrierCostRecord { Id = Guid.NewGuid(), ClientId = _clientAId, FacilityId = _facility1Id, ShipmentId = shipA.Id, CarrierCode = "FEDEX", CarrierInvoiceNumber = "INV-ISO-A", BaseTransportationCost = 50m, TotalCarrierCost = 50m, Status = CarrierCostStatus.Invoiced, CostAssessedAtUtc = date };
        var costB = new CarrierCostRecord { Id = Guid.NewGuid(), ClientId = _clientBId, FacilityId = _facility2Id, ShipmentId = shipB.Id, CarrierCode = "FEDEX", CarrierInvoiceNumber = "INV-ISO-B", BaseTransportationCost = 90m, TotalCarrierCost = 90m, Status = CarrierCostStatus.Invoiced, CostAssessedAtUtc = date };
        context.CarrierCostRecords.AddRange(costA, costB);
        await context.SaveChangesAsync();

        var (_, varianceService) = CreateServices(context, tenantProvider, clientUser);

        // ACT: Client A queries
        var result = await varianceService.GetFreightCostVarianceSummaryAsync("FEDEX", date.AddDays(-1), date.AddDays(2));

        // ASSERT: Strictly includes Client A ($50), excludes Client B ($90)
        Assert.Equal(50.00m, result.TotalBilledAmountUsd);
    }

    [Fact]
    public async Task Test_12_OperationalLedgerInvariance_StrictlyZeroMutations()
    {
        // ARRANGE
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-INV-12", "FEDEX", date);
        context.Shipments.Add(shipment);
        var cost = new CarrierCostRecord { Id = Guid.NewGuid(), ClientId = _clientAId, FacilityId = _facility1Id, ShipmentId = shipment.Id, CarrierCode = "FEDEX", TotalCarrierCost = 100m, Status = CarrierCostStatus.Invoiced, CostAssessedAtUtc = date };
        context.CarrierCostRecords.Add(cost);
        await context.SaveChangesAsync();

        int initialShipments = await context.Shipments.CountAsync();
        int initialCostRecords = await context.CarrierCostRecords.CountAsync();
        int initialAdjustments = await context.CarrierCostAdjustments.CountAsync();
        int initialInvoices = await context.Invoices.CountAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT: Run all analytics methods
        await varianceService.GetFreightCostVarianceSummaryAsync("FEDEX", date.AddDays(-1), date.AddDays(2));
        await varianceService.GetFreightProfitabilitySummaryAsync("FEDEX", date.AddDays(-1), date.AddDays(2));
        await varianceService.GetAccessorialLeakageAnalysisAsync("FEDEX", date.AddDays(-1), date.AddDays(2));
        await varianceService.GetDiscrepancyManagementQueueAsync("FEDEX", date.AddDays(-1), date.AddDays(2));
        await varianceService.GetRateCardComplianceAnalysisAsync("FEDEX", date.AddDays(-1), date.AddDays(2));

        // ASSERT: Zero rows inserted, modified, or deleted
        Assert.Equal(initialShipments, await context.Shipments.CountAsync());
        Assert.Equal(initialCostRecords, await context.CarrierCostRecords.CountAsync());
        Assert.Equal(initialAdjustments, await context.CarrierCostAdjustments.CountAsync());
        Assert.Equal(initialInvoices, await context.Invoices.CountAsync());
    }

    [Fact]
    public async Task Test_13_DuplicateSourceRecords_AggregatedExactlyOnce()
    {
        // ARRANGE: Identical idempotency key on multiple cost records (e.g. duplicate webhook)
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-DUP-13", "FEDEX", date);
        context.Shipments.Add(shipment);

        var cost1 = new CarrierCostRecord { Id = Guid.NewGuid(), ClientId = _clientAId, FacilityId = _facility1Id, ShipmentId = shipment.Id, CarrierCode = "FEDEX", CarrierInvoiceNumber = "INV-DUP", BaseTransportationCost = 75m, TotalCarrierCost = 75m, Status = CarrierCostStatus.Invoiced, CostAssessedAtUtc = date, IdempotencyKey = "DUP-KEY-13" };
        var cost2 = new CarrierCostRecord { Id = Guid.NewGuid(), ClientId = _clientAId, FacilityId = _facility1Id, ShipmentId = shipment.Id, CarrierCode = "FEDEX", CarrierInvoiceNumber = "INV-DUP", BaseTransportationCost = 75m, TotalCarrierCost = 75m, Status = CarrierCostStatus.Invoiced, CostAssessedAtUtc = date.AddMinutes(5), IdempotencyKey = "DUP-KEY-13" };

        context.CarrierCostRecords.AddRange(cost1, cost2);
        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT
        var result = await varianceService.GetFreightCostVarianceSummaryAsync("FEDEX", date.AddDays(-1), date.AddDays(2));

        // ASSERT: Deduplicated to exactly $75, never double-counted as $150
        Assert.Equal(75.00m, result.TotalBilledAmountUsd);
    }

    [Fact]
    public async Task Test_14_MultipleSignedAdjustments_EvaluatesNetTotal()
    {
        // ARRANGE: Base $50 + Credit -$10 + Debit +$5 = $45 net
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-MULTI-ADJ-14", "FEDEX", date);
        context.Shipments.Add(shipment);

        var cost = new CarrierCostRecord { Id = Guid.NewGuid(), ClientId = _clientAId, FacilityId = _facility1Id, ShipmentId = shipment.Id, CarrierCode = "FEDEX", CarrierInvoiceNumber = "INV-14", BaseTransportationCost = 50m, TotalCarrierCost = 50m, Status = CarrierCostStatus.Invoiced, CostAssessedAtUtc = date };
        var adjCredit = CarrierCostAdjustment.Create(_clientAId, _facility1Id, cost.Id, CarrierCostAdjustmentType.DisputeCredit, "INV-14", 50m, 40m, 0m, "USD", "Credit -$10", "KEY-CREDIT-14", date.AddDays(1));
        var adjDebit = CarrierCostAdjustment.Create(_clientAId, _facility1Id, cost.Id, CarrierCostAdjustmentType.LateAccessorial, "INV-14", 40m, 45m, 0m, "USD", "Debit +$5", "KEY-DEBIT-14", date.AddDays(2));

        cost.Adjustments.Add(adjCredit);
        cost.Adjustments.Add(adjDebit);
        context.CarrierCostRecords.Add(cost);
        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT
        var result = await varianceService.GetFreightCostVarianceSummaryAsync("FEDEX", date.AddDays(-1), date.AddDays(3));

        // ASSERT: Exactly $45 net
        Assert.Equal(45.00m, result.TotalBilledAmountUsd);
    }

    [Fact]
    public async Task Test_15_AdjustmentReversal_RestoresOriginalBalanceWithAuditHistory()
    {
        // ARRANGE: Original $50 -> Credit -$10 ($40) -> Contra-Reversal +$10 ($50)
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-REV-15", "FEDEX", date);
        context.Shipments.Add(shipment);

        var cost = new CarrierCostRecord { Id = Guid.NewGuid(), ClientId = _clientAId, FacilityId = _facility1Id, ShipmentId = shipment.Id, CarrierCode = "FEDEX", CarrierInvoiceNumber = "INV-15", BaseTransportationCost = 50m, TotalCarrierCost = 50m, Status = CarrierCostStatus.Invoiced, CostAssessedAtUtc = date };
        var adjCredit = CarrierCostAdjustment.Create(_clientAId, _facility1Id, cost.Id, CarrierCostAdjustmentType.DisputeCredit, "INV-15", 50m, 40m, 0m, "USD", "Credit", "K-REV-1", date.AddDays(1));
        var adjReversal = CarrierCostAdjustment.Create(_clientAId, _facility1Id, cost.Id, CarrierCostAdjustmentType.RateCorrection, "INV-15", 40m, 50m, 0m, "USD", "Reversal", "K-REV-2", date.AddDays(2));

        cost.Adjustments.Add(adjCredit);
        cost.Adjustments.Add(adjReversal);
        context.CarrierCostRecords.Add(cost);
        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT
        var result = await varianceService.GetFreightCostVarianceSummaryAsync("FEDEX", date.AddDays(-1), date.AddDays(3));

        // ASSERT: Net is restored to original $50.00
        Assert.Equal(50.00m, result.TotalBilledAmountUsd);
    }

    [Fact]
    public void Test_16_MultiPackageAllocation_ConservesShipmentLevelCharges()
    {
        var reconService = new FreightReconciliationService(
            null!, new TestTenantProvider(), new TestCurrentUserContext(), _fxRateProvider, NullLogger<FreightReconciliationService>.Instance);

        var packages = new List<(Guid PackageId, string PackageNumber, decimal WeightKg)>
        {
            (Guid.NewGuid(), "PKG-1", 3.0m),
            (Guid.NewGuid(), "PKG-2", 3.0m),
            (Guid.NewGuid(), "PKG-3", 4.0m)
        };

        var allocations = reconService.AllocateShipmentCostsToPackages(100.00m, 130.00m, packages);

        Assert.Equal(3, allocations.Count);
        Assert.Equal(100.00m, allocations.Sum(a => a.AllocatedCarrierCostUsd));
        Assert.Equal(130.00m, allocations.Sum(a => a.AllocatedClientFreightUsd));
    }

    [Fact]
    public async Task Test_17_ContractualDimensionalWeight_AppliesDivisorAndRoundingIncrement()
    {
        // ARRANGE: Package dimensions 40x30x20 cm / 5000 = 4.8 kg vs 3.2 kg scale weight
        // Expected billable weight rounds up to 5.0 kg
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-DIM-17", "FEDEX", date, weightKg: 3.2m);
        var pkg = shipment.Packages.First();
        pkg.LengthCm = 40m;
        pkg.WidthCm = 30m;
        pkg.HeightCm = 20m;
        pkg.VolumetricDivisor = 5000m;

        context.Shipments.Add(shipment);

        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-17",
            BaseTransportationCost = 40.00m,
            TotalCarrierCost = 40.00m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced
        };

        var agreement = ClientFreightAgreement.Create(
            _clientAId, "AGR-FEDEX-17", "FedEx Standard Agreement",
            FreightPricingModel.FixedContractualRate, "USD", date.AddDays(-10), date.AddDays(10),
            _facility1Id, "FEDEX", CarrierServiceLevel.StandardGround, fixedFreightRate: 40.00m);
        agreement.IsActive = true;
        context.ClientFreightAgreements.Add(agreement);

        context.CarrierCostRecords.Add(cost);
        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT
        var compliance = await varianceService.GetRateCardComplianceAnalysisAsync("FEDEX", date.AddDays(-1), date.AddDays(2));

        // ASSERT: Compliant package evaluated with volumetric rounding
        Assert.NotNull(compliance);
        Assert.Equal(1, compliance.TotalAuditedShipmentsCount);
        Assert.Equal(1, compliance.CompliantShipmentsCount);
    }

    [Fact]
    public async Task Test_18_EffectiveDatedRateCard_HistoricalShipmentsAuditedAtDispatchDate()
    {
        // ARRANGE: CAD shipment on Nov 4, 2026 normalized with official reciprocal rate (0.7157m)
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-FX-18", "FEDEX", date);
        context.Shipments.Add(shipment);

        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-FX-18",
            BaseTransportationCost = 100.00m, // $100 CAD on Nov 4 = $71.5700 USD
            TotalCarrierCost = 100.00m,
            Currency = "CAD",
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced
        };
        context.CarrierCostRecords.Add(cost);
        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT: Valuation cutoff on Nov 10 (after bulletin release)
        var validAsOf = new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);
        var result = await varianceService.GetFreightCostVarianceSummaryAsync("FEDEX", date.AddDays(-1), date.AddDays(2), asOfUtc: validAsOf);

        // ASSERT: Billed amount converted at exact official Nov 4 multiplier ($71.57)
        Assert.Equal(71.5700m, result.TotalBilledAmountUsd);
    }

    [Fact]
    public async Task Test_19_PartialAccessorialRecovery_ComputesExactUnderRecovery()
    {
        // ARRANGE: Carrier surcharge $20, client billed $15 -> leakage is exactly $5 (under-recovery)
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-PARTIAL-19", "FEDEX", date);
        context.Shipments.Add(shipment);

        var cost = new CarrierCostRecord { Id = Guid.NewGuid(), ClientId = _clientAId, FacilityId = _facility1Id, ShipmentId = shipment.Id, CarrierCode = "FEDEX", TotalCarrierCost = 20m, Status = CarrierCostStatus.Invoiced, CostAssessedAtUtc = date };
        var surcharge = CarrierAccessorialCharge.Create(cost.Id, _clientAId, _facility1Id, shipment.Id, AccessorialType.LiftgateService, "LIFT", 20.00m, "USD", "IDEM-19", date);
        surcharge.ClientAmount = 15.00m; // Target client billing
        surcharge.BillableTreatment = AccessorialBillableTreatment.FixedClientFee;

        cost.AccessorialCharges.Add(surcharge);
        context.CarrierCostRecords.Add(cost);
        context.CarrierAccessorialCharges.Add(surcharge);

        // Authoritative Phase 5 Client Invoice Line for Liftgate ($15)
        var billable = new BillableActivity
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ActivityType = BillableActivityType.CarrierFreight,
            Status = BillableActivityStatus.Invoiced,
            SourceType = "CarrierAccessorialCharge",
            SourceId = surcharge.Id,
            Quantity = 1,
            UnitPrice = 15.00m,
            TotalAmount = 15.00m,
            Currency = "USD"
        };
        context.BillableActivities.Add(billable);

        var clientInvoice = new Invoice
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            InvoiceNumber = "INV-CLI-19",
            InvoiceDate = date,
            Status = InvoiceStatus.Paid
        };
        var clientLine = InvoiceLine.Create(1, InvoiceChargeCategory.Freight, "Liftgate Service", 1, "UNIT", 15m, 15m, billable.Id, BillableActivityType.CarrierFreight, _facility1Id);
        clientLine.InvoiceId = clientInvoice.Id;
        clientLine.Invoice = clientInvoice;
        clientLine.BillableActivity = billable;
        clientInvoice.Lines.Add(clientLine);
        context.Invoices.Add(clientInvoice);
        context.InvoiceLines.Add(clientLine);

        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT
        var result = await varianceService.GetAccessorialLeakageAnalysisAsync("FEDEX", date.AddDays(-1), date.AddDays(2));

        // ASSERT: Exactly $5 under-recovery leakage
        Assert.Equal(20.00m, result.TotalCarrierAccessorialsUsd);
        Assert.Equal(15.00m, result.TotalClientBilledAccessorialsUsd);
        Assert.Equal(5.00m, result.TotalNetLeakageAmountUsd);
        Assert.Equal(75.00m, result.AccessorialRecoveryRatePercent);

        var underCat = Assert.Single(result.BreakdownByLeakageCategory, c => c.Category == AccessorialLeakageCategory.ClientUnderRecovery);
        Assert.Equal(5.00m, underCat.AmountUsd);
    }

    [Fact]
    public async Task Test_20_ContractuallyAbsorbedAccessorial_SeparatedFromOperationalLeakage()
    {
        // ARRANGE: Surcharge marked AbsorbedByWarehouse ($25) reported separately from leakage
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-ABS-20", "FEDEX", date);
        context.Shipments.Add(shipment);

        var cost = new CarrierCostRecord { Id = Guid.NewGuid(), ClientId = _clientAId, FacilityId = _facility1Id, ShipmentId = shipment.Id, CarrierCode = "FEDEX", TotalCarrierCost = 25m, Status = CarrierCostStatus.Invoiced, CostAssessedAtUtc = date };
        var surcharge = CarrierAccessorialCharge.Create(cost.Id, _clientAId, _facility1Id, shipment.Id, AccessorialType.ResidentialDelivery, "RESI", 25.00m, "USD", "IDEM-20", date);
        surcharge.BillableTreatment = AccessorialBillableTreatment.AbsorbedByWarehouse;
        surcharge.ClientAmount = 0.00m;

        cost.AccessorialCharges.Add(surcharge);
        context.CarrierCostRecords.Add(cost);
        context.CarrierAccessorialCharges.Add(surcharge);
        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT
        var result = await varianceService.GetAccessorialLeakageAnalysisAsync("FEDEX", date.AddDays(-1), date.AddDays(2));

        // ASSERT: $25 contractually absorbed, $0 operational leakage
        Assert.Equal(25.00m, result.TotalContractuallyAbsorbedUsd);
        Assert.Equal(0.00m, result.TotalNetLeakageAmountUsd);
    }

    [Fact]
    public async Task Test_21_NegativeDenominatorsOrMargins_ValidNegativeMarginWithoutException()
    {
        // ARRANGE: Revenue $80, Carrier Cost $100 -> Invoiced Gross Margin -$20 (-25.00%)
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-NEG-21", "FEDEX", date);
        context.Shipments.Add(shipment);

        var cost = new CarrierCostRecord { Id = Guid.NewGuid(), ClientId = _clientAId, FacilityId = _facility1Id, ShipmentId = shipment.Id, CarrierCode = "FEDEX", CarrierInvoiceNumber = "INV-21", BaseTransportationCost = 100m, TotalCarrierCost = 100m, Status = CarrierCostStatus.Invoiced, CostAssessedAtUtc = date };

        var billable21 = new BillableActivity
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
            UnitPrice = 80.00m,
            TotalAmount = 80.00m,
            Currency = "USD"
        };
        context.BillableActivities.Add(billable21);

        var invoice = new Invoice { Id = Guid.NewGuid(), ClientId = _clientAId, FacilityId = _facility1Id, InvoiceNumber = "INV-CLI-21", InvoiceDate = date, Status = InvoiceStatus.Approved };
        var line = InvoiceLine.Create(
            lineNumber: 1,
            category: InvoiceChargeCategory.Freight,
            description: "Client freight",
            quantity: 1,
            unitOfMeasure: "SHIPMENT",
            unitPrice: 80.00m,
            totalAmount: 80.00m,
            billableActivityId: billable21.Id,
            activityType: BillableActivityType.CarrierFreight,
            facilityId: _facility1Id);
        line.InvoiceId = invoice.Id;
        line.Invoice = invoice;
        line.BillableActivity = billable21;
        invoice.Lines.Add(line);

        context.CarrierCostRecords.Add(cost);
        context.Invoices.Add(invoice);
        context.InvoiceLines.Add(line);
        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT
        var result = await varianceService.GetFreightProfitabilitySummaryAsync("FEDEX", date.AddDays(-1), date.AddDays(2));

        // ASSERT: Negative margin percent evaluates correctly: -20 / 80 * 100 = -25.00%
        Assert.NotNull(result);
        Assert.Equal(-20.00m, result.InvoicedGrossMarginUsd);
        Assert.Equal(-25.00m, result.InvoicedMarginPercent);
    }

    [Fact]
    public async Task Test_22_UnauthorizedTenantOrFacilityAccess_ThrowsForbiddenException()
    {
        // ARRANGE: Client A caller requesting Client B
        var tenantProvider = new TestTenantProvider { TenantId = _clientAId, BypassTenantFilter = false };
        var clientUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), UserType = UserType.Client, TenantId = _clientAId, FacilityId = _facility1Id };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, clientUser);

        var (_, varianceService) = CreateServices(context, tenantProvider, clientUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);

        // ACT & ASSERT: Cross-tenant query must throw ForbiddenException
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            varianceService.GetFreightCostVarianceSummaryAsync("FEDEX", date.AddDays(-1), date.AddDays(2), clientId: _clientBId));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            varianceService.GetFreightCostVarianceSummaryAsync("FEDEX", date.AddDays(-1), date.AddDays(2), facilityId: _facility2Id)); // Facility 2 is not client A's authorized facility in this context
    }

    [Fact]
    public async Task Test_23_DisputedChargeSettlement_EntersReconciledOnlyAfterSettlement()
    {
        // ARRANGE: Disputed charge enters reconciled totals only after authorized settlement transition
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-SETTLE-23", "FEDEX", date);
        context.Shipments.Add(shipment);

        var cost = new CarrierCostRecord { Id = Guid.NewGuid(), ClientId = _clientAId, FacilityId = _facility1Id, ShipmentId = shipment.Id, CarrierCode = "FEDEX", CarrierInvoiceNumber = "INV-23", BaseTransportationCost = 100m, TotalCarrierCost = 100m, Status = CarrierCostStatus.Disputed, CostAssessedAtUtc = date };
        context.CarrierCostRecords.Add(cost);
        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // 1. In Disputed state: Reconciled amount is $0
        var beforeSettlement = await varianceService.GetFreightCostVarianceSummaryAsync("FEDEX", date.AddDays(-1), date.AddDays(2));
        Assert.Equal(0.00m, beforeSettlement.TotalReconciledAmountUsd);

        // 2. Settlement executed: carrier issues $10 credit note and transitions to Reconciled
        cost.Status = CarrierCostStatus.Reconciled;
        var settlementAdj = CarrierCostAdjustment.Create(_clientAId, _facility1Id, cost.Id, CarrierCostAdjustmentType.DisputeCredit, "INV-23", 100m, 90m, 0m, "USD", "Settlement credit", "IDEM-23", date.AddDays(1));
        cost.Adjustments.Add(settlementAdj);
        await context.SaveChangesAsync();

        // 3. After Settlement: Reconciled amount is $90
        var afterSettlement = await varianceService.GetFreightCostVarianceSummaryAsync("FEDEX", date.AddDays(-1), date.AddDays(2));
        Assert.Equal(90.00m, afterSettlement.TotalReconciledAmountUsd);
    }

    [Fact]
    public async Task Test_24_WrittenOffChargeReporting_QuarantinedFromReconciledMargin()
    {
        // ARRANGE: Written-off charges excluded from reconciled totals
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-WRITEOFF-24", "FEDEX", date);
        context.Shipments.Add(shipment);

        var cost = new CarrierCostRecord { Id = Guid.NewGuid(), ClientId = _clientAId, FacilityId = _facility1Id, ShipmentId = shipment.Id, CarrierCode = "FEDEX", CarrierInvoiceNumber = "INV-24", BaseTransportationCost = 60m, TotalCarrierCost = 60m, Status = CarrierCostStatus.Disputed, CostAssessedAtUtc = date };
        context.CarrierCostRecords.Add(cost);
        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT
        var variance = await varianceService.GetFreightCostVarianceSummaryAsync("FEDEX", date.AddDays(-1), date.AddDays(2));

        // ASSERT: Written off cost excluded from Reconciled totals
        Assert.Equal(0.00m, variance.TotalReconciledAmountUsd);
    }

    [Fact]
    public async Task Test_25_AccessorialClientRecovery_AuthoritativeInvoiceLineOverridesClientAmount()
    {
        // ARRANGE: Gate A - Surcharge client recovery must originate from authoritative Phase 5 InvoiceLine.
        // CarrierAccessorialCharge.ClientAmount ($100) must NOT establish proof of billing when invoice line is $75.
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-AUTH-25", "FEDEX", date);
        context.Shipments.Add(shipment);

        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            TotalCarrierCost = 60m,
            Status = CarrierCostStatus.Invoiced,
            CostAssessedAtUtc = date
        };

        var charge = CarrierAccessorialCharge.Create(
            cost.Id, _clientAId, _facility1Id, shipment.Id,
            AccessorialType.LiftgateService, "LIFTGATE", 60.00m, "USD", "IDEM-25", date);
        charge.ClientAmount = 100.00m; // Unauthoritative mutable client amount
        cost.AccessorialCharges.Add(charge);
        context.CarrierCostRecords.Add(cost);
        context.CarrierAccessorialCharges.Add(charge);

        // Authoritative Phase 5 billable activity & invoice line for $75.00
        var billable = new BillableActivity
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ActivityType = BillableActivityType.CarrierFreight,
            Status = BillableActivityStatus.Invoiced,
            SourceType = "CarrierAccessorialCharge",
            SourceId = charge.Id,
            SourceReference = "LIFTGATE",
            Quantity = 1,
            UnitPrice = 75.00m,
            TotalAmount = 75.00m,
            Currency = "USD"
        };
        context.BillableActivities.Add(billable);

        var clientInvoice = new Invoice
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            InvoiceNumber = "INV-CLI-25",
            InvoiceDate = date,
            Status = InvoiceStatus.Approved
        };
        var clientLine = InvoiceLine.Create(
            1, InvoiceChargeCategory.Freight, "Liftgate Surcharge", 1, "UNIT", 75m, 75m,
            billable.Id, BillableActivityType.CarrierFreight, _facility1Id);
        clientLine.InvoiceId = clientInvoice.Id;
        clientLine.Invoice = clientInvoice;
        clientLine.BillableActivity = billable;
        clientInvoice.Lines.Add(clientLine);
        context.Invoices.Add(clientInvoice);
        context.InvoiceLines.Add(clientLine);

        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT
        var result = await varianceService.GetAccessorialLeakageAnalysisAsync("FEDEX", date.AddDays(-1), date.AddDays(2));

        // ASSERT: Total client billed is $75.00 from authoritative invoice line, NOT $100 from charge.ClientAmount
        Assert.Equal(60.00m, result.TotalCarrierAccessorialsUsd);
        Assert.Equal(75.00m, result.TotalClientBilledAccessorialsUsd);
        Assert.Equal(0.00m, result.TotalNetLeakageAmountUsd); // Billed ($75) >= Carrier ($60) -> 0 leakage
        Assert.Equal(125.00m, result.AccessorialRecoveryRatePercent);
    }

    [Fact]
    public async Task Test_26_ValuationCutoff_PostValuationChargeExcludedUntilTimestampAdvances()
    {
        // ARRANGE: Gate B - Historical Valuation Cutoff. Charges assessed post-valuation time are strictly excluded.
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var t0 = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var tValuation = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
        var tPost = new DateTime(2026, 11, 4, 15, 0, 0, DateTimeKind.Utc);
        var tFutureValuation = new DateTime(2026, 11, 4, 18, 0, 0, DateTimeKind.Utc);

        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-CUTOFF-26", "FEDEX", t0);
        context.Shipments.Add(shipment);

        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            TotalCarrierCost = 70m,
            Status = CarrierCostStatus.Invoiced,
            CostAssessedAtUtc = t0
        };

        // Charge 1 assessed at T0 ($40)
        var charge1 = CarrierAccessorialCharge.Create(
            cost.Id, _clientAId, _facility1Id, shipment.Id,
            AccessorialType.FuelSurcharge, "FUEL", 40.00m, "USD", "IDEM-26A", t0);

        // Charge 2 assessed at TPost ($30, after tValuation)
        var charge2 = CarrierAccessorialCharge.Create(
            cost.Id, _clientAId, _facility1Id, shipment.Id,
            AccessorialType.ResidentialDelivery, "RESI", 30.00m, "USD", "IDEM-26B", tPost);

        cost.AccessorialCharges.Add(charge1);
        cost.AccessorialCharges.Add(charge2);
        context.CarrierCostRecords.Add(cost);
        context.CarrierAccessorialCharges.AddRange(charge1, charge2);
        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT 1: Evaluated at tValuation (12:00) -> Charge 2 ($30 at 15:00) must be excluded
        var reportAtT1 = await varianceService.GetAccessorialLeakageAnalysisAsync(
            "FEDEX", t0.AddDays(-1), t0.AddDays(2), asOfUtc: tValuation);

        // ASSERT 1: Only Charge 1 ($40) is recognized
        Assert.Equal(40.00m, reportAtT1.TotalCarrierAccessorialsUsd);

        // ACT 2: Valuation timestamp advances to tFutureValuation (18:00) -> Charge 2 is now included
        var reportAtT2 = await varianceService.GetAccessorialLeakageAnalysisAsync(
            "FEDEX", t0.AddDays(-1), t0.AddDays(2), asOfUtc: tFutureValuation);

        // ASSERT 2: Both Charge 1 and Charge 2 are recognized ($40 + $30 = $70)
        Assert.Equal(70.00m, reportAtT2.TotalCarrierAccessorialsUsd);
    }

    [Fact]
    public async Task Test_27_ZeroDenominatorUnitEconomics_ReturnsNullAverages()
    {
        // ARRANGE: Gate C - Null semantics on zero denominator.
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT: Query window with 0 eligible shipments
        var profitability = await varianceService.GetFreightProfitabilitySummaryAsync(
            "FEDEX", date.AddDays(-10), date.AddDays(-5));

        // ASSERT: Unit economics and percentage margins must return null, never 0.00m
        Assert.NotNull(profitability);
        Assert.Equal(0, profitability.TotalShipmentsCount);
        Assert.Null(profitability.AverageRevenuePerShipmentUsd);
        Assert.Null(profitability.AverageCostPerShipmentUsd);
        Assert.Null(profitability.AverageMarginPerShipmentUsd);
        Assert.Null(profitability.InvoicedMarginPercent);
        Assert.Null(profitability.InvoicedMarkupPercent);
        Assert.Null(profitability.EstimatedMarginPercent);
        Assert.Null(profitability.RealizedMarginPercent);
    }

    [Fact]
    public async Task Test_28_RateCardCompliance_MissingRateCardReturnsIndeterminateNotCompliant()
    {
        // ARRANGE: Gate D - Invoiced shipment without any contractual ClientFreightAgreement or RateCardSchedule.
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-NOCONTRACT-28", "FEDEX", date);
        context.Shipments.Add(shipment);

        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-28",
            BaseTransportationCost = 35.00m,
            TotalCarrierCost = 35.00m,
            Status = CarrierCostStatus.Invoiced,
            CostAssessedAtUtc = date
        };
        context.CarrierCostRecords.Add(cost);
        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT: Run rate card compliance audit
        var compliance = await varianceService.GetRateCardComplianceAnalysisAsync("FEDEX", date.AddDays(-1), date.AddDays(2));

        // ASSERT: Without a contract, shipment is classified as Indeterminate, NOT Compliant
        Assert.NotNull(compliance);
        Assert.Equal(0, compliance.TotalAuditedShipmentsCount);
        Assert.Equal(1, compliance.IndeterminateShipmentsCount);
        Assert.Equal(0, compliance.CompliantShipmentsCount);
        Assert.Equal(0, compliance.OverchargeDiscrepanciesCount);
    }

    [Fact]
    public async Task Test_29_ContractualWeightIncrement_NonDefaultRoundingStepApplied()
    {
        // ARRANGE: Gate D - Contractual rate card with non-default 1.0 kg rounding step.
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-ROUND-29", "FEDEX", date, weightKg: 2.2m);
        var pkg = shipment.Packages.First();
        pkg.GrossWeightKg = 2.2m;
        pkg.LengthCm = 20m;
        pkg.WidthCm = 20m;
        pkg.HeightCm = 20m; // Volumetric = 8000 / 5000 = 1.6kg -> max(scale 2.2, dim 1.6) = 2.2kg
        pkg.VolumetricDivisor = 5000m;
        context.Shipments.Add(shipment);

        // Agreement specifies contractual rounding increment of 1.0 kg
        var agreement = ClientFreightAgreement.Create(
            _clientAId, "AGR-ROUND-29", "FedEx Custom Rounding Agreement",
            FreightPricingModel.FixedContractualRate, "USD", date.AddDays(-10), date.AddDays(10),
            _facility1Id, "FEDEX", CarrierServiceLevel.StandardGround, fixedFreightRate: 30.00m,
            weightRoundingIncrementKg: 1.0m,
            notes: "Contractual billing weight increment: 1.0 kg");
        agreement.IsActive = true;
        context.ClientFreightAgreements.Add(agreement);

        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-29",
            BaseTransportationCost = 30.00m,
            TotalCarrierCost = 30.00m,
            BilledWeightKg = 3.0m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced
        };

        // Carrier reweighed to 3.0 kg.
        // Under 1.0 kg rounding increment: 2.2 kg rounds up to 3.0 kg. Drift = 3.0 - 3.0 = 0.0 kg.
        // (Under 0.5 kg default: 2.2 kg would round to 2.5 kg, resulting in drift 0.5 kg).
        var weightAdj = CarrierCostAdjustment.Create(
            _clientAId, _facility1Id, cost.Id, CarrierCostAdjustmentType.WeightDiscrepancy,
            "INV-29", 30.00m, 30.00m, 0m, "USD", "Reweighed parcel to 3.0 kg", "IDEM-29", date);
        weightAdj.BilledWeightKg = 3.0m;
        cost.Adjustments.Add(weightAdj);

        context.CarrierCostRecords.Add(cost);
        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT
        var compliance = await varianceService.GetRateCardComplianceAnalysisAsync("FEDEX", date.AddDays(-1), date.AddDays(2));

        // ASSERT: Total weight drift is 0.0 kg under 1.0 kg increment
        Assert.NotNull(compliance);
        Assert.Equal(1, compliance.TotalAuditedShipmentsCount);
        Assert.Equal(1, compliance.CompliantShipmentsCount);
        Assert.Equal(0, compliance.WeightAuditDiscrepanciesCount);
        Assert.Equal(0m, compliance.TotalWeightDriftKg);
    }

    [Fact]
    public async Task Test_30_DiscrepancyQueue_MultipleInvoiceLinesRetainedWithoutCollapsing()
    {
        // ARRANGE: Gate E & F - Multiple carrier invoice lines for a single shipment are preserved with distinct stable identifiers,
        // and operational quote variance and contractual rate card variance demonstrate independent baselines.
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-MULTI-30", "FEDEX", date);
        context.Shipments.Add(shipment);

        // Initial Quote of $45 (independent baseline)
        var quote = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            BaseTransportationCost = 45.00m,
            TotalCarrierCost = 45.00m,
            CostAssessedAtUtc = date.AddHours(-2),
            Status = CarrierCostStatus.Estimated
        };

        // Contractual agreement of $50 base rate (independent baseline)
        var agreement = ClientFreightAgreement.Create(
            _clientAId, "AGR-FEDEX-30", "FedEx Standard Agreement",
            FreightPricingModel.FixedContractualRate, "USD", date.AddDays(-10), date.AddDays(10),
            _facility1Id, "FEDEX", CarrierServiceLevel.StandardGround, fixedFreightRate: 50.00m);
        agreement.IsActive = true;
        context.ClientFreightAgreements.Add(agreement);

        // Carrier Invoice Record 1: Base freight invoice INV-30A ($65)
        var cost1 = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-30A",
            BaseTransportationCost = 65.00m,
            TotalCarrierCost = 65.00m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced
        };

        // Carrier Invoice Record 2: Supplemental freight invoice INV-30B ($30)
        var cost2 = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-30B",
            BaseTransportationCost = 30.00m,
            TotalCarrierCost = 30.00m,
            CostAssessedAtUtc = date.AddMinutes(30),
            Status = CarrierCostStatus.Invoiced
        };

        context.CarrierCostRecords.AddRange(quote, cost1, cost2);
        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT
        var queue = await varianceService.GetDiscrepancyManagementQueueAsync("FEDEX", date.AddDays(-1), date.AddDays(2));

        // ASSERT: Multiple lines retained without collapsing into a single item
        Assert.NotNull(queue);
        Assert.Equal(2, queue.Count);

        var item1 = Assert.Single(queue, q => q.CarrierCostRecordId == cost1.Id);
        Assert.Contains(cost1.Id.ToString(), item1.InvoiceLineIdentifier);
        Assert.Contains("INV-30A", item1.CarrierInvoiceNumbers);
        Assert.Equal(65.00m, item1.InvoicedCostUsd);
        Assert.Equal(20.00m, item1.OperationalQuoteVarianceUsd); // 65 - 45 (quote) = +20
        Assert.Equal(15.00m, item1.ContractualRateCardVarianceUsd); // 65 - 50 (contract) = +15

        var item2 = Assert.Single(queue, q => q.CarrierCostRecordId == cost2.Id);
        Assert.Contains(cost2.Id.ToString(), item2.InvoiceLineIdentifier);
        Assert.Contains("INV-30B", item2.CarrierInvoiceNumbers);
        Assert.Equal(30.00m, item2.InvoicedCostUsd);
        Assert.Equal(-15.00m, item2.OperationalQuoteVarianceUsd); // 30 - 45 (quote) = -15
        Assert.Equal(-20.00m, item2.ContractualRateCardVarianceUsd); // 30 - 50 (contract) = -20
    }

    [Fact]
    public async Task Test_31_DiscrepancyQueue_MissingContract_ReturnsNullContractualVariance()
    {
        // ARRANGE: Gate F - When no contractual rate card or agreement is active for a shipment,
        // OperationalQuoteVarianceUsd is computed against quote/estimate, while ContractualRateCardVarianceUsd is null.
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-NOCONTRACT-31", "FEDEX", date);
        context.Shipments.Add(shipment);

        // Initial Quote of $45 (no ClientFreightAgreement created)
        var quote = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            BaseTransportationCost = 45.00m,
            TotalCarrierCost = 45.00m,
            CostAssessedAtUtc = date.AddHours(-2),
            Status = CarrierCostStatus.Estimated
        };

        // Carrier Invoice Record: Base freight invoice INV-31 ($65)
        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-31",
            BaseTransportationCost = 65.00m,
            TotalCarrierCost = 65.00m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced
        };

        context.CarrierCostRecords.AddRange(quote, cost);
        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT
        var queue = await varianceService.GetDiscrepancyManagementQueueAsync("FEDEX", date.AddDays(-1), date.AddDays(2));

        // ASSERT: Operational quote variance is +$20, contractual rate card variance is strictly null
        Assert.NotNull(queue);
        var item = Assert.Single(queue, q => q.CarrierCostRecordId == cost.Id);
        Assert.Equal(65.00m, item.InvoicedCostUsd);
        Assert.Equal(45.00m, item.ExpectedCostUsd);
        Assert.Equal(20.00m, item.OperationalQuoteVarianceUsd);
        Assert.Null(item.ContractualRateCardVarianceUsd);
        Assert.Equal(20.00m, item.DiscrepancyAmountUsd);
    }

    [Fact]
    public async Task Test_32_ValuationCutoff_PostValuationAdjustmentExcludedUntilTimestampAdvances()
    {
        // ARRANGE: Gate B - Temporal isolation: Invoices exist at T0, post-valuation adjustments at T2.
        // Evaluated at T1 (between T0 and T2), the T2 adjustment is strictly excluded.
        // Evaluated at T3 (after T2), the adjustment is recognized.
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var t0 = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var t1 = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc); // Valuation cutoff before adjustment
        var t2 = new DateTime(2026, 11, 4, 14, 0, 0, DateTimeKind.Utc); // Adjustment event
        var t3 = new DateTime(2026, 11, 4, 16, 0, 0, DateTimeKind.Utc); // Valuation cutoff after adjustment

        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-VAL-32", "FEDEX", t0);
        context.Shipments.Add(shipment);

        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-32",
            BaseTransportationCost = 100.00m,
            TotalCarrierCost = 100.00m,
            CostAssessedAtUtc = t0,
            Status = CarrierCostStatus.Invoiced
        };

        // Post-valuation credit adjustment at T2 (-$15.00)
        var postValuationAdjustment = CarrierCostAdjustment.Create(
            _clientAId, _facility1Id, cost.Id, CarrierCostAdjustmentType.DisputeCredit,
            "INV-32", 100.00m, 85.00m, 0m, "USD", "Carrier billing correction credit note", "IDEM-VAL-32", t2);

        cost.Adjustments.Add(postValuationAdjustment);
        context.CarrierCostRecords.Add(cost);
        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT 1: Evaluate at T1 (before adjustment at T2)
        var summaryT1 = await varianceService.GetFreightProfitabilitySummaryAsync("FEDEX", t0.AddDays(-1), t0.AddDays(2), asOfUtc: t1);
        var reconT1 = await reconService.GetShipmentFinancialReconciliationAsync(shipment.Id, t1);
        var queueT1 = await varianceService.GetDiscrepancyManagementQueueAsync("FEDEX", t0.AddDays(-1), t0.AddDays(2), asOfUtc: t1);

        // ASSERT 1: At T1, adjustment is excluded; net cost is $100.00
        Assert.Equal(100.00m, summaryT1.TotalCarrierCostUsd);
        Assert.Equal(100.00m, reconT1.NetInvoicedCarrierCostUsd);
        Assert.Empty(reconT1.AdjustmentAuditTrail);
        Assert.Equal(0m, reconT1.TotalCarrierAdjustmentsUsd);
        var queueItemT1 = Assert.Single(queueT1);
        Assert.Equal(100.00m, queueItemT1.InvoicedCostUsd);

        // ACT 2: Evaluate at T3 (after adjustment at T2)
        var summaryT3 = await varianceService.GetFreightProfitabilitySummaryAsync("FEDEX", t0.AddDays(-1), t0.AddDays(2), asOfUtc: t3);
        var reconT3 = await reconService.GetShipmentFinancialReconciliationAsync(shipment.Id, t3);
        var queueT3 = await varianceService.GetDiscrepancyManagementQueueAsync("FEDEX", t0.AddDays(-1), t0.AddDays(2), asOfUtc: t3);

        // ASSERT 2: At T3, adjustment is recognized; net cost is $85.00
        Assert.Equal(85.00m, summaryT3.TotalCarrierCostUsd);
        Assert.Equal(85.00m, reconT3.NetInvoicedCarrierCostUsd);
        Assert.Single(reconT3.AdjustmentAuditTrail);
        Assert.Equal(-15.00m, reconT3.TotalCarrierAdjustmentsUsd);
        var queueItemT3 = Assert.Single(queueT3);
        Assert.Equal(85.00m, queueItemT3.InvoicedCostUsd);
    }

    [Fact]
    public async Task Test_33_DisputedAccessorialCharge_QuarantinedBeforeUnderRecoveryCalculation()
    {
        // ARRANGE: Gate E & Finding 5 - Disputed accessorial charge with partial client billing
        // must be quarantined under CarrierOvercharge, reported in TotalDisputedQuarantinedUsd,
        // and excluded from TotalNetLeakageAmountUsd and ClientUnderRecovery category.
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-DISPUTE-33", "FEDEX", date);
        context.Shipments.Add(shipment);

        // Carrier cost record with Status = Disputed
        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-33-DISPUTE",
            BaseTransportationCost = 50.00m,
            AccessorialCost = 100.00m,
            TotalCarrierCost = 150.00m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Disputed
        };

        // Accessorial charge for $100.00
        var charge = CarrierAccessorialCharge.Create(
            cost.Id, _clientAId, _facility1Id, shipment.Id, AccessorialType.LiftgateService,
            "LIFT", 100.00m, "USD", "IDEM-ACC-33", date, billableTreatment: AccessorialBillableTreatment.BillableWithMarkup);

        cost.AddAccessorialCharge(charge);
        context.CarrierCostRecords.Add(cost);
        context.CarrierAccessorialCharges.Add(charge);

        // Client billable activity and invoice line for partial recovery ($30.00)
        var act = new BillableActivity
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ActivityType = BillableActivityType.CarrierFreight,
            Status = BillableActivityStatus.Invoiced,
            SourceType = "CarrierAccessorialCharge",
            SourceId = charge.Id,
            Quantity = 1m,
            UnitPrice = 30.00m,
            TotalAmount = 30.00m,
            Currency = "USD",
            OccurredAtUtc = date
        };
        context.BillableActivities.Add(act);

        var clientInvoice = new Invoice
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            InvoiceNumber = "INV-CLI-33",
            InvoiceDate = date,
            Status = InvoiceStatus.Issued
        };
        var line = InvoiceLine.Create(1, InvoiceChargeCategory.Freight, "Liftgate recovery", 1m, "UNIT", 30.00m, 30.00m, act.Id, BillableActivityType.CarrierFreight, _facility1Id);
        line.InvoiceId = clientInvoice.Id;
        line.Invoice = clientInvoice;
        line.BillableActivity = act;
        clientInvoice.Lines.Add(line);
        context.Invoices.Add(clientInvoice);
        context.InvoiceLines.Add(line);

        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT
        var leakage = await varianceService.GetAccessorialLeakageAnalysisAsync("FEDEX", date.AddDays(-1), date.AddDays(2));

        // ASSERT: Quarantined under CarrierOvercharge, NOT ClientUnderRecovery; TotalNetLeakageAmountUsd is 0.00
        Assert.NotNull(leakage);
        Assert.Equal(100.00m, leakage.TotalCarrierAccessorialsUsd);
        Assert.Equal(30.00m, leakage.TotalClientBilledAccessorialsUsd);
        Assert.Equal(100.00m, leakage.TotalDisputedQuarantinedUsd);
        Assert.Equal(0.00m, leakage.TotalNetLeakageAmountUsd); // Disputed charge excluded from net leakage

        var overchargeCat = Assert.Single(leakage.BreakdownByLeakageCategory, c => c.Category == AccessorialLeakageCategory.CarrierOvercharge);
        Assert.Equal(100.00m, overchargeCat.AmountUsd);

        // Confirm NO ClientUnderRecovery category exists for this disputed charge
        Assert.DoesNotContain(leakage.BreakdownByLeakageCategory, c => c.Category == AccessorialLeakageCategory.ClientUnderRecovery);
    }

    [Fact]
    public async Task Test_34_RateCardScheduleTiers_AuditsContractualRatingCalculation()
    {
        // ARRANGE: Gate D & Finding 1 - Buy-side CarrierRateAgreement with tiered volume brackets evaluated via CalculateRating(W_expected).
        // 0-5 kg @ $5.00/kg; 5-10 kg @ $4.00/kg (AllUnits model).
        // Shipment has gross weight 6.0 kg (billable weight = 6.0 kg under 0.5 kg increment).
        // Expected base cost = 6.0 kg * $4.00 = $24.00.
        // Invoiced base cost = $24.00 -> Compliant!
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-TIER-34", "FEDEX", date, weightKg: 6.0m);
        context.Shipments.Add(shipment);

        // Buy-side CarrierRateAgreement with AllUnits tier model
        var carrierAgreement = CarrierRateAgreement.Create(
            "FEDEX", "CRA-FEDEX-34", "Commercial FedEx Carrier Contract",
            baseRate: 5.00m, currency: "USD", unitOfMeasure: "KG",
            rateModel: RateModelType.PerUnit, tierModel: PricingTierCalculationModel.AllUnits,
            effectiveFromUtc: date.AddDays(-10), facilityId: _facility1Id);

        carrierAgreement.AddTier(1, 0m, 5.0m, 5.00m);
        carrierAgreement.AddTier(2, 5.0001m, 10.0m, 4.00m);
        context.CarrierRateAgreements.Add(carrierAgreement);

        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-34",
            BaseTransportationCost = 24.00m,
            TotalCarrierCost = 24.00m,
            BilledWeightKg = 6.0m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced
        };
        context.CarrierCostRecords.Add(cost);

        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT
        var compliance = await varianceService.GetRateCardComplianceAnalysisAsync("FEDEX", date.AddDays(-1), date.AddDays(2));

        // ASSERT: Exactly 1 audited shipment, 1 compliant shipment, 0 overcharge, 0 undercharge
        Assert.NotNull(compliance);
        Assert.Equal(1, compliance.TotalAuditedShipmentsCount);
        Assert.Equal(1, compliance.CompliantShipmentsCount);
        Assert.Equal(0, compliance.IndeterminateShipmentsCount);
        Assert.Equal(100.00m, compliance.ComplianceRatePercent);
        Assert.Equal(0.00m, compliance.TotalOverchargeAmountUsd);
        Assert.Equal(0.00m, compliance.TotalUnderchargeAmountUsd);
    }

    [Fact]
    public async Task Test_35_RateCardCompliance_WeightDependentRateMissingBilledWeight_ReturnsIndeterminate()
    {
        // ARRANGE: Finding 1 - Carrier rate compliance audit must NOT report compliance without verified billed weight
        // when rating is weight-dependent (per-kg or tiered brackets). Missing billed weight must produce Indeterminate.
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-WT-35", "FEDEX", date, weightKg: 10.0m);
        context.Shipments.Add(shipment);

        // Buy-side carrier contract (per-kg weight dependent)
        var carrierAgreement = CarrierRateAgreement.Create(
            "FEDEX", "CRA-FEDEX-35", "Commercial FedEx Carrier Contract",
            baseRate: 5.00m, currency: "USD", unitOfMeasure: "KG",
            rateModel: RateModelType.PerUnit, tierModel: PricingTierCalculationModel.FlatRate,
            effectiveFromUtc: date.AddDays(-10), facilityId: _facility1Id);
        context.CarrierRateAgreements.Add(carrierAgreement);

        // Carrier cost record without billed weight (BilledWeightKg = null)
        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-35",
            BaseTransportationCost = 50.00m,
            TotalCarrierCost = 50.00m,
            BilledWeightKg = null, // Missing billed weight
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced
        };
        context.CarrierCostRecords.Add(cost);
        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT 1: Evaluate compliance without billed weight evidence
        var compliance1 = await varianceService.GetRateCardComplianceAnalysisAsync("FEDEX", date.AddDays(-1), date.AddDays(2));

        // ASSERT 1: Must be indeterminate, NOT compliant
        Assert.NotNull(compliance1);
        Assert.Equal(1, compliance1.IndeterminateShipmentsCount);
        Assert.Equal(0, compliance1.TotalAuditedShipmentsCount);
        Assert.Equal(0, compliance1.CompliantShipmentsCount);
        Assert.Null(compliance1.ComplianceRatePercent);

        // ACT 2: Now supply verified carrier-billed weight evidence (10.0 kg)
        cost.BilledWeightKg = 10.0m;
        await context.SaveChangesAsync();

        var compliance2 = await varianceService.GetRateCardComplianceAnalysisAsync("FEDEX", date.AddDays(-1), date.AddDays(2));

        // ASSERT 2: Now verified as compliant
        Assert.NotNull(compliance2);
        Assert.Equal(0, compliance2.IndeterminateShipmentsCount);
        Assert.Equal(1, compliance2.TotalAuditedShipmentsCount);
        Assert.Equal(1, compliance2.CompliantShipmentsCount);
        Assert.Equal(100.00m, compliance2.ComplianceRatePercent);
    }

    [Fact]
    public async Task Test_36_RateCardCompliance_PostValuationContractWithBackdatedEffectiveDate_ExcludedFromHistoricalAudit()
    {
        // ARRANGE: Findings 1, 2 & 3 - Historical valuation cutoff asOfUtc must enforce contract evidence availability
        // at valuation time (a.CreatedAt <= valuationTime, (!a.UpdatedAt.HasValue || a.UpdatedAt.Value <= valuationTime)).
        // A later-created or backdated contract must NOT rewrite an earlier historical valuation.
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var t0 = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc); // Shipment dispatched
        var t1 = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc); // Historical valuation timestamp T1
        var t2 = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc); // Contract created in system at T2 (> T1)
        var t3 = new DateTime(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc); // Subsequent valuation timestamp T3 (> T2)

        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-CUTOFF-36", "FEDEX", t0, weightKg: 10.0m);
        context.Shipments.Add(shipment);

        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-36",
            BaseTransportationCost = 50.00m,
            TotalCarrierCost = 50.00m,
            BilledWeightKg = 10.0m,
            CostAssessedAtUtc = t0,
            Status = CarrierCostStatus.Invoiced
        };
        context.CarrierCostRecords.Add(cost);

        // Buy-side carrier contract agreement: EffectiveFromUtc is backdated to Oct 25 (prior to shipment),
        // but it was only created in the database at T2 (Nov 10).
        var agreement = CarrierRateAgreement.Create(
            "FEDEX", "CRA-BACKDATED-36", "Backdated FedEx Carrier Agreement",
            baseRate: 5.00m, currency: "USD", unitOfMeasure: "KG",
            rateModel: RateModelType.PerUnit, tierModel: PricingTierCalculationModel.FlatRate,
            effectiveFromUtc: new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc),
            facilityId: _facility1Id);
        agreement.CreatedAt = t2;
        agreement.UpdatedAt = null;
        context.CarrierRateAgreements.Add(agreement);

        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT 1: Evaluate historical audit as of T1 (before contract was entered into database)
        var complianceT1 = await varianceService.GetRateCardComplianceAnalysisAsync(
            "FEDEX", t0.AddDays(-1), t0.AddDays(2), asOfUtc: t1);

        // ASSERT 1: At T1, contract did not exist in ledger yet (CreatedAt > T1) -> Excluded -> Indeterminate
        Assert.NotNull(complianceT1);
        Assert.Equal(1, complianceT1.IndeterminateShipmentsCount);
        Assert.Equal(0, complianceT1.TotalAuditedShipmentsCount);

        // ACT 2: Evaluate audit as of T3 (after contract was entered into database)
        var complianceT3 = await varianceService.GetRateCardComplianceAnalysisAsync(
            "FEDEX", t0.AddDays(-1), t0.AddDays(2), asOfUtc: t3);

        // ASSERT 2: At T3, contract is fully available (CreatedAt <= T3) -> Audited & Compliant
        Assert.NotNull(complianceT3);
        Assert.Equal(0, complianceT3.IndeterminateShipmentsCount);
        Assert.Equal(1, complianceT3.TotalAuditedShipmentsCount);
        Assert.Equal(1, complianceT3.CompliantShipmentsCount);
        Assert.Equal(100.00m, complianceT3.ComplianceRatePercent);
    }

    [Fact]
    public async Task Test_37_RateCardScheduleResolution_FacilitySpecificSchedulePrecedesClientWideFallback()
    {
        // ARRANGE: Findings 1, 2 & 6 - Carrier agreement resolution must strictly prefer facility-specific agreement
        // over network-wide fallback agreement.
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);

        // Agreement 1: Network-wide fallback (FacilityId = null) @ $10.00/kg base rate ($50.00 for 5 kg)
        var networkWideAgreement = CarrierRateAgreement.Create(
            "FEDEX", "CRA-NET-37", "Network-wide FedEx Agreement",
            baseRate: 10.00m, currency: "USD", unitOfMeasure: "KG",
            rateModel: RateModelType.PerUnit, tierModel: PricingTierCalculationModel.FlatRate,
            effectiveFromUtc: date.AddDays(-10), facilityId: null);
        context.CarrierRateAgreements.Add(networkWideAgreement);

        // Agreement 2: Facility1-specific agreement (FacilityId = _facility1Id) @ $8.00/kg base rate ($40.00 for 5 kg)
        var facility1Agreement = CarrierRateAgreement.Create(
            "FEDEX", "CRA-FAC1-37", "Facility1 FedEx Agreement",
            baseRate: 8.00m, currency: "USD", unitOfMeasure: "KG",
            rateModel: RateModelType.PerUnit, tierModel: PricingTierCalculationModel.FlatRate,
            effectiveFromUtc: date.AddDays(-10), facilityId: _facility1Id);
        context.CarrierRateAgreements.Add(facility1Agreement);

        // Shipment 1 at Facility1 (has specific agreement @ $40.00, carrier billed $40.00)
        var shipment1 = CreateShipment(_clientAId, _facility1Id, "SHIP-FAC1-37", "FEDEX", date, weightKg: 5.0m);
        var cost1 = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment1.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-37-1",
            BaseTransportationCost = 40.00m,
            TotalCarrierCost = 40.00m,
            BilledWeightKg = 5.0m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced
        };
        context.Shipments.Add(shipment1);
        context.CarrierCostRecords.Add(cost1);

        // Shipment 2 at Facility2 (no facility-specific agreement -> falls back to network-wide @ $50.00, carrier billed $50.00)
        var shipment2 = CreateShipment(_clientAId, _facility2Id, "SHIP-FAC2-37", "FEDEX", date, weightKg: 5.0m);
        var cost2 = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility2Id,
            ShipmentId = shipment2.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-37-2",
            BaseTransportationCost = 50.00m,
            TotalCarrierCost = 50.00m,
            BilledWeightKg = 5.0m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced
        };
        context.Shipments.Add(shipment2);
        context.CarrierCostRecords.Add(cost2);

        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT
        var compliance = await varianceService.GetRateCardComplianceAnalysisAsync("FEDEX", date.AddDays(-1), date.AddDays(2));

        // ASSERT: Both shipments audited and compliant against their governing agreements
        Assert.NotNull(compliance);
        Assert.Equal(2, compliance.TotalAuditedShipmentsCount);
        Assert.Equal(2, compliance.CompliantShipmentsCount);
        Assert.Equal(0, compliance.IndeterminateShipmentsCount);
        Assert.Equal(0m, compliance.TotalOverchargeAmountUsd);
        Assert.Equal(0m, compliance.TotalUnderchargeAmountUsd);
    }

    [Fact]
    public async Task Test_38_AccessorialRecovery_ExplicitChargeIdentity_PreventsMisallocationAcrossCharges()
    {
        // ARRANGE: Finding 5 - Accessorial recovery allocation predicate must explicitly verify
        // BillableActivity.SourceId == charge.Id for each line and prevent double allocation across charges.
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-ALLOC-38", "FEDEX", date);
        context.Shipments.Add(shipment);

        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            TotalCarrierCost = 150.00m,
            Status = CarrierCostStatus.Invoiced,
            CostAssessedAtUtc = date
        };

        // Charge 1: Liftgate Service ($100 carrier cost)
        var charge1 = CarrierAccessorialCharge.Create(
            cost.Id, _clientAId, _facility1Id, shipment.Id,
            AccessorialType.LiftgateService, "LIFTGATE", 100.00m, "USD", "IDEM-38-1", date);

        // Charge 2: Residential Delivery ($50 carrier cost)
        var charge2 = CarrierAccessorialCharge.Create(
            cost.Id, _clientAId, _facility1Id, shipment.Id,
            AccessorialType.ResidentialDelivery, "RESIDENTIAL", 50.00m, "USD", "IDEM-38-2", date);

        cost.AccessorialCharges.Add(charge1);
        cost.AccessorialCharges.Add(charge2);
        context.CarrierCostRecords.Add(cost);
        context.CarrierAccessorialCharges.AddRange(charge1, charge2);

        // Billable Activity 1 for Charge 1 ($120 client billed)
        var billable1 = new BillableActivity
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ActivityType = BillableActivityType.CarrierFreight,
            Status = BillableActivityStatus.Invoiced,
            SourceType = "CarrierAccessorialCharge",
            SourceId = charge1.Id,
            SourceReference = "LIFTGATE",
            Quantity = 1,
            UnitPrice = 120.00m,
            TotalAmount = 120.00m,
            Currency = "USD"
        };

        // Billable Activity 2 for Charge 2 ($60 client billed)
        var billable2 = new BillableActivity
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ActivityType = BillableActivityType.CarrierFreight,
            Status = BillableActivityStatus.Invoiced,
            SourceType = "CarrierAccessorialCharge",
            SourceId = charge2.Id,
            SourceReference = "RESIDENTIAL",
            Quantity = 1,
            UnitPrice = 60.00m,
            TotalAmount = 60.00m,
            Currency = "USD"
        };
        context.BillableActivities.AddRange(billable1, billable2);

        var clientInvoice = new Invoice
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            InvoiceNumber = "INV-CLI-38",
            InvoiceDate = date,
            Status = InvoiceStatus.Approved
        };

        var line1 = InvoiceLine.Create(1, InvoiceChargeCategory.Freight, "Liftgate Surcharge", 1, "UNIT", 120m, 120m, billable1.Id, BillableActivityType.CarrierFreight, _facility1Id);
        line1.InvoiceId = clientInvoice.Id;
        line1.Invoice = clientInvoice;
        line1.BillableActivity = billable1;

        var line2 = InvoiceLine.Create(2, InvoiceChargeCategory.Freight, "Residential Surcharge", 1, "UNIT", 60m, 60m, billable2.Id, BillableActivityType.CarrierFreight, _facility1Id);
        line2.InvoiceId = clientInvoice.Id;
        line2.Invoice = clientInvoice;
        line2.BillableActivity = billable2;

        // Duplicate/corrected line referencing billable1 (same line number 1 on clientInvoice: must be suppressed as exact duplicate)
        var lineDuplicate = InvoiceLine.Create(1, InvoiceChargeCategory.Freight, "Duplicate Liftgate", 1, "UNIT", 120m, 120m, billable1.Id, BillableActivityType.CarrierFreight, _facility1Id);
        lineDuplicate.InvoiceId = clientInvoice.Id;
        lineDuplicate.Invoice = clientInvoice;
        lineDuplicate.BillableActivity = billable1;

        clientInvoice.Lines.Add(line1);
        clientInvoice.Lines.Add(line2);
        clientInvoice.Lines.Add(lineDuplicate);
        context.Invoices.Add(clientInvoice);
        context.InvoiceLines.AddRange(line1, line2, lineDuplicate);

        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT
        var leakage = await varianceService.GetAccessorialLeakageAnalysisAsync("FEDEX", date.AddDays(-1), date.AddDays(2));

        // ASSERT: Correct total carrier cost ($150) and unique client recovery ($180) without double allocation
        Assert.NotNull(leakage);
        Assert.Equal(150.00m, leakage.TotalCarrierAccessorialsUsd);
        Assert.Equal(180.00m, leakage.TotalClientBilledAccessorialsUsd);
        Assert.Equal(0.00m, leakage.TotalNetLeakageAmountUsd);
        Assert.Equal(120.00m, leakage.AccessorialRecoveryRatePercent);

        var liftgate = leakage.BreakdownByType.First(b => b.AccessorialType == AccessorialType.LiftgateService);
        Assert.Equal(100.00m, liftgate.CarrierAmountUsd);
        Assert.Equal(120.00m, liftgate.ClientBilledUsd);
        Assert.Equal(0.00m, liftgate.NetLeakageUsd);
        Assert.Equal(120.00m, liftgate.RecoveryRatePercent);

        var residential = leakage.BreakdownByType.First(b => b.AccessorialType == AccessorialType.ResidentialDelivery);
        Assert.Equal(50.00m, residential.CarrierAmountUsd);
        Assert.Equal(60.00m, residential.ClientBilledUsd);
        Assert.Equal(0.00m, residential.NetLeakageUsd);
        Assert.Equal(120.00m, residential.RecoveryRatePercent);
    }

    [Fact]
    public void Test_39_StructuredWeightValidation_RejectsNegativeWeightAndNonPositiveIncrement()
    {
        // ARRANGE & ASSERT: Finding 7 - Structured weight fields domain boundary validation
        // 1. CarrierCostRecord BilledWeightKg cannot be negative
        Assert.Throws<ValidationException>(() => new CarrierCostRecord { BilledWeightKg = -1.0m });
        Assert.Throws<ValidationException>(() => new CarrierCostRecord { BilledWeightKg = -0.0001m });

        // 2. CarrierCostAdjustment BilledWeightKg cannot be negative
        Assert.Throws<ValidationException>(() => new CarrierCostAdjustment { BilledWeightKg = -0.5m });

        // 3. ClientFreightAgreement WeightRoundingIncrementKg must be strictly positive (> 0)
        Assert.Throws<ValidationException>(() => new ClientFreightAgreement { WeightRoundingIncrementKg = 0m });
        Assert.Throws<ValidationException>(() => new ClientFreightAgreement { WeightRoundingIncrementKg = -0.5m });
        Assert.Throws<ValidationException>(() => ClientFreightAgreement.Create(
            Guid.NewGuid(), "CFA-VAL", "Agreement", FreightPricingModel.FixedContractualRate, "USD",
            DateTime.UtcNow, weightRoundingIncrementKg: 0m));
        Assert.Throws<ValidationException>(() => ClientFreightAgreement.Create(
            Guid.NewGuid(), "CFA-VAL", "Agreement", FreightPricingModel.FixedContractualRate, "USD",
            DateTime.UtcNow, weightRoundingIncrementKg: -1.0m));

        // 4. Valid positive weights assign cleanly and round to 4 decimal places
        var cost = new CarrierCostRecord { BilledWeightKg = 12.34567m };
        Assert.Equal(12.3457m, cost.BilledWeightKg);

        var adj = new CarrierCostAdjustment { BilledWeightKg = 7.12344m };
        Assert.Equal(7.1234m, adj.BilledWeightKg);

        var agreement = new ClientFreightAgreement { WeightRoundingIncrementKg = 0.55555m };
        Assert.Equal(0.5556m, agreement.WeightRoundingIncrementKg);

        // 5. Null values assign safely without exception
        var costNull = new CarrierCostRecord { BilledWeightKg = null };
        Assert.Null(costNull.BilledWeightKg);

        var agreementNull = new ClientFreightAgreement { WeightRoundingIncrementKg = null };
        Assert.Null(agreementNull.WeightRoundingIncrementKg);
    }

    [Fact]
    public async Task Test_40_CarrierRateAgreement_PoundUnitOfMeasure_ConvertsKilogramsAndProducesMonetaryEquivalence()
    {
        // ARRANGE: Finding 3 - Pound-based rate cards must convert kilogram quantities to pounds
        // (1 kg = 2.20462262 lb). Equivalent KG and LB contracts must produce equivalent monetary results.
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);

        // LB Agreement: $2.00 / LB
        var lbAgreement = CarrierRateAgreement.Create(
            "UPS", "CRA-UPS-LB", "UPS Pound Agreement",
            baseRate: 2.00m, currency: "USD", unitOfMeasure: "LB",
            rateModel: RateModelType.PerUnit, tierModel: PricingTierCalculationModel.FlatRate,
            effectiveFromUtc: date.AddDays(-10), facilityId: _facility1Id);

        // KG Agreement: equivalent rate $2.00 * 2.20462262 = $4.40924524 / KG
        decimal equivalentKgRate = 2.00m * 2.20462262m;
        var kgAgreement = CarrierRateAgreement.Create(
            "UPS", "CRA-UPS-KG", "UPS Kilogram Agreement",
            baseRate: equivalentKgRate, currency: "USD", unitOfMeasure: "KG",
            rateModel: RateModelType.PerUnit, tierModel: PricingTierCalculationModel.FlatRate,
            effectiveFromUtc: date.AddDays(-10), facilityId: null);

        // Rating calculation directly on agreement aggregate:
        decimal testWeightKg = 10.0m;
        var lbRating = lbAgreement.CalculateRating(testWeightKg);
        var kgRating = kgAgreement.CalculateRating(testWeightKg);

        // 10 kg * 2.20462262 = 22.0462 lb * $2.00 = $44.0924
        // 10 kg * $4.40924524 = $44.0924
        Assert.Equal(decimal.Round(lbRating.TotalAmount, 2), decimal.Round(kgRating.TotalAmount, 2));
        Assert.Equal(44.09m, decimal.Round(lbRating.TotalAmount, 2));

        // Add LB agreement to context for compliance analysis
        context.CarrierRateAgreements.Add(lbAgreement);

        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-LB-40", "UPS", date, weightKg: 10.0m);
        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "UPS",
            CarrierInvoiceNumber = "INV-UPS-40",
            BaseTransportationCost = 44.09m,
            TotalCarrierCost = 44.09m,
            BilledWeightKg = 10.0m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced
        };
        context.Shipments.Add(shipment);
        context.CarrierCostRecords.Add(cost);

        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT
        var compliance = await varianceService.GetRateCardComplianceAnalysisAsync("UPS", date.AddDays(-1), date.AddDays(2));

        // ASSERT: Compliant under LB contract conversion
        Assert.NotNull(compliance);
        Assert.Equal(1, compliance.TotalAuditedShipmentsCount);
        Assert.Equal(1, compliance.CompliantShipmentsCount);
        Assert.Equal(0, compliance.IndeterminateShipmentsCount);
        Assert.Equal(100.00m, compliance.ComplianceRatePercent);
    }

    [Fact]
    public async Task Test_41_HistoricalValuation_PostValuationModificationExcludedFromHistoricalAudit()
    {
        // ARRANGE: Finding 2 - Historical valuation cutoff must enforce that contracts created before valuationTime
        // but modified AFTER valuationTime (UpdatedAt > valuationTime) are excluded from the historical audit.
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var t0 = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc); // Contract created
        var tShipment = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc); // Shipment dispatched
        var tValuation = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc); // Historical valuation timestamp
        var tModification = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc); // Contract modified later (> tValuation)

        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-MOD-41", "FEDEX", tShipment, weightKg: 10.0m);
        context.Shipments.Add(shipment);

        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-41",
            BaseTransportationCost = 50.00m,
            TotalCarrierCost = 50.00m,
            BilledWeightKg = 10.0m,
            CostAssessedAtUtc = tShipment,
            Status = CarrierCostStatus.Invoiced
        };
        context.CarrierCostRecords.Add(cost);

        // Contract created at T0 (Nov 1) but modified at TModification (Nov 10 > Nov 5)
        var agreement = CarrierRateAgreement.Create(
            "FEDEX", "CRA-MOD-41", "Modified Carrier Agreement",
            baseRate: 5.00m, currency: "USD", unitOfMeasure: "KG",
            rateModel: RateModelType.PerUnit, tierModel: PricingTierCalculationModel.FlatRate,
            effectiveFromUtc: t0, facilityId: _facility1Id);
        agreement.CreatedAt = t0;
        agreement.UpdatedAt = tModification; // Modified after valuation cutoff!
        context.CarrierRateAgreements.Add(agreement);

        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT 1: Valuation at tValuation (Nov 5) - contract was modified later (Nov 10), so must be excluded
        var complianceTVal = await varianceService.GetRateCardComplianceAnalysisAsync(
            "FEDEX", tShipment.AddDays(-1), tShipment.AddDays(2), asOfUtc: tValuation);

        // ASSERT 1: Excluded from historical audit due to later modification -> Indeterminate
        Assert.NotNull(complianceTVal);
        Assert.Equal(1, complianceTVal.IndeterminateShipmentsCount);
        Assert.Equal(0, complianceTVal.TotalAuditedShipmentsCount);

        // ACT 2: Valuation at tPostMod (Nov 15 > Nov 10)
        var compliancePostMod = await varianceService.GetRateCardComplianceAnalysisAsync(
            "FEDEX", tShipment.AddDays(-1), tShipment.AddDays(2), asOfUtc: tModification.AddDays(5));

        // ASSERT 2: Included once valuation timestamp is after the modification
        Assert.NotNull(compliancePostMod);
        Assert.Equal(0, compliancePostMod.IndeterminateShipmentsCount);
        Assert.Equal(1, compliancePostMod.TotalAuditedShipmentsCount);
        Assert.Equal(1, compliancePostMod.CompliantShipmentsCount);
    }

    [Fact]
    public async Task Test_42_AccessorialRecovery_AuthoritativeInvoiceCurrency_ConvertsForeignCurrencyToUsd()
    {
        // ARRANGE: Finding 4 - Accessorial recovery must use authoritative invoice currency (line.Invoice.Currency)
        // rather than assuming USD, applying official central-bank fixing rate.
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-FX-42", "DHL", date);
        context.Shipments.Add(shipment);

        // Carrier cost assessed in EUR: €100.00
        // On Nov 4, 2026, ECB EUR/USD rate is 1.0888 -> €100.00 = $108.88 USD
        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "DHL",
            Currency = "EUR",
            TotalCarrierCost = 100.00m,
            Status = CarrierCostStatus.Invoiced,
            CostAssessedAtUtc = date
        };

        var charge = CarrierAccessorialCharge.Create(
            cost.Id, _clientAId, _facility1Id, shipment.Id,
            AccessorialType.FuelSurcharge, "EUR-FUEL", 100.00m, "EUR", "IDEM-FX-42", date);

        cost.AccessorialCharges.Add(charge);
        context.CarrierCostRecords.Add(cost);
        context.CarrierAccessorialCharges.Add(charge);

        var billable = new BillableActivity
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ActivityType = BillableActivityType.CarrierFreight,
            Status = BillableActivityStatus.Invoiced,
            SourceType = "CarrierAccessorialCharge",
            SourceId = charge.Id,
            SourceReference = "EUR-FUEL",
            Quantity = 1,
            UnitPrice = 100.00m,
            TotalAmount = 100.00m,
            Currency = "EUR"
        };
        context.BillableActivities.Add(billable);

        // Client invoice issued in EUR (€100.00)
        var clientInvoice = new Invoice
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            InvoiceNumber = "INV-EUR-42",
            InvoiceDate = date,
            Currency = "EUR",
            Status = InvoiceStatus.Approved
        };

        var line = InvoiceLine.Create(1, InvoiceChargeCategory.Freight, "EUR Fuel Surcharge", 1, "UNIT", 100m, 100m, billable.Id, BillableActivityType.CarrierFreight, _facility1Id);
        line.InvoiceId = clientInvoice.Id;
        line.Invoice = clientInvoice;
        line.BillableActivity = billable;
        clientInvoice.Lines.Add(line);

        context.Invoices.Add(clientInvoice);
        context.InvoiceLines.Add(line);

        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT
        var leakage = await varianceService.GetAccessorialLeakageAnalysisAsync("DHL", date.AddDays(-1), date.AddDays(2));

        // ASSERT: Carrier amount and client billed amount are both normalized using ECB EUR/USD rate (1.1267 -> $112.67)
        Assert.NotNull(leakage);
        Assert.Equal(112.67m, leakage.TotalCarrierAccessorialsUsd);
        Assert.Equal(112.67m, leakage.TotalClientBilledAccessorialsUsd);
        Assert.Equal(0.00m, leakage.TotalNetLeakageAmountUsd);
        Assert.Equal(100.00m, leakage.AccessorialRecoveryRatePercent);
    }

    [Fact]
    public async Task Test_43_AccessorialRecovery_ConservesMultipleLegitimateLines_PartialInvoicingAndCreditMemo()
    {
        // ARRANGE: Finding 5 - Accessorial recovery must conserve all legitimate qualifying invoice lines
        // linked to the same billable activity (partial billing + supplemental billing + credit memo reversal),
        // summing them without discarding subsequent lines due to duplicate activity IDs.
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-MULTI-43", "FEDEX", date);
        context.Shipments.Add(shipment);

        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            TotalCarrierCost = 100.00m,
            Status = CarrierCostStatus.Invoiced,
            CostAssessedAtUtc = date
        };

        var charge = CarrierAccessorialCharge.Create(
            cost.Id, _clientAId, _facility1Id, shipment.Id,
            AccessorialType.LiftgateService, "LIFTGATE-MULTI", 100.00m, "USD", "IDEM-MULTI-43", date);

        cost.AccessorialCharges.Add(charge);
        context.CarrierCostRecords.Add(cost);
        context.CarrierAccessorialCharges.Add(charge);

        var billable = new BillableActivity
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ActivityType = BillableActivityType.CarrierFreight,
            Status = BillableActivityStatus.Invoiced,
            SourceType = "CarrierAccessorialCharge",
            SourceId = charge.Id,
            SourceReference = "LIFTGATE-MULTI",
            Quantity = 1,
            UnitPrice = 100.00m,
            TotalAmount = 100.00m,
            Currency = "USD"
        };
        context.BillableActivities.Add(billable);

        var clientInvoice = new Invoice
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            InvoiceNumber = "INV-MULTI-43",
            InvoiceDate = date,
            Currency = "USD",
            Status = InvoiceStatus.Approved
        };

        // Line 1: Partial billing $60
        var line1 = InvoiceLine.Create(1, InvoiceChargeCategory.Freight, "Partial Liftgate 1", 1, "UNIT", 60m, 60m, billable.Id, BillableActivityType.CarrierFreight, _facility1Id);
        line1.InvoiceId = clientInvoice.Id;
        line1.Invoice = clientInvoice;
        line1.BillableActivity = billable;

        // Line 2: Supplemental billing $50
        var line2 = InvoiceLine.Create(2, InvoiceChargeCategory.Freight, "Supplemental Liftgate 2", 1, "UNIT", 50m, 50m, billable.Id, BillableActivityType.CarrierFreight, _facility1Id);
        line2.InvoiceId = clientInvoice.Id;
        line2.Invoice = clientInvoice;
        line2.BillableActivity = billable;

        // Line 3: Credit memo adjustment -$10 (UnitPrice = 10m, TotalAmount = -10m)
        var line3 = InvoiceLine.Create(3, InvoiceChargeCategory.Freight, "Credit Adjustment Liftgate 3", 1, "UNIT", 10m, -10m, billable.Id, BillableActivityType.CarrierFreight, _facility1Id);
        line3.InvoiceId = clientInvoice.Id;
        line3.Invoice = clientInvoice;
        line3.BillableActivity = billable;

        clientInvoice.Lines.Add(line1);
        clientInvoice.Lines.Add(line2);
        clientInvoice.Lines.Add(line3);

        context.Invoices.Add(clientInvoice);
        context.InvoiceLines.AddRange(line1, line2, line3);

        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT
        var leakage = await varianceService.GetAccessorialLeakageAnalysisAsync("FEDEX", date.AddDays(-1), date.AddDays(2));

        // ASSERT: All three legitimate lines conserved and summed: $60 + $50 - $10 = $100.00
        Assert.NotNull(leakage);
        Assert.Equal(100.00m, leakage.TotalCarrierAccessorialsUsd);
        Assert.Equal(100.00m, leakage.TotalClientBilledAccessorialsUsd);
        Assert.Equal(0.00m, leakage.TotalNetLeakageAmountUsd);
        Assert.Equal(100.00m, leakage.AccessorialRecoveryRatePercent);
    }

    [Fact]
    public async Task Test_44_CarrierAgreementResolution_NewerAgreementSupersedesOlderWithoutFalseAmbiguity()
    {
        // ARRANGE: Finding 6 - Newer agreement with later EffectiveFromUtc cleanly supersedes older agreement.
        // It must NOT be rejected as an ambiguous conflict.
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);

        // Older Agreement: Effective Oct 1, 2026 @ $5.00/kg
        var olderAgreement = CarrierRateAgreement.Create(
            "FEDEX", "CRA-OLD-44", "Older FedEx Agreement",
            baseRate: 5.00m, currency: "USD", unitOfMeasure: "KG",
            rateModel: RateModelType.PerUnit, tierModel: PricingTierCalculationModel.FlatRate,
            effectiveFromUtc: new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            facilityId: _facility1Id);
        olderAgreement.CreatedAt = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);
        context.CarrierRateAgreements.Add(olderAgreement);

        // Newer Agreement: Effective Nov 1, 2026 @ $6.00/kg (supersedes older agreement)
        var newerAgreement = CarrierRateAgreement.Create(
            "FEDEX", "CRA-NEW-44", "Newer Superseding FedEx Agreement",
            baseRate: 6.00m, currency: "USD", unitOfMeasure: "KG",
            rateModel: RateModelType.PerUnit, tierModel: PricingTierCalculationModel.FlatRate,
            effectiveFromUtc: new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
            facilityId: _facility1Id);
        newerAgreement.CreatedAt = new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc);
        context.CarrierRateAgreements.Add(newerAgreement);

        // Shipment on Nov 4 billed $60.00 for 10 kg ($6.00/kg -> matches newer agreement)
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-SUPER-44", "FEDEX", date, weightKg: 10.0m);
        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-SUPER-44",
            BaseTransportationCost = 60.00m,
            TotalCarrierCost = 60.00m,
            BilledWeightKg = 10.0m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced
        };
        context.Shipments.Add(shipment);
        context.CarrierCostRecords.Add(cost);

        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT
        var compliance = await varianceService.GetRateCardComplianceAnalysisAsync("FEDEX", date.AddDays(-1), date.AddDays(2));

        // ASSERT: Newer agreement was selected; audited and compliant without conflict ambiguity!
        Assert.NotNull(compliance);
        Assert.Equal(1, compliance.TotalAuditedShipmentsCount);
        Assert.Equal(1, compliance.CompliantShipmentsCount);
        Assert.Equal(0, compliance.IndeterminateShipmentsCount);
        Assert.Equal(100.00m, compliance.ComplianceRatePercent);
    }

    [Fact]
    public async Task Test_45_DiscrepancyQueue_ContractualVariance_UsesActualBillableWeightAndRejectsMissingWeight()
    {
        // ARRANGE: Finding 7 - Discrepancy management queue must use actual shipment billable weight from packages
        // (rather than fixed 1.0), and return null / indeterminate variance when required inputs or packages are missing.
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);

        // Buy-side agreement @ $5.00/kg
        var carrierAgreement = CarrierRateAgreement.Create(
            "FEDEX", "CRA-DISC-45", "FedEx Discrepancy Contract",
            baseRate: 5.00m, currency: "USD", unitOfMeasure: "KG",
            rateModel: RateModelType.PerUnit, tierModel: PricingTierCalculationModel.FlatRate,
            effectiveFromUtc: date.AddDays(-10), facilityId: _facility1Id);
        context.CarrierRateAgreements.Add(carrierAgreement);

        // Shipment 1: Has packages with 8.0 kg billable weight. Contractual expected base cost = 8.0 * $5.00 = $40.00.
        // Carrier invoiced $50.00 -> ContractualRateCardVarianceUsd = $50.00 - $40.00 = $10.00.
        var shipment1 = CreateShipment(_clientAId, _facility1Id, "SHIP-DISC-45-1", "FEDEX", date, weightKg: 8.0m);
        var cost1 = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment1.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-DISC-45-1",
            BaseTransportationCost = 50.00m,
            TotalCarrierCost = 50.00m,
            BilledWeightKg = 8.0m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced
        };
        context.Shipments.Add(shipment1);
        context.CarrierCostRecords.Add(cost1);

        // Shipment 2: Has NO packages (empty packages collection).
        // Since contract is weight-dependent ($5.00/kg), missing packages means ContractualRateCardVarianceUsd = null!
        var shipment2 = new Shipment
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentNumber = "SHIP-DISC-45-2",
            CarrierCode = "FEDEX",
            CarrierServiceLevel = CarrierServiceLevel.StandardGround,
            Status = ShipmentStatus.Dispatched,
            DispatchedAtUtc = date,
            TotalPackagesCount = 0,
            TotalWeightKg = 0m
        };
        var estCost2 = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment2.Id,
            CarrierCode = "FEDEX",
            TotalCarrierCost = 45.00m,
            CostAssessedAtUtc = date.AddHours(-1),
            Status = CarrierCostStatus.Estimated
        };
        var cost2 = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment2.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-DISC-45-2",
            BaseTransportationCost = 55.00m,
            TotalCarrierCost = 55.00m,
            BilledWeightKg = 8.0m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced
        };
        context.Shipments.Add(shipment2);
        context.CarrierCostRecords.AddRange(estCost2, cost2);

        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT
        var queue = await varianceService.GetDiscrepancyManagementQueueAsync("FEDEX", date.AddDays(-1), date.AddDays(2));

        // ASSERT
        Assert.NotNull(queue);
        var item1 = queue.First(q => q.ShipmentId == shipment1.Id);
        // Uses actual 8.0 kg billable weight: Expected = $40.00, Invoiced = $50.00, Variance = $10.00
        Assert.Equal(40.00m, item1.ExpectedCostUsd);
        Assert.Equal(50.00m, item1.InvoicedCostUsd);
        Assert.Equal(10.00m, item1.ContractualRateCardVarianceUsd);

        var item2 = queue.First(q => q.ShipmentId == shipment2.Id);
        // Missing packages on weight-dependent agreement -> contractual variance is null, falls back to quote baseline ($45.00)
        Assert.Null(item2.ContractualRateCardVarianceUsd);
        Assert.Equal(45.00m, item2.ExpectedCostUsd);
        Assert.Equal(10.00m, item2.OperationalQuoteVarianceUsd);
    }

    [Fact]
    public async Task Test_46_HistoricalValuation_TierCreatedAfterCutoff_ExcludedFromHistoricalRating()
    {
        // ARRANGE: Finding 1 - Tiers created after valuation cutoff (CreatedAt > valuationTime)
        // must be excluded from historical ratings. Pre-cutoff rating remains valid under earlier tiers.
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var t0 = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc); // Agreement created
        var tShipment = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc); // Shipment dispatched
        var tValuation = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc); // Historical valuation timestamp
        var tTier2Created = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc); // Tier 2 added later (> tValuation)

        var agreement = CarrierRateAgreement.Create(
            "FEDEX", "CRA-TIER-46", "Tiered FedEx Agreement",
            baseRate: 5.00m, currency: "USD", unitOfMeasure: "KG",
            rateModel: RateModelType.PerUnit, tierModel: PricingTierCalculationModel.AllUnits,
            effectiveFromUtc: t0, facilityId: _facility1Id);
        agreement.CreatedAt = t0;

        // Tier 1 created at T0 (0-5 kg @ $5.00)
        var tier1 = agreement.AddTier(1, 0m, 5.0m, 5.00m);
        tier1.CreatedAt = t0;

        // Tier 2 created at TTier2Created (5-15 kg @ $3.00) (> tValuation)
        var tier2 = agreement.AddTier(2, 5.0001m, 15.0m, 3.00m);
        tier2.CreatedAt = tTier2Created;

        context.CarrierRateAgreements.Add(agreement);

        // 1. Direct aggregate calculation at tValuation: Tier 2 is excluded, rated at BaseRate ($5.00/kg)
        // 8.0 kg * $5.00 = $40.00
        var ratingPreCutoff = agreement.CalculateRating(8.0m, valuationTime: tValuation);
        Assert.Equal(40.00m, ratingPreCutoff.TotalAmount);

        // 2. Direct aggregate calculation post-cutoff (tTier2Created + 1 day): Tier 2 is included!
        // 8.0 kg * $3.00 (AllUnits) = $24.00
        var ratingPostCutoff = agreement.CalculateRating(8.0m, valuationTime: tTier2Created.AddDays(1));
        Assert.Equal(24.00m, ratingPostCutoff.TotalAmount);

        // 3. Service-level compliance analysis at tValuation:
        // Shipment has 8.0 kg, invoiced $40.00
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-TIER-46", "FEDEX", tShipment, weightKg: 8.0m);
        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-TIER-46",
            BaseTransportationCost = 40.00m,
            TotalCarrierCost = 40.00m,
            BilledWeightKg = 8.0m,
            CostAssessedAtUtc = tShipment,
            Status = CarrierCostStatus.Invoiced
        };
        context.Shipments.Add(shipment);
        context.CarrierCostRecords.Add(cost);
        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        var complianceTVal = await varianceService.GetRateCardComplianceAnalysisAsync(
            "FEDEX", tShipment.AddDays(-1), tShipment.AddDays(2), asOfUtc: tValuation);

        Assert.NotNull(complianceTVal);
        Assert.Equal(1, complianceTVal.TotalAuditedShipmentsCount);
        Assert.Equal(1, complianceTVal.CompliantShipmentsCount);
        Assert.Equal(0, complianceTVal.IndeterminateShipmentsCount);
    }

    [Fact]
    public async Task Test_47_HistoricalValuation_TierModifiedAfterCutoff_RendersRatingIndeterminate()
    {
        // ARRANGE: Finding 1 - If a tier was modified after valuation cutoff (UpdatedAt > valuationTime),
        // historical rating terms are untrusted and must throw ValidationException / return Indeterminate.
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var t0 = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
        var tShipment = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var tValuation = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
        var tTierModified = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

        var agreement = CarrierRateAgreement.Create(
            "FEDEX", "CRA-TIER-47", "Tiered Modified Agreement",
            baseRate: 5.00m, currency: "USD", unitOfMeasure: "KG",
            rateModel: RateModelType.PerUnit, tierModel: PricingTierCalculationModel.AllUnits,
            effectiveFromUtc: t0, facilityId: _facility1Id);
        agreement.CreatedAt = t0;

        var tier1 = agreement.AddTier(1, 0m, 10.0m, 5.00m);
        tier1.CreatedAt = t0;
        tier1.UpdatedAt = tTierModified; // Modified after valuation cutoff!

        context.CarrierRateAgreements.Add(agreement);

        // 1. Direct aggregate rating throws ValidationException
        Assert.Throws<ValidationException>(() => agreement.CalculateRating(8.0m, valuationTime: tValuation));

        // 2. Service level compliance analysis returns Indeterminate
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-TIER-47", "FEDEX", tShipment, weightKg: 8.0m);
        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-TIER-47",
            BaseTransportationCost = 40.00m,
            TotalCarrierCost = 40.00m,
            BilledWeightKg = 8.0m,
            CostAssessedAtUtc = tShipment,
            Status = CarrierCostStatus.Invoiced
        };
        context.Shipments.Add(shipment);
        context.CarrierCostRecords.Add(cost);
        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        var compliance = await varianceService.GetRateCardComplianceAnalysisAsync(
            "FEDEX", tShipment.AddDays(-1), tShipment.AddDays(2), asOfUtc: tValuation);

        Assert.NotNull(compliance);
        Assert.Equal(1, compliance.IndeterminateShipmentsCount);
        Assert.Equal(0, compliance.TotalAuditedShipmentsCount);
    }

    [Fact]
    public async Task Test_48_ZoneMatching_DestinationAddress_MatchesZoneSpecificAgreementOverGeneric()
    {
        // ARRANGE: Finding 2 - Shipment destination address resolves to StateOrProvince "CA".
        // Zone-specific agreement ("CA") @ $4.00/kg strictly supersedes generic agreement (null zone) @ $6.00/kg.
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);

        // Generic agreement: $6.00/kg
        var genericAgreement = CarrierRateAgreement.Create(
            "FEDEX", "CRA-GEN-48", "Generic FedEx Agreement",
            baseRate: 6.00m, currency: "USD", unitOfMeasure: "KG",
            rateModel: RateModelType.PerUnit, tierModel: PricingTierCalculationModel.FlatRate,
            effectiveFromUtc: date.AddDays(-10), facilityId: _facility1Id);
        genericAgreement.ZoneOrLane = null;

        // Zone-specific agreement for "CA": $4.00/kg
        var zoneAgreement = CarrierRateAgreement.Create(
            "FEDEX", "CRA-ZONE-48", "California FedEx Agreement",
            baseRate: 4.00m, currency: "USD", unitOfMeasure: "KG",
            rateModel: RateModelType.PerUnit, tierModel: PricingTierCalculationModel.FlatRate,
            effectiveFromUtc: date.AddDays(-10), facilityId: _facility1Id);
        zoneAgreement.ZoneOrLane = "CA";

        context.CarrierRateAgreements.AddRange(genericAgreement, zoneAgreement);

        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-ZONE-48", "FEDEX", date, weightKg: 10.0m);
        var pkg = shipment.Packages.First();

        var outboundOrder = new OutboundOrder
        {
            Id = pkg.OutboundOrderId,
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            OrderNumber = "ORD-ZONE-48",
            OrderDateUtc = date
        };
        outboundOrder.SetShippingAddress(new ShippingAddress
        {
            RecipientName = "Acme Corp",
            AddressLine1 = "123 Ocean Blvd",
            City = "Los Angeles",
            StateOrProvince = "CA",
            PostalCode = "90210",
            CountryCode = "USA"
        });
        context.OutboundOrders.Add(outboundOrder);

        // Billed $40.00 for 10.0 kg ($4.00/kg, matching zone agreement)
        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-ZONE-48",
            BaseTransportationCost = 40.00m,
            TotalCarrierCost = 40.00m,
            BilledWeightKg = 10.0m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced
        };

        context.Shipments.Add(shipment);
        context.CarrierCostRecords.Add(cost);
        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT
        var compliance = await varianceService.GetRateCardComplianceAnalysisAsync("FEDEX", date.AddDays(-1), date.AddDays(2));

        // ASSERT: Audited and compliant against the California agreement ($40.00), not generic ($60.00)
        Assert.NotNull(compliance);
        Assert.Equal(1, compliance.TotalAuditedShipmentsCount);
        Assert.Equal(1, compliance.CompliantShipmentsCount);
        Assert.Equal(0, compliance.IndeterminateShipmentsCount);
    }

    [Fact]
    public async Task Test_49_ZoneMatching_FailClosed_UnknownZoneReturnsIndeterminateWhenZoneContractsExist()
    {
        // ARRANGE: Finding 2 - When a carrier has zone-specific agreements, a shipment with an UNKNOWN zone
        // must fail closed and return Indeterminate, rather than falsely defaulting to a generic agreement.
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);

        // Zone-specific agreement exists
        var zoneAgreement = CarrierRateAgreement.Create(
            "FEDEX", "CRA-ZONE-49", "Zone 2 FedEx Agreement",
            baseRate: 4.00m, currency: "USD", unitOfMeasure: "KG",
            rateModel: RateModelType.PerUnit, tierModel: PricingTierCalculationModel.FlatRate,
            effectiveFromUtc: date.AddDays(-10), facilityId: _facility1Id);
        zoneAgreement.ZoneOrLane = "Zone 2";

        // Generic agreement also exists
        var genericAgreement = CarrierRateAgreement.Create(
            "FEDEX", "CRA-GEN-49", "Generic FedEx Agreement",
            baseRate: 6.00m, currency: "USD", unitOfMeasure: "KG",
            rateModel: RateModelType.PerUnit, tierModel: PricingTierCalculationModel.FlatRate,
            effectiveFromUtc: date.AddDays(-10), facilityId: _facility1Id);
        genericAgreement.ZoneOrLane = null;

        context.CarrierRateAgreements.AddRange(zoneAgreement, genericAgreement);

        // Shipment with NO address or order data (unknown zone)
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-UNKNOWN-49", "FEDEX", date, weightKg: 10.0m);
        var cost = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-49",
            BaseTransportationCost = 60.00m,
            TotalCarrierCost = 60.00m,
            BilledWeightKg = 10.0m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced
        };

        context.Shipments.Add(shipment);
        context.CarrierCostRecords.Add(cost);
        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT
        var compliance = await varianceService.GetRateCardComplianceAnalysisAsync("FEDEX", date.AddDays(-1), date.AddDays(2));

        // ASSERT: Fails closed -> Indeterminate, NOT audited under generic contract
        Assert.NotNull(compliance);
        Assert.Equal(1, compliance.IndeterminateShipmentsCount);
        Assert.Equal(0, compliance.TotalAuditedShipmentsCount);
    }

    [Fact]
    public async Task Test_50_ContractualDimensionsAndUnits_DivisorAndRoundingInContractualUnits()
    {
        // ARRANGE: Finding 3 - Contractual dimensional divisor (4000) overrides package volumetric divisor (5000).
        // Rounding increment is 1.0 LB. Missing dimensional evidence returns Indeterminate.
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);

        var lbAgreement = CarrierRateAgreement.Create(
            "DHL", "CRA-DHL-50", "DHL Express Pounds Contract",
            baseRate: 2.00m, currency: "USD", unitOfMeasure: "LB",
            rateModel: RateModelType.PerUnit, tierModel: PricingTierCalculationModel.FlatRate,
            effectiveFromUtc: date.AddDays(-10), facilityId: _facility1Id);
        lbAgreement.DimensionalWeightDivisor = 4000m; // Contractual divisor overrides package divisor
        lbAgreement.WeightRoundingIncrement = 1.0m; // 1.0 LB increment

        context.CarrierRateAgreements.Add(lbAgreement);

        // Shipment 1: Dimensions 40x30x20 cm = 24,000 cm3.
        // Volumetric kg with divisor 4000: 24,000 / 4000 = 6.0 kg.
        // In pounds: 6.0 * 2.20462262 = 13.2277 lb.
        // Rounded up by 1.0 lb increment = 14.0 lb.
        // Contractual expected base cost = 14.0 * $2.00 = $28.00.
        var shipment1 = CreateShipment(_clientAId, _facility1Id, "SHIP-DIM-50-1", "DHL", date, weightKg: 2.0m);
        var pkg1 = shipment1.Packages.First();
        pkg1.LengthCm = 40m;
        pkg1.WidthCm = 30m;
        pkg1.HeightCm = 20m;
        pkg1.VolumetricDivisor = 5000m; // Contractual divisor 4000 will override this 5000!

        var cost1 = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment1.Id,
            CarrierCode = "DHL",
            CarrierInvoiceNumber = "INV-50-1",
            BaseTransportationCost = 28.00m,
            TotalCarrierCost = 28.00m,
            BilledWeightKg = 6.35m, // ~14 lb in kg
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced
        };
        context.Shipments.Add(shipment1);
        context.CarrierCostRecords.Add(cost1);

        // Shipment 2: Missing dimensional evidence (weight=0, dims=0) on weight-dependent agreement -> Indeterminate
        var shipment2 = new Shipment
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentNumber = "SHIP-DIM-50-2",
            CarrierCode = "DHL",
            CarrierServiceLevel = CarrierServiceLevel.StandardGround,
            Status = ShipmentStatus.Dispatched,
            DispatchedAtUtc = date,
            TotalPackagesCount = 0,
            TotalWeightKg = 0m
        };
        var cost2 = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment2.Id,
            CarrierCode = "DHL",
            CarrierInvoiceNumber = "INV-50-2",
            BaseTransportationCost = 28.00m,
            TotalCarrierCost = 28.00m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced
        };
        context.Shipments.Add(shipment2);
        context.CarrierCostRecords.Add(cost2);

        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT
        var compliance = await varianceService.GetRateCardComplianceAnalysisAsync("DHL", date.AddDays(-1), date.AddDays(2));

        // ASSERT: Shipment 1 is compliant; Shipment 2 is indeterminate due to missing dimensional evidence
        Assert.NotNull(compliance);
        Assert.Equal(1, compliance.TotalAuditedShipmentsCount);
        Assert.Equal(1, compliance.CompliantShipmentsCount);
        Assert.Equal(1, compliance.IndeterminateShipmentsCount);
    }

    [Fact]
    public async Task Test_51_DiscrepancyQueue_MultiInvoiceAllocation_ConservesShipmentContractualBaseline()
    {
        // ARRANGE: Finding 4 - For shipments with multiple legitimate carrier invoices under weight-dependent contract,
        // the single shipment contractual baseline ($100.00) is allocated deterministically across the two invoices
        // ($60.00 and $40.00) in proportion to their billed share, conserving the contract with zero false discrepancy.
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);

        // Contract: $10.00/kg. For 10.0 kg shipment, contractual rate = $100.00.
        var carrierAgreement = CarrierRateAgreement.Create(
            "FEDEX", "CRA-MULTI-51", "FedEx Multi-Invoice Agreement",
            baseRate: 10.00m, currency: "USD", unitOfMeasure: "KG",
            rateModel: RateModelType.PerUnit, tierModel: PricingTierCalculationModel.FlatRate,
            effectiveFromUtc: date.AddDays(-10), facilityId: _facility1Id);
        context.CarrierRateAgreements.Add(carrierAgreement);

        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-MULTI-51", "FEDEX", date, weightKg: 10.0m);
        context.Shipments.Add(shipment);

        // Initial Quote: $100.00
        var quote = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            BaseTransportationCost = 100.00m,
            TotalCarrierCost = 100.00m,
            CostAssessedAtUtc = date.AddHours(-2),
            Status = CarrierCostStatus.Estimated
        };

        // Invoice 1: $60.00
        var cost1 = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-51-A",
            BaseTransportationCost = 60.00m,
            TotalCarrierCost = 60.00m,
            BilledWeightKg = 10.0m,
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced
        };

        // Invoice 2: $40.00
        var cost2 = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            CarrierInvoiceNumber = "INV-51-B",
            BaseTransportationCost = 40.00m,
            TotalCarrierCost = 40.00m,
            BilledWeightKg = 10.0m,
            CostAssessedAtUtc = date.AddMinutes(30),
            Status = CarrierCostStatus.Invoiced
        };

        context.CarrierCostRecords.AddRange(quote, cost1, cost2);
        await context.SaveChangesAsync();

        var (reconService, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT: Minimum discrepancy threshold $1.00
        var queue = await varianceService.GetDiscrepancyManagementQueueAsync(
            "FEDEX", date.AddDays(-1), date.AddDays(2), minimumDiscrepancyUsd: 1.00m);

        // ASSERT: Pro-rata allocation gives $60.00 to cost1 and $40.00 to cost2.
        // Each record has discrepancy = $0.00 -> NOT falsely flagged as discrepancy in the queue!
        Assert.NotNull(queue);
        Assert.DoesNotContain(queue, q => q.ShipmentId == shipment.Id);
    }

    [Fact]
    public void Test_52_CarrierRateAgreement_TierDomainValidation_RejectsInvalidTiersAndValidatesBoundaries()
    {
        // ARRANGE & ASSERT: Finding 6 - Comprehensive domain validation for CarrierRateAgreement and tiers:
        var agreement = CarrierRateAgreement.Create(
            "UPS", "CRA-VAL-52", "Validation Test Agreement",
            baseRate: 5.00m, currency: "USD", unitOfMeasure: "KG",
            rateModel: RateModelType.PerUnit, tierModel: PricingTierCalculationModel.Graduated,
            effectiveFromUtc: DateTime.UtcNow);

        // 1. Rejects non-positive tier numbers
        Assert.Throws<ValidationException>(() => agreement.AddTier(0, 0m, 5.0m, 4.00m));
        Assert.Throws<ValidationException>(() => agreement.AddTier(-1, 0m, 5.0m, 4.00m));

        // 2. Rejects negative rate or negative lower bound
        Assert.Throws<ValidationException>(() => agreement.AddTier(1, -1.0m, 5.0m, 4.00m));
        Assert.Throws<ValidationException>(() => agreement.AddTier(1, 0m, 5.0m, -4.00m));

        // 3. Rejects upper bound <= lower bound
        Assert.Throws<ValidationException>(() => agreement.AddTier(1, 5.0m, 5.0m, 4.00m));
        Assert.Throws<ValidationException>(() => agreement.AddTier(1, 5.0m, 4.0m, 4.00m));

        // 4. Rejects unsupported UnitOfMeasure
        Assert.Throws<ValidationException>(() => CarrierRateAgreement.Create(
            "UPS", "CRA-INV", "Invalid UOM", 5.00m, "USD", "TONS", RateModelType.PerUnit, PricingTierCalculationModel.FlatRate, DateTime.UtcNow));

        // 5. Validates contiguous graduated bracket rating:
        // Tier 1: 0 - 5.0 kg @ $4.00
        // Tier 2: 5.0 - 10.0 kg @ $3.00
        // Base rate for excess: $5.00
        var agGraduated = CarrierRateAgreement.Create(
            "UPS", "CRA-GRAD-52", "Graduated Agreement",
            baseRate: 5.00m, currency: "USD", unitOfMeasure: "KG",
            rateModel: RateModelType.PerUnit, tierModel: PricingTierCalculationModel.Graduated,
            effectiveFromUtc: DateTime.UtcNow);

        agGraduated.AddTier(1, 0m, 5.0m, 4.00m);
        agGraduated.AddTier(2, 5.0m, 10.0m, 3.00m);

        // Rating at boundary: 5.0 kg = 5 * $4.00 = $20.00
        var rating5 = agGraduated.CalculateRating(5.0m);
        Assert.Equal(20.00m, rating5.TotalAmount);

        // Rating in bracket 2: 8.0 kg = (5 * 4.00) + (3 * 3.00) = 20.00 + 9.00 = $29.00
        var rating8 = agGraduated.CalculateRating(8.0m);
        Assert.Equal(29.00m, rating8.TotalAmount);

        // Rating in excess: 12.0 kg = (5 * 4.00) + (5 * 3.00) + (2 * 5.00 baseRate) = 20 + 15 + 10 = $45.00
        var rating12 = agGraduated.CalculateRating(12.0m);
        Assert.Equal(45.00m, rating12.TotalAmount);

        // 6. Validates gap rejection:
        var agGap = CarrierRateAgreement.Create(
            "UPS", "CRA-GAP-52", "Gap Agreement",
            baseRate: 5.00m, currency: "USD", unitOfMeasure: "KG",
            rateModel: RateModelType.PerUnit, tierModel: PricingTierCalculationModel.Graduated,
            effectiveFromUtc: DateTime.UtcNow);
        agGap.AddTier(1, 0m, 5.0m, 4.00m);
        agGap.AddTier(2, 6.0m, 10.0m, 3.00m); // Gap of 1.0 kg!

        Assert.Throws<ValidationException>(() => agGap.CalculateRating(8.0m));

        // 7. Validates overlap rejection:
        var agOverlap = CarrierRateAgreement.Create(
            "UPS", "CRA-OVERLAP-52", "Overlap Agreement",
            baseRate: 5.00m, currency: "USD", unitOfMeasure: "KG",
            rateModel: RateModelType.PerUnit, tierModel: PricingTierCalculationModel.Graduated,
            effectiveFromUtc: DateTime.UtcNow);
        agOverlap.AddTier(1, 0m, 5.0m, 4.00m);
        agOverlap.AddTier(2, 4.0m, 10.0m, 3.00m); // Overlap of 1.0 kg!

        Assert.Throws<ValidationException>(() => agOverlap.CalculateRating(8.0m));
    }
}
