using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.DeadLetters;

/// <summary>
/// 集成事件死信的跨服务运维面（#3739）。
/// </summary>
/// <remarks>
/// <c>organizationId</c> / <c>environmentId</c> 是**权限检查的作用域**，不是行过滤条件：
/// 死信行记录的是某个服务消费失败的事实，本身不带组织/环境列，网关不会凭这两个值伪造范围。
/// </remarks>
public sealed class BusinessConsoleDeadLetterListRequest
{
    public string OrganizationId { get; set; } = string.Empty;

    public string EnvironmentId { get; set; } = string.Empty;

    /// <summary>限定单个来源服务；留空则扇出全部来源。</summary>
    public string? Service { get; set; }

    public string? ConsumerName { get; set; }

    public string? EventType { get; set; }

    public string? FailureCode { get; set; }

    public IntegrationEventDeadLetterStatus? Status { get; set; }

    public DateTimeOffset? DeadLetteredFromUtc { get; set; }

    public DateTimeOffset? DeadLetteredToUtc { get; set; }

    /// <summary>逐来源取数上限。扇出没有跨服务游标，因此这里不是全局分页。</summary>
    public int? Take { get; set; }
}

public sealed class BusinessConsoleDeadLetterMetricsRequest
{
    public string OrganizationId { get; set; } = string.Empty;

    public string EnvironmentId { get; set; } = string.Empty;

    public string? Service { get; set; }
}

/// <remarks>
/// 绑定来源写成显式的 <see cref="RouteParamAttribute"/> / <see cref="QueryParamAttribute"/>：
/// 本 DTO 同时挂在一个 GET（详情）与一个 POST（重放）上，不标注时 POST 那条的作用域两字段
/// 在导出的 OpenAPI 里既不落进 query 也不落进 body，生成客户端于是无法发出一次合法调用
/// （服务端校验要求两者非空）。同形共用 DTO 的既有样板 <c>BusinessConsoleSchedulingPlanRequest</c>
/// 正是这样标注的，其 release / revoke 两条 POST 因此带着 query 作用域。
/// </remarks>
public sealed class BusinessConsoleDeadLetterItemRequest
{
    [QueryParam]
    public string OrganizationId { get; set; } = string.Empty;

    [QueryParam]
    public string EnvironmentId { get; set; } = string.Empty;

    [RouteParam]
    public string Service { get; set; } = string.Empty;

    [RouteParam]
    public Guid DeadLetterId { get; set; }
}

public sealed class BusinessConsoleIgnoreDeadLetterRequest
{
    public string OrganizationId { get; set; } = string.Empty;

    public string EnvironmentId { get; set; } = string.Empty;

    public string Service { get; set; } = string.Empty;

    public Guid DeadLetterId { get; set; }

    public string Reason { get; set; } = string.Empty;
}

public sealed class BusinessConsoleReplayDeadLettersRequest
{
    public string OrganizationId { get; set; } = string.Empty;

    public string EnvironmentId { get; set; } = string.Empty;

    public string Service { get; set; } = string.Empty;

    public string? ConsumerName { get; set; }

    public string? EventType { get; set; }

    public string? FailureCode { get; set; }

    public IntegrationEventDeadLetterStatus? Status { get; set; }

    public DateTimeOffset? DeadLetteredFromUtc { get; set; }

    public DateTimeOffset? DeadLetteredToUtc { get; set; }

    public int? Take { get; set; }
}

