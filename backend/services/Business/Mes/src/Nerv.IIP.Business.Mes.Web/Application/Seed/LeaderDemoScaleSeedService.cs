using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Mes.Web.Application.Seed;

/// <summary>
/// MAN-519 白名单内的领导演示「规模块」MES 前置事实：批量已下达工单及其 4 道有前后置关系的工序任务，
/// 使排产工作台的「批量生成」能真实吃到千单级待排工单。
/// 只写「工单下达」这一前置事实，不产生报工、检验、完工入库等结果事实；使用独立 <c>WO-SCALE-#####</c>
/// 号段，绝不触碰 <c>WO-DEMO-Q01</c> 等固定演示事实。批量写入走 <c>SaveChangesAsync</c>（不派发领域事件），
/// 避免千单级 seed 触发下游事件风暴。
/// </summary>
public sealed class LeaderDemoScaleSeedService(
    ApplicationDbContext dbContext,
    MesProductEngineeringHttpClient productEngineeringClient,
    IInternalServiceTokenProvider internalTokenProvider)
{
    public const int BatchSize = 100;
    private const int ResolutionAttempts = 5;
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ResolutionAttemptTimeout = TimeSpan.FromSeconds(5);

    public async Task SeedAsync(
        string organizationId,
        string environmentId,
        int orderCount,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (orderCount <= 0)
        {
            return;
        }

        var anchorDate = DateOnly.FromDateTime(nowUtc.UtcDateTime.Date);
        var earliestStartUtc = new DateTimeOffset(anchorDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var productionVersionBySku = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var skuCode in LeaderDemoScaleSpec.FinishedSkuCodes)
        {
            productionVersionBySku[skuCode] = await ResolveProductionVersionIdAsync(
                organizationId, environmentId, skuCode, anchorDate, cancellationToken);
        }

        for (var batchStart = 1; batchStart <= orderCount; batchStart += BatchSize)
        {
            var batchEnd = Math.Min(batchStart + BatchSize - 1, orderCount);
            var workOrderIds = Enumerable.Range(batchStart, batchEnd - batchStart + 1)
                .Select(LeaderDemoScaleSpec.WorkOrderId)
                .ToArray();
            var existing = await dbContext.WorkOrders
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    workOrderIds.Contains(x.WorkOrderIdValue))
                .Select(x => x.WorkOrderIdValue)
                .ToArrayAsync(cancellationToken);
            var existingSet = existing.ToHashSet(StringComparer.Ordinal);

            var added = 0;
            for (var index = batchStart; index <= batchEnd; index++)
            {
                var workOrderId = LeaderDemoScaleSpec.WorkOrderId(index);
                if (existingSet.Contains(workOrderId))
                {
                    continue;
                }

                var skuCode = LeaderDemoScaleSpec.SkuCode(index);
                var dueUtc = new DateTimeOffset(
                    anchorDate.AddDays(LeaderDemoScaleSpec.DueDayOffset(index)).ToDateTime(new TimeOnly(18, 0)),
                    TimeSpan.Zero);
                var workOrder = WorkOrder.Create(
                    organizationId,
                    environmentId,
                    workOrderId,
                    skuCode,
                    productionVersionBySku[skuCode],
                    LeaderDemoScaleSpec.Quantity(index),
                    LeaderDemoScaleSpec.Priority(index),
                    dueUtc,
                    "pcs");
                var operations = workOrder.Release(
                    earliestStartUtc,
                    LeaderDemoScaleSpec.Stages
                        .Select(stage => new RoutingStepSnapshot(
                            LeaderDemoScaleSpec.OperationTaskId(index, stage),
                            stage.Sequence,
                            stage.WorkCenterCode,
                            [],
                            LeaderDemoScaleSpec.Duration(index, stage),
                            false,
                            stage.OperationCode))
                        .ToArray());
                dbContext.WorkOrders.Add(workOrder);
                dbContext.OperationTasks.AddRange(operations);
                added++;
            }

            if (added > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            dbContext.ChangeTracker.Clear();
        }
    }

    private async Task<string> ResolveProductionVersionIdAsync(
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
                        .ReadFromJsonAsync<ResponseDataEnvelope<ScaleProductionVersionResponse>>(attemptCancellation.Token);
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
            $"ProductEngineering leader-demo scale production version for '{skuCode}' did not converge after {ResolutionAttempts} bounded attempts.",
            lastFailure);
    }

    private sealed record ScaleProductionVersionResponse(string ProductionVersionId, string SkuCode, string Status);
}
