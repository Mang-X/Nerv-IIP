using System.Text.Json;
using DotNetCore.CAP;
using Nerv.IIP.Contracts.Maintenance;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;

public sealed class MesAssetUnavailableDeadLetterReplayHandler(
    ICapPublisher capPublisher,
    IHostEnvironment hostEnvironment) : IIntegrationEventDeadLetterReplayHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public bool CanReplay(IntegrationEventDeadLetterMessage message)
    {
        return message.ConsumerName.StartsWith(
                AssetUnavailableIntegrationEventHandlerForReschedule.ConsumerName,
                StringComparison.Ordinal) &&
            (message.EventClrType == typeof(AssetUnavailableIntegrationEvent).FullName ||
             message.EventClrType == typeof(AssetUnavailableV2IntegrationEvent).FullName);
    }

    public async Task ReplayAsync(
        IntegrationEventDeadLetterMessage message,
        CancellationToken cancellationToken)
    {
        if (message.EventClrType == typeof(AssetUnavailableIntegrationEvent).FullName)
        {
            var integrationEvent = JsonSerializer.Deserialize<AssetUnavailableIntegrationEvent>(
                    message.EventJson,
                    SerializerOptions)
                ?? throw new InvalidOperationException($"Dead-letter payload '{message.Id}' could not be deserialized.");
            await capPublisher.PublishAsync(nameof(AssetUnavailableIntegrationEvent), integrationEvent, cancellationToken: cancellationToken);
            return;
        }

        if (message.EventClrType == typeof(AssetUnavailableV2IntegrationEvent).FullName)
        {
            var integrationEvent = JsonSerializer.Deserialize<AssetUnavailableV2IntegrationEvent>(
                    message.EventJson,
                    SerializerOptions)
                ?? throw new InvalidOperationException($"Dead-letter payload '{message.Id}' could not be deserialized.");
            var topic = AssetUnavailableIntegrationEventTopics.ResolveSubscriptionTemplate(
                AssetUnavailableIntegrationEventTopics.V2Template,
                hostEnvironment.EnvironmentName);
            await capPublisher.PublishAsync(topic, integrationEvent, cancellationToken: cancellationToken);
            return;
        }

        throw new InvalidOperationException($"Unsupported MES asset-unavailable dead-letter type '{message.EventClrType}'.");
    }
}
