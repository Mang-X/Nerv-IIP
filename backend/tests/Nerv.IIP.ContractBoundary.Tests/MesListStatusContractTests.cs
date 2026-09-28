using System.Reflection;
using System.Text.Json;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ShiftHandoverAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;

namespace Nerv.IIP.ContractBoundary.Tests;

/// <summary>
/// MES 列表 <c>status</c> 枚举必须等于对应聚合的真实值域（#3912）。
///
/// <para><b>为什么在本项目而不是 BusinessGateway.Web.Tests</b>：这条断言要同时拿到
/// 「Gateway 的 OpenAPI 产物」和「MES 域常量」，而
/// <c>docs/architecture/overview/repo-layout.md</c> 规则 11 禁止 BusinessGateway 的任何项目
/// 引用 <c>backend/services/Business</c> 下的 Web/Domain/Infrastructure —— 包括它自己的 tests。
/// 本仓已有的合法形态是本项目：中心测试引用多个服务的 Domain，再对导出的 OpenAPI snapshot
/// 断言（与 <c>Nerv.IIP.FacadeCoverage.Tests</c> 读同一份 snapshot 一致）。</para>
///
/// <para><b>权威来源是 MES 域常量</b>，不是从现有枚举反推：域里新增一个状态而 Gateway 契约
/// 没跟上时，本组断言必然红。这正是「域是事实来源」的机器判据。</para>
///
/// <para><b>工单为什么用 <see cref="WorkOrder.AllStatuses"/> 而不是反射</b>：同一个类型上还有
/// <c>captured</c> / <c>no-requirements</c> 两个物料需求快照状态常量，它们写在另一个属性上，
/// 不是工单生命周期状态（域注释已写明）。</para>
///
/// <para><b>停机 / 产能为什么不是反射</b>：<c>WorkCenterUnavailability</c> 没有状态列，状态由
/// 「是否已恢复」在读面派生，因此值域取聚合上的两个常量
/// <see cref="WorkCenterUnavailability.OpenStatus"/> / <see cref="WorkCenterUnavailability.RecoveredStatus"/>。</para>
/// </summary>
public sealed class MesListStatusContractTests
{
    private static readonly string[] WorkOrderStatuses = WorkOrder.AllStatuses.ToArray();

    private static readonly string[] OperationTaskStatuses = Enum.GetNames<OperationTaskLifecycleStatus>();

    private static readonly string[] MaterialIssueRequestStatuses = DeclaredStatusConstants<MaterialIssueRequest>();

    private static readonly string[] FinishedGoodsReceiptRequestStatuses =
        DeclaredStatusConstants<FinishedGoodsReceiptRequest>();

    private static readonly string[] DefectRecordStatuses = DeclaredStatusConstants<DefectRecord>();

    private static readonly string[] ShiftHandoverStatuses = DeclaredStatusConstants<ShiftHandover>();

    private static readonly string[] WorkCenterUnavailabilityStatuses =
        [WorkCenterUnavailability.OpenStatus, WorkCenterUnavailability.RecoveredStatus];

    /// <summary>
    /// 行属性上的状态：schema 后缀 + 属性名 + 该聚合的真实值域。
    /// 一行 schema 一个用例，新增 schema 而忘了登记值域时这条会红（而不是静默漏覆盖）。
    /// </summary>
    public static IEnumerable<object[]> RowStatusProperties =>
    [
        ["BusinessConsoleMesWorkOrderItem", "status", WorkOrderStatuses],
        ["BusinessConsoleMesOperationTaskItem", "status", OperationTaskStatuses],
        ["BusinessConsoleMesMaterialIssueRequestRow", "status", MaterialIssueRequestStatuses],
        ["BusinessConsoleMesDispatchTaskRow", "status", OperationTaskStatuses],
        ["BusinessConsoleMesOperationTaskRow", "status", OperationTaskStatuses],
        ["BusinessConsoleMesWipSummaryRow", "status", OperationTaskStatuses],
        ["BusinessConsoleMesRelatedQualityItemRow", "status", DefectRecordStatuses],
        ["BusinessConsoleMesReceiptRequestRow", "receiptStatus", FinishedGoodsReceiptRequestStatuses],
        ["BusinessConsoleMesDowntimeEventRow", "status", WorkCenterUnavailabilityStatuses],
        ["BusinessConsoleMesCapacityImpactRow", "status", WorkCenterUnavailabilityStatuses],
    ];

    [Theory]
    [MemberData(nameof(RowStatusProperties))]
    public void Row_status_enum_equals_aggregate_value_domain(
        string schemaNameSuffix,
        string propertyName,
        string[] expected)
    {
        using var document = LoadSnapshot();
        var property = FindSchemaBySuffix(document, schemaNameSuffix)
            .GetProperty("properties")
            .GetProperty(propertyName);

        AssertStatusEnumEqualsDomain(property, $"{schemaNameSuffix}.{propertyName}", expected);
    }

