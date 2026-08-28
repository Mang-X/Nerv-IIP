using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Contracts.Mes;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessMesMaterialPrevalidationClientTests
{
    [Fact]
    public async Task Client_posts_exact_strong_identifier_contract_with_internal_bearer()
    {
        var handler = new RecordingHandler();
        var client = new HttpBusinessMesMaterialPrevalidationClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://mes"),
        });

        var response = await client.PrevalidateAsync(
            "internal-token",
            "corr-001",
            new MesMaterialScanPrevalidationRequest(
                "org-001", "env-dev", "MIR-001", "WO-001", "OP-10"),
            CancellationToken.None);

        Assert.Equal(MesMaterialScanDecision.Accepted, response.Decision);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/api/business/v1/mes/material-scan-prevalidation", handler.Path);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("internal-token", handler.AuthorizationParameter);
        Assert.Equal("corr-001", handler.CorrelationId);
        using var body = JsonDocument.Parse(handler.Body);
        Assert.Equal("MIR-001", body.RootElement.GetProperty("materialIssueRequestId").GetString());
        Assert.Equal("WO-001", body.RootElement.GetProperty("workOrderId").GetString());
        Assert.Equal("OP-10", body.RootElement.GetProperty("operationTaskId").GetString());
        Assert.False(body.RootElement.TryGetProperty("inventoryBatchId", out _));
    }

    [Fact]
    public void Client_contract_requires_explicit_correlation_id()
    {
        var method = typeof(IBusinessMesMaterialPrevalidationClient).GetMethod(nameof(IBusinessMesMaterialPrevalidationClient.PrevalidateAsync));

        Assert.NotNull(method);
        Assert.Contains(method.GetParameters(), parameter =>
            parameter.ParameterType == typeof(string) && parameter.Name == "correlationId");
    }

    [Fact]
    public void Client_contract_uses_the_shared_mes_wire_assembly_for_request_and_response()
    {
        var method = Assert.Single(typeof(IBusinessMesMaterialPrevalidationClient).GetMethods());

        Assert.Equal("Nerv.IIP.Contracts.Mes", method.GetParameters()[2].ParameterType.Assembly.GetName().Name);
        Assert.Equal(
            "Nerv.IIP.Contracts.Mes",
            Assert.Single(method.ReturnType.GenericTypeArguments).Assembly.GetName().Name);
    }

    [Fact]
    public void Client_reuses_the_canonical_business_service_http_module()
    {
        var client = new HttpBusinessMesMaterialPrevalidationClient(new HttpClient(new RecordingHandler())
        {
            BaseAddress = new Uri("http://mes"),
        });

        Assert.IsAssignableFrom<BusinessServiceHttpClient>(client);
    }

    [Theory]
    [InlineData("{\"decision\":\"accepted\",\"materialIssueRequestId\":\"MIR-001\",\"workOrderId\":\"WO-001\",\"operationTaskId\":\"OP-10\",\"evaluatedAtUtc\":\"2026-08-26T08:00:00Z\"}")]
    [InlineData("{\"decision\":\"unexpected\",\"reasonCode\":\"material-scan-accepted\",\"materialIssueRequestId\":\"MIR-001\",\"workOrderId\":\"WO-001\",\"operationTaskId\":\"OP-10\",\"evaluatedAtUtc\":\"2026-08-26T08:00:00Z\"}")]
    [InlineData("{\"decision\":0,\"reasonCode\":\"material-scan-accepted\",\"materialIssueRequestId\":\"MIR-001\",\"workOrderId\":\"WO-001\",\"operationTaskId\":\"OP-10\",\"evaluatedAtUtc\":\"2026-08-26T08:00:00Z\"}")]
    [InlineData("{\"decision\":1,\"reasonCode\":\"material-scan-accepted\",\"materialIssueRequestId\":\"MIR-001\",\"workOrderId\":\"WO-001\",\"operationTaskId\":\"OP-10\",\"evaluatedAtUtc\":\"2026-08-26T08:00:00Z\"}")]
    public async Task Client_rejects_success_response_with_missing_required_fact_or_unknown_decision(string json)
    {
        var handler = new RecordingHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        });
        var client = new HttpBusinessMesMaterialPrevalidationClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://mes"),
        });

        await Assert.ThrowsAsync<BusinessServiceProxyException>(() => client.PrevalidateAsync(
            "internal-token",
            "corr-001",
            new MesMaterialScanPrevalidationRequest(
                "org-001", "env-dev", "MIR-001", "WO-001", "OP-10"),
            CancellationToken.None));
    }

    [Theory]
    [InlineData("MIR-OTHER", "WO-001", "OP-10")]
    [InlineData("MIR-001", "WO-OTHER", "OP-10")]
    [InlineData("MIR-001", "WO-001", "OP-OTHER")]
    public async Task Client_rejects_success_response_whose_strong_identifiers_do_not_echo_request(
        string materialIssueRequestId,
        string workOrderId,
        string operationTaskId)
    {
        var handler = new RecordingHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new MesMaterialScanPrevalidationResponse(
                MesMaterialScanDecision.Accepted,
                "material-scan-accepted",
                materialIssueRequestId,
                workOrderId,
                operationTaskId,
                "MAT-001",
                "LOT-001",
                "primary",
                DateTimeOffset.Parse("2026-08-26T08:00:00Z"))),
        });
        var client = new HttpBusinessMesMaterialPrevalidationClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://mes"),
        });

        await Assert.ThrowsAsync<BusinessServiceProxyException>(() => client.PrevalidateAsync(
            "internal-token",
            "corr-001",
            new MesMaterialScanPrevalidationRequest(
                "org-001", "env-dev", "MIR-001", "WO-001", "OP-10"),
            CancellationToken.None));
    }

    [Theory]
    [InlineData("{\"decision\":\"accepted\",\"reasonCode\":\"material-scan-accepted\",\"materialIssueRequestId\":\"MIR-001\",\"workOrderId\":\"WO-001\",\"operationTaskId\":\"OP-10\",\"materialLotId\":\"LOT-001\",\"materialQualification\":\"primary\",\"evaluatedAtUtc\":\"2026-08-26T08:00:00Z\"}")]
    [InlineData("{\"decision\":\"accepted\",\"reasonCode\":\"material-scan-accepted\",\"materialIssueRequestId\":\"MIR-001\",\"workOrderId\":\"WO-001\",\"operationTaskId\":\"OP-10\",\"materialId\":\"MAT-001\",\"materialQualification\":\"primary\",\"evaluatedAtUtc\":\"2026-08-26T08:00:00Z\"}")]
    [InlineData("{\"decision\":\"accepted\",\"reasonCode\":\"material-scan-accepted\",\"materialIssueRequestId\":\"MIR-001\",\"workOrderId\":\"WO-001\",\"operationTaskId\":\"OP-10\",\"materialId\":\"MAT-001\",\"materialLotId\":\"LOT-001\",\"evaluatedAtUtc\":\"2026-08-26T08:00:00Z\"}")]
    [InlineData("{\"decision\":\"accepted\",\"reasonCode\":\"material-not-required\",\"materialIssueRequestId\":\"MIR-001\",\"workOrderId\":\"WO-001\",\"operationTaskId\":\"OP-10\",\"materialId\":\"MAT-001\",\"materialLotId\":\"LOT-001\",\"materialQualification\":\"primary\",\"evaluatedAtUtc\":\"2026-08-26T08:00:00Z\"}")]
    [InlineData("{\"decision\":\"rejected\",\"reasonCode\":\"material-scan-accepted\",\"materialIssueRequestId\":\"MIR-001\",\"workOrderId\":\"WO-001\",\"operationTaskId\":\"OP-10\",\"evaluatedAtUtc\":\"2026-08-26T08:00:00Z\"}")]
    [InlineData("{\"decision\":\"rejected\",\"reasonCode\":\"material-not-required\",\"materialIssueRequestId\":\"MIR-001\",\"workOrderId\":\"WO-001\",\"operationTaskId\":\"OP-10\",\"materialQualification\":\"primary\",\"evaluatedAtUtc\":\"2026-08-26T08:00:00Z\"}")]
    [InlineData("{\"decision\":\"accepted\",\"reasonCode\":\"material-scan-accepted\",\"materialIssueRequestId\":\"MIR-001\",\"workOrderId\":\"WO-001\",\"operationTaskId\":\"OP-10\",\"materialId\":\"MAT-001\",\"materialLotId\":\"LOT-001\",\"materialQualification\":\"unexpected\",\"evaluatedAtUtc\":\"2026-08-26T08:00:00Z\"}")]
    public async Task Client_rejects_success_response_that_violates_decision_invariants(string json)
    {
        var handler = new RecordingHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        });
        var client = new HttpBusinessMesMaterialPrevalidationClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://mes"),
        });

        await Assert.ThrowsAsync<BusinessServiceProxyException>(() => client.PrevalidateAsync(
            "internal-token",
            "corr-001",
            new MesMaterialScanPrevalidationRequest(
                "org-001", "env-dev", "MIR-001", "WO-001", "OP-10"),
            CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Client_rejects_present_but_blank_reason_code(string reasonCode)
    {
        var response = RejectedResponse() with { ReasonCode = reasonCode };

        await AssertInvalidResponseAsync(response);
    }

    [Fact]
    public async Task Client_rejects_present_but_minimum_evaluation_time()
    {
        var response = AcceptedResponse() with { EvaluatedAtUtc = DateTimeOffset.MinValue };

        await AssertInvalidResponseAsync(response);
    }

    [Theory]
    [InlineData("materialId")]
    [InlineData("materialLotId")]
    [InlineData("materialQualification")]
    public async Task Client_rejects_each_invalid_accepted_required_fact_independently(string invalidFact)
    {
        var valid = AcceptedResponse();
        var response = invalidFact switch
        {
            "materialId" => valid with { MaterialId = " " },
            "materialLotId" => valid with { MaterialLotId = "" },
            "materialQualification" => valid with { MaterialQualification = "unexpected" },
            _ => throw new ArgumentOutOfRangeException(nameof(invalidFact), invalidFact, null),
        };

        await AssertInvalidResponseAsync(response);
    }

    [Fact]
    public async Task Client_allows_rejected_response_to_retain_material_identifiers_when_qualification_is_null()
    {
        var response = new MesMaterialScanPrevalidationResponse(
            MesMaterialScanDecision.Rejected,
            "material-not-required",
            "MIR-001",
            "WO-001",
            "OP-10",
            "MAT-001",
            "LOT-001",
            null,
            DateTimeOffset.Parse("2026-08-26T08:00:00Z"));
        var client = ClientReturning(response);

        var actual = await client.PrevalidateAsync(
            "internal-token",
            "corr-001",
            new MesMaterialScanPrevalidationRequest(
                "org-001", "env-dev", "MIR-001", "WO-001", "OP-10"),
            CancellationToken.None);

        Assert.Equal(response, actual);
    }

    private static async Task AssertInvalidResponseAsync(MesMaterialScanPrevalidationResponse response)
    {
        var client = ClientReturning(response);

        await Assert.ThrowsAsync<BusinessServiceProxyException>(() => client.PrevalidateAsync(
            "internal-token",
            "corr-001",
            new MesMaterialScanPrevalidationRequest(
                "org-001", "env-dev", "MIR-001", "WO-001", "OP-10"),
            CancellationToken.None));
    }

    private static HttpBusinessMesMaterialPrevalidationClient ClientReturning(
        MesMaterialScanPrevalidationResponse response) =>
        new(new HttpClient(new RecordingHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(response),
        }))
        {
            BaseAddress = new Uri("http://mes"),
        });

    private static MesMaterialScanPrevalidationResponse AcceptedResponse() =>
        new(
            MesMaterialScanDecision.Accepted,
            "material-scan-accepted",
            "MIR-001",
            "WO-001",
            "OP-10",
            "MAT-001",
            "LOT-001",
            "primary",
            DateTimeOffset.Parse("2026-08-26T08:00:00Z"));

    private static MesMaterialScanPrevalidationResponse RejectedResponse() =>
        new(
            MesMaterialScanDecision.Rejected,
            "material-not-required",
            "MIR-001",
            "WO-001",
            "OP-10",
            "MAT-001",
            "LOT-001",
            null,
            DateTimeOffset.Parse("2026-08-26T08:00:00Z"));

    private sealed class RecordingHandler(Func<HttpResponseMessage>? responseFactory = null) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string Path { get; private set; } = string.Empty;
        public string AuthorizationScheme { get; private set; } = string.Empty;
        public string AuthorizationParameter { get; private set; } = string.Empty;
        public string CorrelationId { get; private set; } = string.Empty;
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            Path = request.RequestUri?.AbsolutePath ?? string.Empty;
            AuthorizationScheme = request.Headers.Authorization?.Scheme ?? string.Empty;
            AuthorizationParameter = request.Headers.Authorization?.Parameter ?? string.Empty;
            CorrelationId = request.Headers.GetValues("X-Correlation-Id").Single();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return responseFactory?.Invoke() ?? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new MesMaterialScanPrevalidationResponse(
                    MesMaterialScanDecision.Accepted, "material-scan-accepted", "MIR-001", "WO-001", "OP-10",
                    "MAT-001", "LOT-001", "primary", DateTimeOffset.Parse("2026-08-26T08:00:00Z"))),
            };
        }
    }
}
