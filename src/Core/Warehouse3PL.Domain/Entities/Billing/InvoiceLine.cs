using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Billing;

/// <summary>
/// Discrete charge line item on an invoice.
/// Provides full backward traceability to the originating BillableActivity, warehouse facility, and activity type.
/// DB-SPEC-3PL-2026 Phase 5.5 Invoicing Engine &amp; AR Ledger.
/// </summary>
public class InvoiceLine : AuditableEntity<Guid>
{
    public Guid InvoiceId { get; set; }
    public int LineNumber { get; set; }
    public InvoiceChargeCategory ChargeCategory { get; set; }
    public string Description { get; set; } = string.Empty;

    public decimal Quantity { get; set; }
    public string UnitOfMeasure { get; set; } = "UNIT";
    public decimal UnitPrice { get; set; }
    public decimal TotalAmount { get; set; }

    // Traceability to operational activity
    public Guid? BillableActivityId { get; set; }
    public BillableActivityType? ActivityType { get; set; }
    public Guid? FacilityId { get; set; }
    public string? MetadataJson { get; set; }

    // Navigations
    public Invoice Invoice { get; set; } = null!;
    public BillableActivity? BillableActivity { get; set; }

    public InvoiceLine()
    {
        Id = Guid.NewGuid();
    }

    public static InvoiceLine Create(
        int lineNumber,
        InvoiceChargeCategory category,
        string description,
        decimal quantity,
        string unitOfMeasure,
        decimal unitPrice,
        decimal totalAmount,
        Guid? billableActivityId = null,
        BillableActivityType? activityType = null,
        Guid? facilityId = null,
        string? metadataJson = null)
    {
        if (lineNumber <= 0)
        {
            throw new ValidationException(nameof(lineNumber), "Line number must be positive.");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ValidationException(nameof(description), "Line item description is required.");
        }

        if (quantity < 0)
        {
            throw new ValidationException(nameof(quantity), "Quantity cannot be negative.");
        }

        if (unitPrice < 0)
        {
            throw new ValidationException(nameof(unitPrice), "Unit price cannot be negative.");
        }

        return new InvoiceLine
        {
            Id = Guid.NewGuid(),
            LineNumber = lineNumber,
            ChargeCategory = category,
            Description = description.Trim(),
            Quantity = decimal.Round(quantity, 4, MidpointRounding.AwayFromZero),
            UnitOfMeasure = string.IsNullOrWhiteSpace(unitOfMeasure) ? "UNIT" : unitOfMeasure.Trim().ToUpperInvariant(),
            UnitPrice = decimal.Round(unitPrice, 4, MidpointRounding.AwayFromZero),
            TotalAmount = decimal.Round(totalAmount, 4, MidpointRounding.AwayFromZero),
            BillableActivityId = billableActivityId,
            ActivityType = activityType,
            FacilityId = facilityId,
            MetadataJson = metadataJson
        };
    }
}

