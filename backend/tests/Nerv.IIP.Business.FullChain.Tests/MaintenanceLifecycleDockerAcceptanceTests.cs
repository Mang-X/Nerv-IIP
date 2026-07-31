using System.Diagnostics;
using System.Text.RegularExpressions;
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
        bool includeDowntimeReason = false)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var suffix = Guid.CreateVersion7().ToString("N");
        var organizationId = $"org-man631-{scenario}-{suffix}";
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
        var settings = await new TransitionMaintenanceWorkOrderCommandLock()
            .GetLockKeysAsync(command, CancellationToken.None);
        await using var handle = await distributedLock.AcquireAsync(
            settings.LockKey!, settings.AcquireTimeout, CancellationToken.None);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var result = await new TransitionMaintenanceWorkOrderCommandHandler(db)
            .Handle(command, CancellationToken.None);
        await db.SaveChangesAsync();
        return result;
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
}

internal sealed class MaintenanceLifecycleDockerDependencies : IAsyncDisposable
{
    private const string OwnershipLabelKey = "com.nerv-iip.test.run";
    private readonly string postgresContainerName;
    private readonly string redisContainerName;
    private readonly string ownershipLabel;

    private MaintenanceLifecycleDockerDependencies(
        string postgresContainerName,
        string redisContainerName,
        string ownershipLabel,
        string postgresConnectionString,
        string redisConnectionString)
    {
        this.postgresContainerName = postgresContainerName;
        this.redisContainerName = redisContainerName;
        this.ownershipLabel = ownershipLabel;
        PostgresConnectionString = postgresConnectionString;
        RedisConnectionString = redisConnectionString;
    }

    public string PostgresConnectionString { get; }

    public string RedisConnectionString { get; }

    public static async Task<MaintenanceLifecycleDockerDependencies> StartAsync()
    {
        await DockerAsync(["version", "--format", "{{.Server.Version}}"], "Docker daemon probe", TimeSpan.FromSeconds(30));

        var suffix = Guid.CreateVersion7().ToString("N")[..12];
        var postgresName = $"nerv-iip-man631-postgres-{suffix}";
        var redisName = $"nerv-iip-man631-redis-{suffix}";
        var ownershipLabel = $"man-631-{suffix}";
        var databaseName = $"maintenance_man631_{suffix}";
        var password = $"man631-{Guid.CreateVersion7():N}";
        MaintenanceLifecycleDockerDependencies? dependencies = null;
        try
        {
            await DockerAsync(
                [
                    "run", "-d", "--rm", "--name", postgresName,
                    "--label", "com.nerv-iip.test=man-631",
                    "--label", $"{OwnershipLabelKey}={ownershipLabel}",
                    "-e", "POSTGRES_USER=nerv_test",
                    "-e", $"POSTGRES_PASSWORD={password}",
                    "-e", $"POSTGRES_DB={databaseName}",
                    "-p", "127.0.0.1::5432",
                    "postgres:18",
                ],
                "start PostgreSQL test container",
                TimeSpan.FromMinutes(5));
            await DockerAsync(
                [
                    "run", "-d", "--rm", "--name", redisName,
                    "--label", "com.nerv-iip.test=man-631",
                    "--label", $"{OwnershipLabelKey}={ownershipLabel}",
                    "-p", "127.0.0.1::6379",
                    "redis:8", "redis-server", "--save", "", "--appendonly", "no",
                ],
                "start Redis test container",
                TimeSpan.FromMinutes(5));

            var postgresPort = ParsePublishedPort(await DockerAsync(
                ["port", postgresName, "5432/tcp"],
                "resolve PostgreSQL test port",
                TimeSpan.FromSeconds(30)));
            var redisPort = ParsePublishedPort(await DockerAsync(
                ["port", redisName, "6379/tcp"],
                "resolve Redis test port",
                TimeSpan.FromSeconds(30)));
            var postgresConnectionString = new NpgsqlConnectionStringBuilder
            {
                Host = "127.0.0.1",
                Port = postgresPort,
                Username = "nerv_test",
                Password = password,
                Database = databaseName,
                Pooling = false,
                IncludeErrorDetail = false,
            }.ConnectionString;
            var redisConnectionString = $"127.0.0.1:{redisPort},abortConnect=false,connectTimeout=1000,syncTimeout=1000";
            dependencies = new MaintenanceLifecycleDockerDependencies(
                postgresName,
                redisName,
                ownershipLabel,
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
                    await CleanupOwnedResourcesAsync(redisName, postgresName, ownershipLabel);
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
        await CleanupOwnedResourcesAsync(redisContainerName, postgresContainerName, ownershipLabel);
    }

    private static Task CleanupOwnedResourcesAsync(
        string redisName,
        string postgresName,
        string expectedOwnershipLabel) =>
        DockerOwnedResourceCleanup.CleanupAsync(
            [redisName, postgresName],
            name => RemoveContainerAsync(name, expectedOwnershipLabel),
            () => AssertNoOwnedContainersAsync(expectedOwnershipLabel));

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

    private static async Task AssertNoOwnedContainersAsync(string expectedOwnershipLabel)
    {
        var residue = (await DockerAsync(
            ["ps", "-a", "--filter", $"label={OwnershipLabelKey}={expectedOwnershipLabel}", "--format", "{{.Names}}"],
            "verify MAN-631 test container cleanup",
            TimeSpan.FromSeconds(30))).Trim();
        if (!string.IsNullOrWhiteSpace(residue))
        {
            throw new InvalidOperationException(
                $"MAN-631 Docker cleanup left owned containers behind: {residue.Replace(Environment.NewLine, ", ", StringComparison.Ordinal)}");
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
        public bool IsContainerNotFound =>
            Message.Contains("No such container", StringComparison.OrdinalIgnoreCase)
            || Message.Contains("No such object", StringComparison.OrdinalIgnoreCase);
    }
}
