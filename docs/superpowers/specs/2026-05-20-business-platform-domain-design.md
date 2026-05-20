# Business Platform Critical Chain Design

## Context

GitHub issues #72 到 #77 提供了业务平台第一版输入，覆盖共享基础域、通用能力域、MES、WMS、ERP 和全链路验收。审阅后确认：MES/WMS/ERP 是主干，但不是完整关键链路。工业业务还需要解释工程版本来源、计划数量来源、设备数据来源、维修与产能影响，以及 CRM/SRM/CPQ/OMS/WCS/SCADA/PLC/DCS 这些系统应如何进入或不进入首批范围。

本 spec 是重新完善后的业务平台规划，不是对旧计划打补丁。它按关键链路组织首批业务域：

1. ProductEngineering：PDM/PLM lite。
2. DemandPlanning：MPS/MRP lite。
3. BusinessMasterData：基础主数据。
4. Inventory、Quality、BarcodeLabel、BusinessApproval：通用业务能力。
5. ERP：Procurement/SRM-lite、Sales/CRM-lite/OMS-lite、Finance MVP。
6. WMS：仓储执行和 WCS adapter 边界。
7. MES：制造执行和规则排产。
8. IndustrialTelemetry：IIoT/Telemetry lite，接入 PLC/DCS/SCADA 数据。
9. Maintenance：CMMS lite。

## Goals

1. 将 MES/WMS/ERP 规划升级为覆盖工程、计划、执行、库存、设备、维护和财务的关键链路规划。
2. 明确 SRM、CRM、CPQ、OMS、WCS、CAD、SCADA、PLC/DCS 的首批处理方式，避免全量建设。
3. 给出关键业务域的需求条目、实体视图、触发动作、聚合、命令、查询、事件和 API 端点清单。
4. 定义可拆 implementation plan 的业务切片顺序。
5. 保持主平台与业务平台边界清晰，业务平台只通过公开能力消费 IAM、File Storage、AppHub、Ops、Notification 和 Connector Host。

## Non-Goals

1. 不在本阶段实现代码、迁移、OpenAPI 快照或前端页面。
2. 不实现 CAD、SCADA、PLC/DCS、WCS、AGV/AMR 这类外部系统本体。
3. 不做完整 PLM 套件，只做产品工程版本、BOM、工艺路线和工程变更 MVP。
4. 不做完整 APS 优化求解器，首批只做 MPS/MRP 与 MES 规则派工。
5. 不做完整 CRM、SRM、CPQ、OMS；首批只把必要能力压缩进 ERP Sales/Procurement/WMS fulfillment。
6. 不做完整 EAM，首批只做 CMMS lite。
7. 不改变 IAM、AppHub、Ops、File Storage、Notification 的主平台事实源职责。

## System Scope

| 系统名 | 首批策略 | 进入原因或后置原因 |
| --- | --- | --- |
| PDM/PLM | ProductEngineering lite | EBOM、MBOM、工艺路线和工程变更是 MRP/MES 的前置事实。 |
| CAD | 外部文件/集成来源 | 通过 File Storage 和 ProductEngineering 管理引用，不实现设计能力。 |
| MPS/MRP | DemandPlanning lite | 解释为什么采购、为什么生产、数量如何计算。 |
| APS | 后置 | 首批规则排产即可，复杂约束优化后续独立。 |
| SRM | ERP Procurement 子域 | 首批覆盖供应商、询价、报价和采购协同最小流程。 |
| CRM | ERP Sales 子域 | 首批覆盖客户、商机、报价和销售订单。 |
| CPQ | 预留边界 | 配置型产品或复杂报价成立时再独立。 |
| OMS | ERP Sales + WMS fulfillment | 首批覆盖销售订单履约；多渠道拆单路由后置。 |
| WMS | 独立域 | 仓储执行是库存变更前的必要过程。 |
| WCS | WMS adapter 边界 | 自动化仓成立时再独立；首批只定义任务/回执。 |
| MES | 独立域 | 制造工单、工序、报工和完工入库是核心执行链路。 |
| IIoT | IndustrialTelemetry lite | 设备状态、报警、OEE 和停机需要工业数据事实。 |
| SCADA/PLC/DCS | 外部系统/Connector 来源 | 通过 Connector Host 接入，不由业务平台控制。 |
| CMMS | Maintenance lite | 维修、保养、点检和停机会影响产能与排产。 |
| EAM | 后置 | 资产财务、折旧、全生命周期后续再扩。 |

## Stakeholders

| 角色 | 目标/痛点 | 权限/限制 | 备注 |
| --- | --- | --- | --- |
| 平台管理员 | 管理用户、权限、环境和业务应用接入 | 通过 IAM 授权；不直接操作业务库存或工程版本 | 主平台 console 用户。 |
| 产品工程师 | 管理 CAD 引用、工程物料、EBOM、MBOM、工艺路线和工程变更 | 只能发布授权范围内的工程版本 | ProductEngineering 用户。 |
| 计划员 | 维护需求、运行 MPS/MRP、确认计划建议 | 不能直接创建正式采购订单或工单 | DemandPlanning 用户。 |
| 业务管理员 | 配置主数据、审批模板、条码规则和基础策略 | 仅在授权组织/环境内操作 | 业务平台管理入口。 |
| 采购员 | 处理采购申请、询价、采购订单、收货和退货 | 不能直接改库存余额或应付台账 | ERP Procurement 用户。 |
| 供应商协同用户 | 响应询价、确认交期和发货信息 | external-client 或受控用户；范围受限 | SRM-lite。 |
| 销售员 | 管理客户、商机、报价、销售订单和发货请求 | 不能直接改库存余额或应收台账 | CRM-lite/ERP Sales。 |
| 仓储作业员 | 执行收货、上架、拣货、复核、盘点 | 只能处理分派或授权范围内的作业 | WMS/PDA。 |
| 生产计划员 | 创建工单、释放工序、触发规则排产 | 不直接改库存余额 | MES。 |
| 车间操作员 | 报工、提交不良、记录完工和停机 | 只能提交所属工序/工单范围数据 | MES。 |
| 质检员 | 执行收货检验、工序检验和不合格处置 | 不直接改采购、销售、库存财务事实 | Quality。 |
| 设备运维人员 | 处理报警、维修、保养和点检 | 不拥有设备主数据，不控制 PLC/DCS | Maintenance。 |
| 财务人员 | 查看应收、应付、凭证和成本核算结果 | MVP 内不做完整总账月结 | ERP Finance。 |
| Connector Host | 上报工业数据、设备状态或执行受控采集 | 机器身份；不获得业务管理权限 | 接入 SCADA/PLC/DCS/WCS。 |
| 外部业务客户端 | 对接采购、销售、计划或仓储 API | 受 IAM external-client 授权约束 | 不直接访问业务服务内部表。 |

## Requirements

