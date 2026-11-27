using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.Application.Features.WarehouseMaster;

public sealed record GetWarehouseZonesQuery(
    Guid? FacilityId,
    WarehouseZoneType? ZoneType = null,
    bool? IsActive = null,
    Guid? DedicatedClientId = null,
    string? Search = null,
    int PageNumber = 1,
    int PageSize = 50) : IRequest<PagedResult<WarehouseZoneDto>>;

public sealed class GetWarehouseZonesQueryHandler : IRequestHandler<GetWarehouseZonesQuery, PagedResult<WarehouseZoneDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly WarehouseMasterAuthorization _auth;

    public GetWarehouseZonesQueryHandler(IApplicationDbContext db, WarehouseMasterAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<PagedResult<WarehouseZoneDto>> Handle(GetWarehouseZonesQuery request, CancellationToken cancellationToken)
    {
        var (_, facilityId, _) = await _auth.ResolveAuthorizedFacilityAsync(
            request.FacilityId,
            cancellationToken,
            Permissions.Facilities.ZonesView,
            Permissions.Facilities.View);

        var page = Math.Max(1, request.PageNumber);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);

        var query = _db.WarehouseZones
            .AsNoTracking()
            .Where(z => z.FacilityId == facilityId && !z.IsDeleted);

        if (request.ZoneType.HasValue)
            query = query.Where(z => z.ZoneType == request.ZoneType.Value);

        if (request.IsActive.HasValue)
            query = query.Where(z => z.IsActive == request.IsActive.Value);

        if (request.DedicatedClientId.HasValue)
            query = query.Where(z => z.DedicatedClientId == request.DedicatedClientId.Value);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToUpperInvariant();
            query = query.Where(z => z.NormalizedZoneCode.Contains(term) || z.Name.ToUpper().Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(z => z.ZoneCode)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<WarehouseZoneDto>(items.Select(WarehouseMasterMappers.ToDto).ToList(), total, page, pageSize);
    }
}

public sealed record GetWarehouseZoneByIdQuery(Guid Id) : IRequest<WarehouseZoneDto>;

public sealed class GetWarehouseZoneByIdQueryHandler : IRequestHandler<GetWarehouseZoneByIdQuery, WarehouseZoneDto>
{
    private readonly IApplicationDbContext _db;
    private readonly WarehouseMasterAuthorization _auth;

    public GetWarehouseZoneByIdQueryHandler(IApplicationDbContext db, WarehouseMasterAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<WarehouseZoneDto> Handle(GetWarehouseZoneByIdQuery request, CancellationToken cancellationToken)
    {
        var zone = await _db.WarehouseZones
            .AsNoTracking()
            .FirstOrDefaultAsync(z => z.Id == request.Id && !z.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(WarehouseZone), request.Id);

        await _auth.ResolveAuthorizedFacilityAsync(
            zone.FacilityId,
            cancellationToken,
            Permissions.Facilities.ZonesView,
            Permissions.Facilities.View);

        return WarehouseMasterMappers.ToDto(zone);
    }
}

public sealed record CreateWarehouseZoneCommand : IRequest<WarehouseZoneDto>
{
    public Guid? FacilityId { get; init; }
    public string ZoneCode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public WarehouseZoneType ZoneType { get; init; } = WarehouseZoneType.BulkStorage;
    public decimal? TempMinC { get; init; }
    public decimal? TempMaxC { get; init; }
    public Guid? DedicatedClientId { get; init; }
    public string? Remarks { get; init; }
}

public sealed class CreateWarehouseZoneCommandValidator : AbstractValidator<CreateWarehouseZoneCommand>
{
    public CreateWarehouseZoneCommandValidator()
    {
        RuleFor(x => x.ZoneCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ZoneType).IsInEnum();
        RuleFor(x => x.Remarks).MaximumLength(500).When(x => x.Remarks != null);
        RuleFor(x => x).Must(x => !x.TempMinC.HasValue || !x.TempMaxC.HasValue || x.TempMinC.Value <= x.TempMaxC.Value)
            .WithMessage("TempMinC must be less than or equal to TempMaxC.");
    }
}

public sealed class CreateWarehouseZoneCommandHandler : IRequestHandler<CreateWarehouseZoneCommand, WarehouseZoneDto>
{
    private readonly IApplicationDbContext _db;
    private readonly WarehouseMasterAuthorization _auth;

