using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Primitives;

namespace Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;

public sealed record ErpIntegrationEventContext(string CorrelationId, string CausationId, string Actor);

public interface IErpIntegrationEventContextAccessor
{
    ErpIntegrationEventContext GetContext();

    IDisposable BeginScope(string causationId, string? correlationId = null, string? actor = null) => EmptyErpIntegrationEventContextScope.Instance;
}

public sealed class HttpErpIntegrationEventContextAccessor(IHttpContextAccessor httpContextAccessor)
    : IErpIntegrationEventContextAccessor
{
    private static readonly AsyncLocal<ScopedErpIntegrationEventContext?> CurrentScope = new();

    public ErpIntegrationEventContext GetContext()
    {
        var httpContext = httpContextAccessor.HttpContext;
        var headers = httpContext?.Request.Headers;
        var scoped = CurrentScope.Value;
        return new ErpIntegrationEventContext(
            scoped?.CorrelationId
                ?? ReadHeader(headers, "X-Correlation-Id")
                ?? Activity.Current?.GetTagItem("correlationId")?.ToString()
                ?? Activity.Current?.TraceId.ToString()
                ?? Guid.NewGuid().ToString("n"),
            ReadHeader(headers, "X-Causation-Id")
                ?? scoped?.CausationId
                ?? throw new InvalidOperationException("ERP sales-order integration events require an explicit command or upstream-event causation id."),
            scoped?.Actor ?? ResolveActor(httpContext?.User, headers));
    }

    public IDisposable BeginScope(string causationId, string? correlationId = null, string? actor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(causationId);
        var previous = CurrentScope.Value;
        CurrentScope.Value = new ScopedErpIntegrationEventContext(causationId, correlationId, actor);
        return new RestoreScope(previous);
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

    private sealed record ScopedErpIntegrationEventContext(string CausationId, string? CorrelationId, string? Actor);

    private sealed class RestoreScope(ScopedErpIntegrationEventContext? previous) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed) return;
            CurrentScope.Value = previous;
            disposed = true;
        }
    }
}

public static class ErpCommandCausationIds
{
    public static string ForHttpCommand(string commandName, params object?[] parts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        var canonical = string.Join('|', parts.Select(part => Convert.ToString(part, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant()[..24];
        return $"command:{commandName}:{hash}";
    }
}

internal sealed class EmptyErpIntegrationEventContextScope : IDisposable
{
    public static EmptyErpIntegrationEventContextScope Instance { get; } = new();

    public void Dispose()
    {
    }
}
