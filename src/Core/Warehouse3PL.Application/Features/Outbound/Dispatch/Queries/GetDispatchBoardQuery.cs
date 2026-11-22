using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.Outbound.Dispatch.DTOs;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Outbound.Dispatch.Queries;

public record GetDispatchBoardQuery : PagedQuery, IRequest<PagedResult<DispatchBoardShipmentDto>>
{
    public Guid? FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public ShipmentStatus? Status { get; init; }
    public TrackingMilestoneStatus? DeliveryStatus { get; init; }
    public string? CarrierCode { get; init; }
    public Guid? AssignedDriverUserId { get; init; }
    public bool? HasOpenExceptions { get; init; }
    public DateTime? FromDateUtc { get; init; }
    public DateTime? ToDateUtc { get; init; }
}

public sealed class GetDispatchBoardQueryHandler : IRequestHandler<GetDispatchBoardQuery, PagedResult<DispatchBoardShipmentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public GetDispatchBoardQueryHandler(
        IApplicationDbContext context,
        OutboundOperationalAuthorization outboundAuth)
    {
        _context = context;
        _outboundAuth = outboundAuth;
    }

    public async Task<PagedResult<DispatchBoardShipmentDto>> Handle(
        GetDispatchBoardQuery query,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureIdentityAllowed(allowClient: true, nameof(GetDispatchBoardQuery));

        var scope = await _outboundAuth.ResolveOutboundQueryScopeAsync(
            query.ClientId,
            query.FacilityId,
            allowCrossFacilityRead: true,
            allowClient: true,
            cancellationToken);

        var dbQuery = _context.Shipments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => !s.IsDeleted);

        if (scope.ClientId.HasValue)
        {
            dbQuery = dbQuery.Where(s => s.ClientId == scope.ClientId.Value);
        }

        if (scope.FacilityId.HasValue)
        {
            dbQuery = dbQuery.Where(s => s.FacilityId == scope.FacilityId.Value);
        }
        else if (scope.AuthorizedFacilityIds != null)
        {
            dbQuery = dbQuery.Where(s => scope.AuthorizedFacilityIds.Contains(s.FacilityId));
        }

        if (query.Status.HasValue)
        {
            dbQuery = dbQuery.Where(s => s.Status == query.Status.Value);
        }

        if (query.DeliveryStatus.HasValue)
        {
            dbQuery = dbQuery.Where(s => s.DeliveryStatus == query.DeliveryStatus.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.CarrierCode))
        {
            var carrier = query.CarrierCode.Trim().ToUpperInvariant();
            dbQuery = dbQuery.Where(s => s.CarrierCode.ToUpper() == carrier);
        }

        if (query.AssignedDriverUserId.HasValue)
        {
            dbQuery = dbQuery.Where(s => s.AssignedDriverUserId == query.AssignedDriverUserId.Value);
        }

        if (query.HasOpenExceptions == true)
        {
            dbQuery = dbQuery.Where(s => s.DeliveryExceptions.Any(e =>
                e.Status != DeliveryExceptionStatus.Resolved &&
                e.Status != DeliveryExceptionStatus.Voided &&
                e.Status != DeliveryExceptionStatus.ReturnToSenderInitiated));
        }

        if (query.FromDateUtc.HasValue)
        {
            dbQuery = dbQuery.Where(s => (s.DispatchedAtUtc ?? s.CreatedAt) >= query.FromDateUtc.Value);
        }

        if (query.ToDateUtc.HasValue)
        {
            dbQuery = dbQuery.Where(s => (s.DispatchedAtUtc ?? s.CreatedAt) <= query.ToDateUtc.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm.Trim().ToLower();
            dbQuery = dbQuery.Where(s =>
                s.ShipmentNumber.ToLower().Contains(term) ||
                s.CarrierCode.ToLower().Contains(term) ||
                (s.BillOfLadingNumber != null && s.BillOfLadingNumber.ToLower().Contains(term)) ||
                (s.TrailerNumber != null && s.TrailerNumber.ToLower().Contains(term)) ||
                s.Packages.Any(p => !p.IsDeleted &&
                    (p.PackageNumber.ToLower().Contains(term) ||
                     (p.TrackingNumber != null && p.TrackingNumber.ToLower().Contains(term)))));
        }

        var pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
        var pageSize = query.PageSize;
        var totalCount = await dbQuery.CountAsync(cancellationToken);

        var shipments = await dbQuery
            .OrderByDescending(s => s.DispatchedAtUtc ?? s.CreatedAt)
            .ThenByDescending(s => s.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new
            {
                s.Id,
                s.ShipmentNumber,
                s.ClientId,
                ClientName = s.Client.CompanyName,
                s.FacilityId,
                FacilityName = s.Facility.Name,
                s.CarrierCode,
                s.CarrierServiceLevel,
                s.Status,
                s.DeliveryStatus,
                s.DeliveryStatusUpdatedAtUtc,
                s.DispatchedAtUtc,
                s.DeliveredAtUtc,
                s.AssignedDriverUserId,
                AssignedDriverName = s.AssignedDriverUser != null
                    ? (s.AssignedDriverUser.FirstName + " " + s.AssignedDriverUser.LastName).Trim()
                    : null,
                AssignedDriverEmail = s.AssignedDriverUser != null ? s.AssignedDriverUser.Email : null,
                s.TrailerNumber,
                s.SealNumber,
                s.BillOfLadingNumber,
                s.TotalPackagesCount,
                s.TotalWeightKg,
                s.Version,
                OpenExceptionsCount = s.DeliveryExceptions.Count(e =>
                    e.Status != DeliveryExceptionStatus.Resolved &&
                    e.Status != DeliveryExceptionStatus.Voided &&
                    e.Status != DeliveryExceptionStatus.ReturnToSenderInitiated),
                Packages = s.Packages
                    .Where(p => !p.IsDeleted)
                    .OrderBy(p => p.PackageNumber)
                    .Select(p => new
                    {
                        p.Id,
                        p.PackageNumber,
                        p.TrackingNumber,
                        p.CarrierCode,
                        p.Status,
                        p.CurrentTrackingStatus,
                        p.DeliveredAtUtc,
                        p.OutboundOrderId,
                        OrderNumber = p.OutboundOrder.OrderNumber
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        var shipmentIds = shipments.Select(s => s.Id).ToList();
        var packageIds = shipments.SelectMany(s => s.Packages).Select(p => p.Id).ToList();

        var latestTransfers = shipmentIds.Count == 0
            ? new Dictionary<Guid, DateTime>()
            : await _context.ShipmentCustodyTransfers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(t => shipmentIds.Contains(t.ShipmentId))
                .GroupBy(t => t.ShipmentId)
                .Select(g => new { ShipmentId = g.Key, LatestAt = g.Max(t => t.TransferredAtUtc) })
                .ToDictionaryAsync(x => x.ShipmentId, x => x.LatestAt, cancellationToken);

        var activePodPackageIds = packageIds.Count == 0
            ? []
            : (await _context.ProofOfDeliveries
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(pod => packageIds.Contains(pod.PackageId) && !pod.IsSuperseded)
                .Select(pod => pod.PackageId)
                .Distinct()
                .ToListAsync(cancellationToken))
                .ToHashSet();

        var items = shipments.Select(s => new DispatchBoardShipmentDto
        {
            ShipmentId = s.Id,
            ShipmentNumber = s.ShipmentNumber,
            ClientId = s.ClientId,
            ClientName = s.ClientName,
            FacilityId = s.FacilityId,
            FacilityName = s.FacilityName,
            CarrierCode = s.CarrierCode,
            CarrierServiceLevel = s.CarrierServiceLevel,
            Status = s.Status,
            DeliveryStatus = s.DeliveryStatus,
            DeliveryStatusUpdatedAtUtc = s.DeliveryStatusUpdatedAtUtc,
            DispatchedAtUtc = s.DispatchedAtUtc,
            DeliveredAtUtc = s.DeliveredAtUtc,
            AssignedDriverUserId = s.AssignedDriverUserId,
            AssignedDriverName = s.AssignedDriverName,
            AssignedDriverEmail = s.AssignedDriverEmail,
            TrailerNumber = s.TrailerNumber,
            SealNumber = s.SealNumber,
            BillOfLadingNumber = s.BillOfLadingNumber,
            TotalPackagesCount = s.TotalPackagesCount,
            DeliveredPackagesCount = s.Packages.Count(p => p.CurrentTrackingStatus == TrackingMilestoneStatus.Delivered),
            OpenExceptionsCount = s.OpenExceptionsCount,
            TotalWeightKg = s.TotalWeightKg,
            Version = s.Version,
            LatestCustodyTransferredAtUtc = latestTransfers.GetValueOrDefault(s.Id),
            Packages = s.Packages.Select(p => new DispatchBoardPackageDto
            {
                PackageId = p.Id,
                PackageNumber = p.PackageNumber,
                TrackingNumber = p.TrackingNumber,
                CarrierCode = p.CarrierCode,
                PackageStatus = p.Status,
                TrackingStatus = p.CurrentTrackingStatus,
                DeliveredAtUtc = p.DeliveredAtUtc,
                OutboundOrderId = p.OutboundOrderId,
                OrderNumber = p.OrderNumber,
                HasActivePod = activePodPackageIds.Contains(p.Id)
            }).ToList()
        }).ToList();

        return PagedResult<DispatchBoardShipmentDto>.Create(items, totalCount, pageNumber, pageSize);
    }
}
