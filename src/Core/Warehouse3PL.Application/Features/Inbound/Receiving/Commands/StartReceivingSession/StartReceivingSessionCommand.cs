using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Entities.Dock;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.Application.Features.Inbound.Receiving.Commands.StartReceivingSession;

public record StartReceivingSessionCommand : IRequest<ReceivingSessionDto>
{
    public Guid FacilityId { get; init; }
    public Guid DockDoorId { get; init; }
    public Guid YardVisitId { get; init; }
    public Guid? AsnId { get; init; }
    public Guid? PurchaseOrderId { get; init; }
    public decimal? InitialProbeTemperature { get; init; }
    public string? SealNumber { get; init; }
    public bool? SealIntact { get; init; }
    public string? Notes { get; init; }
    public bool IsBlindReceiving { get; init; }
}

public class StartReceivingSessionCommandValidator : AbstractValidator<StartReceivingSessionCommand>
{
    public StartReceivingSessionCommandValidator()
    {
        RuleFor(x => x.FacilityId)
            .NotEmpty().WithMessage("Facility ID is required.");

        RuleFor(x => x.DockDoorId)
            .NotEmpty().WithMessage("Dock door ID is required.");

        RuleFor(x => x.YardVisitId)
            .NotEmpty().WithMessage("Yard visit ID is required.");
    }
}

