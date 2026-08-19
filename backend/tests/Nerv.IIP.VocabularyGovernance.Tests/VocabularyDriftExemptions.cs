namespace Nerv.IIP.VocabularyGovernance.Tests;

/// <summary>
/// 词表漂移门禁白名单（#1703）。豁免按（值 × 文件）二元组生效：同文件出现其他被守护值、
/// 或其他文件出现同值，仍然红；条目未命中任何字面量时 stale 检查红（白名单只出不进）。
///
/// 每组豁免必须附中文裁决注释，且只允许两类：
/// <list type="bullet">
/// <item>「同值不同义」——字面量与词表常量恰好同值但语义不同，不可互相引用（永久豁免，票面 (a) 类）；</item>
/// <item>「待 #1370 ③ 销账」——与词表常量同族的真实违例，本票（#1703）只落地门禁不修存量，
/// 修复由 #1370 ③ 分批销账；每销一处必须同步删除对应豁免。</item>
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
            $"{Svc}/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Commands/MasterData/CreateMasterDataCommands.cs",
            $"{Svc}/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Commands/MasterData/MasterDataLifecycleCommands.cs",
            $"{Svc}/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/IntegrationEventConverters/MasterDataIntegrationEventConverters.cs",
            $"{Svc}/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Queries/GetPrincipalWorkContextQuery.cs",
            $"{Svc}/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Queries/ListMasterDataResourcesQuery.cs",
            $"{Svc}/Quality/src/Nerv.IIP.Business.Quality.Web/Application/IntegrationEventHandlers/InspectionTaskTriggerIntegrationEventHandlers.cs",
            $"{Svc}/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Queries/Spc/SpcAnalysisQueries.cs",
            $"{Svc}/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Seed/LeaderDemoSeedService.cs",
            "services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs"),
        // 待 #1370 ③ 销账：以下 Mes 文件比较/断言的是 PE 生产版本状态（消费 PE 契约响应），
        // 与 ProductionEngineeringContractStatuses.Active 同族，应改为常量引用。
        ..Group("active", "待 #1370 ③ 销账：PE 生产版本状态匹配面，应引用 ProductionEngineeringContractStatuses.Active。",
            $"{Svc}/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesMaterialRequirementSnapshotProvider.cs",
            $"{Svc}/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesRoutingSnapshotProvider.cs",
            $"{Svc}/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Seed/LeaderDemoScaleSeedService.cs",
            $"{Svc}/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Seed/LeaderDemoSeedService.cs",
            $"{Svc}/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Seed/WorldHistoryProductionVersionResolver.cs"),

        // ── "archived" ──────────────────────────────────────────────────────────────
        // 同值不同义：Scheduling 保留策略的 Prometheus 指标标签值，非 PE 契约状态。
        ..Group("archived", "同值不同义：指标标签值（快照归档计数），非 PE 生产版本契约状态。",
            $"{Svc}/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Urgency/OrderUrgencyRetentionWorker.cs"),

        // ── DemandPlanning 来源引用族（PascalCase） ─────────────────────────────────
        // 同值不同义：Coding 编码分配请求方标识（CodeAllocationRequest 最后一个参数），
        // 非 PlanningSuggestionAccepted 事件里的来源引用。
        ..Group("DemandPlanning", "同值不同义：编码分配请求方标识，非 DP 建议来源引用。",
            $"{Svc}/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Commands/DemandPlanningCodingService.cs"),
        // 待 #1370 ③ 销账：Mes 计划转工单的 SourceSystem/SourceDocumentType 兜底值正是
        // DemandPlanningSourceReferences 词表所守护的跨服务引用，应改常量引用。
        ..Group("DemandPlanning", "待 #1370 ③ 销账：DP 来源引用族兜底值，应引用 DemandPlanningSourceReferences.DemandPlanning。",
            $"{Svc}/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs"),
        ..Group("PlanningSuggestion", "待 #1370 ③ 销账：DP 来源引用族兜底值，应引用 DemandPlanningSourceReferences.PlanningSuggestion。",
            $"{Svc}/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs"),
        // 同值不同义：MES 追溯图节点类型（与 OperationTask/ProductionReport/Material 同一枚举面），
        // 非 DP 下游单据引用；MES 就绪分类的来源标签 "BusinessMes" 同理。
        ..Group("WorkOrder", "同值不同义：MES 追溯图节点类型，非 DP 下游单据引用。",
            $"{Svc}/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Workbench/MesWorkbenchQueries.cs"),
        ..Group("BusinessMes", "同值不同义：MES 就绪分类来源标签，非 DP 下游服务引用。",
            $"{Svc}/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Readiness/MesReadinessReasonCodes.cs"),

        // ── 集成事件信封来源族 ──────────────────────────────────────────────────────
        // 待 #1370 ③ 销账：AppHub 事件转换器写的正是事件信封 SourceService，
        // 应引用 AppHubIntegrationEventSources.AppHub。
        ..Group("apphub", "待 #1370 ③ 销账：事件信封来源，应引用 AppHubIntegrationEventSources.AppHub。",
            "services/AppHub/src/Nerv.IIP.AppHub.Web/Application/IntegrationEventConverters/ApplicationInstanceStatusChangedIntegrationEventConverter.cs",
            "services/AppHub/src/Nerv.IIP.AppHub.Web/Application/IntegrationEventConverters/ApplicationRegisteredIntegrationEventConverter.cs"),
        // 待 #1370 ③ 销账：DP 种子把「已接受建议的下游服务」写成 "business-mes"，
        // 而契约词表 DemandPlanningDownstreamReferences.BusinessMes 取值是 "BusinessMes"——
        // 两种大小写并存正是 #1683 形态的分叉，销账时须裁决统一口径。
        ..Group("business-mes", "待 #1370 ③ 销账：DP 下游服务引用与契约词表大小写分叉（business-mes vs BusinessMes），须裁决统一。",
            $"{Svc}/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Seed/WorldHistorySeedService.cs"),
        // 同值不同义：分布式锁 key 的命名空间片段。锁前缀是既有锁的身份，与事件信封来源的演化无关，
        // 不可互相引用（改锁前缀会使在途锁失效）。
        ..Group("business-inventory", "同值不同义：分布式锁 key 命名空间片段，非事件信封来源。",
            $"{Svc}/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Commands/StockCounts/CreateStockCountTaskCommand.cs"),
        ..Group("business-product-engineering", "同值不同义：分布式锁 key 命名空间片段，非事件信封来源。",
            $"{Svc}/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/ProductEngineeringReleaseCommands.cs"),
        // 待 #1370 ③ 销账：Quality 审批链来源别名清单（QualityFacts.ServiceName / business-quality / quality
        // 三种历史拼写并存）就是审批 sourceService 匹配面，应收敛到 ApprovalSourceServices.Quality 并裁决别名去留。
        ..Group("business-quality", "待 #1370 ③ 销账：审批链来源历史别名，应收敛到 ApprovalSourceServices 词表。",
            $"{Svc}/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Approvals/ApprovalChainStatusClient.cs"),

        // ── 审批链状态匹配面 ────────────────────────────────────────────────────────
        // 待 #1370 ③ 销账：这里比较的是审批链 Status（approved），审批链状态词表尚未入
        // Nerv.IIP.Contracts.Approval（ApprovalResults.Approved 是步骤结果，恰好同值）；
        // 销账时应把链状态入契约词表后改引用，而不是借用 ApprovalResults。
        ..Group("approved", "待 #1370 ③ 销账：审批链状态匹配面，链状态词表应入契约后改引用。",
            $"{Svc}/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/ProductEngineeringReleaseCommands.cs",
            $"{Svc}/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Approvals/ApprovalChainStatusClient.cs"),
        // 同值不同义：设备控制命令的审批态 / MRB 评审决定，都是各自域内状态机，非审批链词表。
        ..Group("approved", "同值不同义：设备控制命令审批态（DeviceControlCommand），非审批链结果词表。",
            $"{Svc}/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Seed/WorldHistoryControlCommandSpec.cs"),
        ..Group("approved", "同值不同义：MRB 评审决定（质量域内状态），非审批链结果词表。",
            $"{Svc}/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Seed/WorldHistoryConsistencyValidator.cs"),

        // ── "quality"（票面 (a) 类点名的多义值） ────────────────────────────────────
        // 库存质量状态族（quality/unrestricted/blocked/restricted/qualified）已于 #1370 ③ 批次 A 销账；
        // 此处仅余审批链来源与 MES 区域码两类同名值。
        ..Group("quality", "待 #1370 ③ 销账：审批链来源历史别名清单，应引用 ApprovalSourceServices.Quality。",
            $"{Svc}/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Approvals/ApprovalChainStatusClient.cs"),
        // 同值不同义：MES 工作台就绪「区域码」（quality/equipment/master-data/product-engineering/supply
        // 同一枚举面），非质量状态、非审批来源。
        ..Group("quality", "同值不同义：MES 就绪区域码，非质量状态/审批来源。",
            $"{Svc}/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Workbench/MesWorkbenchQueries.cs"),
        ..Group("product-engineering", "同值不同义：MES 就绪区域码，非审批来源服务。",
            $"{Svc}/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Workbench/MesWorkbenchQueries.cs"),

        // ── 检验处置/NCR 处置族（QualityInspectionDispositionStatuses / QualityNcrDispositionTypes） ──
        // #1370 ③ 批次 B 已销账：Mes/Quality 的 NCR 处置类型与 Quality 的检验处置结果全部改常量引用，
        // 对应豁免已删除。下面这条 Erp 的 "passed" 经复核属同值不同义，保留。
        // 同值不同义：Erp 采购收货行的 QualityStatus 走的是 ErpReceiptQualityStatuses 值域
        // （unrestricted/quality/blocked + accepted/qualified/available/inspection/rejected 别名），
        // 与 Quality 的检验处置结果（passed/conditional-release/rejected）是两套目录，不可互相引用。
        ..Group("passed", "同值不同义：Erp 采购收货行质检状态（ErpReceiptQualityStatuses 值域），非 Quality 检验处置结果。",
            $"{Svc}/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Seed/WorldHistorySeedService.cs"),

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
        ..Group("stopped", "同值不同义：AppHub 连接器上报状态，非设备运行态。",
            "services/AppHub/src/Nerv.IIP.AppHub.Web/Application/Connectors/ConnectorCollectionHealthEvaluator.cs"),

        // ── "maintenance"（事件源 vs 站点/owner/字典/启发式） ───────────────────────
        // 待 #1370 ③ 销账：本文件 3 处 "maintenance" 语义混合——SourceService（库存流水来源服务，
        // 真违例：该族词表 InventoryMovementSourceServices 目前只有 quality，需补值后改引用）、
        // SiteCode 与 OwnerType（同值不同义）；按文件粒度整体登记，销账时逐处复核。
        ..Group("maintenance", "待 #1370 ③ 销账：含库存流水 SourceService 真违例（另两处为 Site/Owner 同值不同义），销账时逐处复核。",
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
        // 待 #1370 ③ 销账：维修工单 openedBy 写的是 IT 服务标识，与
        // IndustrialTelemetryIntegrationEventSources.IndustrialTelemetry 同值；是否属同一匹配面待销账时裁决。
        ..Group("industrialTelemetry", "待 #1370 ③ 销账：维修工单 openedBy 服务标识，与 IT 事件源同值，归属待裁决。",
            $"{Svc}/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/Seed/WorldHistorySeedService.cs"),

        // ── "inventory" ─────────────────────────────────────────────────────────────
        // 同值不同义：库存目录响应的 sourceKind 字段，非审批来源服务（票面 (a) 类点名的多义值）。
        ..Group("inventory", "同值不同义：库存目录响应 sourceKind，非审批来源服务。",
            $"{Svc}/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Queries/ListInventoryDirectoryQuery.cs"),

        // ── DP 建议类型族（DemandPlanningSuggestionTypes） ──────────────────────────
        // 待 #1370 ③ 销账：MRP 计算器与规划种子规格写的正是建议类型本尊，应改常量引用。
        ..Group("planned-purchase", "待 #1370 ③ 销账：DP 建议类型族，应引用 DemandPlanningSuggestionTypes.PlannedPurchase。",
            $"{Svc}/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Planning/MrpCalculator.cs",
            $"{Svc}/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Seed/WorldHistoryPlanningSpec.cs"),
        ..Group("planned-work-order", "待 #1370 ③ 销账：DP 建议类型族，应引用 DemandPlanningSuggestionTypes.PlannedWorkOrder。",
            $"{Svc}/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Planning/MrpCalculator.cs",
            $"{Svc}/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Seed/WorldHistoryPlanningSpec.cs"),

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
