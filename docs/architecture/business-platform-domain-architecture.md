# 业务平台领域架构

本文档定义 Nerv-IIP 业务平台的关键链路、服务分层、事实源、外部系统边界和首批实施顺序。它承接 ADR 0012，并约束后续业务平台 spec、实现计划和 CleanDDD 落地。

## 结论

原始 issues #72 到 #77 对 MES/WMS/ERP 主干判断是合理的，但不足以覆盖工业业务关键链路。业务平台规划需要从“系统名清单”转成“链路事实源”：

1. 工程到制造：CAD/PDM/PLM → EBOM/MBOM/工艺路线 → 工程变更 → MES/MRP。
2. 计划到采购/生产：销售订单/预测 → MPS/MRP → 计划采购建议/计划工单建议。
3. 采购到库存到应付：采购申请/订单 → 收货/质检 → WMS 入库 → Inventory → 应付。
4. 订单到交付到应收：商机/报价/销售订单 → 发货履约 → WMS 出库 → Inventory → 应收。
5. 设备到维护到产能：PLC/DCS/SCADA/IIoT → 设备状态/报警/OEE → CMMS → MES 排产约束。
6. 仓储自动化：WMS → WCS adapter → 自动化设备回执 → WMS → Inventory。

首批必须纳入规划的是 ProductEngineering、DemandPlanning、IndustrialTelemetry、Maintenance。CRM、SRM、CPQ、OMS、WCS、CAD、SCADA、PLC/DCS 不全部独立建设，而是按子域、外部系统或升级边界处理。

## 系统取舍

| 名称 | 首批处理 | 原因 |
| --- | --- | --- |
| PDM/PLM | 建 ProductEngineering lite | EBOM、MBOM、工艺路线、ECO/ECN 是制造和 MRP 的前置事实。 |
| CAD | 外部系统/文件来源 | 平台只保存 CAD 文件引用、版本、预览和发布关系，不实现 CAD 设计能力。 |
| MPS/MRP | 建 DemandPlanning lite | 采购建议、工单建议和物料需求数量必须有计划来源。 |
| APS | 后置 | 首批规则排产即可；高级约束优化等 MRP/MES 稳定后再做。 |
| IIoT | 建 IndustrialTelemetry lite | tag、报警、设备状态、OEE 输入是设备链路的基础事实。 |
| PLC/DCS/SCADA | 外部系统/Connector 来源 | 通过 Connector Host 接入，不在业务平台内实现控制系统。 |
| CMMS | 建 Maintenance lite | 维修、保养、点检、停机原因会影响设备可用性和排产。 |
| EAM | 后置 | 首批 CMMS 覆盖维护；资产财务、折旧、全生命周期后置。 |
| SRM | ERP Procurement 子域 | 首批做供应商、询价/报价、采购协同最小能力；供应商门户后置。 |
| CRM | ERP Sales 子域 | 首批做客户、商机、报价、销售订单；营销和线索池后置。 |
| CPQ | 预留边界 | 只有配置型产品、按单设计或复杂报价规则成立时独立。 |
| OMS | ERP Sales + WMS fulfillment 子域 | 首批销售订单到出库履约即可；多渠道拆单、履约路由后置。 |
| WCS | WMS adapter 边界 | 没有自动化仓时不独立；有立库、输送线、AGV/AMR 时再实现。 |

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
  MES：工单、工序、报工、规则排产、完工入库请求
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
  ProductEngineering：工程物料、CAD 引用、EBOM、MBOM、工艺路线、ECO/ECN、发布状态
