using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.CycleCounts.Commands.CreateCycleCount;

public record CreateCycleCountCommand : IRequest<CycleCountDto>
{
    public Guid FacilityId { get; init; }
    public Guid? ZoneId { get; init; }
    public CycleCountType CountType { get; init; } = CycleCountType.AbcVelocity;
    public DateTime? ScheduledDate { get; init; }
    public string? Notes { get; init; }
}

public class CreateCycleCountCommandValidator : AbstractValidator<CreateCycleCountCommand>
{
    public CreateCycleCountCommandValidator()
    {
        RuleFor(x => x.FacilityId).NotEmpty();
        RuleFor(x => x.CountType).IsInEnum();
    }
}

public class CreateCycleCountCommandHandler : IRequestHandler<CreateCycleCountCommand, CycleCountDto>
{
    private readonly IApplicationDbContext _context;

    public CreateCycleCountCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<CycleCountDto> Handle(CreateCycleCountCommand request, CancellationToken cancellationToken)
    {
        var facility = await _context.Facilities.FirstOrDefaultAsync(f => f.Id == request.FacilityId, cancellationToken);
        if (facility == null)
        {
            throw new EntityNotFoundException(nameof(Facility), request.FacilityId);
        }

        // Query active stock records to populate count items
        var stockQuery = _context.StockOnHand
            .AsNoTracking()
            .Include(s => s.Item)
            .Include(s => s.Location)
            .Include(s => s.Lot)
            .Where(s => s.FacilityId == request.FacilityId &&
                        (s.QtyAvailable + s.QtyAllocated + s.QtyReserved + s.QtyQuarantined + s.QtyDamaged + s.QtyOnHold) > 0);

        if (request.ZoneId.HasValue)
        {
            stockQuery = stockQuery.Where(s => s.Location.ZoneId == request.ZoneId.Value);
        }

        if (request.CountType == CycleCountType.AbcVelocity)
        {
            // Focus on high velocity A-items
            stockQuery = stockQuery.Where(s => s.Item.AbcVelocity == AbcVelocity.A);
        }

        var targetStock = await stockQuery.Take(100).ToListAsync(cancellationToken);

        var countNumber = $"CC-{facility.FacilityCode}-{DateTime.UtcNow:yyyyMMddHHmmss}";
        var cycleCount = new CycleCount
        {
            FacilityId = request.FacilityId,
            ZoneId = request.ZoneId,
            CountNumber = countNumber,
            CountType = request.CountType,
            Status = CycleCountStatus.InProgress,
            ScheduledDate = request.ScheduledDate ?? DateTime.UtcNow,
            StartedAt = DateTime.UtcNow,
            Notes = request.Notes
        };

        foreach (var s in targetStock)
        {
            cycleCount.Items.Add(new CycleCountItem
            {
                LocationId = s.LocationId,
                ItemId = s.ItemId,
                LotId = s.LotId,
                ExpectedQty = s.TotalQuantity,
                CountedQty = null,
                IsReconciled = false
            });
        }

        _context.CycleCounts.Add(cycleCount);
        await _context.SaveChangesAsync(cancellationToken);

        var stockLookup = targetStock.ToDictionary(s => (s.LocationId, s.ItemId, s.LotId));

        return new CycleCountDto
        {
            Id = cycleCount.Id,
            FacilityId = cycleCount.FacilityId,
            ZoneId = cycleCount.ZoneId,
            CountNumber = cycleCount.CountNumber,
            CountType = cycleCount.CountType,
            Status = cycleCount.Status,
            ScheduledDate = cycleCount.ScheduledDate,
            StartedAt = cycleCount.StartedAt,
            Notes = cycleCount.Notes,
            TotalItemsCount = cycleCount.Items.Count,
            DiscrepancyCount = 0,
            Items = cycleCount.Items.Select(ci =>
            {
                stockLookup.TryGetValue((ci.LocationId, ci.ItemId, ci.LotId), out var st);
                return new CycleCountItemDto
                {
                    Id = ci.Id,
                    LocationId = ci.LocationId,
                    LocationCode = st?.Location?.LocationCode ?? string.Empty,
                    ItemId = ci.ItemId,
                    Sku = st?.Item?.Sku ?? string.Empty,
                    LotId = ci.LotId,
                    LotNumber = st?.Lot?.LotNumber,
                    ExpectedQty = ci.ExpectedQty,
                    CountedQty = ci.CountedQty,
                    VarianceQty = ci.VarianceQty,
                    IsReconciled = ci.IsReconciled,
                    CountedAt = ci.CountedAt
                };
            }).ToList()
        };
    }
}
