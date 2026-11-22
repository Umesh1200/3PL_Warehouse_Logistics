using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.OrderManagement.Queries.GetOutboundOrders;

public record GetOutboundOrdersQuery : IRequest<PagedResult<OutboundOrderDto>>
{
    public Guid? ClientId { get; init; }
    public Guid? FacilityId { get; init; }
    public OutboundOrderStatus? Status { get; init; }
    public OutboundOrderType? OrderType { get; init; }
    public int? Priority { get; init; }
    public string? SearchTerm { get; init; }
    public DateTime? FromDateUtc { get; init; }
    public DateTime? ToDateUtc { get; init; }

    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public class GetOutboundOrdersQueryHandler : IRequestHandler<GetOutboundOrdersQuery, PagedResult<OutboundOrderDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly ICurrentUserContext _currentUser;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public GetOutboundOrdersQueryHandler(
        IApplicationDbContext context,
        ITenantProvider tenantProvider,
        ICurrentUserContext currentUser,
        OutboundOperationalAuthorization? outboundAuth = null)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _currentUser = currentUser;
        _outboundAuth = outboundAuth ?? new OutboundOperationalAuthorization(context, currentUser, tenantProvider);
    }

    public async Task<PagedResult<OutboundOrderDto>> Handle(GetOutboundOrdersQuery request, CancellationToken cancellationToken)
    {
        var scope = await _outboundAuth.ResolveOutboundQueryScopeAsync(
            request.ClientId,
            request.FacilityId,
            allowCrossFacilityRead: true,
            allowClient: true,
            cancellationToken);

        var query = _context.OutboundOrders
            .Include(o => o.ShippingAddress)
            .Include(o => o.Customer)
            .Include(o => o.Facility)
            .Include(o => o.DestinationFacility)
            .Include(o => o.Lines)
                .ThenInclude(l => l.Item)
            .AsNoTracking()
            .AsQueryable();

        if (scope.ClientId.HasValue)
        {
            query = query.Where(o => o.ClientId == scope.ClientId.Value);
        }

        if (scope.FacilityId.HasValue)
        {
            query = query.Where(o => o.FacilityId == scope.FacilityId.Value);
        }
        else if (scope.AuthorizedFacilityIds != null)
        {
            query = query.Where(o => scope.AuthorizedFacilityIds.Contains(o.FacilityId));
        }

        if (request.Status.HasValue)
        {
            query = query.Where(o => o.Status == request.Status.Value);
        }

        if (request.OrderType.HasValue)
        {
            query = query.Where(o => o.OrderType == request.OrderType.Value);
        }

        if (request.Priority.HasValue)
        {
            query = query.Where(o => o.Priority == request.Priority.Value);
        }

        if (request.FromDateUtc.HasValue)
        {
            query = query.Where(o => o.OrderDateUtc >= request.FromDateUtc.Value);
        }

        if (request.ToDateUtc.HasValue)
        {
            query = query.Where(o => o.OrderDateUtc <= request.ToDateUtc.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.Trim().ToLower();
            query = query.Where(o =>
                o.OrderNumber.ToLower().Contains(term) ||
                (o.ExternalOrderReference != null && o.ExternalOrderReference.ToLower().Contains(term)) ||
                o.CustomerName.ToLower().Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var pageNumber = request.PageNumber <= 0 ? 1 : request.PageNumber;
        var pageSize = request.PageSize <= 0 ? 20 : (request.PageSize > 100 ? 100 : request.PageSize);

        var items = await query
            .OrderByDescending(o => o.Priority)
            .ThenBy(o => o.RequiredShipDateUtc)
            .ThenByDescending(o => o.OrderDateUtc)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var dtos = items.Select(order => new OutboundOrderDto
        {
            Id = order.Id,
            ClientId = order.ClientId,
            FacilityId = order.FacilityId,
            FacilityName = order.Facility?.Name ?? string.Empty,
            DestinationFacilityId = order.DestinationFacilityId,
            DestinationFacilityName = order.DestinationFacility?.Name,
            CustomerId = order.CustomerId,
            CustomerCode = order.Customer?.CustomerCode,
            CustomerName = order.CustomerName,
            OrderNumber = order.OrderNumber,
            ExternalOrderReference = order.ExternalOrderReference,
            OrderType = order.OrderType,
            Status = order.Status,
            StatusBeforeHold = order.StatusBeforeHold,
            Priority = order.Priority,
            ServiceLevel = order.ServiceLevel,
            CarrierCode = order.CarrierCode,
            DestinationAddress = order.DestinationAddress,
            OrderDateUtc = order.OrderDateUtc,
            RequiredShipDateUtc = order.RequiredShipDateUtc,
            EarliestShipDateUtc = order.EarliestShipDateUtc,
            HoldReason = order.HoldReason,
            HeldAtUtc = order.HeldAtUtc,
            CancellationReason = order.CancellationReason,
            CancelledAtUtc = order.CancelledAtUtc,
            Notes = order.Notes,
            Version = order.Version,
            CreatedAt = order.CreatedAt,
            ShippingAddress = order.ShippingAddress == null ? null : new ShippingAddressDto
            {
                RecipientName = order.ShippingAddress.RecipientName,
                CompanyName = order.ShippingAddress.CompanyName,
                AddressLine1 = order.ShippingAddress.AddressLine1,
                AddressLine2 = order.ShippingAddress.AddressLine2,
                City = order.ShippingAddress.City,
                StateOrProvince = order.ShippingAddress.StateOrProvince,
                PostalCode = order.ShippingAddress.PostalCode,
                CountryCode = order.ShippingAddress.CountryCode,
                PhoneNumber = order.ShippingAddress.PhoneNumber,
                DeliveryInstructions = order.ShippingAddress.DeliveryInstructions,
                IsAmended = order.ShippingAddress.IsAmended,
                OriginalAddressSnapshot = order.ShippingAddress.OriginalAddressSnapshot,
                AmendmentReason = order.ShippingAddress.AmendmentReason,
                AmendedAtUtc = order.ShippingAddress.AmendedAtUtc
            },
            Lines = order.Lines.Select(l => new OutboundOrderLineDto
            {
                Id = l.Id,
                OutboundOrderId = l.OutboundOrderId,
                ItemId = l.ItemId,
                Sku = l.Item?.Sku ?? string.Empty,
                ItemDescription = l.Item?.Description ?? string.Empty,
                PackagingUnitId = l.PackagingUnitId,
                LineNumber = l.LineNumber,
                QuantityOrdered = l.QuantityOrdered,
                QuantityAllocated = l.QuantityAllocated,
                QuantityShipped = l.QuantityShipped,
                RemainingDemand = l.RemainingDemand,
                RequiredLotNumber = l.RequiredLotNumber,
                MinExpiryDateUtc = l.MinExpiryDateUtc,
                Status = l.Status,
                Notes = l.Notes
            }).ToList()
        }).ToList();

        return PagedResult<OutboundOrderDto>.Create(dtos, totalCount, pageNumber, pageSize);
    }
}