    [Theory]
    [InlineData("/api/business-console/v1/mes/work-orders")]
    [InlineData("/api/business-console/v1/mes/production-plans")]
    [InlineData("/api/business-console/v1/mes/material-issue-requests")]
    [InlineData("/api/business-console/v1/mes/dispatch-tasks")]
    [InlineData("/api/business-console/v1/mes/operation-tasks")]
    [InlineData("/api/business-console/v1/mes/wip")]
    [InlineData("/api/business-console/v1/mes/related-quality-items")]
    [InlineData("/api/business-console/v1/mes/finished-goods-receipt-requests")]
    [InlineData("/api/business-console/v1/mes/downtime-events")]
    [InlineData("/api/business-console/v1/mes/shift-handovers")]
    [InlineData("/api/business-console/v1/mes/capacity-impacts")]
    public void List_status_query_enum_equals_aggregate_value_domain(string path)
    {
        using var document = LoadSnapshot();
        var schema = document.RootElement
            .GetProperty("paths")
            .GetProperty(path)
            .GetProperty("get")
            .GetProperty("parameters")
            .EnumerateArray()
            .Single(parameter =>
                parameter.GetProperty("in").GetString() == "query"
                && parameter.GetProperty("name").GetString() == "status")
            .GetProperty("schema");

        AssertStatusEnumEqualsDomain(schema, $"{path} status query parameter", ExpectedStatusQueryValues(path));
    }

    /// <summary>取聚合上声明的全部 <c>public const string *Status</c> 值。</summary>
    private static string[] DeclaredStatusConstants<T>() =>
        typeof(T)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is { IsLiteral: true, FieldType: var type } && type == typeof(string))
            .Where(field => field.Name.EndsWith("Status", StringComparison.Ordinal))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

    private static string[] ExpectedStatusQueryValues(string path) => path switch
    {
        // 生产计划读的是带计划来源的工单，status 过滤的就是工单状态。
        "/api/business-console/v1/mes/work-orders" => WorkOrderStatuses,
        "/api/business-console/v1/mes/production-plans" => WorkOrderStatuses,
        "/api/business-console/v1/mes/material-issue-requests" => MaterialIssueRequestStatuses,
        "/api/business-console/v1/mes/dispatch-tasks" => OperationTaskStatuses,
        "/api/business-console/v1/mes/operation-tasks" => OperationTaskStatuses,
        "/api/business-console/v1/mes/wip" => OperationTaskStatuses,
        "/api/business-console/v1/mes/related-quality-items" => DefectRecordStatuses,
        "/api/business-console/v1/mes/finished-goods-receipt-requests" => FinishedGoodsReceiptRequestStatuses,
        "/api/business-console/v1/mes/downtime-events" => WorkCenterUnavailabilityStatuses,
        "/api/business-console/v1/mes/shift-handovers" => ShiftHandoverStatuses,
        "/api/business-console/v1/mes/capacity-impacts" => WorkCenterUnavailabilityStatuses,
        _ => throw new InvalidOperationException($"No MES status value domain registered for {path}."),
    };

    private static void AssertStatusEnumEqualsDomain(JsonElement schema, string description, string[] expected)
    {
        Assert.True(
            schema.TryGetProperty("enum", out var values),
            $"{description} must be an OpenAPI enum, not a free-form string.");

        // 枚举顺序不是契约的一部分，按序值集合比较。
        Assert.Equal(
            expected.Order(StringComparer.Ordinal),
            values.EnumerateArray().Select(value => value.GetString()!).Order(StringComparer.Ordinal));
    }

    /// <summary>读导入的 BusinessGateway OpenAPI 导出产物（见 csproj 的 EmbeddedResource）。</summary>
    private static string ReadSnapshot()
    {
        var assembly = typeof(MesListStatusContractTests).Assembly;
        using var stream = assembly.GetManifestResourceStream("business-gateway-console.v1.json")
            ?? throw new InvalidOperationException(
                "Embedded resource 'business-gateway-console.v1.json' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static JsonDocument LoadSnapshot() => JsonDocument.Parse(ReadSnapshot());

    private static JsonElement FindSchemaBySuffix(JsonDocument document, string schemaNameSuffix)
    {
        var schemaObject = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas");
        if (schemaObject.TryGetProperty(schemaNameSuffix, out var exactSchema))
        {
            return exactSchema;
        }

        var schemas = schemaObject
            .EnumerateObject()
            .Where(schema =>
                schema.Name.EndsWith(schemaNameSuffix, StringComparison.Ordinal) &&
                !schema.Name.Contains("ResponseDataOf", StringComparison.Ordinal))
            .ToArray();

        Assert.Single(schemas);
        return schemas[0].Value;
    }
}
