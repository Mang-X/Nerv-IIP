# 业务平台领域架构

本文档定义 Nerv-IIP 业务平台的关键链路、服务分层、事实源、外部系统边界和首批实施顺序。它承接 ADR 0012，并约束后续业务平台 spec、实现计划和 CleanDDD 落地。

## 结论

原始 issues #72 到 #77 对 MES/WMS/ERP 主干判断是合理的，但不足以覆盖工业业务关键链路。业务平台规划需要从“系统名清单”转成“链路事实源”：

1. 工程到制造：CAD/PDM/PLM → EBOM/MBOM/工艺路线 → 工程变更 → MES/MRP。
2. 计划到采购/生产：销售订单/预测 → MPS/MRP → 计划采购建议/计划工单建议 → APS lite 排程约束。
3. 采购到库存到应付：采购申请/订单 → 收货/质检 → WMS 入库 → Inventory → 应付。
4. 订单到交付到应收：商机/报价/销售订单 → 发货履约 → WMS 出库 → Inventory → 应收。
5. 设备到维护到产能：PLC/DCS/SCADA/IIoT → 设备状态/报警/OEE → CMMS → APS/MES 排程约束。
6. 仓储自动化：WMS → WCS adapter → 自动化设备回执 → WMS → Inventory。

首批必须纳入规划的是 ProductEngineering、DemandPlanning、Scheduling/APS lite、IndustrialTelemetry、Maintenance。CRM、SRM、CPQ、OMS、WCS、CAD、SCADA、PLC/DCS 不全部独立建设，而是按子域、外部系统或升级边界处理。

## 系统取舍

| 名称 | 首批处理 | 原因 |
| --- | --- | --- |
| PDM/PLM | 建 ProductEngineering lite | EBOM、MBOM、工艺路线、ECO/ECN 是制造和 MRP 的前置事实。 |
| CAD | 外部系统/文件来源 | 平台只保存 CAD 文件引用、版本、预览和发布关系，不实现 CAD 设计能力。 |
| MPS/MRP | 建 DemandPlanning lite | 采购建议、工单建议和物料需求数量必须有计划来源。 |
| APS | 建 Scheduling/APS lite | P0 先做排程契约、有限产能启发式内核、资源负载和冲突解释；高级优化器、仿真和自动重排后置。 |
| IIoT | 建 IndustrialTelemetry lite | tag、报警、设备状态、OEE 输入是设备链路的基础事实。 |
| PLC/DCS/SCADA | 外部系统/Connector 来源 | 通过 Connector Host 接入，不在业务平台内实现控制系统。 |
| CMMS | 建 Maintenance lite | 维修、保养、点检、停机原因会影响设备可用性和排产。 |
| EAM | 后置 | 首批 CMMS 覆盖维护；资产财务、折旧、全生命周期后置。 |
| SRM | ERP Procurement 子域 | 首批做供应商、询价/报价、采购协同最小能力；供应商门户后置。 |
| CRM | ERP Sales 子域 | 首批做客户、商机、报价、销售订单；营销和线索池后置。 |
| CPQ | 预留边界 | 只有配置型产品、按单设计或复杂报价规则成立时独立。 |
| OMS | ERP Sales + WMS fulfillment 子域 | 首批销售订单到出库履约即可；多渠道拆单、履约路由后置。 |
| WCS | WMS adapter 边界 | 没有自动化仓时不独立；有立库、输送线、AGV/AMR 时再实现。 |
| 外协加工 | ERP Procurement + MES/WMS 流程候选 | 当前无独立服务和事实源。先按采购订单/外协工序、发料出库、回厂收货、质检和结算的跨域流程处理；只有外协计划、供应商协同、在外库存和结算复杂度足够时再独立。 |

## 业务分层

