using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Infrastructure;

namespace Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;

/// <summary>
/// 应收登记的来源单据存在性闸门。
/// 应收账款必须挂在 ERP 里真实存在的销售单据（发货单或销售订单）上，且客户必须与该单据一致；
/// 否则财务账可以对虚构单号、虚构客户凭空生成（走查实证：虚构销售订单号 + 虚构客户登记成功返回 200）。
/// 确无来源单据的手工入账走总账手工凭证（财务 › 会计凭证 › 过账凭证），不走应收登记。
/// </summary>
internal static class AccountReceivableSourceDocumentGuard
{
    public static async Task<string> EnsureSourceDocumentAndCustomerAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string sourceDocumentNo,
        string customerCode,
        CancellationToken cancellationToken)
    {
        var normalizedSourceDocumentNo = sourceDocumentNo.Trim();
        var sourceCustomerCode = await dbContext.DeliveryOrders
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.DeliveryOrderNo == normalizedSourceDocumentNo)
            .Select(x => x.CustomerCode)
            .FirstOrDefaultAsync(cancellationToken);

        sourceCustomerCode ??= await dbContext.SalesOrders
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.SalesOrderNo == normalizedSourceDocumentNo)
            .Select(x => x.CustomerCode)
            .FirstOrDefaultAsync(cancellationToken);

        if (sourceCustomerCode is null)
        {
            throw new KnownException(
                $"来源单据『{normalizedSourceDocumentNo}』不存在，无法登记应收；无来源手工入账请走财务 › 会计凭证 › 过账凭证。");
        }

        // 客户编码按大小写不敏感比对，与库存侧同类比对一致：编码大小写差异不是不同客户，不能据此误拒。
        var authoritativeCustomerCode = sourceCustomerCode.Trim();
        if (!string.Equals(authoritativeCustomerCode, customerCode.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new KnownException(
                $"单据『{normalizedSourceDocumentNo}』客户『{sourceCustomerCode}』与『{customerCode}』不符，应收拒绝。");
        }

        return authoritativeCustomerCode;
    }
}
