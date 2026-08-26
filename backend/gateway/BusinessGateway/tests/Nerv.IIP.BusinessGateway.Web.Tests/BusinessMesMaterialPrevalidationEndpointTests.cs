using System.Net;
using System.Net.Http.Json;
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
