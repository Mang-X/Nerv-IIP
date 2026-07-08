using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmEventAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmRuleAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Commands;
using NetCorePal.Extensions.Primitives;
using Npgsql;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

public sealed class IndustrialTelemetryIdempotentConcurrencyTests
{
    private const string PostgresConnectionStringEnvironmentVariable = "NERV_IIP_TEST_POSTGRES";

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
            null,
            null,
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
    public async Task Direct_alarm_same_tag_external_id_with_different_alarm_code_returns_known_conflict()
    {
        await using var database = await IndustrialTelemetrySqliteDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var existingCommand = new RaiseAlarmCommand(
            "org-001",
            "env-dev",
            "DEV-RACE-05",
            "TEMP_HIGH",
            "critical",
            new DateTimeOffset(2026, 6, 1, 12, 30, 0, TimeSpan.Zero),
            "race-alarm-tag-001",
            "p1",
            "temperature",
            96.5m,
            90m,
            "celsius");
        var conflictingCommand = existingCommand with
        {
            AlarmCode = "TEMP_CRITICAL"
        };
        var handler = new RaiseAlarmCommandHandler(context);
        var behavior = new IndustrialTelemetryIdempotentIngestionBehavior<RaiseAlarmCommand, AlarmEventId>(context);

        await handler.Handle(existingCommand, CancellationToken.None);
        await context.SaveChangesAsync();
        var exception = await Assert.ThrowsAsync<KnownException>(() => behavior.Handle(
            conflictingCommand,
            async ct =>
            {
                var alarmId = await handler.Handle(conflictingCommand, ct);
                await context.SaveChangesAsync(ct);
                return alarmId;
            },
            CancellationToken.None));

        Assert.Contains("conflicting payload", exception.Message, StringComparison.OrdinalIgnoreCase);
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

    [RealPostgresFact]
    public async Task Rule_alarm_race_with_changed_alarm_code_keeps_single_active_alarm_on_postgres()
    {
        var postgresConnectionString = Environment.GetEnvironmentVariable(PostgresConnectionStringEnvironmentVariable)!;

        await using var database = await IndustrialTelemetryPostgresDatabase.CreateAsync(postgresConnectionString);
        await using (var setupContext = database.CreateContext())
        {
            setupContext.AlarmRules.Add(AlarmRule.Configure("org-001", "env-dev", "DEV-RACE-PG-01", "TEMP_RULE", "TEMP_HIGH", "critical", "temperature", ">=", 90m, "celsius", true));
            await setupContext.SaveChangesAsync();
        }

        await using var staleRuleContext = database.CreateContext();
        var staleRuleHandler = new RecordTelemetrySampleCommandHandler(staleRuleContext);
        var staleRuleBehavior = new IndustrialTelemetryIdempotentIngestionBehavior<RecordTelemetrySampleCommand, RecordTelemetrySampleResult>(staleRuleContext);
        var firstCommand = CreatePostgresRaceSample("pg-race-sample-001", new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var secondCommand = CreatePostgresRaceSample("pg-race-sample-002", new DateTimeOffset(2026, 6, 1, 12, 1, 0, TimeSpan.Zero));

        var stalePreparedResult = await staleRuleHandler.Handle(firstCommand, CancellationToken.None);
        await using (var updateContext = database.CreateContext())
        {
            var rule = await updateContext.AlarmRules.SingleAsync();
            rule.UpdateDefinition("TEMP_CRITICAL", "critical", "temperature", ">=", 90m, "celsius", true);
            await updateContext.SaveChangesAsync();
        }

        await using (var winningContext = database.CreateContext())
        {
            await new RecordTelemetrySampleCommandHandler(winningContext).Handle(secondCommand, CancellationToken.None);
            await winningContext.SaveChangesAsync();
        }

        var attempts = 0;
        await staleRuleBehavior.Handle(
            firstCommand,
            async ct =>
            {
                attempts++;
                var result = stalePreparedResult;
                if (attempts > 1)
                {
                    result = await staleRuleHandler.Handle(firstCommand, ct);
                }

                await staleRuleContext.SaveChangesAsync(ct);
                return result;
            },
            CancellationToken.None);

        await using var assertionContext = database.CreateContext();
        var alarms = await assertionContext.AlarmEvents.OrderBy(x => x.RaisedAtUtc).ToArrayAsync();
        Assert.Equal(2, attempts);
        var alarm = Assert.Single(alarms);
        Assert.Equal("TEMP_CRITICAL", alarm.AlarmCode);
        Assert.Equal("TEMP_RULE", alarm.ExternalAlarmId);
        Assert.Equal("temperature", alarm.TagKey);
        Assert.Equal("raised", alarm.Status);
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

    private static RecordTelemetrySampleCommand CreatePostgresRaceSample(string sourceSequence, DateTimeOffset bucketEndUtc)
    {
        return new RecordTelemetrySampleCommand(
            "org-001",
            "env-dev",
            "DEV-RACE-PG-01",
            "temperature",
            bucketEndUtc.AddMinutes(-1),
            bucketEndUtc,
            10,
            91m,
            96m,
            94m,
            sourceSequence,
            "SCADA-A",
            "opc-ua-cell-race");
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

    private sealed class IndustrialTelemetryPostgresDatabase : IAsyncDisposable
    {
        private readonly string adminConnectionString;
        private readonly string databaseName;

        private IndustrialTelemetryPostgresDatabase(string adminConnectionString, string connectionString, string databaseName)
        {
            this.adminConnectionString = adminConnectionString;
            ConnectionString = connectionString;
            this.databaseName = databaseName;
        }

        public string ConnectionString { get; }

        public static async Task<IndustrialTelemetryPostgresDatabase> CreateAsync(string baseConnectionString)
        {
            var baseBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString);
            var databaseName = $"nerv_iip_it_{Guid.NewGuid():N}";
            var adminBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                Database = string.IsNullOrWhiteSpace(baseBuilder.Database) ? "postgres" : baseBuilder.Database
            };
            var databaseBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                Database = databaseName
            };

            await using (var connection = new NpgsqlConnection(adminBuilder.ConnectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"CREATE DATABASE {QuoteIdentifier(databaseName)}";
                await command.ExecuteNonQueryAsync();
            }

            var database = new IndustrialTelemetryPostgresDatabase(adminBuilder.ConnectionString, databaseBuilder.ConnectionString, databaseName);
            await using var context = database.CreateContext();
            await context.Database.MigrateAsync();
            return database;
        }

        public ApplicationDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(ConnectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "industrial_telemetry"))
                .Options;
            return new ApplicationDbContext(options, new NoopMediator());
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(adminConnectionString);
            await connection.OpenAsync();
            await using (var terminateCommand = connection.CreateCommand())
            {
                terminateCommand.CommandText = "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @databaseName";
                terminateCommand.Parameters.AddWithValue("databaseName", databaseName);
                await terminateCommand.ExecuteNonQueryAsync();
            }

            await using var dropCommand = connection.CreateCommand();
            dropCommand.CommandText = $"DROP DATABASE IF EXISTS {QuoteIdentifier(databaseName)}";
            await dropCommand.ExecuteNonQueryAsync();
        }

        private static string QuoteIdentifier(string identifier)
        {
            return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
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

internal sealed class RealPostgresFactAttribute : FactAttribute
{
    public RealPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")))
        {
            Skip = "Set NERV_IIP_TEST_POSTGRES to run real PostgreSQL IndustrialTelemetry regressions.";
        }
    }
}
