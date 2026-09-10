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
        if (normalized.Length > MaximumLength)
        {
            throw TooLong();
        }

        if (normalized.Any(ch => !IsAllowed(ch)))
        {
            throw InvalidCharacters();
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

    /// <summary>
    /// 409 只留给「同一请求里出现了两个互不相同的幂等键」这一件事。本文件里有且只有
    /// 三个调用点，覆盖它的全部形态（改动本方法射程时请连同这份枚举一起改）：
    /// <list type="number">
    /// <item><description><see cref="Resolve{TRequest}"/>：标准头、legacy 头与请求体三者归一化后不一致。</description></item>
    /// <item><description><see cref="ResolveForAudit"/>：同上，只是键取自审计路径的 body 对象。</description></item>
    /// <item><description><see cref="NormalizeHeaders"/>：<b>同一个头名重复出现</b>且多个取值归一化后不一致
    /// （例如两行 <c>Idempotency-Key</c>）——这一条不在上面两条的「三来源」枚举里。</description></item>
    /// </list>
    /// 共同点是调用方需要去查「是不是同一个键被用在了别的意图上」。输入本身的形状问题
    /// （超长、非法字符）不属于冲突，见 <see cref="TooLong"/> 与 <see cref="InvalidCharacters"/>。
    /// </summary>
    private static BusinessServiceProxyException Mismatch() =>
        BusinessServiceProxyException.FromSafeDownstreamMessage(
            HttpStatusCode.Conflict,
            "idempotency-key-mismatch");

    /// <summary>
    /// 幂等键超过 <see cref="MaximumLength"/>。这是入参超长，不是键冲突，所以是 400 而不是 409：
    /// 409 会把调用方引到「查查这个键是不是重复用了」的方向，而真正要做的是把键改短。
    /// 本方法不改变值域——被拒的输入集合与改动前逐字相同，只改状态码与稳定消息。
    /// </summary>
    private static BusinessServiceProxyException TooLong() =>
        BusinessServiceProxyException.FromSafeDownstreamMessage(
            HttpStatusCode.BadRequest,
            "idempotency-key-too-long");

    /// <summary>
    /// 幂等键含 <see cref="IsAllowed"/> 之外的字符。与 <see cref="TooLong"/> 同理：
    /// 这是入参形状不合法，不是键冲突，因此是 400。
    /// </summary>
    private static BusinessServiceProxyException InvalidCharacters() =>
        BusinessServiceProxyException.FromSafeDownstreamMessage(
            HttpStatusCode.BadRequest,
            "idempotency-key-invalid-characters");
}