```text
外部工程与现场系统
  CAD / SCADA / PLC / DCS / WCS / AGV / AMR
    -> Connector Host / File Storage / Platform SDK

业务平台 Layer 3：订单、采购、财务与履约入口
  ERP Procurement：SRM-lite、采购申请、询价、采购订单、收货、退货
  ERP Sales：CRM-lite、报价、销售订单、发货、退货、OMS-lite
  ERP Finance：应收、应付、凭证、成本核算 MVP

业务平台 Layer 2：计划与执行
  DemandPlanning：MPS、MRP、计划采购建议、计划工单建议、pegging
  Scheduling / APS lite：排程问题、排程方案、有限产能分配、资源负载、冲突解释
  MES：工单、工序、报工、排程结果消费、完工入库请求
  WMS：收货、入库、上架、出库、拣货、复核、盘点执行、WCS adapter
  Maintenance：维修工单、保养计划、点检、故障、停机原因、备件需求

业务平台 Layer 1：通用能力与工业数据
  Inventory：库存台账、库位、批次、序列号、库存移动、盘点调整
  Quality：检验标准、检验计划、检验记录、不合格处置
  BarcodeLabel：条码规则、标签模板、标签打印、扫码记录
  BusinessApproval：业务审批模板、审批链、审批记录
  IndustrialTelemetry：tag 映射、采集点、时序摘要、报警、设备状态快照

业务平台 Layer 0：主数据与产品工程
  BusinessMasterData：SKU、业务伙伴、组织业务属性、资源、工作中心、设备资产
  ProductEngineering：工程物料、CAD 引用、EBOM、MBOM、工艺路线、ProductionVersion、ECO/ECN、发布状态
```

依赖方向只能从高层业务过程域指向低层能力域或主平台公开能力。业务服务之间不共享数据库表，不建跨 schema 外键。

BusinessMasterData 的治理和字段口径见 `docs/adr/0013-business-master-data-governance.md`、`docs/architecture/business-master-data-field-matrix.md` 和 `docs/architecture/business-master-data-process-manufacturing-supplement.md`。MasterData Foundation 的原始计划是最小骨架；继续暴露 API 或让下游依赖前，必须先完成 `docs/superpowers/plans/2026-05-21-business-master-data-realignment.md`。

## 服务与事实源

| 服务/上下文 | 建议落点 | 默认 schema | 拥有事实 | 不拥有事实 | 主要依赖 |
| --- | --- | --- | --- | --- | --- |
| BusinessMasterData | `backend/services/Business/MasterData` | `business_masterdata` | SKU、客户、供应商、承运商、业务部门、班组、人员业务属性、工作中心、工作日历、设备资产 | EBOM/MBOM 发布、IAM 用户角色、AppHub 实例、实时采集数据 | IAM、File Storage |
| ProductEngineering | `backend/services/Business/ProductEngineering` | `product_engineering` | CAD 文件引用、工程物料、EBOM、MBOM、工艺路线版本、ProductionVersion、ECO、ECN、发布记录 | CAD 设计内容、库存、工单、采购订单 | MasterData、File Storage、Notification |
| DemandPlanning | `backend/services/Business/DemandPlanning` | `demand_planning` | 需求来源、MPS、MRP run、计划采购建议、计划工单建议、pegging、净需求 | 正式采购订单、正式工单、库存余额 | ProductEngineering、Inventory、ERP、MES |
| Scheduling / APS lite | `backend/services/Business/Scheduling` | `scheduling` | 排程问题、排程方案、资源负载、冲突项、不可排原因、锁定任务、排程版本 | MRP 需求、正式工单执行、库存余额、设备报警、维修工单 | 按 ADR 0014 分组接入：静态事实输入（snapshot/resolve 契约）来自 MasterData、ProductEngineering；计划输入来自 DemandPlanning 计划建议与 MES 工单候选；运行时约束通过事件投影表达 Inventory/WMS 齐套、Quality 阻断、IndustrialTelemetry 报警和 Maintenance 维护窗口。 |
| Inventory | `backend/services/Business/Inventory` | `inventory` | 库存台账、库位、批次、序列号、库存移动、盘点任务与调整 | 仓储执行步骤、采购/销售/工单状态 | MasterData、Quality、Approval |
| Quality | `backend/services/Business/Quality` | `quality` | 检验标准、检验计划、检验记录、NonconformanceReport 不合格品报告与处置闭环 | 库存余额、仓储作业状态、采购销售单据；处置后续动作只发布集成事件 | MasterData、Inventory、File Storage |
| BarcodeLabel | `backend/services/Business/BarcodeLabel` | `barcode` | 条码规则、标签模板、标签打印批次、扫码记录 | 库存余额、业务单据状态 | MasterData、Inventory、File Storage |
| BusinessApproval | `backend/services/Business/Approval` | `business_approval` | 审批模板、审批链、审批记录、业务审批状态 | Ops 运维任务、平台审计事实 | IAM、Notification |
| ERP | `backend/services/Business/Erp` | `erp` | 采购、SRM-lite、销售、CRM-lite、OMS-lite、应收、应付、凭证、成本核算 | WMS 执行步骤、库存余额、完整总账月结 | MasterData、Planning、Inventory、WMS、MES |
| WMS | `backend/services/Business/Wms` | `wms` | 收货通知、入库单、出库单、拣货、上架、复核包装、盘点执行、WCS 任务映射 | 库存余额、采购/销售/工单业务状态、WCS 内部调度 | MasterData、Inventory、Quality、BarcodeLabel |
| MES | `backend/services/Business/Mes` | `mes` | 工单、工序任务、报工、排产结果、完工入库请求、生产日报 | 库存余额、WMS 入库单、设备维护事实 | ProductEngineering、Planning、Inventory、WMS、Quality、Telemetry、Maintenance |
| IndustrialTelemetry | `backend/services/Business/IndustrialTelemetry` | `industrial_telemetry` | tag 定义、采集点映射、设备状态快照、时序摘要、报警事件、OEE 输入事实 | PLC/DCS 控制、SCADA 画面、设备资产主数据 | Connector Host、MasterData、AppHub |
| Maintenance | `backend/services/Business/Maintenance` | `maintenance` | 维修工单、保养计划、点检记录、故障、停机原因、备件需求 | 设备资产主数据、库存余额、生产工单状态 | MasterData、Telemetry、Inventory、MES |
| BusinessGateway | `backend/gateway/BusinessGateway` | 无持久化默认值 | 业务页面聚合查询、业务前端 OpenAPI、上下文透传 | 领域规则、持久事实 | 业务服务 OpenAPI/Contracts、IAM |

