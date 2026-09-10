using System.Reflection;
using FluentValidation;
using FluentValidation.Internal;
using FluentValidation.Validators;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Commands;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockCounts;
using Nerv.IIP.Business.Maintenance.Web.Application.Commands;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.LifecycleAuditAggregate;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Commands.StandardOperations;
using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionTasks;
using Nerv.IIP.Business.Quality.Web.Application.Commands.NonconformanceReports;
using Nerv.IIP.Business.Quality.Web.Application.Commands.QualityReasons;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Coding;

using DemandPlanningDbContext = Nerv.IIP.Business.DemandPlanning.Infrastructure.ApplicationDbContext;
using ErpDbContext = Nerv.IIP.Business.Erp.Infrastructure.ApplicationDbContext;
using MasterDataDbContext = Nerv.IIP.Business.MasterData.Infrastructure.ApplicationDbContext;
using MesDbContext = Nerv.IIP.Business.Mes.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

/// <summary>
/// 网关端点级幂等键长度上界与其下游权威上界之间的机器可验关系（#3284）。
/// </summary>
/// <remarks>
/// <para><b>被证的真不变量</b>：对每个带端点级 <c>MaximumLength</c> 规则的网关请求类型，
/// <c>网关端点级上界 ≤ 该端点下游的权威上界</c>——即「网关对外承诺的绝不比下游能接受的宽」。
/// 反方向（网关比下游窄，把合法键挡在门外）由 #3287 承担，本类**不**断言。</para>
///
/// <para><b>为什么断言端点级规则值本身，而不是 <c>min(端点级规则值, 全局钳 150)</c></b>：
/// 全局钳（<c>BusinessGatewayIdempotencyKey.MaximumLength = 150</c>）会把一切压到 150。
/// min 形式**只对 MasterData 生命周期开关那一处**（原值 512、下游列宽 200）恒真——
/// <c>min(512, 150) = 150 ≤ 200</c>，512 照样通过，那一格零鉴别力。
/// 对本票另外 5 处 150→128，min 形式**仍会红**（<c>min(150, 150) = 150 &gt; 128</c>），并非零鉴别力。
/// 即便如此这里也不采用 min 形式：它把「两个互不知情的常量偶然盖住一个洞」写进契约，
/// 任何人调大或去掉全局钳，端点级的死值立刻能打出 22001。</para>
///
/// <para><b>枚举面从哪来（不是手写名单）</b>：网关端点级位点由**反射网关程序集**得到——
/// 枚举所有可无参构造的 <see cref="IValidator"/> 实现，实例化后读它建出来的规则，
/// 取落在 <c>IdempotencyKey</c> 成员上的 <see cref="ILengthValidator"/> 分量。
/// 新增一个带该规则的端点会自动进入值域，若未在 <see cref="DownstreamBounds"/> 登记即红
/// （<see cref="Every_gateway_endpoint_level_bound_is_covered_by_the_downstream_map"/>）。
/// 枚举时被丢弃的类型不允许静默消失：
/// <see cref="Gateway_validator_enumeration_discards_nothing"/> 断言丢弃集为空。</para>
///
/// <para><b>下游权威上界怎么解析（不是手抄数字）</b>：<see cref="DownstreamBounds"/> 里登记的是
/// <c>typeof(下游命令)</c> 与 <c>(DbContext, 实体, 属性)</c> 这样的**类型引用**，
/// 具体数值在运行时从下游校验器的规则、或从下游 EF 模型的 <c>GetMaxLength()</c> 读出。
/// 下游把列宽收窄、或把校验器上界改小，本类立刻红；改名或删除则解析失败也红
/// （<see cref="Every_declared_downstream_authority_resolves"/>）。多重权威取最小（#3281 判据）。</para>
///
/// <para><b>登记「列宽」是有前提的</b>：列宽只有在**该列真的存放调用方原始键**时才构成对原始键的约束。
/// 若下游 handler 在落库前对键做了派生（哈希、拼前缀、截断），列宽约束的是派生值、不是原始键，
/// 拿它当权威就是幽灵权威——变异那一格能红，但红的不是真不变量。
/// 因此每条 <see cref="ColumnWidthBound"/> 登记前都必须实读写入点确认「写进去的就是入参本身」。
/// Wms 的两张 receipt 表已实测为派生值（<c>WmsText.IdempotencyKey</c> 无条件 SHA256，
/// 产出定长 75 字符 <c>wms-key-v2:&lt;64 hex&gt;</c>），故**不**在此登记。</para>
///
/// <para><b>本类不证明什么（写清楚，避免「护栏自称完备」）</b>：</para>
/// <list type="number">
/// <item>「网关请求 → 下游命令 / 下游列」这条**链接本身是手写的**。网关是 HTTP 代理，
/// 转发目标由客户端方法里的路径字面量决定，服务内部再经 endpoint → command 一跳；
/// 两跳都不是静态可达的类型关系，静态反推需要 IL 分析、运行时反推需要起网关并逐端点发请求，
/// 都超出本票射程。链接写错的**方向**是：指向一个更宽的下游 ⇒ 假绿。链接失效（改名/删除）⇒ 红。</item>
/// <item>不证明下游校验器上界与下游列宽一致——那是各服务自己的契约
/// （Inventory 有 <c>InventoryIdempotencyKeyLengthContractTests</c>，其它服务不一定有）。</item>
/// <item>不证明 118 个带 <c>IdempotencyKey</c> 的网关请求里那些**没有**端点级规则的位点安全——
/// 它们只吃全局钳，归 #3287。</item>
/// <item>不覆盖 CAP 事件信封键（<c>EventIds.Idempotency(...)</c> 产出、落 inbox 的 512/500/300 那些）：
/// 那不是网关承诺的值域。</item>
/// <item>不覆盖「验证类型是泛型形参」的开放泛型校验器（今日网关侧为 0，
/// 由 <see cref="Gateway_validator_enumeration_discards_nothing"/> 兜住；见该方法注释里的判定边界）。</item>
/// </list>
///
/// <para><b>本类是过渡态实现（跟进票 #3301）</b>：理想形态是各服务把幂等键上界提成 <c>Contracts.*</c>
/// 里的公开常量，网关端点直接引用同一个常量，这条关系就退化成编译期恒等、不需要测试。
/// 该路线需要为网关 <c>.csproj</c> 新增 6 个 <c>ProjectReference</c>
/// （Wms / MasterData / Maintenance / Quality / DemandPlanning / ProductEngineering 的 Contracts），
/// 而新增 <c>ProjectReference</c> 会打破 restore 锁定合同（PR #2693 实证，该 lane 未接门禁），
/// 已在 #3301 定级为 scope:L 的独立任务。**本文件的存在本身就是那件事没做的证据**，
/// 不要把它当终态维护、也不要在它上面继续加登记项来替代 #3301。</para>
/// </remarks>
public sealed class BusinessGatewayIdempotencyKeyDownstreamBoundContractTests
{
    private const string IdempotencyKeyPropertyName = "IdempotencyKey";

