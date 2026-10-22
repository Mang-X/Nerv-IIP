using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Contracts.Mes;

public sealed record AndonCallEscalatedIntegrationEvent(
    string EventId, string EventType, int EventVersion, DateTimeOffset OccurredAtUtc,
    string SourceService, string CorrelationId, string CausationId,
    string OrganizationId, string EnvironmentId, string Actor, string IdempotencyKey,
    AndonCallEscalatedPayload Payload) : IIntegrationEventEnvelope
{
    public const string Type = "mes.AndonCallEscalated";
    public const int Version = 1;
    public const string TopicTemplate =
        "nerv-iip.{deployment-profile}.business-mes.mes.andon-call-escalated.v1";
    public static string Topic(string deploymentEnvironment) =>
        TopicTemplate.Replace(
            "{deployment-profile}",
            deploymentEnvironment.ToLowerInvariant(),
            StringComparison.Ordinal);

    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record AndonCallEscalatedPayload(
    string CallId, string Category, string WorkOrderId, string OperationTaskId, string WorkCenterId,
    string CallerId, DateTimeOffset RaisedAtUtc, string RecipientId, double UnclaimedTimeoutSeconds);
