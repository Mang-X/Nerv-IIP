using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.MachineOverheadReconciliationAggregate;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;

namespace Nerv.IIP.Business.Erp.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class MachineOverheadInternalEndpointAuthorizationTests
{
    private const string Route = "/api/business/v1/erp/finance/work-center-machine-overhead-reconciliations";

    [Fact]
    public async Task Endpoint_distinguishes_401_authentication_from_403_scope_denial()
    {
        var sender = new CapturingSender();
        await using var factory = CreateFactory(sender);
        using var client = factory.CreateClient();
        using var forbiddenRequest = CreateRequest(
            "finance-job-a-token", "org-other", "env-trusted", "ledger:forbidden");
        using var forbiddenResponse = await client.SendAsync(forbiddenRequest);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);

        using var unauthenticatedRequest = CreateRequest(
            "not-an-authorized-token", "org-trusted", "env-trusted", "ledger:unauthenticated");
        using var unauthenticatedResponse = await client.SendAsync(unauthenticatedRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedResponse.StatusCode);

        Assert.Empty(sender.Commands);
    }

    [Fact]
    public async Task Endpoint_audits_authenticated_scope_bound_caller_and_ignores_forwarded_actor()
    {
        var sender = new CapturingSender();
        await using var factory = CreateFactory(sender);
        using var client = factory.CreateClient();

        using (var firstRequest = CreateRequest(
            "finance-job-a-token", "org-trusted", "env-trusted", "ledger:first", "user:forged"))
        using (var firstResponse = await client.SendAsync(firstRequest))
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        using (var secondRequest = CreateRequest(
            "finance-job-b-token", "org-trusted", "env-trusted", "ledger:second"))
        using (var secondResponse = await client.SendAsync(secondRequest))
            Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        Assert.Collection(sender.Commands,
            first =>
            {
                Assert.Equal("org-trusted", first.OrganizationId);
                Assert.Equal("env-trusted", first.EnvironmentId);
                Assert.Equal("internal-service:finance-job-a", first.RecordedBy);
                Assert.Equal("ledger:first", first.SourceReference);
            },
            second =>
            {
                Assert.Equal("internal-service:finance-job-b", second.RecordedBy);
                Assert.Equal("ledger:second", second.SourceReference);
            });
    }

    private static WebApplicationFactory<Program> CreateFactory(CapturingSender sender)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:PostgreSQL"] = "Host=unused;Database=unused;Username=unused;Password=unused",
                    ["InternalService:BearerToken"] = "test-general-internal-token",
                    ["Persistence:AutoMigrate"] = "false",
                    ["Erp:MachineOverheadReconciliation:AuthorizedCallers:0:Subject"] = "finance-job-a",
                    ["Erp:MachineOverheadReconciliation:AuthorizedCallers:0:BearerToken"] = "finance-job-a-token",
                    ["Erp:MachineOverheadReconciliation:AuthorizedCallers:0:OrganizationId"] = "org-trusted",
                    ["Erp:MachineOverheadReconciliation:AuthorizedCallers:0:EnvironmentId"] = "env-trusted",
                    ["Erp:MachineOverheadReconciliation:AuthorizedCallers:1:Subject"] = "finance-job-b",
                    ["Erp:MachineOverheadReconciliation:AuthorizedCallers:1:BearerToken"] = "finance-job-b-token",
                    ["Erp:MachineOverheadReconciliation:AuthorizedCallers:1:OrganizationId"] = "org-trusted",
                    ["Erp:MachineOverheadReconciliation:AuthorizedCallers:1:EnvironmentId"] = "env-trusted",
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISender>();
                services.AddSingleton<ISender>(sender);
            });
        });

    private static HttpRequestMessage CreateRequest(
        string token,
        string organizationId,
        string environmentId,
        string sourceReference,
        string? forwardedActor = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Route)
        {
            Content = JsonContent.Create(new
            {
                workCenterId = "WC-01",
                accountingPeriodCode = "2026-08",
                actualFixedOverheadAmount = 100m,
                actualVariableOverheadAmount = 20m,
                currencyCode = "CNY",
                abnormalDowntimeTicks = 0,
                abnormalDowntimeDisposition = 0,
                sourceReference,
                reason = "month end",
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Organization-Id", organizationId);
        request.Headers.Add("X-Environment-Id", environmentId);
        if (forwardedActor is not null)
            request.Headers.Add("X-Authenticated-Actor", forwardedActor);
        return request;
    }

    private sealed class CapturingSender : ISender
    {
        public List<ReconcileWorkCenterMachineOverheadCommand> Commands { get; } = [];

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Commands.Add(Assert.IsType<ReconcileWorkCenterMachineOverheadCommand>(request));
            return Task.FromResult((TResponse)(object)new WorkCenterMachineOverheadReconciliationId(Guid.CreateVersion7()));
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
