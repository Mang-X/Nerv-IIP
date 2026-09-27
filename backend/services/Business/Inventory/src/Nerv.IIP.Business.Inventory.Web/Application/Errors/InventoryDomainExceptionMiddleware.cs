using FastEndpoints;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel;
using Nerv.IIP.Business.Inventory.Web.Endpoints.Inventory;

namespace Nerv.IIP.Business.Inventory.Web.Application.Errors;

/// <summary>
/// HTTP 边界把库存领域规则拒绝（<see cref="InventoryDomainException"/>）转成服务既有的已知业务错误传输，
/// 而不是「未知错误」500（#3836）。消息按被调用的操作给出：同一个领域失败原因
/// （例如 <see cref="InventoryDomainFailureReason.ReservationAllocationRejected"/>）在预留、续期、确认拣货时含义不同，
/// 不能沿用出库过账的「无法过账」文案。库存移动过账在命令内自行映射，不经过这里。
/// 只作用于同步 HTTP 请求；CAP 消费路径各自按失败回执处理。
/// </summary>
public sealed class InventoryDomainExceptionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (InventoryDomainException exception)
        {
            var endpointType = context.GetEndpoint()?.Metadata.GetMetadata<EndpointDefinition>()?.EndpointType;
            if (endpointType == typeof(MarkStockReservationPickedEndpoint))
            {
                throw new KnownException("库存预留已失效，无法确认拣货完成，请重新创建拣货任务。", exception);
            }

            if (endpointType == typeof(RenewStockReservationEndpoint))
            {
                throw new KnownException("库存预留已失效，无法续期。", exception);
            }

            if (endpointType == typeof(ReserveStockEndpoint) || endpointType == typeof(ReserveFefoStockEndpoint))
            {
                if (exception.Reason == InventoryDomainFailureReason.LedgerFrozen)
                {
                    throw new KnownException("库存台账正在盘点冻结，暂时不能预留。", exception);
                }

                throw new KnownException("可用库存不足，无法预留。", exception);
            }

            throw new KnownException("当前库存状态不允许此操作，请刷新后重试。", exception);
        }
    }
}