    [Fact]
    public void Every_gateway_endpoint_level_bound_is_covered_by_the_downstream_map()
    {
        var sites = GatewayEndpointLevelBounds();

        // 值域非空：正则/反射一旦失配就会退化成空集，而 Assert.All 对空集恒真。
        Assert.NotEmpty(sites);

        // 比对用 Type 而不是 Type.Name：同名不同命名空间（V2 家族）会让登记挂到错的类型上，
        // 而按名字比对的完备性断言照绿，判定测试却按 Type 键取不到值。
        var enumerated = sites.Keys.ToHashSet();
        var declared = DownstreamBounds.Keys.ToHashSet();

        var unregistered = enumerated.Except(declared).ToArray();
        var stale = declared.Except(enumerated).ToArray();

        Assert.True(
            unregistered.Length == 0 && stale.Length == 0,
            $"网关新增了端点级幂等键上界但未登记下游权威（{unregistered.Length}）：{Describe(unregistered)}"
            + $"；登记表里的位点在网关侧已不存在端点级上界（{stale.Length}）：{Describe(stale)}");
    }

    /// <summary>
    /// 网关侧枚举丢弃集必须为空。
    /// </summary>
    /// <remarks>
    /// <para><see cref="GatewayValidatorEnumeration"/> 有两类**静默丢弃**：开放泛型定义（无法实例化取规则）、
    /// 无公共无参构造（同样无法实例化）。「白名单选取会静默漏掉后来者」在本仓已栽过三次，
    /// 所以这两类不允许无声消失——今天实测都是 0，一旦有人写出这样的网关校验器，本条立刻红，
    /// 迫使当轮显式处理，而不是让那个位点从值域里蒸发。</para>
    /// <para><b>判定边界</b>：开放泛型那一类是按「类型是泛型定义」判的，不按「它校验的类型带不带
    /// <c>IdempotencyKey</c>」判——因为开放泛型的验证类型可能就是泛型形参本身，那时根本判不出来。
    /// 代价是可能把与幂等键无关的开放泛型校验器也拦下来；收益是不留「判不出来所以放过」的口子。</para>
    /// </remarks>
    [Fact]
    public void Gateway_validator_enumeration_discards_nothing()
    {
        var enumeration = EnumerateGatewayValidators();

        Assert.True(
            enumeration.Discarded.Count == 0,
            $"网关校验器枚举丢弃了 {enumeration.Discarded.Count} 个类型，它们的端点级幂等键上界不会进入本契约的值域："
            + $"\n{string.Join("\n", enumeration.Discarded)}");
    }

