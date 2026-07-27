using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmEventAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Commands;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Errors;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

public sealed class IndustrialTelemetryLifecycleConflictTests
{
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
    public async Task Cleared_alarm_rejects_a_no_key_shelve_as_a_lifecycle_conflict()
    {
        await using var dbContext = CreateDbContext();
        var raisedAtUtc = new DateTimeOffset(2026, 7, 27, 1, 0, 0, TimeSpan.Zero);
        var alarm = CreateRaisedAlarm(raisedAtUtc, "no-key-shelve-cleared");
        alarm.Clear(raisedAtUtc.AddMinutes(5), "operator-001", "recovered");
        dbContext.AlarmEvents.Add(alarm);
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<IndustrialTelemetryLifecycleConflictException>(() =>
            new ShelveAlarmCommandHandler(dbContext).Handle(
                new ShelveAlarmCommand(
                    alarm.Id,
                    "org-001",
                    "env-dev",
                    raisedAtUtc.AddMinutes(6),
                    30,
                    "operator-002",
                    "maintenance",
                    null),
                CancellationToken.None));

        Assert.Equal("shelve", exception.Action);
        Assert.Equal("cleared", exception.CurrentStatus);
        Assert.Equal("cleared", alarm.Status);
        Assert.Null(alarm.ShelvedAtUtc);
        Assert.Empty(dbContext.AlarmShelveIdempotencies.Local);
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
                null),
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
                null),
            CancellationToken.None);

        Assert.Equal(raisedAtUtc.AddMinutes(2), alarm.ShelvedAtUtc);
        Assert.Equal(firstShelvedUntilUtc, alarm.ShelvedUntilUtc);
        Assert.Equal("operator-first", alarm.ShelvedBy);
        Assert.Equal("first", alarm.ShelveReason);
        Assert.Equal("shelved", alarm.Status);
    }

    [Fact]
    public async Task Shelve_keeps_same_key_different_payload_as_a_known_400_error()
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

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
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

        Assert.IsNotType<IndustrialTelemetryLifecycleConflictException>(exception);
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
