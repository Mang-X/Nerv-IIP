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

internal static class MesRoutingSnapshotSources
{
    public const string NotConfigured = "product-engineering:not-configured";
    public const string MissingProductionVersion = "product-engineering:missing-production-version";

    public static string ProductionVersion(string productionVersionId) =>
        $"product-engineering:production-version:{productionVersionId}";

    public static string Routing(string routingVersionId) =>
        $"product-engineering:routing:{routingVersionId}";

    public static string Captured(string productionVersionId, string routingVersionId) =>
        $"product-engineering-http:{productionVersionId}:{routingVersionId}";
}

public sealed class NullMesRoutingSnapshotProvider : IMesRoutingSnapshotProvider
{
    public Task<MesRoutingSnapshotResult> GetSnapshotAsync(
        MesRoutingSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(MesRoutingSnapshotResult.Missing(MesRoutingSnapshotSources.NotConfigured));
    }
}

public sealed class MesRoutingSnapshotMissingException(string source)
    : KnownException($"ROUTING_SNAPSHOT_MISSING: 工单缺少已发布生产版本的工艺路线快照，Source = {source}。")
{
    public string DiagnosticSource { get; } = source;
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
            return MesRoutingSnapshotResult.Missing(MesRoutingSnapshotSources.MissingProductionVersion);
        }

        var productionVersionId = request.ProductionVersionId.Trim();
        var selectedVersion = await SendOptionalAsync<RoutingProductionVersionSnapshotResponse>(
            $"/api/business/v1/engineering/production-versions/{EscapePath(productionVersionId)}/routing-snapshot?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId)),
            cancellationToken);
        if (selectedVersion is null ||
            !string.Equals(selectedVersion.ProductionVersionId, productionVersionId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(selectedVersion.OrganizationId, request.OrganizationId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(selectedVersion.EnvironmentId, request.EnvironmentId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(selectedVersion.SkuCode, request.SkuId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(selectedVersion.ProductionVersionStatus, ActiveProductionVersionStatus, StringComparison.OrdinalIgnoreCase))
        {
            return MesRoutingSnapshotResult.Missing(MesRoutingSnapshotSources.ProductionVersion(productionVersionId));
        }

        if (string.IsNullOrWhiteSpace(selectedVersion.RoutingVersionId) ||
            string.IsNullOrWhiteSpace(selectedVersion.RoutingCode) ||
            string.IsNullOrWhiteSpace(selectedVersion.RoutingRevision) ||
            !string.Equals(selectedVersion.RoutingStatus, PublishedRoutingStatus, StringComparison.OrdinalIgnoreCase) ||
            !IsValidRouting(selectedVersion.Operations))
        {
            return MesRoutingSnapshotResult.Missing(MesRoutingSnapshotSources.Routing(selectedVersion.RoutingVersionId));
        }

        var operations = selectedVersion.Operations
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
            MesRoutingSnapshotSources.Captured(productionVersionId, selectedVersion.RoutingVersionId),
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

internal sealed record RoutingProductionVersionSnapshotResponse(
    string ProductionVersionId,
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string ProductionVersionStatus,
    string RoutingVersionId,
    string RoutingCode,
    string RoutingRevision,
    string RoutingStatus,
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
