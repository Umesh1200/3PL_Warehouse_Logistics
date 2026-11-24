using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services.Shipping;

/// <summary>
/// Authoritative Proof of Delivery submission, validation, and staged evidence ingestion service.
/// DB-SPEC-3PL-2026 Phase 4.5 Shipment Tracking & Delivery Confirmation.
/// </summary>
public class ProofOfDeliveryService : IProofOfDeliveryService
{
    private readonly IApplicationDbContext _context;
    private readonly IObjectStorageService _storageService;
    private readonly ITenantProvider _tenantProvider;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ILogger<ProofOfDeliveryService> _logger;

    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/jpg",
        "image/webp",
        "application/pdf"
    };

    public ProofOfDeliveryService(
        IApplicationDbContext context,
        IObjectStorageService storageService,
        ITenantProvider tenantProvider,
        ICurrentUserContext currentUserContext,
        ILogger<ProofOfDeliveryService> logger)
    {
        _context = context;
        _storageService = storageService;
        _tenantProvider = tenantProvider;
        _currentUserContext = currentUserContext;
        _logger = logger;
    }

    public async Task<SubmitProofOfDeliveryResult> SubmitProofOfDeliveryAsync(
        SubmitProofOfDeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteSubmissionInternalAsync(request, isSupersession: false, null, cancellationToken);
    }

    public async Task<SubmitProofOfDeliveryResult> SupersedeProofOfDeliveryAsync(
        SupersedeProofOfDeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ExistingPodId == Guid.Empty)
        {
            throw new ValidationException(nameof(request.ExistingPodId), "Existing Proof of Delivery ID must be provided.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ValidationException(nameof(request.Reason), "Supersession reason must be provided.");
        }

        if (request.Reason.Trim().Length > 500)
        {
            throw new ValidationException(nameof(request.Reason), "Supersession reason cannot exceed 500 characters.");
        }

        if (request.ReplacementSubmission == null)
        {
            throw new ValidationException(nameof(request.ReplacementSubmission), "Replacement Proof of Delivery submission must be provided.");
        }

        WarehouseOperationalAuthorization.ValidatePodEvidenceSourceAuthorization(
            _currentUserContext,
            request.ReplacementSubmission.EvidenceSource,
            request.ReplacementSubmission.VerificationReason,
            request.ReplacementSubmission.IsInteractiveSubmission);

        var existingPodSnapshot = await _context.ProofOfDeliveries
            .Include(p => p.Package)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.ExistingPodId, cancellationToken);

        if (existingPodSnapshot == null)
        {
            throw new EntityNotFoundException(nameof(ProofOfDelivery), request.ExistingPodId);
        }

        // Validate that replacement belongs to the same package
        if (request.ReplacementSubmission.PackageId != Guid.Empty &&
            request.ReplacementSubmission.PackageId != existingPodSnapshot.PackageId)
        {
            throw new ValidationException(nameof(request.ReplacementSubmission.PackageId),
                "Replacement Proof of Delivery must belong to the same package as the superseded record.");
        }

        request.ReplacementSubmission.PackageId = existingPodSnapshot.PackageId;

        if (existingPodSnapshot.IsSuperseded)
        {
            var canonicalSupIdempKey = NormalizeIdempotencyKey(request.ReplacementSubmission.IdempotencyKey);
            if (canonicalSupIdempKey != null)
            {
                ValidateSourceSpecificRules(request.ReplacementSubmission);
                ValidateEvidenceContentTypes(request.ReplacementSubmission);
                var supSigSha = await ComputeStreamSha256Async(request.ReplacementSubmission.SignatureStream, cancellationToken);
                var supPhotoSha = await ComputeStreamSha256Async(request.ReplacementSubmission.PhotoStream, cancellationToken);

                var idempPod = await FindFacilityPodByIdempotencyKeyAsync(
                    existingPodSnapshot.FacilityId,
                    canonicalSupIdempKey,
                    includePackage: true,
                    cancellationToken);

                if (idempPod != null)
                {
                    if (IsMatchingSupersededDuplicate(existingPodSnapshot, idempPod, request.Reason, request.ReplacementSubmission, supSigSha, supPhotoSha))
                    {
                        return await BuildSubmissionResultAsync(
                            idempPod,
                            idempPod.Package,
                            isDuplicate: true,
                            "Duplicate POD supersession ignored (Idempotent).",
                            cancellationToken);
                    }

                    throw new ConcurrencyException(
                        $"Proof of Delivery idempotency key '{canonicalSupIdempKey}' has already been consumed in this facility.");
                }
            }

            throw new InvalidDomainStateException($"Proof of Delivery '{existingPodSnapshot.Id}' has already been superseded.");
        }

        // Atomically supersede historical POD + insert replacement POD inside package/shipment lock & transaction
        var result = await ExecuteSubmissionInternalAsync(
            request.ReplacementSubmission,
            isSupersession: true,
            supersedeRequest: request,
            cancellationToken);

        _logger.LogInformation("Superseded POD '{OldPodId}' with new POD '{NewPodId}' for Package '{Pkg}'. Reason: {Reason}",
            existingPodSnapshot.Id, result.ProofOfDeliveryId, existingPodSnapshot.PackageId, request.Reason);

        result.Message = $"Proof of Delivery superseded successfully. Old POD: '{existingPodSnapshot.Id}'.";
        return result;
    }

    private async Task<SubmitProofOfDeliveryResult> ExecuteSubmissionInternalAsync(
        SubmitProofOfDeliveryRequest request,
        bool isSupersession,
        SupersedeProofOfDeliveryRequest? supersedeRequest,
        CancellationToken cancellationToken)
    {
        // 1. Package & Evidence-Source Authorization (fail-closed before DB lookup or staged upload)
        if (request.PackageId == Guid.Empty)
        {
            throw new ValidationException(nameof(request.PackageId), "Package ID must be provided.");
        }

        WarehouseOperationalAuthorization.ValidatePodEvidenceSourceAuthorization(
            _currentUserContext,
            request.EvidenceSource,
            request.VerificationReason,
            request.IsInteractiveSubmission);

        var package = await _context.Packages
            .AsNoTracking()
            .Include(p => p.Shipment)
            .FirstOrDefaultAsync(p => p.Id == request.PackageId && !p.IsDeleted, cancellationToken);

        if (package == null)
        {
            throw new EntityNotFoundException(nameof(Package), request.PackageId);
        }

        // Tenant Isolation Check
        if (request.TenantIdOverride.HasValue && request.TenantIdOverride.Value != package.ClientId)
        {
            throw new ValidationException(nameof(request.PackageId),
                $"Package '{package.PackageNumber}' belongs to a different client tenant. Cross-tenant access rejected.");
        }

        if (_tenantProvider.TenantId.HasValue &&
            !_tenantProvider.BypassTenantFilter &&
            _tenantProvider.TenantId.Value != package.ClientId)
        {
            throw new UnauthorizedException("Unauthorized cross-tenant Proof of Delivery submission.");
        }

        // Package Eligibility Check
        if (string.IsNullOrWhiteSpace(package.TrackingNumber) || string.IsNullOrWhiteSpace(package.CarrierCode))
        {
            throw new ValidationException(nameof(request.PackageId),
                $"Package '{package.PackageNumber}' does not have an assigned tracking number or carrier code.");
        }

        // 2. Channel-Specific Validation
        ValidateSourceSpecificRules(request);

        // 3. MIME Type & File Signature Sanitization
        ValidateEvidenceContentTypes(request);

        var canonicalIdempKey = NormalizeIdempotencyKey(request.IdempotencyKey);
        var incomingSigSha256 = await ComputeStreamSha256Async(request.SignatureStream, cancellationToken);
        var incomingPhotoSha256 = await ComputeStreamSha256Async(request.PhotoStream, cancellationToken);

        // 4. Check for Facility-Scoped Idempotency Key Reuse (Active or Historical) & Existing Active POD Collision
        if (canonicalIdempKey != null)
        {
            var existingIdempPod = await FindFacilityPodByIdempotencyKeyAsync(
                package.FacilityId,
                canonicalIdempKey,
                includePackage: false,
                cancellationToken);

            if (existingIdempPod != null)
            {
                EnsureIdempotencyKeyReusableForSubmission(existingIdempPod, package.Id, canonicalIdempKey, isSupersession);
            }
        }

        if (!isSupersession)
        {
            var activePod = await _context.ProofOfDeliveries
                .FirstOrDefaultAsync(p => p.PackageId == package.Id && !p.IsSuperseded, cancellationToken);

            if (activePod != null)
            {
                if (EvaluateExistingPodCollision(activePod, request, canonicalIdempKey, incomingSigSha256, incomingPhotoSha256))
                {
                    _logger.LogInformation("Idempotent duplicate POD detected for package '{Pkg}'. Returning existing POD '{PodId}'.",
                        package.PackageNumber, activePod.Id);

                    return await BuildSubmissionResultAsync(activePod, package, isDuplicate: true, "Duplicate Proof of Delivery ignored (Idempotent).", cancellationToken);
                }
            }
        }

        // 5. Staged Upload Workflow (Outside Database Transaction)
        string? tempSigKey = null;
        string? finalSigKey = null;
        string? sigSha256 = null;

        string? tempPhotoKey = null;
        string? finalPhotoKey = null;
        string? photoSha256 = null;

        var timestampStr = DateTime.UtcNow.ToString("yyyyMMddHHmmss");

        try
        {
            // Stage signature stream if present
            if (request.SignatureStream != null && !string.IsNullOrWhiteSpace(request.SignatureContentType))
            {
                var sigExt = GetFileExtension(request.SignatureContentType);
                tempSigKey = $"tenants/{package.ClientId}/pod/{package.Id}/temp_sig_{Guid.NewGuid():N}{sigExt}";
                var sigResult = await _storageService.UploadStagedAsync(tempSigKey, request.SignatureStream, request.SignatureContentType, cancellationToken);
                sigSha256 = sigResult.Sha256Hash;
                finalSigKey = $"tenants/{package.ClientId}/pod/{package.Id}/signature_{timestampStr}_{sigSha256[..Math.Min(8, sigSha256.Length)]}{sigExt}";
            }

            // Stage photo stream if present
            if (request.PhotoStream != null && !string.IsNullOrWhiteSpace(request.PhotoContentType))
            {
                var photoExt = GetFileExtension(request.PhotoContentType);
                tempPhotoKey = $"tenants/{package.ClientId}/pod/{package.Id}/temp_photo_{Guid.NewGuid():N}{photoExt}";
                var photoResult = await _storageService.UploadStagedAsync(tempPhotoKey, request.PhotoStream, request.PhotoContentType, cancellationToken);
                photoSha256 = photoResult.Sha256Hash;
                finalPhotoKey = $"tenants/{package.ClientId}/pod/{package.Id}/photo_{timestampStr}_{photoSha256[..Math.Min(8, photoSha256.Length)]}{photoExt}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Staged evidence upload failed for package '{Pkg}'", package.PackageNumber);
            // Clean up any successfully uploaded temporary streams
            if (tempSigKey != null) await _storageService.DeleteAsync(tempSigKey, cancellationToken);
            if (tempPhotoKey != null) await _storageService.DeleteAsync(tempPhotoKey, cancellationToken);
            throw;
        }

        // 6. Persist Metadata in Pending State (P745-R01: IsSigned strictly derived from validated signature artifact)
        var pod = new ProofOfDelivery
        {
            ClientId = package.ClientId,
            FacilityId = package.FacilityId,
            PackageId = package.Id,
            ShipmentId = package.ShipmentId,
            TrackingNumber = package.TrackingNumber,
            CarrierCode = package.CarrierCode,
            RecipientName = request.RecipientName?.Trim(),
            DeliveryLocationDescription = request.DeliveryLocationDescription?.Trim(),
            IsSigned = tempSigKey != null,
            SignatureObjectKey = tempSigKey,
            PhotoObjectKey = tempPhotoKey,
            SignatureContentType = request.SignatureContentType?.Trim().ToLowerInvariant(),
            PhotoContentType = request.PhotoContentType?.Trim().ToLowerInvariant(),
            SignatureSha256 = sigSha256,
            PhotoSha256 = photoSha256,
            IsStorageFinalized = false, // Pending promotion
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            CarrierConfirmationNumber = request.CarrierConfirmationNumber?.Trim(),
            CarrierEventId = request.CarrierEventId?.Trim(),
            IdempotencyKey = canonicalIdempKey,
            EvidenceSource = request.EvidenceSource,
            VerificationReason = request.VerificationReason?.Trim(),
            ConsigneeAcknowledgmentText = request.ConsigneeAcknowledgmentText?.Trim(),
            CapturedAtUtc = DateTime.UtcNow,
            DeliveredAtUtc = NormalizeUtc(request.DeliveredAtUtc),
            HasExplicitDeliveredAt = request.HasExplicitDeliveredAtUtc
        };

        ProofOfDelivery? racedDuplicatePod = null;
        try
        {
            pod.Validate();

            await _context.ExecuteWithPackageLockAsync(package.Id, package.ShipmentId, async () =>
            {
                if (request.BeforeCommitCustodyValidationAsync != null)
                {
                    await request.BeforeCommitCustodyValidationAsync(cancellationToken);
                }

                await using var podIdempLock = canonicalIdempKey != null
                    ? await _context.AcquireScopedIdempotencyLockAsync("POD", package.FacilityId, canonicalIdempKey, cancellationToken)
                    : null;

                if (isSupersession)
                {
                    var lockedExistingPod = await _context.ProofOfDeliveries
                        .FirstOrDefaultAsync(p => p.Id == supersedeRequest!.ExistingPodId, cancellationToken)
                        ?? throw new EntityNotFoundException(nameof(ProofOfDelivery), supersedeRequest!.ExistingPodId);

                    if (lockedExistingPod.PackageId != package.Id)
                    {
                        throw new ValidationException(nameof(request.PackageId),
                            "Replacement Proof of Delivery must belong to the same package as the superseded record.");
                    }

                    if (lockedExistingPod.IsSuperseded)
                    {
                        if (canonicalIdempKey != null)
                        {
                            var racedSupIdempPod = await FindFacilityPodByIdempotencyKeyAsync(
                                package.FacilityId,
                                canonicalIdempKey,
                                includePackage: false,
                                cancellationToken);

                            if (racedSupIdempPod != null)
                            {
                                if (IsMatchingSupersededDuplicate(lockedExistingPod, racedSupIdempPod, supersedeRequest!.Reason, request, incomingSigSha256, incomingPhotoSha256))
                                {
                                    racedDuplicatePod = racedSupIdempPod;
                                    return false;
                                }

                                throw new ConcurrencyException(
                                    $"Proof of Delivery idempotency key '{canonicalIdempKey}' has already been consumed in this facility.");
                            }
                        }

                        throw new InvalidDomainStateException(
                            $"Proof of Delivery '{lockedExistingPod.Id}' has already been superseded.");
                    }

                    if (canonicalIdempKey != null)
                    {
                        var consumedPod = await FindFacilityPodByIdempotencyKeyAsync(
                            package.FacilityId,
                            canonicalIdempKey,
                            includePackage: false,
                            cancellationToken);
                        if (consumedPod != null)
                        {
                            throw new ConcurrencyException(
                                $"Proof of Delivery idempotency key '{canonicalIdempKey}' has already been consumed in this facility.");
                        }
                    }

                    var otherActivePod = await _context.ProofOfDeliveries
                        .AsNoTracking()
                        .FirstOrDefaultAsync(p => p.PackageId == package.Id && !p.IsSuperseded && p.Id != lockedExistingPod.Id, cancellationToken);
                    if (otherActivePod != null)
                    {
                        throw new InvalidDomainStateException(
                            $"Package '{package.PackageNumber}' already has an active Proof of Delivery ('{otherActivePod.Id}') different from '{lockedExistingPod.Id}'.");
                    }

                    var prevSuperseded = lockedExistingPod.IsSuperseded;
                    var prevSupersededBy = lockedExistingPod.SupersededByPodId;
                    var prevSupersededReason = lockedExistingPod.SupersededReason;
                    var prevSupersededAt = lockedExistingPod.SupersededAtUtc;

                    lockedExistingPod.Supersede(pod.Id, supersedeRequest!.Reason, DateTime.UtcNow);
                    try
                    {
                        // Write ordering: UPDATE existing POD first inside the transaction so PostgreSQL partial unique index
                        // ix_proof_of_deliveries_package_unique (WHERE is_superseded = false) allows the replacement INSERT.
                        await _context.SaveChangesAsync(cancellationToken);

                        _context.ProofOfDeliveries.Add(pod);
                        await _context.SaveChangesAsync(cancellationToken);
                    }
                    catch
                    {
                        if (_context is DbContext efContext)
                        {
                            var podEntry = efContext.Entry(pod);
                            if (podEntry.State != EntityState.Detached)
                            {
                                podEntry.State = EntityState.Detached;
                            }
                        }

                        lockedExistingPod.IsSuperseded = prevSuperseded;
                        lockedExistingPod.SupersededByPodId = prevSupersededBy;
                        lockedExistingPod.SupersededReason = prevSupersededReason;
                        lockedExistingPod.SupersededAtUtc = prevSupersededAt;

                        if (!_context.IsRelationalDatabase)
                        {
                            await _context.SaveChangesAsync(CancellationToken.None);
                        }

                        throw;
                    }
                }
                else
                {
                    if (canonicalIdempKey != null)
                    {
                        var racedIdempPod = await FindFacilityPodByIdempotencyKeyAsync(
                            package.FacilityId,
                            canonicalIdempKey,
                            includePackage: false,
                            cancellationToken);
                        if (racedIdempPod != null)
                        {
                            EnsureIdempotencyKeyReusableForSubmission(racedIdempPod, package.Id, canonicalIdempKey, isSupersession: false);
                        }
                    }

                    var racedActivePod = await _context.ProofOfDeliveries
                        .FirstOrDefaultAsync(p => p.PackageId == package.Id && !p.IsSuperseded, cancellationToken);

                    if (racedActivePod != null)
                    {
                        if (EvaluateExistingPodCollision(racedActivePod, request, canonicalIdempKey, incomingSigSha256, incomingPhotoSha256))
                        {
                            racedDuplicatePod = racedActivePod;
                            return false;
                        }
                    }

                    _context.ProofOfDeliveries.Add(pod);
                    await _context.SaveChangesAsync(cancellationToken);
                }

                return true;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist ProofOfDelivery metadata for package '{Pkg}'. Cleaning up staged objects.", package.PackageNumber);
            await TryDeleteTempObjectsAsync(tempSigKey, tempPhotoKey);
            throw;
        }

        if (racedDuplicatePod != null)
        {
            await TryDeleteTempObjectsAsync(tempSigKey, tempPhotoKey);
            return await BuildSubmissionResultAsync(racedDuplicatePod, package, isDuplicate: true, "Duplicate Proof of Delivery ignored (Idempotent).", cancellationToken);
        }

        // 7. Promote Staged Objects to Final Authoritative Keys
        bool finalizationSucceeded = true;
        try
        {
            if (tempSigKey != null && finalSigKey != null)
            {
                await _storageService.FinalizeObjectAsync(tempSigKey, finalSigKey, cancellationToken);
                pod.SignatureObjectKey = finalSigKey;
            }

            if (tempPhotoKey != null && finalPhotoKey != null)
            {
                await _storageService.FinalizeObjectAsync(tempPhotoKey, finalPhotoKey, cancellationToken);
                pod.PhotoObjectKey = finalPhotoKey;
            }

            pod.IsStorageFinalized = true;
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            finalizationSucceeded = false;
            _logger.LogWarning(ex, "Storage finalization promotion failed for POD '{PodId}'. Record preserved in pending state for reconciliation.", pod.Id);
        }

        _logger.LogInformation("Ingested Proof of Delivery '{PodId}' for package '{Pkg}' (Source: {Source}, Finalized: {Finalized})",
            pod.Id, package.PackageNumber, pod.EvidenceSource, finalizationSucceeded);

        return await BuildSubmissionResultAsync(pod, package, isDuplicate: false, "Proof of Delivery submitted successfully.", cancellationToken);
    }

    public async Task<ProofOfDeliveryDto?> GetProofOfDeliveryByPackageIdAsync(
        Guid packageId,
        TimeSpan? urlExpiry = null,
        CancellationToken cancellationToken = default)
    {
        var pod = await _context.ProofOfDeliveries
            .Include(p => p.Package)
            .FirstOrDefaultAsync(p => p.PackageId == packageId && !p.IsSuperseded, cancellationToken);

        if (pod == null)
        {
            return null;
        }

        return await MapToDtoAsync(pod, urlExpiry, cancellationToken);
    }

    public async Task<ProofOfDeliveryDto?> GetProofOfDeliveryByIdAsync(
        Guid podId,
        TimeSpan? urlExpiry = null,
        CancellationToken cancellationToken = default)
    {
        var pod = await _context.ProofOfDeliveries
            .Include(p => p.Package)
            .FirstOrDefaultAsync(p => p.Id == podId, cancellationToken);

        if (pod == null)
        {
            return null;
        }

        return await MapToDtoAsync(pod, urlExpiry, cancellationToken);
    }

    public async Task<int> ReconcilePendingStorageAsync(
        TimeSpan olderThan,
        CancellationToken cancellationToken = default)
    {
        var threshold = DateTime.UtcNow - olderThan;

        var unfinalizedPods = await _context.ProofOfDeliveries
            .Where(p => !p.IsStorageFinalized && p.CreatedAt <= threshold)
            .ToListAsync(cancellationToken);

        int reconciledCount = 0;
        foreach (var pod in unfinalizedPods)
        {
            bool sigOk = true;
            bool photoOk = true;

            if (!string.IsNullOrEmpty(pod.SignatureObjectKey))
            {
                sigOk = await _storageService.ExistsAsync(pod.SignatureObjectKey, cancellationToken);
            }

            if (!string.IsNullOrEmpty(pod.PhotoObjectKey))
            {
                photoOk = await _storageService.ExistsAsync(pod.PhotoObjectKey, cancellationToken);
            }

            if (sigOk && photoOk)
            {
                pod.IsStorageFinalized = true;
                reconciledCount++;
            }
        }

        if (reconciledCount > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        // Also clean up any unreferenced temporary objects
        await _storageService.CleanupOrphanedTempObjectsAsync(olderThan, cancellationToken);

        return reconciledCount;
    }

    #region Helpers & Validations

    private async Task TryDeleteTempObjectsAsync(string? tempSigKey, string? tempPhotoKey)
    {
        if (tempSigKey != null)
        {
            try { await _storageService.DeleteAsync(tempSigKey, CancellationToken.None); } catch { /* best-effort cleanup */ }
        }
        if (tempPhotoKey != null)
        {
            try { await _storageService.DeleteAsync(tempPhotoKey, CancellationToken.None); } catch { /* best-effort cleanup */ }
        }
    }

    private void ValidateSourceSpecificRules(SubmitProofOfDeliveryRequest request)
    {
        switch (request.EvidenceSource)
        {
            case PodEvidenceSource.CarrierApi:
                if (string.IsNullOrWhiteSpace(request.CarrierConfirmationNumber) &&
                    string.IsNullOrWhiteSpace(request.CarrierEventId))
                {
                    throw new ValidationException(nameof(request.CarrierConfirmationNumber),
                        "Carrier API delivery confirmation requires either a carrier confirmation number or carrier event ID.");
                }
                break;

            case PodEvidenceSource.DriverHandheld:
                bool hasSignature = request.SignatureStream != null;
                bool hasPhoto = request.PhotoStream != null;
                bool hasRecipient = !string.IsNullOrWhiteSpace(request.RecipientName);

                if (!hasSignature && !hasPhoto && !hasRecipient)
                {
                    throw new ValidationException(nameof(request.EvidenceSource),
                        "Driver handheld delivery confirmation requires at least one of: signature, photo, or recipient name.");
                }
                break;

            case PodEvidenceSource.ConsigneePortal:
                if (string.IsNullOrWhiteSpace(request.ConsigneeAcknowledgmentText) &&
                    string.IsNullOrWhiteSpace(request.RecipientName))
                {
                    throw new ValidationException(nameof(request.ConsigneeAcknowledgmentText),
                        "Consignee portal confirmation requires recipient acknowledgment or name.");
                }
                break;

            case PodEvidenceSource.ManualVerification:
                if (string.IsNullOrWhiteSpace(request.VerificationReason))
                {
                    throw new ValidationException(nameof(request.VerificationReason),
                        "Manual verification requires a verified operational reason.");
                }

                if (_currentUserContext.UserType == UserType.Client)
                {
                    throw new ForbiddenException("Manual verification can only be performed by authorized warehouse operations staff.");
                }
                break;

            default:
                throw new ValidationException(nameof(request.EvidenceSource), $"Unsupported evidence source '{request.EvidenceSource}'.");
        }
    }

    private static void ValidateEvidenceContentTypes(SubmitProofOfDeliveryRequest request)
    {
        if (request.SignatureStream != null)
        {
            if (string.Equals(request.SignatureContentType, "image/svg+xml", StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(nameof(request.SignatureContentType),
                    "SVG content type is strictly prohibited for Proof of Delivery evidence.");
            }

            if (string.IsNullOrWhiteSpace(request.SignatureContentType) ||
                !AllowedMimeTypes.Contains(request.SignatureContentType))
            {
                throw new ValidationException(nameof(request.SignatureContentType),
                    $"Signature content type '{request.SignatureContentType}' is not permitted. Allowed: image/png, image/jpeg, image/webp.");
            }
        }

        if (request.PhotoStream != null)
        {
            if (string.Equals(request.PhotoContentType, "image/svg+xml", StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(nameof(request.PhotoContentType),
                    "SVG content type is strictly prohibited for Proof of Delivery evidence.");
            }

            if (string.IsNullOrWhiteSpace(request.PhotoContentType) ||
                !AllowedMimeTypes.Contains(request.PhotoContentType))
            {
                throw new ValidationException(nameof(request.PhotoContentType),
                    $"Photo content type '{request.PhotoContentType}' is not permitted. Allowed: image/png, image/jpeg, image/webp, application/pdf.");
            }
        }
    }

    private static string GetFileExtension(string contentType)
    {
        return contentType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/webp" => ".webp",
            "application/pdf" => ".pdf",
            _ => ".bin"
        };
    }

    private async Task<SubmitProofOfDeliveryResult> BuildSubmissionResultAsync(
        ProofOfDelivery pod,
        Package package,
        bool isDuplicate,
        string message,
        CancellationToken cancellationToken)
    {
        string? preSignedSigUrl = null;
        string? preSignedPhotoUrl = null;
        DateTime? expiresAtUtc = null;

        if (!string.IsNullOrEmpty(pod.SignatureObjectKey))
        {
            var sigUrlResult = await _storageService.GeneratePreSignedDownloadUrlAsync(pod.SignatureObjectKey, cancellationToken: cancellationToken);
            preSignedSigUrl = sigUrlResult.DownloadUrl;
            expiresAtUtc = sigUrlResult.ExpiresAtUtc;
        }

        if (!string.IsNullOrEmpty(pod.PhotoObjectKey))
        {
            var photoUrlResult = await _storageService.GeneratePreSignedDownloadUrlAsync(pod.PhotoObjectKey, cancellationToken: cancellationToken);
            preSignedPhotoUrl = photoUrlResult.DownloadUrl;
            expiresAtUtc = photoUrlResult.ExpiresAtUtc;
        }

        return new SubmitProofOfDeliveryResult
        {
            IsSuccess = true,
            IsDuplicate = isDuplicate,
            ProofOfDeliveryId = pod.Id,
            PackageId = package.Id,
            PackageNumber = package.PackageNumber,
            TrackingNumber = pod.TrackingNumber,
            CarrierCode = pod.CarrierCode,
            RecipientName = pod.RecipientName,
            DeliveryLocationDescription = pod.DeliveryLocationDescription,
            IsSigned = pod.IsSigned,
            EvidenceSource = pod.EvidenceSource,
            VerificationReason = pod.VerificationReason,
            ConsigneeAcknowledgmentText = pod.ConsigneeAcknowledgmentText,
            CapturedAtUtc = pod.CapturedAtUtc,
            DeliveredAtUtc = pod.DeliveredAtUtc,
            HasExplicitDeliveredAt = pod.HasExplicitDeliveredAt,
            IsStorageFinalized = pod.IsStorageFinalized,
            SignatureSha256 = pod.SignatureSha256,
            PhotoSha256 = pod.PhotoSha256,
            PreSignedSignatureUrl = preSignedSigUrl,
            PreSignedPhotoUrl = preSignedPhotoUrl,
            PreSignedUrlsExpireAtUtc = expiresAtUtc,
            Message = message
        };
    }

    private static async Task<string?> ComputeStreamSha256Async(Stream? stream, CancellationToken cancellationToken)
    {
        if (stream == null)
        {
            return null;
        }

        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        using var sha = SHA256.Create();
        var hashBytes = await sha.ComputeHashAsync(stream, cancellationToken);

        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        return Convert.ToHexStringLower(hashBytes);
    }

    private Task<ProofOfDelivery?> FindFacilityPodByIdempotencyKeyAsync(
        Guid facilityId,
        string canonicalIdempKey,
        bool includePackage,
        CancellationToken cancellationToken)
    {
        IQueryable<ProofOfDelivery> query = _context.ProofOfDeliveries.AsNoTracking();
        if (includePackage)
        {
            query = query.Include(p => p.Package);
        }

        return query.FirstOrDefaultAsync(
            p => p.FacilityId == facilityId &&
                 p.IdempotencyKey != null &&
                 p.IdempotencyKey.ToUpper() == canonicalIdempKey,
            cancellationToken);
    }

    private static void EnsureIdempotencyKeyReusableForSubmission(
        ProofOfDelivery existingIdempPod,
        Guid targetPackageId,
        string canonicalIdempKey,
        bool isSupersession)
    {
        if (isSupersession)
        {
            throw new ConcurrencyException(
                $"Proof of Delivery idempotency key '{canonicalIdempKey}' has already been consumed in this facility.");
        }

        if (existingIdempPod.PackageId != targetPackageId)
        {
            throw new ConcurrencyException(
                $"Proof of Delivery idempotency key '{canonicalIdempKey}' has already been consumed by a different package in this facility.");
        }

        if (existingIdempPod.IsSuperseded)
        {
            throw new ConcurrencyException(
                $"Proof of Delivery idempotency key '{canonicalIdempKey}' has already been consumed by a historical Proof of Delivery in this facility.");
        }
    }

    private static bool IsMatchingSupersededDuplicate(
        ProofOfDelivery supersededOriginalPod,
        ProofOfDelivery candidateReplacementPod,
        string supersessionReason,
        SubmitProofOfDeliveryRequest replacementRequest,
        string? incomingSigSha256,
        string? incomingPhotoSha256) =>
        supersededOriginalPod.SupersededByPodId == candidateReplacementPod.Id &&
        TextEq(supersededOriginalPod.SupersededReason, supersessionReason) &&
        HasMatchingEvidencePayload(candidateReplacementPod, replacementRequest, incomingSigSha256, incomingPhotoSha256);

    private static string? NormalizeIdempotencyKey(string? idempotencyKey) =>
        string.IsNullOrWhiteSpace(idempotencyKey)
            ? null
            : idempotencyKey.Trim().ToUpperInvariant();

    private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static bool TextEq(string? a, string? b) =>
        string.Equals(a?.Trim() ?? string.Empty, b?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    private static bool TimestampsMatch(DateTime existingUtc, DateTime incomingUtc) =>
        Math.Abs((NormalizeUtc(existingUtc) - NormalizeUtc(incomingUtc)).Ticks) < TimeSpan.TicksPerMicrosecond;

    private static bool EvaluateExistingPodCollision(
        ProofOfDelivery activePod,
        SubmitProofOfDeliveryRequest request,
        string? canonicalIdempKey,
        string? incomingSigSha256,
        string? incomingPhotoSha256)
    {
        bool idempMatches = canonicalIdempKey != null &&
            string.Equals(NormalizeIdempotencyKey(activePod.IdempotencyKey), canonicalIdempKey, StringComparison.Ordinal);

        bool carrierRefMatches = activePod.EvidenceSource == request.EvidenceSource &&
            ((!string.IsNullOrWhiteSpace(activePod.CarrierConfirmationNumber) &&
              !string.IsNullOrWhiteSpace(request.CarrierConfirmationNumber) &&
              string.Equals(activePod.CarrierConfirmationNumber.Trim(), request.CarrierConfirmationNumber.Trim(), StringComparison.OrdinalIgnoreCase)) ||
             (!string.IsNullOrWhiteSpace(activePod.CarrierEventId) &&
              !string.IsNullOrWhiteSpace(request.CarrierEventId) &&
              string.Equals(activePod.CarrierEventId.Trim(), request.CarrierEventId.Trim(), StringComparison.OrdinalIgnoreCase)));

        if (idempMatches || carrierRefMatches)
        {
            if (HasMatchingEvidencePayload(activePod, request, incomingSigSha256, incomingPhotoSha256))
            {
                return true;
            }

            throw new ConcurrencyException(
                $"Conflicting Proof of Delivery evidence submitted for package '{activePod.PackageId}' under existing idempotency or carrier event identifier.");
        }

        if (activePod.HasConflictWith(request.RecipientName, request.CarrierConfirmationNumber, request.EvidenceSource))
        {
            throw new ValidationException(nameof(request.PackageId),
                $"Contradictory delivery confirmation submitted for package '{activePod.PackageId}'. Existing confirmation '{activePod.CarrierConfirmationNumber}', recipient '{activePod.RecipientName}'.");
        }

        throw new ValidationException(nameof(request.PackageId),
            $"Package '{activePod.PackageId}' already has an active authoritative Proof of Delivery ('{activePod.Id}'). An explicit supersession request is required to amend delivery evidence.");
    }

    private static bool HasMatchingEvidencePayload(
        ProofOfDelivery existingPod,
        SubmitProofOfDeliveryRequest incoming,
        string? incomingSigSha256,
        string? incomingPhotoSha256)
    {
        bool incomingSigned = incomingSigSha256 != null;

        bool deliveredAtMatches = existingPod.HasExplicitDeliveredAt == incoming.HasExplicitDeliveredAtUtc &&
            (!incoming.HasExplicitDeliveredAtUtc || TimestampsMatch(existingPod.DeliveredAtUtc, incoming.DeliveredAtUtc));

        return existingPod.EvidenceSource == incoming.EvidenceSource &&
            existingPod.IsSigned == incomingSigned &&
            TextEq(existingPod.RecipientName, incoming.RecipientName) &&
            TextEq(existingPod.DeliveryLocationDescription, incoming.DeliveryLocationDescription) &&
            TextEq(existingPod.SignatureSha256, incomingSigSha256) &&
            TextEq(existingPod.PhotoSha256, incomingPhotoSha256) &&
            existingPod.Latitude == incoming.Latitude &&
            existingPod.Longitude == incoming.Longitude &&
            TextEq(existingPod.CarrierConfirmationNumber, incoming.CarrierConfirmationNumber) &&
            TextEq(existingPod.CarrierEventId, incoming.CarrierEventId) &&
            TextEq(existingPod.VerificationReason, incoming.VerificationReason) &&
            TextEq(existingPod.ConsigneeAcknowledgmentText, incoming.ConsigneeAcknowledgmentText) &&
            deliveredAtMatches;
    }

    private async Task<ProofOfDeliveryDto> MapToDtoAsync(
        ProofOfDelivery pod,
        TimeSpan? urlExpiry,
        CancellationToken cancellationToken)
    {
        string? preSignedSigUrl = null;
        string? preSignedPhotoUrl = null;
        DateTime? expiresAtUtc = null;

        if (!string.IsNullOrEmpty(pod.SignatureObjectKey))
        {
            var sigUrlResult = await _storageService.GeneratePreSignedDownloadUrlAsync(pod.SignatureObjectKey, urlExpiry, cancellationToken);
            preSignedSigUrl = sigUrlResult.DownloadUrl;
            expiresAtUtc = sigUrlResult.ExpiresAtUtc;
        }

        if (!string.IsNullOrEmpty(pod.PhotoObjectKey))
        {
            var photoUrlResult = await _storageService.GeneratePreSignedDownloadUrlAsync(pod.PhotoObjectKey, urlExpiry, cancellationToken);
            preSignedPhotoUrl = photoUrlResult.DownloadUrl;
            expiresAtUtc = photoUrlResult.ExpiresAtUtc;
        }

        return new ProofOfDeliveryDto
        {
            Id = pod.Id,
            ClientId = pod.ClientId,
            FacilityId = pod.FacilityId,
            PackageId = pod.PackageId,
            PackageNumber = pod.Package?.PackageNumber ?? string.Empty,
            ShipmentId = pod.ShipmentId,
            TrackingNumber = pod.TrackingNumber,
            CarrierCode = pod.CarrierCode,
            RecipientName = pod.RecipientName,
            DeliveryLocationDescription = pod.DeliveryLocationDescription,
            IsSigned = pod.IsSigned,
            IsStorageFinalized = pod.IsStorageFinalized,
            PreSignedSignatureUrl = preSignedSigUrl,
            PreSignedPhotoUrl = preSignedPhotoUrl,
            SignatureSha256 = pod.SignatureSha256,
            PhotoSha256 = pod.PhotoSha256,
            Latitude = pod.Latitude,
            Longitude = pod.Longitude,
            CarrierConfirmationNumber = pod.CarrierConfirmationNumber,
            CarrierEventId = pod.CarrierEventId,
            IdempotencyKey = pod.IdempotencyKey,
            EvidenceSource = pod.EvidenceSource,
            VerificationReason = pod.VerificationReason,
            ConsigneeAcknowledgmentText = pod.ConsigneeAcknowledgmentText,
            CapturedAtUtc = pod.CapturedAtUtc,
            DeliveredAtUtc = pod.DeliveredAtUtc,
            HasExplicitDeliveredAt = pod.HasExplicitDeliveredAt,
            IsSuperseded = pod.IsSuperseded,
            SupersededByPodId = pod.SupersededByPodId,
            SupersededReason = pod.SupersededReason,
            SupersededAtUtc = pod.SupersededAtUtc,
            PreSignedUrlsExpireAtUtc = expiresAtUtc
        };
    }

    #endregion
}
