namespace Warehouse3PL.Infrastructure.Services.Storage;

public class ObjectStorageOptions
{
    public const string SectionName = "ObjectStorage";
    public const int MinimumSigningKeyLength = 32;
    public const string RetiredLegacyDefaultKey = "3PL-Warehouse-Logistics-Default-Secure-Storage-Key-2026-Secret!";

    /// <summary>
    /// Base private directory for storing evidence objects.
    /// Default: AppData or local execution directory.
    /// </summary>
    public string? BaseDirectory { get; set; }

    /// <summary>
    /// Cryptographic signing secret key for pre-signed URLs.
    /// Must be explicitly configured via environment/secrets in non-Development environments (min 32 chars).
    /// </summary>
    public string SigningSecretKey { get; set; } = string.Empty;

    /// <summary>
    /// Default pre-signed URL expiry in minutes. Default: 15.
    /// </summary>
    public int DefaultExpiryMinutes { get; set; } = 15;

    /// <summary>
    /// Minimum allowed expiry in minutes. Default: 1.
    /// </summary>
    public int MinExpiryMinutes { get; set; } = 1;

    /// <summary>
    /// Maximum allowed expiry in minutes. Default: 60.
    /// </summary>
    public int MaxExpiryMinutes { get; set; } = 60;

    /// <summary>
    /// Maximum allowed file upload size in bytes. Default: 10 MB.
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Validates that a configured signing key is non-empty, meets minimum length,
    /// contains sufficient character diversity, and is not the retired legacy default key.
    /// </summary>
    public static bool IsStrongSigningKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var trimmed = key.Trim();
        if (trimmed.Length < MinimumSigningKeyLength)
        {
            return false;
        }

        if (string.Equals(trimmed, RetiredLegacyDefaultKey, StringComparison.Ordinal))
        {
            return false;
        }

        return trimmed.Distinct().Count() >= 4;
    }
}