| 需求ID | 场景描述 | 干系人/对象 | 所属业务实体 | 操作类型 | 约束/前置 | 备注 |
| --- | --- | --- | --- | --- | --- | --- |
| BP-MD-001 | 维护 SKU、单位、分类、默认条码和基础属性 | 业务管理员 | SKU | 创建/修改/查看 | SKU 编码在组织+环境内唯一 | MasterData。 |
| BP-MD-002 | 维护客户、供应商、承运商 | 采购员、销售员 | 业务伙伴 | 创建/修改/查看 | 编码唯一；结算信息受权限保护 | CRM/SRM-lite 前置。 |
| BP-MD-003 | 维护工作中心、工作日历、班次和资源费率 | 计划员、生产计划员 | 工作中心/日历 | 创建/修改/查看 | 工作中心编码唯一 | MRP/MES/成本依赖。 |
| BP-MD-004 | 维护设备资产和工作中心归属 | 业务管理员、运维人员 | 设备资产 | 创建/修改/查看 | 不保存 PLC/DCS 密钥；实时状态来自 Telemetry | CMMS/IIoT 前置。 |
| BP-MD-005 | 维护业务部门、班组、人员技能和资质 | 业务管理员、生产计划员 | Department/Team/PersonnelSkill | 创建/修改/查看 | 引用 IAM userId；不复制 IAM 用户事实 | 排班、派工、审批和技能校验前置。 |
| BP-ENG-001 | 上传或引用 CAD/图纸/设计包 | 产品工程师 | EngineeringDocument | 创建/查看 | 文件通过 File Storage；不保存对象 key | CAD 是外部来源。 |
| BP-ENG-002 | 建立工程物料和 EBOM 版本 | 产品工程师 | EngineeringItem/EBOM | 创建/修改/查看 | 子件 SKU 或工程物料必须存在 | PDM-lite。 |
| BP-ENG-003 | 建立 MBOM 和工艺路线版本 | 产品工程师、生产计划员 | MBOM/Routing | 创建/修改/发布 | 工作中心必须存在；版本发布后不可直接改 | PLM-lite。 |
| BP-ENG-004 | 发起 ECO/ECN 并审批发布 | 产品工程师、审批人 | EngineeringChange | 创建/审批/关闭 | 发布后通知 MRP/MES/ERP | 工程变更链路。 |
| BP-PLAN-001 | 维护需求来源：销售订单、预测、安全库存补充 | 计划员 | DemandSource | 创建/修改/查看 | 来源可追踪 | Sales/Forecast 输入。 |
| BP-PLAN-002 | 运行 MPS/MRP | 计划员 | MrpRun | 创建/异步/查看 | 使用已发布 BOM/工艺路线和库存事实 | 生成净需求。 |
| BP-PLAN-003 | 生成计划采购建议和计划工单建议 | 计划员 | PlanningSuggestion | 创建/修改/关闭 | 建议不是正式单据 | ERP/MES 接受后转正式单。 |
| BP-PLAN-004 | 查询 pegging 关系 | 计划员、采购员、生产计划员 | Pegging | 查看 | 能追溯需求到供应 | 用于解释数量来源。 |
| BP-INV-001 | 创建库位、批次、序列号和库存台账 | 仓储管理员 | StockLocation/StockLedger | 创建/修改/查看 | 库位编码唯一 | Inventory。 |
| BP-INV-002 | 提交库存移动 | WMS、ERP、MES | StockMovement | 创建 | 必须携带幂等键；校验可用量 | 唯一库存事实源。 |
| BP-INV-003 | 创建盘点任务、确认差异和调整 | 仓储管理员、审批人 | StockCountTask | 创建/审批/关闭 | 差异调整需审批 | WMS 执行扫码。 |
| BP-QUAL-001 | 建立检验标准、检验计划和检验记录 | 质检员 | InspectionPlan/Record | 创建/修改/关闭 | 附件通过 File Storage | 收货/工序/退货质检。 |
| BP-APP-001 | 创建业务审批链并记录审批结果 | 审批人 | ApprovalChain | 创建/修改/关闭 | 不替代 Ops | 采购、ECO、工单、盘点差异等。 |
| BP-ERP-001 | 从计划采购建议创建采购申请 | 采购员 | PurchaseRequisition | 创建/查看 | suggestionId 可追踪 | MRP 到采购。 |
| BP-ERP-002 | 处理 RFQ、供应商报价、采购订单和采购收货 | 采购员、供应商 | RFQ/PurchaseOrder/Receipt | 创建/修改/关闭 | 供应商有效；价格精度固定 | SRM-lite。 |
| BP-ERP-003 | 管理客户、商机、报价、销售订单和发货请求 | 销售员 | Opportunity/Quotation/SalesOrder | 创建/修改/关闭 | 客户有效；CPQ 边界预留 | CRM-lite/OMS-lite。 |
| BP-ERP-004 | 生成应收、应付、凭证和成本核算 | 财务人员 | AR/AP/Voucher/Cost | 创建/查看/异步 | 借贷平衡；金额精度固定 | Finance MVP。 |
| BP-WMS-001 | 创建收货通知、入库单和上架任务 | 仓储作业员 | InboundOrder/PutawayTask | 创建/修改/关闭 | 质检通过或处置完成后入账 | WMS。 |
| BP-WMS-002 | 创建出库单、拣货任务和复核包装 | 仓储作业员 | OutboundOrder/PickingTask/PackReview | 创建/修改/关闭 | Inventory 校验可用量 | WMS。 |
| BP-WMS-003 | 发送 WCS adapter 任务并处理回执 | 仓储作业员、Connector Host | WcsTask | 创建/异步/补偿 | WCS 外部系统可选 | 不独立建 WCS。 |
| BP-WMS-004 | 执行扫码盘点并提交差异 | 仓储作业员 | CountExecution | 创建/关闭 | 盘点任务来自 Inventory | PDA/扫码。 |
| BP-MES-001 | 从计划工单建议创建正式工单 | 生产计划员 | WorkOrder | 创建/查看 | 引用已发布 MBOM/路线 | MRP 到 MES。 |
| BP-MES-002 | 释放工序任务、规则排产和 Gantt 查询 | 生产计划员 | OperationTask/ScheduleResult | 创建/查看 | 工作中心日历有效 | APS 后置。 |
| BP-MES-003 | 提交报工、工序检验和完工入库请求 | 车间操作员、质检员 | ProductionReport/FinishedReceiptRequest | 创建/修改/关闭 | 报工数量不超过可报数量 | MES 到 WMS/ERP 成本。 |
| BP-TEL-001 | 建立 tag 映射和采集点 | 运维人员、Connector Host | TelemetryTag | 创建/修改/查看 | 设备资产存在；不保存控制密钥 | IIoT-lite。 |
| BP-TEL-002 | 接收设备状态、报警和时序摘要 | Connector Host | DeviceStateSnapshot/AlarmEvent | 创建/查看 | 原始高频时序可后置 | SCADA/PLC/DCS 来源。 |
| BP-MAINT-001 | 从报警或人工报修创建维修工单 | 运维人员 | MaintenanceWorkOrder | 创建/修改/关闭 | 设备资产存在 | CMMS-lite。 |
| BP-MAINT-002 | 创建保养计划、点检记录和停机原因 | 运维人员 | MaintenancePlan/Inspection/Downtime | 创建/修改/查看 | 影响 MES/OEE/Planning | CMMS-lite。 |
| BP-E2E-001 | 验证工程到制造链路 | 产品工程师、计划员、生产计划员 | 跨域链路 | 集成验收 | ProductEngineering、Planning、MES 可用 | 新增关键链路。 |
| BP-E2E-002 | 验证计划到采购/生产链路 | 计划员、采购员、生产计划员 | 跨域链路 | 集成验收 | MRP 可生成建议 | 新增关键链路。 |
| BP-E2E-003 | 验证采购到库存到应付链路 | 采购、仓储、质检、财务 | 跨域链路 | 集成验收 | ERP/WMS/Inventory/Finance 可用 | #77 升级。 |
| BP-E2E-004 | 验证订单到交付到应收链路 | 销售、仓储、财务 | 跨域链路 | 集成验收 | Sales/WMS/Inventory/AR 可用 | #77 升级。 |
| BP-E2E-005 | 验证生产执行到成本链路 | 生产、质检、仓储、财务 | 跨域链路 | 集成验收 | MES/WMS/Inventory/Cost 可用 | #77 升级。 |
| BP-E2E-006 | 验证设备到维护到产能链路 | 运维、生产计划员 | 跨域链路 | 集成验收 | Telemetry/Maintenance/MES 可用 | 新增关键链路。 |
| BP-E2E-007 | 验证 WCS adapter 边界 | 仓储、Connector Host | 跨域链路 | 集成验收 | WMS/WCS adapter 可模拟 | 自动化仓预留。 |

## Business Entity View

