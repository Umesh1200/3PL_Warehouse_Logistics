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

public sealed record CreateFxRateDraftCommand(
    string BaseCurrencyCode,
    string QuoteCurrencyCode,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc,
    int UnitMultiplier,
    decimal BuyRate,
    decimal SellRate,
    decimal MidRate,
    FxRateSourceAuthority SourceAuthority,
    string BulletinVersion,
    DateTime PublishedAtUtc,
    string? BenchmarkSeriesId = null,
    string? IntegrityHash = null) : IRequest<FxRateRecordDto>;

public sealed class CreateFxRateDraftCommandValidator : AbstractValidator<CreateFxRateDraftCommand>
{
    public CreateFxRateDraftCommandValidator()
    {
        RuleFor(x => x.BaseCurrencyCode).NotEmpty().Length(3).Matches("^[A-Za-z]{3}$");
        RuleFor(x => x.QuoteCurrencyCode).NotEmpty().Length(3).Matches("^[A-Za-z]{3}$");
        RuleFor(x => x.UnitMultiplier).GreaterThan(0);
        RuleFor(x => x.BuyRate).GreaterThan(0m);
        RuleFor(x => x.SellRate).GreaterThan(0m);
        RuleFor(x => x.MidRate).GreaterThan(0m);
        RuleFor(x => x.BulletinVersion).NotEmpty().MaximumLength(80);
        RuleFor(x => x.BenchmarkSeriesId).MaximumLength(80);
        RuleFor(x => x.IntegrityHash).MaximumLength(128);
    }
}

public sealed class CreateFxRateDraftCommandHandler : IRequestHandler<CreateFxRateDraftCommand, FxRateRecordDto>
{
    private readonly IApplicationDbContext _db;
    private readonly CompliancePlatformAuthorization _auth;

    public CreateFxRateDraftCommandHandler(IApplicationDbContext db, CompliancePlatformAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<FxRateRecordDto> Handle(CreateFxRateDraftCommand request, CancellationToken cancellationToken)
    {
        var staffUserId = await _auth.RequireVerifiedStaffPermissionAsync(
            cancellationToken,
            Permissions.Global.ManageFxRates,
            Permissions.Billing.ManageRates);

        var baseCode = request.BaseCurrencyCode.Trim().ToUpperInvariant();
        var quoteCode = request.QuoteCurrencyCode.Trim().ToUpperInvariant();

        var currencies = await _db.Currencies
            .AsNoTracking()
            .Where(c => (c.CurrencyCode == baseCode || c.CurrencyCode == quoteCode) && c.IsActive)
            .Select(c => c.CurrencyCode)
            .ToListAsync(cancellationToken);

        if (!currencies.Contains(baseCode, StringComparer.Ordinal))
        {
            throw new DomainException(
                $"BaseCurrencyCode '{baseCode}' is not an active CurrencyMaster record.",
                "FX_RATE_UNKNOWN_BASE_CURRENCY");
        }

        if (!currencies.Contains(quoteCode, StringComparer.Ordinal))
        {
            throw new DomainException(
                $"QuoteCurrencyCode '{quoteCode}' is not an active CurrencyMaster record.",
                "FX_RATE_UNKNOWN_QUOTE_CURRENCY");
        }

        var record = FxRateRecord.CreateDraft(
            baseCode,
            quoteCode,
            request.EffectiveFromUtc,
            request.EffectiveToUtc,
            request.UnitMultiplier,
            request.BuyRate,
            request.SellRate,
            request.MidRate,
            request.SourceAuthority,
            request.BulletinVersion,
            request.PublishedAtUtc,
            request.BenchmarkSeriesId,
            request.IntegrityHash);

        record.CreatedBy = staffUserId;
        _db.FxRateRecords.Add(record);
        await _db.SaveChangesAsync(cancellationToken);

        return record.ToDto();
    }
}

public sealed record ApproveFxRateCommand(
    Guid FxRateId,
    bool EvidenceVerified,
    string VerificationNotes,
    bool SupersedeOverlapping = false,
    Guid? ExpectedActiveRateId = null,
    long? ExpectedConfigRevision = null) : IRequest<FxRateRecordDto>;

public sealed class ApproveFxRateCommandValidator : AbstractValidator<ApproveFxRateCommand>
{
    public ApproveFxRateCommandValidator()
    {
        RuleFor(x => x.FxRateId).NotEmpty();
        RuleFor(x => x.VerificationNotes).MaximumLength(500);
    }
}

public sealed class ApproveFxRateCommandHandler : IRequestHandler<ApproveFxRateCommand, FxRateRecordDto>
{
    private readonly IApplicationDbContext _db;
    private readonly CompliancePlatformAuthorization _auth;
    private readonly IDateTimeProvider _dateTime;

