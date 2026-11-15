using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Locations.Commands.BatchGenerateLocations;

public record BatchGenerateLocationsCommand : IRequest<BatchGenerateLocationsResult>
{
    public Guid FacilityId { get; init; }
    public Guid ZoneId { get; init; }
    public string AislePrefix { get; init; } = "A";
    public int AisleStart { get; init; } = 1;
    public int AisleCount { get; init; } = 2;
    public int RacksPerAisle { get; init; } = 4;
    public int ShelvesPerRack { get; init; } = 3;
    public int BinsPerShelf { get; init; } = 2;
    
    // Default bin dimensions
    public decimal DefaultLengthCm { get; init; } = 120m;
    public decimal DefaultWidthCm { get; init; } = 80m;
    public decimal DefaultHeightCm { get; init; } = 150m;
    public decimal DefaultMaxWeightKg { get; init; } = 1000m;
}

public record BatchGenerateLocationsResult(int TotalCreated, int AislesCreated, int RacksCreated, int BinsCreated);

public class BatchGenerateLocationsCommandValidator : AbstractValidator<BatchGenerateLocationsCommand>
{
    public BatchGenerateLocationsCommandValidator()
    {
        RuleFor(x => x.FacilityId).NotEmpty();
        RuleFor(x => x.ZoneId).NotEmpty();
        RuleFor(x => x.AisleCount).InclusiveBetween(1, 50);
        RuleFor(x => x.RacksPerAisle).InclusiveBetween(1, 50);
        RuleFor(x => x.ShelvesPerRack).InclusiveBetween(1, 20);
        RuleFor(x => x.BinsPerShelf).InclusiveBetween(1, 20);
    }
}

public class BatchGenerateLocationsCommandHandler : IRequestHandler<BatchGenerateLocationsCommand, BatchGenerateLocationsResult>
{
    private readonly IApplicationDbContext _context;

    public BatchGenerateLocationsCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<BatchGenerateLocationsResult> Handle(BatchGenerateLocationsCommand request, CancellationToken cancellationToken)
    {
        var zone = await _context.WarehouseZones
            .Include(z => z.Facility)
            .FirstOrDefaultAsync(z => z.Id == request.ZoneId && z.FacilityId == request.FacilityId, cancellationToken);

        if (zone == null)
        {
            throw new EntityNotFoundException(nameof(WarehouseZone), request.ZoneId);
        }

        var newLocations = new List<Location>();
        var aislesCreated = 0;
        var racksCreated = 0;
        var binsCreated = 0;
        var pickSequenceCounter = 1;

        var maxVol = request.DefaultLengthCm * request.DefaultWidthCm * request.DefaultHeightCm;

        for (int a = request.AisleStart; a < request.AisleStart + request.AisleCount; a++)
        {
            var aisleCode = $"{request.AislePrefix}{a:D2}";
            var aisleLoc = new Location
            {
                FacilityId = request.FacilityId,
                ZoneId = request.ZoneId,
                LocationType = LocationType.Aisle,
                LocationCode = $"{zone.ZoneCode}-{aisleCode}",
                Barcode = $"LOC-{zone.ZoneCode}-{aisleCode}",
                AisleCode = aisleCode,
                IsPickable = false
            };
            newLocations.Add(aisleLoc);
            aislesCreated++;

            for (int r = 1; r <= request.RacksPerAisle; r++)
            {
                var rackCode = $"R{r:D2}";
                var rackLoc = new Location
                {
                    FacilityId = request.FacilityId,
                    ZoneId = request.ZoneId,
                    ParentLocationId = aisleLoc.Id,
                    LocationType = LocationType.Rack,
                    LocationCode = $"{aisleLoc.LocationCode}-{rackCode}",
                    Barcode = $"LOC-{zone.ZoneCode}-{aisleCode}-{rackCode}",
                    AisleCode = aisleCode,
                    RackCode = rackCode,
                    IsPickable = false
                };
                newLocations.Add(rackLoc);
                racksCreated++;

                for (int s = 1; s <= request.ShelvesPerRack; s++)
                {
                    for (int b = 1; b <= request.BinsPerShelf; b++)
                    {
                        var binCode = $"{zone.ZoneCode}-{aisleCode}-{rackCode}-S{s:D2}-B{b:D2}";
                        var binLoc = new Location
                        {
                            FacilityId = request.FacilityId,
                            ZoneId = request.ZoneId,
                            ParentLocationId = rackLoc.Id,
                            LocationType = LocationType.Bin,
                            LocationCode = binCode,
                            Barcode = $"LOC-{binCode}",
                            AisleCode = aisleCode,
                            RackCode = rackCode,
                            ShelfLevel = s,
                            BinNumber = b,
                            PickSequence = pickSequenceCounter++,
                            LengthCm = request.DefaultLengthCm,
                            WidthCm = request.DefaultWidthCm,
                            HeightCm = request.DefaultHeightCm,
                            MaxVolumeCm3 = maxVol,
                            MaxWeightKg = request.DefaultMaxWeightKg,
                            IsPickable = true,
                            IsReceiving = false
                        };
                        newLocations.Add(binLoc);
                        binsCreated++;
                    }
                }
            }
        }

        await _context.ExecuteInTransactionAsync(async () =>
        {
            _context.Locations.AddRange(newLocations);
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);

        return new BatchGenerateLocationsResult(newLocations.Count, aislesCreated, racksCreated, binsCreated);
    }
}

