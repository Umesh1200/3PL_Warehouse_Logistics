using Warehouse3PL.Infrastructure.Authentication;
using Xunit;

namespace Warehouse3PL.UnitTests.Authentication;

public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void HashPassword_ShouldReturnValidBcryptHash()
    {
        // Arrange
        const string password = "SecurePassword123!";

        // Act
        var hash = _hasher.HashPassword(password);

        // Assert
        Assert.NotNull(hash);
        Assert.StartsWith("$2", hash); // BCrypt prefix
    }

    [Fact]
    public void VerifyPassword_WithCorrectPassword_ShouldReturnTrue()
    {
        // Arrange
        const string password = "CorrectPassword123!";
        var hash = _hasher.HashPassword(password);

        // Act
        var result = _hasher.VerifyPassword(password, hash);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void VerifyPassword_WithIncorrectPassword_ShouldReturnFalse()
    {
        // Arrange
        const string password = "CorrectPassword123!";
        var hash = _hasher.HashPassword(password);

        // Act
        var result = _hasher.VerifyPassword("WrongPassword!", hash);

        // Assert
        Assert.False(result);
    }
}

