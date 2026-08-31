using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using FastEndpoints;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed record BusinessConsoleDispatchBarcodePrintBatchBody(
    [property: JsonRequired, Required] string PrintBatchId,
    [property: JsonRequired, Required] string PrinterId);

public sealed record BusinessConsoleDispatchBarcodePrintBatchRequest(
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    [property: FromBody] BusinessConsoleDispatchBarcodePrintBatchBody Body);

public sealed record BusinessConsoleBarcodePrintLifecycleResponse(string PrintBatchId);

public sealed record BusinessConsoleReprintBarcodeLabelBody(
    [property: JsonRequired, Required] string PrintBatchId,
    [property: JsonRequired, Required] int SequenceNo,
    [property: JsonRequired, Required] string PrinterId);

public sealed record BusinessConsoleReprintBarcodeLabelRequest(
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    [property: FromBody] BusinessConsoleReprintBarcodeLabelBody Body);

public sealed record BusinessConsoleReprintBarcodeLabelResponse(
    string PrintBatchId,
    string Status,
    string? PrintJobId,
    string? FailureReason);

public sealed record BusinessConsoleVoidBarcodeLabelBody(
    [property: JsonRequired, Required] string PrintBatchId,
    [property: JsonRequired, Required] int SequenceNo,
    [property: JsonRequired, Required] string Reason);

public sealed record BusinessConsoleVoidBarcodeLabelRequest(
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    [property: FromBody] BusinessConsoleVoidBarcodeLabelBody Body);
