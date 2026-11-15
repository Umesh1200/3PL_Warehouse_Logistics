using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Common;

namespace Warehouse3PL.Infrastructure.Persistence.Interceptors;

public class AuditableEntityInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTime;

    public AuditableEntityInterceptor(ICurrentUserContext currentUser, IDateTimeProvider dateTime)
    {
        _currentUser = currentUser;
        _dateTime = dateTime;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        UpdateEntities(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        UpdateEntities(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void UpdateEntities(DbContext? context)
    {
        if (context == null) return;

        var utcNow = _dateTime.UtcNow;
        var userId = _currentUser.UserId;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity is AuditableEntity<Guid> auditableGuid)
                {
                    auditableGuid.CreatedAt = utcNow;
                    auditableGuid.CreatedBy = userId;
                }
                else if (entry.Entity is AuditableEntity<int> auditableInt)
                {
                    auditableInt.CreatedAt = utcNow;
                    auditableInt.CreatedBy = userId;
                }
                else if (entry.Entity is AuditableEntity<long> auditableLong)
                {
                    auditableLong.CreatedAt = utcNow;
                    auditableLong.CreatedBy = userId;
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                if (entry.Entity is AuditableEntity<Guid> auditableGuid)
                {
                    auditableGuid.UpdatedAt = utcNow;
                    auditableGuid.UpdatedBy = userId;
                }
                else if (entry.Entity is AuditableEntity<int> auditableInt)
                {
                    auditableInt.UpdatedAt = utcNow;
                    auditableInt.UpdatedBy = userId;
                }
                else if (entry.Entity is AuditableEntity<long> auditableLong)
                {
                    auditableLong.UpdatedAt = utcNow;
                    auditableLong.UpdatedBy = userId;
                }
            }
            else if (entry.State == EntityState.Deleted && entry.Entity is ISoftDeletable softDeletable)
            {
                // Convert hard delete to soft delete
                entry.State = EntityState.Modified;
                softDeletable.IsDeleted = true;
                softDeletable.DeletedAt = utcNow;
                softDeletable.DeletedBy = userId;
            }
        }
    }
}

