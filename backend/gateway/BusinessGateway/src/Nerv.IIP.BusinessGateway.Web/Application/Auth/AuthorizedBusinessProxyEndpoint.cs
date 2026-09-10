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
    /// </remarks>
    public override async Task OnBeforeValidateAsync(TRequest req, CancellationToken ct)
    {
        if (!TryBuildRequirements(req, out var requirements))
        {
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
    /// 判断这个请求现在能不能作出**有意义的**鉴权结论。作不出时前置钩子不鉴权，
    /// 交由 DTO 校验先答；若校验放行，<see cref="HandleAsync" /> 会补齐鉴权与归一化，
    /// 因此这两类请求的可观察行为与鉴权前移之前逐字相同，也不存在未鉴权的转发。
    /// </summary>
    /// <remarks>
    /// <para>两种作不出结论的情形：</para>
    /// <list type="number">
    /// <item><description><b>作用域访问器抛</b>——它们读的是尚未经过 DTO 校验的请求对象，
    /// 例如 <c>request.Problem.OrganizationId</c> 在 <c>problem</c> 缺失时会解空引用。
    /// 这里用 catch 而不是逐个端点登记「哪个访问器会抛」，是为了不留一份会被后来者绕过的名单。</description></item>
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
