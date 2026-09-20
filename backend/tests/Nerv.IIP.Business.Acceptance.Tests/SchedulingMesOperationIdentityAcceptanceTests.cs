using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DotNetCore.CAP;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Mes.Web.Application.Queries.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Endpoints.Mes;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Domain.DomainEvents;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.Messaging.CAP;
using Savorboard.CAP.InMemoryMessageQueue;
using MesDbContext = Nerv.IIP.Business.Mes.Infrastructure.ApplicationDbContext;
using SchedulingDbContext = Nerv.IIP.Business.Scheduling.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

[Collection(BusinessAcceptanceCollection.Name)]
public sealed class SchedulingMesOperationIdentityAcceptanceTests
{
    private const string OrganizationId = "org-3666";
    private const string EnvironmentId = "env-3666";
    private const string WorkOrderId = "WO-3666";
    private const string OperationTaskId = "WO-3666-OP-10";
    private const string ProductionVersionId = "PV-3666";
    private static readonly DateTimeOffset HorizonStart = DateTimeOffset.Parse("2026-09-20T08:00:00Z");

    [Fact]
    public async Task Workbench_release_and_real_manual_dispatch_keep_the_mes_operation_identity_end_to_end()
    {
        await using var mesDb = new MesDbContext(
            new DbContextOptionsBuilder<MesDbContext>()
                .UseInMemoryDatabase($"scheduling-mes-identity-{Guid.CreateVersion7():N}")
                .Options,
            new NoopMediator());
        await SeedMesAsync(mesDb);
        var mesSender = new MesAcceptanceSender(mesDb);
        await using var mes = CreateMesFactory(mesSender);
        using var mesClient = mes.CreateClient();
        mesClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", "test-internal-service-token");
        using var bridge = new CountingTestServerBridgeHandler(mesClient);
        using var mesTransport = new HttpClient(bridge)
        {
            BaseAddress = new Uri("http://mes"),
            DefaultRequestHeaders =
            {
                Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-service-token"),
            },
        };
        var productEngineering = new ProductEngineeringClient();
        var sourceProvider = new HttpSchedulingWorkbenchSourceProvider(mesTransport, productEngineering);
        var producer = new SchedulingProblemProducer(productEngineering, new MasterDataClient());
        var scheduler = new FiniteCapacityScheduler();

        var firstProblem = await ResolveProblemAsync(sourceProvider, producer, "problem-3666-first");
        var firstContract = scheduler.Schedule(firstProblem, "plan-3666-first", HorizonStart.AddMinutes(-5));
        var firstAssignment = Assert.Single(firstContract.Assignments);
        var firstPlan = SchedulePlan.FromGeneratedPlan(
            OrganizationId,
            EnvironmentId,
            SchedulePlanContractMapper.ToDomainSnapshot(firstContract));
        firstPlan.ClearDomainEvents();
        firstPlan.Release(HorizonStart.AddMinutes(-4), 1);
        var release = new SchedulePlanReleasedIntegrationEventConverter(
                new FixedTimeProvider(HorizonStart.AddMinutes(-4)),
                new SchedulingContextAccessor())
            .Convert(Assert.IsType<SchedulePlanReleasedDomainEvent>(Assert.Single(firstPlan.GetDomainEvents())));

        var beforeCount = await mesDb.OperationTasks.CountAsync();
        await new SchedulePlanReleasedIntegrationEventHandlerForDispatch(
                mesDb,
                new InMemoryIntegrationEventDeadLetterStore(),
                new PostgreSqlMesScheduleReleaseScopeCoordinator(mesDb))
            .HandleAsync(release, CancellationToken.None);
        await mesDb.SaveChangesAsync();
        mesDb.ChangeTracker.Clear();

        var afterCount = await mesDb.OperationTasks.CountAsync();
        var originalTask = await mesDb.OperationTasks.SingleAsync(x => x.OperationTaskIdValue == OperationTaskId);
        Assert.True(
            afterCount == beforeCount,
            $"检测到平行任务补建：发布前 {beforeCount} 行，发布后 {afterCount} 行。");
        Assert.True(
            originalTask.SchedulePlanId == firstPlan.PlanId && originalTask.ScheduledAtUtc is not null,
            $"原任务未更新：SchedulePlanId={originalTask.SchedulePlanId ?? "<null>"}, ScheduledAtUtc={originalTask.ScheduledAtUtc:O}。");

        mesClient.DefaultRequestHeaders.Remove("X-Authenticated-Actor");
        mesClient.DefaultRequestHeaders.Add("X-Authenticated-Actor", "user:planner-3666");
        using var dispatchResponse = await mesClient.PostAsJsonAsync(
            $"/api/business/v1/mes/dispatch-tasks/{OperationTaskId}/assign",
            new
            {
                organizationId = OrganizationId,
                environmentId = EnvironmentId,
                assignedUserId = "operator-3666",
                deviceAssetId = "DEVICE-2",
                shiftId = "SHIFT-1",
                assignedAtUtc = HorizonStart.AddMinutes(-3),
            });
        var dispatchBody = await dispatchResponse.Content.ReadAsStringAsync();
        Assert.True(dispatchResponse.StatusCode == HttpStatusCode.OK, dispatchBody);
        var dispatch = Assert.IsType<MesOperationTaskManuallyDispatchedIntegrationEvent>(mesSender.LastDispatchEvent);

        await using var schedulingDb = new SchedulingDbContext(
            new DbContextOptionsBuilder<SchedulingDbContext>()
                .UseInMemoryDatabase($"scheduling-operation-override-{Guid.CreateVersion7():N}")
                .Options,
            new NoopMediator());
        await new MesOperationTaskManuallyDispatchedIntegrationEventHandlerForUpsertOverride(
                schedulingDb,
                new InMemoryIntegrationEventDeadLetterStore())
            .HandleAsync(dispatch, CancellationToken.None);

        var secondProblem = await ResolveProblemAsync(sourceProvider, producer, "problem-3666-second");
        var overlaid = await new SchedulingOperationOverrideOverlay(schedulingDb)
            .ApplyAsync(secondProblem, CancellationToken.None);
        var secondContract = scheduler.Schedule(overlaid, "plan-3666-second", HorizonStart.AddMinutes(-2));
        var locked = Assert.Single(secondContract.Assignments);
        Assert.True(
            locked.OperationId == OperationTaskId && locked.IsLocked,
            $"override 未命中：OperationId={locked.OperationId}, IsLocked={locked.IsLocked}。");
        Assert.Equal("DEVICE-2", locked.ResourceId);
        Assert.Equal(1, secondContract.Metrics.LockedOperationCount);
        Assert.Equal(0, secondContract.Metrics.OptimizableOperationCount);
        Assert.True(bridge.RequestCount >= 2, $"MES source-provider HTTP 往返不足：{bridge.RequestCount} 次。");
    }

