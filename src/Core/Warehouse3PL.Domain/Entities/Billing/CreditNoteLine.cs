using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Billing;

/// <summary>
/// Discrete adjustment line on a commercial credit note.
/// DB-SPEC-3PL-2026 Phase 5.5 Invoicing Engine &amp; AR Ledger.
/// </summary>
public class CreditNoteLine : AuditableEntity<Guid>
{
    public Guid CreditNoteId { get; set; }
    public Guid? OriginalInvoiceLineId { get; set; }
    public int LineNumber { get; set; }
    public string Description { get; set; } = string.Empty;

    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalAmount { get; set; }

    // Navigations
    public CreditNote CreditNote { get; set; } = null!;
    public InvoiceLine? OriginalInvoiceLine { get; set; }

    public CreditNoteLine()
    {
        Id = Guid.NewGuid();
    }

    public static CreditNoteLine Create(
        int lineNumber,
        string description,
        decimal quantity,
        decimal unitPrice,
        decimal totalAmount,
        Guid? originalInvoiceLineId = null)
    {
        if (lineNumber <= 0)
        {
            throw new ValidationException(nameof(lineNumber), "Line number must be positive.");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ValidationException(nameof(description), "Description is required.");
        }

        if (quantity < 0 || unitPrice < 0 || totalAmount < 0)
        {
            throw new ValidationException(nameof(totalAmount), "Amounts and quantities on credit lines cannot be negative.");
        }

        return new CreditNoteLine
        {
            Id = Guid.NewGuid(),
            LineNumber = lineNumber,
            Description = description.Trim(),
            Quantity = decimal.Round(quantity, 4, MidpointRounding.AwayFromZero),
            UnitPrice = decimal.Round(unitPrice, 4, MidpointRounding.AwayFromZero),
            TotalAmount = decimal.Round(totalAmount, 4, MidpointRounding.AwayFromZero),
            OriginalInvoiceLineId = originalInvoiceLineId
        };
    }
}

