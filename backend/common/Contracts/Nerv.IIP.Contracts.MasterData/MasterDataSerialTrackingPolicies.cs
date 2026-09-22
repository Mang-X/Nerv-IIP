namespace Nerv.IIP.Contracts.MasterData;

/// <summary>
/// SKU 序列号追踪策略码集（CodeSet <c>serial-tracking-policy</c>，MasterData 字典里的 SystemEnum）。
///
/// 权威码集是 MasterData 的字典种子 <c>MasterDataDictionaryRules.StandardReferenceData</c>；
/// 本类是该码集下沉到契约层的**唯一**可引用副本——字典种子、MES 领域校验与业务网关报工判定
/// 全部引它，避免各层各抄一份后字典新增码值时消费方静默拒绝（#3725 的反向失效，#3747）。
///
/// 新增码值时：字典种子、<c>docs/reference/master-data/dictionary.md</c>、本类、以及
/// <c>frontend/apps/business-console/src/data/masterDataReference.ts</c> 的下拉选项表都要改。
///
/// ⚠️ 这四处里只有一对有门禁：<c>MasterDataDictionaryRulesTests</c> 钉住本类的
/// <see cref="CanonicalValues"/> 与**它自己文件内手抄的** <c>ExpectedDictionaryCodes</c> 逐值相等。
/// 「那张手抄表抄对了 <c>dictionary.md</c>」和「前端下拉表跟上了」都没有任何断言——
/// 前者两边一起改错同一个值时断言照样绿，后者与后端完全无连接（#3747 就地登记的失效方向）。
/// 字典种子那半边现在恒真：种子已经引用本类，「种子与常量不一致」 在结构上不可表达。
/// </summary>
public static class MasterDataSerialTrackingPolicies
{
    /// <summary>不管理：不产生序列号。</summary>
    public const string None = "none";

    /// <summary>入库赋序：入库环节赋序列号。</summary>
    public const string OnReceipt = "on-receipt";

    /// <summary>生产赋序：生产报工时逐件赋序列号，报工必须带标签模板。</summary>
    public const string OnProduction = "on-production";

    /// <summary>出货赋序：出货环节赋序列号。</summary>
    public const string OnShipment = "on-shipment";

    /// <summary>码集全量值（与字典种子同序无关，比较按集合语义）。</summary>
    public static readonly IReadOnlyCollection<string> CanonicalValues =
        [None, OnReceipt, OnProduction, OnShipment];

    /// <summary>是否为码集内的合法策略值。</summary>
    public static bool IsSupported(string? policy) =>
        policy is not null && CanonicalValues.Contains(policy, StringComparer.Ordinal);
}
