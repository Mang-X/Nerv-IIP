using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountingPeriodAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.MachineOverheadReconciliationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkCenterMachineOverheadRateAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands;

namespace Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;

public sealed record ReconcileWorkCenterMachineOverheadCommand(
    string OrganizationId,
    string EnvironmentId,
    string WorkCenterId,
    string AccountingPeriodCode,
    decimal ActualFixedOverheadAmount,
    decimal ActualVariableOverheadAmount,
    string CurrencyCode,
    long AbnormalDowntimeTicks,
    AbnormalDowntimeDisposition AbnormalDowntimeDisposition,
    string RecordedBy,
    string SourceReference,
    string Reason,
    DateTimeOffset RecordedAtUtc) : ICommand<WorkCenterMachineOverheadReconciliationId>;

public sealed class ReconcileWorkCenterMachineOverheadCommandValidator
    : AbstractValidator<ReconcileWorkCenterMachineOverheadCommand>
{
    public ReconcileWorkCenterMachineOverheadCommandValidator()
    {
        RuleFor(x => x.OrganizationId).Must(WorkCenterRateCanonicalization.IsNonBlank).MaximumLength(100);
        RuleFor(x => x.EnvironmentId).Must(WorkCenterRateCanonicalization.IsNonBlank).MaximumLength(100);
        RuleFor(x => x.WorkCenterId).Must(WorkCenterRateCanonicalization.IsNonBlank).MaximumLength(100);
        RuleFor(x => x.AccountingPeriodCode).Must(WorkCenterRateCanonicalization.IsNonBlank).MaximumLength(50);
        RuleFor(x => x.ActualFixedOverheadAmount).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.ActualVariableOverheadAmount).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.CurrencyCode).Must(WorkCenterRateCanonicalization.IsAsciiCurrencyCode);
        RuleFor(x => x.AbnormalDowntimeTicks).GreaterThanOrEqualTo(0);
        RuleFor(x => x.AbnormalDowntimeDisposition).IsInEnum();
        RuleFor(x => x.RecordedBy).Must(WorkCenterRateCanonicalization.IsCanonicalActor).MaximumLength(200);
        RuleFor(x => x.SourceReference).Must(WorkCenterRateCanonicalization.IsNonBlank).MaximumLength(300);
        RuleFor(x => x.Reason).Must(WorkCenterRateCanonicalization.IsNonBlank).MaximumLength(500);
        RuleFor(x => x.RecordedAtUtc).NotEmpty().Must(value => value.Offset == TimeSpan.Zero);
        RuleFor(x => x).Must(HaveConsistentAbnormalDowntime)
            .WithMessage("异常停机为零时处置必须为 None；大于零时必须明确 Pending 或 PeriodExpense。");
    }

    private static bool HaveConsistentAbnormalDowntime(ReconcileWorkCenterMachineOverheadCommand command)
        => command.AbnormalDowntimeTicks == 0
            ? command.AbnormalDowntimeDisposition == AbnormalDowntimeDisposition.None
            : command.AbnormalDowntimeDisposition is AbnormalDowntimeDisposition.Pending or AbnormalDowntimeDisposition.PeriodExpense;
}

