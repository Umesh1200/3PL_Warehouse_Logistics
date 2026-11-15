using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services.Billing;

public class BillableActivityService : IBillableActivityService
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ILogger<BillableActivityService> _logger;

    public BillableActivityService(
        IApplicationDbContext context,
        ITenantProvider tenantProvider,
        ICurrentUserContext currentUserContext,
        ILogger<BillableActivityService> logger)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _currentUserContext = currentUserContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<RecordBillableActivityResult> RecordActivityAsync(
        RecordBillableActivityRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ValidationException(nameof(request), "Record billable activity request cannot be null.");
        }

        // 1. Multi-Tenancy Validation
        ValidateTenantAccess(request.ClientId);

        // 2. Idempotency Deduplication Check
        var normalizedKey = request.IdempotencyKey?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            throw new ValidationException(nameof(request.IdempotencyKey), "Idempotency key is required.");
        }

        var existing = await _context.BillableActivities
            .FirstOrDefaultAsync(a => a.ClientId == request.ClientId && a.IdempotencyKey == normalizedKey, cancellationToken);

        if (existing != null)
        {
            _logger.LogInformation(
                "Idempotent duplicate billable activity detected for Client '{ClientId}', Key '{Key}'. Returning existing ID '{Id}'.",
                request.ClientId, normalizedKey, existing.Id);

            return new RecordBillableActivityResult
            {
                IsSuccess = true,
                IsDuplicate = true,
                BillableActivityId = existing.Id,
                Status = existing.Status,
                IdempotencyKey = existing.IdempotencyKey,
                Message = $"Billable activity already exists for idempotency key '{existing.IdempotencyKey}'."
            };
        }

        // 3. Resolve Billing Period
        Guid? resolvedPeriodId = request.BillingPeriodId;
        if (!resolvedPeriodId.HasValue)
        {
            var activePeriod = await _context.BillingPeriods
                .FirstOrDefaultAsync(p => p.ClientId == request.ClientId &&
                                          p.Status == BillingPeriodStatus.Open &&
                                          p.StartDateUtc <= request.OccurredAtUtc &&
                                          p.EndDateUtc >= request.OccurredAtUtc, cancellationToken);

            if (activePeriod != null)
            {
                resolvedPeriodId = activePeriod.Id;
            }
        }
        else
        {
            var specifiedPeriod = await _context.BillingPeriods
                .FirstOrDefaultAsync(p => p.Id == resolvedPeriodId.Value, cancellationToken);

            if (specifiedPeriod == null)
            {
                throw new EntityNotFoundException(nameof(BillingPeriod), resolvedPeriodId.Value);
            }

            if (specifiedPeriod.ClientId != request.ClientId)
            {
                throw new UnauthorizedException("Specified billing period belongs to a different client tenant.");
            }

            if (specifiedPeriod.Status == BillingPeriodStatus.Closed || specifiedPeriod.Status == BillingPeriodStatus.Invoiced)
            {
                throw new InvalidDomainStateException($"Cannot attach billable activity to a {specifiedPeriod.Status} billing period.");
            }
        }

        // 4. Construct Immutable Aggregate Record
        var activity = BillableActivity.Create(
            request.ClientId,
            request.FacilityId,
            request.ActivityType,
            request.SourceType,
            request.SourceId,
            request.SourceReference,
            request.Quantity,
            request.UnitOfMeasure,
            request.OccurredAtUtc,
            normalizedKey,
            request.Description ?? $"{request.ActivityType} from {request.SourceType}",
            resolvedPeriodId,
            request.MetadataJson);

        _context.BillableActivities.Add(activity);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Billable activity '{Id}' ({Type}) recorded for Client '{ClientId}', Source '{SourceType}/{SourceId}', Qty: {Qty} {Uom}.",
            activity.Id, activity.ActivityType, activity.ClientId, activity.SourceType, activity.SourceId, activity.Quantity, activity.UnitOfMeasure);

        return new RecordBillableActivityResult
        {
            IsSuccess = true,
            IsDuplicate = false,
            BillableActivityId = activity.Id,
            Status = activity.Status,
            IdempotencyKey = activity.IdempotencyKey,
            Message = "Billable activity successfully recorded."
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecordBillableActivityResult>> RecordActivitiesBatchAsync(
        IReadOnlyList<RecordBillableActivityRequest> requests,
        CancellationToken cancellationToken = default)
    {
        if (requests == null || requests.Count == 0)
        {
            return [];
        }

        var results = new List<RecordBillableActivityResult>();
        var seenKeysInBatch = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var req in requests)
        {
            var key = req.IdempotencyKey?.Trim() ?? string.Empty;
            if (seenKeysInBatch.Contains(key))
            {
                // Duplicate within the same incoming batch
                var priorResult = results.First(r => string.Equals(r.IdempotencyKey, key, StringComparison.OrdinalIgnoreCase));
                results.Add(new RecordBillableActivityResult
                {
                    IsSuccess = true,
                    IsDuplicate = true,
                    BillableActivityId = priorResult.BillableActivityId,
                    Status = priorResult.Status,
                    IdempotencyKey = key,
                    Message = "Duplicate billable activity within batch."
                });
                continue;
            }

            var singleResult = await RecordActivityAsync(req, cancellationToken);
            results.Add(singleResult);
            seenKeysInBatch.Add(key);
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<BillingPeriodDto> CreateBillingPeriodAsync(
        CreateBillingPeriodRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ValidationException(nameof(request), "Create billing period request cannot be null.");
        }

        ValidateTenantAccess(request.ClientId);

        var normalizedCode = request.PeriodCode?.Trim().ToUpperInvariant() ?? string.Empty;
        var codeExists = await _context.BillingPeriods
            .AnyAsync(p => p.ClientId == request.ClientId && p.PeriodCode == normalizedCode, cancellationToken);

        if (codeExists)
        {
            throw new ValidationException(nameof(request.PeriodCode), $"A billing period with code '{normalizedCode}' already exists for this client tenant.");
        }

        var period = BillingPeriod.Create(
            request.ClientId,
            normalizedCode,
            request.Name,
            request.StartDateUtc,
            request.EndDateUtc,
            request.Frequency,
            request.FacilityId,
            request.Notes);

        _context.BillingPeriods.Add(period);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Billing period '{Code}' ('{Name}') created for Client '{ClientId}'. Horizon: {Start:yyyy-MM-dd} to {End:yyyy-MM-dd}.",
            period.PeriodCode, period.Name, period.ClientId, period.StartDateUtc, period.EndDateUtc);

        return MapPeriodToDto(period, 0);
    }

    /// <inheritdoc />
    public async Task<BillingPeriodDto> LockBillingPeriodAsync(
        Guid periodId,
        CancellationToken cancellationToken = default)
    {
        var period = await _context.BillingPeriods
            .Include(p => p.Activities)
            .FirstOrDefaultAsync(p => p.Id == periodId, cancellationToken);

        if (period == null)
        {
            throw new EntityNotFoundException(nameof(BillingPeriod), periodId);
        }

        ValidateTenantAccess(period.ClientId);
        period.Lock(DateTime.UtcNow);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Billing period '{PeriodCode}' locked.", period.PeriodCode);
        return MapPeriodToDto(period, period.Activities.Count);
    }

    /// <inheritdoc />
    public async Task<BillingPeriodDto> ReopenBillingPeriodAsync(
        Guid periodId,
        CancellationToken cancellationToken = default)
    {
        var period = await _context.BillingPeriods
            .Include(p => p.Activities)
            .FirstOrDefaultAsync(p => p.Id == periodId, cancellationToken);

        if (period == null)
        {
            throw new EntityNotFoundException(nameof(BillingPeriod), periodId);
        }

        ValidateTenantAccess(period.ClientId);
        period.Reopen();
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Billing period '{PeriodCode}' reopened.", period.PeriodCode);
        return MapPeriodToDto(period, period.Activities.Count);
    }

    /// <inheritdoc />
    public async Task<BillingPeriodDto> CloseBillingPeriodAsync(
        Guid periodId,
        CancellationToken cancellationToken = default)
    {
        var period = await _context.BillingPeriods
            .Include(p => p.Activities)
            .FirstOrDefaultAsync(p => p.Id == periodId, cancellationToken);

        if (period == null)
        {
            throw new EntityNotFoundException(nameof(BillingPeriod), periodId);
        }

        ValidateTenantAccess(period.ClientId);
        period.Close(DateTime.UtcNow);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Billing period '{PeriodCode}' closed.", period.PeriodCode);
        return MapPeriodToDto(period, period.Activities.Count);
    }

    /// <inheritdoc />
    public async Task<BillableActivityDto?> GetActivityByIdAsync(
        Guid activityId,
        CancellationToken cancellationToken = default)
    {
        var activity = await _context.BillableActivities
            .FirstOrDefaultAsync(a => a.Id == activityId, cancellationToken);

        return activity == null ? null : MapActivityToDto(activity);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BillableActivityDto>> GetActivitiesBySourceAsync(
        Guid clientId,
        string sourceType,
        Guid sourceId,
        CancellationToken cancellationToken = default)
    {
        ValidateTenantAccess(clientId);

        var activities = await _context.BillableActivities
            .Where(a => a.ClientId == clientId && a.SourceType == sourceType && a.SourceId == sourceId)
            .OrderBy(a => a.OccurredAtUtc)
            .ToListAsync(cancellationToken);

        return activities.Select(MapActivityToDto).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BillableActivityDto>> GetUnratedActivitiesAsync(
        Guid clientId,
        Guid? periodId = null,
        CancellationToken cancellationToken = default)
    {
        ValidateTenantAccess(clientId);

        var query = _context.BillableActivities
            .Where(a => a.ClientId == clientId && a.Status == BillableActivityStatus.PendingRating);

        if (periodId.HasValue)
        {
            query = query.Where(a => a.BillingPeriodId == periodId.Value);
        }

        var activities = await query
            .OrderBy(a => a.OccurredAtUtc)
            .ToListAsync(cancellationToken);

        return activities.Select(MapActivityToDto).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BillingPeriodDto>> GetBillingPeriodsAsync(
        Guid clientId,
        BillingPeriodStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        ValidateTenantAccess(clientId);

        var query = _context.BillingPeriods
            .Include(p => p.Activities)
            .Where(p => p.ClientId == clientId);

        if (status.HasValue)
        {
            query = query.Where(p => p.Status == status.Value);
        }

        var periods = await query
            .OrderByDescending(p => p.StartDateUtc)
            .ToListAsync(cancellationToken);

        return periods.Select(p => MapPeriodToDto(p, p.Activities.Count)).ToList();
    }

    private void ValidateTenantAccess(Guid clientId)
    {
        if (_tenantProvider.TenantId.HasValue &&
            !_tenantProvider.BypassTenantFilter &&
            _tenantProvider.TenantId.Value != clientId)
        {
            throw new UnauthorizedException("Unauthorized cross-tenant commercial billing operation.");
        }
    }

    private static BillableActivityDto MapActivityToDto(BillableActivity a) => new()
    {
        Id = a.Id,
        ClientId = a.ClientId,
        FacilityId = a.FacilityId,
        BillingPeriodId = a.BillingPeriodId,
        ActivityType = a.ActivityType,
        Status = a.Status,
        SourceType = a.SourceType,
        SourceId = a.SourceId,
        SourceReference = a.SourceReference,
        Quantity = a.Quantity,
        UnitOfMeasure = a.UnitOfMeasure,
        UnitPrice = a.UnitPrice,
        TotalAmount = a.TotalAmount,
        Currency = a.Currency,
        RateCardId = a.RateCardId,
        RateCardTierId = a.RateCardTierId,
        RateCardVersion = a.RateCardVersion,
        RatedAtUtc = a.RatedAtUtc,
        InvoiceId = a.InvoiceId,
        IdempotencyKey = a.IdempotencyKey,
        OccurredAtUtc = a.OccurredAtUtc,
        Description = a.Description
    };

    private static BillingPeriodDto MapPeriodToDto(BillingPeriod p, int activityCount) => new()
    {
        Id = p.Id,
        ClientId = p.ClientId,
        FacilityId = p.FacilityId,
        PeriodCode = p.PeriodCode,
        Name = p.Name,
        StartDateUtc = p.StartDateUtc,
        EndDateUtc = p.EndDateUtc,
        Status = p.Status,
        Frequency = p.Frequency,
        LockedAtUtc = p.LockedAtUtc,
        ClosedAtUtc = p.ClosedAtUtc,
        InvoicedAtUtc = p.InvoicedAtUtc,
        ActivityCount = activityCount,
        Notes = p.Notes
    };
}

