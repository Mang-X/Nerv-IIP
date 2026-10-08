using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Nerv.IIP.Contracts.Maintenance;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

public interface ISchedulingDowntimeFactsProvider
{
    Task<IReadOnlyCollection<SchedulingDowntimeFactContract>> QueryAsync(SchedulingProblemContract problem,
        DateTimeOffset asOf, CancellationToken ct);
}

/// <summary>Reads source-owned facts over HTTP; MES list filters starts, so query history before testing interval overlap.</summary>
public sealed class HttpSchedulingDowntimeFactsProvider(IHttpClientFactory clients, IInternalServiceTokenProvider tokens)
    : ISchedulingDowntimeFactsProvider
{
    public async Task<IReadOnlyCollection<SchedulingDowntimeFactContract>> QueryAsync(SchedulingProblemContract problem,
        DateTimeOffset asOf, CancellationToken ct)
    {
        var mes = ReadMesAsync(problem, asOf, ct);
        var maintenance = ReadMaintenanceAsync(problem, asOf, ct);
        await Task.WhenAll(mes, maintenance);
        return mes.Result.Concat(maintenance.Result).ToArray();
    }

    private async Task<IReadOnlyCollection<SchedulingDowntimeFactContract>> ReadMesAsync(SchedulingProblemContract problem,
        DateTimeOffset asOf, CancellationToken ct)
    {
        var items = new List<SchedulingDowntimeFactContract>();
        var end = problem.HorizonEndUtc > asOf ? problem.HorizonEndUtc : asOf.AddTicks(1);
        for (var skip = 0; ; skip += 100)
        {
            var query = SchedulingProblemHttp.Query(("organizationId", problem.OrganizationId), ("environmentId", problem.EnvironmentId),
                ("windowStartUtc", DateTimeOffset.MinValue), ("windowEndUtc", end), ("skip", skip), ("take", 100));
            using var response = await GetAsync(HttpSchedulingMaterialReadinessProvider.MesClientName,
                "/api/business/v1/mes/downtime-events?" + query, ct);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var page = document.RootElement;
            foreach (var item in page.GetProperty("items").EnumerateArray())
            {
                var device = item.GetProperty("deviceAssetId").GetString();
                var center = item.GetProperty("workCenterId").GetString();
                if (!problem.Resources.Any(x => device is not null ? x.ResourceId == device : x.WorkCenterId == center)) continue;
                items.Add(new(MesIntegrationEventSources.BusinessMes, item.GetProperty("downtimeEventId").GetString()!, device, center,
                    item.GetProperty("startedAtUtc").GetDateTimeOffset(),
                    item.GetProperty("recoveredAtUtc").ValueKind == JsonValueKind.Null ? null : item.GetProperty("recoveredAtUtc").GetDateTimeOffset(), null));
            }
            if (skip + 100 >= page.GetProperty("total").GetInt32()) break;
        }
        return items;
    }

    private async Task<IReadOnlyCollection<SchedulingDowntimeFactContract>> ReadMaintenanceAsync(SchedulingProblemContract problem,
        DateTimeOffset asOf, CancellationToken ct)
    {
        var items = new List<SchedulingDowntimeFactContract>();
        var end = problem.HorizonEndUtc > asOf ? problem.HorizonEndUtc : asOf.AddTicks(1);
        foreach (var ids in problem.Resources.Select(x => x.ResourceId).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).Chunk(HttpSchedulingEquipmentAvailabilityProvider.MaxAvailabilityQueryIdsPerBatch))
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/business/internal/v1/maintenance/downtime-facts/query")
            {
                Content = JsonContent.Create(new MaintenanceDowntimeFactsRequest(problem.OrganizationId, problem.EnvironmentId,
                    problem.HorizonStartUtc < asOf ? problem.HorizonStartUtc : asOf, end, ids), options: SchedulingJson.Options)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.BearerToken);
            using var response = await clients.CreateClient(HttpSchedulingEquipmentAvailabilityProvider.MaintenanceClientName).SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var data = await response.Content.ReadFromJsonAsync<ResponseData<MaintenanceDowntimeFactsResponse>>(SchedulingJson.Options, ct)
                ?? throw new JsonException("Maintenance downtime response is missing.");
            items.AddRange(data.Data.Items.Select(x => new SchedulingDowntimeFactContract(MaintenanceIntegrationEventSources.BusinessMaintenance, x.WorkOrderId,
                x.DeviceAssetId, null, x.UnavailableFromUtc, x.ReleasedAtUtc, x.ExpectedRestoreAtUtc,
                x.Source, x.SourceType, x.SourceReferenceId, x.PredictedRestoreAtUtc, x.RestorePredictionSource, x.RestorePredictionSourceVersion)));
        }
        return items;
    }

    private async Task<HttpResponseMessage> GetAsync(string clientName, string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.BearerToken);
        var response = await clients.CreateClient(clientName).SendAsync(request, ct);
        try { response.EnsureSuccessStatusCode(); return response; }
        catch { response.Dispose(); throw; }
    }
}
