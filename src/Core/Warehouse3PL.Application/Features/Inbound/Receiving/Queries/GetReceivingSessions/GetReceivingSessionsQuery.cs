using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Inbound.Receiving.Queries.GetReceivingSessions;

public record GetReceivingSessionsQuery : PagedQuery, IRequest<PagedResult<ReceivingSessionDto>>
{
    public Guid? FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public Guid? DockDoorId { get; init; }
    public ReceivingStatus? Status { get; init; }
    public List<ReceivingStatus>? Statuses { get; init; }
    public DateTime? DateFrom { get; init; }
    public DateTime? DateTo { get; init; }
}

public class GetReceivingSessionsQueryHandler : IRequestHandler<GetReceivingSessionsQuery, PagedResult<ReceivingSessionDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly WarehouseOperationalAuthorization _operationalAuth;

    public GetReceivingSessionsQueryHandler(
        IApplicationDbContext context,
        WarehouseOperationalAuthorization operationalAuth)
    {
        _context = context;
        _operationalAuth = operationalAuth;
    }

    public async Task<PagedResult<ReceivingSessionDto>> Handle(GetReceivingSessionsQuery request, CancellationToken cancellationToken)
    {
        var scope = await _operationalAuth.ResolveQueryScopeAsync(request.ClientId, request.FacilityId, cancellationToken);

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

        if (scope.ClientId.HasValue)
        {
            query = query.Where(s => s.ClientId == scope.ClientId.Value);
        }

        if (scope.FacilityId.HasValue)
        {
            query = query.Where(s => s.FacilityId == scope.FacilityId.Value);
        }
        else if (scope.AuthorizedFacilityIds != null)
        {
            query = query.Where(s => scope.AuthorizedFacilityIds.Contains(s.FacilityId));
        }

        if (request.DockDoorId.HasValue)
        {
            query = query.Where(s => s.DockDoorId == request.DockDoorId.Value);
        }

        if (request.Statuses != null && request.Statuses.Count > 0)
        {
            query = query.Where(s => request.Statuses.Contains(s.Status));
        }
        else if (request.Status.HasValue)
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

        var items = await query
            .OrderByDescending(s => s.StartedAtUtc)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(s => new ReceivingSessionDto
            {
                Id = s.Id,
                FacilityId = s.FacilityId,
                FacilityCode = s.Facility.FacilityCode,
                DockDoorId = s.DockDoorId,
                DoorNumber = s.DockDoor != null ? s.DockDoor.DoorNumber : null,
                YardVisitId = s.YardVisitId,
                VisitNumber = s.YardVisit != null ? s.YardVisit.VisitNumber : null,
                TrailerNumber = s.YardVisit != null ? s.YardVisit.TrailerNumber : null,
                ClientId = s.ClientId,
                ClientName = s.Client != null ? s.Client.CompanyName : null,
                AsnId = s.AsnId,
                AsnNumber = s.AdvanceShippingNotice != null ? s.AdvanceShippingNotice.AsnNumber : null,
                PurchaseOrderId = s.PurchaseOrderId,
                PoNumber = s.PurchaseOrder != null ? s.PurchaseOrder.PoNumber : null,
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
                    StagingLocationCode = l.StagingLocation != null ? l.StagingLocation.LocationCode : null,
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
            })
            .ToListAsync(cancellationToken);

        return PagedResult<ReceivingSessionDto>.Create(items, totalCount, request.PageNumber, request.PageSize);
    }
}
