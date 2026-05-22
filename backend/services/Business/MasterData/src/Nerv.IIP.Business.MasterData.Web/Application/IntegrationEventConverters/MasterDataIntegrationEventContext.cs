using System.Diagnostics;
using System.Security.Claims;
using Microsoft.Extensions.Primitives;
using Nerv.IIP.Contracts.MasterData;

namespace Nerv.IIP.Business.MasterData.Web.Application.IntegrationEventConverters;

public sealed record MasterDataIntegrationEventContext(
    string CorrelationId,
    string CausationId,
    string Actor);

public interface IMasterDataIntegrationEventContextAccessor
{
    MasterDataIntegrationEventContext GetContext(string defaultCausationId);
}

public sealed class HttpMasterDataIntegrationEventContextAccessor(IHttpContextAccessor httpContextAccessor)
    : IMasterDataIntegrationEventContextAccessor
{
    public MasterDataIntegrationEventContext GetContext(string defaultCausationId)
    {
        var httpContext = httpContextAccessor.HttpContext;
        var headers = httpContext?.Request.Headers;

        return new MasterDataIntegrationEventContext(
            ReadHeader(headers, "X-Correlation-Id")
                ?? Activity.Current?.TraceId.ToString()
                ?? Guid.NewGuid().ToString("n"),
            ReadHeader(headers, "X-Causation-Id") ?? defaultCausationId,
            ResolveActor(httpContext?.User, headers));
    }

    private static string ResolveActor(ClaimsPrincipal? user, IHeaderDictionary? headers)
    {
        var headerActor = ReadHeader(headers, "X-Actor");
        if (!string.IsNullOrWhiteSpace(headerActor))
        {
            return headerActor;
        }

        var subject = user?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user?.FindFirstValue("sub");
        if (!string.IsNullOrWhiteSpace(subject))
        {
            return $"user:{subject}";
        }

        var name = user?.Identity?.Name;
        return string.IsNullOrWhiteSpace(name)
            ? $"system:{MasterDataIntegrationEventSources.BusinessMasterData}"
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
