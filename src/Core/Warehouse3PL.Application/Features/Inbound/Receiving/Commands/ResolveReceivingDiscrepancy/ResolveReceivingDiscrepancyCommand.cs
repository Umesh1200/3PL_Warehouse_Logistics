using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Inbound.Receiving.Commands.ResolveReceivingDiscrepancy;

public record ResolveReceivingDiscrepancyCommand : IRequest<ReceivingDiscrepancyDto>
{
    public Guid ReceivingSessionId { get; init; }
    public Guid DiscrepancyId { get; init; }
    public DiscrepancyResolutionStatus Resolution { get; init; }
    public string? Notes { get; init; }
}

public class ResolveReceivingDiscrepancyCommandValidator : AbstractValidator<ResolveReceivingDiscrepancyCommand>
{
    public ResolveReceivingDiscrepancyCommandValidator()
    {
        RuleFor(x => x.ReceivingSessionId)
            .NotEmpty().WithMessage("Receiving session ID is required.");

        RuleFor(x => x.DiscrepancyId)
            .NotEmpty().WithMessage("Discrepancy ID is required.");

        RuleFor(x => x.Resolution)
            .IsInEnum().WithMessage("A valid discrepancy resolution status is required.");
    }
}

public class ResolveReceivingDiscrepancyCommandHandler : IRequestHandler<ResolveReceivingDiscrepancyCommand, ReceivingDiscrepancyDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly WarehouseOperationalAuthorization _operationalAuth;

    public ResolveReceivingDiscrepancyCommandHandler(
        IApplicationDbContext context,
        ICurrentUserContext currentUser,
        WarehouseOperationalAuthorization operationalAuth)
    {
        _context = context;
        _currentUser = currentUser;
        _operationalAuth = operationalAuth;
    }

    public async Task<ReceivingDiscrepancyDto> Handle(ResolveReceivingDiscrepancyCommand request, CancellationToken cancellationToken)
    {
        var discrepancy = await _context.ReceivingDiscrepancies
            .Include(d => d.ReceivingSession)
            .Include(d => d.ReceiptLine)
            .FirstOrDefaultAsync(d => d.Id == request.DiscrepancyId && d.ReceivingSessionId == request.ReceivingSessionId, cancellationToken);

        if (discrepancy == null)
        {
            throw new EntityNotFoundException(nameof(ReceivingDiscrepancy), request.DiscrepancyId);
        }

        // Operational Authorization
        await _operationalAuth.ValidateFacilityAccessAsync(discrepancy.ReceivingSession.FacilityId, cancellationToken);

        if (!_currentUser.IsStaff && !_currentUser.IsSuperAdmin)
        {
            throw new ForbiddenException("Discrepancy resolution requires staff operational authority.");
        }

        var supervisorId = _currentUser.UserId ?? Guid.NewGuid();
        discrepancy.Resolve(request.Resolution, supervisorId, request.Notes);

        await _context.SaveChangesAsync(cancellationToken);

        return new ReceivingDiscrepancyDto
        {
            Id = discrepancy.Id,
            ReceivingSessionId = discrepancy.ReceivingSessionId,
            ReceiptLineId = discrepancy.ReceiptLineId,
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
