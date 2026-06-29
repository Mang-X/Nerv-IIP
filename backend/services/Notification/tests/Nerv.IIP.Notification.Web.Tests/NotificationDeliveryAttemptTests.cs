using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Infrastructure.Repositories;
using Nerv.IIP.Notification.Web.Application.Commands.Notifications;
using Nerv.IIP.Notification.Web.Application.Notifications;

namespace Nerv.IIP.Notification.Web.Tests;

public sealed class NotificationDeliveryAttemptTests
{
    [Fact]
    public async Task Submit_intent_records_succeeded_in_app_delivery_attempt_for_each_message()
    {
        await using var fixture = await NotificationSqliteFixture.CreateAsync();
        var handler = new SubmitNotificationIntentCommandHandler(
            new NotificationIntentRepository(fixture.Db),
            fixture.Db);

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
    public async Task Concurrent_duplicate_submit_returns_persisted_winner_after_unique_conflict()
    {
        await using var fixture = await NotificationSqliteFixture.CreateAsync();
        var firstHandler = new SubmitNotificationIntentCommandHandler(
            new NotificationIntentRepository(fixture.Db),
            fixture.Db);
        var first = await firstHandler.Handle(new SubmitNotificationIntentCommand(
            "org-001",
            "env-dev",
            CreateIntent("dedupe-race", ["user:admin"]),
            DateTimeOffset.Parse("2026-06-29T00:00:00Z")), CancellationToken.None);

        await using var secondDb = fixture.CreateContext();
        var secondHandler = new SubmitNotificationIntentCommandHandler(
            new NotificationIntentRepository(secondDb),
            secondDb);

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
        return new SubmitNotificationIntentRequest(
            SourceService: "ops",
            SourceEventType: "ops.OperationTaskFailed",
            SourceEventId: dedupeKey,
            IntentType: NotificationContractConstants.IntentTypeTask,
            Severity: NotificationContractConstants.SeverityCritical,
            DedupeKey: dedupeKey,
            Resource: new NotificationResourceRef("operation-task", dedupeKey, null),
            Title: "Restart failed",
            Summary: "Instance restart failed with timeout.",
            SuggestedRecipientRefs: recipients);
    }

    private sealed class NotificationSqliteFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<ApplicationDbContext> _options;

        private NotificationSqliteFixture(SqliteConnection connection, DbContextOptions<ApplicationDbContext> options)
        {
            _connection = connection;
            _options = options;
            Db = CreateContext();
        }

        public ApplicationDbContext Db { get; }

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