    public CreateWarehouseZoneCommandHandler(IApplicationDbContext db, WarehouseMasterAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<WarehouseZoneDto> Handle(CreateWarehouseZoneCommand request, CancellationToken cancellationToken)
    {
        var (userId, facilityId, _) = await _auth.ResolveAuthorizedFacilityAsync(
            request.FacilityId,
            cancellationToken,
            Permissions.Facilities.ZonesManage,
            Permissions.Facilities.Manage);

        var facilityExists = await _db.Facilities
            .AsNoTracking()
            .AnyAsync(f => f.Id == facilityId && !f.IsDeleted, cancellationToken);
        if (!facilityExists)
            throw new EntityNotFoundException(nameof(Facility), facilityId);

        if (request.DedicatedClientId.HasValue)
        {
            await _auth.ValidateClientFacilityBindingAsync(facilityId, request.DedicatedClientId.Value, cancellationToken);
        }

        var normalizedCode = WarehouseZone.NormalizeCode(request.ZoneCode);
        var codeExists = await _db.WarehouseZones
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(z => z.FacilityId == facilityId && z.NormalizedZoneCode == normalizedCode, cancellationToken);
        if (codeExists)
        {
            throw new ValidationException(
                nameof(request.ZoneCode),
                $"Zone code '{request.ZoneCode.Trim()}' already exists (including soft-deleted records) in this facility.");
        }

        var zone = new WarehouseZone
        {
            FacilityId = facilityId,
            ZoneCode = request.ZoneCode.Trim(),
            NormalizedZoneCode = normalizedCode,
            Name = request.Name.Trim(),
            ZoneType = request.ZoneType,
            TempMinC = request.TempMinC,
            TempMaxC = request.TempMaxC,
            DedicatedClientId = request.DedicatedClientId,
            Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim(),
            IsActive = true,
            ConfigRevision = 1,
            CreatedBy = userId
        };

        _db.WarehouseZones.Add(zone);
        await _db.SaveChangesAsync(cancellationToken);

        return WarehouseMasterMappers.ToDto(zone);
    }
}

public sealed record UpdateWarehouseZoneCommand : IRequest<WarehouseZoneDto>
{
    public Guid Id { get; init; }
    public string? ZoneCode { get; init; }
    public string Name { get; init; } = string.Empty;
    public WarehouseZoneType ZoneType { get; init; }
    public decimal? TempMinC { get; init; }
    public decimal? TempMaxC { get; init; }
    public Guid? DedicatedClientId { get; init; }
    public string? Remarks { get; init; }
}

public sealed class UpdateWarehouseZoneCommandValidator : AbstractValidator<UpdateWarehouseZoneCommand>
{
    public UpdateWarehouseZoneCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ZoneType).IsInEnum();
        RuleFor(x => x.Remarks).MaximumLength(500).When(x => x.Remarks != null);
        RuleFor(x => x).Must(x => !x.TempMinC.HasValue || !x.TempMaxC.HasValue || x.TempMinC.Value <= x.TempMaxC.Value)
            .WithMessage("TempMinC must be less than or equal to TempMaxC.");
    }
}

public sealed class UpdateWarehouseZoneCommandHandler : IRequestHandler<UpdateWarehouseZoneCommand, WarehouseZoneDto>
{
    private readonly IApplicationDbContext _db;
    private readonly WarehouseMasterAuthorization _auth;

