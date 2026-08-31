using System.Text.Json;
using DotNetCore.CAP;
using Nerv.IIP.Contracts.Maintenance;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventHandlers;

public sealed class SchedulingAssetUnavailableDeadLetterReplayHandler(
    ICapPublisher publisher,
    IHostEnvironment hostEnvironment)
    : IIntegrationEventDeadLetterReplayHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public bool CanReplay(IntegrationEventDeadLetterMessage message) =>
        message.EventClrType == typeof(AssetUnavailableIntegrationEvent).FullName ||
        message.EventClrType == typeof(AssetUnavailableV2IntegrationEvent).FullName;

    public async Task ReplayAsync(IntegrationEventDeadLetterMessage message, CancellationToken cancellationToken)
    {
        if (message.EventClrType == typeof(AssetUnavailableIntegrationEvent).FullName)
        {
            var value = JsonSerializer.Deserialize<AssetUnavailableIntegrationEvent>(message.EventJson, JsonOptions)
                ?? throw new InvalidOperationException("AssetUnavailable v1 dead-letter payload is empty.");
            await publisher.PublishAsync(AssetUnavailableIntegrationEventTopics.V1LegacyAlias, value);
        }
        else
        {
            var v2 = JsonSerializer.Deserialize<AssetUnavailableV2IntegrationEvent>(message.EventJson, JsonOptions)
                ?? throw new InvalidOperationException("AssetUnavailable v2 dead-letter payload is empty.");
            await publisher.PublishAsync(AssetUnavailableIntegrationEventTopics.V2(hostEnvironment.EnvironmentName), v2);
        }
    }
}
