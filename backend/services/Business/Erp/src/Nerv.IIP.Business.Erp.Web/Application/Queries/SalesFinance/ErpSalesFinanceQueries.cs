using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.CashReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PaymentExecutionAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.QuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SupplierInvoiceAggregate;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;
using Nerv.IIP.Business.Erp.Infrastructure;

namespace Nerv.IIP.Business.Erp.Web.Application.Queries.SalesFinance;

public sealed record ListOpportunitiesQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? Keyword = null,
    int Skip = 0,
    int Take = 100) : IQuery<ListOpportunitiesResponse>;

public sealed record ListOpportunitiesResponse(IReadOnlyCollection<OpportunityListItem> Items, int Total);

public sealed record OpportunityListItem(
    string OpportunityNo,
    string CustomerCode,
    string Topic,
    string Status,
    DateTime OpenedAtUtc);

public sealed class ListOpportunitiesQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListOpportunitiesQuery, ListOpportunitiesResponse>
{
    public async Task<ListOpportunitiesResponse> Handle(ListOpportunitiesQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.Opportunities
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        if (ErpListPaging.IsUnknownSingleStatus(request.Status, "open"))
        {
            query = query.Where(x => false);
        }
        else if (!string.IsNullOrWhiteSpace(request.Status))
        {
            query = query.Where(x => x.Status == "open");
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x =>
                x.OpportunityNo.Contains(keyword)
                || x.CustomerCode.Contains(keyword)
                || x.Topic.Contains(keyword));
        }

        var total = await query.CountAsync(cancellationToken);
        var skip = Math.Max(request.Skip, 0);
        var take = ErpListPaging.NormalizeTake(request.Take);
        var items = await query
            .OrderByDescending(x => x.OpenedAtUtc)
            .Skip(skip)
            .Take(take)
            .Select(x => new OpportunityListItem(x.OpportunityNo, x.CustomerCode, x.Topic, x.Status, x.OpenedAtUtc))
            .ToArrayAsync(cancellationToken);

        return new ListOpportunitiesResponse(items, total);
    }
}

public sealed record ListQuotationsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? Keyword = null,
    int Skip = 0,
    int Take = 100) : IQuery<ListQuotationsResponse>;

public sealed record ListQuotationsResponse(IReadOnlyCollection<QuotationListItem> Items, int Total);

public sealed record QuotationListItem(
    string QuotationNo,
    string CustomerCode,
    DateOnly ExpiresOn,
    string Status,
    decimal TotalAmount,
    IReadOnlyCollection<QuotationLineListItem> Lines,
    DateTime CreatedAtUtc);

public sealed record QuotationLineListItem(
    string LineNo,
    string SkuCode,
    string UomCode,
    decimal Quantity,
    decimal UnitPrice,
    DateOnly RequiredDate);

public sealed class ListQuotationsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListQuotationsQuery, ListQuotationsResponse>
{
    public async Task<ListQuotationsResponse> Handle(ListQuotationsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.Quotations
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        if (!string.IsNullOrWhiteSpace(request.Status)
            && Enum.TryParse<QuotationStatus>(request.Status.Trim(), ignoreCase: true, out var status))
        {
            query = query.Where(x => x.Status == status);
        }
        else if (!string.IsNullOrWhiteSpace(request.Status))
        {
            query = query.Where(x => false);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x =>
                x.QuotationNo.Contains(keyword)
                || x.CustomerCode.Contains(keyword)
                || x.Lines.Any(line => line.SkuCode.Contains(keyword)));
        }

        var total = await query.CountAsync(cancellationToken);
        var skip = Math.Max(request.Skip, 0);
        var take = ErpListPaging.NormalizeTake(request.Take);
        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .Select(x => new QuotationListItem(
                x.QuotationNo,
                x.CustomerCode,
                x.ExpiresOn,
                x.Status.ToString(),
                x.TotalAmount,
                x.Lines
                    .OrderBy(line => line.LineNo)
                    .Select(line => new QuotationLineListItem(
                        line.LineNo,
                        line.SkuCode,
                        line.UomCode,
                        line.Quantity,
                        line.UnitPrice,
                        line.RequiredDate))
                    .ToArray(),
                x.CreatedAtUtc))
            .ToArrayAsync(cancellationToken);

