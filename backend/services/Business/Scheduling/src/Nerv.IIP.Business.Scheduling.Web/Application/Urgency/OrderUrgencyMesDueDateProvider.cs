using System.Net.Http.Headers;
using System.Text.Json;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Urgency;

public interface IOrderUrgencyMesDueDateProvider
{
    Task<IReadOnlyDictionary<string, DateTimeOffset>> ResolveAsync(
        string organizationId,
        string environmentId,
        IReadOnlyCollection<string> workOrderIds,
        CancellationToken cancellationToken);
}

public sealed class HttpOrderUrgencyMesDueDateProvider(
    HttpClient mesClient,
    IInternalServiceTokenProvider? internalTokenProvider = null) : IOrderUrgencyMesDueDateProvider
{
    public async Task<IReadOnlyDictionary<string, DateTimeOffset>> ResolveAsync(
        string organizationId,
        string environmentId,
        IReadOnlyCollection<string> workOrderIds,
        CancellationToken cancellationToken)
    {
        using var throttler = new SemaphoreSlim(8);
        var matches = await Task.WhenAll(workOrderIds.Select(async workOrderId =>
        {
            await throttler.WaitAsync(cancellationToken);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get,
                    "/api/business/v1/mes/work-orders?" + SchedulingProblemHttp.Query(
                        ("organizationId", organizationId),
                        ("environmentId", environmentId),
                        ("workOrderId", workOrderId),
                        ("skip", 0),
                        ("take", 1)));
                var bearerToken = internalTokenProvider?.BearerToken;
                if (!string.IsNullOrWhiteSpace(bearerToken))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
                }
                using var response = await mesClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                var payload = document.RootElement.GetProperty("data");
                foreach (var item in payload.GetProperty("items").EnumerateArray())
                {
                    if (string.Equals(item.GetProperty("workOrderId").GetString(), workOrderId, StringComparison.Ordinal))
                    {
                        return (WorkOrderId: workOrderId, DueUtc: (DateTimeOffset?)item.GetProperty("dueUtc").GetDateTimeOffset());
                    }
                }
                return (WorkOrderId: workOrderId, DueUtc: (DateTimeOffset?)null);
            }
            finally
            {
                throttler.Release();
            }
        }));
        return matches.Where(x => x.DueUtc.HasValue)
            .ToDictionary(x => x.WorkOrderId, x => x.DueUtc!.Value, StringComparer.Ordinal);
    }
}
