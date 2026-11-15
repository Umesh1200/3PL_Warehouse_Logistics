using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services.Billing;

/// <summary>
/// Authoritative ingestion and costing engine for activity-based handling charges.
/// Ingests operational warehouse, fulfillment, reverse-logistics, and VAS events into immutable
/// BillableActivity ledger records with deterministic idempotency deduplication and immediate auto-rating.
/// DB-SPEC-3PL-2026 Phase 5.3 Activity-Based Handling Charges &amp; Operational Ingestion Engine.
/// </summary>
public class HandlingActivityIngestionService : IHandlingActivityIngestionService
{
    private readonly IApplicationDbContext _context;
    private readonly IRateCardService _rateCardService;
    private readonly ITenantProvider _tenantProvider;
    private readonly ILogger<HandlingActivityIngestionService> _logger;

    public HandlingActivityIngestionService(
        IApplicationDbContext context,
        IRateCardService rateCardService,
        ITenantProvider tenantProvider,
        ILogger<HandlingActivityIngestionService> logger)
    {
        _context = context;
        _rateCardService = rateCardService;
        _tenantProvider = tenantProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IngestHandlingActivityResult> IngestActivityAsync(
        IngestHandlingActivityRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ValidationException(nameof(request), "Ingest handling activity request cannot be null.");
        }

        // 1. Multi-Tenancy Security Guard
        ValidateTenantAccess(request.ClientId);

        // 2. Input Validations
        if (request.FacilityId == Guid.Empty)
        {
            throw new ValidationException(nameof(request.FacilityId), "Facility ID is required.");
        }

        if (string.IsNullOrWhiteSpace(request.SourceType))
        {
            throw new ValidationException(nameof(request.SourceType), "Operational source type is required.");
        }

        if (request.SourceId == Guid.Empty)
        {
            throw new ValidationException(nameof(request.SourceId), "Operational source ID is required.");
        }

        if (request.Quantity < 0)
        {
            throw new ValidationException(nameof(request.Quantity), "Billable activity quantity cannot be negative.");
        }

        var normalizedKey = request.IdempotencyKey?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            throw new ValidationException(nameof(request.IdempotencyKey), "Idempotency key is required.");
        }

        // 3. Idempotency Deduplication
        var existing = await _context.BillableActivities
            .FirstOrDefaultAsync(a => a.ClientId == request.ClientId && a.IdempotencyKey == normalizedKey, cancellationToken);

        if (existing != null)
        {
            _logger.LogInformation(
                "Idempotent duplicate handling activity detected for Client '{ClientId}', Key '{Key}'. Returning existing ID '{Id}'.",
                request.ClientId, normalizedKey, existing.Id);

            return MapToResult(existing, isSuccess: true, isDuplicate: true, message: "Activity already exists for idempotency key.");
        }

        // 4. Resolve Billing Period
        Guid? resolvedPeriodId = request.BillingPeriodId;
        if (resolvedPeriodId.HasValue)
        {
            var specifiedPeriod = await _context.BillingPeriods
                .FirstOrDefaultAsync(p => p.Id == resolvedPeriodId.Value, cancellationToken);

            if (specifiedPeriod == null)
            {
                throw new EntityNotFoundException(nameof(BillingPeriod), resolvedPeriodId.Value);
            }

            if (specifiedPeriod.ClientId != request.ClientId)
            {
                throw new UnauthorizedException("Specified billing period belongs to a different client tenant.");
            }

            if (specifiedPeriod.Status == BillingPeriodStatus.Closed || specifiedPeriod.Status == BillingPeriodStatus.Invoiced)
            {
                throw new InvalidDomainStateException($"Cannot attach billable activity to a {specifiedPeriod.Status} billing period.");
            }
        }
        else
        {
            var openPeriod = await _context.BillingPeriods
                .FirstOrDefaultAsync(p => p.ClientId == request.ClientId &&
                                          p.Status == BillingPeriodStatus.Open &&
                                          p.StartDateUtc <= request.OccurredAtUtc &&
                                          p.EndDateUtc >= request.OccurredAtUtc, cancellationToken);

            if (openPeriod != null)
            {
                resolvedPeriodId = openPeriod.Id;
            }
        }

