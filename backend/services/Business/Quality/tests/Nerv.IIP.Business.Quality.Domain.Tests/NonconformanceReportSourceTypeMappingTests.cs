using System.Reflection;
using System.Runtime.ExceptionServices;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Contracts.Quality;

namespace Nerv.IIP.Business.Quality.Domain.Tests;

/// <summary>
/// #2981：<c>NonconformanceReport.ToNcrSourceType</c> 把**检验来源环节**词表映射到 **NCR 自己的来源值域**。
/// 三件事分开由三条断言闭合，职责不可互换：
/// <list type="number">
/// <item>输入域完备 —— <c>QualityInspectionSourceTypes.All</c> 的每个取值都能开出 NCR，且落点在 NCR 值域内；</item>
/// <item>映射目标正确 —— 逐条钉住每个取值映到哪一个 NCR 来源词；</item>
/// <item>兜底臂 fail-closed —— 词表外的取值必须抛，不得静默落进某个来源桶。</item>
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
    /// · 本条「词表新增取值会红」的失败方式是**兜底臂抛异常**，所以它依赖兜底臂存在；把兜底臂改成静默落值
    ///   （<c>_ => "in-process"</c>）本条就不再红了。那一格由断言三接住，别把两者当同一道防线。
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

    /// <summary>
    /// 断言三：兜底臂 fail-closed。#2981 复审实测——把 <c>_ => throw</c> 改成 <c>_ => "in-process"</c> 时
    /// 本套件 141/141 **全绿**（同一格在 base 上红 1，鉴别力是随本 PR 补齐第六个分支一起消失的）。
    ///
    /// 为什么要钉住一条「构造不出来」的分支，而不是当成等价变异放掉：
    /// <c>InspectionRecord.SourceType</c> 是 <c>{ get; private set; }</c> 且由
    /// <c>InspectionRecordEntityTypeConfiguration</c> 映射到 <c>source_type</c> 列，**EF 物化走的是私有无参
    /// 构造函数 + 属性写入，绕过 <c>Supported(sourceType, SourceTypes, ...)</c>**。也就是说库里任何一行
    /// 历史值、旧版本写入值或词表回退后残留值，都会原样流进本方法——`maintenance` 当初撞出 #2981 靠的正是
    /// 「值合法但映射缺失」这条路，而兜底臂守的是「值连合法都不是」那条。二者都不是死代码。
    ///
    /// 失效方向也必须是可见的：兜底臂一旦静默落值，未知来源环节会被冒充成制程不合格写进 NCR，
    /// 而不是响一声——按 <c>docs/governance/testing/validity.md</c> 的负向路径要求，这类分支必须失败关闭。
    ///
    /// ⚠ 证明范围：本条只作用在 <c>ToNcrSourceType</c> 这一层（反射直调）。它**不证明** EF 真的会物化出
    /// 词表外的取值，那需要 <c>postgres</c> lane 上的真实往返；上面那段是解释兜底臂为何不是死代码的
    /// 可达性论证，不是本用例的断言内容。
    /// </summary>
    [Fact]
    public void Unknown_inspection_source_type_fails_closed_instead_of_silently_landing_in_a_bucket()
    {
        const string offVocabularySourceType = "teardown";
        Assert.DoesNotContain(offVocabularySourceType, QualityInspectionSourceTypes.All);

        var exception = Assert.Throws<InvalidOperationException>(
            () => InvokeToNcrSourceType(offVocabularySourceType));

        // 消息必须点名那个取值：排障时「哪个来源环节没映射」是唯一有用的信息。
        Assert.Contains(offVocabularySourceType, exception.Message, StringComparison.Ordinal);
    }

    private static string InvokeToNcrSourceType(string inspectionSourceType)
    {
        // 反射取不到就显式 throw：方法改名或被内联进 OpenFromInspection 时，本用例会因此变红而不是
        // 悄悄退化成不再校验兜底臂（姿势同下面的 NcrSourceValueDomain）。
        var method = typeof(NonconformanceReport)
            .GetMethod("ToNcrSourceType", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                "NonconformanceReport.ToNcrSourceType 不存在：来源映射改名或内联后，本契约会退化成不再校验兜底臂，必须同步。");

        try
        {
            return (string)method.Invoke(null, [inspectionSourceType])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            // 保留原始异常与堆栈，否则 Assert.Throws<InvalidOperationException> 会去匹配反射包装异常。
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
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
            sourceDocumentLineId: null,
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
