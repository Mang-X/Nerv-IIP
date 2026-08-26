using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Testing;
using Nerv.IIP.Business.Mes.Web.Application.Errors;

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
