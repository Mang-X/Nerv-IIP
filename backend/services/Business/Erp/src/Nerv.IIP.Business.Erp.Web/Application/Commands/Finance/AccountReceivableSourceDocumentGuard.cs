using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Infrastructure;

namespace Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;

/// <summary>
/// 应收登记的来源单据存在性闸门。
/// 应收账款必须挂在 ERP 里真实存在的销售单据（发货单或销售订单）上，且客户必须与该单据一致；
/// 否则财务账可以对虚构单号、虚构客户凭空生成（走查实证：虚构销售订单号 + 虚构客户登记成功返回 200）。
/// 确无来源单据的手工入账走总账手工凭证（POST /api/business/v1/erp/finance/vouchers），不走应收登记。
/// </summary>
internal static class AccountReceivableSourceDocumentGuard
{
    public static async Task EnsureSourceDocumentAndCustomerAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string sourceDocumentNo,
        string customerCode,
        CancellationToken cancellationToken)
    {
        var sourceCustomerCode = await dbContext.DeliveryOrders
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.DeliveryOrderNo == sourceDocumentNo)
            .Select(x => x.CustomerCode)
            .FirstOrDefaultAsync(cancellationToken);

        sourceCustomerCode ??= await dbContext.SalesOrders
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.SalesOrderNo == sourceDocumentNo)
            .Select(x => x.CustomerCode)
            .FirstOrDefaultAsync(cancellationToken);

        if (sourceCustomerCode is null)
        {
            throw new KnownException(
                $"来源单据『{sourceDocumentNo}』在 ERP 中不存在（既不是发货单，也不是销售订单），应收不能凭空登记。确需无来源手工入账，请改用总账手工凭证。");
        }

        if (!string.Equals(sourceCustomerCode, customerCode, StringComparison.Ordinal))
        {
            throw new KnownException(
                $"来源单据『{sourceDocumentNo}』的客户是『{sourceCustomerCode}』，与登记的客户『{customerCode}』不一致，应收登记已拒绝。");
        }
    }
}
