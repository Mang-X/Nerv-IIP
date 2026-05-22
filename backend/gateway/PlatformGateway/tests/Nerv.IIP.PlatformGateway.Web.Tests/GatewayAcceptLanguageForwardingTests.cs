using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Nerv.IIP.PlatformGateway.Web.Application.Http;

namespace Nerv.IIP.PlatformGateway.Web.Tests;

public sealed class GatewayAcceptLanguageForwardingTests
{
    [Fact]
    public async Task Forwarding_handler_copies_accept_language_from_current_request()
    {
        var contextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };
        contextAccessor.HttpContext.Request.Headers.AcceptLanguage = new StringValues("en-US");
        var inner = new RecordingHandler();
        var handler = new AcceptLanguageForwardingHandler(contextAccessor)
        {
            InnerHandler = inner
        };
        using var client = new HttpClient(handler);

        await client.GetAsync("http://downstream.local/api");

        Assert.Equal("en-US", inner.Request!.Headers.AcceptLanguage.ToString());
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