Scheduling / APS lite 当前尚未落地独立服务，后续由 #206 创建服务并落地排程契约；现有 MES 内部 `RuleScheduler` 只属于规则排程过渡能力。上表的“主要依赖”表示 `SchedulingProblem` 的输入事实来源、adapter 和事件投影边界，不表示 Scheduling 启动期或单次排程请求需要同步 fan-out 到全部上游服务。首个 #206 增量应按 ADR 0014 只接入必要事实，优先使用 MasterData 资源/日历、DemandPlanning 计划建议或 MES 工单候选，其他事实源以显式 snapshot/fixture adapter 渐进补齐。

## 业务控制台边界

#166 到 #169 的 Business Console MVP 采用独立 `frontend/apps/business-console`
入口和 `backend/gateway/BusinessGateway` BFF。主平台 `frontend/apps/console` 可以保留业务平台状态页或应用入口链接，但不得承载 SKU 维护、库存移动、盘点、检验、NCR、工单或排程等真实业务 CRUD 和工作流页面。

BusinessGateway 暴露 `/api/business-console/v1/**` 页面级 facade，负责用户认证、IAM 权限检查、组织/环境上下文透传、internal service token 下游调用和 OpenAPI 输出。BusinessGateway 不持久化业务事实，不计算库存可用量，不决定 NCR 状态机，不生成 MES 或 APS 排程结果，也不引用业务服务的 Web、Domain 或 Infrastructure 项目。

前端导航的规范、当前 route-ready 范围、能力目录和角色导航口径以 `docs/architecture/frontend-navigation-map.md` 为准。服务边界不是用户操作边界，Business Console 页面可以按角色任务聚合多个服务事实。当前 BusinessGateway 已覆盖 MasterData、ProductEngineering、Inventory、Quality、DemandPlanning 和 MES PC 工作台；ERP、WMS、BarcodeLabel、BusinessApproval、IndustrialTelemetry、Maintenance 和 BusinessScheduling/APS 还没有正式 Business Console facade，不能仅因后端服务存在就在可见菜单中标为已交付。当前 `/erp` 页面只是过渡聚合页，不代表 ERP 前端已完成。

业务控制台首批路由围绕已落地服务能力收敛：

