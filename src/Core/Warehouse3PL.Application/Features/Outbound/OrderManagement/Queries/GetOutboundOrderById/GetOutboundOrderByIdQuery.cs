using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.OrderManagement.Queries.GetOutboundOrderById;

public record GetOutboundOrderByIdQuery(Guid Id) : IRequest<OutboundOrderDto>;

public class GetOutboundOrderByIdQueryHandler : IRequestHandler<GetOutboundOrderByIdQuery, OutboundOrderDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly ICurrentUserContext _currentUser;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public GetOutboundOrderByIdQueryHandler(
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

    public async Task<OutboundOrderDto> Handle(GetOutboundOrderByIdQuery request, CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureIdentityAllowed(allowClient: true, nameof(GetOutboundOrderByIdQuery));

        var order = await _context.OutboundOrders
            .Include(o => o.ShippingAddress)
            .Include(o => o.Customer)
            .Include(o => o.Facility)
            .Include(o => o.DestinationFacility)
            .Include(o => o.Lines)
                .ThenInclude(l => l.Item)
            .Include(o => o.StatusHistories)
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == request.Id, cancellationToken);

        if (order == null)
        {
            throw new EntityNotFoundException(nameof(OutboundOrder), request.Id);
        }

        await _outboundAuth.ValidateEntityReadOrClientMutationAsync(
            nameof(OutboundOrder),
            order.Id,
            order.ClientId,
            order.FacilityId,
            allowClient: true,
            isMutation: false,
            cancellationToken);

        return new OutboundOrderDto
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
            }).ToList(),
            StatusHistories = order.StatusHistories.Select(h => new OrderStatusHistoryDto
            {
                FromStatus = h.FromStatus,
                ToStatus = h.ToStatus,
                Reason = h.Reason,
                Notes = h.Notes,
                ChangedByUserId = h.ChangedByUserId,
                ChangedAtUtc = h.ChangedAtUtc
            }).ToList()
        };
    }
}

