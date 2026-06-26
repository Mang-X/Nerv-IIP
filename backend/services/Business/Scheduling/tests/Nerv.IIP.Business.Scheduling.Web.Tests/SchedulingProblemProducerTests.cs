using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class SchedulingProblemProducerTests
{
    private static readonly DateTimeOffset HorizonStart = new(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset HorizonEnd = new(2026, 6, 1, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Producer_assembles_routing_and_master_data_into_rich_constraints_consumed_by_scheduler()
    {
        var productEngineering = new StubSchedulingProblemProductEngineeringClient(
            new SchedulingProblemRoutingSnapshot(
                "ROUTE-MIX",
                "A",
                "SKU-FG-1000",
                [
                    new SchedulingProblemRoutingOperationSnapshot(
                        Sequence: 10,
                        WorkCenterCode: "WC-MIX-01",
                        OperationCode: "mixing",
                        OperationName: "Mixing",
                        SetupMinutes: 15,
                        RunMinutes: 60,
                        TeardownMinutes: 0)
                ]));
        var masterData = new StubSchedulingProblemMasterDataClient(
            WorkCenters:
            [
                new SchedulingProblemWorkCenterSnapshot(
                    Code: "WC-MIX-01",
                    CapacityMinutesPerDay: 480,
                    DefaultCalendarCode: "CAL-DAY",
                    FiniteCapacity: true,
                    NumberOfCapacities: 2,
                    CapabilityCodes: ["mixing", "skill.operator"])
            ],
            Calendars:
            [
                new SchedulingProblemCalendarSnapshot(
                    Code: "CAL-DAY",
                    Shifts:
                    [
                        new SchedulingProblemShiftWindowSnapshot(HorizonStart, HorizonEnd, "day-shift")
                    ])
            ],
            DeviceAssets:
            [
                new SchedulingProblemDeviceAssetSnapshot("DEV-MIX-01", "WC-MIX-01")
            ]);
        var producer = new SchedulingProblemProducer(productEngineering, masterData);
        var request = new AssembleSchedulingProblemRequest(
            ProblemId: "problem-real-producer-001",
            OrganizationId: "org-001",
            EnvironmentId: "env-dev",
            HorizonStartUtc: HorizonStart,
            HorizonEndUtc: HorizonEnd,
            Orders:
            [
                new SchedulingProblemSourceOrder(
                    OrderId: "WO-REAL-001",
                    SkuCode: "SKU-FG-1000",
                    Quantity: 1,
                    DueUtc: HorizonEnd,
                    Priority: 10,
                    IsRush: false,
                    EarliestStartUtc: HorizonStart,
                    RoutingVersionId: "ROUTE-MIX:A",
                    OperationConstraints:
                    [
                        new SchedulingProblemOperationConstraint(
                            OperationCode: "mixing",
                            RequiredSkillCodes: ["skill.operator"],
                            RequiredToolingIds: ["fixture.mixer"])
                    ]),
                new SchedulingProblemSourceOrder(
                    OrderId: "WO-REAL-002",
                    SkuCode: "SKU-FG-1000",
                    Quantity: 1,
                    DueUtc: HorizonEnd,
                    Priority: 20,
                    IsRush: false,
                    EarliestStartUtc: HorizonStart,
                    RoutingVersionId: "ROUTE-MIX:A")
            ]);

        var problem = await producer.AssembleAsync(request, CancellationToken.None);

        var resource = Assert.Single(problem.Resources);
        Assert.Equal("DEV-MIX-01", resource.ResourceId);
        Assert.Equal("WC-MIX-01", resource.WorkCenterId);
        Assert.Equal(2, resource.CapacityUnits);
        Assert.Contains("mixing", resource.CapabilityCodes);
        Assert.Contains("skill.operator", resource.CapabilityCodes);
        var calendar = Assert.Single(problem.Calendars);
        Assert.Equal("CAL-DAY", calendar.CalendarId);
        Assert.Contains(calendar.ShiftWindows, x => x.StartUtc == HorizonStart && x.EndUtc == HorizonEnd && x.ReasonCode == "day-shift");
        var constrainedOperation = problem.Orders.Single(x => x.OrderId == "WO-REAL-001").Operations.Single();
        Assert.Equal(15, constrainedOperation.SetupMinutes);
        Assert.Equal(["skill.operator"], constrainedOperation.RequiredSkillCodes);
        Assert.Equal(["fixture.mixer"], constrainedOperation.RequiredToolingIds);

        var plan = new FiniteCapacityScheduler().Schedule(problem, "plan-real-producer-001", HorizonStart);

        Assert.Contains(plan.UnscheduledOperations, x =>
            x.OrderId == "WO-REAL-001" &&
            x.OperationId == "WO-REAL-001-10-mixing" &&
            x.ReasonCode == ScheduleConflictReasonCodeContract.NoEligibleResource);
        var scheduled = Assert.Single(plan.Assignments, x => x.OrderId == "WO-REAL-002");
        Assert.Equal("DEV-MIX-01", scheduled.ResourceId);
        Assert.Equal(HorizonStart, scheduled.StartUtc);
    }

    private sealed class StubSchedulingProblemProductEngineeringClient(SchedulingProblemRoutingSnapshot routing)
        : ISchedulingProblemProductEngineeringClient
    {
        public Task<SchedulingProblemRoutingSnapshot> GetRoutingAsync(
            string organizationId,
            string environmentId,
            string routingVersionId,
            CancellationToken cancellationToken)
        {
            Assert.Equal("ROUTE-MIX:A", routingVersionId);
            return Task.FromResult(routing);
        }
    }

    private sealed class StubSchedulingProblemMasterDataClient(
        IReadOnlyCollection<SchedulingProblemWorkCenterSnapshot> WorkCenters,
        IReadOnlyCollection<SchedulingProblemCalendarSnapshot> Calendars,
        IReadOnlyCollection<SchedulingProblemDeviceAssetSnapshot> DeviceAssets)
        : ISchedulingProblemMasterDataClient
    {
        public Task<SchedulingProblemWorkCenterSnapshot> GetWorkCenterAsync(
            string organizationId,
            string environmentId,
            string workCenterCode,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(WorkCenters.Single(x => x.Code == workCenterCode));
        }

        public Task<SchedulingProblemCalendarSnapshot> GetCalendarAsync(
            string organizationId,
            string environmentId,
            string calendarCode,
            DateTimeOffset horizonStartUtc,
            DateTimeOffset horizonEndUtc,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Calendars.Single(x => x.Code == calendarCode));
        }

        public Task<IReadOnlyCollection<SchedulingProblemDeviceAssetSnapshot>> ListDeviceAssetsAsync(
            string organizationId,
            string environmentId,
            string workCenterCode,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyCollection<SchedulingProblemDeviceAssetSnapshot>>(
                DeviceAssets.Where(x => x.WorkCenterCode == workCenterCode).ToArray());
        }
    }
}
