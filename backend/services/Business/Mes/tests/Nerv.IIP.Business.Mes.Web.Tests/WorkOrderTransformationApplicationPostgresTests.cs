using System.Text.Json;
using Npgsql;
using Nerv.IIP.Contracts.Mes;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.Errors;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(MesPostgresLaneDatabase.CollectionName)]
public sealed class WorkOrderTransformationApplicationPostgresTests
{
    [MesRealPostgresFact]
    public async Task MediatR_pipeline_replays_one_postgresql_idempotency_race_without_duplicate_lineage()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var saveChangesGate = new SaveChangesRaceGate();
        await using var factory = CreateFactory(saveChangesGate);
        await StartAndMigrateAsync(factory);

        var occurredAtUtc = DateTimeOffset.Parse("2026-08-26T05:00:00Z");
        await SeedAsync(factory, WorkOrder.Create(
            "org-001", "env-dev", "WO-CONCURRENT-REPLAY", "SKU-001", "PV-001", 3m, 10,
            occurredAtUtc.AddHours(4), "PCS"), plannedQuantity: 1m);

        var command = new SplitWorkOrderCommand(
            "org-001",
            "env-dev",
            "WO-CONCURRENT-REPLAY",
            [
                new("WO-CONCURRENT-REPLAY-CHILD-1", 1m),
                new("WO-CONCURRENT-REPLAY-CHILD-2", 1m),
                new("WO-CONCURRENT-REPLAY-CHILD-3", 1m),
            ],
            "并发幂等拆分",
            "split-application-postgres-race-001",
            "user:planner-001",
            occurredAtUtc);

        var outcomes = await SendConcurrentlyAsync(factory, saveChangesGate, command, command);

        Assert.All(outcomes, outcome => Assert.Null(outcome.Exception));
        var results = outcomes.Select(outcome => Assert.IsType<WorkOrderTransformationResult>(outcome.Result)).ToArray();
        Assert.Equal(1, results.Count(result => !result.IsIdempotentReplay));
        Assert.Equal(1, results.Count(result => result.IsIdempotentReplay));
        Assert.Equal(results[0].TransformationId, results[1].TransformationId);
        Assert.Equal(
            ["WO-CONCURRENT-REPLAY-CHILD-1", "WO-CONCURRENT-REPLAY-CHILD-2", "WO-CONCURRENT-REPLAY-CHILD-3"],
            results[0].TargetWorkOrderIds);

        await using var assertionScope = factory.Services.CreateAsyncScope();
        var assertion = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var source = await assertion.WorkOrders.SingleAsync(x => x.WorkOrderIdValue == "WO-CONCURRENT-REPLAY");
        Assert.Equal(1, await assertion.WorkOrderTransformations.CountAsync(
            x => x.IdempotencyKey == command.IdempotencyKey));
        Assert.Equal(3, await assertion.WorkOrderTransformations
            .Where(x => x.IdempotencyKey == command.IdempotencyKey)
            .SelectMany(x => x.Lines)
            .CountAsync());
        Assert.Equal(3, await assertion.WorkOrders.CountAsync(
            x => x.WorkOrderIdValue.StartsWith("WO-CONCURRENT-REPLAY-CHILD-")));
        Assert.Equal(OperationTaskLifecycleStatus.Cancelled,
            (await assertion.OperationTasks.SingleAsync(x => x.WorkOrderId == source.WorkOrderIdValue)).Status);
        var targetOperations = await assertion.OperationTasks.Where(x => x.WorkOrderId != source.WorkOrderIdValue).ToArrayAsync();
        Assert.Equal(3, targetOperations.Length);
        Assert.Equal(1m, targetOperations.Sum(x => x.PlannedQuantity));
        Assert.Equal([0.333333m, 0.333333m, 0.333334m], targetOperations.Select(x => x.PlannedQuantity).Order());
        Assert.All(targetOperations, operation => Assert.Equal(OperationTaskLifecycleStatus.Queued, operation.Status));
        Assert.Equal(WorkOrder.SplitStatus, source.Status);
        Assert.Equal(2, source.Version);

