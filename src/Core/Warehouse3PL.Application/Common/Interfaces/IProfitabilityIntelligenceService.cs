using Warehouse3PL.Application.Common.Models;
namespace Warehouse3PL.Application.Common.Interfaces;
public interface IProfitabilityIntelligenceService
{
    Task<ProfitabilityReportDto> GetReportAsync(DateTime start, DateTime end, Guid? clientId, Guid? facilityId, CancellationToken ct = default);
}
