using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Reference.Commands;
using Warehouse3PL.Application.Features.Reference.Queries;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Authorize]
[Route("api/v1/compliance-policies")]
[Tags("Country Statutory Compliance Policies")]
public sealed class CompliancePoliciesController : BaseApiController
{
    public sealed record CreateCompliancePolicyDraftRequest(
        string CountryCode,
        string PolicyCode,
        string Title,
        string? StatutorySourceRef,
        string? OfficialNoticeCitation,
        string ApplicabilityPredicateJson,
        ComplianceEnforcementMode EnforcementMode,
        DateTime EffectiveFromUtc,
        DateTime? EffectiveToUtc);

    public sealed record ApproveCompliancePolicyRequest(
        bool EvidenceVerified,
        string VerificationEvidenceNotes,
        string? StatutorySourceRef = null,
        string? OfficialNoticeCitation = null,
        bool SupersedeOverlapping = false,
        Guid? ExpectedActivePolicyId = null,
        long? ExpectedConfigRevision = null);

    /// <summary>
    /// Lists versioned country compliance policies across Draft, VerifiedApproved, and Superseded history.
    /// Restricted to verified platform Staff holding Global:Compliance:View or Global:Compliance:Manage.
    /// </summary>
    [HttpGet]
    [HasAnyPermission(Permissions.Global.ComplianceView, Permissions.Global.ComplianceManage)]
    [ProducesResponseType(typeof(IReadOnlyList<CountryCompliancePolicyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<CountryCompliancePolicyDto>>> GetPolicies(
        [FromQuery] string? countryCode = null,
        [FromQuery] string? policyCode = null,
        [FromQuery] CompliancePolicyApprovalStatus? approvalStatus = null,
        [FromQuery] bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(
            new GetCompliancePoliciesQuery(countryCode, policyCode, approvalStatus, isActive),
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Retrieves a single versioned country compliance policy by ID, preserving published/superseded audit records.
    /// </summary>
    [HttpGet("{id:guid}")]
    [HasAnyPermission(Permissions.Global.ComplianceView, Permissions.Global.ComplianceManage)]
    [ProducesResponseType(typeof(CountryCompliancePolicyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CountryCompliancePolicyDto>> GetPolicyById(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetCompliancePolicyByIdQuery(id), cancellationToken);
        if (result == null)
        {
            return NotFound(new { message = $"Compliance policy '{id}' not found." });
        }

        return Ok(result);
    }

    /// <summary>
    /// Creates a new Draft compliance policy version (IsActive = false, non-enforcing).
    /// </summary>
    [HttpPost]
    [HasPermission(Permissions.Global.ComplianceManage)]
    [ProducesResponseType(typeof(CountryCompliancePolicyDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CountryCompliancePolicyDto>> CreateDraft(
        [FromBody] CreateCompliancePolicyDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        var created = await Mediator.Send(
            new CreateCompliancePolicyDraftCommand(
                request.CountryCode,
                request.PolicyCode,
                request.Title,
                request.StatutorySourceRef,
                request.OfficialNoticeCitation,
                request.ApplicabilityPredicateJson,
                request.EnforcementMode,
                request.EffectiveFromUtc,
                request.EffectiveToUtc),
            cancellationToken);

        return CreatedAtAction(nameof(GetPolicyById), new { id = created.Id }, created);
    }

    /// <summary>
    /// Publishes a Draft compliance policy after verifying Staff identity, statutory provenance,
    /// and PostgreSQL active-window exclusion constraints.
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    [HasPermission(Permissions.Global.ComplianceManage)]
    [ProducesResponseType(typeof(CountryCompliancePolicyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CountryCompliancePolicyDto>> Approve(
        Guid id,
        [FromBody] ApproveCompliancePolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        var approved = await Mediator.Send(
            new ApproveCompliancePolicyCommand(
                id,
                request.EvidenceVerified,
                request.VerificationEvidenceNotes,
                request.StatutorySourceRef,
                request.OfficialNoticeCitation,
                request.SupersedeOverlapping,
                request.ExpectedActivePolicyId,
                request.ExpectedConfigRevision),
            cancellationToken);

        return Ok(approved);
    }
}
