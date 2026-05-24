using Microsoft.AspNetCore.Http;

namespace Nerv.IIP.BusinessGateway.Web.Application.Http;

public sealed class AcceptLanguageForwardingHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var acceptLanguage = httpContextAccessor.HttpContext?.Request.Headers.AcceptLanguage.ToString();
        if (!string.IsNullOrWhiteSpace(acceptLanguage) && !request.Headers.AcceptLanguage.Any())
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
