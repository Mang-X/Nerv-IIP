# 业务模块交付清单

代码事实复核日期：2026-07-01。

本文用于下一步业务排期。它按当前代码事实列出业务模块在后端、BusinessGateway、PC Business Console、PDA/mobile 和大屏/看板上的完成与缺口，不替代 `docs/architecture/implementation-readiness.md` 的全局状态，也不替代 OpenAPI 或测试报告。

## 口径

- `[x]` 表示当前仓库中可以找到对应服务、facade、路由、页面、组件或测试事实。
- `[ ]` 表示当前仓库中未找到正式交付物，或只有设计系统/占位/诊断能力，不能作为业务完成声明。
- “后端已完成”只表示领域服务、API、持久化或事件闭环已存在，不代表 PC、PDA 或大屏已经可用。
- “PC 已完成”只表示 Business Console 有正式路由页面和 facade 消费事实，不代表该业务域已经达到完整 ERP/MES/WMS/CMMS 产品深度。
- “PDA 已完成”只表示 `frontend/apps/business-pda` 中有移动作业页、composable 或任务入口，不代表离线、扫码解析、设备绑定和个人任务已经完成。
- “大屏已完成”只认可业务大屏应用或 live data 页面。`frontend/apps/design-system` 中的 `board.vue` 和 screen 组件文档只能算设计/组件样例。

## 代码事实来源