        return new ListQuotationsResponse(items, total);
    }
}

public sealed record ListSalesOrdersQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? Keyword = null,
    int Skip = 0,
    int Take = 100) : IQuery<ListSalesOrdersResponse>;

public sealed record ListSalesOrdersResponse(IReadOnlyCollection<SalesOrderResponse> Items, int Total);
public sealed record SalesOrderResponse(string SalesOrderNo, string CustomerCode, string SiteCode, string Status, decimal TotalAmount);

public sealed record ListDeliveryOrdersQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? Keyword = null,
    int Skip = 0,
    int Take = 100) : IQuery<ListDeliveryOrdersResponse>;

public sealed record ListDeliveryOrdersResponse(IReadOnlyCollection<DeliveryOrderListItem> Items, int Total);

public sealed record DeliveryOrderListItem(
    string DeliveryOrderNo,
    string SalesOrderNo,
    string CustomerCode,
    string SiteCode,
    string Status,
    IReadOnlyCollection<DeliveryOrderLineListItem> Lines,
    DateTime ReleasedAtUtc,
    DateTime? ShippedAtUtc,
    DateTime? CompletedAtUtc);

public sealed record DeliveryOrderLineListItem(
    string SalesOrderLineNo,
    string SkuCode,
    string UomCode,
    string LocationCode,
    string? LotNo,
    decimal Quantity,
    decimal ShippedQuantity);

public sealed class ListDeliveryOrdersQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListDeliveryOrdersQuery, ListDeliveryOrdersResponse>
{
    public async Task<ListDeliveryOrdersResponse> Handle(ListDeliveryOrdersQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.DeliveryOrders
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status.Trim().ToLowerInvariant();
            query = status is "released" or "partially-shipped" or "completed" or "cancelled"
                ? query.Where(x => x.Status == status)
                : query.Where(x => false);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x =>
                x.DeliveryOrderNo.Contains(keyword)
                || x.SalesOrderNo.Contains(keyword)
                || x.CustomerCode.Contains(keyword));
        }

        var total = await query.CountAsync(cancellationToken);
        var skip = Math.Max(request.Skip, 0);
        var take = ErpListPaging.NormalizeTake(request.Take);
        var items = await query
            .OrderByDescending(x => x.ReleasedAtUtc)
            .Skip(skip)
            .Take(take)
            .Select(x => new DeliveryOrderListItem(
                x.DeliveryOrderNo,
                x.SalesOrderNo,
                x.CustomerCode,
                x.SiteCode,
                x.Status,
                x.Lines
                    .OrderBy(line => line.SalesOrderLineNo)
                    .Select(line => new DeliveryOrderLineListItem(
                        line.SalesOrderLineNo,
                        line.SkuCode,
                        line.UomCode,
                        line.LocationCode,
                        line.LotNo,
                        line.Quantity,
                        line.ShippedQuantity))
                    .ToArray(),
                x.ReleasedAtUtc,
                x.ShippedAtUtc,
                x.CompletedAtUtc))
            .ToArrayAsync(cancellationToken);

        return new ListDeliveryOrdersResponse(items, total);
    }
}

internal static class ErpListPaging
{
    public const int DefaultTake = 100;
    public const int MaxTake = 500;

    public static int NormalizeTake(int take)
    {
        return Math.Min(take <= 0 ? DefaultTake : take, MaxTake);
    }

    public static bool IsUnknownSingleStatus(string? status, string allowedStatus)
    {
        return !string.IsNullOrWhiteSpace(status)
            && !string.Equals(status.Trim(), allowedStatus, StringComparison.OrdinalIgnoreCase);
    }
}

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
            query = status == "released"
                ? query.Where(x => x.Status == status)
                : query.Where(x => false);
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
        var take = ErpListPaging.NormalizeTake(request.Take);
        var orders = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .Select(x => new SalesOrderResponse(x.SalesOrderNo, x.CustomerCode, x.SiteCode, x.Status, x.TotalAmount))
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
    int Take = 100,
    DateOnly? AsOfDate = null) : IQuery<ListAccountPayablesResponse>;

public sealed record ListAccountPayablesResponse(IReadOnlyCollection<AccountPayableListItem> Items, int Total);

