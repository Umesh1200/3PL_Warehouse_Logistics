using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Inbound.Receiving.Queries.GetReceivingSessionById;

public record GetReceivingSessionByIdQuery(Guid Id) : IRequest<ReceivingSessionDto>;

public class GetReceivingSessionByIdQueryHandler : IRequestHandler<GetReceivingSessionByIdQuery, ReceivingSessionDto>
{
    private readonly IApplicationDbContext _context;
    private readonly WarehouseOperationalAuthorization _operationalAuth;

    public GetReceivingSessionByIdQueryHandler(
        IApplicationDbContext context,
        WarehouseOperationalAuthorization operationalAuth)
    {
        _context = context;
        _operationalAuth = operationalAuth;
    }

    public async Task<ReceivingSessionDto> Handle(GetReceivingSessionByIdQuery request, CancellationToken cancellationToken)
    {
        var session = await _context.ReceivingSessions
            .AsNoTracking()
            .Include(s => s.Facility)
            .Include(s => s.DockDoor)
            .Include(s => s.YardVisit)
            .Include(s => s.Client)
            .Include(s => s.AdvanceShippingNotice)
            .Include(s => s.PurchaseOrder)
            .Include(s => s.Lines)
                .ThenInclude(l => l.Item)
            .Include(s => s.Lines)
                .ThenInclude(l => l.StagingLocation)
            .Include(s => s.Discrepancies)
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken);

        if (session == null)
        {
            throw new EntityNotFoundException(nameof(ReceivingSession), request.Id);
        }

        // Enforce trusted tenant and authorized facility scope
        await _operationalAuth.ValidateReceivingSessionAccessAsync(session, cancellationToken);

        return new ReceivingSessionDto
        {
            Id = session.Id,
            FacilityId = session.FacilityId,
            FacilityCode = session.Facility.FacilityCode,
            DockDoorId = session.DockDoorId,
            DoorNumber = session.DockDoor?.DoorNumber,
            YardVisitId = session.YardVisitId,
            VisitNumber = session.YardVisit?.VisitNumber,
            TrailerNumber = session.YardVisit?.TrailerNumber,
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
                Sku = l.Item.Sku,
                ItemDescription = l.Item.Description,
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
                StagingLocationCode = l.StagingLocation != null ? l.StagingLocation.LocationCode : null,
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
