using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Inbound.Receiving.Queries.GetReceivingSessions;

public record GetReceivingSessionsQuery : PagedQuery, IRequest<PagedResult<ReceivingSessionDto>>
{
    public Guid? FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public Guid? DockDoorId { get; init; }
    public ReceivingStatus? Status { get; init; }
    public DateTime? DateFrom { get; init; }
    public DateTime? DateTo { get; init; }
}

public class GetReceivingSessionsQueryHandler : IRequestHandler<GetReceivingSessionsQuery, PagedResult<ReceivingSessionDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public GetReceivingSessionsQueryHandler(IApplicationDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<ReceivingSessionDto>> Handle(GetReceivingSessionsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.ReceivingSessions
            .AsNoTracking()
            .Include(s => s.Facility)
            .Include(s => s.DockDoor)
            .Include(s => s.YardVisit)
            .Include(s => s.Client)
            .Include(s => s.AdvanceShippingNotice)
            .Include(s => s.PurchaseOrder)
            .Include(s => s.Lines)
                .ThenInclude(l => l.Item)
            .Include(s => s.Discrepancies)
            .AsQueryable();

        if (!_currentUser.IsStaff)
        {
            query = query.Where(s => s.ClientId == _currentUser.TenantId || s.ClientId == null);
        }
        else if (request.ClientId.HasValue)
        {
            query = query.Where(s => s.ClientId == request.ClientId.Value);
        }

        if (request.FacilityId.HasValue)
        {
            query = query.Where(s => s.FacilityId == request.FacilityId.Value);
        }

        if (request.DockDoorId.HasValue)
        {
            query = query.Where(s => s.DockDoorId == request.DockDoorId.Value);
        }

        if (request.Status.HasValue)
        {
            query = query.Where(s => s.Status == request.Status.Value);
        }

        if (request.DateFrom.HasValue)
        {
            query = query.Where(s => s.StartedAtUtc >= request.DateFrom.Value);
        }

        if (request.DateTo.HasValue)
        {
            query = query.Where(s => s.StartedAtUtc <= request.DateTo.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.Trim().ToLower();
            query = query.Where(s =>
                s.SessionNumber.ToLower().Contains(term) ||
                (s.AdvanceShippingNotice != null && s.AdvanceShippingNotice.AsnNumber.ToLower().Contains(term)) ||
                (s.PurchaseOrder != null && s.PurchaseOrder.PoNumber.ToLower().Contains(term)) ||
                (s.DockDoor != null && s.DockDoor.DoorNumber.ToLower().Contains(term)) ||
                (s.YardVisit != null && s.YardVisit.TrailerNumber.ToLower().Contains(term)));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var sessions = await query
            .OrderByDescending(s => s.StartedAtUtc)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var items = sessions.Select(s => new ReceivingSessionDto
        {
            Id = s.Id,
            FacilityId = s.FacilityId,
            FacilityCode = s.Facility.FacilityCode,
            DockDoorId = s.DockDoorId,
            DoorNumber = s.DockDoor?.DoorNumber,
            YardVisitId = s.YardVisitId,
            VisitNumber = s.YardVisit?.VisitNumber,
            TrailerNumber = s.YardVisit?.TrailerNumber,
            ClientId = s.ClientId,
            ClientName = s.Client?.CompanyName,
            AsnId = s.AsnId,
            AsnNumber = s.AdvanceShippingNotice?.AsnNumber,
            PurchaseOrderId = s.PurchaseOrderId,
            PoNumber = s.PurchaseOrder?.PoNumber,
            SessionNumber = s.SessionNumber,
            Status = s.Status,
            StartedAtUtc = s.StartedAtUtc,
            CompletedAtUtc = s.CompletedAtUtc,
            ReceivedByUserId = s.ReceivedByUserId,
            InitialProbeTemperature = s.InitialProbeTemperature,
            SealNumber = s.SealNumber,
            SealIntact = s.SealIntact,
            Notes = s.Notes,
            IsBlindReceiving = s.IsBlindReceiving,
            RequiresSupervisorApproval = s.RequiresSupervisorApproval,
            IsSupervisorApproved = s.IsSupervisorApproved,
            ApprovedByUserId = s.ApprovedByUserId,
            ApprovedAtUtc = s.ApprovedAtUtc,
            Version = s.Version,
            Lines = s.Lines.Select(l => new ReceiptLineDto
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
                Notes = l.Notes,
                ScannedAtUtc = l.ScannedAtUtc,
                Version = l.Version
            }).ToList(),
            Discrepancies = s.Discrepancies.Select(d => new ReceivingDiscrepancyDto
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
        }).ToList();

        return PagedResult<ReceivingSessionDto>.Create(items, totalCount, request.PageNumber, request.PageSize);
    }
}
