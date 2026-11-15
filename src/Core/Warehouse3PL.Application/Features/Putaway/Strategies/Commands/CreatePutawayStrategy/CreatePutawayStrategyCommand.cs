using FluentValidation;
using MediatR;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Putaway.Strategies.Commands.CreatePutawayStrategy;

public record CreatePutawayStrategyCommand : IRequest<PutawayStrategyRuleDto>
{
    public Guid ClientId { get; init; }
    public Guid FacilityId { get; init; }
    public int Sequence { get; init; }
    public string RuleName { get; init; } = string.Empty;
    public StorageTempClass TempClassRequirement { get; init; } = StorageTempClass.Ambient;
    public VelocityClass? TargetVelocity { get; init; }
    public bool PreferConsolidation { get; init; } = true;
    public bool AllowDynamicOverflow { get; init; } = false;
}

public class CreatePutawayStrategyCommandValidator : AbstractValidator<CreatePutawayStrategyCommand>
{
    public CreatePutawayStrategyCommandValidator()
    {
        RuleFor(x => x.FacilityId).NotEmpty().WithMessage("FacilityId is required.");
        RuleFor(x => x.RuleName).NotEmpty().MaximumLength(150).WithMessage("RuleName is required and must not exceed 150 characters.");
        RuleFor(x => x.Sequence).GreaterThan(0).WithMessage("Sequence must be greater than zero.");
    }
}

public class CreatePutawayStrategyCommandHandler : IRequestHandler<CreatePutawayStrategyCommand, PutawayStrategyRuleDto>
{
    private readonly IApplicationDbContext _context;

    public CreatePutawayStrategyCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PutawayStrategyRuleDto> Handle(CreatePutawayStrategyCommand request, CancellationToken cancellationToken)
    {
        var rule = new PutawayStrategyRule
        {
            ClientId = request.ClientId,
            FacilityId = request.FacilityId,
            Sequence = request.Sequence,
            RuleName = request.RuleName,
            TempClassRequirement = request.TempClassRequirement,
            TargetVelocity = request.TargetVelocity,
            PreferConsolidation = request.PreferConsolidation,
            AllowDynamicOverflow = request.AllowDynamicOverflow,
            IsActive = true
        };

        _context.PutawayStrategyRules.Add(rule);
        await _context.SaveChangesAsync(cancellationToken);

        return new PutawayStrategyRuleDto
        {
            Id = rule.Id,
            ClientId = rule.ClientId,
            FacilityId = rule.FacilityId,
            Sequence = rule.Sequence,
            RuleName = rule.RuleName,
            TempClassRequirement = rule.TempClassRequirement,
            TargetVelocity = rule.TargetVelocity,
            PreferConsolidation = rule.PreferConsolidation,
            AllowDynamicOverflow = rule.AllowDynamicOverflow,
            IsActive = rule.IsActive,
            Version = rule.Version
        };
    }
}

