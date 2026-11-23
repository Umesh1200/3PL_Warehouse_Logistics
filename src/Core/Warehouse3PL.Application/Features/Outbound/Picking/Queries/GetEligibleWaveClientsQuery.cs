using MediatR;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Outbound.OrderManagement.Queries.GetOutboundReferenceClients;

namespace Warehouse3PL.Application.Features.Outbound.Picking.Queries;

/// <summary>
/// Permission-consistent lookup of active client tenants eligible for pick wave planning in an authorized facility.
/// Restricted to warehouse staff holding Permissions.Outbound.WavesManage.
/// </summary>
public record GetEligibleWaveClientsQuery(
    Guid? FacilityId = null,
    string? SearchTerm = null,
    int PageNumber = 1,
    int PageSize = 100) : IRequest<PagedResult<OutboundReferenceClientDto>>;

public class GetEligibleWaveClientsQueryHandler
    : IRequestHandler<GetEligibleWaveClientsQuery, PagedResult<OutboundReferenceClientDto>>
{
    private readonly ISender _mediator;

    public GetEligibleWaveClientsQueryHandler(ISender mediator)
    {
        _mediator = mediator;
    }

    public Task<PagedResult<OutboundReferenceClientDto>> Handle(
        GetEligibleWaveClientsQuery request,
        CancellationToken cancellationToken)
    {
        return _mediator.Send(
            new GetOutboundReferenceClientsQuery(
                request.FacilityId,
                request.SearchTerm,
                request.PageNumber,
                request.PageSize),
            cancellationToken);
    }
}
