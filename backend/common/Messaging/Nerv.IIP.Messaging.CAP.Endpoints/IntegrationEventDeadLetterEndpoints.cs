using FastEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Dto;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Messaging.CAP;

/// <summary>
/// 死信读取与重放端点的共享实现。每个服务只提供 <typeparamref name="TRoutes"/> 与 6 个密封子类，
/// 请求解析、映射和执行逻辑只有这一份。
/// </summary>
public abstract class ListIntegrationEventDeadLettersEndpointBase<TRoutes>(IIntegrationEventDeadLetterStore deadLetterStore)
    : Endpoint<ListIntegrationEventDeadLettersRequest, ResponseData<IntegrationEventDeadLetterListResponse>>
    where TRoutes : IIntegrationEventDeadLetterRouteGroup
{
    public override void Configure()
    {
        Get(TRoutes.RoutePrefix + IntegrationEventDeadLetterRoutes.Collection);
        Policies(InternalServiceAuthorizationPolicy.Name);
    }

    public override async Task HandleAsync(ListIntegrationEventDeadLettersRequest req, CancellationToken ct)
    {
        var messages = await deadLetterStore.ListAsync(IntegrationEventDeadLetterEndpointMapper.QueryFrom(req), ct);
        await Send.OkAsync(
            new IntegrationEventDeadLetterListResponse(
                messages.Select(IntegrationEventDeadLetterEndpointMapper.ToResponse).ToArray()).AsResponseData(),
            ct);
    }
}

public abstract class GetIntegrationEventDeadLetterMetricsEndpointBase<TRoutes>(IIntegrationEventDeadLetterStore deadLetterStore)
    : EndpointWithoutRequest<ResponseData<IntegrationEventDeadLetterMetricsResponse>>
    where TRoutes : IIntegrationEventDeadLetterRouteGroup
{
    public override void Configure()
    {
        Get(TRoutes.RoutePrefix + IntegrationEventDeadLetterRoutes.Metrics);
        Policies(InternalServiceAuthorizationPolicy.Name);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var metrics = await deadLetterStore.GetMetricsAsync(ct);
        await Send.OkAsync(IntegrationEventDeadLetterEndpointMapper.ToMetricsResponse(metrics).AsResponseData(), ct);
    }
}

public abstract class GetIntegrationEventDeadLetterEndpointBase<TRoutes>(IIntegrationEventDeadLetterStore deadLetterStore)
    : EndpointWithoutRequest<ResponseData<IntegrationEventDeadLetterDetailResponse>>
    where TRoutes : IIntegrationEventDeadLetterRouteGroup
{
    public override void Configure()
    {
        Get(TRoutes.RoutePrefix + IntegrationEventDeadLetterRoutes.Item);
        Policies(InternalServiceAuthorizationPolicy.Name);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var message = await deadLetterStore.GetAsync(Route<Guid>("deadLetterId"), ct);
        if (message is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(IntegrationEventDeadLetterEndpointMapper.ToDetailResponse(message).AsResponseData(), ct);
    }
}

/// <remarks>
/// 执行器按请求解析，不走构造注入：FastEndpoints 在启动期就会构造端点实例来调用 <c>Configure()</c>，
/// 构造注入会把「执行器 → 全部重放 handler → <c>ICapPublisher</c> → CAP storage」整条链拉进启动期，
/// 让不配置 CAP 存储的宿主（例如各服务 Testing 环境）直接起不来。
/// </remarks>
public abstract class ReplayIntegrationEventDeadLetterEndpointBase<TRoutes>
    : EndpointWithoutRequest<ResponseData<IntegrationEventDeadLetterReplayResponse>>
    where TRoutes : IIntegrationEventDeadLetterRouteGroup
{
    public override void Configure()
    {
        Post(TRoutes.RoutePrefix + IntegrationEventDeadLetterRoutes.Replay);
        Policies(InternalServiceAuthorizationPolicy.Name);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var replayExecutor = HttpContext.RequestServices.GetRequiredService<IntegrationEventDeadLetterReplayExecutor>();
        var result = await replayExecutor.ReplayAsync(Route<Guid>("deadLetterId"), ct);
        if (result.Status == IntegrationEventDeadLetterReplayStatus.NotFound)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(IntegrationEventDeadLetterEndpointMapper.ToReplayResponse(result).AsResponseData(), ct);
    }
}

/// <inheritdoc cref="ReplayIntegrationEventDeadLetterEndpointBase{TRoutes}"/>
public abstract class ReplayIntegrationEventDeadLettersEndpointBase<TRoutes>
    : Endpoint<ReplayIntegrationEventDeadLetterBatchRequest, ResponseData<IntegrationEventDeadLetterBatchReplayResponse>>
    where TRoutes : IIntegrationEventDeadLetterRouteGroup
{
    public override void Configure()
    {
        Post(TRoutes.RoutePrefix + IntegrationEventDeadLetterRoutes.ReplayBatch);
        Policies(InternalServiceAuthorizationPolicy.Name);
    }

    public override async Task HandleAsync(ReplayIntegrationEventDeadLetterBatchRequest req, CancellationToken ct)
    {
        var replayExecutor = HttpContext.RequestServices.GetRequiredService<IntegrationEventDeadLetterReplayExecutor>();
        var results = await replayExecutor.ReplayBatchAsync(
            IntegrationEventDeadLetterEndpointMapper.QueryFrom(req),
            ct);
        await Send.OkAsync(
            new IntegrationEventDeadLetterBatchReplayResponse(
                results.Select(IntegrationEventDeadLetterEndpointMapper.ToReplayResponse).ToArray()).AsResponseData(),
            ct);
    }
}

public abstract class IgnoreIntegrationEventDeadLetterEndpointBase<TRoutes>(
    IIntegrationEventDeadLetterStore deadLetterStore,
    TimeProvider timeProvider)
    : Endpoint<IgnoreIntegrationEventDeadLetterRequest, ResponseData<IntegrationEventDeadLetterDetailResponse>>
    where TRoutes : IIntegrationEventDeadLetterRouteGroup
{
    public override void Configure()
    {
        Post(TRoutes.RoutePrefix + IntegrationEventDeadLetterRoutes.Ignore);
        Policies(InternalServiceAuthorizationPolicy.Name);
    }

    public override async Task HandleAsync(IgnoreIntegrationEventDeadLetterRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Reason))
        {
            throw new KnownException("忽略原因不能为空。");
        }

        var deadLetterId = Route<Guid>("deadLetterId");
        if (await deadLetterStore.GetAsync(deadLetterId, ct) is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await deadLetterStore.MarkIgnoredAsync(deadLetterId, req.Reason, timeProvider.GetUtcNow(), ct);
        var updated = await deadLetterStore.GetAsync(deadLetterId, ct)
            ?? throw new InvalidOperationException($"Dead-letter message '{deadLetterId}' was not found after ignore.");
        await Send.OkAsync(IntegrationEventDeadLetterEndpointMapper.ToDetailResponse(updated).AsResponseData(), ct);
    }
}

