using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

/// <summary>
/// 一个死信来源：服务标识、该服务的基址与它的死信路由前缀。
/// 前缀不在网关这边写字面量，取自共享清单 <see cref="IntegrationEventDeadLetterServices"/>——
/// 服务上线的路由和网关请求的路由因此是同一个字符串。
/// </summary>
public sealed record BusinessDeadLetterSource(string Name, Uri BaseAddress, string RoutePrefix);

/// <summary>
/// 网关侧的死信来源表。构造时按 <see cref="IntegrationEventDeadLetterServices.All"/> 逐条要求基址，
/// 少一条就在启动期抛——新服务接入共享死信模块却没在网关配基址时，故障出现在启动而不是
/// 某个运维打开页面时少了一个来源。
/// </summary>
public sealed class BusinessDeadLetterSources
{
    private readonly IReadOnlyDictionary<string, BusinessDeadLetterSource> sourcesByName;

    public BusinessDeadLetterSources(IReadOnlyDictionary<string, Uri> baseAddressesByServiceName)
    {
        ArgumentNullException.ThrowIfNull(baseAddressesByServiceName);
        var missing = IntegrationEventDeadLetterServices.All
            .Where(service => !baseAddressesByServiceName.ContainsKey(service.Name))
            .Select(service => service.Name)
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                "BusinessGateway 缺少这些死信来源的基址，运维面会少掉对应服务："
                + string.Join(", ", missing));
        }

        var unknown = baseAddressesByServiceName.Keys
            .Where(name => IntegrationEventDeadLetterServices.All.All(service => service.Name != name))
            .ToArray();
        if (unknown.Length > 0)
        {
            throw new InvalidOperationException(
                "这些服务不在共享死信清单里，网关不应为它们配置死信基址："
                + string.Join(", ", unknown));
        }

        sourcesByName = IntegrationEventDeadLetterServices.All.ToDictionary(
            service => service.Name,
            service => new BusinessDeadLetterSource(
                service.Name,
                baseAddressesByServiceName[service.Name],
                service.RoutePrefix),
            StringComparer.Ordinal);
        All = sourcesByName.Values.OrderBy(source => source.Name, StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<BusinessDeadLetterSource> All { get; }

    public bool TryGet(string? name, out BusinessDeadLetterSource source)
    {
        if (!string.IsNullOrWhiteSpace(name) && sourcesByName.TryGetValue(name, out var found))
        {
            source = found;
            return true;
        }

        source = null!;
        return false;
    }
}

public interface IBusinessDeadLetterClient
{
    Task<IntegrationEventDeadLetterListResponse> ListAsync(
        string internalBearerToken,
        BusinessDeadLetterSource source,
        ListIntegrationEventDeadLettersRequest request,
        CancellationToken cancellationToken);

    Task<IntegrationEventDeadLetterMetricsResponse> GetMetricsAsync(
        string internalBearerToken,
        BusinessDeadLetterSource source,
        CancellationToken cancellationToken);

    Task<IntegrationEventDeadLetterDetailResponse> GetAsync(
        string internalBearerToken,
        BusinessDeadLetterSource source,
        Guid deadLetterId,
        CancellationToken cancellationToken);

    Task<IntegrationEventDeadLetterReplayResponse> ReplayAsync(
        string internalBearerToken,
        BusinessDeadLetterSource source,
        Guid deadLetterId,
        CancellationToken cancellationToken);

    Task<IntegrationEventDeadLetterBatchReplayResponse> ReplayBatchAsync(
        string internalBearerToken,
        BusinessDeadLetterSource source,
        ReplayIntegrationEventDeadLetterBatchRequest request,
        CancellationToken cancellationToken);

    Task<IntegrationEventDeadLetterDetailResponse> IgnoreAsync(
        string internalBearerToken,
        BusinessDeadLetterSource source,
        Guid deadLetterId,
        IgnoreIntegrationEventDeadLetterRequest request,
        CancellationToken cancellationToken);
}

