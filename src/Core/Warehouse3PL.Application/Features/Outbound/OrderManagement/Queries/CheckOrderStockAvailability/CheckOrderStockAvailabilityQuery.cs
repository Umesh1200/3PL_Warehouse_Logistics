using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.OrderManagement.Queries.CheckOrderStockAvailability;

public record CheckOrderStockAvailabilityQuery(Guid OrderId) : IRequest<StockAvailabilityAssessmentDto>;

public class CheckOrderStockAvailabilityQueryHandler : IRequestHandler<CheckOrderStockAvailabilityQuery, StockAvailabilityAssessmentDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly ICurrentUserContext _currentUser;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public CheckOrderStockAvailabilityQueryHandler(
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

    public async Task<StockAvailabilityAssessmentDto> Handle(CheckOrderStockAvailabilityQuery request, CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureIdentityAllowed(allowClient: true, nameof(CheckOrderStockAvailabilityQuery));

        var order = await _context.OutboundOrders
            .Include(o => o.Lines)
                .ThenInclude(l => l.Item)
            .AsNoTracking()
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
            isMutation: false,
            cancellationToken);

        var itemIds = order.Lines.Select(l => l.ItemId).Distinct().ToList();

        // Load sound, pickable, unquarantined, available on-hand stock for requested items at this facility (Read-Only)
        var stockRecords = await _context.StockOnHand
            .Include(s => s.Location)
            .Include(s => s.Lot)
            .AsNoTracking()
            .Where(s => s.ClientId == order.ClientId &&
                        s.FacilityId == order.FacilityId &&
                        itemIds.Contains(s.ItemId) &&
                        s.QtyAvailable > 0 &&
                        s.Location.IsActive &&
                        !s.Location.IsDamaged &&
                        !s.Location.IsLocked &&
                        s.Location.IsPickable)
            .ToListAsync(cancellationToken);

        var lineAssessments = new List<LineStockAvailabilityDto>();
        decimal totalRequested = 0;
        decimal totalAvailableFulfillable = 0;

        foreach (var line in order.Lines.OrderBy(l => l.LineNumber))
        {
            var eligibleStocks = stockRecords.Where(s => s.ItemId == line.ItemId);

            // Filter by Lot QA status & expiration
            eligibleStocks = eligibleStocks.Where(s =>
                s.Lot == null || (s.Lot.QaStatus == LotQaStatus.Released && !s.Lot.IsExpired));

            // Lot number constraint
            if (!string.IsNullOrWhiteSpace(line.RequiredLotNumber))
            {
                eligibleStocks = eligibleStocks.Where(s =>
                    s.Lot != null && s.Lot.LotNumber.Equals(line.RequiredLotNumber.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            // Expiry date constraint
            if (line.MinExpiryDateUtc.HasValue)
            {
                eligibleStocks = eligibleStocks.Where(s =>
                    s.Lot != null && s.Lot.ExpirationDate.HasValue && s.Lot.ExpirationDate.Value >= line.MinExpiryDateUtc.Value);
            }

            var availableQty = eligibleStocks.Sum(s => s.QtyAvailable);
            var shortage = Math.Max(0, line.QuantityOrdered - availableQty);
            var isLineFullyAvailable = availableQty >= line.QuantityOrdered;

            totalRequested += line.QuantityOrdered;
            totalAvailableFulfillable += Math.Min(availableQty, line.QuantityOrdered);

            lineAssessments.Add(new LineStockAvailabilityDto
            {
                LineId = line.Id,
                LineNumber = line.LineNumber,
                ItemId = line.ItemId,
                Sku = line.Item?.Sku ?? string.Empty,
                ItemDescription = line.Item?.Description ?? string.Empty,
                QuantityRequested = line.QuantityOrdered,
                QuantityAvailable = availableQty,
                ShortageQuantity = shortage,
                IsFullyAvailable = isLineFullyAvailable,
                RequiredLotNumber = line.RequiredLotNumber,
                MinExpiryDateUtc = line.MinExpiryDateUtc
            });
        }

        var overallFulfillmentPercent = totalRequested > 0
            ? Math.Round((totalAvailableFulfillable / totalRequested) * 100m, 2)
            : 100m;

        var isOrderFullyAvailable = lineAssessments.All(l => l.IsFullyAvailable);

        return new StockAvailabilityAssessmentDto
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            IsFullyAvailable = isOrderFullyAvailable,
            OverallFulfillmentPercent = overallFulfillmentPercent,
            Lines = lineAssessments
        };
    }
}