[Tags("Business Console Dead Letters")]
[HttpGet("/api/business-console/v1/dead-letters")]
[BusinessGatewayOperationId("listBusinessConsoleDeadLetters")]
public sealed class ListBusinessConsoleDeadLettersEndpoint(
    BusinessConsoleDeadLetterService deadLetters,
    IBusinessGatewayAuthorizationClient auth)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleDeadLetterListRequest, BusinessConsoleDeadLetterListResponse>(
        auth,
        BusinessGatewayPermissions.DeadLettersRead)
{
    protected override string OrganizationId(BusinessConsoleDeadLetterListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleDeadLetterListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleDeadLetterListResponse> ForwardAsync(
        BusinessConsoleDeadLetterListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        deadLetters.ListAsync(
            request.Service,
            new ListIntegrationEventDeadLettersRequest
            {
                ConsumerName = request.ConsumerName,
                EventType = request.EventType,
                FailureCode = request.FailureCode,
                Status = request.Status,
                DeadLetteredFromUtc = request.DeadLetteredFromUtc,
                DeadLetteredToUtc = request.DeadLetteredToUtc,
                Take = request.Take,
            },
            cancellationToken);
}

[Tags("Business Console Dead Letters")]
[HttpGet("/api/business-console/v1/dead-letters/metrics")]
[BusinessGatewayOperationId("getBusinessConsoleDeadLetterMetrics")]
public sealed class GetBusinessConsoleDeadLetterMetricsEndpoint(
    BusinessConsoleDeadLetterService deadLetters,
    IBusinessGatewayAuthorizationClient auth)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleDeadLetterMetricsRequest, BusinessConsoleDeadLetterMetricsResponse>(
        auth,
        BusinessGatewayPermissions.DeadLettersRead)
{
    protected override string OrganizationId(BusinessConsoleDeadLetterMetricsRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleDeadLetterMetricsRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleDeadLetterMetricsResponse> ForwardAsync(
        BusinessConsoleDeadLetterMetricsRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        deadLetters.GetMetricsAsync(request.Service, cancellationToken);
}

[Tags("Business Console Dead Letters")]
[HttpGet("/api/business-console/v1/dead-letters/{service}/{deadLetterId}")]
[BusinessGatewayOperationId("getBusinessConsoleDeadLetter")]
public sealed class GetBusinessConsoleDeadLetterEndpoint(
    BusinessConsoleDeadLetterService deadLetters,
    IBusinessDeadLetterClient client,
    IInternalServiceTokenProvider internalServiceToken,
    IBusinessGatewayAuthorizationClient auth)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleDeadLetterItemRequest, IntegrationEventDeadLetterDetailResponse>(
        auth,
        BusinessGatewayPermissions.DeadLettersRead)
{
    protected override string OrganizationId(BusinessConsoleDeadLetterItemRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleDeadLetterItemRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleDeadLetterItemRequest request) => "integration-event-dead-letter";

    protected override string ResourceId(BusinessConsoleDeadLetterItemRequest request) => request.DeadLetterId.ToString("D");

    protected override Task<IntegrationEventDeadLetterDetailResponse> ForwardAsync(
        BusinessConsoleDeadLetterItemRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        client.GetAsync(
            internalServiceToken.BearerToken,
            deadLetters.RequireSource(request.Service),
            request.DeadLetterId,
            cancellationToken);
}

[Tags("Business Console Dead Letters")]
[HttpPost("/api/business-console/v1/dead-letters/{service}/{deadLetterId}/replay")]
[BusinessGatewayOperationId("replayBusinessConsoleDeadLetter")]
public sealed class ReplayBusinessConsoleDeadLetterEndpoint(
    BusinessConsoleDeadLetterService deadLetters,
    IBusinessDeadLetterClient client,
    IInternalServiceTokenProvider internalServiceToken,
    IBusinessGatewayAuthorizationClient auth)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleDeadLetterItemRequest, IntegrationEventDeadLetterReplayResponse>(
        auth,
        BusinessGatewayPermissions.DeadLettersManage)
{
    protected override string OrganizationId(BusinessConsoleDeadLetterItemRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleDeadLetterItemRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleDeadLetterItemRequest request) => "integration-event-dead-letter";

    protected override string ResourceId(BusinessConsoleDeadLetterItemRequest request) => request.DeadLetterId.ToString("D");

    protected override Task<IntegrationEventDeadLetterReplayResponse> ForwardAsync(
        BusinessConsoleDeadLetterItemRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        client.ReplayAsync(
            internalServiceToken.BearerToken,
            deadLetters.RequireSource(request.Service),
            request.DeadLetterId,
            cancellationToken);
}

[Tags("Business Console Dead Letters")]
[HttpPost("/api/business-console/v1/dead-letters/{service}/replay-batch")]
[BusinessGatewayOperationId("replayBusinessConsoleDeadLetters")]
public sealed class ReplayBusinessConsoleDeadLettersEndpoint(
    BusinessConsoleDeadLetterService deadLetters,
    IBusinessDeadLetterClient client,
    IInternalServiceTokenProvider internalServiceToken,
    IBusinessGatewayAuthorizationClient auth)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleReplayDeadLettersRequest, IntegrationEventDeadLetterBatchReplayResponse>(
        auth,
        BusinessGatewayPermissions.DeadLettersManage)
{
    protected override string OrganizationId(BusinessConsoleReplayDeadLettersRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleReplayDeadLettersRequest request) => request.EnvironmentId;

    protected override Task<IntegrationEventDeadLetterBatchReplayResponse> ForwardAsync(
        BusinessConsoleReplayDeadLettersRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        client.ReplayBatchAsync(
            internalServiceToken.BearerToken,
            deadLetters.RequireSource(request.Service),
            new ReplayIntegrationEventDeadLetterBatchRequest
            {
                ConsumerName = request.ConsumerName,
                EventType = request.EventType,
                FailureCode = request.FailureCode,
                Status = request.Status,
                DeadLetteredFromUtc = request.DeadLetteredFromUtc,
                DeadLetteredToUtc = request.DeadLetteredToUtc,
                Take = request.Take,
            },
            cancellationToken);
}

[Tags("Business Console Dead Letters")]
[HttpPost("/api/business-console/v1/dead-letters/{service}/{deadLetterId}/ignore")]
[BusinessGatewayOperationId("ignoreBusinessConsoleDeadLetter")]
public sealed class IgnoreBusinessConsoleDeadLetterEndpoint(
    BusinessConsoleDeadLetterService deadLetters,
    IBusinessDeadLetterClient client,
    IInternalServiceTokenProvider internalServiceToken,
    IBusinessGatewayAuthorizationClient auth)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleIgnoreDeadLetterRequest, IntegrationEventDeadLetterDetailResponse>(
        auth,
        BusinessGatewayPermissions.DeadLettersManage)
{
    protected override string OrganizationId(BusinessConsoleIgnoreDeadLetterRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleIgnoreDeadLetterRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleIgnoreDeadLetterRequest request) => "integration-event-dead-letter";

    protected override string ResourceId(BusinessConsoleIgnoreDeadLetterRequest request) => request.DeadLetterId.ToString("D");

    protected override Task<IntegrationEventDeadLetterDetailResponse> ForwardAsync(
        BusinessConsoleIgnoreDeadLetterRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        client.IgnoreAsync(
            internalServiceToken.BearerToken,
            deadLetters.RequireSource(request.Service),
            request.DeadLetterId,
            new IgnoreIntegrationEventDeadLetterRequest { Reason = request.Reason },
            cancellationToken);
}

/// <remarks>
/// 只校验**网关自己需要**的东西：组织/环境是权限检查的作用域，缺了就作不出权限结论；
/// 逐来源取数上限由网关钳住。忽略原因的非空规则由事实所有方（各服务）执行，这里不抄第二遍。
/// </remarks>
public sealed class BusinessConsoleDeadLetterListRequestValidator : Validator<BusinessConsoleDeadLetterListRequest>
{
    public BusinessConsoleDeadLetterListRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.EnvironmentId).NotEmpty();
        RuleFor(x => x.Take).InclusiveBetween(1, 500).When(x => x.Take.HasValue);
    }
}

public sealed class BusinessConsoleDeadLetterMetricsRequestValidator : Validator<BusinessConsoleDeadLetterMetricsRequest>
{
    public BusinessConsoleDeadLetterMetricsRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.EnvironmentId).NotEmpty();
    }
}

public sealed class BusinessConsoleDeadLetterItemRequestValidator : Validator<BusinessConsoleDeadLetterItemRequest>
{
    public BusinessConsoleDeadLetterItemRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.EnvironmentId).NotEmpty();
    }
}

public sealed class BusinessConsoleIgnoreDeadLetterRequestValidator : Validator<BusinessConsoleIgnoreDeadLetterRequest>
{
    public BusinessConsoleIgnoreDeadLetterRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.EnvironmentId).NotEmpty();
    }
}

public sealed class BusinessConsoleReplayDeadLettersRequestValidator : Validator<BusinessConsoleReplayDeadLettersRequest>
{
    public BusinessConsoleReplayDeadLettersRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.EnvironmentId).NotEmpty();
        RuleFor(x => x.Take).InclusiveBetween(1, 500).When(x => x.Take.HasValue);
    }
}
