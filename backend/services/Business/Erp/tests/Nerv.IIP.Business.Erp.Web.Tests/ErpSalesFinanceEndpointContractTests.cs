using Nerv.IIP.Business.Erp.Web.Application.Auth;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Sales;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;
using Nerv.IIP.Business.Erp.Web.Application.MasterData;
using Nerv.IIP.Business.Erp.Web.Application.Queries.SalesFinance;
using Nerv.IIP.Business.Erp.Web.Endpoints.Erp;
using Nerv.IIP.ServiceAuth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetCorePal.Extensions.Primitives;
using System.Net;
using System.Text;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed class ErpSalesFinanceEndpointContractTests
{
    [Fact]
    public void Erp_sales_endpoints_expose_issue_138_routes_permissions_policies_and_operation_ids()
    {
        var contracts = ErpSalesEndpointContracts.All.ToArray();

        Assert.Equal(9, contracts.Length);
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/opportunities" && x.PermissionCode == ErpPermissionCodes.SalesManage && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "openErpOpportunity");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/opportunities" && x.HttpMethod == "GET" && x.PermissionCode == ErpPermissionCodes.SalesRead && x.OperationId == "listErpOpportunities");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/quotations" && x.OperationId == "createErpQuotation");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/quotations" && x.HttpMethod == "GET" && x.PermissionCode == ErpPermissionCodes.SalesRead && x.OperationId == "listErpQuotations");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/quotations/{quotationId}/approve" && x.OperationId == "approveErpQuotation");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/sales-orders" && x.OperationId == "createErpSalesOrder");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/delivery-orders" && x.OperationId == "releaseErpDeliveryOrder");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/delivery-orders" && x.HttpMethod == "GET" && x.PermissionCode == ErpPermissionCodes.SalesRead && x.OperationId == "listErpDeliveryOrders");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/sales-orders" && x.HttpMethod == "GET" && x.PermissionCode == ErpPermissionCodes.SalesRead && x.OperationId == "listErpSalesOrders");
    }

    [Fact]
    public void Erp_finance_endpoints_expose_issue_139_routes_permissions_policies_and_operation_ids()
    {
        var contracts = ErpFinanceEndpointContracts.All.ToArray();

        Assert.Equal(14, contracts.Length);
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/finance/payables" && x.PermissionCode == ErpPermissionCodes.FinanceManage && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "createErpAccountPayable");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/finance/payables/payment" && x.PermissionCode == ErpPermissionCodes.FinanceManage && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "registerErpAccountPayablePayment");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/finance/receivables" && x.OperationId == "createErpAccountReceivable");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/finance/receivables/collection" && x.PermissionCode == ErpPermissionCodes.FinanceManage && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "registerErpAccountReceivableCollection");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/finance/cost-candidates" && x.OperationId == "createErpCostCandidate");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/finance/vouchers" && x.OperationId == "postErpJournalVoucher");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/finance/vouchers" && x.HttpMethod == "GET" && x.PermissionCode == ErpPermissionCodes.FinanceRead && x.OperationId == "listErpJournalVouchers");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/finance/summary" && x.HttpMethod == "GET" && x.PermissionCode == ErpPermissionCodes.FinanceRead && x.OperationId == "getErpFinanceSummary");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/finance/payables" && x.HttpMethod == "GET" && x.PermissionCode == ErpPermissionCodes.FinanceRead && x.OperationId == "listErpAccountPayables");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/finance/receivables" && x.HttpMethod == "GET" && x.PermissionCode == ErpPermissionCodes.FinanceRead && x.OperationId == "listErpAccountReceivables");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/finance/cost-candidates" && x.HttpMethod == "GET" && x.PermissionCode == ErpPermissionCodes.FinanceRead && x.OperationId == "listErpCostCandidates");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/finance/payables/by-source" && x.HttpMethod == "GET" && x.PermissionCode == ErpPermissionCodes.FinanceRead && x.OperationId == "getErpPayableBySourceDocument");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/finance/receivables/by-source" && x.HttpMethod == "GET" && x.PermissionCode == ErpPermissionCodes.FinanceRead && x.OperationId == "getErpReceivableBySourceDocument");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/finance/cost-candidates/by-source" && x.HttpMethod == "GET" && x.PermissionCode == ErpPermissionCodes.FinanceRead && x.OperationId == "getErpCostCandidateBySourceDocument");
    }

    [Fact]
    public async Task List_opportunities_query_applies_status_keyword_and_server_paging()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var handler = new OpenOpportunityCommandHandler(dbContext);
        await handler.Handle(new OpenOpportunityCommand("org-001", "env-dev", "OPP-001", "CUST-001", "Line expansion"), CancellationToken.None);
        await handler.Handle(new OpenOpportunityCommand("org-001", "env-dev", "OPP-002", "CUST-002", "New product launch"), CancellationToken.None);
        await handler.Handle(new OpenOpportunityCommand("org-other", "env-dev", "OPP-003", "CUST-002", "Other org"), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new ListOpportunitiesQueryHandler(dbContext).Handle(
            new ListOpportunitiesQuery("org-001", "env-dev", "open", "product", 0, 1),
            CancellationToken.None);

        Assert.Equal(1, response.Total);
        var item = Assert.Single(response.Items);
        Assert.Equal("OPP-002", item.OpportunityNo);
        Assert.Equal("CUST-002", item.CustomerCode);
        Assert.Equal("open", item.Status);
    }

    [Fact]
    public async Task List_quotations_query_applies_status_keyword_and_server_paging()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await CreateQuotationAsync(dbContext, "QUO-001", "CUST-001", "SKU-FG-001");
        await CreateQuotationAsync(dbContext, "QUO-002", "CUST-002", "SKU-FG-002");
        await CreateQuotationAsync(dbContext, "QUO-003", "CUST-002", "SKU-FG-003", "org-other");

        var response = await new ListQuotationsQueryHandler(dbContext).Handle(
            new ListQuotationsQuery("org-001", "env-dev", "Draft", "SKU-FG-002", 0, 1),
            CancellationToken.None);

        Assert.Equal(1, response.Total);
        var item = Assert.Single(response.Items);
        Assert.Equal("QUO-002", item.QuotationNo);
        Assert.Equal("CUST-002", item.CustomerCode);
        Assert.Equal("Draft", item.Status);
        Assert.Equal(200m, item.TotalAmount);
        Assert.Equal("SKU-FG-002", Assert.Single(item.Lines).SkuCode);
    }

    [Fact]
    public async Task List_delivery_orders_query_applies_status_keyword_and_server_paging()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await CreateReleasedSalesOrderAsync(dbContext, "SO-001", "QUO-001", "CUST-001", "SKU-FG-001");
        await CreateReleasedSalesOrderAsync(dbContext, "SO-002", "QUO-002", "CUST-002", "SKU-FG-002");
        await new ReleaseDeliveryOrderCommandHandler(dbContext).Handle(
            new ReleaseDeliveryOrderCommand("org-001", "env-dev", "DO-001", "SO-001", [new DeliveryOrderCommandLine("LINE-001", 1m)]),
            CancellationToken.None);
        await new ReleaseDeliveryOrderCommandHandler(dbContext).Handle(
            new ReleaseDeliveryOrderCommand("org-001", "env-dev", "DO-002", "SO-002", [new DeliveryOrderCommandLine("LINE-001", 1m)]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new ListDeliveryOrdersQueryHandler(dbContext).Handle(
            new ListDeliveryOrdersQuery("org-001", "env-dev", "released", "CUST-002", 0, 1),
            CancellationToken.None);

        Assert.Equal(1, response.Total);
        var item = Assert.Single(response.Items);
        Assert.Equal("DO-002", item.DeliveryOrderNo);
        Assert.Equal("SO-002", item.SalesOrderNo);
        Assert.Equal("released", item.Status);
        Assert.Equal(1m, Assert.Single(item.Lines).Quantity);
    }

    [Fact]
    public async Task List_journal_vouchers_query_applies_status_keyword_and_server_paging()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await new PostJournalVoucherCommandHandler(dbContext).Handle(
            new PostJournalVoucherCommand(
                "org-001",
                "env-dev",
                "JV-001",
                new DateOnly(2026, 6, 1),
                [
                    new JournalVoucherCommandLine("1401", 100m, 0m, "inventory"),
                    new JournalVoucherCommandLine("2202", 0m, 100m, "payable"),
                ]),
            CancellationToken.None);
        await new PostJournalVoucherCommandHandler(dbContext).Handle(
            new PostJournalVoucherCommand(
                "org-001",
                "env-dev",
                "JV-002",
                new DateOnly(2026, 6, 2),
                [
                    new JournalVoucherCommandLine("6001", 250m, 0m, "cost"),
                    new JournalVoucherCommandLine("1401", 0m, 250m, "inventory"),
                ]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new ListJournalVouchersQueryHandler(dbContext).Handle(
            new ListJournalVouchersQuery("org-001", "env-dev", "posted", "6001", 0, 1),
            CancellationToken.None);

        Assert.Equal(1, response.Total);
        var item = Assert.Single(response.Items);
        Assert.Equal("JV-002", item.VoucherNo);
        Assert.Equal("posted", item.Status);
        Assert.Equal(250m, item.TotalDebitAmount);
        Assert.Equal(250m, item.TotalCreditAmount);
    }

    [Fact]
    public async Task New_sales_finance_list_queries_reject_unknown_status_and_cap_take()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        for (var index = 1; index <= 501; index++)
        {
            await new OpenOpportunityCommandHandler(dbContext).Handle(
                new OpenOpportunityCommand("org-001", "env-dev", $"OPP-{index:D3}", "CUST-001", $"Topic {index:D3}"),
                CancellationToken.None);
            await CreateQuotationAsync(dbContext, $"QUO-{index:D3}", "CUST-001", $"SKU-FG-{index:D3}");
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);

        var unknownOpportunities = await new ListOpportunitiesQueryHandler(dbContext).Handle(
            new ListOpportunitiesQuery("org-001", "env-dev", "not-a-status", null, 0, 100),
            CancellationToken.None);
        var cappedQuotations = await new ListQuotationsQueryHandler(dbContext).Handle(
            new ListQuotationsQuery("org-001", "env-dev", null, null, 0, 1000),
            CancellationToken.None);

        Assert.Equal(0, unknownOpportunities.Total);
        Assert.Empty(unknownOpportunities.Items);
        Assert.Equal(501, cappedQuotations.Total);
        Assert.Equal(500, cappedQuotations.Items.Count);
    }

    [Fact]
    public async Task New_sales_finance_list_queries_apply_skip_offset()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await CreateQuotationAsync(dbContext, "QUO-001", "CUST-001", "SKU-FG-001");
        await CreateQuotationAsync(dbContext, "QUO-002", "CUST-001", "SKU-FG-002");
        await CreateQuotationAsync(dbContext, "QUO-003", "CUST-001", "SKU-FG-003");
        SetQuotationCreatedAt(dbContext, "QUO-001", new DateTime(2026, 6, 1, 0, 0, 1, DateTimeKind.Utc));
        SetQuotationCreatedAt(dbContext, "QUO-002", new DateTime(2026, 6, 1, 0, 0, 2, DateTimeKind.Utc));
        SetQuotationCreatedAt(dbContext, "QUO-003", new DateTime(2026, 6, 1, 0, 0, 3, DateTimeKind.Utc));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new ListQuotationsQueryHandler(dbContext).Handle(
            new ListQuotationsQuery("org-001", "env-dev", null, null, 1, 1),
            CancellationToken.None);

        Assert.Equal(3, response.Total);
        Assert.Equal("QUO-002", Assert.Single(response.Items).QuotationNo);
    }

    [Fact]
    public async Task Finance_source_document_queries_return_precise_ap_ar_and_cost_facts()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await new CreateAccountPayableCommandHandler(dbContext).Handle(
            new CreateAccountPayableCommand("org-001", "env-dev", "AP-001", "RCV-001", "SUP-001", 125.50m, "CNY"),
            CancellationToken.None);
        await new CreateAccountReceivableCommandHandler(dbContext).Handle(
            new CreateAccountReceivableCommand("org-001", "env-dev", "AR-001", "DO-001", "CUS-001", 250.75m, "CNY"),
            CancellationToken.None);
        await new CreateCostCandidateCommandHandler(dbContext).Handle(
            new CreateCostCandidateCommand("org-001", "env-dev", "COST-001", "production-report", "RPT-001", 90.25m, "CNY"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var payable = await new GetAccountPayableBySourceDocumentQueryHandler(dbContext).Handle(
            new GetAccountPayableBySourceDocumentQuery("org-001", "env-dev", "RCV-001"),
            CancellationToken.None);
        var receivable = await new GetAccountReceivableBySourceDocumentQueryHandler(dbContext).Handle(
            new GetAccountReceivableBySourceDocumentQuery("org-001", "env-dev", "DO-001"),
            CancellationToken.None);
        var cost = await new GetCostCandidateBySourceDocumentQueryHandler(dbContext).Handle(
            new GetCostCandidateBySourceDocumentQuery("org-001", "env-dev", "production-report", "RPT-001"),
            CancellationToken.None);

        Assert.Equal("AP-001", payable.PayableNo);
        Assert.Equal("RCV-001", payable.SourceDocumentNo);
        Assert.Equal(125.50m, payable.Amount);
        Assert.Equal("CNY", payable.CurrencyCode);
        Assert.Equal("AR-001", receivable.ReceivableNo);
        Assert.Equal("DO-001", receivable.SourceDocumentNo);
        Assert.Equal(250.75m, receivable.Amount);
        Assert.Equal("COST-001", cost.CandidateNo);
        Assert.Equal("production-report", cost.SourceType);
        Assert.Equal("RPT-001", cost.SourceDocumentNo);
        Assert.Equal(90.25m, cost.Amount);
    }

    [Fact]
    public async Task List_sales_orders_query_applies_status_keyword_and_server_paging()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await CreateReleasedSalesOrderAsync(dbContext, "SO-001", "QUO-001", "CUS-001", "SKU-FG-001");
        await CreateReleasedSalesOrderAsync(dbContext, "SO-002", "QUO-002", "CUS-002", "SKU-FG-002");
        await CreateReleasedSalesOrderAsync(dbContext, "SO-003", "QUO-003", "CUS-002", "SKU-FG-003", "org-other");

        var response = await new ListSalesOrdersQueryHandler(dbContext).Handle(
            new ListSalesOrdersQuery("org-001", "env-dev", "Released", "CUS-002", 0, 1),
            CancellationToken.None);

        Assert.Equal(1, response.Total);
        var item = Assert.Single(response.Items);
        Assert.Equal("SO-002", item.SalesOrderNo);
        Assert.Equal("released", item.Status);
    }

    [Fact]
    public void Create_sales_order_contract_no_longer_accepts_caller_supplied_credit_limit()
    {
        Assert.DoesNotContain(typeof(CreateSalesOrderCommand).GetProperties(), property => property.Name == "CustomerCreditLimit");
        Assert.DoesNotContain(typeof(CreateSalesOrderRequest).GetProperties(), property => property.Name == "CustomerCreditLimit");
    }

    [Fact]
    public async Task Master_data_credit_reader_wraps_non_json_error_responses_as_known_exception()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(HttpStatusCode.BadGateway, "<html>bad gateway</html>", "text/html"))
        {
            BaseAddress = new Uri("http://masterdata.test"),
        };
        var reader = new HttpCustomerCreditProfileReader(httpClient, new TestInternalServiceTokenProvider());

        var exception = await Assert.ThrowsAsync<KnownException>(() => reader.GetAsync("org-001", "env-dev", "CUST-001", CancellationToken.None));

        Assert.Contains("HTTP 502", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_sales_order_blocks_when_customer_credit_master_data_is_missing()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await CreateQuotationAsync(dbContext, "QUO-MISSING-CREDIT", "CUST-MISSING", "SKU-FG-001");
        await new ApproveQuotationCommandHandler(dbContext).Handle(
            new ApproveQuotationCommand("org-001", "env-dev", "QUO-MISSING-CREDIT"),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<KnownException>(() => new CreateSalesOrderCommandHandler(
                dbContext,
                new StaticCustomerCreditProfileReader(null)).Handle(
                new CreateSalesOrderCommand("org-001", "env-dev", "SO-MISSING-CREDIT", "QUO-MISSING-CREDIT"),
                CancellationToken.None));

        Assert.Contains("credit limit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_sales_order_uses_master_data_credit_limit_and_blocks_overrun()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await CreateQuotationAsync(dbContext, "QUO-CREDIT-BLOCK", "CUST-001", "SKU-FG-001");
        await new ApproveQuotationCommandHandler(dbContext).Handle(
            new ApproveQuotationCommand("org-001", "env-dev", "QUO-CREDIT-BLOCK"),
            CancellationToken.None);
        await new CreateAccountReceivableCommandHandler(dbContext).Handle(
            new CreateAccountReceivableCommand("org-001", "env-dev", "AR-CREDIT", "DO-CREDIT", "CUST-001", 50m, "CNY"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<KnownException>(() => new CreateSalesOrderCommandHandler(
                dbContext,
                new StaticCustomerCreditProfileReader(new CustomerCreditProfile("CUST-001", 120m, "CNY"))).Handle(
                new CreateSalesOrderCommand("org-001", "env-dev", "SO-CREDIT-BLOCK", "QUO-CREDIT-BLOCK"),
                CancellationToken.None));

        Assert.Contains("credit limit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_sales_order_passes_with_master_data_credit_limit()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await CreateQuotationAsync(dbContext, "QUO-CREDIT-PASS", "CUST-001", "SKU-FG-001");
        await new ApproveQuotationCommandHandler(dbContext).Handle(
            new ApproveQuotationCommand("org-001", "env-dev", "QUO-CREDIT-PASS"),
            CancellationToken.None);

        var salesOrderId = await new CreateSalesOrderCommandHandler(
                dbContext,
                new StaticCustomerCreditProfileReader(new CustomerCreditProfile("CUST-001", 250m, "CNY"))).Handle(
                new CreateSalesOrderCommand("org-001", "env-dev", "SO-CREDIT-PASS", "QUO-CREDIT-PASS"),
                CancellationToken.None);

        Assert.NotNull(salesOrderId);
    }

    [Fact]
    public async Task Finance_list_queries_apply_status_keyword_and_server_paging()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await new CreateAccountPayableCommandHandler(dbContext).Handle(
            new CreateAccountPayableCommand("org-001", "env-dev", "AP-001", "RCV-001", "SUP-001", 125.50m, "CNY"),
            CancellationToken.None);
        await new CreateAccountPayableCommandHandler(dbContext).Handle(
            new CreateAccountPayableCommand("org-001", "env-dev", "AP-002", "RCV-002", "SUP-002", 225.50m, "CNY"),
            CancellationToken.None);
        await new CreateAccountReceivableCommandHandler(dbContext).Handle(
            new CreateAccountReceivableCommand("org-001", "env-dev", "AR-001", "DO-001", "CUS-001", 250.75m, "CNY"),
            CancellationToken.None);
        await new CreateCostCandidateCommandHandler(dbContext).Handle(
            new CreateCostCandidateCommand("org-001", "env-dev", "COST-001", "production-report", "RPT-001", 90.25m, "CNY"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var payables = await new ListAccountPayablesQueryHandler(dbContext).Handle(
            new ListAccountPayablesQuery("org-001", "env-dev", "open", "SUP-002", 0, 1),
            CancellationToken.None);
        var receivables = await new ListAccountReceivablesQueryHandler(dbContext).Handle(
            new ListAccountReceivablesQuery("org-001", "env-dev", "open", "CUS-001", 0, 10),
            CancellationToken.None);
        var costs = await new ListCostCandidatesQueryHandler(dbContext).Handle(
            new ListCostCandidatesQuery("org-001", "env-dev", "pending", "production", 0, 10),
            CancellationToken.None);

        Assert.Equal(1, payables.Total);
        Assert.Equal("AP-002", Assert.Single(payables.Items).PayableNo);
        Assert.Equal(1, receivables.Total);
        Assert.Equal("AR-001", Assert.Single(receivables.Items).ReceivableNo);
        Assert.Equal(1, costs.Total);
        Assert.Equal("COST-001", Assert.Single(costs.Items).CandidateNo);
    }

    [Fact]
    public async Task Finance_list_queries_reject_unknown_status_and_cap_take()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await new CreateAccountPayableCommandHandler(dbContext).Handle(
            new CreateAccountPayableCommand("org-001", "env-dev", "AP-001", "RCV-001", "SUP-001", 125.50m, "CNY"),
            CancellationToken.None);
        await new CreateAccountReceivableCommandHandler(dbContext).Handle(
            new CreateAccountReceivableCommand("org-001", "env-dev", "AR-001", "DO-001", "CUS-001", 250.75m, "CNY"),
            CancellationToken.None);
        for (var index = 1; index <= 501; index++)
        {
            await new CreateCostCandidateCommandHandler(dbContext).Handle(
                new CreateCostCandidateCommand("org-001", "env-dev", $"COST-{index:D3}", "production-report", $"RPT-{index:D3}", 90.25m, "CNY"),
                CancellationToken.None);
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);

        var payables = await new ListAccountPayablesQueryHandler(dbContext).Handle(
            new ListAccountPayablesQuery("org-001", "env-dev", "not-a-status", null, 0, 100),
            CancellationToken.None);
        var receivables = await new ListAccountReceivablesQueryHandler(dbContext).Handle(
            new ListAccountReceivablesQuery("org-001", "env-dev", "not-a-status", null, 0, 100),
            CancellationToken.None);
        var cappedCosts = await new ListCostCandidatesQueryHandler(dbContext).Handle(
            new ListCostCandidatesQuery("org-001", "env-dev", null, null, 0, 1000),
            CancellationToken.None);

        Assert.Equal(0, payables.Total);
        Assert.Empty(payables.Items);
        Assert.Equal(0, receivables.Total);
        Assert.Empty(receivables.Items);
        Assert.Equal(501, cappedCosts.Total);
        Assert.Equal(500, cappedCosts.Items.Count);
    }

    private static async Task CreateReleasedSalesOrderAsync(
        Infrastructure.ApplicationDbContext dbContext,
        string salesOrderNo,
        string quotationNo,
        string customerCode,
        string skuCode,
        string organizationId = "org-001")
    {
        await new CreateQuotationCommandHandler(dbContext).Handle(
            new CreateQuotationCommand(
                organizationId,
                "env-dev",
                quotationNo,
                customerCode,
                new DateOnly(2026, 12, 31),
                [new QuotationCommandLine("LINE-001", skuCode, "EA", 2m, 100m, new DateOnly(2026, 7, 1))]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new ApproveQuotationCommandHandler(dbContext).Handle(
            new ApproveQuotationCommand(organizationId, "env-dev", quotationNo),
            CancellationToken.None);
        await new CreateSalesOrderCommandHandler(
            dbContext,
            new StaticCustomerCreditProfileReader(new CustomerCreditProfile(customerCode, 1_000_000m, "CNY"))).Handle(
            new CreateSalesOrderCommand(organizationId, "env-dev", salesOrderNo, quotationNo),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static async Task CreateQuotationAsync(
        Infrastructure.ApplicationDbContext dbContext,
        string quotationNo,
        string customerCode,
        string skuCode,
        string organizationId = "org-001")
    {
        await new CreateQuotationCommandHandler(dbContext).Handle(
            new CreateQuotationCommand(
                organizationId,
                "env-dev",
                quotationNo,
                customerCode,
                new DateOnly(2026, 12, 31),
                [new QuotationCommandLine("LINE-001", skuCode, "EA", 2m, 100m, new DateOnly(2026, 7, 1))]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static void SetQuotationCreatedAt(
        Infrastructure.ApplicationDbContext dbContext,
        string quotationNo,
        DateTime createdAtUtc)
    {
        var quotation = dbContext.Quotations.Single(x => x.QuotationNo == quotationNo);
        dbContext.Entry(quotation).Property(x => x.CreatedAtUtc).CurrentValue = createdAtUtc;
    }
}

internal static class ErpTestProvider
{
    public static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<Infrastructure.ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"erp-sales-finance-contract-{Guid.NewGuid():N}"));
        return services.BuildServiceProvider();
    }
}

internal sealed class StaticCustomerCreditProfileReader(CustomerCreditProfile? profile) : ICustomerCreditProfileReader
{
    public Task<CustomerCreditProfile?> GetAsync(string organizationId, string environmentId, string customerCode, CancellationToken cancellationToken)
    {
        return Task.FromResult(profile);
    }
}

internal sealed class TestInternalServiceTokenProvider : IInternalServiceTokenProvider
{
    public string BearerToken => "test-internal-token";
}

internal sealed class StubHttpMessageHandler(HttpStatusCode statusCode, string body, string mediaType) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType),
        });
    }
}
