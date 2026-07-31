using System.Diagnostics;
using System.Text.RegularExpressions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.DowntimeReasonAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Infrastructure;
using Nerv.IIP.Business.Maintenance.Web.Application.Commands;
using Nerv.IIP.Business.Maintenance.Web.Application.Errors;
using Nerv.IIP.DistributedLocking;
using NetCorePal.Extensions.DistributedLocks;
using Npgsql;
using StackExchange.Redis;

namespace Nerv.IIP.Business.FullChain.Tests;

public sealed class MaintenanceLifecycleDockerAcceptanceTests
{
    [Fact]
    public async Task Keyword_substring_query_uses_the_real_postgres_trigram_indexes()
    {
        await using var dependencies = await MaintenanceLifecycleDockerDependencies.StartAsync();
        await using var provider = await CreateMaintenanceProviderAsync(dependencies.PostgresConnectionString);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var workOrder = MaintenanceWorkOrder.OpenFromAlarm(
                "org-man631-search", "env-man631", "DEV-MAN631-NEEDLE", "ALARM-MAN631-NEEDLE", "high",
                assignedTechnicianUserId: "TECH-MAN631-NEEDLE");
            workOrder.Assign("TECH-MAN631-NEEDLE", "TEAM-MAN631-NEEDLE");
            db.MaintenanceWorkOrders.Add(workOrder);
            await db.SaveChangesAsync();
        }

