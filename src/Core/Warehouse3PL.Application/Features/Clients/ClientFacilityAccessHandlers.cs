using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Clients;

public record GetClientFacilityAccessesQuery(Guid ClientId) : IRequest<IReadOnlyList<ClientFacilityAccessDto>>;

public record GrantClientFacilityAccessCommand : IRequest<ClientFacilityAccessDto>
{
    public Guid ClientId { get; init; }
    public Guid FacilityId { get; init; }
    public DateTime? EffectiveDate { get; init; }
    public bool PrimaryOperatingFacility { get; init; }
    public string? Remarks { get; init; }
}

public record UpdateClientFacilityAccessStatusCommand : IRequest<ClientFacilityAccessDto>
{
    public Guid ClientId { get; init; }
    public Guid FacilityId { get; init; }
    public FacilityAccessStatus Status { get; init; }
    public bool? PrimaryOperatingFacility { get; init; }
    public string? Remarks { get; init; }
    public int? ExpectedConfigRevision { get; init; }
}

public class GrantClientFacilityAccessCommandValidator : AbstractValidator<GrantClientFacilityAccessCommand>
{
    public GrantClientFacilityAccessCommandValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.FacilityId).NotEmpty();
        RuleFor(x => x.Remarks).MaximumLength(500);
    }
}

public class UpdateClientFacilityAccessStatusCommandValidator : AbstractValidator<UpdateClientFacilityAccessStatusCommand>
{
    public UpdateClientFacilityAccessStatusCommandValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.FacilityId).NotEmpty();
        RuleFor(x => x.Remarks).MaximumLength(500);
    }
}

public class GetClientFacilityAccessesQueryHandler : IRequestHandler<GetClientFacilityAccessesQuery, IReadOnlyList<ClientFacilityAccessDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly PartnerMasterAuthorization _auth;

    public GetClientFacilityAccessesQueryHandler(IApplicationDbContext context, PartnerMasterAuthorization auth)
    {
        _context = context;
        _auth = auth;
    }

    public async Task<IReadOnlyList<ClientFacilityAccessDto>> Handle(GetClientFacilityAccessesQuery request, CancellationToken cancellationToken)
    {
        var (caller, clientId) = await _auth.ResolveAuthorizedClientScopeAsync(
            request.ClientId,
            allowClientSelf: true,
            cancellationToken,
            allowedStaffPermissions: [Permissions.Clients.View, Permissions.Clients.ManageAccess]);

        var query = _context.ClientFacilityAccesses
            .IgnoreQueryFilters()
            .Include(a => a.Facility)
            .AsNoTracking()
            .Where(a => a.ClientId == clientId);

        if (caller.UserType == UserType.Staff && !caller.IsSuperAdmin && caller.AssignedFacilityId.HasValue)
        {
            query = query.Where(a => a.FacilityId == caller.AssignedFacilityId.Value);
        }

        var list = await query
            .OrderByDescending(a => a.PrimaryOperatingFacility)
            .ThenBy(a => a.Facility.FacilityCode)
            .Select(a => new ClientFacilityAccessDto
            {
                Id = a.Id,
                ClientId = a.ClientId,
                FacilityId = a.FacilityId,
                FacilityCode = a.Facility.FacilityCode,
                FacilityName = a.Facility.Name,
                EffectiveDate = a.EffectiveDate,
                Status = a.Status,
                PrimaryOperatingFacility = a.PrimaryOperatingFacility,
                RevokedAtUtc = a.RevokedAtUtc,
                Remarks = a.Remarks,
                ConfigRevision = a.ConfigRevision
            })
            .ToListAsync(cancellationToken);

        return list;
    }
}

public class GrantClientFacilityAccessCommandHandler : IRequestHandler<GrantClientFacilityAccessCommand, ClientFacilityAccessDto>
{
    private readonly IApplicationDbContext _context;
    private readonly PartnerMasterAuthorization _auth;

    public GrantClientFacilityAccessCommandHandler(IApplicationDbContext context, PartnerMasterAuthorization auth)
    {
        _context = context;
        _auth = auth;
    }

