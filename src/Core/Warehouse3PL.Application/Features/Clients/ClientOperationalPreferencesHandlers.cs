using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.Application.Features.Clients;

public record GetClientOperationalPreferencesQuery(Guid ClientId) : IRequest<IReadOnlyList<ClientOperationalPreferenceDto>>;

public record GetEffectiveClientOperationalPreferenceQuery(Guid ClientId, Guid FacilityId)
    : IRequest<ClientOperationalPreferenceDto?>;

public record UpsertClientOperationalPreferenceCommand : IRequest<ClientOperationalPreferenceDto>
{
    public Guid ClientId { get; init; }
    public Guid? FacilityId { get; init; }
    public Guid? DefaultReceivingZoneId { get; init; }
    public Guid? DefaultShippingZoneId { get; init; }
    public bool RequireAsnBeforeReceiving { get; init; }
    public bool AllowPartialShipments { get; init; } = true;
    public bool RequireQcInspectionOnReturns { get; init; }
    public bool AllowLotMixingInLocation { get; init; }
    public int DefaultOrderPriority { get; init; } = 1;
    public string? Notes { get; init; }
    public int? ExpectedConfigRevision { get; init; }
}

public record DeleteClientOperationalPreferenceCommand(Guid ClientId, Guid PreferenceId) : IRequest<Unit>;

public class UpsertClientOperationalPreferenceCommandValidator : AbstractValidator<UpsertClientOperationalPreferenceCommand>
{
    public UpsertClientOperationalPreferenceCommandValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.DefaultOrderPriority).InclusiveBetween(1, 10);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public class GetClientOperationalPreferencesQueryHandler
    : IRequestHandler<GetClientOperationalPreferencesQuery, IReadOnlyList<ClientOperationalPreferenceDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly PartnerMasterAuthorization _auth;

    public GetClientOperationalPreferencesQueryHandler(IApplicationDbContext context, PartnerMasterAuthorization auth)
    {
        _context = context;
        _auth = auth;
    }

    public async Task<IReadOnlyList<ClientOperationalPreferenceDto>> Handle(
        GetClientOperationalPreferencesQuery request,
        CancellationToken cancellationToken)
    {
        var (caller, clientId) = await _auth.ResolveAuthorizedClientScopeAsync(
            request.ClientId,
            allowClientSelf: true,
            cancellationToken,
            allowedStaffPermissions: [Permissions.Clients.View, Permissions.Clients.ManagePreferences, Permissions.Clients.Edit]);

        var query = _context.ClientOperationalPreferences
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.ClientId == clientId && p.IsActive);

        if (caller.UserType == UserType.Staff && !caller.IsSuperAdmin && caller.AssignedFacilityId.HasValue)
        {
            var facId = caller.AssignedFacilityId.Value;
            query = query.Where(p => p.FacilityId == null || p.FacilityId == facId);
        }

        var items = await query
            .OrderBy(p => p.FacilityId.HasValue ? 1 : 0)
            .ThenBy(p => p.FacilityId)
            .ToListAsync(cancellationToken);

        return items.Select(MapDto).ToList();
    }

    internal static ClientOperationalPreferenceDto MapDto(ClientOperationalPreference p) => new()
    {
        Id = p.Id,
        ClientId = p.ClientId,
        FacilityId = p.FacilityId,
        Scope = p.FacilityId.HasValue ? "FacilityOverride" : "ClientDefault",
        DefaultReceivingZoneId = p.DefaultReceivingZoneId,
        DefaultShippingZoneId = p.DefaultShippingZoneId,
        RequireAsnBeforeReceiving = p.RequireAsnBeforeReceiving,
        AllowPartialShipments = p.AllowPartialShipments,
        RequireQcInspectionOnReturns = p.RequireQcInspectionOnReturns,
        AllowLotMixingInLocation = p.AllowLotMixingInLocation,
        DefaultOrderPriority = p.DefaultOrderPriority,
        Notes = p.Notes,
        IsActive = p.IsActive,
        ConfigRevision = p.ConfigRevision
    };
}

