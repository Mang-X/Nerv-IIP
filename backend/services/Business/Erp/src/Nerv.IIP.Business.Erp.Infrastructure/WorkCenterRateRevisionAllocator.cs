namespace Nerv.IIP.Business.Erp.Infrastructure;

public sealed record WorkCenterRateScope
{
    private WorkCenterRateScope(string organizationId, string environmentId, string workCenterId)
    {
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        WorkCenterId = workCenterId;
    }

    public string OrganizationId { get; }
    public string EnvironmentId { get; }
    public string WorkCenterId { get; }

    public static WorkCenterRateScope From(string organizationId, string environmentId, string workCenterId) =>
        new(
            RequireNonBlank(organizationId, nameof(organizationId)),
            RequireNonBlank(environmentId, nameof(environmentId)),
            RequireNonBlank(workCenterId, nameof(workCenterId)));

    private static string RequireNonBlank(string value, string parameterName) =>
        !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new ArgumentException("Work-center rate scope must not be blank.", parameterName);
}

public sealed record WorkCenterRateCurrency
{
    private WorkCenterRateCurrency(string value) => Value = value;

    public string Value { get; }

    public static WorkCenterRateCurrency From(string currencyCode)
    {
        if (!WorkCenterRateCanonicalization.IsAsciiCurrencyCode(currencyCode))
            throw new ArgumentException("Work-center rate currency must be a three-letter ASCII code.", nameof(currencyCode));
        return new WorkCenterRateCurrency(currencyCode.Trim().ToUpperInvariant());
    }
}

public sealed record WorkCenterRateAccountingPeriod
{
    private WorkCenterRateAccountingPeriod(string value) => Value = value;

    public string Value { get; }

    public static WorkCenterRateAccountingPeriod From(string accountingPeriodCode) =>
        new(!string.IsNullOrWhiteSpace(accountingPeriodCode)
            ? accountingPeriodCode.Trim()
            : throw new ArgumentException("Work-center rate accounting period must not be blank.", nameof(accountingPeriodCode)));
}

public sealed record LaborRateRevisionAllocation(
    WorkCenterRateScope Scope, WorkCenterRateCurrency Currency, int Revision);

public sealed record MachineRateRevisionAllocation(
    WorkCenterRateScope Scope, WorkCenterRateAccountingPeriod AccountingPeriod, WorkCenterRateCurrency Currency,
    int Revision);

public interface IWorkCenterRateRevisionAllocator
{
    Task<LaborRateRevisionAllocation> AllocateLaborAsync(
        WorkCenterRateScope scope, WorkCenterRateCurrency currency, CancellationToken cancellationToken);

    Task<MachineRateRevisionAllocation> AllocateMachineAsync(
        WorkCenterRateScope scope, WorkCenterRateAccountingPeriod accountingPeriodCode, WorkCenterRateCurrency currency,
        CancellationToken cancellationToken);
}

public static class WorkCenterRateCanonicalization
{
    public static bool IsNonBlank(string value) => !string.IsNullOrWhiteSpace(value);

    public static bool IsAsciiCurrencyCode(string value) => !string.IsNullOrWhiteSpace(value)
        && value.Trim().Length == 3
        && value.Trim().All(character => character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z'));

    public static bool IsCanonicalActor(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Any(char.IsWhiteSpace)) return false;
        var separator = value.IndexOf(':', StringComparison.Ordinal);
        return separator > 0 && separator < value.Length - 1;
    }
}

