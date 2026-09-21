using System.Net;
using System.Text;
using System.Text.Json;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessPlanningClientTests
{
    [Fact]
    public async Task List_demands_forwards_keyword_skip_and_take_to_downstream_query()
    {
        var handler = new StubHandler("""{"data":[]}""");
        var client = new HttpBusinessPlanningClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://demand-planning.local"),
        });
        var request = new BusinessConsoleDemandSourceListRequest(
            "org-001",
            "env-dev",
            " pump/line ",
            7,
            23);

        await client.ListDemandSourcesAsync("internal-token", request, CancellationToken.None);

        Assert.Equal(
            "?organizationId=org-001&environmentId=env-dev&keyword=%20pump%2Fline%20&skip=7&take=23",
            handler.RequestUri!.Query);
    }

    [Fact]
    public async Task Create_forecast_returns_the_reference_allocated_by_the_downstream_service()
    {
        var client = new HttpBusinessPlanningClient(new HttpClient(new StubHandler(
            """{"data":{"forecastInputId":"forecast-1","forecastReference":"FC20260823000001"}}"""))
        {
            BaseAddress = new Uri("http://demand-planning.local"),
        });
        var request = new BusinessConsoleCreateOrUpdateForecastInputRequest(
            "org-001",
            "env-dev",
            null,
            "SKU-001",
            "pcs",
            "SITE-01",
            new DateOnly(2026, 8, 23),
            new DateOnly(2026, 9, 22),
            120m,
            IdempotencyKey: "forecast-create-1");

        var response = await client.CreateOrUpdateForecastInputAsync(
            "internal-token",
            request,
            CancellationToken.None);

        Assert.Equal("FC20260823000001", response.ForecastReference);
    }

    [Fact]
    public async Task Mps_review_and_release_forward_the_gateway_supplied_actor_to_demand_planning()
    {
        var reviewHandler = new StubHandler(MpsResponse("Reviewed", reviewedBy: "user-admin"));
        var releaseHandler = new StubHandler(MpsResponse("Released", releasedBy: "user-admin"));
        var reviewClient = PlanningClient(reviewHandler);
        var releaseClient = PlanningClient(releaseHandler);
        var reviewRequest = new BusinessConsoleReviewMpsBucketRequest("mps-001", "org-001", "env-dev", "forged-reviewer");
        var releaseRequest = new BusinessConsoleReleaseMpsBucketRequest("mps-001", "org-001", "env-dev", "forged-releaser");

        await reviewClient.ReviewMpsBucketAsync(
            "internal-token",
            "mps-001",
            "user-admin",
            reviewRequest,
            CancellationToken.None);
        await releaseClient.ReleaseMpsBucketAsync(
            "internal-token",
            "mps-001",
            "user-admin",
            releaseRequest,
            CancellationToken.None);

        using var reviewBody = JsonDocument.Parse(reviewHandler.RequestBody!);
        using var releaseBody = JsonDocument.Parse(releaseHandler.RequestBody!);
        Assert.Equal("user-admin", reviewBody.RootElement.GetProperty("reviewedBy").GetString());
        Assert.Equal("user-admin", releaseBody.RootElement.GetProperty("releasedBy").GetString());
    }

    private static HttpBusinessPlanningClient PlanningClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://demand-planning.local"),
        });

    private static string MpsResponse(string status, string? reviewedBy = null, string? releasedBy = null) =>
        JsonSerializer.Serialize(new
        {
            data = new
            {
                mpsId = "mps-001",
                skuCode = "SKU-001",
                uomCode = "pcs",
                siteCode = "SITE-01",
                bucketDate = "2026-06-15",
                quantity = 120m,
                status,
                reviewedBy,
                reviewedAtUtc = reviewedBy is null ? null : "2026-06-01T08:00:00Z",
                releasedBy,
                releasedAtUtc = releasedBy is null ? null : "2026-06-01T09:00:00Z",
            },
        });

    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }
}
