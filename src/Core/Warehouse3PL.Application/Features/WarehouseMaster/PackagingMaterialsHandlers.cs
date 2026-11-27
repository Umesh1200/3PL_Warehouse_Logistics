using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.Application.Features.WarehouseMaster;

public sealed record GetPackagingMaterialsQuery(
    Guid? FacilityId,
    Guid? ClientId = null,
    bool IncludeFacilityWide = true,
    PackagingMaterialType? MaterialType = null,
    bool? IsActive = null,
    string? Search = null,
    int PageNumber = 1,
    int PageSize = 50) : IRequest<PagedResult<PackagingMaterialDto>>;

public sealed class GetPackagingMaterialsQueryHandler
    : IRequestHandler<GetPackagingMaterialsQuery, PagedResult<PackagingMaterialDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly WarehouseMasterAuthorization _auth;

    public GetPackagingMaterialsQueryHandler(IApplicationDbContext db, WarehouseMasterAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<PagedResult<PackagingMaterialDto>> Handle(GetPackagingMaterialsQuery request, CancellationToken cancellationToken)
    {
        var (_, facilityId, _) = await _auth.ResolveAuthorizedFacilityAsync(
            request.FacilityId,
            cancellationToken,
            Permissions.Facilities.PackagingMaterialsView,
            Permissions.Facilities.View);

        var page = Math.Max(1, request.PageNumber);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);

        var query = _db.PackagingMaterials
            .AsNoTracking()
            .Where(m => m.FacilityId == facilityId && !m.IsDeleted);

        if (request.ClientId.HasValue)
        {
            query = request.IncludeFacilityWide
                ? query.Where(m => m.ClientId == request.ClientId.Value || m.ClientId == null)
                : query.Where(m => m.ClientId == request.ClientId.Value);
        }

        if (request.MaterialType.HasValue)
            query = query.Where(m => m.MaterialType == request.MaterialType.Value);

        if (request.IsActive.HasValue)
            query = query.Where(m => m.IsActive == request.IsActive.Value);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToUpperInvariant();
            query = query.Where(m => m.NormalizedCode.Contains(term) || m.Name.ToUpper().Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(m => m.Code)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<PackagingMaterialDto>(items.Select(WarehouseMasterMappers.ToDto).ToList(), total, page, pageSize);
    }
}

public sealed record GetPackagingMaterialByIdQuery(Guid Id) : IRequest<PackagingMaterialDto>;

public sealed class GetPackagingMaterialByIdQueryHandler
    : IRequestHandler<GetPackagingMaterialByIdQuery, PackagingMaterialDto>
{
    private readonly IApplicationDbContext _db;
    private readonly WarehouseMasterAuthorization _auth;

    public GetPackagingMaterialByIdQueryHandler(IApplicationDbContext db, WarehouseMasterAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<PackagingMaterialDto> Handle(GetPackagingMaterialByIdQuery request, CancellationToken cancellationToken)
    {
        var mat = await _db.PackagingMaterials
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == request.Id && !m.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(PackagingMaterial), request.Id);

        await _auth.ResolveAuthorizedFacilityAsync(
            mat.FacilityId,
            cancellationToken,
            Permissions.Facilities.PackagingMaterialsView,
            Permissions.Facilities.View);

        return WarehouseMasterMappers.ToDto(mat);
    }
}

public sealed record CreatePackagingMaterialCommand : IRequest<PackagingMaterialDto>
{
    public Guid? FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public PackagingMaterialType MaterialType { get; init; } = PackagingMaterialType.CorrugatedBox;
    public decimal LengthCm { get; init; }
    public decimal WidthCm { get; init; }
    public decimal HeightCm { get; init; }
    public decimal MaxWeightKg { get; init; }
    public decimal TareWeightKg { get; init; }
    public decimal? CostPerUnit { get; init; }
    public string? CurrencyCode { get; init; }
}

