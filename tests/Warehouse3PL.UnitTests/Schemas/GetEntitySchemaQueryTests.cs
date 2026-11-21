using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Features.Schemas.Queries.GetEntitySchema;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Xunit;

namespace Warehouse3PL.UnitTests.Schemas;

public class TestCurrentUserContext : ICurrentUserContext
{
    public Guid? UserId { get; set; } = Guid.NewGuid();
    public string? Email { get; set; } = "test@nexus3pl.local";
    public Guid? TenantId { get; set; }
    public Guid? FacilityId { get; set; }
    public Guid? RequestedFacilityId { get; set; }
    public UserType? UserType { get; set; } = Warehouse3PL.Domain.Enums.UserType.Staff;
    public IReadOnlyList<string> Roles { get; set; } = new List<string> { "SuperAdmin" };
    public HashSet<string> GrantedPermissions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> Permissions => GrantedPermissions.ToList();
    public bool IsAuthenticated { get; set; } = true;
    public bool IsStaff { get; set; } = true;
    public bool IsSuperAdmin { get; set; } = false;

    public bool HasPermission(string permission) => GrantedPermissions.Contains(permission);
    public bool IsInRole(string role) => Roles.Contains(role);
}

public class GetEntitySchemaQueryTests
{
    private readonly TestCurrentUserContext _currentUser;

    public GetEntitySchemaQueryTests()
    {
        _currentUser = new TestCurrentUserContext();
    }

    [Fact]
    public async Task Handle_WhenCallerHasViewPermission_ReturnsAuthoritativeSchema()
    {
        // Arrange
        _currentUser.GrantedPermissions.Add(Permissions.Clients.View);
        _currentUser.GrantedPermissions.Add(Permissions.Clients.Create);
        _currentUser.GrantedPermissions.Add(Permissions.Clients.Edit);
        _currentUser.GrantedPermissions.Add(Permissions.Clients.Delete);

        var handler = new GetEntitySchemaQueryHandler(_currentUser);

        // Act
        var result = await handler.Handle(new GetEntitySchemaQuery("Client"), CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Client", result.Name);
        Assert.Equal("1.0.0", result.Version);
        Assert.Equal("Client Tenant", result.Title);
        Assert.Equal("id", result.PrimaryKey);
        Assert.NotEmpty(result.Fields);
        Assert.Contains(result.Fields, f => f.Name == "accountCode");
        Assert.Contains(result.Fields, f => f.Name == "companyName");
        Assert.Contains(result.Fields, f => f.Name == "slaTier");
        Assert.NotEmpty(result.Views.List.Columns);
        Assert.Contains(result.Actions, a => a.Id == "create");
        Assert.Contains(result.Actions, a => a.Id == "edit");
    }

    [Fact]
    public async Task Handle_WhenCallerLacksViewPermission_ThrowsUnauthorizedAccessException()
    {
        // Arrange: View permission omitted
        var handler = new GetEntitySchemaQueryHandler(_currentUser);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new GetEntitySchemaQuery("Client"), CancellationToken.None));
        Assert.Contains("clients:view", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_WhenCallerLacksCreatePermission_PrunesCreateActionFromSchema()
    {
        // Arrange: View and Edit granted, but Create and Delete omitted
        _currentUser.GrantedPermissions.Add(Permissions.Clients.View);
        _currentUser.GrantedPermissions.Add(Permissions.Clients.Edit);

        var handler = new GetEntitySchemaQueryHandler(_currentUser);

        // Act
        var result = await handler.Handle(new GetEntitySchemaQuery("Client"), CancellationToken.None);

        // Assert: Create and deactivate actions pruned on server side
        Assert.NotNull(result);
        Assert.DoesNotContain(result.Actions, a => a.Id == "create");
        Assert.Contains(result.Actions, a => a.Id == "edit");
        Assert.DoesNotContain(result.Actions, a => a.Id == "deactivate");
    }

    [Fact]
    public async Task Handle_WhenSchemaDoesNotExist_ThrowsEntityNotFoundException()
    {
        // Arrange
        _currentUser.GrantedPermissions.Add(Permissions.Clients.View);
        var handler = new GetEntitySchemaQueryHandler(_currentUser);

        // Act & Assert
        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            handler.Handle(new GetEntitySchemaQuery("NonExistentEntity_XYZ"), CancellationToken.None));
    }
}
