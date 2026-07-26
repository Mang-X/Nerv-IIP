using Nerv.IIP.Business.Inventory.Web.Application.Seed;
using System.Globalization;
using Xunit.Abstractions;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

/// <summary>
/// <c>WorldHistoryQualitySpec</c> 在库存域是**逐字面量复制**的一份（与质量域侧只差 namespace 一行）：
/// 库存要为每张 NCR 落隔离 / 持有 / 报废流水，就必须自己把 NCR 事实重算一遍，
/// 而不能跨库去质量域查。
///
/// 本黄金向量锁死这份复制不漂移：一旦两侧的不合格率、处置分布、不良数量或 NCR 编号规则被单边改动，
/// 这里立刻红——比等到真机上「库存报废量对不上质量报废量」再发现要早得多。
/// 若确需改动，两侧同时改，并同步更新这里冻结的期望值。
/// </summary>
public sealed class WorldHistoryQualityMirrorGoldenVectorTests(ITestOutputHelper output)
{
    private static readonly DateOnly AsOfDate = new(2026, 7, 26);

    /// <summary>全量规模下前 8 张 NCR 的「编号|处置|不良数量|源单据」冻结值。</summary>
    private static readonly string[] ExpectedFirstNonconformances =
    [
        "NCR-2026-0001|Rework|4|WO-2026-00057",
        "NCR-2026-0002|Rework|8|WO-2026-00073",
        "NCR-2026-0003|ConditionalRelease|3|WO-2026-00077",
        "NCR-2026-0004|ConditionalRelease|6|WO-2026-00102",
        "NCR-2026-0005|Rework|1|WO-2026-00132",
        "NCR-2026-0006|Rework|5|WO-2026-00184",
        "NCR-2026-0007|ConditionalRelease|1|WO-2026-00222",
        "NCR-2026-0008|ConditionalRelease|3|WO-2026-00251",
    ];

    [Fact]
    public void Mirrored_quality_spec_reproduces_the_frozen_nonconformance_vector()
    {
        var nonconformances = WorldHistoryQualitySpec.BuildInspectionFacts(AsOfDate, 1.0d)
            .Where(fact => fact.HasNonconformance)
            .ToArray();

        var actual = nonconformances
            .Take(ExpectedFirstNonconformances.Length)
            .Select(Describe)
            .ToArray();
        foreach (var line in actual)
        {
            output.WriteLine($"inventory-quality-mirror-vector: {line}");
        }

        var scrapQuantity = nonconformances
            .Where(x => x.Disposition == WorldHistoryInspectionDisposition.Scrap)
            .Sum(x => x.DefectQuantity);
        output.WriteLine($"inventory-quality-mirror-ncr-count={nonconformances.Length}");
        output.WriteLine(FormattableString.Invariant($"inventory-quality-mirror-scrap-quantity={scrapQuantity}"));

        Assert.Equal(ExpectedFirstNonconformances, actual);

        // 总量同样冻结：单条向量对上但总量漂了，说明复制的是「前几张一样、后面不一样」的坏拷贝。
        Assert.Equal(164, nonconformances.Length);
        Assert.Equal(67m, scrapQuantity);
    }

    [Fact]
    public void Mirrored_quality_spec_keeps_the_scrap_movement_id_contract()
    {
        foreach (var fact in WorldHistoryQualitySpec.BuildInspectionFacts(AsOfDate, 0.2d)
                     .Where(x => x.Disposition == WorldHistoryInspectionDisposition.Scrap))
        {
            // 质量域关单时写的就是这个字符串；库存域的报废流水必须用同一个值当幂等键。
            Assert.Equal($"INV-SCRAP-{fact.NcrCode}", fact.ScrapMovementId);
            Assert.Equal("operation", fact.SourceType);
        }

        Assert.Equal(WorldHistoryPhase2Spec.QualityHoldLocationCode, "WH-WB-QC-01");
    }

    private static string Describe(WorldHistoryInspectionFact fact) => string.Create(
        CultureInfo.InvariantCulture,
        $"{fact.NcrCode}|{fact.Disposition}|{fact.DefectQuantity:0.##}|{fact.SourceDocumentId}");
}