    private static async Task<SchedulingProblemContract> ResolveProblemAsync(
        ISchedulingWorkbenchSourceProvider sourceProvider,
        ISchedulingProblemProducer producer,
        string problemId)
    {
        var sourceOrders = await sourceProvider.ResolveOrdersAsync(
            OrganizationId,
            EnvironmentId,
            HorizonStart,
            [new SchedulingWorkbenchOrderSelection(WorkOrderId, 10, false)],
            CancellationToken.None);
        return await producer.AssembleWorkbenchAsync(
            new AssembleSchedulingWorkbenchProblemRequest(
                problemId,
                OrganizationId,
                EnvironmentId,
                HorizonStart,
                HorizonStart.AddHours(8),
                sourceOrders),
            CancellationToken.None);
    }

    private static async Task SeedMesAsync(MesDbContext db)
    {
        var workOrder = WorkOrder.Create(
            OrganizationId,
            EnvironmentId,
            WorkOrderId,
            "SKU-3666",
            ProductionVersionId,
            1m,
            10,
            HorizonStart.AddHours(6),
            "PCS");
        var task = OperationTask.Queue(
            OrganizationId,
            EnvironmentId,
            WorkOrderId,
            OperationTaskId,
            10,
            "WC-001",
            [],
            HorizonStart,
            TimeSpan.FromMinutes(30),
            "SKU-3666",
            operationCode: "cutting");
        workOrder.ClearDomainEvents();
        task.ClearDomainEvents();
        db.AddRange(workOrder, task);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static WebApplicationFactory<AssignDispatchTaskEndpoint> CreateMesFactory(ISender sender) =>
        new WebApplicationFactory<AssignDispatchTaskEndpoint>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("FastEndpoints:RestrictDiscoveryToEntryAssembly", "true");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["InternalService:BearerToken"] = "test-internal-service-token",
                        ["Messaging:Provider"] = "InMemory",
                        ["Cap:Version"] = $"test-operation-identity-{Guid.CreateVersion7():N}",
                        ["ConnectionStrings:PostgreSQL"] = "Host=unused;Database=operation-identity;Username=nerv;Password=nerv",
                        ["HostOptions:BackgroundServiceExceptionBehavior"] = "Ignore",
                    }));
                builder.ConfigureServices(services =>
                {
                    services.AddCap(options => options.UseInMemoryMessageQueue());
                    services.RemoveAll<ISender>();
                    services.AddSingleton<ISender>(sender);
                    services.Configure<HostOptions>(options =>
                        options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);
                });
            });

    private sealed class MesAcceptanceSender(MesDbContext db) : ISender
    {
        public MesOperationTaskManuallyDispatchedIntegrationEvent? LastDispatchEvent { get; private set; }

        public async Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            object response = request switch
            {
                ListMesWorkOrdersQuery query =>
                    await new ListMesWorkOrdersQueryHandler(db, new FixedTimeProvider(HorizonStart))
                        .Handle(query, cancellationToken),
                AssignDispatchTaskCommand command =>
                    await AssignAsync(command, cancellationToken),
                _ => throw new NotSupportedException(request.GetType().Name),
            };
            return (TResponse)response;
        }

        private async Task<MesAcceptedResponse> AssignAsync(
            AssignDispatchTaskCommand command,
            CancellationToken cancellationToken)
        {
            var response = await new AssignDispatchTaskCommandHandler(db).Handle(command, cancellationToken);
            var task = await db.OperationTasks.SingleAsync(
                x => x.OrganizationId == command.OrganizationId &&
                    x.EnvironmentId == command.EnvironmentId &&
                    x.OperationTaskIdValue == command.OperationTaskId,
                cancellationToken);
            LastDispatchEvent = new OperationTaskManuallyDispatchedIntegrationEventConverter().Convert(
                Assert.IsType<OperationTaskManuallyDispatchedDomainEvent>(Assert.Single(task.GetDomainEvents())));
            task.ClearDomainEvents();
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return response;
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException(request.GetType().Name);

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(request.GetType().Name);

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ProductEngineeringClient : ISchedulingProblemProductEngineeringClient
    {
        public Task<SchedulingProblemProductionVersionSnapshot> GetProductionVersionRoutingAsync(
            string organizationId,
            string environmentId,
            string productionVersionId,
            CancellationToken cancellationToken) => Task.FromResult(
                new SchedulingProblemProductionVersionSnapshot(productionVersionId, "SKU-3666", "ROUTE-3666:A"));

        public Task<SchedulingProblemRoutingSnapshot> GetRoutingAsync(
            string organizationId,
            string environmentId,
            string routingVersionId,
            CancellationToken cancellationToken) => Task.FromResult(
                new SchedulingProblemRoutingSnapshot(
                    "ROUTE-3666",
                    "A",
                    "SKU-3666",
                    [new SchedulingProblemRoutingOperationSnapshot(10, "WC-001", "cutting", "Cutting", 0, 30, 0)]));
    }

    private sealed class MasterDataClient : ISchedulingProblemMasterDataClient
    {
        public Task<SchedulingProblemWorkCenterSnapshot> GetWorkCenterAsync(
            string organizationId,
            string environmentId,
            string workCenterCode,
            CancellationToken cancellationToken) => Task.FromResult(
                new SchedulingProblemWorkCenterSnapshot(workCenterCode, "CAL-001", 1, ["cutting"]));

        public Task<SchedulingProblemCalendarSnapshot> GetCalendarAsync(
            string organizationId,
            string environmentId,
            string calendarCode,
            DateTimeOffset horizonStartUtc,
            DateTimeOffset horizonEndUtc,
            CancellationToken cancellationToken) => Task.FromResult(
                new SchedulingProblemCalendarSnapshot(
                    calendarCode,
                    [new SchedulingProblemShiftWindowSnapshot(HorizonStart, HorizonStart.AddHours(8), "day-shift")]));

        public Task<IReadOnlyCollection<SchedulingProblemDeviceAssetSnapshot>> ListDeviceAssetsAsync(
            string organizationId,
            string environmentId,
            string workCenterCode,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<SchedulingProblemDeviceAssetSnapshot>>(
                [new("DEVICE-1", workCenterCode), new("DEVICE-2", workCenterCode)]);

        public Task<IReadOnlyCollection<SchedulingProblemToolingFactSnapshot>> ResolveToolingFactsAsync(
            string organizationId,
            string environmentId,
            IReadOnlyCollection<SchedulingProblemToolingTransitionSnapshot> transitions,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<SchedulingProblemToolingFactSnapshot>>([]);
    }

    private sealed class CountingTestServerBridgeHandler(HttpClient mesClient) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            using var forwarded = new HttpRequestMessage(
                request.Method,
                new Uri(mesClient.BaseAddress!, request.RequestUri!.PathAndQuery));
            foreach (var header in request.Headers)
            {
                forwarded.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return await mesClient.SendAsync(forwarded, cancellationToken);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class SchedulingContextAccessor : ISchedulingIntegrationEventContextAccessor
    {
        public SchedulingIntegrationEventContext GetContext() =>
            new("corr-3666-release", "cause-3666-release", "user:planner-3666");
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
