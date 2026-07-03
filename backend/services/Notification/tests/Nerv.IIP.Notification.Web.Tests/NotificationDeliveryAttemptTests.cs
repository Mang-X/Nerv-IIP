using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Infrastructure.Repositories;
using Nerv.IIP.Notification.Web.Application.Commands.Notifications;
using Nerv.IIP.Notification.Web.Application.Notifications;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Notification.Web.Tests;

public sealed class NotificationDeliveryAttemptTests
{
    [Fact]
    public async Task Submit_intent_dispatches_configured_external_channels_and_records_attempts()
    {
        await using var fixture = await NotificationSqliteFixture.CreateAsync();
        fixture.Db.RecipientChannelBindings.Add(NotificationRecipientChannelBinding.Create(
            "org-001",
            "env-dev",
            "user:admin",
            NotificationDeliveryChannels.Email,
            "admin@example.test",
            DateTimeOffset.Parse("2026-07-03T00:00:00Z")));
        fixture.Db.NotificationSubscriptions.Add(NotificationSubscription.Create(
            "org-001",
            "env-dev",
            "user:admin",
            "ops.OperationTaskFailed",
            NotificationDeliveryChannels.Email,
            DateTimeOffset.Parse("2026-07-03T00:00:00Z")));
        await fixture.Db.SaveChangesAsync();

        var emailProvider = new RecordingDeliveryProvider(NotificationDeliveryChannels.Email);
        var deliveryService = fixture.CreateDeliveryService(emailProvider);
        var handler = fixture.CreateSubmitHandler(deliveryService);

        await handler.Handle(new SubmitNotificationIntentCommand(
            "org-001",
            "env-dev",
            CreateIntent("dedupe-email-dispatch", ["user:admin"]),
            DateTimeOffset.Parse("2026-07-03T00:01:00Z")), CancellationToken.None);

        Assert.Empty(emailProvider.Sent);
        await deliveryService.DispatchDueAttemptsAsync(DateTimeOffset.Parse("2026-07-03T00:01:01Z"), CancellationToken.None);

        var attempts = await fixture.Db.DeliveryAttempts.OrderBy(x => x.Channel).ToListAsync();
        Assert.Collection(
            attempts,
            attempt =>
            {
                Assert.Equal(NotificationDeliveryChannels.Email, attempt.Channel);
                Assert.Equal(NotificationDeliveryAttemptStatuses.Succeeded, attempt.Status);
                Assert.Equal("admin@example.test", attempt.RecipientAddress);
            },
            attempt =>
            {
                Assert.Equal(NotificationDeliveryChannels.InApp, attempt.Channel);
                Assert.Equal(NotificationDeliveryAttemptStatuses.Succeeded, attempt.Status);
            });
        var sent = Assert.Single(emailProvider.Sent);
        Assert.Equal("admin@example.test", sent.RecipientAddress);
        Assert.Equal("Restart failed", sent.Title);
    }

    [Fact]
    public async Task Submit_intent_honors_disabled_preference_for_non_critical_notifications()
    {
        await using var fixture = await NotificationSqliteFixture.CreateAsync();
        fixture.Db.RecipientChannelBindings.Add(NotificationRecipientChannelBinding.Create(
            "org-001",
            "env-dev",
            "user:admin",
            NotificationDeliveryChannels.Email,
            "admin@example.test",
            DateTimeOffset.Parse("2026-07-03T00:00:00Z")));
        fixture.Db.NotificationSubscriptions.Add(NotificationSubscription.Create(
            "org-001",
            "env-dev",
            "user:admin",
            "ops.OperationTaskFailed",
            NotificationDeliveryChannels.Email,
            DateTimeOffset.Parse("2026-07-03T00:00:00Z")));
        fixture.Db.NotificationPreferences.Add(NotificationPreference.Create(
            "org-001",
            "env-dev",
            "user:admin",
            "ops.OperationTaskFailed",
            NotificationDeliveryChannels.Email,
            enabled: false,
            DateTimeOffset.Parse("2026-07-03T00:00:00Z")));
        await fixture.Db.SaveChangesAsync();

        var emailProvider = new RecordingDeliveryProvider(NotificationDeliveryChannels.Email);
        var handler = fixture.CreateSubmitHandler(emailProvider);

        await handler.Handle(new SubmitNotificationIntentCommand(
            "org-001",
            "env-dev",
            CreateIntent("dedupe-email-muted", ["user:admin"], severity: NotificationContractConstants.SeverityWarning),
            DateTimeOffset.Parse("2026-07-03T00:01:00Z")), CancellationToken.None);

        Assert.Empty(emailProvider.Sent);
        var attempt = Assert.Single(await fixture.Db.DeliveryAttempts.ToListAsync());
        Assert.Equal(NotificationDeliveryChannels.InApp, attempt.Channel);
    }

