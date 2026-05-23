using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;

namespace Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionPlans;

public sealed record InspectionPlanCharacteristicInput(
    string CharacteristicCode,
    string Name,
    string Method,
    string Severity,
    bool Required,
    string SamplingRule);

public sealed record CreateInspectionPlanCommand(
    string OrganizationId,
    string EnvironmentId,
    string PlanCode,
    string Category,
    string? SkuCode,
    string? PartnerId,
    string? WorkCenterId,
    string? DeviceAssetId,
    string? DocumentType,
    IReadOnlyCollection<InspectionPlanCharacteristicInput> Characteristics) : ICommand<InspectionPlanId>;

public sealed class CreateInspectionPlanCommandValidator : AbstractValidator<CreateInspectionPlanCommand>
{
    public CreateInspectionPlanCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PlanCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Category).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Characteristics).NotEmpty();
        RuleForEach(x => x.Characteristics).ChildRules(characteristic =>
        {
            characteristic.RuleFor(x => x.CharacteristicCode).NotEmpty().MaximumLength(100);
            characteristic.RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
            characteristic.RuleFor(x => x.Method).NotEmpty().MaximumLength(100);
            characteristic.RuleFor(x => x.Severity).NotEmpty().MaximumLength(50);
            characteristic.RuleFor(x => x.SamplingRule).NotEmpty().MaximumLength(200);
        });
        RuleFor(x => x.Characteristics)
            .Must(HaveUniqueCharacteristicCodes)
            .WithMessage("Inspection characteristic codes must be unique.");
    }

    private static bool HaveUniqueCharacteristicCodes(IReadOnlyCollection<InspectionPlanCharacteristicInput>? characteristics)
    {
        if (characteristics is null)
        {
            return true;
        }

        var normalizedCodes = characteristics
            .Select(x => x.CharacteristicCode?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .ToArray();
        return normalizedCodes.Distinct(StringComparer.OrdinalIgnoreCase).Count() == normalizedCodes.Length;
    }
}

public sealed class CreateInspectionPlanCommandHandler(IInspectionPlanRepository repository)
    : ICommandHandler<CreateInspectionPlanCommand, InspectionPlanId>
{
    public async Task<InspectionPlanId> Handle(CreateInspectionPlanCommand request, CancellationToken cancellationToken)
    {
        if (await repository.CodeExistsAsync(request.OrganizationId, request.EnvironmentId, request.PlanCode, cancellationToken))
        {
            throw new KnownException($"Inspection plan code '{request.PlanCode}' already exists.");
        }

        var plan = InspectionPlan.Create(
            request.OrganizationId,
            request.EnvironmentId,
            request.PlanCode,
            request.Category,
            request.SkuCode,
            request.PartnerId,
            request.WorkCenterId,
            request.DeviceAssetId,
            request.DocumentType);
        foreach (var characteristic in request.Characteristics)
        {
            plan.AddCharacteristic(
                characteristic.CharacteristicCode,
                characteristic.Name,
                characteristic.Method,
                characteristic.Severity,
                characteristic.Required,
                characteristic.SamplingRule);
        }

        await repository.AddAsync(plan, cancellationToken);
        return plan.Id;
    }
}
