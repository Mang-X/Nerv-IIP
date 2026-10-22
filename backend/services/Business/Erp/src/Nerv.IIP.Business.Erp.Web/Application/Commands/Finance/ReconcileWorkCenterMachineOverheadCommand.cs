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
        var snapshots = await ReadForWorkCentersAsync(
            dbContext, organizationId, environmentId, accountingPeriodCode,
            [workCenterId], cancellationToken);
        return snapshots.GetValueOrDefault(workCenterId)
            ?? new MachineOverheadAppliedSnapshot(0, 0m, 0m, 0m, new HashSet<string>(StringComparer.Ordinal));
    }

    public static async Task<IReadOnlyDictionary<string, MachineOverheadAppliedSnapshot>> ReadForWorkCentersAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string accountingPeriodCode,
        IReadOnlyCollection<string> workCenterIds,
        CancellationToken cancellationToken)
        => await ReadGroupedAsync(
            dbContext, organizationId, environmentId, accountingPeriodCode,
            workCenterIds, cancellationToken);

    public static async Task<IReadOnlyDictionary<string, MachineOverheadAppliedSnapshot>> ReadForPeriodAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string accountingPeriodCode,
        CancellationToken cancellationToken)
        => await ReadGroupedAsync(
            dbContext, organizationId, environmentId, accountingPeriodCode,
            null, cancellationToken);

    private static async Task<IReadOnlyDictionary<string, MachineOverheadAppliedSnapshot>> ReadGroupedAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string accountingPeriodCode,
        IReadOnlyCollection<string>? workCenterIds,
        CancellationToken cancellationToken)
    {
        var active =
            from settlement in dbContext.OperationMachineOverheadSettlements.AsNoTracking()
            join state in dbContext.OperationMachineOverheadSettlementStates.AsNoTracking()
                on new { settlement.OrganizationId, settlement.EnvironmentId, settlement.OperationTaskId }
                equals new { state.OrganizationId, state.EnvironmentId, state.OperationTaskId }
            where settlement.OrganizationId == organizationId
                && settlement.EnvironmentId == environmentId
                && settlement.AccountingPeriodCode == accountingPeriodCode
                && settlement.Applicability == MachineOverheadApplicability.Applicable
                && state.ActiveRevision == settlement.SettlementRevision
            select settlement;
        if (workCenterIds is not null)
            active = active.Where(settlement => workCenterIds.Contains(settlement.WorkCenterId));

        var grouped = await (
            from settlement in active
            group settlement by new { settlement.WorkCenterId, settlement.CurrencyCode }
            into rows
            select new
            {
                rows.Key.WorkCenterId,
                rows.Key.CurrencyCode,
                AppliedMachineTicks = rows.Sum(x => x.ActualMachineTicks!.Value),
                AppliedFixedAmount = rows.Sum(x => x.FixedAmount),
                AppliedVariableAmount = rows.Sum(x => x.VariableAmount),
                AppliedTotalAmount = rows.Sum(x => x.Amount),
            }).ToListAsync(cancellationToken);

        return grouped
            .GroupBy(x => x.WorkCenterId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => new MachineOverheadAppliedSnapshot(
                    group.Sum(x => x.AppliedMachineTicks),
                    group.Sum(x => x.AppliedFixedAmount),
                    group.Sum(x => x.AppliedVariableAmount),
                    group.Sum(x => x.AppliedTotalAmount),
                    group.Select(x => x.CurrencyCode).ToHashSet(StringComparer.Ordinal)),
                StringComparer.Ordinal);
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
        var scope = await MachineOverheadPeriodReconciliationEvaluator.ReadScopeAsync(
            dbContext, organizationId, environmentId, accountingPeriodCode, null, cancellationToken);

        foreach (var workCenterId in scope.RequiredWorkCenterIds)
        {
            await reconciliationLock.AcquireAsync(
                ErpAdvisoryLockDomain.WorkCenterMachineOverheadReconciliation,
                organizationId, environmentId,
                $"{accountingPeriodCode}\n{workCenterId}", cancellationToken);
        }

        var evaluation = await MachineOverheadPeriodReconciliationEvaluator.EvaluateAsync(
            dbContext, organizationId, environmentId, accountingPeriodCode, scope, cancellationToken);
        var issue = evaluation.FirstIssue(scope.RequiredWorkCenterIds);
        if (issue is not null)
            throw new KnownException(MessageFor(issue, accountingPeriodCode));
    }

    private static string MessageFor(MachineOverheadReconciliationIssue issue, string accountingPeriodCode)
        => issue.ReasonCode switch
        {
            "reconciliation_not_recorded" =>
                $"会计期间『{accountingPeriodCode}』工作中心『{issue.WorkCenterId}』缺少机器制造费用实际池归集核对。",
            "machine_overhead_rate_not_configured" =>
                $"会计期间『{accountingPeriodCode}』工作中心『{issue.WorkCenterId}』缺少机器制造费用率。",
            "machine_overhead_rate_changed" =>
                $"会计期间『{accountingPeriodCode}』工作中心『{issue.WorkCenterId}』机器制造费用率已变更，请重新归集核对。",
            "currency_conflict" =>
                $"会计期间『{accountingPeriodCode}』工作中心『{issue.WorkCenterId}』机器制造费用币种不一致。",
            "abnormal_downtime_pending" =>
                $"会计期间『{accountingPeriodCode}』工作中心『{issue.WorkCenterId}』仍有未处理异常停机。",
            "active_settlement_currency_conflict" =>
                $"会计期间『{accountingPeriodCode}』工作中心『{issue.WorkCenterId}』active settlement 币种不一致。",
            "active_settlement_changed" =>
                $"会计期间『{accountingPeriodCode}』工作中心『{issue.WorkCenterId}』active settlement 已变化，请重新归集核对。",
            _ => throw new InvalidOperationException($"Unknown reconciliation issue: {issue.ReasonCode}"),
        };
}