        // The concurrent replay commits one transformation and one outbox fact.
        var splitRow = Assert.Single(await ReadTransformationOutboxAsync());
        Assert.Contains(nameof(WorkOrderSplitIntegrationEvent), splitRow.Name, StringComparison.Ordinal);
        var split = ReadEnvelope<WorkOrderSplitIntegrationEvent>(splitRow.Content);
        Assert.Equal(results[0].TransformationId.Id, split.Payload.TransformationId);
        Assert.Equal(command.Actor, split.Actor);
        Assert.Equal(command.OccurredAtUtc, split.OccurredAtUtc);
        Assert.Equal(command.OrganizationId, split.OrganizationId);
        Assert.Equal(command.EnvironmentId, split.EnvironmentId);
        Assert.Equal(command.Reason, split.Payload.Reason);
        Assert.Equal(MesIntegrationEventTypes.WorkOrderSplit, split.EventType);
        Assert.Equal(command.Targets.Select(x => x.WorkOrderId).Order(),
            split.Payload.Lines.Select(x => x.TargetWorkOrderId).Order());
        Assert.All(split.Payload.Lines, line =>
        {
            Assert.Equal(command.SourceWorkOrderId, line.SourceWorkOrderId);
            Assert.Equal(1m, line.Quantity);
            Assert.Equal(3m, line.SourceQuantity);
            Assert.Equal(1m, line.TargetQuantity);
            Assert.Equal("PCS", line.UomCode);
        });

