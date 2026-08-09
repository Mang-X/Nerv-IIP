# Business Console MES PC 端完善实施计划

> **供代理执行者使用：**必须使用子技能 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项实施本计划。各步骤使用复选框（`- [ ]`）语法跟踪。

> **2026-05-27 重定基线：**本计划交付了广泛的 PC 工作台界面，但已不再是 MES 后续交付的规范计划。未来 MES 工作必须遵循 `docs/superpowers/plans/2026-05-27-mes-operational-foundation-reset.md`；只有具备服务端编号、完整源数据、已发布工程版本、MRP/采购就绪、轻量 APS 排程契约、设备 IIoT 运行时事实以及持久化 MES 执行事实后，后续页面完善工作才能计为已交付。

**目标：**先完善符合标准的 PC 端 MES 工作台，使生产计划员、主管、班组长、物料员、质检员和维护协调员能在启动 PDA/移动端工作前，运行从生产计划就绪、工单下达、物料齐套、派工、工序执行、报工、质量处理、成品入库、班次交接到追溯的真实车间闭环。

**架构：**BusinessGateway 继续作为 Business Console 的 BFF，也是 `/api/business-console/v1/**` 唯一面向前端的 API；它负责用户 bearer 验证、IAM 权限检查、组织/环境上下文传递以及内部服务令牌调用。MES 拥有车间执行事实：工单、工序任务、派工、WIP 状态、生产报工、物料消耗证据、停机事件、成品入库请求、班次交接和谱系快照。当 MES 工作台必须查看或触发 ProductEngineering、DemandPlanning、MasterData、Quality、WMS/Inventory、Maintenance/IndustrialTelemetry 和 ERP 的事实时，通过窄粒度读取/动作 facade 集成；MES 不得接管这些服务的事实来源职责。首个版本在 Vue 页面中直接使用中文 UI 文案，并延后完整的 i18n 目录工作流。

**技术栈：**.NET 10、FastEndpoints、CleanDDD 服务边界、BusinessGateway facade、由 Hey API 生成的 `@nerv-iip/api-client`、Vue 3、Vite Plus、Pinia Colada、`@nerv-iip/ui`、Playwright。

## 实施收口 — 2026-05-26

本计划已在 PR #185 中为 PC 优先的 Business Console MES 工作台完成实施：

- 后端 MES 现已暴露生产计划、就绪检查、工单下达、领料请求、派工、工序任务生命周期、WIP、生产报工、不良、停机、入库请求、班次交接、追溯、排程和产能影响等 P0 工作台界面。
- BusinessGateway 暴露匹配的 `/api/business-console/v1/mes/**` facade 路由，并配有窄粒度 IAM 权限码以及生成的 OpenAPI/客户端覆盖。
- Business Console 现已具有中文 PC 路由：`生产驾驶舱`、`基础准备`、`生产计划`、`计划与工单`、`齐套与物料`、`派工看板`、`工序执行`、`报工与完工`、`质量与不良`、`完工入库`、`规则排程`、`设备与停机`、`班次交接`、`追溯查询` 和 `产能影响`。
- `scripts/verify-business-console-mes-pc-workbench.ps1` 是聚焦验证门禁。它覆盖 MES 测试、BusinessGateway 测试、api-client 的生成/类型检查/测试，以及 Business Console 的类型检查/测试/构建；e2e 通过 `-E2E` 按需启用。
- PDA/移动端继续延后，直至这些 PC 契约稳定。

---

## 基线决策

本计划以 PC 优先的业务实施顺序，取代下一步优先移动端/PDA 的假设：

1. 首先完成 Business Console 桌面页面，并以 MES 作为首个深度工作台。
2. 从标准制造执行流程设计 MES，而不是围绕当前 MVP endpoint 清单设计。
3. 从 API/BFF 契约开始，因为 MES 页面依赖 MES 及多个相邻业务上下文的数据。
4. 在推进 MES 的同时只完善最少量相关业务接口，而不是试图完成每个周边系统。
5. 将生产计划和物料备料视为 MES 可见的执行能力，同时让长期规划、仓库执行和库存核算继续归属各自服务。
6. 延后 PDA/移动端，直至桌面工作流和生成的 Business Console 契约稳定。
7. 首轮页面实施直接使用中文文本。仓库虽有 i18n 概念，但首个 MES 工作台不应承担完整翻译目录、语言区域路由和文案治理工作流的成本。

## 标准 MES 参考模型

首个 MES 工作台应遵循 ISA-95 和成熟系统采用的常见 MES/MOM 形态，而不是只暴露 CRUD 页面：

