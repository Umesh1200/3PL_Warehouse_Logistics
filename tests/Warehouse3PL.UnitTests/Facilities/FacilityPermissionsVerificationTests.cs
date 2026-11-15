using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Authorization;
using Warehouse3PL.UnitTests.Common;
using Xunit;

namespace Warehouse3PL.UnitTests.Facilities;

public class FacilityPermissionsVerificationTests
{
    [Fact]
    public async Task ClientFacilityAccess_WhenActive_AuthorizesTenantForFacility()
    {
        // Arrange
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();

        var tenantProvider = new TestTenantProvider { TenantId = clientId };
        var currentUser = new TestCurrentUserContext { TenantId = clientId, UserType = UserType.Client };

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var facility = new Facility
            {
                Id = facilityId,
                FacilityCode = "FAC-CHI1",
                Name = "Chicago Hub",
                Timezone = "America/Chicago"
            };

            var access = new ClientFacilityAccess
            {
                ClientId = clientId,
                FacilityId = facilityId,
                Status = FacilityAccessStatus.Active,
                PrimaryOperatingFacility = true
            };

            context.Facilities.Add(facility);
            context.ClientFacilityAccesses.Add(access);
            await context.SaveChangesAsync();
        }

        // Act - Verify tenant facility authorization
        using (var verifyContext = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var hasAccess = await verifyContext.ClientFacilityAccesses
                .AnyAsync(a => a.ClientId == clientId &&
                               a.FacilityId == facilityId &&
                               a.Status == FacilityAccessStatus.Active);

            // Assert
            Assert.True(hasAccess);
        }
    }

    [Fact]
    public async Task ClientFacilityAccess_WhenRevoked_DeniesTenantAuthorization()
    {
        // Arrange
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();

        var tenantProvider = new TestTenantProvider { TenantId = clientId };
        var currentUser = new TestCurrentUserContext { TenantId = clientId, UserType = UserType.Client };

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var access = new ClientFacilityAccess
            {
                ClientId = clientId,
                FacilityId = facilityId,
                Status = FacilityAccessStatus.Revoked,
                PrimaryOperatingFacility = false
            };

            context.ClientFacilityAccesses.Add(access);
            await context.SaveChangesAsync();
        }

        // Act
        using (var verifyContext = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var hasAccess = await verifyContext.ClientFacilityAccesses
                .AnyAsync(a => a.ClientId == clientId &&
                               a.FacilityId == facilityId &&
                               a.Status == FacilityAccessStatus.Active);

            // Assert
            Assert.False(hasAccess);
        }
    }

    [Fact]
    public async Task PermissionAuthorizationHandler_WhenUserHasFacilityPermission_ShouldSucceed()
    {
        // Arrange
        var currentUser = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            PermissionsList = [Permissions.Facilities.View, Permissions.Facilities.Manage]
        };

        var handler = new PermissionAuthorizationHandler(currentUser);
        var requirement = new PermissionRequirement(Permissions.Facilities.View);
        var context = new AuthorizationHandlerContext([requirement], new ClaimsPrincipal(), null);

        // Act
        await handler.HandleAsync(context);

        // Assert
        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task PermissionAuthorizationHandler_WhenUserLacksPermission_ShouldFail()
    {
        // Arrange
        var currentUser = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            PermissionsList = [Permissions.Clients.View] // Does NOT have facilities:manage
        };

        var handler = new PermissionAuthorizationHandler(currentUser);
        var requirement = new PermissionRequirement(Permissions.Facilities.Manage);
        var context = new AuthorizationHandlerContext([requirement], new ClaimsPrincipal(), null);

        // Act
        await handler.HandleAsync(context);

        // Assert
        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task PermissionAuthorizationHandler_SuperAdmin_ShouldAlwaysSucceed()
    {
        // Arrange
        var currentUser = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            RolesList = [SystemRoles.SuperAdmin],
            PermissionsList = [] // Empty explicit list, but SuperAdmin role grants all
        };

        var handler = new PermissionAuthorizationHandler(currentUser);
        var requirement = new PermissionRequirement("any:arbitrary:permission");
        var context = new AuthorizationHandlerContext([requirement], new ClaimsPrincipal(), null);

        // Act
        await handler.HandleAsync(context);

        // Assert
        Assert.True(context.HasSucceeded);
    }
}

