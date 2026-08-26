using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
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
            "org-001", "env-dev", "WO-CONCURRENT-REPLAY", "SKU-001", "PV-001", 10m, 10,
            occurredAtUtc.AddHours(4), "PCS"));

        var command = new SplitWorkOrderCommand(
            "org-001",
            "env-dev",
            "WO-CONCURRENT-REPLAY",
            [
                new("WO-CONCURRENT-REPLAY-CHILD-1", 4m),
                new("WO-CONCURRENT-REPLAY-CHILD-2", 6m),
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
            ["WO-CONCURRENT-REPLAY-CHILD-1", "WO-CONCURRENT-REPLAY-CHILD-2"],
            results[0].TargetWorkOrderIds);

        await using var assertionScope = factory.Services.CreateAsyncScope();
        var assertion = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var source = await assertion.WorkOrders.SingleAsync(x => x.WorkOrderIdValue == "WO-CONCURRENT-REPLAY");
        Assert.Equal(1, await assertion.WorkOrderTransformations.CountAsync(
            x => x.IdempotencyKey == command.IdempotencyKey));
        Assert.Equal(2, await assertion.WorkOrderTransformations
            .Where(x => x.IdempotencyKey == command.IdempotencyKey)
            .SelectMany(x => x.Lines)
            .CountAsync());
        Assert.Equal(2, await assertion.WorkOrders.CountAsync(
            x => x.WorkOrderIdValue.StartsWith("WO-CONCURRENT-REPLAY-CHILD-")));
        Assert.Equal(WorkOrder.SplitStatus, source.Status);
        Assert.Equal(2, source.Version);
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
        Assert.Equal(WorkOrder.SplitStatus, source.Status);
        Assert.Equal(2, source.Version);
        var firstTargetIds = firstCommand.Targets.Select(target => target.WorkOrderId).ToArray();
        var secondTargetIds = secondCommand.Targets.Select(target => target.WorkOrderId).ToArray();
        var losingTargetIds = winner.TargetWorkOrderIds.Intersect(firstTargetIds, StringComparer.Ordinal).Any()
            ? secondTargetIds
            : firstTargetIds;
        Assert.Equal(0, await assertion.WorkOrders.CountAsync(
            x => losingTargetIds.Contains(x.WorkOrderIdValue)));
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

    private static async Task SeedAsync(WebApplicationFactory<Program> factory, WorkOrder workOrder)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.WorkOrders.Add(workOrder);
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
                    ["Cap:Version"] = $"test-work-order-transformation-application-{Guid.CreateVersion7():N}",
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
