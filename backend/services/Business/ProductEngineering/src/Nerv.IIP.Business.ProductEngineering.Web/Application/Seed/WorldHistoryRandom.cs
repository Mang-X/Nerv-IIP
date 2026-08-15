using System.Globalization;

namespace Nerv.IIP.Business.ProductEngineering.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史引擎的确定性伪随机源（splitmix64），ProductEngineering 侧。
///
/// 关键性质：**按流键取流**。每张单据用自己的业务编码作为流键（如 <c>ECO-2026-0042</c>），
/// 因此同一张单据的内容与生成顺序、缩放比例、其他单据的存在与否都无关——
/// <c>LeaderDemo:History:Scale=0.1</c> 的快速验证跑出来的第 42 张变更单，与全量跑出来的第 42 张逐字段相同。
///
/// 绝不使用 <c>Random.Shared</c> / <c>Guid.NewGuid()</c>：种子必须可复现，否则历史每启动一次就换一副面孔。
/// 与 ERP / MES / Quality / Approval 侧按同一字面量重复声明，各侧各有黄金向量测试防止漂移。
/// </summary>
public sealed class WorldHistoryRandom
{
    /// <summary>引擎根种子。改动它会整体改写历史，属于破坏性变更。</summary>
    public const ulong RootSeed = 0x4E45525649495031UL;

    private ulong state;

    public WorldHistoryRandom(string streamKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamKey);
        state = Fnv1a64(streamKey) ^ RootSeed;
    }

    /// <summary>下一个 64 位样本（splitmix64）。</summary>
    public ulong NextUInt64()
    {
        state += 0x9E3779B97F4A7C15UL;
        var z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>[0,1) 均匀分布。</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);

    /// <summary>[minInclusive, maxExclusive) 均匀整数。</summary>
    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "maxExclusive must be greater than minInclusive.");
        }

        var range = (ulong)((long)maxExclusive - minInclusive);
        return (int)(minInclusive + (long)(NextUInt64() % range));
    }

    /// <summary>等概率取一项。</summary>
    public T Pick<T>(IReadOnlyList<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            throw new ArgumentException("Cannot pick from an empty collection.", nameof(items));
        }

        return items[NextInt(0, items.Count)];
    }

    /// <summary>按权重取一项（权重必须为正）。变更状态分布、文档类型分布都靠它。</summary>
    public T PickWeighted<T>(IReadOnlyList<T> items, IReadOnlyList<int> weights)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(weights);
        if (items.Count == 0 || items.Count != weights.Count)
        {
            throw new ArgumentException("Items and weights must be non-empty and of equal length.", nameof(weights));
        }

        var total = 0;
        foreach (var weight in weights)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(weight, 1);
            total += weight;
        }

        var roll = NextInt(0, total);
        for (var index = 0; index < items.Count; index++)
        {
            roll -= weights[index];
            if (roll < 0)
            {
                return items[index];
            }
        }

        return items[^1];
    }

    /// <summary>以 <paramref name="probability"/>（0–1）命中。</summary>
    public bool Chance(double probability) => NextDouble() < probability;

    /// <summary>FNV-1a 64，用于把流键折成种子。序数比较，跨平台稳定。</summary>
    public static ulong Fnv1a64(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var hash = 0xCBF29CE484222325UL;
        foreach (var character in value)
        {
            hash ^= character;
            hash *= 0x100000001B3UL;
        }

        return hash;
    }

    /// <summary>诊断用：把当前状态渲染成可比对的十六进制串。</summary>
    public string DescribeState() => state.ToString("X16", CultureInfo.InvariantCulture);
}
