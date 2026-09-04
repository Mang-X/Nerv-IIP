using System.Text.Json;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Nerv.IIP.Business.Erp.Web.Application.Queries;
using Nerv.IIP.Business.Erp.Web.Application.Queries.Finance;
using Nerv.IIP.Business.Erp.Web.Application.Queries.Procurement;
using Nerv.IIP.Business.Erp.Web.Application.Queries.SalesFinance;

namespace Nerv.IIP.Business.Erp.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class ErpListQueryCompositionTests
{
    [Fact]
    public void List_query_composes_normalized_tenant_page_and_keyword_without_changing_public_fields()
    {
        var query = new ListRequestsForQuotationQuery(
            " org-001 ",
            " env-dev ",
            Keyword: "  SuP-002 ",
            Skip: 2,
            Take: 25);

        var tenant = TenantScope.From(query.OrganizationId, query.EnvironmentId);
        var page = OffsetPage.From(query.Skip, query.Take);
        var keyword = SearchTerm.From(query.Keyword);

        Assert.Equal("org-001", tenant.OrganizationId);
        Assert.Equal("env-dev", tenant.EnvironmentId);
        Assert.Equal(2, page.Skip);
        Assert.Equal(25, page.Take);
        Assert.Equal("sup-002", keyword.Value);
        Assert.Equal(" org-001 ", query.OrganizationId);
        Assert.Equal("  SuP-002 ", query.Keyword);
    }