| 业务实体 | 主要职责/规则 | 关键输入/输出 |
| --- | --- | --- |
| SKU | 物料基础事实；组织+环境内编码唯一 | 输入 SKU 编码、名称、单位；输出 skuId。 |
| BusinessPartner | 客户、供应商、承运商主数据 | 输入伙伴类型、结算属性；输出 partnerId。 |
| Department | 业务部门和组织内业务归属 | 输入部门编码、名称、上级部门；输出 departmentId。 |
| Team | 班组和部门归属 | 输入班组编码、部门、班次偏好；输出 teamId。 |
| PersonnelSkill | 人员技能、资质和有效期 | 输入 IAM userId、技能代码、等级、有效期；输出 personnelSkillId。 |
| WorkCenter | 产能、日历、费率和排产资源 | 输入班次、产能、费率；输出可排产时段。 |
| DeviceAsset | 设备资产主数据和工作中心归属 | 输入设备编码、位置、型号；输出 deviceAssetId。 |
| EngineeringDocument | CAD/图纸/工艺文件引用 | 输入 fileId、版本、用途；输出 documentId。 |
| EngineeringItem | 工程物料及版本 | 输入工程编码、生命周期状态；输出 engineeringItemId。 |
| EBOM | 设计 BOM 结构 | 输入父子项和用量；输出 ebomVersionId。 |
| MBOM | 制造 BOM 结构 | 输入生产用料和损耗率；输出 mbomVersionId。 |
| Routing | 工艺路线和工序版本 | 输入工序、工作中心、标准工时；输出 routingVersionId。 |
| EngineeringChange | ECO/ECN 变更闭环 | 输入变更范围、原因、审批；输出发布事件。 |
| DemandSource | 销售订单、预测、安全库存等需求来源 | 输入需求类型、数量、日期；输出 demandSourceId。 |
| MrpRun | MRP 计算批次和参数 | 输入需求、库存、BOM；输出净需求和建议。 |
| PlanningSuggestion | 计划采购/工单建议 | 输入净需求；输出 suggestionId 和 pegging。 |
| StockLedger | SKU+库位+批次+序列号维度余额 | 输入库存移动；输出在库/可用/冻结。 |
| StockMovement | 所有库存数量变更的审计事实 | 输入移动类型、数量、幂等键；输出 movementId。 |
| ApprovalChain | 业务单据审批状态 | 输入单据引用、模板；输出审批结果。 |
| InspectionPlan | 检验计划和抽样规则 | 输入来源单据、检验类型；输出判定。 |
| PurchaseOrder | 供应商、价格、交期和收货状态 | 输入供应商、SKU、价格；输出采购状态。 |
| SalesOrder | 客户、价格、交期和履约状态 | 输入客户、SKU、价格；输出销售状态。 |
| InboundOrder | 入库执行事实 | 输入来源单据、SKU、数量、库位；输出入库完成。 |
| OutboundOrder | 出库执行事实 | 输入来源单据、SKU、批次；输出出库完成。 |
| WcsTask | WMS 与外部 WCS 的任务映射 | 输入仓储任务；输出回执或失败。 |
| WorkOrder | 制造任务、版本引用和数量控制 | 输入 SKU、MBOM、路线、数量；输出工单状态。 |
| ProductionReport | 工序报工事实 | 输入工序、数量、工时、不良；输出报工事件。 |
| TelemetryTag | 设备数据映射 | 输入设备、tag、单位、采样策略；输出 tagId。 |
| AlarmEvent | 报警事实 | 输入报警代码、级别、时间；输出报警状态。 |
| MaintenanceWorkOrder | 维修过程事实 | 输入故障、设备、优先级；输出维修结果。 |
| MaintenancePlan | 保养和点检计划 | 输入周期、设备、任务；输出计划实例。 |
| JournalVoucher | 财务凭证 | 输入借贷分录；输出平衡凭证。 |
| AccountReceivable/Payable | 应收应付台账 | 输入来源单据和金额；输出账款状态。 |
| CostCalculation | 材料、人工、制造费用和单位成本 | 输入报工、库存、费用；输出成本结果。 |

## Trigger And Follow-Up Actions

| 触发条件 | 后续动作/影响 | 受影响实体 | 备注 |
| --- | --- | --- | --- |
| 工程变更发布 | 通知 MRP 重算、MES 新工单引用新版本 | EngineeringChange、MrpRun、WorkOrder | 在制工单不自动换版。 |
| MRP run 完成 | 生成计划采购建议和计划工单建议 | MrpRun、PlanningSuggestion | 建议不是正式单据。 |
| 计划采购建议被接受 | ERP 创建采购申请 | PlanningSuggestion、PurchaseRequisition | 保留 pegging。 |
| 计划工单建议被接受 | MES 创建正式工单 | PlanningSuggestion、WorkOrder | 引用已发布 MBOM/路线。 |
| 采购收货记录创建 | Quality 创建收货检验，WMS 准备入库 | PurchaseReceipt、InspectionPlan、InboundOrder | 是否必检由策略决定。 |
| WMS 入库完成 | Inventory 创建库存增加移动，ERP 生成应付候选 | InboundOrder、StockMovement、AP | WMS 完成不等于库存已入账。 |
| 销售订单释放发货 | WMS 创建销售出库单 | SalesOrder、OutboundOrder | OMS-lite。 |
| WMS 出库完成 | Inventory 创建库存扣减移动，ERP 生成应收候选 | OutboundOrder、StockMovement、AR | 批次/序列号必须追踪。 |
| 工序报工提交 | Quality 可创建工序检验，ERP 可消费成本事实 | ProductionReport、InspectionPlan、CostCalculation | 不良数必须有原因。 |
| 完工入库请求创建 | WMS 创建生产入库单 | FinishedGoodsReceiptRequest、InboundOrder | 由 WMS 执行入库。 |
| 设备报警产生 | Maintenance 可创建维修工单，Notification 发送告警 | AlarmEvent、MaintenanceWorkOrder | 报警来源为 Telemetry。 |
| 维修工单导致设备不可用 | MES 排产和 Planning 产能受影响 | MaintenanceWorkOrder、ScheduleResult、MPS | 不直接改 WorkCenter 主数据。 |
| WCS adapter 回执失败 | WMS 标记任务异常并可补偿重发 | WcsTask、WarehouseTask | 外部 WCS 不进入事务。 |

## CleanDDD Modeling

### Aggregates

