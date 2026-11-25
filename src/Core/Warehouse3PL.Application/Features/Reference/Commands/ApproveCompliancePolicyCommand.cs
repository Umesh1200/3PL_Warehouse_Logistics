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

public sealed record ApproveCompliancePolicyCommand(
    Guid PolicyId,
    bool EvidenceVerified,
    string VerificationEvidenceNotes,
    string? StatutorySourceRef = null,
    string? OfficialNoticeCitation = null,
    bool SupersedeOverlapping = false,
    Guid? ExpectedActivePolicyId = null,
    long? ExpectedConfigRevision = null) : IRequest<CountryCompliancePolicyDto>;

public sealed class ApproveCompliancePolicyCommandValidator
    : AbstractValidator<ApproveCompliancePolicyCommand>
{
    public ApproveCompliancePolicyCommandValidator()
    {
        RuleFor(x => x.PolicyId)
            .NotEmpty();

        RuleFor(x => x.StatutorySourceRef)
            .MaximumLength(200);

        RuleFor(x => x.OfficialNoticeCitation)
            .MaximumLength(300);

        RuleFor(x => x.VerificationEvidenceNotes)
            .MaximumLength(500);
    }
}

public sealed class ApproveCompliancePolicyCommandHandler
    : IRequestHandler<ApproveCompliancePolicyCommand, CountryCompliancePolicyDto>
{
    private readonly IApplicationDbContext _db;
    private readonly CompliancePlatformAuthorization _auth;
    private readonly IDateTimeProvider _dateTime;

    public ApproveCompliancePolicyCommandHandler(
        IApplicationDbContext db,
        CompliancePlatformAuthorization auth,
        IDateTimeProvider dateTime)
    {
        _db = db;
        _auth = auth;
        _dateTime = dateTime;
    }

    public async Task<CountryCompliancePolicyDto> Handle(
        ApproveCompliancePolicyCommand request,
        CancellationToken cancellationToken)
    {
        var staffUserId = await _auth.RequireVerifiedStaffPermissionAsync(
            cancellationToken,
            Permissions.Global.ComplianceManage);

        var targetMeta = await _db.CountryCompliancePolicies
            .AsNoTracking()
            .Where(p => p.Id == request.PolicyId)
            .Select(p => new { p.Id, p.CountryCode, p.PolicyCode })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException(nameof(CountryCompliancePolicy), request.PolicyId);

        return await _db.ExecuteWithCompliancePolicyLockAsync(
            targetMeta.CountryCode,
            targetMeta.PolicyCode,
            async () =>
            {
                var policy = await _db.CountryCompliancePolicies
                    .FirstOrDefaultAsync(p => p.Id == request.PolicyId, cancellationToken)
                    ?? throw new EntityNotFoundException(nameof(CountryCompliancePolicy), request.PolicyId);

                if (policy.ApprovalStatus != CompliancePolicyApprovalStatus.Draft || policy.IsActive)
                {
                    throw new ConcurrencyException(
                        "COMPLIANCE_POLICY_ALREADY_PUBLISHED",
                        $"Compliance policy '{policy.PolicyCode}' v{policy.Version} is '{policy.ApprovalStatus}' and cannot be re-approved.");
                }

                if (request.ExpectedConfigRevision.HasValue &&
                    policy.ConfigRevision != request.ExpectedConfigRevision.Value)
                {
                    throw new ConcurrencyException(
                        "COMPLIANCE_STALE_CONFIG_REVISION",
                        $"Compliance policy '{policy.PolicyCode}' v{policy.Version} was modified concurrently (expected revision {request.ExpectedConfigRevision.Value}, actual {policy.ConfigRevision}).");
                }

                if (!request.EvidenceVerified)
                {
                    throw new DomainException(
                        "COMPLIANCE_EVIDENCE_NOT_VERIFIED",
                        "Publishing a compliance policy requires explicit Staff verification attestation (EvidenceVerified = true).");
                }

                if (!ReferenceMappings.TryParsePredicate(policy.ApplicabilityPredicateJson, out _, out var predicateErr))
                {
                    throw new DomainException("COMPLIANCE_INVALID_PREDICATE", predicateErr!);
                }

                var now = _dateTime.UtcNow;

                // Validate domain approval preconditions BEFORE checking/mutating overlapping active policies
                // so missing statutory evidence always fails closed with 422 before touching active rows.
                var originalSourceRef = policy.StatutorySourceRef;
                var originalCitation = policy.OfficialNoticeCitation;
                var tempValidationCopy = CountryCompliancePolicy.CreateDraft(
                    policy.CountryCode,
                    policy.PolicyCode,
                    policy.Version,
                    policy.Title,
                    string.IsNullOrWhiteSpace(request.StatutorySourceRef) ? originalSourceRef : request.StatutorySourceRef!,
                    string.IsNullOrWhiteSpace(request.OfficialNoticeCitation) ? originalCitation : request.OfficialNoticeCitation!,
                    policy.ApplicabilityPredicateJson,
                    policy.EnforcementMode,
                    policy.EffectiveFromUtc,
                    policy.EffectiveToUtc,
                    policy.Id);

                tempValidationCopy.Approve(
                    staffUserId,
                    now,
                    request.VerificationEvidenceNotes);

                var activePolicies = await _db.CountryCompliancePolicies
                    .Where(p => p.CountryCode == policy.CountryCode &&
                                p.PolicyCode == policy.PolicyCode &&
                                p.ApprovalStatus == CompliancePolicyApprovalStatus.VerifiedApproved &&
                                p.IsActive &&
                                p.Id != policy.Id)
                    .ToListAsync(cancellationToken);

                var overlappingActive = activePolicies
                    .Where(a => a.OverlapsWindow(policy.EffectiveFromUtc, policy.EffectiveToUtc))
                    .ToList();

                if (request.ExpectedActivePolicyId.HasValue &&
                    !overlappingActive.Any(a => a.Id == request.ExpectedActivePolicyId.Value))
                {
                    throw new ConcurrencyException(
                        "COMPLIANCE_EXPECTED_ACTIVE_MISMATCH",
                        $"Expected active policy '{request.ExpectedActivePolicyId.Value}' is no longer the overlapping active version for '{policy.PolicyCode}'.");
                }

                if (overlappingActive.Count > 0)
                {
                    if (!request.SupersedeOverlapping)
                    {
                        throw new ConcurrencyException(
                            "COMPLIANCE_OVERLAPPING_ACTIVE_POLICY",
                            $"An active VerifiedApproved policy (v{overlappingActive[0].Version}) already covers an overlapping effective window for '{policy.CountryCode}/{policy.PolicyCode}'. Pass SupersedeOverlapping=true to intentionally supersede it.");
                    }

                    // Deactivate superseded rows FIRST and flush within the same transaction so
                    // PostgreSQL's non-deferred EXCLUDE USING gist constraint never observes two overlapping active rows.
                    foreach (var active in overlappingActive)
                    {
                        active.Supersede(policy.Id, now);
                        active.UpdatedBy = staffUserId;
                    }

                    await _db.SaveChangesAsync(cancellationToken);
                }

                policy.Approve(
                    staffUserId,
                    now,
                    request.VerificationEvidenceNotes,
                    request.StatutorySourceRef,
                    request.OfficialNoticeCitation);
                policy.UpdatedBy = staffUserId;

                await _db.SaveChangesAsync(cancellationToken);
                return policy.ToDto();
            },
            cancellationToken);
    }
}
