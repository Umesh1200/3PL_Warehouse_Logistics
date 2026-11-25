using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Reference;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Reference.Commands;

public sealed record CreateCompliancePolicyDraftCommand(
    string CountryCode,
    string PolicyCode,
    string Title,
    string? StatutorySourceRef,
    string? OfficialNoticeCitation,
    string ApplicabilityPredicateJson,
    ComplianceEnforcementMode EnforcementMode,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc) : IRequest<CountryCompliancePolicyDto>;

public sealed class CreateCompliancePolicyDraftCommandValidator
    : AbstractValidator<CreateCompliancePolicyDraftCommand>
{
    public CreateCompliancePolicyDraftCommandValidator()
    {
        RuleFor(x => x.CountryCode)
            .NotEmpty()
            .Length(2)
            .Matches("^[A-Za-z]{2}$");

        RuleFor(x => x.PolicyCode)
            .NotEmpty()
            .MaximumLength(50);

        RuleFor(x => x.Title)
            .NotEmpty()
            .MaximumLength(160);

        RuleFor(x => x.StatutorySourceRef)
            .MaximumLength(200);

        RuleFor(x => x.OfficialNoticeCitation)
            .MaximumLength(300);

        RuleFor(x => x.ApplicabilityPredicateJson)
            .NotEmpty();

        RuleFor(x => x.EffectiveFromUtc)
            .Must(dt => dt.Kind != DateTimeKind.Unspecified)
            .WithMessage("EffectiveFromUtc must explicitly include UTC ('Z') or a timezone offset.");

        RuleFor(x => x.EffectiveToUtc)
            .Must(to => !to.HasValue || to.Value.Kind != DateTimeKind.Unspecified)
            .WithMessage("EffectiveToUtc must explicitly include UTC ('Z') or a timezone offset when specified.")
            .Must((cmd, to) => !to.HasValue || ToUtcForCompare(to.Value) > ToUtcForCompare(cmd.EffectiveFromUtc))
            .WithMessage("EffectiveToUtc must be strictly greater than EffectiveFromUtc.");
    }

    private static DateTime ToUtcForCompare(DateTime dt) =>
        dt.Kind == DateTimeKind.Local ? dt.ToUniversalTime() : dt;
}

public sealed class CreateCompliancePolicyDraftCommandHandler
    : IRequestHandler<CreateCompliancePolicyDraftCommand, CountryCompliancePolicyDto>
{
    private readonly IApplicationDbContext _db;
    private readonly CompliancePlatformAuthorization _auth;

    public CreateCompliancePolicyDraftCommandHandler(
        IApplicationDbContext db,
        CompliancePlatformAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<CountryCompliancePolicyDto> Handle(
        CreateCompliancePolicyDraftCommand request,
        CancellationToken cancellationToken)
    {
        var staffUserId = await _auth.RequireVerifiedStaffPermissionAsync(
            cancellationToken,
            Permissions.Global.ComplianceManage);

        var countryCode = request.CountryCode.Trim().ToUpperInvariant();
        var policyCode = request.PolicyCode.Trim().ToUpperInvariant();

        var countryExists = await _db.CountryProfiles
            .AsNoTracking()
            .AnyAsync(c => c.CountryCode == countryCode && c.IsActive, cancellationToken);

        if (!countryExists)
        {
            throw new DomainException(
                "COMPLIANCE_UNKNOWN_COUNTRY",
                $"CountryCode '{countryCode}' is not a registered active CountryProfile.");
        }

        if (!ReferenceMappings.TryParsePredicate(request.ApplicabilityPredicateJson, out _, out var predicateError))
        {
            throw new DomainException("COMPLIANCE_INVALID_PREDICATE", predicateError!);
        }

        return await _db.ExecuteWithCompliancePolicyLockAsync(countryCode, policyCode, async () =>
        {
            var maxVersion = await _db.CountryCompliancePolicies
                .AsNoTracking()
                .Where(p => p.CountryCode == countryCode && p.PolicyCode == policyCode)
                .Select(p => (int?)p.Version)
                .MaxAsync(cancellationToken) ?? 0;

            var draft = CountryCompliancePolicy.CreateDraft(
                countryCode,
                policyCode,
                maxVersion + 1,
                request.Title,
                request.StatutorySourceRef ?? string.Empty,
                request.OfficialNoticeCitation ?? string.Empty,
                request.ApplicabilityPredicateJson,
                request.EnforcementMode,
                request.EffectiveFromUtc,
                request.EffectiveToUtc);

            draft.CreatedBy = staffUserId;

            _db.CountryCompliancePolicies.Add(draft);
            await _db.SaveChangesAsync(cancellationToken);

            return draft.ToDto();
        }, cancellationToken);
    }
}