    [Fact]
    public async Task Critical_notification_forces_external_channel_even_when_preference_is_disabled()
    {
        await using var fixture = await NotificationSqliteFixture.CreateAsync();
        fixture.Db.RecipientChannelBindings.Add(NotificationRecipientChannelBinding.Create(
            "org-001",
            "env-dev",
            "user:admin",
            NotificationDeliveryChannels.WeCom,
            "wecom-user-001",
            DateTimeOffset.Parse("2026-07-03T00:00:00Z")));
        fixture.Db.NotificationPreferences.Add(NotificationPreference.Create(
            "org-001",
            "env-dev",
            "user:admin",
            "industrialTelemetry.AlarmRaised",
            NotificationDeliveryChannels.WeCom,
            enabled: false,
            DateTimeOffset.Parse("2026-07-03T00:00:00Z")));
        await fixture.Db.SaveChangesAsync();

        var weComProvider = new RecordingDeliveryProvider(NotificationDeliveryChannels.WeCom);
        var deliveryService = fixture.CreateDeliveryService(weComProvider);
        var handler = fixture.CreateSubmitHandler(deliveryService);

        await handler.Handle(new SubmitNotificationIntentCommand(
            "org-001",
            "env-dev",
            CreateIntent(
                "dedupe-critical-wecom",
                ["user:admin"],
                sourceEventType: "industrialTelemetry.AlarmRaised",
                severity: "Critical"),
            DateTimeOffset.Parse("2026-07-03T00:01:00Z")), CancellationToken.None);

        await deliveryService.DispatchDueAttemptsAsync(DateTimeOffset.Parse("2026-07-03T00:01:01Z"), CancellationToken.None);

        Assert.Single(weComProvider.Sent);
        Assert.Contains(await fixture.Db.DeliveryAttempts.ToListAsync(), attempt =>
            attempt.Channel == NotificationDeliveryChannels.WeCom
            && attempt.Status == NotificationDeliveryAttemptStatuses.Succeeded);
    }

