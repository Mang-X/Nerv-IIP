using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Infrastructure;

namespace Nerv.IIP.Business.Erp.Web.Application.Queries.SalesFinance;

public sealed record ListSalesOrdersQuery(string OrganizationId, string EnvironmentId) : IQuery<ListSalesOrdersResponse>;
public sealed record ListSalesOrdersResponse(IReadOnlyCollection<SalesOrderResponse> Items);
public sealed record SalesOrderResponse(string SalesOrderNo, string CustomerCode, string Status, decimal TotalAmount);

public sealed class ListSalesOrdersQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListSalesOrdersQuery, ListSalesOrdersResponse>
{
    public async Task<ListSalesOrdersResponse> Handle(ListSalesOrdersQuery request, CancellationToken cancellationToken)
    {
        var orders = await dbContext.SalesOrders
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new SalesOrderResponse(x.SalesOrderNo, x.CustomerCode, x.Status, x.TotalAmount))
            .ToArrayAsync(cancellationToken);
        return new ListSalesOrdersResponse(orders);
    }
}

public sealed record GetFinanceSummaryQuery(string OrganizationId, string EnvironmentId) : IQuery<FinanceSummaryResponse>;
public sealed record FinanceSummaryResponse(decimal OpenPayableAmount, decimal OpenReceivableAmount, decimal CostCandidateAmount, int PostedVoucherCount);

public sealed class GetFinanceSummaryQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetFinanceSummaryQuery, FinanceSummaryResponse>
{
    public async Task<FinanceSummaryResponse> Handle(GetFinanceSummaryQuery request, CancellationToken cancellationToken)
    {
        var payables = await dbContext.AccountPayables
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .SumAsync(x => x.Amount - x.PaidAmount, cancellationToken);
        var receivables = await dbContext.AccountReceivables
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .SumAsync(x => x.Amount - x.CollectedAmount, cancellationToken);
        var costCandidates = await dbContext.CostCandidates
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .SumAsync(x => x.Amount, cancellationToken);
        var vouchers = await dbContext.JournalVouchers
            .AsNoTracking()
            .CountAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId, cancellationToken);
        return new FinanceSummaryResponse(payables, receivables, costCandidates, vouchers);
    }
}