| Issue | 页面范围 | BFF 下游 |
| --- | --- | --- |
| #166 | SKU 列表、创建、UOM/站点/产线/工作中心/设备等基础资源选择。 | BusinessMasterData |
| #167 | 可用量查询、库存移动、盘点任务与调整确认。 | Inventory |
| #168 | 检验计划、检验记录、NCR 列表、处置和关闭。 | Quality |
| #169 | 工单、急单、规则排程、报工和完工入库请求可见性；不含甘特。 | MES |
| #206 | APS lite 排程问题、排程方案、资源负载和冲突结果；甘特图只消费输出。 | Scheduling / MES facade |
| #207 | 设备状态、报警、停机、维护窗口、APS 可用性和 MES readiness 联动。 | IndustrialTelemetry / Maintenance / MES facade |

OpenAPI 快照写入 `frontend/packages/api-client/openapi/business-gateway-console.v1.json`，生成代码隔离在 `frontend/packages/api-client/src/generated/business-console/`，业务页面只通过 `@nerv-iip/api-client` 稳定 business-console 导出消费。

## 主平台边界

### IAM 与业务组织

IAM 拥有 `organizationId`、`environmentId`、`userId`、角色、权限和授权范围。业务组织域只保存业务部门、班组、人员技能、资质、岗位、排班等行业属性，并通过公开 ID 引用 IAM。

业务服务不得复制 IAM 角色或权限事实，不得直接读取 IAM 表，也不得把业务部门当成平台组织替代品。

### AppHub、Connector Host 与工业数据

AppHub 拥有受管应用、实例、节点、能力、心跳和 reported state。Connector Host 负责本地资源发现、协议适配和状态上报。IndustrialTelemetry 只接收 Connector Host 或受控数据入口上报后的工业数据事实。

PLC/DCS/SCADA 不进入业务平台内部模型。业务平台保存 tag、报警、状态摘要和映射关系，不控制现场系统。

### File Storage 与工程文件

CAD、图纸、工艺文件、质检附件、维修照片、采购合同和销售报价附件都通过 File Storage 管理。ProductEngineering 只保存 fileId、FileReference、版本和发布关系，不保存对象存储 key 或预签名 URL。

### Ops 与业务审批

Ops 处理平台动作任务，例如重启、停止、备份和诊断。BusinessApproval 处理业务单据，例如 ECO、采购申请、工单、领料、盘点差异和销售折扣。两者不能合并。

## 关键链路

### 工程到制造

```text
CAD file / design package
  -> ProductEngineering.EngineeringItem
  -> ProductEngineering.EBOM
  -> ProductEngineering.ECO/ECN approval
  -> ProductEngineering.MBOM + Routing release
  -> ProductEngineering.ProductionVersion resolve
  -> DemandPlanning.MRP
  -> MES.WorkOrder
```

ProductEngineering 是 EBOM、MBOM、工艺路线版本、ProductionVersion 和工程变更事实源。MRP/MES 使用 ProductionVersion resolve API 以 SKU、有效日期和批量解析 productionVersionId 以及对应的已发布 MBOM/路线，MES 新工单引用 productionVersionId。

### 计划到采购/生产

```text
ERP SalesOrder / Forecast
  -> DemandPlanning.MPS
  -> DemandPlanning.MRP run
  -> PlannedPurchaseSuggestion -> ERP.PurchaseRequisition
  -> PlannedWorkOrderSuggestion -> ProductEngineering.ResolveProductionVersion -> MES.WorkOrder
  -> Scheduling.SchedulePlan -> MES.Dispatch
```

DemandPlanning 生成计划建议，不直接创建正式采购订单、正式工单、库存移动或排程方案。ERP/MES 接受建议后创建正式业务单据；Scheduling/APS lite 消费已接受的计划工单、MES 工单、资源和约束，输出排程方案和冲突解释。

### 采购到库存到应付

```text
DemandPlanning.PlannedPurchaseSuggestion
  -> ERP.PurchaseRequisition
  -> ERP.RFQ / SupplierQuotation (SRM-lite)
  -> ERP.PurchaseOrder
  -> ERP.PurchaseReceipt
  -> ERP.SupplierInvoice (three-way match)
  -> Quality.InspectionPlan
  -> WMS.InboundOrder
  -> Inventory.StockMovement
  -> ERP.AccountPayable
  -> ERP.SubledgerVoucher
```

