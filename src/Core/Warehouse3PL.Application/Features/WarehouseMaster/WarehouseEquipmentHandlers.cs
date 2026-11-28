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

public sealed record GetWarehouseEquipmentQuery(
    Guid? FacilityId,
    WarehouseEquipmentType? EquipmentType = null,
    WarehouseEquipmentStatus? Status = null,
    string? Search = null,
    int PageNumber = 1,
    int PageSize = 50) : IRequest<PagedResult<WarehouseEquipmentDto>>;

public sealed class GetWarehouseEquipmentQueryHandler
    : IRequestHandler<GetWarehouseEquipmentQuery, PagedResult<WarehouseEquipmentDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly WarehouseMasterAuthorization _auth;

    public GetWarehouseEquipmentQueryHandler(IApplicationDbContext db, WarehouseMasterAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<PagedResult<WarehouseEquipmentDto>> Handle(GetWarehouseEquipmentQuery request, CancellationToken cancellationToken)
    {
        var (_, facilityId, _) = await _auth.ResolveAuthorizedFacilityReadAsync(
            request.FacilityId,
            cancellationToken,
            Permissions.Facilities.EquipmentView,
            Permissions.Facilities.View);

        var page = Math.Max(1, request.PageNumber);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);

        var query = _db.WarehouseEquipment
            .AsNoTracking()
            .Where(e => e.FacilityId == facilityId && !e.IsDeleted);

        if (request.EquipmentType.HasValue)
            query = query.Where(e => e.EquipmentType == request.EquipmentType.Value);

        if (request.Status.HasValue)
            query = query.Where(e => e.Status == request.Status.Value);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToUpperInvariant();
            query = query.Where(e => e.NormalizedEquipmentCode.Contains(term) || e.Name.ToUpper().Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(e => e.EquipmentCode)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<WarehouseEquipmentDto>(items.Select(WarehouseMasterMappers.ToDto).ToList(), total, page, pageSize);
    }
}

public sealed record GetWarehouseEquipmentByIdQuery(Guid Id) : IRequest<WarehouseEquipmentDto>;

public sealed class GetWarehouseEquipmentByIdQueryHandler
    : IRequestHandler<GetWarehouseEquipmentByIdQuery, WarehouseEquipmentDto>
{
    private readonly IApplicationDbContext _db;
    private readonly WarehouseMasterAuthorization _auth;

    public GetWarehouseEquipmentByIdQueryHandler(IApplicationDbContext db, WarehouseMasterAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<WarehouseEquipmentDto> Handle(GetWarehouseEquipmentByIdQuery request, CancellationToken cancellationToken)
    {
        var eq = await _db.WarehouseEquipment
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == request.Id && !e.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(WarehouseEquipment), request.Id);

        await _auth.ResolveAuthorizedFacilityReadAsync(
            eq.FacilityId,
            cancellationToken,
            Permissions.Facilities.EquipmentView,
            Permissions.Facilities.View);

        return WarehouseMasterMappers.ToDto(eq);
    }
}

public sealed record CreateWarehouseEquipmentCommand : IRequest<WarehouseEquipmentDto>
{
    public Guid? FacilityId { get; init; }
    public string EquipmentCode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public WarehouseEquipmentType EquipmentType { get; init; }
    public string? SerialNumber { get; init; }
    public decimal? MaxLiftHeightCm { get; init; }
    public decimal? MaxLoadKg { get; init; }
    public bool RequiresCertification { get; init; }
    public WarehouseEquipmentStatus Status { get; init; } = WarehouseEquipmentStatus.Active;
    public string? Remarks { get; init; }
}

public sealed class CreateWarehouseEquipmentCommandValidator : AbstractValidator<CreateWarehouseEquipmentCommand>
{
    public CreateWarehouseEquipmentCommandValidator()
    {
        RuleFor(x => x.EquipmentCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.EquipmentType).IsInEnum();
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.MaxLiftHeightCm).GreaterThan(0).When(x => x.MaxLiftHeightCm.HasValue);
        RuleFor(x => x.MaxLoadKg).GreaterThan(0).When(x => x.MaxLoadKg.HasValue);
    }
}

