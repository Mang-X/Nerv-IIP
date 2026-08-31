using System.Text.Json;
using System.Text.Json.Serialization;
using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Contracts.Maintenance;

public static class MaintenanceIntegrationEventTypes
{
    public const string AssetUnavailable = "maintenance.AssetUnavailable";
    public const string AssetRestored = "maintenance.AssetRestored";
}

public static class MaintenanceIntegrationEventVersions
{
    public const int V1 = 1;
    public const int V2 = 2;
}

public static class MaintenanceIntegrationEventSources
{
    public const string Maintenance = "maintenance";
    public const string BusinessMaintenance = "business-maintenance";
}

public static class AssetUnavailableIntegrationEventTopics
{
    private const string V2Prefix = "nerv-iip.";
    private const string V2Suffix = ".business-maintenance.maintenance.asset-unavailable.v2";

    public const string DeploymentProfileToken = "{deployment-profile}";
    public const string V1LegacyAlias = nameof(AssetUnavailableIntegrationEvent);
    public const string V2Template =
        "nerv-iip.{deployment-profile}.business-maintenance.maintenance.asset-unavailable.v2";

    public static string V2(string deploymentProfile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentProfile);
        return $"nerv-iip.{NormalizeDeploymentProfile(deploymentProfile)}.business-maintenance.maintenance.asset-unavailable.v2";
    }

    public static string? CanonicalSubscriptionTemplate(Type integrationEventType)
    {
        ArgumentNullException.ThrowIfNull(integrationEventType);
        return integrationEventType == typeof(AssetUnavailableV2IntegrationEvent) ? V2Template : null;
    }

    public static string ResolveSubscriptionTemplate(string topic, string deploymentProfile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentProfile);
        return topic.Contains(DeploymentProfileToken, StringComparison.Ordinal)
            ? topic.Replace(
                DeploymentProfileToken,
                NormalizeDeploymentProfile(deploymentProfile),
                StringComparison.Ordinal)
            : topic;
    }

    public static bool TryParseV2(string? topic, out string deploymentProfile)
    {
        deploymentProfile = string.Empty;
        if (string.IsNullOrEmpty(topic)
            || !topic.StartsWith(V2Prefix, StringComparison.Ordinal)
            || !topic.EndsWith(V2Suffix, StringComparison.Ordinal))
            return false;

        var profile = topic[V2Prefix.Length..^V2Suffix.Length];
        if (string.IsNullOrWhiteSpace(profile)
            || profile != NormalizeDeploymentProfile(profile)
            || V2(profile) != topic)
            return false;

        deploymentProfile = profile;
        return true;
    }

    public static void EnsureV2EnvelopeMatches(
        string topic,
        AssetUnavailableV2IntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        AssetUnavailableV2WireContract.Validate(integrationEvent);
        if (!TryParseV2(topic, out _))
            throw new ArgumentException(
                "AssetUnavailable V2 envelope requires the canonical AssetUnavailable V2 topic.",
                nameof(topic));
    }

    private static string NormalizeDeploymentProfile(string deploymentProfile) =>
        deploymentProfile.Trim().ToLowerInvariant();
}

