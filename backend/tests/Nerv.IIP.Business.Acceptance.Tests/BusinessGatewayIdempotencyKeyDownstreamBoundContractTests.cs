using System.Reflection;
using FluentValidation;
using FluentValidation.Internal;
using FluentValidation.Validators;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Commands;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Procurement;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.PrintBatches;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.Scans;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.TemplateAssetRetirements;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.TemplateAssetRetirementDecisionAggregate;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockCounts;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements;
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
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Endpoints.Barcode;
using Nerv.IIP.Coding;

using MesEndpointRequests = Nerv.IIP.Business.Mes.Web.Endpoints.Mes;
using MesQualityAggregate = Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;
using MasterDataToolingAggregate = Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ToolingAssetAggregate;
using BarcodeLabelPrintBatchAggregate = Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using BarcodeLabelScanRecordAggregate = Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.ScanRecordAggregate;
using InventoryStockMovementAggregate = Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;

using BarcodeLabelDbContext = Nerv.IIP.Business.BarcodeLabel.Infrastructure.ApplicationDbContext;
using DemandPlanningDbContext = Nerv.IIP.Business.DemandPlanning.Infrastructure.ApplicationDbContext;
using ErpDbContext = Nerv.IIP.Business.Erp.Infrastructure.ApplicationDbContext;
using InventoryDbContext = Nerv.IIP.Business.Inventory.Infrastructure.ApplicationDbContext;
using MasterDataDbContext = Nerv.IIP.Business.MasterData.Infrastructure.ApplicationDbContext;
using MesDbContext = Nerv.IIP.Business.Mes.Infrastructure.ApplicationDbContext;
using ProductEngineeringDbContext = Nerv.IIP.Business.ProductEngineering.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

