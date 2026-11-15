using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Inbound.Asns.Queries.GetAsns;

public record GetAsnsQuery : PagedQuery, IRequest<PagedResult<AdvanceShippingNoticeDto>>
{
    public Guid? ClientId { get; init; }
    public Guid? FacilityId { get; init; }
    public Guid? PurchaseOrderId { get; init; }
    public AsnStatus? Status { get; init; }
}

public class GetAsnsQueryHandler : IRequestHandler<GetAsnsQuery, PagedResult<AdvanceShippingNoticeDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public GetAsnsQueryHandler(IApplicationDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<AdvanceShippingNoticeDto>> Handle(GetAsnsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.AdvanceShippingNotices
            .AsNoTracking()
            .Include(a => a.Facility)
            .Include(a => a.PurchaseOrder)
            .Include(a => a.Lines)
                .ThenInclude(l => l.Item)
            .AsQueryable();

        if (!_currentUser.IsStaff)
        {
            query = query.Where(a => a.ClientId == _currentUser.TenantId);
        }
        else if (request.ClientId.HasValue)
        {
            query = query.Where(a => a.ClientId == request.ClientId.Value);
        }

        if (request.FacilityId.HasValue)
        {
            query = query.Where(a => a.FacilityId == request.FacilityId.Value);
        }

        if (request.PurchaseOrderId.HasValue)
        {
            query = query.Where(a => a.PurchaseOrderId == request.PurchaseOrderId.Value);
        }

        if (request.Status.HasValue)
        {
            query = query.Where(a => a.Status == request.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.Trim().ToLower();
            query = query.Where(a =>
                a.AsnNumber.ToLower().Contains(term) ||
                (a.CarrierName != null && a.CarrierName.ToLower().Contains(term)) ||
                (a.TrackingReference != null && a.TrackingReference.ToLower().Contains(term)) ||
                (a.TrailerOrContainerNumber != null && a.TrailerOrContainerNumber.ToLower().Contains(term)));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(a => new AdvanceShippingNoticeDto
            {
                Id = a.Id,
                ClientId = a.ClientId,
                FacilityId = a.FacilityId,
                FacilityCode = a.Facility.FacilityCode,
                FacilityName = a.Facility.Name,
                PurchaseOrderId = a.PurchaseOrderId,
                PoNumber = a.PurchaseOrder != null ? a.PurchaseOrder.PoNumber : null,
                AsnNumber = a.AsnNumber,
                SupplierId = a.SupplierId,
                SupplierName = a.SupplierName,
                CarrierName = a.CarrierName,
                TrackingReference = a.TrackingReference,
                TrailerOrContainerNumber = a.TrailerOrContainerNumber,
                EstimatedArrival = a.EstimatedArrival,
                ActualArrival = a.ActualArrival,
                Status = a.Status,
                Notes = a.Notes,
                CancellationReason = a.CancellationReason,
                CancelledAt = a.CancelledAt,
                Version = a.Version,
                CreatedAt = a.CreatedAt,
                Lines = a.Lines.Select(l => new AsnLineItemDto
                {
                    Id = l.Id,
                    AsnId = l.AsnId,
                    PurchaseOrderLineId = l.PurchaseOrderLineId,
                    ItemId = l.ItemId,
                    Sku = l.Item.Sku,
                    ItemDescription = l.Item.Description,
                    ExpectedQty = l.ExpectedQty,
                    ReceivedQty = l.ReceivedQty,
                    ExpectedLotNumber = l.ExpectedLotNumber,
                    ManufactureDate = l.ManufactureDate,
                    ExpiryDate = l.ExpiryDate,
                    PackagingUnitId = l.PackagingUnitId,
                    Notes = l.Notes
                }).ToList()
            })
            .ToListAsync(cancellationToken);

        return PagedResult<AdvanceShippingNoticeDto>.Create(items, totalCount, request.PageNumber, request.PageSize);
    }
}