        var mergeCommand = new MergeWorkOrdersCommand("org-001", "env-dev",
            results[0].TargetWorkOrderIds, "WO-MERGED", "重组合并", "merge-outbox", command.Actor, occurredAtUtc.AddMinutes(1));
        await InstallTransformationOutboxFailureTriggerAsync();
        await using (var failingScope = factory.Services.CreateAsyncScope())
        {
            var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
                failingScope.ServiceProvider.GetRequiredService<ISender>().Send(mergeCommand));
            Assert.Contains("injected transformation outbox failure", exception.ToString(), StringComparison.Ordinal);
        }
        await AssertRollbackAsync(factory, mergeCommand.IdempotencyKey, mergeCommand.SourceWorkOrderIds,
            [mergeCommand.TargetWorkOrderId]);
        Assert.Single(await ReadTransformationOutboxAsync());
        await RemoveTransformationOutboxFailureTriggerAsync();

        WorkOrderTransformationResult merged;
        await using (var mergeScope = factory.Services.CreateAsyncScope())
            merged = await mergeScope.ServiceProvider.GetRequiredService<ISender>().Send(mergeCommand);
        await using (var replayScope = factory.Services.CreateAsyncScope())
        {
            var replay = await replayScope.ServiceProvider.GetRequiredService<ISender>().Send(mergeCommand);
            Assert.True(replay.IsIdempotentReplay);
            Assert.Equal(merged.TransformationId, replay.TransformationId);
        }
        var mergedRow = Assert.Single(await ReadTransformationOutboxAsync(), row =>
            row.Name.Contains(nameof(WorkOrderMergedIntegrationEvent), StringComparison.Ordinal));
        var merge = ReadEnvelope<WorkOrderMergedIntegrationEvent>(mergedRow.Content);
        Assert.Equal(merged.TransformationId.Id, merge.Payload.TransformationId);
        Assert.Equal(mergeCommand.Actor, merge.Actor);
        Assert.Equal(mergeCommand.OccurredAtUtc, merge.OccurredAtUtc);
        Assert.Equal(mergeCommand.Reason, merge.Payload.Reason);
        Assert.Equal(mergeCommand.OrganizationId, merge.OrganizationId);
        Assert.Equal(mergeCommand.EnvironmentId, merge.EnvironmentId);
        Assert.Equal(MesIntegrationEventTypes.WorkOrderMerged, merge.EventType);
        Assert.Equal(mergeCommand.SourceWorkOrderIds.Order(), merge.Payload.Lines.Select(x => x.SourceWorkOrderId).Order());
        Assert.All(merge.Payload.Lines, line =>
        {
            Assert.Equal("WO-MERGED", line.TargetWorkOrderId);
            Assert.Equal(1m, line.Quantity);
            Assert.Equal(1m, line.SourceQuantity);
            Assert.Equal(3m, line.TargetQuantity);
        });
        Assert.Equal(2, (await ReadTransformationOutboxAsync()).Length);

        var failingSplit = new SplitWorkOrderCommand("org-001", "env-dev", "WO-MERGED",
            [new("WO-ROLLBACK-1", 1m), new("WO-ROLLBACK-2", 2m)], "回滚拆分", "split-outbox-failure",
            command.Actor, occurredAtUtc.AddMinutes(2));
        await InstallTransformationOutboxFailureTriggerAsync();
        await using (var failingScope = factory.Services.CreateAsyncScope())
        {
            var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
                failingScope.ServiceProvider.GetRequiredService<ISender>().Send(failingSplit));
            Assert.Contains("injected transformation outbox failure", exception.ToString(), StringComparison.Ordinal);
        }
        await AssertRollbackAsync(factory, failingSplit.IdempotencyKey, [failingSplit.SourceWorkOrderId],
            failingSplit.Targets.Select(x => x.WorkOrderId).ToArray());
        Assert.Equal(2, (await ReadTransformationOutboxAsync()).Length);
    }

    [MesRealPostgresFact]
    public async Task MediatR_pipeline_turns_a_postgresql_source_version_race_into_one_409_without_half_success()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var saveChangesGate = new SaveChangesRaceGate();
        await using var factory = CreateFactory(saveChangesGate);
        await StartAndMigrateAsync(factory);

        var occurredAtUtc = DateTimeOffset.Parse("2026-08-26T06:00:00Z");
        await SeedAsync(factory, WorkOrder.Create(
            "org-001", "env-dev", "WO-CONCURRENT-VERSION", "SKU-001", "PV-001", 10m, 10,
            occurredAtUtc.AddHours(4), "PCS"));

        var firstCommand = new SplitWorkOrderCommand(
            "org-001",
            "env-dev",
            "WO-CONCURRENT-VERSION",
            [
                new("WO-CONCURRENT-VERSION-CHILD-A", 4m),
                new("WO-CONCURRENT-VERSION-CHILD-B", 6m),
            ],
            "并发版本拆分 A",
            "split-application-postgres-version-a",
            "user:planner-001",
            occurredAtUtc);
        var secondCommand = firstCommand with
        {
            Targets =
            [
                new("WO-CONCURRENT-VERSION-CHILD-C", 4m),
                new("WO-CONCURRENT-VERSION-CHILD-D", 6m),
            ],
            Reason = "并发版本拆分 B",
            IdempotencyKey = "split-application-postgres-version-b",
        };

        var outcomes = await SendConcurrentlyAsync(factory, saveChangesGate, firstCommand, secondCommand);

        var winner = Assert.Single(outcomes, outcome => outcome.Result is not null).Result!;
        var loser = Assert.Single(outcomes, outcome => outcome.Exception is not null);
        var conflict = Assert.IsType<MesLifecycleConflictException>(loser.Exception);
        Assert.Equal("work-order-transformation", conflict.Action);
        Assert.Equal("invalid-split", conflict.CurrentStatus);

        await using var assertionScope = factory.Services.CreateAsyncScope();
        var assertion = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var source = await assertion.WorkOrders.SingleAsync(x => x.WorkOrderIdValue == "WO-CONCURRENT-VERSION");
        Assert.Equal(1, await assertion.WorkOrderTransformations.CountAsync());
        Assert.Equal(2, await assertion.WorkOrderTransformations.SelectMany(x => x.Lines).CountAsync());
        Assert.Equal(2, await assertion.WorkOrders.CountAsync(
            x => x.WorkOrderIdValue.StartsWith("WO-CONCURRENT-VERSION-CHILD-")));
        Assert.Equal(OperationTaskLifecycleStatus.Cancelled,
            (await assertion.OperationTasks.SingleAsync(x => x.WorkOrderId == source.WorkOrderIdValue)).Status);
        var targetOperations = await assertion.OperationTasks.Where(x => x.WorkOrderId != source.WorkOrderIdValue).ToArrayAsync();
        Assert.Equal(2, targetOperations.Length);
        Assert.Equal(10m, targetOperations.Sum(x => x.PlannedQuantity));
        Assert.All(targetOperations, operation => Assert.Equal(OperationTaskLifecycleStatus.Queued, operation.Status));
        Assert.Equal(WorkOrder.SplitStatus, source.Status);
        Assert.Equal(2, source.Version);
        var firstTargetIds = firstCommand.Targets.Select(target => target.WorkOrderId).ToArray();
        var secondTargetIds = secondCommand.Targets.Select(target => target.WorkOrderId).ToArray();
        var losingTargetIds = winner.TargetWorkOrderIds.Intersect(firstTargetIds, StringComparer.Ordinal).Any()
            ? secondTargetIds
            : firstTargetIds;
        Assert.Equal(0, await assertion.WorkOrders.CountAsync(
            x => losingTargetIds.Contains(x.WorkOrderIdValue)));
        var row = Assert.Single(await ReadTransformationOutboxAsync());
        var split = ReadEnvelope<WorkOrderSplitIntegrationEvent>(row.Content);
        Assert.Equal(winner.TransformationId.Id, split.Payload.TransformationId);
        Assert.Equal(winner.TargetWorkOrderIds.Order(), split.Payload.Lines.Select(x => x.TargetWorkOrderId).Order());
    }

    private static async Task AssertRollbackAsync(WebApplicationFactory<Program> factory, string idempotencyKey,
        IReadOnlyCollection<string> sourceIds, IReadOnlyCollection<string> targetIds)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.WorkOrderTransformations.Where(x => x.IdempotencyKey == idempotencyKey).ToArrayAsync());
        Assert.Empty(await db.WorkOrders.Where(x => targetIds.Contains(x.WorkOrderIdValue)).ToArrayAsync());
        Assert.Empty(await db.OperationTasks.Where(x => targetIds.Contains(x.WorkOrderId)).ToArrayAsync());
        Assert.All(await db.WorkOrders.Where(x => sourceIds.Contains(x.WorkOrderIdValue)).ToArrayAsync(),
            source => Assert.Equal(WorkOrder.CreatedStatus, source.Status));
        Assert.All(await db.OperationTasks.Where(x => sourceIds.Contains(x.WorkOrderId)).ToArrayAsync(),
            operation => Assert.Equal(OperationTaskLifecycleStatus.Queued, operation.Status));
    }

    private static T ReadEnvelope<T>(string content)
    {
        using var document = JsonDocument.Parse(content);
        return JsonSerializer.Deserialize<T>(document.RootElement.GetProperty("Value").GetRawText())!;
    }

    private static async Task<(string Name, string Content)[]> ReadTransformationOutboxAsync()
    {
        await using var connection = new NpgsqlConnection(MesPostgresLaneDatabase.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT "Name", "Content" FROM cap.published
            WHERE "Content" LIKE '%mes.WorkOrderSplit%' OR "Content" LIKE '%mes.WorkOrderMerged%'
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(string, string)>();
        while (await reader.ReadAsync()) rows.Add((reader.GetString(0), reader.GetString(1)));
        return rows.ToArray();
    }

    private static async Task InstallTransformationOutboxFailureTriggerAsync()
    {
        await using var connection = new NpgsqlConnection(MesPostgresLaneDatabase.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE OR REPLACE FUNCTION cap.reject_transformation_outbox()
            RETURNS trigger AS $$
            BEGIN
                IF NEW."Content" LIKE '%mes.WorkOrderSplit%' OR NEW."Content" LIKE '%mes.WorkOrderMerged%' THEN
                    RAISE EXCEPTION 'injected transformation outbox failure';
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;
            CREATE TRIGGER reject_transformation_outbox
            BEFORE INSERT ON cap.published
            FOR EACH ROW EXECUTE FUNCTION cap.reject_transformation_outbox();
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task RemoveTransformationOutboxFailureTriggerAsync()
    {
        await using var connection = new NpgsqlConnection(MesPostgresLaneDatabase.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DROP TRIGGER reject_transformation_outbox ON cap.published";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<CommandOutcome[]> SendConcurrentlyAsync(
        WebApplicationFactory<Program> factory,
        SaveChangesRaceGate saveChangesGate,
        SplitWorkOrderCommand first,
        SplitWorkOrderCommand second)
    {
        await using var firstScope = factory.Services.CreateAsyncScope();
        await using var secondScope = factory.Services.CreateAsyncScope();
        var firstSender = firstScope.ServiceProvider.GetRequiredService<ISender>();
        var secondSender = secondScope.ServiceProvider.GetRequiredService<ISender>();

        saveChangesGate.Enable();
        try
        {
            var firstTask = CaptureAsync(firstSender, first);
            var secondTask = CaptureAsync(secondSender, second);
            return await Task.WhenAll(firstTask, secondTask);
        }
        finally
        {
            saveChangesGate.Release();
        }
    }

    private static async Task<CommandOutcome> CaptureAsync(ISender sender, SplitWorkOrderCommand command)
    {
        try
        {
            return new(await sender.Send(command, CancellationToken.None), null);
        }
        catch (Exception exception)
        {
            return new(null, exception);
        }
    }

    private static async Task StartAndMigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var client = factory.CreateClient();
        await CapTestHost.WaitForCapBootstrapAsync(factory.Services);
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        MesPostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
        await dbContext.Database.MigrateAsync(CancellationToken.None);
    }

    private static async Task SeedAsync(WebApplicationFactory<Program> factory, WorkOrder workOrder, decimal? plannedQuantity = null)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.Add(OperationTask.Queue(workOrder.OrganizationId, workOrder.EnvironmentId,
            workOrder.WorkOrderIdValue, "SOURCE-OP", 10, "WC-1", [], workOrder.DueUtc,
            TimeSpan.FromMinutes(20), workOrder.SkuId, workOrder.UomCode, plannedQuantity ?? workOrder.Quantity));
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static WebApplicationFactory<Program> CreateFactory(SaveChangesRaceGate saveChangesGate) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                var settings = new Dictionary<string, string?>
                {
                    ["ConnectionStrings:PostgreSQL"] = MesPostgresLaneDatabase.ConnectionString,
                    ["Messaging:Provider"] = "InMemory",
                    ["Cap:Version"] = $"test-wot-{Guid.CreateVersion7().ToString("N")[..11]}",
                    ["InternalService:BearerToken"] = "test-internal-token",
                };

                foreach (var (key, value) in settings)
                {
                    builder.UseSetting(key, value);
                }

                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(settings));
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton(saveChangesGate);
                    services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
                        options.AddInterceptors(serviceProvider.GetRequiredService<SaveChangesRaceGate>()));
                });
            });

    private sealed record CommandOutcome(
        WorkOrderTransformationResult? Result,
        Exception? Exception);

    private sealed class SaveChangesRaceGate : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource<bool> bothSavesArrived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivalCount;
        private int enabled;

        public void Enable() => Volatile.Write(ref enabled, 1);

        public void Release() => bothSavesArrived.TrySetResult(true);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref enabled) == 0)
            {
                return result;
            }

            var arrival = Interlocked.Increment(ref arrivalCount);
            if (arrival <= 2)
            {
                if (arrival == 2)
                {
                    bothSavesArrived.TrySetResult(true);
                }

                await bothSavesArrived.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }

            return result;
        }
    }
}