    public ApproveFxRateCommandHandler(
        IApplicationDbContext db,
        CompliancePlatformAuthorization auth,
        IDateTimeProvider dateTime)
    {
        _db = db;
        _auth = auth;
        _dateTime = dateTime;
    }

    public async Task<FxRateRecordDto> Handle(ApproveFxRateCommand request, CancellationToken cancellationToken)
    {
        var staffUserId = await _auth.RequireVerifiedStaffPermissionAsync(
            cancellationToken,
            Permissions.Global.ManageFxRates,
            Permissions.Billing.ManageRates);

        var targetMeta = await _db.FxRateRecords
            .AsNoTracking()
            .Where(r => r.Id == request.FxRateId)
            .Select(r => new { r.Id, r.BaseCurrencyCode, r.QuoteCurrencyCode, r.SourceAuthority })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException(nameof(FxRateRecord), request.FxRateId);

        return await _db.ExecuteWithFxRateLockAsync(
            targetMeta.BaseCurrencyCode,
            targetMeta.QuoteCurrencyCode,
            targetMeta.SourceAuthority,
            async () =>
            {
                var rate = await _db.FxRateRecords
                    .FirstOrDefaultAsync(r => r.Id == request.FxRateId, cancellationToken)
                    ?? throw new EntityNotFoundException(nameof(FxRateRecord), request.FxRateId);

                if (rate.ApprovalStatus != FxRateApprovalStatus.Draft || rate.IsActive)
                {
                    throw new ConcurrencyException(
                        "FX_RATE_ALREADY_PUBLISHED",
                        $"FX rate '{rate.BulletinVersion}' is '{rate.ApprovalStatus}' and cannot be re-approved.");
                }

                if (request.ExpectedConfigRevision.HasValue &&
                    rate.ConfigRevision != request.ExpectedConfigRevision.Value)
                {
                    throw new ConcurrencyException(
                        "FX_RATE_STALE_REVISION",
                        $"FX rate '{rate.BulletinVersion}' was modified concurrently (expected revision {request.ExpectedConfigRevision.Value}, actual {rate.ConfigRevision}).");
                }

                if (!request.EvidenceVerified || string.IsNullOrWhiteSpace(request.VerificationNotes))
                {
                    throw new DomainException(
                        "Approving an FX rate requires EvidenceVerified = true and non-empty VerificationNotes.",
                        "FX_RATE_VERIFICATION_REQUIRED");
                }

                var now = _dateTime.UtcNow;

                var activeRates = await _db.FxRateRecords
                    .Where(r => r.BaseCurrencyCode == rate.BaseCurrencyCode &&
                                r.QuoteCurrencyCode == rate.QuoteCurrencyCode &&
                                r.SourceAuthority == rate.SourceAuthority &&
                                r.ApprovalStatus == FxRateApprovalStatus.VerifiedApproved &&
                                r.IsActive &&
                                r.Id != rate.Id)
                    .ToListAsync(cancellationToken);

                var overlappingActive = activeRates
                    .Where(a => a.OverlapsWindow(rate.EffectiveFromUtc, rate.EffectiveToUtc))
                    .ToList();

                if (request.ExpectedActiveRateId.HasValue &&
                    !overlappingActive.Any(a => a.Id == request.ExpectedActiveRateId.Value))
                {
                    throw new ConcurrencyException(
                        "FX_RATE_EXPECTED_ACTIVE_MISMATCH",
                        $"Expected active FX rate '{request.ExpectedActiveRateId.Value}' is no longer the overlapping active version for '{rate.BaseCurrencyCode}/{rate.QuoteCurrencyCode}'.");
                }

                if (overlappingActive.Count > 0)
                {
                    if (!request.SupersedeOverlapping)
                    {
                        throw new ConcurrencyException(
                            "FX_RATE_OVERLAPPING_ACTIVE_WINDOW",
                            $"An active VerifiedApproved FX rate ('{overlappingActive[0].BulletinVersion}') already covers an overlapping window for '{rate.BaseCurrencyCode}/{rate.QuoteCurrencyCode}' under '{rate.SourceAuthority}'. Pass SupersedeOverlapping=true to intentionally supersede it.");
                    }

                    // Deactivate superseded rows FIRST and flush so PostgreSQL's non-deferred EXCLUDE USING gist
                    // constraint never observes two overlapping active rows in the same pair/authority.
                    foreach (var active in overlappingActive)
                    {
                        active.Supersede(rate.Id, now);
                        active.UpdatedBy = staffUserId;
                    }

                    await _db.SaveChangesAsync(cancellationToken);
                }

                rate.Approve(staffUserId, now, request.VerificationNotes);
                rate.UpdatedBy = staffUserId;

                await _db.SaveChangesAsync(cancellationToken);
                return rate.ToDto();
            },
            cancellationToken);
    }
}

public sealed record VoidFxRateCommand(
    Guid FxRateId,
    string VoidReason,
    long? ExpectedConfigRevision = null) : IRequest<FxRateRecordDto>;

public sealed class VoidFxRateCommandValidator : AbstractValidator<VoidFxRateCommand>
{
    public VoidFxRateCommandValidator()
    {
        RuleFor(x => x.FxRateId).NotEmpty();
        RuleFor(x => x.VoidReason).NotEmpty().MaximumLength(500);
    }
}

public sealed class VoidFxRateCommandHandler : IRequestHandler<VoidFxRateCommand, FxRateRecordDto>
{
    private readonly IApplicationDbContext _db;
    private readonly CompliancePlatformAuthorization _auth;
    private readonly IDateTimeProvider _dateTime;

