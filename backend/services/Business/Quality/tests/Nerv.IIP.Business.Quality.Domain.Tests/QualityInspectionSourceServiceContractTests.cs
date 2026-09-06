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
        foreach (var sourceService in QualityInspectionSourceServices.All)
        {
            var record = CreateRecord(sourceService);
            Assert.Equal(sourceService, record.SourceService);
        }

        Assert.Throws<ArgumentException>(() => CreateRecord(QualityIntegrationEventSources.BusinessMes));
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
