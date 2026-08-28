namespace Nerv.IIP.VocabularyGovernance.Tests;

/// <summary>
/// 词表漂移门禁白名单（#1703）。豁免按（值 × 文件）二元组生效：同文件出现其他被守护值、
/// 或其他文件出现同值，仍然红；条目未命中任何字面量时 stale 检查红（白名单只出不进）。
///
/// 每组豁免必须附中文裁决注释，且只允许两类：
/// <list type="bullet">
/// <item>「同值不同义」——字面量与词表常量恰好同值但语义不同，不可互相引用（永久豁免，票面 (a) 类）；</item>
/// <item>「待已登记跟踪票销账」——与词表常量同族的真实违例，当前票只落地门禁不修存量，
/// 修复由条目裁决中注明的跟踪票分批完成；每销一处必须同步删除对应豁免。</item>
/// </list>
/// </summary>
internal static class VocabularyDriftExemptions
{
    private const string Svc = "services/Business";

    public static readonly IReadOnlyList<VocabularyExemption> Entries =
    [
        // ── "active" ────────────────────────────────────────────────────────────────
        // 同值不同义：Nerv.IIP.Contracts.ProductEngineering.ProductionEngineeringContractStatuses.Active
        // 守护的是 PE 生产版本/契约的 active/archived 状态；下列文件中的 "active" 分别是
        // 标签模板状态、需求来源状态、SKU 生命周期、采集点激活状态、库位状态、主数据启停状态、
        // 检验方案/SPC 控制图状态、雇佣状态、IAM 组织/环境状态——各域自己的生命周期词，不可互相引用。
        ..Group("active", "同值不同义：各域生命周期/激活状态，非 PE 生产版本契约状态。",
            $"{Svc}/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Application/Queries/LabelTemplates/ListLabelTemplatesQuery.cs",
            $"{Svc}/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Application/Seed/WorldHistoryConsistencyValidator.cs",
            $"{Svc}/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Application/Seed/WorldHistorySeedService.cs",
            $"{Svc}/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Planning/PlanningInputAdapters.cs",
            $"{Svc}/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Commands/IndustrialTelemetryCommands.cs",
            $"{Svc}/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Queries/IndustrialTelemetryQueries.cs",
            $"{Svc}/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Queries/ListInventoryDirectoryQuery.cs",
            $"{Svc}/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Seed/WorldHistorySeedService.cs",
            $"{Svc}/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/IntegrationEventHandlers/PauseMaintenancePlansWhenDeviceDisabledHandler.cs",
            $"{Svc}/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkerSkillQualificationGate.cs",
            $"{Svc}/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Commands/MasterData/CreateMasterDataCommands.cs",
            $"{Svc}/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Commands/MasterData/MasterDataLifecycleCommands.cs",
            $"{Svc}/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/IntegrationEventConverters/MasterDataIntegrationEventConverters.cs",
            $"{Svc}/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Queries/GetPrincipalWorkContextQuery.cs",
            $"{Svc}/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Queries/ListMasterDataResourcesQuery.cs",
            $"{Svc}/Quality/src/Nerv.IIP.Business.Quality.Web/Application/IntegrationEventHandlers/InspectionTaskTriggerIntegrationEventHandlers.cs",
            $"{Svc}/Quality/src/Nerv.IIP.Business.Quality.Web/Application/IntegrationEventHandlers/PeriodicInspectionIntegrationEventHandlers.cs",
            $"{Svc}/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Queries/InspectionTasks/ListDuePeriodicInspectionTimeContextsQuery.cs",
            $"{Svc}/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Queries/Spc/SpcAnalysisQueries.cs",
            $"{Svc}/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Seed/LeaderDemoSeedService.cs",
            "services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs"),

        // ── "archived" ──────────────────────────────────────────────────────────────
        // 同值不同义：Scheduling 保留策略的 Prometheus 指标标签值，非 PE 契约状态。
        ..Group("archived", "同值不同义：指标标签值（快照归档计数），非 PE 生产版本契约状态。",
            $"{Svc}/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Urgency/OrderUrgencyRetentionWorker.cs"),

        // ── DemandPlanning 来源引用族（PascalCase） ─────────────────────────────────
        // 同值不同义：Coding 编码分配请求方标识（CodeAllocationRequest 最后一个参数），
        // 非 PlanningSuggestionAccepted 事件里的来源引用。
        ..Group("DemandPlanning", "同值不同义：编码分配请求方标识，非 DP 建议来源引用。",
            $"{Svc}/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Commands/DemandPlanningCodingService.cs"),
        // 同值不同义：MES 追溯图节点类型（与 OperationTask/ProductionReport/Material 同一枚举面），
        // 非 DP 下游单据引用；MES 就绪分类的来源标签 "BusinessMes" 同理。
        ..Group("WorkOrder", "同值不同义：MES 追溯图节点类型，非 DP 下游单据引用。",
            $"{Svc}/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Workbench/MesWorkbenchQueries.cs"),
        ..Group("BusinessMes", "同值不同义：MES 就绪分类来源标签，非 DP 下游服务引用。",
            $"{Svc}/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Readiness/MesReadinessReasonCodes.cs"),

        // ── 集成事件信封来源族 ──────────────────────────────────────────────────────
        // #1370 ③ 批次 D 已销账：DP 种子的下游服务/单据类型改引 DemandPlanningDownstreamReferences
        // （BusinessMes / WorkOrder），对应豁免已删除。裁决口径：PascalCase 的 DP 接受面与短横线小写的
        // 事件信封来源面（QualityIntegrationEventSources.BusinessMes = "business-mes"）是两个面、并存不合并；
        // 缺陷在种子而非契约取值，故只改种子。网关读面自用的第三变体 "BusinessMES"
        // （gateway/BusinessGateway/.../BusinessConsoleSearchService.cs）不在本门禁扫描范围
        // （只扫 backend/services/**），登记豁免会被 stale 检查判红，故仅在此备案不改不登记。
        // 同值不同义：分布式锁 key 的命名空间片段。锁前缀是既有锁的身份，与事件信封来源的演化无关，
        // 不可互相引用（改锁前缀会使在途锁失效）。
        ..Group("business-inventory", "同值不同义：分布式锁 key 命名空间片段，非事件信封来源。",
            $"{Svc}/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Commands/StockCounts/CreateStockCountTaskCommand.cs"),
        ..Group("business-product-engineering", "同值不同义：分布式锁 key 命名空间片段，非事件信封来源。",
            $"{Svc}/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/ProductEngineeringReleaseCommands.cs"),

        // ── 审批链状态匹配面 ────────────────────────────────────────────────────────
        // #1857 已销账：ApprovalChainStatuses 已下沉进 Nerv.IIP.Contracts.Approval，
        // PE 发布校验与 Quality 放行判定都改引 ApprovalChainStatuses.Approved
        //（而不是借用同值不同义的 ApprovalResults.Approved），对应豁免已删除。
        // 同值不同义：设备控制命令的审批态 / MRB 评审决定，都是各自域内状态机，非审批链词表。
        ..Group("approved", "同值不同义：设备控制命令审批态（DeviceControlCommand），非审批链结果词表。",
            $"{Svc}/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Seed/WorldHistoryControlCommandSpec.cs"),
        ..Group("approved", "同值不同义：MRB 评审决定（质量域内状态），非审批链结果词表。",
            $"{Svc}/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Seed/WorldHistoryConsistencyValidator.cs"),

        // ── 审批链状态 / 审批动作族（#1857 下沉后新进守护面的取值） ─────────────────
        // ApprovalChainStatuses 与 ApprovalDecisions 下沉进契约后，pending / withdrawn /
        // approve / reject / return / withdraw / resubmit / add_signer / transfer 一并进入扫描面。
        // 其中真违例（Notification 按审批动作分流待办/消息）已改引 ApprovalDecisions.Withdraw；
        // 下列全部是同值不同义的各域内状态机/动作词，永久豁免。
        // #2779 起 "pending" 另有 QualityFirstArticleConfirmationStatuses.Pending（首件确认进度）同值，
        // 下列各条的「非审批链状态」裁决同样覆盖它：各域内状态机词与首件确认进度互不相同义。
        ..Group("pending", "同值不同义：标签打印批次状态（pending/sent-to-printer），非审批链状态。",
            $"{Svc}/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Application/Seed/WorldHistoryConsistencyValidator.cs"),
        ..Group("pending", "同值不同义：出库单的库存过账状态（pending/posted/failed/not-started），非审批链状态。",
            $"{Svc}/Wms/src/Nerv.IIP.Business.Wms.Web/Application/Queries/WmsQueries.cs"),
        ..Group("pending", "同值不同义：质检任务状态（pending/in-progress/completed），非审批链状态。",
            $"{Svc}/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Seed/WorldHistoryConsistencyValidator.cs",
            $"{Svc}/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Queries/InspectionTasks/ListInspectionTasksQuery.cs"),
        ..Group("pending", "同值不同义：设备控制命令下发审批态（Ops 域内状态机），非审批链状态；与同文件 approved/rejected 同一裁决。",
            $"{Svc}/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Seed/WorldHistoryControlCommandSpec.cs",
            $"{Svc}/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Seed/WorldHistoryConsistencyValidator.cs"),
        ..Group("pending", "同值不同义：采集点激活状态（pending/active/error/disabled），非审批链状态；与同文件 active 同一裁决。",
            $"{Svc}/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Seed/WorldBibleSpec.cs",
            $"{Svc}/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Commands/IndustrialTelemetryCommands.cs"),
        ..Group("pending", "同值不同义：成本候选清单的列表状态（该聚合尚无持久化生命周期，pending 是唯一列表态），非审批链状态。",
            $"{Svc}/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Queries/SalesFinance/ErpSalesFinanceQueries.cs"),
        // ── "not-required" ──────────────────────────────────────────────────────────
        // 同值不同义：Nerv.IIP.Contracts.Quality.QualityFirstArticleConfirmationStatuses.NotRequired
        // 守护的是「某工单工序无需首件」这一首件确认进度（#2779）；下列文件中的 "not-required"
        // 是 Inventory 单位成本授权协议里「非 MES 完工入库来源、无需成本授权」的取值，两者不可互相引用。
        ..Group("not-required", "同值不同义：Inventory 单位成本授权状态，非首件确认进度。",
            $"{Svc}/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Valuation/InventoryUnitCostAuthority.cs"),

        ..Group("transfer", "同值不同义：检验任务转派动作（质量域内动作面），非审批链裁决动作。",
            $"{Svc}/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Commands/InspectionTasks/InspectionTaskAssignmentCommands.cs"),

        // ── WMS 资源类别（与库存移动类型同值不同义） ──────────────────────────────
        ..Group("inbound", "同值不同义：仓库作业资源类别 inbound，不是库存流水移动类型。",
            $"{Svc}/Wms/src/Nerv.IIP.Business.Wms.Web/Application/Commands/WarehouseAssignmentCommands.cs"),
        ..Group("outbound", "同值不同义：仓库作业资源类别 outbound，不是库存流水移动类型。",
            $"{Svc}/Wms/src/Nerv.IIP.Business.Wms.Web/Application/Commands/WarehouseAssignmentCommands.cs"),

        // ── "quality"（票面 (a) 类点名的多义值） ────────────────────────────────────
        // 库存质量状态族（quality/unrestricted/blocked/restricted/qualified）已于 #1370 ③ 批次 A 销账；
        // 审批链来源受理集合已于 #1857 收敛成 ApprovalSourceServices.QualityAliases（对应豁免已删除）；
        // 此处仅余 MES 区域码一类同名值。
        // 同值不同义：MES 工作台就绪「区域码」（quality/equipment/master-data/product-engineering/supply
        // 同一枚举面），非质量状态、非审批来源。
        ..Group("quality", "同值不同义：MES 就绪区域码，非质量状态/审批来源。",
            $"{Svc}/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Workbench/MesWorkbenchQueries.cs"),
        ..Group("product-engineering", "同值不同义：MES 就绪区域码，非审批来源服务。",
            $"{Svc}/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Workbench/MesWorkbenchQueries.cs"),

        // ── 检验处置/NCR 处置族（QualityInspectionDispositionStatuses / QualityNcrDispositionTypes） ──
        // #1370 ③ 批次 B 已销账：Mes/Quality 的 NCR 处置类型与 Quality 的检验处置结果全部改常量引用，
        // 对应豁免已删除。Erp 世界史种子的同名误用由 #1828 改为 ErpReceiptQualityStatuses 常量并删除豁免。

        // ── "rejected"（审批结果 vs 检验处置 vs 扫描结果 vs 控制命令状态） ──────────
        // 同值不同义：条码扫描结果（accepted/rejected）与设备控制命令状态（completed/failed/rejected）
        // 是各自域内状态机，非审批结果、非检验处置。
        ..Group("rejected", "同值不同义：条码扫描结果域（accepted/rejected），非审批/检验词表。",
            $"{Svc}/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/Scans/RecordScanCommand.cs",
            $"{Svc}/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Application/Seed/WorldHistoryConsistencyValidator.cs",
            $"{Svc}/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Application/Seed/WorldHistoryLabelSpec.cs"),
        ..Group("rejected", "同值不同义：设备控制命令状态/审批态（Ops 域内状态机），非审批链词表。",
            $"{Svc}/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/IntegrationEventHandlers/DeviceControlCommandOpsOutcomeHandlers.cs",
            $"{Svc}/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Seed/WorldHistoryControlCommandSpec.cs"),

        // ── 严重度类（NotificationContractConstants.Severity*） ─────────────────────
        // 同值不同义：critical/warning/info 在下列文件里分别是 IT 报警严重度、CAPA 严重度、
        // 维修优先级、排产紧急度、缺陷严重度、BOM 诊断级别、SPC 告警级别，
        // 以及 Notification 映射 switch 的**输入域**（右侧已正确引用常量）——都不是通知严重度本尊。
        ..Group("critical", "同值不同义：各域严重度/优先级词（IT 报警、CAPA、维修、排产、缺陷），非通知严重度。",
            $"{Svc}/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Queries/EquipmentHealthQueries.cs",
            $"{Svc}/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Queries/IndustrialTelemetryQueries.cs",
            $"{Svc}/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Seed/LeaderDemoSeedService.cs",
            $"{Svc}/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Seed/WorldHistoryControlCommandSpec.cs",
            $"{Svc}/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/Seed/LeaderDemoSeedService.cs",
            $"{Svc}/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/Seed/WorldHistorySeedService.cs",
            $"{Svc}/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Commands/CorrectiveActions/CapaAutomationService.cs",
            $"{Svc}/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Seed/QualitySeedService.cs",
            $"{Svc}/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Urgency/OrderUrgencyApplication.cs",
            "services/Notification/src/Nerv.IIP.Notification.Web/Application/IntegrationEventHandlers/IndustrialTelemetryAlarmIntegrationEventHandlersForNotification.cs"),
        ..Group("warning", "同值不同义：各域严重度/诊断级别词（IT 报警、BOM 诊断、影响评估、SPC 告警），非通知严重度。",
            $"{Svc}/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Queries/EquipmentHealthQueries.cs",
            $"{Svc}/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Queries/IndustrialTelemetryQueries.cs",
            $"{Svc}/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Queries/ProductEngineeringBomQueries.cs",
            $"{Svc}/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Queries/ProductEngineeringImpactQueries.cs",
            $"{Svc}/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Queries/Spc/SpcAnalysisQueries.cs",
            "services/Notification/src/Nerv.IIP.Notification.Web/Application/IntegrationEventHandlers/IndustrialTelemetryAlarmIntegrationEventHandlersForNotification.cs"),
        ..Group("info", "同值不同义：Notification 映射 switch 的输入域（IT 报警严重度），右侧已引用常量。",
            "services/Notification/src/Nerv.IIP.Notification.Web/Application/IntegrationEventHandlers/IndustrialTelemetryAlarmIntegrationEventHandlersForNotification.cs"),

        // ── 设备运行状态族（EquipmentRuntimeDeviceStates / EquipmentRuntimeReasonCodes） ──
        // #1370 ③ 批次 B 已销账：IT 世界史种子/校验器的 running/faulted/planned-down 与 Scheduling 规格的
        // equipment.maintenanceWindow 全部改常量引用，对应豁免已删除；以下仅保留同值不同义条目。
        // 同值不同义：MasterData 字典 device-status 码集（running/idle/maintenance/fault/scrapped）
        // 与 EquipmentRuntime 设备运行态（running/idle/faulted/planned-down…）是两套目录：
        // 前者含 fault/scrapped、后者是 faulted——码面已分叉，属两个权威，不可互相引用。
        ..Group("running", "同值不同义：MasterData device-status 字典码集，与 EquipmentRuntime 运行态是两套目录。",
            $"{Svc}/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Seed/MasterDataDictionaryRules.cs"),
        ..Group("idle", "同值不同义：MasterData device-status 字典码集，与 EquipmentRuntime 运行态是两套目录。",
            $"{Svc}/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Seed/MasterDataDictionaryRules.cs"),
        // 同值不同义：库存目录响应的可用性标志 / AppHub 连接器上报状态，均非设备运行态。
        ..Group("available", "同值不同义：库存目录响应可用性标志，非设备运行态。",
            $"{Svc}/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Queries/ListInventoryDirectoryQuery.cs"),
        ..Group("available", "同值不同义：库存移动载荷 QualityStatus 字段取值（available 不在设备运行态语境）。",
            $"{Svc}/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/IntegrationEventConverters/MaintenanceIntegrationEventConverters.cs"),
        ..Group("available", "同值不同义：ERP 工单人工差异的可计算状态，非设备运行态或 MES 成品收货成本权威状态。",
            $"{Svc}/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Queries/Finance/WorkOrderCostVarianceQueries.cs"),
        ..Group("stopped", "同值不同义：AppHub 连接器上报状态，非设备运行态。",
            "services/AppHub/src/Nerv.IIP.AppHub.Web/Application/Connectors/ConnectorCollectionHealthEvaluator.cs"),

        // ── "maintenance"（事件源 vs 站点/owner/字典/启发式） ───────────────────────
        // #1370 ③ 批次 D 逐处复核结论（InventoryMovementRequestedPayload 按位置参数逐一确认）：
        // 第 2 位 SourceService 是真违例 —— 已给 InventoryMovementSourceServices 补 Maintenance = "maintenance"
        // （纯加法；Inventory 消费端对 payload.SourceService 只透传、无白名单校验，无消费端联动）并改常量引用；
        // 余下两处永久豁免，各自裁决如下：
        //   · 第 9 位 SiteCode —— 维修备件出库的站点码，属 MasterData 站点码值域，非库存流水来源服务；
        //   · 第 13 位 OwnerType —— 库存归属方类型（自有/客供/供应商寄售同一枚举面），非库存流水来源服务。
        // 二者与 SourceService 只是恰好同值，各自独立演化，不可互相引用。
        // 注：白名单按（值 × 文件）二元组建索引，同值同文件只能有一条条目，
        // 故两处裁决逐条写在同一条目的裁决文本里（下方 Adjudication 已逐处列明），不能拆成两条同键条目。
        ..Group(
            "maintenance",
            "同值不同义（逐处）：SiteCode（第 9 位参数，维修备件站点码）与 OwnerType（第 13 位参数，库存归属方类型），"
            + "均非库存流水来源服务；同文件的 SourceService 真违例已于 #1370 ③ 批次 D 改引 InventoryMovementSourceServices.Maintenance。",
            $"{Svc}/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/IntegrationEventConverters/MaintenanceIntegrationEventConverters.cs"),
        ..Group("maintenance", "同值不同义：保养计划 owner 标识，非事件信封来源。",
            $"{Svc}/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/Seed/MaintenanceSeedService.cs"),
        ..Group("maintenance", "同值不同义：MasterData device-status 字典码（保养），非事件信封来源。",
            $"{Svc}/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Seed/MasterDataDictionaryRules.cs"),
        ..Group("maintenance", "同值不同义：就绪原因文本的子串启发式匹配，非事件信封来源。",
            $"{Svc}/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Readiness/MesReadinessReasonCodes.cs"),
        ..Group("maintenance", "同值不同义：日历原因码子串启发式，非事件信封来源。",
            $"{Svc}/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Scheduling/SchedulePlanCalendarProjector.cs"),
        ..Group("maintenance", "同值不同义：维护窗口原因码（排产夹具数据），非事件信封来源。",
            $"{Svc}/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Scheduling/ShockAbsorberSchedulingFixture.cs"),
        // #1370 ③ 批次 D 已销账：维修工单种子的 openedBy 改引
        // IndustrialTelemetryIntegrationEventSources.IndustrialTelemetry——运行路径
        // （OpenWorkOrderWhenAlarmRaisedHandler）本就把该常量直接作为 openedBy 传入建单命令，
        // 种子在模拟同一条路径，属同一匹配面；对应豁免已删除。

        // ── "inventory" ─────────────────────────────────────────────────────────────
        // 同值不同义：库存目录响应的 sourceKind 字段，非审批来源服务（票面 (a) 类点名的多义值）。
        ..Group("inventory", "同值不同义：库存目录响应 sourceKind，非审批来源服务。",
            $"{Svc}/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Queries/ListInventoryDirectoryQuery.cs"),

        // ── "purchase-order" / "purchase-receipt"（审批单据 vs Coding 文档 vs WMS 源单据） ──
        // 同值不同义：Coding 编码规则的文档类型键与计划输入的源单据类型，
        // 与 ApprovalDocumentTypes.PurchaseOrder（采购订单审批单据类型）不同义（票面 (a) 类 delivery-order 同形态）。
        // WMS 源单据类型匹配面已于 #1370 ③ 批次 A 销账（改引 WmsSourceDocumentTypes）。
        ..Group("purchase-order", "同值不同义：Coding 编码规则文档类型键，非审批单据类型。",
            $"{Svc}/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Commands/Procurement/ErpProcurementCommands.cs"),
        ..Group("purchase-order", "同值不同义：计划输入的供给源单据类型，非审批单据类型。",
            $"{Svc}/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Planning/PlanningInputAdapters.cs"),
        ..Group("purchase-receipt", "同值不同义：Coding 编码规则文档类型键，非 WMS 源单据类型。",
            $"{Svc}/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Commands/Procurement/ErpProcurementCommands.cs"),
        ..Group("purchase-receipt", "同值不同义：条码规则源单据类型（BarcodeRule.AllowedSourceDocumentTypes 自成一族，票面 (a) 类）。",
            $"{Svc}/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Application/Seed/WorldHistoryLabelSpec.cs"),

        // ── 检验来源族（QualityInspectionSourceTypes：wms / receiving） ─────────────
    ];

    private static IEnumerable<VocabularyExemption> Group(
        string value,
        string adjudication,
        params string[] relativePaths) =>
        relativePaths.Select(path => new VocabularyExemption(value, path, adjudication));
}