        // 5. Construct Immutable Aggregate Record
        var activity = BillableActivity.Create(
            request.ClientId,
            request.FacilityId,
            request.ActivityType,
            request.SourceType,
            request.SourceId,
            request.SourceReference,
            request.Quantity,
            request.UnitOfMeasure,
            request.OccurredAtUtc,
            normalizedKey,
            request.Description ?? $"{request.ActivityType} on {request.SourceType}",
            resolvedPeriodId,
            request.MetadataJson);

        _context.BillableActivities.Add(activity);
        await _context.SaveChangesAsync(cancellationToken);

        // 6. Auto-Rating (Optional Immediate Contract Rating)
        bool isRated = false;
        string message = "Activity successfully recorded in PendingRating status.";

        if (request.AutoRate)
        {
            var (schedule, line) = await _rateCardService.GetEffectiveRateLineAsync(
                activity.ClientId,
                activity.FacilityId,
                activity.ActivityType,
                activity.OccurredAtUtc,
                activity.Currency,
                cancellationToken);

            if (schedule != null && line != null)
            {
                if (string.Equals(schedule.Currency, activity.Currency, StringComparison.OrdinalIgnoreCase))
                {
                    var ratingResult = line.CalculateRating(activity.Quantity);
                    activity.Rate(
                        unitPrice: ratingResult.UnitPrice,
                        totalAmount: ratingResult.TotalAmount,
                        currency: schedule.Currency,
                        rateCardId: schedule.RateCardId,
                        rateCardTierId: ratingResult.MatchedTierId,
                        rateCardVersion: schedule.Version,
                        ratedByUserId: null,
                        ratedAtUtc: DateTime.UtcNow,
                        rateCardScheduleId: schedule.Id);

                    await _context.SaveChangesAsync(cancellationToken);
                    isRated = true;
                    message = $"Activity successfully recorded and rated: {activity.TotalAmount:F2} {activity.Currency}.";

                    _logger.LogInformation(
                        "Auto-rated handling activity '{Id}' ({Type}) -> UnitPrice: {UnitPrice:F4}, Total: {Total:F2} {Currency}.",
                        activity.Id, activity.ActivityType, activity.UnitPrice, activity.TotalAmount, activity.Currency);
                }
                else
                {
                    message = $"Activity recorded as PendingRating: Currency mismatch ('{activity.Currency}' vs '{schedule.Currency}').";
                    _logger.LogWarning("Currency mismatch auto-rating Activity '{Id}'. Currency: {ActivityCurrency} vs Schedule: {ScheduleCurrency}.",
                        activity.Id, activity.Currency, schedule.Currency);
                }
            }
            else
            {
                message = "Activity recorded as PendingRating: No applicable active rate line found.";
                _logger.LogInformation(
                    "No rate card line found for auto-rating Activity '{Id}' ({Type}). Remaining PendingRating.",
                    activity.Id, activity.ActivityType);
            }
        }