SRM-lite 首批只处理供应商、询价、报价和采购协同最小流程，不做完整供应商门户。采购订单创建后通过 BusinessApproval 公共 API/事件审批门禁释放，未批准前不能收货；采购收货会通过公开 Inventory movement request 事件请求库存入账；供应商发票按 PO、收货和发票行三单匹配，通过后生成 AP 和最小应付子分类账凭证，超出容差或累计已开票数量超过收货数量时进入 `PaymentHeld`，不创建 AP/凭证。held 发票可人工释放生成 AP/凭证，或作废并从后续累计开票量中排除。

### 订单到交付到应收

```text
ERP.Opportunity / Quotation (CRM-lite)
  -> optional CPQ boundary
  -> ERP.SalesOrder
  -> optional OMS allocation boundary
  -> WMS.OutboundOrder
  -> WMS.Pick + PackReview
  -> Inventory.StockMovement
  -> ERP.AccountReceivable
  -> ERP.SubledgerVoucher
```

CRM-lite 与 OMS-lite 首批由 ERP Sales 和 WMS fulfillment 承担。CPQ 只在配置型产品场景进入独立规划。销售订单可按客户信用额度、开放应收和已释放销售订单敞口做最小信用检查；发货请求会通过公开 WMS outbound-requested 事件进入 WMS 出库执行。

### 生产执行到成本

```text
MES.WorkOrder
  -> Scheduling.SchedulePlan
  -> MES.OperationTask
  -> MES.ProductionReport
  -> Quality.OperationInspection
  -> MES.FinishedGoodsReceiptRequest
  -> WMS.InboundOrder
  -> Inventory.StockMovement
  -> ERP.CostCalculation
```

MES 拥有工单、工序和报工事实。Scheduling 拥有排程方案和资源负载事实。Inventory 拥有库存入账事实。ERP Finance 消费报工、物料消耗和库存入账结果生成成本。

### 设备到维护到产能

```text
PLC/DCS/SCADA data
  -> Connector Host
  -> IndustrialTelemetry.TagSample / AlarmEvent / DeviceStateSnapshot
  -> Maintenance.WorkOrder / MaintenancePlan
  -> Scheduling resource availability
  -> MES downtime / OEE
  -> DemandPlanning capacity adjustment
```

IndustrialTelemetry 不控制现场；Maintenance 不拥有设备主数据；Scheduling 不直接读取 PLC/DCS/SCADA；MES 不直接改维修事实。

`IndustrialTelemetry.AlarmEvent` / `industrialTelemetry.AlarmRaised` 到 `Maintenance.MaintenanceWorkOrder`，再到 `maintenance.AssetUnavailable` / `maintenance.AssetRestored` 和 `MES.WorkCenterUnavailability` 的投影链路见 `docs/architecture/equipment-status-event-flow.md`。

### 仓储自动化

```text
WMS.WarehouseTask
  -> WCS adapter command
  -> external WCS / conveyor / ASRS / AGV
  -> WCS task receipt
  -> WMS task completed
  -> Inventory.StockMovement
```

WCS 不是首批业务服务。WMS 预留 adapter、任务号、回执和失败补偿边界。

## IntegrationEvent 基线

业务事件必须采用 ADR 0011 的 envelope。事件名称表达已经发生的事实，不使用命令式名称。