public class StartReceivingSessionCommandHandler : IRequestHandler<StartReceivingSessionCommand, ReceivingSessionDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly WarehouseOperationalAuthorization _operationalAuth;

    public StartReceivingSessionCommandHandler(
        IApplicationDbContext context,
        ICurrentUserContext currentUser,
        WarehouseOperationalAuthorization operationalAuth)
    {
        _context = context;
        _currentUser = currentUser;
        _operationalAuth = operationalAuth;
    }

    public async Task<ReceivingSessionDto> Handle(StartReceivingSessionCommand request, CancellationToken cancellationToken)
    {
        // Enforce facility authorization
        await _operationalAuth.ValidateFacilityAccessAsync(request.FacilityId, cancellationToken);

        var facility = await _context.Facilities
            .FirstOrDefaultAsync(f => f.Id == request.FacilityId, cancellationToken);

        if (facility == null)
        {
            throw new EntityNotFoundException(nameof(Facility), request.FacilityId);
        }

        var yardVisit = await _context.YardVisits
            .Include(y => y.AdvanceShippingNotice)
            .FirstOrDefaultAsync(y => y.Id == request.YardVisitId && y.FacilityId == request.FacilityId, cancellationToken);

        if (yardVisit == null)
        {
            throw new EntityNotFoundException(nameof(YardVisit), request.YardVisitId);
        }

        // State Machine Rule: Trailer must be confirmed AtDock
        if (yardVisit.Status != YardVisitStatus.AtDock)
        {
            throw new ValidationException(
                nameof(request.YardVisitId),
                $"Cannot initiate receiving session. Trailer '{yardVisit.TrailerNumber}' is in '{yardVisit.Status}' status. Trailer must be confirmed 'AtDock' before physical receiving can begin.");
        }

        var dockDoor = await _context.DockDoors
            .FirstOrDefaultAsync(d => d.Id == request.DockDoorId && d.FacilityId == request.FacilityId, cancellationToken);

        if (dockDoor == null)
        {
            throw new EntityNotFoundException(nameof(DockDoor), request.DockDoorId);
        }

        if (!dockDoor.IsOperational)
        {
            throw new ValidationException(nameof(request.DockDoorId), $"Dock door '{dockDoor.DoorNumber}' is not operational (Status: {dockDoor.Status}).");
        }

        // Concurrency Rule: Only one active session per dock door
        var activeSessionOnDoor = await _context.ReceivingSessions
            .AnyAsync(s =>
                s.DockDoorId == request.DockDoorId &&
                (s.Status == ReceivingStatus.Started || s.Status == ReceivingStatus.InProgress),
                cancellationToken);

        if (activeSessionOnDoor)
        {
            throw new ValidationException(nameof(request.DockDoorId), $"Dock door '{dockDoor.DoorNumber}' already has an active receiving session in progress.");
        }

        Guid? targetClientId = yardVisit.ClientId;
        Guid? targetAsnId = request.AsnId ?? yardVisit.AsnId;
        Guid? targetPoId = request.PurchaseOrderId;
        AdvanceShippingNotice? asn = null;

        if (targetAsnId.HasValue)
        {
            asn = await _context.AdvanceShippingNotices
                .IgnoreQueryFilters()
                .Include(a => a.Lines)
                    .ThenInclude(l => l.Item)
                .Include(a => a.PurchaseOrder)
                .FirstOrDefaultAsync(a => a.Id == targetAsnId.Value, cancellationToken);

            if (asn != null)
            {
                if (asn.FacilityId != request.FacilityId)
                {
                    throw new ValidationException(nameof(request.AsnId), "ASN facility does not match receiving facility.");
                }

                targetClientId ??= asn.ClientId;
                targetPoId ??= asn.PurchaseOrderId;

                if (asn.Status == AsnStatus.Arrived || asn.Status == AsnStatus.InTransit)
                {
                    asn.StartReceiving();
                }
            }
        }

        var isBlind = request.IsBlindReceiving || (targetAsnId == null && targetPoId == null);
        var requiresSupervisor = isBlind;

        if (!_currentUser.IsStaff)
        {
            if (_currentUser.UserType != UserType.Client || !_currentUser.TenantId.HasValue)
            {
                throw new ForbiddenException("Authenticated client identity is required to start a receiving session.");
            }

            if (isBlind)
            {
                throw new ForbiddenException("Client users cannot initiate blind receiving sessions.");
            }

            if (targetClientId.HasValue && targetClientId.Value != _currentUser.TenantId.Value)
            {
                throw new ForbiddenException("Client cannot start receiving session for another client.");
            }

            targetClientId = _currentUser.TenantId.Value;
        }

        var sessionCount = await _context.ReceivingSessions
            .CountAsync(s => s.FacilityId == request.FacilityId, cancellationToken);

        var datePrefix = DateTime.UtcNow.ToString("yyyyMMdd");
        var sessionNumber = $"RCV-{facility.FacilityCode}-{datePrefix}-{(sessionCount + 1):D4}";

        var receivingUserId = _currentUser.UserId ?? Guid.NewGuid();

        var session = new ReceivingSession
        {
            FacilityId = request.FacilityId,
            DockDoorId = request.DockDoorId,
            YardVisitId = request.YardVisitId,
            ClientId = targetClientId,
            AsnId = targetAsnId,
            PurchaseOrderId = targetPoId,
            SessionNumber = sessionNumber,
            Status = ReceivingStatus.Started,
            StartedAtUtc = DateTime.UtcNow,
            ReceivedByUserId = receivingUserId,
            InitialProbeTemperature = request.InitialProbeTemperature,
            SealNumber = request.SealNumber,
            SealIntact = request.SealIntact,
            Notes = request.Notes,
            IsBlindReceiving = isBlind,
            RequiresSupervisorApproval = requiresSupervisor,
            IsSupervisorApproved = false
        };

        // Cold Chain Validation: Temp-sensitive shipments on refrigerated doors
        if (dockDoor.SupportsRefrigerated && asn != null)
        {
            var coldLine = asn.Lines.FirstOrDefault(l =>
                l.Item.StorageTempClass == StorageTempClass.Refrigerated ||
                l.Item.StorageTempClass == StorageTempClass.Frozen);

            if (coldLine != null)
            {
                if (!request.InitialProbeTemperature.HasValue)
                {
                    throw new ValidationException(nameof(request.InitialProbeTemperature), "Initial probe temperature recording is mandatory for refrigerated / cold-chain shipments.");
                }

                var temp = request.InitialProbeTemperature.Value;
                bool isExcursion = (coldLine.Item.StorageTempClass == StorageTempClass.Refrigerated && (temp < 0m || temp > 8m)) ||
                                   (coldLine.Item.StorageTempClass == StorageTempClass.Frozen && temp > -15m);

                if (isExcursion)
                {
                    session.FlagSession($"Temperature excursion detected: Recorded {temp}°C for {coldLine.Item.StorageTempClass} product.");

                    var discrepancy = new ReceivingDiscrepancy
                    {
                        ReceivingSessionId = session.Id,
                        Type = DiscrepancyType.TempExcursion,
                        DiscrepantQuantity = coldLine.ExpectedQty,
                        Notes = $"Initial probe temperature {temp}°C exceeds safe bounds for {coldLine.Item.StorageTempClass} items (SKU: {coldLine.Item.Sku}).",
                        Status = DiscrepancyResolutionStatus.PendingReview,
                        ReportedAtUtc = DateTime.UtcNow
                    };
                    session.Discrepancies.Add(discrepancy);
                }
            }
        }

        if (request.SealIntact == false)
        {
            session.FlagSession("Trailer security seal was reported broken or missing upon dock check.");
        }

        _context.ReceivingSessions.Add(session);
        await _context.SaveChangesAsync(cancellationToken);

        return new ReceivingSessionDto
        {
            Id = session.Id,
            FacilityId = session.FacilityId,
            FacilityCode = facility.FacilityCode,
            DockDoorId = session.DockDoorId,
            DoorNumber = dockDoor.DoorNumber,
            YardVisitId = session.YardVisitId,
            VisitNumber = yardVisit.VisitNumber,
            TrailerNumber = yardVisit.TrailerNumber,
            ClientId = session.ClientId,
            ClientName = null,
            AsnId = session.AsnId,
            AsnNumber = asn?.AsnNumber,
            PurchaseOrderId = session.PurchaseOrderId,
            PoNumber = asn?.PurchaseOrder?.PoNumber,
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
            Lines = [],
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
                Version = d.Version
            }).ToList()
        };
    }
}

