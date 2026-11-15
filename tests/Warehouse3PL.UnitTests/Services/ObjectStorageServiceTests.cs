using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Services.Storage;
using Xunit;

namespace Warehouse3PL.UnitTests.Services;

public class ObjectStorageServiceTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly PrivateObjectStorageService _storageService;
    private readonly ObjectStorageOptions _options;

    public ObjectStorageServiceTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "warehouse3pl_storage_tests_" + Guid.NewGuid().ToString("N"));
        _options = new ObjectStorageOptions
        {
            BaseDirectory = _testDirectory,
            SigningSecretKey = "TestSecretKey123456789012345678901234567890!",
            DefaultExpiryMinutes = 15,
            MinExpiryMinutes = 1,
            MaxExpiryMinutes = 60,
            MaxFileSizeBytes = 1024 * 1024 // 1 MB
        };

        _storageService = new PrivateObjectStorageService(
            Options.Create(_options),
            NullLogger<PrivateObjectStorageService>.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    private static byte[] CreateValidPngBytes(int payloadSize = 50)
    {
        byte[] pngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        byte[] payload = new byte[pngHeader.Length + payloadSize];
        Array.Copy(pngHeader, payload, pngHeader.Length);
        for (int i = pngHeader.Length; i < payload.Length; i++)
        {
            payload[i] = (byte)(i % 256);
        }
        return payload;
    }

    private static byte[] CreateValidJpegBytes(int payloadSize = 50)
    {
        byte[] jpegHeader = [0xFF, 0xD8, 0xFF, 0xE0];
        byte[] payload = new byte[jpegHeader.Length + payloadSize];
        Array.Copy(jpegHeader, payload, jpegHeader.Length);
        for (int i = jpegHeader.Length; i < payload.Length; i++)
        {
            payload[i] = (byte)(i % 256);
        }
        return payload;
    }

    private static byte[] CreateValidPdfBytes(int payloadSize = 50)
    {
        byte[] pdfHeader = [0x25, 0x50, 0x44, 0x46]; // %PDF
        byte[] payload = new byte[pdfHeader.Length + payloadSize];
        Array.Copy(pdfHeader, payload, pdfHeader.Length);
        for (int i = pdfHeader.Length; i < payload.Length; i++)
        {
            payload[i] = (byte)(i % 256);
        }
        return payload;
    }

    [Fact]
    public async Task UploadStaged_ValidPngStream_ComputesStreamingSha256AndStagesFile()
    {
        byte[] data = CreateValidPngBytes();
        string expectedSha256;
        using (var sha = SHA256.Create())
        {
            expectedSha256 = Convert.ToHexString(sha.ComputeHash(data)).ToLowerInvariant();
        }

        using var stream = new MemoryStream(data);
        const string tempKey = "temp/tenants/client1/pod/pkg1/sig_temp.png";

        var result = await _storageService.UploadStagedAsync(tempKey, stream, "image/png");

        Assert.NotNull(result);
        Assert.Equal(tempKey, result.TempObjectKey);
        Assert.Equal(data.Length, result.ContentLengthBytes);
        Assert.Equal("image/png", result.ContentType);
        Assert.Equal(expectedSha256, result.Sha256Hash);

        // Verify file exists in temp directory
        Assert.True(await _storageService.ExistsAsync(tempKey));
    }

    [Fact]
    public async Task UploadStaged_ValidJpegAndPdfStreams_Succeeds()
    {
        // JPEG
        byte[] jpegData = CreateValidJpegBytes();
        using (var jpegStream = new MemoryStream(jpegData))
        {
            var result = await _storageService.UploadStagedAsync("temp/photo.jpg", jpegStream, "image/jpeg");
            Assert.Equal("image/jpeg", result.ContentType);
            Assert.Equal(jpegData.Length, result.ContentLengthBytes);
        }

        // PDF
        byte[] pdfData = CreateValidPdfBytes();
        using (var pdfStream = new MemoryStream(pdfData))
        {
            var result = await _storageService.UploadStagedAsync("temp/manifest.pdf", pdfStream, "application/pdf");
            Assert.Equal("application/pdf", result.ContentType);
            Assert.Equal(pdfData.Length, result.ContentLengthBytes);
        }
    }

    [Fact]
    public async Task UploadStaged_MimeTypeMismatch_ThrowsValidationException()
    {
        // Data is PNG, but content type declared is image/jpeg
        byte[] pngData = CreateValidPngBytes();
        using var stream = new MemoryStream(pngData);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _storageService.UploadStagedAsync("temp/sig.jpg", stream, "image/jpeg"));

        Assert.Contains("JPEG signature", ex.Message);
    }

    [Fact]
    public async Task UploadStaged_InvalidMagicBytesForDeclaredMime_ThrowsValidationException()
    {
        // Plain text bytes declared as image/png
        byte[] textData = Encoding.UTF8.GetBytes("This is not a png file at all!");
        using var stream = new MemoryStream(textData);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _storageService.UploadStagedAsync("temp/sig.png", stream, "image/png"));

        Assert.Contains("PNG signature", ex.Message);
    }

    [Fact]
    public async Task UploadStaged_PathTraversalKey_ThrowsValidationException()
    {
        byte[] pngData = CreateValidPngBytes();
        using var stream = new MemoryStream(pngData);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _storageService.UploadStagedAsync("../../etc/passwd", stream, "image/png"));

        Assert.Contains("Path traversal", ex.Message);
    }

    [Fact]
    public async Task UploadStaged_ExceedsMaxFileSize_ThrowsValidationExceptionAndCleansUp()
    {
        // Create custom service with 50-byte max size limit
        var smallOptions = new ObjectStorageOptions
        {
            BaseDirectory = _testDirectory,
            MaxFileSizeBytes = 50
        };
        var smallService = new PrivateObjectStorageService(
            Options.Create(smallOptions),
            NullLogger<PrivateObjectStorageService>.Instance);

        byte[] largeData = CreateValidPngBytes(payloadSize: 200); // ~208 bytes
        using var stream = new MemoryStream(largeData);

        const string tempKey = "temp/large_file.png";
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            smallService.UploadStagedAsync(tempKey, stream, "image/png"));

        Assert.Contains("exceeds maximum allowed limit", ex.Message);
        Assert.False(await smallService.ExistsAsync(tempKey));
    }

    [Fact]
    public async Task FinalizeObject_PromotesTempToFinal_AndPreservesMetadata()
    {
        byte[] data = CreateValidPngBytes();
        using var stream = new MemoryStream(data);
        const string tempKey = "temp/client1/pod/pkg1/sig_temp.png";
        const string finalKey = "tenants/client1/pod/pkg1/signature_final.png";

        var uploadResult = await _storageService.UploadStagedAsync(tempKey, stream, "image/png");

        var finalMetadata = await _storageService.FinalizeObjectAsync(tempKey, finalKey);

        Assert.NotNull(finalMetadata);
        Assert.Equal(finalKey, finalMetadata.ObjectKey);
        Assert.Equal(uploadResult.Sha256Hash, finalMetadata.Sha256Hash);
        Assert.Equal(data.Length, finalMetadata.ContentLengthBytes);

        // Final key must exist
        Assert.True(await _storageService.ExistsAsync(finalKey));

        // Temp key must be cleaned up
        var tempPath = Path.Combine(_testDirectory, "temp", tempKey);
        Assert.False(File.Exists(tempPath));
    }

    [Fact]
    public async Task FinalizeObject_NonExistentTempKey_ThrowsEntityNotFoundException()
    {
        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            _storageService.FinalizeObjectAsync("temp/non_existent.png", "final/non_existent.png"));
    }

    [Fact]
    public async Task DownloadAsync_RetrievesExactUploadedBytes()
    {
        byte[] data = CreateValidPngBytes();
        using var stream = new MemoryStream(data);
        const string tempKey = "temp/client1/pod/pkg1/download_test.png";
        const string finalKey = "tenants/client1/pod/pkg1/download_test.png";

        await _storageService.UploadStagedAsync(tempKey, stream, "image/png");
        await _storageService.FinalizeObjectAsync(tempKey, finalKey);

        await using var downloadedStream = await _storageService.DownloadAsync(finalKey);
        Assert.NotNull(downloadedStream);

        using var ms = new MemoryStream();
        await downloadedStream.CopyToAsync(ms);
        byte[] downloadedBytes = ms.ToArray();

        Assert.Equal(data, downloadedBytes);
    }

    [Fact]
    public async Task GeneratePreSignedDownloadUrl_EnforcesExpiryClamping()
    {
        const string objectKey = "tenants/client1/pod/pkg1/photo.jpg";

        // 1. Default expiry (15 minutes)
        var defaultResult = await _storageService.GeneratePreSignedDownloadUrlAsync(objectKey);
        var expectedDefault = DateTime.UtcNow.AddMinutes(15);
        Assert.InRange((defaultResult.ExpiresAtUtc - expectedDefault).TotalSeconds, -10, 10);
        Assert.Contains(Uri.EscapeDataString(objectKey), defaultResult.DownloadUrl);
        Assert.NotEmpty(defaultResult.Token);

        // 2. Below minimum (requested 30 seconds -> clamped to 1 minute)
        var minResult = await _storageService.GeneratePreSignedDownloadUrlAsync(objectKey, TimeSpan.FromSeconds(30));
        var expectedMin = DateTime.UtcNow.AddMinutes(1);
        Assert.InRange((minResult.ExpiresAtUtc - expectedMin).TotalSeconds, -10, 10);

        // 3. Above maximum (requested 120 minutes -> clamped to 60 minutes)
        var maxResult = await _storageService.GeneratePreSignedDownloadUrlAsync(objectKey, TimeSpan.FromMinutes(120));
        var expectedMax = DateTime.UtcNow.AddMinutes(60);
        Assert.InRange((maxResult.ExpiresAtUtc - expectedMax).TotalSeconds, -10, 10);
    }

    [Fact]
    public async Task ValidatePreSignedToken_ValidToken_ReturnsTrue()
    {
        const string objectKey = "tenants/client1/pod/pkg1/signature.png";
        var result = await _storageService.GeneratePreSignedDownloadUrlAsync(objectKey, TimeSpan.FromMinutes(15));

        bool isValid = _storageService.ValidatePreSignedToken(objectKey, result.Token, DateTime.UtcNow);
        Assert.True(isValid);
    }

    [Fact]
    public async Task ValidatePreSignedToken_ExpiredToken_ReturnsFalse()
    {
        const string objectKey = "tenants/client1/pod/pkg1/signature.png";
        var result = await _storageService.GeneratePreSignedDownloadUrlAsync(objectKey, TimeSpan.FromMinutes(10));

        // Evaluate at a time past the expiration window (e.g. 15 minutes later)
        bool isValid = _storageService.ValidatePreSignedToken(objectKey, result.Token, DateTime.UtcNow.AddMinutes(15));
        Assert.False(isValid);
    }

    [Fact]
    public async Task ValidatePreSignedToken_TamperedTokenOrKey_ReturnsFalse()
    {
        const string objectKey = "tenants/client1/pod/pkg1/signature.png";
        var result = await _storageService.GeneratePreSignedDownloadUrlAsync(objectKey, TimeSpan.FromMinutes(15));

        // Tampered token
        string tamperedToken = result.Token + "xyz";
        Assert.False(_storageService.ValidatePreSignedToken(objectKey, tamperedToken, DateTime.UtcNow));

        // Different object key
        Assert.False(_storageService.ValidatePreSignedToken("tenants/other_client/pod/other.png", result.Token, DateTime.UtcNow));
    }

    [Fact]
    public async Task DeleteAsync_RemovesFileAndMetadata()
    {
        byte[] data = CreateValidPngBytes();
        using var stream = new MemoryStream(data);
        const string tempKey = "temp/delete_me.png";
        const string finalKey = "tenants/client1/pod/pkg1/delete_me.png";

        await _storageService.UploadStagedAsync(tempKey, stream, "image/png");
        await _storageService.FinalizeObjectAsync(tempKey, finalKey);

        Assert.True(await _storageService.ExistsAsync(finalKey));

        bool deleted = await _storageService.DeleteAsync(finalKey);
        Assert.True(deleted);
        Assert.False(await _storageService.ExistsAsync(finalKey));
    }

    [Fact]
    public async Task CleanupOrphanedTempObjects_DeletesExpiredTempFiles()
    {
        byte[] data = CreateValidPngBytes();
        using var stream = new MemoryStream(data);
        const string tempKey = "client1/pod/orphan_test.png";

        await _storageService.UploadStagedAsync(tempKey, stream, "image/png");
        Assert.True(await _storageService.ExistsAsync(tempKey));

        // Backdate file's LastWriteTime to simulate older file
        var filePath = Path.Combine(_testDirectory, "temp", tempKey);
        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow.AddHours(-3));

        int cleaned = await _storageService.CleanupOrphanedTempObjectsAsync(TimeSpan.FromHours(1));
        Assert.True(cleaned >= 1);
        Assert.False(await _storageService.ExistsAsync(tempKey));
    }
}