public class GetEffectiveClientOperationalPreferenceQueryHandler
    : IRequestHandler<GetEffectiveClientOperationalPreferenceQuery, ClientOperationalPreferenceDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly PartnerMasterAuthorization _auth;

    public GetEffectiveClientOperationalPreferenceQueryHandler(IApplicationDbContext context, PartnerMasterAuthorization auth)
    {
        _context = context;
        _auth = auth;
    }

    public async Task<ClientOperationalPreferenceDto?> Handle(
        GetEffectiveClientOperationalPreferenceQuery request,
        CancellationToken cancellationToken)
    {
        _auth.EnsureNoConflictingFacilityHeader(request.FacilityId);

        var (caller, clientId) = await _auth.ResolveAuthorizedClientScopeAsync(
            request.ClientId,
            allowClientSelf: true,
            cancellationToken,
            allowedStaffPermissions: [Permissions.Clients.View, Permissions.Clients.ManagePreferences, Permissions.Clients.Edit]);

        if (caller.UserType == UserType.Staff && !caller.IsSuperAdmin && caller.AssignedFacilityId != request.FacilityId)
        {
            throw new ForbiddenException($"Staff user is not authorized for facility '{request.FacilityId}'.");
        }

        var candidates = await _context.ClientOperationalPreferences
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.ClientId == clientId && p.IsActive && (p.FacilityId == request.FacilityId || p.FacilityId == null))
            .ToListAsync(cancellationToken);

        var effective = candidates.FirstOrDefault(p => p.FacilityId == request.FacilityId)
            ?? candidates.FirstOrDefault(p => p.FacilityId == null);

        return effective == null ? null : GetClientOperationalPreferencesQueryHandler.MapDto(effective);
    }
}

