using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Sdk.Core;
using Nerv.IIP.Sdk.Notification;

namespace Nerv.IIP.Sdk.Notification.Tests;

public sealed class NotificationClientTests
{
    [Fact]
    public async Task SubmitIntentAsyncSendsRequestWithPlatformHeadersAndBody()
    {
        HttpRequestMessage? captured = null;
        string? capturedBody = null;
        using var httpClient = CreateHttpClient(request =>
        {
            captured = request;
            capturedBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(new ResponseDataEnvelope<NotificationIntentResponse>(
                new NotificationIntentResponse("intent-001", Duplicate: false, Messages: [])));
        });
        var client = new HttpNotificationClient(httpClient);

        var request = new SubmitNotificationIntentRequest(
            "master-data",
            "sku.created",
            "evt-001",
            NotificationContractConstants.IntentTypeMessage,
            NotificationContractConstants.SeverityInfo,
            "sku:001",
            new NotificationResourceRef("sku", "sku-001", null),
            "SKU created",
            "A SKU was created.",
            ["user:admin"]);

        var result = await client.SubmitIntentAsync(request, CreateContext());

        Assert.Equal("intent-001", result.IntentId);
        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured.Method);
        Assert.Equal("/api/notifications/v1/intents", captured.RequestUri?.PathAndQuery);
        AssertPlatformHeaders(captured);

        var body = ReadJson<SubmitNotificationIntentRequest>(capturedBody);
        Assert.Equal("master-data", body.SourceService);
        Assert.Equal("sku.created", body.SourceEventType);
        Assert.Equal("SKU created", body.Title);
    }

    [Fact]
    public async Task ListMessagesAsyncSendsFiltersAndPlatformHeaders()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient(request =>
        {
            captured = request;
            return JsonResponse(new ResponseDataEnvelope<NotificationMessageListResponse>(
                new NotificationMessageListResponse([])));
        });
        var client = new HttpNotificationClient(httpClient);

        await client.ListMessagesAsync(CreateContext(), recipientRef: "user:admin", status: "unread");

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Get, captured.Method);
        Assert.Equal("/api/notifications/v1/messages?recipientRef=user%3Aadmin&status=unread", captured.RequestUri?.PathAndQuery);
        AssertPlatformHeaders(captured);
    }

    [Fact]
    public async Task MarkReadAsyncEscapesMessageIdAndReturnsResponse()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient(request =>
        {
            captured = request;
            return JsonResponse(new ResponseDataEnvelope<MarkNotificationMessageReadResponse>(
                new MarkNotificationMessageReadResponse("message/001", "read", DateTimeOffset.Parse("2026-05-22T00:00:00Z"))));
        });
        var client = new HttpNotificationClient(httpClient);

        var result = await client.MarkReadAsync("message/001", CreateContext());

        Assert.Equal("message/001", result.MessageId);
        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured.Method);
        Assert.Equal("/api/notifications/v1/messages/message%2F001/read", captured.RequestUri?.PathAndQuery);
        AssertPlatformHeaders(captured);
    }

    [Fact]
    public async Task SubmitIntentAsyncThrowsForErrorResponse()
    {
        using var httpClient = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(new { message = "invalid notification intent" })
        });
        var client = new HttpNotificationClient(httpClient);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.SubmitIntentAsync(
            new SubmitNotificationIntentRequest(
                "master-data",
                "sku.created",
                "evt-001",
                NotificationContractConstants.IntentTypeMessage,
                NotificationContractConstants.SeverityInfo,
                "sku:001",
                null,
                "SKU created",
                "A SKU was created.",
                ["user:admin"]),
            CreateContext()));
    }

    private static PlatformRequestContext CreateContext()
    {
        return new PlatformRequestContext("org-001", "prod", "corr-001", IdempotencyKey: "idem-001");
    }

    private static void AssertPlatformHeaders(HttpRequestMessage request)
    {
        Assert.Equal("org-001", SingleHeader(request, "X-Organization-Id"));
        Assert.Equal("prod", SingleHeader(request, "X-Environment-Id"));
        Assert.Equal("corr-001", SingleHeader(request, "X-Correlation-Id"));
        Assert.Equal("idem-001", SingleHeader(request, "X-Idempotency-Key"));
    }

    private static string SingleHeader(HttpRequestMessage request, string name)
    {
        return Assert.Single(request.Headers.GetValues(name));
    }

    private static T ReadJson<T>(string? json)
    {
        Assert.False(string.IsNullOrWhiteSpace(json));
        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Request body was empty.");
    }

    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        return new HttpClient(new StubHttpMessageHandler(respond))
        {
            BaseAddress = new Uri("https://notifications.example.test")
        };
    }

    private static HttpResponseMessage JsonResponse<T>(T value)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(value)
        };
    }

    private sealed record ResponseDataEnvelope<T>(T Data, bool Success = true, string Message = "OK", int Code = 200);

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(respond(request));
        }
    }
}
