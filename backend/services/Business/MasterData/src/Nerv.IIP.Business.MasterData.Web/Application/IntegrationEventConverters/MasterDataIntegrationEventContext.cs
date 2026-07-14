using System.Diagnostics;
using System.Security.Claims;
using Microsoft.Extensions.Primitives;
using Nerv.IIP.Contracts.MasterData;

namespace Nerv.IIP.Business.MasterData.Web.Application.IntegrationEventConverters;

public sealed record MasterDataIntegrationEventContext(
    string CorrelationId,
    string CausationId,
    string Actor,
    string? IdempotencyKey = null);

public interface IMasterDataIntegrationEventContextAccessor
{
    MasterDataIntegrationEventContext GetContext();
}

public sealed class HttpMasterDataIntegrationEventContextAccessor(IHttpContextAccessor httpContextAccessor)
    : IMasterDataIntegrationEventContextAccessor
{
    public MasterDataIntegrationEventContext GetContext()
    {
        var httpContext = httpContextAccessor.HttpContext;
        var headers = httpContext?.Request.Headers;

        return new MasterDataIntegrationEventContext(
            ReadHeader(headers, "X-Correlation-Id")
                ?? Activity.Current?.GetTagItem("correlationId")?.ToString()
                ?? Guid.NewGuid().ToString("n"),
            ReadHeader(headers, "X-Causation-Id") ?? Guid.NewGuid().ToString("n"),
            ResolveActor(httpContext?.User, headers),
            ReadHeader(headers, "X-Idempotency-Key"));
    }

    private static string ResolveActor(ClaimsPrincipal? user, IHeaderDictionary? headers)
    {
        var forwardedActor = ReadHeader(headers, "X-Authenticated-Actor");
        var tokenType = user?.FindFirstValue("token_type");
        if (string.Equals(tokenType, "internal_service", StringComparison.Ordinal)
            && IsCanonicalActor(forwardedActor))
        {
            return forwardedActor!;
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
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
