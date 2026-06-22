using Microsoft.Extensions.Logging.Abstractions;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class SchedulingProviderDegradationTests
{
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
}
