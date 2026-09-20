using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.AndonCallAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;
using Nerv.IIP.Contracts.Mes;

namespace Nerv.IIP.Business.Mes.Web.Application.Queries.Andon;

// 数据范围来自已认证的内部调用方；用户权限与 scope 解析由 Gateway 负责。
public sealed record AndonCallScope(string OrganizationId, string EnvironmentId,
    string? AssignedUserIds = null, string? TeamIds = null, string? WorkCenterIds = null);

public enum AndonCallQueue { AwaitingResponse, Unclosed, All }

public sealed record AndonCallResponse(string Id, string OrganizationId, string EnvironmentId,
    [property: JsonConverter(typeof(JsonStringEnumConverter<AndonCallCategory>))] AndonCallCategory Category,
    [property: JsonConverter(typeof(JsonStringEnumConverter<AndonCallStatus>))] AndonCallStatus Status, string WorkOrderId, string OperationTaskId,
    string WorkCenterId, string CallerId, DateTimeOffset RaisedAtUtc, string? ResponderId,
    DateTimeOffset? FirstRespondedAtUtc, double? ResponseDurationSeconds, DateTimeOffset? ClosedAtUtc,
    DateTimeOffset? EscalatedAtUtc, string? EscalationRecipientId)
{
    public static AndonCallResponse From(AndonCall call) => new(call.Id.ToString(), call.OrganizationId, call.EnvironmentId,
        call.Category, call.Status, call.WorkOrderId, call.OperationTaskIdValue, call.WorkCenterId,
        call.CallerId, call.RaisedAtUtc, call.ResponderId, call.FirstRespondedAtUtc,
        call.ResponseDuration?.TotalSeconds, call.ClosedAtUtc, call.EscalatedAtUtc, call.EscalationRecipientId);
}

public sealed record GetAndonCallQuery(AndonCallScope Scope, AndonCallId Id) : IQuery<AndonCallResponse>;
public sealed record ListAndonCallsQuery(AndonCallScope Scope, AndonCallQueue Queue = AndonCallQueue.Unclosed,
    AndonCallCategory? Category = null, string? WorkCenterId = null, int Skip = 0, int Take = 50)
    : IQuery<AndonCallListResponse>;
public sealed record AndonCallListResponse(IReadOnlyCollection<AndonCallResponse> Items, int Total);

public sealed class GetAndonCallQueryHandler(ApplicationDbContext db)
    : IQueryHandler<GetAndonCallQuery, AndonCallResponse>
{
    public async Task<AndonCallResponse> Handle(GetAndonCallQuery request, CancellationToken ct) =>
        AndonCallResponse.From(await AndonCallAccess.Query(db, request.Scope).AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.Id, ct) ?? throw new KnownException("未找到授权范围内的异常呼叫。"));
}

public sealed class ListAndonCallsQueryHandler(ApplicationDbContext db)
    : IQueryHandler<ListAndonCallsQuery, AndonCallListResponse>
{
    public async Task<AndonCallListResponse> Handle(ListAndonCallsQuery request, CancellationToken ct)
    {
        var page = OffsetPage.From(request.Skip, request.Take);
        var query = AndonCallAccess.Query(db, request.Scope).AsNoTracking();
        query = request.Queue switch
        {
            AndonCallQueue.AwaitingResponse => query.Where(x => x.Status == AndonCallStatus.Open),
            AndonCallQueue.Unclosed => query.Where(x => x.Status != AndonCallStatus.Closed),
            AndonCallQueue.All => query,
            _ => throw new KnownException("异常呼叫队列无效。")
        };
        if (request.Category is { } category) query = query.Where(x => x.Category == category);
        if (request.WorkCenterId is not null) query = query.Where(x => x.WorkCenterId == request.WorkCenterId);
        var total = await query.CountAsync(ct);
        var calls = await query.OrderBy(x => x.RaisedAtUtc).ThenBy(x => x.Id)
            .Skip(page.Skip).Take(page.Take).ToArrayAsync(ct);
        return new(calls.Select(AndonCallResponse.From).ToArray(), total);
    }
}

internal static class AndonCallAccess
{
    internal static IQueryable<AndonCall> Query(ApplicationDbContext db, AndonCallScope scope)
    {
        var tenant = TenantScope.From(scope.OrganizationId, scope.EnvironmentId);
        var tasks = GetMesWorkOrderDetailQueryHandler.QueryOperationTaskEntities(db,
            tenant.OrganizationId, tenant.EnvironmentId, null, null,
            assignedUserIds: scope.AssignedUserIds, teamIds: scope.TeamIds, workCenterIds: scope.WorkCenterIds);
        return db.Set<AndonCall>().Where(call => call.OrganizationId == tenant.OrganizationId &&
            call.EnvironmentId == tenant.EnvironmentId && tasks.Any(task =>
                task.OperationTaskIdValue == call.OperationTaskIdValue && task.WorkOrderId == call.WorkOrderId));
    }

    internal static async Task EnsureSourceAsync(ApplicationDbContext db, AndonCallScope scope,
        string workOrderId, string operationTaskId, string workCenterId, CancellationToken ct)
    {
        var tenant = TenantScope.From(scope.OrganizationId, scope.EnvironmentId);
        var tasks = GetMesWorkOrderDetailQueryHandler.QueryOperationTaskEntities(db,
            tenant.OrganizationId, tenant.EnvironmentId, workOrderId, null, workCenterId: workCenterId,
            assignedUserIds: scope.AssignedUserIds, teamIds: scope.TeamIds, workCenterIds: scope.WorkCenterIds,
            operationTaskId: operationTaskId);
        if (!await tasks.AnyAsync(ct) || !await db.WorkOrders.AnyAsync(x =>
                x.OrganizationId == tenant.OrganizationId && x.EnvironmentId == tenant.EnvironmentId &&
                x.WorkOrderIdValue == workOrderId, ct))
            throw new KnownException("未找到授权范围内匹配的工单、工序与工作中心。" );
    }
}
