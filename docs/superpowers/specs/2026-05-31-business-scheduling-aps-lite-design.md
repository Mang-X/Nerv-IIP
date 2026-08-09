# BusinessScheduling APS Lite 设计

## 目标

将 #206 实现为后端所有的 APS lite（轻量高级计划与排程）边界：接收带版本的 `SchedulingProblem`，运行确定性的有限产能排程启发式算法，并返回稳定的 `SchedulePlan`，供 MES 派工决策和 #78 甘特图渲染使用。

首个切片优先保证可复现性、可解释性和可测试性，不追求全局最优。

## 当前状态

`BusinessScheduling` 尚不存在。架构已为 APS lite（轻量高级计划与排程）预留 `backend/services/Business/Scheduling` 和 `scheduling` schema。MES 当前以 `RuleScheduler` 和 `ScheduleResult` 作为过渡路径，但该代码归 MES 所有，不能提供规范的排程契约。

Issue #206 是 APS 的后端执行入口。#78 使用输出 DTO 展示甘特图和排程视图。#207 后续提供设备运行时事实；#191、#194 和 #195 分别提供计划、MES 工单下达和就绪事实。

## 边界

BusinessScheduling 负责：

1. `SchedulingProblem` 输入快照及其指纹。
2. `SchedulePlan` 输出版本。
3. 工序分配、资源负载、冲突、未排程工序和变更摘要。
4. 算法版本元数据和确定性重放证据。
5. 排程计划生命周期：预览、已生成和已发布。

BusinessScheduling 不负责：

1. MRP 需求、计划采购建议或计划工单建议。
2. MES 工单、工序任务、报工或执行状态转换。
3. 库存余额、WMS 备料、质量检验记录、设备主数据、告警或维护工单。
4. PLC/DCS/SCADA 控制命令。
5. 浏览器端排程计算。

## 契约结构

所有契约时间均为 UTC `DateTimeOffset`。本地工厂日历在问题中表示为显式班次窗口和例外窗口，不依赖机器本地时间。

### SchedulingProblem

| 字段 | 含义 |
| --- | --- |
| `problemId` | 调用方提供或服务生成的公共 ID，用于重放和可追溯性。 |
| `contractVersion` | 初始值为 `1`；未来兼容演进所必需。 |
| `organizationId` / `environmentId` | 通过业务边界传递的 IAM 上下文。 |
| `horizonStartUtc` / `horizonEndUtc` | 排程窗口。窗口外的工序以冲突/未排程结果返回。 |
| `orders` | 包含交期、优先级、数量、来源引用和工序顺序的候选工单。 |
| `resources` | 包含能力、产能单位、日历引用和确定性排序键的工作中心与设备。 |
| `calendars` | 约束可用生产时间的班次窗口和例外。 |
| `unavailabilityWindows` | 按资源/工作中心划分的维护、活动告警、停机、检验或人工阻塞窗口。 |
| `materialReadiness` | 按工单或工序划分的最早物料就绪时间和阻塞原因。 |
| `qualityBlocks` | 按工单、工序、SKU、工艺路线或资源划分的质量或检验阻塞。 |
| `lockedAssignments` | 在开放队列排程前必须预留产能的现有分配或用户锁定分配。 |

### 候选工序

每道工序包含：

1. `orderId`、`operationId`、`operationSequence` 和可选的 `predecessorOperationIds`。
2. `durationMinutes`、`requiredCapabilityCode` 和符合条件的 `resourceIds`。P0 工序时长已经按数量调整；工单级 `quantity` 仍保留用于可追溯性和未来的时长扩展。
3. 当工艺路线有首选工作中心/设备时，包含 `primaryResourceId`。
4. `earliestStartUtc`、`dueUtc`、`priority` 和 `isRush`。
5. `splitPolicy`，P0 仅支持 `nonSplittable`。
6. 可选的 `materialReadyUtc`、质量阻塞原因，以及对 DemandPlanning/MES/ProductEngineering 的来源引用。

### SchedulePlan

