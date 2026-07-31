using System.Net;
using System.Text;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

/// <summary>
/// #1324: the console receipt for 领料/收料 used to be produced by deserializing the MES body straight
/// into the console contract, which left `accepted` false and dropped the allocated 领料单号.
/// </summary>
public sealed class BusinessMesMaterialIssueClientTests
{
    [Fact]
    public async Task Create_material_issue_request_returns_an_accepted_receipt_carrying_the_request_number()
    {
        var client = ClientReturning(
            """{"data":{"status":"Accepted","referenceId":"MIR-000123","acceptedAtUtc":"2026-07-31T08:00:00Z"}}""");
        var request = new BusinessConsoleMesCreateMaterialIssueRequest(
            "WO-001", "org", "env", "OP-10", "MAT-OIL", "L", 7m, null, "idem-1");

        var response = await client.CreateMaterialIssueRequestAsync("token", "WO-001", request, CancellationToken.None);

        Assert.True(response.Accepted);
        Assert.Equal("business-mes", response.DownstreamService);
        Assert.Equal("mes-material-issue-request", response.DownstreamDocumentType);
        Assert.Equal("MIR-000123", response.DownstreamDocumentId);
    }

    [Fact]
    public async Task Confirm_line_side_receipt_returns_an_accepted_receipt_carrying_the_request_number()
    {
        var client = ClientReturning(
            """{"data":{"status":"Accepted","referenceId":"MIR-000123","acceptedAtUtc":"2026-07-31T08:05:00Z"}}""");
        var request = new BusinessConsoleMesConfirmLineSideReceiptRequest(
            "MIR-000123", "org", "env", "LOT-A", 7m, null, "idem-2");

        var response = await client.ConfirmLineSideMaterialReceiptAsync("token", "MIR-000123", request, CancellationToken.None);

        Assert.True(response.Accepted);
        Assert.Equal("MIR-000123", response.DownstreamDocumentId);
    }

    private static HttpBusinessMesClient ClientReturning(string json) =>
        new(new HttpClient(new StubHandler(json)) { BaseAddress = new Uri("http://mes") });

    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
    }
}
