using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Features.Facilities.Queries.GetAuthorizedFacilities;
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

    [Fact]
    public async Task GetAuthorizedFacilities_ForStaff_ReturnsAllActiveFacilities()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var staffUser = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            UserType = UserType.Staff,
            PermissionsList = [Permissions.Facilities.View]
        };

        var fac1 = new Facility { Id = Guid.NewGuid(), FacilityCode = "FAC-CHI", Name = "Chicago Hub", IsActive = true };
        var fac2 = new Facility { Id = Guid.NewGuid(), FacilityCode = "FAC-DAL", Name = "Dallas Hub", IsActive = true };
        var facInactive = new Facility { Id = Guid.NewGuid(), FacilityCode = "FAC-INACT", Name = "Closed Hub", IsActive = false };

        using (var seedContext = TestDbContextFactory.Create(new TestTenantProvider(), staffUser, dbName))
        {
            seedContext.Facilities.AddRange(fac1, fac2, facInactive);
            await seedContext.SaveChangesAsync();
        }

        using (var testContext = TestDbContextFactory.Create(new TestTenantProvider(), staffUser, dbName))
        {
            var handler = new GetAuthorizedFacilitiesQueryHandler(testContext, staffUser);

            // Act
            var result = await handler.Handle(new GetAuthorizedFacilitiesQuery(), CancellationToken.None);

            // Assert
            Assert.Equal(2, result.Count);
            Assert.Contains(result, f => f.FacilityCode == "FAC-CHI");
            Assert.Contains(result, f => f.FacilityCode == "FAC-DAL");
            Assert.DoesNotContain(result, f => f.FacilityCode == "FAC-INACT");
        }
    }

    [Fact]
    public async Task GetAuthorizedFacilities_ForClientUser_ReturnsOnlyAuthorizedActiveFacilities()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var clientUser = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            TenantId = clientId,
            UserType = UserType.Client,
            PermissionsList = [Permissions.Facilities.View]
        };

        var facAuthorized = new Facility { Id = Guid.NewGuid(), FacilityCode = "FAC-AUTH", Name = "Authorized Hub", IsActive = true };
        var facUnauthorized = new Facility { Id = Guid.NewGuid(), FacilityCode = "FAC-UNAUTH", Name = "Unauthorized Hub", IsActive = true };

        using (var seedContext = TestDbContextFactory.Create(new TestTenantProvider { TenantId = clientId }, clientUser, dbName))
        {
            seedContext.Facilities.AddRange(facAuthorized, facUnauthorized);
            seedContext.ClientFacilityAccesses.Add(new ClientFacilityAccess
            {
                ClientId = clientId,
                FacilityId = facAuthorized.Id,
                Status = FacilityAccessStatus.Active,
                PrimaryOperatingFacility = true
            });
            await seedContext.SaveChangesAsync();
        }

        using (var testContext = TestDbContextFactory.Create(new TestTenantProvider { TenantId = clientId }, clientUser, dbName))
        {
            var handler = new GetAuthorizedFacilitiesQueryHandler(testContext, clientUser);

            // Act
            var result = await handler.Handle(new GetAuthorizedFacilitiesQuery(), CancellationToken.None);

            // Assert
            Assert.Single(result);
            Assert.Equal("FAC-AUTH", result[0].FacilityCode);
        }
    }

    [Fact]
    public async Task GetAuthorizedFacilities_WhenAccessRevokedOrDeleted_ExcludesFacility()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var clientUser = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            TenantId = clientId,
            UserType = UserType.Client,
            PermissionsList = [Permissions.Facilities.View]
        };

        var facRevoked = new Facility { Id = Guid.NewGuid(), FacilityCode = "FAC-REVOKED", Name = "Revoked Hub", IsActive = true };

        using (var seedContext = TestDbContextFactory.Create(new TestTenantProvider { TenantId = clientId }, clientUser, dbName))
        {
            seedContext.Facilities.Add(facRevoked);
            seedContext.ClientFacilityAccesses.Add(new ClientFacilityAccess
            {
                ClientId = clientId,
                FacilityId = facRevoked.Id,
                Status = FacilityAccessStatus.Revoked
            });
            await seedContext.SaveChangesAsync();
        }

        using (var testContext = TestDbContextFactory.Create(new TestTenantProvider { TenantId = clientId }, clientUser, dbName))
        {
            var handler = new GetAuthorizedFacilitiesQueryHandler(testContext, clientUser);

            // Act
            var result = await handler.Handle(new GetAuthorizedFacilitiesQuery(), CancellationToken.None);

            // Assert
            Assert.Empty(result);
        }
    }
}

