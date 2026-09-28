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
/// <para><b>守门判据的根基：值域归属，不是属性命名（#3912 守门六轮演化的结论）</b>。
/// 前五轮依次用过「属性名以 <c>Status</c> 结尾」「(schema, 属性) 登记表」「跟随 <c>$ref</c>」
/// 「不跟随 <c>$ref</c> + (schema, 属性) 登记表」，每一轮都被找到了新旁路：
/// 改名 <c>state</c>/<c>phase</c> 即绕过 <c>$ref</c> 形态、把枚举藏进 <c>*ResponseDataOf*</c>
/// 包装层即绕过、<c>items[]</c> 元素 <c>$ref</c> 到白名单枚举即绕过。
/// 共同点是<b>都在认「这个位置像不像状态」</b>，而位置是可以随便改的。
/// 现在判据只认一件事：<b>这个枚举的值域等于本票哪个聚合的域值域</b>。
/// 值域是聚合的客观事实，改名、换包装层、换引用形态都改不动它。</para>
///
/// <para>登记表之外的漂移由 <see cref="Every_mes_enum_value_domain_has_an_owner"/> 和
/// <see cref="Aggregated_status_domains_are_declared_only_by_registered_schemas"/> 兜底：
/// 面上每个枚举值域都必须说得出属于谁（要么等于本票聚合域值域，要么在
/// <see cref="NonAggregatedDomains"/> 里写明理由），而本票的值域只允许由登记的行 schema 就地声明。</para>
/// </summary>
public sealed class MesListStatusContractTests
{
    private static readonly string[] WorkOrderStatuses = WorkOrder.AllStatuses.ToArray();

    private static readonly string[] OperationTaskStatuses = Enum.GetNames<OperationTaskLifecycleStatus>();

    private static readonly string[] MaterialIssueRequestStatuses =
        DeclaredStatusConstants<MaterialIssueRequest>();

    private static readonly string[] FinishedGoodsReceiptRequestStatuses =
        DeclaredStatusConstants<FinishedGoodsReceiptRequest>();

    private static readonly string[] DefectRecordStatuses = DeclaredStatusConstants<DefectRecord>();

    private static readonly string[] ShiftHandoverStatuses = DeclaredStatusConstants<ShiftHandover>();

    private static readonly string[] WorkCenterUnavailabilityStatuses =
        [WorkCenterUnavailability.OpenStatus, WorkCenterUnavailability.RecoveredStatus];

    /// <summary>
    /// 行属性上的状态：schema 短名 + 属性名 + 该聚合的真实值域。
    /// 这张表是本票状态登记的<b>唯一来源</b>：逐条比对用它，两条反向穷举的归属集合
    /// 由它派生（<see cref="StatusDeclarers"/>），不再另抄一份。
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

    /// <summary>MES 契约的 schema 命名空间前缀，扫描时用它划定范围。</summary>
    private const string MesSchemaPrefix =
        "NervIIPBusinessGatewayWebApplicationBusinessServicesBusinessConsoleMes";

    /// <summary>
    /// 剥掉命名空间前缀，留下 <c>BusinessConsoleMesXxx</c>，与登记表同一形式。
    /// 剥到 <c>BusinessConsole</c> 就停 —— 不能和 <see cref="MesSchemaPrefix"/> 用同一个常量，
    /// 那会把 <c>Mes</c> 一起吃掉，键就与登记表对不上了。
    /// </summary>
    private const string SchemaNamespacePrefix =
        "NervIIPBusinessGatewayWebApplicationBusinessServices";

