namespace Nerv.IIP.Business.Erp.Infrastructure;

public enum WorkCenterRateKind { Labor, MachineOverhead }

public sealed record WorkCenterRateRevision(
    string OrganizationId, string EnvironmentId, string WorkCenterId,
    string? AccountingPeriodCode, string CurrencyCode, int Revision);

public interface IWorkCenterRateRevisionAllocator
{
    Task<WorkCenterRateRevision> AllocateAsync(
        WorkCenterRateKind kind, string organizationId, string environmentId, string workCenterId,
        string? accountingPeriodCode, string currencyCode, CancellationToken cancellationToken);
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
    ApplicationDbContext dbContext, IErpAdvisoryLockAllocator revisionLock) : IWorkCenterRateRevisionAllocator
{
    public async Task<WorkCenterRateRevision> AllocateAsync(
        WorkCenterRateKind kind, string organizationId, string environmentId, string workCenterId,
        string? accountingPeriodCode, string currencyCode, CancellationToken cancellationToken)
    {
        var organization = organizationId.Trim();
        var environment = environmentId.Trim();
        var workCenter = workCenterId.Trim();
        var period = accountingPeriodCode?.Trim();
        var currency = currencyCode.Trim().ToUpperInvariant();
        await revisionLock.AcquireAsync(
            kind == WorkCenterRateKind.Labor ? ErpAdvisoryLockDomain.WorkCenterLaborCostRate : ErpAdvisoryLockDomain.WorkCenterMachineOverheadRate,
            organization, environment, workCenter, cancellationToken);

        var (persistedCurrencies, localCurrencies, databaseRevision, localRevision) = kind switch
        {
            WorkCenterRateKind.Labor => await LaborState(organization, environment, workCenter, cancellationToken),
            WorkCenterRateKind.MachineOverhead => await MachineState(organization, environment, workCenter, period!, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        if (persistedCurrencies.Concat(localCurrencies).Any(existing => !string.Equals(existing, currency, StringComparison.Ordinal)))
            throw new KnownException($"工作中心费率『{organization}·{environment}·{workCenter}』币种已固定。");
        return new(organization, environment, workCenter, period, currency, Math.Max(databaseRevision, localRevision) + 1);
    }

    private async Task<(IReadOnlyList<string>, IEnumerable<string>, int, int)> LaborState(
        string organization, string environment, string workCenter, CancellationToken cancellationToken)
    {
        var query = dbContext.WorkCenterCostRates.Where(x => x.OrganizationId == organization && x.EnvironmentId == environment && x.WorkCenterId == workCenter);
        return (await query.Select(x => x.CurrencyCode).Distinct().ToListAsync(cancellationToken),
            dbContext.WorkCenterCostRates.Local.Where(x => x.OrganizationId == organization && x.EnvironmentId == environment && x.WorkCenterId == workCenter).Select(x => x.CurrencyCode),
            await query.Select(x => (int?)x.Revision).MaxAsync(cancellationToken) ?? 0,
            dbContext.WorkCenterCostRates.Local.Where(x => x.OrganizationId == organization && x.EnvironmentId == environment && x.WorkCenterId == workCenter).Select(x => x.Revision).DefaultIfEmpty(0).Max());
    }

    private async Task<(IReadOnlyList<string>, IEnumerable<string>, int, int)> MachineState(
        string organization, string environment, string workCenter, string period, CancellationToken cancellationToken)
    {
        var scope = dbContext.WorkCenterMachineOverheadRates.Where(x => x.OrganizationId == organization && x.EnvironmentId == environment && x.WorkCenterId == workCenter);
        var revisions = scope.Where(x => x.AccountingPeriodCode == period);
        return (await scope.Select(x => x.CurrencyCode).Distinct().ToListAsync(cancellationToken),
            dbContext.WorkCenterMachineOverheadRates.Local.Where(x => x.OrganizationId == organization && x.EnvironmentId == environment && x.WorkCenterId == workCenter).Select(x => x.CurrencyCode),
            await revisions.Select(x => (int?)x.Revision).MaxAsync(cancellationToken) ?? 0,
            dbContext.WorkCenterMachineOverheadRates.Local.Where(x => x.OrganizationId == organization && x.EnvironmentId == environment && x.WorkCenterId == workCenter && x.AccountingPeriodCode == period).Select(x => x.Revision).DefaultIfEmpty(0).Max());
    }
}
