using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

public interface ISchedulingMaterialReadinessProvider
{
    Task<IReadOnlyCollection<SchedulingMaterialReadinessContract>> QueryAsync(
        SchedulingProblemContract problem,
        CancellationToken cancellationToken);
}

public sealed class NoopSchedulingMaterialReadinessProvider : ISchedulingMaterialReadinessProvider
{
    public Task<IReadOnlyCollection<SchedulingMaterialReadinessContract>> QueryAsync(
        SchedulingProblemContract problem,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(problem);
        return Task.FromResult<IReadOnlyCollection<SchedulingMaterialReadinessContract>>([]);
    }
}

public sealed class HttpSchedulingMaterialReadinessProvider(
    IHttpClientFactory httpClientFactory,
    IInternalServiceTokenProvider? internalTokenProvider,
    ILogger<HttpSchedulingMaterialReadinessProvider> logger)
    : ISchedulingMaterialReadinessProvider
{
    public const string MesClientName = "SchedulingMesMaterialReadiness";
    public const string SourceUnavailableReasonCode = "mes.materialReadinessSourceUnavailable";

    public async Task<IReadOnlyCollection<SchedulingMaterialReadinessContract>> QueryAsync(
        SchedulingProblemContract problem,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(problem);

        var workOrderIds = problem.Orders
            .Select(x => x.OrderId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (workOrderIds.Length == 0)
        {
            return [];
        }

        var readiness = await QueryOrdersAsync(problem.OrganizationId, problem.EnvironmentId, workOrderIds, cancellationToken);
        return readiness
            .OrderBy(x => x.ScopeType, StringComparer.Ordinal)
            .ThenBy(x => x.ScopeId, StringComparer.Ordinal)
            .ThenBy(x => x.MaterialReadyUtc)
            .ThenBy(x => string.Join('|', x.ReasonCodes), StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<IReadOnlyCollection<SchedulingMaterialReadinessContract>> QueryOrdersAsync(
        string organizationId,
        string environmentId,
        string[] workOrderIds,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/business/v1/mes/work-orders/material-readiness/batch")
        {
            Content = JsonContent.Create(new BatchMaterialReadinessRequest(
                organizationId, environmentId, workOrderIds), options: SchedulingJson.Options)
        };
        var bearerToken = internalTokenProvider?.BearerToken;
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        try
        {
            var client = httpClientFactory.CreateClient(MesClientName);
            using var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var readiness = await ReadReadinessResponseAsync(response.Content, cancellationToken);
            if (readiness is null || readiness.Items.Count != workOrderIds.Length ||
                !readiness.Items.Select(x => x.WorkOrderId).ToHashSet(StringComparer.Ordinal)
                    .SetEquals(workOrderIds))
            {
                logger.LogWarning(
                    "Scheduling material readiness source MES returned an incomplete batch for organization {OrganizationId}.",
                    organizationId);
                return SourceUnavailable(workOrderIds);
            }

            return readiness.Items.SelectMany(ToSchedulingReadiness).ToArray();
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(
                exception,
                "Scheduling material readiness source MES was unavailable for organization {OrganizationId}.",
                organizationId);
            return SourceUnavailable(workOrderIds);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                exception,
                "Scheduling material readiness source MES timed out for organization {OrganizationId}.",
                organizationId);
            return SourceUnavailable(workOrderIds);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(
                exception,
                "Scheduling material readiness source MES returned an invalid response for organization {OrganizationId}.",
                organizationId);
            return SourceUnavailable(workOrderIds);
        }
    }

    private static async Task<MesMaterialReadinessBatchResponse?> ReadReadinessResponseAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        var json = await content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("success", out var success) &&
            success.ValueKind == JsonValueKind.False)
        {
            return null;
        }

        var payload = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data)
            ? data
            : root;
        if (payload.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        var readiness = payload.Deserialize<MesMaterialReadinessBatchResponse>(SchedulingJson.Options);
        return readiness?.Items is null || readiness.Items.Any(x =>
               x is null || string.IsNullOrWhiteSpace(x.WorkOrderId) ||
               string.IsNullOrWhiteSpace(x.ReadinessStatus) ||
               x.BlockingReasons is null || x.Items is null)
            ? null
            : readiness;
    }

    private static IReadOnlyCollection<SchedulingMaterialReadinessContract> ToSchedulingReadiness(
        MesMaterialReadinessResponse response)
    {
        if (string.Equals(response.ReadinessStatus, "Ready", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        // MES 没给原因串时自己兜一条,措辞走本服务唯一入口 SchedulingMaterialReasonText:
        // 这些串会进排程读面的「物料风险」说明,直出英文生码用户读不懂(MAN-698 台账 #35)。
        var reasonCodes = response.BlockingReasons.Count == 0
            ? response.Items
                .Where(x => x.ShortageQuantity > 0)
                .Select(x => SchedulingMaterialReasonText.FormatShortage(
                    x.MaterialId,
                    x.MaterialLotId,
                    x.ShortageQuantity))
                .ToArray()
            : response.BlockingReasons;
        // 结构化缺口:排程读面要能讲清「缺哪个物料、缺多少」,不能只给一串拼好的原因串。
        var shortages = response.Items
            .Where(x => x.ShortageQuantity > 0)
            .Select(x => new SchedulingMaterialShortageContract(
                x.MaterialId,
                string.IsNullOrWhiteSpace(x.MaterialLotId) ? null : x.MaterialLotId,
                x.RequiredQuantity,
                x.AvailableQuantity,
                x.ShortageQuantity,
                x.UomCode))
            .OrderBy(x => x.MaterialId, StringComparer.Ordinal)
            .ThenBy(x => x.MaterialLotId, StringComparer.Ordinal)
            .ToArray();
        var materialReadyUtc = response.Items
            .Where(x => x.ShortageQuantity > 0)
            .Max(x => x.ExpectedAvailableAtUtc);

        return
        [
            new SchedulingMaterialReadinessContract(
                ScopeType: "order",
                ScopeId: response.WorkOrderId,
                MaterialReadyUtc: materialReadyUtc,
                IsReady: false,
                ReasonCodes: reasonCodes,
                Shortages: shortages)
        ];
    }

    private static IReadOnlyCollection<SchedulingMaterialReadinessContract> SourceUnavailable(IEnumerable<string> workOrderIds) =>
        workOrderIds.Select(workOrderId => new SchedulingMaterialReadinessContract(
            ScopeType: "order",
            ScopeId: workOrderId,
            MaterialReadyUtc: null,
            IsReady: false,
            ReasonCodes: [SourceUnavailableReasonCode])).ToArray();

    private sealed record BatchMaterialReadinessRequest(
        string OrganizationId, string EnvironmentId, IReadOnlyCollection<string> WorkOrderIds);

    private sealed record MesMaterialReadinessBatchResponse(IReadOnlyCollection<MesMaterialReadinessResponse> Items);

    private sealed record MesMaterialReadinessResponse(
        string WorkOrderId,
        string ReadinessStatus,
        IReadOnlyCollection<string> BlockingReasons,
        IReadOnlyCollection<MesMaterialReadinessRow> Items);

    private sealed record MesMaterialReadinessRow(
        string MaterialId,
        string? MaterialLotId,
        string? UomCode,
        decimal RequiredQuantity,
        decimal AvailableQuantity,
        decimal RequestedQuantity,
        decimal StagedQuantity,
        decimal ReceivedQuantity,
        decimal ShortageQuantity,
        string Status,
        DateTimeOffset? ExpectedAvailableAtUtc = null);
}

public static class MaterialReadinessSchedulingAdapter
{
    public static SchedulingProblemContract Apply(
        SchedulingProblemContract problem,
        IReadOnlyCollection<SchedulingMaterialReadinessContract> materialReadiness)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(materialReadiness);
        if (materialReadiness.Count == 0)
        {
            return problem;
        }

        return problem with
        {
            MaterialReadiness = problem.MaterialReadiness
                .Concat(materialReadiness)
                .GroupBy(x => (
                    ScopeType: x.ScopeType.Trim().ToLowerInvariant(),
                    ScopeId: x.ScopeId.Trim(),
                    x.MaterialReadyUtc,
                    x.IsReady,
                    ReasonCodes: string.Join('|', x.ReasonCodes.Order(StringComparer.Ordinal))))
                .Select(x => x.First())
                .OrderBy(x => x.ScopeType, StringComparer.Ordinal)
                .ThenBy(x => x.ScopeId, StringComparer.Ordinal)
                .ThenBy(x => x.MaterialReadyUtc)
                .ThenBy(x => string.Join('|', x.ReasonCodes), StringComparer.Ordinal)
                .ToArray()
        };
    }
}
