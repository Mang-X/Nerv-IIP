# MES 运行基础重置实施计划

> **面向代理执行者：**必须使用子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项实施本计划。步骤使用复选框（`- [ ]`）语法进行跟踪。

**目标：**让 MES PC 交付重新建立在真实运行基础上，使减震器制造流程能够从需求、工程和供应就绪一路运行到工单下达、派工、报工、入库和追溯。

**架构：**不再将 MES 页面视为首要交付物。先构建来源事实与服务端业务行为，通过 BusinessGateway 暴露，再实现引导真实用户完成关联工作流的中文 PC 页面。MES 仅拥有执行事实；MasterData、ProductEngineering、DemandPlanning、Scheduling/APS lite、ERP、Inventory、WMS、Quality、BarcodeLabel、Maintenance 和 IndustrialTelemetry 仍是各自领域事实的所有者。

**技术栈：**.NET 10、FastEndpoints、CleanDDD、EF Core PostgreSQL、BusinessGateway facade、由 Hey API 生成的 `@nerv-iip/api-client`、Vue 3、Vite Plus、Pinia Colada、`@nerv-iip/ui`、Playwright。

---

## 重定基线决定

2026-05-26 PC 工作台计划为 MES 提供了广泛的页面与 facade 界面，但仍不足以交付。可用的 MES 不能从工单 CRUD 或静态页面数据起步，而是需要已发布的工程事实、有效主数据、可用物料、可采购供应、MRP 建议、服务端编号、下达快照和执行状态转换。

自本计划起，MES PC 工作受以下简单规则约束：

> 在页面所需的来源事实可以维护或导入、能通过后端契约解析、能在 UI 中选择，并能通过端到端减震器制造场景验证之前，该页面不得视为达到交付就绪状态。

## 执行代理审核结论

两次委派审核得出了相同结论：

1. 仓库中已有的不只是页面。MasterData、ProductEngineering、DemandPlanning、ERP、Inventory、WMS、Quality、MES、Maintenance 和 IndustrialTelemetry 服务均已有真实聚合与 endpoint 界面。
2. 当前 MES PC 工作台在若干 P0 领域仍表现得像契约界面。部分就绪路径返回静态 `Ready`，生产计划可能为空，若干操作在没有持久化下游事实时便返回 `Accepted`。
3. 2026-05-26 计划提到了 BOM、工艺路线、生产版本、MRP、供应、质量、设备和编号，但未将其作为页面完成前的下达门禁。

审核指出的当前重要缺口如下：

| 缺口 | 代码事实 |
| --- | --- |
| 基础就绪状态可能返回静态 `Ready`，而不是检查来源事实。 | `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Workbench/MesWorkbenchQueries.cs` |
| 生产计划列表/就绪状态及计划转工单需要与 DemandPlanning/ERP 建议建立持久关联。 | `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Workbench/MesWorkbenchQueries.cs`; `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs` |
| 物料就绪、发料/线边收料尚未构成真实的 Inventory/WMS 闭环。 | `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/` 下的 MES 工作台 query 和 command |
| 质量上下文、班次交接和批次/物料追溯包含空响应或浅层响应。 | `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Workbench/` 下的 MES 工作台 query handler |
| DemandPlanning 已存在，但当前输入准备仍需真实的 ProductEngineering/Inventory/ERP 来源适配器，才能成为生产计划来源。 | `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Planning/PlanningInputAdapters.cs` |
| 若干创建流程仍可能要求用户提供持久业务 ID。 | MasterData、MES、ProductEngineering 和 ERP 创建请求目前包含用户提供的单据编号或代码。 |

## 减震器 P0 场景

使用一个真实产品族证明系统能够运行：

