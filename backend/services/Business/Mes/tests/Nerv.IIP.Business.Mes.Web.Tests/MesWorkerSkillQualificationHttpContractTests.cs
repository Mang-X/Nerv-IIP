using System.Net;
using System.Net.Http.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class MesWorkerSkillQualificationHttpContractTests
{
    [Fact]
    public async Task Dispatch_http_forwards_exact_worker_and_scope_then_returns_fail_closed_envelope()
    {
        var sender = new QualificationSourceUnavailableSender();
        await using var factory = CreateFactory(sender);
        using var client = await CreateClientAsync(factory);
        client.DefaultRequestHeaders.Add("X-Authenticated-Actor", "user:dispatcher-001");

        var response = await client.PostAsJsonAsync(
            "/api/business/v1/mes/dispatch-tasks/OP-HTTP-DISPATCH/assign",
            new
            {
                organizationId = "org-001",
                environmentId = "env-dev",
                assignedUserId = "worker-001",
                assignedUserName = "操作员甲",
                assignedAtUtc = "2026-08-26T08:00:00Z",
            });

        await AssertFailClosedEnvelopeAsync(response);
        var command = Assert.IsType<AssignDispatchTaskCommand>(sender.Command);
        Assert.Equal("org-001", command.OrganizationId);
        Assert.Equal("env-dev", command.EnvironmentId);
        Assert.Equal("OP-HTTP-DISPATCH", command.OperationTaskId);
        Assert.Equal("worker-001", command.AssignedUserId);
    }

    [Fact]
    public async Task Ordinary_start_http_forwards_exact_scope_and_idempotency_then_returns_fail_closed_envelope()
    {
        var sender = new QualificationSourceUnavailableSender();
        await using var factory = CreateFactory(sender);
        using var client = await CreateClientAsync(factory);
        client.DefaultRequestHeaders.Add("X-Authenticated-Actor", "user:operator-001");

        var response = await client.PostAsJsonAsync(
            "/api/business/v1/mes/operation-tasks/OP-HTTP-START/start",
            new
            {
                organizationId = "org-001",
                environmentId = "env-dev",
                changedAtUtc = "2026-08-26T08:00:00Z",
                idempotencyKey = "start-worker-skill-http",
            });

        await AssertFailClosedEnvelopeAsync(response);
        var command = Assert.IsType<ChangeOperationTaskStateCommand>(sender.Command);
        Assert.Equal("org-001", command.OrganizationId);
        Assert.Equal("env-dev", command.EnvironmentId);
        Assert.Equal("OP-HTTP-START", command.OperationTaskId);
        Assert.Equal("start", command.Action);
        Assert.Equal("start-worker-skill-http", command.IdempotencyKey);
    }

    [Fact]
    public async Task Authorized_start_http_forwards_exact_scope_and_headers_then_returns_fail_closed_envelope()
    {
        var sender = new QualificationSourceUnavailableSender();
        await using var factory = CreateFactory(sender);
        using var client = await CreateClientAsync(factory);
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "corr-worker-skill-http");
        client.DefaultRequestHeaders.Add("X-Idempotency-Key", "authorize-worker-skill-http");

        var response = await client.PostAsJsonAsync(
            "/api/business/v1/mes/operation-tasks/OP-HTTP-AUTH/authorize-start",
            new
            {
                organizationId = "org-001",
                environmentId = "env-dev",
                approvalChainId = "approval-worker-skill-http",
                reason = "设备故障，授权跳站",
            });

        await AssertFailClosedEnvelopeAsync(response);
        var command = Assert.IsType<AuthorizeAndStartOperationTaskCommand>(sender.Command);
        Assert.Equal("org-001", command.OrganizationId);
        Assert.Equal("env-dev", command.EnvironmentId);
        Assert.Equal("OP-HTTP-AUTH", command.OperationTaskId);
        Assert.Equal("approval-worker-skill-http", command.ApprovalChainId);
        Assert.Equal("corr-worker-skill-http", command.CorrelationId);
        Assert.Equal("authorize-worker-skill-http", command.IdempotencyKey);
    }

    private static WebApplicationFactory<Program> CreateFactory(ISender sender) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ISender>();
                    services.AddSingleton(sender);
                });
            });

    private static async Task<HttpClient> CreateClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        await CapTestHost.WaitForCapBootstrapAsync(factory.Services);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");
        return client;
    }

    private static async Task AssertFailClosedEnvelopeAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":false", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WORKER_SKILL_SOURCE_UNAVAILABLE", body, StringComparison.Ordinal);
        Assert.Contains("人员资格来源暂不可用", body, StringComparison.Ordinal);
    }

    private sealed class QualificationSourceUnavailableSender : ISender
    {
        public object? Command { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Command = request;
            throw new KnownException(
                "WORKER_SKILL_SOURCE_UNAVAILABLE: MasterData 人员资格来源暂不可用。");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            Command = request;
            throw new KnownException(
                "WORKER_SKILL_SOURCE_UNAVAILABLE: MasterData 人员资格来源暂不可用。");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            Command = request;
            throw new KnownException(
                "WORKER_SKILL_SOURCE_UNAVAILABLE: MasterData 人员资格来源暂不可用。");
        }

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
