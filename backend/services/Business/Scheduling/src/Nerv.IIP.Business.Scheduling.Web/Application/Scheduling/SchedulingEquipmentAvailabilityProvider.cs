using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

public interface ISchedulingEquipmentAvailabilityProvider
{
    Task<EquipmentRuntimeAvailabilityResponse> QueryAsync(
        SchedulingProblemContract problem,
        CancellationToken cancellationToken);
}

public sealed class NoopSchedulingEquipmentAvailabilityProvider : ISchedulingEquipmentAvailabilityProvider
{
    public Task<EquipmentRuntimeAvailabilityResponse> QueryAsync(
        SchedulingProblemContract problem,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(problem);

        return Task.FromResult(new EquipmentRuntimeAvailabilityResponse(
            ContractVersion: 1,
            OrganizationId: problem.OrganizationId,
            EnvironmentId: problem.EnvironmentId,
            QueryWindowStartUtc: problem.HorizonStartUtc,
            QueryWindowEndUtc: problem.HorizonEndUtc,
            Items: []));
    }
}

public sealed class HttpSchedulingEquipmentAvailabilityProvider(
    IHttpClientFactory httpClientFactory,
    IInternalServiceTokenProvider? internalTokenProvider,
    ILogger<HttpSchedulingEquipmentAvailabilityProvider> logger)
    : ISchedulingEquipmentAvailabilityProvider
{
    public const string IndustrialTelemetryClientName = "SchedulingIndustrialTelemetryAvailability";
    public const string MaintenanceClientName = "SchedulingMaintenanceAvailability";
    public const string SourceUnavailableReasonCode = "equipment.availabilitySourceUnavailable";
    public const int MaxAvailabilityQueryIdsPerBatch = 50;
    public const int MaxConcurrentAvailabilityQueries = 8;

    private static readonly SemaphoreSlim DownstreamQueryGate = new(MaxConcurrentAvailabilityQueries);

    public async Task<EquipmentRuntimeAvailabilityResponse> QueryAsync(
        SchedulingProblemContract problem,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(problem);

        var batches = CreateBatches(problem).ToArray();
        if (batches.Length == 0)
        {
            return MergeResponses(
                problem,
                [
                    SourceUnavailable(problem, IndustrialTelemetryClientName),
                    SourceUnavailable(problem, MaintenanceClientName)
                ]);
        }

        var requests = batches.SelectMany((batch, index) =>
        {
            var batchProblem = problem with { Resources = batch };
            var requestPath = BuildAvailabilityPath(batchProblem);
            var batchNumber = index + 1;

            return new[]
            {
                QueryDownstreamAsync(
                    IndustrialTelemetryClientName,
                    "/api/business/v1/iiot/runtime-availability",
                    requestPath,
                    batchProblem,
                    batchNumber,
                    batches.Length,
                    cancellationToken),
                QueryDownstreamAsync(
                    MaintenanceClientName,
                    "/api/business/v1/maintenance/availability-windows",
                    requestPath,
                    batchProblem,
                    batchNumber,
                    batches.Length,
                    cancellationToken)
            };
        });

        var responses = await Task.WhenAll(requests);
        return MergeResponses(problem, responses);
    }

    private static EquipmentRuntimeAvailabilityResponse MergeResponses(
        SchedulingProblemContract problem,
        IEnumerable<EquipmentRuntimeAvailabilityResponse> responses)
    {
        return new EquipmentRuntimeAvailabilityResponse(
            ContractVersion: 1,
            OrganizationId: problem.OrganizationId,
            EnvironmentId: problem.EnvironmentId,
            QueryWindowStartUtc: problem.HorizonStartUtc,
            QueryWindowEndUtc: problem.HorizonEndUtc,
            Items: responses.SelectMany(x => x.Items)
                .OrderBy(x => x.DeviceAssetId, StringComparer.Ordinal)
                .ThenBy(x => x.WorkCenterId, StringComparer.Ordinal)
                .ThenBy(x => x.StartUtc)
                .ThenBy(x => x.EndUtc)
                .ThenBy(x => x.ReasonCode, StringComparer.Ordinal)
                .ToArray());
    }

    private async Task<EquipmentRuntimeAvailabilityResponse> QueryDownstreamAsync(
        string clientName,
        string route,
        string query,
        SchedulingProblemContract problem,
        int batchNumber,
        int batchCount,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{route}?{query}");
        var bearerToken = internalTokenProvider?.BearerToken;
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        await DownstreamQueryGate.WaitAsync(cancellationToken);
        try
        {
            var client = httpClientFactory.CreateClient(clientName);
            using var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<EquipmentRuntimeAvailabilityResponse>>(
                EquipmentRuntimeJson.Options,
                cancellationToken);
            return envelope?.Data ?? SourceUnavailable(problem, clientName);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(
                exception,
                "Scheduling equipment availability source {Source} was unavailable for problem {ProblemId} batch {BatchNumber}/{BatchCount} ({ResourceCount} resources).",
                clientName,
                problem.ProblemId,
                batchNumber,
                batchCount,
                problem.Resources.Count);
            return SourceUnavailable(problem, clientName);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                exception,
                "Scheduling equipment availability source {Source} timed out for problem {ProblemId} batch {BatchNumber}/{BatchCount} ({ResourceCount} resources).",
                clientName,
                problem.ProblemId,
                batchNumber,
                batchCount,
                problem.Resources.Count);
            return SourceUnavailable(problem, clientName);
        }
        finally
        {
            DownstreamQueryGate.Release();
        }
    }

    private static string BuildAvailabilityPath(SchedulingProblemContract problem)
    {
        // P0 downstream availability endpoints require explicit device ids and reject direct work-center filtering.
        // Work center ids stay on the batch resource slice so fail-closed windows keep their scheduling context.
        var deviceAssetIds = string.Join(',', problem.Resources
            .Select(x => x.ResourceId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal));

        return Query(
            ("organizationId", problem.OrganizationId),
            ("environmentId", problem.EnvironmentId),
            ("windowStartUtc", problem.HorizonStartUtc),
            ("windowEndUtc", problem.HorizonEndUtc),
            ("deviceAssetIds", deviceAssetIds));
    }

    private static IReadOnlyCollection<SchedulingResourceContract>[] CreateBatches(SchedulingProblemContract problem)
    {
        var resources = problem.Resources
            .Where(x => !string.IsNullOrWhiteSpace(x.ResourceId))
            .GroupBy(x => $"{x.ResourceId}\u001F{x.WorkCenterId}", StringComparer.Ordinal)
            .Select(x => x.First())
            .ToArray();
        var deviceAssetIds = resources
            .Select(x => x.ResourceId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return deviceAssetIds
            .Chunk(MaxAvailabilityQueryIdsPerBatch)
            .Select(chunk =>
            {
                var deviceAssetIdSet = chunk.ToHashSet(StringComparer.Ordinal);
                return (IReadOnlyCollection<SchedulingResourceContract>)resources
                    .Where(x => deviceAssetIdSet.Contains(x.ResourceId))
                    .OrderBy(x => x.ResourceId, StringComparer.Ordinal)
                    .ThenBy(x => x.WorkCenterId, StringComparer.Ordinal)
                    .ToArray();
            })
            .ToArray();
    }

    private static string Query(params (string Name, object? Value)[] values)
    {
        var pairs = values
            .Where(x => x.Value is not null && !string.IsNullOrWhiteSpace(Convert.ToString(x.Value, CultureInfo.InvariantCulture)))
            .Select(x => $"{Uri.EscapeDataString(x.Name)}={Uri.EscapeDataString(FormatValue(x.Value!))}");
        return string.Join('&', pairs);
    }

    private static string FormatValue(object value) => value switch
    {
        DateTimeOffset dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
    };

    private static EquipmentRuntimeAvailabilityResponse SourceUnavailable(
        SchedulingProblemContract problem,
        string sourceName)
    {
        var items = problem.Resources
            .GroupBy(x => $"{x.ResourceId}\u001F{x.WorkCenterId}", StringComparer.Ordinal)
            .Select(x => x.First())
            .Select(resource => new EquipmentRuntimeAvailabilityWindowContract(
                DeviceAssetId: resource.ResourceId,
                WorkCenterId: resource.WorkCenterId,
                AvailabilityStatus: EquipmentRuntimeAvailabilityStatus.Unknown,
                ReasonCode: SourceUnavailableReasonCode,
                Severity: EquipmentRuntimeSeverity.Blocked,
                StartUtc: problem.HorizonStartUtc,
                EndUtc: problem.HorizonEndUtc,
                SourceType: EquipmentRuntimeSourceType.StaleSource,
                SourceReferenceId: sourceName,
                MessageKey: "equipment.availability.sourceUnavailable",
                SubstituteDeviceAssetIds: []))
            .ToArray();

        return new EquipmentRuntimeAvailabilityResponse(
            ContractVersion: 1,
            OrganizationId: problem.OrganizationId,
            EnvironmentId: problem.EnvironmentId,
            QueryWindowStartUtc: problem.HorizonStartUtc,
            QueryWindowEndUtc: problem.HorizonEndUtc,
            Items: items);
    }

    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
}
