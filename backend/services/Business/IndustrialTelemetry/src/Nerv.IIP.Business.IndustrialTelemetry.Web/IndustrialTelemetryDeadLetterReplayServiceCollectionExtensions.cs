using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web;

public static class IndustrialTelemetryDeadLetterReplayServiceCollectionExtensions
{
    public static IServiceCollection AddIndustrialTelemetryDeadLetterReplay(this IServiceCollection services)
    {
        services.AddObservedIntegrationEventDeadLetterStore();
        AddOutcome<OperationTaskCompletedIntegrationEvent, DeviceControlCommandCompletedHandler>(services,
            DeviceControlCommandCompletedHandler.ConsumerName, e => e.Payload.OperationTaskId, "completed", e => e.Payload.FinishedAtUtc);
        AddOutcome<OperationTaskFailedIntegrationEvent, DeviceControlCommandFailedHandler>(services,
            DeviceControlCommandFailedHandler.ConsumerName, e => e.Payload.OperationTaskId, "failed", e => e.Payload.FinishedAtUtc);
        AddOutcome<OperationApprovalRejectedIntegrationEvent, DeviceControlCommandRejectedHandler>(services,
            DeviceControlCommandRejectedHandler.ConsumerName, e => e.Payload.OperationTaskId, "rejected", e => e.Payload.DecidedAtUtc);
        services.AddIntegrationEventDeadLetterReplay<ProductionReportRecordedIntegrationEvent, ProductionReportOeeProjectionHandler>(
            ProductionReportOeeProjectionHandler.ConsumerName, (consumer, e, token) => consumer.HandleAsync(e, token),
            (provider, e, token) => provider.GetRequiredService<ApplicationDbContext>().OeeProductionFacts.AsNoTracking()
                .AnyAsync(x => x.OrganizationId == e.OrganizationId && x.EnvironmentId == e.EnvironmentId
                    && x.SourceReportNo == e.Payload.ReportNo, token));
        return services;
    }

    private static void AddOutcome<TEvent, TConsumer>(IServiceCollection services, string consumerName,
        Func<TEvent, string> operationId, string status, Func<TEvent, DateTimeOffset> finishedAt)
        where TEvent : IIntegrationEventEnvelope
        where TConsumer : class, IIntegrationEventHandler<TEvent>
    {
        services.AddIntegrationEventDeadLetterReplay<TEvent, ScopedDeviceOutcomeReplay<TEvent, TConsumer>>(consumerName,
            (consumer, e, token) => consumer.HandleAsync(e, operationId(e), token),
            (provider, e, token) => provider.GetRequiredService<ApplicationDbContext>().DeviceControlCommands.AsNoTracking()
                .AnyAsync(x => x.OrganizationId == e.OrganizationId && x.EnvironmentId == e.EnvironmentId
                    && x.OperationTaskId == operationId(e) && x.Status == status && x.FinishedAtUtc == finishedAt(e), token));
    }
}

internal sealed class ScopedDeviceOutcomeReplay<TEvent, TConsumer>(TConsumer consumer, ApplicationDbContext dbContext)
    where TEvent : IIntegrationEventEnvelope
    where TConsumer : class, IIntegrationEventHandler<TEvent>
{
    public async Task HandleAsync(TEvent integrationEvent, string operationId, CancellationToken token)
    {
        // The existing command addresses a globally unique Ops id. Verify the replay's scope before it can write.
        if (!await dbContext.DeviceControlCommands.AsNoTracking().AnyAsync(x => x.OperationTaskId == operationId
            && x.OrganizationId == integrationEvent.OrganizationId && x.EnvironmentId == integrationEvent.EnvironmentId, token))
            throw new InvalidOperationException("The original device control command is absent from the event scope.");
        await consumer.HandleAsync(integrationEvent, token);
    }
}