    [Fact]
    public async Task Failed_external_delivery_is_recorded_as_pending_retry_and_can_be_retried()
    {
        await using var fixture = await NotificationSqliteFixture.CreateAsync();
        fixture.Db.RecipientChannelBindings.Add(NotificationRecipientChannelBinding.Create(
            "org-001",
            "env-dev",
            "user:admin",
            NotificationDeliveryChannels.Webhook,
            "https://hooks.example.test/notify",
            DateTimeOffset.Parse("2026-07-03T00:00:00Z")));
        fixture.Db.NotificationSubscriptions.Add(NotificationSubscription.Create(
            "org-001",
            "env-dev",
            "user:admin",
            "ops.OperationTaskFailed",
            NotificationDeliveryChannels.Webhook,
            DateTimeOffset.Parse("2026-07-03T00:00:00Z")));
        await fixture.Db.SaveChangesAsync();

        var webhookProvider = new RecordingDeliveryProvider(NotificationDeliveryChannels.Webhook)
        {
            NextResult = NotificationDeliveryProviderResult.Failed("provider-timeout"),
        };
        var deliveryService = fixture.CreateDeliveryService(webhookProvider);
        var handler = fixture.CreateSubmitHandler(deliveryService);

        await handler.Handle(new SubmitNotificationIntentCommand(
            "org-001",
            "env-dev",
            CreateIntent("dedupe-webhook-retry", ["user:admin"]),
            DateTimeOffset.Parse("2026-07-03T00:01:00Z")), CancellationToken.None);

        await deliveryService.DispatchDueAttemptsAsync(DateTimeOffset.Parse("2026-07-03T00:01:01Z"), CancellationToken.None);

        var failedAttempt = await fixture.Db.DeliveryAttempts.SingleAsync(x => x.Channel == NotificationDeliveryChannels.Webhook);
        Assert.Equal(NotificationDeliveryAttemptStatuses.PendingRetry, failedAttempt.Status);
        Assert.Equal("provider-timeout", failedAttempt.FailureReason);
        Assert.Equal(DateTimeOffset.Parse("2026-07-03T00:03:01Z"), failedAttempt.NextRetryAtUtc);

        webhookProvider.NextResult = NotificationDeliveryProviderResult.Succeeded("remote-001");
        await deliveryService.DispatchDueAttemptsAsync(DateTimeOffset.Parse("2026-07-03T00:03:01Z"), CancellationToken.None);

        Assert.Equal(NotificationDeliveryAttemptStatuses.Succeeded, failedAttempt.Status);
        Assert.Equal(2, failedAttempt.AttemptNo);
        Assert.Null(failedAttempt.NextRetryAtUtc);
    }

    [Fact]
    public async Task External_delivery_rate_limit_records_pending_retry_without_calling_provider()
    {
        await using var fixture = await NotificationSqliteFixture.CreateAsync();
        foreach (var recipient in new[] { "user:admin", "user:operator" })
        {
            fixture.Db.RecipientChannelBindings.Add(NotificationRecipientChannelBinding.Create(
                "org-001",
                "env-dev",
                recipient,
                NotificationDeliveryChannels.Email,
                $"{recipient.Replace("user:", string.Empty, StringComparison.Ordinal)}@example.test",
                DateTimeOffset.Parse("2026-07-03T00:00:00Z")));
            fixture.Db.NotificationSubscriptions.Add(NotificationSubscription.Create(
                "org-001",
                "env-dev",
                recipient,
                "ops.OperationTaskFailed",
                NotificationDeliveryChannels.Email,
                DateTimeOffset.Parse("2026-07-03T00:00:00Z")));
        }

        await fixture.Db.SaveChangesAsync();
        var emailProvider = new RecordingDeliveryProvider(NotificationDeliveryChannels.Email);
        var deliveryService = fixture.CreateDeliveryService([emailProvider], emailMaxPerMinute: 1);
        var handler = fixture.CreateSubmitHandler(deliveryService);

        await handler.Handle(new SubmitNotificationIntentCommand(
            "org-001",
            "env-dev",
            CreateIntent("dedupe-email-rate-limit", ["user:admin", "user:operator"]),
            DateTimeOffset.Parse("2026-07-03T00:01:00Z")), CancellationToken.None);

        await deliveryService.DispatchDueAttemptsAsync(DateTimeOffset.Parse("2026-07-03T00:01:01Z"), CancellationToken.None);

        Assert.Single(emailProvider.Sent);
        var limited = Assert.Single(await fixture.Db.DeliveryAttempts
            .Where(x => x.Channel == NotificationDeliveryChannels.Email && x.Status == NotificationDeliveryAttemptStatuses.PendingRetry)
            .ToListAsync());
        Assert.Equal("rate-limit", limited.FailureReason);
        Assert.Equal(DateTimeOffset.Parse("2026-07-03T00:03:01Z"), limited.NextRetryAtUtc);
    }

