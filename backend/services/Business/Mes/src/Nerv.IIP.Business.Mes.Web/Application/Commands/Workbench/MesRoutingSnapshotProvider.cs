using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;

public sealed record MesRoutingSnapshotRequest(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string SkuId,
    string? ProductionVersionId,
    decimal WorkOrderQuantity,
    DateTimeOffset CapturedAtUtc);

public sealed record MesRoutingOperationSnapshot(
    int Sequence,
    string OperationCode,
    string WorkCenterId,
    IReadOnlyCollection<string> AlternativeWorkCenterIds,
    int StandardMinutes,
    bool RequiresQualityInspection);

public enum MesRoutingSnapshotStatus
{
    Captured,
    Missing,
}

public sealed record MesRoutingSnapshotResult(
    MesRoutingSnapshotStatus Status,
    string SourceSystem,
    IReadOnlyCollection<MesRoutingOperationSnapshot> Operations)
{
    public static MesRoutingSnapshotResult Captured(
        string sourceSystem,
        IReadOnlyCollection<MesRoutingOperationSnapshot> operations) =>
        new(MesRoutingSnapshotStatus.Captured, sourceSystem, operations);

    public static MesRoutingSnapshotResult Missing(string sourceSystem) =>
        new(MesRoutingSnapshotStatus.Missing, sourceSystem, []);
}

public interface IMesRoutingSnapshotProvider
{
    Task<MesRoutingSnapshotResult> GetSnapshotAsync(
        MesRoutingSnapshotRequest request,
        CancellationToken cancellationToken);
}

public sealed class NullMesRoutingSnapshotProvider : IMesRoutingSnapshotProvider
{
    public Task<MesRoutingSnapshotResult> GetSnapshotAsync(
        MesRoutingSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(MesRoutingSnapshotResult.Missing("product-engineering:not-configured"));
    }
}

