using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services.Storage;

/// <summary>
/// Authoritative private object storage service for shipment evidence.
/// Enforces private container access, staged temporary-to-final promotion, streaming SHA-256 calculation,
/// file signature magic-byte verification, and HMAC-signed time-limited download URLs.
/// DB-SPEC-3PL-2026 Phase 4.5 Shipment Tracking & Delivery Confirmation; hardened in MDM-0.2.
/// </summary>
public class PrivateObjectStorageService : IObjectStorageService
{
    private readonly ObjectStorageOptions _options;
    private readonly ILogger<PrivateObjectStorageService> _logger;
    private readonly string _baseDirectory;
    private readonly string _tempRootWithSep;
    private readonly string _finalRootWithSep;
    private readonly byte[]? _signingKeyBytes;

    public PrivateObjectStorageService(
        IOptions<ObjectStorageOptions> options,
        ILogger<PrivateObjectStorageService> logger,
        IHostEnvironment? hostEnvironment = null)
    {
        _options = options.Value;
        _logger = logger;

        _baseDirectory = !string.IsNullOrWhiteSpace(_options.BaseDirectory)
            ? Path.GetFullPath(_options.BaseDirectory)
            : Path.Combine(AppContext.BaseDirectory, "storage", "private");

        var tempRoot = Path.Combine(_baseDirectory, "temp");
        var finalRoot = Path.Combine(_baseDirectory, "final");
        Directory.CreateDirectory(tempRoot);
        Directory.CreateDirectory(finalRoot);
        _tempRootWithSep = EnsureTrailingSeparator(Path.GetFullPath(tempRoot));
        _finalRootWithSep = EnsureTrailingSeparator(Path.GetFullPath(finalRoot));

        _signingKeyBytes = ResolveSigningKeyBytes(_options.SigningSecretKey, hostEnvironment, _logger);
    }

