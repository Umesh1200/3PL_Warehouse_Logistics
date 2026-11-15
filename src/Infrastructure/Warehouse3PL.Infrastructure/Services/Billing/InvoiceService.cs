using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services.Billing;

/// <summary>
/// Service governing the lifecycle of commercial invoices and credit notes:
/// approvals, issuance, immutability, voiding, and credit note emissions.
/// DB-SPEC-3PL-2026 Phase 5.5 Invoicing Engine &amp; AR Ledger.
/// </summary>
public class InvoiceService : IInvoiceService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ILogger<InvoiceService> _logger;

    public InvoiceService(
        IApplicationDbContext dbContext,
        ILogger<InvoiceService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<InvoiceDto?> GetInvoiceByIdAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default)
    {
        var invoice = await _dbContext.Invoices
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);

        return invoice == null ? null : MapToDto(invoice);
    }

    public async Task<InvoiceDto> ApproveInvoiceAsync(
        Guid invoiceId,
        Guid approvedByUserId,
        CancellationToken cancellationToken = default)
    {
        var invoice = await _dbContext.Invoices
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);

        if (invoice == null)
        {
            throw new EntityNotFoundException(nameof(Invoice), invoiceId);
        }

        invoice.Approve(approvedByUserId);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Invoice '{InvoiceNumber}' approved by user {UserId}.", invoice.InvoiceNumber, approvedByUserId);
        return MapToDto(invoice);
    }

    public async Task<InvoiceDto> IssueInvoiceAsync(
        Guid invoiceId,
        Guid issuedByUserId,
        CancellationToken cancellationToken = default)
    {
        var invoice = await _dbContext.Invoices
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);

        if (invoice == null)
        {
            throw new EntityNotFoundException(nameof(Invoice), invoiceId);
        }

        invoice.Issue(issuedByUserId);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Invoice '{InvoiceNumber}' issued by user {UserId}.", invoice.InvoiceNumber, issuedByUserId);
        return MapToDto(invoice);
    }

    public async Task<InvoiceDto> VoidInvoiceAsync(
        Guid invoiceId,
        Guid voidedByUserId,
        string reason,
        Guid? replacementInvoiceId = null,
        CancellationToken cancellationToken = default)
    {
        var invoice = await _dbContext.Invoices
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);

        if (invoice == null)
        {
            throw new EntityNotFoundException(nameof(Invoice), invoiceId);
        }

        invoice.Void(voidedByUserId, reason, replacementInvoiceId);

        // Release linked activities back to Rated status so they can be re-evaluated or re-billed
        var activityIds = invoice.Lines
            .Where(l => l.BillableActivityId.HasValue)
            .Select(l => l.BillableActivityId!.Value)
            .ToList();

        if (activityIds.Count > 0)
        {
            var activities = await _dbContext.BillableActivities
                .Where(a => activityIds.Contains(a.Id))
                .ToListAsync(cancellationToken);

            foreach (var act in activities)
            {
                act.Status = BillableActivityStatus.Rated;
                act.InvoiceId = null;
                act.InvoiceLineId = null;
                act.InvoicedAtUtc = null;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Invoice '{InvoiceNumber}' voided. Reason: {Reason}", invoice.InvoiceNumber, reason);
        return MapToDto(invoice);
    }

    public async Task<CreditNoteDto> CreateCreditNoteAsync(
        CreateCreditNoteRequest request,
        CancellationToken cancellationToken = default)
    {
        var invoice = await _dbContext.Invoices
            .Include(i => i.Client)
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == request.OriginalInvoiceId && i.ClientId == request.ClientId, cancellationToken);

        if (invoice == null)
        {
            throw new EntityNotFoundException(nameof(Invoice), request.OriginalInvoiceId);
        }

        if (invoice.Status == InvoiceStatus.Draft || invoice.Status == InvoiceStatus.Voided)
        {
            throw new InvalidDomainStateException($"Cannot issue a credit note against an invoice in '{invoice.Status}' status.");
        }

        if (request.Lines == null || request.Lines.Count == 0)
        {
            throw new ValidationException(nameof(request.Lines), "Credit note must include at least one adjustment line.");
        }

        var issueDate = request.IssueDate ?? DateTime.UtcNow.Date;
        var fiscalYear = issueDate.Year;

        var sequence = await _dbContext.InvoiceSequences
            .FirstOrDefaultAsync(s => s.ClientId == request.ClientId && s.FiscalYear == fiscalYear && s.Prefix == "CRN", cancellationToken);

        if (sequence == null)
        {
            sequence = InvoiceSequence.Create(request.ClientId, fiscalYear, "CRN");
            await _dbContext.InvoiceSequences.AddAsync(sequence, cancellationToken);
        }

        var clientCode = !string.IsNullOrWhiteSpace(invoice.Client?.AccountCode)
            ? invoice.Client.AccountCode
            : "CLI";

        var creditNoteNumber = sequence.NextNumber(clientCode);

        var creditNote = CreditNote.Create(
            clientId: request.ClientId,
            originalInvoiceId: invoice.Id,
            creditNoteNumber: creditNoteNumber,
            issueDate: issueDate,
            currency: invoice.Currency,
            reason: request.Reason,
            disputeId: request.DisputeId);

        int lineNum = 1;
        foreach (var l in request.Lines)
        {
            var line = CreditNoteLine.Create(
                lineNumber: lineNum++,
                description: l.Description,
                quantity: l.Quantity,
                unitPrice: l.UnitPrice,
                totalAmount: l.TotalAmount,
                originalInvoiceLineId: l.OriginalInvoiceLineId);

            creditNote.AddLine(line);
        }

        await _dbContext.CreditNotes.AddAsync(creditNote, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Credit note '{CreditNoteNumber}' created for Invoice '{InvoiceNumber}'.", creditNote.CreditNoteNumber, invoice.InvoiceNumber);
        return MapToCreditNoteDto(creditNote);
    }

    public async Task<CreditNoteDto> IssueCreditNoteAsync(
        Guid creditNoteId,
        CancellationToken cancellationToken = default)
    {
        var creditNote = await _dbContext.CreditNotes
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.Id == creditNoteId, cancellationToken);

        if (creditNote == null)
        {
            throw new EntityNotFoundException(nameof(CreditNote), creditNoteId);
        }

        creditNote.Issue();
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Credit note '{CreditNoteNumber}' formally issued.", creditNote.CreditNoteNumber);
        return MapToCreditNoteDto(creditNote);
    }

    public async Task<CreditNoteDto> ApplyCreditNoteToInvoiceAsync(
        Guid creditNoteId,
        Guid invoiceId,
        decimal amount,
        CancellationToken cancellationToken = default)
    {
        var creditNote = await _dbContext.CreditNotes
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.Id == creditNoteId, cancellationToken);

        if (creditNote == null)
        {
            throw new EntityNotFoundException(nameof(CreditNote), creditNoteId);
        }

        var invoice = await _dbContext.Invoices
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);

        if (invoice == null)
        {
            throw new EntityNotFoundException(nameof(Invoice), invoiceId);
        }

        if (!string.Equals(creditNote.Currency, invoice.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(nameof(amount), $"Currency mismatch: Credit note currency '{creditNote.Currency}' does not match invoice currency '{invoice.Currency}'.");
        }

        creditNote.Apply(amount);
        invoice.ApplyCredit(amount);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Credit note '{CreditNoteNumber}' applied amount {Amount} to Invoice '{InvoiceNumber}'. Outstanding balance: {Balance}",
            creditNote.CreditNoteNumber, amount, invoice.InvoiceNumber, invoice.OutstandingBalance);

        return MapToCreditNoteDto(creditNote);
    }

    private static InvoiceDto MapToDto(Invoice i)
    {
        return new InvoiceDto(
            Id: i.Id,
            ClientId: i.ClientId,
            FacilityId: i.FacilityId,
            BillingPeriodId: i.BillingPeriodId,
            InvoiceNumber: i.InvoiceNumber,
            Status: i.Status,
            InvoiceDate: i.InvoiceDate,
            DueDate: i.DueDate,
            PaymentTermsDays: i.PaymentTermsDays,
            Currency: i.Currency,
            SubtotalAmount: i.SubtotalAmount,
            TaxRate: i.TaxRate,
            TaxAmount: i.TaxAmount,
            TotalAmount: i.TotalAmount,
            PaidAmount: i.PaidAmount,
            CreditedAmount: i.CreditedAmount,
            OutstandingBalance: i.OutstandingBalance,
            ApprovedAtUtc: i.ApprovedAtUtc,
            ApprovedByUserId: i.ApprovedByUserId,
            IssuedAtUtc: i.IssuedAtUtc,
            IssuedByUserId: i.IssuedByUserId,
            VoidedAtUtc: i.VoidedAtUtc,
            VoidedByUserId: i.VoidedByUserId,
            VoidReason: i.VoidReason,
            ReplacementInvoiceId: i.ReplacementInvoiceId,
            Notes: i.Notes,
            Lines: i.Lines.OrderBy(l => l.LineNumber).Select(l => new InvoiceLineDto(
                Id: l.Id,
                InvoiceId: l.InvoiceId,
                LineNumber: l.LineNumber,
                ChargeCategory: l.ChargeCategory,
                Description: l.Description,
                Quantity: l.Quantity,
                UnitOfMeasure: l.UnitOfMeasure,
                UnitPrice: l.UnitPrice,
                TotalAmount: l.TotalAmount,
                BillableActivityId: l.BillableActivityId,
                ActivityType: l.ActivityType,
                FacilityId: l.FacilityId,
                MetadataJson: l.MetadataJson)).ToList());
    }

    private static CreditNoteDto MapToCreditNoteDto(CreditNote c)
    {
        return new CreditNoteDto(
            Id: c.Id,
            ClientId: c.ClientId,
            OriginalInvoiceId: c.OriginalInvoiceId,
            DisputeId: c.DisputeId,
            CreditNoteNumber: c.CreditNoteNumber,
            Status: c.Status,
            IssueDate: c.IssueDate,
            Currency: c.Currency,
            TotalAmount: c.TotalAmount,
            AllocatedAmount: c.AllocatedAmount,
            UnallocatedAmount: c.UnallocatedAmount,
            Reason: c.Reason,
            VoidReason: c.VoidReason);
    }
}