/// <summary>
/// 网关幂等键长度值域的三条机器可验关系（#3284 建立，#3327 补齐闭合与全局钳）。
/// </summary>
/// <remarks>
/// <para><b>被证的三条真不变量</b>：</para>
/// <list type="number">
/// <item><b>端点级上界 ≤ 下游权威上界</b>（#3284）——「网关对外承诺的绝不比下游能接受的宽」，
/// 见 <see cref="Gateway_never_promises_a_longer_idempotency_key_than_its_downstream_accepts"/>。</item>
/// <item><b>受钳的每个请求类型都被上界覆盖或被显式豁免</b>（#3327）——
/// 见 <see cref="Every_clamped_gateway_request_is_bounded_by_an_endpoint_rule_or_registered_without_downstream_authority"/>。
/// 这一条把「有多少请求只吃全局钳、保护力未知」从 0 收敛到「要么有权威派生的上界、要么理由经过实测」。</item>
/// <item><b>全局钳不比任何端点级规则更严</b>（#3327）——
/// 见 <see cref="Global_clamp_is_never_stricter_than_any_endpoint_level_bound"/>，
/// 它挡的是「OpenAPI 声明合法、入口先打 400」这种假承诺。</item>
/// </list>
/// <para>第 1 条的反方向（网关比下游**窄**，把合法键挡在门外）本类仍**不**断言：
/// 收窄一条端点级规则不会让任何一条红。</para>
///
/// <para><b>为什么第 1 条断言端点级规则值本身，而不是 <c>min(端点级规则值, 全局钳)</c></b>：
/// min 形式会把「两个互不知情的常量偶然盖住一个洞」写进契约——
/// 钳一旦调大或去掉，端点级的死值立刻能打出 22001，而契约仍是绿的。
/// 不写今天的钳值：那个数会变（本票就把它从 150 改成了 512），复述它等于制造一处会过期的手抄事实。</para>
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
/// <item>「受全局钳作用、但**没有**端点级规则」这一面已由 #3327 的闭合断言收掉，
/// 但收掉的方式是**二选一**：要么有端点级规则，要么登记为「下游零权威」并由
/// <see cref="Registered_absence_of_downstream_authority_is_still_true"/> 实测理由。
/// 闭合断言**不覆盖**「根本没有 <c>IdempotencyKey</c> 属性、却经审计头携带幂等键」的那 3 个
/// <c>TRequest</c>，见该断言自己的注释。
/// （此处原写「118 个」，是单行 grep 的产物，已被反射测量推翻；该计数每落一张子票就变，
/// 故不在这里复述数字。）</item>
/// <item><b>头部路径的覆盖是间接的，而且这一条被证伪过两次</b>：#3330 把
/// <c>BusinessGatewayIdempotencyKey.Resolve</c> 从 <c>AuthorizedBusinessProxyEndpoint.HandleAsync</c>
/// 挪到 DTO 校验之前（<c>OnBeforeValidateAsync</c>）；#3327 又把它从「鉴权作不出结论时的早返回」
/// 里拆出来（#3345 审核实测：那条支路上头部键仍绕过端点级规则并被转发下去）。
/// 两步都落地后，经 <c>Idempotency-Key</c> / <c>X-Idempotency-Key</c> 头传来的键在校验发生时
/// 已归一化写进 DTO，本类枚举到的端点级规则对头部与请求体两条来源同时生效。
/// <b>但那条性质由网关自己的 <c>BusinessGatewayRequestPipelineOrderTests</c> 证明，不是本类证的</b>：
/// 本类只读校验器规则值、不发请求。若那个次序被改回去（哪怕只在一条支路上），
/// 本类照样全绿而头部路径重新裸奔——这正是它被证伪两次都没被本类抓到的原因。</item>
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

        // stale 要扣掉封闭豁免集（#3328）：登记为「下游零权威」的位点**本来就可能没有端点级规则**——
        // 网关对一个下游根本不消费的键声明 maxLength 是假承诺，本仓判例明令不做。
        // 这一支的完备性由 Only_pinned_requests_are_registered_without_a_downstream_authority
        // （双向钉住登记表与封闭集）与 Registered_absence_of_downstream_authority_is_still_true
        // （按下游命令类型实测「真的没有」）两条共同承担，不是从值域里消失。
        var stale = declared.Except(enumerated).Except(RequestsWithoutDownstreamAuthority).ToArray();

        Assert.True(
            unregistered.Length == 0 && stale.Length == 0,
            $"网关新增了端点级幂等键上界但未登记下游权威（{unregistered.Length}）：{Describe(unregistered)}"
            + $"；登记表里的位点在网关侧已不存在端点级上界、且不在封闭豁免集里（{stale.Length}）：{Describe(stale)}");
    }

    /// <summary>
    /// 受全局钳作用的每一个网关代理请求类型，**要么**有端点级 <c>IdempotencyKey</c> 长度规则，
    /// **要么**被登记为「下游零权威」（#3327 的闭合断言）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么是二选一，而不是「必须有端点级规则」</b>：对**下游根本不消费这把键**的请求
    /// （今天是 IndustrialTelemetry 确认报警 / 解除搁置两处，键的消费者是网关自己的 operation-receipt），
    /// 逼它挂一条 <c>MaximumLength</c> 只能手抄一个**没有权威来源**的数字，
    /// 并把「这里有幂等保护」的错觉写进公开 OpenAPI 契约。豁免那一支走
    /// <see cref="NoDownstreamAuthority"/> + <see cref="RequestsWithoutDownstreamAuthority"/>，
    /// 其**理由**由 <see cref="Registered_absence_of_downstream_authority_is_still_true"/> 按下游命令类型实测，
    /// 不是「说一句就算」。</para>
    ///
    /// <para><b>两个维度必须与钳的实际作用判据逐字一致</b>，否则量的不是同一个集合：</para>
    /// <list type="bullet">
    /// <item>维度 A（本条自己枚举）= <see cref="AuthorizedBusinessProxyEndpoint{TRequest,TResponse}"/>
    /// 全部**非抽象**后代的 <c>TRequest</c>，按
    /// <c>GetProperty("IdempotencyKey", Instance | Public)</c> 且 <c>PropertyType == typeof(string)</c> 过滤
    /// —— 与 <c>BusinessGatewayIdempotencyKey.Resolve&lt;TRequest&gt;</c> 里那两行逐字相同。
    /// 取**顶层请求对象**而不是「所有带该属性的类型」：后者会混进 <c>...Response</c> / <c>...Item</c>
    /// 这类根本不经过钳的嵌套类型。</item>
    /// <item>维度 B = <see cref="GatewayEndpointLevelBounds"/>，复用同文件的反射枚举
    /// （其丢弃集由 <see cref="Gateway_validator_enumeration_discards_nothing"/> 钉为空），不另写一份。</item>
    /// </list>
    ///
    /// <para><b>三条自检，每条挡一种「量到空气」的退化</b>：</para>
    /// <list type="number">
    /// <item>维度 A 非空——反射一旦失配（基类改名、泛型元数变化）会退化成空集，而差集对空集恒为空。</item>
    /// <item>反向差集（B − A）为空——它是「两个维度可比」的判据。若哪天维度 A 被改成了另一个集合而
    /// 恰好仍非空，闭合断言会对着错的值域报绿；反向差集会先红。</item>
    /// <item>豁免集 ⊆ 维度 A——登记为「下游零权威」却已经不受钳作用（例如网关那个字段被摘掉了）
    /// 的项必须清出去，否则豁免会在一个已经不存在的位点上继续挂着。</item>
    /// </list>
    ///
    /// <para><b>本条不覆盖什么（写清楚，避免「护栏自称完备」）</b>：</para>
    /// <list type="number">
    /// <item><b>没有 <c>IdempotencyKey</c> 属性、但仍会携带幂等键的那一面不在闭合集里。</b>
    /// <c>BusinessGatewayIdempotencyKey.ResolveForAudit</c> 从
    /// <c>Idempotency-Key</c> / <c>X-Idempotency-Key</c> 头取键放进审计上下文并转发下游，
    /// 它的调用面（<c>RequireAuditContext</c> / <c>RequireIdempotentAuditContext</c>）里有 3 个
    /// <c>TRequest</c> **根本没有该属性**——实读复核为
    /// <c>BusinessConsoleUpdateMasterDataResourceRequest</c>、
    /// <c>BusinessConsoleAddTeamMemberRequest</c>、<c>BusinessConsoleRemoveTeamMemberRequest</c>
    /// （#3330 席位登记时把后两个写成了去掉 <c>BusinessConsole</c> 前缀的名字，那是 MasterData 服务自己的
    /// 同名 DTO，网关程序集里没有那两个类型；结论一致，名字以此处为准）。
    /// 它们的头部键**只受全局钳约束**，且没有 DTO 属性可挂端点级规则
    /// ⇒ 既不在维度 A 里，也不在本条的闭合集里。
    /// <b>⇒ 本条全绿不等于「网关所有幂等键都受端点级约束」。</b></item>
    /// <item>不看守服务自有端点（如 <c>ErpProcurementEndpoints.cs</c>，#3288 的位点 2、3）——
    /// 它们完全在全局钳射程之外，本类整体都不看守。</item>
    /// <item>不证明端点级规则的**值**对不对：那由
    /// <see cref="Gateway_never_promises_a_longer_idempotency_key_than_its_downstream_accepts"/> 承担。</item>
    /// </list>
    /// </remarks>
    [Fact]
    public void Every_clamped_gateway_request_is_bounded_by_an_endpoint_rule_or_registered_without_downstream_authority()
    {
        var clamped = ClampedProxyRequestTypes();

        // 值域非空：反射失配会退化成空集，而差集对空集恒为空、Assert.All 对空集恒真。
        Assert.NotEmpty(clamped);

        var bounded = GatewayEndpointLevelBounds().Keys.ToHashSet();

        var uncovered = clamped.Except(bounded).Except(RequestsWithoutDownstreamAuthority).ToArray();
        var incomparable = bounded.Except(clamped).ToArray();
        var strayExemptions = RequestsWithoutDownstreamAuthority.Except(clamped).ToArray();

        Assert.True(
            uncovered.Length == 0 && incomparable.Length == 0 && strayExemptions.Length == 0,
            $"受全局钳作用、却既无端点级幂等键上界、也未登记为「下游零权威」（{uncovered.Length}）：{Describe(uncovered)}"
            + $"；有端点级上界却不在受钳集合里、两个维度已不可比（{incomparable.Length}）：{Describe(incomparable)}"
            + $"；登记为「下游零权威」却已不受全局钳作用（{strayExemptions.Length}）：{Describe(strayExemptions)}");
    }

    /// <summary>
    /// 全局钳 <c>BusinessGatewayIdempotencyKey.MaximumLength</c> 不得比任何端点级规则更严。
    /// </summary>
    /// <remarks>
    /// <para><b>是 <c>&gt;=</c> 不是 <c>==</c></b>：钳的角色是兜底，只需「不比任何端点更严」。
    /// <c>==</c> 会把它绑死在端点级最大值上，任何人**收紧**那个最大值都被迫同步改钳。</para>
    ///
    /// <para><b>它挡的是什么</b>：钳一旦小于某条端点级规则，那条规则声明在 OpenAPI 里的
    /// <c>maxLength</c> 就有一段是**假承诺**——契约说合法、入口先打 400
    /// （<c>idempotency-key-too-long</c>）。#3287 侧面④点名的就是这个形状。</para>
    ///
    /// <para><b>为什么用反射读那个 <c>private const</c>，而不是加 <c>InternalsVisibleTo</c> 直接引用</b>：
    /// C# 的 <c>const</c> 在**引用方**编译期内联。直接引用会把当时的值烤进本测试程序集，
    /// 之后只重建网关而不重建测试时，本条会拿着一个陈旧的数报绿（本仓已有判例）。
    /// 反射走的是网关程序集的字段元数据，运行时才读，没有这个窗口。
    /// 代价是字段改名会让解析失败——所以下面对「解析得到」本身也断言，改名即红，不静默跳过。</para>
    /// </remarks>
    [Fact]
    public void Global_clamp_is_never_stricter_than_any_endpoint_level_bound()
    {
        var bounds = GatewayEndpointLevelBounds();
        Assert.NotEmpty(bounds);

        var clamp = ResolveGlobalClamp();

        var falsePromises = bounds
            .Where(pair => pair.Value > clamp)
            .OrderBy(pair => pair.Key.FullName, StringComparer.Ordinal)
            .Select(pair => $"  {pair.Key.FullName}: 端点级上界 {pair.Value} > 全局钳 {clamp}")
            .ToArray();

        Assert.True(
            falsePromises.Length == 0,
            $"全局钳比端点级规则更严，这些位点声明在 OpenAPI 里的 maxLength 有一段是假承诺"
            + $"（{falsePromises.Length} 处 / 共比较 {bounds.Count} 处，钳 = {clamp}）：\n"
            + string.Join("\n", falsePromises));
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

    /// <summary>
    /// 「下游零权威」的登记项，其**理由今天仍然成立**：它点名的每个下游命令类型，
    /// 既不带 <c>IdempotencyKey</c> 成员，也解析不出任何长度上界。
    /// </summary>
    /// <remarks>
    /// <para>这条是 #3290 在注释里写下、但当时**没有位点可以承载**的那个机制：
    /// 「想要真正的自动退役，得让 <see cref="NoDownstreamAuthority"/> 携带它声称「无约束」的下游命令类型，
    /// 并断言那些类型解析出的上界确实为 0。」#3328 给出了第一批这样的位点，故就地建起来。</para>
    /// <para><b>它挡的是什么</b>：有人把幂等键接进 <c>AcknowledgeAlarmCommand</c> /
    /// <c>UnshelveAlarmCommand</c>（加构造参数、或给命令挂一条幂等键长度规则）之后，
    /// 「下游零权威」这句就不再为真——本条立刻红，迫使当轮把登记改成真实权威，
    /// 而不是让一句过期的豁免理由被制度性固化（#3290 判例：封闭豁免集只锁一致性、不验理由）。</para>
    /// <para><b>它不挡什么（写清楚，避免「护栏自称完备」）</b>：它只看**被点名的那些命令类型**。
    /// 「网关请求 → 下游命令」这条链接仍然是手写的（与 <see cref="DownstreamBounds"/> 同一条局限），
    /// 点名了一个错的命令类型 ⇒ 本条会对着错的类型报绿。链接本身的正确性靠 review，不靠本条。
    /// 另外它**不**覆盖「键落进下游某张表但不经命令」这条路径——那条由登记时的人工实读承担，
    /// 见 <see cref="RequestsWithoutDownstreamAuthority"/> 上的证据清单。</para>
    /// </remarks>
    [Fact]
    public void Registered_absence_of_downstream_authority_is_still_true()
    {
        var pinned = DownstreamBounds
            .SelectMany(pair => pair.Value.OfType<NoDownstreamAuthority>()
                .Select(authority => (Request: pair.Key, Authority: authority)))
            .ToArray();

        // 值域非空：封闭集一旦空掉，Assert.All 对空集恒真，本条会静默退化成空断言。
        Assert.NotEmpty(pinned);

        var violations = new List<string>();
        foreach (var (request, authority) in pinned)
        {
            // 每条豁免都必须点名它声称「无权威」的下游命令：不点名就没有可验证的内容，
            // 那正是 #3290 里「理由不可复核」的形状。
            Assert.NotEmpty(authority.DownstreamCommands);

            foreach (var command in authority.DownstreamCommands)
            {
                if (command.GetProperty(IdempotencyKeyPropertyName, BindingFlags.Instance | BindingFlags.Public)
                    is not null)
                {
                    violations.Add(
                        $"  {request.Name} 登记为「下游零权威」，但 {command.Name} 已经带上了 {IdempotencyKeyPropertyName} 成员"
                        + "——键已进入下游，必须撤销本登记并登记真实权威。");
                    continue;
                }

                var bound = new CommandValidatorBound(command).Resolve();
                if (bound > 0)
                {
                    violations.Add(
                        $"  {request.Name} 登记为「下游零权威」，但 {command.Name} 的校验器解析出上界 {bound}"
                        + "——必须撤销本登记并登记真实权威。");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"「下游零权威」的登记理由已不成立（{violations.Count} 处 / 共检查 {pinned.Length} 项）：\n"
            + string.Join("\n", violations));
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

    /// <summary>网关程序集。用一个位于其中的公开类型取，避免手抄程序集名字符串。</summary>
    private static Assembly GatewayAssembly =>
        typeof(BusinessConsoleSetMasterDataResourceEnabledRequest).Assembly;

    /// <summary>
    /// 受全局钳作用的网关代理请求类型（维度 A）：
    /// <see cref="AuthorizedBusinessProxyEndpoint{TRequest,TResponse}"/> 全部**非抽象**后代的
    /// <c>TRequest</c>，按 <c>BusinessGatewayIdempotencyKey.Resolve</c> 自己的判据过滤。
    /// </summary>
    /// <remarks>
    /// 判据必须与 <c>Resolve</c> 逐字一致：<c>GetProperty("IdempotencyKey", Instance | Public)</c>
    /// 且 <c>PropertyType == typeof(string)</c>。属性名写成 <c>IdempotencyKey</c> 但类型不是
    /// <c>string</c> 的请求**不受钳作用**（<c>Resolve</c> 会原样返回），因此也不进本集合——
    /// 今天这样的类型是 0 个，但判据按 <c>Resolve</c> 写而不是按今天的事实写。
    /// </remarks>
    private static HashSet<Type> ClampedProxyRequestTypes()
    {
        var clamped = new HashSet<Type>();
        foreach (var type in GatewayAssembly.GetTypes())
        {
            if (type.IsAbstract || ProxyRequestType(type) is not { } requestType)
            {
                continue;
            }

            if (requestType.GetProperty(IdempotencyKeyPropertyName, BindingFlags.Instance | BindingFlags.Public)
                ?.PropertyType == typeof(string))
            {
                clamped.Add(requestType);
            }
        }

        return clamped;
    }

    /// <summary>
    /// 沿继承链找 <see cref="AuthorizedBusinessProxyEndpoint{TRequest,TResponse}"/> 的闭合实参 <c>TRequest</c>。
    /// 走继承链而不是 <c>IsAssignableFrom</c>：需要的是泛型实参本身，基类可能被中间抽象类再包一层。
    /// </summary>
    private static Type? ProxyRequestType(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType
                && current.GetGenericTypeDefinition() == typeof(AuthorizedBusinessProxyEndpoint<,>))
            {
                return current.GetGenericArguments()[0];
            }
        }

        return null;
    }

    /// <summary>
    /// 从网关程序集的字段元数据读全局钳的值。见
    /// <see cref="Global_clamp_is_never_stricter_than_any_endpoint_level_bound"/> 注释里
    /// 「为什么不直接引用那个 const」。
    /// </summary>
    private static int ResolveGlobalClamp()
    {
        const string clampTypeName = "Nerv.IIP.BusinessGateway.Web.Application.Auth.BusinessGatewayIdempotencyKey";
        const string clampFieldName = "MaximumLength";

        var clampType = GatewayAssembly.GetType(clampTypeName);
        Assert.True(clampType is not null, $"网关程序集里找不到 {clampTypeName}，全局钳的值解析不到。");

        var field = clampType!.GetField(
            clampFieldName,
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.True(
            field is not null,
            $"{clampTypeName} 上找不到字段 {clampFieldName}，全局钳的值解析不到（改名了就把这里一起改，别让它静默跳过）。");

        return Assert.IsType<int>(field!.GetRawConstantValue());
    }

    internal static GatewayValidatorEnumeration EnumerateGatewayValidators()
    {
        var assembly = GatewayAssembly;

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

    /// <summary>
    /// 强制放行质量保留的幂等键落库列（#3324）。写入点实读：网关 <c>ForceReleaseQualityHoldAsync</c>
    /// 以 <c>X-Idempotency-Key</c> 头转发原始键，MES 侧 <c>MesQualityHoldRequestContext.Resolve</c>
    /// 只 <c>Trim</c>，再经 <c>QualityHoldTransition.Record</c>（构造器同样只 <c>Trim</c>）落本列，
    /// 落库的就是调用方原始键，列宽因此可达、不是幽灵权威。
    /// </summary>
    private static readonly DownstreamAuthority MesQualityHoldTransitionColumn =
        Column<MesQualityAggregate.QualityHoldTransition>(
            MesModel,
            nameof(MesQualityAggregate.QualityHoldTransition.IdempotencyKey),
            "Mes");

    private static readonly DownstreamAuthority ErpCodeKeyColumn =
        Column<CodeIdempotencyKey>(ErpModel, nameof(CodeIdempotencyKey.IdempotencyKey), "Erp");

    private static readonly DownstreamAuthority DemandPlanningCodeKeyColumn =
        Column<CodeIdempotencyKey>(
            DemandPlanningModel,
            nameof(CodeIdempotencyKey.IdempotencyKey),
            "DemandPlanning");

    private static readonly DownstreamAuthority ProductEngineeringCodeKeyColumn =
        Column<CodeIdempotencyKey>(
            ProductEngineeringModel,
            nameof(CodeIdempotencyKey.IdempotencyKey),
            "ProductEngineering");

    /// <summary>
    /// 工装写操作的审计身份列（#3326）。写入点实读：网关 <c>RequireIdempotentAuditContext</c>
    /// 以 <c>X-Idempotency-Key</c> 头转发原始键，MasterData 侧
    /// <c>ToolingOperationAuditContext.ToolingAuditSafeText.HttpAdmission.GetRequiredContext</c>
    /// 从该头读出 <c>OperationId</c>（只做形状校验、不派生），再经
    /// <c>ToolingAuditEntry.Register/Status/Usage</c>（构造器只 <c>Trim</c>）落本列，
    /// 落库的就是调用方原始键，列宽因此可达、不是幽灵权威。
    /// </summary>
    private static readonly DownstreamAuthority MasterDataToolingOperationColumn =
        Column<MasterDataToolingAggregate.ToolingAuditEntry>(
            MasterDataModel,
            nameof(MasterDataToolingAggregate.ToolingAuditEntry.OperationId),
            "MasterData");

    private static readonly DownstreamAuthority BarcodeLabelPrintBatchKeyColumn =
        Column<BarcodeLabelPrintBatchAggregate.LabelPrintBatch>(
            BarcodeLabelModel,
            nameof(BarcodeLabelPrintBatchAggregate.LabelPrintBatch.IdempotencyKey),
            "BarcodeLabel");

    private static readonly DownstreamAuthority BarcodeLabelScanRecordKeyColumn =
        Column<BarcodeLabelScanRecordAggregate.ScanRecord>(
            BarcodeLabelModel,
            nameof(BarcodeLabelScanRecordAggregate.ScanRecord.IdempotencyKey),
            "BarcodeLabel");

    private static readonly DownstreamAuthority InventoryStockMovementKeyColumn =
        Column<InventoryStockMovementAggregate.StockMovement>(
            InventoryModel,
            nameof(InventoryStockMovementAggregate.StockMovement.IdempotencyKey),
            "Inventory");

    /// <summary>
    /// 下游**零**幂等键长度权威的封闭豁免集。
    /// </summary>
    /// <remarks>
    /// <para>进这份集合的门槛是「已穷举下游、确认它对键长度不施加任何约束」，不是「暂时没查到」。
    /// 登记进来的位点**不参与**上界不等式，也**不要求**网关侧有端点级规则——
    /// 对一个下游根本不消费的键声明 <c>maxLength</c> 是假承诺，本仓判例明令不做。</para>
    ///
    /// <para><b>今天的两项（#3328）</b>：<c>BusinessConsoleAcknowledgeAlarmRequest</c> 与
    /// <c>BusinessConsoleUnshelveAlarmRequest</c>。它们的 <c>IdempotencyKey</c> **不是空头字段**——
    /// 消费者是**网关自己**：<c>BusinessIndustrialTelemetryClient.AcknowledgeAlarmAsync</c>（该方法里
    /// <c>string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : BusinessConsoleOperationReceipts.Accepted(...)</c>）
    /// 与同文件的 <c>UnshelveAlarmAsync</c> 用它决定是否签发 operation-receipt，
    /// 前端 <c>confirmBusinessConsoleOperation</c> 再拿 <c>receipt.idempotencyKey</c> 与本次业务意图比对。
    /// 摘掉字段 = 控制台与 PDA 的「确认报警 / 解除搁置」每次都抛「写操作缺少权威回执」，
    /// 所以 #3328 对这两处的裁定是**保留字段**，而不是像 Mes 那 5 处那样摘掉。</para>
    ///
    /// <para><b>「下游零权威」这句实测坐实过（#3328，均为实读，不是推断）</b>：</para>
    /// <list type="number">
    /// <item>下游端点 <c>AcknowledgeAlarmEndpoint</c> / <c>UnshelveAlarmEndpoint</c> 的 <c>HandleAsync</c>
    /// 拿到了 DTO 上的键却**不往命令里传**；<see cref="AcknowledgeAlarmCommand"/> 与
    /// <see cref="UnshelveAlarmCommand"/> 的构造参数里没有这个成员（本类
    /// <see cref="Registered_absence_of_downstream_authority_is_still_true"/> 按类型实测这一条）。</item>
    /// <item>IndustrialTelemetry 服务 src 里 <c>Idempotency-Key</c> / <c>X-Idempotency-Key</c>
    /// 命中 <b>0 处</b> ⇒ 网关补的那个请求头在下游根本没人读，走头也进不来。</item>
    /// <item>该服务 EF 模型里承载 <c>IdempotencyKey</c> 列的实体共三个：
    /// <c>AlarmShelveIdempotency</c>（唯一写入点在 <c>ShelveAlarmCommandHandler</c> 内）、
    /// <c>DeviceControlCommand</c>、CAP 的 <c>IntegrationEventDeadLetter</c>（存的是信封键）。
    /// 没有一个能从 acknowledge / unshelve 到达。</item>
    /// <item><c>AlarmAcknowledgedDomainEvent</c> 与 <c>AlarmUnshelvedDomainEvent</c> 在 Domain 之外
    /// **零引用**（无集成事件转换器、无 handler）⇒ 不发 CAP，也就没有信封键这条腿。</item>
    /// <item>网关自身**没有任何存储**：<c>Nerv.IIP.BusinessGateway.Web.csproj</c> 不引用任何 EF/Npgsql 包，
    /// gateway src 里 <c>DbContext</c> / <c>AddDbContext</c> 命中 0 处；唯一的缓存位点是
    /// <c>BusinessGatewayAuthorization</c> 的授权判定缓存，其缓存键由 bearer token 与授权要求构成，
    /// 不含幂等键。</item>
    /// <item>⚠️ 网关对该键还有**第三条**处置路径，别把上面几条读成穷举：
    /// <c>AuthorizedBusinessProxyEndpoint.CreateAuditContext</c> 会经
    /// <c>BusinessGatewayIdempotencyKey.ResolveForAudit</c> 把键放进 <c>BusinessServiceAuditContext</c>
    /// 并作为审计头转发下游。但它**够不到这两个位点**：确认报警与解除搁置的 <c>ForwardAsync</c>
    /// 都不构造审计上下文（<c>RequireAuditContext</c> / <c>RequireIdempotentAuditContext</c> 一次都没调）。
    /// 今天这两个入口的调用方全部落在 <c>Endpoints/MasterData/</c> 下（工装三处走 idempotent 那个重载，
    /// 其余主数据写面走另一个）——不写条数，那个数会过期。故不影响本登记的结论。</item>
    /// </list>
    /// <para>⚠️ <b>新增下游存储时必须撤销本登记</b>：一旦这两条命令开始接收该键、或该键开始落进
    /// IndustrialTelemetry 的任何一张表，本登记就不再为真，必须移出本集合并登记真实权威
    /// （命令校验器上界 / 承载列列宽）。</para>
    ///
    /// <para><b>为什么不顺手把键接下去（这是判断题，不是漏做）</b>：同族的
    /// <c>ShelveAlarm</c> 之所以真有下游键，是因为 <c>Shelve</c> **不是**自然幂等——
    /// <c>ShelveAlarmCommandHandler</c> 为它建了 <c>AlarmShelveIdempotencies</c> 去重表 + payload 指纹，
    /// 用来挡「A→B→延迟的 A」。而 <c>AlarmEvent.Acknowledge</c> 首句是
    /// <c>if (AcknowledgedAtUtc is not null) return;</c>、<c>AlarmEvent.Unshelve</c> 首句是
    /// <c>if (Status != "shelved") return false;</c>——**早退即幂等**，不改状态也不发域事件。
    /// 给一条重放本来就无副作用的路径再加一张去重表是过度防御，#3328 因此明确不做。</para>
    ///
    /// <para><b>这份集合不会自己退役的那一半仍然存在（#3290 判例，写清楚避免误信）</b>：
    /// <see cref="Only_pinned_requests_are_registered_without_a_downstream_authority"/> 只锁
    /// 「登记 None 必须同时进本集合」这一个方向。真正验证理由的是
    /// <see cref="Registered_absence_of_downstream_authority_is_still_true"/>，而它的射程
    /// **只到被点名的下游命令类型**：上面第 2–5 条（头、列、域事件、网关存储）
    /// 今天仍然由人工实读承担，没有机器守着。</para>
    ///
    /// <para><b>第三个方向（#3327）</b>：<see
    /// cref="Every_clamped_gateway_request_is_bounded_by_an_endpoint_rule_or_registered_without_downstream_authority"/>
    /// 还要求本集合 ⊆ 受全局钳作用的请求类型。⇒ 若哪天这两处的 <c>IdempotencyKey</c> 字段被摘掉
    /// （像 #3328 对 Mes 那 5 处做的那样），它们不再受钳作用，本登记必须**一并清除**，
    /// 而不是留在这里对着一个不存在的位点继续挂着。</para>
    /// </remarks>
    private static readonly HashSet<Type> RequestsWithoutDownstreamAuthority =
    [
        typeof(BusinessConsoleAcknowledgeAlarmRequest),
        typeof(BusinessConsoleUnshelveAlarmRequest),
    ];

    /// <summary>
    /// 「网关请求类型 → 下游权威」的登记表。**这张表是手写的链接，数值不是**：
    /// 每一项要么指向下游命令类型（上界从它自己的校验器读），要么指向下游 EF 实体属性（上界从列宽读），
    /// 要么显式声明下游零权威。
    /// <para>登记依据是逐条实读转发链：网关 endpoint 的 <c>ForwardAsync</c> → capability client 的路径字面量
    /// → 下游服务 <c>*EndpointContracts</c> 里同路径的 endpoint → 它 <c>HandleAsync</c> 里发出的命令
    /// → 该命令的校验器 / 该命令 handler 把幂等键写进的那一列。</para>
    /// <para><b>登记列宽的前提</b>：必须实读写入点，确认落库的就是调用方原始键。
    /// 走 <c>CodeAllocator</c> 的位点（MasterData、Erp 收货、Mes 工作台与生产写面、DemandPlanning 需求来源）、
    /// MasterData 生命周期审计的 <c>OperationId</c>、Mes 质量保留时间线的 <c>IdempotencyKey</c>
    /// 都只 <c>Trim</c>、不派生，列宽因此可达。</para>
    ///
    /// <para><b>「派生」不是一个判据，要分两类（#3290 与 #3176 是两条不同的判例，别混用）</b>：</para>
    /// <list type="number">
    /// <item><b>摘要/哈希类派生</b>（输出定长，与输入长度无关）⇒ 列宽对原始键**零约束**，是幽灵权威，不登记。
    /// 实例：Wms 两张 receipt 表落的是 <c>WmsText.IdempotencyKey</c> 的 SHA256，恒 75 字符定长（#3290）。</item>
    /// <item><b>拼接类派生</b>（长度单调：原始键每长一个字符，派生键就长一个字符）⇒ 列宽**仍然是真权威**，
    /// 只是有效上界 = 列宽 − 最长附加段（#3176）。**不要把这一类当幽灵权威豁免掉。**
    /// <para>⚠️ <c>InventoryIdempotencyKeyPolicy.BaseMaxLengthFor</c> /
    /// <c>ErpCodingIdempotencyKeyPolicy.BaseMaxLengthFor</c> 是这条判据的**同族实现，但不能直接套用**：
    /// 它们的 <c>params string[] suffixes</c> 只吃**定长字面量**后缀（取 <c>Max(x =&gt; x.Length)</c>）。
    /// 附加段里若含**变长**片段（组织/环境/单号这类由调用方决定的值），必须按**各段自己的列宽**
    /// 取最坏值再相减，不能把变长段当字面量丢进去——那样算出来的上界会偏大，是假承诺。
    /// 正确算法的实例见本表下方 RetryFinishedGoodsReceiptInventoryPosting 那段注释。</para></item>
    /// </list>
    ///
    /// <para>另有第三种「不登记」，与派生无关：<b>承载列本身无界</b>。Mes 线边退料把原始键作 JSON 字典键落
    /// <c>text</c> 列，<c>GetMaxLength()</c> 为 null，该位点上界由命令校验器单独承担。</para>
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
        // 这处端点级值取 200（= 下游 master_data_lifecycle_audit.operation_id 列宽），不取一个更小的数。
        // ⚠️ 这段原先写「全局钳 150 会让 151..200 的键先被拒，于是 maxLength: 200 是一句假承诺」——
        // **该状态已由 #3327 消除，别再照着读**：钳已抬到 >= max(端点级)，
        // Global_clamp_is_never_stricter_than_any_endpoint_level_bound 现在把这条关系钉住，
        // 本位点的 200 对客户端是真承诺。保留这段是为了记住**当时为什么不把它改成 150**：
        // 用一个更小的数在这里掩盖另一处组件的缺陷是打补丁，#3287 两次禁止。
        [typeof(BusinessConsoleSetMasterDataResourceEnabledRequest)] = [MasterDataLifecycleOperationColumn],

        // ---- Mes ----
        [typeof(BusinessConsoleMesClaimOperationTaskRequest)] = [Command<ClaimDispatchTaskCommand>()],
        [typeof(BusinessConsoleRecordProductionReportRequest)] = [Command<RecordProductionReportCommand>()],
        [typeof(BusinessConsoleMesOperationTaskActionRequest)] = [MesCodeKeyColumn],
        [typeof(BusinessConsoleMesRecordDefectV2Request)] = [MesCodeKeyColumn],
        [typeof(BusinessConsoleMesRecordDowntimeEventV2Request)] = [MesCodeKeyColumn],
        [typeof(BusinessConsoleMesSplitWorkOrderRequest)] = [Command<SplitWorkOrderCommand>()],
        [typeof(BusinessConsoleMesMergeWorkOrdersRequest)] = [Command<MergeWorkOrdersCommand>()],

        // ---- Mes（#3324：#3287 差集里 Mes 侧、下游可声明**正**上界的位点）----
        // 不写条数：那个计数每落一张子票就变，复述它等于制造一处会过期的手抄事实
        // （与本类 <remarks> 里删掉「118 个」同一条理由）。
        // 转发链逐条实读：网关 endpoint 的 ForwardAsync → BusinessMesClient 的路径字面量
        // → MesEndpoints.cs 里同路径的 MesEndpointContracts 条目 → 它 HandleAsync 里发出的命令。
        //
        // 下面 6 处的下游 handler 只把原始键交给 MesCodingService/CodeAllocator，
        // CodeAllocator.Normalize（CodeAllocator.cs:359-362）只 Trim、不派生，
        // 落 mes.code_idempotency_keys.idempotency_key(150)，故列宽对原始键可达。
        [typeof(BusinessConsoleCreateRushWorkOrderRequest)] = [MesCodeKeyColumn],
        [typeof(BusinessConsoleMesConvertPlanToWorkOrderRequest)] = [MesCodeKeyColumn],
        [typeof(BusinessConsoleMesCreateMaterialIssueRequest)] = [MesCodeKeyColumn],
        [typeof(BusinessConsoleMesCreateReceiptRequest)] = [MesCodeKeyColumn],
        [typeof(BusinessConsoleMesCreateShiftHandoverRequest)] = [MesCodeKeyColumn],
        [typeof(BusinessConsoleMesReverseProductionReportRequest)] = [MesCodeKeyColumn],

        // 这两处除了同一条 CodeAllocator 列，还先撞下游端点请求 DTO 自己的校验器
        // （RecordDefectRequestValidator / RecordDowntimeEventRequestValidator）。
        // 两个权威今天同为 150，但它们互相独立：谁先收窄谁就是有效上界，故都登记、取最小。
        [typeof(BusinessConsoleMesRecordDefectRequest)] =
            [DownstreamRequest<MesEndpointRequests.RecordDefectRequest>(), MesCodeKeyColumn],
        [typeof(BusinessConsoleMesRecordDowntimeEventRequest)] =
            [DownstreamRequest<MesEndpointRequests.RecordDowntimeEventRequest>(), MesCodeKeyColumn],

        // 强制放行质量保留：键走 X-Idempotency-Key 头进 MES，命令校验器与落库列同为 512。
        [typeof(BusinessConsoleMesForceReleaseQualityHoldRequest)] =
            [Command<ForceReleaseQualityHoldCommand>(), MesQualityHoldTransitionColumn],

        // 完工入库过账重投（#3324 移出、#3332 修好键的构成、#3327 接回来）。
        // ⚠️ 别沿用 #3324 当时那句「不存在任何正的上界」——那句的前提已经不在了。
        //
        // 旧事实（#3324 当时成立、现已作废）：原始键被拼成
        // "mes:finished-goods-receipt:{org}:{env}:{requestNo}:{原始键}" 跨服务进 Inventory，
        // 纯拼接长度单调 ⇒ stock_movements.idempotency_key(128) 是真权威，
        // 而按各段列宽取最坏情况前缀本身就 27 + 100+1 + 100+1 + 100+1 = 330 > 128，
        // 连空键都放不下 ⇒ 那时确实不存在任何**正**的上界可供声明。
        //
        // 新事实（#3332）：该键改为**两段式回落**（FinishedGoodsReceiptInventoryPostingKey.BuildRetry）。
        // 于是 stock_movements.idempotency_key 对本位点是**条件性派生**，与 WmsText.LineIdempotencyKey
        // 同形，**不是** #3290 那种无条件摘要的纯幽灵权威。两支都要读，哪一支走哪条写死在这里：
        //   · 整键 <= FinishedGoodsReceiptInventoryPostingKey.ColumnMaxLength（128）
        //     ⇒ BuildRetry 返回 candidate 本身、**逐字保持**（含调用方原始键的那个字符串原样落库）
        //     ⇒ 那一列**是**这一支的真权威，有效上界 = 128 − 可读作用域段长度 − 1。
        //   · 整键 > 128
        //     ⇒ BuildRetry 返回 {作用域摘要}.{尾段摘要}，两段都是定长 base64url
        //     ⇒ 落库值与原始键长度无关，那一列对原始键**零约束**。
        // ⇒ 该列**不能**登记为本位点的上界（它只约束前一支，而端点级规则是无条件的）；
        //    但它**也不是**「完全不承重」——不要把这条读成「那一列可以随意收窄」。
        //    该列与回落形态之间的关系由 MesFinishedGoodsReceiptInventoryPostingKeyBoundContractTests
        //    从两侧 EF 模型派生钉住，不由本表承担。
        // ⇒ 可登记的**有效上界**取 Mes 命令校验器
        //    RetryFinishedGoodsReceiptInventoryPostingCommandValidator（MesProductionCommands.cs，
        //    RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(200)），它是本位点**唯一**可解析的下游权威：
        //    下游端点 DTO RetryFinishedGoodsReceiptInventoryPostingRequest 没有自己的校验器
        //    （实读 Mes Web 程序集里 AbstractValidator<RetryFinishedGoodsReceiptInventoryPostingRequest> 共 0 个）。
        [typeof(BusinessConsoleMesRetryFinishedGoodsReceiptInventoryPostingRequest)] =
            [Command<RetryFinishedGoodsReceiptInventoryPostingCommand>()],

        // 线边退料：**不登记承载列**，但理由与上一条不同——不是派生值，是那一列无界。
        // 原始键（只 Trim）作为 JSON 字典的键落 material_issue_requests
        // .line_side_return_idempotency_keys_json，该列 HasColumnType("text") 且没有 HasMaxLength，
        // GetMaxLength() 为 null ⇒ 登记它只会让 Every_declared_downstream_authority_resolves 报「解析不到」。
        // 该列对键长度不施加上界，本位点的上界由命令校验器单独承担。
        [typeof(BusinessConsoleMesReturnLineSideMaterialRequest)] =
            [Command<ReturnLineSideMaterialCommand>()],

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

        // ---- ProductEngineering（#3326：#3287 差集里 ProductEngineering 侧的位点）----
        // 转发链逐条实读：网关 endpoint 的 ForwardAsync → HttpBusinessProductEngineeringClient 的路径字面量
        // → ProductEngineeringReleaseEndpoints.cs 里同路径的 ProductEngineeringEndpointContracts 条目
        // → 它 HandleAsync 里发出的命令 → 该命令 handler 的写入点。
        //
        // 这些位点的 handler 都把**原始键**交给 ProductEngineeringCodingService.AllocateAsync
        // → CodeAllocator，CodeAllocator.Normalize（CodeAllocator.cs:359-362）只 Trim、不派生，
        // 落 product_engineering 库的 code_idempotency_keys.idempotency_key(150)，故列宽对原始键可达。
        // ⚠️ 措辞要准：这些命令**每一条都有校验器**（各自的 AbstractValidator<T>），
        // 只是没有一条含幂等键长度规则——实读 ProductEngineering Web 程序集里全部
        // `RuleFor(x => x.IdempotencyKey)` 只有 1 处，属于 CreateStandardOperationCommand（上一条）。
        // 所以登记列宽不是「顺带加一个」，而是这些位点**唯一**的下游权威。
        [typeof(BusinessConsoleRegisterEngineeringDocumentRequest)] = [ProductEngineeringCodeKeyColumn],
        [typeof(BusinessConsolePublishSopDocumentRequest)] = [ProductEngineeringCodeKeyColumn],
        [typeof(BusinessConsoleCreateEngineeringItemRevisionRequest)] = [ProductEngineeringCodeKeyColumn],
        [typeof(BusinessConsoleReleaseEngineeringBomRequest)] = [ProductEngineeringCodeKeyColumn],
        [typeof(BusinessConsoleReleaseManufacturingBomRequest)] = [ProductEngineeringCodeKeyColumn],
        [typeof(BusinessConsoleReleaseRoutingRequest)] = [ProductEngineeringCodeKeyColumn],
        [typeof(BusinessConsoleReleaseEngineeringChangeRequest)] = [ProductEngineeringCodeKeyColumn],

        // ---- MasterData（#3326）----
        // 建 SKU 与本表里其它登记 MasterDataCodeKeyColumn 的 create 位点同路：
        // MasterDataCodingService → CodeAllocator（只 Trim），落同一张 master_data 的
        // code_idempotency_keys.idempotency_key。不写条数：那个数每落一张子票就变
        // （与本类 <remarks> 里删掉「118 个」同一条理由）。
        [typeof(BusinessConsoleCreateSkuRequest)] = [MasterDataCodeKeyColumn],

        // 工装三处的键**不经请求体进命令**：网关 RequireIdempotentAuditContext 把它放进
        // X-Idempotency-Key 头（BusinessMasterDataClient.ConfigureAuditHeaders），MasterData 侧
        // ToolingOperationAuditContext...HttpAdmission.GetRequiredContext 从该头读出 OperationId，
        // 落 tooling_audit_entries.operation_id(200)。详见 MasterDataToolingOperationColumn 的注释。
        // 注册工装还额外把同一个 OperationId 交给 MasterDataCodingService → CodeAllocator，
        // 落 code_idempotency_keys.idempotency_key(150)；两条腿都登记，有效上界取最小（#3281 判据）。
        // ⚠️ 措辞要准：这三条命令**连校验器都不存在**（MasterData Web 程序集里
        // AbstractValidator<RegisterToolingAssetCommand/ChangeToolingStatusCommand/RecordToolingUsageCommand>
        // 各 0 个；该程序集内 `RuleFor(...IdempotencyKey)` 共 0 处），列宽因此是唯一的下游权威。
        // ⚠️ HttpAdmission.RequireIdentity 另有一条 MaxIdentityLength = 200 的头长度上限，与该列同值；
        // 它是 private const，本表的解析机制够不到，故不登记——它不改变有效上界。
        // 状态变更与用量登记这两处端点级值取 200（= 下游列宽），与
        // BusinessConsoleSetMasterDataResourceEnabledRequest 同一形状。
        // ⚠️ 同一句「151..200 被全局钳先拒、maxLength: 200 是假承诺」的旧表述**已随 #3327 抬钳作废**，
        // 理由与那一处逐字相同，见该处注释。
        [typeof(BusinessConsoleRegisterToolingAssetRequest)] =
            [MasterDataToolingOperationColumn, MasterDataCodeKeyColumn],
        [typeof(BusinessConsoleChangeToolingStatusRequest)] = [MasterDataToolingOperationColumn],
        [typeof(BusinessConsoleRecordToolingUsageRequest)] = [MasterDataToolingOperationColumn],

        // ---- BarcodeLabel（#3326）----
        // 转发链：CreateBusinessConsoleBarcodePrintBatchEndpoint / RecordBusinessConsoleBarcodeScanEndpoint
        // → HttpBusinessBarcodeLabelClient 的 "/api/business/v1/barcodes/print-batches"
        // 与 "/api/business/v1/barcodes/scans" → BarcodeLabelEndpoints.cs 同路径的
        // CreateLabelPrintBatchEndpoint / RecordScanEndpoint → 各自的命令。
        // 两处的命令校验器各带一条 MaximumLength(128)，落库列 label_print_batches.idempotency_key(128)
        // 与 scan_records.idempotency_key(128) 同宽；域构造只 BarcodeLabelText.Required（Trim），
        // 落的就是调用方原始键，故命令校验器与列宽两个权威都登记、取最小。
        // ⚠️ 这两处的下游上界 128 **小于**网关全局钳：本票补规则之前，「> 128 且 <= 钳」的键在网关放行、
        // 到 BarcodeLabel 才被拒。不写钳的具体数：#3327 已把它从 150 改到 512，这类现在时的数字会过期。
        [typeof(BusinessConsoleCreateBarcodePrintBatchRequest)] =
            [Command<CreateLabelPrintBatchCommand>(), BarcodeLabelPrintBatchKeyColumn],
        [typeof(BusinessConsoleRecordBarcodeScanRequest)] =
            [Command<RecordScanCommand>(), BarcodeLabelScanRecordKeyColumn],
        // 退役入口原样转发 IdempotencyKey 到 decision 命令与持久化列。
        [typeof(RetireBusinessConsoleBarcodeTemplateAssetRequest)] =
            [Command<CreateTemplateAssetRetirementDecisionCommand>(),
                Column<TemplateAssetRetirementDecision>(BarcodeLabelModel,
                    nameof(TemplateAssetRetirementDecision.IdempotencyKey), "BarcodeLabel")],

        // ---- Wms（#3326）：出库过账重投 ----
        // 转发链：RetryBusinessConsoleWmsOutboundInventoryPostingEndpoint → BusinessWmsClient 的
        // "/api/business/v1/wms/outbound-orders/{id}/inventory-posting/retry"
        // → RetryOutboundInventoryPostingEndpoint → RetryOutboundInventoryPostingCommand。
        // handler 第一件事就是把键喂给 WmsText.IdempotencyKey——无条件 SHA256，产出恒 75 字符的
        // "wms-key-v2:<64 hex>"；此后进 Inventory 预留键与 InventoryMovementRequests 的都是那个**定长**派生值，
        // 对原始键零约束（#3290 幽灵权威），故沿途各列一概不登记。
        // 命令校验器 MaximumLength(150) 是这一处**唯一**的下游权威。
        [typeof(BusinessConsoleRetryWmsOutboundInventoryPostingRequest)] =
            [Command<RetryOutboundInventoryPostingCommand>()],

        // ---- Inventory（#3326）：库存移动过账 ----
        // 转发链：PostBusinessConsoleInventoryMovementEndpoint → BusinessInventoryClient 的
        // "/api/inventory/v1/movements" → PostStockMovementEndpoint → PostStockMovementCommand。
        // 这条腿是**直接**调用，不是 #3332 那条经 BuildInventoryPostingRetryIdempotencyKey 拼前缀后
        // 跨服务进 Inventory 的派生路径：请求体里的原始键原样进命令，非调拨移动原样落
        // stock_movements.idempotency_key(128)（StockMovement 构造只 InventoryText.Required，即 Trim）。
        // 命令校验器 RequiredInventoryCode(InventoryValidationRules.IdempotencyKeyMaxLength) 与列同宽，
        // 两个权威互相独立，都登记、取最小。
        // ⚠️ 下游上界 128 **小于**网关全局钳（不写钳的具体数，它会变；关系由
        // Global_clamp_is_never_stricter_than_any_endpoint_level_bound 与本条不等式各自看守）。
        // ⚠️ 本条**不覆盖调拨支**（MovementType = transfer）：那一支在 handler 里再拼 ":out" / ":in"，
        // 有效上界收到 PostStockMovementCommandHandler.TransferBaseIdempotencyKeyMaxLength（列宽 − 最长腿后缀），
        // 由 InventoryPostingRejectedException 在运行时拒绝。那是**条件**上界，而网关这条规则是无条件的，
        // 对该支仍偏宽——如实写在这里，不在本票里私自收窄：收窄会把非调拨的合法长键挡在门外，
        // 而「网关比下游窄」这个方向归 #3287，本类不断言。
        [typeof(BusinessConsolePostStockMovementRequest)] =
            [Command<PostStockMovementCommand>(), InventoryStockMovementKeyColumn],

        // ---- IndustrialTelemetry：确认报警 / 解除搁置（#3328 裁定：保留字段，登记为「下游零权威」）----
        // 这两处**没有**端点级 MaximumLength 规则，这是有意的：网关对一个下游根本不消费的键声明
        // maxLength 会把「这里有幂等保护」的错觉写进公开 OpenAPI 契约（本仓判例：护栏自称完备比有洞更坏）。
        // 它们靠 None(...) 进入本表并被封闭豁免集钉住，因此
        // Every_gateway_endpoint_level_bound_is_covered_by_the_downstream_map 的 stale 那一支扣掉它们。
        //
        // ⚠️ 措辞要准（#3325 那轮抓过同形的量词越界）：说的是「**没有一条**作用于这两个位点的
        // 幂等键长度规则」，不是「整个 IndustrialTelemetry 程序集没有」——该程序集里有好几条
        // （ShelveAlarm 的端点 DTO 与命令、DeviceControlCommand 的端点 DTO 与命令），
        // 只是没有一条落在这两条腿上。
        //
        // 键为什么保留、「下游零权威」凭什么成立、以及新增下游存储时必须撤销本登记，
        // 全部写在 RequestsWithoutDownstreamAuthority 的注释里（含五条实测证据与自动退役机制）。
        [typeof(BusinessConsoleAcknowledgeAlarmRequest)] =
        [
            None(
                "键的消费者是网关自己的 operation-receipt（BusinessIndustrialTelemetryClient.AcknowledgeAlarmAsync），"
                + "不进下游命令、不落下游任何一列",
                typeof(AcknowledgeAlarmCommand)),
        ],
        [typeof(BusinessConsoleUnshelveAlarmRequest)] =
        [
            None(
                "键的消费者是网关自己的 operation-receipt（BusinessIndustrialTelemetryClient.UnshelveAlarmAsync），"
                + "不进下游命令、不落下游任何一列",
                typeof(UnshelveAlarmCommand)),
        ],

        // ---- IndustrialTelemetry（网关侧分别落在 Equipment 与 Telemetry 两个端点文件） ----
        [typeof(BusinessConsoleShelveAlarmRequest)] = [Command<ShelveAlarmCommand>()],
        [typeof(BusinessConsoleTelemetryDeviceControlCommandRequest)] = [Command<CreateDeviceControlCommandCommand>()],

        // ---- Erp ----
        [typeof(BusinessConsoleRecordErpPurchaseReceiptRequest)] = [ErpCodeKeyColumn],

        // ---- Erp（#3325：#3287 差集里 Erp 侧的位点）----
        // 不写条数：那个计数每落一张子票就变（与本类 <remarks> 里删掉「118 个」同一条理由）。
        // 转发链逐条实读：网关 endpoint 的 ForwardAsync → HttpBusinessErpClient 的路径字面量
        // → ErpProcurementEndpointContracts / ErpSalesEndpointContracts / ErpFinanceEndpointContracts
        // 里同路径的 endpoint → 它 HandleAsync 里发出的命令 → 该命令 handler 的写入点。
        //
        // 下面这些位点的下游 handler 都把**原始键**交给 ErpCodingService → CodeAllocator，
        // CodeAllocator.Normalize（CodeAllocator.cs:359-362）只 Trim、不派生，
        // 落 erp.code_idempotency_keys.idempotency_key(150)，故列宽对原始键可达。
        // 这些命令**没有一条**带幂等键长度规则（实读 Erp Web 程序集里全部
        // `RuleFor(x => x.IdempotencyKey)` 共 5 处，无一属于这批命令），
        // 所以登记列宽不是「顺带加一个」，而是这些位点**唯一**的下游权威。
        // ⚠️ 措辞要准：**不是**「这批命令零校验器」——多数命令有校验器，只是那些校验器
        // 都不含幂等键长度规则。真正连校验器都不存在的只有 ApprovePaymentExecutionCommand
        // 与 RegisterCashReceiptCommand 两条。两句话的下游结论相同（登记列宽即唯一权威），
        // 但前一句是量词越界，会被后续票直接抄走。
        [typeof(BusinessConsoleApproveErpPaymentExecutionRequest)] = [ErpCodeKeyColumn],
        [typeof(BusinessConsoleCreateErpAccountPayableRequest)] = [ErpCodeKeyColumn],
        [typeof(BusinessConsoleCreateErpAccountReceivableRequest)] = [ErpCodeKeyColumn],
        [typeof(BusinessConsoleCreateErpCostCandidateRequest)] = [ErpCodeKeyColumn],
        [typeof(BusinessConsoleCreateErpPurchaseOrderRequest)] = [ErpCodeKeyColumn],
        [typeof(BusinessConsoleCreateErpPurchaseRequisitionRequest)] = [ErpCodeKeyColumn],
        [typeof(BusinessConsoleCreateErpQuotationRequest)] = [ErpCodeKeyColumn],
        [typeof(BusinessConsoleCreateErpRequestForQuotationRequest)] = [ErpCodeKeyColumn],
        [typeof(BusinessConsoleCreateErpSalesOrderRequest)] = [ErpCodeKeyColumn],
        [typeof(BusinessConsoleOpenErpOpportunityRequest)] = [ErpCodeKeyColumn],
        [typeof(BusinessConsolePostErpJournalVoucherRequest)] = [ErpCodeKeyColumn],
        [typeof(BusinessConsoleReceiveErpSupplierQuotationRequest)] = [ErpCodeKeyColumn],
        [typeof(BusinessConsoleRegisterErpCashReceiptRequest)] = [ErpCodeKeyColumn],
        [typeof(BusinessConsoleReleaseErpDeliveryOrderRequest)] = [ErpCodeKeyColumn],

        // 采购申请转采购订单：**唯一登记命令校验器而不是列宽的 Erp 位点**，理由是拼接。
        // PO 分支原样使用原始键；RFQ 分支落库前经 ErpCodingIdempotencyKeyPolicy.Compose
        // 追加 ":rfq"（ErpProcurementCommands.cs:452-455）。拼接的长度是**单调**的
        // （原始键每长一个字符派生键就长一个字符），所以那一列**是**真权威，
        // 不是 #3290 那种定长哈希幽灵权威——但它在这条腿上的**有效**上界是 150 − 4 = 146。
        // 登记表因此指向已经做完这个减法的 ConvertPurchaseRequisitionsToPurchaseOrderCommandValidator
        // （其值由 ErpCodingIdempotencyKeyPolicy.BaseMaxLengthFor(":rfq") 从 ColumnMaxLength 派生，#3288）；
        // 若在这里再登记一次原始列宽，登记的就是一个把该腿高估 4 个字符的数——
        // 因为取最小它不会改变不等式结果，但那正是「结论对、理由错」的形状，故不登。
        [typeof(BusinessConsoleConvertErpPurchaseRequisitionsRequest)] =
            [Command<ConvertPurchaseRequisitionsToPurchaseOrderCommand>()],
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
    private sealed class NoDownstreamAuthority(string reason, params Type[] downstreamCommands) : DownstreamAuthority
    {
        /// <summary>本项声称「对幂等键长度不施加任何约束」的下游命令类型。</summary>
        /// <remarks>
        /// 它不是装饰：<see cref="Registered_absence_of_downstream_authority_is_still_true"/> 会对这些类型实测，
        /// 一旦其中任何一个长出 <c>IdempotencyKey</c> 成员或幂等键长度规则，本登记立刻报红。
        /// </remarks>
        public IReadOnlyList<Type> DownstreamCommands { get; } = downstreamCommands;

        public override string ToString() =>
            $"none({reason}; downstream: {string.Join(", ", downstreamCommands.Select(x => x.Name))})";
    }

    /// <summary>下游命令（或下游端点请求 DTO，见 <c>DownstreamRequest</c>）自己的校验器对 <c>IdempotencyKey</c> 施加的上界。</summary>
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

    /// <summary>
    /// 下游服务**HTTP 端点请求 DTO** 自己的校验器施加的上界（#3324）。
    /// 网关转发的 body 先撞这一层，再进命令层，所以它与命令校验器是两个独立的下游权威，
    /// 有效上界取两者最小（#3281 判据）。解析机制与 <see cref="CommandValidatorBound"/> 完全相同：
    /// 在该类型所在程序集里找 <c>IValidator&lt;T&gt;</c> 实现并读它建出来的规则。
    /// <para><b>这只是文档性命名</b>：它与 <see cref="Command{TCommand}"/> 构造的是同一个
    /// <see cref="CommandValidatorBound"/>，<c>ToString()</c> 也一样打 <c>validator(...)</c>，
    /// 机器上**不区分**这两种权威，别指望它能挡住把命令类型传进来的误用。</para>
    /// </summary>
    private static DownstreamAuthority DownstreamRequest<TRequest>() => new CommandValidatorBound(typeof(TRequest));

    private static DownstreamAuthority Column<TEntity>(Func<DbContext> contextFactory, string propertyName, string label) =>
        new ColumnWidthBound(contextFactory, typeof(TEntity), propertyName, label);

    private static DownstreamAuthority None(string reason, params Type[] downstreamCommands) =>
        new NoDownstreamAuthority(reason, downstreamCommands);

    private static DbContext MasterDataModel() => ModelOnly<MasterDataDbContext>(
        options => new MasterDataDbContext(options, NullMediator.Instance));

    private static DbContext MesModel() => ModelOnly<MesDbContext>(
        options => new MesDbContext(options, NullMediator.Instance));

    private static DbContext ErpModel() => ModelOnly<ErpDbContext>(
        options => new ErpDbContext(options, NullMediator.Instance));

    private static DbContext DemandPlanningModel() => ModelOnly<DemandPlanningDbContext>(
        options => new DemandPlanningDbContext(options, NullMediator.Instance));

    private static DbContext ProductEngineeringModel() => ModelOnly<ProductEngineeringDbContext>(
        options => new ProductEngineeringDbContext(options, NullMediator.Instance));

    private static DbContext BarcodeLabelModel() => ModelOnly<BarcodeLabelDbContext>(
        options => new BarcodeLabelDbContext(options, NullMediator.Instance));

    private static DbContext InventoryModel() => ModelOnly<InventoryDbContext>(
        options => new InventoryDbContext(options, NullMediator.Instance));

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
