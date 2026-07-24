using System.Net.Http.Headers;
using System.Net.Http.Json;
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

        var firstPage = await ListWorkOrdersAsync(organizationId, environmentId, 0, cancellationToken);
        var byId = firstPage.Items.ToDictionary(x => x.WorkOrderId, StringComparer.Ordinal);
        var requestedIds = requested.Select(x => x.WorkOrderId).ToHashSet(StringComparer.Ordinal);
        for (var skip = SchedulingWorkbenchLimits.MaxOrderCount;
             skip < firstPage.Total && !requestedIds.IsSubsetOf(byId.Keys);
             skip += SchedulingWorkbenchLimits.MaxOrderCount)
        {
            var page = await ListWorkOrdersAsync(organizationId, environmentId, skip, cancellationToken);
            foreach (var item in page.Items)
            {
                byId.TryAdd(item.WorkOrderId, item);
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

    private async Task<MesWorkOrderListResponse> ListWorkOrdersAsync(
        string organizationId,
        string environmentId,
        int skip,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/business/v1/mes/work-orders?" + SchedulingProblemHttp.Query(
                ("organizationId", organizationId),
                ("environmentId", environmentId),
                ("skip", skip),
                ("take", SchedulingWorkbenchLimits.MaxOrderCount)));
        var bearerToken = internalTokenProvider?.BearerToken;
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        using var response = await mesClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<MesWorkOrderListResponse>>(
            SchedulingJson.Options,
            cancellationToken);
        return envelope?.Data ?? throw new InvalidOperationException("MES returned an empty work-order response envelope.");
    }

    // Service-side authority. The Business Console mirrors these values only to improve pool UX.
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "completed", "closed", "cancelled", "canceled", "scrapped"
    };

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
