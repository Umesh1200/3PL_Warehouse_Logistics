using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Billing;

/// <summary>
/// Concurrency-safe sequence tracking entity for sequential invoice and credit note numbering per client tenant.
/// DB-SPEC-3PL-2026 Phase 5.5 Invoicing Engine &amp; AR Ledger.
/// </summary>
public class InvoiceSequence : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public int FiscalYear { get; set; }
    public string Prefix { get; set; } = "INV";
    public int LastSequenceNumber { get; set; }

    public Client Client { get; set; } = null!;

    public InvoiceSequence()
    {
        Id = Guid.NewGuid();
    }

    public static InvoiceSequence Create(Guid clientId, int fiscalYear, string prefix = "INV")
    {
        if (clientId == Guid.Empty)
        {
            throw new ValidationException(nameof(clientId), "Client tenant ID is required.");
        }

        if (fiscalYear < 2000 || fiscalYear > 2100)
        {
            throw new ValidationException(nameof(fiscalYear), "Valid fiscal year is required.");
        }

        return new InvoiceSequence
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            FiscalYear = fiscalYear,
            Prefix = string.IsNullOrWhiteSpace(prefix) ? "INV" : prefix.Trim().ToUpperInvariant(),
            LastSequenceNumber = 0
        };
    }

    /// <summary>
    /// Atomically increments the sequence and returns the next formatted document number.
    /// Format: {PREFIX}-{YEAR}-{CLIENT_CODE}-{SEQUENCE:D6} (e.g. INV-2026-CLI01-000001).
    /// </summary>
    public string NextNumber(string clientCode)
    {
        if (string.IsNullOrWhiteSpace(clientCode))
        {
            throw new ValidationException(nameof(clientCode), "Client account code is required for document formatting.");
        }

        LastSequenceNumber++;
        var cleanCode = clientCode.Trim().ToUpperInvariant();
        return $"{Prefix}-{FiscalYear}-{cleanCode}-{LastSequenceNumber:D6}";
    }
}

