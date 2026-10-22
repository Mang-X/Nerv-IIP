using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkCenterMachineOverheadRateAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Infrastructure.Repositories;
using Nerv.IIP.Business.Erp.Web.Application.Commands;

namespace Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;

public sealed record ConfigureWorkCenterMachineOverheadRateCommand(
    string OrganizationId,
    string EnvironmentId,
    string WorkCenterId,
    string AccountingPeriodCode,
    MachineOverheadApplicability Applicability,
    decimal FixedOverheadBudget,
    decimal VariableOverheadBudget,
    decimal NormalCapacityMachineHours,
    string CurrencyCode,
    string ChangedBy,
    string Reason,
    DateTimeOffset ChangedAtUtc) : ICommand<WorkCenterMachineOverheadRateId>;

public sealed class ConfigureWorkCenterMachineOverheadRateCommandValidator
    : AbstractValidator<ConfigureWorkCenterMachineOverheadRateCommand>
{
    public ConfigureWorkCenterMachineOverheadRateCommandValidator()
    {
        RuleFor(x => x.OrganizationId).Must(WorkCenterRateCanonicalization.IsNonBlank).MaximumLength(100);
        RuleFor(x => x.EnvironmentId).Must(WorkCenterRateCanonicalization.IsNonBlank).MaximumLength(100);
        RuleFor(x => x.WorkCenterId).Must(WorkCenterRateCanonicalization.IsNonBlank).MaximumLength(100);
        RuleFor(x => x.AccountingPeriodCode).Must(WorkCenterRateCanonicalization.IsNonBlank).MaximumLength(50);
        RuleFor(x => x.Applicability).IsInEnum();
        RuleFor(x => x.FixedOverheadBudget).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.VariableOverheadBudget).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.CurrencyCode).Must(WorkCenterRateCanonicalization.IsAsciiCurrencyCode);
        RuleFor(x => x.ChangedBy).Must(WorkCenterRateCanonicalization.IsCanonicalActor).MaximumLength(200);
        RuleFor(x => x.Reason).Must(WorkCenterRateCanonicalization.IsNonBlank).MaximumLength(500);
        RuleFor(x => x.ChangedAtUtc).NotEmpty().Must(value => value.Offset == TimeSpan.Zero);
        RuleFor(x => x).Must(HaveValidCostBasis)
            .WithMessage("适用的机器制造费用率必须使用正数正常产能和正数预算；不适用时成本值必须为零。");
    }

    private static bool HaveValidCostBasis(ConfigureWorkCenterMachineOverheadRateCommand command) =>
        command.Applicability switch
        {
            MachineOverheadApplicability.Applicable =>
                command.NormalCapacityMachineHours > 0m
                && (command.FixedOverheadBudget > 0m || command.VariableOverheadBudget > 0m),
            MachineOverheadApplicability.NotApplicable =>
                command.NormalCapacityMachineHours == 0m
                && command.FixedOverheadBudget == 0m
                && command.VariableOverheadBudget == 0m,
            _ => false,
        };

}

public sealed class ConfigureWorkCenterMachineOverheadRateCommandHandler(
    IAccountingPeriodRepository accountingPeriods,
    IWorkCenterMachineOverheadRateRepository rates,
    IWorkCenterRateRevisionAllocator revisions)
    : ICommandHandler<ConfigureWorkCenterMachineOverheadRateCommand, WorkCenterMachineOverheadRateId>
{
    public async Task<WorkCenterMachineOverheadRateId> Handle(
        ConfigureWorkCenterMachineOverheadRateCommand request,
        CancellationToken cancellationToken)
    {
        var allocation = await revisions.AllocateMachineAsync(
            WorkCenterRateScope.From(request.OrganizationId, request.EnvironmentId, request.WorkCenterId),
            WorkCenterRateAccountingPeriod.From(request.AccountingPeriodCode),
            WorkCenterRateCurrency.From(request.CurrencyCode),
            cancellationToken);
        var periodExists = await accountingPeriods.ExistsAsync(
            allocation.Scope.OrganizationId,
            allocation.Scope.EnvironmentId,
            allocation.AccountingPeriod.Value,
            cancellationToken);
        if (!periodExists)
        {
            throw new KnownException(
                $"未找到会计期间『{allocation.Scope.OrganizationId}·{allocation.Scope.EnvironmentId}·{allocation.AccountingPeriod.Value}』。");
        }

        var rate = request.Applicability switch
        {
            MachineOverheadApplicability.Applicable => WorkCenterMachineOverheadRate.DefineApplicable(
                allocation.Scope.OrganizationId, allocation.Scope.EnvironmentId, allocation.Scope.WorkCenterId, allocation.AccountingPeriod.Value,
                request.FixedOverheadBudget,
                request.VariableOverheadBudget,
                request.NormalCapacityMachineHours,
                allocation.Currency.Value, allocation.Revision,
                request.ChangedBy,
                request.Reason,
                request.ChangedAtUtc),
            MachineOverheadApplicability.NotApplicable
                when request.FixedOverheadBudget == 0m
                    && request.VariableOverheadBudget == 0m
                    && request.NormalCapacityMachineHours == 0m
                => WorkCenterMachineOverheadRate.DefineNotApplicable(
                allocation.Scope.OrganizationId, allocation.Scope.EnvironmentId, allocation.Scope.WorkCenterId, allocation.AccountingPeriod.Value,
                allocation.Currency.Value, allocation.Revision,
                request.ChangedBy,
                request.Reason,
                request.ChangedAtUtc),
            MachineOverheadApplicability.NotApplicable => throw new ArgumentOutOfRangeException(
                nameof(request.Applicability),
                "Not-applicable machine-overhead revisions must have zero cost values."),
            _ => throw new ArgumentOutOfRangeException(
                nameof(request.Applicability),
                request.Applicability,
                "Unknown machine-overhead applicability."),
        };
        await rates.AddAsync(rate, cancellationToken);
        return rate.Id;
    }
}