    public VoidFxRateCommandHandler(
        IApplicationDbContext db,
        CompliancePlatformAuthorization auth,
        IDateTimeProvider dateTime)
    {
        _db = db;
        _auth = auth;
        _dateTime = dateTime;
    }

    public async Task<FxRateRecordDto> Handle(VoidFxRateCommand request, CancellationToken cancellationToken)
    {
        var staffUserId = await _auth.RequireVerifiedStaffPermissionAsync(
            cancellationToken,
            Permissions.Global.ManageFxRates,
            Permissions.Billing.ManageRates);

        var targetMeta = await _db.FxRateRecords
            .AsNoTracking()
            .Where(r => r.Id == request.FxRateId)
            .Select(r => new { r.Id, r.BaseCurrencyCode, r.QuoteCurrencyCode, r.SourceAuthority })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException(nameof(FxRateRecord), request.FxRateId);

        return await _db.ExecuteWithFxRateLockAsync(
            targetMeta.BaseCurrencyCode,
            targetMeta.QuoteCurrencyCode,
            targetMeta.SourceAuthority,
            async () =>
            {
                var rate = await _db.FxRateRecords
                    .FirstOrDefaultAsync(r => r.Id == request.FxRateId, cancellationToken)
                    ?? throw new EntityNotFoundException(nameof(FxRateRecord), request.FxRateId);

                if (request.ExpectedConfigRevision.HasValue &&
                    rate.ConfigRevision != request.ExpectedConfigRevision.Value)
                {
                    throw new ConcurrencyException(
                        "FX_RATE_STALE_REVISION",
                        $"FX rate '{rate.BulletinVersion}' was modified concurrently (expected revision {request.ExpectedConfigRevision.Value}, actual {rate.ConfigRevision}).");
                }

                rate.Void(staffUserId, _dateTime.UtcNow, request.VoidReason);
                rate.UpdatedBy = staffUserId;

                await _db.SaveChangesAsync(cancellationToken);
                return rate.ToDto();
            },
            cancellationToken);
    }
}

public sealed record RejectRetroactiveFxRateMutationCommand(Guid FxRateId, string Operation) : IRequest<Unit>;

public sealed class RejectRetroactiveFxRateMutationCommandHandler
    : IRequestHandler<RejectRetroactiveFxRateMutationCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly CompliancePlatformAuthorization _auth;

    public RejectRetroactiveFxRateMutationCommandHandler(
        IApplicationDbContext db,
        CompliancePlatformAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<Unit> Handle(
        RejectRetroactiveFxRateMutationCommand request,
        CancellationToken cancellationToken)
    {
        await _auth.RequireVerifiedStaffPermissionAsync(
            cancellationToken,
            Permissions.Global.ManageFxRates,
            Permissions.Billing.ManageRates);

        bool exists = await _db.FxRateRecords
            .AsNoTracking()
            .AnyAsync(r => r.Id == request.FxRateId, cancellationToken);

        if (!exists)
        {
            throw new EntityNotFoundException(nameof(FxRateRecord), request.FxRateId);
        }

        throw new ConcurrencyException(
            "FX_RATE_IMMUTABLE_HISTORY",
            $"Retroactive FX rate {request.Operation} is prohibited to preserve financial audit history. Publish a superseding FxRateRecord or void the existing record.");
    }
}