public sealed class WorkCenterRateRevisionAllocator(
    ApplicationDbContext dbContext,
    IErpAdvisoryLockAllocator revisionLock) : IWorkCenterRateRevisionAllocator
{
    public async Task<LaborRateRevisionAllocation> AllocateLaborAsync(
        WorkCenterRateScope scope,
        WorkCenterRateCurrency currency,
        CancellationToken cancellationToken)
    {
        await revisionLock.AcquireAsync(
            ErpAdvisoryLockDomain.WorkCenterLaborCostRate,
            scope.OrganizationId, scope.EnvironmentId, scope.WorkCenterId, cancellationToken);
        var state = await ReadLaborStateAsync(scope, cancellationToken);
        EnsureCurrencyIsFixed(scope, currency, state.PersistedCurrencies, state.LocalCurrencies);
        return new LaborRateRevisionAllocation(scope, currency, NextRevision(state));
    }

    public async Task<MachineRateRevisionAllocation> AllocateMachineAsync(
        WorkCenterRateScope scope,
        WorkCenterRateAccountingPeriod accountingPeriodCode,
        WorkCenterRateCurrency currency,
        CancellationToken cancellationToken)
    {
        await revisionLock.AcquireAsync(
            ErpAdvisoryLockDomain.WorkCenterMachineOverheadReconciliation,
            scope.OrganizationId, scope.EnvironmentId, accountingPeriodCode.Value, cancellationToken);
        await revisionLock.AcquireAsync(
            ErpAdvisoryLockDomain.WorkCenterMachineOverheadReconciliation,
            scope.OrganizationId, scope.EnvironmentId,
            $"{accountingPeriodCode.Value}\n{scope.WorkCenterId}", cancellationToken);
        await revisionLock.AcquireAsync(
            ErpAdvisoryLockDomain.WorkCenterMachineOverheadRate,
            scope.OrganizationId, scope.EnvironmentId, scope.WorkCenterId, cancellationToken);
        var state = await ReadMachineStateAsync(scope, accountingPeriodCode, cancellationToken);
        EnsureCurrencyIsFixed(scope, currency, state.PersistedCurrencies, state.LocalCurrencies);
        return new MachineRateRevisionAllocation(scope, accountingPeriodCode, currency, NextRevision(state));
    }

    private async Task<RevisionState> ReadLaborStateAsync(
        WorkCenterRateScope scope,
        CancellationToken cancellationToken)
    {
        var persisted = dbContext.WorkCenterCostRates.Where(x =>
            x.OrganizationId == scope.OrganizationId
            && x.EnvironmentId == scope.EnvironmentId
            && x.WorkCenterId == scope.WorkCenterId);
        var local = dbContext.WorkCenterCostRates.Local.Where(x =>
            x.OrganizationId == scope.OrganizationId
            && x.EnvironmentId == scope.EnvironmentId
            && x.WorkCenterId == scope.WorkCenterId);
        return new RevisionState(
            await persisted.Select(x => x.CurrencyCode).Distinct().ToListAsync(cancellationToken),
            local.Select(x => x.CurrencyCode).ToArray(),
            await persisted.Select(x => (int?)x.Revision).MaxAsync(cancellationToken) ?? 0,
            local.Select(x => x.Revision).DefaultIfEmpty(0).Max());
    }

    private async Task<RevisionState> ReadMachineStateAsync(
        WorkCenterRateScope scope,
        WorkCenterRateAccountingPeriod accountingPeriodCode,
        CancellationToken cancellationToken)
    {
        var currencyScope = dbContext.WorkCenterMachineOverheadRates.Where(x =>
            x.OrganizationId == scope.OrganizationId
            && x.EnvironmentId == scope.EnvironmentId
            && x.WorkCenterId == scope.WorkCenterId);
        var revisionScope = currencyScope.Where(x => x.AccountingPeriodCode == accountingPeriodCode.Value);
        var local = dbContext.WorkCenterMachineOverheadRates.Local.Where(x =>
            x.OrganizationId == scope.OrganizationId
            && x.EnvironmentId == scope.EnvironmentId
            && x.WorkCenterId == scope.WorkCenterId);
        return new RevisionState(
            await currencyScope.Select(x => x.CurrencyCode).Distinct().ToListAsync(cancellationToken),
            local.Select(x => x.CurrencyCode).ToArray(),
            await revisionScope.Select(x => (int?)x.Revision).MaxAsync(cancellationToken) ?? 0,
            local.Where(x => x.AccountingPeriodCode == accountingPeriodCode.Value)
                .Select(x => x.Revision).DefaultIfEmpty(0).Max());
    }

    private static void EnsureCurrencyIsFixed(
        WorkCenterRateScope scope,
        WorkCenterRateCurrency currency,
        IEnumerable<string> persistedCurrencies,
        IEnumerable<string> localCurrencies)
    {
        if (persistedCurrencies.Concat(localCurrencies)
            .Any(existing => !string.Equals(existing, currency.Value, StringComparison.Ordinal)))
        {
            throw new KnownException(
                $"工作中心费率『{scope.OrganizationId}·{scope.EnvironmentId}·{scope.WorkCenterId}』币种已固定。");
        }
    }

    private static int NextRevision(RevisionState state) =>
        Math.Max(state.DatabaseRevision, state.LocalRevision) + 1;

    private sealed record RevisionState(
        IReadOnlyList<string> PersistedCurrencies,
        IReadOnlyList<string> LocalCurrencies,
        int DatabaseRevision,
        int LocalRevision);
}
