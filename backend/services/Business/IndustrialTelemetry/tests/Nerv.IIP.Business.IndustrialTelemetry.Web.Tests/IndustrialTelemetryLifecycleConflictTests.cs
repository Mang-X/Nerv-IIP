using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmEventAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmShelveIdempotencyAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Commands;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Errors;
using NetCorePal.Extensions.Primitives;
using NetCorePal.Extensions.DistributedLocks;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class IndustrialTelemetryLifecycleConflictTests
{
    [Fact]
    public void Persistence_backstop_only_classifies_the_alarm_shelve_idempotency_constraint()
    {
        using var dbContext = CreateDbContext();
        var constraintName = dbContext.Model.FindEntityType(typeof(AlarmShelveIdempotency))!
            .GetIndexes()
            .Single(index => index.Properties.Select(property => property.Name).SequenceEqual(
                [
                    nameof(AlarmShelveIdempotency.OrganizationId),
                    nameof(AlarmShelveIdempotency.EnvironmentId),
                    nameof(AlarmShelveIdempotency.AlarmEventId),
                    nameof(AlarmShelveIdempotency.IdempotencyKey),
                ]))
            .GetDatabaseName()!;

        Assert.True(IndustrialTelemetryIdempotencyPersistenceConflicts.IsTargetConflict(
            UniqueConflict(constraintName),
            dbContext));
        Assert.False(IndustrialTelemetryIdempotencyPersistenceConflicts.IsTargetConflict(
            UniqueConflict("ux_unrelated_industrial_telemetry_constraint"),
            dbContext));
    }

    [Theory]
    [InlineData("acknowledge")]
    [InlineData("shelve")]
    public async Task Cleared_alarm_rejects_acknowledge_and_shelve_as_lifecycle_conflicts(string action)
    {
        await using var dbContext = CreateDbContext();
        var raisedAtUtc = new DateTimeOffset(2026, 7, 27, 1, 0, 0, TimeSpan.Zero);
        var alarm = AlarmEvent.Raise(
            "org-001",
            "env-dev",
            "DEV-CNC-01",
            "TEMP_HIGH",
            "critical",
            raisedAtUtc,
            "alarm-lifecycle-conflict");
        alarm.Clear(raisedAtUtc.AddMinutes(5), "operator-001", "recovered");
        dbContext.AlarmEvents.Add(alarm);
        await dbContext.SaveChangesAsync();

        var exception = action == "acknowledge"
            ? await Assert.ThrowsAsync<IndustrialTelemetryLifecycleConflictException>(() =>
                new AcknowledgeAlarmCommandHandler(dbContext).Handle(
                    new AcknowledgeAlarmCommand(
                        alarm.Id,
                        "org-001",
                        "env-dev",
                        raisedAtUtc.AddMinutes(6),
                        "operator-002"),
                    CancellationToken.None))
            : await Assert.ThrowsAsync<IndustrialTelemetryLifecycleConflictException>(() =>
                new ShelveAlarmCommandHandler(dbContext).Handle(
                    new ShelveAlarmCommand(
                        alarm.Id,
                        "org-001",
                        "env-dev",
                        raisedAtUtc.AddMinutes(6),
                        30,
                        "operator-002",
                        "maintenance",
                        "shelve-cleared"),
                    CancellationToken.None));

        Assert.Equal(action, exception.Action);
        Assert.Equal("cleared", exception.CurrentStatus);
        Assert.Equal("cleared", alarm.Status);
        Assert.Null(alarm.AcknowledgedAtUtc);
        Assert.Null(alarm.ShelvedAtUtc);
    }

    [Fact]
    public void Shelve_preserves_v1_compatibility_when_idempotency_key_is_omitted()
    {
        var raisedAtUtc = new DateTimeOffset(2026, 7, 27, 1, 0, 0, TimeSpan.Zero);
        var command = new ShelveAlarmCommand(
            new AlarmEventId(Guid.CreateVersion7()),
            "org-001",
            "env-dev",
            raisedAtUtc,
            30,
            "operator-002",
            "maintenance",
            string.Empty);

        var result = new ShelveAlarmCommandValidator().Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Alarm_lifecycle_actions_and_escalation_share_one_scope_distributed_lock()
    {
        var alarmId = new AlarmEventId(Guid.CreateVersion7());
        var changedAtUtc = new DateTimeOffset(2026, 7, 27, 1, 2, 0, TimeSpan.Zero);
        var acknowledge = await new AcknowledgeAlarmCommandLock().GetLockKeysAsync(
            new AcknowledgeAlarmCommand(
                alarmId,
                "org-001",
                "env-dev",
                changedAtUtc,
                "operator-001"),
            CancellationToken.None);
        var shelve = await new ShelveAlarmCommandLock().GetLockKeysAsync(
            new ShelveAlarmCommand(
                alarmId,
                "org-001",
                "env-dev",
                changedAtUtc,
                30,
                "operator-001",
                null,
                "shelve-lock"),
            CancellationToken.None);
        var unshelve = await new UnshelveAlarmCommandLock().GetLockKeysAsync(
            new UnshelveAlarmCommand(
                alarmId,
                "org-001",
                "env-dev",
                changedAtUtc),
            CancellationToken.None);
        var escalation = await new RunAlarmEscalationsCommandLock().GetLockKeysAsync(
            new RunAlarmEscalationsCommand(
                "org-001",
                "env-dev",
                changedAtUtc,
                30,
                ["critical"],
                ["maintenance"]),
            CancellationToken.None);

        Assert.Equal(acknowledge.LockKey, shelve.LockKey);
        Assert.Equal(acknowledge.LockKey, unshelve.LockKey);
        Assert.Equal(acknowledge.LockKey, escalation.LockKey);
        Assert.Equal(
            "business-industrial-telemetry:alarm-lifecycle:org-001:env-dev",
            acknowledge.LockKey);
        Assert.Equal(TimeSpan.FromSeconds(30), acknowledge.AcquireTimeout);
    }

    [Fact]
    public async Task Acknowledge_preserves_first_write_wins_for_an_active_alarm()
    {
        await using var dbContext = CreateDbContext();
        var raisedAtUtc = new DateTimeOffset(2026, 7, 27, 1, 0, 0, TimeSpan.Zero);
        var alarm = CreateRaisedAlarm(raisedAtUtc, "ack-first-write");
        dbContext.AlarmEvents.Add(alarm);
        await dbContext.SaveChangesAsync();
        var handler = new AcknowledgeAlarmCommandHandler(dbContext);

        await handler.Handle(
            new AcknowledgeAlarmCommand(
                alarm.Id,
                "org-001",
                "env-dev",
                raisedAtUtc.AddMinutes(2),
                "operator-first"),
            CancellationToken.None);
        await handler.Handle(
            new AcknowledgeAlarmCommand(
                alarm.Id,
                "org-001",
                "env-dev",
                raisedAtUtc.AddMinutes(3),
                "operator-second"),
            CancellationToken.None);

        Assert.Equal(raisedAtUtc.AddMinutes(2), alarm.AcknowledgedAtUtc);
        Assert.Equal("operator-first", alarm.AcknowledgedBy);
        Assert.Equal("acknowledged", alarm.Status);
    }

    [Fact]
    public async Task Concurrent_acknowledge_requests_are_serialized_and_preserve_one_complete_first_write()
    {
        await using var factory = new ConcurrentAckFactory();
        var raisedAtUtc = new DateTimeOffset(2026, 7, 27, 1, 0, 0, TimeSpan.Zero);
        var alarmId = await factory.SeedAlarmAsync(raisedAtUtc);
        using var firstClient = CreateAuthorizedClient(factory);
        using var secondClient = CreateAuthorizedClient(factory);
        var firstAt = raisedAtUtc.AddMinutes(2);
        var secondAt = raisedAtUtc.AddMinutes(3);

        var responses = await Task.WhenAll(
            firstClient.PostAsJsonAsync(
                $"/api/business/v1/iiot/alarms/{alarmId.Id:D}/acknowledge",
                new
                {
                    organizationId = "org-001",
                    environmentId = "env-dev",
                    acknowledgedAtUtc = firstAt,
                    acknowledgedBy = "operator-first",
                }),
            secondClient.PostAsJsonAsync(
                $"/api/business/v1/iiot/alarms/{alarmId.Id:D}/acknowledge",
                new
                {
                    organizationId = "org-001",
                    environmentId = "env-dev",
                    acknowledgedAtUtc = secondAt,
                    acknowledgedBy = "operator-second",
                }));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var stored = await factory.ReadAlarmAsync(alarmId);
        Assert.Contains(stored.AcknowledgedBy, new[] { "operator-first", "operator-second" });
        Assert.Equal(
            stored.AcknowledgedBy == "operator-first" ? firstAt : secondAt,
            stored.AcknowledgedAtUtc);
        Assert.Equal("acknowledged", stored.Status);
    }

    [Fact]
    public async Task Acknowledge_preserves_first_write_wins_when_the_alarm_is_shelved()
    {
        await using var dbContext = CreateDbContext();
        var raisedAtUtc = new DateTimeOffset(2026, 7, 27, 1, 0, 0, TimeSpan.Zero);
        var alarm = CreateRaisedAlarm(raisedAtUtc, "shelved-ack-first-write");
        alarm.Shelve(
            raisedAtUtc.AddMinutes(1),
            raisedAtUtc.AddMinutes(31),
            "shelving-operator",
            "maintenance");
        dbContext.AlarmEvents.Add(alarm);
        await dbContext.SaveChangesAsync();
        var handler = new AcknowledgeAlarmCommandHandler(dbContext);

        await handler.Handle(
            new AcknowledgeAlarmCommand(
                alarm.Id,
                "org-001",
                "env-dev",
                raisedAtUtc.AddMinutes(2),
                "operator-first"),
            CancellationToken.None);
        await handler.Handle(
            new AcknowledgeAlarmCommand(
                alarm.Id,
                "org-001",
                "env-dev",
                raisedAtUtc.AddMinutes(3),
                "operator-second"),
            CancellationToken.None);

        Assert.Equal("shelved", alarm.Status);
        Assert.Equal(raisedAtUtc.AddMinutes(2), alarm.AcknowledgedAtUtc);
        Assert.Equal("operator-first", alarm.AcknowledgedBy);
        Assert.Equal(raisedAtUtc.AddMinutes(1), alarm.ShelvedAtUtc);
        Assert.Equal(raisedAtUtc.AddMinutes(31), alarm.ShelvedUntilUtc);
    }

    [Fact]
    public async Task Shelve_preserves_active_shelf_noop()
    {
        await using var dbContext = CreateDbContext();
        var raisedAtUtc = new DateTimeOffset(2026, 7, 27, 1, 0, 0, TimeSpan.Zero);
        var alarm = CreateRaisedAlarm(raisedAtUtc, "active-shelf-noop");
        dbContext.AlarmEvents.Add(alarm);
        await dbContext.SaveChangesAsync();
        var handler = new ShelveAlarmCommandHandler(dbContext);

        await handler.Handle(
            new ShelveAlarmCommand(
                alarm.Id,
                "org-001",
                "env-dev",
                raisedAtUtc.AddMinutes(2),
                30,
                "operator-first",
                "first",
                "active-shelf-first"),
            CancellationToken.None);
        var firstShelvedUntilUtc = alarm.ShelvedUntilUtc;
        await handler.Handle(
            new ShelveAlarmCommand(
                alarm.Id,
                "org-001",
                "env-dev",
                raisedAtUtc.AddMinutes(3),
                60,
                "operator-second",
                "second",
                "active-shelf-second"),
            CancellationToken.None);

        Assert.Equal(raisedAtUtc.AddMinutes(2), alarm.ShelvedAtUtc);
        Assert.Equal(firstShelvedUntilUtc, alarm.ShelvedUntilUtc);
        Assert.Equal("operator-first", alarm.ShelvedBy);
        Assert.Equal("first", alarm.ShelveReason);
        Assert.Equal("shelved", alarm.Status);
    }

    [Fact]
    public async Task Shelve_keeps_same_key_different_payload_as_a_stable_idempotency_conflict()
    {
        await using var dbContext = CreateDbContext();
        var raisedAtUtc = new DateTimeOffset(2026, 7, 27, 1, 0, 0, TimeSpan.Zero);
        var alarm = CreateRaisedAlarm(raisedAtUtc, "shelve-key-conflict");
        dbContext.AlarmEvents.Add(alarm);
        await dbContext.SaveChangesAsync();
        var handler = new ShelveAlarmCommandHandler(dbContext);
        await handler.Handle(
            new ShelveAlarmCommand(
                alarm.Id,
                "org-001",
                "env-dev",
                raisedAtUtc.AddMinutes(2),
                30,
                "operator-first",
                "maintenance",
                "same-key"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<IndustrialTelemetryIdempotencyConflictException>(() =>
            handler.Handle(
                new ShelveAlarmCommand(
                    alarm.Id,
                    "org-001",
                    "env-dev",
                    raisedAtUtc.AddMinutes(2),
                    60,
                    "operator-first",
                    "maintenance",
                    "same-key"),
                CancellationToken.None));
        Assert.Equal(raisedAtUtc.AddMinutes(32), alarm.ShelvedUntilUtc);
    }

    [Fact]
    public async Task Shelve_preserves_an_exact_same_key_replay_after_the_alarm_is_cleared()
    {
        await using var dbContext = CreateDbContext();
        var raisedAtUtc = new DateTimeOffset(2026, 7, 27, 1, 0, 0, TimeSpan.Zero);
        var alarm = CreateRaisedAlarm(raisedAtUtc, "shelve-replay-after-clear");
        dbContext.AlarmEvents.Add(alarm);
        await dbContext.SaveChangesAsync();
        var handler = new ShelveAlarmCommandHandler(dbContext);
        var command = new ShelveAlarmCommand(
            alarm.Id,
            "org-001",
            "env-dev",
            raisedAtUtc.AddMinutes(2),
            30,
            "operator-first",
            "maintenance",
            "replay-key");
        await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync();
        alarm.Clear(raisedAtUtc.AddMinutes(10), "operator-first", "recovered");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(alarm.Id, result);
        Assert.Equal("cleared", alarm.Status);
        Assert.Equal(raisedAtUtc.AddMinutes(2), alarm.ShelvedAtUtc);
        Assert.Equal(raisedAtUtc.AddMinutes(32), alarm.ShelvedUntilUtc);
    }

    [Fact]
    public async Task Unshelve_preserves_non_shelved_noop_and_never_invents_a_lifecycle_conflict()
    {
        await using var dbContext = CreateDbContext();
        var raisedAtUtc = new DateTimeOffset(2026, 7, 27, 1, 0, 0, TimeSpan.Zero);
        var alarm = CreateRaisedAlarm(raisedAtUtc, "unshelve-noop");
        dbContext.AlarmEvents.Add(alarm);
        await dbContext.SaveChangesAsync();

        var result = await new UnshelveAlarmCommandHandler(dbContext).Handle(
            new UnshelveAlarmCommand(
                alarm.Id,
                "org-001",
                "env-dev",
                raisedAtUtc.AddMinutes(2)),
            CancellationToken.None);

        Assert.Equal(alarm.Id, result);
        Assert.Equal("raised", alarm.Status);
        Assert.Null(alarm.ShelvedAtUtc);
    }

    [Fact]
    public async Task Acknowledge_keeps_time_validation_as_a_known_400_error()
    {
        await using var dbContext = CreateDbContext();
        var raisedAtUtc = new DateTimeOffset(2026, 7, 27, 1, 0, 0, TimeSpan.Zero);
        var alarm = CreateRaisedAlarm(raisedAtUtc, "ack-time-validation");
        dbContext.AlarmEvents.Add(alarm);
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new AcknowledgeAlarmCommandHandler(dbContext).Handle(
                new AcknowledgeAlarmCommand(
                    alarm.Id,
                    "org-001",
                    "env-dev",
                    raisedAtUtc.AddMinutes(-1),
                    "operator-001"),
                CancellationToken.None));

        Assert.IsNotType<IndustrialTelemetryLifecycleConflictException>(exception);
        Assert.Equal("raised", alarm.Status);
    }

    [Fact]
    public async Task Acknowledge_http_endpoint_returns_409_with_a_safe_envelope()
    {
        await using var factory = CreateFactory(
            new IndustrialTelemetryLifecycleConflictException("acknowledge", "cleared"));
        using var client = CreateAuthorizedClient(factory);
        var alarmEventId = Guid.CreateVersion7();

        var response = await client.PostAsJsonAsync(
            $"/api/business/v1/iiot/alarms/{alarmEventId}/acknowledge",
            new
            {
                organizationId = "org-001",
                environmentId = "env-dev",
                acknowledgedAtUtc = DateTimeOffset.UtcNow,
                acknowledgedBy = "operator-001",
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":false", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"message\":\"lifecycle-conflict\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cleared", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Shelve_http_endpoint_keeps_known_errors_as_400()
    {
        await using var factory = CreateFactory(new KnownException("idempotency-key-conflict"));
        using var client = CreateAuthorizedClient(factory);
        var alarmEventId = Guid.CreateVersion7();

        var response = await client.PostAsJsonAsync(
            $"/api/business/v1/iiot/alarms/{alarmEventId}/shelve",
            new
            {
                organizationId = "org-001",
                environmentId = "env-dev",
                shelvedAtUtc = DateTimeOffset.UtcNow,
                durationMinutes = 30,
                shelvedBy = "operator-001",
                reason = "maintenance",
                idempotencyKey = "same-key",
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static AlarmEvent CreateRaisedAlarm(DateTimeOffset raisedAtUtc, string externalAlarmId)
    {
        return AlarmEvent.Raise(
            "org-001",
            "env-dev",
            "DEV-CNC-01",
            "TEMP_HIGH",
            "critical",
            raisedAtUtc,
            externalAlarmId);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"iiot-lifecycle-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static DbUpdateException UniqueConflict(string constraintName) =>
        new("unique conflict", new FakePostgresException("23505", constraintName));

    private sealed class FakePostgresException(string sqlState, string constraintName) : Exception
    {
        public string SqlState { get; } = sqlState;

        public string ConstraintName { get; } = constraintName;
    }

    private static WebApplicationFactory<Program> CreateFactory(Exception exception)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("environment", "Testing");
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token");
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<ISender>();
                    services.AddSingleton<ISender>(new ThrowingSender(exception));
                });
            });
    }

    private static HttpClient CreateAuthorizedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test-internal-service-token");
        return client;
    }

    private sealed class ConcurrentAckFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = $"industrial-telemetry-ack-{Guid.CreateVersion7():N}";
        private readonly ServiceProvider efServices = new ServiceCollection()
            .AddEntityFrameworkInMemoryDatabase()
            .BuildServiceProvider();

        public async Task<AlarmEventId> SeedAlarmAsync(DateTimeOffset raisedAtUtc)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var alarm = CreateRaisedAlarm(raisedAtUtc, $"concurrent-ack-{Guid.CreateVersion7():N}");
            db.AlarmEvents.Add(alarm);
            await db.SaveChangesAsync();
            return alarm.Id;
        }

        public async Task<AlarmEvent> ReadAlarmAsync(AlarmEventId alarmId)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await db.AlarmEvents.AsNoTracking().SingleAsync(x => x.Id == alarmId);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("environment", "Testing");
            builder.UseSetting("InternalService:BearerToken", "test-internal-service-token");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ApplicationDbContext>();
                services.RemoveAll<DbContextOptions>();
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<IDistributedLock>();
                services.AddInMemoryDistributedLock();
                services.AddDbContext<ApplicationDbContext>(options =>
                    options
                        .UseInMemoryDatabase(databaseName)
                        .UseInternalServiceProvider(efServices)
                        .ConfigureWarnings(warnings =>
                            warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                efServices.Dispose();
            }
        }
    }

    private sealed class ThrowingSender(Exception exception) : ISender
    {
        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            return Task.FromException<TResponse>(exception);
        }

        public Task Send<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            _ = request;
            _ = cancellationToken;
            return Task.FromException(exception);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            return Task.FromException<object?>(exception);
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task Publish<TNotification>(
            TNotification notification,
            CancellationToken cancellationToken = default)
            where TNotification : INotification =>
            Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Noop mediator cannot send requests.");

        public Task Send<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException("Noop mediator cannot send requests.");

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Noop mediator cannot send requests.");

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Noop mediator cannot stream requests.");

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Noop mediator cannot stream requests.");
    }
}
