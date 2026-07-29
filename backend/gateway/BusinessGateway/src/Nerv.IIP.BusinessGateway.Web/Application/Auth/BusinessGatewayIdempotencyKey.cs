using System.Net;
using System.Reflection;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

namespace Nerv.IIP.BusinessGateway.Web.Application.Auth;

internal static class BusinessGatewayIdempotencyKey
{
    private const int MaximumLength = 150;

    public static TRequest Resolve<TRequest>(HttpContext context, TRequest request)
        where TRequest : notnull
    {
        var property = typeof(TRequest).GetProperty(
            "IdempotencyKey",
            BindingFlags.Instance | BindingFlags.Public);
        if (property?.PropertyType != typeof(string))
        {
            return request;
        }

        var standard = NormalizeHeaders(context.Request.Headers["Idempotency-Key"]);
        var legacy = NormalizeHeaders(context.Request.Headers["X-Idempotency-Key"]);
        var body = Normalize(property.GetValue(request) as string);
        var values = new[] { standard, legacy, body }
            .Where(x => x is not null)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (values.Length > 1)
        {
            throw Mismatch();
        }

        var resolved = standard ?? legacy ?? body;
        if (resolved is not null)
        {
            property.SetValue(request, resolved);
        }

        return request;
    }

    public static string? FromBody(object? body)
    {
        if (body is null)
        {
            return null;
        }

        var property = body.GetType().GetProperty(
            "IdempotencyKey",
            BindingFlags.Instance | BindingFlags.Public);
        return property is null ? null : Normalize(property.GetValue(body) as string);
    }

    public static string? ResolveForAudit(HttpContext context, object? body)
    {
        var standard = NormalizeHeaders(context.Request.Headers["Idempotency-Key"]);
        var legacy = NormalizeHeaders(context.Request.Headers["X-Idempotency-Key"]);
        var bodyValue = FromBody(body);
        var values = new[] { standard, legacy, bodyValue }
            .Where(x => x is not null)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (values.Length > 1)
        {
            throw Mismatch();
        }

        return standard ?? legacy ?? bodyValue;
    }

    private static string? NormalizeHeaders(Microsoft.Extensions.Primitives.StringValues values)
    {
        var normalized = values
            .Select(Normalize)
            .Where(x => x is not null)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return normalized.Length switch
        {
            0 => null,
            1 => normalized[0],
            _ => throw Mismatch(),
        };
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > MaximumLength
            || normalized.Any(ch => !IsAllowed(ch)))
        {
            throw Mismatch();
        }

        return normalized;
    }

    private static bool IsAllowed(char value) =>
        value is >= 'a' and <= 'z'
            or >= 'A' and <= 'Z'
            or >= '0' and <= '9'
            or '.'
            or '_'
            or ':'
            or '/'
            or '-';

    private static BusinessServiceProxyException Mismatch() =>
        BusinessServiceProxyException.FromSafeDownstreamMessage(
            HttpStatusCode.Conflict,
            "idempotency-key-mismatch");
}
