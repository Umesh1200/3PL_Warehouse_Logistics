using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.Outbound.Dispatch.DTOs;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Outbound.Dispatch.Queries;

public record GetDeliveryExceptionsQuery : PagedQuery, IRequest<PagedResult<DeliveryExceptionQueueItemDto>>
{
    public Guid? FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public Guid? PackageId { get; init; }
    public Guid? ShipmentId { get; init; }
    public Guid? OrderId { get; init; }
    public DeliveryExceptionStatus? Status { get; init; }
    public DeliveryExceptionSeverity? Severity { get; init; }
    public DeliveryExceptionType? ExceptionType { get; init; }
    public DeliveryExceptionSource? Source { get; init; }
    public string? CarrierCode { get; init; }
    public bool? OpenOnly { get; init; }
}

public sealed class GetDeliveryExceptionsQueryHandler : IRequestHandler<GetDeliveryExceptionsQuery, PagedResult<DeliveryExceptionQueueItemDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public GetDeliveryExceptionsQueryHandler(
        IApplicationDbContext context,
        OutboundOperationalAuthorization outboundAuth)
    {
        _context = context;
        _outboundAuth = outboundAuth;
    }

    public async Task<PagedResult<DeliveryExceptionQueueItemDto>> Handle(
        GetDeliveryExceptionsQuery query,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureIdentityAllowed(allowClient: true, nameof(GetDeliveryExceptionsQuery));

        var scope = await _outboundAuth.ResolveOutboundQueryScopeAsync(
            query.ClientId,
            query.FacilityId,
            allowCrossFacilityRead: true,
            allowClient: true,
            cancellationToken);

        var dbQuery = _context.ShipmentDeliveryExceptions
            .IgnoreQueryFilters()
            .AsNoTracking();

        if (scope.ClientId.HasValue)
        {
            dbQuery = dbQuery.Where(e => e.ClientId == scope.ClientId.Value);
        }

        if (scope.FacilityId.HasValue)
        {
            dbQuery = dbQuery.Where(e => e.FacilityId == scope.FacilityId.Value);
        }
        else if (scope.AuthorizedFacilityIds != null)
        {
            dbQuery = dbQuery.Where(e => scope.AuthorizedFacilityIds.Contains(e.FacilityId));
        }

        if (query.PackageId.HasValue)
        {
            dbQuery = dbQuery.Where(e => e.PackageId == query.PackageId.Value);
        }

        if (query.ShipmentId.HasValue)
        {
            dbQuery = dbQuery.Where(e => e.ShipmentId == query.ShipmentId.Value);
        }

        if (query.OrderId.HasValue)
        {
            dbQuery = dbQuery.Where(e => e.OrderId == query.OrderId.Value);
        }

        if (query.Status.HasValue)
        {
            dbQuery = dbQuery.Where(e => e.Status == query.Status.Value);
        }

        if (query.OpenOnly == true)
        {
            dbQuery = dbQuery.Where(e =>
                e.Status != DeliveryExceptionStatus.Resolved &&
                e.Status != DeliveryExceptionStatus.Voided &&
                e.Status != DeliveryExceptionStatus.ReturnToSenderInitiated);
        }

        if (query.Severity.HasValue)
        {
            dbQuery = dbQuery.Where(e => e.Severity == query.Severity.Value);
        }

        if (query.ExceptionType.HasValue)
        {
            dbQuery = dbQuery.Where(e => e.ExceptionType == query.ExceptionType.Value);
        }

        if (query.Source.HasValue)
        {
            dbQuery = dbQuery.Where(e => e.Source == query.Source.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.CarrierCode))
        {
            var carrier = query.CarrierCode.Trim().ToUpperInvariant();
            dbQuery = dbQuery.Where(e => e.CarrierCode.ToUpper() == carrier);
        }

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm.Trim().ToLower();
            dbQuery = dbQuery.Where(e =>
                e.TrackingNumber.ToLower().Contains(term) ||
                e.CarrierCode.ToLower().Contains(term) ||
                e.CarrierExceptionCode.ToLower().Contains(term) ||
                e.CarrierDescription.ToLower().Contains(term) ||
                e.Package.PackageNumber.ToLower().Contains(term));
        }

        var pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
        var pageSize = query.PageSize;
        var totalCount = await dbQuery.CountAsync(cancellationToken);

        var items = await dbQuery
            .OrderByDescending(e => e.OccurredAtUtc)
            .ThenByDescending(e => e.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new DeliveryExceptionQueueItemDto
            {
                ExceptionId = e.Id,
                ClientId = e.ClientId,
                FacilityId = e.FacilityId,
                PackageId = e.PackageId,
                PackageNumber = e.Package.PackageNumber,
                ShipmentId = e.ShipmentId,
                ShipmentNumber = e.Shipment != null ? e.Shipment.ShipmentNumber : null,
                OrderId = e.OrderId,
                OrderNumber = e.Order != null ? e.Order.OrderNumber : null,
                TrackingNumber = e.TrackingNumber,
                CarrierCode = e.CarrierCode,
                ExceptionType = e.ExceptionType,
                Status = e.Status,
                Severity = e.Severity,
                Resolution = e.Resolution,
                Source = e.Source,
                CarrierExceptionCode = e.CarrierExceptionCode,
                CarrierDescription = e.CarrierDescription,
                OccurredAtUtc = e.OccurredAtUtc,
                AttemptNumber = e.AttemptNumber,
                ReattemptCount = e.ReattemptCount,
                ScheduledReattemptDateUtc = e.ScheduledReattemptDateUtc,
                ContactAttemptsCount = e.ContactAttemptsCount,
                CorrectedAddress = e.CorrectedAddress,
                HoldDepotLocation = e.HoldDepotLocation,
                ReportedByUserId = e.ReportedByUserId,
                ResolvedAtUtc = e.ResolvedAtUtc,
                ResolvedByUserId = e.ResolvedByUserId,
                ResolutionNotes = e.ResolutionNotes,
                CreatedAt = e.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return PagedResult<DeliveryExceptionQueueItemDto>.Create(items, totalCount, pageNumber, pageSize);
    }
}
