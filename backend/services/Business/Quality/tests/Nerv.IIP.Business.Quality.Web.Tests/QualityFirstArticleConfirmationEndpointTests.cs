using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.PeriodicInspectionOperationAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Quality.Web.Application.Queries.InspectionTasks;
using Nerv.IIP.Contracts.Quality;

namespace Nerv.IIP.Business.Quality.Web.Tests;

/// <summary>
/// #2779 首件确认读契约：波 2 的 MES 报工门禁按「工单 + 工序」取首件判定结论。
/// 门禁只在 not-required，或 decided 且结论 passed 时放行，因此每个取值都是线上承重值。
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class QualityFirstArticleConfirmationEndpointTests
{
    [Fact]
    public async Task Operation_without_an_active_first_article_plan_reports_not_required()
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        await SeedAsync(factory, dbContext =>
        {
            // 该工序事实已知，但只有别的工作中心配了首件档。
            dbContext.InspectionPlans.Add(FirstArticlePlan("PLAN-FA-ASSY", "WC-ASSY"));
            dbContext.PeriodicInspectionOperations.Add(ReleasedOperation("WO-001", "OP-10", "SKU-FG-1000", "WC-MIX"));
        });

        var confirmation = await GetConfirmationAsync(client, "WO-001", "OP-10");

        Assert.Equal("WO-001", confirmation.WorkOrderId);
        Assert.Equal("OP-10", confirmation.OperationId);
        Assert.Equal("not-required", confirmation.Status);
        Assert.Null(confirmation.Result);
        Assert.Null(confirmation.AttemptNumber);
        Assert.Null(confirmation.InspectionTaskId);
        Assert.Null(confirmation.InspectionRecordId);
    }

    [Fact]
    public async Task Operation_with_an_active_plan_but_no_task_reports_not_opened()
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        await SeedAsync(factory, dbContext =>
        {
            dbContext.InspectionPlans.Add(FirstArticlePlan());
            dbContext.PeriodicInspectionOperations.Add(ReleasedOperation("WO-001", "OP-10", "SKU-FG-1000", "WC-MIX"));
        });

        var confirmation = await GetConfirmationAsync(client, "WO-001", "OP-10");

        Assert.Equal("not-opened", confirmation.Status);
    }

    [Fact]
    public async Task Operation_whose_release_facts_never_arrived_reports_not_opened()
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var confirmation = await GetConfirmationAsync(client, "WO-001", "OP-10");

        // Quality 不知道该工序的物料/工作中心时不得回报「无需首件」，否则门禁会放行漏开的首件。
        Assert.Equal("not-opened", confirmation.Status);
    }

    [Fact]
    public async Task Open_first_article_task_reports_pending_without_a_result()
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        InspectionTaskId taskId = null!;
        await SeedAsync(factory, dbContext =>
        {
            var task = FirstArticleTask("WO-001", "OP-10");
            dbContext.InspectionTasks.Add(task);
            taskId = task.Id;
        });

        var confirmation = await GetConfirmationAsync(client, "WO-001", "OP-10");

        Assert.Equal("pending", confirmation.Status);
        Assert.Equal(taskId.Id, confirmation.InspectionTaskId);
        Assert.Null(confirmation.Result);
        Assert.Null(confirmation.AttemptNumber);
        Assert.Null(confirmation.InspectionRecordId);
    }

    [Fact]
    public async Task Judged_first_article_task_reports_decided_over_the_wire()
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        InspectionRecordId recordId = null!;
        await SeedAsync(factory, dbContext =>
        {
            var plan = FirstArticlePlan();
            var task = FirstArticleTask("WO-001", "OP-10", plan.Id);
            var record = FirstArticleRecord(plan.Id, InspectionLineResults.Passed);
            task.Start("inspector-001", DateTimeOffset.Parse("2026-07-05T09:00:00Z"));
            task.Complete(record.Id, DateTimeOffset.Parse("2026-07-05T09:30:00Z"));
            dbContext.InspectionPlans.Add(plan);
            dbContext.InspectionRecords.Add(record);
            dbContext.InspectionTasks.Add(task);
            recordId = record.Id;
        });

        var confirmation = await GetConfirmationAsync(client, "WO-001", "OP-10");

        Assert.Equal("decided", confirmation.Status);
        Assert.Equal("passed", confirmation.Result);
        Assert.Equal(1, confirmation.AttemptNumber);
        Assert.Equal(recordId.Id, confirmation.InspectionRecordId);
    }

    [Fact]
    public async Task Confirmation_is_scoped_to_the_requested_work_order_operation()
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        await SeedAsync(factory, dbContext => dbContext.InspectionTasks.Add(FirstArticleTask("WO-001", "OP-10")));

        Assert.Equal("not-opened", (await GetConfirmationAsync(client, "WO-001", "OP-20")).Status);
        Assert.Equal("not-opened", (await GetConfirmationAsync(client, "WO-002", "OP-10")).Status);
    }

    [Fact]
    public async Task Confirmation_requires_internal_service_authorization()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/api/business/v1/quality/first-article-confirmation?organizationId=org-001&environmentId=env-dev&workOrderId=WO-001&operationId=OP-10");

        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"Expected auth failure but received {(int)response.StatusCode}.");
    }

    [Theory]
    [InlineData(InspectionLineResults.Passed, "passed")]
    [InlineData(InspectionLineResults.Failed, "rejected")]
    public async Task Judged_first_article_task_reports_the_initial_result(string lineResult, string expectedResult)
    {
        await using var dbContext = CreateDbContext($"quality-first-article-{expectedResult}");
        var plan = FirstArticlePlan();
        var task = FirstArticleTask("WO-001", "OP-10", plan.Id);
        var record = FirstArticleRecord(plan.Id, lineResult);
        task.Start("inspector-001", DateTimeOffset.Parse("2026-07-05T09:00:00Z"));
        task.Complete(record.Id, DateTimeOffset.Parse("2026-07-05T09:30:00Z"));
        dbContext.InspectionPlans.Add(plan);
        dbContext.InspectionRecords.Add(record);
        dbContext.InspectionTasks.Add(task);
        await dbContext.SaveChangesAsync();

        var confirmation = await HandleAsync(dbContext, "WO-001", "OP-10");

        Assert.Equal(QualityFirstArticleConfirmationStatuses.Decided, confirmation.Status);
        Assert.Equal(expectedResult, confirmation.Result);
        Assert.Equal(1, confirmation.AttemptNumber);
        Assert.Equal(record.Id, confirmation.InspectionRecordId);
    }

    [Fact]
    public async Task Reinspection_result_supersedes_the_rejected_initial_first_article_result()
    {
        await using var dbContext = CreateDbContext(nameof(Reinspection_result_supersedes_the_rejected_initial_first_article_result));
        var plan = FirstArticlePlan();
        var task = FirstArticleTask("WO-001", "OP-10", plan.Id);
        var rejected = FirstArticleRecord(plan.Id, InspectionLineResults.Failed);
        task.Start("inspector-001", DateTimeOffset.Parse("2026-07-05T09:00:00Z"));
        task.Complete(rejected.Id, DateTimeOffset.Parse("2026-07-05T09:30:00Z"));
        // 复检新建 attempt 2 记录并且不回写任务；任务仍指向初检记录。
        var reinspection = InspectionRecord.Reinspect(
            rejected,
            plan,
            [new InspectionResultLineInput("appearance", "ok", null, InspectionLineResults.Passed, null, null, [])],
            null,
            []);
        dbContext.InspectionPlans.Add(plan);
        dbContext.InspectionRecords.AddRange(rejected, reinspection);
        dbContext.InspectionTasks.Add(task);
        await dbContext.SaveChangesAsync();
        Assert.Equal(rejected.Id, task.InspectionRecordId);

        var confirmation = await HandleAsync(dbContext, "WO-001", "OP-10");

        Assert.Equal(QualityFirstArticleConfirmationStatuses.Decided, confirmation.Status);
        Assert.Equal(QualityInspectionDispositionStatuses.Passed, confirmation.Result);
        Assert.Equal(2, confirmation.AttemptNumber);
        Assert.Equal(reinspection.Id, confirmation.InspectionRecordId);
    }

    [Fact]
    public async Task Each_operation_of_the_same_work_order_reports_its_own_result()
    {
        await using var dbContext = CreateDbContext(nameof(Each_operation_of_the_same_work_order_reports_its_own_result));
        var plan = FirstArticlePlan();
        var firstOperationTask = FirstArticleTask("WO-001", "OP-10", plan.Id);
        var firstOperationRecord = FirstArticleRecord(plan.Id, InspectionLineResults.Failed);
        var secondOperationTask = FirstArticleTask("WO-001", "OP-20", plan.Id);
        var secondOperationRecord = FirstArticleRecord(plan.Id, InspectionLineResults.Passed, "OP-20");
        firstOperationTask.Start("inspector-001", DateTimeOffset.Parse("2026-07-05T09:00:00Z"));
        firstOperationTask.Complete(firstOperationRecord.Id, DateTimeOffset.Parse("2026-07-05T09:30:00Z"));
        secondOperationTask.Start("inspector-001", DateTimeOffset.Parse("2026-07-05T10:00:00Z"));
        secondOperationTask.Complete(secondOperationRecord.Id, DateTimeOffset.Parse("2026-07-05T10:30:00Z"));
        dbContext.InspectionPlans.Add(plan);
        dbContext.InspectionRecords.AddRange(firstOperationRecord, secondOperationRecord);
        dbContext.InspectionTasks.AddRange(firstOperationTask, secondOperationTask);
        await dbContext.SaveChangesAsync();

        var firstOperation = await HandleAsync(dbContext, "WO-001", "OP-10");
        var secondOperation = await HandleAsync(dbContext, "WO-001", "OP-20");

        // 首件判定结论必须落到工序：不合格的 OP-10 不得因为同工单 OP-20 合格而被放行。
        Assert.Equal(QualityInspectionDispositionStatuses.Rejected, firstOperation.Result);
        Assert.Equal(firstOperationRecord.Id, firstOperation.InspectionRecordId);
        Assert.Equal(QualityInspectionDispositionStatuses.Passed, secondOperation.Result);
        Assert.Equal(secondOperationRecord.Id, secondOperation.InspectionRecordId);
    }

    [Fact]
    public async Task Reinspection_of_another_operation_does_not_supersede_this_operation_result()
    {
        await using var dbContext = CreateDbContext(nameof(Reinspection_of_another_operation_does_not_supersede_this_operation_result));
        var plan = FirstArticlePlan();
        var task = FirstArticleTask("WO-001", "OP-10", plan.Id);
        var record = FirstArticleRecord(plan.Id, InspectionLineResults.Failed);
        task.Start("inspector-001", DateTimeOffset.Parse("2026-07-05T09:00:00Z"));
        task.Complete(record.Id, DateTimeOffset.Parse("2026-07-05T09:30:00Z"));
        // 同工单另一道工序复检到 attempt 2 并判合格，不得盖过本工序仍未合格的结论。
        var otherOperationRecord = FirstArticleRecord(plan.Id, InspectionLineResults.Failed, "OP-20");
        var otherOperationReinspection = InspectionRecord.Reinspect(
            otherOperationRecord,
            plan,
            [new InspectionResultLineInput("appearance", "ok", null, InspectionLineResults.Passed, null, null, [])],
            null,
            []);
        dbContext.InspectionPlans.Add(plan);
        dbContext.InspectionRecords.AddRange(record, otherOperationRecord, otherOperationReinspection);
        dbContext.InspectionTasks.Add(task);
        await dbContext.SaveChangesAsync();

        var confirmation = await HandleAsync(dbContext, "WO-001", "OP-10");

        Assert.Equal(QualityInspectionDispositionStatuses.Rejected, confirmation.Result);
        Assert.Equal(1, confirmation.AttemptNumber);
        Assert.Equal(record.Id, confirmation.InspectionRecordId);
    }

    private static Task<FirstArticleConfirmationResponse> HandleAsync(
        ApplicationDbContext dbContext,
        string workOrderId,
        string operationId)
    {
        return new GetFirstArticleConfirmationQueryHandler(dbContext).Handle(
            new GetFirstArticleConfirmationQuery("org-001", "env-dev", workOrderId, operationId),
            CancellationToken.None);
    }

    private static async Task<FirstArticleConfirmationWire> GetConfirmationAsync(
        HttpClient client,
        string workOrderId,
        string operationId)
    {
        using var response = await client.GetAsync(
            $"/api/business/v1/quality/first-article-confirmation?organizationId=org-001&environmentId=env-dev&workOrderId={workOrderId}&operationId={operationId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<FirstArticleConfirmationWire>>();
        Assert.NotNull(envelope?.Data);
        return envelope!.Data!;
    }

    private static async Task SeedAsync(WebApplicationFactory<Program> factory, Action<ApplicationDbContext> seed)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        seed(dbContext);
        await dbContext.SaveChangesAsync();
    }

    private static PeriodicInspectionOperation ReleasedOperation(
        string workOrderId,
        string operationId,
        string skuCode,
        string workCenterId)
    {
        var operation = PeriodicInspectionOperation.CreatePending("org-001", "env-dev", workOrderId, operationId);
        operation.ApplyRelease(skuCode, 10, workCenterId, DateTime.Parse("2026-07-05T07:00:00Z").ToUniversalTime(), []);
        return operation;
    }

    private static InspectionPlan FirstArticlePlan(string planCode = "PLAN-FA-1000", string workCenterId = "WC-MIX")
    {
        var plan = InspectionPlan.Create("org-001", "env-dev", planCode, "first-article", "SKU-FG-1000", null, workCenterId, null, null);
        plan.AddCharacteristic("appearance", "Appearance", "visual", "major", required: true, "100%");
        plan.Activate();
        return plan;
    }

    private static InspectionRecord FirstArticleRecord(InspectionPlanId planId, string lineResult, string operationId = "OP-10")
    {
        return InspectionRecord.Create(
            "org-001",
            "env-dev",
            planId,
            "first-article",
            "mes",
            FirstArticleInspection.SourceDocumentId("WO-001", operationId),
            "SKU-FG-1000",
            1m,
            null,
            null,
            [new InspectionResultLineInput("appearance", "ok", null, lineResult, "surface", 1m, [])],
            "首件判定留档",
            []);
    }

    private static InspectionTask FirstArticleTask(string workOrderId, string operationId, InspectionPlanId? planId = null)
    {
        return InspectionTask.CreatePending(
            "org-001",
            "env-dev",
            planId ?? new InspectionPlanId(Guid.Parse("018f7b14-9fb0-7d9b-a7fb-78bd14f9b201")),
            FirstArticleInspection.SourceType,
            FirstArticleInspection.SourceService,
            FirstArticleInspection.SourceDocumentId(workOrderId, operationId),
            operationId,
            "SKU-FG-1000",
            1m,
            "pcs",
            null,
            null,
            DateTimeOffset.Parse("2026-07-05T08:00:00Z"),
            DateTimeOffset.Parse("2026-07-06T08:00:00Z"),
            FirstArticleInspection.TriggerIdempotencyKey("org-001", "env-dev", workOrderId, operationId));
    }

    private static ApplicationDbContext CreateDbContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-service-token");
        return client;
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var databaseName = $"quality-first-article-http-{Guid.NewGuid():N}";
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:PostgreSQL"] = "Host=unused;Database=nerv_iip_quality_first_article_http;Username=nerv;Password=nerv",
                        ["InternalService:BearerToken"] = "test-internal-service-token",
                    }));
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<ApplicationDbContext>();
                    services.RemoveAll<DbContextOptions>();
                    services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                    services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
                    services.AddDbContext<ApplicationDbContext>(options => options
                        .UseInMemoryDatabase(databaseName)
                        .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
                });
            });
    }

    /// <summary>按线上 JSON 形状断言：强类型 id 在 wire 上就是裸 GUID 字符串。</summary>
    private sealed record FirstArticleConfirmationWire(
        string WorkOrderId,
        string OperationId,
        string Status,
        string? Result,
        int? AttemptNumber,
        Guid? InspectionTaskId,
        Guid? InspectionRecordId);

    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
