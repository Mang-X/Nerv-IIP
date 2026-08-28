using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.Queries.InspectionTasks;
using MediatR;
using Nerv.IIP.Contracts.Quality;

namespace Nerv.IIP.Business.Quality.Web.Tests;

/// <summary>
/// #2779 首件确认读契约：波 2 的 MES 报工门禁按「工单 + 工序」取首件判定结论。
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class QualityFirstArticleConfirmationEndpointTests
{
    [Fact]
    public async Task Work_order_operation_without_first_article_task_reports_none()
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var confirmation = await GetConfirmationAsync(client, "WO-001", "OP-10");

        Assert.Equal("WO-001", confirmation.WorkOrderId);
        Assert.Equal("OP-10", confirmation.OperationId);
        Assert.Equal(QualityFirstArticleConfirmationStatuses.NotOpened, confirmation.Status);
        Assert.Null(confirmation.Result);
        Assert.Null(confirmation.InspectionTaskId);
        Assert.Null(confirmation.InspectionRecordId);
        Assert.Null(confirmation.DecidedAtUtc);
    }

    [Fact]
    public async Task Open_first_article_task_reports_pending_without_a_result()
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        InspectionTaskId taskId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var task = FirstArticleTask("WO-001", "OP-10");
            dbContext.InspectionTasks.Add(task);
            await dbContext.SaveChangesAsync();
            taskId = task.Id;
        }

        var confirmation = await GetConfirmationAsync(client, "WO-001", "OP-10");

        Assert.Equal(QualityFirstArticleConfirmationStatuses.Pending, confirmation.Status);
        Assert.Equal(taskId.Id, confirmation.InspectionTaskId);
        Assert.Null(confirmation.Result);
        Assert.Null(confirmation.InspectionRecordId);
        Assert.Null(confirmation.DecidedAtUtc);
    }

    [Theory]
    [InlineData(QualityInspectionDispositionStatuses.Passed, InspectionLineResults.Passed)]
    [InlineData(QualityInspectionDispositionStatuses.Rejected, InspectionLineResults.Failed)]
    public async Task Judged_first_article_task_reports_the_decided_result(string expectedResult, string lineResult)
    {
        await using var dbContext = CreateDbContext($"quality-first-article-{expectedResult}");
        var plan = FirstArticlePlan();
        var task = FirstArticleTask("WO-001", "OP-10", plan.Id);
        var record = InspectionRecord.Create(
            "org-001",
            "env-dev",
            plan.Id,
            "first-article",
            "mes",
            "WO-001",
            "SKU-FG-1000",
            1m,
            null,
            null,
            [new InspectionResultLineInput("appearance", "ok", null, lineResult, "surface", 1m, [])],
            "首件判定留档",
            []);
        task.Start("inspector-001", DateTimeOffset.Parse("2026-07-05T09:00:00Z"));
        task.Complete(record.Id, DateTimeOffset.Parse("2026-07-05T09:30:00Z"));
        dbContext.InspectionPlans.Add(plan);
        dbContext.InspectionRecords.Add(record);
        dbContext.InspectionTasks.Add(task);
        await dbContext.SaveChangesAsync();

        var confirmation = await new GetFirstArticleConfirmationQueryHandler(dbContext).Handle(
            new GetFirstArticleConfirmationQuery("org-001", "env-dev", "WO-001", "OP-10"),
            CancellationToken.None);

        Assert.Equal(QualityFirstArticleConfirmationStatuses.Decided, confirmation.Status);
        Assert.Equal(expectedResult, confirmation.Result);
        Assert.Equal(task.Id, confirmation.InspectionTaskId);
        Assert.Equal(record.Id, confirmation.InspectionRecordId);
        Assert.Equal(DateTimeOffset.Parse("2026-07-05T09:30:00Z"), confirmation.DecidedAtUtc);
    }

    [Fact]
    public async Task Confirmation_is_scoped_to_the_requested_work_order_operation()
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.InspectionTasks.Add(FirstArticleTask("WO-001", "OP-10"));
            await dbContext.SaveChangesAsync();
        }

        Assert.Equal(
            QualityFirstArticleConfirmationStatuses.NotOpened,
            (await GetConfirmationAsync(client, "WO-001", "OP-20")).Status);
        Assert.Equal(
            QualityFirstArticleConfirmationStatuses.NotOpened,
            (await GetConfirmationAsync(client, "WO-002", "OP-10")).Status);
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

    private static InspectionPlan FirstArticlePlan()
    {
        var plan = InspectionPlan.Create("org-001", "env-dev", "PLAN-FA-1000", "first-article", "SKU-FG-1000", null, "WC-MIX", null, null);
        plan.AddCharacteristic("appearance", "Appearance", "visual", "major", required: true, "100%");
        plan.Activate();
        return plan;
    }

    private static InspectionTask FirstArticleTask(string workOrderId, string operationId, InspectionPlanId? planId = null)
    {
        return InspectionTask.CreatePending(
            "org-001",
            "env-dev",
            planId ?? new InspectionPlanId(Guid.Parse("018f7b14-9fb0-7d9b-a7fb-78bd14f9b201")),
            "first-article",
            "mes",
            workOrderId,
            operationId,
            "SKU-FG-1000",
            1m,
            "pcs",
            null,
            null,
            DateTimeOffset.Parse("2026-07-05T08:00:00Z"),
            DateTimeOffset.Parse("2026-07-06T08:00:00Z"),
            $"quality:first-article:org-001:env-dev:{workOrderId}:{operationId}");
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

    private static ApplicationDbContext CreateDbContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

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

    /// <summary>按线上 JSON 形状断言：强类型 id 在 wire 上就是裸 GUID 字符串。</summary>
    private sealed record FirstArticleConfirmationWire(
        string WorkOrderId,
        string OperationId,
        string Status,
        string? Result,
        Guid? InspectionTaskId,
        Guid? InspectionRecordId,
        DateTimeOffset? DecidedAtUtc);

    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
}