| 字段 | 含义 |
| --- | --- |
| `planId` | 公共计划 ID；预览响应可以使用临时 ID。 |
| `problemId` / `problemFingerprint` | 重放与幂等证据。 |
| `contractVersion` / `algorithmVersion` | DTO 和算法版本。 |
| `status` | `preview`、`generated` 或 `released`。 |
| `generatedAtUtc` | 服务层时间戳，不由纯算法生成。 |
| `assignments` | 包含开始/结束时间、来源工单、工艺路线引用和解释代码的工序到资源排程。 |
| `resourceLoads` | 资源/日或资源/窗口的负载和利用率。 |
| `conflicts` | 交期、产能、日历、物料、质量或设备冲突。 |
| `unscheduledOperations` | 无法在排程范围内排入且带有原因代码的工序。 |
| `changeSummary` | 与锁定分配和本次运行结果相比，新增、延迟、保留和阻塞的工序引用。`moved` 是为后续上一计划差异输入保留的枚举值。 |
| `ganttItems` | 面向 #78 的稳定读取 DTO，由分配/冲突派生，不进行浏览器端排程。 |

排程枚举字段序列化为 camel-case（小驼峰）字符串，例如 `generated`、`released`、`dueDate` 和 `nonSplittable`。Gateway OpenAPI 快照和生成客户端必须保留这些字符串值，而不是整数枚举序数。

## 算法 V1

P0 算法是一种确定性的有限产能启发式算法：

1. 在计算指纹前验证问题，并规范化资源、日历、工序和窗口。重复的资源/日历 ID、非正数工序时长、无效窗口和工单内重复的工序 ID 应作为输入错误拒绝，而不是演变成未分类的运行时失败。
2. 首先预留已锁定或进行中的分配。无效锁定保留在输出中，同时报告为冲突。锁定分配在引用缺失资源、超出排程范围/日历/可用性、时间范围无效，或与其他锁定分配一起超过资源有限产能时无效。
3. 依次按 `isRush` 降序、`priority` 降序、`dueUtc`、`orderId`、`operationSequence`、`operationId` 对开放工序排序。
4. 将每道工序的最早开始时间限制为不早于已排程前置工序的最晚结束时间，以强制执行工序前后关系。
5. 通过后移最早开始时间来强制执行物料就绪；当物料或质量阻塞没有结束时间时，将工序标记为未排程。
6. 对每个符合条件的资源，查找满足时长、产能、班次日历和不可用窗口的最早时隙。
7. 选择最早的可行时隙；并列时依次优先选择主资源、较小的确定性资源排序键、资源 ID。
8. 如果工序无法排入，则在 `unscheduledOperations` 中以最具体的可用原因代码返回，不得丢弃：有限产能已饱和时使用 `capacity`，没有合适的班次/日历窗口时使用 `calendar`，只有规范化后的最早开始时间或所需时长无法落入排程范围时才使用 `outsideHorizon`。
9. 如果分配在 `dueUtc` 后结束，保留该分配并增加交期冲突。
10. 根据显式日历产能中的实际分配分钟数计算资源负载。在扣减可用分钟数前，先合并重叠的不可用窗口。

规范指纹在 JSON 序列化前，按稳定业务键对所有无序输入集合排序。它必须不受上游集合顺序影响，同时在任何有排程语义的输入发生变化时改变。

该算法不得调用数据库、HTTP 服务、时钟、随机数生成器或静态本地时间 API。

## 减振器测试夹具

使用此 fixture（测试夹具）作为跨工作器回归用例：

| 事实 | 测试夹具 |
| --- | --- |
| 产品 | `FG-FRONT-SHOCK`、`FG-REAR-SHOCK`。 |
| 工作中心 | `WC-TUBE-WELD`、`WC-ROD-ASSEMBLY`、`WC-OIL-SEAL`、`WC-DAMPING-TEST`。 |
| 设备 | `DEV-WELD-01`、`DEV-ROD-01`、`DEV-OIL-01`、`DEV-TEST-01`。 |
| 班次 | 2026-06-01 08:00-16:00 UTC 和 2026-06-02 08:00-16:00 UTC。 |
| 工艺路线 | 焊接管件 -> 装配活塞杆 -> 注油/密封 -> 阻尼测试/包装。 |
| 维护 | `DEV-OIL-01` 在 2026-06-01 10:00-12:00 UTC 不可用。 |
| 加急工单 | `WO-RUSH-REAR-001`，优先级更高，交期早于普通前减振器工单。 |
| 普通工单 | `WO-FRONT-001`，优先级较低，使用相同的注油/密封瓶颈。 |

