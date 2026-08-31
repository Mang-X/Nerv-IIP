using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Nerv.IIP.Business.Mes.Web.Application.Queries;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Production;
using Nerv.IIP.Business.Mes.Web.Application.Queries.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;
using NetCorePal.Extensions.Primitives;
using Microsoft.Extensions.DependencyInjection;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class MesListQueryCompositionTests
{
    [Fact]
    public void Tenant_scope_normalizes_valid_identifiers()
    {
        var tenant = TenantScope.From("  org-001  ", "  env-dev  ");

        Assert.Equal("org-001", tenant.OrganizationId);
        Assert.Equal("env-dev", tenant.EnvironmentId);
    }

    [Theory]
    [InlineData("", "env-dev", "组织标识不能为空。")]
    [InlineData("org-001", "  ", "环境标识不能为空。")]
    public void Tenant_scope_rejects_missing_identifier(
        string organizationId,
        string environmentId,
        string expectedMessage)
    {
        var exception = Assert.Throws<KnownException>(() =>
            TenantScope.From(organizationId, environmentId));

        Assert.Equal(expectedMessage, exception.Message);
    }

    [Theory]
    [InlineData(-1, 0, 0, 1)]
    [InlineData(0, 100, 0, 100)]
    [InlineData(1, 501, 1, 500)]
    public void Offset_page_preserves_legacy_clamping(
        int skip,
        int take,
        int expectedSkip,
        int expectedTake)
    {
        var page = OffsetPage.From(skip, take);

        Assert.Equal(expectedSkip, page.Skip);
        Assert.Equal(expectedTake, page.Take);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    [InlineData("  PuMp  ", "pump")]
    public void Search_term_normalizes_blank_trim_and_case(string? value, string? expected)
    {
        Assert.Equal(expected, SearchTerm.From(value).Value);
    }

    [Fact]
    public async Task Mes_list_handlers_reject_missing_tenant_before_querying()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handlers = new (string Name, Func<Task> Handle)[]
        {
            (nameof(ListMesWorkOrdersQuery), async () => await new ListMesWorkOrdersQueryHandler(dbContext)
                .Handle(new("", "env-dev", null), CancellationToken.None)),
            (nameof(ListProductionPlansQuery), async () => await new ListProductionPlansQueryHandler(dbContext)
                .Handle(new("", "env-dev", null), CancellationToken.None)),
            (nameof(ListOperationTasksQuery), async () => await new ListOperationTasksQueryHandler(dbContext)
                .Handle(new("", "env-dev", null), CancellationToken.None)),
            (nameof(ListReportableOperationTasksQuery), async () => await new ListReportableOperationTasksQueryHandler(dbContext)
                .Handle(new("", "env-dev"), CancellationToken.None)),
            (nameof(ListMaterialIssueRequestsQuery), async () => await new ListMaterialIssueRequestsQueryHandler(dbContext)
                .Handle(new("", "env-dev", null), CancellationToken.None)),
            (nameof(ListDispatchTasksQuery), async () => await new ListDispatchTasksQueryHandler(dbContext)
                .Handle(new("", "env-dev", null), CancellationToken.None)),
            (nameof(GetWipSummaryQuery), async () => await new GetWipSummaryQueryHandler(dbContext)
                .Handle(new("", "env-dev", null), CancellationToken.None)),
            (nameof(ListRelatedQualityItemsQuery), async () => await new ListRelatedQualityItemsQueryHandler(dbContext)
                .Handle(new("", "env-dev", null, null), CancellationToken.None)),
            (nameof(ListDowntimeEventsQuery), async () => await new ListDowntimeEventsQueryHandler(dbContext, TimeProvider.System)
                .Handle(new("", "env-dev", null, null), CancellationToken.None)),
            (nameof(ListShiftHandoversQuery), async () => await new ListShiftHandoversQueryHandler(dbContext)
                .Handle(new("", "env-dev", null), CancellationToken.None)),
            (nameof(ListProductionReportsQuery), async () => await new ListProductionReportsQueryHandler(dbContext)
                .Handle(new("", "env-dev", null), CancellationToken.None)),
            (nameof(ListFinishedGoodsReceiptRequestsQuery), async () => await new ListFinishedGoodsReceiptRequestsQueryHandler(dbContext)
                .Handle(new("", "env-dev", null), CancellationToken.None)),
            (nameof(ListCapacityImpactsQuery), async () => await new ListCapacityImpactsQueryHandler(dbContext)
                .Handle(new("", "env-dev", null), CancellationToken.None)),
        };

        foreach (var handler in handlers)
        {
            var exception = await Assert.ThrowsAsync<KnownException>(handler.Handle);
            Assert.Equal("组织标识不能为空。", exception.Message);
        }
    }

    [Fact]
    public async Task Work_order_list_applies_normalized_tenant_keyword_and_page_together()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var dueUtc = DateTimeOffset.Parse("2026-08-30T08:00:00Z");
        dbContext.WorkOrders.AddRange(
            WorkOrder.Create("org-001", "env-dev", "WO-PUMP-A", "SKU-A", null, 1m, 1, dueUtc),
            WorkOrder.Create("org-001", "env-dev", "WO-PUMP-B", "SKU-B", null, 1m, 1, dueUtc.AddMinutes(1)),
            WorkOrder.Create("org-other", "env-dev", "WO-PUMP-X", "SKU-X", null, 1m, 1, dueUtc.AddMinutes(2)));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await new ListMesWorkOrdersQueryHandler(dbContext).Handle(
            new("  org-001  ", "  env-dev  ", null, Skip: 1, Take: 1, Keyword: "  Wo-PuMp  "),
            CancellationToken.None);

        Assert.Equal(2, result.Total);
        Assert.Equal("WO-PUMP-B", Assert.Single(result.Items).WorkOrderId);
    }

    [Fact]
    public async Task Mes_list_openapi_keeps_flat_query_parameters_and_defaults()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token"));
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = document.RootElement.GetProperty("paths");

        foreach (var route in ListRoutes)
        {
            var parameters = paths.GetProperty(route)
                .GetProperty("get")
                .GetProperty("parameters")
                .EnumerateArray()
                .ToDictionary(parameter => parameter.GetProperty("name").GetString()!, StringComparer.Ordinal);

            Assert.Contains("organizationId", parameters.Keys);
            Assert.Contains("environmentId", parameters.Keys);
            Assert.Contains("skip", parameters.Keys);
            Assert.Contains("take", parameters.Keys);
            Assert.Contains("keyword", parameters.Keys);
            Assert.Equal(0, parameters["skip"].GetProperty("schema").GetProperty("default").GetInt32());
            Assert.Equal(OffsetPage.DefaultTake, parameters["take"].GetProperty("schema").GetProperty("default").GetInt32());
        }
    }

    private static readonly string[] ListRoutes =
    [
        "/api/business/v1/mes/work-orders",
        "/api/business/v1/mes/production-plans",
        "/api/business/v1/mes/operation-tasks",
        "/api/business/v1/mes/reportable-operation-tasks",
        "/api/business/v1/mes/material-issue-requests",
        "/api/business/v1/mes/dispatch-tasks",
        "/api/business/v1/mes/wip",
        "/api/business/v1/mes/related-quality-items",
        "/api/business/v1/mes/downtime-events",
        "/api/business/v1/mes/shift-handovers",
        "/api/business/v1/mes/production-reports",
        "/api/business/v1/mes/finished-goods-receipt-requests",
        "/api/business/v1/mes/capacity-impacts",
    ];
}
