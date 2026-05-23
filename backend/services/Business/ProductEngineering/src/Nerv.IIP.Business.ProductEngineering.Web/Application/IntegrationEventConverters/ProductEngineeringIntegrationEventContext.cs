using System.Diagnostics;
using System.Security.Claims;
using Microsoft.Extensions.Primitives;
using Nerv.IIP.Contracts.ProductEngineering;

namespace Nerv.IIP.Business.ProductEngineering.Web.Application.IntegrationEventConverters;

public sealed record ProductEngineeringIntegrationEventContext(
    string CorrelationId,
    string CausationId,
    string Actor);

public interface IProductEngineeringIntegrationEventContextAccessor
{
    ProductEngineeringIntegrationEventContext GetContext();
}

public sealed class HttpProductEngineeringIntegrationEventContextAccessor(IHttpContextAccessor httpContextAccessor)
    : IProductEngineeringIntegrationEventContextAccessor
{
    public ProductEngineeringIntegrationEventContext GetContext()
    {
        var httpContext = httpContextAccessor.HttpContext;
        var headers = httpContext?.Request.Headers;
        return new ProductEngineeringIntegrationEventContext(
            ReadHeader(headers, "X-Correlation-Id")
                ?? Activity.Current?.GetTagItem("correlationId")?.ToString()
                ?? Guid.NewGuid().ToString("n"),
            ReadHeader(headers, "X-Causation-Id") ?? Guid.NewGuid().ToString("n"),
            ResolveActor(httpContext?.User, headers));
    }

    private static string ResolveActor(ClaimsPrincipal? user, IHeaderDictionary? headers)
    {
        var subject = user?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user?.FindFirstValue("sub");
        if (!string.IsNullOrWhiteSpace(subject))
        {
            return $"user:{subject}";
        }

        var headerActor = ReadHeader(headers, "X-Actor");
        if (!string.IsNullOrWhiteSpace(headerActor))
        {
            return headerActor;
        }

        var name = user?.Identity?.Name;
        return string.IsNullOrWhiteSpace(name)
            ? $"system:{ProductEngineeringIntegrationEventSources.BusinessProductEngineering}"
            : $"user:{name}";
    }

    private static string? ReadHeader(IHeaderDictionary? headers, string name)
    {
        if (headers is null || !headers.TryGetValue(name, out StringValues values))
        {
            return null;
        }

        var value = values.FirstOrDefault();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
