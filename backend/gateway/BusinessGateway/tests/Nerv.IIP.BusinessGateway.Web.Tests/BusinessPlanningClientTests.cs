using System.Net;
using System.Text;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessPlanningClientTests
{
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
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
    }
}
