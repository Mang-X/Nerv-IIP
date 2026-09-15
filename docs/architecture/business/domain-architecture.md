# 业务平台领域架构

本文只描述 Nerv-IIP 业务平台的**当前领域分层、事实所有权、依赖方向、关键业务链路和平台边界**。实施进度、issue/PR/CI、阶段计划和历史形成过程不属于 Current Architecture；M2-L 清理前的混合正文冻结于 [`../../reports/m2-l-business-platform-domain-architecture-pre-clean-2026-09-07.md`](../../reports/m2-l-business-platform-domain-architecture-pre-clean-2026-09-07.md)。

长期取舍由 [ADR 0012](../../adr/0012-business-platform-domain-layering.md)、[ADR 0013](../../adr/0013-business-master-data-governance.md)、[ADR 0014](../../adr/0014-aps-and-iiot-scheduling-boundary.md) 与 [ADR 0017](../../adr/0017-business-process-manager-and-compensation-strategy.md) 约束；精确 endpoint、schema、permission、事件 payload 与运行配置以当前代码、公开契约、迁移和配置 producer 为准。

## 架构原则

1. 业务规划按“链路事实源”而不是系统名清单组织；同一业务事实只能有一个权威 owner。
2. 业务服务之间不共享数据库表、不建立跨 schema 外键；跨域读取通过公开 resolve/query 契约、事件投影或 Gateway 聚合完成。
3. 依赖方向从高层业务过程域指向低层能力域、主数据/工程域或平台公开能力；消费关系不转移事实所有权。
4. procure-to-pay 与 order-to-cash 当前采用 choreography、服务本地幂等/DLQ/replay 和拥有服务发布的补偿事实，不引入中心 saga/process-manager。
5. BusinessGateway 是前端 BFF/facade，不拥有领域事实，也不把多个领域聚合成新的写入 owner。
6. PLC/DCS/SCADA/WCS/CAD 等现场或工程系统保持外部边界，通过 Connector Host、File Storage 或受控 adapter 接入。

## 业务分层

```text
外部工程与现场系统
  CAD / SCADA / PLC / DCS / WCS / AGV / AMR
    -> Connector Host / File Storage / Platform SDK

Layer 3：订单、采购、财务与履约入口
  ERP Procurement：SRM-lite、采购申请、询价、采购订单、收货、退货
  ERP Sales：CRM-lite、报价、销售订单、发货、退货、OMS-lite
  ERP Finance：应收、应付、凭证、成本核算

Layer 2：计划与执行
  DemandPlanning：MPS、MRP、计划采购建议、计划工单建议、pegging
  Scheduling / APS lite：排程问题、排程方案、有限产能分配、资源负载、冲突解释
  MES：工单、工序、报工、排程结果消费、完工入库请求
  WMS：收货、入库、上架、出库、拣货、复核、盘点执行、WCS adapter
  Maintenance：维修工单、保养计划、点检、故障、停机原因、备件需求

Layer 1：通用业务能力与工业数据
  Inventory：库存台账、库位、批次、序列号、库存移动、盘点调整
  Quality：检验标准、检验计划、检验记录、不合格处置
  BarcodeLabel：条码规则、标签模板、标签打印、扫码与追溯事实
  BusinessApproval：业务审批模板、审批链、审批记录
  IndustrialTelemetry：tag 映射、采集点、设备状态、报警、时序摘要、OEE 输入

Layer 0：主数据与产品工程
  BusinessMasterData：SKU/UOM、业务伙伴、组织业务属性、资源、工作中心、日历、设备资产、参考定义
  ProductEngineering：工程物料、CAD 引用、EBOM、MBOM、工艺路线、ProductionVersion、ECO/ECN、版本发布
```

MasterData 字段与事实 owner 见 [`master-data-field-ownership.md`](master-data-field-ownership.md)，流程制造补充见 [`master-data-process-manufacturing.md`](master-data-process-manufacturing.md)。

## 服务与事实所有权

