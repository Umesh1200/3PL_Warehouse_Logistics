using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
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
    public bool IsPickable { get; init; } = true;
    public bool IsReceiving { get; init; } = false;
}

public class CreateLocationCommandValidator : AbstractValidator<CreateLocationCommand>
{
    public CreateLocationCommandValidator()
    {
        RuleFor(x => x.FacilityId).NotEmpty().WithMessage("FacilityId is required.");
        RuleFor(x => x.LocationCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Barcode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LocationType).IsInEnum();
    }
}

public class CreateLocationCommandHandler : IRequestHandler<CreateLocationCommand, LocationDto>
{
    private readonly IApplicationDbContext _context;

    public CreateLocationCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<LocationDto> Handle(CreateLocationCommand request, CancellationToken cancellationToken)
    {
        var facilityExists = await _context.Facilities.AnyAsync(f => f.Id == request.FacilityId, cancellationToken);
        if (!facilityExists)
        {
            throw new EntityNotFoundException(nameof(Facility), request.FacilityId);
        }

        var codeExists = await _context.Locations.AnyAsync(
            l => l.FacilityId == request.FacilityId && l.LocationCode == request.LocationCode, cancellationToken);
        if (codeExists)
        {
            throw new ValidationException(nameof(request.LocationCode), $"Location with code '{request.LocationCode}' already exists in this facility.");
        }

        var location = new Location
        {
            FacilityId = request.FacilityId,
            ZoneId = request.ZoneId,
            ParentLocationId = request.ParentLocationId,
            LocationType = request.LocationType,
            LocationCode = request.LocationCode,
            Barcode = request.Barcode,
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
            IsPickable = request.IsPickable,
            IsReceiving = request.IsReceiving
        };

        _context.Locations.Add(location);
        await _context.SaveChangesAsync(cancellationToken);

        return new LocationDto
        {
            Id = location.Id,
            FacilityId = location.FacilityId,
            ZoneId = location.ZoneId,
            ParentLocationId = location.ParentLocationId,
            LocationType = location.LocationType,
            LocationCode = location.LocationCode,
            Barcode = location.Barcode,
            AisleCode = location.AisleCode,
            RackCode = location.RackCode,
            ShelfLevel = location.ShelfLevel,
            BinNumber = location.BinNumber,
            PickSequence = location.PickSequence,
            LengthCm = location.LengthCm,
            WidthCm = location.WidthCm,
            HeightCm = location.HeightCm,
            MaxVolumeCm3 = location.MaxVolumeCm3,
            MaxWeightKg = location.MaxWeightKg,
            IsActive = location.IsActive,
            IsPickable = location.IsPickable,
            IsReceiving = location.IsReceiving,
            IsDamaged = location.IsDamaged,
            IsLocked = location.IsLocked
        };
    }
}