| 名称 | 职责摘要 | 关键不变式 |
| --- | --- | --- |
| Sku | 物料主数据 | 编码唯一；停用后不能被新单据引用。 |
| BusinessPartner | 客户/供应商/承运商主数据 | 同类型编码唯一；结算属性受权限保护。 |
| Department | 业务部门 | 组织+环境内编码唯一；停用后不能被新班组引用。 |
| Team | 班组 | 组织+环境内编码唯一；必须归属有效部门。 |
| PersonnelSkill | 人员技能资质 | userId+skillCode+effectiveFrom 唯一；过期资质不能用于新派工。 |
| WorkCenter | 工作中心、日历和费率 | 产能不能为负；日历不能重叠。 |
| DeviceAsset | 设备资产主数据 | 设备编码唯一；不保存采集密钥。 |
| EngineeringDocument | 工程文件引用 | 只保存 fileId/FileReference；不保存对象 key。 |
| EngineeringItem | 工程物料生命周期 | 已发布版本不可直接修改。 |
| EngineeringBom | EBOM 版本 | 子项不能形成循环；发布后不可变。 |
| ManufacturingBom | MBOM 版本 | 必须引用有效 SKU；发布后不可变。 |
| Routing | 工艺路线版本 | 工序顺序唯一；工作中心有效。 |
| EngineeringChange | ECO/ECN | 审批通过后才能发布；发布后不可撤销，只能新变更修正。 |
| DemandSource | 需求来源 | 来源可追踪；同一外部来源幂等。 |
| MasterProductionSchedule | MPS | 计划期间唯一；锁定后不能被自动重算覆盖。 |
| MrpRun | MRP 运行批次 | 输入版本固定；结果可追踪到需求和供应。 |
| PlanningSuggestion | 计划建议 | 接受/拒绝为终态；不能直接变正式单据。 |
| StockLocation | 仓库/库区/库位 | 库位编码唯一；停用库位不能新增移动。 |
| StockLedger | 库存余额 | 在库、可用、冻结不能为负。 |
| StockMovement | 库存移动 | 同一幂等键只能提交一次。 |
| StockCountTask | 盘点任务 | 差异未审批不能调整库存。 |
| BarcodeRule | 条码规则 | 规则范围唯一；序号段不能回退。 |
| ApprovalTemplate | 审批模板 | 单据类型+组织+环境唯一启用。 |
| ApprovalChain | 审批链 | 只能按步骤推进；终态不可再审批。 |
| InspectionStandard | 检验标准 | SKU+检验类型有效标准唯一。 |
| InspectionPlan | 检验计划 | 未完成计划不能判定通过。 |
| PurchaseRequisition | 采购申请 | 审批通过前不能生成采购订单。 |
| RequestForQuotation | 采购询价 | 关闭后不能接收新报价；供应商范围必须明确。 |
| SupplierQuotation | 供应商报价 | 过期报价不能转采购订单。 |
| PurchaseOrder | 采购订单 | 金额由数量和价格计算；收货不能超过规则范围。 |
| PurchaseReceipt | 采购收货 | 收货数量不能超过订单未收数量或允许超收规则。 |
| Opportunity | 商机 | 阶段只能按允许路径流转。 |
| Quotation | 销售报价 | 过期报价不能转销售订单。 |
| SalesOrder | 销售订单 | 审批通过前不能发货。 |
| DeliveryOrder | 发货单 | 发货数量不能超过可发数量。 |
| JournalVoucher | 记账凭证 | 借贷必须平衡。 |
| AccountReceivable | 应收台账 | 已收不能超过应收。 |
| AccountPayable | 应付台账 | 已付不能超过应付。 |
| InboundOrder | 入库单 | 完成后不可改明细。 |
| OutboundOrder | 出库单 | 完成前必须满足拣货和复核要求。 |
| WcsTask | WCS adapter 任务 | 外部任务号幂等；失败必须可诊断。 |
| WorkOrder | 工单 | 释放前必须引用已发布 MBOM/路线。 |
| OperationTask | 工序任务 | 报工数量不能超过剩余数量。 |
| ProductionReport | 报工记录 | 合格数和不良数不能为负；不良必须有原因。 |
| ScheduleResult | 排产结果 | 同一版本不可变；工作中心时间段不重叠。 |
| TelemetryTag | tag 映射 | tag key 在设备范围唯一。 |
| DeviceStateSnapshot | 设备状态快照 | occurredAtUtc 必须来自采集事实。 |
| AlarmEvent | 报警事件 | 同一外部报警实例幂等。 |
| MaintenanceWorkOrder | 维修工单 | 关闭必须有维修结果和停机归因。 |
| MaintenancePlan | 保养计划 | 周期和下次执行时间必须明确。 |

### Commands

| 名称 | 作用聚合 | 输入 | 触发行为/事件 | 幂等性 |
| --- | --- | --- | --- | --- |
| CreateSkuCommand | Sku | code, name, unit, category | SkuCreatedDomainEvent | code 唯一。 |
| CreateBusinessPartnerCommand | BusinessPartner | partnerType, code, name, settlementProfile | BusinessPartnerCreatedDomainEvent | partnerType+code 唯一。 |
| CreateDepartmentCommand | Department | code, name, parentDepartmentId | DepartmentCreatedDomainEvent | code 唯一。 |
| CreateTeamCommand | Team | code, name, departmentId, shiftPattern | TeamCreatedDomainEvent | code 唯一。 |
| AssignPersonnelSkillCommand | PersonnelSkill | userId, skillCode, level, effectiveFrom, effectiveTo | PersonnelSkillAssignedDomainEvent | userId+skillCode+effectiveFrom。 |
| CreateWorkCenterCommand | WorkCenter | code, name, capacityMinutesPerDay, calendarCode | WorkCenterCreatedDomainEvent | code 唯一。 |
| CreateWorkCalendarCommand | WorkCalendar | code, name, workingTimes | WorkCalendarCreatedDomainEvent | code 唯一。 |
| RegisterDeviceAssetCommand | DeviceAsset | code, model, location, workCenterId | DeviceAssetRegisteredDomainEvent | code 唯一。 |
| RegisterEngineeringDocumentCommand | EngineeringDocument | fileId, documentType, version | EngineeringDocumentRegisteredDomainEvent | fileId+version。 |
| ReleaseEngineeringBomCommand | EngineeringBom | engineeringItemId, lines, effectiveDate | EngineeringBomReleasedDomainEvent | item+version。 |
| ReleaseManufacturingBomCommand | ManufacturingBom | skuId, lines, effectiveDate | ManufacturingBomReleasedDomainEvent | sku+version。 |
| ReleaseRoutingCommand | Routing | skuId, operations, effectiveDate | RoutingReleasedDomainEvent | sku+version。 |
| ReleaseEngineeringChangeCommand | EngineeringChange | changeId, approvalRef | EngineeringChangeReleasedDomainEvent | changeId。 |
| CreateDemandSourceCommand | DemandSource | sourceType, sourceRef, skuId, quantity, dueDate | DemandSourceCreatedDomainEvent | sourceType+sourceRef。 |
| RunMrpCommand | MrpRun | planningHorizon, demandScope, versionRefs | MrpRunCompletedDomainEvent | runId。 |
| AcceptPlanningSuggestionCommand | PlanningSuggestion | suggestionId, targetType | PlanningSuggestionAcceptedDomainEvent | suggestionId。 |
| CreateStockLocationCommand | StockLocation | warehouseCode, locationCode, capacity | StockLocationCreatedDomainEvent | warehouse+location code 唯一。 |
| PostStockMovementCommand | StockMovement | movementType, skuId, quantity, locations, batch, documentRef, idempotencyKey | StockMovementPostedDomainEvent | idempotencyKey 必填。 |
| CreateStockCountTaskCommand | StockCountTask | countNo, warehouseCode, scope | StockCountTaskCreatedDomainEvent | countNo。 |
| CreateInspectionPlanCommand | InspectionPlan | sourceType, sourceId, skuId, inspectionType | InspectionPlanCreatedDomainEvent | sourceType+sourceId。 |
| StartApprovalChainCommand | ApprovalChain | documentType, documentId, applicantId, templateId | ApprovalChainStartedDomainEvent | documentType+documentId。 |
| ResolveApprovalStepCommand | ApprovalChain | chainId, approverId, result, comment | ApprovalApproved/RejectedDomainEvent | chainId+step+approver。 |
| RecordInspectionResultCommand | InspectionPlan | planId, measuredValues, result, attachmentFileIds | InspectionPassed/RejectedDomainEvent | planId+sequence。 |
| CreateBarcodeTemplateCommand | BarcodeRule | templateCode, ruleExpression, labelLayout | BarcodeTemplateCreatedDomainEvent | templateCode。 |
| CreateLabelPrintBatchCommand | LabelPrintBatch | templateCode, sourceDocumentRef, labels | LabelPrintBatchCreatedDomainEvent | sourceDocumentRef+templateCode。 |
| RecordBarcodeScanCommand | ScanRecord | barcodeValue, sourceDeviceId, idempotencyKey | BarcodeScannedDomainEvent | sourceDeviceId+idempotencyKey。 |
| CreatePurchaseRequisitionFromSuggestionCommand | PurchaseRequisition | suggestionId, requesterId | PurchaseRequisitionCreatedDomainEvent | suggestionId。 |
| CreateRequestForQuotationCommand | RequestForQuotation | purchaseRequisitionId, supplierIds, lines, validUntil | RequestForQuotationCreatedDomainEvent | purchaseRequisitionId+round。 |
| CreateSupplierQuotationCommand | SupplierQuotation | rfqId, supplierId, lines, validUntil | SupplierQuotationReceivedDomainEvent | rfqId+supplierId。 |
| CreatePurchaseOrderCommand | PurchaseOrder | supplierId, lines, paymentTerms | PurchaseOrderCreatedDomainEvent | orderNo。 |
| RecordPurchaseReceiptCommand | PurchaseReceipt | purchaseOrderId, receiptLines | PurchaseReceiptRecordedDomainEvent | receiptNo。 |
| CreateOpportunityCommand | Opportunity | customerId, title, expectedAmount | OpportunityOpenedDomainEvent | opportunityNo。 |
| CreateQuotationCommand | Quotation | opportunityId, customerId, lines | QuotationCreatedDomainEvent | quotationNo。 |
| CreateSalesOrderCommand | SalesOrder | customerId, lines, deliveryDate | SalesOrderCreatedDomainEvent | salesOrderNo。 |
| ReleaseDeliveryOrderCommand | DeliveryOrder | salesOrderId, deliveryLines | DeliveryOrderReleasedDomainEvent | deliveryNo。 |
| CreateJournalVoucherCommand | JournalVoucher | description, debitLines, creditLines | JournalVoucherPostedDomainEvent | voucherNo；借贷平衡。 |
| CreateInboundOrderCommand | InboundOrder | sourceType, sourceId, lines | InboundOrderCreatedDomainEvent | sourceType+sourceId。 |
| CompleteInboundOrderCommand | InboundOrder | inboundOrderId, putawayLines, idempotencyKey | InboundOrderCompletedDomainEvent | idempotencyKey 必填。 |
| CreateOutboundOrderCommand | OutboundOrder | sourceType, sourceId, lines | OutboundOrderCreatedDomainEvent | sourceType+sourceId。 |
| CompleteOutboundOrderCommand | OutboundOrder | outboundOrderId, pickedLines, packReview, idempotencyKey | OutboundOrderCompletedDomainEvent | idempotencyKey 必填。 |
| DispatchWcsTaskCommand | WcsTask | warehouseTaskId, adapterType, payload | WcsTaskDispatchedDomainEvent | warehouseTaskId+adapterType。 |
| CompleteWcsTaskCommand | WcsTask | externalTaskId, result, occurredAtUtc | WcsTaskCompleted/FailedDomainEvent | externalTaskId。 |
| FailWcsTaskCommand | WcsTask | externalTaskId, failureCode, diagnosticMessage, occurredAtUtc | WcsTaskFailedDomainEvent | externalTaskId。 |
| CreateWorkOrderFromSuggestionCommand | WorkOrder | suggestionId, quantity, mbomVersionId, routingVersionId | WorkOrderCreatedDomainEvent | suggestionId。 |
| ReleaseWorkOrderCommand | WorkOrder | workOrderId, approvalRef | WorkOrderReleasedDomainEvent | workOrderId。 |
| ReportOperationCommand | OperationTask | operationTaskId, goodQty, defectQty, reason, laborMinutes, idempotencyKey | OperationReportedDomainEvent | idempotencyKey。 |
| RunRuleScheduleCommand | ScheduleResult | workOrderScope, calendarScope, ruleSet | ScheduleResultCreatedDomainEvent | scheduleVersion。 |
| CreateTelemetryTagCommand | TelemetryTag | deviceAssetId, tagKey, valueType, unit, samplingPolicy | TelemetryTagCreatedDomainEvent | deviceAssetId+tagKey。 |
| RecordTelemetrySampleCommand | TelemetryTag | tagId, value, occurredAtUtc, sourceSequence | TelemetrySampleRecordedDomainEvent | tagId+sourceSequence。 |
| RaiseAlarmCommand | AlarmEvent | deviceAssetId, alarmCode, severity, occurredAtUtc, externalAlarmId | AlarmRaisedDomainEvent | externalAlarmId。 |
| ClearAlarmCommand | AlarmEvent | alarmId, clearedAtUtc, operatorId | AlarmClearedDomainEvent | alarmId。 |
| CreateMaintenanceWorkOrderCommand | MaintenanceWorkOrder | deviceAssetId, faultCode, priority, sourceAlarmId | MaintenanceWorkOrderOpenedDomainEvent | sourceAlarmId 或 requestNo。 |
| CompleteMaintenanceWorkOrderCommand | MaintenanceWorkOrder | workOrderId, result, downtimeMinutes, sparePartLines | MaintenanceWorkOrderCompletedDomainEvent | workOrderId。 |
| CreateMaintenancePlanCommand | MaintenancePlan | deviceAssetId, planCode, interval, nextDueDate | MaintenancePlanCreatedDomainEvent | planCode。 |
| RecordMaintenanceInspectionCommand | MaintenanceInspection | planId, operatorId, result, occurredAtUtc | MaintenanceInspectionRecordedDomainEvent | planId+occurredAtUtc。 |

