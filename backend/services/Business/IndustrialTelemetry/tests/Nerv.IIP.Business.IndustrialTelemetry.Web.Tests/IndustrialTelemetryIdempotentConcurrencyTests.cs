using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmEventAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmRuleAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Commands;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

public sealed class IndustrialTelemetryIdempotentConcurrencyTests
{
    [Fact]
    public async Task Duplicate_sample_save_conflict_is_retried_as_idempotent_existing_summary()
    {
        await using var database = await IndustrialTelemetrySqliteDatabase.CreateAsync();
        await using (var setupContext = database.CreateContext())
        {
            setupContext.AlarmRules.Add(AlarmRule.Configure("org-001", "env-dev", "DEV-RACE-01", "TEMP_RULE", "TEMP_HIGH", "critical", "temperature", ">=", 90m, "celsius", true));
            await setupContext.SaveChangesAsync();
        }

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
        Assert.Equal(1, await assertionContext.AlarmEvents.CountAsync());
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
