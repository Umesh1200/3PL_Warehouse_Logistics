using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.Outbound.Dispatch.DTOs;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Dispatch.Queries;

/// <summary>
/// Strictly scoped courier stop query returning only active Dispatched shipments
/// currently assigned to the authenticated UserType.Driver (or authorized Staff caller)
/// whose custody has not been revoked.
/// </summary>
public record GetCourierAssignedShipmentsQuery : IRequest<IReadOnlyList<DispatchBoardShipmentDto>>;

public sealed class GetCourierAssignedShipmentsQueryHandler
    : IRequestHandler<GetCourierAssignedShipmentsQuery, IReadOnlyList<DispatchBoardShipmentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public GetCourierAssignedShipmentsQueryHandler(
        IApplicationDbContext context,
        ICurrentUserContext currentUser,
        OutboundOperationalAuthorization outboundAuth)
    {
        _context = context;
        _currentUser = currentUser;
        _outboundAuth = outboundAuth;
    }

    public async Task<IReadOnlyList<DispatchBoardShipmentDto>> Handle(
        GetCourierAssignedShipmentsQuery query,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureAuthenticated();

        if (_currentUser.UserType == UserType.Client)
        {
            throw new ForbiddenException("Client portal identities are not permitted to access courier route execution queues.");
        }

        if (!_currentUser.UserId.HasValue || _currentUser.UserId.Value == Guid.Empty)
        {
            throw new ForbiddenException("Authenticated courier user identity is required.");
        }

        var callerUserId = _currentUser.UserId.Value;

        var dbQuery = _context.Shipments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => !s.IsDeleted &&
                        s.Status == ShipmentStatus.Dispatched &&
                        s.AssignedDriverUserId == callerUserId &&
                        s.CustodyStatus != ShipmentCustodyStatus.Revoked);

        if (_currentUser.UserType == UserType.Driver && _currentUser.TenantId.HasValue)
        {
            var tenantId = _currentUser.TenantId.Value;
            dbQuery = dbQuery.Where(s => s.ClientId == tenantId);
        }

        if (_currentUser.UserType == UserType.Staff && !_currentUser.IsSuperAdmin)
        {
            if (!_currentUser.FacilityId.HasValue || _currentUser.FacilityId.Value == Guid.Empty)
            {
                throw new ForbiddenException("Staff member has no assigned facility scope.");
            }
            var facilityId = _currentUser.FacilityId.Value;
            dbQuery = dbQuery.Where(s => s.FacilityId == facilityId);
        }

        var shipments = await dbQuery
            .OrderByDescending(s => s.DispatchedAtUtc ?? s.CreatedAt)
            .Take(50)
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
                s.CustodyStatus,
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

        var packageIds = shipments.SelectMany(s => s.Packages).Select(p => p.Id).ToList();
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

        return shipments.Select(s => new DispatchBoardShipmentDto
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
            CustodyStatus = s.CustodyStatus,
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
    }
}
