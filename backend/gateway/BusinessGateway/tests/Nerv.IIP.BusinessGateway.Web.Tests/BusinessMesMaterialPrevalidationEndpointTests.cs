using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessMesMaterialPrevalidationEndpointTests
{
    [Fact]
    public async Task Context_prevalidation_facade_preserves_the_resolved_strong_id_contract()
    {
        var mes = new RecordingContextMesClient();
        await using var lease = BusinessGatewayTestHost.Lease(
            FakeBusinessGatewayAuthorizationClient.Allowed(),
            services =>
            {
                services.RemoveAll<IBusinessMesContextPrevalidationClient>();
                services.AddSingleton<IBusinessMesContextPrevalidationClient>(mes);
                services.RemoveAll<IInternalServiceTokenProvider>();
                services.AddSingleton<IInternalServiceTokenProvider>(
                    new TestInternalServiceTokenProvider("internal-test-token"));
            });
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.PostAsJsonAsync(
            "/api/business-console/v1/mes/context-scan-prevalidation",
            new
            {
                organizationId = "org-001",
                environmentId = "env-dev",
                workOrderId = "WO-001",
                operationTaskId = "OP-10",
                objectType = "personnel",
                scannedObjectId = "worker-001",
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(mes.LastRequest);
        Assert.Equal(MesContextScanObjectType.Personnel, mes.LastRequest.ObjectType);
        Assert.Equal("worker-001", mes.LastRequest.ScannedObjectId);
        Assert.Equal("internal-test-token", mes.LastInternalToken);
        Assert.False(string.IsNullOrWhiteSpace(mes.LastCorrelationId));
    }

    [Fact]
    public async Task Context_prevalidation_facade_distinguishes_personnel_mismatch_from_qualification_source_unavailable()
    {
        await using var lease = BusinessGatewayTestHost.Lease(
            FakeBusinessGatewayAuthorizationClient.Allowed(),
            services =>
            {
                services.RemoveAll<IBusinessMesContextPrevalidationClient>();
                services.AddSingleton<IBusinessMesContextPrevalidationClient, DistinguishingContextMesClient>();
                services.RemoveAll<IInternalServiceTokenProvider>();
                services.AddSingleton<IInternalServiceTokenProvider>(
                    new TestInternalServiceTokenProvider("internal-test-token"));
            });
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var mismatch = await client.PostAsJsonAsync(
            "/api/business-console/v1/mes/context-scan-prevalidation",
            ContextRequest("worker-other"));
        var sourceUnavailable = await client.PostAsJsonAsync(
            "/api/business-console/v1/mes/context-scan-prevalidation",
            ContextRequest("worker-001"));

        Assert.Equal(HttpStatusCode.OK, mismatch.StatusCode);
        using var mismatchBody = JsonDocument.Parse(await mismatch.Content.ReadAsStringAsync());
        Assert.Equal(
            "personnel-mismatch",
            mismatchBody.RootElement.GetProperty("data").GetProperty("reasonCode").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, sourceUnavailable.StatusCode);
        var sourceUnavailableBody = await sourceUnavailable.Content.ReadAsStringAsync();
        Assert.Contains("WORKER_SKILL_SOURCE_UNAVAILABLE", sourceUnavailableBody, StringComparison.Ordinal);
        Assert.DoesNotContain("personnel-mismatch", sourceUnavailableBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generated_correlation_is_identical_in_response_log_scope_and_mes_call()
    {
        var loggerProvider = new ScopeRecordingLoggerProvider();
        var mes = new ScopeRecordingMesClient(loggerProvider);
        await using var lease = BusinessGatewayTestHost.Lease(
            FakeBusinessGatewayAuthorizationClient.Allowed(),
            services =>
            {
                services.RemoveAll<IBusinessMesMaterialPrevalidationClient>();
                services.AddSingleton<IBusinessMesMaterialPrevalidationClient>(mes);
                services.RemoveAll<IInternalServiceTokenProvider>();
                services.AddSingleton<IInternalServiceTokenProvider>(
                    new TestInternalServiceTokenProvider("internal-test-token"));
                services.AddSingleton<ILoggerProvider>(loggerProvider);
            });
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.PostAsJsonAsync(
            "/api/business-console/v1/mes/material-scan-prevalidation",
            new
            {
                organizationId = "org-001",
                environmentId = "env-dev",
                materialIssueRequestId = "MIR-001",
                workOrderId = "WO-001",
                operationTaskId = "OP-10",
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Correlation-Id", out var values));
        var responseCorrelationId = Assert.Single(values);
        Assert.Equal(responseCorrelationId, mes.LastCorrelationId);
        Assert.Equal(responseCorrelationId, mes.LastScopeCorrelationId);
    }

    private sealed class ScopeRecordingMesClient(ScopeRecordingLoggerProvider loggerProvider)
        : IBusinessMesMaterialPrevalidationClient
    {
        public string? LastCorrelationId { get; private set; }
        public string? LastScopeCorrelationId { get; private set; }

        public Task<MesMaterialScanPrevalidationResponse> PrevalidateAsync(
            string internalBearerToken,
            string correlationId,
            MesMaterialScanPrevalidationRequest request,
            CancellationToken cancellationToken)
        {
            _ = internalBearerToken;
            _ = cancellationToken;
            LastCorrelationId = correlationId;
            loggerProvider.ScopeProvider.ForEachScope((scope, _) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object>> values)
                {
                    LastScopeCorrelationId = values.SingleOrDefault(x => x.Key == "correlationId").Value?.ToString()
                        ?? LastScopeCorrelationId;
                }
            }, state: 0);

            return Task.FromResult(new MesMaterialScanPrevalidationResponse(
                MesMaterialScanDecision.Accepted,
                "material-scan-accepted",
                request.MaterialIssueRequestId,
                request.WorkOrderId,
                request.OperationTaskId,
                "MAT-001",
                "LOT-001",
                "primary",
                DateTimeOffset.Parse("2026-08-26T08:00:00Z")));
        }
    }

    private sealed class RecordingContextMesClient : IBusinessMesContextPrevalidationClient
    {
        public MesContextScanPrevalidationRequest? LastRequest { get; private set; }
        public string? LastInternalToken { get; private set; }
        public string? LastCorrelationId { get; private set; }

        public Task<MesContextScanPrevalidationResponse> PrevalidateAsync(
            string internalBearerToken,
            string correlationId,
            MesContextScanPrevalidationRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastInternalToken = internalBearerToken;
            LastCorrelationId = correlationId;
            return Task.FromResult(new MesContextScanPrevalidationResponse(
                MesContextScanDecision.Accepted,
                "personnel-scan-accepted",
                request.WorkOrderId,
                request.OperationTaskId,
                request.ObjectType,
                request.ScannedObjectId,
                DateTimeOffset.Parse("2026-08-28T01:00:00Z")));
        }
    }

    private sealed class DistinguishingContextMesClient : IBusinessMesContextPrevalidationClient
    {
        public Task<MesContextScanPrevalidationResponse> PrevalidateAsync(
            string internalBearerToken,
            string correlationId,
            MesContextScanPrevalidationRequest request,
            CancellationToken cancellationToken)
        {
            _ = internalBearerToken;
            _ = correlationId;
            _ = cancellationToken;
            if (request.ScannedObjectId == "worker-001")
            {
                throw BusinessServiceProxyException.FromDownstreamBusinessMessage(
                    "WORKER_SKILL_SOURCE_UNAVAILABLE: MasterData 人员资格来源暂不可用。");
            }

            return Task.FromResult(new MesContextScanPrevalidationResponse(
                MesContextScanDecision.Rejected,
                "personnel-mismatch",
                request.WorkOrderId,
                request.OperationTaskId,
                request.ObjectType,
                request.ScannedObjectId,
                DateTimeOffset.Parse("2026-08-28T01:00:00Z")));
        }
    }

    private static object ContextRequest(string scannedObjectId) =>
        new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            workOrderId = "WO-001",
            operationTaskId = "OP-10",
            objectType = "personnel",
            scannedObjectId,
        };

    private sealed class ScopeRecordingLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        public IExternalScopeProvider ScopeProvider { get; private set; } = new LoggerExternalScopeProvider();

        public ILogger CreateLogger(string categoryName) => NullLogger.Instance;

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => ScopeProvider = scopeProvider;

        public void Dispose()
        {
        }
    }
}