public sealed class ReconcileWorkCenterMachineOverheadCommandHandler(
    ApplicationDbContext dbContext,
    IErpAdvisoryLockAllocator reconciliationLock)
    : ICommandHandler<ReconcileWorkCenterMachineOverheadCommand, WorkCenterMachineOverheadReconciliationId>
{
    public async Task<WorkCenterMachineOverheadReconciliationId> Handle(
        ReconcileWorkCenterMachineOverheadCommand request,
        CancellationToken cancellationToken)
    {
        var organizationId = Require(request.OrganizationId, nameof(request.OrganizationId));
        var environmentId = Require(request.EnvironmentId, nameof(request.EnvironmentId));
        var workCenterId = Require(request.WorkCenterId, nameof(request.WorkCenterId));
        var periodCode = Require(request.AccountingPeriodCode, nameof(request.AccountingPeriodCode));
        var currencyCode = request.CurrencyCode.Trim().ToUpperInvariant();

        await reconciliationLock.AcquireAsync(
            ErpAdvisoryLockDomain.WorkCenterMachineOverheadReconciliation,
            organizationId, environmentId, periodCode, cancellationToken);
        await reconciliationLock.AcquireAsync(
            ErpAdvisoryLockDomain.WorkCenterMachineOverheadReconciliation,
            organizationId, environmentId, $"{periodCode}\n{workCenterId}", cancellationToken);

        var period = await AccountingPeriodPostingGuard.FindPeriodAsync(
            dbContext, organizationId, environmentId, periodCode, cancellationToken);
        if (!period.CanPost)
            throw new KnownException($"会计期间『{periodCode}』已关闭，请先重开期间。");

        var rate = await dbContext.WorkCenterMachineOverheadRates
            .Where(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.WorkCenterId == workCenterId
                && x.AccountingPeriodCode == periodCode)
            .OrderByDescending(x => x.Revision)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KnownException($"工作中心『{workCenterId}』在会计期间『{periodCode}』缺少机器制造费用率。");
        if (rate.Applicability != MachineOverheadApplicability.Applicable)
            throw new KnownException($"工作中心『{workCenterId}』在会计期间『{periodCode}』明确为机器制造费用不适用，无需归集实际池。");
        if (!string.Equals(rate.CurrencyCode, currencyCode, StringComparison.Ordinal))
            throw new KnownException($"工作中心『{workCenterId}』实际费用池币种与机器制造费用率币种不一致。");

        var applied = await MachineOverheadAppliedSnapshotReader.ReadAsync(
            dbContext, organizationId, environmentId, periodCode, workCenterId, cancellationToken);
        if (applied.CurrencyCodes.Any(existing => !string.Equals(existing, currencyCode, StringComparison.Ordinal)))
            throw new KnownException($"工作中心『{workCenterId}』active settlement 币种与实际费用池币种不一致。");

        var databaseRevision = await dbContext.WorkCenterMachineOverheadReconciliations
            .Where(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.WorkCenterId == workCenterId
                && x.AccountingPeriodCode == periodCode)
            .Select(x => (int?)x.Revision)
            .MaxAsync(cancellationToken) ?? 0;
        var localRevision = dbContext.WorkCenterMachineOverheadReconciliations.Local
            .Where(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.WorkCenterId == workCenterId
                && x.AccountingPeriodCode == periodCode)
            .Select(x => x.Revision)
            .DefaultIfEmpty(0)
            .Max();
        var reconciliation = WorkCenterMachineOverheadReconciliation.Record(
            organizationId, environmentId, workCenterId, periodCode,
            rate.Id, rate.Revision, currencyCode,
            request.ActualFixedOverheadAmount, request.ActualVariableOverheadAmount,
            applied.AppliedMachineTicks, applied.AppliedFixedAmount,
            applied.AppliedVariableAmount, applied.AppliedTotalAmount,
            request.AbnormalDowntimeTicks, request.AbnormalDowntimeDisposition,
            Math.Max(databaseRevision, localRevision) + 1,
            request.RecordedBy, request.SourceReference, request.Reason, request.RecordedAtUtc);
        dbContext.WorkCenterMachineOverheadReconciliations.Add(reconciliation);
        return reconciliation.Id;
    }

    private static string Require(string value, string parameterName)
        => !string.IsNullOrWhiteSpace(value) ? value.Trim() : throw new ArgumentException("Value must not be blank.", parameterName);
}

internal sealed record MachineOverheadAppliedSnapshot(
    long AppliedMachineTicks,
    decimal AppliedFixedAmount,
    decimal AppliedVariableAmount,
    decimal AppliedTotalAmount,
    IReadOnlySet<string> CurrencyCodes);

internal static class MachineOverheadAppliedSnapshotReader
{
    public static async Task<MachineOverheadAppliedSnapshot> ReadAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string accountingPeriodCode,
        string workCenterId,
        CancellationToken cancellationToken)
    {
        var active = await (
            from settlement in dbContext.OperationMachineOverheadSettlements.AsNoTracking()
            join state in dbContext.OperationMachineOverheadSettlementStates.AsNoTracking()
                on new { settlement.OrganizationId, settlement.EnvironmentId, settlement.OperationTaskId }
                equals new { state.OrganizationId, state.EnvironmentId, state.OperationTaskId }
            where settlement.OrganizationId == organizationId
                && settlement.EnvironmentId == environmentId
                && settlement.AccountingPeriodCode == accountingPeriodCode
                && settlement.WorkCenterId == workCenterId
                && settlement.Applicability == MachineOverheadApplicability.Applicable
                && state.ActiveRevision == settlement.SettlementRevision
            select new
            {
                Ticks = settlement.ActualMachineTicks!.Value,
                settlement.FixedAmount,
                settlement.VariableAmount,
                settlement.Amount,
                settlement.CurrencyCode,
            }).ToListAsync(cancellationToken);

        return new(
            active.Sum(x => x.Ticks),
            active.Sum(x => x.FixedAmount),
            active.Sum(x => x.VariableAmount),
            active.Sum(x => x.Amount),
            active.Select(x => x.CurrencyCode).ToHashSet(StringComparer.Ordinal));
    }
}

