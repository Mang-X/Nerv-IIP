using System.Diagnostics;
using System.Security.Claims;
using Microsoft.Extensions.Primitives;

namespace Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;

public sealed record ErpIntegrationEventContext(string CorrelationId, string CausationId, string Actor);

public interface IErpIntegrationEventContextAccessor
{
    ErpIntegrationEventContext GetContext();
}

public sealed class HttpErpIntegrationEventContextAccessor(IHttpContextAccessor httpContextAccessor)
    : IErpIntegrationEventContextAccessor
{
    public ErpIntegrationEventContext GetContext()
    {
        var httpContext = httpContextAccessor.HttpContext;
        var headers = httpContext?.Request.Headers;
        return new ErpIntegrationEventContext(
            ReadHeader(headers, "X-Correlation-Id")
                ?? Activity.Current?.GetTagItem("correlationId")?.ToString()
                ?? Activity.Current?.TraceId.ToString()
                ?? Guid.NewGuid().ToString("n"),
            ReadHeader(headers, "X-Causation-Id")
                ?? Activity.Current?.SpanId.ToString()
                ?? Guid.NewGuid().ToString("n"),
            ResolveActor(httpContext?.User, headers));
    }

    private static string ResolveActor(ClaimsPrincipal? user, IHeaderDictionary? headers)
    {
        var forwardedActor = ReadHeader(headers, "X-Authenticated-Actor");
        var tokenType = user?.FindFirstValue("token_type");
        if (string.Equals(tokenType, "internal_service", StringComparison.Ordinal) && IsCanonicalActor(forwardedActor))
        {
            return forwardedActor!;
        }

        var subject = user?.FindFirstValue(ClaimTypes.NameIdentifier) ?? user?.FindFirstValue("sub");
        if (!string.IsNullOrWhiteSpace(subject))
        {
            return $"user:{subject}";
        }

        return "system:business-erp";
    }

    private static bool IsCanonicalActor(string? actor)
    {
        if (string.IsNullOrWhiteSpace(actor)) return false;
        var separator = actor.IndexOf(':', StringComparison.Ordinal);
        return separator > 0 && separator < actor.Length - 1;
    }

    private static string? ReadHeader(IHeaderDictionary? headers, string name)
    {
        if (headers is null || !headers.TryGetValue(name, out StringValues values))
        {
            return null;
        }

        var value = values.FirstOrDefault();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
