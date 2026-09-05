using FastEndpoints;
using MediatR;
using Nerv.IIP.Business.Mes.Web.Application.Remediation;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Mes.Web.Endpoints.Mes;

/// <summary>
/// <c>created</c> 存量工单补下达（#3119）的一次性运维入口。与 #3000 的投影回填端点同形态：
/// 它是部署动作而非业务动作，只认内部服务令牌，不进 <see cref="MesEndpointContracts"/>、不占权限码、
/// 不经 Gateway 暴露给前端。
///
/// <para><b>执行顺序是硬约束</b>：本端点必须在 MES 准入守卫（<c>created</c> 不得开工与报工）生效之前执行，
/// 顺序颠倒会让这批已经在跑的存量工序在补救完成前无法继续报工。重复执行不再补第二次下达。</para>
/// </summary>
public sealed class BackfillCreatedWorkOrderReleaseEndpoint(ISender sender)
    : EndpointWithoutRequest<CreatedWorkOrderReleaseBackfillReport>
{
    public override void Configure()
    {
        Post("/internal/business-mes/v1/created-work-order-release-backfill");
        Policies(InternalServiceAuthorizationPolicy.Name);
        Tags("Business MES Internal");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var report = await sender.Send(new BackfillCreatedWorkOrderReleaseCommand(), ct);
        await Send.OkAsync(report, ct);
    }
}
