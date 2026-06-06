using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Infrastructure;

namespace Nerv.IIP.Business.Erp.Web.Application.Queries.SalesFinance;

public sealed record ListSalesOrdersQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? Keyword = null,
    int Skip = 0,
    int Take = 100) : IQuery<ListSalesOrdersResponse>;

public sealed record ListSalesOrdersResponse(IReadOnlyCollection<SalesOrderResponse> Items, int Total);
public sealed record SalesOrderResponse(string SalesOrderNo, string CustomerCode, string Status, decimal TotalAmount);

public sealed class ListSalesOrdersQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListSalesOrdersQuery, ListSalesOrdersResponse>
{
    public async Task<ListSalesOrdersResponse> Handle(ListSalesOrdersQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.SalesOrders
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status.Trim().ToLowerInvariant();
            query = query.Where(x => x.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x =>
                x.SalesOrderNo.Contains(keyword)
                || x.CustomerCode.Contains(keyword)
                || x.QuotationNo.Contains(keyword));
        }

        var total = await query.CountAsync(cancellationToken);
        var skip = Math.Max(request.Skip, 0);
        var take = request.Take <= 0 ? 100 : request.Take;
        var orders = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .Select(x => new SalesOrderResponse(x.SalesOrderNo, x.CustomerCode, x.Status, x.TotalAmount))
            .ToArrayAsync(cancellationToken);
        return new ListSalesOrdersResponse(orders, total);
    }
}

public sealed record ListAccountPayablesQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? Keyword = null,
    int Skip = 0,
    int Take = 100) : IQuery<ListAccountPayablesResponse>;

public sealed record ListAccountPayablesResponse(IReadOnlyCollection<AccountPayableListItem> Items, int Total);

public sealed record AccountPayableListItem(
    string PayableNo,
    string SourceDocumentNo,
    string SupplierCode,
    decimal Amount,
    decimal OpenAmount,
    string CurrencyCode,
    string Status,
    DateTime CreatedAtUtc);

public sealed class ListAccountPayablesQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListAccountPayablesQuery, ListAccountPayablesResponse>
{
    public async Task<ListAccountPayablesResponse> Handle(ListAccountPayablesQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.AccountPayables
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        if (string.Equals(request.Status, "open", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.Amount > x.PaidAmount);
        }
        else if (string.Equals(request.Status, "settled", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.Amount <= x.PaidAmount);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x =>
                x.PayableNo.Contains(keyword)
                || x.SourceDocumentNo.Contains(keyword)
                || x.SupplierCode.Contains(keyword));
        }

        var total = await query.CountAsync(cancellationToken);
        var skip = Math.Max(request.Skip, 0);
        var take = request.Take <= 0 ? 100 : request.Take;
        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .Select(x => new AccountPayableListItem(
                x.PayableNo,
                x.SourceDocumentNo,
                x.SupplierCode,
                x.Amount,
                x.Amount - x.PaidAmount,
                x.CurrencyCode,
                x.Amount > x.PaidAmount ? "open" : "settled",
                x.CreatedAtUtc))
            .ToArrayAsync(cancellationToken);

        return new ListAccountPayablesResponse(items, total);
    }
}

public sealed record ListAccountReceivablesQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? Keyword = null,
    int Skip = 0,
    int Take = 100) : IQuery<ListAccountReceivablesResponse>;

public sealed record ListAccountReceivablesResponse(IReadOnlyCollection<AccountReceivableListItem> Items, int Total);

public sealed record AccountReceivableListItem(
    string ReceivableNo,
    string SourceDocumentNo,
    string CustomerCode,
    decimal Amount,
    decimal OpenAmount,
    string CurrencyCode,
    string Status,
    DateTime CreatedAtUtc);

public sealed class ListAccountReceivablesQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListAccountReceivablesQuery, ListAccountReceivablesResponse>
{
    public async Task<ListAccountReceivablesResponse> Handle(ListAccountReceivablesQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.AccountReceivables
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        if (string.Equals(request.Status, "open", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.Amount > x.CollectedAmount);
        }
        else if (string.Equals(request.Status, "settled", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.Amount <= x.CollectedAmount);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x =>
                x.ReceivableNo.Contains(keyword)
                || x.SourceDocumentNo.Contains(keyword)
                || x.CustomerCode.Contains(keyword));
        }

        var total = await query.CountAsync(cancellationToken);
        var skip = Math.Max(request.Skip, 0);
        var take = request.Take <= 0 ? 100 : request.Take;
        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .Select(x => new AccountReceivableListItem(
                x.ReceivableNo,
                x.SourceDocumentNo,
                x.CustomerCode,
                x.Amount,
                x.Amount - x.CollectedAmount,
                x.CurrencyCode,
                x.Amount > x.CollectedAmount ? "open" : "settled",
                x.CreatedAtUtc))
            .ToArrayAsync(cancellationToken);

        return new ListAccountReceivablesResponse(items, total);
    }
}

public sealed record ListCostCandidatesQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? Keyword = null,
    int Skip = 0,
    int Take = 100) : IQuery<ListCostCandidatesResponse>;

public sealed record ListCostCandidatesResponse(IReadOnlyCollection<CostCandidateListItem> Items, int Total);

public sealed record CostCandidateListItem(
    string CandidateNo,
    string SourceType,
    string SourceDocumentNo,
    decimal Amount,
    string CurrencyCode,
    string Status,
    DateTime CreatedAtUtc);

public sealed class ListCostCandidatesQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListCostCandidatesQuery, ListCostCandidatesResponse>
{
    public async Task<ListCostCandidatesResponse> Handle(ListCostCandidatesQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.CostCandidates
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        if (!string.IsNullOrWhiteSpace(request.Status)
            && !string.Equals(request.Status, "pending", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => false);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x =>
                x.CandidateNo.Contains(keyword)
                || x.SourceType.Contains(keyword)
                || x.SourceDocumentNo.Contains(keyword));
        }

        var total = await query.CountAsync(cancellationToken);
        var skip = Math.Max(request.Skip, 0);
        var take = request.Take <= 0 ? 100 : request.Take;
        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .Select(x => new CostCandidateListItem(
                x.CandidateNo,
                x.SourceType,
                x.SourceDocumentNo,
                x.Amount,
                x.CurrencyCode,
                "pending",
                x.CreatedAtUtc))
            .ToArrayAsync(cancellationToken);

        return new ListCostCandidatesResponse(items, total);
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
