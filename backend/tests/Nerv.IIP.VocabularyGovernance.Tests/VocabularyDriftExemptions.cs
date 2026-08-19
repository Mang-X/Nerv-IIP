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

        // ── 库存移动类型族（InventoryMovementTypes） ────────────────────────────────
        // 待 #1370 ③ 销账：三处写的都是库存移动/状态转移类型本尊（事件载荷、允许类型清单、种子规格），
        // 与 InventoryMovementTypes 同族，应改常量引用。
        ..Group("adjustment", "待 #1370 ③ 销账：库存移动类型族，应引用 InventoryMovementTypes.Adjustment。",
            $"{Svc}/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Application/IntegrationEventConverters/BarcodeLabelIntegrationEventConverters.cs",
            $"{Svc}/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Commands/StockMovements/PostStockMovementCommand.cs",
            $"{Svc}/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Seed/WorldHistoryInventorySpec.cs"),
        ..Group("status-transfer-in", "待 #1370 ③ 销账：库存移动类型族，应引用 InventoryMovementTypes.StatusTransferIn。",
            $"{Svc}/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Commands/StockStatusTransfers/PostStockStatusTransferCommand.cs",
            $"{Svc}/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Seed/WorldHistoryInventorySpec.cs"),
        ..Group("status-transfer-out", "待 #1370 ③ 销账：库存移动类型族，应引用 InventoryMovementTypes.StatusTransferOut。",
            $"{Svc}/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Commands/StockStatusTransfers/PostStockStatusTransferCommand.cs",
            $"{Svc}/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Seed/WorldHistoryInventorySpec.cs"),

        // ── 集成事件信封来源族 ──────────────────────────────────────────────────────
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

        // ── 库存质量状态族（InventoryQualityStatuses 等四表同值） ───────────────────
        // 待 #1370 ③ 销账：以下写/比的都是库存质量状态本尊（quality/unrestricted/blocked/restricted），
        // 跨 Inventory / Wms / Erp / BarcodeLabel 多处落库与匹配，应改常量引用；
        // 销账时还须裁决四张同值词表（Inventory/Wms/Erp/Quality）各自的适用面。
        ..Group("blocked", "待 #1370 ③ 销账：库存质量状态族，应引用 InventoryQualityStatuses.Blocked。",
            $"{Svc}/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Expiry/ExpiredStockBlockingService.cs",
            $"{Svc}/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Seed/WorldHistoryInventorySpec.cs"),
        ..Group("restricted", "待 #1370 ③ 销账：库存质量状态族，应引用 InventoryQualityStatuses.Restricted。",
            $"{Svc}/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Seed/WorldHistoryInventorySpec.cs"),
        ..Group("unrestricted", "待 #1370 ③ 销账：库存质量状态族，应引用 InventoryQualityStatuses.Unrestricted（或对应域词表）。",
            $"{Svc}/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Application/Seed/WorldHistoryLabelSpec.cs",
            $"{Svc}/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Seed/WorldHistoryInventorySpec.cs",
            $"{Svc}/Wms/src/Nerv.IIP.Business.Wms.Web/Application/Commands/MesMaterialIssueCommands.cs",
            $"{Svc}/Wms/src/Nerv.IIP.Business.Wms.Web/Application/IntegrationEventHandlers/WmsOutboundOrderRequestedIntegrationEventHandler.cs",
            $"{Svc}/Wms/src/Nerv.IIP.Business.Wms.Web/Application/Seed/WorldHistoryWmsSpec.cs"),
        ..Group("qualified", "待 #1370 ③ 销账：WMS 收货质量状态族，应引用 WmsReceivingQualityStatuses.Qualified。",
            $"{Svc}/Wms/src/Nerv.IIP.Business.Wms.Web/Application/Commands/WmsCommands.cs"),

        // ── "quality"（票面 (a) 类点名的多义值） ────────────────────────────────────
        // 待 #1370 ③ 销账：以下是库存质量状态（待检）或库存流水来源服务本尊，
        // 应分别引用 InventoryQualityStatuses.Quality / InventoryMovementSourceServices.Quality；
        // ApprovalChainStatusClient 的 "quality" 是审批来源，应引用 ApprovalSourceServices.Quality。
        ..Group("quality", "待 #1370 ③ 销账：库存质量状态/流水来源服务族，应引用对应契约常量。",
            $"{Svc}/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Application/Seed/WorldHistoryLabelSpec.cs",
            $"{Svc}/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/IntegrationEventHandlers/QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer.cs",
            $"{Svc}/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Seed/WorldHistoryInventorySpec.cs",
            $"{Svc}/Wms/src/Nerv.IIP.Business.Wms.Web/Application/IntegrationEventHandlers/ErpSalesReturnAuthorizedIntegrationEventHandler.cs",
            $"{Svc}/Wms/src/Nerv.IIP.Business.Wms.Web/Application/IntegrationEventHandlers/QualityInspectionResultIntegrationEventHandlerForReleaseWmsInboundGate.cs",
            $"{Svc}/Wms/src/Nerv.IIP.Business.Wms.Web/Application/Seed/WorldHistoryWmsSpec.cs"),
        ..Group("quality", "待 #1370 ③ 销账：审批链来源历史别名清单，应引用 ApprovalSourceServices.Quality。",
            $"{Svc}/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Approvals/ApprovalChainStatusClient.cs"),
        // 同值不同义：MES 工作台就绪「区域码」（quality/equipment/master-data/product-engineering/supply
        // 同一枚举面），非质量状态、非审批来源。
        ..Group("quality", "同值不同义：MES 就绪区域码，非质量状态/审批来源。",
            $"{Svc}/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Workbench/MesWorkbenchQueries.cs"),
        ..Group("product-engineering", "同值不同义：MES 就绪区域码，非审批来源服务。",
            $"{Svc}/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Workbench/MesWorkbenchQueries.cs"),

        // ── 检验处置/NCR 处置族（QualityInspectionDispositionStatuses / QualityNcrDispositionTypes） ──
        // 待 #1370 ③ 销账：种子与校验器写/比的都是 NCR 处置类型与检验处置结果本尊，应改常量引用。
        ..Group("conditional-release", "待 #1370 ③ 销账：NCR/检验处置族，应引用 QualityNcrDispositionTypes.ConditionalRelease。",
            $"{Svc}/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Seed/WorldHistoryFloorEventsSpec.cs",
            $"{Svc}/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Seed/QualitySeedService.cs",
            $"{Svc}/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Seed/WorldHistoryConsistencyValidator.cs"),
        ..Group("rework", "待 #1370 ③ 销账：NCR 处置族，应引用 QualityNcrDispositionTypes.Rework。",
            $"{Svc}/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Seed/WorldHistoryFloorEventsSpec.cs",
            $"{Svc}/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Seed/QualitySeedService.cs",
            $"{Svc}/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Seed/WorldHistoryConsistencyValidator.cs"),
        ..Group("scrap", "待 #1370 ③ 销账：NCR 处置族，应引用 QualityNcrDispositionTypes.Scrap。",
            $"{Svc}/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Seed/WorldHistoryFloorEventsSpec.cs",
            $"{Svc}/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Seed/QualitySeedService.cs",
            $"{Svc}/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Seed/WorldHistoryConsistencyValidator.cs"),
        ..Group("sort-and-screen", "待 #1370 ③ 销账：NCR 处置族，应引用 QualityNcrDispositionTypes.SortAndScreen。",
            $"{Svc}/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Seed/WorldHistoryFloorEventsSpec.cs"),
        ..Group("return-to-supplier", "待 #1370 ③ 销账：NCR 处置族，应引用 QualityNcrDispositionTypes.ReturnToSupplier。",
            $"{Svc}/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Seed/QualitySeedService.cs"),
        ..Group("passed", "待 #1370 ③ 销账：检验处置结果族，应引用 QualityInspectionDispositionStatuses.Passed。",
            $"{Svc}/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Seed/WorldHistorySeedService.cs",
            $"{Svc}/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Commands/CorrectiveActions/CorrectiveActionCommands.cs",
            $"{Svc}/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Seed/WorldHistoryMetrologySeedService.cs"),

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
        // 待 #1370 ③ 销账：IT 世界史种子/校验器写的正是设备运行状态时间线（running/faulted/planned-down），
        // Scheduling 规格写的正是 equipment.maintenanceWindow 原因码——与 EquipmentRuntime 契约同族。
        ..Group("faulted", "待 #1370 ③ 销账：设备运行状态族，应引用 EquipmentRuntimeDeviceStates.Faulted。",
            $"{Svc}/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Seed/WorldHistoryConsistencyValidator.cs",
            $"{Svc}/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Seed/WorldHistorySeedService.cs"),
        ..Group("running", "待 #1370 ③ 销账：设备运行状态族，应引用 EquipmentRuntimeDeviceStates.Running。",
            $"{Svc}/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Seed/WorldHistorySeedService.cs"),
        ..Group("planned-down", "待 #1370 ③ 销账：设备运行状态族，应引用 EquipmentRuntimeDeviceStates.PlannedDown。",
            $"{Svc}/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Seed/WorldHistorySeedService.cs"),
        ..Group("equipment.maintenanceWindow", "待 #1370 ③ 销账：设备原因码族，应引用 EquipmentRuntimeReasonCodes.MaintenanceWindow。",
            $"{Svc}/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Seed/WorldHistorySchedulingSpec.cs"),
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

        // ── "purchase-order" / "purchase-receipt"（审批单据 vs Coding 文档 vs WMS 源单据） ──
        // 同值不同义：Coding 编码规则的文档类型键与计划输入的源单据类型，
        // 与 ApprovalDocumentTypes.PurchaseOrder（采购订单审批单据类型）不同义（票面 (a) 类 delivery-order 同形态）。
        ..Group("purchase-order", "同值不同义：Coding 编码规则文档类型键，非审批单据类型。",
            $"{Svc}/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Commands/Procurement/ErpProcurementCommands.cs"),
        ..Group("purchase-order", "同值不同义：计划输入的供给源单据类型，非审批单据类型。",
            $"{Svc}/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Planning/PlanningInputAdapters.cs"),
        // 待 #1370 ③ 销账：WMS 入库/取消链路上的 sourceDocumentType 历史别名匹配面
        // （purchase-order / erp-purchase-order 并存），应收敛到 WmsSourceDocumentTypes 并裁决别名去留。
        ..Group("purchase-order", "待 #1370 ③ 销账：WMS 源单据类型历史别名匹配面，应收敛到 WmsSourceDocumentTypes。",
            $"{Svc}/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventHandlers/WmsInboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReceipt.cs",
            $"{Svc}/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Wms/WmsInboundCancellationClient.cs"),
        ..Group("purchase-receipt", "同值不同义：Coding 编码规则文档类型键，非 WMS 源单据类型。",
            $"{Svc}/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Commands/Procurement/ErpProcurementCommands.cs"),
        ..Group("purchase-receipt", "同值不同义：条码规则源单据类型（BarcodeRule.AllowedSourceDocumentTypes 自成一族，票面 (a) 类）。",
            $"{Svc}/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Application/Seed/WorldHistoryLabelSpec.cs"),
        ..Group("purchase-receipt", "待 #1370 ③ 销账：跨服务源单据类型匹配面，应引用 WmsSourceDocumentTypes.PurchaseReceipt。",
            $"{Svc}/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventHandlers/WmsInboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReceipt.cs",
            $"{Svc}/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Seed/WorldHistorySeedService.cs",
            $"{Svc}/Quality/src/Nerv.IIP.Business.Quality.Web/Application/InspectionRecords/InspectionExternalFactClients.cs",
            $"{Svc}/Quality/src/Nerv.IIP.Business.Quality.Web/Application/IntegrationEventHandlers/InspectionTaskTriggerIntegrationEventHandlers.cs",
            $"{Svc}/Wms/src/Nerv.IIP.Business.Wms.Web/Application/Seed/WorldHistoryWmsSpec.cs"),

        // ── 检验来源族（QualityInspectionSourceTypes：wms / receiving） ─────────────
        // 待 #1370 ③ 销账：检验触发/核验链路两侧（Quality 与 Wms）的 sourceType/sourceService
        // 匹配面正是该词表的守护对象，应改常量引用。
        ..Group("receiving", "待 #1370 ③ 销账：检验来源类型匹配面，应引用 QualityInspectionSourceTypes.Receiving。",
            $"{Svc}/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Commands/InspectionRecords/CreateInspectionRecordCommand.cs",
            $"{Svc}/Quality/src/Nerv.IIP.Business.Quality.Web/Application/InspectionRecords/InspectionExternalFactClients.cs",
            $"{Svc}/Quality/src/Nerv.IIP.Business.Quality.Web/Application/IntegrationEventHandlers/InspectionTaskTriggerIntegrationEventHandlers.cs",
            $"{Svc}/Wms/src/Nerv.IIP.Business.Wms.Web/Application/IntegrationEventHandlers/QualityInspectionResultIntegrationEventHandlerForReleaseWmsInboundGate.cs"),
        ..Group("wms", "待 #1370 ③ 销账：检验来源/库存流水来源服务匹配面，应引用 QualityInspectionSourceTypes.Wms 等契约常量。",
            $"{Svc}/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Expiry/StockReservationExpirationOptions.cs",
            $"{Svc}/Quality/src/Nerv.IIP.Business.Quality.Web/Application/InspectionRecords/InspectionExternalFactClients.cs",
            $"{Svc}/Quality/src/Nerv.IIP.Business.Quality.Web/Application/IntegrationEventHandlers/InspectionTaskTriggerIntegrationEventHandlers.cs",
            $"{Svc}/Wms/src/Nerv.IIP.Business.Wms.Web/Application/Commands/WmsCommands.cs",
            $"{Svc}/Wms/src/Nerv.IIP.Business.Wms.Web/Application/IntegrationEventConverters/WmsIntegrationEventConverters.cs",
            $"{Svc}/Wms/src/Nerv.IIP.Business.Wms.Web/Application/IntegrationEventHandlers/InventoryReservationExpiredIntegrationEventHandlerForCancelWmsPicking.cs",
            $"{Svc}/Wms/src/Nerv.IIP.Business.Wms.Web/Application/IntegrationEventHandlers/QualityInspectionResultIntegrationEventHandlerForReleaseWmsInboundGate.cs",
            $"{Svc}/Wms/src/Nerv.IIP.Business.Wms.Web/Application/IntegrationEventHandlers/StockMovementPostedIntegrationEventHandlerForMarkWmsRequestPosted.cs",
            $"{Svc}/Wms/src/Nerv.IIP.Business.Wms.Web/Application/IntegrationEventHandlers/StockMovementPostingFailedIntegrationEventHandlerForMarkWmsRequestFailed.cs"),

        // ── 事件类型族（QualityIntegrationEventTypes） ──────────────────────────────
        // 待 #1370 ③ 销账：Wms 种子的检验通过事件类型常量重抄，应引用 QualityIntegrationEventTypes.InspectionPassed。
        ..Group("quality.InspectionPassed", "待 #1370 ③ 销账：事件类型重抄，应引用 QualityIntegrationEventTypes.InspectionPassed。",
            $"{Svc}/Wms/src/Nerv.IIP.Business.Wms.Web/Application/Seed/WorldHistorySeedService.cs"),
    ];

    private static IEnumerable<VocabularyExemption> Group(
        string value,
        string adjudication,
        params string[] relativePaths) =>
        relativePaths.Select(path => new VocabularyExemption(value, path, adjudication));
}
