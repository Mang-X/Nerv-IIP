using System.Net.Http.Headers;
using System.Text.Json;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

public sealed record MaterialDeliveryExecutionOperation(
    string OperationId, int Sequence, string Status, DateTimeOffset EarliestStartUtc,
    DateTimeOffset? StartedAtUtc, decimal NetGoodQuantity);

public sealed record MaterialDeliveryExecutionOrder(
    string WorkOrderId, string? SuggestionId, string? ProductionVersionId, decimal Quantity,
    IReadOnlyCollection<MaterialDeliveryExecutionOperation> Operations);

public interface IMaterialDeliveryMesSourceProvider
{
    Task<MaterialDeliveryExecutionOrder?> GetAsync(string organizationId, string environmentId,
        string workOrderId, CancellationToken cancellationToken);
}

public sealed class HttpMaterialDeliveryMesSourceProvider(
    HttpClient mesClient, IInternalServiceTokenProvider internalTokenProvider) : IMaterialDeliveryMesSourceProvider
{
    public async Task<MaterialDeliveryExecutionOrder?> GetAsync(string organizationId, string environmentId,
        string workOrderId, CancellationToken cancellationToken)
    {
        var scope = SchedulingProblemHttp.Query(("organizationId", organizationId), ("environmentId", environmentId));
        using var list = await GetAsync("/api/business/v1/mes/work-orders?" + scope + "&" +
            SchedulingProblemHttp.Query(("workOrderId", workOrderId), ("skip", 0), ("take", 1)), cancellationToken);
        var order = list.RootElement.GetProperty("data").GetProperty("items").EnumerateArray()
            .SingleOrDefault(x => x.GetProperty("workOrderId").GetString() == workOrderId);
        if (order.ValueKind == JsonValueKind.Undefined) return null;

        using var detail = await GetAsync($"/api/business/v1/mes/work-orders/{Uri.EscapeDataString(workOrderId)}?" + scope,
            cancellationToken);
        var data = detail.RootElement.GetProperty("data");
        var reference = data.GetProperty("sourcePlanReference");
        var suggestionId = reference.ValueKind == JsonValueKind.Object &&
            reference.GetProperty("sourceSystem").GetString() == "DemandPlanning" &&
            reference.GetProperty("sourceDocumentType").GetString() == "PlanningSuggestion"
            ? reference.GetProperty("sourceDocumentId").GetString() : null;
        var goodByOperation = new Dictionary<string, decimal>(StringComparer.Ordinal);
        for (var skip = 0; ; skip += 100)
        {
            using var reports = await GetAsync("/api/business/v1/mes/production-reports?" + scope + "&" +
                SchedulingProblemHttp.Query(("workOrderId", workOrderId), ("skip", skip), ("take", 100)), cancellationToken);
            var page = reports.RootElement.GetProperty("data");
            foreach (var report in page.GetProperty("items").EnumerateArray())
            {
                var id = report.GetProperty("operationTaskId").GetString()!;
                // 冲销报告公开负数量；净 GoodQuantity 与 MES/Scheduling 既有执行口径一致。
                goodByOperation[id] = goodByOperation.GetValueOrDefault(id) + report.GetProperty("goodQuantity").GetDecimal();
            }
            if (skip + 100 >= page.GetProperty("total").GetInt32()) break;
        }
        var startedById = data.GetProperty("operationTasks").EnumerateArray().ToDictionary(
            x => x.GetProperty("operationTaskId").GetString()!,
            x => x.GetProperty("startedAtUtc").ValueKind == JsonValueKind.Null
                ? (DateTimeOffset?)null : x.GetProperty("startedAtUtc").GetDateTimeOffset(), StringComparer.Ordinal);
        return new MaterialDeliveryExecutionOrder(workOrderId, suggestionId,
            order.GetProperty("productionVersionId").GetString(), order.GetProperty("quantity").GetDecimal(),
            order.GetProperty("operationTasks").EnumerateArray().Select(x =>
            {
                var id = x.GetProperty("operationTaskId").GetString()!;
                return new MaterialDeliveryExecutionOperation(id, x.GetProperty("operationSequence").GetInt32(),
                    x.GetProperty("status").GetString()!, x.GetProperty("earliestStartUtc").GetDateTimeOffset(),
                    startedById[id], goodByOperation.GetValueOrDefault(id));
            }).ToArray());
    }

    private async Task<JsonDocument> GetAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalTokenProvider.BearerToken);
        using var response = await mesClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }
}
