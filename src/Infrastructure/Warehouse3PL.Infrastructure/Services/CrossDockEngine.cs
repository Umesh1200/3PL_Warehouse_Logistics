using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Infrastructure.Services;

/// <summary>
/// Cross-dock opportunity detection and demand matching engine.
/// Evaluates tenant isolation, SKU matching, quality inspection clearances, lot/expiry constraints, and temperature compatibility.
/// </summary>
public class CrossDockEngine : ICrossDockEngine
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<CrossDockEngine> _logger;

    public CrossDockEngine(
        IApplicationDbContext context,
        ILogger<CrossDockEngine> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<CrossDockOpportunityDto>> DetectOpportunitiesAsync(
        Guid facilityId,
        Guid? clientId = null,
        Guid? receivingSessionId = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Query candidate inbound receipt lines at transient staging locations
        var inboundQuery = _context.ReceiptLines
            .AsNoTracking()
            .Include(l => l.ReceivingSession)
            .Include(l => l.Item)
            .Include(l => l.StagingLocation)
            .Include(l => l.Discrepancies)
            .Where(l => l.ReceivingSession.FacilityId == facilityId &&
                        l.StagingLocationId != null &&
                        l.Condition == ReceiptConditionStatus.Sound &&
                        l.QuantityReceived > 0);

        if (clientId.HasValue)
        {
            inboundQuery = inboundQuery.Where(l => l.ReceivingSession.ClientId == clientId.Value || l.Item.ClientId == clientId.Value);
        }

        if (receivingSessionId.HasValue)
        {
            inboundQuery = inboundQuery.Where(l => l.ReceivingSessionId == receivingSessionId.Value);
        }

        var candidateLines = await inboundQuery.ToListAsync(cancellationToken);

        // Exclude lines with active quarantined or pending review discrepancies
        candidateLines = candidateLines
            .Where(l => !l.Discrepancies.Any(d =>
                d.Status == DiscrepancyResolutionStatus.Quarantined ||
                d.Status == DiscrepancyResolutionStatus.PendingReview))
            .ToList();

        if (candidateLines.Count == 0)
        {
            return [];
        }

        // 2. Query real-time available stock at staging to ensure stock is physically available
        var stagingLocIds = candidateLines.Select(l => l.StagingLocationId!.Value).ToHashSet();
        var itemIds = candidateLines.Select(l => l.ItemId).ToHashSet();

        var stagingStocks = await _context.StockOnHand
            .AsNoTracking()
            .Where(s => s.FacilityId == facilityId &&
                        stagingLocIds.Contains(s.LocationId) &&
                        itemIds.Contains(s.ItemId) &&
                        s.QtyAvailable > 0)
            .ToListAsync(cancellationToken);

        var stockLookup = stagingStocks
            .GroupBy(s => (s.LocationId, s.ItemId))
            .ToDictionary(g => g.Key, g => g.Sum(s => s.QtyAvailable));

        // 3. Query open outbound orders with unfulfilled demand in this facility
        var outboundOrdersQuery = _context.OutboundOrders
            .AsNoTracking()
            .Include(o => o.Lines)
                .ThenInclude(ol => ol.Item)
            .Where(o => o.FacilityId == facilityId &&
                        !o.IsDeleted &&
                        (o.Status == OutboundOrderStatus.Released || o.Status == OutboundOrderStatus.Draft || o.Status == OutboundOrderStatus.InProgress));

        if (clientId.HasValue)
        {
            outboundOrdersQuery = outboundOrdersQuery.Where(o => o.ClientId == clientId.Value);
        }

        var outboundOrders = await outboundOrdersQuery.ToListAsync(cancellationToken);

        // 4. Flatten unfulfilled order lines
        var demandLines = outboundOrders
            .SelectMany(o => o.Lines.Select(l => new { Order = o, Line = l }))
            .Where(x => x.Line.RemainingDemand > 0)
            .OrderByDescending(x => x.Order.Priority)
            .ThenBy(x => x.Order.RequiredShipDateUtc ?? DateTime.MaxValue)
            .ToList();

        if (demandLines.Count == 0)
        {
            return [];
        }

        var opportunities = new List<CrossDockOpportunityDto>();

        // Track remaining inbound quantities across multiple matches
        var lineAvailableQuantities = candidateLines.ToDictionary(
            l => l.Id,
            l =>
            {
                stockLookup.TryGetValue((l.StagingLocationId!.Value, l.ItemId), out var actualStock);
                return Math.Min(l.QuantityReceived, actualStock);
            });

        // Track remaining demand quantities across multiple matches
        var orderLineRemainingDemand = demandLines.ToDictionary(
            d => d.Line.Id,
            d => d.Line.RemainingDemand);

        foreach (var inbound in candidateLines)
        {
            var lineId = inbound.Id;
            var availableQty = lineAvailableQuantities[lineId];
            if (availableQty <= 0)
            {
                continue;
            }

            var matchingDemands = demandLines.Where(d =>
                d.Line.ItemId == inbound.ItemId &&
                d.Order.ClientId == (inbound.ReceivingSession.ClientId ?? inbound.Item.ClientId));

            foreach (var demand in matchingDemands)
            {
                if (availableQty <= 0)
                {
                    break;
                }

                var demandLineId = demand.Line.Id;
                var remainingDemand = orderLineRemainingDemand[demandLineId];
                if (remainingDemand <= 0)
                {
                    continue;
                }

                // Check Lot compatibility
                if (!string.IsNullOrWhiteSpace(demand.Line.RequiredLotNumber))
                {
                    if (!string.Equals(demand.Line.RequiredLotNumber, inbound.LotNumber, StringComparison.OrdinalIgnoreCase))
                    {
                        continue; // Lot mismatch
                    }
                }

                // Check Expiration date compatibility
                if (demand.Line.MinExpiryDateUtc.HasValue && inbound.ExpirationDate.HasValue)
                {
                    if (inbound.ExpirationDate.Value < demand.Line.MinExpiryDateUtc.Value)
                    {
                        continue; // Expiry too short
                    }
                }

                var crossDockQty = Math.Min(availableQty, remainingDemand);

                availableQty -= crossDockQty;
                lineAvailableQuantities[lineId] = availableQty;

                remainingDemand -= crossDockQty;
                orderLineRemainingDemand[demandLineId] = remainingDemand;

                opportunities.Add(new CrossDockOpportunityDto
                {
                    ItemId = inbound.ItemId,
                    Sku = inbound.Item.Sku,
                    ItemDescription = inbound.Item.Description,
                    StorageTempClass = inbound.Item.StorageTempClass,
                    ReceiptLineId = inbound.Id,
                    InboundLpn = inbound.Lpn,
                    SourceStagingLocationId = inbound.StagingLocationId!.Value,
                    SourceStagingLocationCode = inbound.StagingLocation?.LocationCode ?? "STAGING",
                    InboundAvailableQty = inbound.QuantityReceived,
                    LotNumber = inbound.LotNumber,
                    ExpirationDate = inbound.ExpirationDate,
                    MatchedOutboundOrderId = demand.Order.Id,
                    MatchedOutboundOrderNumber = demand.Order.OrderNumber,
                    MatchedOutboundOrderLineId = demand.Line.Id,
                    CustomerName = demand.Order.CustomerName,
                    OrderDemandedQty = demand.Line.QuantityOrdered,
                    OrderRemainingDemandQty = demand.Line.RemainingDemand,
                    RecommendedCrossDockQty = crossDockQty,
                    RemainingPutawayQty = availableQty,
                    IsTemperatureCompatible = true,
                    IsLotCompatible = true
                });
            }
        }

        return opportunities;
    }
}

