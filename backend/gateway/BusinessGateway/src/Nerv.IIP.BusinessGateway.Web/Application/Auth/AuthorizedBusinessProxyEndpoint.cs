using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using NetCorePal.Extensions.Dto;
using System.Net;
using System.Text.Json;

namespace Nerv.IIP.BusinessGateway.Web.Application.Auth;

[Authorize(Policy = BusinessGatewayPolicies.BusinessConsoleAuthenticated)]
public abstract class AuthorizedBusinessProxyEndpoint<TRequest, TResponse>(
    IBusinessGatewayAuthorizationClient auth,
    IReadOnlyCollection<string> permissionCodes) : Endpoint<TRequest, ResponseData<TResponse>>
    where TRequest : notnull
{
    /// <summary>
    /// 前置钩子把鉴权结论寄存在这里，供 <see cref="HandleAsync" /> 复用，避免同一请求鉴权两次。
    /// 三态：键不存在 = 前置钩子未鉴权（见 <see cref="TryBuildRequirements" /> 的两种推迟情形）；
    /// 值为 <see cref="string" /> = 已放行的 bearer token；值为 <c>null</c> = 已拒绝且响应已写出。
    /// </summary>
    private const string AuthorizationOutcomeItemKey = "Nerv.IIP.BusinessGateway.ProxyAuthorizationOutcome";

    protected AuthorizedBusinessProxyEndpoint(
        IBusinessGatewayAuthorizationClient auth,
        string permissionCode) : this(auth, [permissionCode])
    {
    }

    protected IBusinessGatewayAuthorizationClient AuthorizationClient => auth;

    /// <summary>
    /// 鉴权与幂等键归一化必须跑在 DTO 校验之前（#3330 编排者裁定）。
    /// </summary>
    /// <remarks>
    /// <para>FastEndpoints 的执行序是
    /// <c>BindRequestAsync → OnBeforeValidate(Async) → ValidateRequest → OnAfterValidate(Async)
    /// → PreProcessors → HandleAsync</c>（<c>Endpoint.ExecAsync</c>）。
    /// 注意 <b>PreProcessor 跑在校验之后</b>，因此「校验之前」这个位置只有
    /// <see cref="Endpoint{TRequest,TResponse}.OnBeforeValidateAsync" /> 拿得到；
    /// 它还是实例方法，能直接用 <see cref="OrganizationId" /> 等虚成员，无需把端点状态搬进处理器。</para>
    ///
    /// <para>把这两步前移同时解掉两个同源后果：</para>
    /// <list type="number">
    /// <item><description>归一化后的键在校验时已写回 DTO，端点级 <c>IdempotencyKey</c> 规则
    /// 因此对「<c>Idempotency-Key</c> 头」与「请求体」两条来源同时生效（此前头部来源恒过）。</description></item>
    /// <item><description>鉴权先行，已认证但无权限的调用方恒定拿 403，不再因载荷触发某条校验规则而变成 400。</description></item>
    /// </list>
    ///
    /// <para>短路方式是把响应标记为已开始（<see cref="Endpoint{TRequest,TResponse}.ResponseStarted" />）：
    /// <c>ExecAsync</c> 在 PreProcessors 之后、<c>HandleAsync</c> 之前检查该标记；
    /// 校验失败分支同样只在未开始时才写错误体，因此先写出的 403/400/409 不会被覆盖。</para>
    ///
    /// <para><b>两步的耦合已在 #3345 拆开：鉴权可以推迟，归一化不推迟。</b>
    /// <see cref="TryBuildRequirements" /> 作不出鉴权结论时，#3330 把**两步一起**推迟到
    /// <see cref="HandleAsync" />，于是那条支路上头部来的键在校验时仍是 <c>null</c>、端点级规则恒过。
    /// 该支路**能走到 <see cref="ForwardAsync" /></b>——#3345 审核用真 HTTP 探针实测：
    /// 请求体的作用域字段**缺省**（是 <c>null</c>，不是空串）∧ 令牌也没有
    /// <c>organizationId</c> / <c>environmentId</c> 声明（两者都为 <c>null</c> 时
    /// <c>BusinessGatewayAuthorization</c> 的作用域相等门 <c>string.Equals(null, null)</c> 为真）
    /// ∧ 键走头部 ⇒ <c>200</c>、<c>forwarded=1</c>、下游收到 300 字符的键。
    /// 我此前在这里写的「该支路今天走不到 <see cref="ForwardAsync" />」**是错的，已删**。</para>
    ///
    /// <para><b>为什么在这条支路上让归一化先于（推迟的）鉴权，不违反上面第 2 条</b>：
    /// 第 2 条护的是「**已认证但无权限**的调用方恒定拿 403」，前提是**作得出**权限结论。
    /// 而 <see cref="TryBuildRequirements" /> 返回 <c>false</c> 的定义就是**作不出**——
    /// 见该方法注释里 #3330 自己写下的判断：那里的 403 是「拿空串跟令牌比出来的结果，
    /// **不是一句关于权限的真话**」，所以 #3330 明确把这一情形「留给 DTO 校验回答」。
    /// 让归一化先跑，正是把这一情形真正交给 DTO 校验回答（此前它拿不到头部键，等于没回答）。
    /// ⇒ 本改动**没有**反转 #3330 的方向，而是补齐了它；
    /// 凡是作得出鉴权结论的请求（<see cref="TryBuildRequirements" /> 为真），鉴权仍然先行。</para>
    ///
    /// <para><b>可观察行为在这条支路上确实变了，如实登记</b>：作用域**缺省或为空串**
    /// （<see cref="TryBuildRequirements" /> 里 <c>IsNullOrWhiteSpace</c> 为真的**整条支路**，
    /// 两种取值都算——别按「缺省」一种读窄了）且键违反端点级规则时，响应从 403 变成 400；
    /// 其中「作用域缺省 ∧ 令牌也无作用域声明」那一格更是从 <c>200</c>（键已转发下去）变成 400。
    /// 方向是「用一句真话（这把键太长）替换一句关于权限的假话、以及替换一次危险的放行」，
    /// 但它是行为变更，不是纯重构。五格探针（<c>BusinessGatewayRequestPipelineOrderTests</c> 里
    /// <c>Deferred_authorization_still_binds_header_supplied_keys_to_the_endpoint_level_rule</c>，
    /// 缺省与空串两种取值都覆盖）把改动前后的每一格都钉住。</para>
    /// </remarks>
    public override async Task OnBeforeValidateAsync(TRequest req, CancellationToken ct)
    {
        if (!TryBuildRequirements(req, out var requirements))
        {
            // 鉴权推迟了（见 TryBuildRequirements），**归一化不跟着推迟**。#3345 审核实测：
            // 两者写在同一个早返回里时，这条支路上经头传来的键在 DTO 校验发生时仍是 null，
            // 端点级规则恒过，键一路进 ForwardAsync（探针：作用域字段缺省 ∧ 令牌无
            // organizationId/environmentId 声明 ∧ 键走头部 ⇒ 200 + forwarded=1 + 键长 300）。
            // Resolve 不依赖鉴权结论（只要 HttpContext 与请求对象），所以这里可以、也必须先跑。
            if (!await NormalizeIdempotencyKeyAsync(req, ct))
            {
                ResponseStarted = true;
            }

            return;
        }

        if (!await AuthorizeAndNormalizeAsync(req, requirements, ct))
        {
            ResponseStarted = true;
        }
    }

    public override async Task HandleAsync(TRequest req, CancellationToken ct)
    {
        if (!HttpContext.Items.ContainsKey(AuthorizationOutcomeItemKey) &&
            !await AuthorizeAndNormalizeAsync(req, BuildRequirements(req), ct))
        {
            return;
        }

        if (HttpContext.Items[AuthorizationOutcomeItemKey] is not string bearerToken)
        {
            return;
        }

        try
        {
            var response = await ForwardAsync(req, bearerToken, ct);
            await ResponseDataEndpointResults.WriteDataAsync(HttpContext, StatusCode, response, ct, ResponseJsonOptions);
        }
        catch (BusinessServiceProxyException ex)
        {
            await ResponseDataEndpointResults.WriteErrorAsync(
                HttpContext,
                ex,
                ct);
        }
    }

    /// <summary>
    /// 鉴权，通过后就地把幂等键归一化写回 DTO。返回 <c>false</c> 表示响应已写出，调用方应停止后续处理。
    /// </summary>
    private async Task<bool> AuthorizeAndNormalizeAsync(
        TRequest req,
        IReadOnlyCollection<BusinessGatewayPermissionRequirement> requirements,
        CancellationToken ct)
    {
        var bearerToken = await BusinessGatewayAuthorization.RequireAnyPermissionAsync(
            HttpContext,
            auth,
            requirements,
            AuthorizationContinuityMode,
            ct);
        HttpContext.Items[AuthorizationOutcomeItemKey] = bearerToken;
        if (bearerToken is null)
        {
            return false;
        }

        return await NormalizeIdempotencyKeyAsync(req, ct);
    }

    /// <summary>
    /// 就地把幂等键归一化写回 DTO。返回 <c>false</c> 表示响应已写出，调用方应停止后续处理。
    /// </summary>
    /// <remarks>
    /// 从 <see cref="AuthorizeAndNormalizeAsync" /> 里拆出来，是因为它**不依赖鉴权结论**：
    /// 只要 <see cref="HttpContext" /> 与请求对象。#3330 把两者写在一起是实现耦合，
    /// 不是逻辑依赖；#3345 的残余窗口就是这个耦合的直接后果。
    /// </remarks>
    private async Task<bool> NormalizeIdempotencyKeyAsync(TRequest req, CancellationToken ct)
    {
        try
        {
            // Resolve 用反射就地写回 DTO 的 IdempotencyKey（返回的就是同一个实例），
            // 所以随后的 DTO 校验看到的是归一化后的值。
            BusinessGatewayIdempotencyKey.Resolve(HttpContext, req);
        }
        catch (BusinessServiceProxyException ex)
        {
            // 超长 400 / 非法字符 400 / 双键不一致 409：与转发失败共用同一个错误响应通道，
            // 不因为挪到校验之前就变成未处理异常。
            await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, ex, ct);
            return false;
        }

        return true;
    }

    private BusinessGatewayPermissionRequirement[] BuildRequirements(TRequest req) =>
        permissionCodes.Select(permissionCode => new BusinessGatewayPermissionRequirement(
                permissionCode,
                OrganizationId(req),
                EnvironmentId(req),
                ResourceType(req),
                ResourceId(req),
                IncludePrincipalContext))
            .ToArray();

    /// <summary>
    /// 判断这个请求现在能不能作出**有意义的**鉴权结论。作不出时前置钩子**不鉴权**，
    /// 交由 DTO 校验先答；若校验放行，<see cref="HandleAsync" /> 会补齐鉴权，
    /// 因此不存在未鉴权的转发。
    /// </summary>
    /// <remarks>
    /// <para>⚠️ <b>这里原写「这两类请求的可观察行为与鉴权前移之前逐字相同」，#3327 之后**不再成立**</b>：
    /// 那句的前提是「鉴权与幂等键归一化一起推迟」，而 #3327 已把归一化留在前置钩子里
    /// （<see cref="OnBeforeValidateAsync" />），正因为一起推迟会让头部来的键绕过端点级规则并被转发下去。
    /// ⇒ 本支路上「键违反端点级规则」的请求，响应确实与从前不同；变更集与实测见
    /// <see cref="OnBeforeValidateAsync" /> 的注释。**鉴权本身的行为没变**——推迟的仍然只是鉴权。</para>
    ///
    /// <para>两种作不出结论的情形：</para>
    /// <list type="number">
    /// <item><description><b>作用域访问器抛</b>——它们读的是尚未经过 DTO 校验的请求对象，
    /// 例如 <c>request.Problem.OrganizationId</c> 在 <c>problem</c> 缺失时会解空引用。
    /// 这里用 catch 而不是逐个端点登记「哪个访问器会抛」，是为了不留一份会被后来者绕过的名单。
    /// <para><b>这个 <c>catch (Exception)</c> 不吞异常</b>（看到裸 catch 请先读完这段）：
    /// 它只是**不在这里**作鉴权判断，异常本身并没有被消化掉。两条出路都已实测：
    /// 若 DTO 校验随后放行，<see cref="HandleAsync" /> 会再调一次 <see cref="BuildRequirements" />，
    /// 同一个异常在**没有任何 catch** 的路径上原样抛出，交给宿主的异常处理中间件；
    /// 若 DTO 校验拒绝，请求根本走不到 <see cref="HandleAsync" />，
    /// 这时的静默与鉴权前移**之前**逐字相同（那时异常同样不会发生，因为校验先答）。
    /// ⇒ 无论哪条，都不存在「异常被这里咽掉、故障因此隐身」的窗口。</para></description></item>
    /// <item><description><b>请求没有指名租户作用域</b>——权限是「某主体在某组织/某环境内的权限」，
    /// 组织或环境为空时不存在可判定的权限问题。此时
    /// <see cref="BusinessGatewayAuthorization.RequireAnyPermissionAsync" /> 会因为
    /// 「声明的作用域与令牌声明不一致」返回 403，那是拿空串跟令牌比出来的结果，
    /// 不是一句关于权限的真话：一个确实有权限、只是漏填了 <c>environmentId</c> 的调用方
    /// 会被告知「你没有权限」。本票要消除的正是这类误导，所以这一情形留给 DTO 校验回答。</description></item>
    /// </list>
    /// </remarks>
    private bool TryBuildRequirements(TRequest req, out BusinessGatewayPermissionRequirement[] requirements)
    {
        try
        {
            requirements = BuildRequirements(req);
        }
        catch (Exception)
        {
            requirements = [];
            return false;
        }

        return requirements.Length > 0 &&
            requirements.All(requirement =>
                !string.IsNullOrWhiteSpace(requirement.OrganizationId) &&
                !string.IsNullOrWhiteSpace(requirement.EnvironmentId));
    }

    protected virtual int StatusCode => StatusCodes.Status200OK;

    protected virtual JsonSerializerOptions? ResponseJsonOptions => null;

    protected virtual bool IncludePrincipalContext => false;

    protected virtual BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        HttpMethods.IsGet(HttpContext.Request.Method)
            ? BusinessGatewayAuthorizationContinuityMode.ReadCacheAllowed
            : BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected virtual string? ResourceType(TRequest request) => null;

    protected virtual string? ResourceId(TRequest request) => null;

    protected BusinessGatewayAuthorizationResult? AuthorizationResult =>
        HttpContext.Items.TryGetValue(BusinessGatewayAuthorization.PrincipalItemKey, out var value)
            ? value as BusinessGatewayAuthorizationResult
            : null;

    protected (string ActorType, string ActorRef) RequireAuthorizedPrincipalActor()
    {
        var authorization = AuthorizationResult
            ?? throw new BusinessServiceProxyException(HttpStatusCode.Forbidden, "approval-principal-unresolved");
        var actorRef = authorization.PrincipalId ?? authorization.LoginName;
        if (string.IsNullOrWhiteSpace(actorRef))
        {
            throw new BusinessServiceProxyException(HttpStatusCode.Forbidden, "approval-principal-unresolved");
        }

        var actorType = string.IsNullOrWhiteSpace(authorization.PrincipalType)
            ? "user"
            : authorization.PrincipalType;
        return (actorType, actorRef);
    }

    protected string RequireAuthorizedPrincipalRecipientRef()
        => RequireAuthorizedPrincipalActorReference();

    protected string RequireAuthorizedPrincipalActorReference()
    {
        var (actorType, actorRef) = RequireAuthorizedPrincipalActor();
        return BusinessGatewayPrincipalReferences.ToRecipientRef(actorType, actorRef);
    }

    protected string RequireAuthorizedPrincipalId()
    {
        var principalId = AuthorizationResult?.PrincipalId;
        return string.IsNullOrWhiteSpace(principalId)
            ? throw new BusinessServiceProxyException(HttpStatusCode.Forbidden, "principal-unresolved")
            : principalId;
    }

    protected BusinessServiceAuditContext RequireAuditContext(object? request) =>
        CreateAuditContext(request);

    protected BusinessServiceAuditContext RequireIdempotentAuditContext(object? request)
    {
        var auditContext = CreateAuditContext(request);
        if (auditContext.IdempotencyKey is null)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadRequest,
                "idempotency-key-required");
        }

        return auditContext;
    }

    private BusinessServiceAuditContext CreateAuditContext(object? request)
    {
        var correlationId = ResolveCorrelationId();
        var causationId = HttpContext.Request.Headers["X-Causation-Id"].FirstOrDefault();
        causationId = string.IsNullOrWhiteSpace(causationId)
            ? correlationId
            : causationId.Trim();
        return new BusinessServiceAuditContext(
            RequireAuthorizedPrincipalActorReference(),
            correlationId,
            causationId,
            BusinessGatewayIdempotencyKey.ResolveForAudit(HttpContext, request));
    }

    protected string ResolveCorrelationId()
    {
        var correlationId = HttpContext.Response.Headers["X-Correlation-Id"].ToString();
        return string.IsNullOrWhiteSpace(correlationId)
            ? throw new InvalidOperationException("The correlation middleware did not establish a correlation ID.")
            : correlationId;
    }

    protected abstract string OrganizationId(TRequest request);

    protected abstract string EnvironmentId(TRequest request);

    protected abstract Task<TResponse> ForwardAsync(
        TRequest request,
        string bearerToken,
        CancellationToken cancellationToken);
}

internal static class BusinessGatewayPrincipalReferences
{
    public static string ToRecipientRef(string actorType, string actorRef) =>
        $"{actorType.Trim().ToLowerInvariant()}:{actorRef.Trim()}";
}
