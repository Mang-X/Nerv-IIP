using System.Reflection;
using NetCorePal.Extensions.DistributedTransactions;

using AppHubConverters = Nerv.IIP.AppHub.Web.Application.IntegrationEventConverters;
using ApprovalConverters = Nerv.IIP.Business.Approval.Web.Application.IntegrationEventConverters;
using BarcodeLabelConverters = Nerv.IIP.Business.BarcodeLabel.Web.Application.IntegrationEventConverters;
using DemandPlanningConverters = Nerv.IIP.Business.DemandPlanning.Web.Application.IntegrationEventConverters;
using ErpConverters = Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;
using IndustrialTelemetryConverters = Nerv.IIP.Business.IndustrialTelemetry.Web.Application.IntegrationEventConverters;
using InventoryConverters = Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventConverters;
using MaintenanceConverters = Nerv.IIP.Business.Maintenance.Web.Application.IntegrationEventConverters;
using MasterDataConverters = Nerv.IIP.Business.MasterData.Web.Application.IntegrationEventConverters;
using MesConverters = Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using ProductEngineeringConverters = Nerv.IIP.Business.ProductEngineering.Web.Application.IntegrationEventConverters;
using QualityConverters = Nerv.IIP.Business.Quality.Web.Application.IntegrationEventConverters;
using SchedulingConverters = Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventConverters;
using WmsConverters = Nerv.IIP.Business.Wms.Web.Application.IntegrationEventConverters;
using OpsConverters = Nerv.IIP.Ops.Web.Application.IntegrationEventConverters;

namespace Nerv.IIP.Business.Acceptance.Tests;