预期证据：

1. 每次重复运行都以相同顺序和相同时间戳返回分配。
2. 两个工单的工序顺序均得到保持。
3. 注油/密封工序不与维护窗口重叠。
4. 当两个工单都可行时，共享瓶颈上的加急工单排在普通工单之前。
5. 加急插单导致的任何普通工单延迟都出现在 `changeSummary` 或 `conflicts` 中。
6. `ganttItems` 包含工单、工序、资源、开始/结束时间、状态和冲突标记字段，无需前端排程计算。

## API 界面

| API | 用途 | 权限 |
| --- | --- | --- |
| `POST /api/business/v1/scheduling/plans/preview` | 运行算法，但不持久化已发布计划。 | `business.scheduling.plans.manage` |
| `POST /api/business/v1/scheduling/plans` | 从问题快照持久化已生成计划。 | `business.scheduling.plans.manage` |
| `GET /api/business/v1/scheduling/plans` | 列出已生成/已发布计划。 | `business.scheduling.plans.read` |
| `GET /api/business/v1/scheduling/plans/{planId}` | 读取完整计划，范围由 `organizationId` 和 `environmentId` 查询参数限定。 | `business.scheduling.plans.read` |
| `GET /api/business/v1/scheduling/plans/{planId}/gantt` | 读取稳定的甘特图 DTO，范围由 `organizationId` 和 `environmentId` 查询参数限定。 | `business.scheduling.plans.read` |
| `POST /api/business/v1/scheduling/plans/{planId}/release` | 将范围内计划标记为已发布，并发出供 MES 使用的发布意图/事件。 | `business.scheduling.plans.release` |

服务 API 存在后，BusinessGateway 可以公开等效的 `/api/business-console/v1/scheduling/**` 页面 facade（门面）。Gateway 不得持久化排程事实或实现排程逻辑。

## 事件

BusinessScheduling 发布符合 ADR 0011 envelope（信封）的事件：

1. `scheduling.SchedulePlanGenerated`
2. `scheduling.ScheduleConflictDetected`
3. `scheduling.SchedulePlanReleased`

事件 payload（载荷）包含公共 ID、契约版本、算法版本、问题指纹、计划状态、受影响的工单引用和冲突原因代码。它们不包含完整的浏览器 DTO 载荷、数据库行 ID、凭据、PLC 命令或原始高频遥测数据。

## 持久化

默认 schema：`scheduling`。

必需的 P0 表：

1. `schedule_problems`
2. `schedule_plans`
3. `schedule_plan_assignments`
4. `schedule_plan_resource_loads`
5. `schedule_plan_conflicts`
6. `schedule_plan_unscheduled_operations`

每张表和每个业务列都必须有 schema 注释。PostgreSQL migration history（迁移历史）必须使用 `scheduling.__EFMigrationsHistory`。

`schedule_problems` 的幂等唯一范围为 `organizationId + environmentId + problemId`。自然 `problemId` 可以跨租户或环境重复，而不会复用另一上下文的快照或已生成计划。

## 测试

验收要求：

1. 针对确定性重复运行、前后关系、产能冲突、日历班次、维护窗口、锁定分配、加急插单、交期冲突和未排程原因代码的纯算法测试。
2. 针对 `SchedulingProblem`、`SchedulePlan`、`GanttScheduleItem` 和原因代码枚举的契约序列化测试。
3. 针对计划生命周期和已发布计划不可变性的领域测试。
4. 针对路由结构、operation ID、授权策略和请求/响应字段的 Web 契约测试。
5. 使用 `Nerv.IIP.Testing` 的 schema 约定测试。
6. 针对三个排程事件名称的事件转换器测试。
7. 服务 API 注册后的 BusinessGateway facade（门面）测试。
8. 针对字符串枚举 OpenAPI/codegen（代码生成）结构，以及租户范围计划详情、甘特图和发布访问的回归测试。

## 范围外事项

1. 全局求解器优化、遗传算法、MILP/CP-SAT、仿真和自动重排程。
2. 跨所有上游服务的递归多级产能规划。
3. 直接 PLC/DCS/SCADA 控制。
4. 浏览器端权威排程。
5. 高频历史数据库存储。
