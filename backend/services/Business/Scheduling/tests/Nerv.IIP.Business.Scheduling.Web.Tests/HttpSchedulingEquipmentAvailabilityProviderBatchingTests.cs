using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class HttpSchedulingEquipmentAvailabilityProviderBatchingTests
{
    private static readonly DateTimeOffset HorizonStart = new(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset HorizonEnd = new(2026, 6, 1, 16, 0, 0, TimeSpan.Zero);
    private const int MaxExpectedRequestUriLength = 4096;

    [Fact]
    public async Task QueryAsync_LargeResourceSet_BatchesDownstreamRequestsAndMergesWindows()
    {
        var handler = new CapturingAvailabilityHandler((request, clientName) =>
        {
            var deviceAssetIds = QueryList(request, "deviceAssetIds");
            var reasonCode = clientName == HttpSchedulingEquipmentAvailabilityProvider.IndustrialTelemetryClientName
                ? EquipmentRuntimeReasonCodes.ActiveAlarm
                : EquipmentRuntimeReasonCodes.MaintenanceWindow;
            var deviceAssetId = clientName == HttpSchedulingEquipmentAvailabilityProvider.IndustrialTelemetryClientName
                ? deviceAssetIds[0]
                : deviceAssetIds[^1];

            return AvailabilityResponse(deviceAssetId, reasonCode);
        });
        var provider = CreateProvider(handler);
        var problem = CreateProblem(53);

        var response = await provider.QueryAsync(problem, CancellationToken.None);

        Assert.Equal(4, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            var deviceAssetIds = QueryList(request.Request, "deviceAssetIds");
            Assert.InRange(deviceAssetIds.Length, 1, HttpSchedulingEquipmentAvailabilityProvider.MaxAvailabilityQueryIdsPerBatch);
            Assert.DoesNotContain("workCenterIds=", request.Request.RequestUri!.Query, StringComparison.Ordinal);
            Assert.True(request.Request.RequestUri.AbsoluteUri.Length < MaxExpectedRequestUriLength);
        });
        Assert.Contains(response.Items, x => x.DeviceAssetId == "DEV-001" && x.ReasonCode == EquipmentRuntimeReasonCodes.ActiveAlarm);
        Assert.Contains(response.Items, x => x.DeviceAssetId == "DEV-050" && x.ReasonCode == EquipmentRuntimeReasonCodes.MaintenanceWindow);
        Assert.Contains(response.Items, x => x.DeviceAssetId == "DEV-051" && x.ReasonCode == EquipmentRuntimeReasonCodes.ActiveAlarm);
        Assert.Contains(response.Items, x => x.DeviceAssetId == "DEV-053" && x.ReasonCode == EquipmentRuntimeReasonCodes.MaintenanceWindow);
    }

    [Fact]
    public async Task QueryAsync_SmallResourceSet_UsesSingleRequestPerSource()
    {
        var handler = new CapturingAvailabilityHandler((request, _) =>
        {
            var deviceAssetIds = QueryList(request, "deviceAssetIds");
            return AvailabilityResponse(deviceAssetIds[0], EquipmentRuntimeReasonCodes.ActiveAlarm);
        });
        var provider = CreateProvider(handler);
        var problem = CreateProblem(2);

        await provider.QueryAsync(problem, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
            Assert.Equal(["DEV-001", "DEV-002"], QueryList(request.Request, "deviceAssetIds")));
    }

    [Fact]
    public async Task QueryAsync_WhenSingleBatchFails_ReturnsFailClosedWindowsForThatBatchOnly()
    {
        var handler = new CapturingAvailabilityHandler((request, clientName) =>
        {
            var deviceAssetIds = QueryList(request, "deviceAssetIds");
            if (clientName == HttpSchedulingEquipmentAvailabilityProvider.MaintenanceClientName
                && deviceAssetIds.Contains("DEV-051", StringComparer.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.RequestUriTooLong);
            }

            return AvailabilityResponse(deviceAssetIds[0], EquipmentRuntimeReasonCodes.ActiveAlarm);
        });
        var provider = CreateProvider(handler);
        var problem = CreateProblem(51);

        var response = await provider.QueryAsync(problem, CancellationToken.None);

        Assert.Contains(response.Items, x =>
            x.DeviceAssetId == "DEV-001" &&
            x.ReasonCode == EquipmentRuntimeReasonCodes.ActiveAlarm);
        Assert.Contains(response.Items, x =>
            x.DeviceAssetId == "DEV-051" &&
            x.ReasonCode == HttpSchedulingEquipmentAvailabilityProvider.SourceUnavailableReasonCode &&
            x.SourceReferenceId == HttpSchedulingEquipmentAvailabilityProvider.MaintenanceClientName);
        Assert.DoesNotContain(response.Items, x =>
            x.DeviceAssetId == "DEV-001" &&
            x.ReasonCode == HttpSchedulingEquipmentAvailabilityProvider.SourceUnavailableReasonCode);
    }

    [Fact]
    public async Task QueryAsync_LargeResourceSet_LimitsConcurrentDownstreamRequests()
    {
        var tracker = new ConcurrentRequestTracker();
        var provider = new HttpSchedulingEquipmentAvailabilityProvider(
            new ConcurrentHttpClientFactory(tracker),
            null,
            NullLogger<HttpSchedulingEquipmentAvailabilityProvider>.Instance);
        var problem = CreateProblem(451);

        await provider.QueryAsync(problem, CancellationToken.None);

        Assert.Equal(20, tracker.TotalRequests);
        Assert.InRange(tracker.MaxInFlight, 1, HttpSchedulingEquipmentAvailabilityProvider.MaxConcurrentAvailabilityQueries);
    }

    [Fact]
    public async Task QueryAsync_WhenNoValidDeviceIds_ReturnsFailClosedWindows()
    {
        var handler = new CapturingAvailabilityHandler((_, _) => throw new InvalidOperationException("No downstream request expected."));
        var provider = CreateProvider(handler);
        var problem = CreateProblem(0) with
        {
            Resources =
            [
                new SchedulingResourceContract(
                    ResourceId: " ",
                    WorkCenterId: "WC-BLANK",
                    CapabilityCodes: ["CAP-SNAPSHOT"],
                    CapacityUnits: 1,
                    CalendarId: "CAL-SNAPSHOT",
                    SortKey: "001")
            ]
        };

        var response = await provider.QueryAsync(problem, CancellationToken.None);

        Assert.Empty(handler.Requests);
        Assert.Equal(2, response.Items.Count);
        Assert.Contains(response.Items, x =>
            x.WorkCenterId == "WC-BLANK" &&
            x.ReasonCode == HttpSchedulingEquipmentAvailabilityProvider.SourceUnavailableReasonCode &&
            x.SourceReferenceId == HttpSchedulingEquipmentAvailabilityProvider.IndustrialTelemetryClientName);
        Assert.Contains(response.Items, x =>
            x.WorkCenterId == "WC-BLANK" &&
            x.ReasonCode == HttpSchedulingEquipmentAvailabilityProvider.SourceUnavailableReasonCode &&
            x.SourceReferenceId == HttpSchedulingEquipmentAvailabilityProvider.MaintenanceClientName);
    }

    private static HttpSchedulingEquipmentAvailabilityProvider CreateProvider(CapturingAvailabilityHandler handler) =>
        new(
            new CapturingHttpClientFactory(handler),
            null,
            NullLogger<HttpSchedulingEquipmentAvailabilityProvider>.Instance);

    private static SchedulingProblemContract CreateProblem(int resourceCount)
    {
        var resources = Enumerable.Range(1, resourceCount)
            .Select(x => new SchedulingResourceContract(
                ResourceId: $"DEV-{x:000}",
                WorkCenterId: $"WC-{x:000}",
                CapabilityCodes: ["CAP-SNAPSHOT"],
                CapacityUnits: 1,
                CalendarId: "CAL-SNAPSHOT",
                SortKey: x.ToString("000")))
            .ToArray();

        return new SchedulingProblemContract(
            ContractVersion: 1,
            ProblemId: "problem-provider-batching-001",
            OrganizationId: "org-001",
            EnvironmentId: "prod",
            HorizonStartUtc: HorizonStart,
            HorizonEndUtc: HorizonEnd,
            Orders: [],
            Resources: resources,
            Calendars:
            [
                new SchedulingCalendarContract(
                    CalendarId: "CAL-SNAPSHOT",
                    ShiftWindows: [new SchedulingTimeWindowContract(HorizonStart, HorizonEnd, "day-shift")])
            ],
            UnavailabilityWindows: [],
            MaterialReadiness: [],
            QualityBlocks: [],
            LockedAssignments: []);
    }

    private static HttpResponseMessage AvailabilityResponse(string deviceAssetId, string reasonCode)
    {
        var payload = new
        {
            data = new EquipmentRuntimeAvailabilityResponse(
                ContractVersion: 1,
                OrganizationId: "org-001",
                EnvironmentId: "prod",
                QueryWindowStartUtc: HorizonStart,
                QueryWindowEndUtc: HorizonEnd,
                Items:
                [
                    new EquipmentRuntimeAvailabilityWindowContract(
                        DeviceAssetId: deviceAssetId,
                        WorkCenterId: null,
                        AvailabilityStatus: EquipmentRuntimeAvailabilityStatus.Unavailable,
                        ReasonCode: reasonCode,
                        Severity: EquipmentRuntimeSeverity.Blocked,
                        StartUtc: HorizonStart.AddHours(1),
                        EndUtc: HorizonStart.AddHours(2),
                        SourceType: EquipmentRuntimeSourceType.Alarm,
                        SourceReferenceId: $"source:{deviceAssetId}",
                        MessageKey: "equipment.test",
                        SubstituteDeviceAssetIds: [])
                ]),
            success = true,
            message = string.Empty,
            code = 0
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload, options: EquipmentRuntimeJson.Options)
        };
    }

    private static string[] QueryList(HttpRequestMessage request, string name)
    {
        var value = request.RequestUri!.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Split('=', 2))
            .Where(x => x.Length == 2)
            .Where(x => string.Equals(Uri.UnescapeDataString(x[0]), name, StringComparison.Ordinal))
            .Select(x => Uri.UnescapeDataString(x[1]))
            .FirstOrDefault();

        return (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private sealed class CapturingHttpClientFactory(CapturingAvailabilityHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            handler.ClientName = name;
            return new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = new Uri("http://localhost")
            };
        }
    }

    private sealed class CapturingAvailabilityHandler(
        Func<HttpRequestMessage, string, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        public string ClientName { get; set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(ClientName, request));
            return Task.FromResult(responseFactory(request, ClientName));
        }
    }

    private sealed record CapturedRequest(string ClientName, HttpRequestMessage Request);

    private sealed class ConcurrentHttpClientFactory(ConcurrentRequestTracker tracker) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(new ConcurrentAvailabilityHandler(tracker, name))
            {
                BaseAddress = new Uri("http://localhost")
            };
    }

    private sealed class ConcurrentAvailabilityHandler(
        ConcurrentRequestTracker tracker,
        string clientName) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            tracker.SendAsync(request, clientName, cancellationToken);
    }

    private sealed class ConcurrentRequestTracker
    {
        private int currentInFlight;
        private int maxInFlight;
        private int totalRequests;

        public int MaxInFlight => maxInFlight;

        public int TotalRequests => totalRequests;

        public async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            string clientName,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref totalRequests);
            var current = Interlocked.Increment(ref currentInFlight);
            TrackMax(current);

            try
            {
                await Task.Delay(50, cancellationToken);
                var deviceAssetId = QueryList(request, "deviceAssetIds").First();
                var reasonCode = clientName == HttpSchedulingEquipmentAvailabilityProvider.IndustrialTelemetryClientName
                    ? EquipmentRuntimeReasonCodes.ActiveAlarm
                    : EquipmentRuntimeReasonCodes.MaintenanceWindow;
                return AvailabilityResponse(deviceAssetId, reasonCode);
            }
            finally
            {
                Interlocked.Decrement(ref currentInFlight);
            }
        }

        private void TrackMax(int current)
        {
            while (true)
            {
                var snapshot = maxInFlight;
                if (current <= snapshot)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref maxInFlight, current, snapshot) == snapshot)
                {
                    return;
                }
            }
        }
    }
}
