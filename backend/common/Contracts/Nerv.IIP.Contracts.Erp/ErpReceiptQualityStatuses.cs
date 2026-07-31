namespace Nerv.IIP.Contracts.Erp;

/// <summary>
/// 采购收货的质检状态值域（#1345）。ERP 命令、网关契约与前端表单共用同一份合法值与别名表，
/// 避免各层各写一套导致未知值静默落库、应付计提被悄悄跳过。
/// </summary>
public static class ErpReceiptQualityStatuses
{
    /// <summary>合格 / 免检：可直接使用，计入应付，不再触发来料检验。</summary>
    public const string Unrestricted = "unrestricted";

    /// <summary>待检：先收货，转来料检验裁定，计入应付。</summary>
    public const string Quality = "quality";

    /// <summary>冻结：暂扣不计应付，仍会触发来料检验。</summary>
    public const string Blocked = "blocked";

    /// <summary>归一化后的规范值集合。</summary>
    public static readonly IReadOnlyCollection<string> CanonicalValues = [Unrestricted, Quality, Blocked];

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["accepted"] = Unrestricted,
        ["unrestricted"] = Unrestricted,
        ["qualified"] = Unrestricted,
        ["available"] = Unrestricted,
        ["inspection"] = Quality,
        ["quality"] = Quality,
        ["rejected"] = Blocked,
        ["blocked"] = Blocked,
    };

    /// <summary>接受的输入值（规范值 + 历史别名），用于校验器白名单。</summary>
    public static readonly IReadOnlyCollection<string> AcceptedValues = [.. Aliases.Keys];

    /// <summary>是否为已知的质检状态输入值。未知值必须被拒绝，而不是原样落库。</summary>
    public static bool IsSupported(string? qualityStatus)
    {
        return !string.IsNullOrWhiteSpace(qualityStatus) && Aliases.ContainsKey(qualityStatus.Trim());
    }

    /// <summary>把输入值归一化为规范值；未知值原样小写返回，交由调用方的白名单校验拦截。</summary>
    public static string Normalize(string qualityStatus)
    {
        var trimmed = qualityStatus.Trim();
        return Aliases.TryGetValue(trimmed, out var canonical) ? canonical : trimmed.ToLowerInvariant();
    }

    /// <summary>是否计入应付：合格与待检计提，冻结（暂扣）不计。</summary>
    public static bool IsPayable(string qualityStatus)
    {
        return Normalize(qualityStatus) is Unrestricted or Quality;
    }
}
