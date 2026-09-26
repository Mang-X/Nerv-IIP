using Nerv.IIP.Business.Inventory.Domain.AggregatesModel;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements;

namespace Nerv.IIP.Business.Inventory.Web.Application.Errors;

/// <summary>
/// HTTP 边界把库存领域规则拒绝（<see cref="InventoryDomainException"/>）转成服务既有的已知业务错误传输，
/// 而不是「未知错误」500：调用方（WMS 预留、拣货标记等）拿到的是可识别的业务拒绝（#3836）。
/// 消息与失败码沿用过账拒绝的同一张中文映射，不把领域异常的英文原文透出。
/// 只作用于同步 HTTP 请求；CAP 消费路径各自按失败回执处理，不经过这里。
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
            throw InventoryPostingRejectedException.FromDomain(exception);
        }
    }
}