        await using var connection = new NpgsqlConnection(dependencies.PostgresConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SET enable_seqscan = off;
            EXPLAIN (COSTS OFF)
            SELECT id
            FROM maintenance.maintenance_work_orders
            WHERE lower(device_asset_id) LIKE '%man631-needle%'
               OR lower(source_alarm_id) LIKE '%man631-needle%'
               OR lower(source_reference_id) LIKE '%man631-needle%'
               OR lower(assigned_technician_user_id) LIKE '%man631-needle%'
               OR lower(assigned_team_id) LIKE '%man631-needle%';
            """;
        var planLines = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                planLines.Add(reader.GetString(0));
            }
        }
        var plan = string.Join(Environment.NewLine, planLines);

        Assert.Contains("BitmapOr", plan, StringComparison.Ordinal);
        foreach (var indexName in new[]
                 {
                     "ix_maintenance_work_orders_search_device_trgm",
                     "ix_maintenance_work_orders_search_alarm_trgm",
                     "ix_maintenance_work_orders_search_reference_trgm",
                     "ix_maintenance_work_orders_search_technician_trgm",
                     "ix_maintenance_work_orders_search_team_trgm",
                 })
        {
            Assert.Contains(indexName, plan, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Lifecycle_idempotency_is_serialized_by_real_postgres_and_redis()
    {
        await using var dependencies = await MaintenanceLifecycleDockerDependencies.StartAsync();
        await using var provider = await CreateMaintenanceProviderAsync(dependencies.PostgresConnectionString);
        await using var redisConnection = await ConnectionMultiplexer.ConnectAsync(dependencies.RedisConnectionString);
        var distributedLock = CreateDistributedLock(redisConnection);

        var replayWorkOrder = await SeedWorkOrderAsync(provider, "replay", assigned: true);
        var replayCommand = Accept(replayWorkOrder, "same-payload", "same-reason");
        var replayResults = await Task.WhenAll(
            ExecuteTransitionAsync(provider, distributedLock, replayCommand),
            ExecuteTransitionAsync(provider, distributedLock, replayCommand));

        Assert.All(replayResults, result => Assert.Equal(replayResults[0], result));
        await AssertPersistedLifecycleOnceAsync(provider, replayWorkOrder.Id, "same-payload");

        var conflictWorkOrder = await SeedWorkOrderAsync(provider, "conflict", assigned: true);
        var conflictResults = await Task.WhenAll(
            CaptureTransitionAsync(provider, distributedLock, Accept(conflictWorkOrder, "different-payload", "reason-a")),
            CaptureTransitionAsync(provider, distributedLock, Accept(conflictWorkOrder, "different-payload", "reason-b")));

        Assert.Single(conflictResults, outcome => outcome.Result is not null);
        Assert.Single(conflictResults, outcome => outcome.Exception is MaintenanceIdempotencyConflictException);
        await AssertPersistedLifecycleOnceAsync(provider, conflictWorkOrder.Id, "different-payload");
    }

    [Fact]
    public async Task Same_scope_idempotency_key_across_work_orders_returns_stable_conflict_without_state_crossover()
    {
        await using var dependencies = await MaintenanceLifecycleDockerDependencies.StartAsync();
        await using var provider = await CreateMaintenanceProviderAsync(dependencies.PostgresConnectionString);
        await using var redisConnection = await ConnectionMultiplexer.ConnectAsync(dependencies.RedisConnectionString);
        var distributedLock = CreateDistributedLock(redisConnection);
        var organizationId = $"org-man631-cross-work-order-{Guid.CreateVersion7():N}";
        var first = await SeedWorkOrderAsync(provider, "cross-a", assigned: true, organizationId: organizationId);
        var second = await SeedWorkOrderAsync(provider, "cross-b", assigned: true, organizationId: organizationId);
        var saveGate = new ConcurrentLifecycleSaveGate(2, TimeSpan.FromSeconds(2));
        const string idempotencyKey = "cross-work-order-shared-key";

        var outcomes = await Task.WhenAll(
            CaptureTransitionThroughBehaviorAsync(
                provider,
                distributedLock,
                Accept(first, idempotencyKey, "accept-first"),
                saveGate),
            CaptureTransitionThroughBehaviorAsync(
                provider,
                distributedLock,
                Accept(second, idempotencyKey, "accept-second"),
                saveGate));

        var winner = Assert.Single(outcomes, outcome => outcome.Result is not null).Result!;
        var loser = Assert.Single(outcomes, outcome => outcome.Exception is not null).Exception!;
        Assert.IsType<MaintenanceIdempotencyConflictException>(loser);
        Assert.Equal("idempotency-conflict", MaintenanceIdempotencyConflictException.SafeCode);
        Assert.DoesNotContain(outcomes, outcome => outcome.Exception is DbUpdateException);

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = await db.MaintenanceWorkOrders.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == "env-man631")
            .ToArrayAsync();
        Assert.Equal(2, persisted.Length);
        Assert.Single(persisted, workOrder =>
            workOrder.Id == winner.WorkOrderId
            && workOrder.Status == MaintenanceWorkOrderStatus.Accepted
            && workOrder.Version == 1);
        Assert.Single(persisted, workOrder =>
            workOrder.Id != winner.WorkOrderId
            && workOrder.Status == MaintenanceWorkOrderStatus.Open
            && workOrder.Version == 0);
        var receipt = Assert.Single(await db.MaintenanceWorkOrderLifecycleEvents.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.EnvironmentId == "env-man631"
                && x.IdempotencyKey == idempotencyKey)
            .ToArrayAsync());
        Assert.Equal(winner.WorkOrderId, receipt.WorkOrderId);
    }

    [Fact]
    public async Task Parallel_dependency_instances_use_distinct_owned_resources_and_cleanup_only_their_run()
    {
        var firstStart = MaintenanceLifecycleDockerDependencies.StartAsync();
        var secondStart = MaintenanceLifecycleDockerDependencies.StartAsync();
        MaintenanceLifecycleDockerDependencies? first = null;
        MaintenanceLifecycleDockerDependencies? second = null;
        var firstDisposed = false;
        var secondDisposed = false;
        try
        {
            var dependencies = await Task.WhenAll(firstStart, secondStart);
            first = dependencies[0];
            second = dependencies[1];

            Assert.NotEqual(first.Identity.RunId, second.Identity.RunId);
            Assert.NotEqual(first.Identity.PostgresContainerName, second.Identity.PostgresContainerName);
            Assert.NotEqual(first.Identity.RedisContainerName, second.Identity.RedisContainerName);
            Assert.NotEqual(first.Identity.PostgresVolumeName, second.Identity.PostgresVolumeName);
            Assert.NotEqual(first.Identity.RedisVolumeName, second.Identity.RedisVolumeName);
            Assert.NotEqual(first.Identity.OwnershipLabel, second.Identity.OwnershipLabel);
            Assert.Equal(4, (await MaintenanceLifecycleDockerDependencies.ListOwnedResourcesAsync(
                first.Identity.OwnershipLabel)).Count);
            Assert.Equal(4, (await MaintenanceLifecycleDockerDependencies.ListOwnedResourcesAsync(
                second.Identity.OwnershipLabel)).Count);

            var firstOwnershipLabel = first.Identity.OwnershipLabel;
            await first.DisposeAsync();
            firstDisposed = true;
            Assert.Empty(await MaintenanceLifecycleDockerDependencies.ListOwnedResourcesAsync(firstOwnershipLabel));
            Assert.Equal(4, (await MaintenanceLifecycleDockerDependencies.ListOwnedResourcesAsync(
                second.Identity.OwnershipLabel)).Count);

            var secondOwnershipLabel = second.Identity.OwnershipLabel;
            await second.DisposeAsync();
            secondDisposed = true;
            Assert.Empty(await MaintenanceLifecycleDockerDependencies.ListOwnedResourcesAsync(secondOwnershipLabel));
        }
        finally
        {
            if (!firstDisposed && first is null && firstStart.IsCompletedSuccessfully)
            {
                first = await firstStart;
            }
            if (!secondDisposed && second is null && secondStart.IsCompletedSuccessfully)
            {
                second = await secondStart;
            }
            if (!firstDisposed && first is not null)
            {
                await first.DisposeAsync();
            }
            if (!secondDisposed && second is not null)
            {
                await second.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task Legacy_complete_and_lifecycle_accept_share_the_real_redis_aggregate_lock()
    {
        await using var dependencies = await MaintenanceLifecycleDockerDependencies.StartAsync();
        await using var provider = await CreateMaintenanceProviderAsync(dependencies.PostgresConnectionString);
        await using var redisConnection = await ConnectionMultiplexer.ConnectAsync(dependencies.RedisConnectionString);
        var distributedLock = CreateDistributedLock(redisConnection);
        var workOrder = await SeedWorkOrderAsync(provider, "legacy", assigned: true, includeDowntimeReason: true);

        var outcomes = await Task.WhenAll(
            CaptureLegacyCompleteAsync(provider, distributedLock, workOrder),
            CaptureTransitionAsync(provider, distributedLock, Accept(workOrder, "lifecycle-accept", "accept")));

        Assert.Single(outcomes, outcome => outcome.Result is not null);
        Assert.Single(outcomes, outcome => outcome.Exception is MaintenanceLifecycleConflictException);

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = await db.MaintenanceWorkOrders.AsNoTracking().SingleAsync(x => x.Id == workOrder.Id);
        Assert.Equal(1, persisted.Version);
        Assert.Contains(persisted.Status, new[] { MaintenanceWorkOrderStatus.Accepted, MaintenanceWorkOrderStatus.Completed });
        var lifecycleReceipts = await db.MaintenanceWorkOrderLifecycleEvents.CountAsync(x => x.WorkOrderId == workOrder.Id);
        var completionReceipts = await db.CodeIdempotencyKeys.CountAsync(x =>
            x.OrganizationId == workOrder.OrganizationId &&
            x.EnvironmentId == "env-man631" &&
            (x.IdempotencyKey == "legacy-complete" || x.IdempotencyKey == "lifecycle-accept"));
        Assert.Equal(1, lifecycleReceipts + completionReceipts);
    }

    private static async Task<ServiceProvider> CreateMaintenanceProviderAsync(string postgresConnectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssembly(typeof(TransitionMaintenanceWorkOrderCommandHandler).Assembly));
        services.AddMaintenancePostgreSqlPersistence(postgresConnectionString);
        var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
        return provider;
    }

    private static RedisCommandDistributedLock CreateDistributedLock(IConnectionMultiplexer connection) =>
        new(
            new StackExchangeRedisCommandLockStore(connection.GetDatabase(), "business-maintenance"),
            TimeProvider.System);

    private static async Task<SeededLifecycleWorkOrder> SeedWorkOrderAsync(
        ServiceProvider provider,
        string scenario,
        bool assigned,
        bool includeDowntimeReason = false,
        string? organizationId = null)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var suffix = Guid.CreateVersion7().ToString("N");
        organizationId ??= $"org-man631-{scenario}-{suffix}";
        var workOrder = MaintenanceWorkOrder.OpenManual(
            organizationId,
            "env-man631",
            $"DEV-{suffix}",
            "high",
            "reporter-001",
            assignedTechnicianUserId: assigned ? "tech-001" : null);
        db.MaintenanceWorkOrders.Add(workOrder);
        if (includeDowntimeReason)
        {
            db.DowntimeReasons.Add(DowntimeReason.Create(
                organizationId,
                "env-man631",
                "equipment-failure",
                "Equipment failure"));
        }

        await db.SaveChangesAsync();
        return new SeededLifecycleWorkOrder(workOrder.Id, organizationId);
    }

    private static TransitionMaintenanceWorkOrderCommand Accept(
        SeededLifecycleWorkOrder workOrder,
        string idempotencyKey,
        string reason) =>
        new(
            workOrder.OrganizationId,
            "env-man631",
            workOrder.Id,
            MaintenanceWorkOrderAction.Accept,
            "tech-001",
            reason,
            idempotencyKey,
            0);

    private static async Task<MaintenanceWorkOrderCommandResult> ExecuteTransitionAsync(
        ServiceProvider provider,
        IDistributedLock distributedLock,
        TransitionMaintenanceWorkOrderCommand command)
    {
        var behavior = new NervIipCommandLockBehavior<
            TransitionMaintenanceWorkOrderCommand,
            MaintenanceWorkOrderCommandResult>(
            [new TransitionMaintenanceWorkOrderCommandLock()],
            distributedLock);
        return await behavior.Handle(
            command,
            async cancellationToken =>
            {
                await using var scope = provider.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var result = await new TransitionMaintenanceWorkOrderCommandHandler(db)
                    .Handle(command, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                return result;
            },
            CancellationToken.None);
    }

    private static async Task<(MaintenanceWorkOrderCommandResult? Result, Exception? Exception)> CaptureTransitionAsync(
        ServiceProvider provider,
        IDistributedLock distributedLock,
        TransitionMaintenanceWorkOrderCommand command)
    {
        try
        {
            return (await ExecuteTransitionAsync(provider, distributedLock, command), null);
        }
        catch (Exception exception)
        {
            return (null, exception);
        }
    }

    private static async Task<(MaintenanceWorkOrderCommandResult? Result, Exception? Exception)>
        CaptureTransitionThroughBehaviorAsync(
            ServiceProvider provider,
            IDistributedLock distributedLock,
            TransitionMaintenanceWorkOrderCommand command,
            ConcurrentLifecycleSaveGate saveGate)
    {
        try
        {
            var behavior = new NervIipCommandLockBehavior<
                TransitionMaintenanceWorkOrderCommand,
                MaintenanceWorkOrderCommandResult>(
                [new TransitionMaintenanceWorkOrderCommandLock()],
                distributedLock);
            var result = await behavior.Handle(
                command,
                async cancellationToken =>
                {
                    await using var scope = provider.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var staged = await new TransitionMaintenanceWorkOrderCommandHandler(db)
                        .Handle(command, cancellationToken);
                    await saveGate.SignalAndWaitAsync(cancellationToken);
                    await db.SaveChangesAsync(cancellationToken);
                    return staged;
                },
                CancellationToken.None);
            return (result, null);
        }
        catch (Exception exception)
        {
            return (null, exception);
        }
    }

    private static async Task<(MaintenanceWorkOrderCommandResult? Result, Exception? Exception)> CaptureLegacyCompleteAsync(
        ServiceProvider provider,
        IDistributedLock distributedLock,
        SeededLifecycleWorkOrder workOrder)
    {
        var command = new CompleteMaintenanceWorkOrderCommand(
            workOrder.Id,
            "fixed",
            "equipment-failure",
            5,
            [],
            IdempotencyKey: "legacy-complete",
            OrganizationId: workOrder.OrganizationId,
            EnvironmentId: "env-man631");
        try
        {
            var settings = await new CompleteMaintenanceWorkOrderCommandLock()
                .GetLockKeysAsync(command, CancellationToken.None);
            await using var handle = await distributedLock.AcquireAsync(
                settings.LockKey!, settings.AcquireTimeout, CancellationToken.None);
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var result = await new CompleteMaintenanceWorkOrderCommandHandler(db)
                .Handle(command, CancellationToken.None);
            await db.SaveChangesAsync();
            return (result, null);
        }
        catch (Exception exception)
        {
            return (null, exception);
        }
    }

    private static async Task AssertPersistedLifecycleOnceAsync(
        ServiceProvider provider,
        MaintenanceWorkOrderId workOrderId,
        string idempotencyKey)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.MaintenanceWorkOrderLifecycleEvents.CountAsync(x =>
            x.WorkOrderId == workOrderId && x.IdempotencyKey == idempotencyKey));
        Assert.Equal(1, await db.MaintenanceWorkOrders
            .Where(x => x.Id == workOrderId)
            .Select(x => x.Version)
            .SingleAsync());
    }

    private sealed record SeededLifecycleWorkOrder(MaintenanceWorkOrderId Id, string OrganizationId);

    private sealed class ConcurrentLifecycleSaveGate(int participants, TimeSpan maximumWait)
    {
        private readonly TaskCompletionSource allArrived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;

        public async Task SignalAndWaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref arrivals) == participants)
            {
                allArrived.TrySetResult();
            }

            await Task.WhenAny(allArrived.Task, Task.Delay(maximumWait, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}

internal sealed class MaintenanceLifecycleDockerDependencies : IAsyncDisposable
{
    private const string OwnershipLabelKey = "com.nerv-iip.test.run";
    private readonly MaintenanceLifecycleDockerRunIdentity identity;

    private MaintenanceLifecycleDockerDependencies(
        MaintenanceLifecycleDockerRunIdentity identity,
        string postgresConnectionString,
        string redisConnectionString)
    {
        this.identity = identity;
        PostgresConnectionString = postgresConnectionString;
        RedisConnectionString = redisConnectionString;
    }

    public string PostgresConnectionString { get; }

    public string RedisConnectionString { get; }

    internal MaintenanceLifecycleDockerRunIdentity Identity => identity;

    public static async Task<MaintenanceLifecycleDockerDependencies> StartAsync()
    {
        await DockerAsync(["version", "--format", "{{.Server.Version}}"], "Docker daemon probe", TimeSpan.FromSeconds(30));

        var identity = MaintenanceLifecycleDockerRunIdentity.Create();
        var password = $"man631-{Guid.CreateVersion7():N}";
        MaintenanceLifecycleDockerDependencies? dependencies = null;
        try
        {
            await CreateOwnedVolumeAsync(identity.PostgresVolumeName, identity.OwnershipLabel);
            await CreateOwnedVolumeAsync(identity.RedisVolumeName, identity.OwnershipLabel);
            await DockerAsync(
                [
                    "run", "-d", "--rm", "--name", identity.PostgresContainerName,
                    "--label", "com.nerv-iip.test=man-631",
                    "--label", $"{OwnershipLabelKey}={identity.OwnershipLabel}",
                    "--mount", $"type=volume,source={identity.PostgresVolumeName},target=/var/lib/postgresql",
                    "-e", "POSTGRES_USER=nerv_test",
                    "-e", $"POSTGRES_PASSWORD={password}",
                    "-e", $"POSTGRES_DB={identity.DatabaseName}",
                    "-p", "127.0.0.1::5432",
                    "postgres:18",
                ],
                "start PostgreSQL test container",
                TimeSpan.FromMinutes(5));
            await DockerAsync(
                [
                    "run", "-d", "--rm", "--name", identity.RedisContainerName,
                    "--label", "com.nerv-iip.test=man-631",
                    "--label", $"{OwnershipLabelKey}={identity.OwnershipLabel}",
                    "--mount", $"type=volume,source={identity.RedisVolumeName},target=/data",
                    "-p", "127.0.0.1::6379",
                    "redis:8", "redis-server", "--save", "", "--appendonly", "no",
                ],
                "start Redis test container",
                TimeSpan.FromMinutes(5));

            var postgresPort = ParsePublishedPort(await DockerAsync(
                ["port", identity.PostgresContainerName, "5432/tcp"],
                "resolve PostgreSQL test port",
                TimeSpan.FromSeconds(30)));
            var redisPort = ParsePublishedPort(await DockerAsync(
                ["port", identity.RedisContainerName, "6379/tcp"],
                "resolve Redis test port",
                TimeSpan.FromSeconds(30)));
            var postgresConnectionString = new NpgsqlConnectionStringBuilder
            {
                Host = "127.0.0.1",
                Port = postgresPort,
                Username = "nerv_test",
                Password = password,
                Database = identity.DatabaseName,
                Pooling = false,
                IncludeErrorDetail = false,
            }.ConnectionString;
            var redisConnectionString = $"127.0.0.1:{redisPort},abortConnect=false,connectTimeout=1000,syncTimeout=1000";
            dependencies = new MaintenanceLifecycleDockerDependencies(
                identity,
                postgresConnectionString,
                redisConnectionString);
            await WaitForPostgresAsync(postgresConnectionString);
            await WaitForRedisAsync(redisConnectionString);
            return dependencies;
        }
        catch (Exception startupException)
        {
            try
            {
                if (dependencies is not null)
                {
                    await dependencies.DisposeAsync();
                }
                else
                {
                    await CleanupOwnedResourcesAsync(identity);
                }
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(
                    "MAN-631 Docker dependency startup and cleanup both failed.",
                    startupException,
                    cleanupException);
            }

            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CleanupOwnedResourcesAsync(identity);
    }

    private static Task CleanupOwnedResourcesAsync(MaintenanceLifecycleDockerRunIdentity runIdentity) =>
        DockerOwnedResourceCleanup.CleanupAsync(
            [
                $"container:{runIdentity.RedisContainerName}",
                $"container:{runIdentity.PostgresContainerName}",
                $"volume:{runIdentity.RedisVolumeName}",
                $"volume:{runIdentity.PostgresVolumeName}",
            ],
            resource => resource.StartsWith("container:", StringComparison.Ordinal)
                ? RemoveContainerAsync(resource["container:".Length..], runIdentity.OwnershipLabel)
                : RemoveVolumeAsync(resource["volume:".Length..], runIdentity.OwnershipLabel),
            () => AssertNoOwnedResourcesAsync(runIdentity.OwnershipLabel));

    private static Task CreateOwnedVolumeAsync(string name, string ownershipLabel) =>
        DockerAsync(
            [
                "volume", "create",
                "--label", "com.nerv-iip.test=man-631",
                "--label", $"{OwnershipLabelKey}={ownershipLabel}",
                name,
            ],
            "create MAN-631 test volume",
            TimeSpan.FromSeconds(30));

    private static async Task WaitForPostgresAsync(string connectionString)
    {
        var deadline = DateTimeOffset.UtcNow.AddMinutes(2);
        Exception? lastException = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();
                return;
            }
            catch (Exception exception)
            {
                lastException = exception;
                await Task.Delay(250);
            }
        }

        throw new InvalidOperationException(
            $"Docker PostgreSQL did not become ready within two minutes ({lastException?.GetType().Name ?? "unknown"}).");
    }

    private static async Task WaitForRedisAsync(string connectionString)
    {
        var deadline = DateTimeOffset.UtcNow.AddMinutes(2);
        Exception? lastException = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                await using var connection = await ConnectionMultiplexer.ConnectAsync(connectionString);
                await connection.GetDatabase().PingAsync();
                return;
            }
            catch (Exception exception)
            {
                lastException = exception;
                await Task.Delay(250);
            }
        }

        throw new InvalidOperationException(
            $"Docker Redis did not become ready within two minutes ({lastException?.GetType().Name ?? "unknown"}).");
    }

    private static int ParsePublishedPort(string output)
    {
        var match = Regex.Match(output.Trim(), @"(?:\[[^\]]+\]|[^:\r\n]+):(?<port>\d+)\s*$", RegexOptions.CultureInvariant);
        if (!match.Success || !int.TryParse(match.Groups["port"].Value, out var port))
        {
            throw new InvalidOperationException("Docker did not report a valid loopback host port for a MAN-631 test container.");
        }

        return port;
    }

    private static async Task RemoveContainerAsync(string name, string expectedOwnershipLabel)
    {
        string actualOwnershipLabel;
        try
        {
            actualOwnershipLabel = (await DockerAsync(
                ["inspect", "--format", $"{{{{ index .Config.Labels \"{OwnershipLabelKey}\" }}}}", name],
                "inspect MAN-631 test container ownership",
                TimeSpan.FromSeconds(30))).Trim();
        }
        catch (DockerCommandException exception) when (exception.IsContainerNotFound)
        {
            return;
        }

        if (!string.Equals(actualOwnershipLabel, expectedOwnershipLabel, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Refusing to remove Docker container '{name}' because ownership label '{OwnershipLabelKey}' was '{actualOwnershipLabel}', expected '{expectedOwnershipLabel}'.");
        }

        try
        {
            await DockerAsync(["rm", "-f", name], "remove MAN-631 test container", TimeSpan.FromSeconds(30));
        }
        catch (DockerCommandException exception) when (exception.IsContainerNotFound)
        {
            // --rm can win the race after the ownership inspection.
        }
    }

    private static async Task RemoveVolumeAsync(string name, string expectedOwnershipLabel)
    {
        string actualOwnershipLabel;
        try
        {
            actualOwnershipLabel = (await DockerAsync(
                ["volume", "inspect", "--format", $"{{{{ index .Labels \"{OwnershipLabelKey}\" }}}}", name],
                "inspect MAN-631 test volume ownership",
                TimeSpan.FromSeconds(30))).Trim();
        }
        catch (DockerCommandException exception) when (exception.IsResourceNotFound)
        {
            return;
        }

        if (!string.Equals(actualOwnershipLabel, expectedOwnershipLabel, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Refusing to remove Docker volume '{name}' because ownership label '{OwnershipLabelKey}' was '{actualOwnershipLabel}', expected '{expectedOwnershipLabel}'.");
        }

        try
        {
            await DockerAsync(["volume", "rm", name], "remove MAN-631 test volume", TimeSpan.FromSeconds(30));
        }
        catch (DockerCommandException exception) when (exception.IsResourceNotFound)
        {
        }
    }

    internal static async Task<IReadOnlyCollection<string>> ListOwnedResourcesAsync(string expectedOwnershipLabel)
    {
        var containers = (await DockerAsync(
            ["ps", "-a", "--filter", $"label={OwnershipLabelKey}={expectedOwnershipLabel}", "--format", "{{.Names}}"],
            "list MAN-631 owned test containers",
            TimeSpan.FromSeconds(30))).Trim();
        var volumes = (await DockerAsync(
            ["volume", "ls", "--filter", $"label={OwnershipLabelKey}={expectedOwnershipLabel}", "--format", "{{.Name}}"],
            "list MAN-631 owned test volumes",
            TimeSpan.FromSeconds(30))).Trim();
        return containers.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(name => $"container:{name.Trim()}")
            .Concat(volumes.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(name => $"volume:{name.Trim()}"))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task AssertNoOwnedResourcesAsync(string expectedOwnershipLabel)
    {
        var residue = await ListOwnedResourcesAsync(expectedOwnershipLabel);
        if (residue.Count > 0)
        {
            throw new InvalidOperationException(
                $"MAN-631 Docker cleanup left owned resources behind: {string.Join(", ", residue)}");
        }
    }

    private static async Task<string> DockerAsync(
        IReadOnlyCollection<string> arguments,
        string operation,
        TimeSpan timeout)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Unable to start Docker for {operation}.");
            }
        }
        catch (Exception exception) when (exception is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Docker is required for MAN-631 integration tests ({operation}).", exception);
        }

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeoutSource = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"Docker timed out during {operation}.");
        }

        var output = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0)
        {
            var diagnostic = string.IsNullOrWhiteSpace(error) ? "no diagnostic output" : error.Trim();
            throw new DockerCommandException(operation, process.ExitCode, diagnostic);
        }

        return output;
    }

    private sealed class DockerCommandException(string operation, int exitCode, string diagnostic)
        : InvalidOperationException($"Docker failed during {operation} (exit={exitCode}): {diagnostic}")
    {
        public bool IsResourceNotFound =>
            Message.Contains("No such container", StringComparison.OrdinalIgnoreCase)
            || Message.Contains("No such object", StringComparison.OrdinalIgnoreCase)
            || Message.Contains("No such volume", StringComparison.OrdinalIgnoreCase);

        public bool IsContainerNotFound => IsResourceNotFound;
    }
}

internal sealed record MaintenanceLifecycleDockerRunIdentity(
    string RunId,
    string PostgresContainerName,
    string RedisContainerName,
    string PostgresVolumeName,
    string RedisVolumeName,
    string OwnershipLabel,
    string DatabaseName)
{
    public static MaintenanceLifecycleDockerRunIdentity Create()
    {
        var runId = Guid.CreateVersion7().ToString("N");
        return new MaintenanceLifecycleDockerRunIdentity(
            runId,
            $"nerv-iip-man631-postgres-{runId}",
            $"nerv-iip-man631-redis-{runId}",
            $"nerv-iip-man631-postgres-data-{runId}",
            $"nerv-iip-man631-redis-data-{runId}",
            $"man-631-{runId}",
            $"maintenance_man631_{runId}");
    }
}
