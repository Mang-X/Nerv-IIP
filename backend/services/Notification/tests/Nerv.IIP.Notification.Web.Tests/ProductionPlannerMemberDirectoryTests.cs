using System.Net;
using System.Net.Http.Json;
using Nerv.IIP.Notification.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Notification.Web.Tests;

public sealed class ProductionPlannerMemberDirectoryTests
{
    [Fact]
    public async Task Reads_all_IAM_planner_pages_for_the_requested_scope_with_internal_authentication()
    {
        var requests = new List<(string Uri, string? Bearer)>();
        using var client = new HttpClient(new PlannerPagesHandler(requests)) { BaseAddress = new Uri("http://iam.test") };
        var directory = new HttpProductionPlannerMemberDirectory(client, new FixedTokenProvider());

        var members = await directory.ListMemberIdsAsync("org-001", "env-dev", CancellationToken.None);

        Assert.Equal(101, members.Count);
        Assert.Equal("planner-001", members[0]);
        Assert.Equal("planner-101", members[100]);
        Assert.Equal(2, requests.Count);
        Assert.All(requests, request =>
        {
            Assert.Contains("organizationId=org-001&environmentId=env-dev", request.Uri, StringComparison.Ordinal);
            Assert.Equal("test-internal-token", request.Bearer);
        });
        Assert.Contains("pageIndex=1&pageSize=100", requests[0].Uri, StringComparison.Ordinal);
        Assert.Contains("pageIndex=2&pageSize=100", requests[1].Uri, StringComparison.Ordinal);
    }

    private sealed class PlannerPagesHandler(List<(string Uri, string? Bearer)> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requests.Add((request.RequestUri!.ToString(), request.Headers.Authorization?.Parameter));
            var pageIndex = requests.Count;
            var ids = Enumerable.Range(pageIndex == 1 ? 1 : 101, pageIndex == 1 ? 100 : 1)
                .Select(index => $"planner-{index:000}")
                .ToArray();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    success = true,
                    data = new { pageIndex, pageSize = 100, totalCount = 101, items = ids },
                }),
            });
        }
    }

    private sealed record FixedTokenProvider : IInternalServiceTokenProvider
    {
        public string BearerToken => "test-internal-token";
    }
}