public class UpsertClientOperationalPreferenceCommandHandler
    : IRequestHandler<UpsertClientOperationalPreferenceCommand, ClientOperationalPreferenceDto>
{
    private readonly IApplicationDbContext _context;
    private readonly PartnerMasterAuthorization _auth;

    public UpsertClientOperationalPreferenceCommandHandler(IApplicationDbContext context, PartnerMasterAuthorization auth)
    {
        _context = context;
        _auth = auth;
    }

    public async Task<ClientOperationalPreferenceDto> Handle(
        UpsertClientOperationalPreferenceCommand request,
        CancellationToken cancellationToken)
    {
        _auth.EnsureNoConflictingFacilityHeader(request.FacilityId);

        var (caller, clientId) = await _auth.ResolveAuthorizedClientScopeAsync(
            request.ClientId,
            allowClientSelf: false,
            cancellationToken,
            allowedStaffPermissions: [Permissions.Clients.ManagePreferences, Permissions.Clients.Edit]);

        if (request.FacilityId.HasValue)
        {
            if (!caller.IsSuperAdmin && caller.AssignedFacilityId != request.FacilityId.Value)
            {
                throw new ForbiddenException($"Staff user is not authorized to configure preferences for facility '{request.FacilityId.Value}'.");
            }

            var hasActiveAccess = await _context.ClientFacilityAccesses
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(a => a.ClientId == clientId && a.FacilityId == request.FacilityId.Value && a.Status == FacilityAccessStatus.Active, cancellationToken);
            if (!hasActiveAccess)
            {
                throw new ValidationException("FacilityId", $"Client '{clientId}' does not have active facility access to facility '{request.FacilityId.Value}'.");
            }

            await ValidateZoneAsync(request.DefaultReceivingZoneId, request.FacilityId.Value, "DefaultReceivingZoneId", cancellationToken);
            await ValidateZoneAsync(request.DefaultShippingZoneId, request.FacilityId.Value, "DefaultShippingZoneId", cancellationToken);
        }
        else if (request.DefaultReceivingZoneId.HasValue || request.DefaultShippingZoneId.HasValue)
        {
            throw new ValidationException(
                "FacilityId",
                "Facility-specific default receiving/shipping zones can only be set on a FacilityOverride preference (FacilityId must be specified).");
        }

        return await _context.ExecuteWithClientLifecycleLockAsync(clientId, async () =>
        {
            var clientExists = await _context.Clients
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(c => c.Id == clientId && !c.IsDeleted, cancellationToken);
            if (!clientExists)
            {
                throw new EntityNotFoundException(nameof(Client), clientId);
            }

            var existing = await _context.ClientOperationalPreferences
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => p.ClientId == clientId && p.FacilityId == request.FacilityId, cancellationToken);

            if (existing != null)
            {
                if (request.ExpectedConfigRevision.HasValue && request.ExpectedConfigRevision.Value != existing.ConfigRevision)
                {
                    throw new ConcurrencyException("CLIENT_PREFERENCE_CONCURRENCY_CONFLICT", "Client operational preference was modified concurrently.");
                }

                existing.DefaultReceivingZoneId = request.DefaultReceivingZoneId;
                existing.DefaultShippingZoneId = request.DefaultShippingZoneId;
                existing.RequireAsnBeforeReceiving = request.RequireAsnBeforeReceiving;
                existing.AllowPartialShipments = request.AllowPartialShipments;
                existing.RequireQcInspectionOnReturns = request.RequireQcInspectionOnReturns;
                existing.AllowLotMixingInLocation = request.AllowLotMixingInLocation;
                existing.DefaultOrderPriority = request.DefaultOrderPriority;
                existing.Notes = request.Notes?.Trim();
                existing.IsActive = true;
                existing.ConfigRevision++;

                await _context.SaveChangesAsync(cancellationToken);
                return GetClientOperationalPreferencesQueryHandler.MapDto(existing);
            }

            var pref = new ClientOperationalPreference
            {
                Id = Guid.NewGuid(),
                ClientId = clientId,
                FacilityId = request.FacilityId,
                DefaultReceivingZoneId = request.DefaultReceivingZoneId,
                DefaultShippingZoneId = request.DefaultShippingZoneId,
                RequireAsnBeforeReceiving = request.RequireAsnBeforeReceiving,
                AllowPartialShipments = request.AllowPartialShipments,
                RequireQcInspectionOnReturns = request.RequireQcInspectionOnReturns,
                AllowLotMixingInLocation = request.AllowLotMixingInLocation,
                DefaultOrderPriority = request.DefaultOrderPriority,
                Notes = request.Notes?.Trim(),
                IsActive = true,
                ConfigRevision = 1
            };

            _context.ClientOperationalPreferences.Add(pref);
            await _context.SaveChangesAsync(cancellationToken);
            return GetClientOperationalPreferencesQueryHandler.MapDto(pref);
        }, cancellationToken);
    }

    private async Task ValidateZoneAsync(Guid? zoneId, Guid facilityId, string fieldName, CancellationToken ct)
    {
        if (!zoneId.HasValue) return;

        var valid = await _context.WarehouseZones
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(z => z.Id == zoneId.Value && z.FacilityId == facilityId && z.IsActive && !z.IsDeleted, ct);
        if (!valid)
        {
            throw new ValidationException(fieldName, $"Zone '{zoneId.Value}' is not an active zone in facility '{facilityId}'.");
        }
    }
}

public class DeleteClientOperationalPreferenceCommandHandler : IRequestHandler<DeleteClientOperationalPreferenceCommand, Unit>
{
    private readonly IApplicationDbContext _context;
    private readonly PartnerMasterAuthorization _auth;

    public DeleteClientOperationalPreferenceCommandHandler(IApplicationDbContext context, PartnerMasterAuthorization auth)
    {
        _context = context;
        _auth = auth;
    }

    public async Task<Unit> Handle(DeleteClientOperationalPreferenceCommand request, CancellationToken cancellationToken)
    {
        var (caller, clientId) = await _auth.ResolveAuthorizedClientScopeAsync(
            request.ClientId,
            allowClientSelf: false,
            cancellationToken,
            allowedStaffPermissions: [Permissions.Clients.ManagePreferences, Permissions.Clients.Edit]);

        var pref = await _context.ClientOperationalPreferences
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == request.PreferenceId && p.ClientId == clientId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(ClientOperationalPreference), request.PreferenceId);

        if (pref.FacilityId.HasValue && !caller.IsSuperAdmin && caller.AssignedFacilityId != pref.FacilityId.Value)
        {
            throw new ForbiddenException("Staff user cannot delete preferences for another facility.");
        }

        _context.ClientOperationalPreferences.Remove(pref);
        await _context.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