    public async Task<ClientFacilityAccessDto> Handle(GrantClientFacilityAccessCommand request, CancellationToken cancellationToken)
    {
        _auth.EnsureNoConflictingTenantHeader(request.ClientId);
        _auth.EnsureNoConflictingFacilityHeader(request.FacilityId);

        var caller = await _auth.RequireStaffPermissionAsync(cancellationToken, Permissions.Clients.ManageAccess);
        if (!caller.IsSuperAdmin && (!caller.AssignedFacilityId.HasValue || caller.AssignedFacilityId.Value != request.FacilityId))
        {
            throw new ForbiddenException($"Staff user is not authorized to manage client access for facility '{request.FacilityId}'.");
        }

        return await _context.ExecuteWithClientFacilityAccessLockAsync(request.ClientId, request.FacilityId, async () =>
        {
            var client = await _context.Clients
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == request.ClientId && !c.IsDeleted, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Client), request.ClientId);

            if (client.Status != ClientStatus.Active)
            {
                throw new DomainException(
                    $"Client '{client.AccountCode}' is '{client.Status}' and cannot be granted active facility access.",
                    "CLIENT_INACTIVE");
            }

            var facility = await _context.Facilities
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == request.FacilityId && !f.IsDeleted, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Facility), request.FacilityId);

            if (!facility.IsActive)
            {
                throw new DomainException(
                    $"Facility '{facility.FacilityCode}' is inactive and cannot accept client access assignments.",
                    "FACILITY_INACTIVE");
            }

            var existing = await _context.ClientFacilityAccesses
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(a => a.ClientId == request.ClientId && a.FacilityId == request.FacilityId, cancellationToken);

            if (existing != null && existing.Status == FacilityAccessStatus.Active)
            {
                throw new DomainException(
                    $"Client '{client.AccountCode}' already has active access to facility '{facility.FacilityCode}'.",
                    "CLIENT_FACILITY_ACCESS_DUPLICATE");
            }

            if (request.PrimaryOperatingFacility)
            {
                var currentPrimaries = await _context.ClientFacilityAccesses
                    .IgnoreQueryFilters()
                    .Where(a => a.ClientId == request.ClientId && a.PrimaryOperatingFacility && a.FacilityId != request.FacilityId)
                    .ToListAsync(cancellationToken);

                foreach (var prev in currentPrimaries)
                {
                    prev.PrimaryOperatingFacility = false;
                    prev.ConfigRevision++;
                }
            }

            ClientFacilityAccess access;
            if (existing != null)
            {
                existing.Status = FacilityAccessStatus.Active;
                existing.EffectiveDate = request.EffectiveDate ?? DateTime.UtcNow;
                existing.PrimaryOperatingFacility = request.PrimaryOperatingFacility;
                existing.RevokedAtUtc = null;
                existing.Remarks = request.Remarks?.Trim();
                existing.ConfigRevision++;
                access = existing;
            }
            else
            {
                access = new ClientFacilityAccess
                {
                    ClientId = request.ClientId,
                    FacilityId = request.FacilityId,
                    EffectiveDate = request.EffectiveDate ?? DateTime.UtcNow,
                    Status = FacilityAccessStatus.Active,
                    PrimaryOperatingFacility = request.PrimaryOperatingFacility,
                    Remarks = request.Remarks?.Trim(),
                    ConfigRevision = 1
                };
                _context.ClientFacilityAccesses.Add(access);
            }

            await _context.SaveChangesAsync(cancellationToken);

            return new ClientFacilityAccessDto
            {
                Id = access.Id,
                ClientId = access.ClientId,
                FacilityId = access.FacilityId,
                FacilityCode = facility.FacilityCode,
                FacilityName = facility.Name,
                EffectiveDate = access.EffectiveDate,
                Status = access.Status,
                PrimaryOperatingFacility = access.PrimaryOperatingFacility,
                RevokedAtUtc = access.RevokedAtUtc,
                Remarks = access.Remarks,
                ConfigRevision = access.ConfigRevision
            };
        }, cancellationToken);
    }
}

public class UpdateClientFacilityAccessStatusCommandHandler : IRequestHandler<UpdateClientFacilityAccessStatusCommand, ClientFacilityAccessDto>
{
    private readonly IApplicationDbContext _context;
    private readonly PartnerMasterAuthorization _auth;