    [Fact]
    public async Task External_delivery_rate_limit_is_shared_across_delivery_service_instances()
    {
        await using var fixture = await NotificationSqliteFixture.CreateAsync();
        fixture.Db.RecipientChannelBindings.Add(NotificationRecipientChannelBinding.Create(
            "org-001",
            "env-dev",
            "user:admin",
            NotificationDeliveryChannels.Email,
            "admin@example.test",
            DateTimeOffset.Parse("2026-07-03T00:00:00Z")));
        fixture.Db.NotificationSubscriptions.Add(NotificationSubscription.Create(
            "org-001",
            "env-dev",
            "user:admin",
            "ops.OperationTaskFailed",
            NotificationDeliveryChannels.Email,
            DateTimeOffset.Parse("2026-07-03T00:00:00Z")));
        await fixture.Db.SaveChangesAsync();

        var firstProvider = new RecordingDeliveryProvider(NotificationDeliveryChannels.Email);
        var firstDeliveryService = fixture.CreateDeliveryService([firstProvider], emailMaxPerMinute: 1);
        await fixture.CreateSubmitHandler(firstDeliveryService).Handle(new SubmitNotificationIntentCommand(
            "org-001",
            "env-dev",
            CreateIntent("dedupe-email-rate-limit-first", ["user:admin"]),
            DateTimeOffset.Parse("2026-07-03T00:01:00Z")), CancellationToken.None);
        await firstDeliveryService.DispatchDueAttemptsAsync(DateTimeOffset.Parse("2026-07-03T00:01:01Z"), CancellationToken.None);

        var secondProvider = new RecordingDeliveryProvider(NotificationDeliveryChannels.Email);
        var secondDeliveryService = fixture.CreateDeliveryService([secondProvider], emailMaxPerMinute: 1);
        await fixture.CreateSubmitHandler(secondDeliveryService).Handle(new SubmitNotificationIntentCommand(
            "org-001",
            "env-dev",
            CreateIntent("dedupe-email-rate-limit-second", ["user:admin"]),
            DateTimeOffset.Parse("2026-07-03T00:01:10Z")), CancellationToken.None);
        await secondDeliveryService.DispatchDueAttemptsAsync(DateTimeOffset.Parse("2026-07-03T00:01:11Z"), CancellationToken.None);

        Assert.Single(firstProvider.Sent);
        Assert.Empty(secondProvider.Sent);
        Assert.Equal(1, await fixture.Db.DeliveryAttempts.CountAsync(x =>
            x.Channel == NotificationDeliveryChannels.Email
            && x.Status == NotificationDeliveryAttemptStatuses.PendingRetry
            && x.FailureReason == "rate-limit"));
    }

    [Fact]
    public async Task Submit_intent_records_succeeded_in_app_delivery_attempt_for_each_message()
    {
        await using var fixture = await NotificationSqliteFixture.CreateAsync();
        var handler = fixture.CreateSubmitHandler();

        var response = await handler.Handle(new SubmitNotificationIntentCommand(
            "org-001",
            "env-dev",
            CreateIntent("dedupe-delivery-attempt", ["user:admin", "user:operator"]),
            DateTimeOffset.Parse("2026-06-29T00:00:00Z")), CancellationToken.None);

        var attempts = await fixture.Db.DeliveryAttempts.OrderBy(x => x.NotificationMessageId).ToListAsync();
        Assert.False(response.Duplicate);
        Assert.Equal(response.Messages.Select(x => x.MessageId).Order(), attempts.Select(x => x.NotificationMessageId.Id.ToString()).Order());
        Assert.All(attempts, attempt =>
        {
            Assert.Equal(NotificationDeliveryChannels.InApp, attempt.Channel);
            Assert.Equal(NotificationDeliveryAttemptStatuses.Succeeded, attempt.Status);
            Assert.Equal(1, attempt.AttemptNo);
            Assert.False(attempt.NextRetryAtUtc.HasValue);
            Assert.Null(attempt.FailureReason);
        });
    }

