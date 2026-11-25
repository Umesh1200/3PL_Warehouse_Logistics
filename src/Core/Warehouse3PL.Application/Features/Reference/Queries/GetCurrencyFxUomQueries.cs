using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Reference.Queries;

public sealed record GetCurrenciesQuery(bool ActiveOnly = true) : IRequest<IReadOnlyList<CurrencyMasterDto>>;

public sealed class GetCurrenciesQueryHandler : IRequestHandler<GetCurrenciesQuery, IReadOnlyList<CurrencyMasterDto>>
{
    private readonly IApplicationDbContext _db;

    public GetCurrenciesQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<CurrencyMasterDto>> Handle(
        GetCurrenciesQuery request,
        CancellationToken cancellationToken)
    {
        var query = _db.Currencies.AsNoTracking();
        if (request.ActiveOnly)
        {
            query = query.Where(c => c.IsActive);
        }

        var rows = await query.OrderBy(c => c.CurrencyCode).ToListAsync(cancellationToken);
        return rows.Select(c => c.ToDto()).ToList();
    }
}

public sealed record GetCurrencyByCodeQuery(string CurrencyCode) : IRequest<CurrencyMasterDto?>;

public sealed class GetCurrencyByCodeQueryHandler : IRequestHandler<GetCurrencyByCodeQuery, CurrencyMasterDto?>
{
    private readonly IApplicationDbContext _db;

    public GetCurrencyByCodeQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<CurrencyMasterDto?> Handle(
        GetCurrencyByCodeQuery request,
        CancellationToken cancellationToken)
    {
        var normalized = (request.CurrencyCode ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length != 3)
        {
            return null;
        }

        var entity = await _db.Currencies
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CurrencyCode == normalized, cancellationToken);

        return entity?.ToDto();
    }
}

public sealed record GetUomDefinitionsQuery(
    UomDimension? Dimension = null,
    UomSystemOfMeasure? SystemOfMeasure = null,
    bool ActiveOnly = true) : IRequest<IReadOnlyList<UomDefinitionDto>>;

public sealed class GetUomDefinitionsQueryHandler
    : IRequestHandler<GetUomDefinitionsQuery, IReadOnlyList<UomDefinitionDto>>
{
    private readonly IApplicationDbContext _db;

    public GetUomDefinitionsQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<UomDefinitionDto>> Handle(
        GetUomDefinitionsQuery request,
        CancellationToken cancellationToken)
    {
        var query = _db.UomDefinitions.AsNoTracking();

        if (request.Dimension.HasValue)
        {
            query = query.Where(d => d.Dimension == request.Dimension.Value);
        }

        if (request.SystemOfMeasure.HasValue)
        {
            query = query.Where(d => d.SystemOfMeasure == request.SystemOfMeasure.Value);
        }

        if (request.ActiveOnly)
        {
            query = query.Where(d => d.IsActive);
        }

        var rows = await query
            .OrderBy(d => d.Dimension)
            .ThenByDescending(d => d.IsCanonicalBase)
            .ThenBy(d => d.UomCode)
            .ToListAsync(cancellationToken);

        return rows.Select(d => d.ToDto()).ToList();
    }
}

public sealed record GetUomConversionsQuery(
    string? FromUomCode = null,
    string? ToUomCode = null,
    UomDimension? Dimension = null,
    bool ActiveOnly = true) : IRequest<IReadOnlyList<UomConversionDto>>;

public sealed class GetUomConversionsQueryHandler
    : IRequestHandler<GetUomConversionsQuery, IReadOnlyList<UomConversionDto>>
{
    private readonly IApplicationDbContext _db;

    public GetUomConversionsQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<UomConversionDto>> Handle(
        GetUomConversionsQuery request,
        CancellationToken cancellationToken)
    {
        var query = _db.UomConversions.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.FromUomCode))
        {
            var fromCode = request.FromUomCode.Trim().ToUpperInvariant();
            query = query.Where(c => c.FromUomCode == fromCode);
        }

        if (!string.IsNullOrWhiteSpace(request.ToUomCode))
        {
            var toCode = request.ToUomCode.Trim().ToUpperInvariant();
            query = query.Where(c => c.ToUomCode == toCode);
        }

        if (request.Dimension.HasValue)
        {
            query = query.Where(c => c.Dimension == request.Dimension.Value);
        }

        if (request.ActiveOnly)
        {
            query = query.Where(c => c.IsActive);
        }

        var rows = await query
            .OrderBy(c => c.Dimension)
            .ThenBy(c => c.FromUomCode)
            .ThenBy(c => c.ToUomCode)
            .ToListAsync(cancellationToken);

        return rows.Select(c => c.ToDto()).ToList();
    }
}

public sealed record GetFxRatesQuery(
    string? BaseCurrencyCode = null,
    string? QuoteCurrencyCode = null,
    FxRateSourceAuthority? SourceAuthority = null,
    FxRateApprovalStatus? ApprovalStatus = null,
    bool? IsActive = null,
    DateTime? EffectiveAtUtc = null) : IRequest<IReadOnlyList<FxRateRecordDto>>;

public sealed class GetFxRatesQueryHandler : IRequestHandler<GetFxRatesQuery, IReadOnlyList<FxRateRecordDto>>
{
    private readonly IApplicationDbContext _db;

    public GetFxRatesQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<FxRateRecordDto>> Handle(
        GetFxRatesQuery request,
        CancellationToken cancellationToken)
    {
        var query = _db.FxRateRecords.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.BaseCurrencyCode))
        {
            var b = request.BaseCurrencyCode.Trim().ToUpperInvariant();
            query = query.Where(r => r.BaseCurrencyCode == b);
        }

        if (!string.IsNullOrWhiteSpace(request.QuoteCurrencyCode))
        {
            var q = request.QuoteCurrencyCode.Trim().ToUpperInvariant();
            query = query.Where(r => r.QuoteCurrencyCode == q);
        }

        if (request.SourceAuthority.HasValue)
        {
            query = query.Where(r => r.SourceAuthority == request.SourceAuthority.Value);
        }

        if (request.ApprovalStatus.HasValue)
        {
            query = query.Where(r => r.ApprovalStatus == request.ApprovalStatus.Value);
        }

        if (request.IsActive.HasValue)
        {
            query = query.Where(r => r.IsActive == request.IsActive.Value);
        }

        if (request.EffectiveAtUtc.HasValue)
        {
            var utc = request.EffectiveAtUtc.Value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(request.EffectiveAtUtc.Value, DateTimeKind.Utc)
                : request.EffectiveAtUtc.Value.ToUniversalTime();
            query = query.Where(r => r.EffectiveFromUtc <= utc && (r.EffectiveToUtc == null || r.EffectiveToUtc > utc));
        }

        var rows = await query
            .OrderByDescending(r => r.EffectiveFromUtc)
            .ThenByDescending(r => r.PublishedAtUtc)
            .ThenBy(r => r.BaseCurrencyCode)
            .ThenBy(r => r.QuoteCurrencyCode)
            .ToListAsync(cancellationToken);

        return rows.Select(r => r.ToDto()).ToList();
    }
}

public sealed record GetFxRateByIdQuery(Guid Id) : IRequest<FxRateRecordDto?>;

public sealed class GetFxRateByIdQueryHandler : IRequestHandler<GetFxRateByIdQuery, FxRateRecordDto?>
{
    private readonly IApplicationDbContext _db;

    public GetFxRateByIdQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<FxRateRecordDto?> Handle(
        GetFxRateByIdQuery request,
        CancellationToken cancellationToken)
    {
        var row = await _db.FxRateRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);

        return row?.ToDto();
    }
}
