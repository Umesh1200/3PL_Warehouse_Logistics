using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.OrderManagement.Commands.CancelOutboundOrder;

public record CancelOutboundOrderCommand : IRequest<OutboundOrderDto>
{
    public Guid OrderId { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public class CancelOutboundOrderCommandValidator : AbstractValidator<CancelOutboundOrderCommand>
{
    public CancelOutboundOrderCommandValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty().WithMessage("Order ID is required.");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A cancellation reason must be provided.")
            .MaximumLength(500).WithMessage("Reason cannot exceed 500 characters.");
    }
}

public class CancelOutboundOrderCommandHandler : IRequestHandler<CancelOutboundOrderCommand, OutboundOrderDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly ICurrentUserContext _currentUser;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public CancelOutboundOrderCommandHandler(
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

    public async Task<OutboundOrderDto> Handle(CancelOutboundOrderCommand request, CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureIdentityAllowed(allowClient: true, nameof(CancelOutboundOrderCommand));

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

        await _outboundAuth.ValidateEntityReadOrClientMutationAsync(
            nameof(OutboundOrder),
            order.Id,
            order.ClientId,
            order.FacilityId,
            allowClient: true,
            isMutation: true,
            cancellationToken);

        var operatorId = _currentUser.UserId ?? Guid.Empty;
        order.Cancel(request.Reason.Trim(), operatorId);

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
                CountryNormalizationStatus = order.ShippingAddress.CountryNormalizationStatus,
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

