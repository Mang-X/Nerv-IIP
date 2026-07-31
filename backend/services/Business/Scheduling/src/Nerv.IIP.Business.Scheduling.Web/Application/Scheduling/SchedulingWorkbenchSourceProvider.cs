using System.Net.Http.Headers;
using System.Text.Json;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

public sealed record SchedulingWorkbenchOrderSelection(string WorkOrderId, int Priority, bool IsRush);

public interface ISchedulingWorkbenchSourceProvider
{
    Task<IReadOnlyCollection<SchedulingProblemSourceOrder>> ResolveOrdersAsync(
        string organizationId,
        string environmentId,
        DateTimeOffset earliestStartFallbackUtc,
        IReadOnlyCollection<SchedulingWorkbenchOrderSelection> selections,
        CancellationToken cancellationToken);
}

public sealed class HttpSchedulingWorkbenchSourceProvider(
    HttpClient mesClient,
    ISchedulingProblemProductEngineeringClient productEngineeringClient,
    IInternalServiceTokenProvider? internalTokenProvider = null) : ISchedulingWorkbenchSourceProvider
{
    public async Task<IReadOnlyCollection<SchedulingProblemSourceOrder>> ResolveOrdersAsync(
        string organizationId,
        string environmentId,
        DateTimeOffset earliestStartFallbackUtc,
        IReadOnlyCollection<SchedulingWorkbenchOrderSelection> selections,
        CancellationToken cancellationToken)
    {
        var requested = selections
            .Select(x => x with { WorkOrderId = x.WorkOrderId.Trim() })
            .ToArray();
        if (requested.Length is < 1 or > SchedulingWorkbenchLimits.MaxOrderCount ||
            requested.Any(x => string.IsNullOrWhiteSpace(x.WorkOrderId)) ||
            requested.Select(x => x.WorkOrderId).Distinct(StringComparer.Ordinal).Count() != requested.Length)
        {
            throw new KnownException($"Scheduling workbench requires between 1 and {SchedulingWorkbenchLimits.MaxOrderCount} distinct work orders.");
        }

        // 只翻「可排状态」的工单页:终态工单(closed/completed/...)占了 MES 工单表的绝大多数,
        // 且按 DueUtc 升序排在最前,不过滤就得顺序翻过几千行历史关单才够到在制工单,
        // 单次生成方案会超过网关的下游超时线(#1400)。
        var firstPage = await ListWorkOrdersAsync(organizationId, environmentId, 0, SchedulableStatusesCsv, cancellationToken);
        var byId = firstPage.Items.ToDictionary(x => x.WorkOrderId, StringComparer.Ordinal);
        var requestedIds = requested.Select(x => x.WorkOrderId).ToHashSet(StringComparer.Ordinal);
        for (var skip = SchedulingWorkbenchLimits.MaxOrderCount;
             skip < firstPage.Total && !requestedIds.IsSubsetOf(byId.Keys);
             skip += SchedulingWorkbenchLimits.MaxOrderCount)
        {
            var page = await ListWorkOrdersAsync(organizationId, environmentId, skip, SchedulableStatusesCsv, cancellationToken);
            foreach (var item in page.Items)
            {
                byId.TryAdd(item.WorkOrderId, item);
            }
        }

        // 可排状态页里找不到的工单,再按 id 定向查一次:这样才能把「工单不存在」和
        // 「工单存在但已终态」区分开,保住原有的两种报错语义。
        var unresolved = requested
            .Where(x => !byId.ContainsKey(x.WorkOrderId))
            .Select(x => x.WorkOrderId)
            .ToArray();
        if (unresolved.Length > 0)
        {
            // 与同目录另两个 provider 一致地限流扇出:勾选 500 个终态工单是合法输入,
            // 串行回查会退化成 500 次往返。
            using var throttler = new SemaphoreSlim(MaxConcurrentMesLookups);
            var exactMatches = await Task.WhenAll(unresolved.Select(async workOrderId =>
            {
                await throttler.WaitAsync(cancellationToken);
                try
                {
                    return await FindWorkOrderByIdAsync(organizationId, environmentId, workOrderId, cancellationToken);
                }
                finally
                {
                    throttler.Release();
                }
            }));
            foreach (var exact in exactMatches)
            {
                if (exact is not null)
                {
                    byId.TryAdd(exact.WorkOrderId, exact);
                }
            }
        }

        var missing = requested.Where(x => !byId.ContainsKey(x.WorkOrderId)).Select(x => x.WorkOrderId).ToArray();
        if (missing.Length > 0)
        {
            throw new KnownException($"MES work orders were not found in the requested scope: {string.Join(", ", missing)}");
        }

        var productionVersionIds = requested
            .Select(x => byId[x.WorkOrderId].ProductionVersionId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Select(x => x!)
            .ToArray();
        var routingResults = await Task.WhenAll(productionVersionIds.Select(async productionVersionId => new
        {
            ProductionVersionId = productionVersionId,
            Routing = await productEngineeringClient.GetProductionVersionRoutingAsync(
                    organizationId,
                    environmentId,
                    productionVersionId,
                    cancellationToken)
        }));
        var routingsByVersion = routingResults.ToDictionary(
            x => x.ProductionVersionId,
            x => x.Routing,
            StringComparer.Ordinal);

        return requested.Select(selection =>
        {
            var order = byId[selection.WorkOrderId];
            if (TerminalStatuses.Contains(order.Status))
            {
                throw new KnownException($"MES work order '{order.WorkOrderId}' is terminal and cannot be scheduled.");
            }

            if (string.IsNullOrWhiteSpace(order.ProductionVersionId))
            {
                throw new KnownException($"MES work order '{order.WorkOrderId}' has no production version.");
            }

            var routing = routingsByVersion[order.ProductionVersionId];
            if (!string.Equals(order.SkuCode ?? order.SkuId, routing.SkuCode, StringComparison.OrdinalIgnoreCase))
            {
                throw new KnownException($"MES work order '{order.WorkOrderId}' does not match production version '{order.ProductionVersionId}'.");
            }

            return new SchedulingProblemSourceOrder(
                order.WorkOrderId,
                routing.SkuCode,
                order.Quantity,
                order.DueUtc,
                selection.Priority,
                selection.IsRush,
                order.OperationTasks.Count == 0
                    ? earliestStartFallbackUtc
                    : order.OperationTasks.Min(x => x.EarliestStartUtc),
                routing.RoutingVersionId,
                BusinessReference: order.WorkOrderNo);
        }).ToArray();
    }

    private async Task<MesWorkOrderItem?> FindWorkOrderByIdAsync(
        string organizationId,
        string environmentId,
        string workOrderId,
        CancellationToken cancellationToken)
    {
        var page = await ListWorkOrdersAsync(
            organizationId,
            environmentId,
            0,
            statuses: null,
            cancellationToken,
            workOrderId: workOrderId);
        return page.Items.FirstOrDefault(x => string.Equals(x.WorkOrderId, workOrderId, StringComparison.Ordinal));
    }

    private async Task<MesWorkOrderListResponse> ListWorkOrdersAsync(
        string organizationId,
        string environmentId,
        int skip,
        string? statuses,
        CancellationToken cancellationToken,
        string? workOrderId = null)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/business/v1/mes/work-orders?" + SchedulingProblemHttp.Query(
                ("organizationId", organizationId),
                ("environmentId", environmentId),
                ("statuses", statuses),
                ("workOrderId", workOrderId),
                ("skip", skip),
                ("take", SchedulingWorkbenchLimits.MaxOrderCount)));
        var bearerToken = internalTokenProvider?.BearerToken;
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        using var response = await mesClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadMesWorkOrderListResponseAsync(response.Content, cancellationToken);
    }

    private static async Task<MesWorkOrderListResponse> ReadMesWorkOrderListResponseAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new KnownException("MES returned an empty work-order response.");
            }

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var payload = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data)
                ? data
                : root;
            if (payload.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                throw new KnownException("MES returned an empty work-order response payload.");
            }

            return payload.Deserialize<MesWorkOrderListResponse>(SchedulingJson.Options)
                ?? throw new KnownException("MES returned an empty work-order response payload.");
        }
        catch (JsonException exception)
        {
            throw new KnownException($"MES returned an invalid work-order response: {exception.Message}");
        }
        catch (NotSupportedException exception)
        {
            throw new KnownException($"MES returned an unsupported work-order response: {exception.Message}");
        }
    }

    // Service-side authority. The Business Console mirrors these values only to improve pool UX.
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "completed", "closed", "cancelled", "canceled", "scrapped"
    };

    private const int MaxConcurrentMesLookups = 8;

    // TerminalStatuses 的补集,用来把候选翻页收敛到在制工单。两者必须同源:
    // 新增一个终态而这里不同步,会让该状态的工单被当成可排候选翻出来。
    private static readonly string SchedulableStatusesCsv = string.Join(
        ',',
        "created", "released", "started", "hold");

    private sealed record MesWorkOrderListResponse(IReadOnlyCollection<MesWorkOrderItem> Items, int Total);
    private sealed record MesWorkOrderItem(
        string WorkOrderId,
        string SkuId,
        string? ProductionVersionId,
        decimal Quantity,
        int Priority,
        DateTimeOffset DueUtc,
        string Status,
        IReadOnlyCollection<MesOperationTaskItem> OperationTasks,
        string? WorkOrderNo,
        string? SkuCode);
    private sealed record MesOperationTaskItem(DateTimeOffset EarliestStartUtc);
}
