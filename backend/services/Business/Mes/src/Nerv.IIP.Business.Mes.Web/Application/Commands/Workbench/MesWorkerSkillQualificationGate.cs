using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;

public interface IMesWorkerSkillQualificationGate
{
    Task EnsureQualifiedAsync(
        string organizationId,
        string environmentId,
        string? assignedUserId,
        string? requiredSkillCode,
        CancellationToken cancellationToken);
}

internal sealed class UnconfiguredMesWorkerSkillQualificationGate : IMesWorkerSkillQualificationGate
{
    internal static readonly UnconfiguredMesWorkerSkillQualificationGate Instance = new();

    public Task EnsureQualifiedAsync(
        string organizationId,
        string environmentId,
        string? assignedUserId,
        string? requiredSkillCode,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.IsNullOrWhiteSpace(requiredSkillCode))
        {
            throw new KnownException(
                "WORKER_SKILL_SOURCE_UNAVAILABLE: MES 人员资格门禁未配置，不能校验所需技能。");
        }

        return Task.CompletedTask;
    }
}

public sealed class HttpMesWorkerSkillQualificationGate(
    MesMasterDataHttpClient masterDataClient,
    IInternalServiceTokenProvider internalTokenProvider)
    : IMesWorkerSkillQualificationGate
{
    private const string SourceUnavailable =
        "WORKER_SKILL_SOURCE_UNAVAILABLE: MasterData 人员资格来源暂不可用。";

    public async Task EnsureQualifiedAsync(
        string organizationId,
        string environmentId,
        string? assignedUserId,
        string? requiredSkillCode,
        CancellationToken cancellationToken)
    {
        var skillCode = Normalize(requiredSkillCode);
        if (skillCode is null)
        {
            return;
        }

        var userId = Normalize(assignedUserId);
        if (userId is null)
        {
            throw new KnownException($"工序要求技能 '{skillCode}'，必须先指派人员。");
        }

        var requestUri = "/api/business/v1/master-data/workers?" + string.Join(
            '&',
            Pair("organizationId", organizationId),
            Pair("environmentId", environmentId),
            Pair("userId", userId),
            Pair("skillCode", skillCode),
            "includeDisabled=true",
            "pageIndex=1",
            "pageSize=2");

        WorkerDirectoryResponse response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            if (!string.IsNullOrWhiteSpace(internalTokenProvider.BearerToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    internalTokenProvider.BearerToken);
            }

            using var httpResponse = await masterDataClient.HttpClient.SendAsync(request, cancellationToken);
            if (!httpResponse.IsSuccessStatusCode)
            {
                throw new KnownException(SourceUnavailable);
            }

            var envelope = await httpResponse.Content.ReadFromJsonAsync<ResponseDataEnvelope<WorkerDirectoryResponse>>(
                cancellationToken);
            response = envelope is { Success: true, Data: not null }
                ? envelope.Data
                : throw new KnownException(SourceUnavailable);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (KnownException)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            throw new KnownException(SourceUnavailable, exception);
        }
        catch (TaskCanceledException exception)
        {
            throw new KnownException(SourceUnavailable, exception);
        }
        catch (JsonException exception)
        {
            throw new KnownException(SourceUnavailable, exception);
        }
        catch (NotSupportedException exception)
        {
            throw new KnownException(SourceUnavailable, exception);
        }

        if (response.PageIndex != 1 || response.PageSize != 2)
        {
            throw new KnownException(SourceUnavailable);
        }

        if (response.TotalCount == 0 && response.Items is { Count: 0 })
        {
            throw Unqualified(userId, skillCode);
        }

        if (response.TotalCount != 1 || response.Items is not { Count: 1 })
        {
            throw new KnownException(SourceUnavailable);
        }

        var worker = response.Items.Single();
        if (!string.Equals(worker.UserId, userId, StringComparison.Ordinal))
        {
            throw new KnownException(SourceUnavailable);
        }

        if (worker.Active is null || string.IsNullOrWhiteSpace(worker.EmploymentStatus))
        {
            throw new KnownException(SourceUnavailable);
        }

        if (!worker.Active.Value)
        {
            throw new KnownException($"人员 '{userId}' 已停用，不能派工或开工。");
        }

        if (!string.Equals(worker.EmploymentStatus, "active", StringComparison.Ordinal))
        {
            throw new KnownException($"人员 '{userId}' 当前不是在职状态，不能派工或开工。");
        }

        if (worker.Skills is null || worker.Skills.Any(x =>
                string.IsNullOrWhiteSpace(x.SkillCode) || string.IsNullOrWhiteSpace(x.Level)))
        {
            throw new KnownException(SourceUnavailable);
        }

        var matchingSkills = worker.Skills
            .Where(x => string.Equals(x.SkillCode, skillCode, StringComparison.Ordinal))
            .ToArray();
        if (matchingSkills.Length == 0)
        {
            throw Unqualified(userId, skillCode);
        }

        if (matchingSkills.Length != 1 || string.IsNullOrWhiteSpace(matchingSkills[0].Level))
        {
            throw new KnownException(SourceUnavailable);
        }
    }

    private static KnownException Unqualified(string userId, string skillCode) =>
        new($"人员 '{userId}' 未具备工序所需的有效技能 '{skillCode}'（技能缺失、登记停用、尚未生效或已过期）。");

    private static string Pair(string name, string value) =>
        $"{name}={Uri.EscapeDataString(value.Trim())}";

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record WorkerDirectoryResponse(
        IReadOnlyCollection<WorkerDirectoryItem>? Items,
        int TotalCount,
        int PageIndex,
        int PageSize);

    private sealed record WorkerDirectoryItem(
        string? UserId,
        string? EmploymentStatus,
        bool? Active,
        IReadOnlyCollection<WorkerSkillItem>? Skills);

    private sealed record WorkerSkillItem(string? SkillCode, string? Level);
}
