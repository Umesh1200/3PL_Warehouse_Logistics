namespace Warehouse3PL.Infrastructure.Services.Storage;

public class ObjectStorageOptions
{
    public const string SectionName = "ObjectStorage";

    /// <summary>
    /// Base private directory for storing evidence objects.
    /// Default: AppData or local execution directory.
    /// </summary>
    public string? BaseDirectory { get; set; }

    /// <summary>
    /// Cryptographic signing secret key for pre-signed URLs.
    /// </summary>
    public string SigningSecretKey { get; set; } = "3PL-Warehouse-Logistics-Default-Secure-Storage-Key-2026-Secret!";

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
}

