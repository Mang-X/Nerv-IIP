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

    public async Task<EquipmentRuntimeAvailabilityResponse> QueryAsync(
        SchedulingProblemContract problem,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(problem);

        var requestPath = BuildAvailabilityPath(problem);
        var industrialTelemetry = QueryDownstreamAsync(
            IndustrialTelemetryClientName,
            "/api/business/v1/iiot/runtime-availability",
            requestPath,
            problem,
            cancellationToken);
        var maintenance = QueryDownstreamAsync(
            MaintenanceClientName,
            "/api/business/v1/maintenance/availability-windows",
            requestPath,
            problem,
            cancellationToken);

        var responses = await Task.WhenAll(industrialTelemetry, maintenance);
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
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{route}?{query}");
        var bearerToken = internalTokenProvider?.BearerToken;
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

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
                "Scheduling equipment availability source {Source} was unavailable for problem {ProblemId}.",
                clientName,
                problem.ProblemId);
            return SourceUnavailable(problem, clientName);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                exception,
                "Scheduling equipment availability source {Source} timed out for problem {ProblemId}.",
                clientName,
                problem.ProblemId);
            return SourceUnavailable(problem, clientName);
        }
    }

    private static string BuildAvailabilityPath(SchedulingProblemContract problem)
    {
        var deviceAssetIds = string.Join(',', problem.Resources
            .Select(x => x.ResourceId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal));
        var workCenterIds = string.Join(',', problem.Resources
            .Select(x => x.WorkCenterId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal));

        return Query(
            ("organizationId", problem.OrganizationId),
            ("environmentId", problem.EnvironmentId),
            ("windowStartUtc", problem.HorizonStartUtc),
            ("windowEndUtc", problem.HorizonEndUtc),
            ("deviceAssetIds", deviceAssetIds),
            ("workCenterIds", workCenterIds));
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
