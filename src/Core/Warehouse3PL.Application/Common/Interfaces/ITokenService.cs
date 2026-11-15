using System.Security.Claims;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Security;

namespace Warehouse3PL.Application.Common.Interfaces;

public interface ITokenService
{
    AuthResponseDto GenerateTokens(User user, IEnumerable<string> roles, IEnumerable<string> permissions);
    string GenerateRefreshToken();
    ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);
}