| 层次 | 所需场景事实 |
| --- | --- |
| 成品 | 前减震器总成和后减震器总成。 |
| 组件 | 活塞杆、外筒、活塞阀、密封套件、弹簧座、减震器油、纸箱、标签和托盘。 |
| 供应商 | 至少三个已批准供应商：机加工供应商、密封件/油品供应商和包装供应商。 |
| 工厂模型 | 一个工厂、两条生产线、四个工作中心：筒体焊接、杆件装配、注油/密封、阻尼测试/包装。 |
| 工程 | 每种成品均有已发布的 MBOM、工艺路线和 ProductionVersion；工序顺序包含标准时长、所需工作中心、所需技能和物料需求。 |
| 需求 | 一个销售订单、一项预测需求和一项安全库存补货需求。 |
| 计划 | MRP 创建一条计划工单建议，以及至少一条可追溯至需求的计划采购建议。 |
| 采购 | 采购建议可转为采购申请、RFQ 或采购订单；收货可为质量/库存就绪提供事实。 |
| MES | 已接受的生产建议转为工单，检查就绪状态、下达快照、创建发料请求、派发工序、记录生产、创建成品入库并支持追溯。 |

## P0 前置条件矩阵

| 能力 | P0 要求 | 所有者 | 当前待弥补缺口 |
| --- | --- | --- | --- |
| 服务端编号 | 在服务端生成 SKU、工程单据、BOM/工艺路线、生产版本、需求、MRP 运行、采购、销售、工单、工序任务、报工、缺陷、停机、交接和收货请求编号。 | 各归属服务，采用共享治理。 | 缺少完整的规则/计数器/并发/幂等策略。 |
| 物料主数据 | 维护带有角色标记、可追溯性、UOM 和质量要求的产品、半成品、原材料和包装记录。 | MasterData | Business Console 仅暴露狭窄的 SKU 页面，且仍要求用户输入代码。 |
| 合作方主数据 | 维护客户和供应商记录、角色、资质及启用状态。 | MasterData / ERP | 缺少供应商/客户页面和关联选择器，或其不够突出。 |
| 工厂/资源主数据 | 维护工厂、产线、工作中心、设备、班次、日历、团队、技能和资源能力。 | MasterData | 后端已存在，但 UI、种子数据和表单关联尚不完整。 |
| 工程发布 | 维护并发布 EBOM、MBOM、工艺路线、工序定义和 ProductionVersion。 | ProductEngineering | 后端已存在；Business Console 缺少可用的工程工作台，MES 尚未在每条路径强制使用已发布快照。 |
| 需求与 MRP | 创建销售/预测/安全库存需求，运行 MRP，展示需求追溯并接受计划采购/工单建议。 | DemandPlanning / ERP Sales | MRP 已存在，但来源适配器和 UI 工作流尚不完整。 |
| 采购供应 | 将采购建议转为采购单据，跟踪供应商报价、采购订单和收货就绪状态。 | ERP Procurement / WMS / Inventory / Quality | ERP 后端已存在；PC 页面和 MES 就绪关联尚不完整。 |
| 库存与线边供应 | 展示可用数量、质量状态、备料路线、发料请求和线边收料。 | Inventory / WMS / MES | MES 物料就绪当前需要真实 BOM 与 Inventory/WMS 关联。 |
| 质量门禁 | 解析检验计划、首件/过程/终检、质量冻结和 NCR 上下文。 | Quality / MES | MES 当前需要更强的质量就绪与下钻能力。 |
| 设备可用性 | 解析静态设备能力以及运行时维护/报警/停机可用性。 | MasterData / Maintenance / IndustrialTelemetry / MES | Maintenance/telemetry 事实已存在，但 PC 就绪关联尚不完整。 |
| MES 生命周期 | 将已接受计划转为工单，执行下达快照、派工、开始/暂停/恢复/完成、报工、入库和追溯。 | MES | 必须停止接受自由文本工单 ID，并用持久事实补齐稀疏 query handler。 |
| APS lite | 定义排程输入/输出契约、有限产能启发式排程、资源负载、冲突说明、锁定任务和插急单。 | Scheduling / MES / DemandPlanning / IndustrialTelemetry / Maintenance | P0 现已包括 #206 排程核心和 #207 设备运行时事实。完整优化器、仿真和自动重排留待后续。 |

## 交付阶段

### P0-A：运行基础门禁

**目标：**使来源事实可见，并强制执行“没有后端，就不算页面完成”规则。

