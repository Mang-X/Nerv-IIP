using System.Reflection;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Contracts.Quality;

namespace Nerv.IIP.Business.Quality.Domain.Tests;

/// <summary>
/// #2981：<c>NonconformanceReport.ToNcrSourceType</c> 把**检验来源环节**词表映射到 **NCR 自己的来源值域**。
/// 两件事分开由两条断言闭合，职责不可互换：
/// <list type="number">
/// <item>输入域完备 —— <c>QualityInspectionSourceTypes.All</c> 的每个取值都能开出 NCR，且落点在 NCR 值域内；</item>
/// <item>映射目标正确 —— 逐条钉住每个取值映到哪一个 NCR 来源词。</item>
/// </list>
/// </summary>
public sealed class NonconformanceReportSourceTypeMappingTests
{
    /// <summary>
    /// #2981 的坐实用例，保留为回归防线。修复前实测读数：
    /// <c>System.InvalidOperationException : Inspection source type 'maintenance' cannot open an NCR.</c>
    /// （抛在 <c>ToNcrSourceType</c>，由 <c>OpenFromInspection</c> 触发）。
    /// </summary>
    [Fact]
    public void Maintenance_inspection_failure_opens_an_in_process_ncr()
    {
        var ncr = OpenNcrFrom(QualityInspectionSourceTypes.Maintenance);

        Assert.Equal("in-process", ncr.SourceType);
    }

    /// <summary>
    /// 断言一：输入域完备。词表新增第七个来源环节而 <c>ToNcrSourceType</c> 没跟上时，这里会因为
    /// <c>_ =></c> 分支抛 <c>InvalidOperationException</c> 而红——这正是 #2981 里 <c>maintenance</c> 的失败形状。
    ///
    /// ⚠ 有效性边界（别把它读成比实际更强）：
    /// · 「落点必须在 NCR 值域内」这一半**与生产代码同向**——<c>NonconformanceReport</c> 构造函数本身就
    ///   <c>Supported(sourceType, SourceTypes, ...)</c> 失败关闭，所以把某个目标改成词表外的值时，
    ///   红的其实是构造函数。这里显式反射比对是为了让**这条不变量被写下来**，不是为了新增鉴别力。
    /// · 「目标改成另一个**合法**值」这一格本条**杀不掉**（`final` 也在 NCR 值域里），那是断言二的职责。
    /// · 本条不依赖任何硬编码期望表：断言二那张表若被后来人当成「重复」删掉，完备性防线仍在这里。
    /// </summary>
    [Fact]
    public void Every_inspection_source_type_maps_into_the_ncr_source_value_domain()
    {
        var ncrSourceTypes = NcrSourceValueDomain();

        foreach (var inspectionSourceType in QualityInspectionSourceTypes.All)
        {
            var ncr = OpenNcrFrom(inspectionSourceType);

            Assert.Contains(ncr.SourceType, ncrSourceTypes);
        }
    }

    /// <summary>
    /// 断言二：映射目标本身。#2981 的证据轴原始发现是——把 <c>"first-article" => "in-process"</c> 改成
    /// <c>"final"</c> 时 Web/Domain **全绿**，只有整条删掉才红；也就是说当时只证明了「能开 NCR」，
    /// 没证明「开成哪一种」。这张表把每个目标钉死：**改任一目标为另一个合法值即红**。
    ///
    /// 期望值来源：`operation`/`first-article`/`maintenance` 同属制程内不合格（见 <c>ToNcrSourceType</c>
    /// 各分支上的业务理由）；`receiving`/`final`/`customer-return` 两个值域同名同义，直通。
    /// </summary>
    [Fact]
    public void Inspection_source_types_map_to_pinned_ncr_source_types()
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [QualityInspectionSourceTypes.Receiving] = "receiving",
            [QualityInspectionSourceTypes.Operation] = "in-process",
            [QualityInspectionSourceTypes.Final] = "final",
            [QualityInspectionSourceTypes.FirstArticle] = "in-process",
            [QualityInspectionSourceTypes.Maintenance] = "in-process",
            [QualityInspectionSourceTypes.CustomerReturn] = "customer-return",
        };

        var actual = QualityInspectionSourceTypes.All.ToDictionary(
            inspectionSourceType => inspectionSourceType,
            inspectionSourceType => OpenNcrFrom(inspectionSourceType).SourceType,
            StringComparer.Ordinal);

        Assert.Equal(expected, actual);
    }

    private static NonconformanceReport OpenNcrFrom(string inspectionSourceType)
    {
        var record = InspectionRecord.Create(
            "org-001",
            "env-dev",
            null,
            inspectionSourceType,
            QualityInspectionSourceServices.Maintenance,
            "SRC-001",
            "SKU-RM-1000",
            4m,
            "LOT-001",
            null,
            [InspectionResultLineInput.Fail("coa", "mismatch", "wrong-spec", 4m, [])],
            "Inspection failed",
            []);

        return NonconformanceReport.OpenFromInspection("NCR-20260910-0001", record, "wrong-spec", []);
    }

    private static IReadOnlyCollection<string> NcrSourceValueDomain()
    {
        // 反射取不到就显式 throw，而不是回空集合悄悄放行：字段改名会让上面的 Assert.Contains 退化成恒假/恒真，
        // 那种失效方向是不可见的（姿势同 #3191 的 QualityInspectionSourceServiceContractTests）。
        var field = typeof(NonconformanceReport).GetField("SourceTypes", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                "NonconformanceReport.SourceTypes 不存在：NCR 来源值域改名后，本契约会退化成不再校验落点，必须同步。");
        return (HashSet<string>)field.GetValue(null)!;
    }
}