```

依赖方向只能从高层业务过程域指向低层能力域或主平台公开能力。业务服务之间不共享数据库表，不建跨 schema 外键。

BusinessMasterData 的治理和字段口径见 `docs/adr/0013-business-master-data-governance.md`、`docs/architecture/business-master-data-field-matrix.md` 和 `docs/architecture/business-master-data-process-manufacturing-supplement.md`。MasterData Foundation 的原始计划是最小骨架；继续暴露 API 或让下游依赖前，必须先完成 `docs/superpowers/plans/2026-05-21-business-master-data-realignment.md`。

## 服务与事实源

| 服务/上下文 | 建议落点 | 默认 schema | 拥有事实 | 不拥有事实 | 主要依赖 |
| --- | --- | --- | --- | --- | --- |
| BusinessMasterData | `backend/services/Business/MasterData` | `business_masterdata` | SKU、客户、供应商、承运商、业务部门、班组、人员业务属性、工作中心、工作日历、设备资产 | EBOM/MBOM 发布、IAM 用户角色、AppHub 实例、实时采集数据 | IAM、File Storage |
| ProductEngineering | `backend/services/Business/ProductEngineering` | `product_engineering` | CAD 文件引用、工程物料、EBOM、MBOM、工艺路线版本、ECO、ECN、发布记录 | CAD 设计内容、库存、工单、采购订单 | MasterData、File Storage、Notification |
| DemandPlanning | `backend/services/Business/DemandPlanning` | `demand_planning` | 需求来源、MPS、MRP run、计划采购建议、计划工单建议、pegging、净需求 | 正式采购订单、正式工单、库存余额 | ProductEngineering、Inventory、ERP、MES |
| Inventory | `backend/services/Business/Inventory` | `inventory` | 库存台账、库位、批次、序列号、库存移动、盘点任务与调整 | 仓储执行步骤、采购/销售/工单状态 | MasterData、Quality、Approval |
| Quality | `backend/services/Business/Quality` | `quality` | 检验标准、检验计划、检验记录、NonconformanceReport 不合格品报告与处置闭环 | 库存余额、仓储作业状态、采购销售单据；处置后续动作只发布集成事件 | MasterData、Inventory、File Storage |
| BarcodeLabel | `backend/services/Business/BarcodeLabel` | `barcode` | 条码规则、标签模板、标签打印批次、扫码记录 | 库存余额、业务单据状态 | MasterData、Inventory、File Storage |
| BusinessApproval | `backend/services/Business/Approval` | `business_approval` | 审批模板、审批链、审批记录、业务审批状态 | Ops 运维任务、平台审计事实 | IAM、Notification |
| ERP | `backend/services/Business/Erp` | `erp` | 采购、SRM-lite、销售、CRM-lite、OMS-lite、应收、应付、凭证、成本核算 | WMS 执行步骤、库存余额、完整总账月结 | MasterData、Planning、Inventory、WMS、MES |
| WMS | `backend/services/Business/Wms` | `wms` | 收货通知、入库单、出库单、拣货、上架、复核包装、盘点执行、WCS 任务映射 | 库存余额、采购/销售/工单业务状态、WCS 内部调度 | MasterData、Inventory、Quality、BarcodeLabel |
| MES | `backend/services/Business/Mes` | `mes` | 工单、工序任务、报工、排产结果、完工入库请求、生产日报 | 库存余额、WMS 入库单、设备维护事实 | ProductEngineering、Planning、Inventory、WMS、Quality、Telemetry、Maintenance |
| IndustrialTelemetry | `backend/services/Business/IndustrialTelemetry` | `industrial_telemetry` | tag 定义、采集点映射、设备状态快照、时序摘要、报警事件、OEE 输入事实 | PLC/DCS 控制、SCADA 画面、设备资产主数据 | Connector Host、MasterData、AppHub |
| Maintenance | `backend/services/Business/Maintenance` | `maintenance` | 维修工单、保养计划、点检记录、故障、停机原因、备件需求 | 设备资产主数据、库存余额、生产工单状态 | MasterData、Telemetry、Inventory、MES |
| BusinessGateway | `backend/gateway/BusinessGateway` 或业务服务内部 BFF | 无持久化默认值 | 业务页面聚合查询、业务前端 OpenAPI、上下文透传 | 领域规则、持久事实 | 业务服务 OpenAPI/Contracts、IAM |

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
  -> DemandPlanning.MRP
  -> MES.WorkOrder
```

ProductEngineering 是 EBOM、MBOM、工艺路线版本和工程变更事实源。MES 与 MRP 只能引用已发布版本。

### 计划到采购/生产

```text
ERP SalesOrder / Forecast
  -> DemandPlanning.MPS
  -> DemandPlanning.MRP run
  -> PlannedPurchaseSuggestion -> ERP.PurchaseRequisition
  -> PlannedWorkOrderSuggestion -> MES.WorkOrder
```

DemandPlanning 生成计划建议，不直接创建正式采购订单、正式工单或库存移动。ERP/MES 接受建议后创建正式业务单据。

### 采购到库存到应付

```text
DemandPlanning.PlannedPurchaseSuggestion
  -> ERP.PurchaseRequisition
  -> ERP.RFQ / SupplierQuotation (SRM-lite)
  -> ERP.PurchaseOrder
  -> ERP.PurchaseReceipt
  -> Quality.InspectionPlan
  -> WMS.InboundOrder
  -> Inventory.StockMovement
  -> ERP.AccountPayable
```

SRM-lite 首批只处理供应商、询价、报价和采购协同最小流程，不做完整供应商门户。

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
```

CRM-lite 与 OMS-lite 首批由 ERP Sales 和 WMS fulfillment 承担。CPQ 只在配置型产品场景进入独立规划。

### 生产执行到成本

```text
MES.WorkOrder
  -> MES.ScheduleResult
  -> MES.OperationTask
  -> MES.ProductionReport
  -> Quality.OperationInspection
  -> MES.FinishedGoodsReceiptRequest
  -> WMS.InboundOrder
  -> Inventory.StockMovement
  -> ERP.CostCalculation
