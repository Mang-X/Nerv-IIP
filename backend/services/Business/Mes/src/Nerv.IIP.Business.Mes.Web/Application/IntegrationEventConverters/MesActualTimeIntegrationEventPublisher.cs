using DotNetCore.CAP;
using MediatR;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Contracts.Mes;

namespace Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;

internal static class MesActualTimeIntegrationEventTopics
{
    public const string SettledV1LegacyAlias = nameof(MesOperationActualTimeSettledIntegrationEvent);
    public const string VoidedV1LegacyAlias = nameof(MesOperationActualTimeSettlementVoidedIntegrationEvent);

    public static string Settled(string deploymentEnvironment, int version) =>
        Build(deploymentEnvironment, "operation-actual-time-settled", version);

    public static string Voided(string deploymentEnvironment, int version) =>
        Build(deploymentEnvironment, "operation-actual-time-settlement-voided", version);

    private static string Build(string deploymentEnvironment, string eventName, int version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentEnvironment);
        return $"nerv-iip.{deploymentEnvironment.ToLowerInvariant()}.business-mes.mes.{eventName}.v{version}";
    }
}

internal interface IMesActualTimeOutboxPublisher
{
    Task PublishAsync<T>(string topic, T integrationEvent);
}

internal sealed record MesActualTimeTopicOptions(string DeploymentEnvironment);

internal sealed class CapMesActualTimeOutboxPublisher(ICapPublisher publisher) : IMesActualTimeOutboxPublisher
{
    public Task PublishAsync<T>(string topic, T integrationEvent) => publisher.PublishAsync(topic, integrationEvent);
}

internal sealed class OperationActualTimeSettledIntegrationEventPublisher(
    IMesActualTimeOutboxPublisher publisher,
    MesActualTimeTopicOptions topicOptions,
    OperationActualTimeSettledV1IntegrationEventConverter v1Converter,
    OperationActualTimeSettledIntegrationEventConverter v2Converter)
    : INotificationHandler<OperationActualTimeSettledDomainEvent>
{
    public async Task Handle(OperationActualTimeSettledDomainEvent notification, CancellationToken cancellationToken)
    {
        var v1 = v1Converter.Convert(notification);
        await publisher.PublishAsync(MesActualTimeIntegrationEventTopics.SettledV1LegacyAlias, v1);
        await publisher.PublishAsync(
            MesActualTimeIntegrationEventTopics.Settled(topicOptions.DeploymentEnvironment, v1.EventVersion),
            v1);
        var v2 = v2Converter.Convert(notification);
        await publisher.PublishAsync(
            MesActualTimeIntegrationEventTopics.Settled(topicOptions.DeploymentEnvironment, v2.EventVersion),
            v2);
    }
}

internal sealed class OperationActualTimeSettlementVoidedIntegrationEventPublisher(
    IMesActualTimeOutboxPublisher publisher,
    MesActualTimeTopicOptions topicOptions,
    OperationActualTimeSettlementVoidedV1IntegrationEventConverter v1Converter,
    OperationActualTimeSettlementVoidedIntegrationEventConverter v2Converter)
    : INotificationHandler<OperationActualTimeSettlementVoidedDomainEvent>
{
    public async Task Handle(OperationActualTimeSettlementVoidedDomainEvent notification, CancellationToken cancellationToken)
    {
        var v1 = v1Converter.Convert(notification);
        await publisher.PublishAsync(MesActualTimeIntegrationEventTopics.VoidedV1LegacyAlias, v1);
        await publisher.PublishAsync(
            MesActualTimeIntegrationEventTopics.Voided(topicOptions.DeploymentEnvironment, v1.EventVersion),
            v1);
        var v2 = v2Converter.Convert(notification);
        await publisher.PublishAsync(
            MesActualTimeIntegrationEventTopics.Voided(topicOptions.DeploymentEnvironment, v2.EventVersion),
            v2);
    }
}
