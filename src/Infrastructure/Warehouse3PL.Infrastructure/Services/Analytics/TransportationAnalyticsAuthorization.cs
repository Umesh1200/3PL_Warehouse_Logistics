using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services.Analytics;

/// <summary>Explicit query confinement, independent of ambient EF tenant-filter bypass.</summary>
public sealed class TransportationAnalyticsAuthorization(IApplicationDbContext db, ICurrentUserContext user)
{
    public async Task<TransportationQueryScope> ResolveAsync(Guid? clientId, Guid? facilityId, CancellationToken ct = default)
    {
        if (!user.IsAuthenticated) throw new UnauthorizedException("Authentication is required for transportation analytics.");
        if (user.IsSuperAdmin) return new(clientId, facilityId, null);
        if (user.UserType == UserType.Client)
        {
            // A request header/provider value is never a substitute for authenticated tenant identity.
            var tenantId = user.TenantId;
            if (!tenantId.HasValue || tenantId == Guid.Empty) throw new ForbiddenException("Authenticated client identity is required.");
            if (clientId.HasValue && clientId != tenantId) throw new ForbiddenException("Cross-client analytics access is forbidden.");
            var facilities = await db.ClientFacilityAccesses.AsNoTracking()
                .Where(a => a.ClientId == tenantId && a.Status == FacilityAccessStatus.Active)
                .Select(a => a.FacilityId).ToListAsync(ct);
            if (user.FacilityId is Guid assigned && assigned != Guid.Empty)
                facilities = facilities.Where(f => f == assigned).ToList();
            if (facilities.Count == 0 || (facilityId.HasValue && !facilities.Contains(facilityId.Value)))
                throw new ForbiddenException("Client has no access to the requested facility scope.");
            var effectiveFacility = facilityId ?? (user.FacilityId != Guid.Empty ? user.FacilityId : null);
            return new(tenantId, effectiveFacility, facilities);
        }
        if (user.UserType != UserType.Staff) throw new ForbiddenException("Transportation analytics access is forbidden.");
        if (user.HasPermission(Permissions.Analytics.MultiFacilityView) || user.HasPermission("Analytics:CrossFacility") ||
            user.HasPermission("Shipments:CrossFacility") || user.HasPermission("Financial:CrossFacility") || user.HasPermission("Financials:CrossFacility"))
            return new(clientId, facilityId, null);
        if (user.FacilityId is not Guid staffFacility || staffFacility == Guid.Empty || (facilityId.HasValue && facilityId != staffFacility))
            throw new ForbiddenException("Staff must use their assigned facility or have multi-facility permission.");
        return new(clientId, staffFacility, null);
    }

    public async Task<Shipment> RequireShipmentAsync(Guid shipmentId, CancellationToken ct = default)
    {
        var scope = await ResolveAsync(null, null, ct);
        return await scope.Apply(db.Shipments.AsNoTracking()).FirstOrDefaultAsync(s => s.Id == shipmentId, ct)
            ?? throw new EntityNotFoundException(nameof(Shipment), shipmentId);
    }
}

public sealed record TransportationQueryScope(Guid? ClientId, Guid? FacilityId, List<Guid>? AuthorizedFacilities)
{
    // These transportation records have Guid FacilityId and Guid/Guid? ClientId. Build SQL-translatable
    // predicates instead of invoking an in-memory authorization method inside an EF query.
    public IQueryable<T> Apply<T>(IQueryable<T> query)
    {
        var row = Expression.Parameter(typeof(T), "row");
        Expression predicate = Expression.Constant(true);
        if (ClientId.HasValue)
        {
            var property = Expression.Property(row, "ClientId");
            Expression value = Expression.Constant(ClientId.Value);
            if (property.Type == typeof(Guid?)) value = Expression.Convert(value, typeof(Guid?));
            predicate = Expression.AndAlso(predicate, Expression.Equal(property, value));
        }
        var facility = Expression.Property(row, "FacilityId");
        if (FacilityId.HasValue)
            predicate = Expression.AndAlso(predicate, Expression.Equal(facility, Expression.Constant(FacilityId.Value)));
        if (AuthorizedFacilities != null)
            predicate = Expression.AndAlso(predicate, Expression.Call(Expression.Constant(AuthorizedFacilities),
                typeof(List<Guid>).GetMethod(nameof(List<Guid>.Contains))!, facility));
        return query.Where(Expression.Lambda<Func<T, bool>>(predicate, row));
    }
}
