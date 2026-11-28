using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.WarehouseMaster;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

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
    private readonly WarehouseMasterAuthorization _auth;

    public BatchGenerateLocationsCommandHandler(IApplicationDbContext context, WarehouseMasterAuthorization auth)
    {
        _context = context;
        _auth = auth;
    }

    public async Task<BatchGenerateLocationsResult> Handle(BatchGenerateLocationsCommand request, CancellationToken cancellationToken)
    {
        var (userId, facilityId, _) = await _auth.ResolveAuthorizedFacilityAsync(
            request.FacilityId,
            cancellationToken,
            Permissions.Facilities.LocationsManage,
            Permissions.Facilities.Manage);

        return await _context.ExecuteWithFacilityHierarchyLockAsync(facilityId, async () =>
        {
            var zone = await _context.WarehouseZones
                .AsNoTracking()
                .FirstOrDefaultAsync(z => z.Id == request.ZoneId && z.FacilityId == facilityId && !z.IsDeleted, cancellationToken);

            if (zone == null)
            {
                throw new EntityNotFoundException(nameof(WarehouseZone), request.ZoneId);
            }

            if (!zone.IsActive)
            {
                throw new ValidationException(nameof(request.ZoneId), $"Zone '{zone.ZoneCode}' is inactive and cannot accept generated locations.");
            }

            // Validate configured hierarchy rules for Aisle (L1) -> Rack (L2) -> Bin (L3)
            await LocationHierarchyRuleEvaluator.ValidatePlacementAsync(_context, facilityId, null, LocationType.Aisle, 1, cancellationToken, requestedIsStorable: false);
            await LocationHierarchyRuleEvaluator.ValidatePlacementAsync(_context, facilityId, LocationType.Aisle, LocationType.Rack, 2, cancellationToken, requestedIsStorable: false);
            await LocationHierarchyRuleEvaluator.ValidatePlacementAsync(_context, facilityId, LocationType.Rack, LocationType.Bin, 3, cancellationToken, requestedIsStorable: true);

            var newLocations = new List<Location>();
            var aislesCreated = 0;
            var racksCreated = 0;
            var binsCreated = 0;
            var pickSequenceCounter = 1;
            var maxVol = request.DefaultLengthCm * request.DefaultWidthCm * request.DefaultHeightCm;

            for (int a = request.AisleStart; a < request.AisleStart + request.AisleCount; a++)
            {
                var aisleCode = $"{request.AislePrefix}{a:D2}";
                var aisleFullCode = $"{zone.ZoneCode}-{aisleCode}";
                var aisleBarcode = $"LOC-{zone.ZoneCode}-{aisleCode}";
                var aisleId = Guid.NewGuid();
                var aislePath = Location.BuildHierarchyPath(null, aisleId);

                var aisleLoc = new Location
                {
                    Id = aisleId,
                    FacilityId = facilityId,
                    ZoneId = request.ZoneId,
                    LocationType = LocationType.Aisle,
                    LocationCode = aisleFullCode,
                    NormalizedLocationCode = Location.NormalizeCode(aisleFullCode),
                    LocationName = aisleFullCode,
                    Barcode = aisleBarcode,
                    NormalizedBarcode = Location.NormalizeCode(aisleBarcode),
                    HierarchyLevel = 1,
                    HierarchyPath = aislePath,
                    AisleCode = aisleCode,
                    Status = LocationStatus.Active,
                    IsActive = true,
                    IsStorable = false,
                    IsPickable = false,
                    ConfigRevision = 1,
                    CreatedBy = userId
                };
                newLocations.Add(aisleLoc);
                aislesCreated++;

                for (int r = 1; r <= request.RacksPerAisle; r++)
                {
                    var rackCode = $"R{r:D2}";
                    var rackFullCode = $"{aisleLoc.LocationCode}-{rackCode}";
                    var rackBarcode = $"LOC-{zone.ZoneCode}-{aisleCode}-{rackCode}";
                    var rackId = Guid.NewGuid();
                    var rackPath = Location.BuildHierarchyPath(aislePath, rackId);

                    var rackLoc = new Location
                    {
                        Id = rackId,
                        FacilityId = facilityId,
                        ZoneId = request.ZoneId,
                        ParentLocationId = aisleLoc.Id,
                        LocationType = LocationType.Rack,
                        LocationCode = rackFullCode,
                        NormalizedLocationCode = Location.NormalizeCode(rackFullCode),
                        LocationName = rackFullCode,
                        Barcode = rackBarcode,
                        NormalizedBarcode = Location.NormalizeCode(rackBarcode),
                        HierarchyLevel = 2,
                        HierarchyPath = rackPath,
                        AisleCode = aisleCode,
                        RackCode = rackCode,
                        Status = LocationStatus.Active,
                        IsActive = true,
                        IsStorable = false,
                        IsPickable = false,
                        ConfigRevision = 1,
                        CreatedBy = userId
                    };
                    newLocations.Add(rackLoc);
                    racksCreated++;

                    for (int s = 1; s <= request.ShelvesPerRack; s++)
                    {
                        for (int b = 1; b <= request.BinsPerShelf; b++)
                        {
                            var binCode = $"{zone.ZoneCode}-{aisleCode}-{rackCode}-S{s:D2}-B{b:D2}";
                            var binBarcode = $"LOC-{binCode}";
                            var binId = Guid.NewGuid();
                            var binPath = Location.BuildHierarchyPath(rackPath, binId);

                            var binLoc = new Location
                            {
                                Id = binId,
                                FacilityId = facilityId,
                                ZoneId = request.ZoneId,
                                ParentLocationId = rackLoc.Id,
                                LocationType = LocationType.Bin,
                                LocationCode = binCode,
                                NormalizedLocationCode = Location.NormalizeCode(binCode),
                                LocationName = binCode,
                                Barcode = binBarcode,
                                NormalizedBarcode = Location.NormalizeCode(binBarcode),
                                HierarchyLevel = 3,
                                HierarchyPath = binPath,
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
                                Status = LocationStatus.Active,
                                IsActive = true,
                                IsStorable = true,
                                IsPickable = true,
                                IsReceiving = false,
                                ConfigRevision = 1,
                                CreatedBy = userId
                            };
                            newLocations.Add(binLoc);
                            binsCreated++;
                        }
                    }
                }
            }

            // A03: Pre-check collisions across all existing facility codes/barcodes including soft-deleted rows
            var candidateCodes = newLocations.Select(l => l.NormalizedLocationCode).ToHashSet(StringComparer.Ordinal);
            var candidateBarcodes = newLocations.Select(l => l.NormalizedBarcode).ToHashSet(StringComparer.Ordinal);

            var existingCollision = await _context.Locations
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(l => l.FacilityId == facilityId &&
                            (candidateCodes.Contains(l.NormalizedLocationCode) || candidateBarcodes.Contains(l.NormalizedBarcode)))
                .Select(l => l.LocationCode)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingCollision != null)
            {
                throw new ValidationException(
                    "LocationCode",
                    $"Batch generation aborted: location code or barcode '{existingCollision}' already exists in this facility.");
            }

            _context.Locations.AddRange(newLocations);
            await _context.SaveChangesAsync(cancellationToken);

            return new BatchGenerateLocationsResult(newLocations.Count, aislesCreated, racksCreated, binsCreated);
        }, cancellationToken);
    }
}