### Queries

| 名称 | 过滤/排序/分页 | 输出 DTO |
| --- | --- | --- |
| ListSkusQuery | keyword, category, status, page | SkuListItemResponse。 |
| ListBusinessPartnersQuery | partnerType, keyword, status, page | BusinessPartnerListItemResponse。 |
| ListDepartmentsQuery | keyword, status, page | DepartmentListItemResponse。 |
| ListTeamsQuery | departmentId, keyword, status, page | TeamListItemResponse。 |
| ListPersonnelSkillsQuery | userId, skillCode, validOn, page | PersonnelSkillResponse。 |
| ListResourcesQuery | resourceType, keyword, status, page | BusinessResourceResponse。 |
| ListEngineeringDocumentsQuery | documentType, keyword, status, page | EngineeringDocumentResponse。 |
| ListEngineeringBomsQuery | engineeringItemId, status, page | EngineeringBomVersionResponse。 |
| GetEngineeringChangeQuery | changeId | EngineeringChangeDetailResponse。 |
| ListDemandSourcesQuery | sourceType, skuId, from, to, page | DemandSourceResponse。 |
| ListMrpRunsQuery | from, to, status, page | MrpRunListItemResponse。 |
| GetMrpPeggingQuery | runId, skuId, demandSourceId | MrpPeggingResponse。 |
| ListPlanningSuggestionsQuery | runId, suggestionType, status, page | PlanningSuggestionResponse。 |
| ListStockBalancesQuery | skuId, locationId, batch, page | StockBalanceResponse。 |
| ListInspectionRecordsQuery | sourceType, sourceId, result, page | InspectionRecordResponse。 |
| GetApprovalChainQuery | chainId | ApprovalChainDetailResponse。 |
| ListPurchaseOrdersQuery | supplierId, status, from, to, page | PurchaseOrderListItemResponse。 |
| ListSalesOrdersQuery | customerId, status, from, to, page | SalesOrderListItemResponse。 |
| ListReceivablesQuery | customerId, status, period, page | AccountReceivableResponse。 |
| ListPayablesQuery | supplierId, status, period, page | AccountPayableResponse。 |
| ListInboundOrdersQuery | status, sourceType, page | InboundOrderListItemResponse。 |
| ListOutboundOrdersQuery | status, sourceType, page | OutboundOrderListItemResponse。 |
| ListWorkOrdersQuery | skuId, status, from, to, page | WorkOrderListItemResponse。 |
| ListProductionReportsQuery | workOrderId, operationTaskId, from, to, page | ProductionReportResponse。 |
| GetScheduleGanttQuery | scheduleVersion, workCenterId, from, to | ScheduleGanttResponse。 |
| ListTelemetryTagsQuery | deviceAssetId, keyword, page | TelemetryTagResponse。 |
| QueryDeviceStateTimelineQuery | deviceAssetId, from, to, resolution | DeviceStateTimelineResponse。 |
| ListAlarmEventsQuery | deviceAssetId, severity, status, from, to, page | AlarmEventResponse。 |
| ListMaintenanceWorkOrdersQuery | deviceAssetId, status, priority, page | MaintenanceWorkOrderResponse。 |
| ListMaintenancePlansQuery | deviceAssetId, status, page | MaintenancePlanResponse。 |
| GetInventoryReportQuery | skuScope, locationScope, asOfDate, page | InventoryReportResponse。 |
| GetFinanceSummaryQuery | period, partnerScope | FinanceSummaryResponse。 |

### API Endpoints