| 发布方 | 事件 | 主要消费者 | 说明 |
| --- | --- | --- | --- |
| BusinessMasterData | `masterData.SkuChanged`、`masterData.SkuDisabled`、`masterData.UnitOfMeasureChanged`、`masterData.BusinessPartnerChanged`、`masterData.ResourceChanged`、`masterData.WorkCalendarChanged`、`masterData.DeviceAssetChanged`、`masterData.ReferenceDataCodeChanged` | ProductEngineering、DemandPlanning、Inventory、Quality、ERP、WMS、MES、IndustrialTelemetry、Maintenance、BarcodeLabel | 主数据公共事实变化；下游缓存或引用快照必须消费这些事件或通过 resolve API 校验。 |
| ProductEngineering | `productEngineering.BomReleased`、`productEngineering.RoutingReleased`、`productEngineering.ProductionVersionCreated`、`productEngineering.EngineeringChangeReleased` | DemandPlanning、MES、ERP | 工程版本、生产版本绑定和变更发布。 |
| DemandPlanning | `demandPlanning.MrpRunCompleted`、`demandPlanning.PlannedPurchaseSuggested`、`demandPlanning.PlannedWorkOrderSuggested` | ERP、MES、Notification | 计划建议生成。 |
| Scheduling | `scheduling.SchedulePlanGenerated`、`scheduling.ScheduleConflictDetected`、`scheduling.SchedulePlanReleased` | MES、DemandPlanning、Notification | 排程方案、冲突和发布事实；P0 先冻结事件名称和 payload 边界，随 Scheduling schema/API 落地补契约测试。 |
| ERP | `erp.PurchaseReceiptRecorded`、`erp.DeliveryOrderReleased`、`erp.AccountPayableCreated`、`erp.AccountReceivableCreated` | WMS、Quality、Inventory、Notification | 采购、销售、财务事实。 |
| WMS | `wms.InboundOrderCompleted`、`wms.OutboundOrderCompleted`、`wms.CountExecutionCompleted`、`wms.WcsTaskFailed` | Inventory、ERP、MES、Notification | 仓储作业完成或自动化失败。 |
| Inventory | `inventory.StockMovementPosted`、`inventory.StockCountVarianceConfirmed`、`inventory.StockAvailabilityChanged` | ERP、MES、Planning、WMS、Notification | 库存事实变化。 |
| MES | `mes.WorkOrderReleased`、`mes.OperationReported`、`mes.FinishedGoodsReceiptRequested`、`mes.DowntimeRecorded` | WMS、Quality、ERP、Maintenance | 制造过程事实。 |
| IndustrialTelemetry | `industrialTelemetry.DeviceStateChanged`、`industrialTelemetry.AlarmRaised`、`industrialTelemetry.AlarmCleared` | MES、Maintenance、Notification | 设备状态和报警事实。 |
| Maintenance | `maintenance.WorkOrderOpened`、`maintenance.WorkOrderCompleted`、`maintenance.AssetUnavailable`、`maintenance.AssetRestored` | MES、Planning、Notification | 维护和产能影响事实。 |
| Quality | `quality.InspectionPassed`、`quality.InspectionRejected`、`quality.NcrOpened`、`quality.DispositionDecided`、`quality.NcrClosed` | WMS、MES、ERP、Inventory、Notification | 质检与不合格处置结果；NCR 处置只发事件，不直接改库存、返工工单、退货或仓储任务。 |
| BusinessApproval | `businessApproval.ApprovalApproved`、`businessApproval.ApprovalRejected` | ERP、MES、Inventory、ProductEngineering、Maintenance | 业务审批结果。 |

事件 payload 不携带 token、密码、完整附件内容、对象存储 key、PLC 控制指令或大体积时序数据。

## 权限命名

业务权限码采用 `business.{context}.{resource}.{action}`。示例：

1. `business.engineering.boms.manage`
2. `business.planning.mrp.run`
3. `business.scheduling.plans.manage`
4. `business.inventory.movements.create`
5. `business.iiot.telemetry.read`
6. `business.maintenance.work-orders.manage`
7. `business.wms.receipts.manage`
8. `business.mes.work-orders.manage`
9. `business.erp.finance.read`

权限码必须在 `docs/architecture/authorization-matrix.md` 登记后，才能进入 IAM seed、Endpoint 鉴权和测试。

## 首批实施顺序

### Slice 0. 文档冻结

ADR 0012、本架构文档、业务 spec、README/repo layout/权限矩阵入口完成。

### Slice 1. MasterData Foundation

建立 SKU、业务伙伴、组织业务属性、工作中心、工作日历、设备资产和资源主数据。

2026-05-21 起，Slice 1 继续实施前必须先完成 MasterData realignment：补齐 UOM/换算、SKU 工业属性、资源层级、设备静态能力、流程型制造边界、下游 resolve 契约和 MasterData 变更事件。该步骤不把 Recipe/Formula、批次实例、库存余额、检验记录、批生产记录或实时采集数据移入 MasterData。

