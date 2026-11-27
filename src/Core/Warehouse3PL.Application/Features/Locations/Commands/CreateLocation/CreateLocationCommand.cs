using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.WarehouseMaster;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.Application.Features.Locations.Commands.CreateLocation;

public record CreateLocationCommand : IRequest<LocationDto>
{
    public Guid FacilityId { get; init; }
    public Guid? ZoneId { get; init; }
    public Guid? ParentLocationId { get; init; }
    public LocationType LocationType { get; init; }
    public string LocationCode { get; init; } = string.Empty;
    public string? LocationName { get; init; }
    public string Barcode { get; init; } = string.Empty;

    public string? AisleCode { get; init; }
    public string? RackCode { get; init; }
    public int? ShelfLevel { get; init; }
    public int? BinNumber { get; init; }
    public int? PickSequence { get; init; }

    public decimal? LengthCm { get; init; }
    public decimal? WidthCm { get; init; }
    public decimal? HeightCm { get; init; }
    public decimal? MaxVolumeCm3 { get; init; }
    public decimal? MaxWeightKg { get; init; }
    public bool? IsStorable { get; init; }
    public bool IsPickable { get; init; } = true;
    public bool IsReceiving { get; init; } = false;
    public string? Remarks { get; init; }
}

public class CreateLocationCommandValidator : AbstractValidator<CreateLocationCommand>
{
    public CreateLocationCommandValidator()
    {
        RuleFor(x => x.FacilityId).NotEmpty().WithMessage("FacilityId is required.");
        RuleFor(x => x.LocationCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LocationName).MaximumLength(150).When(x => x.LocationName != null);
        RuleFor(x => x.Barcode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LocationType).IsInEnum();
        RuleFor(x => x.Remarks).MaximumLength(500).When(x => x.Remarks != null);
    }
}

public class CreateLocationCommandHandler : IRequestHandler<CreateLocationCommand, LocationDto>
{
    private readonly IApplicationDbContext _context;
    private readonly WarehouseMasterAuthorization _auth;

    public CreateLocationCommandHandler(IApplicationDbContext context, WarehouseMasterAuthorization auth)
    {
        _context = context;
        _auth = auth;
    }

    public async Task<LocationDto> Handle(CreateLocationCommand request, CancellationToken cancellationToken)
    {
        var (userId, facilityId, _) = await _auth.ResolveAuthorizedFacilityAsync(
            request.FacilityId,
            cancellationToken,
            Permissions.Facilities.LocationsManage,
            Permissions.Facilities.Manage);

        return await _context.ExecuteWithFacilityHierarchyLockAsync(facilityId, async () =>
        {
            var facilityExists = await _context.Facilities
                .AsNoTracking()
                .AnyAsync(f => f.Id == facilityId && !f.IsDeleted, cancellationToken);
            if (!facilityExists)
            {
                throw new EntityNotFoundException(nameof(Facility), facilityId);
            }

            if (request.ZoneId.HasValue)
            {
                var zoneValid = await _context.WarehouseZones
                    .AsNoTracking()
                    .AnyAsync(z => z.Id == request.ZoneId.Value && z.FacilityId == facilityId && !z.IsDeleted, cancellationToken);
                if (!zoneValid)
                {
                    throw new ValidationException(
                        nameof(request.ZoneId),
                        $"Zone '{request.ZoneId.Value}' does not exist in facility '{facilityId}'.");
                }
            }

            Location? parent = null;
            if (request.ParentLocationId.HasValue)
            {
                parent = await _context.Locations
                    .AsNoTracking()
                    .FirstOrDefaultAsync(l => l.Id == request.ParentLocationId.Value &&
                                              l.FacilityId == facilityId &&
                                              !l.IsDeleted,
                        cancellationToken);
                if (parent == null)
                {
                    throw new ValidationException(
                        nameof(request.ParentLocationId),
                        $"Parent location '{request.ParentLocationId.Value}' does not exist in facility '{facilityId}'.");
                }
            }

            int hierarchyLevel = parent == null ? 1 : parent.HierarchyLevel + 1;
            await LocationHierarchyRuleEvaluator.ValidatePlacementAsync(
                _context,
                facilityId,
                parent?.LocationType,
                request.LocationType,
                hierarchyLevel,
                cancellationToken);

            // A03: Permanent normalized uniqueness across all records including soft-deleted
            var normalizedCode = Location.NormalizeCode(request.LocationCode);
            var codeExists = await _context.Locations
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(l => l.FacilityId == facilityId && l.NormalizedLocationCode == normalizedCode, cancellationToken);
            if (codeExists)
            {
                throw new ValidationException(
                    nameof(request.LocationCode),
                    $"Location with normalized code '{request.LocationCode.Trim()}' already exists (permanently reserved) in this facility.");
            }

            var normalizedBarcode = Location.NormalizeCode(request.Barcode);
            var barcodeExists = await _context.Locations
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(l => l.FacilityId == facilityId && l.NormalizedBarcode == normalizedBarcode, cancellationToken);
            if (barcodeExists)
            {
                throw new ValidationException(
                    nameof(request.Barcode),
                    $"Location barcode '{request.Barcode.Trim()}' is already permanently reserved in this facility.");
            }

            var id = Guid.NewGuid();
            var trimmedCode = request.LocationCode.Trim();
            var location = new Location
            {
                Id = id,
                FacilityId = facilityId,
                ZoneId = request.ZoneId,
                ParentLocationId = parent?.Id,
                LocationType = request.LocationType,
                LocationCode = trimmedCode,
                NormalizedLocationCode = normalizedCode,
                LocationName = string.IsNullOrWhiteSpace(request.LocationName) ? trimmedCode : request.LocationName.Trim(),
                Barcode = request.Barcode.Trim(),
                NormalizedBarcode = normalizedBarcode,
                HierarchyLevel = hierarchyLevel,
                HierarchyPath = Location.BuildHierarchyPath(parent?.HierarchyPath, id),
                AisleCode = request.AisleCode,
                RackCode = request.RackCode,
                ShelfLevel = request.ShelfLevel,
                BinNumber = request.BinNumber,
                PickSequence = request.PickSequence,
                LengthCm = request.LengthCm,
                WidthCm = request.WidthCm,
                HeightCm = request.HeightCm,
                MaxVolumeCm3 = request.MaxVolumeCm3 ?? (request.LengthCm * request.WidthCm * request.HeightCm),
                MaxWeightKg = request.MaxWeightKg,
                Status = LocationStatus.Active,
                IsActive = true,
                IsStorable = request.IsStorable ?? Location.DefaultStorableForType(request.LocationType),
                IsPickable = request.IsPickable,
                IsReceiving = request.IsReceiving,
                Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim(),
                ConfigRevision = 1,
                CreatedBy = userId
            };

            _context.Locations.Add(location);
            await _context.SaveChangesAsync(cancellationToken);

            return WarehouseMasterMappers.ToLocationDto(location);
        }, cancellationToken);
    }
}
