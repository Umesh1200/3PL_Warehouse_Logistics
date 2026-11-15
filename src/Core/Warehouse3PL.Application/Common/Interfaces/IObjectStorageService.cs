namespace Warehouse3PL.Application.Common.Interfaces;

/// <summary>
/// Authoritative private object storage service abstraction for shipment evidence (POD signatures, photos, documents).
/// Operates outside database transactions using a staged temporary-to-finalized workflow with streaming SHA-256 verification.
/// DB-SPEC-3PL-2026 Phase 4.5 Shipment Tracking & Delivery Confirmation.
/// </summary>
public interface IObjectStorageService
{
    /// <summary>
    /// Streams content to private temporary storage, validating magic bytes, size limits, and calculating streaming SHA-256.
    /// </summary>
    Task<StagedUploadResult> UploadStagedAsync(
        string tempObjectKey,
        Stream contentStream,
        string contentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finalizes a temporary staged object by promoting it to its authoritative final storage key.
    /// </summary>
    Task<ObjectStorageMetadata> FinalizeObjectAsync(
        string tempObjectKey,
        string finalObjectKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a readable stream for a private object. Returns null if not found.
    /// </summary>
    Task<Stream?> DownloadAsync(
        string objectKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a time-limited pre-signed download URL with cryptographically signed token.
    /// Clamps expiry to [1, 60] minutes, defaulting to 15 minutes. Never exposes public endpoints.
    /// </summary>
    Task<PreSignedUrlResult> GeneratePreSignedDownloadUrlAsync(
        string objectKey,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates an incoming pre-signed token against the object key and current UTC time.
    /// </summary>
    bool ValidatePreSignedToken(
        string objectKey,
        string token,
        DateTime utcNow);

    /// <summary>
    /// Deletes an object (temporary or final) from storage.
    /// </summary>
    Task<bool> DeleteAsync(
        string objectKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if an object exists in storage.
    /// </summary>
    Task<bool> ExistsAsync(
        string objectKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves metadata for an object in storage.
    /// </summary>
    Task<ObjectStorageMetadata?> GetMetadataAsync(
        string objectKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Purges temporary staged objects older than the specified threshold.
    /// </summary>
    Task<int> CleanupOrphanedTempObjectsAsync(
        TimeSpan olderThan,
        CancellationToken cancellationToken = default);
}

public class StagedUploadResult
{
    public string TempObjectKey { get; set; } = string.Empty;
    public string Sha256Hash { get; set; } = string.Empty;
    public long ContentLengthBytes { get; set; }
    public string ContentType { get; set; } = string.Empty;
}

public class ObjectStorageMetadata
{
    public string ObjectKey { get; set; } = string.Empty;
    public string Sha256Hash { get; set; } = string.Empty;
    public long ContentLengthBytes { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

public class PreSignedUrlResult
{
    public string ObjectKey { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
}

