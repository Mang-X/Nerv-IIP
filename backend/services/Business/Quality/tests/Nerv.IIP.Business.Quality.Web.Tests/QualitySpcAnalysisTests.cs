using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Quality.Web.Application.Queries.Spc;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Quality.Web.Tests;

public sealed class QualitySpcAnalysisTests
{
    [Fact]
    public async Task Control_chart_projection_detects_increasing_trend_for_sku_characteristic_and_work_center()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var plan = NewVariablePlan("IQP-SPC-TREND-001", lowerSpecLimit: 9m, upperSpecLimit: 12m);
        dbContext.InspectionPlans.Add(plan);
        AddMeasurements(dbContext, plan, [10.0m, 10.1m, 10.2m, 10.3m, 10.4m, 10.5m, 10.6m, 10.7m, 10.8m, 10.9m, 11.0m, 11.1m]);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new QuerySpcControlChartQueryHandler(dbContext).Handle(
            new QuerySpcControlChartQuery("org-001", "env-dev", "SKU-RM-1000", "length", "WC-MIX-01", 2, 50),
            CancellationToken.None);

        Assert.Equal("SKU-RM-1000", response.SkuCode);
        Assert.Equal("length", response.CharacteristicCode);
        Assert.Equal("WC-MIX-01", response.WorkCenterId);
        Assert.Equal(12, response.DataPoints.Count);
        Assert.Equal(6, response.Subgroups.Count);
        Assert.False(response.ControlLimits.Locked);
        Assert.Contains(response.RuleViolations, x => x.Rule == QualitySpcRuleCodes.TrendIncreasing);
    }

    [Fact]
    public async Task Process_capability_matches_manual_cp_cpk_sample()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var plan = NewVariablePlan("IQP-SPC-CPK-001", lowerSpecLimit: 8m, upperSpecLimit: 12m);
        dbContext.InspectionPlans.Add(plan);
        AddMeasurements(dbContext, plan, [9m, 11m, 9m, 11m]);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new QueryProcessCapabilityQueryHandler(dbContext).Handle(
            new QueryProcessCapabilityQuery("org-001", "env-dev", "SKU-RM-1000", "length", "WC-MIX-01", Take: 50, SubgroupSize: 2),
            CancellationToken.None);

        Assert.Equal(4, response.SampleCount);
        Assert.Equal(10m, response.Mean);
        Assert.Equal(1.77305m, Math.Round(response.StandardDeviation, 5, MidpointRounding.AwayFromZero));
        Assert.Equal(0.38m, Math.Round(response.Cp!.Value, 2, MidpointRounding.AwayFromZero));
        Assert.Equal(0.38m, Math.Round(response.Cpk!.Value, 2, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public async Task Process_capability_returns_null_cpk_when_spec_limits_are_missing()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var plan = NewVariablePlan("IQP-SPC-NOSPEC-001", lowerSpecLimit: null, upperSpecLimit: null);
        dbContext.InspectionPlans.Add(plan);
        AddMeasurements(dbContext, plan, [9m, 11m, 9m, 11m]);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new QueryProcessCapabilityQueryHandler(dbContext).Handle(
            new QueryProcessCapabilityQuery("org-001", "env-dev", "SKU-RM-1000", "length", "WC-MIX-01", Take: 50, SubgroupSize: 2),
            CancellationToken.None);

        Assert.Null(response.Cp);
        Assert.Null(response.Cpk);
    }

    [Fact]
    public async Task Evaluate_control_chart_publishes_quality_spc_alert_event_for_detected_trend()
    {
        await using var provider = CreateInMemoryMediatorProvider();
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var plan = NewVariablePlan("IQP-SPC-ALERT-001", lowerSpecLimit: 9m, upperSpecLimit: 12m);
            dbContext.InspectionPlans.Add(plan);
            AddMeasurements(dbContext, plan, [10.0m, 10.1m, 10.2m, 10.3m, 10.4m, 10.5m, 10.6m, 10.7m, 10.8m, 10.9m, 11.0m, 11.1m]);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        using (var scope = provider.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await sender.Send(
                new EvaluateSpcControlChartCommand("org-001", "env-dev", "SKU-RM-1000", "length", "WC-MIX-01", 2, 50),
                CancellationToken.None);
        }

        var publisher = provider.GetRequiredService<RecordingIntegrationEventPublisher>();
        var alert = Assert.Single(publisher.Published.OfType<SpcAlertRaisedIntegrationEvent>());
        Assert.Equal(QualityIntegrationEventTypes.SpcAlertRaised, alert.EventType);
        Assert.Equal("quality-spc-alert:org-001:env-dev:SKU-RM-1000:length:WC-MIX-01", alert.Payload.AlertKey);
        Assert.Contains(QualitySpcRuleCodes.TrendIncreasing, alert.Payload.RuleCodes);
        Assert.Equal("quality-spc-alert", alert.Payload.ResourceType);
    }

    [Fact]
    public async Task Spc_alert_event_idempotency_key_includes_latest_measurement_occurrence()
    {
        await using var provider = CreateInMemoryMediatorProvider();
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var plan = NewVariablePlan("IQP-SPC-RE-ALERT-001", lowerSpecLimit: 9m, upperSpecLimit: 12m);
            dbContext.InspectionPlans.Add(plan);
            AddMeasurements(
                dbContext,
                plan,
                [10.0m, 10.1m, 10.2m, 10.3m, 10.4m, 10.5m, 10.6m, 10.7m, 10.8m, 10.9m, 11.0m, 11.1m],
                firstCreatedAtUtc: new DateTime(2026, 7, 8, 1, 0, 0, DateTimeKind.Utc));
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        using (var scope = provider.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await sender.Send(new EvaluateSpcControlChartCommand("org-001", "env-dev", "SKU-RM-1000", "length", "WC-MIX-01", 2, 50), CancellationToken.None);
            await sender.Send(new EvaluateSpcControlChartCommand("org-001", "env-dev", "SKU-RM-1000", "length", "WC-MIX-01", 2, 50), CancellationToken.None);
        }

        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var plan = await dbContext.InspectionPlans
                .Include(x => x.Characteristics)
                .SingleAsync(x => x.PlanCode == "IQP-SPC-RE-ALERT-001", CancellationToken.None);
            AddMeasurements(
                dbContext,
                plan,
                [11.2m, 11.3m],
                startIndex: 12,
                firstCreatedAtUtc: new DateTime(2026, 7, 8, 2, 0, 0, DateTimeKind.Utc));
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        using (var scope = provider.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await sender.Send(new EvaluateSpcControlChartCommand("org-001", "env-dev", "SKU-RM-1000", "length", "WC-MIX-01", 2, 50), CancellationToken.None);
        }

        var alerts = provider.GetRequiredService<RecordingIntegrationEventPublisher>().Published.OfType<SpcAlertRaisedIntegrationEvent>().ToArray();
        Assert.Equal(3, alerts.Length);
        Assert.Equal(alerts[0].Payload.AlertKey, alerts[2].Payload.AlertKey);
        Assert.Equal(alerts[0].IdempotencyKey, alerts[1].IdempotencyKey);
        Assert.NotEqual(alerts[0].IdempotencyKey, alerts[2].IdempotencyKey);
        Assert.Contains("2026-07-08T01:12:00.0000000+00:00", alerts[0].IdempotencyKey, StringComparison.Ordinal);
        Assert.Contains("2026-07-08T02:02:00.0000000+00:00", alerts[2].IdempotencyKey, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inspection_result_event_triggers_spc_evaluation_for_measured_characteristics()
    {
        await using var provider = CreateInMemoryMediatorProvider();
        InspectionPlan plan;
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            plan = NewVariablePlan("IQP-SPC-AUTO-001", lowerSpecLimit: 9m, upperSpecLimit: 12m);
            dbContext.InspectionPlans.Add(plan);
            AddMeasurements(dbContext, plan, [10.0m, 10.1m, 10.2m, 10.3m, 10.4m, 10.5m, 10.6m, 10.7m, 10.8m, 10.9m, 11.0m, 11.1m]);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        using (var scope = provider.CreateScope())
        {
            var handler = new InspectionResultIntegrationEventHandlerForEvaluateSpc(
                scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
                scope.ServiceProvider.GetRequiredService<ISender>(),
                new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(NewInspectionResultEvent(plan), CancellationToken.None);
        }

        var alert = Assert.Single(provider.GetRequiredService<RecordingIntegrationEventPublisher>().Published.OfType<SpcAlertRaisedIntegrationEvent>());
        Assert.Equal("quality-spc-alert:org-001:env-dev:SKU-RM-1000:length:WC-MIX-01", alert.Payload.AlertKey);
        Assert.Contains(QualitySpcRuleCodes.TrendIncreasing, alert.Payload.RuleCodes);
    }

    [Fact]
    public async Task Inspection_result_event_ignores_spc_warmup_before_complete_subgroup()
    {
        await using var provider = CreateInMemoryMediatorProvider();
        InspectionPlan plan;
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            plan = NewVariablePlan("IQP-SPC-WARMUP-001", lowerSpecLimit: 9m, upperSpecLimit: 12m);
            dbContext.InspectionPlans.Add(plan);
            AddMeasurements(dbContext, plan, [10.0m]);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        using (var scope = provider.CreateScope())
        {
            var handler = new InspectionResultIntegrationEventHandlerForEvaluateSpc(
                scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
                scope.ServiceProvider.GetRequiredService<ISender>(),
                deadLetters);
            await handler.HandleAsync(NewInspectionResultEvent(plan), CancellationToken.None);
        }

        Assert.Empty(provider.GetRequiredService<RecordingIntegrationEventPublisher>().Published.OfType<SpcAlertRaisedIntegrationEvent>());
        Assert.Empty(deadLetters.Messages);
    }

    [Fact]
    public async Task Spc_rule_detection_reports_maximal_trend_run()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var plan = NewVariablePlan("IQP-SPC-MAX-TREND-001", lowerSpecLimit: 9m, upperSpecLimit: 12m);
        dbContext.InspectionPlans.Add(plan);
        AddMeasurements(dbContext, plan, [10.0m, 10.1m, 10.2m, 10.3m, 10.4m, 10.5m, 10.6m, 10.7m, 10.8m, 10.9m, 11.0m, 11.1m, 11.2m, 11.3m, 11.4m, 11.5m]);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new QuerySpcControlChartQueryHandler(dbContext).Handle(
            new QuerySpcControlChartQuery("org-001", "env-dev", "SKU-RM-1000", "length", "WC-MIX-01", 2, 50),
            CancellationToken.None);

        var trend = Assert.Single(response.RuleViolations, x => x.Rule == QualitySpcRuleCodes.TrendIncreasing);
        Assert.Equal(1, trend.StartSubgroupIndex);
        Assert.Equal(8, trend.EndSubgroupIndex);
    }

    [QualityPostgresFact]
    public async Task Postgres_spc_point_projection_materializes_latest_points_without_client_translation()
    {
        var connectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
        await using var database = await TemporaryPostgresDatabase.CreateAsync(connectionString, "quality_spc");
        await using var provider = CreatePostgresProvider(database.ConnectionString);
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.Database.MigrateAsync(CancellationToken.None);
            var plan = NewVariablePlan("IQP-SPC-PG-001", lowerSpecLimit: 9m, upperSpecLimit: 12m);
            dbContext.InspectionPlans.Add(plan);
            AddMeasurements(dbContext, plan, [10.0m, 10.1m, 10.2m, 10.3m]);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var response = await new QuerySpcControlChartQueryHandler(dbContext).Handle(
                new QuerySpcControlChartQuery("org-001", "env-dev", "SKU-RM-1000", "length", "WC-MIX-01", 2, 4),
                CancellationToken.None);

            Assert.Equal(4, response.DataPoints.Count);
            Assert.Equal(2, response.Subgroups.Count);
        }
    }

    private static InspectionPlan NewVariablePlan(string planCode, decimal? lowerSpecLimit, decimal? upperSpecLimit)
    {
        var plan = InspectionPlan.Create(
            "org-001",
            "env-dev",
            planCode,
            "operation",
            "SKU-RM-1000",
            null,
            "WC-MIX-01",
            null,
            "mes-operation");
        plan.AddCharacteristic(
            "length",
            "Length",
            "caliper",
            "major",
            required: true,
            "subgroup-2",
            InspectionCharacteristicTypes.Variable,
            nominalValue: 10m,
            lowerSpecLimit,
            upperSpecLimit,
            "mm",
            samplingPlan: null);
        plan.Activate();
        return plan;
    }

    private static void AddMeasurements(
        ApplicationDbContext dbContext,
        InspectionPlan plan,
        IReadOnlyCollection<decimal> measurements,
        int startIndex = 0,
        DateTime? firstCreatedAtUtc = null)
    {
        var index = startIndex;
        foreach (var measurement in measurements)
        {
            index++;
            var record = InspectionRecord.CreateFromPlan(
                plan,
                "operation",
                "mes-operation",
                $"WO-SPC-{index:000}",
                "SKU-RM-1000",
                1m,
                null,
                null,
                null,
                [InspectionResultLineInput.Measure("length", measurement, "mm", [])],
                measurement is < 9m or > 12m ? "out-of-specification" : null,
                []);
            dbContext.InspectionRecords.Add(record);
            if (firstCreatedAtUtc.HasValue)
            {
                var createdAtUtc = firstCreatedAtUtc.Value.AddMinutes(index - startIndex);
                dbContext.Entry(record).Property(x => x.CreatedAtUtc).CurrentValue = createdAtUtc;
                dbContext.Entry(record).Property(x => x.UpdatedAtUtc).CurrentValue = createdAtUtc;
            }
        }
    }

    private static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        var databaseName = $"quality-spc-{Guid.NewGuid():N}";
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        return services.BuildServiceProvider();
    }

    private static ServiceProvider CreateInMemoryMediatorProvider()
    {
        var services = new ServiceCollection();
        var databaseName = $"quality-spc-uow-{Guid.NewGuid():N}";
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly)
                .AddUnitOfWorkBehaviors());
        services.AddDbContext<ApplicationDbContext>(options => options
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddUnitOfWork<ApplicationDbContext>();
        services.AddIntegrationEvents(typeof(Program));
        services.AddSingleton<IQualityIntegrationEventContextAccessor, FixedQualityIntegrationEventContextAccessor>();
        services.AddSingleton<RecordingIntegrationEventPublisher>();
        services.AddSingleton<IIntegrationEventPublisher>(serviceProvider =>
            serviceProvider.GetRequiredService<RecordingIntegrationEventPublisher>());
        return services.BuildServiceProvider();
    }

    private static ServiceProvider CreatePostgresProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "quality")));
        return services.BuildServiceProvider();
    }

    private static InspectionResultIntegrationEvent NewInspectionResultEvent(InspectionPlan plan)
    {
        return new InspectionResultIntegrationEvent(
            "evt-inspection-spc-001",
            QualityIntegrationEventTypes.InspectionPassed,
            QualityIntegrationEventVersions.V1,
            DateTimeOffset.UtcNow,
            QualityIntegrationEventSources.BusinessQuality,
            "corr-spc-001",
            "cause-spc-001",
            "org-001",
            "env-dev",
            "system:business-quality",
            "quality:inspection-passed:org-001:env-dev:mes-operation:WO-SPC-012",
            new InspectionResultPayload(
                "inspection-record-012",
                plan.Id.ToString(),
                "operation",
                "mes-operation",
                "WO-SPC-012",
                "SKU-RM-1000",
                1m,
                "passed",
                null,
                [],
                DateTimeOffset.UtcNow,
                ResultLines:
                [
                    new InspectionResultLinePayload("length", 11.1m, "11.1", "mm", "passed", null, null),
                    new InspectionResultLinePayload("appearance", null, "ok", null, "passed", null, null),
                ]));
    }

    private sealed class FixedQualityIntegrationEventContextAccessor : IQualityIntegrationEventContextAccessor
    {
        public QualityIntegrationEventContext GetContext()
        {
            return new QualityIntegrationEventContext(
                "corr-spc-001",
                "cause-spc-001",
                "system:business-quality");
        }
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

    private sealed class InMemoryIntegrationEventDeadLetterStore : IIntegrationEventDeadLetterStore
    {
        public List<IntegrationEventDeadLetterMessage> Messages { get; } = [];

        public Task<IntegrationEventDeadLetterMessage> AddAsync(IntegrationEventDeadLetterMessage message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.FromResult(message);
        }

        public Task<IReadOnlyList<IntegrationEventDeadLetterMessage>> AddRangeAsync(IReadOnlyCollection<IntegrationEventDeadLetterMessage> messages, CancellationToken cancellationToken)
        {
            Messages.AddRange(messages);
            return Task.FromResult<IReadOnlyList<IntegrationEventDeadLetterMessage>>(messages.ToArray());
        }

        public Task<IReadOnlyList<IntegrationEventDeadLetterMessage>> ListAsync(string? consumerName, IntegrationEventDeadLetterStatus? status, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IntegrationEventDeadLetterMessage>>(Messages);

        public Task<IReadOnlyList<IntegrationEventDeadLetterMessage>> ListAsync(IntegrationEventDeadLetterQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IntegrationEventDeadLetterMessage>>(Messages);

        public Task<IntegrationEventDeadLetterMetrics> GetMetricsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new IntegrationEventDeadLetterMetrics(0, 0, 0, 0, []));

        public Task<IntegrationEventDeadLetterMessage?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Messages.SingleOrDefault(x => x.Id == id));

        public Task MarkReplayedAsync(Guid id, DateTimeOffset replayedAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task MarkFailedAsync(Guid id, string failureCode, string failureMessage, DateTimeOffset failedAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task MarkIgnoredAsync(Guid id, string reason, DateTimeOffset ignoredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TemporaryPostgresDatabase : IAsyncDisposable
    {
        private readonly string adminConnectionString;
        private readonly string databaseName;

        private TemporaryPostgresDatabase(string adminConnectionString, string connectionString, string databaseName)
        {
            this.adminConnectionString = adminConnectionString;
            ConnectionString = connectionString;
            this.databaseName = databaseName;
        }

        public string ConnectionString { get; }

        public static async Task<TemporaryPostgresDatabase> CreateAsync(string baseConnectionString, string prefix)
        {
            var baseBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString);
            var adminBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                Database = string.IsNullOrWhiteSpace(baseBuilder.Database) ? "postgres" : baseBuilder.Database,
                Pooling = false,
            };
            var databaseName = $"{prefix}_{Guid.NewGuid():N}";
            var databaseBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                Database = databaseName,
                Pooling = false,
            };
            await using var connection = new NpgsqlConnection(adminBuilder.ConnectionString);
            await connection.OpenAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand($"""CREATE DATABASE "{databaseName}";""", connection);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
            return new TemporaryPostgresDatabase(adminBuilder.ConnectionString, databaseBuilder.ConnectionString, databaseName);
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(adminConnectionString);
            await connection.OpenAsync(CancellationToken.None);
            await using (var terminate = new NpgsqlCommand(
                """
                SELECT pg_terminate_backend(pid)
                FROM pg_stat_activity
                WHERE datname = @databaseName AND pid <> pg_backend_pid();
                """,
                connection))
            {
                terminate.Parameters.AddWithValue("databaseName", databaseName);
                await terminate.ExecuteNonQueryAsync(CancellationToken.None);
            }

            await using var drop = new NpgsqlCommand($"""DROP DATABASE IF EXISTS "{databaseName}";""", connection);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
