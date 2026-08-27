using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkCenterMachineOverheadRateAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
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
        RuleFor(x => x.OrganizationId).Must(BeNonBlank).MaximumLength(100);
        RuleFor(x => x.EnvironmentId).Must(BeNonBlank).MaximumLength(100);
        RuleFor(x => x.WorkCenterId).Must(BeNonBlank).MaximumLength(100);
        RuleFor(x => x.AccountingPeriodCode).Must(BeNonBlank).MaximumLength(50);
        RuleFor(x => x.Applicability).IsInEnum();
        RuleFor(x => x.FixedOverheadBudget).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.VariableOverheadBudget).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.CurrencyCode).Must(BeAsciiCurrencyCode);
        RuleFor(x => x.ChangedBy).Must(BeCanonicalActor).MaximumLength(200);
        RuleFor(x => x.Reason).Must(BeNonBlank).MaximumLength(500);
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

    private static bool BeNonBlank(string value) => !string.IsNullOrWhiteSpace(value);

    private static bool BeAsciiCurrencyCode(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Trim().Length == 3
        && value.Trim().All(character => character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z'));

    private static bool BeCanonicalActor(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Any(char.IsWhiteSpace)) return false;
        var separator = value.IndexOf(':', StringComparison.Ordinal);
        return separator > 0 && separator < value.Length - 1;
    }
}

public sealed class ConfigureWorkCenterMachineOverheadRateCommandHandler(
    ApplicationDbContext dbContext,
    IErpAdvisoryLockAllocator revisionLock)
    : ICommandHandler<ConfigureWorkCenterMachineOverheadRateCommand, WorkCenterMachineOverheadRateId>
{
    public async Task<WorkCenterMachineOverheadRateId> Handle(
        ConfigureWorkCenterMachineOverheadRateCommand request,
        CancellationToken cancellationToken)
    {
        var organizationId = request.OrganizationId.Trim();
        var environmentId = request.EnvironmentId.Trim();
        var workCenterId = request.WorkCenterId.Trim();
        var accountingPeriodCode = request.AccountingPeriodCode.Trim();
        var currencyCode = request.CurrencyCode.Trim().ToUpperInvariant();

        var periodExists = await dbContext.AccountingPeriods.AnyAsync(
            x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.PeriodCode == accountingPeriodCode,
            cancellationToken);
        if (!periodExists)
        {
            throw new KnownException(
                $"未找到会计期间『{organizationId}·{environmentId}·{accountingPeriodCode}』。");
        }

        await revisionLock.AcquireAsync(
            ErpAdvisoryLockDomain.WorkCenterMachineOverheadRate,
            organizationId,
            environmentId,
            workCenterId,
            cancellationToken);

        var persistedCurrencies = await dbContext.WorkCenterMachineOverheadRates
            .Where(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.WorkCenterId == workCenterId)
            .Select(x => x.CurrencyCode)
            .Distinct()
            .ToListAsync(cancellationToken);
        var localCurrencies = dbContext.WorkCenterMachineOverheadRates.Local
            .Where(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.WorkCenterId == workCenterId)
            .Select(x => x.CurrencyCode);
        if (persistedCurrencies.Concat(localCurrencies)
            .Any(existing => !string.Equals(existing, currencyCode, StringComparison.Ordinal)))
        {
            throw new KnownException(
                $"机器制造费用率『{organizationId}·{environmentId}·{workCenterId}』币种已固定。");
        }

        var databaseRevision = await dbContext.WorkCenterMachineOverheadRates
            .Where(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.WorkCenterId == workCenterId
                && x.AccountingPeriodCode == accountingPeriodCode)
            .Select(x => (int?)x.Revision)
            .MaxAsync(cancellationToken) ?? 0;
        var localRevision = dbContext.WorkCenterMachineOverheadRates.Local
            .Where(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.WorkCenterId == workCenterId
                && x.AccountingPeriodCode == accountingPeriodCode)
            .Select(x => x.Revision)
            .DefaultIfEmpty(0)
            .Max();
        var revision = Math.Max(databaseRevision, localRevision) + 1;

        var rate = request.Applicability switch
        {
            MachineOverheadApplicability.Applicable => WorkCenterMachineOverheadRate.DefineApplicable(
                organizationId,
                environmentId,
                workCenterId,
                accountingPeriodCode,
                request.FixedOverheadBudget,
                request.VariableOverheadBudget,
                request.NormalCapacityMachineHours,
                currencyCode,
                revision,
                request.ChangedBy,
                request.Reason,
                request.ChangedAtUtc),
            MachineOverheadApplicability.NotApplicable
                when request.FixedOverheadBudget == 0m
                    && request.VariableOverheadBudget == 0m
                    && request.NormalCapacityMachineHours == 0m
                => WorkCenterMachineOverheadRate.DefineNotApplicable(
                organizationId,
                environmentId,
                workCenterId,
                accountingPeriodCode,
                currencyCode,
                revision,
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
        dbContext.WorkCenterMachineOverheadRates.Add(rate);
        return rate.Id;
    }
}
