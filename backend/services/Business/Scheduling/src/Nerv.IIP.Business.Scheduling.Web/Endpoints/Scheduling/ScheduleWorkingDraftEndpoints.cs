using FastEndpoints;
using Nerv.IIP.Business.Scheduling.Web.Application.Commands;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Endpoints.Scheduling;

public sealed record SaveScheduleWorkingDraftRequest([property: RouteParam] string PlanId,
    string OrganizationId, string EnvironmentId, SchedulingWorkingDraftStateContract State);
public sealed record ListScheduleWorkingDraftsRequest([property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId, [property: QueryParam] string? PlanId = null);
public sealed record ClearScheduleWorkingDraftRequest([property: RouteParam] string PlanId,
    [property: QueryParam] string OrganizationId, [property: QueryParam] string EnvironmentId);

internal static class ScheduleWorkingDraftIdentity
{
    // Endpoints require the InternalService policy. The forwarded user is never taken from request JSON/query.
    public static string Read(HttpContext context)
    {
        var values = context.Request.Headers[SchedulingWorkingDraftHeaders.UserId];
        if (values.Count != 1 || string.IsNullOrWhiteSpace(values[0]) || values[0]!.Length > 128 || values[0] != values[0]!.Trim())
            throw new KnownException("排程草稿请求缺少有效的当前用户身份，请通过业务网关重试。");
        return values[0]!;
    }
}

public sealed class SaveScheduleWorkingDraftEndpoint(ISender sender)
    : SchedulingEndpoint<SaveScheduleWorkingDraftRequest, ResponseData<SchedulingWorkingDraftContract>>
{
    public override void Configure() => ConfigureSchedulingContract(SchedulingEndpointContracts.Get<SaveScheduleWorkingDraftEndpoint>());
    public override async Task HandleAsync(SaveScheduleWorkingDraftRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new SaveScheduleWorkingDraftCommand(req.OrganizationId, req.EnvironmentId,
            req.PlanId, ScheduleWorkingDraftIdentity.Read(HttpContext), req.State), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class ListScheduleWorkingDraftsEndpoint(ISender sender)
    : SchedulingEndpoint<ListScheduleWorkingDraftsRequest, ResponseData<IReadOnlyList<SchedulingWorkingDraftContract>>>
{
    public override void Configure() => ConfigureSchedulingContract(SchedulingEndpointContracts.Get<ListScheduleWorkingDraftsEndpoint>());
    public override async Task HandleAsync(ListScheduleWorkingDraftsRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new ListScheduleWorkingDraftsQuery(req.OrganizationId, req.EnvironmentId,
            ScheduleWorkingDraftIdentity.Read(HttpContext), req.PlanId), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class ClearScheduleWorkingDraftEndpoint(ISender sender)
    : SchedulingEndpoint<ClearScheduleWorkingDraftRequest, ResponseData>
{
    public override void Configure() => ConfigureSchedulingContract(SchedulingEndpointContracts.Get<ClearScheduleWorkingDraftEndpoint>());
    public override async Task HandleAsync(ClearScheduleWorkingDraftRequest req, CancellationToken ct)
    {
        await sender.Send(new ClearScheduleWorkingDraftCommand(req.OrganizationId, req.EnvironmentId,
            req.PlanId, ScheduleWorkingDraftIdentity.Read(HttpContext)), ct);
        await Send.OkAsync(new ResponseData(), cancellation: ct);
    }
}