### Slice 2. ProductEngineering MVP

建立 CAD 文件引用、工程物料、EBOM、MBOM、工艺路线版本、ProductionVersion、ECO/ECN 和发布状态。ProductionVersion 绑定已发布 MBOM + Routing，并为 MRP/MES 提供 productionVersionId 解析契约。

### Slice 3. Common Capability Foundation

建立 Inventory、Quality、BarcodeLabel、BusinessApproval 的最小能力，尤其是库存移动唯一事实源和业务审批链。

### Slice 4. DemandPlanning MVP

建立需求来源、MPS、MRP run、净需求、计划采购建议、计划工单建议和 pegging。

### Slice 5. ERP Procurement/Sales/Finance MVP

建立 SRM-lite、CRM-lite、OMS-lite、采购、销售、应收、应付、凭证和成本核算最小闭环。

### Slice 6. WMS Execution MVP

建立收货、入库、上架、出库、拣货、复核、盘点执行和 WCS adapter 边界。

### Slice 7. MES Execution MVP

建立工单、工序任务、报工、排程结果消费、完工入库请求和生产日报。

### Slice 7.5. Scheduling / APS Lite

建立排程输入输出契约、有限产能启发式调度内核、资源负载、冲突解释、锁定任务和急单插入能力。高级优化器、仿真、自动重排和求解器级 APS 后置。

### Slice 8. IndustrialTelemetry MVP

建立 tag 映射、设备状态、报警事件和时序摘要。

### Slice 9. Maintenance MVP

建立维修工单、保养计划、点检和停机原因，并消费 IndustrialTelemetry 报警事件。

### Slice 10. Full-Chain Acceptance

验证工程到制造、计划到采购/生产、采购到库存到应付、订单到交付到应收、生产执行到成本、设备到维护到产能、仓储自动化 adapter 七条链路。

## Issue Roadmap

GitHub issue 是实施跟踪容器，不改变 ADR 0012/0013/0014 与本文档冻结的领域边界。#78 是甘特/RFC 参考，只覆盖前端图形渲染与交互；后端 APS 调度内核由 #206 跟踪，设备 IIoT 运行事实与 APS/MES 联动由 #207 跟踪。

| Slice / 能力 | GitHub 跟踪 | 当前处理 |
| --- | --- | --- |
| 平台补齐与前置能力 | #70、#71、#141、#142、#143 | #70/#71/#141/#143 已关闭；FileStorage metadata/local+tus hardening、业务控制台组件、事件可靠性、部署、安全和性能基线已形成当前平台底座。#142 保持开放，作为 MinIO/S3 multipart/object storage post-MVP 补强项。 |
| Slice 1. MasterData Foundation | #72 已关闭 | BusinessMasterData realignment 已落地并作为下游服务引用事实源；后续只承接跨服务接线。 |
| Slice 2. ProductEngineering MVP | #127 已关闭 | EngineeringDocument、EngineeringItem、EBOM、MBOM、Routing、ECO/ECN 与 ProductionVersion 已形成 MVP 事实面；后续只补真实跨服务验收和业务 UI。 |
| Slice 3. Common Capability Foundation | #73、#131、#132、#133、#134 已关闭 | Inventory、Quality inspection、BarcodeLabel、BusinessApproval 已拆分并落地；公共能力已纳入 #77 P0 验收口径，后续只做 hardening 与 UI 接线。 |
| Slice 4. DemandPlanning MVP | #128 已关闭 | MPS/MRP、pegging 与计划建议 MVP 已落地；计划建议 accept 后对 ERP/MES downstream reference 的链路已纳入 #77 P0 验收 evidence。 |
| Slice 5. ERP Procurement/Sales/Finance MVP | #76、#137、#138、#139 已关闭 | Procurement、Sales、Finance MVP 已接入 solution/AppHost/schema catalog/verify；完整总账月结、税务、银行与发布 bundle 后置。 |
| Slice 6. WMS Execution MVP | #75、#136 已关闭 | WMS 入库、出库、盘点和 WCS adapter 边界已落地；Inventory posting 已演进为公共 `Nerv.IIP.Contracts.Inventory` movement-requested / stock-movement-posted 异步闭环，WCS 可观测事实由 public-surface focused tests 继续覆盖。 |
| Slice 7. MES Execution MVP | #74、#135 已关闭；#194 开放 | MES 已从 in-memory Web 原型迁移到 CleanDDD Domain/Infrastructure/PostgreSQL；生产报工、完工入库请求和维护产能影响查询已纳入 #77 P0 支撑 surface。#194 继续补工单释放快照与执行生命周期。 |
| Slice 7.5. Scheduling / APS Lite | #206 开放 | APS 不再完全后置；P0 先做调度内核、排程契约、资源负载和冲突解释。#78 甘特图只消费排程输出，不承担算法。 |
| Slice 8. IndustrialTelemetry MVP | #129 已关闭；#207 开放 | 设备状态、报警和摘要事实已进入独立服务，PLC/DCS/SCADA 继续保持外部系统边界。#207 继续补设备 IIoT 运行事实到 APS/MES readiness 的联动。 |
| Slice 9. Maintenance MVP | #130 已关闭 | 维修工单、保养、点检、停机事实已落地，报警触发维修工单消费 #129 公共契约。 |
| Slice 10. Full-Chain Acceptance | #77 P0 收口、#140 已关闭 | #140 已关闭；#77 P0 通过 governed verify 覆盖 WMS public-surface/event contract、MES/ERP 支撑 surface 与七条链路 acceptance evidence。真实 PostgreSQL/RabbitMQ/外部设备联调作为后续 hardening 扩展。 |
| MES Operational Foundation Reset | #188 到 #207 开放 | 围绕汽车减振器 P0 场景重新排序：自动编号、主数据、工程资料、MRP、采购供应、库存/WMS 齐套、MES 生命周期、质量设备 readiness、PC 工作流、APS lite、设备 IIoT 联动。 |

