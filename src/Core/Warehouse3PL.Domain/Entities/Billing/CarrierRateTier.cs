using Warehouse3PL.Domain.Common;

namespace Warehouse3PL.Domain.Entities.Billing;

/// <summary>
/// Volume pricing bracket tier within a buy-side CarrierRateAgreement.
/// </summary>
public class CarrierRateTier : AuditableEntity<Guid>
{
    public Guid CarrierRateAgreementId { get; set; }
    public int TierNumber { get; set; }
    public decimal LowerBound { get; set; }
    public decimal? UpperBound { get; set; }
    public decimal Rate { get; set; }

    // Navigations
    public CarrierRateAgreement CarrierRateAgreement { get; set; } = null!;

    public CarrierRateTier()
    {
        Id = Guid.NewGuid();
    }
}
