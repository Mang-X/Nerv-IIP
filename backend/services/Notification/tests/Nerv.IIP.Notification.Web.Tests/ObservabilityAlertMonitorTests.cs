using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nerv.IIP.Contracts.AppHubQueries;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;
using Nerv.IIP.Notification.Domain.ObservabilityAlerts;
using Nerv.IIP.Notification.Web.Application.ObservabilityAlerts;

namespace Nerv.IIP.Notification.Web.Tests;

public sealed class ObservabilityAlertMonitorTests
{
    [Fact]
    public async Task Alert_monitor_submits_failure_once_then_resolved_after_recovery_across_independent_scopes()
    {
        var probe = new SequenceAlertProbe(
            new ObservabilityAlertSample("service-health:apphub", "apphub health", ObservabilityAlertStatus.Firing, "HTTP 503", "apphub"),
            new ObservabilityAlertSample("service-health:apphub", "apphub health", ObservabilityAlertStatus.Firing, "HTTP 503", "apphub"),
            new ObservabilityAlertSample("service-health:apphub", "apphub health", ObservabilityAlertStatus.Resolved, "HTTP 200", "apphub"));
        using var factory = new NotificationEndpointTests.NotificationWebApplicationFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Observability:Alerts:Enabled"] = "true",
                        ["Observability:Alerts:OrganizationId"] = "org-001",
                        ["Observability:Alerts:EnvironmentId"] = "env-001",
                        ["Observability:Alerts:RecipientRefs:0"] = "role:ops-admin",
                        ["Observability:Alerts:DedupeWindow"] = "00:30:00",
                        ["Observability:Alerts:SilentWindow"] = "00:10:00"
                    });
                });
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IObservabilityAlertProbe>();
                    services.AddSingleton(probe);
                    services.AddScoped<IObservabilityAlertProbe>(sp => sp.GetRequiredService<SequenceAlertProbe>());
                });
            });

        ObservabilityAlertMonitorResult first;
        ObservabilityAlertMonitorResult duplicate;
        ObservabilityAlertMonitorResult resolved;
        using (var firstScope = factory.Services.CreateScope())
        {
            first = await firstScope.ServiceProvider.GetRequiredService<ObservabilityAlertMonitor>()
                .CheckOnceAsync(DateTimeOffset.Parse("2026-07-05T01:00:00Z"), CancellationToken.None);
        }

        using (var duplicateScope = factory.Services.CreateScope())
        {
            duplicate = await duplicateScope.ServiceProvider.GetRequiredService<ObservabilityAlertMonitor>()
                .CheckOnceAsync(DateTimeOffset.Parse("2026-07-05T01:05:00Z"), CancellationToken.None);
        }

        using (var resolvedScope = factory.Services.CreateScope())
        {
            resolved = await resolvedScope.ServiceProvider.GetRequiredService<ObservabilityAlertMonitor>()
                .CheckOnceAsync(DateTimeOffset.Parse("2026-07-05T01:12:00Z"), CancellationToken.None);
        }

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var intents = await dbContext.NotificationIntents
            .Include(x => x.Messages)
            .OrderBy(x => x.CreatedAtUtc)
            .ToArrayAsync();

        Assert.Equal(1, first.SubmittedCount);
        Assert.Equal(0, duplicate.SubmittedCount);
        Assert.Equal(1, duplicate.SuppressedCount);
        Assert.Equal(1, resolved.SubmittedCount);
        Assert.Equal(2, intents.Length);
        Assert.Equal("observability", intents[0].SourceService);
        Assert.Equal("observability.AlertFiring", intents[0].SourceEventType);
        Assert.Equal(NotificationContractConstants.SeverityCritical, intents[0].Severity);
        Assert.Equal("observability-alert:service-health:apphub:202607050100", intents[0].DedupeKey);
        Assert.Equal(NotificationIntentTypes.Task, intents[0].IntentType);
        Assert.Equal("role:ops-admin", Assert.Single(intents[0].Messages).RecipientRef);
        Assert.Equal("observability.AlertResolved", intents[1].SourceEventType);
        Assert.Equal(NotificationContractConstants.SeverityInfo, intents[1].Severity);
        Assert.Equal(NotificationIntentTypes.Message, intents[1].IntentType);
        Assert.Contains("resolved", intents[1].Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Built_in_probes_cover_dlq_heartbeat_and_postgres_watermark_rules()
    {
        using var factory = new NotificationEndpointTests.NotificationWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        await AddDeadLetterAsync(factory, "event-observability-dlq", "operation-task-failed:observability-dlq");

        var options = Options.Create(new ObservabilityAlertOptions
        {
            Enabled = true,
            OrganizationId = "org-001",
            EnvironmentId = "env-001",
            AppHubBaseUrl = "http://apphub.local",
            Rules =
            [
                new ObservabilityAlertRuleOptions
                {
                    RuleId = "notification-dlq-backlog",
                    Name = "Notification CAP/DLQ backlog",
                    Kind = "cap-dlq-backlog",
                    Threshold = 1
                },
                new ObservabilityAlertRuleOptions
                {
                    RuleId = "connector-host-heartbeat-stale",
                    Name = "Connector Host heartbeat stale",
                    Kind = "connector-heartbeat-stale",
                    Threshold = 1,
                    HeartbeatMaxAge = TimeSpan.FromMinutes(5)
                },
                new ObservabilityAlertRuleOptions
                {
                    RuleId = "postgres-connection-watermark",
                    Name = "PostgreSQL connection watermark",
                    Kind = "postgres-watermark",
                    MetricName = "connections",
                    WatermarkPercent = 80
                }
            ]
        });
        var staleHeartbeatPayload = JsonSerializer.Serialize(new InstanceListResponse(
            1,
            250,
            1,
            [
                new InstanceListItem(
                    "connector-host",
                    "Connector Host",
                    "1.0.0",
                    "node-001",
                    "Node 001",
                    "connector-host-001",
                    "Connector Host 001",
                    "running",
                    "healthy",
                    DateTimeOffset.Parse("2026-07-05T01:00:00Z"),
                    DateTimeOffset.Parse("2026-07-05T01:00:00Z"))
            ]), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var dlqProbe = new NotificationDeadLetterBacklogAlertProbe(
            scope.ServiceProvider.GetRequiredService<Nerv.IIP.Messaging.CAP.IIntegrationEventDeadLetterStore>(),
            options);
        var heartbeatProbe = new AppHubConnectorHeartbeatAlertProbe(
            new StaticHttpClientFactory(staleHeartbeatPayload),
            options,
            NullLogger<AppHubConnectorHeartbeatAlertProbe>.Instance);
        var databaseWatermarkReader = new RecordingDatabaseWatermarkReader(91);
        var postgresProbe = new PostgreSqlWatermarkAlertProbe(
            databaseWatermarkReader,
            options,
            NullLogger<PostgreSqlWatermarkAlertProbe>.Instance);

        var now = DateTimeOffset.Parse("2026-07-05T01:10:00Z");
        var dlq = Assert.Single(await dlqProbe.CollectAsync(now, CancellationToken.None));
        var heartbeat = Assert.Single(await heartbeatProbe.CollectAsync(now, CancellationToken.None));
        var postgres = Assert.Single(await postgresProbe.CollectAsync(now, CancellationToken.None));

        Assert.Equal(ObservabilityAlertStatus.Firing, dlq.Status);
        Assert.Equal(ObservabilityAlertStatus.Firing, heartbeat.Status);
        Assert.Equal(ObservabilityAlertStatus.Firing, postgres.Status);
        Assert.Contains("stale", heartbeat.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("91", postgres.Summary, StringComparison.Ordinal);
        var request = Assert.Single(databaseWatermarkReader.Requests);
        Assert.Equal("NotificationDb", request.ConnectionStringName);
        Assert.Equal("connections", request.MetricName);
        Assert.Null(request.CapacityMegabytes);
    }

    private sealed class SequenceAlertProbe(params ObservabilityAlertSample[] samples) : IObservabilityAlertProbe
    {
        private int index;

        public Task<IReadOnlyCollection<ObservabilityAlertSample>> CollectAsync(DateTimeOffset now, CancellationToken cancellationToken)
        {
            var sample = samples[Math.Min(index, samples.Length - 1)];
            index++;
            return Task.FromResult<IReadOnlyCollection<ObservabilityAlertSample>>([sample]);
        }
    }

    private sealed class RecordingDatabaseWatermarkReader(double value) : IDatabaseWatermarkReader
    {
        public List<DatabaseWatermarkReadRequest> Requests { get; } = [];

        public Task<double?> ReadPercentAsync(DatabaseWatermarkReadRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult<double?>(value);
        }
    }

    private static async Task<Nerv.IIP.Messaging.CAP.IntegrationEventDeadLetterMessage> AddDeadLetterAsync(
        NotificationEndpointTests.NotificationWebApplicationFactory factory,
        string eventId,
        string idempotencyKey)
    {
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<Nerv.IIP.Messaging.CAP.IIntegrationEventDeadLetterStore>();
        return await store.AddAsync(
            Nerv.IIP.Messaging.CAP.IntegrationEventDeadLetterMessage.Create(
                "observability-alert-tests",
                new Nerv.IIP.Contracts.Ops.OperationTaskFailedIntegrationEvent(
                    EventId: eventId,
                    EventType: "ops.OperationTaskFailed",
                    EventVersion: 1,
                    OccurredAtUtc: DateTimeOffset.Parse("2026-07-05T01:00:00Z"),
                    SourceService: "ops",
                    CorrelationId: $"corr-{eventId}",
                    CausationId: $"cause-{eventId}",
                    OrganizationId: "org-001",
                    EnvironmentId: "env-001",
                    Actor: "connector-host-001",
                    IdempotencyKey: idempotencyKey,
                    Payload: new Nerv.IIP.Contracts.Ops.OperationTaskFailedPayload(
                        OperationTaskId: $"task-{eventId}",
                        AttemptId: $"attempt-{eventId}",
                        InstanceKey: "demo-api-001",
                        OperationCode: "lifecycle.restart",
                        FinishedAtUtc: DateTimeOffset.Parse("2026-07-05T01:00:05Z"),
                        FailureCode: "timeout")),
                "handler-retry-exhausted",
                "Simulated exhausted handler exception."),
            CancellationToken.None);
    }

    private sealed class StaticHttpClientFactory(string json) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new StaticJsonHandler(json));
        }
    }

    private sealed class StaticJsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
