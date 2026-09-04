using DotNetCore.CAP;
using Nerv.IIP.Business.Maintenance.Domain.DomainEvents;
using Nerv.IIP.Contracts.Maintenance;

namespace Nerv.IIP.Business.Maintenance.Web.Application.IntegrationEventConverters;

/// <summary>
/// Maintenance 集成事件的 CAP outbox 出口（<c>ICapPublisher</c> 的测试缝）。生产实现由
/// <c>AddMaintenanceCapIntegrationEvents</c> 在非 Testing 分支注册；Testing 分支与既有 v1 路径一样不接 CAP。
/// </summary>
public interface IMaintenanceIntegrationEventOutboxPublisher
{
    Task PublishAsync<T>(string topic, T integrationEvent, CancellationToken cancellationToken);
}

/// <summary>canonical topic 的 <c>{deployment-profile}</c> 段来源；与 MES/Scheduling 消费侧一样取 host 的 EnvironmentName。</summary>
public sealed record MaintenanceAssetUnavailableTopicOptions(string DeploymentProfile);

public sealed class CapMaintenanceIntegrationEventOutboxPublisher(ICapPublisher publisher) : IMaintenanceIntegrationEventOutboxPublisher
{
    public Task PublishAsync<T>(string topic, T integrationEvent, CancellationToken cancellationToken) =>
        publisher.PublishAsync(topic, integrationEvent, cancellationToken: cancellationToken);
}

/// <summary>
/// #2964 C/D 阶段的双发：一次已提交的 v2 不可用事实生成两个 envelope——v1 companion（legacy alias topic，供 v1-only 消费者）
/// 与 v2 canonical envelope（canonical v2 topic）。两者 <c>eventId</c> 各自独立、<c>idempotencyKey</c> 精确相同
/// （<c>asset-unavailable:{workOrderId}:{fromUtc:O}</c>，与 v1 converter 同一表达式），MES/Scheduling 的
/// <c>(ConsumerName, IdempotencyKey)</c> 业务身份据此把双投折叠为一次副作用。
/// 本 handler 作为领域事件通知在 UoW 事务内执行；任一 outbox 写入失败都会让工单与另一条 outbox 一起回滚。
/// v2 envelope 在写入任何 outbox 之前先经共享契约校验（topic/version/source/type 一致性），不会先写错消息再指望消费者修复。
/// </summary>
public sealed class AssetUnavailableV2IntegrationEventPublisher(
    IMaintenanceIntegrationEventOutboxPublisher publisher,
    MaintenanceAssetUnavailableTopicOptions topicOptions)
    : INotificationHandler<AssetUnavailableByReasonCodeDomainEvent>
{
    public async Task Handle(AssetUnavailableByReasonCodeDomainEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var (v1, v2) = Build(notification);
        var v2Topic = AssetUnavailableIntegrationEventTopics.V2(topicOptions.DeploymentProfile);
        AssetUnavailableIntegrationEventTopics.EnsureV2EnvelopeMatches(v2Topic, v2);

        await publisher.PublishAsync(AssetUnavailableIntegrationEventTopics.V1LegacyAlias, v1, cancellationToken);
        await publisher.PublishAsync(v2Topic, v2, cancellationToken);
    }

    /// <summary>纯函数：从同一领域事实构造 v1 companion 与 v2 envelope，供 handler 与契约测试共用。</summary>
    public static (AssetUnavailableIntegrationEvent V1, AssetUnavailableV2IntegrationEvent V2) Build(
        AssetUnavailableByReasonCodeDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var workOrder = domainEvent.WorkOrder;
        var fromUtc = domainEvent.FromUtc;
        var correlationId = workOrder.Id.ToString();
        var causationId = workOrder.SourceAlarmId ?? workOrder.Id.ToString();
        var idempotencyKey = $"asset-unavailable:{workOrder.Id}:{fromUtc:O}";

        var v1 = new AssetUnavailableIntegrationEvent(
            EventIds.New(),
            MaintenanceIntegrationEventTypes.AssetUnavailable,
            MaintenanceIntegrationEventVersions.V1,
            fromUtc,
            MaintenanceIntegrationEventSources.Maintenance,
            correlationId,
            causationId,
            workOrder.OrganizationId,
            workOrder.EnvironmentId,
            workOrder.OpenedBy,
            idempotencyKey,
            new AssetUnavailablePayload(workOrder.DeviceAssetId, domainEvent.ReasonCode, fromUtc));
        var v2 = new AssetUnavailableV2IntegrationEvent(
            EventIds.New(),
            MaintenanceIntegrationEventTypes.AssetUnavailable,
            MaintenanceIntegrationEventVersions.V2,
            fromUtc,
            MaintenanceIntegrationEventSources.BusinessMaintenance,
            correlationId,
            causationId,
            workOrder.OrganizationId,
            workOrder.EnvironmentId,
            workOrder.OpenedBy,
            idempotencyKey,
            new AssetUnavailableV2Payload(workOrder.DeviceAssetId, domainEvent.ReasonCode, fromUtc));
        return (v1, v2);
    }
}