Wave 1 handoff 入口是 `docs/superpowers/specs/2026-05-23-business-wave-1-agent-session-design.md`，对应 #127、#131、#132、#135 和 #140，当前已完成。Wave 2 handoff 入口是 `docs/superpowers/specs/2026-05-23-business-wave-2-agent-session-design.md`，对应 #128、#133、#134 和 #136，当前已完成。Equipment Reliability closure 记录见 `docs/superpowers/specs/2026-05-23-business-wave-2-5-equipment-reliability-closure.md`，对应 #129 和 #130，当前已完成。

Wave 3 ERP 已落地并接入 solution/AppHost/IAM/schema catalog/verify scripts，代码事实覆盖 Procurement、Sales 和 Finance MVP。Wave 4 的 #77 P0 收口入口是 `docs/superpowers/specs/2026-05-23-business-full-chain-acceptance-design.md` 和 `docs/superpowers/plans/2026-05-23-business-full-chain-acceptance.md`；2026-05-27 起，后续主线转向 #188 到 #207 的 MES operational foundation reset，其中 #206/#207 分别负责 APS lite 和设备 IIoT 联动。#142 对象存储、PDA/mobile、事件可靠性 hardening 与部署发布深化继续作为并行专题推进。

## 开放问题

| 问题 | 影响 | 当前处理 |
| --- | --- | --- |
| CPQ 是否进入首批 | 影响报价、BOM 变体和按单设计链路 | 默认不进；如产品强配置化，再单独建 CPQ spec。 |
| OMS 是否独立 | 影响多渠道、拆单合单和履约路由 | 默认不独立；ERP Sales + WMS fulfillment 先覆盖。 |
| WCS 是否独立 | 取决于是否存在自动化仓 | 首批只做 adapter 边界。 |
| MRP 时间粒度 | 影响计划算法、性能和数据量 | MRP 首批按日粒度；班次/小时级约束由 Scheduling/APS lite 在排程窗口内处理。 |
| APS 优化深度 | 影响算法复杂度、可解释性和交付周期 | P0 只做确定性启发式有限产能排程；全局最优、仿真和自动重排后置。 |
| IIoT 时序存储 | 影响数据量和查询性能 | P0 保存摘要、状态快照、报警和关键事件；高频原始时序进入后续 historian/profile。 |
| 财务入账时点 | 影响应收应付和成本核算时点 | 默认以业务单据和库存事实组合校验，实施计划中冻结。 |
