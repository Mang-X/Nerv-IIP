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

public sealed record GetAccountPayableBySourceDocumentQuery(
    string OrganizationId,
    string EnvironmentId,
    string SourceDocumentNo) : IQuery<AccountPayableSourceDocumentResponse>;

public sealed record AccountPayableSourceDocumentResponse(
    string PayableNo,
    string SourceDocumentNo,
    string SupplierCode,
    decimal Amount,
    decimal OpenAmount,
    string CurrencyCode,
    DateTime CreatedAtUtc);

public sealed class GetAccountPayableBySourceDocumentQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetAccountPayableBySourceDocumentQuery, AccountPayableSourceDocumentResponse>
{
    public async Task<AccountPayableSourceDocumentResponse> Handle(GetAccountPayableBySourceDocumentQuery request, CancellationToken cancellationToken)
    {
        return await dbContext.AccountPayables
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.SourceDocumentNo == request.SourceDocumentNo)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new AccountPayableSourceDocumentResponse(
                x.PayableNo,
                x.SourceDocumentNo,
                x.SupplierCode,
                x.Amount,
                x.Amount - x.PaidAmount,
                x.CurrencyCode,
                x.CreatedAtUtc))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KnownException($"Account payable was not found for source document: {request.SourceDocumentNo}");
    }
}

public sealed record GetAccountReceivableBySourceDocumentQuery(
    string OrganizationId,
    string EnvironmentId,
    string SourceDocumentNo) : IQuery<AccountReceivableSourceDocumentResponse>;

public sealed record AccountReceivableSourceDocumentResponse(
    string ReceivableNo,
    string SourceDocumentNo,
    string CustomerCode,
    decimal Amount,
    decimal OpenAmount,
    string CurrencyCode,
    DateTime CreatedAtUtc);

public sealed class GetAccountReceivableBySourceDocumentQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetAccountReceivableBySourceDocumentQuery, AccountReceivableSourceDocumentResponse>
{
    public async Task<AccountReceivableSourceDocumentResponse> Handle(GetAccountReceivableBySourceDocumentQuery request, CancellationToken cancellationToken)
    {
        return await dbContext.AccountReceivables
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.SourceDocumentNo == request.SourceDocumentNo)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new AccountReceivableSourceDocumentResponse(
                x.ReceivableNo,
                x.SourceDocumentNo,
                x.CustomerCode,
                x.Amount,
                x.Amount - x.CollectedAmount,
                x.CurrencyCode,
                x.CreatedAtUtc))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KnownException($"Account receivable was not found for source document: {request.SourceDocumentNo}");
    }
}

public sealed record GetCostCandidateBySourceDocumentQuery(
    string OrganizationId,
    string EnvironmentId,
    string? SourceType,
    string SourceDocumentNo) : IQuery<CostCandidateSourceDocumentResponse>;

public sealed record CostCandidateSourceDocumentResponse(
    string CandidateNo,
    string SourceType,
    string SourceDocumentNo,
    decimal Amount,
    string CurrencyCode,
    DateTime CreatedAtUtc);

public sealed class GetCostCandidateBySourceDocumentQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetCostCandidateBySourceDocumentQuery, CostCandidateSourceDocumentResponse>
{
    public async Task<CostCandidateSourceDocumentResponse> Handle(GetCostCandidateBySourceDocumentQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.CostCandidates
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.SourceDocumentNo == request.SourceDocumentNo);

        if (!string.IsNullOrWhiteSpace(request.SourceType))
        {
            query = query.Where(x => x.SourceType == request.SourceType);
        }

        return await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new CostCandidateSourceDocumentResponse(
                x.CandidateNo,
                x.SourceType,
                x.SourceDocumentNo,
                x.Amount,
                x.CurrencyCode,
                x.CreatedAtUtc))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KnownException($"Cost candidate was not found for source document: {request.SourceDocumentNo}");
    }
}
