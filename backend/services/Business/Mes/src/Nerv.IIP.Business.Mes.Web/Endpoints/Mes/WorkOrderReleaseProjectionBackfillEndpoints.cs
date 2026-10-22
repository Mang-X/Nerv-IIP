using FastEndpoints;
using MediatR;
using Nerv.IIP.Business.Mes.Web.Application.Quality;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Mes.Web.Endpoints.Mes;

/// <summary>
/// 存量在制工单发布投影回填（#3000）的一次性运维入口。它是部署动作而非业务动作：
/// 只认内部服务令牌，不进 <see cref="MesEndpointContracts"/>、不占权限码、不经 Gateway 暴露给前端。
/// 按 #2780 的硬约束，本端点必须在首件报工门禁上线前执行；重复执行不改变 Quality 侧投影。
/// </summary>
public sealed class BackfillWorkOrderReleaseProjectionEndpoint(ISender sender)
    : EndpointWithoutRequest<WorkOrderReleaseProjectionBackfillReport>
{
    public override void Configure()
    {
        Post("/internal/business-mes/v1/work-order-release-projection-backfill");
        Policies(InternalServiceAuthorizationPolicy.Name);
        Tags("Business MES Internal");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var report = await sender.Send(new BackfillWorkOrderReleaseProjectionCommand(), ct);
        await Send.OkAsync(report, ct);
    }
}
