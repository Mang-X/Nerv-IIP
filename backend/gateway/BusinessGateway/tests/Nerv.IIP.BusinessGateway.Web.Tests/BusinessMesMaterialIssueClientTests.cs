using System.Net;
using System.Text;
using System.Text.Json;
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
        // #1341: downstreamService / downstreamDocumentType 与全仓 PascalCase 词表对齐
        // （BusinessMes / WorkOrder / MaterialIssueRequest…），前端按精确等值判断。
        Assert.Equal("BusinessMes", response.DownstreamService);
        Assert.Equal("MaterialIssueRequest", response.DownstreamDocumentType);
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

    [Fact]
    public async Task Create_material_issue_request_forwards_supplementary_semantics_to_mes()
    {
        var handler = new RecordingHandler(
            """{"data":{"status":"Accepted","referenceId":"MIR-SUP-001","acceptedAtUtc":"2026-08-25T08:00:00Z"}}""");
        var client = new HttpBusinessMesClient(new HttpClient(handler) { BaseAddress = new Uri("http://mes") });
        var request = new BusinessConsoleMesCreateMaterialIssueRequest(
            "WO-001", "org", "env", "OP-10", "MAT-OIL", "L", 7m, null, "idem-sup", true, "MIR-000123");

        await client.CreateMaterialIssueRequestAsync("token", "WO-001", request, CancellationToken.None);

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        var root = body.RootElement;
        Assert.True(root.GetProperty("isSupplementary").GetBoolean());
        Assert.Equal("MIR-000123", root.GetProperty("originalMaterialIssueRequestNo").GetString());
    }

    [Fact]
    public async Task List_material_issue_requests_maps_supplementary_count_and_row_fields()
    {
        var client = ClientReturning(
            """{"data":{"items":[{"requestId":"MIR-SUP-001","workOrderId":"WO-001","operationTaskId":"OP-10","materialId":"MAT-OIL","uomCode":"L","materialLotId":null,"requestedQuantity":7,"receivedQuantity":0,"consumedQuantity":0,"status":"Requested","requestedAtUtc":"2026-08-25T08:00:00Z","isSupplementary":true,"originalMaterialIssueRequestNo":"MIR-000123"}],"total":2,"supplementaryCount":1}}""");

        var response = await client.ListMaterialIssueRequestsAsync(
            "token",
            new BusinessConsoleMesListRequest("org", "env"),
            CancellationToken.None);

        Assert.Equal(1, response.SupplementaryCount);
        var row = Assert.Single(response.Items);
        Assert.True(row.IsSupplementary);
        Assert.Equal("MIR-000123", row.OriginalMaterialIssueRequestNo);
    }

    [Fact]
    public async Task Return_line_side_material_returns_an_accepted_receipt_carrying_the_request_number()
    {
        var client = ClientReturning(
            """{"data":{"status":"Accepted","referenceId":"MIR-000123","acceptedAtUtc":"2026-07-31T08:10:00Z"}}""");
        var request = new BusinessConsoleMesReturnLineSideMaterialRequest(
            "MIR-000123", "org", "env", null, 2m);

        var response = await client.ReturnLineSideMaterialAsync("token", "MIR-000123", request, CancellationToken.None);

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

    private sealed class RecordingHandler(string json) : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }
}