    public UpdateClientFacilityAccessStatusCommandHandler(IApplicationDbContext context, PartnerMasterAuthorization auth)
    {
        _context = context;
        _auth = auth;
    }

    public async Task<ClientFacilityAccessDto> Handle(UpdateClientFacilityAccessStatusCommand request, CancellationToken cancellationToken)
    {
        _auth.EnsureNoConflictingTenantHeader(request.ClientId);
        _auth.EnsureNoConflictingFacilityHeader(request.FacilityId);

        var caller = await _auth.RequireStaffPermissionAsync(cancellationToken, Permissions.Clients.ManageAccess);
        if (!caller.IsSuperAdmin && (!caller.AssignedFacilityId.HasValue || caller.AssignedFacilityId.Value != request.FacilityId))
        {
            throw new ForbiddenException($"Staff user is not authorized to manage client access for facility '{request.FacilityId}'.");
        }

        return await _context.ExecuteWithClientFacilityAccessLockAsync(request.ClientId, request.FacilityId, async () =>
        {
            var access = await _context.ClientFacilityAccesses
                .IgnoreQueryFilters()
                .Include(a => a.Facility)
                .FirstOrDefaultAsync(a => a.ClientId == request.ClientId && a.FacilityId == request.FacilityId, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(ClientFacilityAccess), $"{request.ClientId}:{request.FacilityId}");

            if (request.ExpectedConfigRevision.HasValue && request.ExpectedConfigRevision.Value != access.ConfigRevision)
            {
                throw new ConcurrencyException("CLIENT_FACILITY_ACCESS_CONCURRENCY_CONFLICT", "Client facility access was modified concurrently.");
            }

            if (request.Status == FacilityAccessStatus.Active)
            {
                var client = await _context.Clients
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == request.ClientId && !c.IsDeleted, cancellationToken)
                    ?? throw new EntityNotFoundException(nameof(Client), request.ClientId);

                if (client.Status != ClientStatus.Active)
                {
                    throw new DomainException(
                        $"Client '{client.AccountCode}' is '{client.Status}' and cannot have active facility access.",
                        "CLIENT_INACTIVE");
                }

                if (access.Facility == null || !access.Facility.IsActive || access.Facility.IsDeleted)
                {
                    throw new DomainException("Facility is inactive and cannot accept active client access.", "FACILITY_INACTIVE");
                }
            }
            else if (access.Status != request.Status)
            {
                await PartnerMasterDependencyGuard.EnsureClientFacilityAccessCanBeRestrictedAsync(
                    _context, request.ClientId, request.FacilityId, request.Status, cancellationToken);
            }

            bool targetPrimary = request.Status == FacilityAccessStatus.Active
                ? (request.PrimaryOperatingFacility ?? access.PrimaryOperatingFacility)
                : false;

            if (targetPrimary)
            {
                var currentPrimaries = await _context.ClientFacilityAccesses
                    .IgnoreQueryFilters()
                    .Where(a => a.ClientId == request.ClientId && a.PrimaryOperatingFacility && a.FacilityId != request.FacilityId)
                    .ToListAsync(cancellationToken);

                foreach (var prev in currentPrimaries)
                {
                    prev.PrimaryOperatingFacility = false;
                    prev.ConfigRevision++;
                }
            }

            access.Status = request.Status;
            access.PrimaryOperatingFacility = targetPrimary;
            access.RevokedAtUtc = request.Status == FacilityAccessStatus.Revoked ? DateTime.UtcNow : null;
            if (request.Remarks != null)
            {
                access.Remarks = request.Remarks.Trim();
            }
            access.ConfigRevision++;

            await _context.SaveChangesAsync(cancellationToken);

            return new ClientFacilityAccessDto
            {
                Id = access.Id,
                ClientId = access.ClientId,
                FacilityId = access.FacilityId,
                FacilityCode = access.Facility?.FacilityCode,
                FacilityName = access.Facility?.Name,
                EffectiveDate = access.EffectiveDate,
                Status = access.Status,
                PrimaryOperatingFacility = access.PrimaryOperatingFacility,
                RevokedAtUtc = access.RevokedAtUtc,
                Remarks = access.Remarks,
                ConfigRevision = access.ConfigRevision
            };
        }, cancellationToken);
    }
}
