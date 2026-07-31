using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Infrastructure;

namespace Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;

/// <summary>
/// 应付登记的来源单据存在性闸门。
/// 应付账款必须挂在 ERP 里真实存在的采购单据（采购收货单、采购订单或供应商发票）上，且供应商必须与该单据一致；
/// 否则财务账可以对虚构单号、虚构供应商凭空生成。
/// 确无来源单据的手工入账走总账手工凭证（财务 › 会计凭证 › 过账凭证），不走应付登记。
/// </summary>
internal static class AccountPayableSourceDocumentGuard
{
    public static async Task<string> EnsureSourceDocumentAndSupplierAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string sourceDocumentNo,
        string supplierCode,
        CancellationToken cancellationToken)
    {
        var normalizedSourceDocumentNo = sourceDocumentNo.Trim();
        var sourceSupplierCode = await dbContext.PurchaseReceipts
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.PurchaseReceiptNo == normalizedSourceDocumentNo)
            .Select(x => x.SupplierCode)
            .FirstOrDefaultAsync(cancellationToken);

        sourceSupplierCode ??= await dbContext.PurchaseOrders
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.PurchaseOrderNo == normalizedSourceDocumentNo)
            .Select(x => x.SupplierCode)
            .FirstOrDefaultAsync(cancellationToken);

        sourceSupplierCode ??= await dbContext.SupplierInvoices
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.InvoiceNo == normalizedSourceDocumentNo)
            .Select(x => x.SupplierCode)
            .FirstOrDefaultAsync(cancellationToken);

        if (sourceSupplierCode is null)
        {
            throw new KnownException(
                $"来源单据『{normalizedSourceDocumentNo}』在 ERP 中不存在（既不是采购收货单、采购订单，也不是供应商发票），应付不能凭空登记。确需无来源手工入账，请前往财务 › 会计凭证 › 过账凭证。");
        }

        // 供应商编码按大小写不敏感比对，与库存侧同类比对一致：编码大小写差异不是不同供应商，不能据此误拒。
        var authoritativeSupplierCode = sourceSupplierCode.Trim();
        if (!string.Equals(authoritativeSupplierCode, supplierCode.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new KnownException(
                $"来源单据『{normalizedSourceDocumentNo}』的供应商是『{sourceSupplierCode}』，与登记的供应商『{supplierCode}』不一致，应付登记已拒绝。");
        }

        return authoritativeSupplierCode;
    }
}
