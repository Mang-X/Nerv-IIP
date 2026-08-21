using System.Security.Cryptography;
using System.Text;

namespace Nerv.IIP.Contracts.Approval;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史里「NCR 处置审批回链」的**跨服务确定性公式**（#1684）。
///
/// <para>
/// 两个种子引擎在不通信、不跨库查询、不建跨 schema 外键的前提下必须精确互指：
/// </para>
/// <list type="bullet">
/// <item>Approval 侧（<c>WorldHistoryApprovalSeedService</c>）用 <see cref="SeededDispositionChainId"/>
/// 给历史 NCR 处置审批链定聚合 id（经 <c>ApprovalChain.StartWithSeededIdentity</c> 落库）；</item>
/// <item>Quality 侧（<c>WorldHistorySeedService</c>）用同一公式把该 id 回填到
/// <c>NonconformanceReport.DispositionApprovalChainId</c>，NCR 详情页据此拉取审批区。</item>
/// </list>
///
/// <para>
/// **两侧必须逐字一致**：盐串（域前缀 + 模板码 + NCR 单号）、SHA-256 派生和位运算与仓库既有先例
/// <c>WorldHistoryPlanningSpec.StablePlanningSuggestionId</c> 同构；任何一侧对公式或其输入
/// （<c>NCR-2026-####</c> 单号公式、覆盖面下界 <see cref="CoveredNcrCount"/>）的漂移都会让回链
/// 静默指向不存在的链。两侧各有一份逐字相同的黄金向量测试
/// （<c>WorldHistoryNcrDispositionApprovalGoldenVector</c>）钉住本公式的全部输出。
/// </para>
///
/// <para>
/// **仅限世界观种子及其校验器使用**：生产路径的审批链 id 一律由
/// <c>ApprovalChain.Start</c> 产出 <c>Guid.CreateVersion7()</c>，绝不引用本类型。
/// </para>
/// </summary>
public static class WorldHistoryNcrDispositionApprovals
{
    // NERV-1123 legacy 边界：这些值标识已持久化的 WorldHistory 模板/链。
    // 在 NERV-1135 删除整个旧边界之前，它们必须与 NCR 确定性公式共同冻结在此。
    public const string LegacyPurchaseOrderReleaseTemplateCode = "APT-WB-PO-001";
    public const string LegacyPurchaseOrderChangeTemplateCode = "APT-WB-PO-002";
    public const string LegacyNcrDispositionTemplateCode = "APT-WB-NCR-001";
    public const string LegacyStockCountVarianceTemplateCode = "APT-WB-CNT-001";
    public const string LegacyEngineeringChangeOrderTemplateCode = "APT-WB-ECO-001";

    /// <summary>
    /// 历史 NCR 处置审批链的确定性 id。盐串维度：固定域前缀 + 处置审批模板码
    /// （<see cref="LegacyNcrDispositionTemplateCode"/>）+ NCR 单号（<c>NCR-2026-####</c>）。
    /// 该旧码是已持久化世界观链的盐；产品模板码迁移不得改变本公式。
    /// 哈希派生手法照抄 DemandPlanning 侧先例 <c>StablePlanningSuggestionId</c>
    /// （SHA-256、<c>bytes[6]</c> 版本位打 5、<c>bytes[8]</c> 变体位）。
    /// </summary>
    public static Guid SeededDispositionChainId(string ncrCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ncrCode);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"nerv-iip:world-history:ncr-disposition-approval-chain:{LegacyNcrDispositionTemplateCode}:{ncrCode}"));
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes.AsSpan(0, 16));
    }

    /// <summary>
    /// 处置审批回链的覆盖面条数 K（经标定的保守下界，裁决记录见 Approval 侧
    /// <c>WorldHistoryApprovalSpec.NcrReferenceCount</c> 的标定说明）：
    /// <c>K = floor(0.18 × totalPurchaseOrders)</c>；小样本区间
    /// （<paramref name="scale"/> &lt; 0.05 或采购单不足 30 张）直接取 0。
    ///
    /// <para>
    /// 审批域只为 <c>NCR-2026-0001..K</c> 造历史处置审批链，因此 Quality 侧**只允许**给前 K 张
    /// NCR 回填 <see cref="SeededDispositionChainId"/>，第 K+1 张起必须保持 <c>null</c>——
    /// 否则回链会指向审批库里不存在的链。<paramref name="totalPurchaseOrders"/> 由两侧各自的
    /// <c>WorldHistoryProcurementSpec.TotalPurchaseOrders(asOfDate, scale)</c> 复算
    /// （两份 spec 副本逐字相同，各有黄金向量测试防漂移）。
    /// </para>
    /// </summary>
    public static int CoveredNcrCount(int totalPurchaseOrders, double scale)
    {
        if (scale < 0.05d)
        {
            return 0;
        }

        return totalPurchaseOrders < 30 ? 0 : (int)Math.Floor(totalPurchaseOrders * 0.18d);
    }
}