    [Fact]
    public void Delivery_attempt_failed_status_schedules_retry_until_dead_lettered()
    {
        var messageId = new NotificationMessageId(Guid.CreateVersion7());
        var now = DateTimeOffset.Parse("2026-06-29T00:00:00Z");
        var attempt = DeliveryAttempt.Start(messageId, NotificationDeliveryChannels.InApp, now);

        attempt.MarkFailed("provider-timeout", now.AddSeconds(1), maxAttempts: 3, retryDelay: TimeSpan.FromMinutes(2));

        Assert.Equal(NotificationDeliveryAttemptStatuses.PendingRetry, attempt.Status);
        Assert.Equal(1, attempt.AttemptNo);
        Assert.Equal(now.AddMinutes(2).AddSeconds(1), attempt.NextRetryAtUtc);
        Assert.Equal("provider-timeout", attempt.FailureReason);

        attempt.StartRetry(now.AddMinutes(2).AddSeconds(2));
        attempt.MarkFailed("provider-timeout", now.AddMinutes(2).AddSeconds(3), maxAttempts: 2, retryDelay: TimeSpan.FromMinutes(2));

        Assert.Equal(NotificationDeliveryAttemptStatuses.DeadLettered, attempt.Status);
        Assert.Equal(2, attempt.AttemptNo);
        Assert.Null(attempt.NextRetryAtUtc);
    }

    [Fact]
    public void Delivery_attempt_rejects_non_positive_max_attempts()
    {
        var messageId = new NotificationMessageId(Guid.CreateVersion7());
        var now = DateTimeOffset.Parse("2026-06-29T00:00:00Z");
        var attempt = DeliveryAttempt.Start(messageId, NotificationDeliveryChannels.InApp, now);

        var exception = Assert.Throws<KnownException>(() =>
            attempt.MarkFailed("provider-timeout", now.AddSeconds(1), maxAttempts: 0, retryDelay: TimeSpan.FromMinutes(2)));

        Assert.Equal("Delivery max attempts must be positive.", exception.Message);
    }

    [Fact]
    public async Task Concurrent_duplicate_submit_returns_persisted_winner_after_unique_conflict()
    {
        await using var fixture = await NotificationSqliteFixture.CreateAsync();
        var firstHandler = fixture.CreateSubmitHandler();
        var first = await firstHandler.Handle(new SubmitNotificationIntentCommand(
            "org-001",
            "env-dev",
            CreateIntent("dedupe-race", ["user:admin"]),
            DateTimeOffset.Parse("2026-06-29T00:00:00Z")), CancellationToken.None);

        await using var secondDb = fixture.CreateContext();
        await using var transaction = await secondDb.Database.BeginTransactionAsync();
        var secondHandler = new SubmitNotificationIntentCommandHandler(
            new NotificationIntentRepository(secondDb),
            secondDb,
            fixture.CreateDeliveryService(secondDb));

        var second = await secondHandler.Handle(new SubmitNotificationIntentCommand(
            "org-001",
            "env-dev",
            CreateIntent("dedupe-race", ["user:operator"]),
            DateTimeOffset.Parse("2026-06-29T00:00:01Z")), CancellationToken.None);

        Assert.True(second.Duplicate);
        Assert.Equal(first.IntentId, second.IntentId);
        Assert.Equal("user:admin", Assert.Single(second.Messages).RecipientRef);
        Assert.Equal(1, await fixture.Db.NotificationIntents.CountAsync());
        Assert.Equal(1, await fixture.Db.NotificationMessages.CountAsync());
    }

