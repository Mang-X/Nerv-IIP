using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

public interface ISchedulingMaterialReadinessProvider
{
    Task<IReadOnlyCollection<SchedulingMaterialReadinessContract>> QueryAsync(
        SchedulingProblemContract problem,
        CancellationToken cancellationToken);
}

public sealed class NoopSchedulingMaterialReadinessProvider : ISchedulingMaterialReadinessProvider
{
    public Task<IReadOnlyCollection<SchedulingMaterialReadinessContract>> QueryAsync(
        SchedulingProblemContract problem,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(problem);
        return Task.FromResult<IReadOnlyCollection<SchedulingMaterialReadinessContract>>([]);
    }
}

public sealed class HttpSchedulingMaterialReadinessProvider(
    IHttpClientFactory httpClientFactory,
    IInternalServiceTokenProvider? internalTokenProvider,
    ILogger<HttpSchedulingMaterialReadinessProvider> logger)
    : ISchedulingMaterialReadinessProvider
{
    public const string MesClientName = "SchedulingMesMaterialReadiness";
    public const string SourceUnavailableReasonCode = "mes.materialReadinessSourceUnavailable";
    private const int MaxConcurrentMesReadinessRequests = 8;

    public async Task<IReadOnlyCollection<SchedulingMaterialReadinessContract>> QueryAsync(
        SchedulingProblemContract problem,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(problem);

        var workOrderIds = problem.Orders
            .Select(x => x.OrderId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (workOrderIds.Length == 0)
        {
            return [];
        }

        using var throttler = new SemaphoreSlim(MaxConcurrentMesReadinessRequests);
        var readiness = await Task.WhenAll(workOrderIds.Select(async workOrderId =>
        {
            await throttler.WaitAsync(cancellationToken);
            try
            {
                return await QueryWorkOrderAsync(problem, workOrderId, cancellationToken);
            }
            finally
            {
                throttler.Release();
            }
        }));
        return readiness
            .SelectMany(x => x)
            .OrderBy(x => x.ScopeType, StringComparer.Ordinal)
            .ThenBy(x => x.ScopeId, StringComparer.Ordinal)
            .ThenBy(x => x.MaterialReadyUtc)
            .ThenBy(x => string.Join('|', x.ReasonCodes), StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<IReadOnlyCollection<SchedulingMaterialReadinessContract>> QueryWorkOrderAsync(
        SchedulingProblemContract problem,
        string workOrderId,
        CancellationToken cancellationToken)
    {
        var query = Query(
            ("organizationId", problem.OrganizationId),
            ("environmentId", problem.EnvironmentId));
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/business/v1/mes/work-orders/{Uri.EscapeDataString(workOrderId)}/material-readiness?{query}");
        var bearerToken = internalTokenProvider?.BearerToken;
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        try
        {
            var client = httpClientFactory.CreateClient(MesClientName);
            using var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<MesMaterialReadinessResponse>>(
                SchedulingJson.Options,
                cancellationToken);
            return ToSchedulingReadiness(envelope?.Data, workOrderId);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(
                exception,
                "Scheduling material readiness source MES was unavailable for problem {ProblemId}, work order {WorkOrderId}.",
                problem.ProblemId,
                workOrderId);
            return SourceUnavailable(workOrderId);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                exception,
                "Scheduling material readiness source MES timed out for problem {ProblemId}, work order {WorkOrderId}.",
                problem.ProblemId,
                workOrderId);
            return SourceUnavailable(workOrderId);
        }
    }

    private static IReadOnlyCollection<SchedulingMaterialReadinessContract> ToSchedulingReadiness(
        MesMaterialReadinessResponse? response,
        string workOrderId)
    {
        if (response is null)
        {
            return SourceUnavailable(workOrderId);
        }

        if (string.Equals(response.ReadinessStatus, "Ready", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var reasonCodes = response.BlockingReasons.Count == 0
            ? response.Items
                .Where(x => x.ShortageQuantity > 0)
                .Select(x => string.IsNullOrWhiteSpace(x.MaterialLotId)
                    ? $"{x.MaterialId} shortage {x.ShortageQuantity:0.######}"
                    : $"{x.MaterialId} {x.MaterialLotId} shortage {x.ShortageQuantity:0.######}")
                .ToArray()
            : response.BlockingReasons;

        return
        [
            new SchedulingMaterialReadinessContract(
                ScopeType: "order",
                ScopeId: response.WorkOrderId,
                MaterialReadyUtc: null,
                IsReady: false,
                ReasonCodes: reasonCodes)
        ];
    }

    private static IReadOnlyCollection<SchedulingMaterialReadinessContract> SourceUnavailable(string workOrderId)
    {
        return
        [
            new SchedulingMaterialReadinessContract(
                ScopeType: "order",
                ScopeId: workOrderId,
                MaterialReadyUtc: null,
                IsReady: false,
                ReasonCodes: [SourceUnavailableReasonCode])
        ];
    }

    private static string Query(params (string Name, object? Value)[] values)
    {
        var pairs = values
            .Where(x => x.Value is not null && !string.IsNullOrWhiteSpace(Convert.ToString(x.Value, CultureInfo.InvariantCulture)))
            .Select(x => $"{Uri.EscapeDataString(x.Name)}={Uri.EscapeDataString(Convert.ToString(x.Value, CultureInfo.InvariantCulture) ?? string.Empty)}");
        return string.Join('&', pairs);
    }

    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);

    private sealed record MesMaterialReadinessResponse(
        string WorkOrderId,
        string ReadinessStatus,
        IReadOnlyCollection<string> BlockingReasons,
        IReadOnlyCollection<MesMaterialReadinessRow> Items);

    private sealed record MesMaterialReadinessRow(
        string MaterialId,
        string? MaterialLotId,
        decimal RequiredQuantity,
        decimal AvailableQuantity,
        decimal RequestedQuantity,
        decimal StagedQuantity,
        decimal ReceivedQuantity,
        decimal ShortageQuantity,
        string Status);
}

public static class MaterialReadinessSchedulingAdapter
{
    public static SchedulingProblemContract Apply(
        SchedulingProblemContract problem,
        IReadOnlyCollection<SchedulingMaterialReadinessContract> materialReadiness)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(materialReadiness);
        if (materialReadiness.Count == 0)
        {
            return problem;
        }

        return problem with
        {
            MaterialReadiness = problem.MaterialReadiness
                .Concat(materialReadiness)
                .GroupBy(x => (
                    ScopeType: x.ScopeType.Trim().ToLowerInvariant(),
                    ScopeId: x.ScopeId.Trim(),
                    x.MaterialReadyUtc,
                    x.IsReady,
                    ReasonCodes: string.Join('|', x.ReasonCodes.Order(StringComparer.Ordinal))))
                .Select(x => x.First())
                .OrderBy(x => x.ScopeType, StringComparer.Ordinal)
                .ThenBy(x => x.ScopeId, StringComparer.Ordinal)
                .ThenBy(x => x.MaterialReadyUtc)
                .ThenBy(x => string.Join('|', x.ReasonCodes), StringComparer.Ordinal)
                .ToArray()
        };
    }
}