| 路径/方法 | 命令/查询 | 认证/鉴权 | 幂等/一致性说明 |
| --- | --- | --- | --- |
| `POST /api/business/v1/master-data/skus` | CreateSkuCommand | `business.masterdata.products.manage` | code 唯一。 |
| `GET /api/business/v1/master-data/skus` | ListSkusQuery | `business.masterdata.products.read` | 只读分页查询。 |
| `POST /api/business/v1/master-data/partners` | CreateBusinessPartnerCommand | `business.masterdata.partners.manage` | type+code 唯一。 |
| `GET /api/business/v1/master-data/partners` | ListBusinessPartnersQuery | `business.masterdata.partners.read` | 只读分页查询。 |
| `POST /api/business/v1/master-data/departments` | CreateDepartmentCommand | `business.masterdata.resources.manage` | code 唯一；不复制 IAM 组织事实。 |
| `GET /api/business/v1/master-data/departments` | ListDepartmentsQuery | `business.masterdata.resources.read` | 只读分页查询。 |
| `POST /api/business/v1/master-data/teams` | CreateTeamCommand | `business.masterdata.resources.manage` | code 唯一；departmentId 必须有效。 |
| `GET /api/business/v1/master-data/teams` | ListTeamsQuery | `business.masterdata.resources.read` | 只读分页查询。 |
| `POST /api/business/v1/master-data/personnel-skills` | AssignPersonnelSkillCommand | `business.masterdata.resources.manage` | userId+skillCode+effectiveFrom。 |
| `GET /api/business/v1/master-data/personnel-skills` | ListPersonnelSkillsQuery | `business.masterdata.resources.read` | 只读分页查询。 |
| `POST /api/business/v1/master-data/work-centers` | CreateWorkCenterCommand | `business.masterdata.resources.manage` | code 唯一。 |
| `POST /api/business/v1/master-data/work-calendars` | CreateWorkCalendarCommand | `business.masterdata.resources.manage` | code 唯一。 |
| `POST /api/business/v1/master-data/device-assets` | RegisterDeviceAssetCommand | `business.masterdata.resources.manage` | code 唯一；不保存控制密钥。 |
| `GET /api/business/v1/master-data/resources` | ListResourcesQuery | `business.masterdata.resources.read` | 聚合资源查询。 |
| `POST /api/business/v1/engineering/documents` | RegisterEngineeringDocumentCommand | `business.engineering.documents.manage` | fileId+version。 |
| `GET /api/business/v1/engineering/documents` | ListEngineeringDocumentsQuery | `business.engineering.documents.read` | 只读分页查询。 |
| `POST /api/business/v1/engineering/eboms/{ebomId}/release` | ReleaseEngineeringBomCommand | `business.engineering.boms.manage` | item+version。 |
| `POST /api/business/v1/engineering/mboms/{mbomId}/release` | ReleaseManufacturingBomCommand | `business.engineering.boms.manage` | sku+version。 |
| `POST /api/business/v1/engineering/routings/{routingId}/release` | ReleaseRoutingCommand | `business.engineering.boms.manage` | sku+version。 |
| `GET /api/business/v1/engineering/eboms` | ListEngineeringBomsQuery | `business.engineering.boms.read` | 只读分页查询。 |
| `POST /api/business/v1/engineering/changes/{changeId}/release` | ReleaseEngineeringChangeCommand | `business.engineering.changes.manage` | changeId。 |
| `GET /api/business/v1/engineering/changes/{changeId}` | GetEngineeringChangeQuery | `business.engineering.changes.read` | 只读详情。 |
| `POST /api/business/v1/planning/demands` | CreateDemandSourceCommand | `business.planning.demands.manage` | sourceType+sourceRef 幂等。 |
| `GET /api/business/v1/planning/demands` | ListDemandSourcesQuery | `business.planning.demands.read` | 只读分页查询。 |
| `POST /api/business/v1/planning/mrp-runs` | RunMrpCommand | `business.planning.mrp.run` | runId。 |
| `GET /api/business/v1/planning/mrp-runs` | ListMrpRunsQuery | `business.planning.mrp.read` | 只读分页查询。 |
| `GET /api/business/v1/planning/mrp-runs/{runId}/pegging` | GetMrpPeggingQuery | `business.planning.mrp.read` | 只读。 |
| `GET /api/business/v1/planning/suggestions` | ListPlanningSuggestionsQuery | `business.planning.mrp.read` | 只读分页查询。 |
| `POST /api/business/v1/planning/suggestions/{suggestionId}/accept` | AcceptPlanningSuggestionCommand | `business.planning.suggestions.manage` | suggestionId。 |
| `POST /api/business/v1/inventory/locations` | CreateStockLocationCommand | `business.inventory.locations.manage` | warehouse+location code 唯一。 |
| `GET /api/business/v1/inventory/balances` | ListStockBalancesQuery | `business.inventory.ledger.read` | 只读库存余额。 |
| `POST /api/business/v1/inventory/movements` | PostStockMovementCommand | `business.inventory.movements.create` | `Idempotency-Key` 必填。 |
| `POST /api/business/v1/inventory/counts` | CreateStockCountTaskCommand | `business.inventory.counts.manage` | countNo 唯一。 |
| `GET /api/business/v1/inventory/reports` | GetInventoryReportQuery | `business.inventory.ledger.read` | asOfDate 只读报表。 |
| `POST /api/business/v1/quality/inspection-plans` | CreateInspectionPlanCommand | `business.quality.inspections.manage` | source document 幂等。 |
| `POST /api/business/v1/quality/inspection-plans/{planId}/records` | RecordInspectionResultCommand | `business.quality.inspections.manage` | planId+sequence。 |
| `GET /api/business/v1/quality/inspection-records` | ListInspectionRecordsQuery | `business.quality.inspections.read` | 只读分页查询。 |
| `POST /api/business/v1/barcodes/templates` | CreateBarcodeTemplateCommand | `business.barcodes.templates.manage` | template code 唯一。 |
| `POST /api/business/v1/barcodes/print-batches` | CreateLabelPrintBatchCommand | `business.barcodes.print` | print batch 幂等。 |
| `POST /api/business/v1/barcodes/scans` | RecordBarcodeScanCommand | `business.barcodes.scans.write` | device+idempotencyKey。 |
| `POST /api/business/v1/approvals/chains` | StartApprovalChainCommand | `business.approvals.manage` | documentType+documentId。 |
| `POST /api/business/v1/approvals/chains/{chainId}/steps/{stepNo}/resolve` | ResolveApprovalStepCommand | `business.approvals.manage` | chainId+step+approver。 |
| `GET /api/business/v1/approvals/chains/{chainId}` | GetApprovalChainQuery | `business.approvals.read` | 只读详情。 |
| `POST /api/business/v1/erp/purchase-requisitions/from-suggestion` | CreatePurchaseRequisitionFromSuggestionCommand | `business.erp.procurement.manage` | suggestionId。 |
| `POST /api/business/v1/erp/rfqs` | CreateRequestForQuotationCommand | `business.erp.procurement.manage` | requisition+round。 |
| `POST /api/business/v1/erp/supplier-quotations` | CreateSupplierQuotationCommand | `business.erp.procurement.manage` | rfqId+supplierId。 |
| `POST /api/business/v1/erp/purchase-orders` | CreatePurchaseOrderCommand | `business.erp.procurement.manage` | orderNo。 |
| `POST /api/business/v1/erp/purchase-receipts` | RecordPurchaseReceiptCommand | `business.erp.procurement.manage` | receiptNo。 |
| `GET /api/business/v1/erp/purchase-orders` | ListPurchaseOrdersQuery | `business.erp.procurement.read` | 只读分页查询。 |
| `POST /api/business/v1/erp/opportunities` | CreateOpportunityCommand | `business.erp.sales.manage` | opportunityNo。 |
| `POST /api/business/v1/erp/quotations` | CreateQuotationCommand | `business.erp.sales.manage` | quotationNo。 |
| `POST /api/business/v1/erp/sales-orders` | CreateSalesOrderCommand | `business.erp.sales.manage` | salesOrderNo。 |
| `POST /api/business/v1/erp/delivery-orders` | ReleaseDeliveryOrderCommand | `business.erp.sales.manage` | deliveryNo。 |
| `GET /api/business/v1/erp/sales-orders` | ListSalesOrdersQuery | `business.erp.sales.read` | 只读分页查询。 |
| `POST /api/business/v1/erp/finance/vouchers` | CreateJournalVoucherCommand | `business.erp.finance.manage` | 借贷平衡。 |
| `GET /api/business/v1/erp/finance/summary` | GetFinanceSummaryQuery | `business.erp.finance.read` | 只读汇总。 |
| `GET /api/business/v1/erp/finance/receivables` | ListReceivablesQuery | `business.erp.finance.read` | 只读分页查询。 |
| `GET /api/business/v1/erp/finance/payables` | ListPayablesQuery | `business.erp.finance.read` | 只读分页查询。 |
| `POST /api/business/v1/wms/inbound-orders` | CreateInboundOrderCommand | `business.wms.receipts.manage` | sourceType+sourceId 幂等。 |
| `POST /api/business/v1/wms/inbound-orders/{inboundOrderId}/complete` | CompleteInboundOrderCommand | `business.wms.receipts.manage` | `Idempotency-Key` 必填。 |
| `GET /api/business/v1/wms/inbound-orders` | ListInboundOrdersQuery | `business.wms.receipts.read` | 只读分页查询。 |
| `POST /api/business/v1/wms/outbound-orders` | CreateOutboundOrderCommand | `business.wms.shipments.manage` | sourceType+sourceId 幂等。 |
| `POST /api/business/v1/wms/outbound-orders/{outboundOrderId}/complete` | CompleteOutboundOrderCommand | `business.wms.shipments.manage` | `Idempotency-Key` 必填。 |
| `GET /api/business/v1/wms/outbound-orders` | ListOutboundOrdersQuery | `business.wms.shipments.read` | 只读分页查询。 |
| `POST /api/business/v1/wms/wcs-tasks/{warehouseTaskId}/dispatch` | DispatchWcsTaskCommand | `business.wms.automation.manage` | warehouseTaskId+adapter。 |
| `POST /api/business/v1/wms/wcs-tasks/{externalTaskId}/complete` | CompleteWcsTaskCommand | `business.wms.automation.manage` | externalTaskId。 |
| `POST /api/business/v1/wms/wcs-tasks/{externalTaskId}/fail` | FailWcsTaskCommand | `business.wms.automation.manage` | externalTaskId。 |
| `POST /api/business/v1/mes/work-orders/from-suggestion` | CreateWorkOrderFromSuggestionCommand | `business.mes.work-orders.manage` | suggestionId。 |
| `POST /api/business/v1/mes/work-orders/{workOrderId}/release` | ReleaseWorkOrderCommand | `business.mes.work-orders.manage` | workOrderId。 |
| `GET /api/business/v1/mes/work-orders` | ListWorkOrdersQuery | `business.mes.work-orders.read` | 只读分页查询。 |
| `POST /api/business/v1/mes/operation-tasks/{operationTaskId}/reports` | ReportOperationCommand | `business.mes.reporting.write` | `Idempotency-Key` 必填。 |
| `GET /api/business/v1/mes/reports` | ListProductionReportsQuery | `business.mes.reporting.read` | 只读分页查询。 |
| `POST /api/business/v1/mes/schedules/run` | RunRuleScheduleCommand | `business.mes.schedules.manage` | scheduleVersion。 |
| `GET /api/business/v1/mes/schedules/gantt` | GetScheduleGanttQuery | `business.mes.schedules.read` | 只读甘特数据。 |
| `POST /api/business/v1/iiot/tags` | CreateTelemetryTagCommand | `business.iiot.tags.manage` | device+tag key。 |
| `POST /api/business/v1/iiot/samples` | RecordTelemetrySampleCommand | `business.iiot.telemetry.write` | tagId+sourceSequence。 |
| `POST /api/business/v1/iiot/alarms` | RaiseAlarmCommand | `business.iiot.alarms.write` | externalAlarmId。 |
| `POST /api/business/v1/iiot/alarms/{alarmId}/clear` | ClearAlarmCommand | `business.iiot.alarms.write` | alarmId。 |
| `GET /api/business/v1/iiot/alarms` | ListAlarmEventsQuery | `business.iiot.alarms.read` | 只读分页查询。 |
| `GET /api/business/v1/iiot/devices/{deviceAssetId}/timeline` | QueryDeviceStateTimelineQuery | `business.iiot.telemetry.read` | 只读时间线。 |
| `POST /api/business/v1/maintenance/work-orders` | CreateMaintenanceWorkOrderCommand | `business.maintenance.work-orders.manage` | sourceAlarmId 或 requestNo。 |
| `POST /api/business/v1/maintenance/work-orders/{workOrderId}/complete` | CompleteMaintenanceWorkOrderCommand | `business.maintenance.work-orders.manage` | workOrderId。 |
| `GET /api/business/v1/maintenance/work-orders` | ListMaintenanceWorkOrdersQuery | `business.maintenance.work-orders.read` | 只读分页查询。 |
| `POST /api/business/v1/maintenance/plans` | CreateMaintenancePlanCommand | `business.maintenance.plans.manage` | plan code 唯一。 |
| `GET /api/business/v1/maintenance/plans` | ListMaintenancePlansQuery | `business.maintenance.plans.read` | 只读分页查询。 |
| `POST /api/business/v1/maintenance/inspections` | RecordMaintenanceInspectionCommand | `business.maintenance.plans.manage` | planId+occurredAtUtc。 |

