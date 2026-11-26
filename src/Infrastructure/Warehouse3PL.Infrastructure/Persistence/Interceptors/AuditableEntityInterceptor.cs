using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Reference;

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
        bool allowExplicit = context is IApplicationDbContext appDb && appDb.AllowExplicitAuditTimestamps;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity is AuditableEntity<Guid> auditableGuid)
                {
                    if (!allowExplicit || auditableGuid.CreatedAt == default)
                    {
                        auditableGuid.CreatedAt = utcNow;
                    }
                    auditableGuid.CreatedBy ??= userId;
                }
                else if (entry.Entity is AuditableEntity<int> auditableInt)
                {
                    if (!allowExplicit || auditableInt.CreatedAt == default)
                    {
                        auditableInt.CreatedAt = utcNow;
                    }
                    auditableInt.CreatedBy ??= userId;
                }
                else if (entry.Entity is AuditableEntity<long> auditableLong)
                {
                    if (!allowExplicit || auditableLong.CreatedAt == default)
                    {
                        auditableLong.CreatedAt = utcNow;
                    }
                    auditableLong.CreatedBy ??= userId;
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                if (entry.Entity is AuditableEntity<Guid> auditableGuid)
                {
                    if (!allowExplicit || !entry.Property(nameof(auditableGuid.UpdatedAt)).IsModified || auditableGuid.UpdatedAt == null)
                    {
                        auditableGuid.UpdatedAt = utcNow;
                    }
                    auditableGuid.UpdatedBy ??= userId;
                }
                else if (entry.Entity is AuditableEntity<int> auditableInt)
                {
                    if (!allowExplicit || !entry.Property(nameof(auditableInt.UpdatedAt)).IsModified || auditableInt.UpdatedAt == null)
                    {
                        auditableInt.UpdatedAt = utcNow;
                    }
                    auditableInt.UpdatedBy ??= userId;
                }
                else if (entry.Entity is AuditableEntity<long> auditableLong)
                {
                    if (!allowExplicit || !entry.Property(nameof(auditableLong.UpdatedAt)).IsModified || auditableLong.UpdatedAt == null)
                    {
                        auditableLong.UpdatedAt = utcNow;
                    }
                    auditableLong.UpdatedBy ??= userId;
                }

                IncrementConfigRevisionIfModifiedDirectly(entry);
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

    private static void IncrementConfigRevisionIfModifiedDirectly(EntityEntry entry)
    {
        switch (entry.Entity)
        {
            case CountryCompliancePolicy policy when !entry.Property(nameof(CountryCompliancePolicy.ConfigRevision)).IsModified:
                policy.ConfigRevision++;
                break;
            case LegalEntity legalEntity when !entry.Property(nameof(LegalEntity.ConfigRevision)).IsModified:
                legalEntity.ConfigRevision++;
                break;
            case FxRateRecord fxRate when !entry.Property(nameof(FxRateRecord.ConfigRevision)).IsModified:
                fxRate.ConfigRevision++;
                break;
            case FacilityCalendar facilityCalendar when !entry.Property(nameof(FacilityCalendar.ConfigRevision)).IsModified:
                facilityCalendar.ConfigRevision++;
                break;
            case HolidayCalendar holidayCalendar when !entry.Property(nameof(HolidayCalendar.ConfigRevision)).IsModified:
                holidayCalendar.ConfigRevision++;
                break;
        }
    }
}

