using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionRecords;
using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionTasks;
using Nerv.IIP.Business.Quality.Web.Application.Commands.NonconformanceReports;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Quality.Web.Application.Queries.InspectionTasks;
using Nerv.IIP.Business.Quality.Web.Application.Queries.Spc;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Quality.Web.Tests;

internal static class PeriodicInspectionClosurePostgresScenario
{
    public static async Task RunAsync()
    {
        await using var provider = CreateProvider();
        var publisher = provider.GetRequiredService<RecordingIntegrationEventPublisher>();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();

        using (var scope = provider.CreateScope())
        {
            var services = scope.ServiceProvider;
            var dbContext = services.GetRequiredService<ApplicationDbContext>();
            QualityPostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
            await dbContext.Database.MigrateAsync();
            dbContext.InspectionPlans.AddRange(
                NewPeriodicPlan(
                    "IQP-PERIODIC-TIME-001",
                    timeIntervalHours: 2m,
                    quantityInterval: null,
                    assignedInspectorUserId: "inspector-001",
                    assignedTeamId: null),
                NewPeriodicPlan(
                    "IQP-PERIODIC-QUANTITY-001",
                    timeIntervalHours: null,
                    quantityInterval: 100m,
                    assignedInspectorUserId: null,
                    assignedTeamId: "team-quality-001"));
            await dbContext.SaveChangesAsync();

            var coordinator = services.GetRequiredService<IPeriodicInspectionOperationScopeCoordinator>();
            var releaseHandler = new WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts(
                dbContext,
                coordinator,
                deadLetters);
            var reportHandler = new ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection(
                dbContext,
                coordinator,
                deadLetters);
            var release = WorkOrderReleased();
            var report = ProductionReport();
            await releaseHandler.HandleAsync(release, CancellationToken.None);
            await reportHandler.HandleAsync(report, CancellationToken.None);

            var sender = services.GetRequiredService<ISender>();
            var timeContextId = await dbContext.PeriodicInspectionRuntimeContexts
                .Where(x => x.TimeIntervalHours != null)
                .Select(x => x.Id)
                .SingleAsync();
            var generated = await sender.Send(
                new GeneratePeriodicInspectionTimeTaskForContextCommand(
                    "org-001",
                    "env-dev",
                    "WO-001",
                    "OP-001",
                    timeContextId,
                    DateTimeOffset.Parse("2026-08-24T03:30:00Z").UtcDateTime,
                    24),
                CancellationToken.None);
            Assert.Equal(1, generated);

            var selfTasks = await sender.Send(
                new ListInspectionTasksQuery(
                    "org-001",
                    "env-dev",
                    "pending",
                    null,
                    0,
                    20,
                    ScopeKind: "self",
                    PrincipalId: "inspector-001",
                    AuthorizedTeamIds: ["team-quality-001"]),
                CancellationToken.None);
            var timeTask = Assert.Single(selfTasks.Items);
            Assert.Contains("claim", timeTask.AllowedActions);

            var teamTasks = await sender.Send(
                new ListInspectionTasksQuery(
                    "org-001",
                    "env-dev",
                    "pending",
                    null,
                    0,
                    20,
                    ScopeKind: "team",
                    PrincipalId: "inspector-001",
                    AuthorizedTeamIds: ["team-quality-001"]),
                CancellationToken.None);
            Assert.Equal(2, teamTasks.Items.Count);
            Assert.DoesNotContain(teamTasks.Items, task => task.InspectionTaskId == timeTask.InspectionTaskId);

            var timeClaim = new ClaimInspectionTaskCommand(
                timeTask.InspectionTaskId,
                "org-001",
                "env-dev",
                "inspector-001",
                [],
                "claim-periodic-time-001",
                timeTask.Version);
            var claimedTime = await sender.Send(timeClaim, CancellationToken.None);
            var replayedTimeClaim = await sender.Send(timeClaim, CancellationToken.None);
            Assert.Equal(claimedTime, replayedTimeClaim);

            foreach (var teamTask in teamTasks.Items)
            {
                var claim = new ClaimInspectionTaskCommand(
                    teamTask.InspectionTaskId,
                    "org-001",
                    "env-dev",
                    "inspector-001",
                    ["team-quality-001"],
                    $"claim-{teamTask.InspectionTaskId}",
                    teamTask.Version);
                var claimed = await sender.Send(claim, CancellationToken.None);
                var replayed = await sender.Send(claim, CancellationToken.None);
                Assert.Equal(claimed, replayed);
            }

            var orderedTeamTasks = teamTasks.Items.OrderBy(task => task.Quantity).ToArray();
            var submissions = new[]
            {
                new CreateInspectionRecordFromTaskCommand(
                    timeTask.InspectionTaskId,
                    "inspector-001",
                    [
                        new InspectionResultLineCommandInput("length", "10.2", "mm", "passed", null, null, [], 10.2m),
                        new InspectionResultLineCommandInput("appearance", "ok", null, "passed", null, null, []),
                    ],
                    null,
                    [],
                    "submit-periodic-time-001",
                    "org-001",
                    "env-dev"),
                new CreateInspectionRecordFromTaskCommand(
                    orderedTeamTasks[0].InspectionTaskId,
                    "inspector-001",
                    [
                        new InspectionResultLineCommandInput("length", "10.4", "mm", "passed", null, null, [], 10.4m),
                        new InspectionResultLineCommandInput("appearance", "ok", null, "passed", null, null, []),
                    ],
                    null,
                    [],
                    "submit-periodic-quantity-001",
                    "org-001",
                    "env-dev"),
                new CreateInspectionRecordFromTaskCommand(
                    orderedTeamTasks[1].InspectionTaskId,
                    "inspector-001",
                    [new InspectionResultLineCommandInput("appearance", "ok", null, "passed", null, null, [])],
                    null,
                    [],
                    "submit-periodic-quantity-002",
                    "org-001",
                    "env-dev"),
            };

            foreach (var submission in submissions)
            {
                var result = await sender.Send(submission, CancellationToken.None);
                var replay = await sender.Send(submission, CancellationToken.None);
                Assert.Equal(result, replay);
            }

            var resultEvents = publisher.Published.OfType<InspectionResultIntegrationEvent>().ToArray();
            Assert.Equal(3, resultEvents.Length);
            var spcHandler = new InspectionResultIntegrationEventHandlerForEvaluateSpc(dbContext, sender, deadLetters);
            foreach (var resultEvent in resultEvents)
            {
                await spcHandler.HandleAsync(resultEvent, CancellationToken.None);
                await spcHandler.HandleAsync(resultEvent, CancellationToken.None);
            }

            await releaseHandler.HandleAsync(release, CancellationToken.None);
            await reportHandler.HandleAsync(report, CancellationToken.None);
            var replayedGeneration = await sender.Send(
                new GeneratePeriodicInspectionTimeTaskForContextCommand(
                    "org-001",
                    "env-dev",
                    "WO-001",
                    "OP-001",
                    timeContextId,
                    DateTimeOffset.Parse("2026-08-24T03:30:00Z").UtcDateTime,
                    24),
                CancellationToken.None);
            Assert.Equal(0, replayedGeneration);
        }

        using (var assertionScope = provider.CreateScope())
        {
            var dbContext = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(3, await dbContext.InspectionTasks.CountAsync());
            Assert.Equal(3, await dbContext.InspectionRecords.CountAsync());
            Assert.All(await dbContext.InspectionTasks.ToArrayAsync(), task => Assert.Equal("completed", task.Status));
            Assert.Equal(3, await dbContext.InspectionTaskAssignmentReceipts.CountAsync(x => x.Action == "claim"));
            Assert.Equal(3, await dbContext.CodeIdempotencyKeys.CountAsync(x => x.RuleKey == "inspection-task-submit"));

            var chart = await new QuerySpcControlChartQueryHandler(dbContext).Handle(
                new QuerySpcControlChartQuery(
                    "org-001",
                    "env-dev",
                    "SKU-FG-1000",
                    "length",
                    "WC-001",
                    SubgroupSize: 2,
                    Take: 20),
                CancellationToken.None);
            Assert.Equal([10.2m, 10.4m], chart.DataPoints.Select(point => point.MeasuredValue));
            Assert.Single(chart.Subgroups);
            Assert.Equal(
                3,
                await dbContext.InspectionRecords
                    .SelectMany(record => record.ResultLines)
                    .CountAsync(line => line.CharacteristicCode == "appearance"));
            Assert.Equal(
                0,
                await dbContext.InspectionRecords
                    .SelectMany(record => record.ResultLines)
                    .CountAsync(line => line.CharacteristicCode == "appearance" && line.MeasuredValue != null));
        }

        Assert.Empty(await deadLetters.ListAsync(null, null, CancellationToken.None));
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly)
                .AddUnitOfWorkBehaviors());
        services.AddQualityPostgreSqlPersistence(QualityPostgresLaneDatabase.ConnectionString);
        services.AddIntegrationEvents(typeof(Program));
        services.AddScoped<INonconformanceReportCodeGenerator, NonconformanceReportCodeGenerator>();
        services.AddSingleton<IQualityIntegrationEventContextAccessor, FixedQualityIntegrationEventContextAccessor>();
        services.AddSingleton<RecordingIntegrationEventPublisher>();
        services.AddSingleton<IIntegrationEventPublisher>(serviceProvider =>
            serviceProvider.GetRequiredService<RecordingIntegrationEventPublisher>());
        return services.BuildServiceProvider();
    }

    private static InspectionPlan NewPeriodicPlan(
        string planCode,
        decimal? timeIntervalHours,
        decimal? quantityInterval,
        string? assignedInspectorUserId,
        string? assignedTeamId)
    {
        var plan = InspectionPlan.Create(
            "org-001",
            "env-dev",
            planCode,
            "operation",
            "SKU-FG-1000",
            null,
            "WC-001",
            null,
            "mes-operation",
            timeIntervalHours,
            quantityInterval,
            assignedInspectorUserId,
            assignedTeamId);
        plan.AddCharacteristic(
            "length",
            "Length",
            "caliper",
            "major",
            timeIntervalHours.HasValue,
            "subgroup-2",
            InspectionCharacteristicTypes.Variable,
            10m,
            9m,
            11m,
            "mm",
            null);
        plan.AddCharacteristic("appearance", "Appearance", "visual", "critical", true, "zero-defect");
        plan.Activate();
        return plan;
    }

    private static WorkOrderReleasedIntegrationEvent WorkOrderReleased() => new(
        "evt-release-closure-001",
        MesIntegrationEventTypes.WorkOrderReleased,
        MesIntegrationEventVersions.V1,
        DateTimeOffset.Parse("2026-08-24T01:00:00Z"),
        MesIntegrationEventSources.BusinessMes,
        "corr-release-closure-001",
        "WO-001",
        "org-001",
        "env-dev",
        "system:mes",
        "mes:work-order-released:org-001:env-dev:WO-001",
        new WorkOrderReleasedPayload(
            "WO-001",
            "SKU-FG-1000",
            1000m,
            DateTimeOffset.Parse("2026-08-24T01:00:00Z"),
            [new ReleasedOperationPayload("OP-001", 10, "WC-001")]));

    private static ProductionReportRecordedIntegrationEvent ProductionReport() => new(
        "evt-report-closure-001",
        MesIntegrationEventTypes.ProductionReportRecorded,
        MesIntegrationEventVersions.V1,
        DateTimeOffset.Parse("2026-08-24T01:30:00Z"),
        MesIntegrationEventSources.BusinessMes,
        "corr-report-closure-001",
        "WO-001",
        "org-001",
        "env-dev",
        "system:mes",
        "mes:production-report-recorded:org-001:env-dev:RPT-001",
        new ProductionReportRecordedPayload(
            "RPT-001",
            "WO-001",
            "OP-001",
            "WC-001",
            null,
            200m,
            0m,
            0m,
            "EA",
            null,
            DateTimeOffset.Parse("2026-08-24T01:30:00Z"),
            false));

    private sealed class FixedQualityIntegrationEventContextAccessor : IQualityIntegrationEventContextAccessor
    {
        public QualityIntegrationEventContext GetContext() => new(
            "corr-periodic-closure-001",
            "cause-periodic-closure-001",
            "inspector-001");
    }

    private sealed class RecordingIntegrationEventPublisher : IIntegrationEventPublisher
    {
        public List<object> Published { get; } = [];

        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(
            TIntegrationEvent integrationEvent,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Published.Add(integrationEvent!);
            return Task.CompletedTask;
        }
    }

}
