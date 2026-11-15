using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Features.Auth.Commands.Login;
using Warehouse3PL.Application.Features.Auth.Commands.RefreshToken;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Authentication;
using Warehouse3PL.UnitTests.Common;
using Xunit;

namespace Warehouse3PL.UnitTests.Authentication;

public class AuthenticationSecurityVerificationTests
{
    private class TestDateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow { get; set; } = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsTokensAndUserDto()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var hasher = new PasswordHasher();
        var dt = new TestDateTimeProvider();
        var jwtOptions = Options.Create(new JwtOptions
        {
            SecretKey = "SuperSecureKeyForSecurityTestingPhase15!",
            Issuer = "test-issuer",
            Audience = "test-audience",
            ExpiryMinutes = 60
        });
        var tokenService = new JwtTokenService(jwtOptions, dt);

        var tenantId = Guid.NewGuid();
        var passwordHash = hasher.HashPassword("CorrectPassword123!");

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var user = new User
            {
                Email = "operator@apex.com",
                PasswordHash = passwordHash,
                FirstName = "John",
                LastName = "Doe",
                Status = UserStatus.Active,
                ClientId = tenantId
            };
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        // Act
        using (var loginContext = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var handler = new LoginCommandHandler(loginContext, hasher, tokenService, dt);
            var response = await handler.Handle(new LoginCommand
            {
                Email = "operator@apex.com",
                Password = "CorrectPassword123!"
            }, CancellationToken.None);

            // Assert
            Assert.NotNull(response);
            Assert.NotEmpty(response.AccessToken);
            Assert.NotEmpty(response.RefreshToken);
            Assert.Equal("operator@apex.com", response.User.Email);
        }
    }

    [Fact]
    public async Task Login_WithInvalidPassword_ThrowsUnauthorizedException()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var hasher = new PasswordHasher();
        var dt = new TestDateTimeProvider();
        var jwtOptions = Options.Create(new JwtOptions { SecretKey = "Key12345678901234567890123456789012!" });
        var tokenService = new JwtTokenService(jwtOptions, dt);

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            context.Users.Add(new User
            {
                Email = "user@test.com",
                PasswordHash = hasher.HashPassword("RealPassword!"),
                Status = UserStatus.Active
            });
            await context.SaveChangesAsync();
        }

        // Act & Assert
        using (var loginContext = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var handler = new LoginCommandHandler(loginContext, hasher, tokenService, dt);
            await Assert.ThrowsAsync<UnauthorizedException>(async () =>
            {
                await handler.Handle(new LoginCommand
                {
                    Email = "user@test.com",
                    Password = "WrongPassword!"
                }, CancellationToken.None);
            });
        }
    }

    [Fact]
    public async Task Login_WhenAccountIsLocked_ThrowsForbiddenException()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var hasher = new PasswordHasher();
        var dt = new TestDateTimeProvider();
        var jwtOptions = Options.Create(new JwtOptions { SecretKey = "Key12345678901234567890123456789012!" });
        var tokenService = new JwtTokenService(jwtOptions, dt);

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            context.Users.Add(new User
            {
                Email = "locked@test.com",
                PasswordHash = hasher.HashPassword("RealPassword!"),
                Status = UserStatus.Locked
            });
            await context.SaveChangesAsync();
        }

        // Act & Assert
        using (var loginContext = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var handler = new LoginCommandHandler(loginContext, hasher, tokenService, dt);
            await Assert.ThrowsAsync<ForbiddenException>(async () =>
            {
                await handler.Handle(new LoginCommand
                {
                    Email = "locked@test.com",
                    Password = "RealPassword!"
                }, CancellationToken.None);
            });
        }
    }

    [Fact]
    public void TokenValidation_WithTamperedSignature_ShouldFailValidation()
    {
        // Arrange
        const string keyA = "Key12345678901234567890123456789012!";
        const string keyB = "CompletelyDifferentSigningKey1234567!";
        var dt = new TestDateTimeProvider();

        var tokenService = new JwtTokenService(Options.Create(new JwtOptions { SecretKey = keyA }), dt);
        var authResponse = tokenService.GenerateTokens(
            new User { Id = Guid.NewGuid(), Email = "user@test.com" },
            ["ClientUser"],
            ["inventory:view"]);

        // Validate using key B (simulating forged/tampered token)
        var validator = new JwtSecurityTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyB))
        };

        // Act & Assert
        Assert.ThrowsAny<SecurityTokenException>(() =>
        {
            validator.ValidateToken(authResponse.AccessToken, validationParameters, out _);
        });
    }

    [Fact]
    public async Task RefreshToken_WhenExpired_ThrowsUnauthorizedException()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var dt = new TestDateTimeProvider { UtcNow = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc) };
        var jwtOptions = Options.Create(new JwtOptions { SecretKey = "Key12345678901234567890123456789012!" });
        var tokenService = new JwtTokenService(jwtOptions, dt);

        var userId = Guid.NewGuid();
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            context.Users.Add(new User
            {
                Id = userId,
                Email = "refreshtest@test.com",
                Status = UserStatus.Active,
                RefreshToken = "valid_refresh_token_string",
                RefreshTokenExpiryTime = dt.UtcNow.AddMinutes(-5) // Expired 5 minutes ago!
            });
            await context.SaveChangesAsync();
        }

        var authResponse = tokenService.GenerateTokens(
            new User { Id = userId, Email = "refreshtest@test.com" },
            ["ClientUser"],
            []);

        // Act & Assert
        using (var refreshContext = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var handler = new RefreshTokenCommandHandler(refreshContext, tokenService, dt);
            await Assert.ThrowsAsync<UnauthorizedException>(async () =>
            {
                await handler.Handle(new RefreshTokenCommand
                {
                    AccessToken = authResponse.AccessToken,
                    RefreshToken = "valid_refresh_token_string"
                }, CancellationToken.None);
            });
        }
    }
}

