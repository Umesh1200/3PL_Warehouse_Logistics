using FluentValidation;
using Mapster;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Helpers;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.Application.Features.Clients.Commands.UpdateClient;

public record UpdateClientCommand : IRequest<ClientDto>
{
    public Guid Id { get; init; }
    public string CompanyName { get; init; } = string.Empty;
    public string? AccountCode { get; init; }
    public ClientStatus Status { get; init; }
    public SlaTier SlaTier { get; init; }
    public string Currency { get; init; } = "USD";
    public string? BusinessClassification { get; init; }
    public string? TaxRegistrationNumber { get; init; }
    public string? BillingAddressLine1 { get; init; }
    public string? BillingAddressLine2 { get; init; }
    public string? BillingCity { get; init; }
    public string? BillingStateOrProvince { get; init; }
    public string? BillingPostalCode { get; init; }
    public string? BillingCountryCode { get; init; }
    public int? ExpectedConfigRevision { get; init; }
}

public class UpdateClientCommandValidator : AbstractValidator<UpdateClientCommand>
{
    public UpdateClientCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.CompanyName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.BusinessClassification).MaximumLength(100);
        RuleFor(x => x.TaxRegistrationNumber).MaximumLength(64);
        RuleFor(x => x.BillingAddressLine1).MaximumLength(255);
        RuleFor(x => x.BillingAddressLine2).MaximumLength(255);
        RuleFor(x => x.BillingCity).MaximumLength(100);
        RuleFor(x => x.BillingStateOrProvince).MaximumLength(100);
        RuleFor(x => x.BillingPostalCode).MaximumLength(30);
        RuleFor(x => x.BillingCountryCode).MaximumLength(50);
    }
}

public class UpdateClientCommandHandler : IRequestHandler<UpdateClientCommand, ClientDto>
{
    private readonly IApplicationDbContext _context;
    private readonly PartnerMasterAuthorization _auth;

    public UpdateClientCommandHandler(
        IApplicationDbContext context,
        ICurrentUserContext currentUser,
        PartnerMasterAuthorization? auth = null)
    {
        _context = context;
        _auth = auth ?? new PartnerMasterAuthorization(context, currentUser);
    }

    public async Task<ClientDto> Handle(UpdateClientCommand request, CancellationToken cancellationToken)
    {
        var (caller, _) = await _auth.ResolveAuthorizedClientScopeAsync(
            request.Id,
            allowClientSelf: true,
            cancellationToken,
            allowedStaffPermissions: [Permissions.Clients.Edit],
            allowedClientPermissions: [Permissions.Clients.Edit]);

        var currency = await _auth.ValidateRequiredCurrencyAsync(request.Currency, cancellationToken);

        var (billingCountry, countryNormStatus) = await PartnerAddressValidationHelper.ValidateAndNormalizeAddressAsync(
            _context,
            request.BillingCountryCode,
            request.BillingStateOrProvince,
            request.BillingPostalCode,
            "Billing",
            cancellationToken);

        return await _context.ExecuteWithClientLifecycleLockAsync(request.Id, async () =>
        {
            var client = await _context.Clients
                .Include(c => c.Contacts)
                .Include(c => c.Users)
                .FirstOrDefaultAsync(c => c.Id == request.Id && !c.IsDeleted, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Client), request.Id);

            if (!string.IsNullOrWhiteSpace(request.AccountCode) &&
                !string.Equals(Client.NormalizeCode(request.AccountCode), client.NormalizedAccountCode, StringComparison.Ordinal))
            {
                throw new DomainException(
                    $"Client account code '{client.AccountCode}' is immutable after creation.",
                    "CLIENT_CODE_IMMUTABLE");
            }

            if (request.ExpectedConfigRevision.HasValue && request.ExpectedConfigRevision.Value != client.ConfigRevision)
            {
                throw new ConcurrencyException(
                    "CLIENT_CONCURRENCY_CONFLICT",
                    $"Client '{client.AccountCode}' was modified concurrently (expected revision {request.ExpectedConfigRevision.Value}, actual {client.ConfigRevision}).");
            }

            bool transitioningToRestricted =
                (request.Status == ClientStatus.Suspended || request.Status == ClientStatus.Terminated) &&
                client.Status != request.Status;

            if (transitioningToRestricted)
            {
                if (caller.UserType != UserType.Staff)
                {
                    throw new ForbiddenException("Only authorized platform Staff may suspend or terminate a client account.");
                }
                await PartnerMasterDependencyGuard.EnsureClientCanBeDeactivatedAsync(
                    _context, client.Id, client.AccountCode, request.Status, cancellationToken);
            }

            client.CompanyName = request.CompanyName.Trim();
            client.Status = request.Status;
            client.SlaTier = request.SlaTier;
            client.Currency = currency;
            client.BusinessClassification = request.BusinessClassification?.Trim();
            client.TaxRegistrationNumber = request.TaxRegistrationNumber?.Trim();
            client.BillingAddressLine1 = request.BillingAddressLine1?.Trim();
            client.BillingAddressLine2 = request.BillingAddressLine2?.Trim();
            client.BillingCity = request.BillingCity?.Trim();
            client.BillingStateOrProvince = request.BillingStateOrProvince?.Trim();
            client.BillingPostalCode = request.BillingPostalCode?.Trim();
            client.BillingCountryCode = billingCountry;
            client.CountryNormalizationStatus = countryNormStatus;
            client.ConfigRevision++;

            await _context.SaveChangesAsync(cancellationToken);

            return client.Adapt<ClientDto>();
        }, cancellationToken);
    }
}
