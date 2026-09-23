using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Mes.Web.Application.Quality;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Readiness;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// 生产准备检查的三项基础数据缺口（#3771）：工作中心成本费率（ERP）、MES 配置库位（Inventory）、
/// 待开工工序的设备绑定（MES）。全新部署上这三类数据为零时，链路只会静默失败（#3730），
/// 这里证明检查能指出缺的是哪一条，补齐后该项转为就绪。
/// </summary>
public sealed class MesFoundationReadinessDataGapTests
{
    [Fact]
    public async Task Cost_rate_area_blocks_each_work_center_without_an_effective_rate_until_it_is_configured()
    {
        await using var dbContext = CreateDbContext();
        var sources = new FakeFoundationSources
        {
            WorkCenters = [new("WC-TUB-01", "制管一线"), new("WC-TUB-02", "制管二线")],
        };
        sources.RatedWorkCenters.Add("WC-TUB-02");

        var blocked = await ReadAreaAsync(dbContext, sources, "erp");

        Assert.Equal("Blocked", blocked.Status);
        var issue = Assert.Single(blocked.Issues);
        Assert.Equal("WORK_CENTER_COST_RATE_MISSING", issue.Code);
        Assert.Equal("WC-TUB-01", issue.ReferenceId);
        Assert.Contains("WC-TUB-01", issue.Message, StringComparison.Ordinal);
        Assert.Contains("工作中心费率", issue.FixHint, StringComparison.Ordinal);

        sources.RatedWorkCenters.Add("WC-TUB-01");
        var ready = await ReadAreaAsync(dbContext, sources, "erp");

        Assert.Equal("Ready", ready.Status);
        Assert.Empty(ready.Issues);
    }

    [Fact]
    public async Task Cost_rate_area_passes_the_requested_scope_to_the_work_center_catalog()
    {
        await using var dbContext = CreateDbContext();
        var sources = new FakeFoundationSources();

        await ReadAreaAsync(dbContext, sources, "erp", siteCode: "SITE-001", lineCode: "LINE-TUB", workCenterCode: "WC-TUB-01");

        Assert.Equal(("SITE-001", "LINE-TUB", "WC-TUB-01"), sources.LastWorkCenterScope);
    }