public sealed class HttpMesProductEngineeringRoutingSnapshotProvider(
    MesProductEngineeringHttpClient productEngineeringClient,
    IInternalServiceTokenProvider? internalTokenProvider = null) : IMesRoutingSnapshotProvider
{
    private const string ActiveProductionVersionStatus = "active";
    private const string PublishedRoutingStatus = "published";

    public async Task<MesRoutingSnapshotResult> GetSnapshotAsync(
        MesRoutingSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ProductionVersionId))
        {
            return MesRoutingSnapshotResult.Missing("product-engineering:missing-production-version");
        }

        var productionVersionId = request.ProductionVersionId.Trim();
        var selectedVersion = await SendOptionalAsync<RoutingProductionVersionResponse>(
            "/api/business/v1/engineering/production-versions/resolve?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", request.SkuId),
                ("effectiveDate", DateOnly.FromDateTime(request.CapturedAtUtc.UtcDateTime)),
                ("lotSize", request.WorkOrderQuantity)),
            cancellationToken);
        if (selectedVersion is null ||
            !string.Equals(selectedVersion.ProductionVersionId, productionVersionId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(selectedVersion.OrganizationId, request.OrganizationId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(selectedVersion.EnvironmentId, request.EnvironmentId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(selectedVersion.SkuCode, request.SkuId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(selectedVersion.Status, ActiveProductionVersionStatus, StringComparison.OrdinalIgnoreCase))
        {
            return MesRoutingSnapshotResult.Missing($"product-engineering:production-version:{productionVersionId}");
        }

        if (!TryParseVersionReference(selectedVersion.RoutingVersionId, out var routingCode, out var revision))
        {
            return MesRoutingSnapshotResult.Missing($"product-engineering:routing:{selectedVersion.RoutingVersionId}");
        }

        var routing = await SendOptionalAsync<RoutingDetailResponse>(
            $"/api/business/v1/engineering/routings/{EscapePath(routingCode)}/{EscapePath(revision)}?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId)),
            cancellationToken);
        if (routing is null ||
            !string.Equals(routing.RoutingCode, routingCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(routing.Revision, revision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(routing.SkuCode, request.SkuId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(routing.Status, PublishedRoutingStatus, StringComparison.OrdinalIgnoreCase) ||
            !IsValidRouting(routing.Operations))
        {
            return MesRoutingSnapshotResult.Missing($"product-engineering:routing:{selectedVersion.RoutingVersionId}");
        }

        var operations = routing.Operations
            .OrderBy(x => x.Sequence)
            .Select(x => new MesRoutingOperationSnapshot(
                x.Sequence,
                x.OperationCode.Trim(),
                x.WorkCenterCode.Trim(),
                [],
                x.StandardMinutes,
                x.RequiresQualityInspection))
            .ToArray();

        return MesRoutingSnapshotResult.Captured(
            $"product-engineering-http:{productionVersionId}:{selectedVersion.RoutingVersionId}",
            operations);
    }

    private async Task<T?> SendOptionalAsync<T>(string requestUri, CancellationToken cancellationToken)
        where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        if (!string.IsNullOrWhiteSpace(internalTokenProvider?.BearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalTokenProvider.BearerToken);
        }

        HttpResponseMessage response;
        try
        {
            response = await productEngineeringClient.HttpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new KnownException($"ROUTING_SNAPSHOT_SOURCE_UNAVAILABLE: ProductEngineering 工艺路线来源服务暂不可用。{exception.Message}");
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new KnownException($"ROUTING_SNAPSHOT_SOURCE_UNAVAILABLE: ProductEngineering 工艺路线来源服务请求超时。{exception.Message}");
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new KnownException($"ROUTING_SNAPSHOT_SOURCE_UNAVAILABLE: ProductEngineering 工艺路线来源服务返回 {(int)response.StatusCode} {response.ReasonPhrase}。");
            }

            var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<T>>(cancellationToken);
            return envelope?.Data;
        }
    }

    private static bool IsValidRouting(IReadOnlyCollection<RoutingOperationResponse> operations)
    {
        return operations.Count > 0 &&
            operations.All(x =>
                x.Sequence > 0 &&
                !string.IsNullOrWhiteSpace(x.OperationCode) &&
                !string.IsNullOrWhiteSpace(x.WorkCenterCode) &&
                x.StandardMinutes > 0) &&
            operations.Select(x => x.Sequence).Distinct().Count() == operations.Count;
    }

    private static bool TryParseVersionReference(string versionId, out string code, out string revision)
    {
        code = string.Empty;
        revision = string.Empty;
        var parts = versionId.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            return false;
        }

        code = parts[0];
        revision = parts[1];
        return true;
    }

    private static string EscapePath(string value) => Uri.EscapeDataString(value);

    private static string Query(params (string Name, object? Value)[] values)
    {
        return string.Join('&', values
            .Where(x => x.Value is not null && !string.IsNullOrWhiteSpace(Convert.ToString(x.Value, CultureInfo.InvariantCulture)))
            .Select(x => $"{Uri.EscapeDataString(x.Name)}={Uri.EscapeDataString(FormatValue(x.Value!))}"));
    }

    private static string FormatValue(object value) => value switch
    {
        DateOnly date => date.ToString("O", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };
}

internal sealed record RoutingProductionVersionResponse(
    string ProductionVersionId,
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string MbomVersionId,
    string RoutingVersionId,
    DateOnly EffectiveDate,
    decimal LotSize,
    string Status);

internal sealed record RoutingDetailResponse(
    string RoutingCode,
    string Revision,
    string SkuCode,
    string Status,
    DateOnly? EffectiveDate,
    IReadOnlyCollection<RoutingOperationResponse> Operations);

internal sealed record RoutingOperationResponse(
    int Sequence,
    string WorkCenterCode,
    string OperationCode,
    string OperationName,
    int StandardMinutes,
    int SetupMinutes,
    int RunMinutes,
    int TeardownMinutes,
    string ControlKey,
    bool RequiresReporting,
    bool RequiresQualityInspection,
    bool IsOutsourced);
