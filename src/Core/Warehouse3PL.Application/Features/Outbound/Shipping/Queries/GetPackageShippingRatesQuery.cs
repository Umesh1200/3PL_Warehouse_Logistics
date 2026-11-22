using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Shipping.Queries;

public record GetPackageShippingRatesQuery(
    Guid PackageId,
    string? PreferredCarrierCode = null,
    CarrierServiceLevel? PreferredServiceLevel = null) : IRequest<IReadOnlyList<CarrierRateQuote>>;

public class GetPackageShippingRatesQueryHandler : IRequestHandler<GetPackageShippingRatesQuery, IReadOnlyList<CarrierRateQuote>>
{
    private readonly IShippingLabelService _shippingLabelService;
    private readonly IApplicationDbContext _context;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public GetPackageShippingRatesQueryHandler(
        IShippingLabelService shippingLabelService,
        IApplicationDbContext context,
        OutboundOperationalAuthorization outboundAuth)
    {
        _shippingLabelService = shippingLabelService;
        _context = context;
        _outboundAuth = outboundAuth;
    }

    public async Task<IReadOnlyList<CarrierRateQuote>> Handle(
        GetPackageShippingRatesQuery query,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(GetPackageShippingRatesQuery));

        var package = await _context.Packages
            .AsNoTracking()
            .Where(p => p.Id == query.PackageId && !p.IsDeleted)
            .Select(p => new { p.Id, p.FacilityId })
            .FirstOrDefaultAsync(cancellationToken);

        if (package == null)
        {
            throw new EntityNotFoundException(nameof(Package), query.PackageId);
        }

        _outboundAuth.ValidateStaffMutationFacilityAccess(package.FacilityId, nameof(GetPackageShippingRatesQuery));

        return await _shippingLabelService.GetRatesForPackageAsync(new GetRatesForPackageRequest
        {
            PackageId = query.PackageId,
            PreferredCarrierCode = query.PreferredCarrierCode,
            PreferredServiceLevel = query.PreferredServiceLevel
        }, cancellationToken);
    }
}

