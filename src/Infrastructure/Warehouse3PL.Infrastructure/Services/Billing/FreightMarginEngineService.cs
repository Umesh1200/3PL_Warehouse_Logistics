using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services.Billing;

/// <summary>
/// Authoritative implementation of Shipping Margin, Accessorial Charges &amp; Carrier Markup Engine.
/// Manages buy-side carrier costs, contractual client markups, accessorial classifications,
/// gross freight margin calculations, and carrier invoice reconciliation.
/// DB-SPEC-3PL-2026 Phase 5.4 Shipping Margin, Accessorial Charges &amp; Carrier Markup Engine.
/// </summary>
public class FreightMarginEngineService : IFreightMarginEngineService
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly ILogger<FreightMarginEngineService> _logger;

    public FreightMarginEngineService(
        IApplicationDbContext context,
        ITenantProvider tenantProvider,
        ILogger<FreightMarginEngineService> logger)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IngestCarrierCostResult> IngestCarrierCostAsync(
        IngestCarrierCostRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ValidationException(nameof(request), "Ingest carrier cost request cannot be null.");
        }

        ValidateTenantAccess(request.ClientId);

        var normalizedKey = request.IdempotencyKey?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            throw new ValidationException(nameof(request.IdempotencyKey), "Idempotency key is required.");
        }

        // 1. Idempotency Check
        var existing = await _context.CarrierCostRecords
            .Include(c => c.AccessorialCharges)
            .FirstOrDefaultAsync(c => c.ClientId == request.ClientId && c.IdempotencyKey == normalizedKey, cancellationToken);

        if (existing != null)
        {
            _logger.LogInformation(
                "Idempotent duplicate carrier cost record detected for Client '{ClientId}', Key '{Key}'. Returning ID '{Id}'.",
                request.ClientId, normalizedKey, existing.Id);

            return new IngestCarrierCostResult
            {
                IsSuccess = true,
                IsDuplicate = true,
                CarrierCostRecordId = existing.Id,
                ShipmentId = existing.ShipmentId,
                TotalCarrierCost = existing.TotalCarrierCost,
                Currency = existing.Currency,
                Status = existing.Status,
                IdempotencyKey = existing.IdempotencyKey,
                Message = "Carrier cost record already exists for idempotency key."
            };
        }

        // 2. Validate Shipment
        var shipmentExists = await _context.Shipments
            .AnyAsync(s => s.Id == request.ShipmentId && s.ClientId == request.ClientId, cancellationToken);

        if (!shipmentExists)
        {
            throw new EntityNotFoundException(nameof(Shipment), request.ShipmentId);
        }

        // 3. Construct CarrierCostRecord
        var record = CarrierCostRecord.Create(
            clientId: request.ClientId,
            facilityId: request.FacilityId,
            shipmentId: request.ShipmentId,
            carrierCode: request.CarrierCode,
            carrierServiceLevel: request.CarrierServiceLevel,
            baseCost: request.BaseTransportationCost,
            fuelSurcharge: request.FuelSurcharge,
            insuranceCost: request.InsuranceCost,
            currency: request.Currency,
            idempotencyKey: normalizedKey,
            costAssessedAtUtc: request.CostAssessedAtUtc,
            packageId: request.PackageId,
            trackingNumber: request.TrackingNumber,
            status: request.Status,
            notes: request.Notes);

        // 4. Attach Accessorials if provided
        if (request.Accessorials != null && request.Accessorials.Count > 0)
        {
            foreach (var acc in request.Accessorials)
            {
                var accKey = $"{normalizedKey}-ACC-{acc.AccessorialType}-{acc.CarrierAccessorialCode.Trim()}";
                var charge = CarrierAccessorialCharge.Create(
                    carrierCostRecordId: record.Id,
                    clientId: request.ClientId,
                    facilityId: request.FacilityId,
                    shipmentId: request.ShipmentId,
                    accessorialType: acc.AccessorialType,
                    carrierAccessorialCode: acc.CarrierAccessorialCode,
                    carrierAmount: acc.CarrierAmount,
                    currency: request.Currency,
                    idempotencyKey: accKey,
                    assessedAtUtc: request.CostAssessedAtUtc,
                    packageId: acc.PackageId,
                    description: acc.Description,
                    sourceDocumentReference: acc.SourceDocumentReference);

                record.AddAccessorialCharge(charge);
            }
        }

        _context.CarrierCostRecords.Add(record);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Carrier cost record '{Id}' created for Shipment '{ShipmentId}' ({CarrierCode}). Total Cost: {Total:F2} {Currency}.",
            record.Id, record.ShipmentId, record.CarrierCode, record.TotalCarrierCost, record.Currency);

        return new IngestCarrierCostResult
        {
            IsSuccess = true,
            IsDuplicate = false,
            CarrierCostRecordId = record.Id,
            ShipmentId = record.ShipmentId,
            TotalCarrierCost = record.TotalCarrierCost,
            Currency = record.Currency,
            Status = record.Status,
            IdempotencyKey = record.IdempotencyKey,
            Message = "Carrier cost record successfully ingested."
        };
    }

    /// <inheritdoc />
    public async Task<CarrierAccessorialCharge> IngestAccessorialChargeAsync(
        Guid carrierCostRecordId,
        AccessorialType accessorialType,
        string carrierAccessorialCode,
        decimal carrierAmount,
        string currency,
        string idempotencyKey,
        DateTime assessedAtUtc,
        Guid? packageId = null,
        string? description = null,
        string? sourceDocumentReference = null,
        CancellationToken cancellationToken = default)
    {
        var record = await _context.CarrierCostRecords
            .Include(c => c.AccessorialCharges)
            .FirstOrDefaultAsync(c => c.Id == carrierCostRecordId, cancellationToken);

        if (record == null)
        {
            throw new EntityNotFoundException(nameof(CarrierCostRecord), carrierCostRecordId);
        }

        ValidateTenantAccess(record.ClientId);

        var normalizedKey = idempotencyKey?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            throw new ValidationException(nameof(idempotencyKey), "Idempotency key is required.");
        }

        var existing = await _context.CarrierAccessorialCharges
            .FirstOrDefaultAsync(a => a.ClientId == record.ClientId && a.IdempotencyKey == normalizedKey, cancellationToken);

        if (existing != null)
        {
            _logger.LogInformation("Duplicate accessorial charge '{Key}' detected. Returning existing ID '{Id}'.", normalizedKey, existing.Id);
            return existing;
        }

        var charge = CarrierAccessorialCharge.Create(
            carrierCostRecordId: record.Id,
            clientId: record.ClientId,
            facilityId: record.FacilityId,
            shipmentId: record.ShipmentId,
            accessorialType: accessorialType,
            carrierAccessorialCode: carrierAccessorialCode,
            carrierAmount: carrierAmount,
            currency: currency,
            idempotencyKey: normalizedKey,
            assessedAtUtc: assessedAtUtc,
            packageId: packageId,
            description: description,
            sourceDocumentReference: sourceDocumentReference);

        record.AddAccessorialCharge(charge);
        _context.CarrierAccessorialCharges.Add(charge);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Accessorial '{Type}' ({Amount:F2} {Currency}) attached to CarrierCostRecord '{Id}'. New Total: {Total:F2}.",
            accessorialType, carrierAmount, currency, record.Id, record.TotalCarrierCost);

        return charge;
    }

    /// <inheritdoc />
    public async Task<ClientFreightAgreement> CreateFreightAgreementAsync(
        CreateFreightAgreementRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ValidationException(nameof(request), "Create freight agreement request cannot be null.");
        }

        ValidateTenantAccess(request.ClientId);

        var normalizedCode = request.AgreementCode?.Trim().ToUpperInvariant() ?? string.Empty;
        var exists = await _context.ClientFreightAgreements
            .AnyAsync(a => a.ClientId == request.ClientId && a.AgreementCode == normalizedCode, cancellationToken);

        if (exists)
        {
            throw new ValidationException(nameof(request.AgreementCode),
                $"A freight agreement with code '{normalizedCode}' already exists for this client tenant.");
        }

        var agreement = ClientFreightAgreement.Create(
            clientId: request.ClientId,
            agreementCode: normalizedCode,
            name: request.Name,
            pricingModel: request.PricingModel,
            currency: request.Currency,
            effectiveFromUtc: request.EffectiveFromUtc,
            effectiveToUtc: request.EffectiveToUtc,
            facilityId: request.FacilityId,
            carrierCode: request.CarrierCode,
            carrierServiceLevel: request.CarrierServiceLevel,
            markupPercentage: request.MarkupPercentage,
            flatFeePerShipment: request.FlatFeePerShipment,
            flatFeePerPackage: request.FlatFeePerPackage,
            fixedFreightRate: request.FixedFreightRate,
            minimumMarkupAmount: request.MinimumMarkupAmount,
            maximumMarkupAmount: request.MaximumMarkupAmount,
            defaultAccessorialTreatment: request.DefaultAccessorialTreatment,
            notes: request.Notes);

        _context.ClientFreightAgreements.Add(agreement);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Client freight agreement '{Code}' ('{Name}') created for Client '{ClientId}'. Model: {Model}.",
            agreement.AgreementCode, agreement.Name, agreement.ClientId, agreement.PricingModel);

        return agreement;
    }

    /// <inheritdoc />
    public async Task<ClientAccessorialRule> AddAccessorialRuleAsync(
        Guid agreementId,
        AccessorialType accessorialType,
        AccessorialBillableTreatment treatment,
        decimal? markupPercentage = null,
        decimal? fixedFee = null,
        CancellationToken cancellationToken = default)
    {
        var agreement = await _context.ClientFreightAgreements
            .Include(a => a.AccessorialRules)
            .FirstOrDefaultAsync(a => a.Id == agreementId, cancellationToken);

        if (agreement == null)
        {
            throw new EntityNotFoundException(nameof(ClientFreightAgreement), agreementId);
        }

        ValidateTenantAccess(agreement.ClientId);

        var rule = agreement.AddAccessorialRule(accessorialType, treatment, markupPercentage, fixedFee);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Accessorial rule for '{Type}' ({Treatment}) added to Agreement '{Code}'.",
            accessorialType, treatment, agreement.AgreementCode);

        return rule;
    }

    /// <inheritdoc />
    public async Task<ClientFreightAgreement?> GetEffectiveFreightAgreementAsync(
        Guid clientId,
        Guid? facilityId,
        string? carrierCode,
        CarrierServiceLevel? serviceLevel,
        DateTime occurredAtUtc,
        string currency = "USD",
        CancellationToken cancellationToken = default)
    {
        ValidateTenantAccess(clientId);

        var normalizedCarrier = carrierCode?.Trim().ToUpperInvariant();
        var normalizedCurrency = currency?.Trim().ToUpperInvariant() ?? "USD";

        // Query active agreements within effective dates and matching currency
        var agreements = await _context.ClientFreightAgreements
            .Include(a => a.AccessorialRules)
            .Where(a => a.ClientId == clientId &&
                        a.IsActive &&
                        a.Currency == normalizedCurrency &&
                        a.EffectiveFromUtc <= occurredAtUtc &&
                        (!a.EffectiveToUtc.HasValue || a.EffectiveToUtc.Value >= occurredAtUtc))
            .ToListAsync(cancellationToken);

        if (agreements.Count == 0)
        {
            return null;
        }

        // Explicit 6-Level Precedence Hierarchy:
        // Level 1: Facility-specific agreement with exact CarrierCode and CarrierServiceLevel
        if (facilityId.HasValue && !string.IsNullOrWhiteSpace(normalizedCarrier) && serviceLevel.HasValue)
        {
            var match = agreements.FirstOrDefault(a =>
                a.FacilityId == facilityId &&
                string.Equals(a.CarrierCode, normalizedCarrier, StringComparison.OrdinalIgnoreCase) &&
                a.CarrierServiceLevel == serviceLevel);
            if (match != null) return match;
        }

        // Level 2: Facility-specific agreement with exact CarrierCode (any service level)
        if (facilityId.HasValue && !string.IsNullOrWhiteSpace(normalizedCarrier))
        {
            var match = agreements.FirstOrDefault(a =>
                a.FacilityId == facilityId &&
                string.Equals(a.CarrierCode, normalizedCarrier, StringComparison.OrdinalIgnoreCase) &&
                !a.CarrierServiceLevel.HasValue);
            if (match != null) return match;
        }

        // Level 3: Facility-specific default agreement (any carrier)
        if (facilityId.HasValue)
        {
            var match = agreements.FirstOrDefault(a =>
                a.FacilityId == facilityId &&
                string.IsNullOrWhiteSpace(a.CarrierCode) &&
                !a.CarrierServiceLevel.HasValue);
            if (match != null) return match;
        }

        // Level 4: Client-wide agreement with exact CarrierCode and CarrierServiceLevel
        if (!string.IsNullOrWhiteSpace(normalizedCarrier) && serviceLevel.HasValue)
        {
            var match = agreements.FirstOrDefault(a =>
                !a.FacilityId.HasValue &&
                string.Equals(a.CarrierCode, normalizedCarrier, StringComparison.OrdinalIgnoreCase) &&
                a.CarrierServiceLevel == serviceLevel);
            if (match != null) return match;
        }

        // Level 5: Client-wide agreement with exact CarrierCode (any service level)
        if (!string.IsNullOrWhiteSpace(normalizedCarrier))
        {
            var match = agreements.FirstOrDefault(a =>
                !a.FacilityId.HasValue &&
                string.Equals(a.CarrierCode, normalizedCarrier, StringComparison.OrdinalIgnoreCase) &&
                !a.CarrierServiceLevel.HasValue);
            if (match != null) return match;
        }

        // Level 6: Client-wide default agreement (any carrier, any service level)
        return agreements.FirstOrDefault(a =>
            !a.FacilityId.HasValue &&
            string.IsNullOrWhiteSpace(a.CarrierCode) &&
            !a.CarrierServiceLevel.HasValue);
    }

    /// <inheritdoc />
    public async Task<RateShipmentFreightResult> RateShipmentFreightAsync(
        Guid shipmentId,
        bool autoCreateBillableActivity = true,
        CancellationToken cancellationToken = default)
    {
        // 1. Fetch Shipment
        var shipment = await _context.Shipments
            .Include(s => s.Packages)
            .FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);

        if (shipment == null)
        {
            throw new EntityNotFoundException(nameof(Shipment), shipmentId);
        }

        ValidateTenantAccess(shipment.ClientId);

        // 2. Idempotency Check: If already rated in ShipmentFreightMarginRecords, return existing snapshot
        var existingMargin = await _context.ShipmentFreightMarginRecords
            .FirstOrDefaultAsync(m => m.ShipmentId == shipmentId, cancellationToken);

        if (existingMargin != null)
        {
            _logger.LogInformation(
                "Shipment '{ShipmentId}' is already rated in freight margin ledger. Preserving immutable snapshot.",
                shipmentId);

            return new RateShipmentFreightResult
            {
                IsSuccess = true,
                IsAlreadyRated = true,
                MarginRecordId = existingMargin.Id,
                ShipmentId = shipmentId,
                BillableActivityId = existingMargin.BillableActivityId,
                TotalCarrierCost = existingMargin.TotalCarrierCost,
                TotalClientFreight = existingMargin.TotalClientFreight,
                GrossMarginAmount = existingMargin.GrossMarginAmount,
                MarkupPercentage = existingMargin.MarkupPercentage,
                MarginPercentage = existingMargin.MarginPercentage,
                Currency = existingMargin.Currency,
                PricingModelApplied = existingMargin.PricingModelApplied,
                Message = "Shipment freight already rated. Preserving historical pricing snapshot."
            };
        }

        // 3. Fetch CarrierCostRecord
        var costRecord = await _context.CarrierCostRecords
            .Include(c => c.AccessorialCharges)
            .FirstOrDefaultAsync(c => c.ShipmentId == shipmentId && c.ClientId == shipment.ClientId, cancellationToken);

        if (costRecord == null)
        {
            throw new InvalidDomainStateException($"No carrier cost record found for Shipment '{shipmentId}'. Freight rating cannot proceed.");
        }

        // 4. Resolve Effective Freight Agreement
        var agreement = await GetEffectiveFreightAgreementAsync(
            clientId: shipment.ClientId,
            facilityId: shipment.FacilityId,
            carrierCode: shipment.CarrierCode,
            serviceLevel: shipment.CarrierServiceLevel,
            occurredAtUtc: costRecord.CostAssessedAtUtc,
            currency: costRecord.Currency,
            cancellationToken: cancellationToken);

        if (agreement == null)
        {
            throw new InvalidDomainStateException(
                $"No applicable active commercial freight agreement found for Client '{shipment.ClientId}', " +
                $"Carrier '{shipment.CarrierCode}' at Facility '{shipment.FacilityId}' in currency '{costRecord.Currency}'.");
        }

        // 5. Currency Guard
        if (!string.Equals(agreement.Currency, costRecord.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(nameof(agreement.Currency),
                $"Currency mismatch: Agreement currency '{agreement.Currency}' does not match carrier cost currency '{costRecord.Currency}'.");
        }

        // 6. Calculate Pricing on Base + Fuel + Insurance
        var packageCount = Math.Max(1, shipment.Packages?.Count ?? shipment.TotalPackagesCount);
        var baseTotalCost = decimal.Round(
            costRecord.BaseTransportationCost + costRecord.FuelSurcharge + costRecord.InsuranceCost,
            4,
            MidpointRounding.AwayFromZero);

        var (clientFreightOnBase, markupAmount, handlingFee) = agreement.CalculatePricing(baseTotalCost, packageCount);

        // 7. Rate Accessorial Surcharges
        decimal clientAccessorialTotal = 0m;
        foreach (var acc in costRecord.AccessorialCharges)
        {
            var rule = agreement.AccessorialRules.FirstOrDefault(r => r.AccessorialType == acc.AccessorialType);
            decimal clientCharge;
            AccessorialBillableTreatment treatment;

            if (rule != null)
            {
                clientCharge = rule.EvaluateClientCharge(acc.CarrierAmount, agreement.MarkupPercentage);
                treatment = rule.Treatment;
            }
            else
            {
                treatment = agreement.DefaultAccessorialTreatment;
                switch (treatment)
                {
                    case AccessorialBillableTreatment.PassThroughAtCost:
                        clientCharge = acc.CarrierAmount;
                        break;
                    case AccessorialBillableTreatment.AbsorbedByWarehouse:
                        clientCharge = 0m;
                        break;
                    case AccessorialBillableTreatment.BillableWithMarkup:
                    default:
                        var pct = (agreement.MarkupPercentage ?? 0m) / 100m;
                        clientCharge = decimal.Round(acc.CarrierAmount * (1m + pct), 4, MidpointRounding.AwayFromZero);
                        break;
                }
            }

            acc.RateClientCharge(clientCharge, treatment);
            clientAccessorialTotal += clientCharge;
        }

        clientAccessorialTotal = decimal.Round(clientAccessorialTotal, 4, MidpointRounding.AwayFromZero);
        var totalClientFreight = decimal.Round(clientFreightOnBase + clientAccessorialTotal, 4, MidpointRounding.AwayFromZero);

        // 8. Create BillableActivity (Commercial Ledger Linkage)
        Guid? billableActivityId = null;
        if (autoCreateBillableActivity)
        {
            var activity = BillableActivity.Create(
                clientId: shipment.ClientId,
                facilityId: shipment.FacilityId,
                activityType: BillableActivityType.CarrierFreight,
                sourceType: "Shipment",
                sourceId: shipment.Id,
                sourceReference: shipment.ShipmentNumber,
                quantity: 1m,
                unitOfMeasure: "SHIPMENT",
                occurredAtUtc: costRecord.CostAssessedAtUtc,
                idempotencyKey: $"FREIGHT-{shipment.Id:N}",
                description: $"Carrier freight ({shipment.CarrierCode} {shipment.CarrierServiceLevel}) for {shipment.ShipmentNumber}");

            activity.Rate(
                unitPrice: totalClientFreight,
                totalAmount: totalClientFreight,
                currency: agreement.Currency,
                rateCardId: null,
                rateCardTierId: null,
                rateCardVersion: null,
                ratedByUserId: null,
                ratedAtUtc: DateTime.UtcNow);

            _context.BillableActivities.Add(activity);
            await _context.SaveChangesAsync(cancellationToken);
            billableActivityId = activity.Id;
        }

        // 9. Construct Immutable Freight Margin Record
        decimal clientBasePortion = costRecord.BaseTransportationCost;
        decimal clientFuelPortion = costRecord.FuelSurcharge;
        decimal clientInsPortion = costRecord.InsuranceCost;
        decimal clientMarkupPortion = markupAmount;
        decimal clientHandlingPortion = handlingFee;

        if (agreement.PricingModel == FreightPricingModel.FixedContractualRate)
        {
            clientBasePortion = agreement.FixedFreightRate ?? 0m;
            clientFuelPortion = 0m;
            clientInsPortion = 0m;
            clientMarkupPortion = 0m;
            clientHandlingPortion = 0m;
        }

        var marginRecord = ShipmentFreightMarginRecord.Create(
            clientId: shipment.ClientId,
            facilityId: shipment.FacilityId,
            shipmentId: shipment.Id,
            carrierCostRecordId: costRecord.Id,
            carrierCode: shipment.CarrierCode,
            carrierServiceLevel: shipment.CarrierServiceLevel,
            pricingModelApplied: agreement.PricingModel,
            packageCount: packageCount,
            carrierBaseCost: costRecord.BaseTransportationCost,
            carrierFuelSurcharge: costRecord.FuelSurcharge,
            carrierInsuranceCost: costRecord.InsuranceCost,
            carrierAccessorialCost: costRecord.AccessorialCost,
            clientBaseFreight: clientBasePortion,
            clientFuelCharge: clientFuelPortion,
            clientInsuranceCharge: clientInsPortion,
            clientAccessorialCharge: clientAccessorialTotal,
            clientMarkupAmount: clientMarkupPortion,
            clientHandlingFee: clientHandlingPortion,
            currency: agreement.Currency,
            idempotencyKey: $"MARGIN-{shipment.Id:N}",
            ratedAtUtc: DateTime.UtcNow,
            freightAgreementId: agreement.Id,
            billableActivityId: billableActivityId,
            trackingNumber: costRecord.TrackingNumber);

        _context.ShipmentFreightMarginRecords.Add(marginRecord);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Freight margin rated for Shipment '{ShipmentNumber}': Carrier Cost: {Cost:F2}, Client Freight: {Sell:F2}, Margin: {Margin:F2} {Currency} (Markup: {Markup:F1}%, Margin: {MarginPct:F1}%).",
            shipment.ShipmentNumber, marginRecord.TotalCarrierCost, marginRecord.TotalClientFreight, marginRecord.GrossMarginAmount,
            marginRecord.Currency, marginRecord.MarkupPercentage, marginRecord.MarginPercentage);

        return new RateShipmentFreightResult
        {
            IsSuccess = true,
            IsAlreadyRated = false,
            MarginRecordId = marginRecord.Id,
            ShipmentId = shipment.Id,
            BillableActivityId = billableActivityId,
            TotalCarrierCost = marginRecord.TotalCarrierCost,
            TotalClientFreight = marginRecord.TotalClientFreight,
            GrossMarginAmount = marginRecord.GrossMarginAmount,
            MarkupPercentage = marginRecord.MarkupPercentage,
            MarginPercentage = marginRecord.MarginPercentage,
            Currency = marginRecord.Currency,
            PricingModelApplied = marginRecord.PricingModelApplied,
            Message = "Shipment freight successfully rated with gross margin ledger entry."
        };
    }

    /// <inheritdoc />
    public async Task<ReconcileCarrierInvoiceResult> ReconcileCarrierInvoiceCostAsync(
        ReconcileCarrierInvoiceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ValidationException(nameof(request), "Reconcile carrier invoice request cannot be null.");
        }

        ValidateTenantAccess(request.ClientId);

        var costRecord = await _context.CarrierCostRecords
            .Include(c => c.Adjustments)
            .FirstOrDefaultAsync(c => c.ShipmentId == request.ShipmentId && c.ClientId == request.ClientId, cancellationToken);

        if (costRecord == null)
        {
            throw new EntityNotFoundException(nameof(CarrierCostRecord), request.ShipmentId);
        }

        var marginRecord = await _context.ShipmentFreightMarginRecords
            .FirstOrDefaultAsync(m => m.ShipmentId == request.ShipmentId, cancellationToken);

        var originalCost = costRecord.TotalCarrierCost;
        var invoicedCost = decimal.Round(request.InvoicedTotalCost, 4, MidpointRounding.AwayFromZero);
        var costDelta = decimal.Round(invoicedCost - originalCost, 4, MidpointRounding.AwayFromZero);

        // If no cost difference, simply record reconciliation
        if (costDelta == 0m)
        {
            costRecord.ReconcileWithInvoice(request.CarrierInvoiceNumber, request.ReconciledAtUtc, CarrierCostStatus.Reconciled);
            if (marginRecord != null)
            {
                marginRecord.IsReconciled = true;
            }
            await _context.SaveChangesAsync(cancellationToken);

            return new ReconcileCarrierInvoiceResult
            {
                IsSuccess = true,
                HasDiscrepancy = false,
                CarrierCostRecordId = costRecord.Id,
                OriginalCarrierCost = originalCost,
                InvoicedCarrierCost = invoicedCost,
                CarrierCostDelta = 0m,
                ClientFreightDelta = 0m,
                NewNetGrossMargin = marginRecord?.GrossMarginAmount ?? 0m,
                Currency = costRecord.Currency,
                Message = "Carrier invoice reconciled with zero discrepancy."
            };
        }

        // Calculate Client Freight Delta
        decimal clientFreightDelta = 0m;
        if (request.ClientFreightDeltaOverride.HasValue)
        {
            clientFreightDelta = decimal.Round(request.ClientFreightDeltaOverride.Value, 4, MidpointRounding.AwayFromZero);
        }
        else if (request.BillAdjustmentToClient)
        {
            // If margin record exists and has a freight agreement with markup, calculate markup on the delta
            if (marginRecord?.FreightAgreementId != null)
            {
                var agreement = await _context.ClientFreightAgreements
                    .FirstOrDefaultAsync(a => a.Id == marginRecord.FreightAgreementId.Value, cancellationToken);

                if (agreement != null && agreement.PricingModel == FreightPricingModel.CostPlusPercentageMarkup)
                {
                    var pct = (agreement.MarkupPercentage ?? 0m) / 100m;
                    clientFreightDelta = decimal.Round(costDelta * (1m + pct), 4, MidpointRounding.AwayFromZero);
                }
                else
                {
                    clientFreightDelta = costDelta; // Pass through delta
                }
            }
            else
            {
                clientFreightDelta = costDelta;
            }
        }

        // Idempotency check for adjustment
        var adjKey = $"CARRIER-ADJ-{costRecord.ShipmentId:N}-{request.CarrierInvoiceNumber.Trim()}-{request.AdjustmentType}";
        var existingAdj = await _context.CarrierCostAdjustments
            .FirstOrDefaultAsync(a => a.ClientId == costRecord.ClientId && a.IdempotencyKey == adjKey, cancellationToken);

        if (existingAdj != null)
        {
            _logger.LogInformation("Duplicate carrier cost adjustment '{Key}' detected. Returning existing adjustment.", adjKey);
            return new ReconcileCarrierInvoiceResult
            {
                IsSuccess = true,
                HasDiscrepancy = true,
                CarrierCostRecordId = costRecord.Id,
                AdjustmentId = existingAdj.Id,
                AdjustmentBillableActivityId = existingAdj.BillableActivityAdjustmentId,
                OriginalCarrierCost = existingAdj.OriginalCarrierCost,
                InvoicedCarrierCost = existingAdj.AdjustedCarrierCost,
                CarrierCostDelta = existingAdj.CarrierCostDelta,
                ClientFreightDelta = existingAdj.ClientFreightDelta,
                NewNetGrossMargin = marginRecord?.NetAdjustedMargin ?? marginRecord?.GrossMarginAmount ?? 0m,
                Currency = costRecord.Currency,
                Message = "Adjustment already reconciled for this invoice and adjustment type."
            };
        }

        // Create CarrierCostAdjustment
        Guid? adjustmentActivityId = null;
        if (request.BillAdjustmentToClient && clientFreightDelta != 0m)
        {
            var adjActivity = BillableActivity.Create(
                clientId: costRecord.ClientId,
                facilityId: costRecord.FacilityId,
                activityType: BillableActivityType.CarrierFreight,
                sourceType: "CarrierInvoiceAdjustment",
                sourceId: costRecord.ShipmentId,
                sourceReference: request.CarrierInvoiceNumber,
                quantity: 1m,
                unitOfMeasure: "ADJUSTMENT",
                occurredAtUtc: request.ReconciledAtUtc,
                idempotencyKey: $"FREIGHT-ADJ-{costRecord.ShipmentId:N}-{request.CarrierInvoiceNumber.Trim()}",
                description: $"Carrier invoice freight adjustment ({request.AdjustmentType}) for invoice {request.CarrierInvoiceNumber}: {request.Reason}");

            adjActivity.Rate(
                unitPrice: Math.Abs(clientFreightDelta),
                totalAmount: Math.Abs(clientFreightDelta),
                currency: costRecord.Currency);

            if (clientFreightDelta < 0m)
            {
                // Credit adjustment
                adjActivity.Description = $"[Credit Adjustment]: {adjActivity.Description}";
            }

            _context.BillableActivities.Add(adjActivity);
            await _context.SaveChangesAsync(cancellationToken);
            adjustmentActivityId = adjActivity.Id;
        }

        var adjustment = CarrierCostAdjustment.Create(
            clientId: costRecord.ClientId,
            facilityId: costRecord.FacilityId,
            carrierCostRecordId: costRecord.Id,
            adjustmentType: request.AdjustmentType,
            carrierInvoiceNumber: request.CarrierInvoiceNumber,
            originalCarrierCost: originalCost,
            adjustedCarrierCost: invoicedCost,
            clientFreightDelta: clientFreightDelta,
            currency: costRecord.Currency,
            reason: request.Reason,
            idempotencyKey: adjKey,
            adjustedAtUtc: request.ReconciledAtUtc,
            shipmentFreightMarginRecordId: marginRecord?.Id,
            billableActivityAdjustmentId: adjustmentActivityId);

        _context.CarrierCostAdjustments.Add(adjustment);

        // Update CarrierCostRecord
        costRecord.ReconcileWithInvoice(request.CarrierInvoiceNumber, request.ReconciledAtUtc, CarrierCostStatus.Reconciled);

        // Update Margin Record if present
        if (marginRecord != null)
        {
            marginRecord.ApplyAdjustment(costDelta, clientFreightDelta);
            marginRecord.IsReconciled = true;
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Carrier invoice reconciliation completed for Shipment '{ShipmentId}'. Cost Delta: {CostDelta:F2}, Client Delta: {ClientDelta:F2}, New Net Margin: {Margin:F2} {Currency}.",
            costRecord.ShipmentId, costDelta, clientFreightDelta, marginRecord?.NetAdjustedMargin, costRecord.Currency);

        return new ReconcileCarrierInvoiceResult
        {
            IsSuccess = true,
            HasDiscrepancy = true,
            CarrierCostRecordId = costRecord.Id,
            AdjustmentId = adjustment.Id,
            AdjustmentBillableActivityId = adjustmentActivityId,
            OriginalCarrierCost = originalCost,
            InvoicedCarrierCost = invoicedCost,
            CarrierCostDelta = costDelta,
            ClientFreightDelta = clientFreightDelta,
            NewNetGrossMargin = marginRecord?.NetAdjustedMargin ?? (originalCost - invoicedCost),
            Currency = costRecord.Currency,
            Message = $"Carrier invoice reconciled with adjustment delta of {costDelta:F2} {costRecord.Currency}."
        };
    }

    private void ValidateTenantAccess(Guid clientId)
    {
        if (_tenantProvider.TenantId.HasValue &&
            !_tenantProvider.BypassTenantFilter &&
            _tenantProvider.TenantId.Value != clientId)
        {
            throw new UnauthorizedException("Unauthorized cross-tenant freight margin operation.");
        }
    }
}