| 上下文 | 拥有事实 | 明确不拥有 | 主要依赖/输入 |
| --- | --- | --- | --- |
| BusinessMasterData | SKU/UOM、业务伙伴、业务组织属性、资源、工作中心、日历、设备资产、跨域 ReferenceData | EBOM/MBOM、IAM 角色、实时采集、库存余额 | IAM/File Storage 引用 |
| ProductEngineering | CAD 引用、工程物料、EBOM/MBOM、Routing、ProductionVersion、ECO/ECN、版本发布 | CAD 设计内容、库存、工单、采购订单 | MasterData、File Storage |
| DemandPlanning | DemandSource、Forecast、MPS/MRP、计划建议、pegging、净需求 | 正式采购订单、正式工单、库存余额 | ProductEngineering、Inventory、ERP/MES 来源事实 |
| Scheduling / APS lite | SchedulingProblem、SchedulePlan、资源负载、冲突、锁定任务、排程版本 | MRP 需求、工单执行、库存、报警、维修工单 | MasterData/ProductEngineering 静态事实；Planning/MES 候选；运行约束投影 |
| Inventory | 库存台账、库位、批次/序列实例、库存移动、盘点任务/调整 | WMS 执行步骤、采购/销售/工单状态 | MasterData、Quality、Approval |
| Quality | 检验标准/计划/记录、NCR、处置与放行决策 | 库存余额、采购销售单据、仓储任务 | MasterData、Inventory、File Storage |
| BarcodeLabel | 条码规则、模板引用、打印批次/传输事实、扫码记录、追溯事件 | 库存余额、业务单据状态、物理打印确认之外的设备事实 | MasterData、Inventory、File Storage |
| BusinessApproval | 审批模板、审批链、审批记录、业务审批状态 | 平台 Ops 任务、平台审计 | IAM、Notification |
| ERP | 采购/SRM-lite、销售/CRM-lite/OMS-lite、应收应付、凭证、成本核算 | WMS 执行、库存余额 | MasterData、Planning、Inventory、WMS、MES |
| WMS | 收货/入库/出库、拣货、上架、复核、盘点执行、WCS 任务映射 | 库存余额、采购/销售/工单业务状态、WCS 内部调度 | MasterData、Inventory、Quality、BarcodeLabel、ERP 采购收货来源 |
| MES | 工单、工序任务、报工、排产结果消费、完工入库请求、执行侧不可用投影 | 库存余额、设备维护事实、排程算法 | MasterData、ProductEngineering、Planning、Inventory/WMS、Quality、Telemetry、Maintenance |
| IndustrialTelemetry | tag/采集点、设备状态、报警、时序摘要、OEE 输入事实 | PLC/DCS 控制、资产主数据、维修处置 | Connector Host、MasterData |
| Maintenance | 维修工单、保养计划、点检、故障、停机原因、资产恢复判定、备件需求事实 | 设备主数据、库存余额、MES 工单 | MasterData、Telemetry、Inventory、MES |
| BusinessGateway | 页面级聚合、身份/权限上下文透传、前端 OpenAPI facade | 任一领域持久事实或领域状态机 | IAM 与公开业务契约 |

## 关键业务链路

### 工程到制造

```text
CAD / design package
  -> ProductEngineering EngineeringItem / EBOM
  -> ECO/ECN
  -> released MBOM + Routing
  -> ProductionVersion resolve
  -> DemandPlanning MRP
  -> MES WorkOrder
```

ProductEngineering 拥有 EBOM、MBOM、Routing、ProductionVersion 和工程变更；MasterData 继续拥有 SKU/material identity 与静态资源事实。

### 销售订单到计划

销售订单事实由 ERP 拥有；DemandPlanning 只投影版本化的订单需求行与消费水位。收敛规则见 [`sales-order-to-demand-planning.md`](sales-order-to-demand-planning.md)。MRP、pegging 与计划工单沿用稳定来源引用，不复制 ERP 订单详情。

### 采购到库存到应付

```text
PlannedPurchaseSuggestion
  -> ERP PurchaseRequisition / RFQ / PurchaseOrder
  -> ERP PurchaseReceipt + GR/IR
  -> Quality inspection
  -> WMS inbound execution
  -> Inventory movement
  -> ERP SupplierInvoice / AP / voucher
```

ERP 拥有采购和财务事实，WMS 拥有仓储执行，Inventory 拥有库存过账，Quality 拥有放行。退货会计补偿边界见 [`erp-return-accounting.md`](erp-return-accounting.md)。

采购收货的库存过账路径由 ERP 在收货时冻结：`Direct` 由 ERP 发起库存请求，`Wms` 由 WMS 入库执行发起。WMS 在采购来源入库首次完成、创建库存请求之前，通过 ERP 公开来源查询核对同一组织、环境和收货单的冻结路径；仅 `Wms` 放行，直接路径（含历史直接收货）、不存在的来源与查询失败均不能生成新请求。已有请求的匹配重放继续返回原请求身份，不借此修补历史库存事实。