public sealed record AccountPayableListItem(
    string PayableNo,
    string SourceDocumentNo,
    string SupplierCode,
    decimal Amount,
    decimal OpenAmount,
    string CurrencyCode,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    string PaymentTermCode,
    string AgingBucket,
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
        else if (!string.IsNullOrWhiteSpace(request.Status))
        {
            query = query.Where(x => false);
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
        var take = ErpListPaging.NormalizeTake(request.Take);
        var asOfDate = request.AsOfDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var rows = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToArrayAsync(cancellationToken);
        var items = rows
            .Select(x => new AccountPayableListItem(
                x.PayableNo,
                x.SourceDocumentNo,
                x.SupplierCode,
                x.Amount,
                x.Amount - x.PaidAmount,
                x.CurrencyCode,
                x.InvoiceDate,
                x.DueDate,
                x.PaymentTermCode,
                x.GetAgingBucket(asOfDate),
                x.Amount > x.PaidAmount ? "open" : "settled",
                x.CreatedAtUtc))
            .ToArray();

        return new ListAccountPayablesResponse(items, total);
    }
}

public sealed record ListAccountReceivablesQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? Keyword = null,
    int Skip = 0,
    int Take = 100,
    DateOnly? AsOfDate = null) : IQuery<ListAccountReceivablesResponse>;

public sealed record ListAccountReceivablesResponse(IReadOnlyCollection<AccountReceivableListItem> Items, int Total);

public sealed record AccountReceivableListItem(
    string ReceivableNo,
    string SourceDocumentNo,
    string CustomerCode,
    decimal Amount,
    decimal OpenAmount,
    string CurrencyCode,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    string PaymentTermCode,
    string AgingBucket,
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
        else if (!string.IsNullOrWhiteSpace(request.Status))
        {
            query = query.Where(x => false);
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
        var take = ErpListPaging.NormalizeTake(request.Take);
        var asOfDate = request.AsOfDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var rows = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToArrayAsync(cancellationToken);
        var items = rows
            .Select(x => new AccountReceivableListItem(
                x.ReceivableNo,
                x.SourceDocumentNo,
                x.CustomerCode,
                x.Amount,
                x.Amount - x.CollectedAmount,
                x.CurrencyCode,
                x.InvoiceDate,
                x.DueDate,
                x.PaymentTermCode,
                x.GetAgingBucket(asOfDate),
                x.Amount > x.CollectedAmount ? "open" : "settled",
                x.CreatedAtUtc))
            .ToArray();

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

        // Cost candidates do not yet carry a persisted lifecycle status; pending is the only list status.
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
        var take = ErpListPaging.NormalizeTake(request.Take);
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

public sealed record ListJournalVouchersQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? Keyword = null,
    int Skip = 0,
    int Take = 100) : IQuery<ListJournalVouchersResponse>;

public sealed record ListJournalVouchersResponse(IReadOnlyCollection<JournalVoucherListItem> Items, int Total);

public sealed record JournalVoucherListItem(
    string VoucherNo,
    DateOnly PostingDate,
    string Status,
    decimal TotalDebitAmount,
    decimal TotalCreditAmount,
    IReadOnlyCollection<JournalVoucherLineListItem> Lines,
    DateTime PostedAtUtc);

public sealed record JournalVoucherLineListItem(
    string AccountCode,
    decimal DebitAmount,
    decimal CreditAmount,
    string Memo,
    string CurrencyCode,
    decimal ExchangeRate,
    decimal LocalDebitAmount,
    decimal LocalCreditAmount);

public sealed class ListJournalVouchersQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListJournalVouchersQuery, ListJournalVouchersResponse>
{
    public async Task<ListJournalVouchersResponse> Handle(ListJournalVouchersQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.JournalVouchers
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        if (ErpListPaging.IsUnknownSingleStatus(request.Status, "posted"))
        {
            query = query.Where(x => false);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x =>
                x.VoucherNo.Contains(keyword)
                || x.Lines.Any(line =>
                    line.AccountCode.Contains(keyword)
                    || line.Memo.Contains(keyword)));
        }

        var total = await query.CountAsync(cancellationToken);
        var skip = Math.Max(request.Skip, 0);
        var take = ErpListPaging.NormalizeTake(request.Take);
        var items = await query
            .OrderByDescending(x => x.PostedAtUtc)
            .Skip(skip)
            .Take(take)
            .Select(x => new JournalVoucherListItem(
                x.VoucherNo,
                x.PostingDate,
                "posted",
                x.Lines.Sum(line => line.DebitAmount),
                x.Lines.Sum(line => line.CreditAmount),
                x.Lines
                    .OrderBy(line => line.AccountCode)
                    .Select(line => new JournalVoucherLineListItem(line.AccountCode, line.DebitAmount, line.CreditAmount, line.Memo, line.CurrencyCode, line.ExchangeRate, line.LocalDebitAmount, line.LocalCreditAmount))
                    .ToArray(),
                x.PostedAtUtc))
            .ToArrayAsync(cancellationToken);

        return new ListJournalVouchersResponse(items, total);
    }
}