public sealed class CreatePackagingMaterialCommandValidator : AbstractValidator<CreatePackagingMaterialCommand>
{
    public CreatePackagingMaterialCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.MaterialType).IsInEnum();
        RuleFor(x => x.LengthCm).GreaterThan(0);
        RuleFor(x => x.WidthCm).GreaterThan(0);
        RuleFor(x => x.HeightCm).GreaterThan(0);
        RuleFor(x => x.MaxWeightKg).GreaterThan(0);
        RuleFor(x => x.TareWeightKg).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CostPerUnit).GreaterThanOrEqualTo(0).When(x => x.CostPerUnit.HasValue);
    }
}

public sealed class CreatePackagingMaterialCommandHandler
    : IRequestHandler<CreatePackagingMaterialCommand, PackagingMaterialDto>
{
    private readonly IApplicationDbContext _db;
    private readonly WarehouseMasterAuthorization _auth;

    public CreatePackagingMaterialCommandHandler(IApplicationDbContext db, WarehouseMasterAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<PackagingMaterialDto> Handle(CreatePackagingMaterialCommand request, CancellationToken cancellationToken)
    {
        var (userId, facilityId, _) = await _auth.ResolveAuthorizedFacilityAsync(
            request.FacilityId,
            cancellationToken,
            Permissions.Facilities.PackagingMaterialsManage,
            Permissions.Facilities.Manage);

        var facilityExists = await _db.Facilities
            .AsNoTracking()
            .AnyAsync(f => f.Id == facilityId && !f.IsDeleted, cancellationToken);
        if (!facilityExists)
            throw new EntityNotFoundException(nameof(Facility), facilityId);

        if (request.ClientId.HasValue)
        {
            await _auth.ValidateClientFacilityBindingAsync(facilityId, request.ClientId.Value, cancellationToken);
        }

        var normalizedCurrency = await _auth.ValidateOptionalCurrencyAsync(request.CurrencyCode, cancellationToken);
        var normalizedCode = PackagingMaterial.NormalizeMaterialCode(request.Code);

        // A05: Deterministic uniqueness — facility-wide (ClientId == null) vs client-specific (ClientId == X)
        var duplicate = await _db.PackagingMaterials
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(m => m.FacilityId == facilityId &&
                           m.ClientId == request.ClientId &&
                           m.NormalizedCode == normalizedCode,
                cancellationToken);

        if (duplicate)
        {
            throw new ValidationException(
                nameof(request.Code),
                $"Packaging material code '{request.Code.Trim()}' already exists in this facility/client scope.");
        }

        var entity = new PackagingMaterial
        {
            FacilityId = facilityId,
            ClientId = request.ClientId,
            Code = request.Code.Trim(),
            NormalizedCode = normalizedCode,
            Name = request.Name.Trim(),
            MaterialType = request.MaterialType,
            LengthCm = request.LengthCm,
            WidthCm = request.WidthCm,
            HeightCm = request.HeightCm,
            MaxWeightKg = request.MaxWeightKg,
            TareWeightKg = request.TareWeightKg,
            CostPerUnit = request.CostPerUnit,
            CurrencyCode = normalizedCurrency,
            IsActive = true,
            ConfigRevision = 1,
            CreatedBy = userId
        };

        _db.PackagingMaterials.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return WarehouseMasterMappers.ToDto(entity);
    }
}

public sealed record UpdatePackagingMaterialCommand : IRequest<PackagingMaterialDto>
{
    public Guid Id { get; init; }
    public string? Code { get; init; }
    public string Name { get; init; } = string.Empty;
    public PackagingMaterialType MaterialType { get; init; }
    public decimal LengthCm { get; init; }
    public decimal WidthCm { get; init; }
    public decimal HeightCm { get; init; }
    public decimal MaxWeightKg { get; init; }
    public decimal TareWeightKg { get; init; }
    public decimal? CostPerUnit { get; init; }
    public string? CurrencyCode { get; init; }
}

public sealed class UpdatePackagingMaterialCommandValidator : AbstractValidator<UpdatePackagingMaterialCommand>
{
    public UpdatePackagingMaterialCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.MaterialType).IsInEnum();
        RuleFor(x => x.LengthCm).GreaterThan(0);
        RuleFor(x => x.WidthCm).GreaterThan(0);
        RuleFor(x => x.HeightCm).GreaterThan(0);
        RuleFor(x => x.MaxWeightKg).GreaterThan(0);
        RuleFor(x => x.TareWeightKg).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CostPerUnit).GreaterThanOrEqualTo(0).When(x => x.CostPerUnit.HasValue);
    }
}