    private static SubmitNotificationIntentRequest CreateIntent(string dedupeKey, IReadOnlyCollection<string> recipients)
    {
        return CreateIntent(dedupeKey, recipients, sourceEventType: "ops.OperationTaskFailed", severity: NotificationContractConstants.SeverityCritical);
    }

    private static SubmitNotificationIntentRequest CreateIntent(
        string dedupeKey,
        IReadOnlyCollection<string> recipients,
        string sourceEventType = "ops.OperationTaskFailed",
        string severity = NotificationContractConstants.SeverityCritical)
    {
        return new SubmitNotificationIntentRequest(
            SourceService: "ops",
            SourceEventType: sourceEventType,
            SourceEventId: dedupeKey,
            IntentType: NotificationContractConstants.IntentTypeTask,
            Severity: severity,
            DedupeKey: dedupeKey,
            Resource: new NotificationResourceRef("operation-task", dedupeKey, null),
            Title: "Restart failed",
            Summary: "Instance restart failed with timeout.",
            SuggestedRecipientRefs: recipients);
    }

    private sealed class RecordingDeliveryProvider(string channel) : INotificationDeliveryProvider
    {
        public string Channel { get; } = channel;
        public List<NotificationDeliveryRequest> Sent { get; } = [];
        public NotificationDeliveryProviderResult NextResult { get; set; } = NotificationDeliveryProviderResult.Succeeded("provider-message");

        public Task<NotificationDeliveryProviderResult> SendAsync(NotificationDeliveryRequest request, CancellationToken cancellationToken)
        {
            Sent.Add(request);
            return Task.FromResult(NextResult);
        }
    }

    private sealed class NotificationSqliteFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<ApplicationDbContext> _options;
        private readonly NotificationChannelRateLimiter _rateLimiter = new();

        private NotificationSqliteFixture(SqliteConnection connection, DbContextOptions<ApplicationDbContext> options)
        {
            _connection = connection;
            _options = options;
            Db = CreateContext();
        }

        public ApplicationDbContext Db { get; }

        public SubmitNotificationIntentCommandHandler CreateSubmitHandler(params INotificationDeliveryProvider[] providers)
        {
            return CreateSubmitHandler(CreateDeliveryService(providers));
        }

        public SubmitNotificationIntentCommandHandler CreateSubmitHandler(NotificationDeliveryService deliveryService)
        {
            return new SubmitNotificationIntentCommandHandler(
                new NotificationIntentRepository(Db),
                Db,
                deliveryService);
        }

        public NotificationDeliveryService CreateDeliveryService(params INotificationDeliveryProvider[] providers)
        {
            return CreateDeliveryService(Db, providers);
        }

        public NotificationDeliveryService CreateDeliveryService(
            INotificationDeliveryProvider[] providers,
            int emailMaxPerMinute)
        {
            return CreateDeliveryService(Db, providers, emailMaxPerMinute);
        }

        public NotificationDeliveryService CreateDeliveryService(ApplicationDbContext dbContext)
        {
            return CreateDeliveryService(dbContext, []);
        }

        public NotificationDeliveryService CreateDeliveryService(
            ApplicationDbContext dbContext,
            INotificationDeliveryProvider[] providers,
            int? emailMaxPerMinute = null)
        {
            var options = new NotificationDeliveryOptions
            {
                MaxAttempts = 3,
                RetryDelay = TimeSpan.FromMinutes(2),
            };
            if (emailMaxPerMinute.HasValue)
            {
                options.ChannelRateLimits[NotificationDeliveryChannels.Email] = emailMaxPerMinute.Value;
            }

            return new NotificationDeliveryService(
                dbContext,
                providers,
                Options.Create(options),
                _rateLimiter);
        }

        public static async Task<NotificationSqliteFixture> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;
            var fixture = new NotificationSqliteFixture(connection, options);
            await fixture.Db.Database.EnsureCreatedAsync();
            return fixture;
        }

        public ApplicationDbContext CreateContext() => new(_options, mediator: null!);

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
