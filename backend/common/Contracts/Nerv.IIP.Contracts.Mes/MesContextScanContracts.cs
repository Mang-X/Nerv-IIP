using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nerv.IIP.Contracts.Mes;

public sealed record MesContextScanPrevalidationRequest(
    [property: JsonRequired, Required] string OrganizationId,
    [property: JsonRequired, Required] string EnvironmentId,
    [property: JsonRequired, Required] string WorkOrderId,
    [property: JsonRequired, Required] string OperationTaskId,
    string? ScannedOperationTaskId,
    string? DeviceAssetId,
    string? UserId);

public sealed record MesContextScanPrevalidationResponse(
    [property: JsonRequired, Required] MesContextScanDecision Decision,
    [property: JsonRequired, Required] string ReasonCode,
    [property: JsonRequired, Required] string WorkOrderId,
    [property: JsonRequired, Required] string OperationTaskId,
    [property: JsonRequired, Required] MesContextScanObjectType ObjectType,
    [property: JsonRequired, Required] string ScannedObjectId,
    [property: JsonRequired, Required] DateTimeOffset EvaluatedAtUtc);

[JsonConverter(typeof(MesContextScanDecisionJsonConverter))]
public enum MesContextScanDecision
{
    Accepted,
    Rejected,
}

public sealed class MesContextScanDecisionJsonConverter()
    : JsonStringEnumConverter<MesContextScanDecision>(JsonNamingPolicy.CamelCase, allowIntegerValues: false);

[JsonConverter(typeof(MesContextScanObjectTypeJsonConverter))]
public enum MesContextScanObjectType
{
    OperationTask,
    DeviceAsset,
    Personnel,
}

public sealed class MesContextScanObjectTypeJsonConverter()
    : JsonStringEnumConverter<MesContextScanObjectType>(JsonNamingPolicy.CamelCase, allowIntegerValues: false);
