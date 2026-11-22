using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Infrastructure.Authentication;

/// <summary>
/// Authoritative JWT validation check ensuring active user state and strict alignment between
/// the signed JWT facility_id claim and database Users.AssignedFacilityId for warehouse Staff.
/// </summary>
public static class JwtStaffFacilityValidator
{
    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        var userIdStr = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.Principal?.FindFirst("sub")?.Value;

        if (!Guid.TryParse(userIdStr, out var userId) || userId == Guid.Empty)
        {
            context.Fail("Invalid token subject identity.");
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<IApplicationDbContext>();
        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.Id,
                u.IsDeleted,
                u.Status,
                u.UserType,
                u.AssignedFacilityId
            })
            .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

        if (user == null || user.IsDeleted || user.Status != UserStatus.Active)
        {
            context.Fail("User account is inactive, suspended, or deleted.");
            return;
        }

        if (user.UserType != UserType.Staff)
        {
            return;
        }

        var facilityClaim = context.Principal?.FindFirst("facility_id")?.Value;
        Guid? tokenFacilityId = null;
        if (!string.IsNullOrWhiteSpace(facilityClaim))
        {
            if (!Guid.TryParse(facilityClaim, out var parsedFacilityId) || parsedFacilityId == Guid.Empty)
            {
                context.Fail("Invalid staff facility claim.");
                return;
            }
            tokenFacilityId = parsedFacilityId;
        }

        if (tokenFacilityId != user.AssignedFacilityId)
        {
            context.Fail("Staff facility assignment has changed; token facility scope is stale.");
            return;
        }

        if (user.AssignedFacilityId.HasValue)
        {
            var facilityIsActive = await db.Facilities
                .AsNoTracking()
                .AnyAsync(f => f.Id == user.AssignedFacilityId.Value && !f.IsDeleted && f.IsActive,
                    context.HttpContext.RequestAborted);

            if (!facilityIsActive)
            {
                context.Fail("Assigned staff facility is inactive or no longer exists.");
            }
        }
    }
}
