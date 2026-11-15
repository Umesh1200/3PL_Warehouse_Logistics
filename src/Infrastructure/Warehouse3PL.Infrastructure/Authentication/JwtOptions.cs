namespace Warehouse3PL.Infrastructure.Authentication;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "https://auth.3pllogistics.com";
    public string Audience { get; set; } = "3pl-warehouse-api";
    public string SecretKey { get; set; } = "EnterpriseWarehouse3PLPlatformSuperSecretMasterSigningKey2026!";
    public int ExpiryMinutes { get; set; } = 120;
    public int RefreshTokenExpiryDays { get; set; } = 7;

    // Keycloak integration options
    public bool UseKeycloak { get; set; } = false;
    public string? KeycloakAuthority { get; set; }
    public string? KeycloakMetadataAddress { get; set; }
    public bool RequireHttpsMetadata { get; set; } = false;
}

