using System.Net;
using System.Text.Json;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public interface IBusinessMesWorkOrderTransformationClient
{
    Task<BusinessMesWorkOrderTransformationResult> SplitAsync(
        string internalBearerToken,
        BusinessConsoleMesSplitWorkOrderRequest request,
        CancellationToken cancellationToken);

    Task<BusinessMesWorkOrderTransformationResult> MergeAsync(
        string internalBearerToken,
        BusinessConsoleMesMergeWorkOrdersRequest request,
        CancellationToken cancellationToken);

    Task<BusinessMesWorkOrderTransformationReadback> GetReadbackAsync(
        string internalBearerToken,
        BusinessConsoleMesWorkOrderTransformationReadbackRequest request,
        CancellationToken cancellationToken);
}

public sealed class HttpBusinessMesWorkOrderTransformationClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessMesWorkOrderTransformationClient
{
    public async Task<BusinessMesWorkOrderTransformationResult> SplitAsync(
        string internalBearerToken,
        BusinessConsoleMesSplitWorkOrderRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamTransformationResult>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/mes/work-orders/{Uri.EscapeDataString(request.WorkOrderId)}/split",
            new SplitWireRequest(
                request.OrganizationId,
                request.EnvironmentId,
                request.Targets.Select(x => new TargetWireRequest(x.WorkOrderId, x.Quantity)).ToArray(),
                request.Reason,
                request.IdempotencyKey),
            cancellationToken);
        return MapResult(response, "split");
    }

    public async Task<BusinessMesWorkOrderTransformationResult> MergeAsync(
        string internalBearerToken,
        BusinessConsoleMesMergeWorkOrdersRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamTransformationResult>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/mes/work-orders/merge",
            new MergeWireRequest(
                request.OrganizationId,
                request.EnvironmentId,
                request.SourceWorkOrderIds,
                request.TargetWorkOrderId,
                request.Reason,
                request.IdempotencyKey),
            cancellationToken);
        return MapResult(response, "merge");
    }

    public async Task<BusinessMesWorkOrderTransformationReadback> GetReadbackAsync(
        string internalBearerToken,
        BusinessConsoleMesWorkOrderTransformationReadbackRequest request,
        CancellationToken cancellationToken)
    {
        var transformationId = request.TransformationId.Trim();
        var response = await SendAsync<DownstreamTransformationReadback>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/mes/work-order-transformations/{Uri.EscapeDataString(transformationId)}?"
                + Query(("organizationId", request.OrganizationId), ("environmentId", request.EnvironmentId)),
            null,
            cancellationToken);
        return MapReadback(response, transformationId);
    }

    private static BusinessMesWorkOrderTransformationResult MapResult(
        DownstreamTransformationResult response,
        string expectedType)
    {
        var transformationId = ReadStrongId(response.TransformationId);
        var type = ReadType(response.Type);
        var sourceIds = NormalizeIds(response.SourceWorkOrderIds);
        var targetIds = NormalizeIds(response.TargetWorkOrderIds);
        if (transformationId is null
            || type is null
            || !string.Equals(type, expectedType, StringComparison.Ordinal)
            || sourceIds is null
            || targetIds is null
            || sourceIds.Count == 0
            || targetIds.Count == 0)
        {
            throw InvalidDownstreamResponse();
        }

        return new(transformationId, type, sourceIds, targetIds, response.IsIdempotentReplay);
    }

    private static BusinessMesWorkOrderTransformationReadback MapReadback(
        DownstreamTransformationReadback response,
        string requestedTransformationId)
    {
        var transformationId = ReadStrongId(response.TransformationId);
        var type = ReadType(response.Type);
        var lines = response.Lines?
            .Select(line => line is null
                ? null
                : new
                {
                    Line = line,
                    Source = line.SourceWorkOrderId?.Trim(),
                    Target = line.TargetWorkOrderId?.Trim(),
                    Uom = line.UomCode?.Trim(),
                    SourceStatus = line.SourceStatus?.Trim(),
                    TargetStatus = line.TargetStatus?.Trim(),
                })
            .ToArray();
        var idempotencyKey = response.IdempotencyKey?.Trim();
        var actor = response.Actor?.Trim();
        var reason = response.Reason?.Trim();
        if (transformationId is null
            || !MatchesStrongId(transformationId, requestedTransformationId)
            || type is null
            || string.IsNullOrWhiteSpace(idempotencyKey)
            || string.IsNullOrWhiteSpace(actor)
            || string.IsNullOrWhiteSpace(reason)
            || response.OccurredAtUtc == default
            || lines is null
            || lines.Length == 0
            || lines.Any(x => x is null
                || string.IsNullOrWhiteSpace(x.Source)
                || string.IsNullOrWhiteSpace(x.Target)
                || string.IsNullOrWhiteSpace(x.Uom)
                || string.IsNullOrWhiteSpace(x.SourceStatus)
                || string.IsNullOrWhiteSpace(x.TargetStatus)
                || x.Line.Quantity <= 0
                || x.Line.SourceVersion < 0
                || x.Line.TargetVersion < 0))
        {
            throw InvalidDownstreamResponse();
        }

        return new(
            transformationId,
            type,
            idempotencyKey!,
            actor!,
            reason!,
            response.OccurredAtUtc,
            lines.Select(x => new BusinessMesWorkOrderTransformationLine(
                x!.Source!,
                x.Target!,
                x!.Line.Quantity,
                x.Uom!,
                x.SourceStatus!,
                x.TargetStatus!,
                x!.Line.SourceVersion,
                x!.Line.TargetVersion)).ToArray());
    }

    private static string? ReadStrongId(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object
            || !value.TryGetProperty("id", out var id)
            || id.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = id.GetString()?.Trim();
        return Guid.TryParse(text, out var parsed) && parsed != Guid.Empty
            ? parsed.ToString("D")
            : null;
    }

    private static bool MatchesStrongId(string actual, string requested) =>
        Guid.TryParse(actual, out var actualId)
        && actualId != Guid.Empty
        && Guid.TryParse(requested, out var requestedId)
        && requestedId != Guid.Empty
        && actualId == requestedId;

    private static string? ReadType(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString()?.Trim().ToLowerInvariant() switch
            {
                "split" => "split",
                "merge" => "merge",
                _ => null,
            };
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var numeric))
        {
            return numeric switch
            {
                0 => "split",
                1 => "merge",
                _ => null,
            };
        }

        return null;
    }

    private static IReadOnlyCollection<string>? NormalizeIds(IReadOnlyCollection<string>? values)
    {
        if (values is null)
        {
            return null;
        }

        var normalized = values.Select(x => x?.Trim() ?? string.Empty).ToArray();
        return normalized.Any(string.IsNullOrWhiteSpace)
            ? null
            : normalized;
    }

    private static BusinessServiceProxyException InvalidDownstreamResponse() =>
        BusinessServiceProxyException.FromSafeDownstreamMessage(
            HttpStatusCode.BadGateway,
            "downstream-invalid-response");

    private sealed record SplitWireRequest(
        string OrganizationId,
        string EnvironmentId,
        IReadOnlyCollection<TargetWireRequest> Targets,
        string Reason,
        string IdempotencyKey);

    private sealed record TargetWireRequest(string WorkOrderId, decimal Quantity);

    private sealed record MergeWireRequest(
        string OrganizationId,
        string EnvironmentId,
        IReadOnlyCollection<string> SourceWorkOrderIds,
        string TargetWorkOrderId,
        string Reason,
        string IdempotencyKey);

    private sealed record DownstreamTransformationResult(
        JsonElement TransformationId,
        JsonElement Type,
        IReadOnlyCollection<string>? SourceWorkOrderIds,
        IReadOnlyCollection<string>? TargetWorkOrderIds,
        bool IsIdempotentReplay);

    private sealed record DownstreamTransformationReadback(
        JsonElement TransformationId,
        JsonElement Type,
        string? IdempotencyKey,
        string? Actor,
        string? Reason,
        DateTimeOffset OccurredAtUtc,
        IReadOnlyCollection<DownstreamTransformationLine?>? Lines);

    private sealed record DownstreamTransformationLine(
        string? SourceWorkOrderId,
        string? TargetWorkOrderId,
        decimal Quantity,
        string? UomCode,
        string? SourceStatus,
        string? TargetStatus,
        long SourceVersion,
        long TargetVersion);
}
