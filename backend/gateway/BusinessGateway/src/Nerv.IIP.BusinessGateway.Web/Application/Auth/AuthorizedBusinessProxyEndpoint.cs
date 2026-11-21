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
    /// 在 DTO 校验前鉴权并归一化幂等键。已写出的拒绝响应优先于后续校验结果。
    /// </summary>
    /// <remarks>
    /// 无法提前判定租户权限时，仅推迟鉴权。头部幂等键仍须写回 DTO，确保端点规则检查两种键来源。
    /// FastEndpoints 的预处理器晚于 DTO 校验，因此该逻辑位于 OnBeforeValidateAsync。
    /// </remarks>
    public override async Task OnBeforeValidateAsync(TRequest req, CancellationToken ct)
    {
        if (!TryBuildRequirements(req, out var requirements))
        {
            // 即使鉴权推迟，DTO 校验也需要归一化后的头部幂等键。
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
    /// 作用域访问器可能因 DTO 缺字段而抛异常，空作用域也无法形成权限结论。
    /// 前置钩子把这两种情形交给 DTO 校验。如果校验通过，HandleAsync 重新读取作用域并鉴权，
    /// 读取异常由宿主处理，鉴权得到放行后才转发。
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
