using System.Reflection;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Contracts.Quality;

namespace Nerv.IIP.Business.Quality.Domain.Tests;

/// <summary>
/// #3191：来源**服务**轴此前没有公开词表，那 8 个取值只活在 <c>InspectionRecord</c> 的私有 HashSet 里，
/// 跨服务消费者无物可引——排程侧那道门于是拿**信封面**常量 <c>business-mes</c> 凑数，恒不相等。
/// 这条契约把词表与域内值域绑在一起：Quality 加第九个来源服务而没登记进词表，这里就红。
/// </summary>
public sealed class QualityInspectionSourceServiceContractTests
{
    [Fact]
    public void Contract_vocabulary_freezes_every_inspection_source_service()
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Inventory"] = "inventory",
            ["Wms"] = "wms",
            ["Mes"] = "mes",
            ["Erp"] = "erp",
            ["Maintenance"] = "maintenance",
            ["PurchaseReceipt"] = "purchase-receipt",
            ["MesOperation"] = "mes-operation",
            ["CustomerReturn"] = "customer-return",
        };

        Assert.Equal(expected, PublicStringConstantsOf(typeof(QualityInspectionSourceServices)));
        Assert.Equal(
            expected.Values.Order(StringComparer.Ordinal),
            QualityInspectionSourceServices.All.Order(StringComparer.Ordinal));
        Assert.Equal(
            [QualityInspectionSourceServices.Mes, QualityInspectionSourceServices.MesOperation],
            QualityInspectionSourceServices.MesOwned);
        // 信封面常量不属于 payload 面词表——把它混进来正是本票的缺陷形状。
        Assert.DoesNotContain(QualityIntegrationEventSources.BusinessMes, QualityInspectionSourceServices.All);
    }

    [Fact]
    public void Inspection_record_accepts_exactly_the_contract_source_services()
    {
        // ① contract ⊆ domain：词表里的每个取值 Domain 都必须收。
        foreach (var sourceService in QualityInspectionSourceServices.All)
        {
            var record = CreateRecord(sourceService);
            Assert.Equal(sourceService, record.SourceService);
        }

        // ② domain ⊆ contract：Domain 不得多收词表以外的取值。
        //
        // 缺了这一半，用例名里的 exactly 就是不成立的完备性声明——而**漏掉的正是 #3191 复发所需的
        // 全部条件**：Quality 侧新增一个 sourceService 而公开词表不认、跨服务的门自然也不认，
        // 本票修的缺陷原样回来，且这条契约照绿。这里直接反射域内值域逐值比对，不靠抽样。
        //
        // ⚠ 两道断言的分工不可互换，**谁都不是对方的冗余**（#3191 复审实测）：
        //   · 上面那个硬编码 8 项字典（Contract_vocabulary_freezes_every_inspection_source_service）
        //     才是**唯一的词表漂移门**——它钉的是「词表本身不许悄悄变」。
        //   · 本断言②钉的是「Domain 值域**不得脱离词表派生**」。由于 InspectionRecord.SourceServices
        //     今天正是 new(QualityInspectionSourceServices.All, ...) 派生而来，它在当前实现下**按构造成立**。
        // 后果：**两边同时加同一个值**时，红的是那个硬编码字典，本断言②是**绿**的。
        // 所以谁要是觉得那个字典"重复了、删掉吧"，唯一的词表漂移防线就没了，而这里照绿。
        // 真要动，先想清楚删掉之后「词表漂移」由谁接住。
        Assert.Equal(
            QualityInspectionSourceServices.All.Order(StringComparer.Ordinal),
            DomainSourceServices().Order(StringComparer.Ordinal));

        Assert.Throws<ArgumentException>(() => CreateRecord(QualityIntegrationEventSources.BusinessMes));
    }

    private static IReadOnlyCollection<string> DomainSourceServices()
    {
        // 反射取不到就显式 throw，而不是回空集合悄悄放行：字段改名会让断言②退化成恒真，
        // 那种失效方向是不可见的（#3191 复审的第三条绕法）。
        var field = typeof(InspectionRecord).GetField("SourceServices", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                "InspectionRecord.SourceServices 不存在：域内来源服务值域改名后，本契约会退化成只断言单向，必须同步。");
        return (HashSet<string>)field.GetValue(null)!;
    }

    private static InspectionRecord CreateRecord(string sourceService)
    {
        return InspectionRecord.Create(
            "org-001",
            "env-dev",
            null,
            QualityInspectionSourceTypes.Operation,
            sourceService,
            "WO-001",
            sourceDocumentLineId: null,
            "SKU-RM-1000",
            10m,
            null,
            null,
            [InspectionResultLineInput.Pass("appearance", "ok", null, [])],
            null,
            []);
    }

    private static IReadOnlyDictionary<string, string> PublicStringConstantsOf(Type type)
    {
        return type
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            .OrderBy(field => field.Name, StringComparer.Ordinal)
            .ToDictionary(field => field.Name, field => (string)field.GetRawConstantValue()!, StringComparer.Ordinal);
    }
}
