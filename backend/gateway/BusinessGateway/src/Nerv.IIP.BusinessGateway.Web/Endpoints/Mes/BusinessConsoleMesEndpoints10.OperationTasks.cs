using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.BusinessGateway.Web.Endpoints;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Mes;

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/dispatch-tasks")]
[BusinessGatewayOperationId("listBusinessConsoleMesDispatchTasks")]
public sealed class ListBusinessConsoleMesDispatchTasksEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesDispatchTaskListRequest, BusinessConsoleMesDispatchTaskListResponse>(
        auth,
        BusinessGatewayPermissions.MesDispatchRead)
{
    protected override string OrganizationId(BusinessConsoleMesDispatchTaskListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesDispatchTaskListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesDispatchTaskListResponse> ForwardAsync(
        BusinessConsoleMesDispatchTaskListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ListDispatchTasksAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/dispatch-tasks/{operationTaskId}/assign")]
[BusinessGatewayOperationId("assignBusinessConsoleMesDispatchTask")]
public sealed class AssignBusinessConsoleMesDispatchTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesAssignDispatchTaskRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesDispatchManage)
{
    protected override string OrganizationId(BusinessConsoleMesAssignDispatchTaskRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesAssignDispatchTaskRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesAssignDispatchTaskRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Every assignee/participant must be a registered, on-duty worker. Resolving all names here keeps
        // collaboration snapshots trustworthy and stops arbitrary caller-provided identities from reaching MES.
        var workerIds = new[] { request.AssignedUserId }
            .Concat(request.Participants?.Select(x => x.WorkerId) ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(x => x!)
            .ToArray();
        var resolvedWorkers = await Task.WhenAll(workerIds.Select(ResolveWorkerAsync));
        var workers = resolvedWorkers.ToDictionary(x => x.WorkerId, x => x.Worker, StringComparer.OrdinalIgnoreCase);

        async Task<(string WorkerId, BusinessConsoleWorkerDirectoryItem Worker)> ResolveWorkerAsync(string workerId)
        {
            var directory = await masterData.ListWorkersAsync(
                tokenProvider.BearerToken,
                new BusinessConsoleWorkerDirectoryRequest(
                    request.OrganizationId,
                    request.EnvironmentId,
                    UserId: workerId,
                    PageIndex: 1,
                    PageSize: 1),
                cancellationToken);
            var worker = directory.Items.FirstOrDefault();
            if (worker is null)
            {
                throw BusinessServiceProxyException.FromLocalRejection(System.Net.HttpStatusCode.BadRequest, "未找到员工，无法派工。");
            }

            if (!worker.Active || !string.Equals(worker.EmploymentStatus, "active", StringComparison.Ordinal))
            {
                throw BusinessServiceProxyException.FromLocalRejection(System.Net.HttpStatusCode.BadRequest, "员工当前不在岗，无法派工。");
            }

            return (workerId, worker);
        }

        string? assignedUserName = null;
        string? teamId = null;
        string? teamName = null;
        if (!string.IsNullOrWhiteSpace(request.AssignedUserId))
        {
            var worker = workers[request.AssignedUserId];
            assignedUserName = worker.DisplayName;

            // 班组随派工落快照，口径与 assignedUserName 一致：由网关从主数据解析，不信调用方传入。
            // 一名工人可能挂多个班组，取其带班的那个，没有带班关系则取首个。
            var team = worker.Teams.FirstOrDefault(x => x.IsLeader) ?? worker.Teams.FirstOrDefault();
            teamId = team?.TeamCode;
            teamName = team?.TeamName;
        }
        var participants = request.Participants?.Select(participant =>
            new BusinessConsoleMesDispatchParticipantForwardInput(
                participant.WorkerId,
                workers[participant.WorkerId].DisplayName,
                participant.SharePercent)).ToArray();

        return await mes.AssignDispatchTaskAsync(
            tokenProvider.BearerToken,
            request.OperationTaskId,
            new BusinessConsoleMesAssignDispatchTaskForwardRequest(
                request.OrganizationId,
                request.EnvironmentId,
                request.AssignedUserId,
                assignedUserName,
                request.DeviceAssetId,
                request.ShiftId,
                teamId,
                teamName,
                participants),
            RequireAuthorizedPrincipalActorReference(),
            cancellationToken);
    }
}

public sealed class BusinessConsoleMesAssignDispatchTaskRequestValidator
    : Validator<BusinessConsoleMesAssignDispatchTaskRequest>
{
    public BusinessConsoleMesAssignDispatchTaskRequestValidator()
    {
        RuleFor(x => x.Participants).Must(participants => participants is null || participants.Count is > 0 and <= 20)
            .WithMessage("提供参与者列表时必须包含 1 至 20 人。");
        RuleForEach(x => x.Participants).ChildRules(participant =>
        {
            participant.RuleFor(x => x.WorkerId)
                .NotEmpty().WithMessage("参与者人员 ID 不能为空。")
                .MaximumLength(100).WithMessage("参与者人员 ID 长度不能超过 100 个字符。");
            participant.RuleFor(x => x.SharePercent)
                .GreaterThan(0m).WithMessage("工时占比必须大于 0。")
                .LessThanOrEqualTo(100m).WithMessage("工时占比不能超过 100。")
                .Must(HasPersistableSharePrecision)
                .WithMessage("工时占比最多保留四位小数。");
        });
        RuleFor(x => x.Participants).Must(HaveUniqueWorkersAndBalancedShares)
            .WithMessage("工序参与者必须唯一，且工时占比合计必须为 100%。");
    }

    private static bool HaveUniqueWorkersAndBalancedShares(
        IReadOnlyCollection<BusinessConsoleMesDispatchParticipantRequest>? participants)
    {
        if (participants is null || participants.Count == 0)
        {
            return true;
        }

        return participants.All(x => !string.IsNullOrWhiteSpace(x.WorkerId)) &&
            participants.Select(x => x.WorkerId.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == participants.Count &&
            participants.Sum(x => x.SharePercent) == 100m;
    }

    private static bool HasPersistableSharePrecision(decimal sharePercent) =>
        decimal.Round(sharePercent, 4) == sharePercent;
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/operation-tasks/{operationTaskId}/claim")]
[BusinessGatewayOperationId("claimBusinessConsoleMesOperationTask")]
public sealed class ClaimBusinessConsoleMesOperationTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IBusinessMasterDataClient masterData,
    PrincipalWorkScopeResolver workScopeResolver,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesClaimOperationTaskRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesOperationsManage)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleMesClaimOperationTaskRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesClaimOperationTaskRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesClaimOperationTaskRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var scopedRequest = await ListBusinessConsoleMesOperationTasksEndpoint.ResolveRequestAsync(
            workScopeResolver,
            AuthorizationResult,
            new BusinessConsoleMesOperationTaskListRequest(
                request.OrganizationId,
                request.EnvironmentId,
                Status: "Queued",
                Skip: 0,
                Take: 2,
                ScopeKind: request.ScopeKind,
                ScopeId: request.ScopeId,
                OperationTaskId: request.OperationTaskId),
            BusinessGatewayPermissions.MesOperationsManage,
            cancellationToken);
        var tasks = await mes.ListOperationTasksAsync(tokenProvider.BearerToken, scopedRequest, cancellationToken);
        var task = tasks.Items.SingleOrDefault(x =>
            string.Equals(x.OperationTaskId, request.OperationTaskId, StringComparison.Ordinal));
        if (task is null)
        {
            throw BusinessServiceProxyException.FromLocalRejection(System.Net.HttpStatusCode.Forbidden, BusinessGatewayLocalRejectionStableWireCodes.OperationTaskOutsideWorkCenter);
        }

        var principalId = RequireAuthorizedPrincipalId();
        var directory = await masterData.ListWorkersAsync(
            tokenProvider.BearerToken,
            new BusinessConsoleWorkerDirectoryRequest(
                request.OrganizationId,
                request.EnvironmentId,
                UserId: principalId,
                WorkCenterCode: task.WorkCenterId,
                EmploymentStatus: "active",
                PageIndex: 1,
                PageSize: 2),
            cancellationToken);
        var worker = directory.Items.SingleOrDefault();
        if (worker is null || !worker.Active)
        {
            throw BusinessServiceProxyException.FromLocalRejection(System.Net.HttpStatusCode.Forbidden, BusinessGatewayLocalRejectionStableWireCodes.WorkerNotOnDutyAtWorkCenter);
        }

        var team = worker.Teams.FirstOrDefault(x => x.IsLeader) ?? worker.Teams.FirstOrDefault();
        return await mes.ClaimDispatchTaskAsync(
            tokenProvider.BearerToken,
            request.OperationTaskId,
            new BusinessConsoleMesClaimDispatchTaskForwardRequest(
                request.OrganizationId,
                request.EnvironmentId,
                principalId,
                worker.DisplayName,
                task.DeviceAssetId,
                task.ShiftId,
                request.IdempotencyKey,
                team?.TeamCode,
                team?.TeamName),
            RequireAuthorizedPrincipalActorReference(),
            cancellationToken);
    }
}

public sealed class BusinessConsoleMesClaimOperationTaskRequestValidator
    : Validator<BusinessConsoleMesClaimOperationTaskRequest>
{
    public BusinessConsoleMesClaimOperationTaskRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.OperationTaskId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ScopeKind).Equal("work-center");
        RuleFor(x => x.ScopeId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
    }
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/operation-tasks")]
[BusinessGatewayOperationId("listBusinessConsoleMesOperationTasks")]
public sealed class ListBusinessConsoleMesOperationTasksEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    PrincipalWorkScopeResolver workScopeResolver,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesOperationTaskListRequest, BusinessConsoleMesOperationTaskListResponse>(
        auth,
        BusinessGatewayPermissions.MesOperationsRead)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleMesOperationTaskListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesOperationTaskListRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleMesOperationTaskListResponse> ForwardAsync(
        BusinessConsoleMesOperationTaskListRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var scopedRequest = await ResolveRequestAsync(
            workScopeResolver,
            AuthorizationResult,
            request,
            BusinessGatewayPermissions.MesOperationsRead,
            cancellationToken);
        return await mes.ListOperationTasksAsync(tokenProvider.BearerToken, scopedRequest, cancellationToken);
    }

    internal static async Task<BusinessMesOperationTaskListRequest> ResolveRequestAsync(
        PrincipalWorkScopeResolver workScopeResolver,
        BusinessGatewayAuthorizationResult? authorization,
        BusinessConsoleMesOperationTaskListRequest request,
        string permissionCode,
        CancellationToken cancellationToken) =>
        await ResolveRequestCoreAsync(
            workScopeResolver,
            authorization,
            new OperationTaskListInput(
                request.OrganizationId,
                request.EnvironmentId,
                request.Status,
                request.Keyword,
                request.WorkCenterId,
                request.ShiftId,
                request.DeviceAssetId,
                request.WorkOrderId,
                request.Skip,
                request.Take,
                request.ScopeKind,
                request.ScopeId,
                request.OperationTaskId),
            permissionCode,
            cancellationToken);

    internal static async Task<BusinessMesOperationTaskListRequest> ResolveRequestAsync(
        PrincipalWorkScopeResolver workScopeResolver,
        BusinessGatewayAuthorizationResult? authorization,
        BusinessConsoleMesReportableOperationTaskListRequest request,
        string permissionCode,
        CancellationToken cancellationToken) =>
        await ResolveRequestCoreAsync(
            workScopeResolver,
            authorization,
            new OperationTaskListInput(
                request.OrganizationId,
                request.EnvironmentId,
                request.Status,
                request.Keyword,
                request.WorkCenterId,
                request.ShiftId,
                request.DeviceAssetId,
                request.WorkOrderId,
                request.Skip,
                request.Take,
                request.ScopeKind,
                request.ScopeId,
                OperationTaskId: null),
            permissionCode,
            cancellationToken);

    private static async Task<BusinessMesOperationTaskListRequest> ResolveRequestCoreAsync(
        PrincipalWorkScopeResolver workScopeResolver,
        BusinessGatewayAuthorizationResult? authorization,
        OperationTaskListInput request,
        string permissionCode,
        CancellationToken cancellationToken)
    {
        var scope = await workScopeResolver.ResolveAsync(
            authorization,
            request.OrganizationId,
            request.EnvironmentId,
            permissionCode,
            request.ScopeKind,
            request.ScopeId,
            cancellationToken);
        return new BusinessMesOperationTaskListRequest(
            request.OrganizationId,
            request.EnvironmentId,
            request.Status,
            request.Keyword,
            request.WorkCenterId,
            request.ShiftId,
            request.DeviceAssetId,
            request.WorkOrderId,
            request.Skip,
            request.Take,
            Join(scope.AssignedUserIds),
            Join(scope.TeamIds),
            Join(scope.WorkCenterIds),
            request.OperationTaskId);
    }

    private sealed record OperationTaskListInput(
        string OrganizationId,
        string EnvironmentId,
        string? Status,
        string? Keyword,
        string? WorkCenterId,
        string? ShiftId,
        string? DeviceAssetId,
        string? WorkOrderId,
        int Skip,
        int Take,
        string? ScopeKind,
        string? ScopeId,
        string? OperationTaskId);

    private static string? Join(IReadOnlyCollection<string> values) =>
        values.Count == 0 ? null : string.Join(',', values.Order(StringComparer.Ordinal));
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/reportable-operation-tasks")]
[BusinessGatewayOperationId("listBusinessConsoleMesReportableOperationTasks")]
public sealed class ListBusinessConsoleMesReportableOperationTasksEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    PrincipalWorkScopeResolver workScopeResolver,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesReportableOperationTaskListRequest, BusinessConsoleMesOperationTaskListResponse>(
        auth,
        BusinessGatewayPermissions.MesReportingRead)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleMesReportableOperationTaskListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesReportableOperationTaskListRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleMesOperationTaskListResponse> ForwardAsync(
        BusinessConsoleMesReportableOperationTaskListRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var scopedRequest = await ListBusinessConsoleMesOperationTasksEndpoint.ResolveRequestAsync(
            workScopeResolver,
            AuthorizationResult,
            request,
            BusinessGatewayPermissions.MesReportingRead,
            cancellationToken);
        return await mes.ListReportableOperationTasksAsync(
            tokenProvider.BearerToken,
            scopedRequest,
            cancellationToken);
    }
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/operation-sops/current")]
[BusinessGatewayOperationId("getBusinessConsoleMesCurrentOperationSops")]
public sealed class GetBusinessConsoleMesCurrentOperationSopsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCurrentSopDocumentsRequest, BusinessConsoleCurrentSopDocumentsResponse>(
        auth,
        BusinessGatewayPermissions.MesOperationsRead)
{
    protected override string OrganizationId(BusinessConsoleCurrentSopDocumentsRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCurrentSopDocumentsRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCurrentSopDocumentsResponse> ForwardAsync(
        BusinessConsoleCurrentSopDocumentsRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.GetCurrentSopDocumentsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

public abstract class BusinessConsoleMesOperationTaskActionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    MesPrincipalWorkScopeAuthorizer workScopeAuthorizer,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesOperationTaskActionRequest, BusinessConsoleMesOperationTaskActionResponse>(
        auth,
        BusinessGatewayPermissions.MesOperationsManage)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleMesOperationTaskActionRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesOperationTaskActionRequest request) => request.EnvironmentId;

    protected abstract Task<BusinessConsoleMesOperationTaskActionResponse> ForwardOperationAsync(
        string internalBearerToken,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken);

    protected override async Task<BusinessConsoleMesOperationTaskActionResponse> ForwardAsync(
        BusinessConsoleMesOperationTaskActionRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        await workScopeAuthorizer.EnsureOperationTaskAccessAsync(
            AuthorizationResult,
            request.OrganizationId,
            request.EnvironmentId,
            BusinessGatewayPermissions.MesOperationsManage,
            request.ScopeKind,
            request.ScopeId,
            request.OperationTaskId,
            cancellationToken);
        return await ForwardOperationAsync(tokenProvider.BearerToken, request, cancellationToken);
    }
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/operation-tasks/{operationTaskId}/start")]
[BusinessGatewayOperationId("startBusinessConsoleMesOperationTask")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
public sealed class StartBusinessConsoleMesOperationTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    MesPrincipalWorkScopeAuthorizer workScopeAuthorizer,
    IInternalServiceTokenProvider tokenProvider)
    : BusinessConsoleMesOperationTaskActionEndpoint(auth, workScopeAuthorizer, tokenProvider)
{
    protected override Task<BusinessConsoleMesOperationTaskActionResponse> ForwardOperationAsync(
        string internalBearerToken,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken) =>
        mes.StartOperationTaskAsync(internalBearerToken, request.OperationTaskId, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/operation-tasks/{operationTaskId}/pause")]
[BusinessGatewayOperationId("pauseBusinessConsoleMesOperationTask")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
public sealed class PauseBusinessConsoleMesOperationTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    MesPrincipalWorkScopeAuthorizer workScopeAuthorizer,
    IInternalServiceTokenProvider tokenProvider)
    : BusinessConsoleMesOperationTaskActionEndpoint(auth, workScopeAuthorizer, tokenProvider)
{
    protected override Task<BusinessConsoleMesOperationTaskActionResponse> ForwardOperationAsync(
        string internalBearerToken,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken) =>
        mes.PauseOperationTaskAsync(internalBearerToken, request.OperationTaskId, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/operation-tasks/{operationTaskId}/resume")]
[BusinessGatewayOperationId("resumeBusinessConsoleMesOperationTask")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
public sealed class ResumeBusinessConsoleMesOperationTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    MesPrincipalWorkScopeAuthorizer workScopeAuthorizer,
    IInternalServiceTokenProvider tokenProvider)
    : BusinessConsoleMesOperationTaskActionEndpoint(auth, workScopeAuthorizer, tokenProvider)
{
    protected override Task<BusinessConsoleMesOperationTaskActionResponse> ForwardOperationAsync(
        string internalBearerToken,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken) =>
        mes.ResumeOperationTaskAsync(internalBearerToken, request.OperationTaskId, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/operation-tasks/{operationTaskId}/complete")]
[BusinessGatewayOperationId("completeBusinessConsoleMesOperationTask")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
public sealed class CompleteBusinessConsoleMesOperationTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    MesPrincipalWorkScopeAuthorizer workScopeAuthorizer,
    IInternalServiceTokenProvider tokenProvider)
    : BusinessConsoleMesOperationTaskActionEndpoint(auth, workScopeAuthorizer, tokenProvider)
{
    protected override Task<BusinessConsoleMesOperationTaskActionResponse> ForwardOperationAsync(
        string internalBearerToken,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken) =>
        mes.CompleteOperationTaskAsync(internalBearerToken, request.OperationTaskId, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/wip")]
[BusinessGatewayOperationId("getBusinessConsoleMesWipSummary")]
public sealed class GetBusinessConsoleMesWipSummaryEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesWipSummaryRequest, BusinessConsoleMesWipSummaryResponse>(
        auth,
        BusinessGatewayPermissions.MesOperationsRead)
{
    protected override string OrganizationId(BusinessConsoleMesWipSummaryRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesWipSummaryRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesWipSummaryResponse> ForwardAsync(
        BusinessConsoleMesWipSummaryRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.GetWipSummaryAsync(tokenProvider.BearerToken, request, cancellationToken);
}

public sealed class BusinessConsoleMesOperationTaskActionRequestValidator
    : Validator<BusinessConsoleMesOperationTaskActionRequest>
{
    public BusinessConsoleMesOperationTaskActionRequestValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ScopeKind)
            .NotEmpty()
            .MaximumLength(50)
            .Must(Endpoints.Principal.BusinessGatewayWorkScopeKinds.Contains);
        RuleFor(x => x.ScopeId).NotEmpty().MaximumLength(200);
    }
}
