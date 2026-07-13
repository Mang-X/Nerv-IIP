using System.Net;
using System.Text;
using System.Text.Json;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessMesQualityHoldClientTests
{
    [Fact]
    public async Task Force_release_sends_governed_headers_and_omits_actor_from_body()
    {
        HttpRequestMessage? captured = null;
        var handler = new RecordingHandler(async request =>
        {
            captured = await CloneAsync(request);
            return JsonResponse("""{"data":{"status":"Accepted","referenceId":"DOC","acceptedAtUtc":"2026-07-13T08:00:00Z"}}""");
        });
        var client = new HttpBusinessMesClient(new HttpClient(handler) { BaseAddress = new Uri("http://mes") });
        var request = new BusinessConsoleMesForceReleaseQualityHoldRequest("DOC", "org", "env", "reason", "source", null, "idem-client");

        await client.ForceReleaseQualityHoldAsync("token", "DOC", request, "user:qa", "corr-governed", CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("user:qa", captured!.Headers.GetValues("X-Authenticated-Actor").Single());
        Assert.Equal("corr-governed", captured.Headers.GetValues("X-Correlation-Id").Single());
        Assert.Equal("idem-client", captured.Headers.GetValues("X-Idempotency-Key").Single());
        var body = JsonDocument.Parse(await captured.Content!.ReadAsStringAsync()).RootElement;
        Assert.False(body.TryGetProperty("actor", out _));
    }

    [Fact]
    public async Task Timeline_get_forwards_full_scope_and_deserializes_lineage()
    {
        HttpRequestMessage? captured = null;
        var handler = new RecordingHandler(async request =>
        {
            captured = await CloneAsync(request);
            return JsonResponse("""{"data":{"items":[{"transitionId":"019f0000-0000-7000-8000-000000000001","sourceService":"source","sourceDocumentId":"DOC / 1","holdCycleId":"cycle","correlationId":"corr","eventKind":"hold-applied","actor":"quality","occurredAtUtc":"2026-07-13T08:00:00Z","reason":"defect","sourceInspectionRecordId":"QI","sourceInspectionDocumentId":"PLAN","origin":"automatic","idempotencyKey":"event-key"}]}}""");
        });
        var client = new HttpBusinessMesClient(new HttpClient(handler) { BaseAddress = new Uri("http://mes") });

        var response = await client.GetQualityHoldTimelineAsync("token", "DOC / 1", new("DOC / 1", "org", "env", "source"), CancellationToken.None);

        Assert.Equal("/api/business/v1/mes/quality-holds/DOC%20%2F%201/timeline?organizationId=org&environmentId=env&sourceService=source", captured!.RequestUri!.PathAndQuery);
        var item = Assert.Single(response.Items);
        Assert.Equal("event-key", item.IdempotencyKey);
        Assert.Equal("PLAN", item.SourceInspectionDocumentId);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request) { var clone = new HttpRequestMessage(request.Method, request.RequestUri); foreach (var h in request.Headers) clone.Headers.TryAddWithoutValidation(h.Key, h.Value); if (request.Content is not null) clone.Content = new StringContent(await request.Content.ReadAsStringAsync(), Encoding.UTF8, "application/json"); return clone; }
    private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => response(request); }
}