    /// <summary>
    /// <b>本票聚合的值域集合</b>，从 <see cref="RowStatusProperties"/> 派生，<b>不另抄一份</b>。
    ///
    /// <para>早先这里有一张手抄的 <c>StatusDeclarers</c>（schema 短名 → 属性名 + 值域）。它是
    /// <see cref="RowStatusProperties"/> 的第二份拷贝，两份可以各改各的：只从
    /// <c>StatusDeclarers</c> 摘掉一项而 <c>RowStatusProperties</c> 不动，两条反向穷举的
    /// 「归属集合」就少了一个值域 —— 而这个值域同时还在 <c>NonAggregatedDomains</c> 里
    /// （工序任务与停机/产能各被多个读面共用），于是唯一一处约束消失、守门照绿。
    /// 派生而非拷贝，这条旁路就不存在了：两张断言的归属集合与逐条比对的集合是同一个。</para>
    ///
    /// <para>这张派生表回答的是「哪个 schema 就地声明了本票状态」，实测每个值域恰好一个声明者
    /// （<c>*ListResponse</c> 包装层只经 <c>items[].$ref</c> 传递含有、不就地声明）。</para>
    /// </summary>
    private static readonly Dictionary<string, string[]> StatusDeclarers = RowStatusProperties
        .Select(row => ((string)row[0], (string[])row[2]))
        .ToDictionary(entry => entry.Item1, entry => entry.Item2, StringComparer.Ordinal);

    /// <summary>
    /// 本票聚合之外的值域显式登记表：值域 → 为什么不归本票管。
    ///
    /// <para>这是判据的一部分，不是逃生口 —— 表里没有的值域既不是本票状态、又没写明理由，直接判红。
    /// 实测面上共 15 个唯一值域：本票 6 个（覆盖 7 个读面聚合 —— 工序任务聚合被 4 个读面共用、
    /// 停机与产能共用一个 2 值域）+ 下表 9 个，逐条写明理由。键为「排好序、用空格分隔」的值域串。</para>
    ///
    /// <para><b>为什么必须逐条写、不能用「属性名不像 Status」筛</b>：那正是实测出的旁路 ——
    /// 属性改名 <c>state</c> / <c>phase</c> 即绕过。名字能改，值域不能。
    /// 反过来完全跟随 <c>$ref</c> 也不可行：会把 57 个非状态 <c>$ref</c> 连同
    /// <c>GET</c> / <c>confirmed</c> / <c>accepted</c> 这类回执字面量一起收进判定面。
    /// 用<b>值域</b>划界，两个问题同时消失：改名无效，包装层与 <c>items[]</c> 引用也无处藏身。</para>
    /// </summary>
    private static readonly Dictionary<string, string> NonAggregatedDomains =
        new(StringComparer.Ordinal)
        {
            ["equipment materialShortage process quality"] =
                "安灯类别（AndonCategory），不是聚合状态值域。",
            ["claimed closed open"] =
                "安灯呼叫状态（AndonCallStatus，AndonCall.cs 的域枚举），不是本票 7 个读面聚合的状态。",
            ["all awaitingResponse unclosed"] =
                "安灯队列过滤值（AndonQueue），是筛选面取值不是状态值域。",
            ["day shift sku workCenter"] =
                "生产统计维度（ProductionStatisticsDimension），是统计口径不是状态。",
            ["degraded resolved"] =
                "生产统计快照解析状态（ResolutionStatus），不是聚合生命周期状态。",
            ["historicalDimensionLegacyUnresolved historicalDimensionSnapshotDegraded "
                + "historicalLocalTimeAmbiguous historicalLocalTimeInvalid "
                + "historicalReportOutsideShiftWindow historicalShiftDefinitionInvalid "
                + "historicalShiftDefinitionMissing historicalTimezoneInvalid "
                + "historicalTimezoneMissing nonPositiveTotalOutput workCenterMissing"] =
                "生产统计降级原因（DegradedReason），是原因码不是状态值域。",
            ["GET"] = "工序动作回执里的 HTTP 方法字面量。",
            ["confirmed"] = "工序动作回执的确认位字面量。",
            ["accepted"] = "工单转序回执的受理位字面量。",
        };

