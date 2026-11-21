using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Inbound.Receiving.Commands.RecordReceivingDiscrepancy;

public record RecordReceivingDiscrepancyCommand : IRequest<ReceivingDiscrepancyDto>
{
    public Guid ReceivingSessionId { get; init; }
    public Guid? ReceiptLineId { get; init; }
    public DiscrepancyType Type { get; init; }
    public decimal DiscrepantQuantity { get; init; }
    public string Notes { get; init; } = string.Empty;
}

public class RecordReceivingDiscrepancyCommandValidator : AbstractValidator<RecordReceivingDiscrepancyCommand>
{
    public RecordReceivingDiscrepancyCommandValidator()
    {
        RuleFor(x => x.ReceivingSessionId)
            .NotEmpty().WithMessage("Receiving session ID is required.");

        RuleFor(x => x.DiscrepantQuantity)
            .GreaterThan(0).WithMessage("Discrepant quantity must be positive.");

        RuleFor(x => x.Notes)
            .NotEmpty().WithMessage("Discrepancy notes are required.")
            .MaximumLength(1000).WithMessage("Notes must not exceed 1000 characters.");
    }
}

public class RecordReceivingDiscrepancyCommandHandler : IRequestHandler<RecordReceivingDiscrepancyCommand, ReceivingDiscrepancyDto>
{
    private readonly IApplicationDbContext _context;
    private readonly WarehouseOperationalAuthorization _operationalAuth;

    public RecordReceivingDiscrepancyCommandHandler(
        IApplicationDbContext context,
        WarehouseOperationalAuthorization operationalAuth)
    {
        _context = context;
        _operationalAuth = operationalAuth;
    }

    public async Task<ReceivingDiscrepancyDto> Handle(RecordReceivingDiscrepancyCommand request, CancellationToken cancellationToken)
    {
        var session = await _context.ReceivingSessions
            .Include(s => s.Discrepancies)
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == request.ReceivingSessionId, cancellationToken);

        if (session == null)
        {
            throw new EntityNotFoundException(nameof(ReceivingSession), request.ReceivingSessionId);
        }

        // Enforce trusted tenant and authorized facility scope
        await _operationalAuth.ValidateReceivingSessionAccessAsync(session, cancellationToken);

        ReceiptLine? line = null;
        if (request.ReceiptLineId.HasValue)
        {
            line = session.Lines.FirstOrDefault(l => l.Id == request.ReceiptLineId.Value);
            if (line == null)
            {
                throw new EntityNotFoundException(nameof(ReceiptLine), request.ReceiptLineId.Value);
            }
        }

        var defaultStatus = (request.Type == DiscrepancyType.DamagedGoods || request.Type == DiscrepancyType.TempExcursion)
            ? DiscrepancyResolutionStatus.Quarantined
            : DiscrepancyResolutionStatus.PendingReview;

        var discrepancy = new ReceivingDiscrepancy
        {
            ReceivingSessionId = session.Id,
            ReceiptLineId = request.ReceiptLineId,
            Type = request.Type,
            DiscrepantQuantity = request.DiscrepantQuantity,
            Notes = request.Notes.Trim(),
            Status = defaultStatus,
            ReportedAtUtc = DateTime.UtcNow
        };

        _context.ReceivingDiscrepancies.Add(discrepancy);
        await _context.SaveChangesAsync(cancellationToken);

        return new ReceivingDiscrepancyDto
        {
            Id = discrepancy.Id,
            ReceivingSessionId = discrepancy.ReceivingSessionId,
            ReceiptLineId = discrepancy.ReceiptLineId,
            Lpn = line?.Lpn,
            Type = discrepancy.Type,
            DiscrepantQuantity = discrepancy.DiscrepantQuantity,
            Notes = discrepancy.Notes,
            Status = discrepancy.Status,
            ReportedAtUtc = discrepancy.ReportedAtUtc,
            ResolvedAtUtc = discrepancy.ResolvedAtUtc,
            ResolvedByUserId = discrepancy.ResolvedByUserId,
            ResolutionNotes = discrepancy.ResolutionNotes,
            Version = discrepancy.Version
        };
    }
}

