using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class SchedulingProviderDegradationTests
{
    [Fact]
    public async Task MaterialReadinessProvider_AcceptsRawMesResponseForExactScope()
    {
        var factory = new StaticResponseHttpClientFactory(
            """
            {
              "workOrderId": "WO-SNAPSHOT-001",
              "readinessStatus": "Ready",
              "blockingReasons": [],
              "items": []
            }
            """);
        var provider = new HttpSchedulingMaterialReadinessProvider(
            factory,
            new TestInternalServiceTokenProvider("test-internal-token"),
            NullLogger<HttpSchedulingMaterialReadinessProvider>.Instance);

        var readiness = await provider.QueryAsync(CreateSingleOperationProblem(), CancellationToken.None);

        Assert.Empty(readiness);
        var request = Assert.Single(factory.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "/api/business/v1/mes/work-orders/WO-SNAPSHOT-001/material-readiness?organizationId=org-001&environmentId=prod",
            request.PathAndQuery);
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.Equal("test-internal-token", request.AuthorizationParameter);
    }

    [Fact]
    public async Task MaterialReadinessProvider_AcceptsSuccessfulResponseDataEnvelope()
    {
        var provider = new HttpSchedulingMaterialReadinessProvider(
            new StaticResponseHttpClientFactory(
                """
                {
                  "success": true,
                  "message": "",
                  "code": 0,
                  "data": {
                    "workOrderId": "WO-SNAPSHOT-001",
                    "readinessStatus": "Ready",
                    "blockingReasons": [],
                    "items": []
                  }
                }
                """),
            new TestInternalServiceTokenProvider("test-internal-token"),
            NullLogger<HttpSchedulingMaterialReadinessProvider>.Instance);

        var readiness = await provider.QueryAsync(CreateSingleOperationProblem(), CancellationToken.None);

        Assert.Empty(readiness);
    }

    [Fact]
    public async Task MaterialReadinessProvider_FailsClosedForUnsuccessfulResponseDataEnvelope()
    {
        var provider = new HttpSchedulingMaterialReadinessProvider(
            new StaticResponseHttpClientFactory(
                """
                {
                  "success": false,
                  "message": "MES rejected the request",
                  "code": 400,
                  "data": {
                    "workOrderId": "WO-SNAPSHOT-001",
                    "readinessStatus": "Ready",
                    "blockingReasons": [],
                    "items": []
                  }
                }
                """),
            new TestInternalServiceTokenProvider("test-internal-token"),
            NullLogger<HttpSchedulingMaterialReadinessProvider>.Instance);

        var readiness = await provider.QueryAsync(CreateSingleOperationProblem(), CancellationToken.None);

        var block = Assert.Single(readiness);
        Assert.Equal("WO-SNAPSHOT-001", block.ScopeId);
        Assert.Contains("mes.materialReadinessSourceUnavailable", block.ReasonCodes);
    }

    [Fact]
    public async Task MaterialReadinessProvider_ReturnsOpenEndedBlockForMalformedSuccessfulResponse()
    {
        var provider = new HttpSchedulingMaterialReadinessProvider(
            new StaticResponseHttpClientFactory("{ invalid-json"),
            new TestInternalServiceTokenProvider("test-internal-token"),
            NullLogger<HttpSchedulingMaterialReadinessProvider>.Instance);

        var readiness = await provider.QueryAsync(CreateSingleOperationProblem(), CancellationToken.None);

        var block = Assert.Single(readiness);
        Assert.Equal("WO-SNAPSHOT-001", block.ScopeId);
        Assert.False(block.IsReady);
        Assert.Contains("mes.materialReadinessSourceUnavailable", block.ReasonCodes);
    }

    [Fact]
    public async Task MaterialReadinessProvider_FailsClosedWhenRequiredCollectionsAreMissing()
    {
        var provider = new HttpSchedulingMaterialReadinessProvider(
            new StaticResponseHttpClientFactory(
                """
                {
                  "workOrderId": "WO-SNAPSHOT-001",
                  "readinessStatus": "Blocked"
                }
                """),
            new TestInternalServiceTokenProvider("test-internal-token"),
            NullLogger<HttpSchedulingMaterialReadinessProvider>.Instance);

        var readiness = await provider.QueryAsync(CreateSingleOperationProblem(), CancellationToken.None);

        var block = Assert.Single(readiness);
        Assert.Equal("WO-SNAPSHOT-001", block.ScopeId);
        Assert.Contains("mes.materialReadinessSourceUnavailable", block.ReasonCodes);
    }

    [Fact]
    public async Task MaterialReadinessProvider_FailsClosedWhenMesResponseTargetsAnotherWorkOrder()
    {
        var provider = new HttpSchedulingMaterialReadinessProvider(
            new StaticResponseHttpClientFactory(
                """
                {
                  "workOrderId": "WO-OTHER-001",
                  "readinessStatus": "Ready",
                  "blockingReasons": [],
                  "items": []
                }
                """),
            new TestInternalServiceTokenProvider("test-internal-token"),
            NullLogger<HttpSchedulingMaterialReadinessProvider>.Instance);

        var readiness = await provider.QueryAsync(CreateSingleOperationProblem(), CancellationToken.None);

        var block = Assert.Single(readiness);
        Assert.Equal("WO-SNAPSHOT-001", block.ScopeId);
        Assert.False(block.IsReady);
        Assert.Contains("mes.materialReadinessSourceUnavailable", block.ReasonCodes);
    }

    [Fact]
    public async Task EquipmentAvailabilityProvider_ReturnsBlockingUnknownWindowWhenSourceIsUnavailable()
    {
        var provider = new HttpSchedulingEquipmentAvailabilityProvider(
            new ThrowingHttpClientFactory(),
            null,
            NullLogger<HttpSchedulingEquipmentAvailabilityProvider>.Instance);
        var problem = CreateSingleOperationProblem();

        var response = await provider.QueryAsync(problem, CancellationToken.None);

        Assert.NotEmpty(response.Items);
        Assert.Contains(response.Items, item =>
            item.DeviceAssetId == "DEV-SNAPSHOT-01" &&
            item.WorkCenterId == "WC-SNAPSHOT" &&
            item.AvailabilityStatus == EquipmentRuntimeAvailabilityStatus.Unknown &&
            item.Severity == EquipmentRuntimeSeverity.Blocked &&
            item.StartUtc == problem.HorizonStartUtc &&
            item.EndUtc == problem.HorizonEndUtc &&
            item.ReasonCode == "equipment.availabilitySourceUnavailable");
    }

    [Fact]
    public async Task MaterialReadinessProvider_ReturnsOpenEndedBlockWhenMesSourceIsUnavailable()
    {
        var provider = new HttpSchedulingMaterialReadinessProvider(
            new ThrowingHttpClientFactory(),
            null,
            NullLogger<HttpSchedulingMaterialReadinessProvider>.Instance);
        var problem = CreateSingleOperationProblem();

        var readiness = await provider.QueryAsync(problem, CancellationToken.None);

        var block = Assert.Single(readiness);
        Assert.Equal("order", block.ScopeType);
        Assert.Equal("WO-SNAPSHOT-001", block.ScopeId);
        Assert.Null(block.MaterialReadyUtc);
        Assert.False(block.IsReady);
        Assert.Contains("mes.materialReadinessSourceUnavailable", block.ReasonCodes);
    }

    private static SchedulingProblemContract CreateSingleOperationProblem()
    {
        var shiftStart = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
        var shiftEnd = new DateTimeOffset(2026, 6, 1, 16, 0, 0, TimeSpan.Zero);

        return new SchedulingProblemContract(
            ContractVersion: 1,
            ProblemId: "problem-provider-degradation-001",
            OrganizationId: "org-001",
            EnvironmentId: "prod",
            HorizonStartUtc: shiftStart,
            HorizonEndUtc: shiftEnd,
            Orders:
            [
                new SchedulingOrderContract(
                    OrderId: "WO-SNAPSHOT-001",
                    SkuCode: "FG-SNAPSHOT",
                    Quantity: 1,
                    DueUtc: shiftEnd,
                    Priority: 1,
                    IsRush: false,
                    Operations:
                    [
                        new SchedulingOperationContract(
                            OperationId: "WO-SNAPSHOT-001-OP10",
                            OperationSequence: 10,
                            PredecessorOperationIds: [],
                            DurationMinutes: 60,
                            RequiredCapabilityCode: "CAP-SNAPSHOT",
                            EligibleResourceIds: ["DEV-SNAPSHOT-01"],
                            PrimaryResourceId: "DEV-SNAPSHOT-01",
                            EarliestStartUtc: shiftStart,
                            DueUtc: shiftEnd,
                            Priority: 1,
                            IsRush: false,
                            SplitPolicy: ScheduleSplitPolicyContract.NonSplittable,
                            MaterialReadyUtc: null,
                            QualityBlockReason: null,
                            SourceReference: "TEST:SNAPSHOT")
                    ])
            ],
            Resources:
            [
                new SchedulingResourceContract(
                    ResourceId: "DEV-SNAPSHOT-01",
                    WorkCenterId: "WC-SNAPSHOT",
                    CapabilityCodes: ["CAP-SNAPSHOT"],
                    CapacityUnits: 1,
                    CalendarId: "CAL-SNAPSHOT",
                    SortKey: "001")
            ],
            Calendars:
            [
                new SchedulingCalendarContract(
                    CalendarId: "CAL-SNAPSHOT",
                    ShiftWindows: [new SchedulingTimeWindowContract(shiftStart, shiftEnd, "day-shift")])
            ],
            UnavailabilityWindows: [],
            MaterialReadiness: [],
            QualityBlocks: [],
            LockedAssignments: []);
    }

    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new ThrowingHandler())
            {
                BaseAddress = new Uri("http://localhost")
            };
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException("source unavailable");
        }
    }

    private sealed class StaticResponseHttpClientFactory(string responseBody) : IHttpClientFactory
    {
        private readonly List<CapturedRequest> requests = [];

        public IReadOnlyCollection<CapturedRequest> Requests => requests;

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new StaticResponseHandler(responseBody, requests))
            {
                BaseAddress = new Uri("http://mes.local")
            };
        }
    }

    private sealed class StaticResponseHandler(
        string responseBody,
        ICollection<CapturedRequest> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri?.PathAndQuery ?? string.Empty,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        string PathAndQuery,
        string? AuthorizationScheme,
        string? AuthorizationParameter);

    private sealed record TestInternalServiceTokenProvider(string BearerToken) : IInternalServiceTokenProvider;
}