    [Fact]
    public async Task Inventory_area_blocks_configured_locations_that_are_missing_or_not_line_side()
    {
        await using var dbContext = CreateDbContext();
        var sources = new FakeFoundationSources();
        sources.Locations["loc-raw-01"] = new("loc-raw-01", "SITE-001", "storage");
        sources.Locations["loc-line-01"] = new("loc-line-01", "SITE-001", "storage");
        sources.Locations["loc-fg-01"] = new("loc-fg-01", "SITE-002", "storage");

        var readiness = await ReadAreaAsync(dbContext, sources, "inventory");

        Assert.Equal("Blocked", readiness.Status);
        Assert.Equal(
            [
                ("INVENTORY_LOCATION_MISSING", "loc-semi-01"),
                ("LINE_SIDE_LOCATION_TYPE_INVALID", "loc-line-01"),
                // 编码存在但在另一个工厂：线边/成品过账按「工厂 + 库位」对账，等同于不存在。
                ("INVENTORY_LOCATION_MISSING", "loc-fg-01"),
            ],
            readiness.Issues.Select(x => (x.Code, x.ReferenceId!)).ToArray());
        Assert.All(readiness.Issues, x => Assert.Contains("库存管理 ▸ 库位", x.FixHint, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Inventory_area_is_ready_once_every_configured_location_exists_with_the_right_type()
    {
        await using var dbContext = CreateDbContext();
        var sources = new FakeFoundationSources();
        sources.Locations["loc-raw-01"] = new("loc-raw-01", "SITE-001", "storage");
        sources.Locations["loc-semi-01"] = new("loc-semi-01", "SITE-001", "storage");
        sources.Locations["loc-line-01"] = new("loc-line-01", "SITE-001", "line-side");
        sources.Locations["loc-fg-01"] = new("loc-fg-01", "SITE-001", "storage");

        var readiness = await ReadAreaAsync(dbContext, sources, "inventory");

        Assert.Equal("Ready", readiness.Status);
        Assert.Empty(readiness.Issues);
    }

    [Fact]
    public async Task Equipment_area_warns_per_work_order_about_queued_tasks_without_a_device()
    {
        await using var dbContext = CreateDbContext();
        var now = DateTimeOffset.Parse("2026-09-23T08:00:00Z");
        dbContext.OperationTasks.AddRange(
            QueuedTask("WO-001", "OT-001-10", "WC-TUB-01", now),
            QueuedTask("WO-001", "OT-001-20", "WC-TUB-02", now),
            AssignedTask("WO-001", "OT-001-30", "WC-TUB-01", now),
            StartedTask("WO-002", "OT-002-10", "WC-TUB-01", now),
            QueuedTask("WO-003", "OT-003-10", "WC-TUB-02", now));
        await dbContext.SaveChangesAsync();
        var sources = new FakeFoundationSources();

        var all = await ReadAreaAsync(dbContext, sources, "equipment");

        Assert.Equal("Warning", all.Status);
        Assert.All(all.Issues, x => Assert.Equal("Warning", x.Severity));
        Assert.Collection(
            all.Issues,
            x =>
            {
                Assert.Equal("WO-001", x.ReferenceId);
                Assert.Contains("2 道", x.Message, StringComparison.Ordinal);
            },
            x =>
            {
                Assert.Equal("WO-003", x.ReferenceId);
                Assert.Contains("1 道", x.Message, StringComparison.Ordinal);
            });
        Assert.All(all.Issues, x => Assert.Contains("派工看板", x.FixHint, StringComparison.Ordinal));

        var scoped = await ReadAreaAsync(dbContext, sources, "equipment", workCenterCode: "WC-TUB-01");

        var issue = Assert.Single(scoped.Issues);
        Assert.Equal("WO-001", issue.ReferenceId);
        Assert.Contains("1 道", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Equipment_area_counts_only_tasks_of_the_requested_sku()
    {
        await using var dbContext = CreateDbContext();
        var now = DateTimeOffset.Parse("2026-09-23T08:00:00Z");
        dbContext.OperationTasks.AddRange(
            OperationTask.Queue("org-001", "env-dev", "WO-A", "OT-A-10", 10, "WC-TUB-01", [], now, TimeSpan.FromHours(1), "FG-A"),
            OperationTask.Queue("org-001", "env-dev", "WO-B", "OT-B-10", 10, "WC-TUB-01", [], now, TimeSpan.FromHours(1), "FG-B"));
        await dbContext.SaveChangesAsync();

        var readiness = await ReadAreaAsync(dbContext, new FakeFoundationSources(), "equipment", skuId: "FG-A");

        Assert.Equal("WO-A", Assert.Single(readiness.Issues).ReferenceId);
    }

    [Fact]
    public async Task Equipment_area_is_ready_when_every_queued_task_has_a_device()
    {
        await using var dbContext = CreateDbContext();
        var now = DateTimeOffset.Parse("2026-09-23T08:00:00Z");
        dbContext.OperationTasks.Add(AssignedTask("WO-001", "OT-001-10", "WC-TUB-01", now));
        await dbContext.SaveChangesAsync();

        var readiness = await ReadAreaAsync(dbContext, new FakeFoundationSources(), "equipment");

        Assert.Equal("Ready", readiness.Status);
        Assert.Empty(readiness.Issues);
    }

    [Theory]
    [InlineData("{\"data\":{\"currentEffectiveRevision\":3,\"items\":[]},\"success\":true}", true)]
    [InlineData("{\"data\":{\"currentEffectiveRevision\":null,\"items\":[]},\"success\":true}", false)]
    public async Task Http_reader_treats_a_work_center_without_a_current_revision_as_unrated(string body, bool expected)
    {
        var handler = new StubHttpMessageHandler(_ => Json(body));
        var reader = CreateReader(erp: handler);

        var hasRate = await reader.HasEffectiveWorkCenterCostRateAsync("org-001", "env-dev", "WC-TUB-01", CancellationToken.None);

        Assert.Equal(expected, hasRate);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/api/business/v1/erp/finance/work-center-cost-rates", request.RequestUri!.AbsolutePath);
        Assert.Contains("workCenterId=WC-TUB-01", request.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Http_reader_matches_the_location_code_exactly_because_keyword_search_is_a_contains_match()
    {
        var handler = new StubHttpMessageHandler(_ => Json(
            "{\"data\":{\"items\":[" +
            "{\"locationCode\":\"loc-line-010\",\"siteCode\":\"SITE-001\",\"locationType\":\"storage\"}," +
            "{\"locationCode\":\"loc-line-01\",\"siteCode\":\"SITE-001\",\"locationType\":\"line-side\"}" +
            "],\"totalCount\":2,\"page\":1,\"pageSize\":200},\"success\":true}"));
        var reader = CreateReader(inventory: handler);

        var location = await reader.FindStockLocationAsync("org-001", "env-dev", "loc-line-01", CancellationToken.None);

        Assert.Equal(new MesFoundationStockLocation("loc-line-01", "SITE-001", "line-side"), location);
    }

    [Fact]
    public async Task Http_reader_narrows_to_the_requested_work_center_because_master_data_does_not()
    {
        // MasterData 的 work-center 分支不认 workCenterCode 参数（真栈上按工作中心检查时曾列出全部 17 个）。
        var handler = new StubHttpMessageHandler(_ => Json(
            "{\"data\":{\"resources\":[" +
            "{\"resourceType\":\"work-center\",\"code\":\"WC-TUB-01\",\"displayName\":\"缸筒加工中心一线\",\"active\":true,\"snapshotVersion\":\"1\"}," +
            "{\"resourceType\":\"work-center\",\"code\":\"WC-TUB-02\",\"displayName\":\"缸筒加工中心二线\",\"active\":true,\"snapshotVersion\":\"1\"}" +
            "],\"total\":2},\"success\":true}"));
        var reader = CreateReader(masterData: handler);

        var workCenters = await reader.ListActiveWorkCentersAsync("org-001", "env-dev", "SITE-001", null, "WC-TUB-02", CancellationToken.None);

        Assert.Equal([new MesFoundationWorkCenter("WC-TUB-02", "缸筒加工中心二线")], workCenters);
        Assert.Contains("siteCode=SITE-001", Assert.Single(handler.Requests).RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Http_reader_reports_source_failure_instead_of_missing_data()
    {
        var reader = CreateReader(erp: new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            reader.HasEffectiveWorkCenterCostRateAsync("org-001", "env-dev", "WC-TUB-01", CancellationToken.None));

        Assert.StartsWith("FOUNDATION_SOURCE_UNAVAILABLE:", exception.Message, StringComparison.Ordinal);
    }

    private static Task<MesReadinessArea> ReadAreaAsync(
        ApplicationDbContext dbContext,
        FakeFoundationSources sources,
        string areaCode,
        string? siteCode = null,
        string? lineCode = null,
        string? workCenterCode = null,
        string? skuId = null) =>
        new GetMesFoundationReadinessAreaQueryHandler(
            FoundationReadinessServices.Create(dbContext, NoQualityPlans.Instance, sources, DevelopmentSupplyLocations, DevelopmentFinishedGoodsLocation))
            .Handle(
                new GetMesFoundationReadinessAreaQuery(
                    "org-001", "env-dev", areaCode, siteCode, lineCode, workCenterCode, skuId, null, null, null),
                CancellationToken.None);

    /// <summary>与 AppHost 普通 Development 回落、Inventory 产品基线种子同一组库位码。</summary>
    private static readonly MesMaterialSupplyLocationOptions DevelopmentSupplyLocations = new()
    {
        SiteCode = "SITE-001",
        SourceLocationCodes = ["loc-raw-01", "loc-semi-01"],
        LineSideLocationCode = "loc-line-01",
    };

    private static readonly MesFinishedGoodsReceiptLocationOptions DevelopmentFinishedGoodsLocation = new()
    {
        SiteCode = "SITE-001",
        LocationCode = "loc-fg-01",
    };

    private static ApplicationDbContext CreateDbContext() =>
        new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"{nameof(MesFoundationReadinessDataGapTests)}-{Guid.NewGuid():N}")
                .Options,
            new NoopMediator());

    private static OperationTask QueuedTask(string workOrderId, string operationTaskId, string workCenterId, DateTimeOffset now) =>
        OperationTask.Queue("org-001", "env-dev", workOrderId, operationTaskId, 10, workCenterId, [], now, TimeSpan.FromHours(1), "FG-TUBE");

    private static OperationTask AssignedTask(string workOrderId, string operationTaskId, string workCenterId, DateTimeOffset now)
    {
        var task = QueuedTask(workOrderId, operationTaskId, workCenterId, now);
        task.Assign("user-1", "DEV-TUB-01", null, now, actor: "user:dispatcher-1");
        return task;
    }

    private static OperationTask StartedTask(string workOrderId, string operationTaskId, string workCenterId, DateTimeOffset now)
    {
        var task = QueuedTask(workOrderId, operationTaskId, workCenterId, now);
        task.Start(now);
        return task;
    }

    private static HttpMesFoundationSourceReader CreateReader(
        StubHttpMessageHandler? erp = null,
        StubHttpMessageHandler? inventory = null,
        StubHttpMessageHandler? masterData = null) =>
        new(
            new MesMasterDataHttpClient(new HttpClient(masterData ?? new StubHttpMessageHandler(_ => throw new InvalidOperationException("unexpected MasterData call"))) { BaseAddress = new Uri("http://master-data.local") }),
            new MesErpHttpClient(new HttpClient(erp ?? new StubHttpMessageHandler(_ => throw new InvalidOperationException("unexpected ERP call"))) { BaseAddress = new Uri("http://erp.local") }),
            new MesInventoryHttpClient(new HttpClient(inventory ?? new StubHttpMessageHandler(_ => throw new InvalidOperationException("unexpected Inventory call"))) { BaseAddress = new Uri("http://inventory.local") }),
            new TestInternalServiceTokenProvider("internal-token"));

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed record TestInternalServiceTokenProvider(string BearerToken) : IInternalServiceTokenProvider;

    private sealed class NoQualityPlans : IMesQualityInspectionPlanReader
    {
        public static readonly NoQualityPlans Instance = new();

        public Task<bool> HasActiveOperationPlanAsync(
            string organizationId,
            string environmentId,
            string skuCode,
            string? workCenterId,
            CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class FakeFoundationSources : IMesFoundationSourceReader
    {
        public IReadOnlyCollection<MesFoundationWorkCenter> WorkCenters { get; init; } = [];

        public HashSet<string> RatedWorkCenters { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, MesFoundationStockLocation> Locations { get; } = new(StringComparer.Ordinal);

        public (string?, string?, string?) LastWorkCenterScope { get; private set; }

        public Task<IReadOnlyCollection<MesFoundationWorkCenter>> ListActiveWorkCentersAsync(
            string organizationId,
            string environmentId,
            string? siteCode,
            string? lineCode,
            string? workCenterCode,
            CancellationToken cancellationToken)
        {
            LastWorkCenterScope = (siteCode, lineCode, workCenterCode);
            return Task.FromResult(WorkCenters);
        }

        public Task<bool> HasEffectiveWorkCenterCostRateAsync(
            string organizationId,
            string environmentId,
            string workCenterId,
            CancellationToken cancellationToken) => Task.FromResult(RatedWorkCenters.Contains(workCenterId));

        public Task<MesFoundationStockLocation?> FindStockLocationAsync(
            string organizationId,
            string environmentId,
            string locationCode,
            CancellationToken cancellationToken) =>
            Task.FromResult(Locations.TryGetValue(locationCode, out var location) ? location : null);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responseFactory(request));
        }
    }
}

/// <summary>构造生产准备检查服务；不关心基础数据缺口的用例拿到「工作中心都有费率、未配置任何库位」的来源。</summary>
internal static class FoundationReadinessServices
{
    public static MesFoundationReadinessService Create(
        ApplicationDbContext dbContext,
        IMesQualityInspectionPlanReader qualityInspectionPlanReader,
        IMesFoundationSourceReader? foundationSourceReader = null,
        MesMaterialSupplyLocationOptions? materialSupplyLocations = null,
        MesFinishedGoodsReceiptLocationOptions? finishedGoodsReceiptLocation = null) =>
        new(
            dbContext,
            qualityInspectionPlanReader,
            foundationSourceReader ?? NoGapFoundationSources.Instance,
            materialSupplyLocations ?? new MesMaterialSupplyLocationOptions(),
            finishedGoodsReceiptLocation ?? new MesFinishedGoodsReceiptLocationOptions());

    private sealed class NoGapFoundationSources : IMesFoundationSourceReader
    {
        public static readonly NoGapFoundationSources Instance = new();

        public Task<IReadOnlyCollection<MesFoundationWorkCenter>> ListActiveWorkCentersAsync(
            string organizationId,
            string environmentId,
            string? siteCode,
            string? lineCode,
            string? workCenterCode,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<MesFoundationWorkCenter>>([]);

        public Task<bool> HasEffectiveWorkCenterCostRateAsync(
            string organizationId,
            string environmentId,
            string workCenterId,
            CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<MesFoundationStockLocation?> FindStockLocationAsync(
            string organizationId,
            string environmentId,
            string locationCode,
            CancellationToken cancellationToken) => Task.FromResult<MesFoundationStockLocation?>(null);
    }
}
