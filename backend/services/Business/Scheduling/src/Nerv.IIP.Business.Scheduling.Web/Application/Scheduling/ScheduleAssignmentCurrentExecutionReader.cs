using System.Net.Http.Headers;
using System.Text.Json;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

/// <summary>仅装饰 HTTP 读响应；实时值不进入方案持久化、算法输入或发布判定。</summary>
public sealed class ScheduleAssignmentCurrentExecutionReader(
    IHttpClientFactory clients,
    HttpSchedulingMaterialReadinessProvider materials,
    IInternalServiceTokenProvider? tokens,
    TimeProvider time,
    ILogger<ScheduleAssignmentCurrentExecutionReader> logger)
{
    public async Task<SchedulePlanContract> ReadAsync(
        SchedulePlanContract plan, string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        if (plan.Assignments.Count == 0) return plan;
        var observedAt = time.GetUtcNow();
        var contextQuery = $"organizationId={Uri.EscapeDataString(organizationId)}&environmentId={Uri.EscapeDataString(environmentId)}";
        var orderIds = plan.Assignments.Select(x => x.OrderId).Distinct(StringComparer.Ordinal).ToArray();
        var resourceIds = plan.Assignments.Select(x => x.ResourceId).Distinct(StringComparer.Ordinal).ToArray();
        var readinessTask = materials.QueryOrdersAsync(organizationId, environmentId, orderIds, cancellationToken);
        var progressTask = ReadDistinctAsync(orderIds, id => ReadSourceAsync(
            HttpSchedulingMaterialReadinessProvider.MesClientName,
            $"/api/business/v1/mes/work-orders/{Uri.EscapeDataString(id)}?{contextQuery}",
            value => value.TryGetProperty("completedQuantity", out var completed) && completed.TryGetDecimal(out var completedQuantity) &&
                value.TryGetProperty("quantity", out var planned) && planned.TryGetDecimal(out var plannedQuantity)
                ? new ScheduleWorkOrderProgressContract(completedQuantity, plannedQuantity) : null,
            cancellationToken));
        var equipmentTask = ReadDistinctAsync(resourceIds, id => ReadSourceAsync(
            HttpSchedulingEquipmentAvailabilityProvider.IndustrialTelemetryClientName,
            $"/api/business/v1/iiot/devices/{Uri.EscapeDataString(id)}/current-state?{contextQuery}&asOfUtc={Uri.EscapeDataString(observedAt.ToString("O"))}",
            value => value.Deserialize<EquipmentRuntimeCurrentStateResponse>(EquipmentRuntimeJson.Options), cancellationToken));
        await Task.WhenAll(readinessTask, progressTask, equipmentTask);
        var readiness = (await readinessTask).ToDictionary(x => x.ScopeId, StringComparer.Ordinal);
        var progress = await progressTask;
        var equipment = await equipmentTask;
        return plan with
        {
            Assignments = plan.Assignments.Select(assignment =>
            {
                readiness.TryGetValue(assignment.OrderId, out var material);
                var state = equipment[assignment.ResourceId];
                return assignment with
                {
                    CurrentExecution = new ScheduleAssignmentCurrentExecutionContract(
                        observedAt, progress[assignment.OrderId], material?.MaterialReadyUtc,
                        material?.ReasonCodes.Contains(HttpSchedulingMaterialReadinessProvider.SourceUnavailableReasonCode) == true
                            ? null : material?.IsReady ?? true,
                        state?.CurrentState, state?.StateOccurredAtUtc, state?.IsSourceFresh)
                };
            }).ToArray()
        };
    }

    // 历史方案可能包含大量工单；去重并限制同时进行的权威 API 调用，不制造新的批量来源。
    private static async Task<Dictionary<string, T>> ReadDistinctAsync<T>(string[] ids, Func<string, Task<T>> read)
    {
        var values = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var batch in ids.Chunk(8))
        {
            var results = await Task.WhenAll(batch.Select(async id => (Id: id, Value: await read(id))));
            foreach (var result in results) values.Add(result.Id, result.Value);
        }
        return values;
    }

    private async Task<T?> ReadSourceAsync<T>(string clientName, string path, Func<JsonElement, T?> project, CancellationToken cancellationToken)
        where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (!string.IsNullOrWhiteSpace(tokens?.BearerToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.BearerToken);
        try
        {
            using var response = await clients.CreateClient(clientName).SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False) return null;
            var payload = root.TryGetProperty("data", out var data) ? data : root;
            return payload.ValueKind == JsonValueKind.Object ? project(payload) : null;
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Current assignment source {ClientName} was unavailable.", clientName);
            return null;
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Current assignment source {ClientName} timed out.", clientName);
            return null;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Current assignment source {ClientName} returned invalid JSON.", clientName);
            return null;
        }
    }
}