WMS 的 ERP HTTP 客户端使用 `Erp:BaseUrl`（环境变量 `Erp__BaseUrl`）；Aspire AppHost 从 ERP HTTP endpoint 注入地址和资源引用，不增加反向启动等待。精确配置注册以 WMS `Program.cs` 与 AppHost 为准。

### 订单到交付到应收

```text
ERP Opportunity / Quotation / SalesOrder
  -> WMS outbound / pick / pack
  -> Inventory movement
  -> ERP AccountReceivable / voucher
```

CRM-lite 与 OMS-lite 属于 ERP Sales / WMS fulfillment 子域；多渠道拆单或独立 CPQ 不是这些事实 owner 的前置条件。

### 生产执行到成本

MES 拥有工单、工序和报工；Scheduling 拥有排程；Inventory 拥有库存过账；ERP Finance 消费报工、消耗和库存结果形成成本事实。

### 设备到维护到产能

```text
PLC/DCS/SCADA
  -> Connector Host
  -> IndustrialTelemetry state / alarm
  -> Maintenance work order / availability
  -> MES work-center availability projection
  -> Scheduling resource availability
```

完整事件边界见 [`equipment-status-event-flow.md`](equipment-status-event-flow.md)。Scheduling 不直接解释原始 PLC/SCADA 数据或修改 Maintenance/MES 事实。

### 仓储自动化

WMS 通过 adapter 向外部 WCS/输送线/ASRS/AGV/AMR 发起受控任务，并消费回执；WCS 内部调度不是 WMS 的领域事实。库存最终过账仍由 Inventory 拥有。

## 集成事件边界

事件遵循平台公共 envelope，表达已经发生的事实而不是远程命令。主要 owner 关系如下：

- MasterData 发布 SKU/UOM/Partner/Resource/Calendar/Device/ReferenceData 变化事实。
- ProductEngineering 发布 BOM/Routing/ProductionVersion/EngineeringChange 发布事实。
- ERP 发布销售/采购/财务生命周期事实；销售订单到 Planning 的版本收敛由专页定义。
- DemandPlanning 发布 MRP 完成和计划建议事实。
- Scheduling 发布计划生成、冲突和发布事实。
- WMS 发布入出库/盘点/WCS 执行事实；Inventory 发布库存移动与可用性事实。
- MES 发布工单、报工、完工入库请求和停机事实。
- IndustrialTelemetry 发布设备状态/报警事实；Maintenance 发布资产不可用/恢复与维护事实。
- Quality 发布检验、NCR 与处置事实；BusinessApproval 发布审批结果。

事件 payload 不承载 token、密码、对象存储 key、PLC 控制指令或大体积附件/时序数据。跨域消费者维护本地 inbox/idempotency 与必要投影，不因消费事件获得 source domain 的写入权。

## 平台边界

### IAM 与业务组织

IAM 拥有 organization/environment/user、角色、权限和授权 scope；BusinessMasterData 只拥有部门、班组、Worker、技能/资质等业务组织事实。业务服务不得复制 IAM 角色/权限或直接读取 IAM 表。

### AppHub / Connector Host / IndustrialTelemetry

AppHub 拥有受管应用/实例/节点/能力/心跳；Connector Host 负责本地资源发现与协议适配；IndustrialTelemetry 只拥有受控接入后的工业数据事实，不控制现场系统。

### File Storage

CAD、图纸、工艺文件、质检附件、维修照片和业务附件由 File Storage 管理。业务域保存 fileId/FileReference 与自己的版本/关系，不拥有对象存储 key 或预签名 URL。

### Ops 与业务审批

Ops 处理平台运维动作；BusinessApproval 处理业务单据审批。两者的任务、审计与权限边界不能合并。

## BusinessGateway 与前端边界

BusinessGateway 暴露业务页面 facade，负责用户认证、IAM 权限校验、organization/environment 上下文透传、internal service identity 和 OpenAPI 输出；它不持久化领域事实、不计算领域状态机，也不直接引用业务服务 Domain/Infrastructure 项目。

Business Console 可以按角色任务聚合多个服务事实，但“页面聚合”不改变后端 owner。前端 workspace 见 [`../frontend/workspace-structure.md`](../frontend/workspace-structure.md)，产品导航/IA 由 `docs/product/` 维护；Architecture 不记录菜单交付进度或 issue 状态。
