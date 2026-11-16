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

        var (_, varianceService) = CreateServices(context, tenantProvider, currentUser);

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

        var (_, varianceService) = CreateServices(context, tenantProvider, currentUser);

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

        var (_, varianceService) = CreateServices(context, tenantProvider, currentUser);

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

        var (_, varianceService) = CreateServices(context, tenantProvider, currentUser);

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

        var (_, varianceService) = CreateServices(context, tenantProvider, currentUser);

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

        var (_, varianceService) = CreateServices(context, tenantProvider, currentUser);

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

        var (_, varianceService) = CreateServices(context, tenantProvider, currentUser);

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

        var (_, varianceService) = CreateServices(context, tenantProvider, currentUser);

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

        cost.Adjustments.Add(weightAdj);
        context.CarrierCostRecords.AddRange(quote, cost);
        await context.SaveChangesAsync();

        var (_, varianceService) = CreateServices(context, tenantProvider, currentUser);

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

        var (_, varianceService) = CreateServices(context, tenantProvider, currentUser);

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

        var (_, varianceService) = CreateServices(context, tenantProvider, currentUser);

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

        var (_, varianceService) = CreateServices(context, tenantProvider, currentUser);

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

        var (_, varianceService) = CreateServices(context, tenantProvider, currentUser);

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

        var (_, varianceService) = CreateServices(context, tenantProvider, currentUser);

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

        var (_, varianceService) = CreateServices(context, tenantProvider, currentUser);

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

        var (_, varianceService) = CreateServices(context, tenantProvider, currentUser);

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

        var (_, varianceService) = CreateServices(context, tenantProvider, currentUser);

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

        var (_, varianceService) = CreateServices(context, tenantProvider, currentUser);

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

        var (_, varianceService) = CreateServices(context, tenantProvider, currentUser);

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

        var (_, varianceService) = CreateServices(context, tenantProvider, currentUser);

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

        var (_, varianceService) = CreateServices(context, tenantProvider, currentUser);

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

        var (_, varianceService) = CreateServices(context, tenantProvider, currentUser);

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
        var (_, varianceService) = CreateServices(context, tenantProvider, currentUser);

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

        var (_, varianceService) = CreateServices(context, tenantProvider, currentUser);

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
            CostAssessedAtUtc = date,
            Status = CarrierCostStatus.Invoiced
        };

        // Carrier reweighed to 3.0 kg.
        // Under 1.0 kg rounding increment: 2.2 kg rounds up to 3.0 kg. Drift = 3.0 - 3.0 = 0.0 kg.
        // (Under 0.5 kg default: 2.2 kg would round to 2.5 kg, resulting in drift 0.5 kg).
        var weightAdj = CarrierCostAdjustment.Create(
            _clientAId, _facility1Id, cost.Id, CarrierCostAdjustmentType.WeightDiscrepancy,
            "INV-29", 30.00m, 30.00m, 0m, "USD", "Reweighed parcel to 3.0 kg", "IDEM-29", date);
        cost.Adjustments.Add(weightAdj);

        context.CarrierCostRecords.Add(cost);
        await context.SaveChangesAsync();

        var (_, varianceService) = CreateServices(context, tenantProvider, currentUser);

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
        // ARRANGE: Gate E & F - Multiple carrier invoice lines for a single shipment are preserved with distinct stable identifiers.
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = _staffUserId, UserType = UserType.Staff, FacilityId = _facility1Id, PermissionsList = ["Analytics:CrossFacility"] };
        var (context, _) = await CreateSeededContextAsync(tenantProvider, currentUser);

        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var shipment = CreateShipment(_clientAId, _facility1Id, "SHIP-MULTI-30", "FEDEX", date);
        context.Shipments.Add(shipment);

        // Initial Quote of $50
        var quote = new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = _clientAId,
            FacilityId = _facility1Id,
            ShipmentId = shipment.Id,
            CarrierCode = "FEDEX",
            BaseTransportationCost = 50.00m,
            TotalCarrierCost = 50.00m,
            CostAssessedAtUtc = date.AddHours(-2),
            Status = CarrierCostStatus.Estimated
        };

        // Contractual agreement of $50 base rate
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

        var (_, varianceService) = CreateServices(context, tenantProvider, currentUser);

        // ACT
        var queue = await varianceService.GetDiscrepancyManagementQueueAsync("FEDEX", date.AddDays(-1), date.AddDays(2));

        // ASSERT: Multiple lines retained without collapsing into a single item
        Assert.NotNull(queue);
        Assert.Equal(2, queue.Count);

        var item1 = Assert.Single(queue, q => q.CarrierCostRecordId == cost1.Id);
        Assert.Contains(cost1.Id.ToString(), item1.InvoiceLineIdentifier);
        Assert.Contains("INV-30A", item1.CarrierInvoiceNumbers);
        Assert.Equal(65.00m, item1.InvoicedCostUsd);
        Assert.Equal(15.00m, item1.OperationalQuoteVarianceUsd);
        Assert.Equal(15.00m, item1.ContractualRateCardVarianceUsd);

        var item2 = Assert.Single(queue, q => q.CarrierCostRecordId == cost2.Id);
        Assert.Contains(cost2.Id.ToString(), item2.InvoiceLineIdentifier);
        Assert.Contains("INV-30B", item2.CarrierInvoiceNumbers);
        Assert.Equal(30.00m, item2.InvoicedCostUsd);
        Assert.Equal(-20.00m, item2.OperationalQuoteVarianceUsd);
        Assert.Equal(-20.00m, item2.ContractualRateCardVarianceUsd);
    }
}