public sealed record GetFinanceSummaryQuery(string OrganizationId, string EnvironmentId) : IQuery<FinanceSummaryResponse>;
public sealed record CurrencyAmountSummary(string CurrencyCode, decimal OpenAmount, decimal LocalOpenAmount);
public sealed record FinanceSummaryResponse(
    decimal OpenPayableAmount,
    decimal OpenReceivableAmount,
    decimal CostCandidateAmount,
    int PostedVoucherCount,
    IReadOnlyCollection<CurrencyAmountSummary> PayablesByCurrency = null!,
    IReadOnlyCollection<CurrencyAmountSummary> ReceivablesByCurrency = null!,
    IReadOnlyCollection<CurrencyAmountSummary> CostCandidatesByCurrency = null!);

public sealed class GetFinanceSummaryQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetFinanceSummaryQuery, FinanceSummaryResponse>
{
    public async Task<FinanceSummaryResponse> Handle(GetFinanceSummaryQuery request, CancellationToken cancellationToken)
    {
        var payablesByCurrency = await dbContext.AccountPayables
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .GroupBy(x => x.CurrencyCode)
            .Select(x => new CurrencyAmountSummary(x.Key, x.Sum(line => line.Amount - line.PaidAmount), x.Sum(line => line.LocalAmount - line.LocalPaidAmount)))
            .ToArrayAsync(cancellationToken);
        var receivablesByCurrency = await dbContext.AccountReceivables
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .GroupBy(x => x.CurrencyCode)
            .Select(x => new CurrencyAmountSummary(x.Key, x.Sum(line => line.Amount - line.CollectedAmount), x.Sum(line => line.LocalAmount - line.LocalCollectedAmount)))
            .ToArrayAsync(cancellationToken);
        var costCandidatesByCurrency = await dbContext.CostCandidates
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .GroupBy(x => x.CurrencyCode)
            .Select(x => new CurrencyAmountSummary(x.Key, x.Sum(line => line.Amount), x.Sum(line => line.LocalAmount)))
            .ToArrayAsync(cancellationToken);
        var vouchers = await dbContext.JournalVouchers
            .AsNoTracking()
            .CountAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId, cancellationToken);
        var payables = payablesByCurrency.Sum(x => x.OpenAmount);
        var receivables = receivablesByCurrency.Sum(x => x.OpenAmount);
        var costCandidates = costCandidatesByCurrency.Sum(x => x.OpenAmount);
        return new FinanceSummaryResponse(payables, receivables, costCandidates, vouchers, payablesByCurrency, receivablesByCurrency, costCandidatesByCurrency);
    }
}

public sealed record GetTrialBalanceQuery(
    string OrganizationId,
    string EnvironmentId,
    DateOnly PeriodStartDate,
    DateOnly PeriodEndDate) : IQuery<TrialBalanceResponse>;

public sealed record TrialBalanceResponse(
    DateOnly PeriodStartDate,
    DateOnly PeriodEndDate,
    decimal TotalLocalDebitAmount,
    decimal TotalLocalCreditAmount,
    bool IsBalanced,
    IReadOnlyCollection<TrialBalanceLine> Lines);

public sealed record TrialBalanceLine(
    string AccountCode,
    decimal DebitAmount,
    decimal CreditAmount,
    decimal LocalDebitAmount,
    decimal LocalCreditAmount,
    decimal LocalBalanceAmount);