    [Theory]
    [MemberData(nameof(RowStatusProperties))]
    public void Row_status_enum_equals_aggregate_value_domain(
        string schemaNameSuffix,
        string propertyName,
        string[] expected)
    {
        using var document = LoadSnapshot();
        var property = FindSchemaByShortName(document, schemaNameSuffix)
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

    /// <summary>
    /// <b>豁免表不能谎报</b>：<see cref="NonAggregatedDomains"/> 的任何一条键都不得等于
    /// 任一本票聚合的真实状态值域。
    ///
    /// <para><b>为什么这条独立于其余断言、且判据必须取域常量</b>：豁免表是「沉默的归属表」——
    /// 它把一个值域判成「不归本票管」，而其余断言只问「值域有没有归属」，两者不矛盾。
    /// 于是一条真实的漂移可以这样绕过去：域里给聚合加一个新状态（比如
    /// <c>WorkOrder.AllStatuses</c> 多一个 <c>brandnew</c>）而契约不跟进，
    /// 同时把豁免表的键改成这个新值域，并把该聚合的读面从 <see cref="RowStatusProperties"/> 摘掉 ——
    /// 此时该聚合相关的逐条比对整体消失，守门照绿。这正是本票要防的那类漂移。</para>
    ///
    /// <para><b>判据取 <see cref="AggregateValueDomains"/>（七个域常量数组本身），不取
    /// <see cref="StatusDeclarers"/> 的派生值</b>：上面那条绕法第二步正是「掏空派生表」，
    /// 用派生值判据的话断言会跟着一起失效。这里要判的是「域里真实存在哪些状态」，
    /// 那是域常量的性质，与任何一张登记表的当前内容无关。</para>
    ///
    /// <para>新增豁免项时本条一并把关：只有确实不由本票任何聚合定义的值域才允许加进来。</para>
    /// </summary>
    [Fact]
    public void Every_exempt_value_domain_is_genuinely_non_aggregated()
    {
        var aggregated = AggregateValueDomains
            .Select(Normalize)
            .ToHashSet(StringComparer.Ordinal);

        var lying = NonAggregatedDomains.Keys
            .Where(domain => aggregated.Contains(domain))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            lying.Length == 0,
            "NonAggregatedDomains 里有键等于本票聚合的真实状态值域 —— 豁免表谎报了归属，"
                + "等于把该聚合的状态整体移出守门范围。本仓只允许加「值域确实不由本票任何聚合的"
                + "域常量定义」的项。违规项：\n"
                + string.Join("\n", lying.Select(domain => "  - " + domain)));
    }

    /// <summary>
    /// 本票 7 个 MES 读面聚合的真实状态值域，<b>直接取自域常量</b>，不经任何登记表派生。
    /// </summary>
    private static IEnumerable<string[]> AggregateValueDomains =>
    [
        WorkOrderStatuses,
        OperationTaskStatuses,
        MaterialIssueRequestStatuses,
        FinishedGoodsReceiptRequestStatuses,
        DefectRecordStatuses,
        ShiftHandoverStatuses,
        WorkCenterUnavailabilityStatuses,
    ];