    public UpdateWarehouseZoneCommandHandler(IApplicationDbContext db, WarehouseMasterAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<WarehouseZoneDto> Handle(UpdateWarehouseZoneCommand request, CancellationToken cancellationToken)
    {
        var zone = await _db.WarehouseZones
            .FirstOrDefaultAsync(z => z.Id == request.Id && !z.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(WarehouseZone), request.Id);

        var (userId, facilityId, _) = await _auth.ResolveAuthorizedFacilityAsync(
            zone.FacilityId,
            cancellationToken,
            Permissions.Facilities.ZonesManage,
            Permissions.Facilities.Manage);

        if (!string.IsNullOrWhiteSpace(request.ZoneCode) &&
            !string.Equals(WarehouseZone.NormalizeCode(request.ZoneCode), zone.NormalizedZoneCode, StringComparison.Ordinal))
        {
            throw new DomainException(
                "ZONE_IMMUTABLE_CODE",
                $"ZoneCode '{zone.ZoneCode}' is immutable and cannot be changed to '{request.ZoneCode}'.");
        }

        if (request.DedicatedClientId.HasValue)
        {
            await _auth.ValidateClientFacilityBindingAsync(facilityId, request.DedicatedClientId.Value, cancellationToken);
        }

        zone.Name = request.Name.Trim();
        zone.ZoneType = request.ZoneType;
        zone.TempMinC = request.TempMinC;
        zone.TempMaxC = request.TempMaxC;
        zone.DedicatedClientId = request.DedicatedClientId;
        zone.Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim();
        zone.ConfigRevision++;
        zone.UpdatedBy = userId;

        await _db.SaveChangesAsync(cancellationToken);
        return WarehouseMasterMappers.ToDto(zone);
    }
}

public sealed record UpdateWarehouseZoneStatusCommand(Guid Id, bool IsActive, string? Remarks = null) : IRequest<WarehouseZoneDto>;

public sealed class UpdateWarehouseZoneStatusCommandHandler : IRequestHandler<UpdateWarehouseZoneStatusCommand, WarehouseZoneDto>
{
    private readonly IApplicationDbContext _db;
    private readonly WarehouseMasterAuthorization _auth;

    public UpdateWarehouseZoneStatusCommandHandler(IApplicationDbContext db, WarehouseMasterAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<WarehouseZoneDto> Handle(UpdateWarehouseZoneStatusCommand request, CancellationToken cancellationToken)
    {
        var zone = await _db.WarehouseZones
            .FirstOrDefaultAsync(z => z.Id == request.Id && !z.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(WarehouseZone), request.Id);

        var (userId, _, _) = await _auth.ResolveAuthorizedFacilityAsync(
            zone.FacilityId,
            cancellationToken,
            Permissions.Facilities.ZonesManage,
            Permissions.Facilities.Manage);

        if (zone.IsActive && !request.IsActive)
        {
            var hasActiveLocations = await _db.Locations
                .AsNoTracking()
                .AnyAsync(l => l.FacilityId == zone.FacilityId &&
                               l.ZoneId == zone.Id &&
                               !l.IsDeleted &&
                               l.Status == LocationStatus.Active,
                    cancellationToken);

            if (hasActiveLocations)
            {
                throw new DomainException(
                    "ZONE_DEACTIVATION_BLOCKED_LOCATIONS",
                    $"Cannot deactivate zone '{zone.ZoneCode}' while active locations are assigned to it.");
            }

            var hasStock = await _db.StockOnHand
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(s => s.FacilityId == zone.FacilityId &&
                               s.Location.ZoneId == zone.Id &&
                               (s.QtyAvailable > 0m || s.QtyAllocated > 0m || s.QtyQuarantined > 0m),
                    cancellationToken);

            if (hasStock)
            {
                throw new DomainException(
                    "ZONE_DEACTIVATION_BLOCKED_STOCK",
                    $"Cannot deactivate zone '{zone.ZoneCode}' while active stock exists in its locations.");
            }
        }

        zone.IsActive = request.IsActive;
        if (request.Remarks != null)
        {
            zone.Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim();
        }
        zone.ConfigRevision++;
        zone.UpdatedBy = userId;

        await _db.SaveChangesAsync(cancellationToken);
        return WarehouseMasterMappers.ToDto(zone);
    }
}