    [Fact]
    public void Every_declared_downstream_authority_resolves()
    {
        Assert.NotEmpty(DownstreamBounds);

        foreach (var (gatewayRequestType, authorities) in DownstreamBounds)
        {
            Assert.NotEmpty(authorities);

            if (authorities.OfType<NoDownstreamAuthority>().Any())
            {
                // 「下游零权威」不允许与可解析权威混登：混登之后不等式仍然会跑，
                // 那条 none 就成了纯装饰，读者会以为下游被穷举过。
                Assert.True(
                    authorities.Length == 1,
                    $"{gatewayRequestType.Name} 同时登记了「下游零权威」与可解析权威（{Describe(authorities)}），"
                    + "两者互斥：要么下游有权威（登记它），要么没有（只登记 none）。");
                continue;
            }

            foreach (var authority in authorities.OfType<ResolvableDownstreamAuthority>())
            {
                var resolved = authority.Resolve();
                Assert.True(
                    resolved > 0,
                    $"{gatewayRequestType.Name} 登记的下游权威 {authority} 解析不到长度上界。");
            }
        }
    }

    /// <summary>
    /// 「下游零权威」的登记项必须与封闭豁免集逐一对应。
    /// </summary>
    /// <remarks>
    /// 没有这条，任何人遇到 <see cref="Gateway_never_promises_a_longer_idempotency_key_than_its_downstream_accepts"/>
    /// 报红时，都可以把该位点改登记成 <see cref="NoDownstreamAuthority"/> 让它退出不等式而门禁照绿。
    /// 有了这条，退出不等式必须同时改动这份封闭集合，在 review 里可见。
    /// </remarks>
    [Fact]
    public void Only_pinned_requests_are_registered_without_a_downstream_authority()
    {
        var exempted = DownstreamBounds
            .Where(pair => pair.Value.OfType<NoDownstreamAuthority>().Any())
            .Select(pair => pair.Key)
            .ToHashSet();

        var unexpected = exempted.Except(RequestsWithoutDownstreamAuthority).ToArray();
        var missing = RequestsWithoutDownstreamAuthority.Except(exempted).ToArray();

        Assert.True(
            unexpected.Length == 0 && missing.Length == 0,
            $"未在封闭豁免集里却登记成「下游零权威」（{unexpected.Length}）：{Describe(unexpected)}"
            + $"；封闭豁免集里却已有下游权威（{missing.Length}）：{Describe(missing)}");
    }

    [Fact]
    public void Gateway_never_promises_a_longer_idempotency_key_than_its_downstream_accepts()
    {
        var sites = GatewayEndpointLevelBounds();
        Assert.NotEmpty(sites);

        var violations = new List<string>();
        var compared = 0;
        foreach (var (gatewayRequestType, gatewayBound) in sites)
        {
            var authorities = Assert.Contains(gatewayRequestType, (IDictionary<Type, DownstreamAuthority[]>)DownstreamBounds);
            var resolvable = authorities.OfType<ResolvableDownstreamAuthority>().ToArray();
            if (resolvable.Length == 0)
            {
                // 显式登记为「下游零权威」：没有可比的上界，不参与不等式。
                // 它仍然被完备性断言与封闭豁免集覆盖，不是从值域里消失。
                continue;
            }

            compared++;
            var downstreamBound = resolvable.Min(authority => authority.Resolve());
            if (gatewayBound > downstreamBound)
            {
                violations.Add(
                    $"  {gatewayRequestType.FullName}: 网关端点级上界 {gatewayBound} > 下游权威上界 {downstreamBound}"
                    + $"（权威：{Describe(authorities)}）");
            }
        }

        // 参与比较的位点数必须非空：若哪天所有位点都退成「零权威」，不等式会静默变成空断言。
        Assert.True(compared > 0, "没有任何位点参与上界比较，本条已退化成空断言。");

        Assert.True(
            violations.Count == 0,
            $"网关对外承诺的幂等键长度超出下游能接受的上界（{violations.Count} 处 / 共比较 {compared} 处）：\n"
            + string.Join("\n", violations));
    }

