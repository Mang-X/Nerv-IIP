using System.Net.Http.Headers;
using System.Net.Http.Json;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Notification.Web.Application.IntegrationEventHandlers;

public interface IProductionPlannerMemberDirectory
{
    Task<IReadOnlyList<string>> ListMemberIdsAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken);
}

public sealed class HttpProductionPlannerMemberDirectory(
    HttpClient httpClient,
    IInternalServiceTokenProvider tokenProvider) : IProductionPlannerMemberDirectory
{
    private const int PageSize = 100;

    public async Task<IReadOnlyList<string>> ListMemberIdsAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var ids = new List<string>();
        for (var pageIndex = 1; ; pageIndex++)
        {
            var uri = "/internal/iam/v1/production-planner-members"
                + "?organizationId=" + Uri.EscapeDataString(organizationId)
                + "&environmentId=" + Uri.EscapeDataString(environmentId)
                + "&pageIndex=" + pageIndex
                + "&pageSize=" + PageSize;
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenProvider.BearerToken);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<PlannerMemberPage>>(cancellationToken);
            var page = envelope?.Data ?? throw new InvalidOperationException("IAM planner member response has no data.");
            ids.AddRange(page.Items);
            if (ids.Count >= page.TotalCount)
            {
                return ids;
            }
        }
    }

    private sealed record ResponseDataEnvelope<T>(T? Data);

    private sealed record PlannerMemberPage(int TotalCount, IReadOnlyList<string> Items);
}