```

MES 拥有工单、工序和报工事实。Inventory 拥有库存入账事实。ERP Finance 消费报工、物料消耗和库存入账结果生成成本。

### 设备到维护到产能

```text
PLC/DCS/SCADA data
  -> Connector Host
  -> IndustrialTelemetry.TagSample / AlarmEvent / DeviceStateSnapshot
  -> Maintenance.WorkOrder / MaintenancePlan
  -> MES downtime / OEE
  -> DemandPlanning capacity adjustment
```

IndustrialTelemetry 不控制现场；Maintenance 不拥有设备主数据；MES 不直接改维修事实。

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
| ProductEngineering | `productEngineering.BomReleased`、`productEngineering.RoutingReleased`、`productEngineering.EngineeringChangeReleased` | DemandPlanning、MES、ERP | 工程版本和变更发布。 |
| DemandPlanning | `demandPlanning.MrpRunCompleted`、`demandPlanning.PlannedPurchaseSuggested`、`demandPlanning.PlannedWorkOrderSuggested` | ERP、MES、Notification | 计划建议生成。 |
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
3. `business.inventory.movements.create`
4. `business.iiot.telemetry.read`
5. `business.maintenance.work-orders.manage`
6. `business.wms.receipts.manage`
7. `business.mes.work-orders.manage`
8. `business.erp.finance.read`

权限码必须在 `docs/architecture/authorization-matrix.md` 登记后，才能进入 IAM seed、Endpoint 鉴权和测试。

## 首批实施顺序

### Slice 0. 文档冻结

ADR 0012、本架构文档、业务 spec、README/repo layout/权限矩阵入口完成。

### Slice 1. MasterData Foundation

建立 SKU、业务伙伴、组织业务属性、工作中心、工作日历、设备资产和资源主数据。

2026-05-21 起，Slice 1 继续实施前必须先完成 MasterData realignment：补齐 UOM/换算、SKU 工业属性、资源层级、设备静态能力、流程型制造边界、下游 resolve 契约和 MasterData 变更事件。该步骤不把 Recipe/Formula、批次实例、库存余额、检验记录、批生产记录或实时采集数据移入 MasterData。

### Slice 2. ProductEngineering MVP

建立 CAD 文件引用、工程物料、EBOM、MBOM、工艺路线版本、ECO/ECN 和发布状态。

### Slice 3. Common Capability Foundation

建立 Inventory、Quality、BarcodeLabel、BusinessApproval 的最小能力，尤其是库存移动唯一事实源和业务审批链。

### Slice 4. DemandPlanning MVP

建立需求来源、MPS、MRP run、净需求、计划采购建议、计划工单建议和 pegging。

### Slice 5. ERP Procurement/Sales/Finance MVP

建立 SRM-lite、CRM-lite、OMS-lite、采购、销售、应收、应付、凭证和成本核算最小闭环。

### Slice 6. WMS Execution MVP

建立收货、入库、上架、出库、拣货、复核、盘点执行和 WCS adapter 边界。

### Slice 7. MES Execution MVP

建立工单、工序任务、报工、规则排产、完工入库请求和生产日报。

### Slice 8. IndustrialTelemetry MVP

建立 tag 映射、设备状态、报警事件和时序摘要。

### Slice 9. Maintenance MVP

建立维修工单、保养计划、点检和停机原因，并消费 IndustrialTelemetry 报警事件。

### Slice 10. Full-Chain Acceptance

验证工程到制造、计划到采购/生产、采购到库存到应付、订单到交付到应收、生产执行到成本、设备到维护到产能、仓储自动化 adapter 七条链路。

## 开放问题

| 问题 | 影响 | 当前处理 |
| --- | --- | --- |
| CPQ 是否进入首批 | 影响报价、BOM 变体和按单设计链路 | 默认不进；如产品强配置化，再单独建 CPQ spec。 |
| OMS 是否独立 | 影响多渠道、拆单合单和履约路由 | 默认不独立；ERP Sales + WMS fulfillment 先覆盖。 |
| WCS 是否独立 | 取决于是否存在自动化仓 | 首批只做 adapter 边界。 |
| MRP 时间粒度 | 影响计划算法、性能和数据量 | 首批按日粒度，班次/小时级后置。 |
| IIoT 时序存储 | 影响数据量和查询性能 | 首批只保存摘要和关键事件；高频原始时序进入后续 profile。 |
| 财务入账时点 | 影响应收应付和成本核算时点 | 默认以业务单据和库存事实组合校验，实施计划中冻结。 |
