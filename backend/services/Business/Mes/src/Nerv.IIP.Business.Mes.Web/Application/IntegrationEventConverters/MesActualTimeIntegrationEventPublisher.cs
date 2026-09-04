using DotNetCore.CAP;
using MediatR;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Contracts.Mes;

namespace Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;

/// <summary>
/// MES 集成事件的 CAP outbox 出口（<c>ICapPublisher</c> 的测试缝）。实际时间结算与
/// #3000 的工单发布投影回填共用它，不各写一份形状相同的薄包装。
/// </summary>
internal interface IMesIntegrationEventOutboxPublisher
{
    Task PublishAsync<T>(string topic, T integrationEvent);
}

internal sealed record MesActualTimeTopicOptions(string DeploymentEnvironment);

internal sealed class CapMesIntegrationEventOutboxPublisher(ICapPublisher publisher) : IMesIntegrationEventOutboxPublisher
{
    public Task PublishAsync<T>(string topic, T integrationEvent) => publisher.PublishAsync(topic, integrationEvent);
}

internal sealed class OperationActualTimeSettledIntegrationEventPublisher(
    IMesIntegrationEventOutboxPublisher publisher,
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
    IMesIntegrationEventOutboxPublisher publisher,
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