public sealed record AssetUnavailableIntegrationEvent(
    string EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    string SourceService,
    string CorrelationId,
    string CausationId,
    string OrganizationId,
    string EnvironmentId,
    string Actor,
    string IdempotencyKey,
    AssetUnavailablePayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record AssetUnavailablePayload(
    string DeviceAssetId,
    string Reason,
    DateTimeOffset FromUtc);

[JsonConverter(typeof(AssetUnavailableV2IntegrationEventJsonConverter))]
public sealed record AssetUnavailableV2IntegrationEvent(
    string EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    string SourceService,
    string CorrelationId,
    string CausationId,
    string OrganizationId,
    string EnvironmentId,
    string Actor,
    string IdempotencyKey,
    AssetUnavailableV2Payload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record AssetUnavailableV2Payload(
    string DeviceAssetId,
    string ReasonCode,
    DateTimeOffset FromUtc);

internal static class AssetUnavailableV2WireContract
{
    public static void Validate(AssetUnavailableV2IntegrationEvent integrationEvent)
    {
        Require(integrationEvent.EventId, "eventId");
        if (integrationEvent.EventVersion != MaintenanceIntegrationEventVersions.V2)
            throw new JsonException("AssetUnavailable V2 envelope requires eventVersion 2.");
        if (integrationEvent.EventType != MaintenanceIntegrationEventTypes.AssetUnavailable)
            throw new JsonException("AssetUnavailable V2 envelope requires the maintenance.AssetUnavailable event type.");
        if (integrationEvent.OccurredAtUtc == default)
            throw new JsonException("AssetUnavailable V2 envelope requires occurredAtUtc.");
        if (integrationEvent.SourceService != MaintenanceIntegrationEventSources.BusinessMaintenance)
            throw new JsonException("AssetUnavailable V2 envelope requires the business-maintenance source service.");
        Require(integrationEvent.CorrelationId, "correlationId");
        Require(integrationEvent.CausationId, "causationId");
        Require(integrationEvent.OrganizationId, "organizationId");
        Require(integrationEvent.EnvironmentId, "environmentId");
        Require(integrationEvent.Actor, "actor");
        Require(integrationEvent.IdempotencyKey, "idempotencyKey");
        if (integrationEvent.Payload is null)
            throw new JsonException("AssetUnavailable V2 payload is required.");
        Require(integrationEvent.Payload.DeviceAssetId, "payload.deviceAssetId");
        if (string.IsNullOrWhiteSpace(integrationEvent.Payload.ReasonCode))
            throw new JsonException("AssetUnavailable V2 payload requires reasonCode.");
        if (integrationEvent.Payload.FromUtc == default)
            throw new JsonException("AssetUnavailable V2 payload requires fromUtc.");
    }

    private static void Require(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new JsonException($"AssetUnavailable V2 envelope requires {fieldName}.");
    }
}

public sealed class AssetUnavailableV2IntegrationEventJsonConverter
    : JsonConverter<AssetUnavailableV2IntegrationEvent>
{
    public override AssetUnavailableV2IntegrationEvent Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var dto = JsonSerializer.Deserialize<AssetUnavailableV2Dto>(ref reader, options)
            ?? throw new JsonException("AssetUnavailable V2 envelope is required.");
        var integrationEvent = new AssetUnavailableV2IntegrationEvent(
            dto.EventId,
            dto.EventType,
            dto.EventVersion,
            dto.OccurredAtUtc,
            dto.SourceService,
            dto.CorrelationId,
            dto.CausationId,
            dto.OrganizationId,
            dto.EnvironmentId,
            dto.Actor,
            dto.IdempotencyKey,
            dto.Payload!);
        AssetUnavailableV2WireContract.Validate(integrationEvent);
        return integrationEvent;
    }

    public override void Write(
        Utf8JsonWriter writer,
        AssetUnavailableV2IntegrationEvent value,
        JsonSerializerOptions options)
    {
        AssetUnavailableV2WireContract.Validate(value);
        JsonSerializer.Serialize(
            writer,
            new AssetUnavailableV2Dto(
                value.EventId,
                value.EventType,
                value.EventVersion,
                value.OccurredAtUtc,
                value.SourceService,
                value.CorrelationId,
                value.CausationId,
                value.OrganizationId,
                value.EnvironmentId,
                value.Actor,
                value.IdempotencyKey,
                value.Payload),
            options);
    }

    private sealed record AssetUnavailableV2Dto(
        string EventId,
        string EventType,
        int EventVersion,
        DateTimeOffset OccurredAtUtc,
        string SourceService,
        string CorrelationId,
        string CausationId,
        string OrganizationId,
        string EnvironmentId,
        string Actor,
        string IdempotencyKey,
        AssetUnavailableV2Payload? Payload);
}

public sealed record AssetRestoredIntegrationEvent(
    string EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    string SourceService,
    string CorrelationId,
    string CausationId,
    string OrganizationId,
    string EnvironmentId,
    string Actor,
    string IdempotencyKey,
    AssetRestoredPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record AssetRestoredPayload(
    string DeviceAssetId,
    DateTimeOffset RestoredAtUtc);