public static class IntegrationEventDeadLetterEndpointMapper
{
    public static IntegrationEventDeadLetterQuery QueryFrom(ListIntegrationEventDeadLettersRequest request) =>
        new(
            request.ConsumerName,
            request.Status,
            request.EventType,
            ParseSkip(request.Skip),
            ParseTake(request.Take),
            request.FailureCode,
            request.DeadLetteredFromUtc,
            request.DeadLetteredToUtc);

    public static IntegrationEventDeadLetterQuery QueryFrom(ReplayIntegrationEventDeadLetterBatchRequest request) =>
        new(
            request.ConsumerName,
            request.Status,
            request.EventType,
            Skip: 0,
            ParseTake(request.Take),
            request.FailureCode,
            request.DeadLetteredFromUtc,
            request.DeadLetteredToUtc);

    public static IntegrationEventDeadLetterResponse ToResponse(IntegrationEventDeadLetterMessage message) =>
        new(
            message.Id,
            message.ConsumerName,
            message.EventId,
            message.EventType,
            message.EventVersion,
            message.SourceService,
            message.IdempotencyKey,
            message.FailureCode,
            message.FailureMessage,
            message.Status,
            message.DeadLetteredAtUtc,
            message.ReplayedAtUtc);

    public static IntegrationEventDeadLetterDetailResponse ToDetailResponse(IntegrationEventDeadLetterMessage message) =>
        new(
            message.Id,
            message.ConsumerName,
            message.EventId,
            message.EventType,
            message.EventVersion,
            message.SourceService,
            message.IdempotencyKey,
            message.EventClrType,
            message.EventJson,
            message.FailureCode,
            message.FailureMessage,
            message.Status,
            message.DeadLetteredAtUtc,
            message.ReplayedAtUtc);

    public static IntegrationEventDeadLetterReplayResponse ToReplayResponse(IntegrationEventDeadLetterReplayResult result) =>
        new(result.Id, result.Succeeded, result.Status, result.Message);

    public static IntegrationEventDeadLetterMetricsResponse ToMetricsResponse(IntegrationEventDeadLetterMetrics metrics) =>
        new(
            metrics.ActionableCount,
            metrics.PendingCount,
            metrics.FailedCount,
            metrics.IgnoredCount,
            metrics.ReplayedCount,
            metrics.EventTypes.Select(ToEventTypeMetricsResponse).ToArray());

    public static int ParseTake(int? take)
    {
        if (take is null)
        {
            return 100;
        }

        return take > 0
            ? take.Value
            : throw new KnownException("死信获取数量必须为正整数。");
    }

    private static IntegrationEventDeadLetterEventTypeMetricsResponse ToEventTypeMetricsResponse(
        IntegrationEventDeadLetterEventTypeMetrics metrics) =>
        new(
            metrics.EventType,
            metrics.ActionableCount,
            metrics.PendingCount,
            metrics.FailedCount,
            metrics.IgnoredCount,
            metrics.ReplayedCount);

    private static int ParseSkip(int? skip)
    {
        if (skip is null)
        {
            return 0;
        }

        return skip >= 0
            ? skip.Value
            : throw new KnownException("死信跳过数量必须为非负整数。");
    }
}