public sealed class CreateWarehouseEquipmentCommandHandler
    : IRequestHandler<CreateWarehouseEquipmentCommand, WarehouseEquipmentDto>
{
    private readonly IApplicationDbContext _db;
    private readonly WarehouseMasterAuthorization _auth;

    public CreateWarehouseEquipmentCommandHandler(IApplicationDbContext db, WarehouseMasterAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<WarehouseEquipmentDto> Handle(CreateWarehouseEquipmentCommand request, CancellationToken cancellationToken)
    {
        var (userId, facilityId, _) = await _auth.ResolveAuthorizedFacilityAsync(
            request.FacilityId,
            cancellationToken,
            Permissions.Facilities.EquipmentManage,
            Permissions.Facilities.Manage);

        var facilityExists = await _db.Facilities
            .AsNoTracking()
            .AnyAsync(f => f.Id == facilityId && !f.IsDeleted, cancellationToken);
        if (!facilityExists)
            throw new EntityNotFoundException(nameof(Facility), facilityId);

        var normalizedCode = WarehouseEquipment.NormalizeCode(request.EquipmentCode);
        var exists = await _db.WarehouseEquipment
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(e => e.FacilityId == facilityId && e.NormalizedEquipmentCode == normalizedCode, cancellationToken);
        if (exists)
        {
            throw new ValidationException(
                nameof(request.EquipmentCode),
                $"Equipment code '{request.EquipmentCode.Trim()}' already exists in this facility.");
        }

        var entity = new WarehouseEquipment
        {
            FacilityId = facilityId,
            EquipmentCode = request.EquipmentCode.Trim(),
            NormalizedEquipmentCode = normalizedCode,
            Name = request.Name.Trim(),
            EquipmentType = request.EquipmentType,
            SerialNumber = string.IsNullOrWhiteSpace(request.SerialNumber) ? null : request.SerialNumber.Trim(),
            MaxLiftHeightCm = request.MaxLiftHeightCm,
            MaxLoadKg = request.MaxLoadKg,
            RequiresCertification = request.RequiresCertification,
            Status = request.Status,
            Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim(),
            ConfigRevision = 1,
            CreatedBy = userId
        };

        _db.WarehouseEquipment.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return WarehouseMasterMappers.ToDto(entity);
    }
}

public sealed record UpdateWarehouseEquipmentCommand : IRequest<WarehouseEquipmentDto>
{
    public Guid Id { get; init; }
    public string? EquipmentCode { get; init; }
    public string Name { get; init; } = string.Empty;
    public WarehouseEquipmentType EquipmentType { get; init; }
    public string? SerialNumber { get; init; }
    public decimal? MaxLiftHeightCm { get; init; }
    public decimal? MaxLoadKg { get; init; }
    public bool RequiresCertification { get; init; }
    public string? Remarks { get; init; }
}

public sealed class UpdateWarehouseEquipmentCommandHandler
    : IRequestHandler<UpdateWarehouseEquipmentCommand, WarehouseEquipmentDto>
{
    private readonly IApplicationDbContext _db;
    private readonly WarehouseMasterAuthorization _auth;

    public UpdateWarehouseEquipmentCommandHandler(IApplicationDbContext db, WarehouseMasterAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<WarehouseEquipmentDto> Handle(UpdateWarehouseEquipmentCommand request, CancellationToken cancellationToken)
    {
        var eq = await _db.WarehouseEquipment
            .FirstOrDefaultAsync(e => e.Id == request.Id && !e.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(WarehouseEquipment), request.Id);

        var (userId, _, _) = await _auth.ResolveAuthorizedFacilityAsync(
            eq.FacilityId,
            cancellationToken,
            Permissions.Facilities.EquipmentManage,
            Permissions.Facilities.Manage);

        if (!string.IsNullOrWhiteSpace(request.EquipmentCode) &&
            !string.Equals(WarehouseEquipment.NormalizeCode(request.EquipmentCode), eq.NormalizedEquipmentCode, StringComparison.Ordinal))
        {
            throw new DomainException(
                "EQUIPMENT_IMMUTABLE_CODE",
                $"EquipmentCode '{eq.EquipmentCode}' is immutable.");
        }

        eq.Name = request.Name.Trim();
        eq.EquipmentType = request.EquipmentType;
        eq.SerialNumber = string.IsNullOrWhiteSpace(request.SerialNumber) ? null : request.SerialNumber.Trim();
        eq.MaxLiftHeightCm = request.MaxLiftHeightCm;
        eq.MaxLoadKg = request.MaxLoadKg;
        eq.RequiresCertification = request.RequiresCertification;
        eq.Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim();
        eq.ConfigRevision++;
        eq.UpdatedBy = userId;

        await _db.SaveChangesAsync(cancellationToken);
        return WarehouseMasterMappers.ToDto(eq);
    }
}

public sealed record UpdateWarehouseEquipmentStatusCommand(
    Guid Id,
    WarehouseEquipmentStatus Status,
    string? Remarks = null) : IRequest<WarehouseEquipmentDto>;

public sealed class UpdateWarehouseEquipmentStatusCommandHandler
    : IRequestHandler<UpdateWarehouseEquipmentStatusCommand, WarehouseEquipmentDto>
{
    private readonly IApplicationDbContext _db;
    private readonly WarehouseMasterAuthorization _auth;

    public UpdateWarehouseEquipmentStatusCommandHandler(IApplicationDbContext db, WarehouseMasterAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<WarehouseEquipmentDto> Handle(UpdateWarehouseEquipmentStatusCommand request, CancellationToken cancellationToken)
    {
        var eq = await _db.WarehouseEquipment
            .FirstOrDefaultAsync(e => e.Id == request.Id && !e.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(WarehouseEquipment), request.Id);

        var (userId, _, _) = await _auth.ResolveAuthorizedFacilityAsync(
            eq.FacilityId,
            cancellationToken,
            Permissions.Facilities.EquipmentManage,
            Permissions.Facilities.Manage);

        eq.Status = request.Status;
        if (request.Remarks != null)
        {
            eq.Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim();
        }
        eq.ConfigRevision++;
        eq.UpdatedBy = userId;

        await _db.SaveChangesAsync(cancellationToken);
        return WarehouseMasterMappers.ToDto(eq);
    }
}