| 参考资料 | 相关设计启示 |
| --- | --- |
| [ISA-95 / IEC 62264](https://www.isa.org/standards-and-publications/isa-standards/isa-95-standard) | 第 3 层制造运营管理覆盖生产运营，以及第 3 层制造系统与第 4 层业务系统之间的接口。以此保持 ERP 计划/财务与车间执行分离。 |
| [Siemens Opcenter Execution](https://www.siemens.com/en-gb/products/opcenter/execution/discrete/) | 成熟 MES 强调工单、物料/组件/工艺变更、生产跟踪、JIT/JIS 物料可见性、质量和追溯。 |
| [Siemens Opcenter APS / Planning and Scheduling](https://www.siemens.com/en-us/products/opcenter/production-planning-scheduling-capabilities/) | 高级计划和有限产能排程是独立的计划/排程能力。MES 应消费或执行短周期派工，不应在首个版本中变成完整 APS。 |
| [SAP Digital Manufacturing](https://www.sap.com/products/scm/digital-manufacturing.html) | 资源编排包含利用仓库/库存、质量、劳动力和维护变量进行实时运营计划；执行侧跟踪劳动力、作业指导、报废、返工和过程控制。 |
| [SAP Digital Manufacturing + EWM staging](https://help.sap.com/docs/sap-digital-manufacturing/execution/614d9a19fb28417fbd200cd0c200b75c.html) | MES 可根据工单、派工、资源、工作中心和生产供应区上下文触发物料备料请求，而 EWM/WMS 执行仓库任务。 |
| [Rockwell Plex MES/MOM](https://plex.rockwellautomation.com/en-us/products/manufacturing-execution-system.html) | 成熟 MES 是具备实时可见性、质量、库存/物料追溯、条码扫描和合规证据的生产管理系统。 |

### 本计划的 P0 MES 核心

| 能力 | MES 是否拥有该事实？ | 首个版本预期 |
| --- | --- | --- |
| 生产计划就绪与工单下达 | 部分拥有 | MES 工作台评估 DemandPlanning/ERP 建议能否转化为可执行工单。长期计划事实仍归 DemandPlanning/ERP。 |
| 工单执行 | 是 | 工单状态、下达快照、执行状态、优先级、急单/插单处理，以及关闭/重开控制。 |
| 工序派工 | 是 | 将工序任务分配给产线/工作中心/设备/人员/班次；物料缺失、质量冻结或设备不可用时阻止或警告。 |
| WIP 跟踪 | 是 | 跟踪当前工序、等待/运行/暂停/完成/冻结状态、工序间数量流转和阻塞原因。 |
| 生产报工 | 是 | 合格、报废、返工、人工工时、机器工时、开始/结束、操作员、设备以及工序状态影响。 |
| 物料消耗证据 | 对执行证据而言是 | 记录工单/工序实际消耗的物料批次/序列号。库存余额和仓库任务仍在 MES 之外。 |
| 物料齐套与领料请求 | MES 可见的触发器 | MES 根据 BOM/工艺路线/工作中心上下文以及库存/WMS 可用性计算就绪状态，然后创建备料/领料请求，交由 WMS/Inventory 执行。 |
| 过程质量与不合格 | 部分拥有 | MES 捕获过程不良并阻止执行；Quality 拥有检验标准、NCR 生命周期和正式处置。 |
| 停机与设备影响 | 对执行事件而言是 | MES 记录影响生产的停机与恢复确认；Maintenance 拥有维护工单和资产生命周期。 |
| 成品入库请求 | 对生产请求而言是 | MES 在生产/质量就绪后创建请求；WMS/Inventory 拥有入库收货和库存过账。 |
| 班次交接 | 是 | MES 跨班次传递未解决的生产、物料、质量、设备和入库问题。 |
| 谱系/追溯 | 作为执行证据是 | 追踪工单、批次/序列号、物料、工序、人员、设备、质量、停机和入库关联。 |

### 不属于首个版本核心的 P1/P2

P1 后续项：更丰富的有限产能派工、线边库存明细、工装/模具生命周期、SPC/Cpk、电子作业指导书版本强制、Andon 升级、OEE 损失树分析，以及流程行业的批次/配方称量。

P2 集成：完整 APS 优化、完整 WMS/AGV/WCS 自动化、完整 QMS/LIMS、完整 CMMS/EAM、SCADA/PLC 控制、BI/数据湖分析、移动端/PDA 扫描和详细成本核算。

## 生产基础就绪基线

MES 工作台不得从工单 CRUD 开始。它首先需要生产基础就绪层，检查下达和执行工单所需的核心事实是否存在、有效，并可用于选定的组织/环境/站点/产线/日期。

### 基础事实归属

| 基础领域 | 事实来源 | MES 首个版本职责 |
| --- | --- | --- |
| 组织、环境、用户、权限 | IAM | 使用 ID 和权限检查；不得将 IAM 角色或成员关系复制进 MES。 |
| 站点、工厂、区域、产线、工作中心、工位 | BusinessMasterData | 在计划下达、派工、报工和交接前解析并验证生产层级。 |
| 工作日历、班次、团队 | BusinessMasterData | 验证计划开始/结束、派工、报工和交接处于有效的日历/班次/团队上下文中。 |
| 人员业务属性与技能 | IAM 用户 ID + BusinessMasterData `PersonnelSkill` | 验证操作员/团队分配和技能资质；MES 只存储分配快照。 |
| 设备资产与资源能力 | BusinessMasterData 静态事实、Maintenance/Telemetry 运行时事实 | 派工前验证静态兼容性和当前可用性；MES 记录实际设备使用和停机影响。 |
| SKU、UOM、UOM 换算、追溯策略 | BusinessMasterData | 下达/报工前验证启用制造的 SKU、UOM 换算、批次/序列号策略和默认条码规则。 |
| 生产版本、MBOM、工艺路线、工序定义 | ProductEngineering | 解析已发布生产版本并锁定下达快照；MES 不编辑工程设计事实。 |
| 仓库、生产供应区、线边库位、库存状态 | WMS/Inventory 及 MasterData 标签 | 验证物料可用性和备料路径；MES 创建请求意图并记录线边收货证据。 |
| 检验标准、检验计划、质量冻结 | Quality 及 MasterData 特性定义 | 验证检验要求和阻塞性质量状态；MES 记录执行不良上下文并关联 Quality 事实。 |
| 维护计划、停机、资产恢复 | Maintenance 及 IndustrialTelemetry | 验证资产可用性和影响生产的维护状态；MES 记录车间停机和恢复确认。 |
| 条码规则、标签、扫描记录 | BarcodeLabel | 解析工单、物料批次、产品序列号、流转卡、容器、托盘和检验标签的条码/标签规则引用。 |
| 业务单据编号 | 受共享治理的服务本地编号策略；未来 Numbering 服务仍为可选 | 使用一致的规则契约和冲突测试，为 MES 自有单据生成稳定 ID；不得将 UI 输入的硬编码 ID 作为长期来源。 |

### 最低就绪检查

每条计划转工单或工单下达路径都必须计算就绪结果，其状态为 `Ready`、`Warning` 或 `Blocked`，并带机器可读原因码。首个版本必须覆盖：

| 就绪检查 | 阻塞示例 | 警告示例 |
| --- | --- | --- |
| MasterData 层级 | 工厂、产线、工作中心、班次、团队、SKU、UOM 或设备缺失/禁用。 | 工作中心有效，但缺少产能元数据。 |
| 日历与班次 | 计划时间没有有效工作日历或班次。 | 计划时间跨越班次边界，需要交接。 |
| 人员与技能 | 已分配用户缺少必要技能/资质，或引用了非活动 IAM 用户。 | 技能即将到期，或需要主管人工确认。 |
| 产品工程 | 没有已发布生产版本、MBOM、工艺路线或工序序列。 | 生产版本有效，但接近到期/生效日期变更。 |
| 物料与供应 | 所需物料没有 UOM 换算、追溯策略不匹配、没有可用库存或没有备料路径。 | 物料部分可用、有替代料，或已知预计到货日期。 |
| 质量 | SKU 或工序需要检验，但不存在检验计划，或源批次处于质量冻结。 | 检验计划存在，但需要首件确认。 |
| 设备与维护 | 所需设备/工作中心不可用、处于维护中，或有活动的阻塞告警。 | 设备可用，但与计划维护冲突。 |
| 条码与标签 | 可追溯物料、序列化产品、流转卡或入库标签缺少必要条码/标签规则。 | 条码规则存在，但模板没有打印机映射。 |
| 编号 | 工单、工序任务、物料请求、报工、不良、停机、入库请求、交接或追溯事件缺少必要单据编号规则。 | 规则存在，但前缀序列接近配置阈值。 |

### 基础记录契约

MES 使用的每个基础解析器都必须返回足以支持执行决策和用户指引的数据，不得只返回 `true`/`false`。

| 字段 | 要求 |
| --- | --- |
| `sourceSystem` | `IAM`、`MasterData`、`ProductEngineering`、`WMS`、`Inventory`、`Quality`、`Maintenance`、`IndustrialTelemetry`、`BarcodeLabel` 或 `MES` 之一。 |
| `referenceType` | 稳定类型名，例如 `Plant`、`ProductionLine`、`WorkCenter`、`WorkCalendar`、`Shift`、`Team`、`PersonnelSkill`、`DeviceAsset`、`Sku`、`Uom`、`ProductionVersion`、`Mbom`、`Routing`、`InventoryLocation`、`InspectionPlan`、`BarcodeRule` 或 `NumberingRule`。 |
| `referenceId` | 持久的源系统 ID；绝不使用显示文本作为 ID。 |
| `displayName` | 有名称时使用人类可读的中文名称；无名称的记录以源代码作为后备。 |
| `status` | `Ready`、`Warning` 或 `Blocked`；必须在 BusinessGateway/MES 工作台边界规范化源系统特有状态。 |
| `effectiveFromUtc` / `effectiveToUtc` | 当源数据具有有效期时，生产版本、BOM/工艺路线、日历、班次、技能资质、检验计划、条码规则和编号规则必须提供。 |
| `version` | 当源数据具有版本时，生产版本、MBOM、工艺路线、条码模板和检验计划必须提供。 |
| `fixHint` | 简短的中文操作员/计划员指引，例如 `请先维护该产线的工作日历` 或 `请发布该物料的生产版本`。 |

### 原因码基线

使用稳定原因码，使页面、测试以及后续移动端/PDA 流程可复用相同语义：

| 代码 | 严重程度 | 含义 |
| --- | --- | --- |
| `MASTERDATA_HIERARCHY_MISSING` | Blocked | 无法解析站点、工厂、区域、产线、工作中心或工位。 |
| `MASTERDATA_REFERENCE_INACTIVE` | Blocked | 必要 MasterData 记录存在，但已禁用或不在有效期内。 |
| `CALENDAR_SHIFT_MISSING` | Blocked | 计划执行时间没有有效工作日历或班次。 |
| `SHIFT_HANDOVER_REQUIRED` | Warning | 计划执行跨越班次边界，必须创建/使用交接上下文。 |
| `PERSONNEL_SKILL_MISSING` | Blocked | 已分配用户/团队缺少必要技能或资质。 |
| `PERSONNEL_SKILL_EXPIRING` | Warning | 必要技能当前有效，但将在配置的警告窗口内到期。 |
| `PRODUCTION_VERSION_MISSING` | Blocked | 无法为 SKU、站点/产线和计划日期解析已发布生产版本。 |
| `BOM_ROUTING_MISSING` | Blocked | 已发布生产版本没有可用的 MBOM、工艺路线或工序序列。 |
| `MATERIAL_TRACEABILITY_MISMATCH` | Blocked | 必要物料追溯策略与 SKU 或条码规则冲突。 |
| `MATERIAL_NOT_AVAILABLE` | Blocked | 必要物料没有可用库存或备料供应路径。 |
| `MATERIAL_PARTIAL_AVAILABLE` | Warning | 部分必要物料可领用，但仍有短缺。 |
| `QUALITY_PLAN_MISSING` | Blocked | 无法解析必要检验计划或质量标准。 |
| `QUALITY_HOLD_ACTIVE` | Blocked | 相关源批次、物料或产品处于质量冻结。 |
| `EQUIPMENT_UNAVAILABLE` | Blocked | 必要工作中心/设备停机、处于维护中，或被活动告警阻塞。 |
| `EQUIPMENT_MAINTENANCE_CONFLICT` | Warning | 设备当前可用，但与计划维护冲突。 |
| `BARCODE_RULE_MISSING` | Blocked | 无法解析必要条码或标签规则。 |
| `LABEL_TEMPLATE_PRINTER_MISSING` | Warning | 标签规则存在，但未配置打印机/模板映射。 |
| `NUMBERING_RULE_MISSING` | Blocked | MES 无法在服务端生成必要单据编号。 |
| `NUMBERING_SEQUENCE_NEAR_LIMIT` | Warning | 编号序列接近其配置上限。 |
| `SOURCE_SERVICE_UNAVAILABLE` | Blocked | BusinessGateway 无法连接必要源服务，或源服务返回超时、5xx 或格式错误的就绪响应。 |

### 源系统边界规则

`/mes/foundation` 页面是就绪检查与指引界面，不是基础数据维护模块。路由存在时可显示阻塞卡片和源页面链接，但不得从 MES 内部创建 MasterData、ProductEngineering、WMS/Inventory、Quality、Maintenance、Telemetry、BarcodeLabel 或 IAM 记录。这样可使 MES 聚焦执行，并避免复制主数据工作流。

### 源服务失败规则

基础就绪是决策界面，因此源服务失败必须作为生产阻塞显式呈现，而不是显示空白页面。对于 `GET /api/business-console/v1/mes/foundation-readiness`，当 MasterData、ProductEngineering、WMS/Inventory、Quality、Maintenance/Telemetry、BarcodeLabel 或 MES 编号策略解析器超时、返回 5xx、返回无效 JSON 或遗漏必要就绪字段时，BusinessGateway 必须返回 HTTP 200，将受影响领域标为 `Blocked` 并附带 `SOURCE_SERVICE_UNAVAILABLE` 问题。IAM 身份验证/授权失败仍为正常的 401/403 响应，不得转换为就绪问题。

### 快照规则

下达工单或派发工序任务时，MES 必须存储不可变执行快照：

1. MasterData 快照：站点/工厂/产线/工作中心/工位、班次/团队、SKU/UOM、设备静态身份、资源能力和人员技能引用。
2. ProductEngineering 快照：`productionVersionId`、MBOM ID/版本、工艺路线 ID/版本、工序序列、必要资源能力、标准时长和物料需求摘要。
3. 物料快照：必要物料、UOM、追溯策略、计划数量、替代策略、请求/备料/收货数量以及 WMS/Inventory 引用。
4. 质量快照：检验要求、首件/过程/终检触发器、质量冻结状态以及相关检验/NCR 引用。
5. 编号/条码快照：为已下达执行对象生成的单据 ID，以及使用的条码/标签规则引用。

快照使历史执行保持可读，但不会将事实来源归属从 MasterData、ProductEngineering、WMS/Inventory、Quality、Maintenance 或 BarcodeLabel 移出。

## 已验证的当前代码事实

以下事实已于 2026-05-26 对照仓库核实：

| 领域 | 当前事实 | 影响 |
| --- | --- | --- |
| Business Console 应用 | `frontend/apps/business-console` 作为基于 BusinessGateway facade 的独立 Vite 应用存在。 | 可在不改变主平台控制台的情况下继续开发 PC 页面。 |
| 当前 MES 页面 | `frontend/apps/business-console/src/pages/mes/work-orders.vue` 和 `frontend/apps/business-console/src/pages/mes/schedules.vue` 已存在。 | 后续工作应增强 MES 页面，而不是新建应用外壳。 |
| 当前 MES 组合式函数 | `frontend/apps/business-console/src/composables/useBusinessMes.ts` 使用生成的 Business Console API。 | 新页面数据应在此添加，或拆分为同一应用边界下的 MES 专用组合式函数。 |
| BusinessGateway MES facade | `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Mes/BusinessConsoleMesEndpoints.cs` 暴露 `listBusinessConsoleMesWorkOrders`、`createBusinessConsoleMesRushWorkOrder`、`runBusinessConsoleMesSchedule` 和 `recordBusinessConsoleMesProductionReport`。 | Business Console 当前只有 MVP MES 界面。 |
| MES 服务界面 | `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Endpoints/Mes/MesEndpoints.cs` 已包含工单、生产报工、成品入库请求和产能影响的服务 endpoint。 | 首轮 API 工作可先扩展 BusinessGateway facade 覆盖，再引入更广泛的领域变更。 |
| DemandPlanning 与 ERP 计划上下文 | DemandPlanning 在 `/api/business/v1/planning/**` 下暴露需求来源、MRP 运行、MRP 追溯和计划建议；ERP 暴露销售订单与财务源单据下钻，但没有经验证且字面命名为 `production-plans` 的 endpoint。 | BusinessGateway 应根据已验证的计划建议或 ERP 销售优先级上下文构建 MES 生产计划 facade。缺少稳定读取 endpoint 时，应显示 MES/源系统原始 ID，并在实施 PR 中记录缺口，不得编造数据。 |
| 现有页面文案 | 当前 MES Vue 页面仍包含 `Work orders`、`Create rush work order`、`Run schedule` 和 `No work orders returned.` 等英文用户可见标签。 | PC 端完善必须包含中文文案处理。 |
| 移动端/PDA | 当前不存在移动版 Business Console 客户端或生成的移动 API 边界。 | 移动端/PDA 不会阻塞 PC MES，应在 PC 契约稳定后启动。 |

## 业务范围

### 范围内

1. 跨 MasterData、ProductEngineering、WMS/Inventory、Quality、Maintenance/Telemetry、BarcodeLabel 和编号策略的生产基础就绪检查。
2. 展示今日计划达成、工单进度、物料阻塞、停机、质量异常和交接事项的生产驾驶舱。
3. 生产计划就绪、计划转工单、工单下达、急单/插单处理和下达风险检查。
4. 物料齐套、短缺可见性、领料/备料请求创建、线边收货确认、退料/补料请求可见性和物料消耗证据。
5. 向班次、团队、人员、工作中心和设备派工；工序任务开始、暂停、恢复、完成、转移和冻结。
6. 生产报工，包括合格数量、报废、返工、人工/机器工时、物料批次/序列号证据和附件。
7. 过程质量和不合格入口：首件/过程/终检任务可见性、不良登记、返工/报废关联，以及 Quality/NCR 下钻。
8. 从生产完工到 WMS/Inventory 收货证据的成品入库请求创建与状态可见性。
9. 停机、设备影响、维护请求可见性、恢复确认，以及资产不可用时的派工阻止/警告。
10. 班次交接，将未解决的生产、物料、质量、设备和入库问题传递至下一班次。
11. 工单级和批次/序列号级谱系/追溯，涵盖计划、BOM/工艺路线版本、工序任务、报工、物料批次、质量、设备、人员、停机和入库请求。
12. 扩展 BusinessGateway MES facade，以覆盖现有 MES 读写服务能力和缺失的标准 MES P0 契约。
13. MES 页面需要上下文时使用最小跨领域读取/动作 facade：
   - ProductEngineering：生产版本、MBOM、工艺路线下达上下文。
   - DemandPlanning/ERP：已有能力范围内的生产计划来源、计划工单建议、销售/订单优先级上下文。
   - MasterData：SKU、工作中心、生产线、设备资产标签。
   - Quality：与工单和工序报工相关的检验任务、不良、NCR、返工/报废处置上下文。
   - WMS/Inventory：库存可用性、领料/备料执行状态、线边收货、成品入库和库存移动可见性。
   - Maintenance/IndustrialTelemetry：资产不可用/已恢复、停机、告警、恢复和产能影响可见性。
   - BarcodeLabel：追溯所需的条码规则、标签模板、打印批次和扫描记录引用。
   - ERP Finance：现有服务界面已支持时，提供生产成本证据的源单据下钻。
14. 刷新 Business Console 生成客户端及稳定导出。
15. 具有中文可见文案的桌面 UI 页面。
16. 聚焦的单元、API 契约、前端和 e2e 验证。

### 范围外

1. PDA/移动端扫描流程。
2. 完整 APS/Gantt 优化 UI。除非明确恢复 #78，否则排程仍是面向派工的列表/时间线/表格工作台。
3. 完整仓库执行：库位策略、波次拣选、AGV/WCS 路由、上架、盘点和仓库任务优化仍归 WMS。
4. 库存核算：库存台账、全局可用量承诺、估值和财务库存仍归 Inventory/ERP。
5. 完整 QMS/LIMS：正式检验标准治理、实验室样本生命周期、CAPA、供应商质量和审计计划仍归 Quality/QMS。
6. 完整 CMMS/EAM：资产生命周期、维护计划归属、备件计划和维护成本核算仍归 Maintenance/EAM。
7. 前端直接调用业务服务。
8. 将领域规则移入 BusinessGateway。
9. 完整 i18n 翻译目录、语言区域切换器或路由本地化文案。
10. 在 MES 内实现原始 PLC/DCS/SCADA 控制或 WCS。

## 依赖矩阵

| PC MES 需求 | 执行归属 | 外部事实归属 | BusinessGateway 方案 |
| --- | --- | --- | --- |
| 基础就绪 | MES 工作台决策界面 | MasterData、ProductEngineering、WMS/Inventory、Quality、Maintenance/Telemetry、BarcodeLabel、IAM | 添加就绪 endpoint，验证全部必要引用，并返回带原因码的 `Ready`/`Warning`/`Blocked`。 |
| 生产计划就绪 | MES 工作台决策界面 | DemandPlanning/ERP 源计划、ProductEngineering BOM/工艺路线、Inventory 可用性、Maintenance 产能 | 添加聚合就绪 endpoint，返回风险原因和允许的下达动作，且不将源事实移入 MES。 |
| 工单下达与执行 | MES | ProductEngineering、MasterData | 添加工单详情/下达 endpoint，包含 BOM/工艺路线/版本、工作中心和计划来源的下达快照。 |
| 物料齐套与短缺 | MES 可见的就绪结果 | ProductEngineering BOM、Inventory 可用性/预留、WMS 线边状态 | 添加以计划/工单/工序为键的物料就绪 endpoint；库存数量继续以 Inventory/WMS 为权威。 |
| 领料/备料请求 | MES 触发并跟踪请求意图 | WMS/Inventory 执行拣选、备料、收货和库存移动 | 添加请求创建/状态 endpoint；不得在 MES 内建模仓库任务。 |
| 工序派工 | MES | MasterData 资源、Maintenance 可用性、Quality 冻结 | 添加带阻塞/警告原因的派工任务列表和分配 endpoint。 |
| WIP 与生产报工 | MES | Quality/Inventory 下游影响 | 添加工序状态、报工列表、报工创建和 WIP 摘要 endpoint。 |
| 不合格与返工/报废 | MES 创建执行不良上下文 | Quality 拥有 NCR/处置；Inventory 拥有报废移动 | 添加不良录入和相关质量下钻；正式处置仍归 Quality。 |
| 成品入库 | MES 请求 | WMS/Inventory 入库收货和库存过账 | 呈现 MES 入库请求及下游 WMS/Inventory 证据。 |
| 停机与设备影响 | MES 执行影响 | IndustrialTelemetry 事件和 Maintenance 工单 | 呈现停机列表/创建/恢复 endpoint 和下游维护状态。 |
| 班次交接 | MES | 相关上下文提供未结问题状态 | 添加交接摘要/创建/接受 endpoint。 |
| 追溯 | MES 执行谱系 | ProductEngineering、Quality、WMS/Inventory、Maintenance 提供关联事实 | 添加按工单、批次/序列号、物料批次和不良 ID 查询的追溯 endpoint。 |
| 条码与标签 | MES 引用规则并记录扫描 | BarcodeLabel 拥有规则、模板、打印批次、扫描记录 | 在物料、报工、入库和追溯 DTO 中添加规则解析以及标签/扫描引用。 |
| 编号 | MES 拥有 MES 单据 ID | 共享编号治理；首个版本使用服务本地生成器 | 添加显式编号规则检查，并在服务端生成 MES 单据 ID。 |
| 成本/源单据下钻 | ERP Finance | ERP Finance | 只有在路由和权限经验证后，才链接到现有 ERP Finance 候选/源单据界面。 |

## 文件结构

计划的文件职责：

| 路径 | 职责 |
| --- | --- |
| `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Endpoints/*` | 添加或验证站点、产线、工作中心、日历、班次、团队、人员技能、设备资产、资源能力、SKU、UOM 和参考数据的批量解析/就绪 endpoint。 |
| `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Endpoints/*` | 添加或验证生产版本解析 endpoint，返回已发布 MBOM、工艺路线、工序序列、物料需求和资源能力引用。 |
| `backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Endpoints/*` | 添加或验证工单、工序任务、物料批次、产品序列号、入库和追溯标签的条码规则与标签模板解析。 |
| `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Endpoints/Mes/MesEndpoints.cs` | 仅在 MES 服务缺少页面级查询时添加缺失的 MES 读取 endpoint。 |
| `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/...` | 生产就绪、工单详情、物料齐套、派工任务列表、工序任务列表、WIP、生产报工、停机、交接、追溯、排程结果历史及任何缺失读模型的查询处理程序。 |
| `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/...` | 工单下达、派工分配、工序开始/暂停/恢复/完成、领料请求意图、不良录入、停机录入/恢复、成品入库请求和班次交接命令。 |
| `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/...` | MES 服务 endpoint 和查询测试。 |
| `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessConsoleModels.cs` | MES 工作台响应的 Business Console DTO。 |
| `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessServiceClients.cs` | MES 及最少量相关业务读取 endpoint 的内部 HTTP 客户端。 |
| `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/Auth/BusinessGatewayAuthorization.cs` | MES 权限矩阵的 `BusinessGatewayPermissions` 常量和 Business Console 授权检查。 |
| `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Mes/BusinessConsoleMesEndpoints.cs` | BusinessGateway MES facade endpoint 和稳定 operation ID。 |
| `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayOpenApiTests.cs` | 稳定路由和 operationId 测试。 |
| `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayProxyTests.cs` | Bearer、权限、上下文和下游代理测试。 |
| `frontend/packages/api-client/src/business-console.ts` | 生成客户端刷新后的稳定 business-console 导出。 |
| `frontend/apps/business-console/src/composables/useBusinessMes.ts` | MES PC 页面的查询/变更组合入口，将分组 hook 委托给 `src/composables/mes/*.ts`。 |
| `frontend/apps/business-console/src/pages/mes/foundation.vue` | 主数据、产品工程、供应、质量、设备、条码和编号阻塞项的基础就绪页面。 |
| `frontend/apps/business-console/src/pages/mes/index.vue` | 生产驾驶舱：计划达成、阻塞、异常、交接和追溯入口。 |
| `frontend/apps/business-console/src/pages/mes/plans.vue` | 生产计划就绪、计划转工单、下达风险检查和急单/插单影响。 |
| `frontend/apps/business-console/src/pages/mes/work-orders.vue` | 工单列表、下达状态、就绪摘要和快捷动作。 |
| `frontend/apps/business-console/src/pages/mes/work-orders/[workOrderId].vue` | 工单详情页。 |
| `frontend/apps/business-console/src/pages/mes/materials.vue` | 物料齐套、短缺、领料/备料请求状态、线边收货、退料/补料请求可见性。 |
| `frontend/apps/business-console/src/pages/mes/dispatch.vue` | 将工序任务分配给班次/团队/人员/工作中心/设备的派工看板。 |
| `frontend/apps/business-console/src/pages/mes/operation-tasks.vue` | 工序任务队列及开始/暂停/恢复/完成动作。 |
| `frontend/apps/business-console/src/pages/mes/reports.vue` | 合格/报废/返工/人工工时/机器工时的生产报工列表和创建入口。 |
| `frontend/apps/business-console/src/pages/mes/quality.vue` | 过程质量任务、不良录入、相关 NCR/返工/报废上下文。 |
| `frontend/apps/business-console/src/pages/mes/receipts.vue` | 成品入库请求可见性和 WMS/Inventory 证据。 |
| `frontend/apps/business-console/src/pages/mes/schedules.vue` | 规则排程运行以及面向派工的结果表格/时间线。 |
| `frontend/apps/business-console/src/pages/mes/downtime.vue` | 停机登记、设备影响、维护状态和恢复确认。 |
| `frontend/apps/business-console/src/pages/mes/handovers.vue` | 班次交接摘要、未解决事项结转和接班人确认。 |
| `frontend/apps/business-console/src/pages/mes/traceability.vue` | 工单、批次/序列号、物料批次和不良追溯搜索。 |
| `frontend/apps/business-console/src/pages/mes/capacity.vue` | MES-维护集成的产能影响可见性；若与停机分离则保留。 |
| `frontend/apps/business-console/tests/e2e/business-console.spec.ts` | 桌面 MES 导航和冒烟覆盖。 |
| `scripts/verify-business-console-mes-pc-workbench.ps1` | 本计划受治理的聚焦验证脚本。 |
| `docs/architecture/frontend-structure.md` | 仅在路由实施后更新，以保持 Business Console 路由表为最新。 |
| `docs/architecture/implementation-readiness.md` | 仅在实施落地并具有验证证据后更新。 |

## 契约目标

目标 BusinessGateway operation ID：

| 方法 | 路由 | Operation ID | 下游归属 |
| --- | --- | --- | --- |
| GET | `/api/business-console/v1/mes/foundation-readiness` | `getBusinessConsoleMesFoundationReadiness` | BusinessGateway 聚合 MasterData、ProductEngineering、WMS/Inventory、Quality、Maintenance/Telemetry、BarcodeLabel、IAM |
| GET | `/api/business-console/v1/mes/foundation-readiness/master-data` | `getBusinessConsoleMesMasterDataReadiness` | MasterData 解析/验证 facade |
| GET | `/api/business-console/v1/mes/foundation-readiness/product-engineering` | `getBusinessConsoleMesProductEngineeringReadiness` | ProductEngineering 生产版本解析 facade |
| GET | `/api/business-console/v1/mes/foundation-readiness/supply` | `getBusinessConsoleMesSupplyReadiness` | WMS/Inventory 可用性与备料路径 facade |
| GET | `/api/business-console/v1/mes/foundation-readiness/quality` | `getBusinessConsoleMesQualityReadiness` | Quality 检验/冻结 facade |
| GET | `/api/business-console/v1/mes/foundation-readiness/equipment` | `getBusinessConsoleMesEquipmentReadiness` | MasterData、Maintenance、IndustrialTelemetry 门面 |
| GET | `/api/business-console/v1/mes/foundation-readiness/barcode-numbering` | `getBusinessConsoleMesBarcodeNumberingReadiness` | BarcodeLabel 及 MES 编号策略 facade |
| GET | `/api/business-console/v1/mes/overview` | `getBusinessConsoleMesOverview` | BusinessGateway 聚合 MES 查询 |
| GET | `/api/business-console/v1/mes/production-plans` | `listBusinessConsoleMesProductionPlans` | 通过 MES 工作台 facade 获取 DemandPlanning/ERP 源计划 |
| GET | `/api/business-console/v1/mes/production-plans/{productionPlanId}/readiness` | `getBusinessConsoleMesProductionPlanReadiness` | MES 聚合 ProductEngineering、Inventory/WMS、Quality、Maintenance |
| POST | `/api/business-console/v1/mes/production-plans/{productionPlanId}/work-orders` | `convertBusinessConsoleMesPlanToWorkOrder` | 带 DemandPlanning/ERP 源引用的 MES 命令 |
| GET | `/api/business-console/v1/mes/work-orders` | `listBusinessConsoleMesWorkOrders` | 现有 MES 服务列表 |
| GET | `/api/business-console/v1/mes/work-orders/{workOrderId}` | `getBusinessConsoleMesWorkOrderDetail` | MES 服务详情查询 |
| POST | `/api/business-console/v1/mes/work-orders/{workOrderId}/release` | `releaseBusinessConsoleMesWorkOrder` | MES 命令 |
| POST | `/api/business-console/v1/mes/work-orders/rush` | `createBusinessConsoleMesRushWorkOrder` | 现有 MES 服务命令 |
| GET | `/api/business-console/v1/mes/work-orders/{workOrderId}/material-readiness` | `getBusinessConsoleMesMaterialReadiness` | MES 聚合 ProductEngineering、Inventory/WMS |
| POST | `/api/business-console/v1/mes/work-orders/{workOrderId}/material-issue-requests` | `createBusinessConsoleMesMaterialIssueRequest` | MES 请求意图，由 WMS/Inventory 执行 |
| GET | `/api/business-console/v1/mes/material-issue-requests` | `listBusinessConsoleMesMaterialIssueRequests` | MES/WMS 状态聚合 |
| POST | `/api/business-console/v1/mes/material-issue-requests/{requestId}/line-side-receipts` | `confirmBusinessConsoleMesLineSideMaterialReceipt` | MES 收货确认及 WMS/Inventory 证据 |
| GET | `/api/business-console/v1/mes/dispatch-tasks` | `listBusinessConsoleMesDispatchTasks` | MES 派工查询 |
| POST | `/api/business-console/v1/mes/dispatch-tasks/{operationTaskId}/assign` | `assignBusinessConsoleMesDispatchTask` | MES 派工命令 |
| GET | `/api/business-console/v1/mes/operation-tasks` | `listBusinessConsoleMesOperationTasks` | MES 服务查询 |
| POST | `/api/business-console/v1/mes/operation-tasks/{operationTaskId}/start` | `startBusinessConsoleMesOperationTask` | MES 命令 |
| POST | `/api/business-console/v1/mes/operation-tasks/{operationTaskId}/pause` | `pauseBusinessConsoleMesOperationTask` | MES 命令 |
| POST | `/api/business-console/v1/mes/operation-tasks/{operationTaskId}/resume` | `resumeBusinessConsoleMesOperationTask` | MES 命令 |
| POST | `/api/business-console/v1/mes/operation-tasks/{operationTaskId}/complete` | `completeBusinessConsoleMesOperationTask` | MES 命令 |
| GET | `/api/business-console/v1/mes/wip` | `getBusinessConsoleMesWipSummary` | MES 查询 |
| GET | `/api/business-console/v1/mes/production-reports` | `listBusinessConsoleMesProductionReports` | 现有 MES 服务列表 |
| POST | `/api/business-console/v1/mes/production-reports` | `recordBusinessConsoleMesProductionReport` | 现有 MES 服务命令 |
| POST | `/api/business-console/v1/mes/defects` | `recordBusinessConsoleMesDefect` | MES 不良上下文，下游为 Quality |
| GET | `/api/business-console/v1/mes/related-quality-items` | `listBusinessConsoleMesRelatedQualityItems` | Quality 读取 facade |
| GET | `/api/business-console/v1/mes/finished-goods-receipt-requests` | `listBusinessConsoleMesFinishedGoodsReceiptRequests` | 现有 MES 服务列表 |
| POST | `/api/business-console/v1/mes/finished-goods-receipt-requests` | `createBusinessConsoleMesFinishedGoodsReceiptRequest` | 现有 MES 服务命令 |
| GET | `/api/business-console/v1/mes/downtime-events` | `listBusinessConsoleMesDowntimeEvents` | MES/Maintenance/Telemetry 聚合 |
| POST | `/api/business-console/v1/mes/downtime-events` | `recordBusinessConsoleMesDowntimeEvent` | MES 命令 |
| POST | `/api/business-console/v1/mes/downtime-events/{downtimeEventId}/recover` | `confirmBusinessConsoleMesDowntimeRecovery` | MES 恢复命令及 Maintenance 上下文 |
| GET | `/api/business-console/v1/mes/shift-handovers` | `listBusinessConsoleMesShiftHandovers` | MES 查询 |
| POST | `/api/business-console/v1/mes/shift-handovers` | `createBusinessConsoleMesShiftHandover` | MES 命令 |
| POST | `/api/business-console/v1/mes/shift-handovers/{handoverId}/accept` | `acceptBusinessConsoleMesShiftHandover` | MES 命令 |
| GET | `/api/business-console/v1/mes/traceability/work-orders/{workOrderId}` | `getBusinessConsoleMesWorkOrderTraceability` | MES 谱系查询 |
| GET | `/api/business-console/v1/mes/traceability/batches/{batchOrSerial}` | `getBusinessConsoleMesBatchTraceability` | MES 谱系查询 |
| GET | `/api/business-console/v1/mes/traceability/material-lots/{materialLotId}` | `getBusinessConsoleMesMaterialLotTraceability` | MES 谱系查询 |
| GET | `/api/business-console/v1/mes/capacity-impacts` | `listBusinessConsoleMesCapacityImpacts` | 现有 MES 服务列表 |

## 权限目标

在 `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/Auth/BusinessGatewayAuthorization.cs` 中显式定义 Business Console 权限；`BusinessGatewayPermissions` 当前位于此处。在实施 PR 中添加匹配的 IAM seed/catalog 和 `docs/architecture/authorization-matrix.md` 条目。不得让无关读取页面回落到 `business.mes.work-orders.manage`。下表路由省略共享前缀 `/api/business-console/v1`。

| 权限常量 | 权限码 | 路由 |
| --- | --- | --- |
| `MesFoundationRead` | `business.mes.foundation.read` | 所有 `/mes/foundation-readiness*` 路由。 |
| `MesOverviewRead` | `business.mes.overview.read` | `/mes/overview`. |
| `MesPlansRead` | `business.mes.plans.read` | `GET /mes/production-plans`、`GET /mes/production-plans/{productionPlanId}/readiness`。 |
| `MesWorkOrdersRead` | `business.mes.work-orders.read` | `GET /mes/work-orders`、`GET /mes/work-orders/{workOrderId}`。 |
| `MesWorkOrdersManage` | `business.mes.work-orders.manage` | 工单下达、急单创建和计划转工单。 |
| `MesMaterialsRead` | `business.mes.materials.read` | 物料就绪和领料请求列表。 |
| `MesMaterialsManage` | `business.mes.materials.manage` | 领料请求创建和线边收货确认。 |
| `MesDispatchRead` | `business.mes.dispatch.read` | 派工任务列表。 |
| `MesDispatchManage` | `business.mes.dispatch.manage` | 派工分配。 |
| `MesOperationsRead` | `business.mes.operations.read` | 工序任务列表和 WIP 摘要。 |
| `MesOperationsManage` | `business.mes.operations.manage` | 工序开始、暂停、恢复、完成、转移和冻结命令。 |
| `MesReportingRead` | `business.mes.reporting.read` | 生产报工列表。 |
| `MesReportingWrite` | `business.mes.reporting.write` | 生产报工创建。 |
| `MesQualityRead` | `business.mes.quality.read` | 相关质量事项和不良上下文下钻。 |
| `MesQualityWrite` | `business.mes.quality.write` | MES 执行不良创建。 |
| `MesReceiptsRead` | `business.mes.receipts.read` | 成品入库请求列表。 |
| `MesReceiptsManage` | `business.mes.receipts.manage` | 成品入库请求创建。 |
| `MesDowntimeRead` | `business.mes.downtime.read` | 停机事件列表。 |
| `MesDowntimeManage` | `business.mes.downtime.manage` | 停机事件创建和恢复确认。 |
| `MesHandoversRead` | `business.mes.handovers.read` | 班次交接列表。 |
| `MesHandoversManage` | `business.mes.handovers.manage` | 班次交接创建和接受。 |
| `MesTraceabilityRead` | `business.mes.traceability.read` | 工单、批次/序列号和物料批次追溯查询。 |
| `MesSchedulesRead` | `business.mes.schedules.read` | 排程结果/状态历史。 |
| `MesSchedulesManage` | `business.mes.schedules.manage` | 规则排程运行。 |
| `MesCapacityRead` | `business.mes.capacity.read` | 产能影响列表。 |

MES 服务的 `MesPermissionCodes` 应以相同粒度映射 MES 自有 endpoint 意图，供契约元数据使用。源服务保留自己的权限目录；BusinessGateway 在转发内部 bearer 调用前仍执行终端用户授权检查。

所有目标路由必须保持现有 BusinessGateway 模式：

1. Gateway endpoint 使用 `AuthorizedBusinessProxyEndpoint`。
2. Gateway endpoint 权限来自 `BusinessGatewayPermissions`。
3. Gateway 将 `tokenProvider.BearerToken` 转发给业务服务。
4. 业务服务继续受 `InternalServiceAuthorizationPolicy` 保护。
5. 前端只使用 `@nerv-iip/api-client` 稳定的 business-console 导出。

## Task 0：生产基础就绪

**文件：**
- 修改：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessConsoleModels.cs`
- 修改：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessServiceClients.cs`
- 修改：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/Auth/BusinessGatewayAuthorization.cs`
- 修改：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Mes/BusinessConsoleMesEndpoints.cs`
- endpoint 缺失时审核并修改：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Endpoints/*`，用于生产基础就绪。
- endpoint 缺失时审核并修改：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Endpoints/*`，用于生产版本就绪。
- endpoint 缺失时审核并修改：`backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Endpoints/*`，用于条码和标签规则就绪。
- 测试：`backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayOpenApiTests.cs`
- 测试：`backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayProxyTests.cs`

- [ ] **步骤 1：定义就绪 DTO**

添加名称和原因码稳定的 Business Console DTO：

```csharp
public sealed record BusinessConsoleMesFoundationReadinessRequest(
    string OrganizationId,
    string EnvironmentId,
    string? SiteCode,
    string? LineCode,
    string? WorkCenterCode,
    string? SkuId,
    string? ProductionVersionId,
    DateTimeOffset? PlannedStartUtc,
    DateTimeOffset? PlannedEndUtc);

public sealed record BusinessConsoleMesFoundationReadinessResponse(
    string Status,
    IReadOnlyCollection<BusinessConsoleMesReadinessArea> Areas,
    IReadOnlyCollection<BusinessConsoleMesReadinessIssue> BlockingIssues,
    IReadOnlyCollection<BusinessConsoleMesReadinessIssue> WarningIssues);

public sealed record BusinessConsoleMesReadinessArea(
    string AreaCode,
    string Status,
    IReadOnlyCollection<BusinessConsoleMesReadinessIssue> Issues);

public sealed record BusinessConsoleMesReadinessIssue(
    string Code,
    string Severity,
    string Message,
    string? SourceSystem,
    string? ReferenceType,
    string? ReferenceId,
    string? ReferenceDisplayName,
    DateTimeOffset? EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    string? Version,
    string? FixHint);
```

首个版本使用这些领域代码：`master-data`、`product-engineering`、`supply`、`quality`、`equipment`、`barcode-numbering` 和 `iam-context`。
仅使用这些状态值：`Ready`、`Warning` 和 `Blocked`。
使用“原因码基线”表中的原因码；只有同时添加 gateway 契约测试和页面渲染断言时，才可新增代码。

- [ ] **步骤 2：编写基础就绪 gateway 测试**

添加测试，证明 `GET /api/business-console/v1/mes/foundation-readiness`：

1. 需要已通过身份验证的 Business Console 用户 bearer。
2. 使用 `BusinessGatewayPermissions.MesFoundationRead` 调用 IAM 授权。
3. 使用内部服务 bearer 令牌调用下游读取客户端。
4. 任一 P0 领域返回阻塞问题时返回 `Blocked`。
5. 不存在阻塞但至少有一个警告时返回 `Warning`。
6. 所有领域均就绪时返回 `Ready`。
7. 保留源系统和引用 ID，使用户知道需要修复哪条基础记录。
8. 将源服务超时、5xx、无效 JSON 和缺少必要就绪字段转换为 `Blocked` 领域并附带 `SOURCE_SERVICE_UNAVAILABLE`，同时为 IAM 身份验证和授权失败保留正常 401/403 响应。

- [ ] **步骤 3：验证源服务解析器覆盖**

添加新 endpoint 前检查这些源服务：

```powershell
rg -n "Resolve|Validate|ProductionVersion|Barcode|Rule|WorkCalendar|Shift|PersonnelSkill|DeviceAsset|WorkCenter" backend/services/Business
```

现有解析器 endpoint 已返回“基础记录契约”字段时应直接复用。覆盖缺失时，只添加以下窄粒度读取 endpoint：

| 服务 | Endpoint 形态 | 必须回答 |
| --- | --- | --- |
| MasterData | `POST /api/business/master-data/v1/readiness/production-foundation` | 层级、工作日历、班次、团队、人员技能、SKU/UOM、工作中心、设备资产和资源能力就绪状态。 |
| ProductEngineering | `POST /api/business/product-engineering/v1/readiness/production-version` | 已发布生产版本、MBOM、工艺路线、工序序列、物料需求、标准时长和必要资源能力就绪状态。 |
| BarcodeLabel | `POST /api/business/barcode-label/v1/readiness/rules` | MES 单据/物料/产品/入库/追溯用例的条码规则、标签模板、打印机映射和扫描规则就绪状态。 |

不得在 MES 内添加宽泛的基础数据维护界面或 CRUD endpoint。

- [ ] **步骤 4：添加编号就绪契约**

对于 MES 自有单据，在命令创建记录前添加服务端编号规则检查：

| 单据 | 必要前缀示例 | 规则归属 |
| --- | --- | --- |
| 工单 | `MO` | MES 服务本地策略 |
| 工序任务 | `OP` | MES 服务本地策略 |
| 领料请求 | `MI` | MES 服务本地策略 |
| 生产报工 | `PR` | MES 服务本地策略 |
| 不良记录 | `DF` | MES 服务本地策略 |
| 停机事件 | `DT` | MES 服务本地策略 |
| 成品入库请求 | `FG` | MES 服务本地策略 |
| 班次交接 | `SH` | MES 服务本地策略 |

首轮实施可以将生成器保留在 MES 内，但规则形态必须足够明确，以便未来在不改变 Business Console 契约的情况下迁移到共享 Numbering 服务。

- [ ] **步骤 5：运行 gateway 聚焦测试**

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --no-restore
```

实施后预期：通过。

- [ ] **步骤 6：提交基础就绪变更**

```powershell
git add backend/gateway/BusinessGateway backend/services/Business/MasterData backend/services/Business/ProductEngineering backend/services/Business/BarcodeLabel docs/architecture/authorization-matrix.md
git commit -m "feat: add mes foundation readiness contracts"
```

## Task 1：契约缺口图与首批失败测试

**文件：**
- 修改：`backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayOpenApiTests.cs`
- 修改：`backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayProxyTests.cs`
- 若当前文档缺少新增路由的 BusinessGateway 导出预期，则修改：`docs/architecture/api-contract-and-codegen.md`。

- [ ] **步骤 1：编写 OpenAPI operationId 断言**

为“契约目标”表中的每条路由添加断言。保持 `BusinessGatewayOpenApiTests.cs` 中现有断言风格，例如：

```csharp
AssertOperationId(paths, "/api/business-console/v1/mes/production-reports", "get", "listBusinessConsoleMesProductionReports");
AssertOperationId(paths, "/api/business-console/v1/mes/finished-goods-receipt-requests", "get", "listBusinessConsoleMesFinishedGoodsReceiptRequests");
AssertOperationId(paths, "/api/business-console/v1/mes/capacity-impacts", "get", "listBusinessConsoleMesCapacityImpacts");
```

- [ ] **步骤 2：实施前编写代理测试**

添加测试，证明每个新 facade：

1. 拒绝未通过身份验证的请求。
2. 使用预期权限码调用 IAM 授权。
3. 转发 `organizationId`、`environmentId`、ID、筛选条件和 `take`。
4. 向下游发送内部服务 bearer 令牌。
5. IAM 拒绝访问时不调用下游服务。

- [ ] **步骤 3：运行预期失败的 gateway 测试**

运行：

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --no-restore
```

此时预期：失败，因为新路由、客户端、模型和权限尚不存在。

- [ ] **步骤 4：仅提交测试**

```powershell
git add backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayOpenApiTests.cs backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayProxyTests.cs docs/architecture/api-contract-and-codegen.md
git commit -m "test: define mes pc workbench business gateway contracts"
```

## Task 2：MES 服务读取界面

**文件：**
- 修改：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Endpoints/Mes/MesEndpoints.cs`
- 创建或修改：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/WorkOrders/*`
- 创建或修改：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Production/*`
- 测试：`backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/*`

- [ ] **步骤 1：添加缺失的 MES 服务测试**

编写以下服务级测试：

1. `GET /api/business/v1/mes/work-orders/{workOrderId}` 返回一个工单，包含工序任务、下达快照、物料就绪摘要、质量状态、设备状态和入库状态。
2. `GET /api/business/v1/mes/operation-tasks` 可按组织、环境、状态、工作中心、设备、班次、团队和工单筛选。
3. `POST /api/business/v1/mes/work-orders/{workOrderId}/release` 在生产版本、工艺路线、关键物料、质量冻结或设备可用性阻塞执行时拒绝下达；策略允许人工确认时，允许带警告下达。
4. `GET /api/business/v1/mes/work-orders/{workOrderId}/material-readiness` 返回需求数量、可用数量、请求数量、备料数量、收货数量、短缺数量、替代料可用性和阻塞原因。
5. `POST /api/business/v1/mes/work-orders/{workOrderId}/material-issue-requests` 创建 MES 物料请求意图，不直接创建仓库任务。
6. `POST /api/business/v1/mes/dispatch-tasks/{operationTaskId}/assign` 记录人员/设备/班次分配，并按规则阻止使用不可用设备或处于质量冻结的对象。
7. `POST /api/business/v1/mes/operation-tasks/{operationTaskId}/start|pause|resume|complete` 改变工序任务状态，并保留便于审计的时间戳和操作者 ID。
8. 现有 `GET /api/business/v1/mes/production-reports` 继续可用，报工创建可包含合格、报废、返工、人工工时、机器工时、物料批次/序列号证据和附件。
9. `POST /api/business/v1/mes/defects` 记录执行不良上下文，并在可用时关联 Quality/NCR 下游标识符。
10. 现有 `GET /api/business/v1/mes/finished-goods-receipt-requests` 继续可用。
11. 现有 `GET /api/business/v1/mes/capacity-impacts` 继续可用。
12. `POST /api/business/v1/mes/downtime-events` 和恢复确认记录影响生产的停机。
13. `POST /api/business/v1/mes/shift-handovers` 将未解决的生产/物料/质量/设备/入库问题传递到下一班次。
14. `GET /api/business/v1/mes/wip` 按工单、工序、工作中心、状态、阻塞原因、班次、团队和计划/实际数量返回 WIP 计数。
15. 追溯查询至少返回工单、生产版本、工序任务、报工、物料批次、不良、停机、入库请求、人员和设备关联。

- [ ] **步骤 2：实施缺失的读取查询**

只添加尚不存在的 MES 服务查询和命令。使用带 `CancellationToken` 的异步 EF Core 调用，并将查询/endpoint DTO 保留在 Web/Application 层，而不是 Domain 层。

预期新增的 endpoint 契约：

```csharp
new(typeof(GetMesWorkOrderDetailEndpoint), "GET", "/api/business/v1/mes/work-orders/{workOrderId}", MesPermissionCodes.WorkOrdersRead, "getBusinessMesWorkOrderDetail"),
new(typeof(ListOperationTasksEndpoint), "GET", "/api/business/v1/mes/operation-tasks", MesPermissionCodes.OperationsRead, "listBusinessMesOperationTasks"),
new(typeof(GetMaterialReadinessEndpoint), "GET", "/api/business/v1/mes/work-orders/{workOrderId}/material-readiness", MesPermissionCodes.MaterialsRead, "getBusinessMesMaterialReadiness"),
new(typeof(AssignDispatchTaskEndpoint), "POST", "/api/business/v1/mes/dispatch-tasks/{operationTaskId}/assign", MesPermissionCodes.DispatchManage, "assignBusinessMesDispatchTask"),
new(typeof(GetWipSummaryEndpoint), "GET", "/api/business/v1/mes/wip", MesPermissionCodes.OperationsRead, "getBusinessMesWipSummary"),
new(typeof(RecordDowntimeEventEndpoint), "POST", "/api/business/v1/mes/downtime-events", MesPermissionCodes.DowntimeManage, "recordBusinessMesDowntimeEvent"),
new(typeof(CreateShiftHandoverEndpoint), "POST", "/api/business/v1/mes/shift-handovers", MesPermissionCodes.HandoversManage, "createBusinessMesShiftHandover"),
new(typeof(GetWorkOrderTraceabilityEndpoint), "GET", "/api/business/v1/mes/traceability/work-orders/{workOrderId}", MesPermissionCodes.TraceabilityRead, "getBusinessMesWorkOrderTraceability"),
```

将每个新 MES 服务权限常量添加到 `MesPermissionCodes.All` 及其 endpoint 契约测试中。服务 endpoint 继续受 `InternalServiceAuthorizationPolicy` 保护；权限码仍是契约/目录元数据，不是终端用户 bearer 授权决策。

- [ ] **步骤 3：运行 MES 聚焦测试**

```powershell
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj --no-restore
```

实施后预期：通过。

- [ ] **步骤 4：提交 MES 服务界面**

```powershell
git add backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests
git commit -m "feat: expose mes pc workbench read surface"
```

## Task 3：扩展 BusinessGateway MES Facade

**文件：**
- 修改：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessConsoleModels.cs`
- 修改：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessServiceClients.cs`
- 修改：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/Auth/BusinessGatewayAuthorization.cs`
- 修改：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Mes/BusinessConsoleMesEndpoints.cs`
- 测试：`backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayOpenApiTests.cs`
- 测试：`backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayProxyTests.cs`

- [ ] **步骤 1：添加 Business Console MES DTO**

为以下内容添加紧凑 DTO：

1. MES 驾驶舱计数、阻塞摘要和特定角色待办工作。
2. 生产计划就绪行和下达风险详情。
3. 工单详情和下达快照。
4. 物料就绪、短缺、领料/备料请求、线边收货和物料消耗证据行。
5. 派工任务行以及分配请求/响应。
6. 工序任务行以及开始/暂停/恢复/完成响应。
7. WIP 摘要行。
8. 生产报工行。
9. 不良/不合格执行上下文行。
10. 成品入库请求行。
11. 停机和设备影响行。
12. 班次交接行。
13. 追溯图/列表行。
14. 产能影响行。
15. 相关质量事项行。

保持 DTO 属性名稳定且面向前端，例如 `productionPlanId`、`workOrderId`、`operationTaskId`、`materialId`、`materialLotId`、`batchOrSerial`、`status`、`readinessStatus`、`blockingReasons`、`workCenterId`、`deviceAssetId`、`shiftId`、`assignedUserId`、`plannedStartUtc`、`startedAtUtc`、`reportedAtUtc`、`qualityStatus`、`receiptStatus` 和 `handoverStatus`。

- [ ] **步骤 2：添加内部客户端方法**

为“契约目标”表中的 MES 路由扩展 `IBusinessMesClient` 和 `HttpBusinessMesClient`。只有页面需要非 MES 事实归属方时，才添加独立客户端接口：

```csharp
Task<BusinessConsoleMesProductionReportListResponse> ListProductionReportsAsync(
    string internalBearerToken,
    BusinessConsoleMesProductionReportListRequest request,
    CancellationToken cancellationToken);

Task<BusinessConsoleMesFinishedGoodsReceiptRequestListResponse> ListFinishedGoodsReceiptRequestsAsync(
    string internalBearerToken,
    BusinessConsoleMesReceiptRequestListRequest request,
    CancellationToken cancellationToken);

Task<BusinessConsoleMesCapacityImpactListResponse> ListCapacityImpactsAsync(
    string internalBearerToken,
    BusinessConsoleMesCapacityImpactListRequest request,
    CancellationToken cancellationToken);

Task<BusinessConsoleMesMaterialReadinessResponse> GetMaterialReadinessAsync(
    string internalBearerToken,
    string workOrderId,
    BusinessConsoleMesContextRequest request,
    CancellationToken cancellationToken);

Task<BusinessConsoleMesTraceabilityResponse> GetWorkOrderTraceabilityAsync(
    string internalBearerToken,
    string workOrderId,
    BusinessConsoleMesContextRequest request,
    CancellationToken cancellationToken);
```

- [ ] **步骤 3：添加 facade endpoint**

按照现有 endpoint 风格，在 `BusinessConsoleMesEndpoints.cs` 中为每条路由添加一个 FastEndpoints 类。不得在 startup 文件中放置路由映射。

- [ ] **步骤 4：使用窄粒度权限**

严格实施“权限目标”表：

1. 将缺失常量添加到 `BusinessGatewayPermissions`；该对象位于 `BusinessGatewayAuthorization.cs`。
2. 将每个 BusinessGateway endpoint 映射到表中权限；不得为基础、物料、派工、工序、质量、入库、停机、交接、追溯、排程读取或产能读取页面复用 `MesWorkOrdersManage`。
3. 为每个新权限码添加 IAM seed/catalog 和 `docs/architecture/authorization-matrix.md` 行。
4. 添加 gateway 测试，证明每个 MES 领域中至少一条读取路由与一条写入路由使用不同权限码。

- [ ] **步骤 5：运行 gateway 测试**

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --no-restore
```

实施后预期：通过。

- [ ] **步骤 6：提交 gateway facade**

```powershell
git add backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests docs/architecture/authorization-matrix.md
git commit -m "feat: expand mes business console facade"
```

## Task 4：OpenAPI 快照与生成客户端

**文件：**
- 修改生成的快照：`frontend/packages/api-client/openapi/business-gateway-console.v1.json`
- 修改以下目录中的生成客户端文件：`frontend/packages/api-client/src/generated/business-console/`
- 修改稳定导出：`frontend/packages/api-client/src/business-console.ts`
- 测试：`frontend/packages/api-client/src/generated-contract.test.ts`

- [ ] **步骤 1：导出 BusinessGateway OpenAPI**

使用仓库现有受治理的 OpenAPI 导出路径。不得手工编辑 OpenAPI JSON。

- [ ] **步骤 2：重新生成前端 API 客户端**

```powershell
pnpm -C frontend generate:api
```

预期：生成的 business-console 客户端包含新 operation 函数和 Pinia Colada 查询/变更选项。

- [ ] **步骤 3：添加稳定导出**

只从 `frontend/packages/api-client/src/business-console.ts` 导出必要的 MES 工作台函数和类型别名。应用代码不得深层导入生成文件。

- [ ] **步骤 4：更新生成契约测试**

为新查询/变更选项和稳定导出添加 `expect(...).toBeTypeOf('function')` 断言。

- [ ] **步骤 5：运行 api-client 测试**

```powershell
pnpm -C frontend --filter @nerv-iip/api-client test
pnpm -C frontend --filter @nerv-iip/api-client typecheck
```

实施后预期：通过。

- [ ] **步骤 6：提交契约产物**

```powershell
git add frontend/packages/api-client
git commit -m "feat: generate mes pc business console client"
```

## Task 5：PC MES 组合式函数

**文件：**
- 修改：`frontend/apps/business-console/src/composables/useBusinessMes.ts`
- 创建：`frontend/apps/business-console/src/composables/mes/useMesWorkbench.ts`
- 创建：`frontend/apps/business-console/src/composables/mes/useMesReferenceLabels.ts`
- 测试：`frontend/apps/business-console/src/**/__tests__` 或 `frontend/apps/business-console/tests` 下现有或新增 Vitest 文件

- [ ] **步骤 1：添加查询封装**

暴露以下 composable 函数：

1. `useMesOverview()`
2. `useMesFoundationReadiness()`
3. `useMesProductionPlans()`
4. `useMesProductionPlanReadiness(productionPlanId)`
5. `useMesWorkOrders()`
6. `useMesWorkOrderDetail(workOrderId)`
7. `useMesMaterialReadiness(workOrderId)`
8. `useMesMaterialIssueRequests()`
9. `useMesDispatchTasks()`
10. `useMesOperationTasks()`
11. `useMesWipSummary()`
12. `useMesProductionReports()`
13. `useMesQualityContext()`
14. `useMesFinishedGoodsReceiptRequests()`
15. `useMesDowntimeEvents()`
16. `useMesShiftHandovers()`
17. `useMesTraceability()`
18. `useMesCapacityImpacts()`
19. `useMesSchedules()`

- [ ] **步骤 2：替换硬编码上下文来源**

只在一个显式的应用本地 helper 后保留现有 `org-001` 和 `env-dev` 开发默认值，使页面未来可迁移到真实上下文选择器，而无需编辑每个表单。

- [ ] **步骤 3：添加失效规则**

计划转换、工单下达、领料请求创建、线边物料收货、派工分配、工序状态变更、生产报工创建、不良录入、成品入库请求创建、停机恢复、班次交接接受或排程运行后，按 operation ID 使受影响的 MES 查询失效。复用现有 `isBusinessQuery` 模式。

- [ ] **步骤 4：运行 Business Console 类型检查**

```powershell
pnpm -C frontend --filter @nerv-iip/business-console typecheck
```

实施后预期：通过。

- [ ] **步骤 5：提交 composable**

```powershell
git add frontend/apps/business-console/src/composables
git commit -m "feat: add mes pc workbench composables"
```

## Task 6：采用中文文案的 PC MES 页面

**文件：**
- 创建：`frontend/apps/business-console/src/pages/mes/index.vue`
- 创建：`frontend/apps/business-console/src/pages/mes/foundation.vue`
- 创建：`frontend/apps/business-console/src/pages/mes/plans.vue`
- 修改：`frontend/apps/business-console/src/pages/mes/work-orders.vue`
- 创建：`frontend/apps/business-console/src/pages/mes/work-orders/[workOrderId].vue`
- 创建：`frontend/apps/business-console/src/pages/mes/materials.vue`
- 创建：`frontend/apps/business-console/src/pages/mes/dispatch.vue`
- 创建：`frontend/apps/business-console/src/pages/mes/operation-tasks.vue`
- 创建：`frontend/apps/business-console/src/pages/mes/reports.vue`
- 创建：`frontend/apps/business-console/src/pages/mes/quality.vue`
- 创建：`frontend/apps/business-console/src/pages/mes/receipts.vue`
- 修改：`frontend/apps/business-console/src/pages/mes/schedules.vue`
- 创建：`frontend/apps/business-console/src/pages/mes/downtime.vue`
- 创建：`frontend/apps/business-console/src/pages/mes/handovers.vue`
- 创建：`frontend/apps/business-console/src/pages/mes/traceability.vue`
- 创建：`frontend/apps/business-console/src/pages/mes/capacity.vue`
- 修改：`frontend/apps/business-console/src/layouts/BusinessLayout.vue`
- 测试：`frontend/apps/business-console/tests/e2e/business-console.spec.ts`

- [ ] **步骤 1：构建桌面 MES 导航**

使用中文标签将 MES 页面添加到 Business Console 导航：

| 路由 | 标签 |
| --- | --- |
| `/mes` | `生产驾驶舱` |
| `/mes/foundation` | `基础准备` |
| `/mes/plans` | `生产计划` |
| `/mes/work-orders` | `计划与工单` |
| `/mes/materials` | `齐套与物料` |
| `/mes/dispatch` | `派工看板` |
| `/mes/operation-tasks` | `工序执行` |
| `/mes/reports` | `报工与完工` |
| `/mes/quality` | `质量与不良` |
| `/mes/receipts` | `完工入库` |
| `/mes/schedules` | `规则排程` |
| `/mes/downtime` | `设备与停机` |
| `/mes/handovers` | `班次交接` |
| `/mes/traceability` | `追溯查询` |
| `/mes/capacity` | `产能影响` |

- [ ] **步骤 2：替换可见的英文 MES 文案**

本阶段所有可见 MES 页面文本必须是中文文字。示例：

| 当前英文 | 必要中文 |
| --- | --- |
| `Work orders` | `生产工单` |
| `Create rush work order` | `创建急单` |
| `Record production report` | `提交生产报工` |
| `Run schedule` | `运行排程` |
| `No work orders returned.` | `暂无生产工单。` |
| `Material readiness` | `齐套检查` |
| `Issue request` | `领料申请` |
| `Dispatch` | `派工` |
| `Downtime` | `停机` |
| `Traceability` | `追溯` |
| `Organization` | `组织` |
| `Environment` | `环境` |
| `Status` | `状态` |
| `Take` | `数量上限` |

本任务不得引入新翻译目录或语言区域切换器。

对于现有 `frontend/apps/business-console/src/pages/mes/schedules.vue`，替换所有可见英文文案，使用 `useMesSchedules()`，将页面保持为规则排程结果/状态工作台，并且本任务不得添加完整 APS/Gantt 行为。

- [ ] **步骤 3：实施页面状态**

每个页面必须覆盖加载、空、错误、成功和禁用提交状态。只使用 `Spinner`、`TableEmpty`、`Badge`、`Button`、`Field` 及相关 `@nerv-iip/ui` 导出。

- [ ] **步骤 4：保持页面面向运营而非营销风格**

使用高密度表格、筛选器、简洁指标和直接操作面板。不得添加落地页主视觉区或装饰性卡片布局。

- [ ] **步骤 5：添加路由级冒烟覆盖**

扩展 Playwright 冒烟测试，打开每条 MES 路由，并断言至少出现一个中文标题或表格标签。

- [ ] **步骤 6：运行前端聚焦检查**

```powershell
pnpm -C frontend --filter @nerv-iip/business-console typecheck
pnpm -C frontend --filter @nerv-iip/business-console test
pnpm -C frontend --filter @nerv-iip/business-console build
pnpm -C frontend --filter @nerv-iip/business-console e2e -- business-console.spec.ts
```

实施后预期：通过。若缺少本地 Playwright 托管浏览器，将 `PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH` 设为已安装的 Chromium/Chrome 路径并重新运行一次。

- [ ] **步骤 7：提交 PC MES 页面**

```powershell
git add frontend/apps/business-console/src frontend/apps/business-console/tests
git commit -m "feat: complete mes pc business console pages"
```

## Task 7：最小跨领域 MES 上下文

**文件：**
- 修改：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessServiceClients.cs`
- 修改：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessConsoleModels.cs`
- 修改或创建以下目录中的 endpoint 文件：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Mes/`
- 测试：`backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayProxyTests.cs`
- 仅在显示新增上下文的位置修改前端页面。

- [ ] **步骤 1：只添加 MES 页面所需上下文**

按以下顺序实施跨领域读取：

1. 用于生产计划就绪的 DemandPlanning/ERP 源计划和优先级上下文。
2. SKU、工作中心、生产线、班次、团队、设备资产和生产供应区的 MasterData 标签。
3. 工单详情上的 ProductEngineering 生产版本、MBOM、工艺路线、作业指导书和有效版本摘要。
4. Inventory/WMS 可用性、预留、领料/备料状态、线边收货、退料/补料，以及下游成品入库证据。
5. 与工单和工序任务相关的 Quality 检验任务、冻结、不良、NCR、返工、报废和处置行。
6. 若 MES 查询只返回 ID，则添加 Maintenance/IndustrialTelemetry 资产状态、告警、停机、恢复和产能影响标签。
7. 只有在现有 ERP 界面经验证时，才添加 ERP Finance 源单据链接。

- [ ] **步骤 2：保持基于 ID 的后备方案**

若相关服务尚无稳定读取 endpoint，显示 MES 原始 ID，且不得阻塞 MES 页面。在实施 PR 描述中记录缺失的读取 endpoint，不得编造数据。

- [ ] **步骤 3：运行 gateway 和前端检查**

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --no-restore
pnpm -C frontend --filter @nerv-iip/business-console typecheck
```

实施后预期：通过。

- [ ] **步骤 4：提交跨领域上下文**

```powershell
git add backend/gateway/BusinessGateway frontend/apps/business-console/src
git commit -m "feat: add mes related business context"
```

## Task 8：聚焦验证脚本

**文件：**
- 创建：`scripts/verify-business-console-mes-pc-workbench.ps1`
- 修改：`docs/architecture/implementation-readiness.md`
- 修改：`docs/architecture/frontend-structure.md`
- 若路由/契约文档发生变化则修改：`docs/architecture/api-contract-and-codegen.md`

- [ ] **步骤 1：创建受治理脚本**

脚本必须 dot-source `scripts/lib/ScriptAutomation.ps1`，并使用 `Invoke-DotNet`、`Invoke-Pnpm` 等 helper 函数。不得直接调用 `dotnet`、`pnpm` 或 `pwsh`。

脚本应运行：

```powershell
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj --no-restore
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --no-restore
pnpm -C frontend generate:api
pnpm -C frontend --filter @nerv-iip/api-client typecheck
pnpm -C frontend --filter @nerv-iip/api-client test
pnpm -C frontend --filter @nerv-iip/business-console typecheck
pnpm -C frontend --filter @nerv-iip/business-console test
pnpm -C frontend --filter @nerv-iip/business-console build
```

- [ ] **步骤 2：添加可选 e2e 模式**

支持按需启用以下命令的开关：

```powershell
pnpm -C frontend --filter @nerv-iip/business-console e2e -- business-console.spec.ts
```

说明可能需要本地 Chrome/Chromium 可执行文件。

- [ ] **步骤 3：运行脚本治理**

```powershell
scripts/check-script-governance.ps1
```

实施后预期：通过。

- [ ] **步骤 4：运行聚焦验证脚本**

```powershell
scripts/verify-business-console-mes-pc-workbench.ps1
```

实施后预期：通过。

- [ ] **步骤 5：以已验证事实更新架构文档**

只有脚本通过后，才更新：

1. `docs/architecture/frontend-structure.md` 中新 MES 页面的路由表。
2. `docs/architecture/implementation-readiness.md` 中 Business Console PC MES 完善的当前代码事实条目。
3. 若 BusinessGateway 导出/代码生成命令或快照发生变化，则更新 `docs/architecture/api-contract-and-codegen.md`。

- [ ] **步骤 6：提交验证与文档**

```powershell
git add scripts/verify-business-console-mes-pc-workbench.ps1 docs/architecture/implementation-readiness.md docs/architecture/frontend-structure.md docs/architecture/api-contract-and-codegen.md
git commit -m "docs: record mes pc workbench verification"
```

## 最终验证

运行聚焦门禁：

```powershell
scripts/verify-business-console-mes-pc-workbench.ps1
```

然后运行与变更界面匹配的更广泛前端和后端检查：

```powershell
dotnet test backend/Nerv.IIP.sln --no-restore
pnpm -C frontend typecheck
pnpm -C frontend test
pnpm -C frontend build
```

若未运行依赖 Docker 的门禁，须在 PR 中明确说明 Docker 阻塞。

## 推进顺序

1. 首先合并 Task 0 基础就绪契约：MasterData、ProductEngineering、WMS/Inventory、Quality、Maintenance/Telemetry、BarcodeLabel、IAM、源服务失败处理、权限和编号检查。
2. 在扩展页面前合并 Task 1 API/BFF 契约工作。
3. 按以下顺序实施 Task 2 标准 P0 MES 服务界面：计划就绪与工单下达、物料就绪/请求、派工与工序状态、WIP、报工/质量/停机、入库/交接/追溯。
4. 契约之后立即合并生成客户端和组合式函数。
5. 合并具有中文文案和面向角色导航的 PC MES 页面。
6. 若最小跨领域上下文使审核规模过大，可作为后续工作添加，但不得从目标模型中删除基础就绪、物料就绪、派工、停机、交接或追溯。
7. MES 桌面流程可用后，启动 WMS 工作台、DemandPlanning/MRP、ERP 下钻、Quality 深度工作流和 Maintenance/Telemetry PC 页面。
8. 只有 MES PC 契约和主要流程停止变化后，才启动 PDA/移动端。

## 验收清单

- [ ] BusinessGateway 暴露“契约目标”表中的 MES PC 工作台路由。
- [ ] BusinessGateway 和 IAM 暴露“权限目标”矩阵，读写路由没有合并到一个宽泛管理权限中。
- [ ] BusinessGateway 测试覆盖身份验证、权限、上下文传递、内部 bearer 转发和下游拒绝行为。
- [ ] 基础就绪在 MasterData、ProductEngineering、WMS/Inventory、Quality、Maintenance/Telemetry、BarcodeLabel、IAM 和编号领域返回 `Ready`、`Warning` 或 `Blocked`。
- [ ] 基础就绪将源服务超时、5xx、无效 JSON 或格式错误的就绪载荷转换为 `Blocked` 领域并附带 `SOURCE_SERVICE_UNAVAILABLE`。
- [ ] 工单下达存储主数据、生产版本、物料就绪、质量要求、设备/人员分配、条码规则和生成单据 ID 的快照。
- [ ] P0 执行事实具有 MES 服务 endpoint：计划就绪、工单下达、物料就绪/请求意图、派工、工序状态、WIP、报工、不良上下文、停机、入库请求、班次交接和追溯。
- [ ] 生成的 business-console 客户端导出稳定的 MES 工作台函数和类型。
- [ ] `frontend/apps/business-console/src/pages/mes` 下存在 PC MES 路由，包含基础就绪、生产驾驶舱、生产计划、物料就绪、派工、工序执行、报工、质量、入库、停机、交接和追溯页面。
- [ ] 首轮实施中用户可见的 MES 页面文案为中文。
- [ ] 任何页面均未直接调用业务服务 URL 或生成文件的深层导入。
- [ ] MES 可查看并触发领料/备料流程，但 WMS/Inventory 仍是仓库执行和库存余额的事实来源。
- [ ] MES 可查看质量、停机和维护上下文，但 Quality 和 Maintenance 仍各自是其事实来源服务。
- [ ] MES 可使用条码和标签规则，但 BarcodeLabel 仍是模板、打印批次和扫描记录的事实来源。
- [ ] MES 单据 ID 使用显式编号规则在服务端生成；无需用户手工编造持久 ID。
- [ ] 追溯可从工单、批次/序列号、物料批次或不良开始，并返回关联执行证据。
- [ ] `scripts/verify-business-console-mes-pc-workbench.ps1` 通过。
- [ ] 实施证据存在后，已更新 `docs/architecture/frontend-structure.md` 和 `docs/architecture/implementation-readiness.md`。
- [ ] PDA/移动端明确延后，直至 PC MES 契约稳定。