internal static class MachineOverheadPeriodCloseGuard
{
    public static async Task EnsureReadyAsync(
        ApplicationDbContext dbContext,
        IErpAdvisoryLockAllocator reconciliationLock,
        string organizationId,
        string environmentId,
        string accountingPeriodCode,
        CancellationToken cancellationToken)
    {
        organizationId = organizationId.Trim();
        environmentId = environmentId.Trim();
        accountingPeriodCode = accountingPeriodCode.Trim();
        var rateRevisions = await dbContext.WorkCenterMachineOverheadRates.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.AccountingPeriodCode == accountingPeriodCode)
            .ToListAsync(cancellationToken);
        var applicableRates = rateRevisions
            .GroupBy(x => x.WorkCenterId, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(x => x.Revision).First())
            .Where(x => x.Applicability == MachineOverheadApplicability.Applicable)
            .OrderBy(x => x.WorkCenterId, StringComparer.Ordinal)
            .ToArray();

        foreach (var rate in applicableRates)
        {
            await reconciliationLock.AcquireAsync(
                ErpAdvisoryLockDomain.WorkCenterMachineOverheadReconciliation,
                organizationId, environmentId,
                $"{accountingPeriodCode}\n{rate.WorkCenterId}", cancellationToken);
        }

        foreach (var rate in applicableRates)
        {
            var reconciliation = await dbContext.WorkCenterMachineOverheadReconciliations.AsNoTracking()
                .Where(x => x.OrganizationId == organizationId
                    && x.EnvironmentId == environmentId
                    && x.WorkCenterId == rate.WorkCenterId
                    && x.AccountingPeriodCode == accountingPeriodCode)
                .OrderByDescending(x => x.Revision)
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new KnownException(
                    $"会计期间『{accountingPeriodCode}』工作中心『{rate.WorkCenterId}』缺少机器制造费用实际池归集核对。");
            if (reconciliation.WorkCenterMachineOverheadRateId != rate.Id
                || reconciliation.RateRevision != rate.Revision)
            {
                throw new KnownException(
                    $"会计期间『{accountingPeriodCode}』工作中心『{rate.WorkCenterId}』机器制造费用率已变更，请重新归集核对。");
            }
            if (!string.Equals(reconciliation.CurrencyCode, rate.CurrencyCode, StringComparison.Ordinal))
                throw new KnownException(
                    $"会计期间『{accountingPeriodCode}』工作中心『{rate.WorkCenterId}』机器制造费用币种不一致。");
            if (!reconciliation.IsReadyForClose)
                throw new KnownException(
                    $"会计期间『{accountingPeriodCode}』工作中心『{rate.WorkCenterId}』仍有未处理异常停机。");

            var current = await MachineOverheadAppliedSnapshotReader.ReadAsync(
                dbContext, organizationId, environmentId, accountingPeriodCode, rate.WorkCenterId,
                cancellationToken);
            if (current.CurrencyCodes.Any(currency => !string.Equals(currency, rate.CurrencyCode, StringComparison.Ordinal)))
                throw new KnownException(
                    $"会计期间『{accountingPeriodCode}』工作中心『{rate.WorkCenterId}』active settlement 币种不一致。");
            if (reconciliation.AppliedMachineTicks != current.AppliedMachineTicks
                || reconciliation.AppliedFixedAmount != current.AppliedFixedAmount
                || reconciliation.AppliedVariableAmount != current.AppliedVariableAmount
                || reconciliation.AppliedTotalAmount != current.AppliedTotalAmount)
            {
                throw new KnownException(
                    $"会计期间『{accountingPeriodCode}』工作中心『{rate.WorkCenterId}』active settlement 已变化，请重新归集核对。");
            }
        }
    }
}
