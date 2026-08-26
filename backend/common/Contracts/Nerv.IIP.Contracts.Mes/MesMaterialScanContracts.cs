using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nerv.IIP.Contracts.Mes;

public sealed record BusinessConsoleMesMaterialScanPrevalidationRequest(
    [property: JsonRequired, Required] string OrganizationId,
    [property: JsonRequired, Required] string EnvironmentId,
    [property: JsonRequired, Required] string MaterialIssueRequestId,
    [property: JsonRequired, Required] string WorkOrderId,
    [property: JsonRequired, Required] string OperationTaskId);

public sealed record BusinessConsoleMesMaterialScanPrevalidationResponse(
    [property: JsonRequired, Required] MesMaterialScanDecision Decision,
    [property: JsonRequired, Required] string ReasonCode,
    [property: JsonRequired, Required] string MaterialIssueRequestId,
    [property: JsonRequired, Required] string WorkOrderId,
    [property: JsonRequired, Required] string OperationTaskId,
    string? MaterialId,
    string? MaterialLotId,
    string? MaterialQualification,
    [property: JsonRequired, Required] DateTimeOffset EvaluatedAtUtc);

[JsonConverter(typeof(MesMaterialScanDecisionJsonConverter))]
public enum MesMaterialScanDecision
{
    Accepted,
    Rejected,
}

public sealed class MesMaterialScanDecisionJsonConverter()
    : JsonStringEnumConverter<MesMaterialScanDecision>(JsonNamingPolicy.CamelCase, allowIntegerValues: false);
