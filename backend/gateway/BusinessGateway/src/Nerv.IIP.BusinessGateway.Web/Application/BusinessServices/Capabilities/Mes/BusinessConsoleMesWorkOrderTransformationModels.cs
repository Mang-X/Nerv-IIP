using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using FastEndpoints;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed record BusinessConsoleMesWorkOrderTransformationTargetRequest(
    [property: JsonRequired, Required] string WorkOrderId,
    [property: JsonRequired, Required] decimal Quantity);

public sealed record BusinessConsoleMesSplitWorkOrderRequest(
    [property: RouteParam] string WorkOrderId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    [property: JsonRequired, Required] IReadOnlyCollection<BusinessConsoleMesWorkOrderTransformationTargetRequest> Targets,
    [property: JsonRequired, Required] string Reason,
    [property: JsonRequired, Required] string IdempotencyKey,
    [property: QueryParam] string? ScopeKind = null,
    [property: QueryParam] string? ScopeId = null);

public sealed record BusinessConsoleMesMergeWorkOrdersRequest(
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    [property: JsonRequired, Required] IReadOnlyCollection<string> SourceWorkOrderIds,
    [property: JsonRequired, Required] string TargetWorkOrderId,
    [property: JsonRequired, Required] string Reason,
    [property: JsonRequired, Required] string IdempotencyKey,
    [property: QueryParam] string? ScopeKind = null,
    [property: QueryParam] string? ScopeId = null);

public sealed record BusinessConsoleMesWorkOrderTransformationReadbackRequest(
    [property: RouteParam] string TransformationId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    [property: QueryParam] string? ScopeKind = null,
    [property: QueryParam] string? ScopeId = null);

public sealed record BusinessConsoleMesWorkOrderTransformationMutationResponse(
    [property: JsonRequired, Required] bool Accepted,
    [property: JsonRequired, Required] string TransformationId,
    [property: JsonRequired, Required] string Type,
    [property: JsonRequired, Required] IReadOnlyCollection<string> SourceWorkOrderIds,
    [property: JsonRequired, Required] IReadOnlyCollection<string> TargetWorkOrderIds,
    [property: JsonRequired, Required] bool IsIdempotentReplay,
    [property: JsonRequired, Required] BusinessConsoleOperationReceipt OperationReceipt);

public sealed record BusinessConsoleMesWorkOrderTransformationReadbackResponse(
    [property: JsonRequired, Required] string TransformationId,
    [property: JsonRequired, Required] string Type,
    [property: JsonRequired, Required] string IdempotencyKey,
    [property: JsonRequired, Required] string Actor,
    [property: JsonRequired, Required] string Reason,
    [property: JsonRequired, Required] DateTimeOffset OccurredAtUtc,
    [property: JsonRequired, Required] IReadOnlyCollection<BusinessConsoleMesWorkOrderTransformationLineResponse> Lines);

public sealed record BusinessConsoleMesWorkOrderTransformationLineResponse(
    [property: JsonRequired, Required] string SourceWorkOrderId,
    [property: JsonRequired, Required] string TargetWorkOrderId,
    [property: JsonRequired, Required] decimal Quantity,
    [property: JsonRequired, Required] string UomCode,
    [property: JsonRequired, Required] string SourceStatus,
    [property: JsonRequired, Required] string TargetStatus,
    [property: JsonRequired, Required] long SourceVersion,
    [property: JsonRequired, Required] long TargetVersion);

public sealed record BusinessMesWorkOrderTransformationResult(
    string TransformationId,
    string Type,
    IReadOnlyCollection<string> SourceWorkOrderIds,
    IReadOnlyCollection<string> TargetWorkOrderIds,
    bool IsIdempotentReplay);

public sealed record BusinessMesWorkOrderTransformationReadback(
    string TransformationId,
    string Type,
    string IdempotencyKey,
    string Actor,
    string Reason,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyCollection<BusinessMesWorkOrderTransformationLine> Lines);

public sealed record BusinessMesWorkOrderTransformationLine(
    string SourceWorkOrderId,
    string TargetWorkOrderId,
    decimal Quantity,
    string UomCode,
    string SourceStatus,
    string TargetStatus,
    long SourceVersion,
    long TargetVersion);
