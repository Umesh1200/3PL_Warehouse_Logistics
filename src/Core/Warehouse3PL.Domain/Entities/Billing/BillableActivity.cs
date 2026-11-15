using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Billing;

/// <summary>
/// Immutable commercial activity record capturing a discrete billable event originating
/// from warehouse, courier, or reverse-logistics operations.
/// Acts as the authoritative decoupled boundary between physical warehouse execution and financial accounting.
/// DB-SPEC-3PL-2026 Phase 5.0 Commercial Domain &amp; Billing Foundation.
/// </summary>
public class BillableActivity : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid? BillingPeriodId { get; set; }

    public BillableActivityType ActivityType { get; set; }
    public BillableActivityStatus Status { get; set; } = BillableActivityStatus.PendingRating;

    // Operational Source Traceability
    public string SourceType { get; set; } = string.Empty;
    public Guid SourceId { get; set; }
    public string SourceReference { get; set; } = string.Empty;

    // Measured Operational Metrics
    public decimal Quantity { get; set; }
    public string UnitOfMeasure { get; set; } = "UNIT";

    // Commercial Rating & Financials
    public decimal? UnitPrice { get; set; }
    public decimal? TotalAmount { get; set; }
    public string Currency { get; set; } = "USD";

    public Guid? RateCardId { get; set; }
    public Guid? RateCardScheduleId { get; set; }
    public Guid? RateCardTierId { get; set; }
    public int? RateCardVersion { get; set; }
    public DateTime? RatedAtUtc { get; set; }
    public Guid? RatedByUserId { get; set; }

    // Invoicing Linkage
    public Guid? InvoiceId { get; set; }
    public Guid? InvoiceLineId { get; set; }
    public DateTime? InvoicedAtUtc { get; set; }

    // Concurrency, Idempotency & Auditing
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? MetadataJson { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public BillingPeriod? BillingPeriod { get; set; }

    public BillableActivity()
    {
        Id = Guid.NewGuid();
    }

    /// <summary>
    /// Factory method to construct and initialize an immutable BillableActivity from an operational event.
    /// </summary>
    public static BillableActivity Create(
        Guid clientId,
        Guid facilityId,
        BillableActivityType activityType,
        string sourceType,
        Guid sourceId,
        string sourceReference,
        decimal quantity,
        string unitOfMeasure,
        DateTime occurredAtUtc,
        string idempotencyKey,
        string description,
        Guid? billingPeriodId = null,
        string? metadataJson = null)
    {
        if (clientId == Guid.Empty)
        {
            throw new ValidationException(nameof(clientId), "Client tenant ID is required.");
        }

        if (facilityId == Guid.Empty)
        {
            throw new ValidationException(nameof(facilityId), "Facility ID is required.");
        }

        if (string.IsNullOrWhiteSpace(sourceType))
        {
            throw new ValidationException(nameof(sourceType), "Operational source type is required.");
        }

        if (sourceId == Guid.Empty)
        {
            throw new ValidationException(nameof(sourceId), "Operational source ID is required.");
        }

        if (quantity < 0)
        {
            throw new ValidationException(nameof(quantity), "Billable activity quantity cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(unitOfMeasure))
        {
            throw new ValidationException(nameof(unitOfMeasure), "Unit of measure is required.");
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ValidationException(nameof(idempotencyKey), "Idempotency key is required.");
        }

        return new BillableActivity
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            FacilityId = facilityId,
            BillingPeriodId = billingPeriodId,
            ActivityType = activityType,
            Status = BillableActivityStatus.PendingRating,
            SourceType = sourceType.Trim(),
            SourceId = sourceId,
            SourceReference = string.IsNullOrWhiteSpace(sourceReference) ? sourceId.ToString() : sourceReference.Trim(),
            Quantity = quantity,
            UnitOfMeasure = unitOfMeasure.Trim().ToUpperInvariant(),
            OccurredAtUtc = occurredAtUtc == default ? DateTime.UtcNow : occurredAtUtc,
            IdempotencyKey = idempotencyKey.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? $"{activityType} on {sourceType}" : description.Trim(),
            MetadataJson = metadataJson
        };
    }

    /// <summary>
    /// Assigns contract rating calculation results to this billable activity.
    /// </summary>
    public void Rate(
        decimal unitPrice,
        decimal totalAmount,
        string currency,
        Guid? rateCardId = null,
        Guid? rateCardTierId = null,
        int? rateCardVersion = null,
        Guid? ratedByUserId = null,
        DateTime? ratedAtUtc = null,
        Guid? rateCardScheduleId = null)
    {
        if (Status == BillableActivityStatus.Invoiced)
        {
            throw new InvalidDomainStateException("Cannot rate an activity that has already been invoiced.");
        }

        if (Status == BillableActivityStatus.Cancelled)
        {
            throw new InvalidDomainStateException("Cannot rate a cancelled billable activity.");
        }

        if (unitPrice < 0)
        {
            throw new ValidationException(nameof(unitPrice), "Unit price cannot be negative.");
        }

        if (totalAmount < 0)
        {
            throw new ValidationException(nameof(totalAmount), "Total rated amount cannot be negative.");
        }

        var normalizedCurrency = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();
        if (normalizedCurrency.Length != 3)
        {
            throw new ValidationException(nameof(currency), "Currency code must be a 3-character ISO 4217 code.");
        }

        UnitPrice = decimal.Round(unitPrice, 4, MidpointRounding.AwayFromZero);
        TotalAmount = decimal.Round(totalAmount, 4, MidpointRounding.AwayFromZero);
        Currency = normalizedCurrency;
        RateCardId = rateCardId;
        RateCardScheduleId = rateCardScheduleId;
        RateCardTierId = rateCardTierId;
        RateCardVersion = rateCardVersion;
        RatedByUserId = ratedByUserId;
        RatedAtUtc = ratedAtUtc ?? DateTime.UtcNow;
        Status = BillableActivityStatus.Rated;
    }

    /// <summary>
    /// Links the rated activity to an approved or issued invoice.
    /// </summary>
    public void MarkInvoiced(Guid invoiceId, Guid invoiceLineId, DateTime? invoicedAtUtc = null)
    {
        if (Status != BillableActivityStatus.Rated && Status != BillableActivityStatus.Waived)
        {
            throw new InvalidDomainStateException($"Cannot invoice an activity in status '{Status}'. Activity must be Rated or Waived.");
        }

        if (invoiceId == Guid.Empty)
        {
            throw new ValidationException(nameof(invoiceId), "Invoice ID is required.");
        }

        if (invoiceLineId == Guid.Empty)
        {
            throw new ValidationException(nameof(invoiceLineId), "Invoice line ID is required.");
        }

        InvoiceId = invoiceId;
        InvoiceLineId = invoiceLineId;
        InvoicedAtUtc = invoicedAtUtc ?? DateTime.UtcNow;
        Status = BillableActivityStatus.Invoiced;
    }

    /// <summary>
    /// Applies a commercial fee waiver to this activity (rates to 0.00).
    /// </summary>
    public void Waive(string reason)
    {
        if (Status == BillableActivityStatus.Invoiced)
        {
            throw new InvalidDomainStateException("Cannot waive an activity that has already been invoiced.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException(nameof(reason), "A waiver reason is required.");
        }

        UnitPrice = 0m;
        TotalAmount = 0m;
        Status = BillableActivityStatus.Waived;
        Description = string.IsNullOrWhiteSpace(Description)
            ? $"[Waived]: {reason.Trim()}"
            : $"{Description} (Waived: {reason.Trim()})";
    }

    /// <summary>
    /// Cancels this billable activity if the source operational transaction was reversed or voided.
    /// </summary>
    public void Cancel(string reason)
    {
        if (Status == BillableActivityStatus.Invoiced)
        {
            throw new InvalidDomainStateException("Cannot cancel an activity that has already been billed on an invoice. A credit note or adjustment must be issued.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException(nameof(reason), "A cancellation reason is required.");
        }

        Status = BillableActivityStatus.Cancelled;
        Description = string.IsNullOrWhiteSpace(Description)
            ? $"[Cancelled]: {reason.Trim()}"
            : $"{Description} (Cancelled: {reason.Trim()})";
    }

    /// <summary>
    /// Links this activity to an open or locked billing period.
    /// Rejects attachment if the period is already Closed or Invoiced.
    /// </summary>
    public void AssignBillingPeriod(BillingPeriod period)
    {
        if (period == null)
        {
            throw new ValidationException(nameof(period), "Billing period is required.");
        }

        if (period.Status == BillingPeriodStatus.Closed || period.Status == BillingPeriodStatus.Invoiced)
        {
            throw new InvalidDomainStateException(
                $"Cannot assign activity to sealed billing period '{period.PeriodCode}' with status '{period.Status}'. Late-arriving activities must be routed to the next open billing period.");
        }

        AssignBillingPeriod(period.Id);
    }

    /// <summary>
    /// Links this activity to an open or locked billing period by ID.
    /// </summary>
    public void AssignBillingPeriod(Guid billingPeriodId)
    {
        if (Status == BillableActivityStatus.Invoiced)
        {
            throw new InvalidDomainStateException("Cannot reassign billing period for an already invoiced activity.");
        }

        if (billingPeriodId == Guid.Empty)
        {
            throw new ValidationException(nameof(billingPeriodId), "A valid billing period ID is required.");
        }

        BillingPeriodId = billingPeriodId;
    }
}

