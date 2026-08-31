using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountingPeriodAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkCenterMachineOverheadRateAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;

namespace Nerv.IIP.Business.Erp.Infrastructure.Repositories;

public interface IAccountingPeriodRepository : IRepository<AccountingPeriod, AccountingPeriodId>
{
    Task<bool> ExistsAsync(string organizationId, string environmentId, string periodCode, CancellationToken cancellationToken);
}

public sealed class AccountingPeriodRepository(ApplicationDbContext context)
    : RepositoryBase<AccountingPeriod, AccountingPeriodId, ApplicationDbContext>(context), IAccountingPeriodRepository
{
    public Task<bool> ExistsAsync(string organizationId, string environmentId, string periodCode, CancellationToken cancellationToken) =>
        DbContext.AccountingPeriods.AnyAsync(x => x.OrganizationId == organizationId
            && x.EnvironmentId == environmentId && x.PeriodCode == periodCode, cancellationToken);
}

public interface IWorkCenterMachineOverheadRateRepository
    : IRepository<WorkCenterMachineOverheadRate, WorkCenterMachineOverheadRateId>;

public sealed class WorkCenterMachineOverheadRateRepository(ApplicationDbContext context)
    : RepositoryBase<WorkCenterMachineOverheadRate, WorkCenterMachineOverheadRateId, ApplicationDbContext>(context),
        IWorkCenterMachineOverheadRateRepository;

public interface IWorkCenterCostRateRepository : IRepository<WorkCenterCostRate, WorkCenterCostRateId>;

public sealed class WorkCenterCostRateRepository(ApplicationDbContext context)
    : RepositoryBase<WorkCenterCostRate, WorkCenterCostRateId, ApplicationDbContext>(context), IWorkCenterCostRateRepository;
