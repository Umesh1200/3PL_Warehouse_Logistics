using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.Application.Features.Outbound.OrderManagement.Commands.CreateOutboundOrder;

public record CreateOutboundOrderCommand : IRequest<OutboundOrderDto>
{
    public Guid FacilityId { get; init; }
    public Guid? DestinationFacilityId { get; init; }
    public Guid? CustomerId { get; init; }

    public string? OrderNumber { get; init; }
    public string? ExternalOrderReference { get; init; }

    public OutboundOrderType OrderType { get; init; } = OutboundOrderType.B2BSalesOrder;
    public int Priority { get; init; } = 1;

    public CarrierServiceLevel ServiceLevel { get; init; } = CarrierServiceLevel.StandardGround;
    public string? CarrierCode { get; init; }

    public DateTime? RequiredShipDateUtc { get; init; }
    public DateTime? EarliestShipDateUtc { get; init; }
    public string? Notes { get; init; }

    public CreateShippingAddressDto ShippingAddress { get; init; } = null!;
    public List<CreateOutboundOrderLineDto> Lines { get; init; } = [];
}

public record CreateShippingAddressDto
{
    public string RecipientName { get; init; } = string.Empty;
    public string? CompanyName { get; init; }
    public string AddressLine1 { get; init; } = string.Empty;
    public string? AddressLine2 { get; init; }
    public string City { get; init; } = string.Empty;
    public string StateOrProvince { get; init; } = string.Empty;
    public string PostalCode { get; init; } = string.Empty;
    public string CountryCode { get; init; } = "USA";
    public string? PhoneNumber { get; init; }
    public string? DeliveryInstructions { get; init; }
}

public record CreateOutboundOrderLineDto
{
    public Guid ItemId { get; init; }
    public decimal QuantityOrdered { get; init; }
    public Guid? PackagingUnitId { get; init; }
    public string? RequiredLotNumber { get; init; }
    public DateTime? MinExpiryDateUtc { get; init; }
    public string? Notes { get; init; }
}

public class CreateOutboundOrderCommandValidator : AbstractValidator<CreateOutboundOrderCommand>
{
    public CreateOutboundOrderCommandValidator()
    {
        RuleFor(x => x.FacilityId)
            .NotEmpty().WithMessage("Facility ID is required.");

        RuleFor(x => x.Lines)
            .NotEmpty().WithMessage("Order must contain at least one line item.");

        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ItemId)
                .NotEmpty().WithMessage("Line item ItemId is required.");

            line.RuleFor(l => l.QuantityOrdered)
                .GreaterThan(0).WithMessage("Quantity ordered must be greater than zero.");
        });

        RuleFor(x => x.ShippingAddress)
            .NotNull().WithMessage("Shipping address is required.");

        When(x => x.ShippingAddress != null, () =>
        {
            RuleFor(x => x.ShippingAddress.RecipientName).NotEmpty().WithMessage("Recipient name is required.");
            RuleFor(x => x.ShippingAddress.AddressLine1).NotEmpty().WithMessage("Address line 1 is required.");
            RuleFor(x => x.ShippingAddress.City).NotEmpty().WithMessage("City is required.");
            RuleFor(x => x.ShippingAddress.StateOrProvince).NotEmpty().WithMessage("State or province is required.");
            RuleFor(x => x.ShippingAddress.PostalCode).NotEmpty().WithMessage("Postal code is required.");
            RuleFor(x => x.ShippingAddress.CountryCode).NotEmpty().WithMessage("Country code is required.");
        });

        When(x => x.OrderType == OutboundOrderType.StockTransferOrder, () =>
        {
            RuleFor(x => x.DestinationFacilityId)
                .NotEmpty().WithMessage("Destination facility ID is required for stock transfer orders.");

            RuleFor(x => x.DestinationFacilityId)
                .NotEqual(x => x.FacilityId).WithMessage("Source and destination facilities must be different for a stock transfer order.");
        });
    }
}

