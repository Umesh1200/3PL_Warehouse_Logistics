using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Storage.Queries.ResolveStorageDownload;

public sealed record ResolveStorageDownloadQuery(string? ObjectKey, string? Token) : IRequest<AuthorizedStorageDownloadResult>;

public sealed record AuthorizedStorageDownloadResult(
    string NormalizedKey,
    string ContentType,
    string SafeFileName,
    Stream ContentStream);

/// <summary>
/// Validates object-key namespace/path safety, verifies HMAC-SHA256 pre-signed download tokens,
/// enforces tenant/facility/driver evidence ownership against persisted DB records, and emits
/// structured security audit logs on all rejected attempts without disclosing foreign metadata.
/// </summary>
public sealed class ResolveStorageDownloadQueryHandler
    : IRequestHandler<ResolveStorageDownloadQuery, AuthorizedStorageDownloadResult>
{
    private const string GenericDeniedMessage = "Access to the requested storage object is denied.";

    private static readonly HashSet<string> AllowedTenantCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "pod", "labels", "bol", "returns"
    };

    private static readonly HashSet<string> AllowedLegacyTopCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "pod", "labels", "bol", "returns", "signatures", "photos"
    };

    private static readonly HashSet<string> SafeDownloadMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/jpg",
        "image/webp",
        "image/svg+xml",
        "application/pdf"
    };

    private readonly IApplicationDbContext _context;
    private readonly IObjectStorageService _storageService;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<ResolveStorageDownloadQueryHandler> _logger;

    public ResolveStorageDownloadQueryHandler(
        IApplicationDbContext context,
        IObjectStorageService storageService,
        ICurrentUserContext currentUser,
        IDateTimeProvider dateTimeProvider,
        ILogger<ResolveStorageDownloadQueryHandler> logger)
    {
        _context = context;
        _storageService = storageService;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    public async Task<AuthorizedStorageDownloadResult> Handle(
        ResolveStorageDownloadQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
        {
            AuditRejected("Unauthenticated", request.ObjectKey);
            throw new UnauthorizedException("Authentication is required to access private storage.");
        }

        if (string.IsNullOrWhiteSpace(request.ObjectKey) || string.IsNullOrWhiteSpace(request.Token))
        {
            AuditRejected("MissingKeyOrToken", request.ObjectKey);
            throw new ValidationException(nameof(request.ObjectKey), "Object key and signed download token are required.");
        }

        if (!TryNormalizeEvidenceKey(request.ObjectKey, out var normalizedKey, out var tenantSegment, out var pathReason))
        {
            AuditRejected(pathReason, request.ObjectKey);
            throw new ForbiddenException(GenericDeniedMessage);
        }

        if (!_storageService.ValidatePreSignedToken(normalizedKey, request.Token.Trim(), _dateTimeProvider.UtcNow))
        {
            AuditRejected("InvalidOrExpiredToken", normalizedKey);
            throw new ForbiddenException(GenericDeniedMessage);
        }

        var resolution = await ResolveAuthorizedEvidenceAsync(normalizedKey, tenantSegment, cancellationToken);
        if (!resolution.IsAuthorized)
        {
            throw new ForbiddenException(GenericDeniedMessage);
        }

        if (!resolution.IsFinalized)
        {
            AuditRejected("EvidenceStorageNotFinalized", normalizedKey);
            throw new ForbiddenException(GenericDeniedMessage);
        }

        var metadata = await _storageService.GetFinalizedMetadataAsync(normalizedKey, cancellationToken);
        var finalizedStream = await _storageService.DownloadFinalizedAsync(normalizedKey, cancellationToken);
        if (finalizedStream == null)
        {
            AuditRejected("AuthorizedRecordMissingBlob", normalizedKey);
            throw new EntityNotFoundException("StorageObject", "Requested storage object was not found.");
        }

        Stream responseStream;
        if (!string.IsNullOrWhiteSpace(resolution.ExpectedSha256))
        {
            var buffered = new MemoryStream();
            try
            {
                await using (finalizedStream)
                {
                    await finalizedStream.CopyToAsync(buffered, cancellationToken);
                }

                if (!VerifyContentSha256(buffered, resolution.ExpectedSha256))
                {
                    await buffered.DisposeAsync();
                    AuditRejected("EvidenceHashMismatch", normalizedKey);
                    throw new ForbiddenException(GenericDeniedMessage);
                }

                buffered.Position = 0;
                responseStream = buffered;
            }
            catch
            {
                await buffered.DisposeAsync();
                throw;
            }
        }
        else
        {
            responseStream = finalizedStream;
        }

        string safeContentType = ResolveSafeContentType(metadata?.ContentType);
        string safeFileName = BuildSafeFileName(normalizedKey);

        _logger.LogInformation(
            "SECURITY_AUDIT StorageDownloadGranted UserId={UserId} UserType={UserType} TenantId={TenantId} KeyHash={KeyHash}",
            _currentUser.UserId, _currentUser.UserType, _currentUser.TenantId, HashKey(normalizedKey));

        return new AuthorizedStorageDownloadResult(normalizedKey, safeContentType, safeFileName, responseStream);
    }

    private sealed record EvidenceResolution(bool IsAuthorized, bool IsFinalized, string? ExpectedSha256);

    private async Task<EvidenceResolution> ResolveAuthorizedEvidenceAsync(
        string normalizedKey,
        string? keyTenantSegment,
        CancellationToken cancellationToken)
    {
        // 1. Check ProofOfDelivery evidence.
        // Historical evidence-read eligibility:
        // - Active and superseded historical PODs (and delivered packages) remain readable for Staff (Permissions.Dispatch.View)
        //   and Client owners (tenant + Permissions.Dispatch.View + active ClientFacilityAccess).
        // - Drivers may read ONLY non-superseded PODs on shipments where they actively hold confirmed custody.
        var pod = await _context.ProofOfDeliveries
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(p => p.Shipment)
            .FirstOrDefaultAsync(
                p => p.SignatureObjectKey == normalizedKey || p.PhotoObjectKey == normalizedKey,
                cancellationToken);

        if (pod != null)
        {
            if (!MatchesKeyTenantPrefix(keyTenantSegment, pod.ClientId))
            {
                AuditRejected("KeyTenantPrefixMismatch", normalizedKey);
                return new EvidenceResolution(false, false, null);
            }

            if (!await IsAuthorizedForPodAsync(pod, cancellationToken))
            {
                AuditRejected("PodOwnershipDenied", normalizedKey);
                return new EvidenceResolution(false, false, null);
            }

            string? expectedSha256 = string.Equals(normalizedKey, pod.SignatureObjectKey, StringComparison.OrdinalIgnoreCase)
                ? pod.SignatureSha256
                : pod.PhotoSha256;

            return new EvidenceResolution(true, pod.IsStorageFinalized, expectedSha256);
        }

        // 2. Check ReturnShipment labels
        var returnShipment = await _context.ReturnShipments
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                r => r.LabelObjectKey == normalizedKey,
                cancellationToken);

        if (returnShipment != null)
        {
            if (!MatchesKeyTenantPrefix(keyTenantSegment, returnShipment.ClientId))
            {
                AuditRejected("KeyTenantPrefixMismatch", normalizedKey);
                return new EvidenceResolution(false, false, null);
            }

            if (await IsAuthorizedForReturnShipmentAsync(
                    returnShipment.ClientId,
                    returnShipment.OriginFacilityId,
                    returnShipment.DestinationFacilityId,
                    cancellationToken))
            {
                return new EvidenceResolution(true, true, null);
            }

            AuditRejected("ReturnLabelOwnershipDenied", normalizedKey);
            return new EvidenceResolution(false, false, null);
        }

        // Fail closed for unregistered/orphaned files on disk without revealing existence.
        AuditRejected("UnregisteredEvidenceKey", normalizedKey);
        return new EvidenceResolution(false, false, null);
    }

    private async Task<bool> IsAuthorizedForPodAsync(
        Domain.Entities.Outbound.ProofOfDelivery pod,
        CancellationToken cancellationToken)
    {
        if (_currentUser.IsSuperAdmin && _currentUser.UserType == UserType.Staff)
        {
            return true;
        }

        if (_currentUser.UserType == UserType.Staff)
        {
            // Aligned with GET /api/v1/outbound/dispatch/packages/{packageId}/pod (Permissions.Dispatch.View required).
            if (!_currentUser.HasPermission(Permissions.Dispatch.View))
            {
                return false;
            }

            if (HasCrossFacilityRead())
            {
                return true;
            }

            return _currentUser.FacilityId.HasValue && _currentUser.FacilityId.Value == pod.FacilityId;
        }

        if (_currentUser.UserType == UserType.Client)
        {
            if (!_currentUser.TenantId.HasValue || _currentUser.TenantId.Value != pod.ClientId)
            {
                return false;
            }

            if (!_currentUser.HasPermission(Permissions.Dispatch.View))
            {
                return false;
            }

            if (_currentUser.FacilityId.HasValue &&
                _currentUser.FacilityId.Value != Guid.Empty &&
                _currentUser.FacilityId.Value != pod.FacilityId)
            {
                return false;
            }

            return await _context.ClientFacilityAccesses
                .AsNoTracking()
                .AnyAsync(a => a.ClientId == pod.ClientId &&
                               a.FacilityId == pod.FacilityId &&
                               a.Status == FacilityAccessStatus.Active,
                    cancellationToken);
        }

        if (_currentUser.UserType == UserType.Driver)
        {
            if (pod.IsSuperseded ||
                !_currentUser.HasPermission(Permissions.Dispatch.ExecuteDelivery) ||
                !_currentUser.UserId.HasValue ||
                _currentUser.UserId.Value == Guid.Empty)
            {
                return false;
            }

            var driverUserId = _currentUser.UserId.Value;
            var shipment = pod.Shipment;
            if (!pod.ShipmentId.HasValue ||
                pod.ShipmentId.Value == Guid.Empty ||
                shipment == null ||
                shipment.IsDeleted ||
                shipment.ClientId != pod.ClientId ||
                shipment.FacilityId != pod.FacilityId ||
                shipment.Status != ShipmentStatus.Dispatched ||
                !shipment.AssignedDriverUserId.HasValue ||
                shipment.AssignedDriverUserId.Value != driverUserId ||
                shipment.CustodyStatus != ShipmentCustodyStatus.Confirmed)
            {
                return false;
            }

            // Enforce Phase 7.4 authoritative latest custody transfer chain verification.
            var latestCustodyTransfer = await _context.ShipmentCustodyTransfers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(t => t.ShipmentId == shipment.Id &&
                            t.ClientId == shipment.ClientId &&
                            t.FacilityId == shipment.FacilityId)
                .OrderByDescending(t => t.TransferredAtUtc)
                .ThenByDescending(t => t.Id)
                .Select(t => new
                {
                    t.AssignedDriverUserId,
                    t.CustodyStatus,
                    t.DriverSignatureAcknowledgment
                })
                .FirstOrDefaultAsync(cancellationToken);

            return latestCustodyTransfer != null &&
                   latestCustodyTransfer.AssignedDriverUserId.HasValue &&
                   latestCustodyTransfer.AssignedDriverUserId.Value == driverUserId &&
                   latestCustodyTransfer.CustodyStatus == ShipmentCustodyStatus.Confirmed &&
                   !string.IsNullOrWhiteSpace(latestCustodyTransfer.DriverSignatureAcknowledgment) &&
                   !latestCustodyTransfer.DriverSignatureAcknowledgment.StartsWith(
                       "SUPERVISOR_CUSTODY_REASSIGNMENT:",
                       StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private async Task<bool> IsAuthorizedForReturnShipmentAsync(
        Guid evidenceClientId,
        Guid originFacilityId,
        Guid destinationFacilityId,
        CancellationToken cancellationToken)
    {
        if (_currentUser.IsSuperAdmin && _currentUser.UserType == UserType.Staff)
        {
            return true;
        }

        if (_currentUser.UserType == UserType.Staff)
        {
            bool hasReadPermission =
                _currentUser.HasPermission(Permissions.Dispatch.View) ||
                _currentUser.HasPermission(Permissions.Outbound.Ship) ||
                _currentUser.HasPermission(Permissions.Outbound.View);

            if (!hasReadPermission)
            {
                return false;
            }

            if (HasCrossFacilityRead())
            {
                return true;
            }

            return _currentUser.FacilityId.HasValue &&
                   (_currentUser.FacilityId.Value == originFacilityId ||
                    _currentUser.FacilityId.Value == destinationFacilityId);
        }

        if (_currentUser.UserType == UserType.Client)
        {
            if (!_currentUser.TenantId.HasValue || _currentUser.TenantId.Value != evidenceClientId)
            {
                return false;
            }

            bool hasClientRead =
                _currentUser.HasPermission(Permissions.Dispatch.View) ||
                _currentUser.HasPermission(Permissions.Outbound.View) ||
                _currentUser.HasPermission(Permissions.Outbound.OrdersView);

            if (!hasClientRead)
            {
                return false;
            }

            if (_currentUser.FacilityId.HasValue &&
                _currentUser.FacilityId.Value != Guid.Empty &&
                _currentUser.FacilityId.Value != originFacilityId &&
                _currentUser.FacilityId.Value != destinationFacilityId)
            {
                return false;
            }

            return await _context.ClientFacilityAccesses
                .AsNoTracking()
                .AnyAsync(a => a.ClientId == evidenceClientId &&
                               (a.FacilityId == originFacilityId || a.FacilityId == destinationFacilityId) &&
                               a.Status == FacilityAccessStatus.Active,
                    cancellationToken);
        }

        return false;
    }

    private bool HasCrossFacilityRead() =>
        _currentUser.HasPermission(Permissions.Analytics.MultiFacilityView);

    private static bool VerifyContentSha256(MemoryStream buffered, string expectedSha256Hex)
    {
        byte[] expectedBytes;
        try
        {
            expectedBytes = Convert.FromHexString(expectedSha256Hex.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        if (expectedBytes.Length != SHA256.HashSizeInBytes)
        {
            return false;
        }

        byte[] actualBytes = SHA256.HashData(buffered.GetBuffer().AsSpan(0, (int)buffered.Length));
        return CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }

    private static bool MatchesKeyTenantPrefix(string? keyTenantSegment, Guid ownerClientId)
    {
        if (string.IsNullOrEmpty(keyTenantSegment))
        {
            return true;
        }

        return Guid.TryParse(keyTenantSegment, out var parsedClientId) && parsedClientId == ownerClientId;
    }

    private static bool TryNormalizeEvidenceKey(
        string rawKey,
        out string normalizedKey,
        out string? tenantSegment,
        out string rejectionReason)
    {
        normalizedKey = string.Empty;
        tenantSegment = null;

        if (rawKey.Contains("..", StringComparison.Ordinal) ||
            rawKey.Contains('\\') ||
            rawKey.Contains(':') ||
            rawKey.IndexOf('\0') >= 0 ||
            rawKey.Any(char.IsControl))
        {
            rejectionReason = "UnsafeObjectKeyCharacters";
            return false;
        }

        var trimmed = rawKey.Trim().TrimStart('/');
        if (string.IsNullOrWhiteSpace(trimmed) ||
            trimmed.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase))
        {
            rejectionReason = "ProtectedMetadataOrEmptyKey";
            return false;
        }

        var segments = trimmed.Split('/');
        if (segments.Length < 2 || segments.Any(s => string.IsNullOrWhiteSpace(s) || s == "." || s == ".."))
        {
            rejectionReason = "InvalidObjectKeySegments";
            return false;
        }

        // Reject temporary staging namespaces explicitly from HTTP download
        if (string.Equals(segments[0], "temp", StringComparison.OrdinalIgnoreCase) ||
            segments[^1].StartsWith("temp_", StringComparison.OrdinalIgnoreCase))
        {
            rejectionReason = "TemporaryObjectDownloadDenied";
            return false;
        }

        if (string.Equals(segments[0], "tenants", StringComparison.OrdinalIgnoreCase))
        {
            if (segments.Length < 4 || !AllowedTenantCategories.Contains(segments[2]))
            {
                rejectionReason = "UnsupportedTenantNamespace";
                return false;
            }

            tenantSegment = segments[1];
            normalizedKey = trimmed;
            rejectionReason = string.Empty;
            return true;
        }

        if (AllowedLegacyTopCategories.Contains(segments[0]))
        {
            normalizedKey = trimmed;
            rejectionReason = string.Empty;
            return true;
        }

        rejectionReason = "UnsupportedObjectNamespace";
        return false;
    }

    private static string ResolveSafeContentType(string? rawContentType)
    {
        if (string.IsNullOrWhiteSpace(rawContentType))
        {
            return "application/octet-stream";
        }

        var normalized = rawContentType.Trim().ToLowerInvariant();
        if (normalized == "image/jpg")
        {
            return "image/jpeg";
        }

        return SafeDownloadMimeTypes.Contains(normalized)
            ? normalized
            : "application/octet-stream";
    }

    private static string BuildSafeFileName(string normalizedKey)
    {
        var rawName = normalizedKey.Split('/').LastOrDefault() ?? "evidence.bin";
        var sb = new StringBuilder(rawName.Length);
        foreach (var ch in rawName)
        {
            if (char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or '_')
            {
                sb.Append(ch);
            }
            else
            {
                sb.Append('_');
            }
        }

        var sanitized = sb.ToString().Trim('.');
        return string.IsNullOrWhiteSpace(sanitized) ? "evidence.bin" : sanitized;
    }

    private void AuditRejected(string reason, string? objectKey)
    {
        _logger.LogWarning(
            "SECURITY_AUDIT StorageDownloadRejected Reason={Reason} UserId={UserId} UserType={UserType} TenantId={TenantId} FacilityId={FacilityId} KeyHash={KeyHash}",
            reason,
            _currentUser.UserId,
            _currentUser.UserType,
            _currentUser.TenantId,
            _currentUser.FacilityId,
            string.IsNullOrWhiteSpace(objectKey) ? "empty" : HashKey(objectKey));
    }

    private static string HashKey(string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}
