using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Authentication;
using Xunit;

namespace Warehouse3PL.UnitTests.Authentication;

public class JwtTokenServiceTests
{
    private class FakeDateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow => new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
    }

    [Fact]
    public void GenerateTokens_ShouldContainTenantAndRoleClaims()
    {
        // Arrange
        var options = Options.Create(new JwtOptions
        {
            SecretKey = "SuperSecretKeyForTestingPurposesOnly2026!",
            Issuer = "test-issuer",
            Audience = "test-audience",
            ExpiryMinutes = 60
        });

        var tokenService = new JwtTokenService(options, new FakeDateTimeProvider());

        var tenantId = Guid.NewGuid();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "operator@apexlogistics.com",
            FirstName = "Warehouse",
            LastName = "User",
            UserType = UserType.Client,
            ClientId = tenantId
        };

        var roles = new[] { "WarehouseOperator", "ClientUser" };
        var permissions = new[] { "inventory:view", "inventory:adjust", "outbound:pick" };

        // Act
        var authResponse = tokenService.GenerateTokens(user, roles, permissions);

        // Assert
        Assert.NotNull(authResponse);
        Assert.NotEmpty(authResponse.AccessToken);
        Assert.NotEmpty(authResponse.RefreshToken);
        Assert.Equal("Bearer", authResponse.TokenType);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(authResponse.AccessToken);

        Assert.Equal(user.Id.ToString(), jwt.Subject);
        Assert.Equal(user.Email, jwt.Claims.First(c => c.Type == "email").Value);
        Assert.Equal(tenantId.ToString(), jwt.Claims.First(c => c.Type == "tenant_id").Value);
        Assert.Equal(tenantId.ToString(), jwt.Claims.First(c => c.Type == "client_id").Value);

        var roleClaims = jwt.Claims.Where(c => c.Type == ClaimTypes.Role || c.Type == "roles").Select(c => c.Value).Distinct().ToList();
        Assert.Contains("WarehouseOperator", roleClaims);
        Assert.Contains("ClientUser", roleClaims);

        var permissionClaims = jwt.Claims.Where(c => c.Type == "permissions").Select(c => c.Value).ToList();
        Assert.Contains("inventory:view", permissionClaims);
        Assert.Contains("inventory:adjust", permissionClaims);
        Assert.Contains("outbound:pick", permissionClaims);
    }
}

