using MediatR;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Outbound.Shipping.Queries;

public record GetPackageShippingRatesQuery(
    Guid PackageId,
    string? PreferredCarrierCode = null,
    CarrierServiceLevel? PreferredServiceLevel = null) : IRequest<IReadOnlyList<CarrierRateQuote>>;

public class GetPackageShippingRatesQueryHandler : IRequestHandler<GetPackageShippingRatesQuery, IReadOnlyList<CarrierRateQuote>>
{
    private readonly IShippingLabelService _shippingLabelService;

    public GetPackageShippingRatesQueryHandler(IShippingLabelService shippingLabelService)
    {
        _shippingLabelService = shippingLabelService;
    }

    public async Task<IReadOnlyList<CarrierRateQuote>> Handle(
        GetPackageShippingRatesQuery query,
        CancellationToken cancellationToken)
    {
        return await _shippingLabelService.GetRatesForPackageAsync(new GetRatesForPackageRequest
        {
            PackageId = query.PackageId,
            PreferredCarrierCode = query.PreferredCarrierCode,
            PreferredServiceLevel = query.PreferredServiceLevel
        }, cancellationToken);
    }
}

