using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Infrastructure.Authentication;

public class JwtTokenService : ITokenService
{
    private readonly JwtOptions _options;
    private readonly IDateTimeProvider _dateTime;

    public JwtTokenService(IOptions<JwtOptions> options, IDateTimeProvider dateTime)
    {
        _options = options.Value;
        _dateTime = dateTime;
    }

    public AuthResponseDto GenerateTokens(User user, IEnumerable<string> roles, IEnumerable<string> permissions)
    {
        var roleList = roles.ToList();
        var permissionList = permissions.ToList();

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("user_type", user.UserType.ToString()),
            new("first_name", user.FirstName),
            new("last_name", user.LastName)
        };

        if (user.ClientId.HasValue)
        {
            claims.Add(new Claim("tenant_id", user.ClientId.Value.ToString()));
            claims.Add(new Claim("client_id", user.ClientId.Value.ToString()));
        }

        if (user.UserType == UserType.Staff &&
            user.AssignedFacilityId.HasValue &&
            user.AssignedFacilityId.Value != Guid.Empty)
        {
            claims.Add(new Claim("facility_id", user.AssignedFacilityId.Value.ToString()));
        }

        foreach (var role in roleList)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
            claims.Add(new Claim("roles", role));
        }

        foreach (var permission in permissionList)
        {
            claims.Add(new Claim("permissions", permission));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SecretKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var now = _dateTime.UtcNow;
        var expires = now.AddMinutes(_options.ExpiryMinutes);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            NotBefore = now,
            IssuedAt = now,
            Expires = expires,
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            SigningCredentials = creds
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        var accessToken = tokenHandler.WriteToken(token);
        var refreshToken = GenerateRefreshToken();

        return new AuthResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            TokenType = "Bearer",
            ExpiresIn = _options.ExpiryMinutes * 60,
            User = new UserDto
            {
                Id = user.Id,
                ClientId = user.ClientId,
                AssignedFacilityId = user.UserType == UserType.Staff ? user.AssignedFacilityId : null,
                CompanyName = user.Client?.CompanyName,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                PhoneNumber = user.PhoneNumber,
                UserType = user.UserType,
                Status = user.Status,
                CreatedAt = user.CreatedAt,
                Roles = roleList
            }
        };
    }

    public string GenerateRefreshToken()
    {
        var randomNumber = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        return Convert.ToBase64String(randomNumber);
    }

    public ClaimsPrincipal? GetPrincipalFromExpiredToken(string token)
    {
        var tokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = false,
            ValidateIssuer = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SecretKey)),
            ValidateLifetime = false // Here we don't care about token's expiration date
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        try
        {
            var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out var securityToken);
            if (securityToken is not JwtSecurityToken jwtSecurityToken ||
                !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
            {
                return null;
            }

            return principal;
        }
        catch
        {
            return null;
        }
    }
}

