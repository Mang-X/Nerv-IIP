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

    /// <summary>
    /// 把 DLQ 里保存的完整 envelope 重新发布到 canonical topic，让它经真实 CAP transport/group/retry 重入消费者。
    /// 反序列化使用契约自带的 wire converter：v2 converter 在 Read 时执行 wire 契约校验（版本、事件类型、
    /// source service、UTC 时间等），一条违反契约的 envelope（例如错误 source service）在生产中根本到不了
    /// 消费者，也不可能被重入成功。这类行以可操作的原因拒绝 replay，由执行器记为 replay 失败并保留原行，
    /// 而不是把一个必然再次失败的 envelope 推回 broker。
    /// </summary>
    public async Task ReplayAsync(IntegrationEventDeadLetterMessage message, CancellationToken cancellationToken)
    {
        if (message.EventClrType == typeof(AssetUnavailableIntegrationEvent).FullName)
        {
            var value = Deserialize<AssetUnavailableIntegrationEvent>(message, "v1");
            await publisher.PublishAsync(AssetUnavailableIntegrationEventTopics.V1LegacyAlias, value);
        }
        else
        {
            var v2 = Deserialize<AssetUnavailableV2IntegrationEvent>(message, "v2");
            await publisher.PublishAsync(AssetUnavailableIntegrationEventTopics.V2(hostEnvironment.EnvironmentName), v2);
        }
    }

    private static TIntegrationEvent Deserialize<TIntegrationEvent>(IntegrationEventDeadLetterMessage message, string version)
    {
        try
        {
            return JsonSerializer.Deserialize<TIntegrationEvent>(message.EventJson, JsonOptions)
                ?? throw new InvalidOperationException($"AssetUnavailable {version} dead-letter payload is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"AssetUnavailable {version} dead-letter {message.Id} (eventId '{message.EventId}') cannot be replayed: the stored envelope violates the wire contract and would be rejected again on consumption. {exception.Message}",
                exception);
        }
    }
}