/// <summary>
/// <c>IIntegrationEventConverter&lt;,&gt;</c> 实现的**登记完备性**（#3382 3a）。
/// </summary>
/// <remarks>
/// <para><b>被证的真不变量</b>：扫描面里的**每一个** converter 实现，都必须出现在
/// <see cref="IntegrationEventEnvelopeIdempotencyKeyBudgetContractTests.ConvertedProducerConverterTypes"/>（声明为已接入平台预算、
/// 附一条最坏输入读数）或 <see cref="PendingBudgetOnboardingConverters"/>（已登记、**尚未**接入）
/// 之一，且两张表不重叠、无重复、没有反射不到的条目。</para>
///
/// <para><b>它把失败方向从假绿翻成假红</b>：改前
/// <c>ConvertedProducerKeys()</c> 是手写 9 条、三条 <c>[Theory]</c> 只跑在这 9 条上
/// ⇒ 新写一个 converter（新服务、或**已登记服务新增一个事件种类**）跑在**零个用例**上、门禁全绿
/// （本仓同形状已栽三次：#3003 / #3135 / #3300）。改后新 converter 编译得过但本类**报红**，
/// 作者必须在两张表里择一登记 —— 登记进 <see cref="PendingBudgetOnboardingConverters"/> 也算数，
/// ⭐ 本装置要的是「这件事进入 diff 与审核视野」，不是「必须当场接入预算」。</para>
///
/// <para><b>⛔ 不是文本扫描护栏</b>：判据是 <c>Type.GetInterfaces()</c> 上的封闭泛型实参，
/// 不看源码文本（#3176 / PR #3214 三轮实证文本扫描不收敛，护栏自身涨到 1139 行、8 种绕法，已裁定移除）。
/// 登记侧用 <c>typeof(...)</c> 而不是字符串：登记一条**不存在**的 converter 是**编译错误**（CS0234/CS0246），
/// 连红都轮不到；登记一条存在但**不再实现该接口**的类型，则由本类的「多出来的」那一向报红。</para>
///
/// <para><b>⭐ 扫描面（量词的适用范围，别读成全仓）</b>：
/// 本测试程序集 <c>bin</c> 目录下全部 <c>Nerv.IIP.*.dll</c>（排除 <c>*.Tests</c>），
/// 即本项目 <c>ProjectReference</c> 的**传递闭包产物**。
/// ⚠️ <b>刻意不用「沿 <c>GetReferencedAssemblies()</c> 走引用图」那条路</b>——
/// ⭐ <b>「109 vs 117」是一个<u>中间态</u>读数，⛔ 不是本文件 head 上的当前读数，别照着复现</b>：
/// 在「已加 <c>Nerv.IIP.Ops.Web</c> 的 <c>ProjectReference</c>、但登记表里<b>还没写</b>
/// <c>typeof(OpsConverters.*)</c>」的那一刻，因为测试代码没有引用它的任何类型，
/// C# 编译器把这条引用从元数据里裁掉了 ⇒ 引用图走法只枚举到 <b>109</b> 个 converter，
/// 而 <c>bin</c> 目录走法枚举到 <b>117</b> 个，Ops 那 8 个**静默隐身**
/// （#3122「身份判据退化成白名单」的同一形状）。
/// 登记表写上 <c>typeof</c> 之后编译器不再裁剪，<b>head 上两种走法都是 117</b>（实测）。
/// ⇒ 改走 <c>bin</c> 的理由**不是**「引用图今天少扫了」，而是
/// 「引用图会不会少扫，取决于源码有没有恰好硬引用到那个程序集里的类型」——
/// 这个条件不由本类控制，也不会在它失守时报红。
/// ⚠️ 顺带的正面依据：<b>main 上那条既有的引用图走法
/// （<see cref="CodeIdempotencyKeyCrossServiceWidthContractTests"/>）并没有正在少扫</b>
/// （main 上 bin 与引用图同为 81 个 <c>Nerv.IIP.*</c> 程序集、109 个 converter，复审实测）——
/// ⛔ 本条不是在指控既有实现有缺陷。
/// <c>bin</c> 目录走法不受编译器裁剪影响。
/// ⭐ 另有一条**编译期**的兜底：<see cref="PendingBudgetOnboardingConverters"/> 用 <c>typeof(...)</c>
/// 硬引用了 15 个服务程序集里的类型 ⇒ 撤掉其中任何一个 <c>ProjectReference</c> 都是**编译错误**（CS0234），
/// 「悄悄把扫描面改窄」这条路走不通（本票实测：撤掉 Ops.Web 的 ProjectReference ⇒ 构建红，不是测试绿）。</para>
///
/// <para><b>⭐ 本类不证明什么（覆盖边界，⛔ 别读成「关死了这一族」——它是收窄不是关死）</b>：</para>
/// <list type="number">
/// <item><b>非 converter 路径的 producer 仍然漏。</b>
/// ⛔「converter 是闭集」<b>不成立，已证伪</b>：按 95 个信封 record 类型名锚定扫全仓生产 <c>*.cs</c>，
/// 97 个生产构造点里 <b>90 个</b>落在 <c>IntegrationEventConverters/</c> 目录、<b>7 个</b>在外面，
/// 其中 <b>5 个是真 producer 旁路</b>（直接 <c>IIntegrationEventPublisher.PublishAsync</c>，不经任何 converter）：
/// <c>Quality/Application/Queries/Spc/SpcAnalysisQueries.cs</c> 的 <c>SpcAlertIntegrationEvents.Create</c>、
/// <c>Quality/.../PublishOverdueInspectionTaskRemindersCommand.cs</c>、
/// <c>Quality/.../PublishMeasuringDeviceCalibrationAlertsCommand.cs</c>、
/// <c>Mes/Application/Quality/WorkOrderReleaseProjectionBackfill.cs</c>、
/// <c>Inventory/.../InventoryMovementRequestedIntegrationEventHandlerForPostingMovement.cs</c>；
/// 另 2 个不是 producer（<c>Contracts.Maintenance</c> 里 v1→v2 归一的 <c>JsonConverter</c>、
/// <c>Scheduling</c> 的服务本地 <c>PersistableEnvelope</c>）。
/// ⇒ <b>本类对这 5 个零覆盖</b>，新写一个直接 <c>PublishAsync</c> 的 producer **不会**让本类报红。
/// ⚠️ 这是**存在式**举证（「至少 5 个」），⛔ 不是「恰好 5 个」：
/// 扫描面是 <c>new &lt;信封类型名&gt;(</c> 这一形态，经工厂方法/<c>with</c> 表达式产出的构造点不在其内。</item>
/// <item><b>「登记了但最坏输入给小了」仍是空转。</b>
/// 本类只管「在不在表里」，⛔ 完全不管「表里那个数对不对」——
/// 已接入的 9 条各自的最坏输入由
/// <see cref="IntegrationEventEnvelopeIdempotencyKeyBudgetContractTests"/> 从各自 EF 模型饱和派生，
/// 但「某条读数饱和的是不是**该 producer 真正最长的那些段**」没有任何机器判据，
/// 少饱和一段 ⇒ 那条 <c>[Theory]</c> 照绿而真实最坏长度更长。</item>
/// <item><b>不证明扫描面覆盖全仓服务。</b>
/// <see cref="Converter_scan_face_is_closed_over_repository_service_web_projects"/> 把
/// <b>后缀 <c>.Web.csproj</c> 且位于 <c>backend/services</c> 或 <c>backend/gateway</c> 之下</b>
/// 的项目与扫描面对撞（⚠️ 这个限定本身是第 4 条失效方向），此形态的新服务不登记即红；
/// ⛔ 但 <see cref="ScanFaceExcludedServiceProjects"/> 里那 3 个**排除项内部**若新增 converter，
/// 本类看不见（它们不在本测试项目的引用图里）。
/// ⚠️ 「今天那 3 个里 converter 数为 0」是 #3382 实施时按 <c>IIntegrationEventConverter&lt;</c>
/// 全仓文本扫描得到的**当前读数**（117 处声明全部落在上面那 15 个程序集里），
/// ⛔ **不是**被本类看守的性质——那条读数明天就可能过期而本类不会报红。</item>
/// <item><b>⭐ 闭集探针的「服务」身份判据是<u>命名 + 目录</u>，不是「是不是一个服务」。</b>
/// <see cref="Converter_scan_face_is_closed_over_repository_service_web_projects"/> 枚举的是
/// <c>backend/services</c> 与 <c>backend/gateway</c> 两个目录下后缀为 <c>.Web.csproj</c> 的项目文件。
/// ⇒ 下面两类新宿主**静默绿**（复审自设变异实测，两格均未报红）：
/// <list type="bullet">
/// <item>宿主项目**不叫** <c>*.Web</c>
/// （例：<c>backend/services/ProbeRvApi/src/Nerv.IIP.ProbeRvApi.Api/*.csproj</c>）；</item>
/// <item>叫 <c>*.Web</c> 但**不在那两个目录**下
/// （例：<c>backend/platform/…/Nerv.IIP.ProbeRvPlat.Web/*.csproj</c>。
/// 仓内今天已有一个同形状的存量项 <c>backend/common/Web/Nerv.IIP.Web/Nerv.IIP.Web.csproj</c>，
/// 它是共享 Web 库不是服务宿主、且本来就在扫描面里，因此今天无缺口）。</item>
/// </list>
/// ⇒ ⛔ <b>上一条的保证不能读成无限定的「新开一个服务而不做选择就红」</b>——
/// 那是**否定式全称**，已被上面两格证伪。
/// 按本仓「登记边界优于扩护栏」原则，本 PR **登记**这条边界而**不扩**身份判据
/// （#3122 判例：身份判据在实现时会退化成白名单，扩它只把绕法从 2 种变成 N 种）。</item>
/// </list>
/// </remarks>
public sealed class IntegrationEventConverterRegistryCompletenessContractTests
{
    /// <summary>
    /// 已在扫描面里、但**尚未**接入 <c>IntegrationEventIdempotencyKey</c> 平台预算的 converter。
    /// </summary>
    /// <remarks>
    /// <para>这不是豁免白名单，是**待接入台账**：登记在这里的每一条都仍然是
    /// 「最坏输入下的信封键长度**从未被算过**」的状态，销账路径是 #3382 票面 T0–T17
    /// （owner 2026-09-15 裁定**不启动**，票面保留作将来重估依据）。
    /// ⛔ 往这里加一条不是「修好了」，是「记上账了」——两者在 diff 里必须看得出区别，
    /// 所以本表与 <c>ConvertedProducerKeys()</c> 是**两张分开的表**而不是一张带标志位的表。</para>
    /// <para>⚠️ <b>⛔ 但别把「挪进已接入表」读成「必须真接入」——那道门槛比它看起来低。</b>
    /// 复审实测两格：
    /// <list type="bullet">
    /// <item>拿**平凡输入**把一条挪进 <c>ConvertedProducerKeys()</c> ⇒ 🔴 红，被
    /// <c>Fallback_shape_fits_the_narrowest_platform_carrier_column_for_every_converted_producer</c>
    /// 里的 <c>IsDigested</c> 哨兵抓住 ⇒ 确实不是「只换个名字」；</item>
    /// <item>但拿**手写的长字符串**（如 <c>new string('x', 200)</c>）＋**手写的**
    /// <c>PlainConcatenationLength</c> 挪进去 ⇒ 🟢 <b>27/27 全绿</b>。</item>
    /// </list>
    /// ⇒ 已接入表的真实门槛只是「输入够长、走得到回落分支」，
    /// ⛔ <b>它<u>不</u>校验那些段是否派生自被测服务自己的 EF 列宽</b>。
    /// ⚠️ 这个弱点**早于本 PR 就存在**，本 PR 不修、只登记；
    /// 它正是本类覆盖边界第 2 条「登记了但最坏输入给小了仍是空转」的**机器可证形态**。</para>
    /// <para>⚠️ 本表条目数**没有**签入棘轮常量（⛔ 不是 <c>N == K</c> 等式）：
    /// 本类要的是「集合相等」，集合相等已经强制任何增减都进 diff，再加一个计数常量只是同义重复。</para>
    /// </remarks>
    private static IReadOnlyList<Type> PendingBudgetOnboardingConverters() =>
    [
        // AppHub（3 条）
        typeof(AppHubConverters.ApplicationInstanceStatusChangedIntegrationEventConverter),
        typeof(AppHubConverters.ApplicationRegisteredIntegrationEventConverter),
        typeof(AppHubConverters.ConnectorHostRestoredIntegrationEventConverter),
        // Approval（6 条）
        typeof(ApprovalConverters.ApprovalApprovedIntegrationEventConverter),
        typeof(ApprovalConverters.ApprovalChainActionRecordedIntegrationEventConverter),
        typeof(ApprovalConverters.ApprovalRejectedIntegrationEventConverter),
        typeof(ApprovalConverters.ApprovalReturnedIntegrationEventConverter),
        typeof(ApprovalConverters.ApprovalStartedIntegrationEventConverter),
        typeof(ApprovalConverters.ApprovalStepOverdueIntegrationEventConverter),
        // BarcodeLabel（6 条）
        typeof(BarcodeLabelConverters.BarcodeScanAcceptedIntegrationEventConverter),
        typeof(BarcodeLabelConverters.InventoryMovementRequestedFromBarcodeScanIntegrationEventConverter),
        typeof(BarcodeLabelConverters.LabelPrintBatchCompletedIntegrationEventConverter),
        typeof(BarcodeLabelConverters.LabelPrintBatchCreatedIntegrationEventConverter),
        typeof(BarcodeLabelConverters.LabelScannedIntegrationEventConverter),
        typeof(BarcodeLabelConverters.ScanRejectedIntegrationEventConverter),
        // DemandPlanning（4 条）
        typeof(DemandPlanningConverters.MrpRunCompletedIntegrationEventConverter),
        typeof(DemandPlanningConverters.PlannedPurchaseSuggestedIntegrationEventConverter),
        typeof(DemandPlanningConverters.PlannedWorkOrderSuggestedIntegrationEventConverter),
        typeof(DemandPlanningConverters.PlanningSuggestionAcceptedIntegrationEventConverter),
        // Erp（16 条）
        typeof(ErpConverters.AccountPayableCreatedIntegrationEventConverter),
        typeof(ErpConverters.AccountReceivableCreatedIntegrationEventConverter),
        typeof(ErpConverters.CostCandidateCreatedIntegrationEventConverter),
        typeof(ErpConverters.DeliveryOrderOutboundOrderRequestedIntegrationEventConverter),
        typeof(ErpConverters.DeliveryOrderReleasedIntegrationEventConverter),
        typeof(ErpConverters.JournalVoucherPostedIntegrationEventConverter),
        typeof(ErpConverters.MaterialSupplyEtaChangedIntegrationEventConverter),
        typeof(ErpConverters.PurchaseOrderReleasedIntegrationEventConverter),
        typeof(ErpConverters.PurchaseReceiptInventoryMovementRequestedIntegrationEventConverter),
        typeof(ErpConverters.PurchaseReceiptRecordedIntegrationEventConverter),
        typeof(ErpConverters.PurchaseRequisitionCreatedIntegrationEventConverter),
        typeof(ErpConverters.SalesOrderCancelledIntegrationEventConverter),
        typeof(ErpConverters.SalesOrderChangedIntegrationEventConverter),
        typeof(ErpConverters.SalesOrderReleasedIntegrationEventConverter),
        typeof(ErpConverters.SalesReturnAuthorizedIntegrationEventConverter),
        typeof(ErpConverters.WorkOrderCostCompletedIntegrationEventConverter),
        // IndustrialTelemetry（4 条）
        typeof(IndustrialTelemetryConverters.AlarmClearedIntegrationEventConverter),
        typeof(IndustrialTelemetryConverters.AlarmEscalatedIntegrationEventConverter),
        typeof(IndustrialTelemetryConverters.AlarmRaisedIntegrationEventConverter),
        typeof(IndustrialTelemetryConverters.DeviceStateChangedIntegrationEventConverter),
        // Inventory（3 条）
        typeof(InventoryConverters.StockAvailabilityChangedIntegrationEventConverter),
        typeof(InventoryConverters.StockCountVarianceConfirmedIntegrationEventConverter),
        typeof(InventoryConverters.StockReservationExpiredIntegrationEventConverter),
        // Maintenance（5 条）
        typeof(MaintenanceConverters.AssetRestoredIntegrationEventConverter),
        typeof(MaintenanceConverters.AssetUnavailableIntegrationEventConverter),
        typeof(MaintenanceConverters.MaintenanceSparePartIssuedIntegrationEventConverter),
        typeof(MaintenanceConverters.MaintenanceWorkOrderCompletedIntegrationEventConverter),
        typeof(MaintenanceConverters.MaintenanceWorkOrderOpenedIntegrationEventConverter),
        // MasterData（8 条）
        typeof(MasterDataConverters.BusinessPartnerChangedIntegrationEventConverter),
        typeof(MasterDataConverters.DeviceAssetChangedIntegrationEventConverter),
        typeof(MasterDataConverters.ReferenceDataCodeChangedIntegrationEventConverter),
        typeof(MasterDataConverters.ResourceChangedIntegrationEventConverter),
        typeof(MasterDataConverters.SkuChangedIntegrationEventConverter),
        typeof(MasterDataConverters.SkuDisabledIntegrationEventConverter),
        typeof(MasterDataConverters.UnitOfMeasureChangedIntegrationEventConverter),
        typeof(MasterDataConverters.WorkCalendarChangedIntegrationEventConverter),
        // Mes（23 条）
        typeof(MesConverters.DefectRaisedIntegrationEventConverter),
        typeof(MesConverters.DowntimeRestoredIntegrationEventConverter),
        typeof(MesConverters.DowntimeStartedIntegrationEventConverter),
        typeof(MesConverters.FinishedGoodsReceiptRequestedForQualityIntegrationEventConverter),
        typeof(MesConverters.FinishedGoodsReceiptRequestedIntegrationEventConverter),
        typeof(MesConverters.MaterialIssueRequestCreatedIntegrationEventConverter),
        typeof(MesConverters.MaterialIssueRequestedIntegrationEventConverter),
        typeof(MesConverters.MaterialLineSideReceiptConfirmedIntegrationEventConverter),
        typeof(MesConverters.MaterialLineSideReturnRequestedIntegrationEventConverter),
        typeof(MesConverters.MaterialReturnedToWarehouseIntegrationEventConverter),
        typeof(MesConverters.OperationTaskCompletedIntegrationEventConverter),
        typeof(MesConverters.OperationTaskManualDispatchClearedIntegrationEventConverter),
        typeof(MesConverters.OperationTaskManuallyDispatchedIntegrationEventConverter),
        typeof(MesConverters.OperationTaskPausedIntegrationEventConverter),
        typeof(MesConverters.OperationTaskResumedIntegrationEventConverter),
        typeof(MesConverters.OperationTaskStartedIntegrationEventConverter),
        typeof(MesConverters.ProductionReportRecordedIntegrationEventConverter),
        typeof(MesConverters.ReworkWorkOrderCreatedIntegrationEventConverter),
        typeof(MesConverters.WorkOrderCancelledIntegrationEventConverter),
        typeof(MesConverters.WorkOrderClosedIntegrationEventConverter),
        typeof(MesConverters.WorkOrderCompletedIntegrationEventConverter),
        typeof(MesConverters.WorkOrderEngineeringChangeImpactDetectedIntegrationEventConverter),
        typeof(MesConverters.WorkOrderReleasedIntegrationEventConverter),
        // ProductEngineering（4 条）
        typeof(ProductEngineeringConverters.EngineeringBomReleasedIntegrationEventConverter),
        typeof(ProductEngineeringConverters.EngineeringChangeReleasedIntegrationEventConverter),
        typeof(ProductEngineeringConverters.ManufacturingBomReleasedIntegrationEventConverter),
        typeof(ProductEngineeringConverters.RoutingReleasedIntegrationEventConverter),
        // Quality（10 条）
        typeof(QualityConverters.CapaClosedIntegrationEventConverter),
        typeof(QualityConverters.CapaEffectivenessVerifiedIntegrationEventConverter),
        typeof(QualityConverters.CapaOpenedIntegrationEventConverter),
        typeof(QualityConverters.InspectionPassedIntegrationEventConverter),
        typeof(QualityConverters.InspectionRejectedIntegrationEventConverter),
        typeof(QualityConverters.NcrClosedIntegrationEventConverter),
        typeof(QualityConverters.NcrDispositionDecidedIntegrationEventConverter),
        typeof(QualityConverters.NcrInventoryDispositionRequestedIntegrationEventConverter),
        typeof(QualityConverters.NcrOpenedIntegrationEventConverter),
        typeof(QualityConverters.NcrReworkRequestedIntegrationEventConverter),
        // Scheduling（4 条）
        typeof(SchedulingConverters.ScheduleConflictDetectedIntegrationEventConverter),
        typeof(SchedulingConverters.SchedulePlanGeneratedIntegrationEventConverter),
        typeof(SchedulingConverters.SchedulePlanReleasedIntegrationEventConverter),
        typeof(SchedulingConverters.SchedulePlanRevokedIntegrationEventConverter),
        // Wms（10 条）
        typeof(WmsConverters.CountExecutionCompletedIntegrationEventConverter),
        typeof(WmsConverters.InboundOrderCompletedIntegrationEventConverter),
        typeof(WmsConverters.InventoryMovementRequestCreatedIntegrationEventConverter),
        typeof(WmsConverters.MaterialIssueOutboundPreparedIntegrationEventConverter),
        typeof(WmsConverters.OutboundOrderCancelledIntegrationEventConverter),
        typeof(WmsConverters.OutboundOrderCompletedIntegrationEventConverter),
        typeof(WmsConverters.WcsTaskCancelledIntegrationEventConverter),
        typeof(WmsConverters.WcsTaskCompletedIntegrationEventConverter),
        typeof(WmsConverters.WcsTaskDispatchedIntegrationEventConverter),
        typeof(WmsConverters.WcsTaskFailedIntegrationEventConverter),
        // Ops（8 条）
        typeof(OpsConverters.AuditRecordedIntegrationEventConverter),
        typeof(OpsConverters.OperationApprovalApprovedIntegrationEventConverter),
        typeof(OpsConverters.OperationApprovalRejectedIntegrationEventConverter),
        typeof(OpsConverters.OperationApprovalRequestedIntegrationEventConverter),
        typeof(OpsConverters.OperationTaskClaimedIntegrationEventConverter),
        typeof(OpsConverters.OperationTaskCompletedIntegrationEventConverter),
        typeof(OpsConverters.OperationTaskFailedIntegrationEventConverter),
        typeof(OpsConverters.OperationTaskRequestedIntegrationEventConverter),
    ];

