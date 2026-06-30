using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmEventAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Commands;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

public sealed class IndustrialTelemetryIdempotentConcurrencyTests
{
    [Fact]
    public async Task Duplicate_sample_save_conflict_is_retried_as_idempotent_existing_summary()
    {
        await using var database = await IndustrialTelemetrySqliteDatabase.CreateAsync();
        await using var winningContext = database.CreateContext();
        await using var racingContext = database.CreateContext();
        var command = new RecordTelemetrySampleCommand(
            "org-001",
            "env-dev",
            "DEV-RACE-01",
            "temperature",
            new DateTimeOffset(2026, 6, 1, 11, 59, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
            1,
            91m,
            96m,
            94m,
            "race-sample-001",
            "SCADA-A",
            "opc-ua-cell-race",
            "running",
            new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var winningResult = await new RecordTelemetrySampleCommandHandler(winningContext).Handle(command, CancellationToken.None);
        var racingHandler = new RecordTelemetrySampleCommandHandler(racingContext);
        var behavior = new IndustrialTelemetryIdempotentIngestionBehavior<RecordTelemetrySampleCommand, RecordTelemetrySampleResult>(racingContext);
        var attempts = 0;

        var recoveredResult = await behavior.Handle(
            command,
            async ct =>
            {
                attempts++;
                var result = await racingHandler.Handle(command, ct);
                if (attempts == 1)
                {
                    await winningContext.SaveChangesAsync(ct);
                }

                await racingContext.SaveChangesAsync(ct);
                return result;
            },
            CancellationToken.None);

        Assert.Equal(2, attempts);
        Assert.Equal(winningResult.TelemetrySummaryId, recoveredResult.TelemetrySummaryId);
        Assert.Equal(winningResult.DeviceStateSnapshotId, recoveredResult.DeviceStateSnapshotId);

        await using var assertionContext = database.CreateContext();
        Assert.Equal(1, await assertionContext.TelemetrySummaries.CountAsync());
        Assert.Equal(1, await assertionContext.DeviceStateSnapshots.CountAsync());
        Assert.Equal(0, await assertionContext.AlarmEvents.CountAsync());
    }

    [Fact]
    public async Task Duplicate_alarm_save_conflict_is_retried_as_idempotent_existing_alarm()
    {
        await using var database = await IndustrialTelemetrySqliteDatabase.CreateAsync();
        await using var winningContext = database.CreateContext();
        await using var racingContext = database.CreateContext();
        var command = new RaiseAlarmCommand(
            "org-001",
            "env-dev",
            "DEV-RACE-02",
            "TEMP_HIGH",
            "critical",
            new DateTimeOffset(2026, 6, 1, 12, 30, 0, TimeSpan.Zero),
            "race-alarm-001");
        var winningAlarmId = await new RaiseAlarmCommandHandler(winningContext).Handle(command, CancellationToken.None);
        var racingHandler = new RaiseAlarmCommandHandler(racingContext);
        var behavior = new IndustrialTelemetryIdempotentIngestionBehavior<RaiseAlarmCommand, AlarmEventId>(racingContext);
        var attempts = 0;

        var recoveredAlarmId = await behavior.Handle(
            command,
            async ct =>
            {
                attempts++;
                var alarmId = await racingHandler.Handle(command, ct);
                if (attempts == 1)
                {
                    await winningContext.SaveChangesAsync(ct);
                }

                await racingContext.SaveChangesAsync(ct);
                return alarmId;
            },
            CancellationToken.None);

        Assert.Equal(2, attempts);
        Assert.Equal(winningAlarmId, recoveredAlarmId);

        await using var assertionContext = database.CreateContext();
        Assert.Equal(1, await assertionContext.AlarmEvents.CountAsync());
    }

    [Fact]
    public async Task Duplicate_alarm_same_external_id_ignores_continuous_measurement_values()
    {
        await using var database = await IndustrialTelemetrySqliteDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var first = new RaiseAlarmCommand(
            "org-001",
            "env-dev",
            "DEV-RACE-04",
            "TEMP_HIGH",
            "critical",
            new DateTimeOffset(2026, 6, 1, 12, 30, 0, TimeSpan.Zero),
            "race-alarm-continuous-001",
            "p1",
            "temperature",
            96.5m,
            90m,
            "celsius");
        var replayWithFreshReading = first with
        {
            ObservedValue = 97.25m,
            ThresholdValue = 90.000001m
        };
        var handler = new RaiseAlarmCommandHandler(context);

        var firstAlarmId = await handler.Handle(first, CancellationToken.None);
        await context.SaveChangesAsync();
        var replayAlarmId = await handler.Handle(replayWithFreshReading, CancellationToken.None);
        await context.SaveChangesAsync();

        Assert.Equal(firstAlarmId, replayAlarmId);
        Assert.Equal(1, await context.AlarmEvents.CountAsync());
    }

    [Fact]
    public async Task Duplicate_sample_save_conflict_with_different_payload_still_raises_known_conflict_after_retry()
    {
        await using var database = await IndustrialTelemetrySqliteDatabase.CreateAsync();
        await using var winningContext = database.CreateContext();
        await using var racingContext = database.CreateContext();
        var winningCommand = new RecordTelemetrySampleCommand(
            "org-001",
            "env-dev",
            "DEV-RACE-03",
            "temperature",
            new DateTimeOffset(2026, 6, 1, 11, 59, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
            1,
            91m,
            96m,
            94m,
            "race-sample-conflict-001",
            "SCADA-A",
            "opc-ua-cell-race");
        var racingCommand = winningCommand with
        {
            AverageValue = 95m
        };
        await new RecordTelemetrySampleCommandHandler(winningContext).Handle(winningCommand, CancellationToken.None);
        var racingHandler = new RecordTelemetrySampleCommandHandler(racingContext);
        var behavior = new IndustrialTelemetryIdempotentIngestionBehavior<RecordTelemetrySampleCommand, RecordTelemetrySampleResult>(racingContext);
        var attempts = 0;

        var exception = await Assert.ThrowsAsync<KnownException>(() => behavior.Handle(
            racingCommand,
            async ct =>
            {
                attempts++;
                var result = await racingHandler.Handle(racingCommand, ct);
                if (attempts == 1)
                {
                    await winningContext.SaveChangesAsync(ct);
                }

                await racingContext.SaveChangesAsync(ct);
                return result;
            },
            CancellationToken.None));

        Assert.Equal(2, attempts);
        Assert.Contains("conflicting payload", exception.Message, StringComparison.OrdinalIgnoreCase);

        await using var assertionContext = database.CreateContext();
        Assert.Equal(1, await assertionContext.TelemetrySummaries.CountAsync());
    }

    [Fact]
    public async Task Duplicate_alarm_save_conflict_with_different_payload_still_raises_known_conflict_after_retry()
    {
        await using var database = await IndustrialTelemetrySqliteDatabase.CreateAsync();
        await using var winningContext = database.CreateContext();
        await using var racingContext = database.CreateContext();
        var winningCommand = new RaiseAlarmCommand(
            "org-001",
            "env-dev",
            "DEV-RACE-03",
            "TEMP_HIGH",
            "critical",
            new DateTimeOffset(2026, 6, 1, 12, 30, 0, TimeSpan.Zero),
            "race-alarm-conflict-001");
        var racingCommand = winningCommand with
        {
            Severity = "warning"
        };
        await new RaiseAlarmCommandHandler(winningContext).Handle(winningCommand, CancellationToken.None);
        var racingHandler = new RaiseAlarmCommandHandler(racingContext);
        var behavior = new IndustrialTelemetryIdempotentIngestionBehavior<RaiseAlarmCommand, AlarmEventId>(racingContext);
        var attempts = 0;

        var exception = await Assert.ThrowsAsync<KnownException>(() => behavior.Handle(
            racingCommand,
            async ct =>
            {
                attempts++;
                var alarmId = await racingHandler.Handle(racingCommand, ct);
                if (attempts == 1)
                {
                    await winningContext.SaveChangesAsync(ct);
                }

                await racingContext.SaveChangesAsync(ct);
                return alarmId;
            },
            CancellationToken.None));

        Assert.Equal(2, attempts);
        Assert.Contains("conflicting payload", exception.Message, StringComparison.OrdinalIgnoreCase);

        await using var assertionContext = database.CreateContext();
        Assert.Equal(1, await assertionContext.AlarmEvents.CountAsync());
    }

    [Fact]
    public void Idempotent_ingestion_behavior_wraps_unit_of_work_save_in_real_mediatr_pipeline()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("environment", "Testing");
                builder.UseSetting("InternalService:BearerToken", "test-internal-token");
            });
        using var scope = factory.Services.CreateScope();

        var behaviorTypes = scope.ServiceProvider
            .GetServices<IPipelineBehavior<RaiseAlarmCommand, AlarmEventId>>()
            .Select(behavior => behavior.GetType())
            .ToArray();
        var idempotentBehaviorIndex = Array.FindIndex(behaviorTypes, IsIdempotentIngestionBehavior);
        var unitOfWorkBehaviorIndex = Array.FindIndex(
            behaviorTypes,
            type => type.FullName?.Contains("UnitOfWorkBehavior", StringComparison.Ordinal) is true);

        Assert.True(idempotentBehaviorIndex >= 0, "IndustrialTelemetry idempotent ingestion behavior must be registered.");
        Assert.True(unitOfWorkBehaviorIndex >= 0, "Unit of work behavior must be registered.");
        Assert.True(
            idempotentBehaviorIndex < unitOfWorkBehaviorIndex,
            "IndustrialTelemetry idempotent ingestion behavior must wrap unit of work save to catch DbUpdateException.");
    }

    private static bool IsIdempotentIngestionBehavior(Type type)
    {
        return type.IsGenericType &&
            type.GetGenericTypeDefinition() == typeof(IndustrialTelemetryIdempotentIngestionBehavior<,>);
    }

    private sealed class IndustrialTelemetrySqliteDatabase : IAsyncDisposable
    {
        private readonly string connectionString = $"Data Source=file:industrial-telemetry-race-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        private readonly SqliteConnection keepAliveConnection;

        private IndustrialTelemetrySqliteDatabase()
        {
            keepAliveConnection = new SqliteConnection(connectionString);
        }

        public static async Task<IndustrialTelemetrySqliteDatabase> CreateAsync()
        {
            var database = new IndustrialTelemetrySqliteDatabase();
            await database.keepAliveConnection.OpenAsync();
            await using var context = database.CreateContext();
            await context.Database.EnsureCreatedAsync();
            return database;
        }

        public ApplicationDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connectionString)
                .Options;
            return new ApplicationDbContext(options, new NoopMediator());
        }

        public async ValueTask DisposeAsync()
        {
            await keepAliveConnection.DisposeAsync();
        }
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot stream requests.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot stream requests.");
        }
    }
}
