using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Shipping.Queries;

public record GetShippingLabelByIdQuery(Guid LabelId) : IRequest<ShippingLabelDto?>;

public class GetShippingLabelByIdQueryHandler : IRequestHandler<GetShippingLabelByIdQuery, ShippingLabelDto?>
{
    private readonly IShippingLabelService _shippingLabelService;

    public GetShippingLabelByIdQueryHandler(IShippingLabelService shippingLabelService)
    {
        _shippingLabelService = shippingLabelService;
    }

    public async Task<ShippingLabelDto?> Handle(
        GetShippingLabelByIdQuery query,
        CancellationToken cancellationToken)
    {
        return await _shippingLabelService.GetLabelAsync(query.LabelId, cancellationToken);
    }
}