    public async Task<StagedUploadResult> UploadStagedAsync(
        string tempObjectKey,
        Stream contentStream,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tempObjectKey))
        {
            throw new ValidationException(nameof(tempObjectKey), "Temporary object key must be provided.");
        }

        if (contentStream == null || !contentStream.CanRead)
        {
            throw new ValidationException(nameof(contentStream), "Readable content stream is required.");
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new ValidationException(nameof(contentType), "Content type is required.");
        }

        var normalizedKey = SanitizeObjectKey(tempObjectKey);
        var normalizedContentType = contentType.Trim().ToLowerInvariant();

        var tempFilePath = GetTempFilePath(normalizedKey);
        var tempDirectory = Path.GetDirectoryName(tempFilePath);
        if (!string.IsNullOrEmpty(tempDirectory))
        {
            Directory.CreateDirectory(tempDirectory);
        }

        // 1. Read and validate magic bytes for MIME integrity
        byte[] headerBuffer = new byte[16];
        int headerBytesRead = await contentStream.ReadAsync(headerBuffer.AsMemory(0, headerBuffer.Length), cancellationToken);

        if (headerBytesRead < 4)
        {
            throw new ValidationException(nameof(contentStream), "Uploaded file is too small or empty.");
        }

        ValidateFileSignature(headerBuffer, headerBytesRead, normalizedContentType);

        // 2. Stream data to disk while calculating SHA-256 incrementally
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long totalBytes = headerBytesRead;

        // Process header bytes in hash
        sha256.AppendData(headerBuffer, 0, headerBytesRead);

        await using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            // Write header bytes to file
            await fileStream.WriteAsync(headerBuffer.AsMemory(0, headerBytesRead), cancellationToken);

            byte[] buffer = new byte[81920];
            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                totalBytes += bytesRead;
                if (totalBytes > _options.MaxFileSizeBytes)
                {
                    // Clean up partially written file
                    fileStream.Close();
                    File.Delete(tempFilePath);
                    throw new ValidationException(nameof(contentStream),
                        $"File size exceeds maximum allowed limit of {_options.MaxFileSizeBytes / (1024 * 1024)} MB.");
                }

                sha256.AppendData(buffer, 0, bytesRead);
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            }
        }

        string hashHex = Convert.ToHexString(sha256.GetHashAndReset()).ToLowerInvariant();

        // 3. Write companion metadata file
        var meta = new ObjectStorageMetadata
        {
            ObjectKey = normalizedKey,
            Sha256Hash = hashHex,
            ContentLengthBytes = totalBytes,
            ContentType = normalizedContentType,
            CreatedAtUtc = DateTime.UtcNow
        };

        var metaPath = tempFilePath + ".meta.json";
        await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(meta), cancellationToken);

        _logger.LogInformation("Staged upload completed for key '{Key}', size {Size} bytes, SHA-256 {Hash}",
            normalizedKey, totalBytes, hashHex);

        return new StagedUploadResult
        {
            TempObjectKey = normalizedKey,
            Sha256Hash = hashHex,
            ContentLengthBytes = totalBytes,
            ContentType = normalizedContentType
        };
    }

    public async Task<ObjectStorageMetadata> FinalizeObjectAsync(
        string tempObjectKey,
        string finalObjectKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tempObjectKey))
        {
            throw new ValidationException(nameof(tempObjectKey), "Temporary object key must be provided.");
        }

        if (string.IsNullOrWhiteSpace(finalObjectKey))
        {
            throw new ValidationException(nameof(finalObjectKey), "Final object key must be provided.");
        }

        var normalizedTempKey = SanitizeObjectKey(tempObjectKey);
        var normalizedFinalKey = SanitizeObjectKey(finalObjectKey);

        var tempFilePath = GetTempFilePath(normalizedTempKey);
        var tempMetaPath = tempFilePath + ".meta.json";

        if (!File.Exists(tempFilePath))
        {
            throw new EntityNotFoundException("StagedTempObject", normalizedTempKey);
        }

        var finalFilePath = GetFinalFilePath(normalizedFinalKey);
        var finalDirectory = Path.GetDirectoryName(finalFilePath);
        if (!string.IsNullOrEmpty(finalDirectory))
        {
            Directory.CreateDirectory(finalDirectory);
        }

        ObjectStorageMetadata metadata;
        if (File.Exists(tempMetaPath))
        {
            var metaJson = await File.ReadAllTextAsync(tempMetaPath, cancellationToken);
            metadata = JsonSerializer.Deserialize<ObjectStorageMetadata>(metaJson) ?? new ObjectStorageMetadata();
        }
        else
        {
            // Fallback: Compute hash and length from file if metadata file was missing
            using var sha = SHA256.Create();
            await using var fs = File.OpenRead(tempFilePath);
            var hashBytes = await sha.ComputeHashAsync(fs, cancellationToken);
            metadata = new ObjectStorageMetadata
            {
                ObjectKey = normalizedFinalKey,
                Sha256Hash = Convert.ToHexString(hashBytes).ToLowerInvariant(),
                ContentLengthBytes = new FileInfo(tempFilePath).Length,
                ContentType = "application/octet-stream",
                CreatedAtUtc = DateTime.UtcNow
            };
        }

        metadata.ObjectKey = normalizedFinalKey;

        // Atomically move staged file to finalized location
        File.Move(tempFilePath, finalFilePath, overwrite: true);

        // Save finalized metadata companion
        var finalMetaPath = finalFilePath + ".meta.json";
        await File.WriteAllTextAsync(finalMetaPath, JsonSerializer.Serialize(metadata), cancellationToken);

        // Clean up temp metadata
        if (File.Exists(tempMetaPath))
        {
            File.Delete(tempMetaPath);
        }

        _logger.LogInformation("Finalized object from temp key '{TempKey}' to final key '{FinalKey}'",
            normalizedTempKey, normalizedFinalKey);

        return metadata;
    }

    public Task<Stream?> DownloadAsync(
        string objectKey,
        CancellationToken cancellationToken = default) =>
        DownloadInternalAsync(objectKey, allowTempFallback: true);

    public Task<Stream?> DownloadFinalizedAsync(
        string objectKey,
        CancellationToken cancellationToken = default) =>
        DownloadInternalAsync(objectKey, allowTempFallback: false);

    private Task<Stream?> DownloadInternalAsync(string objectKey, bool allowTempFallback)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            throw new ValidationException(nameof(objectKey), "Object key must be provided.");
        }

        var normalizedKey = SanitizeObjectKey(objectKey);
        var finalPath = GetFinalFilePath(normalizedKey);

        if (File.Exists(finalPath))
        {
            Stream stream = new FileStream(finalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Task.FromResult<Stream?>(stream);
        }

        if (allowTempFallback)
        {
            var tempPath = GetTempFilePath(normalizedKey);
            if (File.Exists(tempPath))
            {
                Stream stream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return Task.FromResult<Stream?>(stream);
            }
        }

        return Task.FromResult<Stream?>(null);
    }

    public Task<PreSignedUrlResult> GeneratePreSignedDownloadUrlAsync(
        string objectKey,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            throw new ValidationException(nameof(objectKey), "Object key must be provided.");
        }

        var normalizedKey = SanitizeObjectKey(objectKey);

        // Clamp expiry strictly between MinExpiryMinutes and MaxExpiryMinutes
        double requestedMinutes = expiry?.TotalMinutes ?? _options.DefaultExpiryMinutes;
        if (requestedMinutes < _options.MinExpiryMinutes)
        {
            requestedMinutes = _options.MinExpiryMinutes;
        }
        else if (requestedMinutes > _options.MaxExpiryMinutes)
        {
            requestedMinutes = _options.MaxExpiryMinutes;
        }

        var expiresAtUtc = DateTime.UtcNow.AddMinutes(requestedMinutes);
        string expiryString = expiresAtUtc.ToString("o");

        if (_signingKeyBytes == null || _signingKeyBytes.Length == 0)
        {
            throw new InvalidOperationException(
                "ObjectStorage:SigningSecretKey is not configured. Pre-signed URL generation requires a configured strong signing key.");
        }

        // Generate HMAC-SHA256 signature
        string payloadToSign = $"GET\n{normalizedKey}\n{expiryString}";
        using var hmac = new HMACSHA256(_signingKeyBytes);
        byte[] signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadToSign));
        string signatureHex = Convert.ToHexString(signatureBytes).ToLowerInvariant();

        string rawToken = $"{expiryString}:{signatureHex}";
        string tokenBase64Url = Convert.ToBase64String(Encoding.UTF8.GetBytes(rawToken))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        string downloadUrl = $"/api/v1/storage/download?key={Uri.EscapeDataString(normalizedKey)}&token={tokenBase64Url}";

        return Task.FromResult(new PreSignedUrlResult
        {
            ObjectKey = normalizedKey,
            DownloadUrl = downloadUrl,
            Token = tokenBase64Url,
            ExpiresAtUtc = expiresAtUtc
        });
    }

    public bool ValidatePreSignedToken(string objectKey, string token, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(objectKey) || string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        if (_signingKeyBytes == null || _signingKeyBytes.Length == 0)
        {
            _logger.LogError("Rejected pre-signed token validation because ObjectStorage:SigningSecretKey is not configured.");
            return false;
        }

        string normalizedKey;
        try
        {
            normalizedKey = SanitizeObjectKey(objectKey);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning(ex, "Rejected pre-signed token validation due to unsafe object key.");
            return false;
        }

        try
        {
            // Decode base64url token
            string padded = token.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
            }

            string rawToken = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            int colonIndex = rawToken.LastIndexOf(':');
            if (colonIndex <= 0)
            {
                return false;
            }

            string expiryString = rawToken[..colonIndex];
            string providedSignatureHex = rawToken[(colonIndex + 1)..];
            if (providedSignatureHex.Length != 64)
            {
                return false;
            }

            if (!DateTime.TryParse(
                    expiryString,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var expiresAtUtc) ||
                expiresAtUtc.Kind != DateTimeKind.Utc)
            {
                return false;
            }

            var utcNowNormalized = utcNow.Kind == DateTimeKind.Utc
                ? utcNow
                : utcNow.ToUniversalTime();

            if (utcNowNormalized > expiresAtUtc)
            {
                _logger.LogWarning("Pre-signed token expired at '{Expiry}', current time '{Now}'", expiresAtUtc, utcNowNormalized);
                return false;
            }

            byte[] providedHash = Convert.FromHexString(providedSignatureHex);

            // Verify signature
            string payloadToSign = $"GET\n{normalizedKey}\n{expiryString}";
            using var hmac = new HMACSHA256(_signingKeyBytes);
            byte[] expectedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadToSign));

            return CryptographicOperations.FixedTimeEquals(expectedHash, providedHash);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate pre-signed token for key '{Key}'", normalizedKey);
            return false;
        }
    }

    public Task<bool> DeleteAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return Task.FromResult(false);
        }

        var normalizedKey = SanitizeObjectKey(objectKey);
        var finalPath = GetFinalFilePath(normalizedKey);
        var tempPath = GetTempFilePath(normalizedKey);

        bool deleted = false;
        if (File.Exists(finalPath))
        {
            File.Delete(finalPath);
            var finalMeta = finalPath + ".meta.json";
            if (File.Exists(finalMeta)) File.Delete(finalMeta);
            deleted = true;
        }

        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
            var tempMeta = tempPath + ".meta.json";
            if (File.Exists(tempMeta)) File.Delete(tempMeta);
            deleted = true;
        }

        return Task.FromResult(deleted);
    }

    public Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return Task.FromResult(false);
        }

        var normalizedKey = SanitizeObjectKey(objectKey);
        var finalPath = GetFinalFilePath(normalizedKey);
        var tempPath = GetTempFilePath(normalizedKey);

        return Task.FromResult(File.Exists(finalPath) || File.Exists(tempPath));
    }

    public Task<ObjectStorageMetadata?> GetMetadataAsync(string objectKey, CancellationToken cancellationToken = default) =>
        GetMetadataInternalAsync(objectKey, allowTempFallback: true, cancellationToken);

    public Task<ObjectStorageMetadata?> GetFinalizedMetadataAsync(string objectKey, CancellationToken cancellationToken = default) =>
        GetMetadataInternalAsync(objectKey, allowTempFallback: false, cancellationToken);

    private async Task<ObjectStorageMetadata?> GetMetadataInternalAsync(
        string objectKey,
        bool allowTempFallback,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return null;
        }

        var normalizedKey = SanitizeObjectKey(objectKey);
        var finalMetaPath = GetFinalFilePath(normalizedKey) + ".meta.json";

        if (File.Exists(finalMetaPath))
        {
            var json = await File.ReadAllTextAsync(finalMetaPath, cancellationToken);
            return JsonSerializer.Deserialize<ObjectStorageMetadata>(json);
        }

        if (allowTempFallback)
        {
            var tempMetaPath = GetTempFilePath(normalizedKey) + ".meta.json";
            if (File.Exists(tempMetaPath))
            {
                var json = await File.ReadAllTextAsync(tempMetaPath, cancellationToken);
                return JsonSerializer.Deserialize<ObjectStorageMetadata>(json);
            }
        }

        // If finalized file exists without sidecar metadata
        var finalPath = GetFinalFilePath(normalizedKey);
        if (File.Exists(finalPath))
        {
            var fi = new FileInfo(finalPath);
            return new ObjectStorageMetadata
            {
                ObjectKey = normalizedKey,
                ContentLengthBytes = fi.Length,
                ContentType = "application/octet-stream",
                CreatedAtUtc = fi.CreationTimeUtc
            };
        }

        return null;
    }

    public Task<int> CleanupOrphanedTempObjectsAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        var tempRoot = Path.Combine(_baseDirectory, "temp");
        if (!Directory.Exists(tempRoot))
        {
            return Task.FromResult(0);
        }

        int count = 0;
        var thresholdUtc = DateTime.UtcNow - olderThan;

        foreach (var file in Directory.EnumerateFiles(tempRoot, "*", SearchOption.AllDirectories))
        {
            try
            {
                var fi = new FileInfo(file);
                if (fi.LastWriteTimeUtc < thresholdUtc)
                {
                    fi.Delete();
                    count++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete orphaned temp file '{Path}'", file);
            }
        }

        _logger.LogInformation("Cleaned up {Count} orphaned temp objects older than {Threshold}", count, thresholdUtc);
        return Task.FromResult(count);
    }

    #region Helpers & Validation

    private static byte[]? ResolveSigningKeyBytes(
        string? configuredKey,
        IHostEnvironment? hostEnvironment,
        ILogger logger)
    {
        var trimmed = configuredKey?.Trim() ?? string.Empty;
        if (string.Equals(trimmed, ObjectStorageOptions.RetiredLegacyDefaultKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "ObjectStorage:SigningSecretKey cannot use the retired legacy default key. Provision a unique cryptographic secret.");
        }

        if (ObjectStorageOptions.IsStrongSigningKey(trimmed))
        {
            return Encoding.UTF8.GetBytes(trimmed);
        }

        if (hostEnvironment != null && !hostEnvironment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"ObjectStorage:SigningSecretKey must be explicitly configured with at least {ObjectStorageOptions.MinimumSigningKeyLength} characters in '{hostEnvironment.EnvironmentName}' environments.");
        }

        if (!string.IsNullOrEmpty(trimmed))
        {
            throw new InvalidOperationException(
                $"ObjectStorage:SigningSecretKey must be at least {ObjectStorageOptions.MinimumSigningKeyLength} characters with sufficient entropy.");
        }

        if (hostEnvironment != null && hostEnvironment.IsDevelopment())
        {
            logger.LogWarning(
                "ObjectStorage:SigningSecretKey is not configured in Development; generating ephemeral runtime key (pre-signed URLs will not survive process restarts).");
            return RandomNumberGenerator.GetBytes(32);
        }

        // Unconfigured direct unit-test instantiation without IHostEnvironment: permit upload-only operations
        // while failing closed on signing/validation calls.
        return null;
    }

    private static string EnsureTrailingSeparator(string fullPath) =>
        fullPath.EndsWith(Path.DirectorySeparatorChar)
            ? fullPath
            : fullPath + Path.DirectorySeparatorChar;

    private static string SanitizeObjectKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ValidationException(nameof(key), "Object key must be provided.");
        }

        if (key.Contains("..", StringComparison.Ordinal))
        {
            throw new ValidationException(nameof(key), "Path traversal characters ('..') are strictly prohibited.");
        }

        if (key.Contains('\\') || key.Contains(':') || key.IndexOf('\0') >= 0 || key.Any(char.IsControl))
        {
            throw new ValidationException(nameof(key), "Object key contains invalid path characters.");
        }

        var normalized = key.Trim().TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(nameof(key), "Object key is empty or references protected storage metadata.");
        }

        if (normalized.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            throw new ValidationException(nameof(key), "Object key contains invalid path characters.");
        }

        var segments = normalized.Split('/');
        if (segments.Any(s => string.IsNullOrWhiteSpace(s) || s == "." || s == ".."))
        {
            throw new ValidationException(nameof(key), "Object key contains invalid or relative path segments.");
        }

        return normalized;
    }

    private string GetTempFilePath(string sanitizedKey)
    {
        var full = Path.GetFullPath(Path.Combine(_baseDirectory, "temp", sanitizedKey));
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(_tempRootWithSep, cmp))
        {
            throw new ValidationException(nameof(sanitizedKey), "Resolved temporary storage path escapes root boundary.");
        }
        return full;
    }

    private string GetFinalFilePath(string sanitizedKey)
    {
        var full = Path.GetFullPath(Path.Combine(_baseDirectory, "final", sanitizedKey));
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(_finalRootWithSep, cmp))
        {
            throw new ValidationException(nameof(sanitizedKey), "Resolved final storage path escapes root boundary.");
        }
        return full;
    }

    private static void ValidateFileSignature(byte[] headerBuffer, int bytesRead, string contentType)
    {
        switch (contentType)
        {
            case "image/png":
                // PNG signature: 89 50 4E 47 0D 0A 1A 0A
                if (bytesRead < 8 ||
                    headerBuffer[0] != 0x89 || headerBuffer[1] != 0x50 ||
                    headerBuffer[2] != 0x4E || headerBuffer[3] != 0x47 ||
                    headerBuffer[4] != 0x0D || headerBuffer[5] != 0x0A ||
                    headerBuffer[6] != 0x1A || headerBuffer[7] != 0x0A)
                {
                    throw new ValidationException(nameof(contentType), "File content does not match PNG signature.");
                }
                break;

            case "image/jpeg":
            case "image/jpg":
                // JPEG signature: FF D8 FF
                if (bytesRead < 3 ||
                    headerBuffer[0] != 0xFF || headerBuffer[1] != 0xD8 || headerBuffer[2] != 0xFF)
                {
                    throw new ValidationException(nameof(contentType), "File content does not match JPEG signature.");
                }
                break;

            case "application/pdf":
                // PDF signature: 25 50 44 46 (%PDF)
                if (bytesRead < 4 ||
                    headerBuffer[0] != 0x25 || headerBuffer[1] != 0x50 ||
                    headerBuffer[2] != 0x44 || headerBuffer[3] != 0x46)
                {
                    throw new ValidationException(nameof(contentType), "File content does not match PDF signature.");
                }
                break;

            case "image/webp":
                // WEBP: RIFF....WEBP
                if (bytesRead < 12 ||
                    headerBuffer[0] != 0x52 || headerBuffer[1] != 0x49 ||
                    headerBuffer[2] != 0x46 || headerBuffer[3] != 0x46 ||
                    headerBuffer[8] != 0x57 || headerBuffer[9] != 0x45 ||
                    headerBuffer[10] != 0x42 || headerBuffer[11] != 0x50)
                {
                    throw new ValidationException(nameof(contentType), "File content does not match WebP signature.");
                }
                break;

            case "image/svg+xml":
                // SVG text check (starts with XML declaration or <svg)
                string headerText = Encoding.UTF8.GetString(headerBuffer, 0, bytesRead).TrimStart();
                if (!headerText.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) &&
                    !headerText.StartsWith("<svg", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ValidationException(nameof(contentType), "File content does not match SVG signature.");
                }
                break;

            default:
                throw new ValidationException(nameof(contentType),
                    $"Unsupported content type '{contentType}'. Allowed types: image/png, image/jpeg, image/webp, image/svg+xml, application/pdf.");
        }
    }

    #endregion
}