## Integration Events

| 事件 | 发布方 | 主要 payload | 消费方 |
| --- | --- | --- | --- |
| `productEngineering.EngineeringChangeReleased` | ProductEngineering | changeId, affectedItems, effectiveDate | Planning、MES、ERP。 |
| `productEngineering.BomReleased` | ProductEngineering | bomVersionId, itemId, lines | Planning、MES。 |
| `productEngineering.RoutingReleased` | ProductEngineering | routingVersionId, skuId, operations | Planning、MES。 |
| `demandPlanning.MrpRunCompleted` | DemandPlanning | runId, horizon, summary | ERP、MES、Notification。 |
| `demandPlanning.PlannedPurchaseSuggested` | DemandPlanning | suggestionId, skuId, quantity, dueDate, pegging | ERP Procurement。 |
| `demandPlanning.PlannedWorkOrderSuggested` | DemandPlanning | suggestionId, skuId, quantity, dueDate, versionRefs | MES。 |
| `erp.PurchaseReceiptRecorded` | ERP | purchaseReceiptId, purchaseOrderId, supplierId, lines | Quality、WMS、Finance。 |
| `erp.DeliveryOrderReleased` | ERP | deliveryOrderId, salesOrderId, customerId, lines | WMS、Inventory projection。 |
| `wms.InboundOrderCompleted` | WMS | inboundOrderId, sourceType, lines | Inventory、ERP。 |
| `wms.OutboundOrderCompleted` | WMS | outboundOrderId, sourceType, lines | Inventory、ERP。 |
| `inventory.StockMovementPosted` | Inventory | movementId, movementType, documentRef, lines | ERP、MES、Planning、WMS。 |
| `mes.OperationReported` | MES | reportId, workOrderId, operationTaskId, goodQty, defectQty | Quality、ERP、Maintenance。 |
| `industrialTelemetry.AlarmRaised` | IndustrialTelemetry | alarmId, deviceAssetId, severity, occurredAtUtc | Maintenance、MES、Notification。 |
| `industrialTelemetry.DeviceStateChanged` | IndustrialTelemetry | deviceAssetId, previousState, currentState, occurredAtUtc | MES、Maintenance、Planning。 |
| `maintenance.AssetUnavailable` | Maintenance | deviceAssetId, reason, fromUtc | MES、Planning、Notification。 |
| `maintenance.AssetRestored` | Maintenance | deviceAssetId, restoredAtUtc | MES、Planning、Notification。 |

## Slices

### Slice 0. Documentation Freeze

Scope:

1. ADR 0012。
2. 业务平台领域架构。
3. 本 spec。
4. README、repo layout、权限矩阵入口。

Acceptance:

1. 文档解释哪些系统进入关键链路，哪些作为子域、外部系统或升级边界。
2. 文档明确主平台与业务平台边界。
3. 文档给出后续 CleanDDD implementation plan 输入。

