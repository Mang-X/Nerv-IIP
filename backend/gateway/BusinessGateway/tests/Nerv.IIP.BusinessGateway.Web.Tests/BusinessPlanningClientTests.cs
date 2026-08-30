using System.Net;
using System.Text;
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

    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
