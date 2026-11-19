using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Features.Facilities.Queries.GetAuthorizedFacilities;
using Warehouse3PL.Application.Features.Facilities.Queries.GetFacilityById;
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
    public async Task GetAuthorizedFacilities_ForStaff_WithMultiFacilityView_ReturnsAllActiveFacilities()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var staffUser = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            UserType = UserType.Staff,
            PermissionsList = [Permissions.Facilities.View, Permissions.Analytics.MultiFacilityView]
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
    public async Task GetAuthorizedFacilities_ForStaff_AssignedToSpecificFacility_ReturnsOnlyAssignedFacility()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var fac1 = new Facility { Id = Guid.NewGuid(), FacilityCode = "FAC-CHI", Name = "Chicago Hub", IsActive = true };
        var fac2 = new Facility { Id = Guid.NewGuid(), FacilityCode = "FAC-DAL", Name = "Dallas Hub", IsActive = true };

        var staffUser = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            UserType = UserType.Staff,
            FacilityId = fac1.Id, // Assigned to CHI only
            PermissionsList = [Permissions.Facilities.View] // No cross-facility permission
        };

        using (var seedContext = TestDbContextFactory.Create(new TestTenantProvider(), staffUser, dbName))
        {
            seedContext.Facilities.AddRange(fac1, fac2);
            await seedContext.SaveChangesAsync();
        }

        using (var testContext = TestDbContextFactory.Create(new TestTenantProvider(), staffUser, dbName))
        {
            var handler = new GetAuthorizedFacilitiesQueryHandler(testContext, staffUser);

            // Act
            var result = await handler.Handle(new GetAuthorizedFacilitiesQuery(), CancellationToken.None);

            // Assert
            Assert.Single(result);
            Assert.Equal("FAC-CHI", result[0].FacilityCode);
            Assert.DoesNotContain(result, f => f.FacilityCode == "FAC-DAL");
        }
    }

    [Fact]
    public async Task GetAuthorizedFacilities_ForStaff_UnassignedWithoutCrossFacility_FailsClosedReturnsEmpty()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var fac1 = new Facility { Id = Guid.NewGuid(), FacilityCode = "FAC-CHI", Name = "Chicago Hub", IsActive = true };

        var staffUser = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            UserType = UserType.Staff,
            FacilityId = null, // Unassigned
            PermissionsList = [Permissions.Facilities.View] // No cross-facility permission
        };

        using (var seedContext = TestDbContextFactory.Create(new TestTenantProvider(), staffUser, dbName))
        {
            seedContext.Facilities.Add(fac1);
            await seedContext.SaveChangesAsync();
        }

        using (var testContext = TestDbContextFactory.Create(new TestTenantProvider(), staffUser, dbName))
        {
            var handler = new GetAuthorizedFacilitiesQueryHandler(testContext, staffUser);

            // Act
            var result = await handler.Handle(new GetAuthorizedFacilitiesQuery(), CancellationToken.None);

            // Assert: Must fail closed
            Assert.Empty(result);
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

    [Fact]
    public async Task GetAuthorizedFacilities_WhenClientUserLacksTenantId_FailsClosedReturnsEmpty()
    {
        // Arrange - Nonstaff caller with facilities:view but NO tenant ID
        var dbName = Guid.NewGuid().ToString();
        var clientWithoutTenant = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            TenantId = null, // Missing Tenant
            UserType = UserType.Client,
            PermissionsList = [Permissions.Facilities.View]
        };

        var fac = new Facility { Id = Guid.NewGuid(), FacilityCode = "FAC-CHI", Name = "Chicago Hub", IsActive = true };

        using (var seedContext = TestDbContextFactory.Create(new TestTenantProvider(), clientWithoutTenant, dbName))
        {
            seedContext.Facilities.Add(fac);
            await seedContext.SaveChangesAsync();
        }

        using (var testContext = TestDbContextFactory.Create(new TestTenantProvider(), clientWithoutTenant, dbName))
        {
            var handler = new GetAuthorizedFacilitiesQueryHandler(testContext, clientWithoutTenant);

            // Act
            var result = await handler.Handle(new GetAuthorizedFacilitiesQuery(), CancellationToken.None);

            // Assert: Must fail closed and return empty, NEVER return facilities to tenant-less caller
            Assert.Empty(result);
        }
    }

    [Fact]
    public async Task GetAuthorizedFacilities_WhenUnsupportedNonstaffCaller_FailsClosedReturnsEmpty()
    {
        // Arrange - Driver or unknown identity without tenant access
        var dbName = Guid.NewGuid().ToString();
        var driverUser = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            TenantId = null,
            UserType = UserType.Driver,
            PermissionsList = [Permissions.Facilities.View]
        };

        var fac = new Facility { Id = Guid.NewGuid(), FacilityCode = "FAC-CHI", Name = "Chicago Hub", IsActive = true };

        using (var seedContext = TestDbContextFactory.Create(new TestTenantProvider(), driverUser, dbName))
        {
            seedContext.Facilities.Add(fac);
            await seedContext.SaveChangesAsync();
        }

        using (var testContext = TestDbContextFactory.Create(new TestTenantProvider(), driverUser, dbName))
        {
            var handler = new GetAuthorizedFacilitiesQueryHandler(testContext, driverUser);

            // Act
            var result = await handler.Handle(new GetAuthorizedFacilitiesQuery(), CancellationToken.None);

            // Assert
            Assert.Empty(result);
        }
    }

    [Fact]
    public async Task GetAuthorizedFacilities_WhenClientUserHoldsCrossFacilityGrant_StillConfinedToTenantAccess()
    {
        // Arrange - Client user who somehow holds MultiFacilityView permission
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var clientUser = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            TenantId = clientId,
            UserType = UserType.Client,
            PermissionsList = [Permissions.Facilities.View, Permissions.Analytics.MultiFacilityView, "Analytics:CrossFacility"]
        };

        var facAuthorized = new Facility { Id = Guid.NewGuid(), FacilityCode = "FAC-AUTH", Name = "Authorized Hub", IsActive = true };
        var facForeign = new Facility { Id = Guid.NewGuid(), FacilityCode = "FAC-FOREIGN", Name = "Foreign Hub", IsActive = true };

        using (var seedContext = TestDbContextFactory.Create(new TestTenantProvider { TenantId = clientId }, clientUser, dbName))
        {
            seedContext.Facilities.AddRange(facAuthorized, facForeign);
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

            // Assert: Cross-facility grant must NOT bypass client isolation. Only authorized facility returned.
            Assert.Single(result);
            Assert.Equal("FAC-AUTH", result[0].FacilityCode);
            Assert.DoesNotContain(result, f => f.FacilityCode == "FAC-FOREIGN");
        }
    }

    [Fact]
    public async Task GetAuthorizedFacilities_WhenDriverHoldsCrossFacilityGrant_FailsClosedReturnsEmpty()
    {
        // Arrange - Driver identity who holds MultiFacilityView
        var dbName = Guid.NewGuid().ToString();
        var driverUser = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            TenantId = null,
            UserType = UserType.Driver,
            PermissionsList = [Permissions.Facilities.View, Permissions.Analytics.MultiFacilityView]
        };

        var fac = new Facility { Id = Guid.NewGuid(), FacilityCode = "FAC-CHI", Name = "Chicago Hub", IsActive = true };

        using (var seedContext = TestDbContextFactory.Create(new TestTenantProvider(), driverUser, dbName))
        {
            seedContext.Facilities.Add(fac);
            await seedContext.SaveChangesAsync();
        }

        using (var testContext = TestDbContextFactory.Create(new TestTenantProvider(), driverUser, dbName))
        {
            var handler = new GetAuthorizedFacilitiesQueryHandler(testContext, driverUser);

            // Act
            var result = await handler.Handle(new GetAuthorizedFacilitiesQuery(), CancellationToken.None);

            // Assert: Must fail closed
            Assert.Empty(result);
        }
    }

    [Fact]
    public async Task GetFacilityById_ForStaff_WhenAssigned_ReturnsFacility()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var facilityId = Guid.NewGuid();
        var staffUser = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            UserType = UserType.Staff,
            FacilityId = facilityId,
            PermissionsList = [Permissions.Facilities.View]
        };

        var fac = new Facility { Id = facilityId, FacilityCode = "FAC-STAFF", Name = "Staff Hub", IsActive = true };

        using (var seedContext = TestDbContextFactory.Create(new TestTenantProvider(), staffUser, dbName))
        {
            seedContext.Facilities.Add(fac);
            await seedContext.SaveChangesAsync();
        }

        using (var testContext = TestDbContextFactory.Create(new TestTenantProvider(), staffUser, dbName))
        {
            var handler = new GetFacilityByIdQueryHandler(testContext, staffUser);

            // Act
            var result = await handler.Handle(new GetFacilityByIdQuery(facilityId), CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("FAC-STAFF", result.FacilityCode);
        }
    }

    [Fact]
    public async Task GetFacilityById_ForStaff_WhenAssignedToDifferentFacility_ReturnsNull()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var assignedFacilityId = Guid.NewGuid();
        var requestedFacilityId = Guid.NewGuid();
        var staffUser = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            UserType = UserType.Staff,
            FacilityId = assignedFacilityId,
            PermissionsList = [Permissions.Facilities.View] // No cross-facility permission
        };

        var fac = new Facility { Id = requestedFacilityId, FacilityCode = "FAC-OTHER", Name = "Other Hub", IsActive = true };

        using (var seedContext = TestDbContextFactory.Create(new TestTenantProvider(), staffUser, dbName))
        {
            seedContext.Facilities.Add(fac);
            await seedContext.SaveChangesAsync();
        }

        using (var testContext = TestDbContextFactory.Create(new TestTenantProvider(), staffUser, dbName))
        {
            var handler = new GetFacilityByIdQueryHandler(testContext, staffUser);

            // Act: Staff attempts to query facility other than assigned
            var result = await handler.Handle(new GetFacilityByIdQuery(requestedFacilityId), CancellationToken.None);

            // Assert: Must fail closed (null)
            Assert.Null(result);
        }
    }

    [Fact]
    public async Task GetFacilityById_ForStaff_WhenUnassignedWithoutCrossFacility_ReturnsNull()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var facilityId = Guid.NewGuid();
        var staffUser = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            UserType = UserType.Staff,
            FacilityId = null, // Unassigned
            PermissionsList = [Permissions.Facilities.View]
        };

        var fac = new Facility { Id = facilityId, FacilityCode = "FAC-STAFF", Name = "Staff Hub", IsActive = true };

        using (var seedContext = TestDbContextFactory.Create(new TestTenantProvider(), staffUser, dbName))
        {
            seedContext.Facilities.Add(fac);
            await seedContext.SaveChangesAsync();
        }

        using (var testContext = TestDbContextFactory.Create(new TestTenantProvider(), staffUser, dbName))
        {
            var handler = new GetFacilityByIdQueryHandler(testContext, staffUser);

            // Act
            var result = await handler.Handle(new GetFacilityByIdQuery(facilityId), CancellationToken.None);

            // Assert: Unassigned staff fails closed
            Assert.Null(result);
        }
    }

    [Fact]
    public async Task GetFacilityById_ForStaff_WhenMultiFacilityView_ReturnsFacility()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var facilityId = Guid.NewGuid();
        var staffUser = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            UserType = UserType.Staff,
            FacilityId = null, // Unassigned or different
            PermissionsList = [Permissions.Facilities.View, Permissions.Analytics.MultiFacilityView]
        };

        var fac = new Facility { Id = facilityId, FacilityCode = "FAC-MULTI", Name = "Multi Hub", IsActive = true };

        using (var seedContext = TestDbContextFactory.Create(new TestTenantProvider(), staffUser, dbName))
        {
            seedContext.Facilities.Add(fac);
            await seedContext.SaveChangesAsync();
        }

        using (var testContext = TestDbContextFactory.Create(new TestTenantProvider(), staffUser, dbName))
        {
            var handler = new GetFacilityByIdQueryHandler(testContext, staffUser);

            // Act
            var result = await handler.Handle(new GetFacilityByIdQuery(facilityId), CancellationToken.None);

            // Assert: Permitted by multi-facility permission
            Assert.NotNull(result);
            Assert.Equal("FAC-MULTI", result.FacilityCode);
        }
    }

    [Fact]
    public async Task GetFacilityById_ForClientUser_WhenAuthorized_ReturnsFacility()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var clientUser = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            TenantId = clientId,
            UserType = UserType.Client,
            PermissionsList = [Permissions.Facilities.View]
        };

        var fac = new Facility { Id = facilityId, FacilityCode = "FAC-CLIENT", Name = "Client Hub", IsActive = true };

        using (var seedContext = TestDbContextFactory.Create(new TestTenantProvider { TenantId = clientId }, clientUser, dbName))
        {
            seedContext.Facilities.Add(fac);
            seedContext.ClientFacilityAccesses.Add(new ClientFacilityAccess
            {
                ClientId = clientId,
                FacilityId = facilityId,
                Status = FacilityAccessStatus.Active
            });
            await seedContext.SaveChangesAsync();
        }

        using (var testContext = TestDbContextFactory.Create(new TestTenantProvider { TenantId = clientId }, clientUser, dbName))
        {
            var handler = new GetFacilityByIdQueryHandler(testContext, clientUser);

            // Act
            var result = await handler.Handle(new GetFacilityByIdQuery(facilityId), CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("FAC-CLIENT", result.FacilityCode);
        }
    }

    [Fact]
    public async Task GetFacilityById_ForClientUser_WhenUnauthorizedOrRevoked_ReturnsNull()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var clientUser = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            TenantId = clientId,
            UserType = UserType.Client,
            PermissionsList = [Permissions.Facilities.View]
        };

        var fac = new Facility { Id = facilityId, FacilityCode = "FAC-UNAUTH", Name = "Unauthorized Hub", IsActive = true };

        using (var seedContext = TestDbContextFactory.Create(new TestTenantProvider { TenantId = clientId }, clientUser, dbName))
        {
            seedContext.Facilities.Add(fac);
            seedContext.ClientFacilityAccesses.Add(new ClientFacilityAccess
            {
                ClientId = clientId,
                FacilityId = facilityId,
                Status = FacilityAccessStatus.Revoked // Revoked access
            });
            await seedContext.SaveChangesAsync();
        }

        using (var testContext = TestDbContextFactory.Create(new TestTenantProvider { TenantId = clientId }, clientUser, dbName))
        {
            var handler = new GetFacilityByIdQueryHandler(testContext, clientUser);

            // Act
            var result = await handler.Handle(new GetFacilityByIdQuery(facilityId), CancellationToken.None);

            // Assert
            Assert.Null(result);
        }
    }

    [Fact]
    public async Task GetFacilityById_WhenClientUserHoldsCrossFacilityGrant_CannotAccessUnauthorizedFacility()
    {
        // Arrange - Client user who holds cross-facility permission attempts to lookup unauthorized facility
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var foreignFacilityId = Guid.NewGuid();
        var clientUser = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            TenantId = clientId,
            UserType = UserType.Client,
            PermissionsList = [Permissions.Facilities.View, Permissions.Analytics.MultiFacilityView]
        };

        var fac = new Facility { Id = foreignFacilityId, FacilityCode = "FAC-FOREIGN", Name = "Foreign Hub", IsActive = true };

        using (var seedContext = TestDbContextFactory.Create(new TestTenantProvider { TenantId = clientId }, clientUser, dbName))
        {
            seedContext.Facilities.Add(fac);
            await seedContext.SaveChangesAsync();
        }

        using (var testContext = TestDbContextFactory.Create(new TestTenantProvider { TenantId = clientId }, clientUser, dbName))
        {
            var handler = new GetFacilityByIdQueryHandler(testContext, clientUser);

            // Act: Lookup foreign facility
            var result = await handler.Handle(new GetFacilityByIdQuery(foreignFacilityId), CancellationToken.None);

            // Assert: Must fail closed (null), cross-facility grant cannot bypass client tenant confinement
            Assert.Null(result);
        }
    }

    [Fact]
    public async Task GetFacilityById_WhenDriverHoldsCrossFacilityGrant_FailsClosedReturnsNull()
    {
        // Arrange - Driver who holds cross-facility permission attempts to lookup facility
        var dbName = Guid.NewGuid().ToString();
        var facilityId = Guid.NewGuid();
        var driverUser = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            TenantId = null,
            UserType = UserType.Driver,
            PermissionsList = [Permissions.Facilities.View, Permissions.Analytics.MultiFacilityView]
        };

        var fac = new Facility { Id = facilityId, FacilityCode = "FAC-CHI", Name = "Chicago Hub", IsActive = true };

        using (var seedContext = TestDbContextFactory.Create(new TestTenantProvider(), driverUser, dbName))
        {
            seedContext.Facilities.Add(fac);
            await seedContext.SaveChangesAsync();
        }

        using (var testContext = TestDbContextFactory.Create(new TestTenantProvider(), driverUser, dbName))
        {
            var handler = new GetFacilityByIdQueryHandler(testContext, driverUser);

            // Act
            var result = await handler.Handle(new GetFacilityByIdQuery(facilityId), CancellationToken.None);

            // Assert: Must fail closed
            Assert.Null(result);
        }
    }
}

