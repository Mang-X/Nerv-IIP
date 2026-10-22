using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Web.Application.IntegrationEventHandlers;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Notification.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class MesAndonNotificationConsumerTests
{
    [Fact]
    public async Task Handle_andon_call_escalated_creates_one_critical_task_for_the_explicit_recipient()
    {
        using var factory = new NotificationConsumerWebApplicationFactory();
        var integrationEvent = CreateEvent();

        await HandleAsync(factory, integrationEvent);
        await HandleAsync(factory, integrationEvent);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var intent = await dbContext.NotificationIntents
            .Include(x => x.Messages)
            .Include(x => x.Tasks)
            .SingleAsync();
        var processed = await dbContext.ProcessedIntegrationEvents.SingleAsync();

        Assert.Equal("org-001", intent.OrganizationId);
        Assert.Equal("env-001", intent.EnvironmentId);
        Assert.Equal(MesIntegrationEventSources.BusinessMes, intent.SourceService);
        Assert.Equal(AndonCallEscalatedIntegrationEvent.Type, intent.SourceEventType);
        Assert.Equal("event-andon-escalated-001", intent.SourceEventId);
        Assert.Equal(NotificationIntentTypes.Task, intent.IntentType);
        Assert.Equal(NotificationContractConstants.SeverityCritical, intent.Severity);
        Assert.Equal("mes-andon-call", intent.ResourceType);
        Assert.Equal("call-001", intent.ResourceId);
        Assert.Equal("user:supervisor-001", Assert.Single(intent.Messages).RecipientRef);
        Assert.Single(intent.Tasks);
        Assert.Contains("Equipment", intent.Summary, StringComparison.Ordinal);
        Assert.Contains("WO-001", intent.Summary, StringComparison.Ordinal);
        Assert.Contains("OP-001", intent.Summary, StringComparison.Ordinal);
        Assert.Contains("WC-001", intent.Summary, StringComparison.Ordinal);
        Assert.Contains("caller-001", intent.Summary, StringComparison.Ordinal);
        Assert.Contains("300", intent.Summary, StringComparison.Ordinal);
        Assert.Contains("/mes/andon", intent.Summary, StringComparison.Ordinal);
        Assert.Equal(AndonCallEscalatedIntegrationEventHandlerForNotification.ConsumerName, processed.ConsumerName);
        Assert.Equal("andon-call-escalated:call-001", processed.IdempotencyKey);
    }

    [Fact]
    public async Task Handle_andon_call_escalated_without_recipient_does_not_create_a_default_notification()
    {
        using var factory = new NotificationConsumerWebApplicationFactory();
        var integrationEvent = CreateEvent() with
        {
            Payload = CreateEvent().Payload with { RecipientId = string.Empty },
        };

        await Assert.ThrowsAsync<NetCorePal.Extensions.Primitives.KnownException>(
            () => HandleAsync(factory, integrationEvent));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await dbContext.NotificationIntents.ToListAsync());
        Assert.Empty(await dbContext.ProcessedIntegrationEvents.ToListAsync());
    }

    private static async Task HandleAsync(
        NotificationConsumerWebApplicationFactory factory,
        AndonCallEscalatedIntegrationEvent integrationEvent)
    {
        using var scope = factory.Services.CreateScope();
        IIntegrationEventHandler<AndonCallEscalatedIntegrationEvent> handler =
            ActivatorUtilities.CreateInstance<AndonCallEscalatedIntegrationEventHandlerForNotification>(scope.ServiceProvider);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
    }

    private static AndonCallEscalatedIntegrationEvent CreateEvent()
    {
        return new AndonCallEscalatedIntegrationEvent(
            EventId: "event-andon-escalated-001",
            EventType: AndonCallEscalatedIntegrationEvent.Type,
            EventVersion: AndonCallEscalatedIntegrationEvent.Version,
            OccurredAtUtc: DateTimeOffset.Parse("2026-09-20T06:05:00Z"),
            SourceService: MesIntegrationEventSources.BusinessMes,
            CorrelationId: "andon-call-escalated:call-001",
            CausationId: "raise-andon-call:call-001",
            OrganizationId: "org-001",
            EnvironmentId: "env-001",
            Actor: "system:business-mes",
            IdempotencyKey: "andon-call-escalated:call-001",
            Payload: new AndonCallEscalatedPayload(
                CallId: "call-001",
                Category: "Equipment",
                WorkOrderId: "WO-001",
                OperationTaskId: "OP-001",
                WorkCenterId: "WC-001",
                CallerId: "caller-001",
                RaisedAtUtc: DateTimeOffset.Parse("2026-09-20T06:00:00Z"),
                RecipientId: "supervisor-001",
                UnclaimedTimeoutSeconds: 300));
    }

    private sealed class NotificationConsumerWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Persistence:Provider"] = "InMemory",
                    ["Persistence:InMemoryDatabaseName"] = Guid.NewGuid().ToString("N"),
                }));
        }
    }
}