        return MapToResult(activity, isSuccess: true, isDuplicate: false, isRated: isRated, message: message);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IngestHandlingActivityResult>> IngestBatchAsync(
        IReadOnlyList<IngestHandlingActivityRequest> requests,
        CancellationToken cancellationToken = default)
    {
        if (requests == null || requests.Count == 0)
        {
            return [];
        }

        var results = new List<IngestHandlingActivityResult>(requests.Count);
        var seenInBatch = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var req in requests)
        {
            var key = req.IdempotencyKey?.Trim() ?? string.Empty;
            if (seenInBatch.Contains(key))
            {
                var prior = results.First(r => string.Equals(r.IdempotencyKey, key, StringComparison.OrdinalIgnoreCase));
                results.Add(new IngestHandlingActivityResult
                {
                    IsSuccess = true,
                    IsDuplicate = true,
                    IsRated = prior.IsRated,
                    BillableActivityId = prior.BillableActivityId,
                    ClientId = req.ClientId,
                    FacilityId = req.FacilityId,
                    ActivityType = req.ActivityType,
                    Status = prior.Status,
                    Quantity = req.Quantity,
                    UnitOfMeasure = req.UnitOfMeasure,
                    UnitPrice = prior.UnitPrice,
                    TotalAmount = prior.TotalAmount,
                    Currency = prior.Currency,
                    RateCardId = prior.RateCardId,
                    RateCardScheduleId = prior.RateCardScheduleId,
                    RateCardTierId = prior.RateCardTierId,
                    RateCardVersion = prior.RateCardVersion,
                    IdempotencyKey = key,
                    SourceType = req.SourceType,
                    SourceId = req.SourceId,
                    SourceReference = req.SourceReference,
                    OccurredAtUtc = req.OccurredAtUtc,
                    Message = "Duplicate activity detected within batch."
                });
                continue;
            }

            var singleResult = await IngestActivityAsync(req, cancellationToken);
            results.Add(singleResult);
            seenInBatch.Add(key);
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<IngestHandlingActivityResult> IngestInboundReceiptAsync(
        Guid clientId,
        Guid facilityId,
        Guid receiptLineId,
        string poNumber,
        decimal quantity,
        string unitOfMeasure,
        DateTime occurredAtUtc,
        bool autoRate = true,
        CancellationToken cancellationToken = default)
    {
        var req = new IngestHandlingActivityRequest
        {
            ClientId = clientId,
            FacilityId = facilityId,
            ActivityType = BillableActivityType.InboundReceiving,
            SourceType = "ReceiptLine",
            SourceId = receiptLineId,
            SourceReference = string.IsNullOrWhiteSpace(poNumber) ? receiptLineId.ToString() : poNumber.Trim(),
            Quantity = quantity,
            UnitOfMeasure = string.IsNullOrWhiteSpace(unitOfMeasure) ? "UNIT" : unitOfMeasure.Trim().ToUpperInvariant(),
            OccurredAtUtc = occurredAtUtc,
            IdempotencyKey = $"INB-RCV-{receiptLineId:N}",
            Description = $"Inbound receiving of {quantity} {unitOfMeasure} (PO: {poNumber})",
            AutoRate = autoRate
        };

        return await IngestActivityAsync(req, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IngestHandlingActivityResult> IngestInboundInspectionAsync(
        Guid clientId,
        Guid facilityId,
        Guid inspectionId,
        string reference,
        decimal sampleQuantity,
        DateTime occurredAtUtc,
        bool autoRate = true,
        CancellationToken cancellationToken = default)
    {
        var req = new IngestHandlingActivityRequest
        {
            ClientId = clientId,
            FacilityId = facilityId,
            ActivityType = BillableActivityType.InboundInspection,
            SourceType = "InboundInspection",
            SourceId = inspectionId,
            SourceReference = string.IsNullOrWhiteSpace(reference) ? inspectionId.ToString() : reference.Trim(),
            Quantity = sampleQuantity,
            UnitOfMeasure = "SAMPLE",
            OccurredAtUtc = occurredAtUtc,
            IdempotencyKey = $"INB-INSP-{inspectionId:N}",
            Description = $"Inbound QA inspection of {sampleQuantity} samples ({reference})",
            AutoRate = autoRate
        };

        return await IngestActivityAsync(req, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IngestHandlingActivityResult> IngestPutawayTaskAsync(
        Guid clientId,
        Guid facilityId,
        Guid putawayTaskId,
        string taskReference,
        decimal quantity,
        string unitOfMeasure,
        DateTime occurredAtUtc,
        bool autoRate = true,
        CancellationToken cancellationToken = default)
    {
        var req = new IngestHandlingActivityRequest
        {
            ClientId = clientId,
            FacilityId = facilityId,
            ActivityType = BillableActivityType.Putaway,
            SourceType = "PutawayTask",
            SourceId = putawayTaskId,
            SourceReference = string.IsNullOrWhiteSpace(taskReference) ? putawayTaskId.ToString() : taskReference.Trim(),
            Quantity = quantity,
            UnitOfMeasure = string.IsNullOrWhiteSpace(unitOfMeasure) ? "UNIT" : unitOfMeasure.Trim().ToUpperInvariant(),
            OccurredAtUtc = occurredAtUtc,
            IdempotencyKey = $"PUTAWAY-{putawayTaskId:N}",
            Description = $"Putaway handling of {quantity} {unitOfMeasure} ({taskReference})",
            AutoRate = autoRate
        };

        return await IngestActivityAsync(req, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IngestHandlingActivityResult> IngestCrossDockExecutionAsync(
        Guid clientId,
        Guid facilityId,
        Guid crossDockTaskId,
        string reference,
        decimal quantity,
        string unitOfMeasure,
        DateTime occurredAtUtc,
        bool autoRate = true,
        CancellationToken cancellationToken = default)
    {
        var req = new IngestHandlingActivityRequest
        {
            ClientId = clientId,
            FacilityId = facilityId,
            ActivityType = BillableActivityType.CrossDock,
            SourceType = "CrossDockTask",
            SourceId = crossDockTaskId,
            SourceReference = string.IsNullOrWhiteSpace(reference) ? crossDockTaskId.ToString() : reference.Trim(),
            Quantity = quantity,
            UnitOfMeasure = string.IsNullOrWhiteSpace(unitOfMeasure) ? "UNIT" : unitOfMeasure.Trim().ToUpperInvariant(),
            OccurredAtUtc = occurredAtUtc,
            IdempotencyKey = $"CROSSDOCK-{crossDockTaskId:N}",
            Description = $"Cross-dock handling of {quantity} {unitOfMeasure} ({reference})",
            AutoRate = autoRate
        };

        return await IngestActivityAsync(req, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IngestHandlingActivityResult> IngestOrderFulfillmentAsync(
        Guid clientId,
        Guid facilityId,
        Guid orderId,
        string orderNumber,
        DateTime occurredAtUtc,
        bool autoRate = true,
        CancellationToken cancellationToken = default)
    {
        var req = new IngestHandlingActivityRequest
        {
            ClientId = clientId,
            FacilityId = facilityId,
            ActivityType = BillableActivityType.OrderProcessing,
            SourceType = "OutboundOrder",
            SourceId = orderId,
            SourceReference = string.IsNullOrWhiteSpace(orderNumber) ? orderId.ToString() : orderNumber.Trim(),
            Quantity = 1m,
            UnitOfMeasure = "ORDER",
            OccurredAtUtc = occurredAtUtc,
            IdempotencyKey = $"OUT-ORD-{orderId:N}",
            Description = $"Base order processing fee for order {orderNumber}",
            AutoRate = autoRate
        };

        return await IngestActivityAsync(req, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IngestHandlingActivityResult>> IngestPickExecutionAsync(
        Guid clientId,
        Guid facilityId,
        Guid pickTaskId,
        string taskNumber,
        decimal pickedQuantity,
        DateTime occurredAtUtc,
        bool autoRate = true,
        CancellationToken cancellationToken = default)
    {
        var results = new List<IngestHandlingActivityResult>(2);

        // 1. Pick Line Fee (1 per pick task line)
        var lineReq = new IngestHandlingActivityRequest
        {
            ClientId = clientId,
            FacilityId = facilityId,
            ActivityType = BillableActivityType.PickLine,
            SourceType = "PickTask",
            SourceId = pickTaskId,
            SourceReference = string.IsNullOrWhiteSpace(taskNumber) ? pickTaskId.ToString() : taskNumber.Trim(),
            Quantity = 1m,
            UnitOfMeasure = "LINE",
            OccurredAtUtc = occurredAtUtc,
            IdempotencyKey = $"PICK-LINE-{pickTaskId:N}",
            Description = $"Pick line task {taskNumber}",
            AutoRate = autoRate
        };
        var lineResult = await IngestActivityAsync(lineReq, cancellationToken);
        results.Add(lineResult);

        // 2. Pick Unit Fee (per piece/each unit picked)
        if (pickedQuantity > 0)
        {
            var unitReq = new IngestHandlingActivityRequest
            {
                ClientId = clientId,
                FacilityId = facilityId,
                ActivityType = BillableActivityType.PickUnit,
                SourceType = "PickTask",
                SourceId = pickTaskId,
                SourceReference = string.IsNullOrWhiteSpace(taskNumber) ? pickTaskId.ToString() : taskNumber.Trim(),
                Quantity = pickedQuantity,
                UnitOfMeasure = "UNIT",
                OccurredAtUtc = occurredAtUtc,
                IdempotencyKey = $"PICK-UNIT-{pickTaskId:N}",
                Description = $"Pick unit items for task {taskNumber} ({pickedQuantity} units)",
                AutoRate = autoRate
            };
            var unitResult = await IngestActivityAsync(unitReq, cancellationToken);
            results.Add(unitResult);
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IngestHandlingActivityResult>> IngestPackingSessionAsync(
        Guid clientId,
        Guid facilityId,
        Guid packageId,
        string packageNumber,
        Guid? packagingMaterialId,
        string? materialCode,
        decimal materialQuantity,
        DateTime occurredAtUtc,
        bool autoRate = true,
        CancellationToken cancellationToken = default)
    {
        var results = new List<IngestHandlingActivityResult>(2);

        // 1. Package Carton Fee
        var cartonReq = new IngestHandlingActivityRequest
        {
            ClientId = clientId,
            FacilityId = facilityId,
            ActivityType = BillableActivityType.PackingCarton,
            SourceType = "Package",
            SourceId = packageId,
            SourceReference = string.IsNullOrWhiteSpace(packageNumber) ? packageId.ToString() : packageNumber.Trim(),
            Quantity = 1m,
            UnitOfMeasure = "CARTON",
            OccurredAtUtc = occurredAtUtc,
            IdempotencyKey = $"PACK-BOX-{packageId:N}",
            Description = $"Carton packing for package {packageNumber}",
            AutoRate = autoRate
        };
        var cartonResult = await IngestActivityAsync(cartonReq, cancellationToken);
        results.Add(cartonResult);

        // 2. Packaging Material Consumption Fee (if material consumed)
        if (packagingMaterialId.HasValue && materialQuantity > 0)
        {
            var matReq = new IngestHandlingActivityRequest
            {
                ClientId = clientId,
                FacilityId = facilityId,
                ActivityType = BillableActivityType.PackagingMaterial,
                SourceType = "PackagingMaterial",
                SourceId = packagingMaterialId.Value,
                SourceReference = string.IsNullOrWhiteSpace(materialCode) ? packageNumber : materialCode.Trim(),
                Quantity = materialQuantity,
                UnitOfMeasure = "UNIT",
                OccurredAtUtc = occurredAtUtc,
                IdempotencyKey = $"PACK-MAT-{packageId:N}-{packagingMaterialId.Value:N}",
                Description = $"Packaging material consumption ({materialCode}) for package {packageNumber}",
                AutoRate = autoRate
            };
            var matResult = await IngestActivityAsync(matReq, cancellationToken);
            results.Add(matResult);
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<IngestHandlingActivityResult> IngestShipmentDispatchAsync(
        Guid clientId,
        Guid facilityId,
        Guid shipmentId,
        string shipmentNumber,
        decimal packageCount,
        DateTime occurredAtUtc,
        bool autoRate = true,
        CancellationToken cancellationToken = default)
    {
        var qty = packageCount > 0 ? packageCount : 1m;
        var req = new IngestHandlingActivityRequest
        {
            ClientId = clientId,
            FacilityId = facilityId,
            ActivityType = BillableActivityType.ShippingHandling,
            SourceType = "Shipment",
            SourceId = shipmentId,
            SourceReference = string.IsNullOrWhiteSpace(shipmentNumber) ? shipmentId.ToString() : shipmentNumber.Trim(),
            Quantity = qty,
            UnitOfMeasure = "PACKAGE",
            OccurredAtUtc = occurredAtUtc,
            IdempotencyKey = $"SHIP-DISP-{shipmentId:N}",
            Description = $"Shipment handling & staging for {shipmentNumber} ({qty} packages)",
            AutoRate = autoRate
        };

        return await IngestActivityAsync(req, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IngestHandlingActivityResult>> IngestReturnProcessingAsync(
        Guid clientId,
        Guid facilityId,
        Guid returnLineId,
        string rmaNumber,
        decimal quantity,
        bool isInspected,
        bool isRestocked,
        DateTime occurredAtUtc,
        bool autoRate = true,
        CancellationToken cancellationToken = default)
    {
        var results = new List<IngestHandlingActivityResult>(3);

        // 1. Return Receipt Processing Fee
        var rcvReq = new IngestHandlingActivityRequest
        {
            ClientId = clientId,
            FacilityId = facilityId,
            ActivityType = BillableActivityType.ReturnProcessing,
            SourceType = "ReturnReceivingLine",
            SourceId = returnLineId,
            SourceReference = string.IsNullOrWhiteSpace(rmaNumber) ? returnLineId.ToString() : rmaNumber.Trim(),
            Quantity = quantity,
            UnitOfMeasure = "UNIT",
            OccurredAtUtc = occurredAtUtc,
            IdempotencyKey = $"RET-RCV-{returnLineId:N}",
            Description = $"Return receipt processing for RMA {rmaNumber} ({quantity} units)",
            AutoRate = autoRate
        };
        var rcvResult = await IngestActivityAsync(rcvReq, cancellationToken);
        results.Add(rcvResult);

        // 2. Return Inspection Fee (if item underwent quality / grading inspection)
        if (isInspected)
        {
            var inspReq = new IngestHandlingActivityRequest
            {
                ClientId = clientId,
                FacilityId = facilityId,
                ActivityType = BillableActivityType.ReturnInspection,
                SourceType = "ReturnReceivingLine",
                SourceId = returnLineId,
                SourceReference = string.IsNullOrWhiteSpace(rmaNumber) ? returnLineId.ToString() : rmaNumber.Trim(),
                Quantity = quantity,
                UnitOfMeasure = "UNIT",
                OccurredAtUtc = occurredAtUtc,
                IdempotencyKey = $"RET-INSP-{returnLineId:N}",
                Description = $"Return condition grading and inspection for RMA {rmaNumber} ({quantity} units)",
                AutoRate = autoRate
            };
            var inspResult = await IngestActivityAsync(inspReq, cancellationToken);
            results.Add(inspResult);
        }

        // 3. Return Restocking Fee (if item returned to active sellable stock)
        if (isRestocked)
        {
            var restockReq = new IngestHandlingActivityRequest
            {
                ClientId = clientId,
                FacilityId = facilityId,
                ActivityType = BillableActivityType.Restocking,
                SourceType = "ReturnReceivingLine",
                SourceId = returnLineId,
                SourceReference = string.IsNullOrWhiteSpace(rmaNumber) ? returnLineId.ToString() : rmaNumber.Trim(),
                Quantity = quantity,
                UnitOfMeasure = "UNIT",
                OccurredAtUtc = occurredAtUtc,
                IdempotencyKey = $"RET-RESTOCK-{returnLineId:N}",
                Description = $"Return restocking to sellable inventory for RMA {rmaNumber} ({quantity} units)",
                AutoRate = autoRate
            };
            var restockResult = await IngestActivityAsync(restockReq, cancellationToken);
            results.Add(restockResult);
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<IngestHandlingActivityResult> IngestVasActivityAsync(
        Guid clientId,
        Guid facilityId,
        BillableActivityType vasType,
        Guid sourceId,
        string sourceReference,
        decimal quantity,
        string unitOfMeasure,
        string description,
        DateTime occurredAtUtc,
        bool autoRate = true,
        CancellationToken cancellationToken = default)
    {
        if (vasType != BillableActivityType.SpecialHandling &&
            vasType != BillableActivityType.KittingAndAssembly &&
            vasType != BillableActivityType.CustomVas)
        {
            throw new ValidationException(nameof(vasType), $"Activity type '{vasType}' is not a valid Value-Added Service (VAS) type.");
        }

        var normalizedRef = string.IsNullOrWhiteSpace(sourceReference) ? sourceId.ToString("N") : sourceReference.Trim();
        var req = new IngestHandlingActivityRequest
        {
            ClientId = clientId,
            FacilityId = facilityId,
            ActivityType = vasType,
            SourceType = "VAS",
            SourceId = sourceId,
            SourceReference = normalizedRef,
            Quantity = quantity,
            UnitOfMeasure = string.IsNullOrWhiteSpace(unitOfMeasure) ? "UNIT" : unitOfMeasure.Trim().ToUpperInvariant(),
            OccurredAtUtc = occurredAtUtc,
            IdempotencyKey = $"VAS-{vasType}-{sourceId:N}-{normalizedRef}",
            Description = string.IsNullOrWhiteSpace(description) ? $"{vasType} for {normalizedRef}" : description.Trim(),
            AutoRate = autoRate
        };

        return await IngestActivityAsync(req, cancellationToken);
    }

    private void ValidateTenantAccess(Guid clientId)
    {
        if (_tenantProvider.TenantId.HasValue &&
            !_tenantProvider.BypassTenantFilter &&
            _tenantProvider.TenantId.Value != clientId)
        {
            throw new UnauthorizedException("Unauthorized cross-tenant commercial activity ingestion operation.");
        }
    }

    private static IngestHandlingActivityResult MapToResult(
        BillableActivity a,
        bool isSuccess,
        bool isDuplicate,
        bool? isRated = null,
        string? message = null)
    {
        var rated = isRated ?? (a.Status == BillableActivityStatus.Rated);
        return new IngestHandlingActivityResult
        {
            IsSuccess = isSuccess,
            IsDuplicate = isDuplicate,
            IsRated = rated,
            BillableActivityId = a.Id,
            ClientId = a.ClientId,
            FacilityId = a.FacilityId,
            ActivityType = a.ActivityType,
            Status = a.Status,
            Quantity = a.Quantity,
            UnitOfMeasure = a.UnitOfMeasure,
            UnitPrice = a.UnitPrice,
            TotalAmount = a.TotalAmount,
            Currency = a.Currency,
            RateCardId = a.RateCardId,
            RateCardScheduleId = a.RateCardScheduleId,
            RateCardTierId = a.RateCardTierId,
            RateCardVersion = a.RateCardVersion,
            IdempotencyKey = a.IdempotencyKey,
            SourceType = a.SourceType,
            SourceId = a.SourceId,
            SourceReference = a.SourceReference,
            OccurredAtUtc = a.OccurredAtUtc,
            Message = message ?? (isDuplicate ? "Duplicate activity retrieved." : "Activity processed.")
        };
    }
}

