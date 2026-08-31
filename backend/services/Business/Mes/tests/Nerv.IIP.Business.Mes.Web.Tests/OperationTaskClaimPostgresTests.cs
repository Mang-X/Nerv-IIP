using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(MesPostgresLaneDatabase.CollectionName)]
public sealed class OperationTaskClaimPostgresTests
{
    private static readonly DateTimeOffset ClaimedAtUtc =
        DateTimeOffset.Parse("2026-08-30T08:00:00Z");

    // Break caught: removing assignment ownership concurrency or its retry lets two claimants
    // succeed, or leaves more than one owner, participant, or intent receipt.
    [MesRealPostgresFact]
    public async Task Concurrent_claims_persist_one_owner_participant_and_receipt_and_reject_the_loser_on_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var saveChangesGate = new SaveChangesRaceGate();
        await using var factory = CreateFactory(saveChangesGate);
        await StartMigrateAndSeedAsync(factory);

        var first = new ClaimDispatchTaskCommand(
            "org-001", "env-dev", "OP-CLAIM-001", "worker-001", "操作员甲",
            null, "SHIFT-A", ClaimedAtUtc, "user:worker-001", "claim-postgres-001");
        var second = new ClaimDispatchTaskCommand(
            "org-001", "env-dev", "OP-CLAIM-001", "worker-002", "操作员乙",
            null, "SHIFT-A", ClaimedAtUtc.AddSeconds(1), "user:worker-002", "claim-postgres-002");

        var outcomes = await SendConcurrentlyAsync(factory, saveChangesGate, first, second);

        var accepted = Assert.Single(outcomes, outcome => outcome.Response is not null);
        Assert.Equal("Accepted", accepted.Response!.Status);
        var rejected = Assert.Single(outcomes, outcome => outcome.Exception is not null);
        var conflict = Assert.IsType<KnownException>(rejected.Exception);
        Assert.Equal("该工序任务已被领取。", conflict.Message);

        await using var assertionScope = factory.Services.CreateAsyncScope();
        var dbContext = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persistedTask = await dbContext.OperationTasks.SingleAsync();
        var participant = await dbContext.OperationTaskParticipants.SingleAsync();
        var receipt = await dbContext.CodeIdempotencyKeys.SingleAsync();
        var winner = outcomes.Single(outcome => outcome.Response is not null).Command;

        Assert.Equal(winner.AssignedUserId, persistedTask.AssignedUserId);
        Assert.Equal(winner.AssignedUserName, persistedTask.AssignedUserName);
        Assert.Equal(winner.AssignedUserId, participant.WorkerId);
        Assert.Equal(winner.AssignedUserName, participant.WorkerName);
        Assert.Equal(100m, participant.SharePercent);
        Assert.Equal(winner.IdempotencyKey, receipt.IdempotencyKey);
        Assert.Equal("operation-task-claim", receipt.RuleKey);
        Assert.Equal(winner.OperationTaskId, receipt.Code);
    }

    private static async Task<ClaimOutcome[]> SendConcurrentlyAsync(
        WebApplicationFactory<Program> factory,
        SaveChangesRaceGate saveChangesGate,
        ClaimDispatchTaskCommand first,
        ClaimDispatchTaskCommand second)
    {
        await using var firstScope = factory.Services.CreateAsyncScope();
        await using var secondScope = factory.Services.CreateAsyncScope();
        var firstSender = firstScope.ServiceProvider.GetRequiredService<ISender>();
        var secondSender = secondScope.ServiceProvider.GetRequiredService<ISender>();

        saveChangesGate.Enable();
        try
        {
            return await Task.WhenAll(
                CaptureAsync(firstSender, first),
                CaptureAsync(secondSender, second));
        }
        finally
        {
            saveChangesGate.Release();
        }
    }

    private static async Task<ClaimOutcome> CaptureAsync(
        ISender sender,
        ClaimDispatchTaskCommand command)
    {
        try
        {
            return new(command, await sender.Send(command, CancellationToken.None), null);
        }
        catch (Exception exception)
        {
            return new(command, null, exception);
        }
    }

    private static async Task StartMigrateAndSeedAsync(WebApplicationFactory<Program> factory)
    {
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        MesPostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
        await dbContext.Database.MigrateAsync(CancellationToken.None);

        var workOrder = WorkOrder.Create(
            "org-001", "env-dev", "WO-CLAIM-001", "SKU-001", "PV-001", 10m, 1,
            ClaimedAtUtc.AddHours(8), "PCS");
        workOrder.MarkReleased();
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-CLAIM-001",
            "OP-CLAIM-001",
            OperationTaskLifecycleStatus.Queued,
            10,
            "WC-001",
            [],
            ClaimedAtUtc,
            TimeSpan.FromHours(1),
            null,
            null));
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
                    ["Cap:Version"] = $"test-operation-task-claim-{Guid.CreateVersion7():N}",
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

    private sealed record ClaimOutcome(
        ClaimDispatchTaskCommand Command,
        MesAcceptedResponse? Response,
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

                await TestTimeout.RunAsync(
                    $"MES operation-task claim SaveChanges race gate; observed arrivals={arrival}",
                    async token => await bothSavesArrived.Task.WaitAsync(token),
                    TimeSpan.FromSeconds(15),
                    cancellationToken);
            }

            return result;
        }
    }
}