### Slice 1. MasterData Foundation

Scope:

1. SKU、业务伙伴、工作中心、工作日历、设备资产。
2. 业务组织属性：部门、班组、人员技能资质。

Acceptance:

1. 可通过 API 创建和查询主数据。
2. 业务键唯一约束、schema 注释、catalog、migration、OpenAPI 测试齐备。
3. 不直接读写 IAM、AppHub 或 Telemetry 表。

### Slice 2. ProductEngineering MVP

Scope:

1. EngineeringDocument、EngineeringItem。
2. EBOM、MBOM、Routing。
3. ECO/ECN 和发布状态。

Acceptance:

1. CAD/图纸文件通过 File Storage 引用。
2. EBOM/MBOM/工艺路线可发布，发布版本不可直接修改。
3. 工程变更发布事件可被 Planning/MES 消费。

### Slice 3. Common Capability Foundation

Scope:

1. Inventory 库位、批次、序列号、库存台账、库存移动。
2. Quality 检验标准、计划、记录。
3. BarcodeLabel 条码规则、标签模板、扫码记录。
4. BusinessApproval 审批模板、审批链、审批记录。

Acceptance:

1. Inventory 是唯一库存余额事实源。
2. 业务审批不替代 Ops。
3. 质检不合格不能直接入可用库存。

### Slice 4. DemandPlanning MVP

Scope:

1. DemandSource。
2. MPS。
3. MRP run。
4. PlanningSuggestion 和 Pegging。

Acceptance:

1. MRP 使用已发布 BOM/路线、库存余额和需求来源。
2. 可生成计划采购建议和计划工单建议。
3. 建议可追溯到需求来源和供应来源。

### Slice 5. ERP Procurement/Sales/Finance MVP

Scope:

1. Procurement + SRM-lite：采购申请、RFQ、供应商报价、采购订单、收货、退货。
2. Sales + CRM-lite + OMS-lite：客户、商机、报价、销售订单、发货请求、退货。
3. Finance MVP：应收、应付、凭证、成本核算。

Acceptance:

1. 采购建议可转采购申请。
2. 销售订单可释放发货请求。
3. 库存入账事实能驱动应收、应付或成本核算候选。
4. 凭证借贷平衡校验生效。

### Slice 6. WMS Execution MVP

Scope:

1. 收货通知、入库单、上架任务。
2. 出库单、拣货任务、复核包装。
3. 盘点执行。
4. WCS adapter 任务和回执模拟。

Acceptance:

1. 入库完成后请求 Inventory 创建库存增加移动。
2. 出库完成后请求 Inventory 创建库存扣减移动。
3. WMS schema 不保存库存余额字段。
4. WCS adapter 失败可诊断、可补偿。

### Slice 7. MES Execution MVP

Scope:

1. 从计划工单建议创建正式工单。
2. 工序任务、报工、规则排产、Gantt 查询。
3. 完工入库请求和生产日报。

Acceptance:

1. 工单引用已发布 MBOM/工艺路线。
2. 报工数量不破坏工序不变式。
3. 完工入库请求能创建 WMS 入库单。

### Slice 8. IndustrialTelemetry MVP

Scope:

1. tag 映射、采集点、设备状态快照、报警事件、时序摘要。

Acceptance:

1. Connector Host 可写入受控工业数据事实。
2. 设备状态和报警事件可被 Maintenance、MES、Planning 和 Notification 消费。
3. 不实现 PLC/DCS 控制和 SCADA 画面。

### Slice 9. Maintenance MVP

Scope:

1. 维修工单、保养计划、点检记录、停机原因。
2. 报警到维修工单的异步消费。

Acceptance:

1. 报警可触发维修工单。
2. 设备不可用/恢复事件可被 MES/Planning 消费。
3. Maintenance 不拥有设备资产主数据和库存余额。

### Slice 10. Full-Chain Acceptance

Scope:

1. 工程到制造。
2. 计划到采购/生产。
3. 采购到库存到应付。
4. 订单到交付到应收。
5. 生产执行到成本。
6. 设备到维护到产能。
7. WMS 到 WCS adapter。

Acceptance:

1. API 端到端可调用。
2. 库存域在采购、生产、销售、盘点链路下数量正确。
3. MRP 建议能追溯到需求、BOM、库存和供应。
4. 工单能追溯到工程发布版本。
5. 设备报警能触发维护，并影响 MES/Planning 可用性。
6. 基础页面覆盖创建单据、列表、详情、审批和关键状态。
7. 条码打印、扫码、Gantt、库存报表、生产日报、应收应付汇总可走通。

## Testing Strategy

1. 聚合测试覆盖构造、状态流转、不变式、领域事件和异常路径。
2. 命令测试覆盖权限上下文、幂等键、重复提交和 KnownException。
3. 查询测试覆盖分页、排序、过滤、组织/环境隔离和版本追溯。
4. Endpoint 测试覆盖认证、鉴权、OpenAPI operationId、响应包装和错误模型。
5. IntegrationEvent 测试覆盖 envelope 必填字段、payload JSON 稳定性、版本不匹配拒绝、重复消费幂等。
6. PostgreSQL profile 测试覆盖 migration、schema convention、唯一约束、索引意图和 JSON/text 注释。
7. 全链路测试覆盖 Slice 10 七条链路，重点断言库存余额、MRP pegging、工程版本引用、财务候选事实和设备维护影响。

## Open Questions

| 项 | 描述 | 责任人 | 优先级 |
| --- | --- | --- | --- |
| CPQ 是否进入首批 | 是否存在配置型产品、按单设计或复杂报价规则。 | 业务负责人 + 销售负责人 | 中 |
| WCS 是否需要真实接入 | 是否存在自动化立库、输送线、AGV/AMR。 | 仓储负责人 | 中 |
| MRP 时间粒度 | 首批按日、班次还是小时粒度。 | 计划负责人 | 高 |
| 工程变更对在制单影响 | ECO/ECN 发布后在制工单是否换版。 | 产品工程负责人 + 生产负责人 | 高 |
| IIoT 原始时序保存策略 | 高频原始数据保存在哪里，业务库只保存摘要还是全部。 | 设备负责人 + 架构负责人 | 中 |
| 财务入账时点 | 应收/应付由业务单据、库存入账，还是组合校验后触发。 | 财务负责人 | 高 |

## Handoff

本 spec 已完成关键链路级需求分析与 CleanDDD 建模输入。进入实现前，应先补齐主平台 SDK/公开契约最小集成就绪项，再按切片分别执行 implementation plan。业务切片只能通过主平台公开 API、公开 Contracts、Platform SDK、IntegrationEvent 和 IAM 授权上下文消费主平台能力，不得引用主平台服务的 Domain 或 Infrastructure 项目。

0. [business-main-platform-integration-readiness](../plans/2026-05-20-business-main-platform-integration-readiness.md)
1. [business-master-data-foundation](../plans/2026-05-20-business-master-data-foundation.md)
2. [business-product-engineering-mvp](../plans/2026-05-20-business-product-engineering-mvp.md)
3. [business-common-capability-foundation](../plans/2026-05-20-business-common-capability-foundation.md)
4. [business-demand-planning-mvp](../plans/2026-05-20-business-demand-planning-mvp.md)
5. [business-erp-procurement-sales-finance-mvp](../plans/2026-05-20-business-erp-procurement-sales-finance-mvp.md)
6. [business-wms-execution-mvp](../plans/2026-05-20-business-wms-execution-mvp.md)
7. [business-mes-execution-mvp](../plans/2026-05-20-business-mes-execution-mvp.md)
8. [business-industrial-telemetry-mvp](../plans/2026-05-20-business-industrial-telemetry-mvp.md)
9. [business-maintenance-mvp](../plans/2026-05-20-business-maintenance-mvp.md)
10. [business-full-chain-acceptance](../plans/2026-05-20-business-full-chain-acceptance.md)

每个 implementation plan 必须列出具体文件、测试、迁移、OpenAPI、权限 seed、schema catalog 和验证命令。