    [Fact]
    public void List_query_criteria_preserves_legacy_page_clamping()
    {
        Assert.Equal(100, OffsetPage.From(0, 100).Take);
        Assert.Equal(500, OffsetPage.From(0, 500).Take);
        Assert.Equal((0, 100), (OffsetPage.From(-1, 0).Skip, OffsetPage.From(-1, 0).Take));
        Assert.Equal(500, OffsetPage.From(0, 501).Take);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void List_query_rules_treat_blank_keyword_as_absent(string? keyword)
    {
        Assert.Null(SearchTerm.From(keyword).Value);
    }

    [Theory]
    [InlineData("", "env-dev")]
    [InlineData("org-001", "")]
    [InlineData("   ", "env-dev")]
    public void Procurement_and_sales_list_query_rules_require_both_tenant_values(
        string organizationId,
        string environmentId)
    {
        var procurement = new ListRequestsForQuotationQueryValidator().Validate(
            new ListRequestsForQuotationQuery(organizationId, environmentId));
        var sales = new ListOpportunitiesQueryValidator().Validate(
            new ListOpportunitiesQuery(organizationId, environmentId));
        var expectedMessage = string.IsNullOrWhiteSpace(organizationId)
            ? "组织标识不能为空。"
            : "环境标识不能为空。";

        Assert.False(procurement.IsValid);
        Assert.False(sales.IsValid);
        Assert.Contains(procurement.Errors, error => error.ErrorMessage == expectedMessage);
        Assert.Contains(sales.Errors, error => error.ErrorMessage == expectedMessage);
    }

    [Theory]
    [InlineData(-1, 100)]
    [InlineData(0, 0)]
    [InlineData(0, 501)]
    public void Work_order_cost_list_keeps_rejecting_invalid_page_values(int skip, int take)
    {
        var result = new ListWorkOrderCostsQueryValidator().Validate(
            new ListWorkOrderCostsQuery("org-001", "env-dev", Skip: skip, Take: take));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Every_keyword_list_query_rejects_missing_tenant_scope()
    {
        var results = new[]
        {
            new ListRequestsForQuotationQueryValidator().Validate(new ListRequestsForQuotationQuery("", "env-dev")),
            new ListSupplierQuotationsQueryValidator().Validate(new ListSupplierQuotationsQuery("", "env-dev")),
            new ListPurchaseRequisitionsQueryValidator().Validate(new ListPurchaseRequisitionsQuery("", "env-dev")),
            new ListPurchaseOrdersQueryValidator().Validate(new ListPurchaseOrdersQuery("", "env-dev")),
            new ListOpportunitiesQueryValidator().Validate(new ListOpportunitiesQuery("", "env-dev")),
            new ListQuotationsQueryValidator().Validate(new ListQuotationsQuery("", "env-dev")),
            new ListSalesOrdersQueryValidator().Validate(new ListSalesOrdersQuery("", "env-dev")),
            new ListDeliveryOrdersQueryValidator().Validate(new ListDeliveryOrdersQuery("", "env-dev")),
            new ListAccountPayablesQueryValidator().Validate(new ListAccountPayablesQuery("", "env-dev")),
            new ListAccountReceivablesQueryValidator().Validate(new ListAccountReceivablesQuery("", "env-dev")),
            new ListCostCandidatesQueryValidator().Validate(new ListCostCandidatesQuery("", "env-dev")),
            new ListJournalVouchersQueryValidator().Validate(new ListJournalVouchersQuery("", "env-dev")),
        };

        Assert.All(results, result => Assert.False(result.IsValid));
    }

    [Fact]
    public async Task OpenApi_keeps_erp_list_queries_flat_with_existing_page_defaults()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = document.RootElement.GetProperty("paths");

        var listContracts = new Dictionary<string, string[]>
        {
            ["/api/business/v1/erp/rfqs"] = ["organizationId", "environmentId", "status", "keyword", "skip", "take"],
            ["/api/business/v1/erp/supplier-quotations"] = ["organizationId", "environmentId", "rfqNo", "supplierCode", "keyword", "skip", "take"],
            ["/api/business/v1/erp/purchase-requisitions"] = ["organizationId", "environmentId", "status", "keyword", "skip", "take"],
            ["/api/business/v1/erp/purchase-orders"] = ["organizationId", "environmentId", "status", "keyword", "skip", "take"],
            ["/api/business/v1/erp/opportunities"] = ["organizationId", "environmentId", "status", "keyword", "skip", "take"],
            ["/api/business/v1/erp/quotations"] = ["organizationId", "environmentId", "status", "keyword", "skip", "take"],
            ["/api/business/v1/erp/sales-orders"] = ["organizationId", "environmentId", "status", "keyword", "skip", "take"],
            ["/api/business/v1/erp/delivery-orders"] = ["organizationId", "environmentId", "status", "keyword", "skip", "take"],
            ["/api/business/v1/erp/finance/payables"] = ["organizationId", "environmentId", "status", "keyword", "skip", "take", "asOfDate"],
            ["/api/business/v1/erp/finance/receivables"] = ["organizationId", "environmentId", "status", "keyword", "skip", "take", "asOfDate"],
            ["/api/business/v1/erp/finance/cost-candidates"] = ["organizationId", "environmentId", "status", "keyword", "skip", "take", "asOfDate"],
            ["/api/business/v1/erp/finance/vouchers"] = ["organizationId", "environmentId", "status", "keyword", "skip", "take", "asOfDate"],
            ["/api/business/v1/erp/finance/work-order-costs"] = ["organizationId", "environmentId", "workOrderId", "sourceNcrId", "sourceWorkOrderId", "skip", "take"],
        };

        foreach (var (path, expectedParameters) in listContracts)
        {
            var parameters = paths.GetProperty(path).GetProperty("get").GetProperty("parameters")
                .EnumerateArray()
                .ToArray();
            Assert.Equal(expectedParameters, parameters.Select(parameter => parameter.GetProperty("name").GetString()));
            Assert.Equal(0, GetDefault(parameters, "skip"));
            Assert.Equal(100, GetDefault(parameters, "take"));
        }
    }

    [Theory]
    [InlineData("?environmentId=env-dev", "组织标识不能为空")]
    [InlineData("?organizationId=org-001", "环境标识不能为空")]
    public async Task Rfq_list_rejects_missing_tenant_with_existing_error_envelope(
        string query,
        string expectedMessage)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            "test-general-internal-token");

        using var response = await client.GetAsync("/api/business/v1/erp/rfqs" + query);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(400, document.RootElement.GetProperty("code").GetInt32());
        Assert.Contains(expectedMessage, document.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.NotEmpty(document.RootElement.GetProperty("errorData").EnumerateArray());
        Assert.False(document.RootElement.TryGetProperty("data", out _));
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["InternalService:BearerToken"] = "test-general-internal-token",
                    ["ConnectionStrings:PostgreSQL"] = "Host=unused;Database=unused;Username=unused;Password=unused",
                    ["Persistence:AutoMigrate"] = "false",
                }));
        });

    private static int GetDefault(IEnumerable<JsonElement> parameters, string name) =>
        parameters.Single(parameter => parameter.GetProperty("name").GetString() == name)
            .GetProperty("schema")
            .GetProperty("default")
            .GetInt32();
}
