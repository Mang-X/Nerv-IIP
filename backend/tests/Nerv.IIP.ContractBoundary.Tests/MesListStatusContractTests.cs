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
///
/// <para><b>登记表之外的漂移怎么防</b>：见
/// <see cref="Every_mes_status_enum_is_registered"/> —— 它反向穷举 <c>BusinessConsoleMes*</c>
/// 下的全部内嵌 enum，不从登记表出发，所以新增一个带状态 enum 的行 schema 而忘了登记值域时必然红。</para>
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

    /// <summary>
    /// MES 契约前缀。列表 schema 的全名以此开头，穷举扫描时用它划定范围。
    /// </summary>
    private const string MesSchemaPrefix =
        "NervIIPBusinessGatewayWebApplicationBusinessServicesBusinessConsoleMes";

    /// <summary>
    /// 命名空间前缀，扫描产出 schema 短名时要剥掉它。剥完留下
    /// <c>BusinessConsoleMesXxx</c>，与 <see cref="RowStatusProperties"/> 登记表的键
    /// 同一形式 —— 剥到 <c>BusinessConsole</c> 就停，不要把 <c>Mes</c> 一起吃掉。
    /// </summary>
    private const string SchemaNamespacePrefix =
        "NervIIPBusinessGatewayWebApplicationBusinessServices";

    /// <summary>
    /// <b>不在 <see cref="RowStatusProperties"/> 值域守门范围内</b>的独立命名类型白名单。
    ///
    /// <para>这些不是行 schema 的内嵌状态属性，而是各自独立的 top-level enum 类型，
    /// 被别的 schema 以 <c>$ref</c> 引用。它们的值域不由 MES 域常量定义（安灯队列、
    /// 统计维度、统计降级原因等），所以不在「枚举 = 聚合真实值域」这条断言范围内。</para>
    ///
    /// <para><b>为什么必须显式列出</b>：<see cref="Every_mes_status_enum_is_registered"/> 会穷举
    /// <c>BusinessConsoleMes*</c> 下所有内嵌 enum 并要求每一条都登记在
    /// <see cref="RowStatusProperties"/> 里。少列一个，那条断言就红 —— 这是刻意的：
    /// 新增一个带状态 enum 的行 schema 而忘了在这里登记值域，必须立刻暴露，
    /// 而不是像原先那样「登记表外的 schema 根本进不来」（见 <see cref="RowStatusProperties"/>）。
    /// 确实不该纳管时，把类型名加进本白名单并写明理由。</para>
    /// </summary>
    private static readonly HashSet<string> NonRowStatusEnums =
    [
        "BusinessConsoleMesAndonCategory",
        "BusinessConsoleMesAndonQueue",
        "BusinessConsoleMesAndonStatus",
        "BusinessConsoleMesProductionStatisticsDegradedReason",
        "BusinessConsoleMesProductionStatisticsDimension",
        "BusinessConsoleMesProductionStatisticsResolutionStatus",
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

    /// <summary>
    /// <b>穷举守门：<c>BusinessConsoleMes*</c> 下的行内嵌 enum 必须全部登记在
    /// <see cref="RowStatusProperties"/> 里。</b>
    ///
    /// <para>前几轮这条守门只对「已登记的 10 个」生效：<c>FindSchemaBySuffix</c> 的
    /// <c>Assert.Single</c> 只在已登记后缀匹配到 ≥1 个时起作用，登记表外的 schema 根本进不来。
    /// 实测证伪：把 snapshot 里一个未登记的 <c>MesTelemetryCandidateRow.status</c> 改成任意
    /// enum，21 条用例仍全绿 —— 明天有人往 <c>RowStatusProperties</c> 之外加第 11 个行 schema，
    /// 门禁照绿。这正是本票前三轮反复出现的同一形状：断言没盖住它声称盖的行为。</para>
    ///
    /// <para><b>因此这里换个口径</b>：不从登记表出发逐条找，而是<b>反向穷举</b> ——
    /// 扫出 <c>BusinessConsoleMes*</c> 下所有 schema 的所有内嵌 enum，逐个问「你登记了吗」。
    /// 这样新增一个带状态 enum 的行 schema、而忘了登记值域时，本条必然红。</para>
    ///
    /// <para>独立命名的 top-level enum 类型（安灯 / 统计那 6 个）经 <c>$ref</c> 引用，
    /// 不是行内嵌状态属性，值域也不由 MES 域常量定义，按
    /// <see cref="NonRowStatusEnums"/> 排除。</para>
    /// </summary>
    [Fact]
    public void Every_mes_status_enum_is_registered()
    {
        using var document = LoadSnapshot();
        // 登记表存的是「schema 短名（含 BusinessConsoleMes 前缀）+ 属性名」，
        // 扫描产出的也是同一形式，两边逐字符可比。
        var registered = RowStatusProperties
            .Select(row => row[0] + "." + row[1])
            .ToHashSet(StringComparer.Ordinal);

        var unregistered = FindInlineEnums(document)
            .Select(inline => $"{inline.Schema}{inline.PropertyPath}")
            .Where(location => !registered.Contains(location))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            unregistered.Length == 0,
            "以下 MES 行 schema 的内嵌 enum 没有在 RowStatusProperties 里登记值域，"
                + "等于对「域新增状态 / 契约漂移」失守。逐条处理：确属状态枚举则补登记到 "
                + "RowStatusProperties 并指明对应聚合；确非状态枚举则把 schema 短名加进 "
                + "NonRowStatusEnums 白名单并写明理由。未登记项：\n"
                + string.Join("\n", unregistered.Select(location => $"  - {location}")));
    }

    /// <summary>
    /// 扫出 <c>BusinessConsoleMes*</c> 下所有 schema 的所有内嵌 enum（任意属性名、任意深度，
    /// 含 <c>items</c> 与 <c>oneOf</c>）。独立命名的 top-level enum 类型本身也返回一条记录，
    /// 由 <see cref="NonRowStatusEnums"/> 白名单排除。
    /// </summary>
    private static IEnumerable<(string Schema, string PropertyPath)> FindInlineEnums(JsonDocument document)
    {
        var schemas = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .EnumerateObject()
            .Where(schema => schema.Name.StartsWith(MesSchemaPrefix, StringComparison.Ordinal))
            .Where(schema => !schema.Name.Contains("ResponseDataOf", StringComparison.Ordinal));

        foreach (var schema in schemas)
        {
            // 去掉命名空间前缀，留下 "BusinessConsoleMesXxx"，
            // 与 RowStatusProperties 登记表里的键同一形式。
            var registeredName = schema.Name[SchemaNamespacePrefix.Length..];

            // 独立命名的 top-level enum 类型不纳管：它们经 $ref 被引用，值域也不由
            // MES 域常量定义（安灯 / 统计那几类）。跳过而不是登记。
            if (NonRowStatusEnums.Contains(registeredName))
            {
                continue;
            }

            foreach (var propertyPath in FindEnumPaths(schema.Value, string.Empty))
            {
                yield return (registeredName, propertyPath);
            }
        }
    }

    /// <summary>
    /// 递归收集一个 schema 内所有 enum 所在的位置（点号路径）。
    ///
    /// <para>两种形态都要认：属性写成 <c>{ "type": "string", "enum": [...] }</c>（最常见），
    /// 或直接把裸字符串数组写在属性值上。碰到 <c>enum</c> 键就以该属性的路径收一条，
    /// 不再往里递归 —— 否则同一条值域会被 <c>enum</c> 键和它下面的数组各记一次。</para>
    ///
    /// <para><c>properties</c> 是「属性名 → 属性 schema」的映射而非一个属性，所以它的
    /// 路径段直接是属性名（<c>.status</c> 而不是 <c>.properties.status</c>）；
    /// <c>required</c> 是必填属性**名字**的数组，与值域无关，必须跳过，
    /// 否则每个带 required 的请求体都会被误判成一条未登记枚举。</para>
    /// </summary>
    private static IEnumerable<string> FindEnumPaths(JsonElement schema, string path)
    {
        if (schema.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in schema.EnumerateObject())
            {
                if (property.Name == "properties")
                {
                    foreach (var field in property.Value.EnumerateObject())
                    {
                        foreach (var found in FindEnumPaths(field.Value, path + "." + field.Name))
                        {
                            yield return found;
                        }
                    }

                    continue;
                }

                // 独立命名的 enum 经 $ref 引用，不是本 schema 的内嵌值域。
                if (property.Name is "$ref" or "description" or "title" or "example" or "default" or "required")
                {
                    continue;
                }

                // NSwag 的厂商扩展：与同一个 enum 键配对给出显示名，不是独立值域。
                if (property.Name.StartsWith("x-", StringComparison.Ordinal))
                {
                    continue;
                }

                if (property.Name == "enum")
                {
                    yield return path;
                    continue;
                }

                foreach (var found in FindEnumPaths(property.Value, path + "." + property.Name))
                {
                    yield return found;
                }
            }

            yield break;
        }

        if (schema.ValueKind == JsonValueKind.Array)
        {
            if (IsEnumNode(schema))
            {
                yield return path;
                yield break;
            }

            foreach (var item in schema.EnumerateArray())
            {
                foreach (var found in FindEnumPaths(item, path + "[]"))
                {
                    yield return found;
                }
            }
        }
    }

    /// <summary>
    /// 不在路径侧穷举守门范围内的 MES 列表路径，各带原因。
    ///
    /// <para>这两条路径的 <c>status</c> 查询参数<b>根本没有 enum</b>（自由 string），
    /// 不属于「枚举被统一并集覆盖」这一类漂移，因此不是 #3912 的收窄对象。
    /// 把它们列进来是因为否则它们会被「新增带 status 参数的路径必须登记」这条卡住 ——
    /// 登记的前提是有一个域值域可登记，而它们目前没有。</para>
    ///
    /// <para>它们真正的缺陷是<b>该有值域却没有</b>（自由 string 等于把过滤语义交给调用方），
    /// 方向与本票相反，属独立票，不在本次修。</para>
    /// </summary>
    private static readonly Dictionary<string, string> NonListStatusQueryPaths = new()
    {
        // 可报工工序任务：status 是自由 string，读面按可报工判据过滤，未声明枚举值域。
        ["/api/business-console/v1/mes/reportable-operation-tasks"] =
            "status 无 enum，自由 string；属「缺值域」而非「值域被覆盖」，独立票处理。",
        // 遥测生产报告候选：同上，status 无 enum。
        ["/api/business-console/v1/mes/telemetry-production-report-candidates"] =
            "status 无 enum，自由 string；属「缺值域」而非「值域被覆盖」，独立票处理。",
    };

    /// <summary>
    /// <b>与 <see cref="Every_mes_status_enum_is_registered"/> 对称的路径侧穷举</b>：
    /// <c>/api/business-console/v1/mes/**</c> 下所有带 <c>status</c> 查询参数的 GET 路径，
    /// 都必须登记在 <see cref="List_status_query_enum_equals_aggregate_value_domain"/> 的
    /// <c>InlineData</c> 里，或在 <see cref="NonListStatusQueryPaths"/> 里写明排除原因。
    ///
    /// <para>同样是反向穷举：新增一条带 status 查询参数的 MES 列表路径而忘了登记值域时，
    /// 本条必然红。只登记不穷举的话，那条新路径会带着一个未经域常量核对的值域直接进契约。</para>
    /// </summary>
    [Fact]
    public void Every_mes_list_status_query_is_registered()
    {
        using var document = LoadSnapshot();
        var registered = new HashSet<string>(
            [
                "/api/business-console/v1/mes/work-orders",
                "/api/business-console/v1/mes/production-plans",
                "/api/business-console/v1/mes/material-issue-requests",
                "/api/business-console/v1/mes/dispatch-tasks",
                "/api/business-console/v1/mes/operation-tasks",
                "/api/business-console/v1/mes/wip",
                "/api/business-console/v1/mes/related-quality-items",
                "/api/business-console/v1/mes/finished-goods-receipt-requests",
                "/api/business-console/v1/mes/downtime-events",
                "/api/business-console/v1/mes/shift-handovers",
                "/api/business-console/v1/mes/capacity-impacts",
            ],
            StringComparer.Ordinal);

        var unregistered = document.RootElement
            .GetProperty("paths")
            .EnumerateObject()
            .Where(path => path.Name.StartsWith("/api/business-console/v1/mes/", StringComparison.Ordinal))
            .Where(path => path.Value.TryGetProperty("get", out var get)
                && get.TryGetProperty("parameters", out var parameters)
                && parameters.EnumerateArray().Any(parameter =>
                    parameter.GetProperty("in").GetString() == "query"
                    && parameter.GetProperty("name").GetString() == "status"))
            .Select(path => path.Name)
            .Where(path => !registered.Contains(path))
            .Where(path => !NonListStatusQueryPaths.ContainsKey(path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            unregistered.Length == 0,
            "以下 MES 列表路径带 status 查询参数但没有登记值域，等于对「域新增状态 / 契约漂移」"
                + "失守。请在 List_status_query_enum_equals_aggregate_value_domain 补一条 InlineData "
                + "并指明该路径过滤的是哪个聚合；确实不该带 status 的路径请从契约移除该参数；"
                + "确属票外（如同 NonListStatusQueryPaths 那两条自由 string 的）请连同原因加进该表。"
                + "未登记路径：\n"
                + string.Join("\n", unregistered.Select(path => $"  - {path}")));
    }

    private static bool IsEnumNode(JsonElement node) =>
        node.ValueKind == JsonValueKind.Array
        && node.GetArrayLength() > 0
        && node.EnumerateArray().All(value => value.ValueKind == JsonValueKind.String);

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
