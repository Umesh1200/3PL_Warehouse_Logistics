namespace Warehouse3PL.Application.Common.Models;

public record AuthResponseDto
{
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public string TokenType { get; init; } = "Bearer";
    public int ExpiresIn { get; init; }
    public UserDto User { get; init; } = null!;
}