public sealed class GetTrialBalanceQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetTrialBalanceQuery, TrialBalanceResponse>
{
    public async Task<TrialBalanceResponse> Handle(GetTrialBalanceQuery request, CancellationToken cancellationToken)
    {
        var lineBalances = await dbContext.JournalVouchers
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.PostingDate >= request.PeriodStartDate
                && x.PostingDate <= request.PeriodEndDate)
            .SelectMany(x => x.Lines)
            .GroupBy(x => x.AccountCode)
            .Select(x => new
            {
                AccountCode = x.Key,
                DebitAmount = x.Sum(line => line.DebitAmount),
                CreditAmount = x.Sum(line => line.CreditAmount),
                LocalDebitAmount = x.Sum(line => line.LocalDebitAmount),
                LocalCreditAmount = x.Sum(line => line.LocalCreditAmount),
            })
            .OrderBy(x => x.AccountCode)
            .ToArrayAsync(cancellationToken);

        var lines = lineBalances
            .Select(x => new TrialBalanceLine(
                x.AccountCode,
                x.DebitAmount,
                x.CreditAmount,
                x.LocalDebitAmount,
                x.LocalCreditAmount,
                x.LocalDebitAmount - x.LocalCreditAmount))
            .ToArray();

        var totalDebit = lines.Sum(x => x.LocalDebitAmount);
        var totalCredit = lines.Sum(x => x.LocalCreditAmount);
        return new TrialBalanceResponse(
            request.PeriodStartDate,
            request.PeriodEndDate,
            totalDebit,
            totalCredit,
            totalDebit == totalCredit,
            lines);
    }
}

public sealed record GetMonthEndChecklistQuery(
    string OrganizationId,
    string EnvironmentId,
    DateOnly PeriodStartDate,
    DateOnly PeriodEndDate) : IQuery<MonthEndChecklistResponse>;

public sealed record MonthEndChecklistResponse(
    DateOnly PeriodStartDate,
    DateOnly PeriodEndDate,
    int UnpostedDocumentCount,
    int UnmatchedSupplierInvoiceCount,
    decimal GrIrLocalBalance,
    int PostedVoucherCount);

public sealed class GetMonthEndChecklistQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetMonthEndChecklistQuery, MonthEndChecklistResponse>
{
    public async Task<MonthEndChecklistResponse> Handle(GetMonthEndChecklistQuery request, CancellationToken cancellationToken)
    {
        var unexecutedPayments = await dbContext.PaymentExecutions
            .AsNoTracking()
            .CountAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.PaymentDate >= request.PeriodStartDate
                && x.PaymentDate <= request.PeriodEndDate
                && x.Status != PaymentExecutionStatus.Executed,
                cancellationToken);
        var unmatchedCashReceipts = await dbContext.CashReceipts
            .AsNoTracking()
            .CountAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.ReceiptDate >= request.PeriodStartDate
                && x.ReceiptDate <= request.PeriodEndDate
                && x.Status != CashReceiptStatus.Matched,
                cancellationToken);
        var unmatchedSupplierInvoices = await dbContext.SupplierInvoices
            .AsNoTracking()
            .CountAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.InvoiceDate >= request.PeriodStartDate
                && x.InvoiceDate <= request.PeriodEndDate
                && x.MatchStatus != SupplierInvoiceMatchStatus.Matched
                && x.MatchStatus != SupplierInvoiceMatchStatus.Voided,
                cancellationToken);
        var grIrBalance = await dbContext.JournalVouchers
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.PostingDate >= request.PeriodStartDate
                && x.PostingDate <= request.PeriodEndDate)
            .SelectMany(x => x.Lines)
            .Where(x => x.AccountCode == FinanceVoucherFactory.GoodsReceiptInvoiceReceiptAccountCode)
            .SumAsync(x => x.LocalDebitAmount - x.LocalCreditAmount, cancellationToken);
        var postedVoucherCount = await dbContext.JournalVouchers
            .AsNoTracking()
            .CountAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.PostingDate >= request.PeriodStartDate
                && x.PostingDate <= request.PeriodEndDate,
                cancellationToken);

        return new MonthEndChecklistResponse(
            request.PeriodStartDate,
            request.PeriodEndDate,
            unexecutedPayments + unmatchedCashReceipts,
            unmatchedSupplierInvoices,
            Math.Abs(grIrBalance),
            postedVoucherCount);
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
