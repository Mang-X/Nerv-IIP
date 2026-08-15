using System.Net;
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
                    DefaultCalendarCode: "CAL-DAY",
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
            ],
            ToolingFacts:
            [
                new SchedulingProblemToolingFactSnapshot("WO-REAL-001-10-mixing", 25, ["fixture.mixer"]),
                new SchedulingProblemToolingFactSnapshot("WO-REAL-002-10-mixing", 25, ["fixture.mixer"])
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
                            RequiredToolingIds: ["caller-inline-tooling-must-be-ignored"])
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
        Assert.Equal(1, resource.CapacityUnits);
        Assert.Contains("mixing", resource.CapabilityCodes);
        Assert.Contains("skill.operator", resource.CapabilityCodes);
        var calendar = Assert.Single(problem.Calendars);
        Assert.Equal("CAL-DAY", calendar.CalendarId);
        Assert.Contains(calendar.ShiftWindows, x => x.StartUtc == HorizonStart && x.EndUtc == HorizonEnd && x.ReasonCode == "day-shift");
        var constrainedOperation = problem.Orders.Single(x => x.OrderId == "WO-REAL-001").Operations.Single();
        Assert.Equal(25, constrainedOperation.SetupMinutes);
        Assert.Equal(["skill.operator"], constrainedOperation.RequiredSkillCodes);
        Assert.Equal(["fixture.mixer"], constrainedOperation.RequiredToolingIds);

        var plan = new FiniteCapacityScheduler().Schedule(problem, "plan-real-producer-001", HorizonStart);

        Assert.Empty(plan.UnscheduledOperations);
        Assert.Equal(2, plan.Assignments.Count);
        Assert.All(plan.Assignments, scheduled => Assert.Equal("DEV-MIX-01", scheduled.ResourceId));
    }

    [Fact]
    public async Task Producer_scales_operation_run_minutes_by_order_quantity()
    {
        var producer = new SchedulingProblemProducer(
            new StubSchedulingProblemProductEngineeringClient(
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
                            SetupMinutes: 11,
                            RunMinutes: 7,
                            TeardownMinutes: 3)
                    ])),
            MasterDataForWorkCenter(
                new SchedulingProblemWorkCenterSnapshot(
                    Code: "WC-MIX-01",
                    DefaultCalendarCode: "CAL-DAY",
                    NumberOfCapacities: 1,
                    CapabilityCodes: ["mixing"])));
        var request = RequestFor(
            new SchedulingProblemSourceOrder(
                OrderId: "WO-QTY-001",
                SkuCode: "SKU-FG-1000",
                Quantity: 5,
                DueUtc: HorizonEnd,
                Priority: 10,
                IsRush: false,
                EarliestStartUtc: HorizonStart,
                RoutingVersionId: "ROUTE-MIX:A"));

        var problem = await producer.AssembleAsync(request, CancellationToken.None);

        var operation = problem.Orders.Single().Operations.Single();
        Assert.Equal(11, operation.SetupMinutes);
        Assert.Equal(38, operation.DurationMinutes);
    }

    [Fact]
    public async Task Producer_uses_one_capacity_unit_per_device_resource()
    {
        var producer = new SchedulingProblemProducer(
            new StubSchedulingProblemProductEngineeringClient(
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
                            SetupMinutes: 0,
                            RunMinutes: 30,
                            TeardownMinutes: 0)
                    ])),
            MasterDataForWorkCenter(
                new SchedulingProblemWorkCenterSnapshot(
                    Code: "WC-MIX-01",
                    DefaultCalendarCode: "CAL-DAY",
                    NumberOfCapacities: 3,
                    CapabilityCodes: ["mixing"]),
                [
                    new SchedulingProblemDeviceAssetSnapshot("DEV-MIX-01", "WC-MIX-01"),
                    new SchedulingProblemDeviceAssetSnapshot("DEV-MIX-02", "WC-MIX-01"),
                    new SchedulingProblemDeviceAssetSnapshot("DEV-MIX-03", "WC-MIX-01")
                ]));
        var request = RequestFor(
            new SchedulingProblemSourceOrder(
                OrderId: "WO-DEV-001",
                SkuCode: "SKU-FG-1000",
                Quantity: 1,
                DueUtc: HorizonEnd,
                Priority: 10,
                IsRush: false,
                EarliestStartUtc: HorizonStart,
                RoutingVersionId: "ROUTE-MIX:A"));

        var problem = await producer.AssembleAsync(request, CancellationToken.None);

        Assert.Equal(["DEV-MIX-01", "DEV-MIX-02", "DEV-MIX-03"], problem.Resources.Select(x => x.ResourceId).ToArray());
        Assert.All(problem.Resources, x => Assert.Equal(1, x.CapacityUnits));
    }

    [Fact]
    public async Task Master_data_client_preserves_shift_boundaries_when_paid_minutes_is_net_capacity()
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.EndsWith("/api/business/v1/master-data/resources/work-calendar/CAL-DAY", StringComparison.Ordinal))
            {
                return JsonResponse("""
                    {
                      "data": {
                        "resourceType": "work-calendar",
                        "code": "CAL-DAY",
                        "displayName": "Day calendar",
                        "active": true,
                        "snapshotVersion": "1",
                        "organizationId": "org-001",
                        "environmentId": "env-dev",
                        "workingTimes": [{ "dayOfWeek": "monday" }],
                        "holidays": [],
                        "exceptions": []
                      },
                      "success": true,
                      "message": "",
                      "code": 0
                    }
                    """);
            }

            if (path.EndsWith("/api/business/v1/master-data/resources", StringComparison.Ordinal))
            {
                return JsonResponse("""
                    {
                      "data": {
                        "resources": [
                          {
                            "resourceType": "shift",
                            "code": "DAY",
                            "displayName": "Day shift",
                            "active": true,
                            "snapshotVersion": "1"
                          }
                        ],
                        "total": 1,
                        "truncated": false
                      },
                      "success": true,
                      "message": "",
                      "code": 0
                    }
                    """);
            }

            if (path.EndsWith("/api/business/v1/master-data/resources/shift/DAY", StringComparison.Ordinal))
            {
                return JsonResponse("""
                    {
                      "data": {
                        "resourceType": "shift",
                        "code": "DAY",
                        "displayName": "Day shift",
                        "active": true,
                        "snapshotVersion": "1",
                        "organizationId": "org-001",
                        "environmentId": "env-dev",
                        "startsAt": "08:00:00",
                        "endsAt": "16:00:00",
                        "paidMinutes": 420
                      },
                      "success": true,
                      "message": "",
                      "code": 0
                    }
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri("http://masterdata")
        };
        var client = new HttpSchedulingProblemMasterDataClient(httpClient);

        var calendar = await client.GetCalendarAsync("org-001", "env-dev", "CAL-DAY", HorizonStart, HorizonEnd, CancellationToken.None);

        var window = Assert.Single(calendar.Shifts);
        Assert.Equal(HorizonStart, window.StartUtc);
        Assert.Equal(HorizonEnd, window.EndUtc);
    }

    /// <summary>
    /// #1320 黄金向量:同一台设备在两侧必须解析到同一个键。
    /// MasterData 读面同时给业务编码 <c>code</c> 与聚合主键 <c>deviceAssetId</c>(GUID),
    /// 而 IIoT / 维护世界只认业务编码。排程资源必须落在业务编码上,否则可用性查询一台都对不上,
    /// 全部回落成「状态未知」。
    /// </summary>
    [Fact]
    public async Task Master_data_client_uses_device_business_code_as_scheduling_resource_id()
    {
        // IIoT 侧对同一台设备持有的键(DeviceStateSnapshot.DeviceAssetId / AlarmEvent.DeviceAssetId)。
        const string IiotDeviceAssetId = "DEV-MIX-01";
        var httpClient = new HttpClient(new StubHttpMessageHandler(_ => JsonResponse("""
            {
              "data": {
                "resources": [
                  {
                    "resourceType": "device-asset",
                    "code": "DEV-MIX-01",
                    "displayName": "Mixer 01",
                    "active": true,
                    "snapshotVersion": "1",
                    "workCenterCode": "WC-MIX-01",
                    "deviceAssetId": "0198f0aa-1111-7000-8000-000000000001"
                  }
                ],
                "total": 1,
                "truncated": false
              },
              "success": true,
              "message": "",
              "code": 0
            }
            """)))
        {
            BaseAddress = new Uri("http://master-data")
        };
        var client = new HttpSchedulingProblemMasterDataClient(httpClient);

        var devices = await client.ListDeviceAssetsAsync("org-001", "env-dev", "WC-MIX-01", CancellationToken.None);

        var device = Assert.Single(devices);
        Assert.Equal(IiotDeviceAssetId, device.ResourceId);
        Assert.False(Guid.TryParse(device.ResourceId, out _), "排程资源标识不能是 MasterData 的聚合主键 GUID。");
    }

    [Theory]
    // 有业务编码时永远用业务编码,哪怕主数据同时给了 GUID 主键。
    [InlineData("DEV-CNC-01", "0198f0aa-1111-7000-8000-000000000001", "DEV-CNC-01")]
    [InlineData("  DEV-CNC-02  ", null, "DEV-CNC-02")]
    // 编码缺失才兜底用 GUID:宁可给一个对不上的键,也不产出空标识把整台设备从问题里抹掉。
    [InlineData("", "0198f0aa-1111-7000-8000-000000000002", "0198f0aa-1111-7000-8000-000000000002")]
    [InlineData(null, null, "")]
    public void Device_asset_key_prefers_the_business_code_shared_by_both_sides(
        string? code,
        string? deviceAssetId,
        string expected)
    {
        Assert.Equal(expected, SchedulingDeviceAssetKey.Resolve(code, deviceAssetId));
    }

    [Fact]
    public async Task Master_data_client_reads_authoritative_tooling_facts_over_internal_http_contract()
    {
        HttpRequestMessage? captured = null;
        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            captured = request;
            return JsonResponse("""
                { "data": { "facts": [
                  { "operationId": "WO-2-10-mixing", "setupMinutes": 35, "requiredToolingCodes": ["TOOL-00001"], "toolingAvailable": true }
                ] }, "success": true, "message": "", "code": 0 }
                """);
        })) { BaseAddress = new Uri("http://master-data") };
        var client = new HttpSchedulingProblemMasterDataClient(httpClient);

        var facts = await client.ResolveToolingFactsAsync("org-001", "env-dev",
            [new SchedulingProblemToolingTransitionSnapshot("WO-2-10-mixing", "WC-01", "SKU-A", null, "SKU-B")], CancellationToken.None);

        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("/api/business/v1/master-data/scheduling-tooling-facts/resolve", captured.RequestUri!.AbsolutePath);
        var fact = Assert.Single(facts);
        Assert.Equal(35, fact.SetupMinutes);
        Assert.Equal(["TOOL-00001"], fact.RequiredToolingCodes);
    }

    private static AssembleSchedulingProblemRequest RequestFor(params SchedulingProblemSourceOrder[] orders)
    {
        return new AssembleSchedulingProblemRequest(
            ProblemId: "problem-review-follow-up",
            OrganizationId: "org-001",
            EnvironmentId: "env-dev",
            HorizonStartUtc: HorizonStart,
            HorizonEndUtc: HorizonEnd,
            Orders: orders);
    }

    private static StubSchedulingProblemMasterDataClient MasterDataForWorkCenter(
        SchedulingProblemWorkCenterSnapshot workCenter,
        IReadOnlyCollection<SchedulingProblemDeviceAssetSnapshot>? deviceAssets = null)
    {
        return new StubSchedulingProblemMasterDataClient(
            WorkCenters: [workCenter],
            Calendars:
            [
                new SchedulingProblemCalendarSnapshot(
                    Code: "CAL-DAY",
                    Shifts:
                    [
                        new SchedulingProblemShiftWindowSnapshot(HorizonStart, HorizonEnd, "day-shift")
                    ])
            ],
            DeviceAssets: deviceAssets ?? []);
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
        IReadOnlyCollection<SchedulingProblemDeviceAssetSnapshot> DeviceAssets,
        IReadOnlyCollection<SchedulingProblemToolingFactSnapshot>? ToolingFacts = null)
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

        public Task<IReadOnlyCollection<SchedulingProblemToolingFactSnapshot>> ResolveToolingFactsAsync(
            string organizationId,
            string environmentId,
            IReadOnlyCollection<SchedulingProblemToolingTransitionSnapshot> transitions,
            CancellationToken cancellationToken) => Task.FromResult(ToolingFacts ?? (IReadOnlyCollection<SchedulingProblemToolingFactSnapshot>)[]);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(handler(request));
        }
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }
}
