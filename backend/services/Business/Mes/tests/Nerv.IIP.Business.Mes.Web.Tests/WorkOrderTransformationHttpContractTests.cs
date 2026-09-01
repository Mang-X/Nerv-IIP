using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DotNetCore.CAP;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Testing;
using Nerv.IIP.Business.Mes.Web.Application.Errors;
using Savorboard.CAP.InMemoryMessageQueue;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class WorkOrderTransformationHttpContractTests
{
    [Fact]
    public async Task Transformation_endpoints_are_available_over_real_http_and_declared_in_openapi()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ISender>();
                    services.AddSingleton<ISender>(new SourceUnavailableSender());
                });
            });
        using var client = factory.CreateClient();
        await CapTestHost.WaitForCapBootstrapAsync(factory.Services);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");
        client.DefaultRequestHeaders.Add("X-Authenticated-Actor", "user:planner-001");

        var splitResponse = await client.PostAsJsonAsync(
            "/api/business/v1/mes/work-orders/WO-PARENT-001/split",
            new
            {
                organizationId = "org-001",
                environmentId = "env-dev",
                reason = "按客户批次拆分",
                idempotencyKey = "split-http-001",
                targets = new[]
                {
                    new { workOrderId = "WO-CHILD-001", quantity = 4m },
                    new { workOrderId = "WO-CHILD-002", quantity = 6m },
                },
            });

        Assert.Equal(HttpStatusCode.OK, splitResponse.StatusCode);
        Assert.Contains("\"success\":false", await splitResponse.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        using var document = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = document.RootElement.GetProperty("paths");
        AssertPathDeclaresConflict(paths, "/api/business/v1/mes/work-orders/{workOrderId}/split", "post");
        AssertPathDeclaresConflict(paths, "/api/business/v1/mes/work-orders/merge", "post");
        Assert.True(paths.TryGetProperty("/api/business/v1/mes/work-order-transformations/{transformationId}", out _));
    }

    private static void AssertPathDeclaresConflict(JsonElement paths, string path, string method)
    {
        Assert.True(paths.TryGetProperty(path, out var pathItem));
        Assert.True(pathItem.TryGetProperty(method, out var operation));
        Assert.True(operation.GetProperty("responses").TryGetProperty("409", out _));
    }

    [Fact]
    public async Task Idempotency_conflict_is_exposed_as_http_409()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ISender>();
                    services.AddSingleton<ISender>(new IdempotencyConflictSender());
                });
            });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");
        client.DefaultRequestHeaders.Add("X-Authenticated-Actor", "user:planner-001");

        var response = await client.PostAsJsonAsync(
            "/api/business/v1/mes/work-orders/WO-PARENT-001/split",
            new
            {
                organizationId = "org-001",
                environmentId = "env-dev",
                reason = "重放冲突",
                idempotencyKey = "split-http-conflict-001",
                targets = new[]
                {
                    new { workOrderId = "WO-CHILD-001", quantity = 4m },
                    new { workOrderId = "WO-CHILD-002", quantity = 6m },
                },
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("idempotency-conflict", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Real_http_split_merge_and_readback_use_the_registered_sender_and_persist_wire_json()
    {
        await using var factory = CreateSqliteFactory(out var connection);
        using var client = factory.CreateClient();
        using (var seedScope = factory.Services.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
            var dueUtc = DateTimeOffset.Parse("2026-08-27T08:00:00Z");
            dbContext.WorkOrders.AddRange(
                WorkOrder.Create("org-001", "env-dev", "WO-HTTP-SPLIT-PARENT", "SKU-HTTP", "PV-HTTP", 10m, 10, dueUtc, "PCS"),
                WorkOrder.Create("org-001", "env-dev", "WO-HTTP-MERGE-A", "SKU-HTTP", "PV-HTTP", 4m, 10, dueUtc, "PCS"),
                WorkOrder.Create("org-001", "env-dev", "WO-HTTP-MERGE-B", "SKU-HTTP", "PV-HTTP", 6m, 10, dueUtc, "PCS"));
            await dbContext.SaveChangesAsync();
        }

        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");
        client.DefaultRequestHeaders.Add("X-Authenticated-Actor", "user:planner-001");

        var splitRequest = new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            reason = "真实 HTTP 拆分",
            idempotencyKey = "split-http-real-001",
            targets = new[]
            {
                new { workOrderId = "WO-HTTP-SPLIT-CHILD-A", quantity = 4m },
                new { workOrderId = "WO-HTTP-SPLIT-CHILD-B", quantity = 6m },
            },
        };
        using var firstSplitResponse = await client.PostAsJsonAsync(
            "/api/business/v1/mes/work-orders/WO-HTTP-SPLIT-PARENT/split", splitRequest);
        var firstSplit = await ReadDataAsync(firstSplitResponse);
        var splitTransformationId = ReadStrongId(firstSplit.GetProperty("transformationId"));
        Assert.False(firstSplit.GetProperty("isIdempotentReplay").GetBoolean());
        Assert.Equal(
            ["WO-HTTP-SPLIT-CHILD-A", "WO-HTTP-SPLIT-CHILD-B"],
            firstSplit.GetProperty("targetWorkOrderIds").EnumerateArray().Select(x => x.GetString()!).ToArray());

        using var splitReplayResponse = await client.PostAsJsonAsync(
            "/api/business/v1/mes/work-orders/WO-HTTP-SPLIT-PARENT/split", splitRequest);
        var splitReplay = await ReadDataAsync(splitReplayResponse);
        Assert.True(splitReplay.GetProperty("isIdempotentReplay").GetBoolean());
        Assert.Equal(splitTransformationId, ReadStrongId(splitReplay.GetProperty("transformationId")));

        using var splitReadbackResponse = await client.GetAsync(
            $"/api/business/v1/mes/work-order-transformations/{splitTransformationId}?organizationId=org-001&environmentId=env-dev");
        var splitReadback = await ReadDataAsync(splitReadbackResponse);
        Assert.Equal(splitTransformationId, ReadStrongId(splitReadback.GetProperty("transformationId")));
        Assert.Equal("split-http-real-001", splitReadback.GetProperty("idempotencyKey").GetString());
        Assert.Equal(2, splitReadback.GetProperty("lines").GetArrayLength());

        using var splitConflictResponse = await client.PostAsJsonAsync(
            "/api/business/v1/mes/work-orders/WO-HTTP-SPLIT-PARENT/split",
            new
            {
                splitRequest.organizationId,
                splitRequest.environmentId,
                reason = "真实 HTTP 指纹冲突",
                splitRequest.idempotencyKey,
                splitRequest.targets,
            });
        Assert.Equal(HttpStatusCode.Conflict, splitConflictResponse.StatusCode);
        Assert.Contains("idempotency-conflict", await splitConflictResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var mergeRequest = new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            sourceWorkOrderIds = new[] { "WO-HTTP-MERGE-A", "WO-HTTP-MERGE-B" },
            targetWorkOrderId = "WO-HTTP-MERGE-TARGET",
            reason = "真实 HTTP 合并",
            idempotencyKey = "merge-http-real-001",
        };
        using var firstMergeResponse = await client.PostAsJsonAsync(
            "/api/business/v1/mes/work-orders/merge", mergeRequest);
        var firstMerge = await ReadDataAsync(firstMergeResponse);
        var mergeTransformationId = ReadStrongId(firstMerge.GetProperty("transformationId"));
        Assert.False(firstMerge.GetProperty("isIdempotentReplay").GetBoolean());
        Assert.Equal(["WO-HTTP-MERGE-TARGET"],
            firstMerge.GetProperty("targetWorkOrderIds").EnumerateArray().Select(x => x.GetString()!).ToArray());

        using var mergeReplayResponse = await client.PostAsJsonAsync(
            "/api/business/v1/mes/work-orders/merge", mergeRequest);
        var mergeReplay = await ReadDataAsync(mergeReplayResponse);
        Assert.True(mergeReplay.GetProperty("isIdempotentReplay").GetBoolean());
        Assert.Equal(mergeTransformationId, ReadStrongId(mergeReplay.GetProperty("transformationId")));

        using var mergeReadbackResponse = await client.GetAsync(
            $"/api/business/v1/mes/work-order-transformations/{mergeTransformationId}?organizationId=org-001&environmentId=env-dev");
        var mergeReadback = await ReadDataAsync(mergeReadbackResponse);
        Assert.Equal(mergeTransformationId, ReadStrongId(mergeReadback.GetProperty("transformationId")));
        Assert.Equal("merge-http-real-001", mergeReadback.GetProperty("idempotencyKey").GetString());
        Assert.Equal(2, mergeReadback.GetProperty("lines").GetArrayLength());

        using var assertionScope = factory.Services.CreateScope();
        var assertion = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await assertion.WorkOrderTransformations.CountAsync());
        Assert.Equal(WorkOrder.SplitStatus, (await assertion.WorkOrders.SingleAsync(
            x => x.WorkOrderIdValue == "WO-HTTP-SPLIT-PARENT")).Status);
        Assert.Equal(2, await assertion.WorkOrders.CountAsync(
            x => x.WorkOrderIdValue.StartsWith("WO-HTTP-SPLIT-CHILD-")));
        Assert.Equal(2, await assertion.WorkOrders.CountAsync(
            x => x.Status == WorkOrder.MergedStatus &&
                (x.WorkOrderIdValue == "WO-HTTP-MERGE-A" || x.WorkOrderIdValue == "WO-HTTP-MERGE-B")));
        Assert.Equal(1, await assertion.WorkOrders.CountAsync(
            x => x.WorkOrderIdValue == "WO-HTTP-MERGE-TARGET"));
    }

    private static async Task<JsonElement> ReadDataAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    private static string ReadStrongId(JsonElement element) =>
        element.GetProperty("id").GetString()!;

    private static WebApplicationFactory<Program> CreateSqliteFactory(out SqliteConnection connection)
    {
        var sqliteConnection = new SqliteConnection("Data Source=:memory:");
        sqliteConnection.Open();
        connection = sqliteConnection;
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                var settings = new Dictionary<string, string?>
                {
                    ["InternalService:BearerToken"] = "test-internal-service-token",
                    ["Messaging:Provider"] = "InMemory",
                    ["Cap:Version"] = $"test-work-order-transformation-http-{Guid.CreateVersion7():N}",
                    ["ConnectionStrings:PostgreSQL"] = "Host=unused;Database=mes-work-order-transformation-http;Username=nerv;Password=nerv",
                    ["HostOptions:BackgroundServiceExceptionBehavior"] = "Ignore",
                };
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(settings));
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<ApplicationDbContext>();
                    services.RemoveAll<DbContextOptions>();
                    services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                    services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
                    services.AddDbContext<ApplicationDbContext>(options => options
                        .UseSqlite(sqliteConnection));
                    services.AddSingleton(sqliteConnection);
                    services.AddCap(options => options.UseInMemoryMessageQueue());
                    services.Configure<HostOptions>(options =>
                        options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);
                });
            });
    }

    private sealed class SourceUnavailableSender : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            Task.FromException<TResponse>(new KnownException("WORK_ORDER_TRANSFORMATION_SOURCE_UNAVAILABLE"));

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            Task.FromException(new KnownException("WORK_ORDER_TRANSFORMATION_SOURCE_UNAVAILABLE"));

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            Task.FromException<object?>(new KnownException("WORK_ORDER_TRANSFORMATION_SOURCE_UNAVAILABLE"));

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class IdempotencyConflictSender : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            Task.FromException<TResponse>(new MesIdempotencyConflictException());

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            Task.FromException(new MesIdempotencyConflictException());

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            Task.FromException<object?>(new MesIdempotencyConflictException());

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
