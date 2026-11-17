#nullable enable
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Application.Common.Models;
namespace Warehouse3PL.Infrastructure.Services.Analytics;
public partial class AnalyticsProjectionService{
public async Task<SkuVelocityClassificationRecord> CalculateSkuVelocityAsync(
        Guid facilityId,
        Guid clientId,
        Guid itemId,
        string sku,
        VelocityObservationWindow window = VelocityObservationWindow.Days90,
        AbcClassificationCriteria criteria = AbcClassificationCriteria.PickFrequency,
        DateTime? asOfUtc = null,
        int version = 1,
        CancellationToken cancellationToken = default) { SkuVelocityClassificationRecord? result=null; await JournalIngestAsync(SkuVelocityProjection,nameof(CalculateSkuVelocityAsync),Guid.NewGuid(),0,asOfUtc??DateTime.UtcNow,new {facilityId, clientId, itemId, sku, window, criteria, asOfUtc, version},async()=>{result=await CalculateSkuVelocityAsyncCore(facilityId, clientId, itemId, sku, window, criteria, asOfUtc, version, cancellationToken);},cancellationToken);return result!;}
public async Task<AbcClassificationBatchResultDto> CalculateAbcClassificationBatchAsync(
        Guid facilityId,
        Guid? clientId = null,
        Guid? policyId = null,
        AbcClassificationCriteria? criteriaOverride = null,
        VelocityObservationWindow? windowOverride = null,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default) { AbcClassificationBatchResultDto? result=null; await JournalIngestAsync(SkuVelocityProjection,nameof(CalculateAbcClassificationBatchAsync),Guid.NewGuid(),0,asOfUtc??DateTime.UtcNow,new {facilityId, clientId, policyId, criteriaOverride, windowOverride, asOfUtc},async()=>{result=await CalculateAbcClassificationBatchAsyncCore(facilityId, clientId, policyId, criteriaOverride, windowOverride, asOfUtc, cancellationToken);},cancellationToken);return result!;}
}