- 后端业务服务：`backend/services/Business/*`
- BusinessGateway facade：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/*`
- PC Business Console：`frontend/apps/business-console/src/navigation.ts` 和 `frontend/apps/business-console/src/pages/*`
- Business PDA：`frontend/apps/business-pda/src/pages/*`、`frontend/apps/business-pda/src/composables/*`、`frontend/packages/business-core/src/tasks/pdaTaskKinds.ts`
- 大屏/设计系统：`frontend/apps/design-system/src/pages/board.vue`、`frontend/apps/design-system/docs/components/screen/*`、`frontend/packages/ui/src/components/screen/*`
- 当前没有 `frontend/apps/business-board`，也没有 `frontend/apps/business-console/src/pages/barcode` 或 `frontend/apps/business-pda/src/pages/scan.vue`；Business Console 已有 `frontend/apps/business-console/src/pages/approval`。

## 总览

| 模块 | 后端服务 | BusinessGateway | PC Business Console | PDA/mobile | 大屏/看板 | 下一步缺口 |
| --- | --- | --- | --- | --- | --- | --- |
| MasterData 主数据 | [x] | [x] | [x] | [ ] | [ ] | PDA 字典/bootstrap、批量导入导出、主数据变更影响分析、角色化主数据工作台。 |
| ProductEngineering 产品工程 | [x] | [x] | [x] | [ ] | [ ] | 工程对象详情/版本对比/变更影响、文档预览与 FileStorage 深集成、移动端只读准备信息。 |
| DemandPlanning 需求/MRP | [x] | [x] | [x] 单页 | [ ] | [ ] | MRP 运行详情、pegging 可视化、建议接受后的跟踪、计划员专用工作台。 |
| Scheduling / APS lite | [x] | [x] | [x] MES 规则排程页，[ ] 独立 APS 工作台 | [ ] | [ ] | 独立 APS/Gantt 页面、重排/RFC、排程版本对比、生产大屏甘特。 |
| Inventory 库存 | [x] | [x] | [x] | [ ] 独立库存 PDA | [ ] | 批次/序列号/冻结/预留分析、WMS 作业页内库存上下文深化、移动盘点与库存 PDA 边界。 |
| Quality 质量 | [x] | [x] | [x] | [ ] 独立质量 PDA | [ ] | 移动检验、CAPA 深化、质量趋势/供应商质量分析、NCR 到返工/库存冻结穿透。 |
| MES 制造执行 | [x] | [x] | [x] | [x] 部分一线作业 | [ ] | PC 详情深度、真实个人任务、扫码直达、离线报工、生产指挥大屏。 |
| WMS 仓储作业 | [x] | [x] | [x] | [x] 部分一线作业 | [ ] | FEFO/FIFO、ASN 差异、directed putaway、LPN/HU、扫描解析与离线作业。 |
| ERP 经营管理 | [x] | [x] | [x] 采购/销售/财务窄面 | [ ] | [ ] | 完整采购/销售/财务菜单、税务/银行/月结/报表、退货/RMA、移动审批/收货协同。 |
| IndustrialTelemetry 设备监控 | [x] | [x] | [x] 设备看板/报警，[ ] 规则/OEE 正式页面 | [x] 报警查看 | [ ] | tag/rule/OEE 配置页、实时趋势、报警处理闭环、OEE/停机大屏。 |
| Maintenance 设备运维 | [x] | [x] | [x] 工单/计划 | [x] 报修/点检 | [ ] | 完整 CMMS 资产视角、备件成本、移动离线点检、维修绩效分析。 |
| BarcodeLabel 条码标签 | [x] | [x] | [ ] | [ ] | [ ] | PC 标签规则/模板/打印批次/扫码记录页，PDA `/scan` 解析和条码业务直达。 |
| BusinessApproval 审批中心 | [x] | [x] | [x] | [ ] | [ ] | PC 审批中心已接入模板、流程、任务、决策和委托；移动审批、工作台待办深度整合和跨域单据详情穿透仍后续。 |
| Search / Workbench 工作台 | [x] facade | [x] | [x] 首页，[ ] Cmd/Ctrl+K 实装 | [x] 首页壳，[ ] 我的任务真实数据 | [ ] | 全局对象搜索面板、近期/星标、角色化待办、跨域指标和预警聚合。 |

## 后端清单

### MasterData

- [x] 服务存在：`backend/services/Business/MasterData`
- [x] 覆盖 SKU、UOM、UOM conversion、BusinessPartner、Site、Workshop、ProductionLine、WorkCenter、DeviceAsset、Department、Team、TeamMember、Shift、WorkCalendar、ReferenceData、ProductCategory、Skill、PersonnelSkill、CodeRule。
- [x] 支持资源 list/detail/update/enable/disable，以及 reference resolve/validate。
- [x] 有 schema migration、schema convention tests、IAM seed/catalog 和 AppHost 接入事实。
- [x] BusinessGateway 暴露 MasterData typed list、资源 lifecycle、组织/资源/技能/编码规则 facade。
- [ ] 还缺面向业务运营的大批量导入/导出、变更影响分析、跨模块引用可视化和移动端字典 bootstrap/delta。

### ProductEngineering

- [x] 服务存在：`backend/services/Business/ProductEngineering`
- [x] 覆盖 EngineeringDocument、EngineeringItem、EBOM、MBOM、Routing、StandardOperation、ProductionVersion、ECO/ECN。
- [x] StandardOperation、ProductionVersion、release/归档/校验链路已有 Web/API contract 和测试事实。
- [x] BusinessGateway 暴露 documents、items、EBOM、MBOM、routing、standard operations、production versions、ECO facade。
- [ ] 还缺工程对象详情深度、版本 diff、ECO future effective 切换、文档预览/签审和 FileStorage 深度产品化。

### DemandPlanning

- [x] 服务存在：`backend/services/Business/DemandPlanning`
- [x] 覆盖 demand source、MPS/MRP run、pegging、planning suggestions、suggestion accept。
- [x] 已消费上游 SKU/工程/ERP/MES/WMS 相关事实作为 MRP 输入的一部分。
- [x] BusinessGateway 暴露 planning facade。
- [ ] 还缺计划运行详情页所需的更细粒度解释、异常处理、计划版本对比和更完整的供应/需求快照。

### Scheduling / APS lite

- [x] 服务存在：`backend/services/Business/Scheduling`
- [x] 覆盖 deterministic finite-capacity heuristic、preview/create/list/detail/gantt/release。
- [x] 覆盖设备 availability provider、MES material readiness adapter、setup/changeover、技能/工装门禁、release feasibility gate、KPI metrics。
- [x] BusinessGateway 暴露 scheduling plans preview/create/list/detail/gantt/release。
- [ ] 不包含全局优化器、自动重排、人工拖拽调度、RFC 审批、模拟对比和生产大屏甘特。

### Inventory

- [x] 服务存在：`backend/services/Business/Inventory`
- [x] 覆盖 stock location、stock availability、stock movement、reservation/release、status transfer、stock count create/confirm/cancel。
- [x] 覆盖 Quality inspection result 到库存状态、WMS/MES movement request、posting failed/posted 反馈闭环。
- [x] BusinessGateway 暴露 availability、movements、counts facade。
- [ ] 还缺更完整的批次/序列号 UI 查询、库存冻结原因分析、预留分配分析、成本分析和移动端独立库存应用。

### Quality

- [x] 服务存在：`backend/services/Business/Quality`
- [x] 覆盖 inspection plan、inspection record、NCR、quality reason、corrective action。
- [x] 覆盖 NCR-from-inspection、defect raised、inspection result 与 Inventory/MES/ERP/WMS 的关联事实。
- [x] BusinessGateway 暴露 inspections、NCR、reason-codes facade。
- [ ] 还缺移动检验、CAPA 全流程产品化、供应商质量分析、质量趋势和质量到库存/MES 返工的完整前端穿透。

### MES

- [x] 服务存在：`backend/services/Business/Mes`
- [x] 覆盖 production plan/work order、dispatch、material readiness、operation task、production report、finished goods receipt、quality hold、defect、downtime/capacity、schedule result、handover、traceability、foundation readiness。
- [x] 消费 Planning suggestion accepted、ProductionVersion created、Scheduling plan released、Quality inspection result、Inventory posting posted/failed、Maintenance asset events 等跨域事实。
- [x] BusinessGateway 暴露 MES workbench、plans、work orders、dispatch、materials、operation tasks、WIP、reports、receipts、quality、downtime、capacity、schedules、handovers、traceability、foundation facade。
- [ ] 还缺完整班组个人任务、扫码入口解析、离线报工、异常协同详情、生产指挥大屏和更完整的一线 UX 闭环。

### WMS

- [x] 服务存在：`backend/services/Business/Wms`
- [x] 覆盖 inbound order、putaway task、outbound order、picking task、warehouse task progress/complete、count execution、WCS task dispatch/fail/complete、Inventory posting retry。
- [x] 消费 ERP outbound/inbound、Inventory posted/failed、WCS task cancelled 等事件事实。
- [x] BusinessGateway 暴露 inbound/outbound/putaway/picking/count/WCS facade。
- [ ] 还缺 FEFO/FIFO、ASN expected/received 差异、directed putaway、LPN/HU、完整库内策略、离线扫描和设备绑定。

### ERP

- [x] 服务存在：`backend/services/Business/Erp`
- [x] Procurement 覆盖 purchase requisition、RFQ、supplier quotation、purchase order、purchase receipt、supplier invoice、payment hold release/void。
- [x] Sales 覆盖 opportunity、quotation、sales order、delivery order。
- [x] Finance 覆盖 AP、AR、cost candidate、journal voucher、summary、source-document drill-down、payment/collection。
- [x] BusinessGateway 暴露 procurement/sales/finance 窄化 facade。
- [ ] 还缺完整税务、多币种深度、银行/收付款工作台、完整总账月结、退货/RMA、销售预测和经营报表。

### IndustrialTelemetry

- [x] 服务存在：`backend/services/Business/IndustrialTelemetry`
- [x] 覆盖 TelemetryTag、AlarmRule、DeviceStateSnapshot、AlarmEvent、TelemetrySummary、OEE/runtime availability。
- [x] 有报警 raised/cleared 公共事件，Maintenance 可消费报警事实。
- [x] BusinessGateway 暴露 equipment overview/device/availability/alarms，以及 telemetry tags/alarm-rules/samples/alarms/history/OEE/runtime-availability。
- [ ] 还缺 PLC/DCS/SCADA 控制命令边界外的 connector 产品化、趋势分析页、规则配置 UX、OEE 实时大屏和报警处置闭环。

### Maintenance

- [x] 服务存在：`backend/services/Business/Maintenance`
- [x] 覆盖 maintenance work order、maintenance plan、inspection、downtime reason、spare part line、availability windows、MTBF/MTTR P0。
- [x] 消费 IndustrialTelemetry alarm raised/cleared，支持报警触发维修工单。
- [x] BusinessGateway 暴露 work-orders、plans、inspections、spares、availability-windows facade。
- [ ] 还缺完整 CMMS 资产台账视角、备件成本/领退料、维保绩效分析、移动离线点检和计划日历排程体验。

### BarcodeLabel

- [x] 服务存在：`backend/services/Business/BarcodeLabel`
- [x] 覆盖 barcode rule、label template、print batch、scan record。
- [x] BusinessGateway 暴露 `/api/business-console/v1/barcode/*` rules/templates/print-batches/scans facade。
- [ ] PC 没有正式 barcode 页面目录。
- [ ] PDA 没有 `/scan` 路由和扫码解析端点消费。
- [ ] 还缺打印机/模板设计器、扫码业务直达、标签追溯 UI 和移动扫描闭环。

### BusinessApproval

- [x] 服务存在：`backend/services/Business/Approval`
- [x] 覆盖 approval template、approval chain、step decision、pending tasks、overdue check、withdraw/resubmit、add signer、transfer、delegation。
- [x] BusinessGateway 暴露 `/api/business-console/v1/approval/*` templates/chains/tasks/decisions/delegations facade。
- [x] PC 已有正式 approval 页面目录，覆盖模板、流程实例、我的任务、决策记录和委托设置。
- [ ] PDA 没有移动审批页。
- [ ] 还缺移动审批、工作台待办深度整合和跨域单据详情穿透。

## PC Business Console 清单

### 已有 PC 入口与页面

- [x] T 型导航模型存在：`frontend/apps/business-console/src/navigation.ts`
- [x] 工作台首页：`pages/index.vue`
- [x] MasterData：SKU、伙伴、工厂结构、设备、组织、排班、技能目录、人员技能、产品分类、计量单位、数据字典、编码规则。
- [x] ProductEngineering：工程物料、EBOM、MBOM、标准工序、工艺路线、生产版本、ECO、工程文档。
- [x] DemandPlanning：`pages/planning/index.vue`
- [x] MES：生产驾驶舱、计划、工单、工单详情、派工、物料、工序任务、WIP、报工、完工入库、质量、停机、产能、规则排程、交接、追溯、foundation readiness。
- [x] Quality：检验、NCR、原因码。
- [x] Inventory：可用量、库存移动、盘点。
- [x] WMS：收货、上架、出库、拣货、WCS、盘点。
- [x] ERP：采购与供应、销售、财务三页窄化入口。
- [x] Equipment/Maintenance：设备看板、设备详情、报警、维护工单、保养计划。

### PC 仍明显落后的部分

- [ ] BarcodeLabel 没有正式 PC 页面，尽管后端和 BusinessGateway 已经具备 rules/templates/print-batches/scans。
- [x] BusinessApproval 已有正式 PC 页面，消费 BusinessGateway templates/chains/tasks/decisions/delegations facade。
- [ ] Scheduling 没有独立 APS 顶级工作台，当前主要落在 MES `schedules` 页面和 BusinessGateway facade。
- [ ] IndustrialTelemetry 没有完整 tags/alarm-rules/OEE/runtime-availability 配置与分析页面，当前主要是设备看板和报警。
- [ ] ERP 只有采购/销售/财务窄面，不能声明完整 ERP。税务、银行、月结、完整报表、退货/RMA 等未交付。
- [ ] DemandPlanning 只有单页入口，MRP 运行详情、pegging 可视化、建议追踪和异常处理明显不足。
- [ ] MES 页面覆盖面广，但多处仍偏工作台/列表/诊断，不等于完整一线执行产品。个人任务、扫码直达、离线、异常协同和详情穿透不足。
- [ ] WMS 页面覆盖收发存拣盘，但库内策略、LPN/HU、ASN 差异、FEFO/FIFO、扫描作业闭环不足。
- [ ] Workbench 首页还没有达到角色化待办、真实预警、近期/星标和跨域对象搜索的最低可用版本。
- [ ] Cmd/Ctrl+K 命令搜索入口存在于壳层口径，但全局对象搜索面板尚未正式实装。
- [ ] 权限裁剪机制存在，但各业务域具体 permission code 与 feature flag 裁剪仍需逐域挂接。

## PDA / mobile 清单

### 已有 PDA 能力

- [x] 独立应用存在：`frontend/apps/business-pda`
- [x] 移动组件包存在：`frontend/packages/ui-mobile`
- [x] 移动业务核心包存在：`frontend/packages/business-core`
- [x] 登录页、移动工作台、应用墙、ScanBar 组件存在。
- [x] WMS PDA 页面：收货入库、复核发货、拣货、上架、盘点。
- [x] MES PDA 页面：工序执行、报工、领料、完工入库。
- [x] 设备运维 PDA 页面：报修、点检、报警查看。
- [x] PDA composable 已覆盖 WMS、MES、Maintenance、Equipment alarms。
- [x] WMS/MES/设备 SOP StepFlow 和移动标签存在测试事实。

### PDA/mobile 仍明显落后的部分

- [ ] 没有 `/scan` 页面。PDA 首页代码明确标注扫码直达仍待 `/scan` 路由和扫码解析端点。
- [ ] ScanBar 当前只能回显扫码内容，不能把工单、库位、物料、设备、批次等解析后路由到业务页。
- [ ] “我的任务”当前是空态，没有从 BusinessGateway 或移动专用 facade 拉取个人任务。
- [ ] 没有独立 mobile API contract 或 `/api/mobile/v1/**` 分层，当前仍主要复用 Business Console facade 和业务 composable。
- [ ] 没有离线 outbox、同步重放、冲突解决、弱网缓存或本地队列。
- [ ] 没有设备注册、终端绑定、扫描枪/摄像头能力封装和真实硬件 smoke。
- [ ] 没有移动审批、移动质量检验、移动库存独立作业、移动主数据只读查找等横向能力。
- [ ] PDA 作业页目前覆盖 WMS/MES/设备运维的 P0 子集，不等于完整移动端产品。

## 大屏 / 看板清单

### 已有大屏相关事实

- [x] `frontend/apps/design-system/src/pages/board.vue` 存在车间看板样例。
- [x] Design System 中存在 screen 类组件/文档，例如 OEE、KPI、节拍甘特、数字翻牌、报警表等组件方向。
- [x] UI 组件层已经具备构建大屏的基础控件和视觉样例。

### 大屏仍明显落后的部分

- [ ] 没有 `frontend/apps/business-board` 或等价业务大屏应用。
- [ ] 没有接入 BusinessGateway live data 的生产指挥大屏、OEE 大屏、WMS 仓储大屏、APS 甘特大屏或质量大屏。
- [ ] 没有大屏专用路由、权限、部署入口、刷新策略、断线降级、全屏适配和播放/轮播模式。
- [ ] design-system 的 `board.vue` 使用本地示例数据和交互，不是业务交付面。
- [ ] 大屏所需的聚合 read model 仍需明确放在 BusinessGateway 还是专门的 read facade 中。

## 下一步建议排期

### P0：先补前端业务可见缺口

- [ ] PC BarcodeLabel：规则、模板、打印批次、扫码记录，接现有 BusinessGateway facade。
- [x] PC BusinessApproval：我的待办、审批链详情、审批决策、模板、委托，接现有 BusinessGateway facade。
- [ ] PC Workbench：真实待办、预警、跨域快捷入口、近期/星标、空态/降级状态。
- [ ] PDA Scan：新增扫码解析 route/facade，支持工单、库位、物料、设备、批次、任务跳转。
- [ ] PDA My Tasks：从真实任务源聚合 WMS/MES/设备/审批/质量任务，替换首页空态。

### P1：补 PC 深度和移动闭环

- [ ] APS 独立工作台：Gantt、版本对比、发布、重排/RFC。
- [ ] IndustrialTelemetry 页面：tag、alarm rule、OEE、runtime availability、历史趋势。
- [ ] DemandPlanning 深化：MRP 运行详情、pegging 图、建议接受追踪、异常解释。
- [ ] WMS 深化：ASN 差异、directed putaway、LPN/HU、FEFO/FIFO、扫描作业联动。
- [ ] Quality PDA：移动检验、NCR、拍照/附件、离线提交。
- [ ] PDA 离线底座：outbox、幂等键、重放、冲突提示、弱网缓存。

### P2：独立大屏和完整业务域

- [ ] 建立业务大屏应用或等价入口，先从只读生产/OEE/WMS/APS/质量大屏开始。
- [ ] ERP 深化：税务、银行、月结、报表、RMA/退货、多币种深度。
- [ ] Maintenance 深化：资产 CMMS、备件成本、维修绩效、点检日历。
- [ ] ProductEngineering 深化：版本 diff、ECO 生效计划、文档预览/签审、工程影响分析。
- [ ] 全局对象搜索：Cmd/Ctrl+K 面板、权限过滤、对象详情穿透、近期/星标。

## 当前判断

- [x] 后端业务主干已经明显进入业务阶段，不再是单纯平台骨架。
- [x] BusinessGateway 覆盖面已经比 PC/PDA 页面更完整，多个模块是“后端和 facade 已经先到位，前端未跟上”。
- [x] PC Business Console 覆盖面扩大，但仍有 BarcodeLabel、Telemetry/OEE、Workbench/search 等明显缺口。
- [x] PDA v1 已有 WMS/MES/设备运维三域一线作业，但扫码直达、个人任务、离线、移动审批/质量/库存仍未完成。
- [x] 大屏目前只有设计系统样例和组件方向，没有业务大屏应用，不能按“已交付大屏”宣传。