**文件：**
- 修改：`docs/superpowers/plans/2026-05-26-business-console-mes-pc-completion.md`
- 修改：`frontend/DESIGN/roadmaps/business-console-mes-pc-workbench.md`
- 创建：`docs/superpowers/plans/2026-05-27-mes-operational-foundation-reset.md`
- 创建：`docs/superpowers/plans/2026-05-27-mes-operational-foundation-reset.html`

- [ ] **步骤 1：冻结本次重定基线**

在先前的 2026-05-26 计划中增加通知，说明后续 MES 工作改由本运行基础计划取代。

- [ ] **步骤 2：向 DESIGN 增加 UI 交付门禁**

增加规则：MES 页面必须由来源事实、关联选择器、服务端编号和中文业务文案支持，才能计为已交付。

- [ ] **步骤 3：验证文档**

运行：

```powershell
rg -n "待确认|待补充" docs/superpowers/plans/2026-05-27-mes-operational-foundation-reset.md docs/superpowers/plans/2026-05-27-mes-operational-foundation-reset.html frontend/DESIGN/roadmaps/business-console-mes-pc-workbench.md
```

预期：未引入未解决的占位符。产品文案禁止示例仍在 DESIGN 中作为反例记录，而不是产品 UI 文本。

### P0-B：编号与幂等创建

**目标：**从所有生成持久业务单据的创建流程中移除用户生成的系统 ID。

**2026-05-27 实施说明：**#188 已移除 Business Console SKU 创建、MES 插急单创建、MES 计划转工单和 MES 报工中的普通 UI/手工编号输入，并为 MasterData、MES、ProductEngineering、DemandPlanning 和 ERP 的 P0 创建 command 增加可选幂等键及服务本地持久编号分配。归属服务现已包含 `numbering_counters` 和 `numbering_idempotency_keys` migration、schema 约定测试及 schema 目录条目；MES 还为报工、发料、缺陷、停机、交接和成品入库请求流程分配持久业务编号。

**文件：**
- 修改：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Endpoints/MasterData/MasterDataEndpoints.cs`
- 修改：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Commands/MasterData/CreateMasterDataCommands.cs`
- 修改：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Endpoints/Mes/MesEndpoints.cs`
- 修改：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/WorkOrders/CreateRushWorkOrderCommand.cs`
- 修改：ProductEngineering、DemandPlanning 和 ERP 中目前需要用户提供编号的等效创建 command 文件。
- 测试：各受影响服务下的服务级 endpoint 与并发测试。

- [x] **步骤 1：增加服务本地编号规则与计数器聚合**

为每个服务创建编号规则和计数器表。按组织、环境、单据类型、可选场地/工厂前缀和日期段限定计数器作用域。在 Infrastructure 中使用乐观并发或行级锁；保留最终单据编号的唯一索引。

- [x] **步骤 2：在创建单据的同一事务内生成 ID**

创建 command 必须在一个工作单元中分配编号并持久化业务单据。UI 请求可以包含幂等键；除特权导入/覆盖路径外，不得包含系统 ID。

- [x] **步骤 3：增加重复与并发测试**

测试 20 个并行的 SKU 和 MES 工单创建请求。预期：所有持久化系统编号均唯一、在规则作用域内有序，且对相同幂等键的重试不会创建重复单据。

### P0-C：主数据工作台与种子场景

**目标：**让业务用户能够维护 MES 所需的工厂、资源、物料和合作方。

