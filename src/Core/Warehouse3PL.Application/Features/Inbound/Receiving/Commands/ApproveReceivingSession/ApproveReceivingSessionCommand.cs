using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Exceptions;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.Application.Features.Inbound.Receiving.Commands.ApproveReceivingSession;

public record ApproveReceivingSessionCommand(Guid ReceivingSessionId) : IRequest<ReceivingSessionDto>;

public class ApproveReceivingSessionCommandValidator : AbstractValidator<ApproveReceivingSessionCommand>
{
    public ApproveReceivingSessionCommandValidator()
    {
        RuleFor(x => x.ReceivingSessionId)
            .NotEmpty().WithMessage("Receiving session ID is required.");
    }
}

public class ApproveReceivingSessionCommandHandler : IRequestHandler<ApproveReceivingSessionCommand, ReceivingSessionDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly WarehouseOperationalAuthorization _operationalAuth;

    public ApproveReceivingSessionCommandHandler(
        IApplicationDbContext context,
        ICurrentUserContext currentUser,
        WarehouseOperationalAuthorization operationalAuth)
    {
        _context = context;
        _currentUser = currentUser;
        _operationalAuth = operationalAuth;
    }

    public async Task<ReceivingSessionDto> Handle(ApproveReceivingSessionCommand request, CancellationToken cancellationToken)
    {
        var session = await _context.ReceivingSessions
            .Include(s => s.Facility)
            .Include(s => s.DockDoor)
            .Include(s => s.YardVisit)
            .Include(s => s.Client)
            .Include(s => s.Discrepancies)
            .Include(s => s.Lines)
                .ThenInclude(l => l.Item)
            .Include(s => s.Lines)
                .ThenInclude(l => l.StagingLocation)
            .Include(s => s.AdvanceShippingNotice)
            .Include(s => s.PurchaseOrder)
            .FirstOrDefaultAsync(s => s.Id == request.ReceivingSessionId, cancellationToken);

        if (session == null)
        {
            throw new EntityNotFoundException(nameof(ReceivingSession), request.ReceivingSessionId);
        }

        // Operational Authorization
        await _operationalAuth.ValidateReceivingSessionAccessAsync(session, cancellationToken);

        // Supervisor authorization requirement: Staff or SuperAdmin with Inbound permissions
        if (!_currentUser.IsSuperAdmin &&
            (!_currentUser.IsStaff || !_currentUser.HasPermission(Permissions.Inbound.Approve)))
        {
            throw new ForbiddenException("Supervisor approval requires the dedicated inbound approval permission.");
        }

        var supervisorId = _currentUser.UserId ?? Guid.NewGuid();
        session.ApproveSupervisor(supervisorId);

        await _context.SaveChangesAsync(cancellationToken);

        return new ReceivingSessionDto
        {
            Id = session.Id,
            FacilityId = session.FacilityId,
            FacilityCode = session.Facility?.FacilityCode ?? string.Empty,
            DockDoorId = session.DockDoorId,
            DoorNumber = session.DockDoor?.DoorNumber ?? string.Empty,
            YardVisitId = session.YardVisitId,
            VisitNumber = session.YardVisit?.VisitNumber ?? string.Empty,
            TrailerNumber = session.YardVisit?.TrailerNumber ?? string.Empty,
            ClientId = session.ClientId,
            ClientName = session.Client?.CompanyName,
            AsnId = session.AsnId,
            AsnNumber = session.AdvanceShippingNotice?.AsnNumber,
            PurchaseOrderId = session.PurchaseOrderId,
            PoNumber = session.PurchaseOrder?.PoNumber,
            SessionNumber = session.SessionNumber,
            Status = session.Status,
            StartedAtUtc = session.StartedAtUtc,
            CompletedAtUtc = session.CompletedAtUtc,
            ReceivedByUserId = session.ReceivedByUserId,
            InitialProbeTemperature = session.InitialProbeTemperature,
            SealNumber = session.SealNumber,
            SealIntact = session.SealIntact,
            Notes = session.Notes,
            IsBlindReceiving = session.IsBlindReceiving,
            RequiresSupervisorApproval = session.RequiresSupervisorApproval,
            IsSupervisorApproved = session.IsSupervisorApproved,
            ApprovedByUserId = session.ApprovedByUserId,
            ApprovedAtUtc = session.ApprovedAtUtc,
            Version = session.Version,
            Lines = session.Lines.Select(l => new ReceiptLineDto
            {
                Id = l.Id,
                ReceivingSessionId = l.ReceivingSessionId,
                ItemId = l.ItemId,
                Sku = l.Item?.Sku ?? string.Empty,
                ItemDescription = l.Item?.Description ?? string.Empty,
                PurchaseOrderLineId = l.PurchaseOrderLineId,
                AsnLineItemId = l.AsnLineItemId,
                Lpn = l.Lpn,
                QuantityReceived = l.QuantityReceived,
                QuantityExpected = l.QuantityExpected,
                LotNumber = l.LotNumber,
                SerialNumber = l.SerialNumber,
                CatchWeightKg = l.CatchWeightKg,
                ManufactureDate = l.ManufactureDate,
                ExpirationDate = l.ExpirationDate,
                Condition = l.Condition,
                StagingLocationId = l.StagingLocationId,
                StagingLocationCode = l.StagingLocation?.LocationCode,
                Notes = l.Notes,
                ScannedAtUtc = l.ScannedAtUtc,
                Version = l.Version
            }).ToList(),
            Discrepancies = session.Discrepancies.Select(d => new ReceivingDiscrepancyDto
            {
                Id = d.Id,
                ReceivingSessionId = d.ReceivingSessionId,
                ReceiptLineId = d.ReceiptLineId,
                Type = d.Type,
                DiscrepantQuantity = d.DiscrepantQuantity,
                Notes = d.Notes,
                Status = d.Status,
                ReportedAtUtc = d.ReportedAtUtc,
                ResolvedAtUtc = d.ResolvedAtUtc,
                ResolvedByUserId = d.ResolvedByUserId,
                ResolutionNotes = d.ResolutionNotes,
                Version = d.Version
            }).ToList()
        };
    }
}