public class CreateOutboundOrderCommandHandler : IRequestHandler<CreateOutboundOrderCommand, OutboundOrderDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly ICurrentUserContext _currentUser;

    public CreateOutboundOrderCommandHandler(
        IApplicationDbContext context,
        ITenantProvider tenantProvider,
        ICurrentUserContext currentUser)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _currentUser = currentUser;
    }

    public async Task<OutboundOrderDto> Handle(CreateOutboundOrderCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantProvider.TenantId;
        if (!tenantId.HasValue && !_currentUser.IsStaff)
        {
            throw new UnauthorizedException("Tenant context is required to create an outbound order.");
        }

        var clientId = tenantId ?? _currentUser.TenantId ?? throw new UnauthorizedException("Client identifier could not be determined.");

        // 1. Validate Facility Access
        var sourceFacility = await _context.Facilities.FirstOrDefaultAsync(f => f.Id == request.FacilityId, cancellationToken);
        if (sourceFacility == null)
        {
            throw new EntityNotFoundException(nameof(Domain.Entities.Facilities.Facility), request.FacilityId);
        }

        if (!_currentUser.IsStaff)
        {
            var hasAccess = await _context.ClientFacilityAccesses.AnyAsync(
                a => a.ClientId == clientId && a.FacilityId == request.FacilityId,
                cancellationToken);

            if (!hasAccess)
            {
                throw new ForbiddenException($"Client '{clientId}' does not have authorized access to facility '{sourceFacility.FacilityCode}'.");
            }
        }

        // 2. Validate Stock Transfer Destination Facility
        Domain.Entities.Facilities.Facility? destFacility = null;
        if (request.OrderType == OutboundOrderType.StockTransferOrder)
        {
            if (!request.DestinationFacilityId.HasValue || request.DestinationFacilityId.Value == request.FacilityId)
            {
                throw new ValidationException(nameof(request.DestinationFacilityId), "Stock transfer source and destination facilities must be different.");
            }

            destFacility = await _context.Facilities.FirstOrDefaultAsync(f => f.Id == request.DestinationFacilityId.Value, cancellationToken);
            if (destFacility == null)
            {
                throw new EntityNotFoundException(nameof(Domain.Entities.Facilities.Facility), request.DestinationFacilityId.Value);
            }

            if (!_currentUser.IsStaff)
            {
                var hasDestAccess = await _context.ClientFacilityAccesses.AnyAsync(
                    a => a.ClientId == clientId && a.FacilityId == request.DestinationFacilityId.Value,
                    cancellationToken);

                if (!hasDestAccess)
                {
                    throw new ForbiddenException($"Client '{clientId}' does not have authorized access to destination facility '{destFacility.FacilityCode}'.");
                }
            }
        }

        // 3. Validate Customer Ownership
        Customer? customer = null;
        if (request.CustomerId.HasValue)
        {
            customer = await _context.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId.Value && c.ClientId == clientId, cancellationToken);
            if (customer == null)
            {
                throw new ValidationException(nameof(request.CustomerId), $"Customer '{request.CustomerId}' does not exist or belong to the client.");
            }
        }

        // 4. Validate All Line SKUs Belong to Client
        var requestedItemIds = request.Lines.Select(l => l.ItemId).Distinct().ToList();
        var clientItems = await _context.Items
            .Where(i => requestedItemIds.Contains(i.Id) && i.ClientId == clientId)
            .ToDictionaryAsync(i => i.Id, cancellationToken);

        foreach (var requestedId in requestedItemIds)
        {
            if (!clientItems.ContainsKey(requestedId))
            {
                throw new ValidationException(nameof(request.Lines), $"Item with ID '{requestedId}' does not belong to client '{clientId}'. Cross-tenant item injection rejected.");
            }
        }

        // 5. Validate Duplicate External Reference
        if (!string.IsNullOrWhiteSpace(request.ExternalOrderReference))
        {
            var duplicateRef = await _context.OutboundOrders.AnyAsync(
                o => o.ClientId == clientId && o.ExternalOrderReference == request.ExternalOrderReference.Trim(),
                cancellationToken);

            if (duplicateRef)
            {
                throw new ValidationException(nameof(request.ExternalOrderReference), $"An order with external reference '{request.ExternalOrderReference}' already exists for this client.");
            }
        }

        // 6. Generate OrderNumber if not specified
        var orderNumber = request.OrderNumber?.Trim();
        if (string.IsNullOrWhiteSpace(orderNumber))
        {
            var dateStamp = DateTime.UtcNow.ToString("yyyyMMdd");
            var countToday = await _context.OutboundOrders.CountAsync(o => o.ClientId == clientId && o.OrderNumber.StartsWith($"SO-{dateStamp}-"), cancellationToken);
            orderNumber = $"SO-{dateStamp}-{(countToday + 1):D4}";
        }
        else
        {
            var duplicateNum = await _context.OutboundOrders.AnyAsync(
                o => o.ClientId == clientId && o.OrderNumber == orderNumber,
                cancellationToken);

            if (duplicateNum)
            {
                throw new ValidationException(nameof(request.OrderNumber), $"An order with order number '{orderNumber}' already exists for this client.");
            }
        }

        // 7. Construct OutboundOrder and Children
        var order = new OutboundOrder
        {
            ClientId = clientId,
            FacilityId = request.FacilityId,
            DestinationFacilityId = request.DestinationFacilityId,
            CustomerId = request.CustomerId,
            OrderNumber = orderNumber,
            ExternalOrderReference = request.ExternalOrderReference?.Trim(),
            OrderType = request.OrderType,
            Status = OutboundOrderStatus.Released,
            Priority = request.Priority,
            ServiceLevel = request.ServiceLevel,
            CarrierCode = request.CarrierCode,
            CustomerName = customer?.CustomerName ?? request.ShippingAddress.CompanyName ?? request.ShippingAddress.RecipientName,
            RequiredShipDateUtc = request.RequiredShipDateUtc,
            EarliestShipDateUtc = request.EarliestShipDateUtc,
            Notes = request.Notes
        };

        var shippingAddress = new ShippingAddress
        {
            OutboundOrderId = order.Id,
            RecipientName = request.ShippingAddress.RecipientName.Trim(),
            CompanyName = request.ShippingAddress.CompanyName?.Trim(),
            AddressLine1 = request.ShippingAddress.AddressLine1.Trim(),
            AddressLine2 = request.ShippingAddress.AddressLine2?.Trim(),
            City = request.ShippingAddress.City.Trim(),
            StateOrProvince = request.ShippingAddress.StateOrProvince.Trim(),
            PostalCode = request.ShippingAddress.PostalCode.Trim(),
            CountryCode = request.ShippingAddress.CountryCode.Trim(),
            PhoneNumber = request.ShippingAddress.PhoneNumber?.Trim(),
            DeliveryInstructions = request.ShippingAddress.DeliveryInstructions?.Trim()
        };
        order.SetShippingAddress(shippingAddress);

        int lineNum = 1;
        foreach (var lineReq in request.Lines)
        {
            var orderLine = new OutboundOrderLine
            {
                OutboundOrderId = order.Id,
                ItemId = lineReq.ItemId,
                PackagingUnitId = lineReq.PackagingUnitId,
                LineNumber = lineNum++,
                QuantityOrdered = lineReq.QuantityOrdered,
                QuantityAllocated = 0m,
                QuantityShipped = 0m,
                RequiredLotNumber = lineReq.RequiredLotNumber?.Trim(),
                MinExpiryDateUtc = lineReq.MinExpiryDateUtc,
                Status = OutboundOrderLineStatus.Pending,
                Notes = lineReq.Notes
            };
            order.Lines.Add(orderLine);
        }

        // Record initial creation in OrderStatusHistory
        var operatorId = _currentUser.UserId ?? Guid.Empty;
        order.StatusHistories.Add(new OrderStatusHistory
        {
            OutboundOrderId = order.Id,
            FromStatus = OutboundOrderStatus.Draft,
            ToStatus = OutboundOrderStatus.Released,
            Reason = "Initial order registration and release",
            ChangedByUserId = operatorId,
            ChangedAtUtc = DateTime.UtcNow
        });

        _context.OutboundOrders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);

        return new OutboundOrderDto
        {
            Id = order.Id,
            ClientId = order.ClientId,
            FacilityId = order.FacilityId,
            FacilityName = sourceFacility.Name,
            DestinationFacilityId = order.DestinationFacilityId,
            DestinationFacilityName = destFacility?.Name,
            CustomerId = order.CustomerId,
            CustomerCode = customer?.CustomerCode,
            CustomerName = order.CustomerName,
            OrderNumber = order.OrderNumber,
            ExternalOrderReference = order.ExternalOrderReference,
            OrderType = order.OrderType,
            Status = order.Status,
            Priority = order.Priority,
            ServiceLevel = order.ServiceLevel,
            CarrierCode = order.CarrierCode,
            DestinationAddress = order.DestinationAddress,
            OrderDateUtc = order.OrderDateUtc,
            RequiredShipDateUtc = order.RequiredShipDateUtc,
            EarliestShipDateUtc = order.EarliestShipDateUtc,
            Notes = order.Notes,
            Version = order.Version,
            CreatedAt = order.CreatedAt,
            ShippingAddress = new ShippingAddressDto
            {
                RecipientName = shippingAddress.RecipientName,
                CompanyName = shippingAddress.CompanyName,
                AddressLine1 = shippingAddress.AddressLine1,
                AddressLine2 = shippingAddress.AddressLine2,
                City = shippingAddress.City,
                StateOrProvince = shippingAddress.StateOrProvince,
                PostalCode = shippingAddress.PostalCode,
                CountryCode = shippingAddress.CountryCode,
                PhoneNumber = shippingAddress.PhoneNumber,
                DeliveryInstructions = shippingAddress.DeliveryInstructions
            },
            Lines = order.Lines.Select(l => new OutboundOrderLineDto
            {
                Id = l.Id,
                OutboundOrderId = l.OutboundOrderId,
                ItemId = l.ItemId,
                Sku = clientItems[l.ItemId].Sku,
                ItemDescription = clientItems[l.ItemId].Description,
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
                ChangedByUserId = h.ChangedByUserId,
                ChangedAtUtc = h.ChangedAtUtc
            }).ToList()
        };
    }
}

