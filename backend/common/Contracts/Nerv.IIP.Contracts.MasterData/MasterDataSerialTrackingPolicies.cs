namespace Nerv.IIP.Contracts.MasterData;

/// <summary>
/// SKU 序列号追踪策略码集（CodeSet <c>serial-tracking-policy</c>，MasterData 字典里的 SystemEnum）。
///
/// 权威码集是 MasterData 的字典种子 <c>MasterDataDictionaryRules.StandardReferenceData</c>；
/// 本类是该码集下沉到契约层的**唯一**可引用副本——字典种子、MES 领域校验与业务网关报工判定
/// 全部引它，避免各层各抄一份后字典新增码值时消费方静默拒绝（#3725 的反向失效，#3747）。
///
/// 新增码值时：先改字典种子与 <c>docs/reference/master-data/dictionary.md</c>，再改这里；
/// 两者不一致由 <c>MasterDataDictionaryRulesTests</c> 报红。
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
