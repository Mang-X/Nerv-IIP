using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;

namespace Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionPlans;

public sealed record InspectionPlanCharacteristicInput(
    string CharacteristicCode,
    string Name,
    string Method,
    string Severity,
    bool Required,
    string SamplingRule,
    string? CharacteristicType = null,
    decimal? NominalValue = null,
    decimal? LowerSpecLimit = null,
    decimal? UpperSpecLimit = null,
    string? UnitCode = null,
    InspectionSamplingPlanInput? SamplingPlan = null);

public sealed record InspectionSamplingPlanInput(
    string InspectionLevel,
    string Aql,
    int SampleSize,
    int AcceptanceNumber,
    int RejectionNumber);

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
    IReadOnlyCollection<InspectionPlanCharacteristicInput> Characteristics,
    decimal? TimeIntervalHours = null,
    decimal? QuantityInterval = null,
    string? AssignedInspectorUserId = null,
    string? AssignedTeamId = null) : ICommand<InspectionPlanId>;

public sealed class CreateInspectionPlanCommandValidator : AbstractValidator<CreateInspectionPlanCommand>
{
    public CreateInspectionPlanCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PlanCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Category).NotEmpty().MaximumLength(50);
        RuleFor(x => x.TimeIntervalHours)
            .Must(value => value is null || value >= 0.000001m)
            .WithMessage("巡检时间间隔必须至少为 0.000001 小时。");
        RuleFor(x => x.TimeIntervalHours)
            .Must(value => value is null || value <= (decimal)TimeSpan.MaxValue.TotalHours)
            .WithMessage("巡检时间间隔超出支持范围。");
        RuleFor(x => x.QuantityInterval)
            .Must(value => value is null || value >= 0.000001m)
            .WithMessage("巡检数量间隔必须至少为 0.000001。");
        RuleFor(x => x.QuantityInterval)
            .Must(value => value is null || value <= InspectionPlan.MaximumQuantityInterval)
            .WithMessage($"巡检数量间隔不能超过 {InspectionPlan.MaximumQuantityInterval}。");
        RuleFor(x => x.AssignedInspectorUserId).MaximumLength(150);
        RuleFor(x => x.AssignedTeamId).MaximumLength(150);
        RuleFor(x => x)
            .Must(x => !HasPeriodicPolicy(x) || string.Equals(x.Category?.Trim(), "operation", StringComparison.OrdinalIgnoreCase))
            .WithMessage("只有 operation 类检验方案可以配置巡检策略。");
        RuleFor(x => x)
            .Must(x => !HasPeriodicPolicy(x) || (!string.IsNullOrWhiteSpace(x.SkuCode) && !string.IsNullOrWhiteSpace(x.WorkCenterId)))
            .WithMessage("巡检策略必须同时配置 SKU 和 WorkCenterId。");
        RuleFor(x => x)
            .Must(x => string.IsNullOrWhiteSpace(x.AssignedInspectorUserId) || string.IsNullOrWhiteSpace(x.AssignedTeamId))
            .WithMessage("巡检策略不能同时指定检验员和团队。");
        RuleFor(x => x)
            .Must(x => !HasAssignment(x) || x.TimeIntervalHours.HasValue || x.QuantityInterval.HasValue)
            .WithMessage("巡检投递目标必须同时配置至少一个巡检间隔。");
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

    private static bool HasPeriodicPolicy(CreateInspectionPlanCommand command) =>
        command.TimeIntervalHours.HasValue
        || command.QuantityInterval.HasValue
        || HasAssignment(command);

    private static bool HasAssignment(CreateInspectionPlanCommand command) =>
        !string.IsNullOrWhiteSpace(command.AssignedInspectorUserId)
        || !string.IsNullOrWhiteSpace(command.AssignedTeamId);
}

public sealed class CreateInspectionPlanCommandHandler(IInspectionPlanRepository repository)
    : ICommandHandler<CreateInspectionPlanCommand, InspectionPlanId>
{
    public async Task<InspectionPlanId> Handle(CreateInspectionPlanCommand request, CancellationToken cancellationToken)
    {
        if (await repository.CodeExistsAsync(request.OrganizationId, request.EnvironmentId, request.PlanCode, cancellationToken))
        {
            throw new KnownException($"检验方案编号 {request.PlanCode} 已存在，请在检验方案页更换编号后重新提交。");
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
            request.DocumentType,
            request.TimeIntervalHours,
            request.QuantityInterval,
            request.AssignedInspectorUserId,
            request.AssignedTeamId);
        foreach (var characteristic in request.Characteristics)
        {
            plan.AddCharacteristic(
                characteristic.CharacteristicCode,
                characteristic.Name,
                characteristic.Method,
                characteristic.Severity,
                characteristic.Required,
                characteristic.SamplingRule,
                characteristic.CharacteristicType ?? InspectionCharacteristicTypes.Attribute,
                characteristic.NominalValue,
                characteristic.LowerSpecLimit,
                characteristic.UpperSpecLimit,
                characteristic.UnitCode,
                characteristic.SamplingPlan is null
                    ? null
                    : InspectionSamplingPlan.Create(
                        characteristic.SamplingPlan.InspectionLevel,
                        characteristic.SamplingPlan.Aql,
                        characteristic.SamplingPlan.SampleSize,
                        characteristic.SamplingPlan.AcceptanceNumber,
                        characteristic.SamplingPlan.RejectionNumber));
        }

        await repository.AddAsync(plan, cancellationToken);
        return plan.Id;
    }
}
