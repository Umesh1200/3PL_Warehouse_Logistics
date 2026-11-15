using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services.Billing;

/// <summary>
/// Service managing customer and operational invoice disputes:
/// registration, commercial review, and resolution with automatic credit note generation and invoice balance adjustment.
/// DB-SPEC-3PL-2026 Phase 5.5 Invoicing Engine &amp; AR Ledger.
/// </summary>
public class DisputeManagementService : IDisputeManagementService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ILogger<DisputeManagementService> _logger;

    public DisputeManagementService(
        IApplicationDbContext dbContext,
        ILogger<DisputeManagementService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<InvoiceDisputeDto> SubmitDisputeAsync(
        SubmitDisputeRequest request,
        CancellationToken cancellationToken = default)
    {
        var invoice = await _dbContext.Invoices
            .Include(i => i.Disputes)
            .FirstOrDefaultAsync(i => i.Id == request.InvoiceId && i.ClientId == request.ClientId, cancellationToken);

        if (invoice == null)
        {
            throw new EntityNotFoundException(nameof(Invoice), request.InvoiceId);
        }

        if (invoice.Status == InvoiceStatus.Draft || invoice.Status == InvoiceStatus.Voided)
        {
            throw new InvalidDomainStateException($"Cannot dispute an invoice in '{invoice.Status}' status.");
        }

        if (request.DisputedAmount <= 0)
        {
            throw new ValidationException(nameof(request.DisputedAmount), "Disputed amount must be strictly positive.");
        }

        if (request.DisputedAmount > invoice.TotalAmount)
        {
            throw new InvalidDomainStateException(
                $"Disputed amount ({request.DisputedAmount}) cannot exceed invoice total ({invoice.TotalAmount}).");
        }

        var disputeCount = invoice.Disputes.Count + 1;
        var disputeReference = $"DSP-{invoice.InvoiceNumber}-{disputeCount:D2}";

        var dispute = InvoiceDispute.Create(
            clientId: request.ClientId,
            invoiceId: invoice.Id,
            disputeReference: disputeReference,
            disputedAmount: request.DisputedAmount,
            reasonCode: request.ReasonCode,
            reasonDescription: request.ReasonDescription,
            submittedByUserId: request.SubmittedByUserId,
            invoiceLineId: request.InvoiceLineId);

        await _dbContext.InvoiceDisputes.AddAsync(dispute, cancellationToken);
        invoice.MarkDisputed(request.ReasonDescription);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Dispute '{DisputeRef}' submitted for Invoice '{InvoiceNumber}'. Disputed amount: {Amount}",
            dispute.DisputeReference, invoice.InvoiceNumber, dispute.DisputedAmount);

        return MapToDto(dispute);
    }

    public async Task<InvoiceDisputeDto> ReviewDisputeAsync(
        ReviewDisputeRequest request,
        CancellationToken cancellationToken = default)
    {
        var dispute = await _dbContext.InvoiceDisputes
            .FirstOrDefaultAsync(d => d.Id == request.DisputeId, cancellationToken);

        if (dispute == null)
        {
            throw new EntityNotFoundException(nameof(InvoiceDispute), request.DisputeId);
        }

        dispute.Review(request.ReviewedByUserId, request.ReviewNotes);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Dispute '{DisputeRef}' marked UnderReview by user {UserId}.", dispute.DisputeReference, request.ReviewedByUserId);
        return MapToDto(dispute);
    }

    public async Task<InvoiceDisputeDto> ResolveDisputeAsync(
        ResolveDisputeRequest request,
        CancellationToken cancellationToken = default)
    {
        var dispute = await _dbContext.InvoiceDisputes
            .Include(d => d.Invoice)
                .ThenInclude(i => i.Client)
            .Include(d => d.Invoice)
                .ThenInclude(i => i.Lines)
            .Include(d => d.Invoice)
                .ThenInclude(i => i.Disputes)
            .FirstOrDefaultAsync(d => d.Id == request.DisputeId, cancellationToken);

        if (dispute == null)
        {
            throw new EntityNotFoundException(nameof(InvoiceDispute), request.DisputeId);
        }

        if (dispute.Status == DisputeStatus.Resolved || dispute.Status == DisputeStatus.Accepted || dispute.Status == DisputeStatus.Rejected)
        {
            throw new InvalidDomainStateException(
                $"Dispute '{dispute.DisputeReference}' has already been resolved with status '{dispute.Status}'. Duplicate resolution is prohibited.");
        }

        var invoice = dispute.Invoice;

        if (request.Accept)
        {
            var approvedAmount = request.ApprovedCreditAmount ?? dispute.DisputedAmount;
            if (approvedAmount <= 0)
            {
                throw new ValidationException(nameof(request.ApprovedCreditAmount), "Approved credit amount must be positive.");
            }

            if (approvedAmount > dispute.DisputedAmount)
            {
                throw new InvalidDomainStateException($"Approved credit amount ({approvedAmount}) cannot exceed disputed amount ({dispute.DisputedAmount}).");
            }

            var fiscalYear = DateTime.UtcNow.Year;
            var sequence = await _dbContext.InvoiceSequences
                .FirstOrDefaultAsync(s => s.ClientId == dispute.ClientId && s.FiscalYear == fiscalYear && s.Prefix == "CRN", cancellationToken);

            if (sequence == null)
            {
                sequence = InvoiceSequence.Create(dispute.ClientId, fiscalYear, "CRN");
                await _dbContext.InvoiceSequences.AddAsync(sequence, cancellationToken);
            }

            var clientCode = !string.IsNullOrWhiteSpace(invoice.Client?.AccountCode)
                ? invoice.Client.AccountCode
                : "CLI";

            var creditNoteNumber = sequence.NextNumber(clientCode);

            var creditNote = CreditNote.Create(
                clientId: dispute.ClientId,
                originalInvoiceId: invoice.Id,
                creditNoteNumber: creditNoteNumber,
                issueDate: DateTime.UtcNow.Date,
                currency: invoice.Currency,
                reason: $"Dispute Resolution: {dispute.DisputeReference} - {request.ResolutionNotes}",
                disputeId: dispute.Id);

            var lineDesc = dispute.InvoiceLineId.HasValue
                ? $"Credit for dispute {dispute.DisputeReference} against line item"
                : $"Credit for dispute {dispute.DisputeReference}";

            var creditLine = CreditNoteLine.Create(
                lineNumber: 1,
                description: lineDesc,
                quantity: 1,
                unitPrice: approvedAmount,
                totalAmount: approvedAmount,
                originalInvoiceLineId: dispute.InvoiceLineId);

            creditNote.AddLine(creditLine);
            creditNote.Issue();

            // Apply credit note against invoice
            creditNote.Apply(approvedAmount);
            invoice.ApplyCredit(approvedAmount);

            await _dbContext.CreditNotes.AddAsync(creditNote, cancellationToken);

            dispute.Accept(request.ResolvedByUserId, approvedAmount, request.ResolutionNotes, creditNote.Id);
            dispute.MarkResolved();

            // If no other unresolved disputes exist on this invoice, update invoice status
            var otherActiveDisputes = invoice.Disputes.Any(d => d.Id != dispute.Id && (d.Status == DisputeStatus.Submitted || d.Status == DisputeStatus.UnderReview));
            if (!otherActiveDisputes)
            {
                invoice.ResolveDispute();
            }

            _logger.LogInformation(
                "Dispute '{DisputeRef}' accepted with Credit Note '{CRN}' for {Amount}. Invoice balance: {Balance}",
                dispute.DisputeReference, creditNote.CreditNoteNumber, approvedAmount, invoice.OutstandingBalance);
        }
        else
        {
            dispute.Reject(request.ResolvedByUserId, request.ResolutionNotes);
            dispute.MarkResolved();

            var otherActiveDisputes = invoice.Disputes.Any(d => d.Id != dispute.Id && (d.Status == DisputeStatus.Submitted || d.Status == DisputeStatus.UnderReview));
            if (!otherActiveDisputes)
            {
                invoice.ResolveDispute();
            }

            _logger.LogInformation("Dispute '{DisputeRef}' rejected. Reason: {Notes}", dispute.DisputeReference, request.ResolutionNotes);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapToDto(dispute);
    }

    public async Task<InvoiceDisputeDto?> GetDisputeByIdAsync(
        Guid disputeId,
        CancellationToken cancellationToken = default)
    {
        var dispute = await _dbContext.InvoiceDisputes
            .FirstOrDefaultAsync(d => d.Id == disputeId, cancellationToken);

        return dispute == null ? null : MapToDto(dispute);
    }

    private static InvoiceDisputeDto MapToDto(InvoiceDispute d)
    {
        return new InvoiceDisputeDto(
            Id: d.Id,
            ClientId: d.ClientId,
            InvoiceId: d.InvoiceId,
            InvoiceLineId: d.InvoiceLineId,
            DisputeReference: d.DisputeReference,
            DisputedAmount: d.DisputedAmount,
            Status: d.Status,
            ReasonCode: d.ReasonCode,
            ReasonDescription: d.ReasonDescription,
            SubmittedAtUtc: d.SubmittedAtUtc,
            ReviewedAtUtc: d.ReviewedAtUtc,
            ReviewNotes: d.ReviewNotes,
            ResolvedAtUtc: d.ResolvedAtUtc,
            ResolutionNotes: d.ResolutionNotes,
            ApprovedCreditAmount: d.ApprovedCreditAmount,
            CreditNoteId: d.CreditNoteId);
    }
}