/// <remarks>
/// 这个 client 没有 <c>BaseAddress</c>：同一份死信契约挂在 10 个服务上，按服务各注册一个 typed client
/// 只会把同一段转发代码抄 10 份。基址随 <see cref="BusinessDeadLetterSource"/> 逐次传入，
/// 弹性策略由这一个 named client 统一承担。
/// </remarks>
public sealed class HttpBusinessDeadLetterClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessDeadLetterClient
{
    public Task<IntegrationEventDeadLetterListResponse> ListAsync(
        string internalBearerToken,
        BusinessDeadLetterSource source,
        ListIntegrationEventDeadLettersRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);
        var query = Query(
            ("consumerName", request.ConsumerName),
            ("eventType", request.EventType),
            ("failureCode", request.FailureCode),
            ("status", request.Status),
            ("deadLetteredFromUtc", request.DeadLetteredFromUtc),
            ("deadLetteredToUtc", request.DeadLetteredToUtc),
            ("skip", request.Skip),
            ("take", request.Take));
        return SendAsync<IntegrationEventDeadLetterListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            Url(source, IntegrationEventDeadLetterRoutes.Collection, query),
            body: null,
            cancellationToken);
    }

    public Task<IntegrationEventDeadLetterMetricsResponse> GetMetricsAsync(
        string internalBearerToken,
        BusinessDeadLetterSource source,
        CancellationToken cancellationToken) =>
        SendAsync<IntegrationEventDeadLetterMetricsResponse>(
            internalBearerToken,
            HttpMethod.Get,
            Url(source, IntegrationEventDeadLetterRoutes.Metrics),
            body: null,
            cancellationToken);

    public Task<IntegrationEventDeadLetterDetailResponse> GetAsync(
        string internalBearerToken,
        BusinessDeadLetterSource source,
        Guid deadLetterId,
        CancellationToken cancellationToken) =>
        SendAsync<IntegrationEventDeadLetterDetailResponse>(
            internalBearerToken,
            HttpMethod.Get,
            Url(source, IntegrationEventDeadLetterRoutes.ItemFor(deadLetterId)),
            body: null,
            cancellationToken);

    public Task<IntegrationEventDeadLetterReplayResponse> ReplayAsync(
        string internalBearerToken,
        BusinessDeadLetterSource source,
        Guid deadLetterId,
        CancellationToken cancellationToken) =>
        SendAsync<IntegrationEventDeadLetterReplayResponse>(
            internalBearerToken,
            HttpMethod.Post,
            Url(source, IntegrationEventDeadLetterRoutes.ReplayFor(deadLetterId)),
            body: null,
            cancellationToken);

    public Task<IntegrationEventDeadLetterBatchReplayResponse> ReplayBatchAsync(
        string internalBearerToken,
        BusinessDeadLetterSource source,
        ReplayIntegrationEventDeadLetterBatchRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<IntegrationEventDeadLetterBatchReplayResponse>(
            internalBearerToken,
            HttpMethod.Post,
            Url(source, IntegrationEventDeadLetterRoutes.ReplayBatch),
            request,
            cancellationToken);

    public Task<IntegrationEventDeadLetterDetailResponse> IgnoreAsync(
        string internalBearerToken,
        BusinessDeadLetterSource source,
        Guid deadLetterId,
        IgnoreIntegrationEventDeadLetterRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<IntegrationEventDeadLetterDetailResponse>(
            internalBearerToken,
            HttpMethod.Post,
            Url(source, IntegrationEventDeadLetterRoutes.IgnoreFor(deadLetterId)),
            request,
            cancellationToken);

    private static string Url(BusinessDeadLetterSource source, string routeSuffix, string query = "")
    {
        var path = source.RoutePrefix + routeSuffix;
        return new Uri(source.BaseAddress, string.IsNullOrEmpty(query) ? path : $"{path}?{query}").ToString();
    }
}
