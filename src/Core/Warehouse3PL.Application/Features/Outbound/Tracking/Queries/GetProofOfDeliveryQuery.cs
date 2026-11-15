using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Tracking.Queries;

public record GetProofOfDeliveryQuery(
    Guid? PackageId = null,
    Guid? ProofOfDeliveryId = null,
    TimeSpan? UrlExpiry = null) : IRequest<ProofOfDeliveryDto?>;

public class GetProofOfDeliveryQueryHandler : IRequestHandler<GetProofOfDeliveryQuery, ProofOfDeliveryDto?>
{
    private readonly IProofOfDeliveryService _podService;

    public GetProofOfDeliveryQueryHandler(IProofOfDeliveryService podService)
    {
        _podService = podService;
    }

    public async Task<ProofOfDeliveryDto?> Handle(GetProofOfDeliveryQuery query, CancellationToken cancellationToken)
    {
        if (query.PackageId.HasValue)
        {
            return await _podService.GetProofOfDeliveryByPackageIdAsync(query.PackageId.Value, query.UrlExpiry, cancellationToken);
        }

        if (query.ProofOfDeliveryId.HasValue)
        {
            return await _podService.GetProofOfDeliveryByIdAsync(query.ProofOfDeliveryId.Value, query.UrlExpiry, cancellationToken);
        }

        return null;
    }
}

