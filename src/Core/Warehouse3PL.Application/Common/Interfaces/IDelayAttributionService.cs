using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

/// <summary>
/// Service assessing root-cause and operational responsibility for carrier delivery exceptions and delays.
/// Separates empirical exception categories from fault attribution.
/// DB-SPEC-3PL-2026 Phase 6.4.3 Carrier Scorecards & Exception Analytics.
/// </summary>
public interface IDelayAttributionService
{
    DelayAttributionResultDto AttributeException(
        Guid shipmentId,
        string trackingNumber,
        string carrierCode,
        CanonicalDeliveryExceptionCategory category,
        string? rawCarrierCode = null,
        string? rawDescription = null,
        bool isDispatchedAfterCutoff = false);

    DelayAttributionResultDto AttributeLostShipment(
        Guid shipmentId,
        string trackingNumber,
        string carrierCode,
        bool hasCarrierScanEvidence,
        bool isThresholdBreached,
        string? details = null);

    Task<IReadOnlyList<DelayAttributionResultDto>> GetShipmentDelayAttributionsAsync(
        Guid shipmentId,
        CancellationToken cancellationToken = default);
}