**文件：**
- 修改：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/MasterData/BusinessConsoleMasterDataEndpoints.cs`
- 修改：`frontend/apps/business-console/src/pages/master-data/skus.vue`
- 创建或补齐：合作方、资源、日历、团队、技能和设备的 Business Console 页面。
- 创建：减震器场景的确定性种子/导入 fixture。

- [ ] **步骤 1：以服务端生成的 ID 替换手工代码字段**

移除“生成”按钮和普通用户对系统代码的输入。物料类型、UOM、可追溯性、工厂、产线、工作中心、班次及供应商/客户角色应使用业务标签和选择器。

- [ ] **步骤 2：补齐关联选择器**

表单必须从真实 MasterData 资源中选择。工作中心筛选必须依赖工厂/产线；设备筛选必须依赖工作中心；团队/班次筛选必须依赖生效日期。

- [ ] **步骤 3：播种减震器基础数据**

至少播种成品、原材料、供应商、工厂、产线、工作中心、设备、班次、日历、团队和技能。种子必须位于 backend/dev 设置或有文档记录的导入 fixture 中，不得在产品 UI 中宣称为示例数据。

### P0-D：产品工程发布工作台

**目标：**确保 BOM、BOM 版本、工艺路线、工序和 ProductionVersion 在 MES 工单下达前可用。

**文件：**
- 按需修改：ProductEngineering endpoint/facade 覆盖。
- 创建：工程物料、EBOM、MBOM、工艺路线和生产版本的 Business Console 工程页面。
- 测试：ProductEngineering 契约测试和 BusinessGateway 代理测试。

- [ ] **步骤 1：为已发布工程事实暴露列表/详情/解析 facade**

BusinessGateway 必须暴露计划与 MES 页面所需的已发布 MBOM、工艺路线、工序顺序和 ProductionVersion 解析能力。

- [ ] **步骤 2：增加发布状态 UI**

工程页面必须显示草稿/已发布/已归档状态，并阻止 MES 选择草稿工程数据。

- [ ] **步骤 3：MES 工单下达时锁定发布快照**

MES 下达必须存储 productionVersionId、MBOM 版本、工艺路线版本、工序、物料需求和资源能力快照。

### P0-E：需求、MRP 与采购就绪

**目标：**使生产计划来自真实需求和 MRP 建议，而不是临时页面。

**文件：**
- 修改：DemandPlanning 输入适配器与 BusinessGateway facade。
- 修改：ERP 采购/销售 facade 与页面。
- 修改：`erp`、`planning` 或更清晰领域路由下的 Business Console 页面。

- [ ] **步骤 1：从销售订单、预测和安全库存向 MRP 提供输入**

DemandPlanning 必须接受或导入 P0 场景的需求来源，并列出指向原始需求的追溯链接。

- [ ] **步骤 2：将 MRP 连接到 ProductEngineering 和 Inventory 快照**

MRP 必须解析 ProductionVersion 和 BOM 组件，并在创建计划工单或采购建议前扣减库存可用量。

- [ ] **步骤 3：创建采购就绪流程**

计划采购建议可以转为采购申请/订单/收货；供应商从合作方主数据中选择，且收货状态对 MES 物料就绪可见。

### P0-F：MES 执行主干

**目标：**仅在基础、工程、物料、质量和设备就绪检查通过后，MES 工单才能执行。

**文件：**
- 修改：生产计划、工单下达、物料就绪、派工、工序生命周期、报工、入库、停机、交接和追溯的 MES command/query handler。
- 修改：BusinessGateway MES facade。
- 修改：后端行为完成后的 Business Console MES 页面。

- [ ] **步骤 1：限制工单来源**

普通工单来自已接受的计划工单建议或已发布生产计划。仍允许插急单，但依然需要生产版本、物料、质量、设备和编号检查。

- [ ] **步骤 2：使用持久事实补齐稀疏 query handler**

发料请求、班次交接、相关质量项和追溯 query 必须读取持久化事实或关联服务事实。空 stub 响应不可接受为交付结果。

- [ ] **步骤 3：强制执行生命周期操作**

下达、派工、开始、暂停、恢复、完成、报工和入库操作必须校验就绪状态与当前状态。仅当后端返回允许时，UI 才能暴露相应操作。

### P0-G：围绕工作流重建 PC UI

**目标：**在后端/数据基础就绪后改造 Business Console 页面。

**文件：**
- 修改：`frontend/apps/business-console/src/pages/**`
- 修改：`frontend/apps/business-console/src/composables/**`
- 按需修改：共享业务组件。

- [ ] **步骤 1：按工作角色重建导航**

使用`主数据`、`工程资料`、`计划与采购`、`生产执行`、`质量与库存`、`设备异常`或等效业务领域。不得将诊断页面作为操作员的主要工作流暴露。

- [ ] **步骤 2：以引导式操作替换孤立表单**

主页面显示队列、筛选器、KPI 和表格/详情。创建/报工/确认操作从行上下文中打开，并预填已知事实。

- [ ] **步骤 3：在浏览器中验证 P0 场景**

使用已播种的减震器场景，证明销售/预测需求 -> MRP -> 采购就绪 -> 生产版本 -> 工单 -> 发料 -> 派工 -> 报工 -> 入库 -> 追溯链路。截取屏幕截图供审核。

### P0-H：Scheduling / APS Lite 核心

**目标：**在 Gantt 视图成为交付界面之前，确保派工决策可复现。

**文件：**
- 创建或修改：`SchedulingProblem`、`SchedulePlan`、资源负载和冲突原因的 Scheduling/APS 契约。
- 修改：契约就绪后的 DemandPlanning/MES/BusinessGateway 集成点。
- 测试：减震器场景的确定性排程用例。

- [ ] **步骤 1：冻结排程契约**

定义来自工单、工序、已发布生产版本、资源、日历、物料就绪、质量阻塞和设备可用性的排程输入。输出必须包括分配结果、开始/结束窗口、资源负载、冲突原因和无法排程原因。

- [ ] **步骤 2：实现确定性有限产能排程**

首个算法是启发式算法，而不是求解器。它必须处理工序优先关系、设备产能、班次日历、维护窗口、活动报警、锁定任务、交期优先级和插急单。

- [ ] **步骤 3：保持 Gantt 的消费者角色**

Gantt/排程 UI 消费 `SchedulePlan` 并发送调整意图，不在浏览器中计算正式排程。

### P0-I：设备 IIoT 运行时事实

**目标：**使设备状态、报警、停机和维护窗口影响 APS 与 MES 就绪状态。

**文件：**
- 按需修改：IndustrialTelemetry 和 Maintenance query/event 界面。
- 修改：契约就绪后的 MES 就绪与 Scheduling 可用性集成。
- 创建或修改：后端事实就绪后的 Business Console 设备/IIoT 页面。

- [ ] **步骤 1：映射设备运行时事实**

设备资产、工作中心、遥测标签、状态映射、报警严重度、采样策略和来源序列必须明确且幂等。

- [ ] **步骤 2：为 APS 暴露可用性**

Scheduling 必须能够查询时间窗口内的设备可用性，包括活动报警、停机、维护、检验和替代设备上下文。

- [ ] **步骤 3：为 MES 暴露就绪状态**

MES 下达、派工和开始操作必须与 Scheduling 使用相同的设备原因代码，而不是仅在独立诊断页面显示设备问题。

## P1 范围

1. 在 APS lite 之上提供更丰富的排程比较、可视化时间线交互和派工仿真。
2. 已保存视图、列可见性、导出、批量操作和审批交接。
3. 高级质量工作流：首件检验、SPC、NCR 详情和 CAPA 交接。
4. 工装/模具生命周期、预防性维护窗口和 OEE 损失树。
5. 供应商评分卡、交付周期偏差和采购异常驾驶舱。

## P2 范围

1. 求解器级 APS 优化、场景仿真和自动重排。
2. PDA/移动端扫描和离线同步。
3. 深度 WCS/AGV/AMR 自动化。
4. 完整 QMS/LIMS 和完整 CMMS/EAM。
5. 财务成本结算和完整总账月结。

## 验收门禁

仅当以下条件全部满足时，P0 才算完成：

1. 普通创建流程中，用户不手工输入系统编号。
2. 所有 P0 表单均使用关联选择器或行上下文，而不是自由文本 ID。
3. 减震器场景可以创建或播种，然后完整运行 MRP、采购就绪、工单下达、派工、报工、入库和追溯流程。
4. 当生产版本、BOM/工艺路线、物料、质量、设备、日历、班次、条码或编号就绪受阻时，MES 拒绝下达/开始操作。
5. APS lite 可以根据 P0 工单、资源、日历、物料就绪和设备运行时事实生成确定性排程计划或冲突说明。
6. Business Console 可见文案采用中文业务文案，不包含 Gateway 契约、实施上下文或示例数据说明等开发者元数据。
7. 验证包括后端测试、BusinessGateway 代理/授权测试、生成客户端刷新、前端 typecheck/test/build，以及 P0 场景的浏览器屏幕截图。