    /// <summary>
    /// 仓库里存在、但**不在**本测试项目引用图里的服务 Web 项目。
    /// 每条都必须写清为什么不拉进来——⛔ 空着或凭感觉加一行，等于把扫描面悄悄改窄。
    /// </summary>
    private static IReadOnlyDictionary<string, string> ScanFaceExcludedServiceProjects() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // ⛔ 理由不是「拉进来会污染 WebApplicationFactory 的装配面」——本 PR 自证那条理由无效：
            // 加了 Nerv.IIP.Ops.Web 的 ProjectReference 之后整程序集 152 条读数**无一变化**，
            // 说明 WebApplicationFactory<TEntryPoint> 按**入口类型**定装配面，⛔ 不按本程序集的引用面。
            // 真实理由是下面这条实读：三者今天各自**零 converter、零集成事件发布点**，
            // 拉进来只增加编译面与产物体积，换不到任何鉴别力。
            // ⚠️ 这是**当前读数**不是被看守的性质——见类 <remarks> 覆盖边界第 3 条。
            ["Nerv.IIP.Iam.Web"] = "平台身份服务：IIntegrationEventConverter 命中 0、集成事件发布点命中 0（backend/services/Iam/src 实读）。",
            ["Nerv.IIP.FileStorage.Web"] = "文件存储服务：IIntegrationEventConverter 命中 0、集成事件发布点命中 0（backend/services/FileStorage/src 实读）。",
            ["Nerv.IIP.PlatformGateway.Web"] = "平台网关：IIntegrationEventConverter 命中 0、集成事件发布点命中 0（backend/gateway/PlatformGateway/src 实读）。",
        };

    /// <summary>
    /// ⭐ 本票的主断言：扫描面里每一个 converter 都必须被登记，缺一即红。
    /// 反向也红：登记表里出现扫描面里找不到的条目（类型还在但不再实现该接口）同样红。
    /// </summary>
    [Fact]
    public void Every_discovered_integration_event_converter_is_registered()
    {
        var discovered = DiscoverConverterTypes();
        var registered = RegisteredConverterTypes();

        var unregistered = discovered.Except(registered).OrderBy(Name, StringComparer.Ordinal).ToArray();
        var stale = registered.Except(discovered).OrderBy(Name, StringComparer.Ordinal).ToArray();

        Assert.True(
            discovered.Count > 0,
            "扫描面里一个 converter 都没枚举到——本条断言已退化成空转，先查扫描面而不是查登记表。");
        Assert.True(
            unregistered.Length == 0,
            $"这些 IIntegrationEventConverter<,> 实现没有登记（{unregistered.Length} 条）："
            + $"{string.Join(", ", unregistered.Select(Name))}。"
            + "接入了平台预算就进 ConvertedProducerKeys() 并补一条最坏输入读数；"
            + "没接入就进 PendingBudgetOnboardingConverters() 记账。");
        Assert.True(
            stale.Length == 0,
            $"登记表里这些条目在扫描面里找不到（{stale.Length} 条）：{string.Join(", ", stale.Select(Name))}。"
            + "它们已不再实现 IIntegrationEventConverter<,>，登记表须同步删除，否则表里会积垃圾。");
    }

    /// <summary>
    /// 两张表必须互斥且各自无重复——否则「已接入」与「待接入」可以同时成立，计数失去意义。
    /// </summary>
    [Fact]
    public void Registry_tables_are_disjoint_and_duplicate_free()
    {
        var onboarded = IntegrationEventEnvelopeIdempotencyKeyBudgetContractTests.ConvertedProducerConverterTypes();
        var pending = PendingBudgetOnboardingConverters();

        var onboardedDuplicates = Duplicates(onboarded);
        var pendingDuplicates = Duplicates(pending);
        var overlap = onboarded.Intersect(pending).OrderBy(Name, StringComparer.Ordinal).ToArray();

        Assert.True(
            onboardedDuplicates.Length == 0,
            $"ConvertedProducerKeys() 里有重复 converter：{string.Join(", ", onboardedDuplicates.Select(Name))}。");
        Assert.True(
            pendingDuplicates.Length == 0,
            $"PendingBudgetOnboardingConverters() 里有重复条目：{string.Join(", ", pendingDuplicates.Select(Name))}。");
        Assert.True(
            overlap.Length == 0,
            $"同一个 converter 同时登记为「已接入」和「待接入」：{string.Join(", ", overlap.Select(Name))}。");
    }

    /// <summary>
    /// 登记表两侧都必须**真的**实现 <c>IIntegrationEventConverter&lt;,&gt;</c>。
    /// 这条挡的是「往表里塞一个同名但无关的类型」——<c>typeof</c> 保证它存在，不保证它是 converter。
    /// </summary>
    [Fact]
    public void Every_registered_type_really_implements_the_converter_interface()
    {
        var notConverters = RegisteredConverterTypes()
            .Where(type => !ImplementsConverterInterface(type))
            .OrderBy(Name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            notConverters.Length == 0,
            $"登记表里这些类型不实现 IIntegrationEventConverter<,>（{notConverters.Length} 条）："
            + $"{string.Join(", ", notConverters.Select(Name))}。");
    }

    /// <summary>
    /// ⭐ 扫描面自身的闭集探针：仓库里每个服务 <c>*.Web.csproj</c>，
    /// 要么它的程序集在扫描面里，要么它在 <see cref="ScanFaceExcludedServiceProjects"/> 里带理由登记。
    /// ⇒ 新开一个服务而不做选择，本条红；⛔ 但排除项内部新增 converter 仍看不见（见类 <c>&lt;remarks&gt;</c> 第 3 条）。
    /// </summary>
    [Fact]
    public void Converter_scan_face_is_closed_over_repository_service_web_projects()
    {
        var root = FindRepositoryRoot();
        var projects = new[] { Path.Combine(root, "backend", "services"), Path.Combine(root, "backend", "gateway") }
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.Web.csproj", SearchOption.AllDirectories))
            .Where(path => !path.Replace('\\', '/').Contains("/tests/", StringComparison.Ordinal))
            .Select(Path.GetFileNameWithoutExtension)
            .Select(name => name!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            projects.Length > 0,
            $"从 {root} 下没枚举到任何服务 *.Web.csproj——本条已退化成空转，先查仓库根定位。");

        var scanned = ScanFaceAssemblyNames();
        var excluded = ScanFaceExcludedServiceProjects();

        var unaccounted = projects
            .Where(name => !scanned.Contains(name) && !excluded.ContainsKey(name))
            .ToArray();
        var staleExclusions = excluded.Keys
            .Where(name => !projects.Contains(name, StringComparer.Ordinal) || scanned.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            unaccounted.Length == 0,
            $"这些服务 Web 项目既不在 converter 扫描面里、也没登记为排除项（{unaccounted.Length} 个）："
            + $"{string.Join(", ", unaccounted)}。要么给本测试项目加 ProjectReference 把它纳入扫描面，"
            + "要么写清理由登记进 ScanFaceExcludedServiceProjects()。");
        Assert.True(
            staleExclusions.Length == 0,
            $"排除项登记已过期（{staleExclusions.Length} 个）：{string.Join(", ", staleExclusions)}。"
            + "它们要么已不存在，要么已经进了扫描面，登记须删除。");
    }

    private static IReadOnlyCollection<Type> RegisteredConverterTypes() =>
    [
        .. IntegrationEventEnvelopeIdempotencyKeyBudgetContractTests.ConvertedProducerConverterTypes(),
        .. PendingBudgetOnboardingConverters(),
    ];

    private static IReadOnlyCollection<Type> DiscoverConverterTypes() =>
        [.. ScanFaceAssemblies()
            .SelectMany(LoadableTypes)
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .Where(ImplementsConverterInterface)
            .Distinct()];

    private static bool ImplementsConverterInterface(Type type) =>
        type.GetInterfaces().Any(contract =>
            contract.IsGenericType
            && contract.GetGenericTypeDefinition() == typeof(IIntegrationEventConverter<,>));

    private static HashSet<string> ScanFaceAssemblyNames() =>
        [.. ScanFaceAssemblies().Select(assembly => assembly.GetName().Name!)];

    /// <summary>
    /// 扫描面 = <c>bin</c> 下全部 <c>Nerv.IIP.*.dll</c>（排除 <c>*.Tests</c>）。
    /// ⛔ 不走 <c>GetReferencedAssemblies()</c>：编译器会把「有 ProjectReference 但源码未引用类型」的
    /// 那条引用从元数据里裁掉，走引用图会静默少看见整整一个程序集（本票实测 109 vs 117）。
    /// </summary>
    private static IReadOnlyList<Assembly> ScanFaceAssemblies()
    {
        var assemblies = new List<Assembly>();
        foreach (var path in Directory
            .EnumerateFiles(AppContext.BaseDirectory, "Nerv.IIP.*.dll")
            .OrderBy(path => path, StringComparer.Ordinal))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (name.EndsWith(".Tests", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                assemblies.Add(Assembly.LoadFrom(path));
            }
            catch (BadImageFormatException)
            {
                // 本机产物里可能混入非托管/资源 dll，跳过；托管程序集加载失败会在下游断言上暴露。
            }
        }

        return assemblies;
    }

    private static Type[] Duplicates(IEnumerable<Type> types) =>
        [.. types
            .GroupBy(type => type)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(Name, StringComparer.Ordinal)];

    private static string Name(Type type) => type.FullName ?? type.Name;

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(type => type is not null)!;
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "README.md"))
                && Directory.Exists(Path.Combine(directory.FullName, "backend")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