    // ---------------------------------------------------------------------
    // 网关侧枚举（机械）
    // ---------------------------------------------------------------------

    /// <summary>
    /// 网关程序集里所有「对 <c>IdempotencyKey</c> 施加了长度上界」的端点级校验器，
    /// 键是被校验的请求类型，值是该规则的上界。
    /// </summary>
    internal static IReadOnlyDictionary<Type, int> GatewayEndpointLevelBounds() =>
        EnumerateGatewayValidators().Bounds;

    /// <summary>网关校验器枚举结果：进入值域的位点，与被丢弃、因此**不在**值域里的类型。</summary>
    internal sealed record GatewayValidatorEnumeration(
        IReadOnlyDictionary<Type, int> Bounds,
        IReadOnlyList<string> Discarded);

    internal static GatewayValidatorEnumeration EnumerateGatewayValidators()
    {
        var assembly = typeof(Nerv.IIP.BusinessGateway.Web.Application.BusinessServices
            .BusinessConsoleSetMasterDataResourceEnabledRequest).Assembly;

        var bounds = new Dictionary<Type, int>();
        var discarded = new List<string>();
        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || !typeof(IValidator).IsAssignableFrom(type))
            {
                continue;
            }

            if (type.IsGenericTypeDefinition)
            {
                discarded.Add(
                    $"  {type.FullName}：开放泛型定义，无法实例化读规则，"
                    + "且验证类型可能是泛型形参、无法判定是否带 IdempotencyKey。");
                continue;
            }

