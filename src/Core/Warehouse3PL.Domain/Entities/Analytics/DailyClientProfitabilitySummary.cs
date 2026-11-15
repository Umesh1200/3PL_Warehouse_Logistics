using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Analytics;

/// <summary>
/// Aggregated read model projection tracking client-level commercial profitability, revenue streams, and margin realization.
/// Reconciles to Phase 5 billing ledgers (Invoices, BillableActivities, FreightMarginRecords).
/// DB-SPEC-3PL-2026 Phase 6.0 Analytics Foundation & KPI Engine.
/// </summary>
public class DailyClientProfitabilitySummary : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid? FacilityId { get; set; }
    public DateTime DateUtc { get; set; }
    public TimeGrain TimeGrain { get; set; } = TimeGrain.Daily;

    // Commercial Revenue Streams
    public decimal BilledStorageRevenue { get; set; }
    public decimal BilledHandlingRevenue { get; set; }
    public decimal BilledFreightRevenue { get; set; }
    public decimal BilledVasRevenue { get; set; }
    public decimal TotalBilledRevenue { get; set; }

    // Direct Costs & Margins
    public decimal CarrierFreightCost { get; set; }
    public decimal NetFreightMargin { get; set; }
    public decimal FreightMarginPercent { get; set; }
    public decimal DirectLaborEstimatedCost { get; set; }
    public decimal NetCommercialMargin { get; set; }
    public decimal CommercialMarginPercent { get; set; }

    // Billing & AR Realization
    public int InvoicesIssuedCount { get; set; }
    public decimal TotalInvoicedAmount { get; set; }
    public decimal TotalTaxAmount { get; set; }
    public decimal PaymentsCollectedAmount { get; set; }
    public decimal CreditsAppliedAmount { get; set; }
    public decimal OutstandingArBalance { get; set; }

    public string Currency { get; set; } = "USD";

    public DailyClientProfitabilitySummary()
    {
        Id = Guid.NewGuid();
    }

    public static DailyClientProfitabilitySummary Create(
        Guid clientId,
        DateTime dateUtc,
        TimeGrain timeGrain = TimeGrain.Daily,
        Guid? facilityId = null,
        string currency = "USD")
    {
        if (clientId == Guid.Empty)
        {
            throw new ValidationException(nameof(clientId), "Client ID is required.");
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ValidationException(nameof(currency), "Currency is required.");
        }

        return new DailyClientProfitabilitySummary
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            FacilityId = facilityId,
            DateUtc = dateUtc.Kind == DateTimeKind.Utc ? dateUtc : DateTime.SpecifyKind(dateUtc, DateTimeKind.Utc),
            TimeGrain = timeGrain,
            Currency = currency.Trim().ToUpperInvariant(),
            BilledStorageRevenue = 0m,
            BilledHandlingRevenue = 0m,
            BilledFreightRevenue = 0m,
            BilledVasRevenue = 0m,
            TotalBilledRevenue = 0m,
            CarrierFreightCost = 0m,
            NetFreightMargin = 0m,
            FreightMarginPercent = 0m,
            DirectLaborEstimatedCost = 0m,
            NetCommercialMargin = 0m,
            CommercialMarginPercent = 0m,
            InvoicesIssuedCount = 0,
            TotalInvoicedAmount = 0m,
            TotalTaxAmount = 0m,
            PaymentsCollectedAmount = 0m,
            CreditsAppliedAmount = 0m,
            OutstandingArBalance = 0m
        };
    }

    public void AddStorageRevenue(decimal amount)
    {
        if (amount < 0m) throw new ValidationException(nameof(amount), "Storage revenue cannot be negative.");
        BilledStorageRevenue += amount;
        RecalculateMargins();
    }

    public void AddHandlingRevenue(decimal amount, decimal estimatedLaborCost = 0m)
    {
        if (amount < 0m) throw new ValidationException(nameof(amount), "Handling revenue cannot be negative.");
        if (estimatedLaborCost < 0m) throw new ValidationException(nameof(estimatedLaborCost), "Labor cost cannot be negative.");

        BilledHandlingRevenue += amount;
        DirectLaborEstimatedCost += estimatedLaborCost;
        RecalculateMargins();
    }

    public void AddFreightRevenue(decimal billedFreight, decimal carrierCost)
    {
        if (billedFreight < 0m) throw new ValidationException(nameof(billedFreight), "Billed freight cannot be negative.");
        if (carrierCost < 0m) throw new ValidationException(nameof(carrierCost), "Carrier cost cannot be negative.");

        BilledFreightRevenue += billedFreight;
        CarrierFreightCost += carrierCost;
        RecalculateMargins();
    }

    public void AddVasRevenue(decimal amount, decimal estimatedLaborCost = 0m)
    {
        if (amount < 0m) throw new ValidationException(nameof(amount), "VAS revenue cannot be negative.");
        if (estimatedLaborCost < 0m) throw new ValidationException(nameof(estimatedLaborCost), "Labor cost cannot be negative.");

        BilledVasRevenue += amount;
        DirectLaborEstimatedCost += estimatedLaborCost;
        RecalculateMargins();
    }

    public void RecordInvoiceIssued(decimal subtotal, decimal tax)
    {
        if (subtotal < 0m) throw new ValidationException(nameof(subtotal), "Subtotal cannot be negative.");
        if (tax < 0m) throw new ValidationException(nameof(tax), "Tax cannot be negative.");

        InvoicesIssuedCount++;
        TotalInvoicedAmount += subtotal + tax;
        TotalTaxAmount += tax;
        OutstandingArBalance += subtotal + tax;
    }

    public void RecordPaymentCollected(decimal paymentAmount)
    {
        if (paymentAmount < 0m) throw new ValidationException(nameof(paymentAmount), "Payment amount cannot be negative.");

        PaymentsCollectedAmount += paymentAmount;
        OutstandingArBalance = Math.Max(0m, OutstandingArBalance - paymentAmount);
    }

    public void RecordCreditApplied(decimal creditAmount)
    {
        if (creditAmount < 0m) throw new ValidationException(nameof(creditAmount), "Credit amount cannot be negative.");

        CreditsAppliedAmount += creditAmount;
        OutstandingArBalance = Math.Max(0m, OutstandingArBalance - creditAmount);
    }

    public void RecalculateMargins()
    {
        TotalBilledRevenue = BilledStorageRevenue + BilledHandlingRevenue + BilledFreightRevenue + BilledVasRevenue;
        NetFreightMargin = BilledFreightRevenue - CarrierFreightCost;

        FreightMarginPercent = BilledFreightRevenue > 0m
            ? Math.Round((NetFreightMargin / BilledFreightRevenue) * 100m, 2)
            : 0m;

        NetCommercialMargin = TotalBilledRevenue - (CarrierFreightCost + DirectLaborEstimatedCost);

        CommercialMarginPercent = TotalBilledRevenue > 0m
            ? Math.Round((NetCommercialMargin / TotalBilledRevenue) * 100m, 2)
            : 0m;
    }
}