public sealed class UpdatePackagingMaterialCommandHandler
    : IRequestHandler<UpdatePackagingMaterialCommand, PackagingMaterialDto>
{
    private readonly IApplicationDbContext _db;
    private readonly WarehouseMasterAuthorization _auth;

    public UpdatePackagingMaterialCommandHandler(IApplicationDbContext db, WarehouseMasterAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<PackagingMaterialDto> Handle(UpdatePackagingMaterialCommand request, CancellationToken cancellationToken)
    {
        var mat = await _db.PackagingMaterials
            .FirstOrDefaultAsync(m => m.Id == request.Id && !m.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(PackagingMaterial), request.Id);

        var (userId, _, _) = await _auth.ResolveAuthorizedFacilityAsync(
            mat.FacilityId,
            cancellationToken,
            Permissions.Facilities.PackagingMaterialsManage,
            Permissions.Facilities.Manage);

        if (!string.IsNullOrWhiteSpace(request.Code) &&
            !string.Equals(PackagingMaterial.NormalizeMaterialCode(request.Code), mat.NormalizedCode, StringComparison.Ordinal))
        {
            throw new DomainException("PACKAGING_IMMUTABLE_CODE", $"Packaging material code '{mat.Code}' is immutable.");
        }

        var normalizedCurrency = await _auth.ValidateOptionalCurrencyAsync(request.CurrencyCode, cancellationToken);

        mat.Name = request.Name.Trim();
        mat.MaterialType = request.MaterialType;
        mat.LengthCm = request.LengthCm;
        mat.WidthCm = request.WidthCm;
        mat.HeightCm = request.HeightCm;
        mat.MaxWeightKg = request.MaxWeightKg;
        mat.TareWeightKg = request.TareWeightKg;
        mat.CostPerUnit = request.CostPerUnit;
        mat.CurrencyCode = normalizedCurrency;
        mat.ConfigRevision++;
        mat.UpdatedBy = userId;

        await _db.SaveChangesAsync(cancellationToken);
        return WarehouseMasterMappers.ToDto(mat);
    }
}

public sealed record UpdatePackagingMaterialStatusCommand(Guid Id, bool IsActive) : IRequest<PackagingMaterialDto>;

public sealed class UpdatePackagingMaterialStatusCommandHandler
    : IRequestHandler<UpdatePackagingMaterialStatusCommand, PackagingMaterialDto>
{
    private readonly IApplicationDbContext _db;
    private readonly WarehouseMasterAuthorization _auth;

    public UpdatePackagingMaterialStatusCommandHandler(IApplicationDbContext db, WarehouseMasterAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<PackagingMaterialDto> Handle(UpdatePackagingMaterialStatusCommand request, CancellationToken cancellationToken)
    {
        var mat = await _db.PackagingMaterials
            .FirstOrDefaultAsync(m => m.Id == request.Id && !m.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(PackagingMaterial), request.Id);

        var (userId, _, _) = await _auth.ResolveAuthorizedFacilityAsync(
            mat.FacilityId,
            cancellationToken,
            Permissions.Facilities.PackagingMaterialsManage,
            Permissions.Facilities.Manage);

        mat.IsActive = request.IsActive;
        mat.ConfigRevision++;
        mat.UpdatedBy = userId;

        await _db.SaveChangesAsync(cancellationToken);
        return WarehouseMasterMappers.ToDto(mat);
    }
}
