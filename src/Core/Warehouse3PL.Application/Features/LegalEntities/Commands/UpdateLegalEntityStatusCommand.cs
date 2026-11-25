using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.LegalEntities.Commands;

public sealed record UpdateLegalEntityStatusCommand(Guid Id, bool IsActive) : IRequest<LegalEntityDto>;

public sealed class UpdateLegalEntityStatusCommandHandler
    : IRequestHandler<UpdateLegalEntityStatusCommand, LegalEntityDto>
{
    private readonly IApplicationDbContext _db;
    private readonly CompliancePlatformAuthorization _auth;

    public UpdateLegalEntityStatusCommandHandler(IApplicationDbContext db, CompliancePlatformAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<LegalEntityDto> Handle(
        UpdateLegalEntityStatusCommand request,
        CancellationToken cancellationToken)
    {
        var staffUserId = await _auth.RequireVerifiedStaffPermissionAsync(
            cancellationToken,
            Permissions.Global.ManageLegalEntities);

        return await _db.ExecuteWithLegalEntityLocksAsync([request.Id], async () =>
        {
            var entity = await _db.LegalEntities
                .FirstOrDefaultAsync(le => le.Id == request.Id, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(LegalEntity), request.Id);

            if (!request.IsActive)
            {
                bool referencedByActiveFacility = await _db.Facilities
                    .AsNoTracking()
                    .AnyAsync(f => !f.IsDeleted && f.IsActive && f.DefaultLegalEntityId == entity.Id, cancellationToken);

                if (referencedByActiveFacility)
                {
                    throw new ConcurrencyException(
                        "LEGAL_ENTITY_IN_USE_BY_ACTIVE_FACILITY",
                        $"Cannot deactivate LegalEntity '{entity.LegalCode}' while it is bound as DefaultLegalEntityId on one or more active facilities.");
                }
            }

            entity.SetActiveStatus(request.IsActive);
            entity.UpdatedBy = staffUserId;
            await _db.SaveChangesAsync(cancellationToken);

            return entity.ToDto();
        }, cancellationToken);
    }
}

public sealed record DeleteLegalEntityCommand(Guid Id) : IRequest<Unit>;

public sealed class DeleteLegalEntityCommandHandler : IRequestHandler<DeleteLegalEntityCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly CompliancePlatformAuthorization _auth;

    public DeleteLegalEntityCommandHandler(IApplicationDbContext db, CompliancePlatformAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<Unit> Handle(DeleteLegalEntityCommand request, CancellationToken cancellationToken)
    {
        var staffUserId = await _auth.RequireVerifiedStaffPermissionAsync(
            cancellationToken,
            Permissions.Global.ManageLegalEntities);

        return await _db.ExecuteWithLegalEntityLocksAsync([request.Id], async () =>
        {
            var entity = await _db.LegalEntities
                .FirstOrDefaultAsync(le => le.Id == request.Id, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(LegalEntity), request.Id);

            bool referencedByAnyFacility = await _db.Facilities
                .AsNoTracking()
                .AnyAsync(f => !f.IsDeleted && f.DefaultLegalEntityId == entity.Id, cancellationToken);

            if (referencedByAnyFacility)
            {
                throw new ConcurrencyException(
                    "LEGAL_ENTITY_REFERENCED_BY_FACILITY",
                    $"Cannot delete LegalEntity '{entity.LegalCode}' because one or more facilities reference it.");
            }

            entity.UpdatedBy = staffUserId;
            _db.LegalEntities.Remove(entity); // converted to soft-delete via AuditableEntityInterceptor
            await _db.SaveChangesAsync(cancellationToken);

            return Unit.Value;
        }, cancellationToken);
    }
}
