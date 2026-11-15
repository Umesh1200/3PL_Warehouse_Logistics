namespace Warehouse3PL.Application.Common.Interfaces;

public record PutawayRecommendation(
    Guid LocationId,
    string LocationCode,
    string Barcode,
    string ZoneCode,
    decimal FitScore,
    string Reason);

public interface IPutawayRecommendationService
{
    Task<List<PutawayRecommendation>> RecommendLocationsAsync(
        Guid clientId,
        Guid facilityId,
        Guid itemId,
        decimal quantity,
        Guid? lotId = null,
        CancellationToken cancellationToken = default);
}

