using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.OrderManagement.Commands.ReleaseOutboundOrder;

public record ReleaseOutboundOrderCommand : IRequest<OutboundOrderDto>
{
    public Guid OrderId { get; init; }
    public string? Notes { get; init; }
}

public class ReleaseOutboundOrderCommandValidator : AbstractValidator<ReleaseOutboundOrderCommand>
{
    public ReleaseOutboundOrderCommandValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty().WithMessage("Order ID is required.");
    }
}

public class ReleaseOutboundOrderCommandHandler : IRequestHandler<ReleaseOutboundOrderCommand, OutboundOrderDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly ICurrentUserContext _currentUser;

    public ReleaseOutboundOrderCommandHandler(
        IApplicationDbContext context,
        ITenantProvider tenantProvider,
        ICurrentUserContext currentUser)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _currentUser = currentUser;
    }

    public async Task<OutboundOrderDto> Handle(ReleaseOutboundOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _context.OutboundOrders
            .Include(o => o.ShippingAddress)
            .Include(o => o.Customer)
            .Include(o => o.Facility)
            .Include(o => o.DestinationFacility)
            .Include(o => o.Lines)
                .ThenInclude(l => l.Item)
            .Include(o => o.StatusHistories)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken);

        if (order == null)
        {
            throw new EntityNotFoundException(nameof(OutboundOrder), request.OrderId);
        }

        var operatorId = _currentUser.UserId ?? Guid.Empty;
        order.ReleaseHold(operatorId, request.Notes?.Trim());

        await _context.SaveChangesAsync(cancellationToken);

        return new OutboundOrderDto
        {
            Id = order.Id,
            ClientId = order.ClientId,
            FacilityId = order.FacilityId,
            FacilityName = order.Facility.Name,
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

