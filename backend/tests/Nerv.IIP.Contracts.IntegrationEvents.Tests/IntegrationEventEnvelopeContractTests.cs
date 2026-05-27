using System.Reflection;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.IndustrialTelemetry;
using Nerv.IIP.Contracts.Maintenance;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Contracts.Wms;

namespace Nerv.IIP.Contracts.IntegrationEvents.Tests;

public sealed class IntegrationEventEnvelopeContractTests
{
    private static readonly string[] RequiredEnvelopeProperties =
    [
        "EventId",
        "EventType",
        "EventVersion",
        "OccurredAtUtc",
        "SourceService",
        "CorrelationId",
        "CausationId",
        "OrganizationId",
        "EnvironmentId",
        "Actor",
        "IdempotencyKey",
        "Payload"
    ];

    public static TheoryData<Type> IntegrationEventTypes()
    {
        return new TheoryData<Type>
        {
            typeof(OperationTaskCompletedIntegrationEvent),
            typeof(OperationTaskFailedIntegrationEvent),
            typeof(OperationTaskRequestedIntegrationEvent),
            typeof(OperationApprovalRequestedIntegrationEvent),
            typeof(OperationApprovalApprovedIntegrationEvent),
            typeof(OperationApprovalRejectedIntegrationEvent),
            typeof(OperationTaskClaimedIntegrationEvent),
            typeof(AuditRecordedIntegrationEvent),
            typeof(AssetUnavailableIntegrationEvent),
            typeof(AssetRestoredIntegrationEvent),
            typeof(WmsIntegrationEvent),
            typeof(DeviceStateChangedIntegrationEvent),
            typeof(AlarmRaisedIntegrationEvent),
            typeof(AlarmClearedIntegrationEvent)
        };
    }

    [Theory]
    [MemberData(nameof(IntegrationEventTypes))]
    public void Integration_events_expose_required_adr_0011_envelope_properties(Type eventType)
    {
        var properties = eventType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(x => x.Name)
            .ToHashSet(StringComparer.Ordinal);

        var missing = RequiredEnvelopeProperties
            .Where(property => !properties.Contains(property))
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"{eventType.FullName} is missing ADR 0011 envelope properties: {string.Join(", ", missing)}");
    }

    [Theory]
    [MemberData(nameof(IntegrationEventTypes))]
    public void Integration_events_implement_compile_time_envelope_contract(Type eventType)
    {
        Assert.True(
            typeof(IIntegrationEventEnvelope).IsAssignableFrom(eventType),
            $"{eventType.FullName} must implement {nameof(IIntegrationEventEnvelope)} for consumer guard validation.");
    }

    [Theory]
    [MemberData(nameof(IntegrationEventTypes))]
    public void Integration_event_payload_is_a_dedicated_contract_type(Type eventType)
    {
        var payloadProperty = eventType.GetProperty("Payload", BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(payloadProperty);
        Assert.False(payloadProperty.PropertyType == typeof(string), $"{eventType.FullName} payload must be structured.");
        Assert.True(payloadProperty.PropertyType.Namespace?.StartsWith("Nerv.IIP.Contracts.", StringComparison.Ordinal) == true);
    }
}
