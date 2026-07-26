using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.ServiceAuth;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Nerv.IIP.Business.Mes.Web.Application.Seed;

/// <summary>
/// 成品 SKU → ProductEngineering 生产版本 id 的解析边界。
/// 抽成接口是为了让 seed 的形状测试不必架起 HTTP 桩——解析路径本身由规模块既有测试覆盖。
/// </summary>
public interface IWorldHistoryProductionVersionResolver
{
    Task<IReadOnlyDictionary<string, string>> ResolveAsync(
        string organizationId,
        string environmentId,
        IReadOnlyCollection<string> skuCodes,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 通过**真实 HTTP 边界**把 L0 的成品 SKU 解析成 ProductEngineering 的生产版本 id。
///
/// 生产版本 id 是 ProductEngineering 侧 DB 生成的 GUID，MES 无法本地推导，
/// 也不允许跨服务查库或建跨 schema 外键——只能走已有的 resolve 端点（与千单规模块同一路径）。
/// 24 个成品各解析一次并缓存，失败按有界重试后直接抛出：宁可启动失败，也不写一批没有工艺版本的工单。
/// </summary>
public sealed class WorldHistoryProductionVersionResolver(
    MesProductEngineeringHttpClient productEngineeringClient,
    IInternalServiceTokenProvider internalTokenProvider) : IWorldHistoryProductionVersionResolver
{
    private const int ResolutionAttempts = 5;
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ResolutionAttemptTimeout = TimeSpan.FromSeconds(5);

    public async Task<IReadOnlyDictionary<string, string>> ResolveAsync(
        string organizationId,
        string environmentId,
        IReadOnlyCollection<string> skuCodes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(skuCodes);
        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);

        // 用上线日解析：L0 的 V1 生产版本自 2026-01-05 生效，历史工单引用的就是它。
        var effectiveDate = WorldHistoryCalendar.GoLiveDate;
        foreach (var skuCode in skuCodes)
        {
            resolved[skuCode] = await ResolveOneAsync(organizationId, environmentId, skuCode, effectiveDate, cancellationToken);
        }

        return resolved;
    }

    private async Task<string> ResolveOneAsync(
        string organizationId,
        string environmentId,
        string skuCode,
        DateOnly effectiveDate,
        CancellationToken cancellationToken)
    {
        var query = $"/api/business/v1/engineering/production-versions/resolve?organizationId={Uri.EscapeDataString(organizationId)}" +
                    $"&environmentId={Uri.EscapeDataString(environmentId)}&skuCode={Uri.EscapeDataString(skuCode)}" +
                    $"&effectiveDate={effectiveDate:yyyy-MM-dd}&lotSize={1m.ToString(CultureInfo.InvariantCulture)}";
        Exception? lastFailure = null;

        for (var attempt = 1; attempt <= ResolutionAttempts; attempt++)
        {
            try
            {
                using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptCancellation.CancelAfter(ResolutionAttemptTimeout);
                using var request = new HttpRequestMessage(HttpMethod.Get, query);
                var token = internalTokenProvider.BearerToken;
                if (!string.IsNullOrWhiteSpace(token))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }

                using var response = await productEngineeringClient.HttpClient.SendAsync(request, attemptCancellation.Token);
                if (response.IsSuccessStatusCode)
                {
                    var envelope = await response.Content
                        .ReadFromJsonAsync<ResponseDataEnvelope<WorldHistoryProductionVersionResponse>>(attemptCancellation.Token);
                    if (envelope?.Success == true && envelope.Data is not null &&
                        !string.IsNullOrWhiteSpace(envelope.Data.ProductionVersionId) &&
                        string.Equals(envelope.Data.SkuCode, skuCode, StringComparison.Ordinal) &&
                        string.Equals(envelope.Data.Status, "active", StringComparison.OrdinalIgnoreCase))
                    {
                        return envelope.Data.ProductionVersionId;
                    }
                }

                lastFailure = new HttpRequestException(
                    $"ProductEngineering production-version resolve for '{skuCode}' returned HTTP {(int)response.StatusCode}.");
            }
            catch (HttpRequestException exception)
            {
                lastFailure = exception;
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                lastFailure = exception;
            }

            if (attempt < ResolutionAttempts)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(InitialRetryDelay.TotalMilliseconds * Math.Pow(2, attempt - 1)),
                    cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"World-history production version for '{skuCode}' did not converge after {ResolutionAttempts} bounded attempts. " +
            "Ensure the L0 world-bible engineering seed (LeaderDemo:World:Enabled) ran in ProductEngineering first.",
            lastFailure);
    }

    private sealed record WorldHistoryProductionVersionResponse(string ProductionVersionId, string SkuCode, string Status);
}
