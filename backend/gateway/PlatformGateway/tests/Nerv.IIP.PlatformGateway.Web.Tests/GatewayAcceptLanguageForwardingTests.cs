using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Nerv.IIP.Localization;

namespace Nerv.IIP.PlatformGateway.Web.Tests;

public sealed class GatewayAcceptLanguageForwardingTests
{
    [Theory]
    [InlineData(true, null, "en-US,zh-CN;q=0.8")]
    [InlineData(true, "fr", "fr")]
    [InlineData(false, null, "")]
    [InlineData(false, "fr", "fr")]
    public async Task Forwarding_handler_preserves_language_preferences(bool hasContext, string? outgoing, string expected)
    {
        var contextAccessor = new HttpContextAccessor
        {
            HttpContext = hasContext ? new DefaultHttpContext() : null
        };
        if (contextAccessor.HttpContext is { } context)
        {
            context.Request.Headers.AcceptLanguage = new StringValues(["en-US", "zh-CN;q=0.8"]);
        }
        var inner = new RecordingHandler();
        var handler = new AcceptLanguageForwardingHandler(contextAccessor)
        {
            InnerHandler = inner
        };
        using var client = new HttpClient(handler);

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://downstream.local/api");
        if (outgoing is not null)
        {
            request.Headers.AcceptLanguage.ParseAdd(outgoing);
        }
        using var response = await client.SendAsync(request);

        Assert.Equal(expected, inner.Request!.Headers.AcceptLanguage.ToString());
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