    /// <summary>
    /// <b>值域归属穷举</b>：<c>BusinessConsoleMes*</c> 面上每个枚举值域都必须说得出属于谁 ——
    /// 要么等于本票某个聚合的域值域，要么在 <see cref="NonAggregatedDomains"/> 里写明不属于本票的理由。
    ///
    /// <para>上面 22 条逐条比对只在<b>已登记</b>的位置生效：登记表里没写的地方冒出一个等于本票状态
    /// 值域的枚举，逐条比对看不见。这条从值域侧反过来穷举，补上那个缺口。</para>
    ///
    /// <para>扫描跟随 <c>$ref</c> 解析到实际值域，所以藏在 <c>*ResponseDataOf*</c> 包装层、
    /// 藏在 <c>items[]</c> 元素上、经几层间接引用都算同一处；环状 <c>$ref</c> 由已访问集合截断。</para>
    /// </summary>
    [Fact]
    public void Every_mes_enum_value_domain_has_an_owner()
    {
        using var document = LoadSnapshot();
        var owned = StatusDeclarers
            .Values
            .Select(declarer => Normalize(declarer))
            .Concat(NonAggregatedDomains.Keys)
            .ToHashSet(StringComparer.Ordinal);

        var unowned = CollectEnumValueDomains(document)
            .Where(domain => !owned.Contains(domain))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            unowned.Length == 0,
            "以下枚举值域既不等于本票 7 个 MES 读面聚合的域值域、也没在 NonAggregatedDomains 里写明理由 —— "
                + "无法判断它该归谁。确属本票聚合状态则补登记到 StatusDeclarers（并确认对应 schema 的"
                + "属性已进 RowStatusProperties）；确属其他读面则在 NonAggregatedDomains 加一条并写明理由。"
                + "未认领的值域：\n"
                + string.Join("\n", unowned.Select(domain => "  - " + domain)));
    }

    /// <summary>
    /// <b>本票状态值域只能由登记表里的 schema 声明。</b>
    ///
    /// <para><see cref="Every_mes_enum_value_domain_has_an_owner"/> 只保证「每个值域都说得出属于谁」，
    /// 这一条保证「本票的状态没跑到别处去」：<b>不跟随 <c>$ref</c>、就地声明</b>某个聚合域值域的
    /// schema，只允许是 <see cref="StatusDeclarers"/> 登记的那 10 个行 schema。
    /// 新增一个带本票状态值域的 schema 而忘了登记，这里必然红 —— 与那个属性叫什么名字无关。</para>
    ///
    /// <para><b>为什么只判「声明」而不判「携带」</b>：NSwag 为每个列表生成
    /// <c>XxxListResponse</c> / <c>XxxResponse</c> 包装层，它通过 <c>items[].$ref</c> 指向行 schema，
    /// 于是<b>传递地</b>含有该值域，但自己并不声明任何状态。实测 10 个包装层全部是这种形态。
    /// 把传递携带也判红，等于要求连 NSwag 的响应包装都登记一遍 —— 那是生成物，不是契约。
    /// 判「声明」则恰好卡在真问题上：谁真正写出了这个状态枚举，那个 schema 必须在登记表里。</para>
    /// </summary>
    [Fact]
    public void Aggregated_status_domains_are_declared_only_by_registered_schemas()
    {
        using var document = LoadSnapshot();
        var aggregatedDomains = StatusDeclarers
            .Values
            .Select(declarer => Normalize(declarer))
            .ToHashSet(StringComparer.Ordinal);

        var strays = new List<string>();
        foreach (var schemaName in MesSchemaNames(document))
        {
            if (StatusDeclarers.ContainsKey(schemaName))
            {
                continue;
            }

            foreach (var domain in FindLocalEnumDomains(document, FindSchema(document, schemaName)))
            {
                if (aggregatedDomains.Contains(Normalize(domain)))
                {
                    strays.Add($"{schemaName}: {Normalize(domain)}");
                }
            }
        }

        Assert.True(
            strays.Count == 0,
            "以下 schema 就地声明了本票聚合的状态值域，但不在 StatusDeclarers 登记的 10 个行 schema 里。"
                + "若它确实是同一聚合的列表行，请补登记到 StatusDeclarers 与 RowStatusProperties；"
                + "若是别处复用同一值域，请在 NonAggregatedDomains 写明理由。游离项：\n"
                + string.Join("\n", strays.Select(stray => "  - " + stray)));
    }

    /// <summary>
    /// <b>路径侧穷举</b>：<c>/api/business-console/v1/mes/**</c> 下所有带 <c>status</c> 查询参数的
    /// GET 路径，都必须登记在 <see cref="List_status_query_enum_equals_aggregate_value_domain"/> 的
    /// <c>InlineData</c> 里，或在 <see cref="NonListStatusQueryPaths"/> 里写明排除原因。
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
            .Where(path => !registered.Contains(path) && !NonListStatusQueryPaths.ContainsKey(path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            unregistered.Length == 0,
            "以下 MES 列表路径带 status 查询参数但没有登记值域。请在 "
                + "List_status_query_enum_equals_aggregate_value_domain 补一条 InlineData 并指明该路径"
                + "过滤的是哪个聚合；确实不该带 status 的请从契约移除该参数；确属票外的"
                + "（如同 NonListStatusQueryPaths 那两条自由 string 的）请连同原因加进该表。"
                + "未登记路径：\n"
                + string.Join("\n", unregistered.Select(path => $"  - {path}")));
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

    /// <summary>不在路径侧穷举守门范围内的 MES 列表路径，各带原因。</summary>
    private static readonly Dictionary<string, string> NonListStatusQueryPaths = new()
    {
        // 可报工工序任务：status 是自由 string，读面按可报工判据过滤，未声明枚举值域。
        ["/api/business-console/v1/mes/reportable-operation-tasks"] =
            "status 无 enum，自由 string；属「缺值域」而非「值域被覆盖」，独立票处理。",
        // 遥测生产报告候选：同上，status 无 enum。
        ["/api/business-console/v1/mes/telemetry-production-report-candidates"] =
            "status 无 enum，自由 string；属「缺值域」而非「值域被覆盖」，独立票处理。",
    };

    private static void AssertStatusEnumEqualsDomain(JsonElement schema, string description, string[] expected)
    {
        Assert.True(
            schema.TryGetProperty("enum", out var values),
            $"{description} must be an OpenAPI enum, not a free-form string.");

        // 枚举顺序不是契约的一部分，按排好序的值集合比较。
        Assert.Equal(
            expected.Order(StringComparer.Ordinal),
            values.EnumerateArray().Select(value => value.GetString()!).Order(StringComparer.Ordinal));
    }

    /// <summary>把值域排好序后拼成登记表的键形式：顺序不是契约的一部分，值域才是。</summary>
    private static string Normalize(IEnumerable<string> domain) =>
        string.Join(' ', domain.Order(StringComparer.Ordinal));

    /// <summary>面上所有 <c>BusinessConsoleMes*</c> schema 的短名（剥掉命名空间前缀）。</summary>
    private static string[] MesSchemaNames(JsonDocument document) => document.RootElement
        .GetProperty("components")
        .GetProperty("schemas")
        .EnumerateObject()
        .Select(schema => schema.Name)
        .Where(name => name.StartsWith(MesSchemaPrefix, StringComparison.Ordinal))
        .Select(name => name[SchemaNamespacePrefix.Length..])
        .Order(StringComparer.Ordinal)
        .ToArray();

    /// <summary>面上所有唯一枚举值域，排好序去重。</summary>
    private static string[] CollectEnumValueDomains(JsonDocument document) => document.RootElement
        .GetProperty("components")
        .GetProperty("schemas")
        .EnumerateObject()
        .Where(schema => schema.Name.StartsWith(MesSchemaPrefix, StringComparison.Ordinal))
        .SelectMany(schema => CollectEnumValueDomains(document, schema.Name[SchemaNamespacePrefix.Length..]))
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();

    /// <summary>
    /// 单个 schema 内所有枚举值域（跟随 <c>$ref</c> 解析到实际值域），排好序去重。
    /// 已访问集合按 <c>$ref</c> 目标名截断，环状引用不会无限展开。
    /// </summary>
    private static string[] CollectEnumValueDomains(JsonDocument document, string shortSchemaName) =>
        FindEnumDomains(
                document,
                FindSchema(document, shortSchemaName),
                new HashSet<string>(StringComparer.Ordinal))
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// 在一个 schema 节点内找出所有枚举值域，<b>跟随 <c>$ref</c> 解析到目标类型</b>。
    ///
    /// <para>跟随的理由（与被否决的旧方案不同）：旧方案把「属性路径」当判据，跟随只是为了拿路径；
    /// 现在判据是值域本身，<c>$ref</c> 背后的值域和内联的值域在判据上没有区别，所以必须跟随 ——
    /// 否则把状态藏到 <c>$ref</c> 后面就等于逃出判定面。隐藏位置因此只剩「换名字」一种，
    /// 而换名字在值域判据下无效。</para>
    ///
    /// <para><c>enum</c> 键下不再往里递归，否则同一条值域会被记两次；
    /// <c>required</c> 是字段名数组不是值域；<c>x-enumNames</c> 是 NSwag 的显示名扩展不是值域。</para>
    /// </summary>
    /// <summary>
    /// 与 <see cref="FindEnumDomains"/> 相同，但<b>不跟随 <c>$ref</c></b>：只返回本 schema
    /// 就地写出的值域，引用到别处的值域不算「它声明的」。
    /// </summary>
    private static IEnumerable<string[]> FindLocalEnumDomains(JsonDocument document, JsonElement node)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                if (TryReadStringArray(node, "enum", out var enumValues))
                {
                    yield return enumValues;
                }

                foreach (var property in node.EnumerateObject())
                {
                    if (property.Name is "enum" or "description" or "title" or "example" or "default"
                        or "required" or "x-enumNames" or "$ref")
                    {
                        continue;
                    }

                    foreach (var found in FindLocalEnumDomains(document, property.Value))
                    {
                        yield return found;
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in node.EnumerateArray())
                {
                    foreach (var found in FindLocalEnumDomains(document, item))
                    {
                        yield return found;
                    }
                }

                break;
        }
    }

    private static IEnumerable<string[]> FindEnumDomains(
        JsonDocument document,
        JsonElement node,
        HashSet<string> visitedRefs)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                if (TryReadStringArray(node, "enum", out var enumValues))
                {
                    yield return enumValues;
                }

                foreach (var property in node.EnumerateObject())
                {
                    switch (property.Name)
                    {
                        case "enum":
                        case "description":
                        case "title":
                        case "example":
                        case "default":
                        case "required":
                        case "x-enumNames":
                            continue;

                        case "$ref":
                            var reference = property.Value.GetString();
                            if (reference is not null
                                && reference.StartsWith("#/components/schemas/", StringComparison.Ordinal)
                                && visitedRefs.Add(reference)
                                && TryFindSchemaByReference(document, reference, out var target))
                            {
                                foreach (var found in FindEnumDomains(document, target, visitedRefs))
                                {
                                    yield return found;
                                }
                            }

                            continue;

                        default:
                            foreach (var found in FindEnumDomains(document, property.Value, visitedRefs))
                            {
                                yield return found;
                            }

                            continue;
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in node.EnumerateArray())
                {
                    foreach (var found in FindEnumDomains(document, item, visitedRefs))
                    {
                        yield return found;
                    }
                }

                break;
        }
    }

    /// <summary>读一个非空、全字符串的 JSON 数组；不是这个形态就当它不是值域。</summary>
    private static bool TryReadStringArray(JsonElement node, string name, out string[] values)
    {
        values = [];
        if (!node.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var items = array.EnumerateArray().ToArray();
        if (items.Length == 0 || items.Any(value => value.ValueKind != JsonValueKind.String))
        {
            return false;
        }

        values = items.Select(value => value.GetString()!).ToArray();
        return true;
    }

    private static bool TryFindSchemaByReference(JsonDocument document, string reference, out JsonElement schema)
    {
        schema = default;
        return document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .TryGetProperty(reference["#/components/schemas/".Length..], out schema);
    }

    private static JsonElement FindSchema(JsonDocument document, string shortSchemaName)
    {
        Assert.True(
            document.RootElement
                .GetProperty("components")
                .GetProperty("schemas")
                .TryGetProperty(SchemaNamespacePrefix + shortSchemaName, out var schema),
            $"Schema {shortSchemaName} is missing from the snapshot.");
        return schema;
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

    private static JsonElement FindSchemaByShortName(JsonDocument document, string schemaNameSuffix)
    {
        var schemaObject = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas");
        if (schemaObject.TryGetProperty(SchemaNamespacePrefix + schemaNameSuffix, out var exactSchema))
        {
            return exactSchema;
        }

        var matches = schemaObject
            .EnumerateObject()
            .Where(schema => schema.Name.EndsWith(schemaNameSuffix, StringComparison.Ordinal))
            .ToArray();

        Assert.Single(matches);
        return matches[0].Value;
    }
}
