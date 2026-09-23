namespace Nerv.IIP.Contracts.MasterData;

/// <summary>
/// SKU 序列号追踪策略码集（CodeSet <c>serial-tracking-policy</c>，MasterData 字典里的 SystemEnum）
/// 在契约层的副本。MasterData 字典种子、MES 领域校验与业务网关报工判定都引它（#3747）。
///
/// 一致性由 <c>MasterDataDictionaryRulesTests</c> 的两条 Fact 经测试内手抄的 oracle
/// <c>ExpectedDictionaryCodes</c> 间接钉住：一条比对本类 <see cref="CanonicalValues"/>，
/// 一条比对种子落库结果。该 oracle 与 <c>docs/reference/master-data/dictionary.md</c> 之间无断言。
/// </summary>
public static class MasterDataSerialTrackingPolicies
{
    /// <summary>不管理。</summary>
    public const string None = "none";

    /// <summary>入库赋序。</summary>
    public const string OnReceipt = "on-receipt";

    /// <summary>生产赋序。</summary>
    public const string OnProduction = "on-production";

    /// <summary>出货赋序。</summary>
    public const string OnShipment = "on-shipment";

    /// <summary>码集全量值。</summary>
    public static readonly IReadOnlyCollection<string> CanonicalValues =
        [None, OnReceipt, OnProduction, OnShipment];

    /// <summary>是否为码集内的值。</summary>
    public static bool IsSupported(string? policy) =>
        policy is not null && CanonicalValues.Contains(policy, StringComparer.Ordinal);
}
