using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services.Shipping;

public class DeliveryExceptionService : IDeliveryExceptionService
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ICarrierExceptionClassificationService _classificationService;
    private readonly ILogger<DeliveryExceptionService> _logger;

    public DeliveryExceptionService(
        IApplicationDbContext context,
        ITenantProvider tenantProvider,
        ICurrentUserContext currentUserContext,
        ICarrierExceptionClassificationService classificationService,
        ILogger<DeliveryExceptionService> logger)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _currentUserContext = currentUserContext;
        _classificationService = classificationService;
        _logger = logger;
    }

    public async Task<DeliveryExceptionResult> IngestCarrierExceptionAsync(
        IngestCarrierExceptionRequest request,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate Input Payload
        if (request == null)
        {
            throw new ValidationException(nameof(request), "Ingest request cannot be null.");
        }

        if (string.IsNullOrWhiteSpace(request.CarrierCode))
        {
            throw new ValidationException(nameof(request.CarrierCode), "Carrier code is required.");
        }

        if (string.IsNullOrWhiteSpace(request.TrackingNumber))
        {
            throw new ValidationException(nameof(request.TrackingNumber), "Tracking number is required.");
        }

        if (string.IsNullOrWhiteSpace(request.CarrierExceptionCode) && string.IsNullOrWhiteSpace(request.CarrierDescription))
        {
            throw new ValidationException(nameof(request.CarrierExceptionCode), "Carrier exception code or description must be supplied.");
        }

        if (request.OccurredAtUtc == default)
        {
            throw new ValidationException(nameof(request.OccurredAtUtc), "Valid occurrence timestamp is required.");
        }

        if (request.OccurredAtUtc > DateTime.UtcNow.AddMinutes(15))
        {
            throw new ValidationException(nameof(request.OccurredAtUtc), "Occurrence timestamp cannot be in the future.");
        }

        var normalizedCarrier = request.CarrierCode.Trim().ToUpperInvariant();
        var normalizedTracking = request.TrackingNumber.Trim();

        // 2. Lookup Package by Tracking Number
        var package = await _context.Packages
            .Include(p => p.Shipment)
            .Include(p => p.OutboundOrder)
                .ThenInclude(o => o.Packages)
            .FirstOrDefaultAsync(p => p.TrackingNumber == normalizedTracking && !p.IsDeleted, cancellationToken);

        if (package == null)
        {
            throw new EntityNotFoundException(nameof(Package), normalizedTracking);
        }

        // Validate carrier ownership
        if (!string.IsNullOrWhiteSpace(package.CarrierCode) &&
            !string.Equals(package.CarrierCode.Trim(), normalizedCarrier, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(nameof(request.CarrierCode),
                $"Carrier '{request.CarrierCode}' does not match package assigned carrier '{package.CarrierCode}' for tracking number '{normalizedTracking}'.");
        }

        // Cross-Tenant Validation
        if (request.TenantIdOverride.HasValue && request.TenantIdOverride.Value != package.ClientId)
        {
            throw new UnauthorizedException($"Tracking number '{normalizedTracking}' belongs to a different client tenant. Cross-tenant access rejected.");
        }

        if (_tenantProvider.TenantId.HasValue &&
            !_tenantProvider.BypassTenantFilter &&
            _tenantProvider.TenantId.Value != package.ClientId)
        {
            throw new UnauthorizedException("Unauthorized cross-tenant delivery exception ingestion attempt.");
        }

        // 3. Deduplication Check via SHA-256 Fingerprint
        var fingerprint = ShipmentDeliveryException.ComputeEventFingerprint(
            package.ClientId,
            normalizedCarrier,
            normalizedTracking,
            request.CarrierEventId,
            request.CarrierExceptionCode,
            request.OccurredAtUtc);

        var existingException = await _context.ShipmentDeliveryExceptions
            .FirstOrDefaultAsync(e => e.ClientId == package.ClientId &&
                                      e.CarrierCode == normalizedCarrier &&
                                      e.EventFingerprint == fingerprint, cancellationToken);

        if (existingException != null)
        {
            _logger.LogInformation("Duplicate delivery exception detected for Package '{Pkg}', Tracking '{Trk}', Fingerprint '{Fp}'. Returning idempotent result.",
                package.PackageNumber, normalizedTracking, fingerprint);

            return new DeliveryExceptionResult
            {
                IsSuccess = true,
                IsDuplicate = true,
                ExceptionId = existingException.Id,
                PackageId = existingException.PackageId,
                ShipmentId = existingException.ShipmentId,
                OrderId = existingException.OrderId ?? package.OutboundOrderId,
                TrackingNumber = existingException.TrackingNumber,
                CarrierCode = existingException.CarrierCode,
                ExceptionType = existingException.ExceptionType,
                Status = existingException.Status,
                Severity = existingException.Severity,
                PackageTrackingStatus = package.CurrentTrackingStatus,
                OrderDeliveryStatus = package.OutboundOrder?.DeliveryStatus ?? OrderDeliveryStatus.Dispatched,
                Message = "Duplicate delivery exception ignored (Idempotent)."
            };
        }

        // 4. Classify Exception Code and Severity
        var classifiedType = _classificationService.ClassifyException(
            normalizedCarrier,
            request.CarrierExceptionCode,
            request.CarrierDescription);

        var severity = ShipmentDeliveryException.DetermineDefaultSeverity(classifiedType);

        // 5. Create ShipmentDeliveryException Record
        var deliveryException = new ShipmentDeliveryException
        {
            ClientId = package.ClientId,
            FacilityId = package.FacilityId,
            PackageId = package.Id,
            ShipmentId = package.ShipmentId,
            OrderId = package.OutboundOrderId,
            TrackingNumber = normalizedTracking,
            CarrierCode = normalizedCarrier,
            ExceptionType = classifiedType,
            Status = DeliveryExceptionStatus.Open,
            Severity = severity,
            CarrierExceptionCode = request.CarrierExceptionCode.Trim(),
            CarrierDescription = string.IsNullOrWhiteSpace(request.CarrierDescription) ? request.CarrierExceptionCode.Trim() : request.CarrierDescription.Trim(),
            OccurredAtUtc = request.OccurredAtUtc,
            Source = request.Source,
            CarrierEventId = request.CarrierEventId?.Trim(),
            RawPayload = request.RawPayloadJson,
            EventFingerprint = fingerprint
        };

        deliveryException.ValidateRelationships(package, package.Shipment, package.OutboundOrder);
        _context.ShipmentDeliveryExceptions.Add(deliveryException);

        // 6. Append Immutable Tracking Event
        var trackingFp = ComputeTrackingFingerprint(package.ClientId, normalizedCarrier, normalizedTracking, request.CarrierEventId, $"EXC_{request.CarrierExceptionCode}", request.OccurredAtUtc);
        var existingTracking = await _context.ShipmentTrackingEvents
            .AnyAsync(t => t.ClientId == package.ClientId && t.EventFingerprint == trackingFp, cancellationToken);

        if (!existingTracking)
        {
            var trackingEvent = new ShipmentTrackingEvent
            {
                ClientId = package.ClientId,
                FacilityId = package.FacilityId,
                PackageId = package.Id,
                ShipmentId = package.ShipmentId,
                TrackingNumber = normalizedTracking,
                CarrierCode = normalizedCarrier,
                CarrierEventCode = request.CarrierExceptionCode.Trim(),
                MilestoneStatus = TrackingMilestoneStatus.DeliveryException,
                CarrierDescription = deliveryException.CarrierDescription,
                EventCity = request.EventCity?.Trim(),
                EventStateOrProvince = request.EventStateOrProvince?.Trim(),
                OccurredAtUtc = request.OccurredAtUtc,
                ReceivedAtUtc = DateTime.UtcNow,
                EventFingerprint = trackingFp
            };
            _context.ShipmentTrackingEvents.Add(trackingEvent);
        }

        // 7. Update Package Tracking Milestone & Rollup
        package.CurrentTrackingStatus = TrackingMilestoneStatus.DeliveryException;
        package.TrackingStatusUpdatedAtUtc = DateTime.UtcNow;

        if (package.Shipment != null)
        {
            package.Shipment.RecalculateDeliveryStatus();
        }

        if (package.OutboundOrder != null)
        {
            package.OutboundOrder.RecalculateDeliveryStatus();
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _logger.LogWarning(ex, "Concurrent duplicate delivery exception ingestion detected for fingerprint '{Fp}'. Recovering idempotently.", fingerprint);
            var recovered = await _context.ShipmentDeliveryExceptions
                .FirstOrDefaultAsync(e => e.ClientId == package.ClientId &&
                                          e.CarrierCode == normalizedCarrier &&
                                          e.EventFingerprint == fingerprint, cancellationToken);

            if (recovered != null)
            {
                return new DeliveryExceptionResult
                {
                    IsSuccess = true,
                    IsDuplicate = true,
                    ExceptionId = recovered.Id,
                    PackageId = recovered.PackageId,
                    ShipmentId = recovered.ShipmentId,
                    OrderId = recovered.OrderId ?? package.OutboundOrderId,
                    TrackingNumber = recovered.TrackingNumber,
                    CarrierCode = recovered.CarrierCode,
                    ExceptionType = recovered.ExceptionType,
                    Status = recovered.Status,
                    Severity = recovered.Severity,
                    PackageTrackingStatus = package.CurrentTrackingStatus,
                    OrderDeliveryStatus = package.OutboundOrder?.DeliveryStatus ?? OrderDeliveryStatus.Dispatched,
                    Message = "Duplicate delivery exception resolved via concurrent race recovery (Idempotent)."
                };
            }
            throw;
        }

        return new DeliveryExceptionResult
        {
            IsSuccess = true,
            IsDuplicate = false,
            ExceptionId = deliveryException.Id,
            PackageId = package.Id,
            ShipmentId = package.ShipmentId,
            OrderId = package.OutboundOrderId,
            TrackingNumber = normalizedTracking,
            CarrierCode = normalizedCarrier,
            ExceptionType = classifiedType,
            Status = deliveryException.Status,
            Severity = severity,
            PackageTrackingStatus = package.CurrentTrackingStatus,
            OrderDeliveryStatus = package.OutboundOrder?.DeliveryStatus ?? OrderDeliveryStatus.DeliveryException,
            Message = "Carrier delivery exception ingested successfully."
        };
    }

    public async Task<DeliveryExceptionResult> ReportDriverExceptionAsync(
        ReportDriverExceptionRequest request,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate Input Payload
        if (request == null)
        {
            throw new ValidationException(nameof(request), "Report request cannot be null.");
        }

        if (request.PackageId == Guid.Empty)
        {
            throw new ValidationException(nameof(request.PackageId), "Package ID must be provided.");
        }

        if (string.IsNullOrWhiteSpace(request.DriverNotes))
        {
            throw new ValidationException(nameof(request.DriverNotes), "Driver notes are required when reporting a delivery exception.");
        }

        if (request.OccurredAtUtc == default)
        {
            throw new ValidationException(nameof(request.OccurredAtUtc), "Valid occurrence timestamp is required.");
        }

        if (request.OccurredAtUtc > DateTime.UtcNow.AddMinutes(15))
        {
            throw new ValidationException(nameof(request.OccurredAtUtc), "Occurrence timestamp cannot be in the future.");
        }

        if (request.Latitude.HasValue && (request.Latitude.Value < -90m || request.Latitude.Value > 90m))
        {
            throw new ValidationException(nameof(request.Latitude), "Latitude must be between -90 and 90 degrees.");
        }

        if (request.Longitude.HasValue && (request.Longitude.Value < -180m || request.Longitude.Value > 180m))
        {
            throw new ValidationException(nameof(request.Longitude), "Longitude must be between -180 and 180 degrees.");
        }

        // 2. Authentication & Authorization Check
        var driverUserId = request.DriverUserId ?? _currentUserContext.UserId;
        if (!_currentUserContext.IsStaff && _currentUserContext.UserType != UserType.Driver)
        {
            throw new ForbiddenException("Only authorized drivers or warehouse operations staff can report delivery exceptions.");
        }

        var shipmentIdForLock = await _context.Packages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.Id == request.PackageId && !p.IsDeleted)
            .Select(p => p.ShipmentId)
            .FirstOrDefaultAsync(cancellationToken);

        return await _context.ExecuteWithShipmentLockAsync(shipmentIdForLock, async () =>
        {
            if (request.BeforeCommitCustodyValidationAsync != null)
            {
                await request.BeforeCommitCustodyValidationAsync(cancellationToken);
            }

            // 3. Lookup Package with Shipment and Order
            var package = await _context.Packages
                .Include(p => p.Shipment)
                .Include(p => p.OutboundOrder)
                    .ThenInclude(o => o.Packages)
                .FirstOrDefaultAsync(p => p.Id == request.PackageId && !p.IsDeleted, cancellationToken);

            if (package == null)
            {
                throw new EntityNotFoundException(nameof(Package), request.PackageId);
            }

            // Cross-Tenant Validation
            if (request.TenantIdOverride.HasValue && request.TenantIdOverride.Value != package.ClientId)
            {
                throw new UnauthorizedException($"Package '{package.PackageNumber}' belongs to a different client tenant. Cross-tenant access rejected.");
            }

            if (_tenantProvider.TenantId.HasValue &&
                !_tenantProvider.BypassTenantFilter &&
                _tenantProvider.TenantId.Value != package.ClientId)
            {
                throw new UnauthorizedException("Unauthorized cross-tenant driver exception reporting attempt.");
            }

            // Facility Authorization
            if (_currentUserContext.FacilityId.HasValue &&
                _currentUserContext.FacilityId.Value != package.FacilityId &&
                !_currentUserContext.IsSuperAdmin)
            {
                throw new ForbiddenException($"User is not authorized for package facility '{package.FacilityId}'.");
            }

            // Package State Validation: Cannot report exception on cancelled or open package
            if (package.Status == PackageStatus.Cancelled || package.Status == PackageStatus.Open)
            {
                throw new InvalidDomainStateException($"Cannot report exception for package '{package.PackageNumber}' in status '{package.Status}'.");
            }

            // 4. Create Driver Delivery Exception
            var severity = ShipmentDeliveryException.DetermineDefaultSeverity(request.ExceptionType);
            var carrierCode = string.IsNullOrWhiteSpace(package.CarrierCode) ? "DRIVER" : package.CarrierCode.Trim().ToUpperInvariant();
            var trackingNumber = package.TrackingNumber ?? package.PackageNumber;

            var driverFingerprint = ShipmentDeliveryException.ComputeEventFingerprint(
                package.ClientId,
                carrierCode,
                trackingNumber,
                $"DRIVER_{driverUserId}_{request.OccurredAtUtc:yyyyMMddHHmmss}",
                request.ExceptionType.ToString(),
                request.OccurredAtUtc);

            var deliveryException = new ShipmentDeliveryException
            {
                ClientId = package.ClientId,
                FacilityId = package.FacilityId,
                PackageId = package.Id,
                ShipmentId = package.ShipmentId,
                OrderId = package.OutboundOrderId,
                TrackingNumber = trackingNumber,
                CarrierCode = carrierCode,
                ExceptionType = request.ExceptionType,
                Status = DeliveryExceptionStatus.Open,
                Severity = severity,
                CarrierExceptionCode = "DRIVER_REPORTED",
                CarrierDescription = request.DriverNotes.Trim(),
                OccurredAtUtc = request.OccurredAtUtc,
                Source = DeliveryExceptionSource.DriverHandheld,
                ReportedByUserId = driverUserId,
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                EventFingerprint = driverFingerprint
            };

            deliveryException.ValidateRelationships(package, package.Shipment, package.OutboundOrder);
            _context.ShipmentDeliveryExceptions.Add(deliveryException);

            // 5. Append Immutable Tracking Event
            var trackingFp = ComputeTrackingFingerprint(package.ClientId, carrierCode, trackingNumber, null, $"DRIVER_EXC_{request.OccurredAtUtc.Ticks}", request.OccurredAtUtc);
            _context.ShipmentTrackingEvents.Add(new ShipmentTrackingEvent
            {
                ClientId = package.ClientId,
                FacilityId = package.FacilityId,
                PackageId = package.Id,
                ShipmentId = package.ShipmentId,
                TrackingNumber = trackingNumber,
                CarrierCode = carrierCode,
                CarrierEventCode = "DRIVER_REPORTED",
                MilestoneStatus = TrackingMilestoneStatus.DeliveryException,
                CarrierDescription = request.DriverNotes.Trim(),
                OccurredAtUtc = request.OccurredAtUtc,
                ReceivedAtUtc = DateTime.UtcNow,
                EventFingerprint = trackingFp
            });

            // 6. Update Package Tracking Status & Rollup
            package.CurrentTrackingStatus = TrackingMilestoneStatus.DeliveryException;
            package.TrackingStatusUpdatedAtUtc = DateTime.UtcNow;

            if (package.Shipment != null)
            {
                package.Shipment.RecalculateDeliveryStatus();
            }

            if (package.OutboundOrder != null)
            {
                package.OutboundOrder.RecalculateDeliveryStatus();
            }

            await _context.SaveChangesAsync(cancellationToken);

            return new DeliveryExceptionResult
            {
                IsSuccess = true,
                IsDuplicate = false,
                ExceptionId = deliveryException.Id,
                PackageId = package.Id,
                ShipmentId = package.ShipmentId,
                OrderId = package.OutboundOrderId,
                TrackingNumber = trackingNumber,
                CarrierCode = carrierCode,
                ExceptionType = request.ExceptionType,
                Status = deliveryException.Status,
                Severity = severity,
                PackageTrackingStatus = package.CurrentTrackingStatus,
                OrderDeliveryStatus = package.OutboundOrder?.DeliveryStatus ?? OrderDeliveryStatus.DeliveryException,
                Message = "Driver delivery exception recorded successfully."
            };
        }, cancellationToken);
    }

    public async Task<DeliveryExceptionResult> ResolveExceptionAsync(
        ResolveDeliveryExceptionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ValidationException(nameof(request), "Resolve request cannot be null.");
        }

        var exception = await GetAuthorizedExceptionAsync(request.ExceptionId, request.TenantIdOverride, cancellationToken);
        var resolvedByUserId = request.ResolvedByUserId ?? _currentUserContext.UserId ?? Guid.Empty;

        // Perform domain transition
        exception.Resolve(request.Resolution, request.ResolutionNotes, resolvedByUserId);

        // Append tracking event
        var trackingFp = ComputeTrackingFingerprint(exception.ClientId, exception.CarrierCode, exception.TrackingNumber, null, $"RESOLVE_{exception.Id:N}_{DateTime.UtcNow.Ticks}", DateTime.UtcNow);
        _context.ShipmentTrackingEvents.Add(new ShipmentTrackingEvent
        {
            ClientId = exception.ClientId,
            FacilityId = exception.FacilityId,
            PackageId = exception.PackageId,
            ShipmentId = exception.ShipmentId,
            TrackingNumber = exception.TrackingNumber,
            CarrierCode = exception.CarrierCode,
            CarrierEventCode = "EXCEPTION_RESOLVED",
            MilestoneStatus = TrackingMilestoneStatus.InTransit,
            CarrierDescription = $"Exception resolved: {request.Resolution}. {request.ResolutionNotes.Trim()}",
            OccurredAtUtc = DateTime.UtcNow,
            ReceivedAtUtc = DateTime.UtcNow,
            EventFingerprint = trackingFp
        });

        // Check if any other unresolved exceptions remain on package
        var otherUnresolved = await _context.ShipmentDeliveryExceptions
            .AnyAsync(e => e.PackageId == exception.PackageId &&
                           e.Id != exception.Id &&
                           !e.ResolvedAtUtc.HasValue &&
                           e.Status != DeliveryExceptionStatus.Resolved &&
                           e.Status != DeliveryExceptionStatus.ReturnToSenderInitiated &&
                           e.Status != DeliveryExceptionStatus.Voided, cancellationToken);

        if (!otherUnresolved && exception.Package != null && exception.Package.CurrentTrackingStatus == TrackingMilestoneStatus.DeliveryException)
        {
            exception.Package.CurrentTrackingStatus = TrackingMilestoneStatus.InTransit;
            exception.Package.TrackingStatusUpdatedAtUtc = DateTime.UtcNow;
        }

        if (exception.Shipment != null)
        {
            exception.Shipment.RecalculateDeliveryStatus();
        }

        if (exception.Order != null)
        {
            exception.Order.RecalculateDeliveryStatus();
        }

        await _context.SaveChangesAsync(cancellationToken);

        return BuildResult(exception, "Delivery exception resolved successfully.");
    }

    public async Task<DeliveryExceptionResult> ReopenExceptionAsync(
        ReopenDeliveryExceptionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ValidationException(nameof(request), "Reopen request cannot be null.");
        }

        var exception = await GetAuthorizedExceptionAsync(request.ExceptionId, request.TenantIdOverride, cancellationToken);
        var userId = request.UserId ?? _currentUserContext.UserId ?? Guid.Empty;

        // Perform domain transition
        exception.Reopen(request.Reason, userId);

        // Append tracking event
        var trackingFp = ComputeTrackingFingerprint(exception.ClientId, exception.CarrierCode, exception.TrackingNumber, null, $"REOPEN_{exception.Id:N}_{DateTime.UtcNow.Ticks}", DateTime.UtcNow);
        _context.ShipmentTrackingEvents.Add(new ShipmentTrackingEvent
        {
            ClientId = exception.ClientId,
            FacilityId = exception.FacilityId,
            PackageId = exception.PackageId,
            ShipmentId = exception.ShipmentId,
            TrackingNumber = exception.TrackingNumber,
            CarrierCode = exception.CarrierCode,
            CarrierEventCode = "EXCEPTION_REOPENED",
            MilestoneStatus = TrackingMilestoneStatus.DeliveryException,
            CarrierDescription = $"Exception reopened: {request.Reason.Trim()}",
            OccurredAtUtc = DateTime.UtcNow,
            ReceivedAtUtc = DateTime.UtcNow,
            EventFingerprint = trackingFp
        });

        // Set package back to DeliveryException
        if (exception.Package != null)
        {
            exception.Package.CurrentTrackingStatus = TrackingMilestoneStatus.DeliveryException;
            exception.Package.TrackingStatusUpdatedAtUtc = DateTime.UtcNow;
        }

        if (exception.Shipment != null)
        {
            exception.Shipment.RecalculateDeliveryStatus();
        }

        if (exception.Order != null)
        {
            exception.Order.RecalculateDeliveryStatus();
        }

        await _context.SaveChangesAsync(cancellationToken);

        return BuildResult(exception, "Delivery exception reopened successfully.");
    }

    public async Task<DeliveryExceptionResult> VoidExceptionAsync(
        VoidDeliveryExceptionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ValidationException(nameof(request), "Void request cannot be null.");
        }

        var exception = await GetAuthorizedExceptionAsync(request.ExceptionId, request.TenantIdOverride, cancellationToken);
        var userId = request.UserId ?? _currentUserContext.UserId ?? Guid.Empty;

        // Perform domain transition
        exception.Void(request.Reason, userId);

        // Append tracking event
        var trackingFp = ComputeTrackingFingerprint(exception.ClientId, exception.CarrierCode, exception.TrackingNumber, null, $"VOID_{exception.Id:N}_{DateTime.UtcNow.Ticks}", DateTime.UtcNow);
        _context.ShipmentTrackingEvents.Add(new ShipmentTrackingEvent
        {
            ClientId = exception.ClientId,
            FacilityId = exception.FacilityId,
            PackageId = exception.PackageId,
            ShipmentId = exception.ShipmentId,
            TrackingNumber = exception.TrackingNumber,
            CarrierCode = exception.CarrierCode,
            CarrierEventCode = "EXCEPTION_VOIDED",
            MilestoneStatus = TrackingMilestoneStatus.InTransit,
            CarrierDescription = $"Exception voided: {request.Reason.Trim()}",
            OccurredAtUtc = DateTime.UtcNow,
            ReceivedAtUtc = DateTime.UtcNow,
            EventFingerprint = trackingFp
        });

        // Check if any other unresolved exceptions remain on package
        var otherUnresolved = await _context.ShipmentDeliveryExceptions
            .AnyAsync(e => e.PackageId == exception.PackageId &&
                           e.Id != exception.Id &&
                           !e.ResolvedAtUtc.HasValue &&
                           e.Status != DeliveryExceptionStatus.Resolved &&
                           e.Status != DeliveryExceptionStatus.ReturnToSenderInitiated &&
                           e.Status != DeliveryExceptionStatus.Voided, cancellationToken);

        if (!otherUnresolved && exception.Package != null && exception.Package.CurrentTrackingStatus == TrackingMilestoneStatus.DeliveryException)
        {
            exception.Package.CurrentTrackingStatus = TrackingMilestoneStatus.InTransit;
            exception.Package.TrackingStatusUpdatedAtUtc = DateTime.UtcNow;
        }

        if (exception.Shipment != null)
        {
            exception.Shipment.RecalculateDeliveryStatus();
        }

        if (exception.Order != null)
        {
            exception.Order.RecalculateDeliveryStatus();
        }

        await _context.SaveChangesAsync(cancellationToken);

        return BuildResult(exception, "Delivery exception voided successfully.");
    }

    public async Task<DeliveryExceptionResult> ScheduleReattemptAsync(
        ScheduleDeliveryReattemptRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ValidationException(nameof(request), "Schedule reattempt request cannot be null.");
        }

        var exception = await GetAuthorizedExceptionAsync(request.ExceptionId, request.TenantIdOverride, cancellationToken);
        var userId = request.UserId ?? _currentUserContext.UserId ?? Guid.Empty;

        // Perform domain transition
        exception.ScheduleReattempt(request.ScheduledDateUtc, request.Notes, userId);

        // Append tracking event
        var trackingFp = ComputeTrackingFingerprint(exception.ClientId, exception.CarrierCode, exception.TrackingNumber, null, $"REATTEMPT_{exception.Id:N}_{request.ScheduledDateUtc.Ticks}", DateTime.UtcNow);
        _context.ShipmentTrackingEvents.Add(new ShipmentTrackingEvent
        {
            ClientId = exception.ClientId,
            FacilityId = exception.FacilityId,
            PackageId = exception.PackageId,
            ShipmentId = exception.ShipmentId,
            TrackingNumber = exception.TrackingNumber,
            CarrierCode = exception.CarrierCode,
            CarrierEventCode = "REATTEMPT_SCHEDULED",
            MilestoneStatus = TrackingMilestoneStatus.DeliveryException,
            CarrierDescription = $"Delivery re-attempt scheduled for {request.ScheduledDateUtc:yyyy-MM-dd}: {request.Notes.Trim()}",
            OccurredAtUtc = DateTime.UtcNow,
            ReceivedAtUtc = DateTime.UtcNow,
            EventFingerprint = trackingFp
        });

        if (exception.Shipment != null)
        {
            exception.Shipment.RecalculateDeliveryStatus();
        }

        if (exception.Order != null)
        {
            exception.Order.RecalculateDeliveryStatus();
        }

        await _context.SaveChangesAsync(cancellationToken);

        return BuildResult(exception, "Delivery re-attempt scheduled successfully.");
    }

    public async Task<DeliveryExceptionResult> CancelReattemptAsync(
        CancelDeliveryReattemptRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ValidationException(nameof(request), "Cancel reattempt request cannot be null.");
        }

        var exception = await GetAuthorizedExceptionAsync(request.ExceptionId, request.TenantIdOverride, cancellationToken);
        var userId = request.UserId ?? _currentUserContext.UserId ?? Guid.Empty;

        // Perform domain transition
        exception.CancelReattempt(request.Reason, userId);

        // Append tracking event
        var trackingFp = ComputeTrackingFingerprint(exception.ClientId, exception.CarrierCode, exception.TrackingNumber, null, $"CANCEL_REATTEMPT_{exception.Id:N}_{DateTime.UtcNow.Ticks}", DateTime.UtcNow);
        _context.ShipmentTrackingEvents.Add(new ShipmentTrackingEvent
        {
            ClientId = exception.ClientId,
            FacilityId = exception.FacilityId,
            PackageId = exception.PackageId,
            ShipmentId = exception.ShipmentId,
            TrackingNumber = exception.TrackingNumber,
            CarrierCode = exception.CarrierCode,
            CarrierEventCode = "REATTEMPT_CANCELLED",
            MilestoneStatus = TrackingMilestoneStatus.DeliveryException,
            CarrierDescription = $"Delivery re-attempt cancelled: {request.Reason.Trim()}",
            OccurredAtUtc = DateTime.UtcNow,
            ReceivedAtUtc = DateTime.UtcNow,
            EventFingerprint = trackingFp
        });

        await _context.SaveChangesAsync(cancellationToken);

        return BuildResult(exception, "Delivery re-attempt cancelled successfully.");
    }

    public async Task<DeliveryExceptionResult> RescheduleReattemptAsync(
        RescheduleDeliveryReattemptRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ValidationException(nameof(request), "Reschedule reattempt request cannot be null.");
        }

        var exception = await GetAuthorizedExceptionAsync(request.ExceptionId, request.TenantIdOverride, cancellationToken);
        var userId = request.UserId ?? _currentUserContext.UserId ?? Guid.Empty;

        // Perform domain transition
        exception.RescheduleReattempt(request.NewScheduledDateUtc, request.Reason, userId);

        // Append tracking event
        var trackingFp = ComputeTrackingFingerprint(exception.ClientId, exception.CarrierCode, exception.TrackingNumber, null, $"RESCHEDULE_{exception.Id:N}_{request.NewScheduledDateUtc.Ticks}", DateTime.UtcNow);
        _context.ShipmentTrackingEvents.Add(new ShipmentTrackingEvent
        {
            ClientId = exception.ClientId,
            FacilityId = exception.FacilityId,
            PackageId = exception.PackageId,
            ShipmentId = exception.ShipmentId,
            TrackingNumber = exception.TrackingNumber,
            CarrierCode = exception.CarrierCode,
            CarrierEventCode = "REATTEMPT_RESCHEDULED",
            MilestoneStatus = TrackingMilestoneStatus.DeliveryException,
            CarrierDescription = $"Delivery re-attempt rescheduled to {request.NewScheduledDateUtc:yyyy-MM-dd}: {request.Reason.Trim()}",
            OccurredAtUtc = DateTime.UtcNow,
            ReceivedAtUtc = DateTime.UtcNow,
            EventFingerprint = trackingFp
        });

        await _context.SaveChangesAsync(cancellationToken);

        return BuildResult(exception, "Delivery re-attempt rescheduled successfully.");
    }

    public async Task<DeliveryExceptionResult> RecordCustomerContactAsync(
        RecordCustomerContactRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ValidationException(nameof(request), "Record contact request cannot be null.");
        }

        var exception = await GetAuthorizedExceptionAsync(request.ExceptionId, request.TenantIdOverride, cancellationToken);
        var staffUserId = request.StaffUserId ?? _currentUserContext.UserId ?? Guid.Empty;

        // Perform domain transition
        exception.RecordCustomerContact(request.Notes, staffUserId);

        // Create contact log
        var contactLog = new DeliveryExceptionContactLog
        {
            ClientId = exception.ClientId,
            ExceptionId = exception.Id,
            PackageId = exception.PackageId,
            ContactMethod = request.ContactMethod,
            Outcome = request.Outcome,
            ContactedAtUtc = DateTime.UtcNow,
            StaffUserId = staffUserId,
            Notes = request.Notes.Trim(),
            ResolutionAction = request.ResolutionAction?.Trim()
        };
        _context.DeliveryExceptionContactLogs.Add(contactLog);

        // Append tracking event
        var trackingFp = ComputeTrackingFingerprint(exception.ClientId, exception.CarrierCode, exception.TrackingNumber, null, $"CONTACT_{contactLog.Id:N}", DateTime.UtcNow);
        _context.ShipmentTrackingEvents.Add(new ShipmentTrackingEvent
        {
            ClientId = exception.ClientId,
            FacilityId = exception.FacilityId,
            PackageId = exception.PackageId,
            ShipmentId = exception.ShipmentId,
            TrackingNumber = exception.TrackingNumber,
            CarrierCode = exception.CarrierCode,
            CarrierEventCode = "CUSTOMER_CONTACTED",
            MilestoneStatus = TrackingMilestoneStatus.DeliveryException,
            CarrierDescription = $"Consignee contacted via {request.ContactMethod}. Outcome: {request.Outcome}. {request.Notes.Trim()}",
            OccurredAtUtc = DateTime.UtcNow,
            ReceivedAtUtc = DateTime.UtcNow,
            EventFingerprint = trackingFp
        });

        await _context.SaveChangesAsync(cancellationToken);

        return BuildResult(exception, "Customer contact recorded successfully.");
    }

    public async Task<DeliveryExceptionResult> UpdateAddressAsync(
        UpdateDeliveryAddressRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ValidationException(nameof(request), "Update address request cannot be null.");
        }

        var exception = await GetAuthorizedExceptionAsync(request.ExceptionId, request.TenantIdOverride, cancellationToken);
        var userId = request.UserId ?? _currentUserContext.UserId ?? Guid.Empty;

        // Perform domain transition
        exception.UpdateAddress(request.CorrectedAddress, request.Notes, userId);

        // Append tracking event
        var trackingFp = ComputeTrackingFingerprint(exception.ClientId, exception.CarrierCode, exception.TrackingNumber, null, $"ADDR_{exception.Id:N}_{DateTime.UtcNow.Ticks}", DateTime.UtcNow);
        _context.ShipmentTrackingEvents.Add(new ShipmentTrackingEvent
        {
            ClientId = exception.ClientId,
            FacilityId = exception.FacilityId,
            PackageId = exception.PackageId,
            ShipmentId = exception.ShipmentId,
            TrackingNumber = exception.TrackingNumber,
            CarrierCode = exception.CarrierCode,
            CarrierEventCode = "ADDRESS_CORRECTED",
            MilestoneStatus = TrackingMilestoneStatus.DeliveryException,
            CarrierDescription = $"Consignee address corrected: {request.CorrectedAddress.Trim()}. {request.Notes.Trim()}",
            OccurredAtUtc = DateTime.UtcNow,
            ReceivedAtUtc = DateTime.UtcNow,
            EventFingerprint = trackingFp
        });

        await _context.SaveChangesAsync(cancellationToken);

        return BuildResult(exception, "Delivery address updated successfully.");
    }

    public async Task<DeliveryExceptionResult> RedirectToDepotAsync(
        RedirectToCarrierDepotRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ValidationException(nameof(request), "Redirect to depot request cannot be null.");
        }

        var exception = await GetAuthorizedExceptionAsync(request.ExceptionId, request.TenantIdOverride, cancellationToken);
        var userId = request.UserId ?? _currentUserContext.UserId ?? Guid.Empty;

        // Perform domain transition
        exception.RedirectToDepot(request.DepotLocation, request.Notes, userId);

        // Append tracking event
        var trackingFp = ComputeTrackingFingerprint(exception.ClientId, exception.CarrierCode, exception.TrackingNumber, null, $"DEPOT_{exception.Id:N}_{DateTime.UtcNow.Ticks}", DateTime.UtcNow);
        _context.ShipmentTrackingEvents.Add(new ShipmentTrackingEvent
        {
            ClientId = exception.ClientId,
            FacilityId = exception.FacilityId,
            PackageId = exception.PackageId,
            ShipmentId = exception.ShipmentId,
            TrackingNumber = exception.TrackingNumber,
            CarrierCode = exception.CarrierCode,
            CarrierEventCode = "HOLD_AT_DEPOT",
            MilestoneStatus = TrackingMilestoneStatus.DeliveryException,
            CarrierDescription = $"Package redirected to carrier depot: {request.DepotLocation.Trim()}. {request.Notes.Trim()}",
            OccurredAtUtc = DateTime.UtcNow,
            ReceivedAtUtc = DateTime.UtcNow,
            EventFingerprint = trackingFp
        });

        await _context.SaveChangesAsync(cancellationToken);

        return BuildResult(exception, "Package redirected to carrier depot successfully.");
    }

    public async Task<List<DeliveryExceptionContactLog>> GetContactLogsForExceptionAsync(
        Guid exceptionId,
        CancellationToken cancellationToken = default)
    {
        return await _context.DeliveryExceptionContactLogs
            .Include(c => c.StaffUser)
            .Where(c => c.ExceptionId == exceptionId)
            .OrderByDescending(c => c.ContactedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<ShipmentDeliveryException?> GetExceptionByIdAsync(
        Guid exceptionId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ShipmentDeliveryExceptions
            .Include(e => e.Package)
            .Include(e => e.Shipment)
            .Include(e => e.Order)
            .Include(e => e.ReportedByUser)
            .Include(e => e.ResolvedByUser)
            .Include(e => e.ReturnShipment)
            .Include(e => e.ContactLogs)
            .FirstOrDefaultAsync(e => e.Id == exceptionId, cancellationToken);
    }

    public async Task<List<ShipmentDeliveryException>> GetExceptionsForPackageAsync(
        Guid packageId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ShipmentDeliveryExceptions
            .Include(e => e.ReportedByUser)
            .Include(e => e.ResolvedByUser)
            .Include(e => e.ReturnShipment)
            .Include(e => e.ContactLogs)
            .Where(e => e.PackageId == packageId)
            .OrderByDescending(e => e.OccurredAtUtc)
            .ToListAsync(cancellationToken);
    }

    private async Task<ShipmentDeliveryException> GetAuthorizedExceptionAsync(
        Guid exceptionId,
        Guid? tenantIdOverride,
        CancellationToken cancellationToken)
    {
        if (exceptionId == Guid.Empty)
        {
            throw new ValidationException(nameof(exceptionId), "Exception ID must be provided.");
        }

        var exception = await _context.ShipmentDeliveryExceptions
            .Include(e => e.Package)
            .Include(e => e.Shipment)
            .Include(e => e.Order)
                .ThenInclude(o => o!.Packages)
            .FirstOrDefaultAsync(e => e.Id == exceptionId, cancellationToken);

        if (exception == null)
        {
            throw new EntityNotFoundException(nameof(ShipmentDeliveryException), exceptionId);
        }

        // Tenant Isolation
        if (tenantIdOverride.HasValue && tenantIdOverride.Value != exception.ClientId)
        {
            throw new UnauthorizedException($"Exception belongs to a different client tenant. Cross-tenant access rejected.");
        }

        if (_tenantProvider.TenantId.HasValue &&
            !_tenantProvider.BypassTenantFilter &&
            _tenantProvider.TenantId.Value != exception.ClientId)
        {
            throw new UnauthorizedException("Unauthorized cross-tenant exception operation attempt.");
        }

        // Facility Isolation
        if (_currentUserContext.FacilityId.HasValue &&
            _currentUserContext.FacilityId.Value != exception.FacilityId &&
            !_currentUserContext.IsSuperAdmin)
        {
            throw new ForbiddenException($"User is not authorized for exception facility '{exception.FacilityId}'.");
        }

        return exception;
    }

    private static DeliveryExceptionResult BuildResult(ShipmentDeliveryException exception, string message)
    {
        return new DeliveryExceptionResult
        {
            IsSuccess = true,
            IsDuplicate = false,
            ExceptionId = exception.Id,
            PackageId = exception.PackageId,
            ShipmentId = exception.ShipmentId,
            OrderId = exception.OrderId ?? (exception.Package?.OutboundOrderId ?? Guid.Empty),
            TrackingNumber = exception.TrackingNumber,
            CarrierCode = exception.CarrierCode,
            ExceptionType = exception.ExceptionType,
            Status = exception.Status,
            Severity = exception.Severity,
            PackageTrackingStatus = exception.Package?.CurrentTrackingStatus ?? TrackingMilestoneStatus.DeliveryException,
            OrderDeliveryStatus = exception.Order?.DeliveryStatus ?? OrderDeliveryStatus.DeliveryException,
            Message = message
        };
    }

    private static string ComputeTrackingFingerprint(
        Guid clientId,
        string carrierCode,
        string trackingNumber,
        string? carrierEventId,
        string carrierEventCode,
        DateTime occurredAtUtc)
    {
        string raw;
        if (!string.IsNullOrWhiteSpace(carrierEventId))
        {
            raw = $"{clientId:D}:{carrierCode.ToUpperInvariant()}:{trackingNumber.ToUpperInvariant()}:ID:{carrierEventId.Trim()}";
        }
        else
        {
            raw = $"{clientId:D}:{carrierCode.ToUpperInvariant()}:{trackingNumber.ToUpperInvariant()}:{carrierEventCode.ToUpperInvariant()}:{occurredAtUtc.ToUniversalTime():yyyy-MM-ddTHH:mm:ss.fffZ}";
        }

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("ix_shipment_delivery_exceptions_fingerprint", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase);
    }
}
