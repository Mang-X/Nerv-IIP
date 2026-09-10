using System.Net;
using System.Reflection;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

namespace Nerv.IIP.BusinessGateway.Web.Application.Auth;

internal static class BusinessGatewayIdempotencyKey
{
    /// <summary>
    /// 全局幂等键长度钳。**它的角色是兜底，不是约束**（#3287 契约裁定）：
    /// 真正决定各端点值域的是端点级 <c>MaximumLength</c> 规则——那些规则进 OpenAPI 的
    /// <c>maxLength</c>，对客户端是公开契约；钳只负责挡住荒谬输入。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么它不得小于任何端点级规则值</b>：小了就会出现「契约声明
    /// <c>maxLength: 512</c>，实际 151–512 被入口 400 拒」这种**假承诺**。
    /// 该关系由验收侧的
    /// <c>BusinessGatewayIdempotencyKeyDownstreamBoundContractTests
    /// .Global_clamp_is_never_stricter_than_any_endpoint_level_bound</c> 机器钉住
    /// （用 <c>&gt;=</c> 不用 <c>==</c>：钳是兜底，只需「不比任何端点更严」；
    /// <c>==</c> 会把它绑死在端点最大值上，任何人收紧那个最大值就被迫同步改钳）。</para>
    ///
    /// <para><b>150 → 512 抬高的安全性靠什么成立（#3327；不是「试出来的」）</b>：
    /// 抬钳唯一放行的新输入是「长度落在旧钳与新钳之间的键」。它安全的前提是三条合取，
    /// 每一条今天都有机器在守：</para>
    /// <list type="number">
    /// <item>受钳的每一个网关请求类型，要么有端点级规则、要么被登记为「下游零权威」
    /// （<c>Every_clamped_gateway_request_is_bounded_by_an_endpoint_rule_or_registered_without_downstream_authority</c>）。</item>
    /// <item>每条端点级规则 ≤ 其下游权威上界
    /// （<c>Gateway_never_promises_a_longer_idempotency_key_than_its_downstream_accepts</c>）。</item>
    /// <item>端点级规则对**头部与请求体两条来源同时生效**——#3330 把
    /// <see cref="Resolve{TRequest}"/> 挪到了 DTO 校验之前，
    /// #3327 又把它从「鉴权推迟」的那个早返回里拆出来（<c>BusinessGatewayRequestPipelineOrderTests</c>：
    /// <c>Header_supplied_key_is_bound_by_the_endpoint_level_rule</c> 守普通路径，
    /// <c>Deferred_authorization_still_binds_header_supplied_keys_to_the_endpoint_level_rule</c>
    /// 五格守鉴权推迟那条支路）。
    /// <para>⚠️ **这一条是三条里最脆的，历史值得记住**：它不是「一直成立」——
    /// #3330 之前整条头部路径都不受端点级规则约束；#3330 之后仍有一条支路不受约束，
    /// 而且我在 #3327 第一版里把那条支路误判成「走不到 <c>ForwardAsync</c>」，
    /// 被真 HTTP 探针实测证伪（作用域字段缺省 ∧ 令牌无作用域声明 ∧ 键走头部
    /// ⇒ <c>200</c> + <c>forwarded=1</c> + 键长 300）。改动 <c>OnBeforeValidateAsync</c>
    /// 的任何人都要先问：**这条路径上 <see cref="Resolve{TRequest}"/> 还跑不跑？**</para></item>
    /// </list>
    /// <para>⇒ 抬钳后仍没有任何请求能把超出自己下游承受力的键送下去。
    /// 反过来说，**谁把第 3 条改回去，钳就重新变成头部路径上唯一的防线**，
    /// 而那时它已经是 512、拦不住什么了。</para>
    /// </remarks>
    private const int MaximumLength = 512;

    /// <summary>
    /// 全局钳的值，供网关自有测试按边界构造夹具（<c>MaximumKeyLength + 1</c> 之类），
    /// 不必手抄一个会随 <see cref="MaximumLength"/> 变化的数字。
    /// </summary>
    /// <remarks>
    /// **故意是属性而不是把 <see cref="MaximumLength"/> 改成 <c>internal const</c>**：
    /// C# 的 <c>const</c> 在引用方编译期内联，测试程序集会把当时的值烤进去，
    /// 之后只重建本程序集时那些夹具仍用旧值——本仓已有「const 内联导致变异测试假绿假红」的判例。
    /// 属性在运行时读，没有这个窗口。
    /// </remarks>
    internal static int MaximumKeyLength => MaximumLength;

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
