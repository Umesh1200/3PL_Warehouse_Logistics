using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

/// <summary>
/// Service providing carrier performance scorecards, transit time distributions, and exception intelligence.
/// Operates strictly as a read-only analytics query service over specialized read models.
/// DB-SPEC-3PL-2026 Phase 6.4.3 Carrier Scorecards & Exception Analytics.
/// </summary>
public interface ICarrierScorecardService
{
    Task<CarrierScorecardDto> GetCarrierScorecardAsync(
        string carrierCode,
        DateTime startDateUtc,
        DateTime endDateUtc,
        Guid? facilityId = null,
        Guid? clientId = null,
        CarrierServiceLevel? serviceLevel = null,
        CancellationToken cancellationToken = default);

    Task<TransitTimeDistributionDto> GetTransitTimeDistributionAsync(
        string carrierCode,
        DateTime startDateUtc,
        DateTime endDateUtc,
        Guid? facilityId = null,
        Guid? clientId = null,
        CarrierServiceLevel? serviceLevel = null,
        string? destinationCountry = null,
        string? destinationState = null,
        CancellationToken cancellationToken = default);

    Task<DeliveryExceptionIntelligenceDto> GetDeliveryExceptionIntelligenceAsync(
        string carrierCode,
        DateTime startDateUtc,
        DateTime endDateUtc,
        Guid? facilityId = null,
        Guid? clientId = null,
        CancellationToken cancellationToken = default);
}