            var validatedType = type.GetInterfaces()
                .Where(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IValidator<>))
                .Select(x => x.GetGenericArguments()[0])
                .FirstOrDefault();
            if (validatedType is null
                || validatedType.GetProperty(IdempotencyKeyPropertyName, BindingFlags.Instance | BindingFlags.Public)
                    ?.PropertyType != typeof(string))
            {
                continue;
            }

            if (type.GetConstructor(Type.EmptyTypes) is null)
            {
                discarded.Add(
                    $"  {type.FullName}：校验 {validatedType.Name}（带 string IdempotencyKey），但无公共无参构造，无法实例化读规则。");
                continue;
            }

            var maximum = MaximumIdempotencyKeyLength((IValidator)Activator.CreateInstance(type)!);
            if (maximum is { } value)
            {
                bounds[validatedType] = bounds.TryGetValue(validatedType, out var existing)
                    ? Math.Min(existing, value)
                    : value;
            }
        }

        return new GatewayValidatorEnumeration(bounds, discarded);
    }

    /// <summary>
    /// 从已建好的 FluentValidation 规则里读 <c>IdempotencyKey</c> 上的长度上界。
    /// 读的是**规则本身**而不是源码文本，所以经由扩展方法（Inventory 的
    /// <c>RequiredInventoryCode</c>）或共享配置入口（Wms 的 <c>WarehouseTaskActionValidation.Configure</c>）
    /// 加上去的规则同样会被读到。
    /// </summary>
    private static int? MaximumIdempotencyKeyLength(IValidator validator)
    {
        if (validator is not IEnumerable<IValidationRule> rules)
        {
            return null;
        }

        int? maximum = null;
        foreach (var rule in rules)
        {
            var member = rule.Member?.Name ?? rule.PropertyName;
            if (!string.Equals(member, IdempotencyKeyPropertyName, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var component in rule.Components)
            {
                if (component.Validator is ILengthValidator length && length.Max > 0)
                {
                    maximum = maximum is { } current ? Math.Min(current, length.Max) : length.Max;
                }
            }
        }

        return maximum;
    }

    private static string Describe(IEnumerable<Type> types) =>
        string.Join(", ", types.Select(x => x.FullName).OrderBy(x => x, StringComparer.Ordinal));

    private static string Describe(IEnumerable<DownstreamAuthority> authorities) =>
        string.Join(" / ", authorities.Select(x => x.ToString()));

    // ---------------------------------------------------------------------
    // 下游权威（登记的是类型引用，数值运行时解析）
    // ---------------------------------------------------------------------

    private static readonly DownstreamAuthority MasterDataCodeKeyColumn =
        Column<CodeIdempotencyKey>(MasterDataModel, nameof(CodeIdempotencyKey.IdempotencyKey), "MasterData");

    private static readonly DownstreamAuthority MasterDataLifecycleOperationColumn =
        Column<MasterDataLifecycleAuditEntry>(
            MasterDataModel,
            nameof(MasterDataLifecycleAuditEntry.OperationId),
            "MasterData");

    private static readonly DownstreamAuthority MesCodeKeyColumn =
        Column<CodeIdempotencyKey>(MesModel, nameof(CodeIdempotencyKey.IdempotencyKey), "Mes");

    private static readonly DownstreamAuthority ErpCodeKeyColumn =
        Column<CodeIdempotencyKey>(ErpModel, nameof(CodeIdempotencyKey.IdempotencyKey), "Erp");

    private static readonly DownstreamAuthority DemandPlanningCodeKeyColumn =
        Column<CodeIdempotencyKey>(
            DemandPlanningModel,
            nameof(CodeIdempotencyKey.IdempotencyKey),
            "DemandPlanning");

    /// <summary>
    /// 下游**零**幂等键长度权威的封闭豁免集。**目前为空**。
    /// </summary>
    /// <remarks>
    /// <para>进这份集合的门槛是「已穷举下游、确认它对键长度不施加任何约束」，不是「暂时没查到」。</para>
    /// <para><b>唯一曾经的一项已于 #3291 退役</b>：Wms 受控分配家族
    /// （<c>BusinessConsoleAssignWmsResourceRequest</c>）当时下游 5 条命令零校验——
    /// 规则全写在 sealed 开放泛型 <c>WarehouseAssignmentCommandValidator&lt;TCommand&gt;</c> 上，
    /// 无法派生闭合、程序集扫描也不注册泛型定义，实测真实 host 里 <c>IValidator&lt;C&gt;</c> 解析数均为 0。
    /// #3291 改成「静态辅助 + 5 个具体校验器」姿势后，这 5 条命令各有可解析的 128 上界，
    /// 该位点因此从豁免集移出、登记为 <c>validator(...)</c>×5 参与不等式。</para>
    /// <para><b>集合为空不等于本条断言退化</b>：
    /// <see cref="Only_pinned_requests_are_registered_without_a_downstream_authority"/> 是双向的——
    /// 任何人把某个位点改登记成 <see cref="NoDownstreamAuthority"/> 让它退出不等式，
    /// 而不同时往这份集合里加一项，立刻红。空集是「今天没有任何位点需要退出不等式」这一事实本身，
    /// 不要为了让机制「看起来在用」而留一个假项。</para>
    /// <para><b>这条断言不会自己退役（#3291 实测，写清楚避免误信）</b>：它只锁「登记 None 必须同时进集合」
    /// 这一个方向，**不**验证豁免理由今天是否还成立。实测过：只落 Wms 侧的 5 个具体校验器、
    /// 本文件一字不动，本类 5 条断言全部照绿——豁免项不会因为下游长出权威而报红。
    /// 所以下游修复时必须**人工**把对应位点从这里移出并登记真实权威；
    /// 想要真正的自动退役，得让 <see cref="NoDownstreamAuthority"/> 携带它声称「无约束」的下游命令类型，
    /// 并断言那些类型解析出的上界确实为 0。今天集合为空，没有位点可以承载这个机制，故不预建。</para>
    /// </remarks>
    private static readonly HashSet<Type> RequestsWithoutDownstreamAuthority = [];

    /// <summary>
    /// 「网关请求类型 → 下游权威」的登记表。**这张表是手写的链接，数值不是**：
    /// 每一项要么指向下游命令类型（上界从它自己的校验器读），要么指向下游 EF 实体属性（上界从列宽读），
    /// 要么显式声明下游零权威。
    /// <para>登记依据是逐条实读转发链：网关 endpoint 的 <c>ForwardAsync</c> → capability client 的路径字面量
    /// → 下游服务 <c>*EndpointContracts</c> 里同路径的 endpoint → 它 <c>HandleAsync</c> 里发出的命令
    /// → 该命令的校验器 / 该命令 handler 把幂等键写进的那一列。</para>
    /// <para><b>登记列宽的前提</b>：必须实读写入点，确认落库的就是调用方原始键。
    /// 走 <c>CodeAllocator</c> 的位点（MasterData 全部、Erp 收货、Mes 三个写面、DemandPlanning 需求来源）
    /// 与 MasterData 生命周期审计的 <c>OperationId</c> 都只 <c>Trim</c>、不派生，列宽因此可达；
    /// Wms 两张 receipt 表落的是 <c>WmsText.IdempotencyKey</c> 的 SHA256 派生值，列宽不可达，**不登记**。</para>
    /// </summary>
    private static readonly Dictionary<Type, DownstreamAuthority[]> DownstreamBounds = new()
    {
        // ---- Wms：作业动作四处 + 分配 + 三个单据完成 ----
        // 这四处的权威是共享校验器入口 WarehouseTaskActionValidation.Configure<TCommand>
        // （WmsCommands.cs:992，MaximumLength(128)），由 Start/RecordProgress/ReportException/Complete
        // 各自的具体校验器调用，确实生效。落库列 warehouse_task_action_receipts.idempotency_key(128)
        // 存的是 WmsText.IdempotencyKey 派生的 75 字符定长值，对原始键零约束，故不登记为权威。
        [typeof(BusinessConsoleStartWmsWarehouseTaskRequest)] = [Command<StartWarehouseTaskCommand>()],
        [typeof(BusinessConsoleRecordWmsWarehouseTaskProgressRequest)] =
            [Command<RecordWarehouseTaskProgressActionCommand>()],
        [typeof(BusinessConsoleReportWmsWarehouseTaskExceptionRequest)] =
            [Command<ReportWarehouseTaskExceptionCommand>()],
        [typeof(BusinessConsoleCompleteWmsWarehouseTaskRequest)] = [Command<CompleteWarehouseTaskActionCommand>()],

        // 分配家族：一个网关端点族（5 个 endpoint 共用同一个 request 类型）转发到 5 条不同的下游命令，
        // 所以 5 条全部登记、有效上界取其中最小（#3281 判据）。
        // #3291 之前这 5 条命令的规则挂在从未闭合的 sealed 开放泛型校验器上、一条都没跑过，
        // 此处曾登记为 None；修复后各自有具体校验器（WarehouseAssignmentValidation.Configure，
        // IdempotencyKey MaximumLength(128)），由 Wms 侧
        // WarehouseAssignmentValidatorRegistrationTests 从真实 host 容器与 MediatR 管道两面钉住。
        // 落库列 warehouse_assignment_receipts.idempotency_key(128) 存的是 WmsText.IdempotencyKey
        // 派生的 75 字符定长值，对原始键零约束，仍**不**登记为权威。
        [typeof(BusinessConsoleAssignWmsResourceRequest)] =
        [
            Command<AssignInboundOrderCommand>(),
            Command<AssignPutawayTaskCommand>(),
            Command<AssignOutboundOrderCommand>(),
            Command<AssignPickingTaskCommand>(),
            Command<AssignCountExecutionCommand>(),
        ],

        [typeof(BusinessConsoleCompleteWmsInboundOrderRequest)] = [Command<CompleteInboundOrderCommand>()],
        [typeof(BusinessConsoleCompleteWmsOutboundOrderRequest)] = [Command<CompleteOutboundOrderCommand>()],
        [typeof(BusinessConsoleCompleteWmsCountExecutionRequest)] = [Command<CompleteCountExecutionCommand>()],

        // ---- Inventory ----
        [typeof(BusinessConsoleConfirmStockCountAdjustmentRequest)] =
            [Command<ConfirmStockCountAdjustmentCommand>()],

        // ---- MasterData：14 个 create 走 CodeAllocator，1 个生命周期开关走审计表 ----
        [typeof(BusinessConsoleCreateProductCategoryRequest)] = [MasterDataCodeKeyColumn],
        [typeof(BusinessConsoleCreateSkillRequest)] = [MasterDataCodeKeyColumn],
        [typeof(BusinessConsoleCreateBusinessPartnerRequest)] = [MasterDataCodeKeyColumn],
        [typeof(BusinessConsoleCreateUnitOfMeasureRequest)] = [MasterDataCodeKeyColumn],
        [typeof(BusinessConsoleCreateWorkerRequest)] = [MasterDataCodeKeyColumn],
        [typeof(BusinessConsoleCreateWorkshopRequest)] = [MasterDataCodeKeyColumn],
        [typeof(BusinessConsoleCreateSiteRequest)] = [MasterDataCodeKeyColumn],
        [typeof(BusinessConsoleCreateProductionLineRequest)] = [MasterDataCodeKeyColumn],
        [typeof(BusinessConsoleCreateWorkCenterRequest)] = [MasterDataCodeKeyColumn],
        [typeof(BusinessConsoleRegisterDeviceAssetRequest)] = [MasterDataCodeKeyColumn],
        [typeof(BusinessConsoleCreateShiftRequest)] = [MasterDataCodeKeyColumn],
        [typeof(BusinessConsoleCreateWorkCalendarRequest)] = [MasterDataCodeKeyColumn],
        [typeof(BusinessConsoleCreateTeamRequest)] = [MasterDataCodeKeyColumn],
        [typeof(BusinessConsoleCreateDepartmentRequest)] = [MasterDataCodeKeyColumn],
        // 这处端点级值取 200（= 下游 master_data_lifecycle_audit.operation_id 列宽），不取 150。
        // 全局钳 BusinessGatewayIdempotencyKey.MaximumLength = 150 会让 151..200 的键先撞 409，
        // 于是 OpenAPI 里新写的 maxLength: 200 对客户端是一句假承诺——该缺陷的真因在全局钳，
        // 归 #3287（该票已追加此侧面）；用一个更小的数在这里掩盖它是打补丁，本票不做。
        [typeof(BusinessConsoleSetMasterDataResourceEnabledRequest)] = [MasterDataLifecycleOperationColumn],

        // ---- Mes ----
        [typeof(BusinessConsoleMesClaimOperationTaskRequest)] = [Command<ClaimDispatchTaskCommand>()],
        [typeof(BusinessConsoleRecordProductionReportRequest)] = [Command<RecordProductionReportCommand>()],
        [typeof(BusinessConsoleMesOperationTaskActionRequest)] = [MesCodeKeyColumn],
        [typeof(BusinessConsoleMesRecordDefectV2Request)] = [MesCodeKeyColumn],
        [typeof(BusinessConsoleMesRecordDowntimeEventV2Request)] = [MesCodeKeyColumn],
        [typeof(BusinessConsoleMesSplitWorkOrderRequest)] = [Command<SplitWorkOrderCommand>()],
        [typeof(BusinessConsoleMesMergeWorkOrdersRequest)] = [Command<MergeWorkOrdersCommand>()],

        // ---- DemandPlanning ----
        [typeof(BusinessConsoleCreateOrUpdateDemandSourceRequest)] = [DemandPlanningCodeKeyColumn],
        [typeof(BusinessConsoleCreateOrUpdateForecastInputRequest)] = [Command<CreateOrUpdateForecastInputCommand>()],
        [typeof(BusinessConsoleAcceptPlanningSuggestionRequest)] = [Command<AcceptPlanningSuggestionCommand>()],

        // ---- Maintenance ----
        [typeof(BusinessConsoleAssignMaintenanceWorkOrderRequest)] = [Command<AssignMaintenanceWorkOrderCommand>()],
        [typeof(BusinessConsoleTransitionMaintenanceWorkOrderRequest)] = [Command<TransitionMaintenanceWorkOrderCommand>()],
        [typeof(BusinessConsoleCreateMaintenanceWorkOrderRequest)] = [Command<CreateMaintenanceWorkOrderCommand>()],
        [typeof(BusinessConsoleCreateMaintenanceWorkOrderV2Request)] = [Command<CreateMaintenanceWorkOrderV2Command>()],
        [typeof(BusinessConsoleCompleteMaintenanceWorkOrderRequest)] = [Command<CompleteMaintenanceWorkOrderCommand>()],
        [typeof(BusinessConsoleCreateMaintenancePlanRequest)] = [Command<CreateMaintenancePlanCommand>()],

        // ---- Quality ----
        [typeof(BusinessConsoleCreateQualityReasonRequest)] = [Command<CreateQualityReasonCommand>()],
        [typeof(BusinessConsoleAssignQualityInspectionTaskRequest)] = [Command<AssignInspectionTaskCommand>()],
        [typeof(BusinessConsoleClaimQualityInspectionTaskRequest)] = [Command<ClaimInspectionTaskCommand>()],
        [typeof(BusinessConsoleCreateInspectionRecordFromTaskRequest)] = [Command<CreateInspectionRecordFromTaskCommand>()],
        // 这条规则在网关侧写成多行（RuleFor / NotEmpty / MaximumLength / When 各占一行），
        // 票面与测量席位用的单行 grep 都没扫到它——本类改用反射枚举后它才现身，
        // 所以「48 个端点级位点」这个计数其实是 49。
        // 语义上它不是无条件 150：网关侧带 .When(DispositionType == "rework")，
        // 下游 SubmitNonconformanceReportDispositionCommandValidator 带完全相同的
        // .When(QualityNcrDispositionTypes.Rework)——两侧生效条件一致，所以 150 ≤ 150 成立且有意义
        // （不是「一条恒生效规则对一条条件规则」那种错配比较）。下游同为 150，本票不改数值，只需登记。
        [typeof(BusinessConsoleNcrDispositionRequest)] = [Command<SubmitNonconformanceReportDispositionCommand>()],

        // ---- ProductEngineering ----
        [typeof(BusinessConsoleCreateStandardOperationRequest)] = [Command<CreateStandardOperationCommand>()],

        // ---- IndustrialTelemetry（网关侧分别落在 Equipment 与 Telemetry 两个端点文件） ----
        [typeof(BusinessConsoleShelveAlarmRequest)] = [Command<ShelveAlarmCommand>()],
        [typeof(BusinessConsoleTelemetryDeviceControlCommandRequest)] = [Command<CreateDeviceControlCommandCommand>()],

        // ---- Erp ----
        [typeof(BusinessConsoleRecordErpPurchaseReceiptRequest)] = [ErpCodeKeyColumn],
    };

    private abstract class DownstreamAuthority;

    /// <summary>能解析出一个数值长度上界的下游权威。</summary>
    private abstract class ResolvableDownstreamAuthority : DownstreamAuthority
    {
        public abstract int Resolve();
    }

    /// <summary>
    /// 显式登记「该位点下游不存在任何幂等键长度权威」：参与完备性覆盖与封闭豁免集，但不参与上界不等式。
    /// </summary>
    private sealed class NoDownstreamAuthority(string reason) : DownstreamAuthority
    {
        public override string ToString() => $"none({reason})";
    }

    /// <summary>下游命令自己的校验器对 <c>IdempotencyKey</c> 施加的上界。</summary>
    private sealed class CommandValidatorBound(Type commandType) : ResolvableDownstreamAuthority
    {
        public override int Resolve()
        {
            // 取最小而不是「首个命中」：Assembly.GetTypes() 的顺序未定义，首个命中不是稳定语义；
            // 同一命令挂多个校验器时全都会跑，有效上界是其中最小的那个（#3281 判据）。
            int? minimum = null;
            foreach (var candidate in commandType.Assembly.GetTypes())
            {
                if (candidate.IsAbstract || candidate.IsGenericTypeDefinition
                    || candidate.GetConstructor(Type.EmptyTypes) is null)
                {
                    continue;
                }

                var validates = candidate.GetInterfaces().Any(x =>
                    x.IsGenericType
                    && x.GetGenericTypeDefinition() == typeof(IValidator<>)
                    && x.GetGenericArguments()[0] == commandType);
                if (!validates)
                {
                    continue;
                }

                if (MaximumIdempotencyKeyLength((IValidator)Activator.CreateInstance(candidate)!) is { } bound)
                {
                    minimum = minimum is { } current ? Math.Min(current, bound) : bound;
                }
            }

            return minimum ?? 0;
        }

        public override string ToString() => $"validator({commandType.Name})";
    }

    /// <summary>
    /// 幂等键最终落库的那一列的宽度（#3281：一个值写进多列时有效上界取最小列宽）。
    /// 只有在实读确认「写进该列的就是调用方原始键」时才允许登记；派生值列是幽灵权威。
    /// </summary>
    private sealed class ColumnWidthBound(Func<DbContext> contextFactory, Type entityType, string propertyName, string label)
        : ResolvableDownstreamAuthority
    {
        public override int Resolve()
        {
            using var context = contextFactory();
            return context.Model.FindEntityType(entityType)?.FindProperty(propertyName)?.GetMaxLength() ?? 0;
        }

        public override string ToString() => $"column({label}.{entityType.Name}.{propertyName})";
    }

    private static DownstreamAuthority Command<TCommand>() => new CommandValidatorBound(typeof(TCommand));

    private static DownstreamAuthority Column<TEntity>(Func<DbContext> contextFactory, string propertyName, string label) =>
        new ColumnWidthBound(contextFactory, typeof(TEntity), propertyName, label);

    private static DownstreamAuthority None(string reason) => new NoDownstreamAuthority(reason);

    private static DbContext MasterDataModel() => ModelOnly<MasterDataDbContext>(
        options => new MasterDataDbContext(options, NullMediator.Instance));

    private static DbContext MesModel() => ModelOnly<MesDbContext>(
        options => new MesDbContext(options, NullMediator.Instance));

    private static DbContext ErpModel() => ModelOnly<ErpDbContext>(
        options => new ErpDbContext(options, NullMediator.Instance));

    private static DbContext DemandPlanningModel() => ModelOnly<DemandPlanningDbContext>(
        options => new DemandPlanningDbContext(options, NullMediator.Instance));

    /// <summary>
    /// 只用于读 EF 模型：不开连接、不建库。连接串必须语法合法，Npgsql 才肯建 provider。
    /// </summary>
    private static DbContext ModelOnly<TContext>(Func<DbContextOptions<TContext>, TContext> factory)
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseNpgsql("Host=127.0.0.1;Database=nerv_iip_gateway_idempotency_key_contract;Username=nerv;Password=nerv")
            .Options;
        return factory(options);
    }

    private sealed class NullMediator : IMediator
    {
        public static readonly NullMediator Instance = new();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification =>
            throw new NotSupportedException();

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
