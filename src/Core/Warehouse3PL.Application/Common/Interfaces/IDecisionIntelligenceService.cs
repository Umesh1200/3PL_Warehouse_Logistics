using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Enums;
namespace Warehouse3PL.Application.Common.Interfaces;
public interface IDecisionIntelligenceService
{
    Task<LaborDecisionDto> GetLaborAdviceAsync(Guid facilityId, DateTime startDateUtc, DateTime endDateUtc, CancellationToken ct = default);
    Task<CarrierDecisionDto> GetCarrierAdviceAsync(IReadOnlyList<string> carrierCodes, DateTime startDateUtc, DateTime endDateUtc,
        Guid? clientId, Guid? facilityId, CarrierServiceLevel? serviceLevel, CancellationToken ct = default);
}
