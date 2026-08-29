# Maintenance / IndustrialTelemetry 稳定错误值调查与兼容裁决

- 状态：#1870 spike 调查结论；未修改生产代码、生产消息或公开 wire 值
- 基准：`origin/main` `38218296536f749c8d4796b94c295315d9a3bc67`
- 盘点时间：2026-08-20T06:11:35Z
- 上位口径：[KnownException 用户可见口径与 Gateway 传输政策](known-exception-user-visibility.md)

本文只裁决 #1870 指定的 Maintenance 与 IndustrialTelemetry 同步 HTTP 失败信封中的稳定
kebab-case `message`。所有实现事实、文件行号和计数均对应上述基准；后续实施必须从当时最新
`main` 重新运行本文命令，不能把本页当作永久不变的生成账本。

## 结论

1. 两个服务当前共有 **5 个唯一生产 wire 值**：Maintenance 专属的 3 个 HTTP 400 值，
   以及两域共同使用的 `idempotency-conflict`、`lifecycle-conflict` 两个 HTTP 409 值。
2. 旧票所称“Maintenance / IndustrialTelemetry 共 50 处错误码”不是稳定码分母。当前生产
   `new KnownException(` 构造位点已是 26 + 31 = 57 个，但稳定 kebab-case
   `KnownException` 字面量只有 4 个源码位点、3 个唯一值；再加 4 个 `SafeCode` 定义位点、
   2 个唯一共享值，最终仍是 5 个唯一 wire 值。
3. `missing-downtime-reason` 与 `idempotency-key-conflict` 各只出现于一个 HTTP 测试的异常
   注入，不是生产者，也不是可迁移的生产契约。
4. 5 个生产值全部保持现有 `message` 与 HTTP 状态；中文展示走前端精确码表。
   `lifecycle-conflict` 已被 PDA 以字符串全等分支消费，且两项 409 值还与 MES、WMS、Quality
   共享。没有发现精确前端消费者的值也不能据此改 wire。
5. 当前公开失败信封已经有数值型 `code: int32`。本轮不把它改成字符串错误码，也不在最小
   后续票中新增契约字段；如以后需要结构化机器码，应另行设计可选 `errorCode` 并完成
   Gateway、OpenAPI、生成客户端与读写双轨迁移。

## 盘点边界与分类

### 纳入

- 两个目标服务中显式 `KnownException("kebab-case")` 生产者；
- 两个目标服务冲突 middleware 写入失败信封的稳定 `SafeCode`；
- 对应服务 endpoint、公开 BusinessGateway facade、状态与消息保留/改写路径；
- 全仓精确字符串消费者、契约测试以及只在测试中注入的相似值。

### 不纳入 5 个生产值的分母

- 英文或中文自然语言 `KnownException`：属于 #1864 的域消息中文化盘点，不是稳定码裁决；
- `blockReasons`、`degradedReasons`、设备控制结果、Connector 激活错误、集成事件和 seed 中的
  kebab-case 业务数据：它们不是同步 HTTP 失败信封的 `message`；
- BusinessGateway 通用传输/授权错误与 Gateway 自己的前置校验码：它们不是两个目标服务的
  `KnownException` 或冲突 middleware 产物。为防止把它们误算成服务码，本文仍在后文单列
  这些相邻值及其真实改写结果；
- 测试用 idempotency key、事件 ID、状态和 fixture 数据；仅字符串形似错误码不能证明它是
  失败契约。

## 计数校准与可复查命令

旧 #1864 快照在 2026-08-19 用 `grep -A1` 后取“最长字符串字面量”，得到 Maintenance 27、
IndustrialTelemetry 23。该方法统计的是 `KnownException` 附近文本，既会把自然语言、动态消息
和测试注入混在一起，也没有识别冲突 middleware；它不能回答“有多少唯一稳定错误码”。

| 口径                                            | Maintenance | IndustrialTelemetry | 合计 | 含义                                           |
| ----------------------------------------------- | ----------: | ------------------: | ---: | ---------------------------------------------- |
| 当前生产 `new KnownException(` 构造位点         |          26 |                  31 |   57 | 包含自然语言、插值、pass-through 与稳定码      |
| 当前生产 + 测试构造位点                         |          27 |                  32 |   59 | 每域多出的 1 个正是仅测试注入                  |
| 生产显式 kebab-case `KnownException` 字面量位点 |           4 |                   0 |    4 | Maintenance 的完工回执值在两个校验分支重复出现 |
| 生产 `SafeCode` 定义位点                        |           2 |                   2 |    4 | 两域分别定义相同的两个 409 值                  |
| 唯一生产 wire 值                                |           5 |                   2 |    5 | 3 个域专属 + 2 个跨域共享；合计按值去重        |

候选与计数命令：

```bash
rg -o 'new KnownException\(' backend/services/Business/Maintenance/src -g '*.cs' | wc -l
rg -o 'new KnownException\(' backend/services/Business/IndustrialTelemetry/src -g '*.cs' | wc -l
rg -n 'new KnownException\("[a-z0-9]+(?:-[a-z0-9]+)+"\)' \
  backend/services/Business/{Maintenance,IndustrialTelemetry}/src -g '*.cs'
rg -n 'SafeCode\s*=\s*"[a-z0-9]+(?:-[a-z0-9]+)+"' \
  backend/services/Business/{Maintenance,IndustrialTelemetry}/src -g '*.cs'
rg -n '"[a-z0-9]+(?:-[a-z0-9]+)+"' \
  backend/services/Business/{Maintenance,IndustrialTelemetry}/src \
  backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/{Maintenance,Equipment} \
  -g '*.cs' -g '!**/bin/**' -g '!**/obj/**'
```

最后一条是宽口径候选扫描；必须回到实际 throw、middleware 和失败信封写入点人工分类，不能
按正则命中直接计数。对 5 个值和 2 个测试注入做全仓反查：

```bash
for value in \
  stored-maintenance-work-order-receipt-is-invalid \
  source-alarm-already-bound-to-a-different-create-intent \
  stored-maintenance-completion-receipt-is-invalid \
  idempotency-conflict lifecycle-conflict \
  missing-downtime-reason idempotency-key-conflict
do
  printf '\nVALUE=%s\n' "$value"
  rg -n -F "$value" backend frontend scripts .github \
    -g '!**/bin/**' -g '!**/obj/**' -g '!**/node_modules/**'
done
```

## 生产稳定值事实表

下表的“消费者”只把字符串全等、固定正则/状态映射和精确契约断言算作证据；仅调用同一个
operation 不等于按该值消费。

| wire 值                                                   | 分类与生产者                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       | 服务 endpoint → 公开 Gateway facade                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       | HTTP / Gateway 行为                                                                                                      | 精确消费者与冻结证据                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          | 裁决                                                                                            |
| --------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------- |
| `stored-maintenance-work-order-receipt-is-invalid`        | Maintenance 专属生产码。创建幂等回执能解析、但回读不到目标工单时由 `CreateMaintenanceWorkOrderCommandHandler` 抛出；[`MaintenanceCommands.cs`](../../backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/Commands/MaintenanceCommands.cs) 第 105-122 行                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        | `POST /api/business/v1/maintenance/work-orders` / `createMaintenanceWorkOrder` → `POST /api/business-console/v1/maintenance/work-orders` / `createBusinessConsoleMaintenanceWorkOrder`；服务 endpoint 见 [`MaintenanceEndpoints.cs`](../../backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Endpoints/Maintenance/MaintenanceEndpoints.cs) 第 475-489、879 行，Gateway 见 [`BusinessConsoleMaintenanceEndpoints.cs`](../../backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Maintenance/BusinessConsoleMaintenanceEndpoints.cs) 第 23-99 行          | KnownException middleware 生成 400；Gateway 的 400 business-message 安全过滤允许该值，公开响应仍为 400 且 `message` 不变 | 生产前端没有字符串全等消费者；Business Console 与 PDA 均调用创建 operation。精确处理器断言见 [`MaintenanceWorkOrderIdempotencyTests.cs`](../../backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/MaintenanceWorkOrderIdempotencyTests.cs) 第 114 行                                                                                                                                                                                                                                                                                                                                                                                                         | 保持 wire；前端映射“工单创建回执异常，请刷新后重试；仍失败请联系管理员。”                       |
| `source-alarm-already-bound-to-a-different-create-intent` | Maintenance 专属生产码。同一 source alarm 已被另一创建意图绑定时抛出；`MaintenanceCommands.cs` 第 125-135 行                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       | 与上一行相同的创建 endpoint / facade                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      | 400 → 400，原值保留                                                                                                      | 无生产精确字符串消费者；精确处理器/seed 断言见 `MaintenanceWorkOrderIdempotencyTests.cs` 第 152 行与 [`MaintenanceSeedServiceTests.cs`](../../backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/MaintenanceSeedServiceTests.cs) 第 123 行                                                                                                                                                                                                                                                                                                                                                                                                                   | 保持 wire；前端映射“该报警已关联其他维护工单，请刷新后核对。”                                   |
| `stored-maintenance-completion-receipt-is-invalid`        | Maintenance 专属生产码。完工幂等回执状态、时间或版本无效时，由两个互斥校验分支抛出同一值；`MaintenanceCommands.cs` 第 433-455 行                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   | `POST /api/business/v1/maintenance/work-orders/{workOrderId}/complete` / `completeMaintenanceWorkOrder` → `POST /api/business-console/v1/maintenance/work-orders/{workOrderId}/complete` / `completeBusinessConsoleMaintenanceWorkOrder`；服务见 `MaintenanceEndpoints.cs` 第 492-523、881 行，Gateway 见 `BusinessConsoleMaintenanceEndpoints.cs` 第 150-175 行                                                                                                                                                                                                                                          | 400 → 400，原值保留                                                                                                      | 无生产精确字符串消费者；精确处理器断言见 `MaintenanceWorkOrderIdempotencyTests.cs` 第 237 行。Business Console 当前消费完工 operation，并在非生命周期错误时进入通用通知                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       | 保持 wire；前端映射“工单完工回执异常，请刷新后重试；仍失败请联系管理员。”                       |
| `idempotency-conflict`                                    | **共享生产码**。Maintenance 创建、完工和生命周期回放在相同 key / 不同 payload 时抛出；`MaintenanceCommands.cs` 第 105-111、428-431 行，[`MaintenanceLifecycleCommands.cs`](../../backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/Commands/MaintenanceLifecycleCommands.cs) 第 292-309 行。IndustrialTelemetry 搁置显式冲突与目标唯一约束竞态均映射此值；[`IndustrialTelemetryCommands.cs`](../../backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Commands/IndustrialTelemetryCommands.cs) 第 1712-1744 行，[`IndustrialTelemetryLifecycleConflictException.cs`](../../backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Errors/IndustrialTelemetryLifecycleConflictException.cs) 第 35-52 行 | Maintenance：创建、完工、派工、生命周期 actions；派工公开 facade 还会先调用 internal assignment replay probe（[`MaintenanceQueries.cs`](../../backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/Queries/MaintenanceQueries.cs) 第 247-305 行）。IndustrialTelemetry：`POST /api/business/v1/iiot/alarms/{alarmEventId}/shelve` → `POST /api/business-console/v1/equipment/alarms/{alarmEventId}/shelve`                                                                                                                                                             | 两域 middleware 均生成 409；Gateway 非 400 严格安全码过滤允许该值，状态与消息均保持 409 / 原值                           | 没有生产前端字符串全等分支；Business Console 按 409 统一恢复，通用通知正则匹配 `idempotency`。Gateway 精确 409 合同见 [`BusinessGatewayIdempotencySafetyTests.cs`](../../backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayIdempotencySafetyTests.cs) 第 90-113 行；FullChain 还冻结 Maintenance 常量。MES、WMS、Quality 生产代码也定义同值                                                                                                                                                                                                                                                                                                             | 保持 wire；前端精确映射“该操作标识已用于其他内容，请刷新后重新发起。”；不因没有精确消费者而改码 |
| `lifecycle-conflict`                                      | **共享生产码**。Maintenance 完工、派工、版本守卫及 actions 非法状态均映射此值；`MaintenanceCommands.cs` 第 360-363 行，`MaintenanceLifecycleCommands.cs` 第 52-62、84-92、150-162、220-231 行。IndustrialTelemetry 对已清除报警的确认/搁置映射此值；`IndustrialTelemetryCommands.cs` 第 1633-1642、1747-1750 行                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    | Maintenance：完工、派工、actions。IndustrialTelemetry：`POST /api/business/v1/iiot/alarms/{alarmEventId}/acknowledge`、`.../shelve` → 对应 Business Console equipment facade；服务路由见 [`IndustrialTelemetryEndpoints.cs`](../../backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Endpoints/Iiot/IndustrialTelemetryEndpoints.cs) 第 426-449、754-755 行，Gateway 见 [`BusinessConsoleEquipmentEndpoints.cs`](../../backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Equipment/BusinessConsoleEquipmentEndpoints.cs) 第 262-313 行 | 两域 middleware 生成 409；Gateway 保持 409 / 原值                                                                        | PDA 的 [`lifecycleActionRecovery.ts`](../../frontend/apps/business-pda/src/composables/lifecycleActionRecovery.ts) 第 28-43 行执行 `message === 'lifecycle-conflict'`，设备报警页在第 213-239 行据此重置、刷新并显示固定中文。Business Console 的 [`lifecycleAction.ts`](../../frontend/apps/business-console/src/composables/lifecycleAction.ts) 第 85-117 行按 409 执行同类恢复。Gateway 对 Maintenance 与 IndustrialTelemetry 的精确合同见 [`BusinessGatewayMaintenanceTelemetryTests.cs`](../../backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayMaintenanceTelemetryTests.cs) 第 2377-2409、2804-2834、2888-2894 行；MES、WMS、Quality 也定义同值 | 保持 wire 与 PDA 精确恢复分支；展示文案统一复用现有“状态已被其他操作更新”                       |

### Endpoint 路由展开

同一值可能由多个同步根生产；下表展开所有已确认的服务路由，避免把表格中的“创建/完工/actions”
误读成一个 endpoint。`assignment-replay-probe` 自身为 `internal`，但它是公开派工 facade 的
前置调用，因此其 409 会由该公开派工响应带回。

| wire 值                                                                                                       | 服务 endpoint / operationId                                                                                                                                                                                                                                          | 公开 Gateway endpoint / operationId                                                                                             |
| ------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------- |
| `stored-maintenance-work-order-receipt-is-invalid`、`source-alarm-already-bound-to-a-different-create-intent` | `POST /api/business/v1/maintenance/work-orders` / `createMaintenanceWorkOrder`                                                                                                                                                                                       | `POST /api/business-console/v1/maintenance/work-orders` / `createBusinessConsoleMaintenanceWorkOrder`                           |
| `stored-maintenance-completion-receipt-is-invalid`                                                            | `POST /api/business/v1/maintenance/work-orders/{workOrderId}/complete` / `completeMaintenanceWorkOrder`                                                                                                                                                              | `POST /api/business-console/v1/maintenance/work-orders/{workOrderId}/complete` / `completeBusinessConsoleMaintenanceWorkOrder`  |
| `idempotency-conflict`                                                                                        | Maintenance 创建 endpoint，同上                                                                                                                                                                                                                                      | Maintenance 创建 facade，同上                                                                                                   |
| `idempotency-conflict`                                                                                        | `POST /api/business/v1/maintenance/work-orders/{workOrderId}/complete` / `completeMaintenanceWorkOrder`                                                                                                                                                              | `POST /api/business-console/v1/maintenance/work-orders/{workOrderId}/complete` / `completeBusinessConsoleMaintenanceWorkOrder`  |
| `idempotency-conflict`                                                                                        | `POST /api/business/internal/v1/maintenance/work-orders/{workOrderId}/assignment-replay-probe` / `probeMaintenanceWorkOrderAssignmentReplay`，随后首次写入走 `POST /api/business/v1/maintenance/work-orders/{workOrderId}/assignment` / `assignMaintenanceWorkOrder` | `POST /api/business-console/v1/maintenance/work-orders/{workOrderId}/assignment` / `assignBusinessConsoleMaintenanceWorkOrder`  |
| `idempotency-conflict`                                                                                        | `POST /api/business/v1/maintenance/work-orders/{workOrderId}/actions` / `transitionMaintenanceWorkOrder`                                                                                                                                                             | `POST /api/business-console/v1/maintenance/work-orders/{workOrderId}/actions` / `transitionBusinessConsoleMaintenanceWorkOrder` |
| `idempotency-conflict`                                                                                        | `POST /api/business/v1/iiot/alarms/{alarmEventId}/shelve` / `shelveBusinessIiotAlarm`                                                                                                                                                                                | `POST /api/business-console/v1/equipment/alarms/{alarmEventId}/shelve` / `shelveBusinessConsoleEquipmentAlarm`                  |
| `lifecycle-conflict`                                                                                          | Maintenance complete endpoint，同上                                                                                                                                                                                                                                  | Maintenance complete facade，同上                                                                                               |
| `lifecycle-conflict`                                                                                          | `POST /api/business/v1/maintenance/work-orders/{workOrderId}/assignment` / `assignMaintenanceWorkOrder`                                                                                                                                                              | `POST /api/business-console/v1/maintenance/work-orders/{workOrderId}/assignment` / `assignBusinessConsoleMaintenanceWorkOrder`  |
| `lifecycle-conflict`                                                                                          | `POST /api/business/v1/maintenance/work-orders/{workOrderId}/actions` / `transitionMaintenanceWorkOrder`                                                                                                                                                             | `POST /api/business-console/v1/maintenance/work-orders/{workOrderId}/actions` / `transitionBusinessConsoleMaintenanceWorkOrder` |
| `lifecycle-conflict`                                                                                          | `POST /api/business/v1/iiot/alarms/{alarmEventId}/acknowledge` / `acknowledgeBusinessIiotAlarm`                                                                                                                                                                      | `POST /api/business-console/v1/equipment/alarms/{alarmEventId}/acknowledge` / `acknowledgeBusinessConsoleEquipmentAlarm`        |
| `lifecycle-conflict`                                                                                          | `POST /api/business/v1/iiot/alarms/{alarmEventId}/shelve` / `shelveBusinessIiotAlarm`                                                                                                                                                                                | `POST /api/business-console/v1/equipment/alarms/{alarmEventId}/shelve` / `shelveBusinessConsoleEquipmentAlarm`                  |

Maintenance 路由注册表证据见 `MaintenanceEndpoints.cs` 第 879-887 行；IndustrialTelemetry 见
`IndustrialTelemetryEndpoints.cs` 第 754-755 行；公开 facade 与 operationId 另由
[`facade-coverage-matrix.json`](facade-coverage-matrix.json) 第 1267-1295、1974-2055 行登记。

两项共享值不是只有两个目标域碰巧同名：MES 的
[`MesLifecycleConflictException.cs`](../../backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Errors/MesLifecycleConflictException.cs)
第 8、14 行，WMS 的
[`WmsLifecycleConflictException.cs`](../../backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Application/Errors/WmsLifecycleConflictException.cs)
第 12、56 行，以及 Quality 的
[`QualityLifecycleConflictException.cs`](../../backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Errors/QualityLifecycleConflictException.cs)
第 11、29 行均定义相同 wire 值。仅改单域会把既有共享语义拆成两套。

Maintenance middleware 的两个安全值定义和 409 信封写入见
[`MaintenanceLifecycleConflictException.cs`](../../backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/Errors/MaintenanceLifecycleConflictException.cs)
第 5-48 行；IndustrialTelemetry 对应实现见 `IndustrialTelemetryLifecycleConflictException.cs`
第 8-65 行。两个服务都先注册 400 KnownException handler，再注册冲突 middleware：
[`Maintenance/Program.cs`](../../backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Program.cs)
第 234-236 行与
[`IndustrialTelemetry/Program.cs`](../../backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Program.cs)
第 214-216 行。

## 前端消费面

- Maintenance 创建与完工已由 Business Console 的
  [`useBusinessMaintenance.ts`](../../frontend/apps/business-console/src/composables/useBusinessMaintenance.ts)
  第 239-247、251-315 行消费；PDA 也在自己的 `useBusinessMaintenance.ts` 第 194-235 行和
  equipment repair 页面第 288-312 行消费创建 operation。三个 400 生产值当前都没有全等分支，
  因而会落入现有通用兜底。
- IndustrialTelemetry 确认/搁置由 Business Console 的
  [`useBusinessEquipment.ts`](../../frontend/apps/business-console/src/composables/useBusinessEquipment.ts)
  第 470-512、515-587 行消费，并经 `executeLifecycleAction` 按 409 恢复；PDA 的
  [`useBusinessEquipmentAlarms.ts`](../../frontend/apps/business-pda/src/composables/useBusinessEquipmentAlarms.ts)
  第 181-190 行使用相同 Gateway operations。
- `lifecycle-conflict` 是唯一生产全等消费者：PDA 的 `isLifecycleConflictError`。Business Console
  不检查该文字，而是检查 HTTP 409；`idempotency-conflict` 只被通用通知中的 `idempotency`
  正则覆盖。其余三个生产值没有精确生产消费者。

## Gateway 传输与信封约束

BusinessGateway 对上述值没有业务语义改写：

- 下游 400 走 `FromDownstreamBusinessMessage`；非空、最多 500 字符、首字符非空白且不含控制字符
  或 `<>{}/\` 时保留原 `message`；
- 下游非 2xx 且非 400 走 `FromSafeDownstreamMessage`；最多 128 字符且只含 ASCII
  字母、数字、`-_.` 时保留原状态和消息；
- `AuthorizedBusinessProxyEndpoint` 捕获代理异常，并把状态与 `ex.Message` 写入公开
  `ResponseData(false, message, statusCode, [])`。

实现证据分别在
[`BusinessServiceClients.cs`](../../backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessServiceClients.cs)
第 1755-1841、1903-1908、1952-1963、1983-2005 行，
[`AuthorizedBusinessProxyEndpoint.cs`](../../backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/Auth/AuthorizedBusinessProxyEndpoint.cs)
第 44-56 行，以及
[`ResponseDataEndpointResults.cs`](../../backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/ResponseDataEndpointResults.cs)
第 26-38 行。

公开 OpenAPI 中 `NetCorePalExtensionsDtoResponseData` 的 `code` 已定义为 `integer/int32`，且对象
`additionalProperties: false`；见
[`business-gateway-console.v1.json`](../../frontend/packages/api-client/openapi/business-gateway-console.v1.json)
第 29929-29947 行。因此“兼容新增 code 字段”不是空位新增，而是改变既有字段类型/语义，属于
破坏性变更。真正兼容的结构化方案只能使用另一个可选字段名，例如 `errorCode`。

## 相邻 Gateway 值：已反查但不混入服务码分母

Maintenance 的公开创建 facade 在调用目标服务前还会读取报警与主数据。下列值是
BusinessGateway 自己的生产值，不是 Maintenance / IndustrialTelemetry 服务的生产值；列出它们
是为了说明 5 个服务码的边界，而不是授权顺手改动。

| 值                             | 实际生产者 / HTTP                                                                                             | 公开 endpoint                                           | 精确消费者与裁决                                                                                           |
| ------------------------------ | ------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------- |
| `source-alarm-unavailable`     | Gateway 未能唯一、同租户回读 source alarm 时以 502 生产；`BusinessConsoleMaintenanceEndpoints.cs` 第 51-75 行 | `POST /api/business-console/v1/maintenance/work-orders` | 全仓无生产精确消费者；保持 wire，本次最小后续票不扩入 Gateway 前置校验文案                                 |
| `source-alarm-device-mismatch` | Gateway 确认报警设备与请求设备不一致时以 409 生产；同文件第 77-92 行                                          | 同上                                                    | 全仓无生产精确消费者；保持 wire，不与服务端 `source-alarm-already-bound-to-a-different-create-intent` 合并 |
| `device-reference-unavailable` | Gateway 设备引用为空或 MasterData 权威回读不一致时以 502 生产；同文件第 101-134 行                            | 同上                                                    | 全仓无生产精确消费者；保持 wire                                                                            |

所有目标 facade 还共享 `downstream-timeout`（503/504）、`downstream-unavailable`（503）、
`downstream-invalid-response`（502）和安全过滤失败时的 `downstream-request-failed`（保留触发
分支的状态）等通用传输码；它们由 `BusinessServiceHttpClient` 统一生产，不属于任一业务域。
Business Console 已对 `downstream-timeout`、`downstream-invalid-response` 和对应状态做中文
映射，见 [`notify.ts`](../../frontend/apps/business-console/src/utils/notify.ts) 第 43-56 行；PDA 按
HTTP 状态提供本地指引，见
[`request-timeout.ts`](../../frontend/apps/business-pda/src/api/request-timeout.ts) 第 283-311、
383-402 行。

另有两个形似稳定码的 Gateway 构造器参数需要特别排除：
`work-scope-not-authorized` 与 `maintenance-action-owner-required` 分别出现在
`BusinessConsoleMaintenanceEndpoints.cs` 第 806、852 行，但它们调用公开
`new BusinessServiceProxyException(status, message)`。该构造器在 `BusinessServiceClients.cs`
第 1759-1767 行刻意丢弃传入消息，实际公开 wire 是 `downstream-request-failed`，不是这两个值。
`work-scope-not-authorized` 在另一条成功响应的 `blockReasons` 里有前端标签消费，也不能反推它
是这里的失败 `message`。

## HTTP 契约哨兵与仅测试注入

| 值                         | 分类                                                                      | 注入 endpoint / 断言                                                                                                                                                                                                                                                   | 生产反查结论                                                  |
| -------------------------- | ------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------- |
| `missing-downtime-reason`  | 仅测试注入；用于证明 KnownException 仍由 400 handler 处理的 HTTP 契约哨兵 | [`MaintenanceLifecycleConflictTests.cs`](../../backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/MaintenanceLifecycleConflictTests.cs) 第 258-293 行把自造异常注入 `ISender`，只断言 complete endpoint 返回 400                      | 全仓除该注入外无命中；不是 Maintenance 生产值                 |
| `idempotency-key-conflict` | 仅测试注入；作用与上一行相同                                              | [`IndustrialTelemetryLifecycleConflictTests.cs`](../../backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/IndustrialTelemetryLifecycleConflictTests.cs) 第 455-475、508-519 行向 shelve endpoint 注入异常，只断言 400 | 全仓除该注入外无命中；真实生产值是 409 `idempotency-conflict` |

真实生产契约哨兵不能与上述假值混为一谈：同两个测试文件分别对
`lifecycle-conflict` 的 409 信封做精确断言；Gateway 还有跨边界 409 / 原消息断言。这些测试
冻结的是生产行为，不能因为值也出现在测试中就归为 fixture。

## 逐值兼容裁决与迁移顺序

### 统一裁决

- 5 个值的现有 `message`、大小写、拼写和 HTTP 状态全部保持；
- 前端使用全等码表映射中文，未知值继续走当前兜底；不得先把后端值改成中文再追消费者；
- `lifecycle-conflict` 的码表只负责文案。PDA/Business Console 已有的 reset、权威刷新、固定
  提示和幂等重试语义必须保留，不能退化成普通 toast；
- 不复用数值型 `code`。未来若引入 `errorCode`，必须另开契约票，不得夹带在本次中文化中；
- “全仓没有精确消费者”只说明当前仓库内未找到读者，不证明仓库外客户端不存在，更不构成
  破坏已公开 wire 的授权。

### 最小迁移顺序

1. 在最新 `main` 重跑本文候选、精确反查与 facade matrix 核验，确认 5 个值、状态和公开路由
   未漂移。
2. 在 `@nerv-iip/business-core` 增加覆盖 5 个原值的纯前端映射与单元测试；未知值返回空，交给
   各端既有兜底。
3. Business Console 在 WMS 专用码表之后、通用状态/英文正则之前调用该映射，使 Maintenance
   创建/完工和 IIoT 报警操作得到稳定中文，同时保留 409 生命周期恢复。
4. PDA 的通用错误描述接入同一映射；`isLifecycleConflictError` 的精确 wire 判断及设备报警页的
   reset + refresh 分支保持不变，显示文案复用映射结果。
5. 运行共享包、Business Console、PDA 的聚焦测试和类型检查，并以精确搜索证明后端常量、
   KnownException 字面量、状态码和 OpenAPI `code: int32` 均未改变。
6. 只有出现明确的机器码独立字段需求时，才新建 `errorCode` 契约迁移票：先加可选 writer，
   再升级 Gateway/OpenAPI/生成客户端和 reader，保留 `message` 双轨；完成全部已知消费者迁移和
   版本化弃用后，才可能另议 message 语义。

## 重新定级的后续 Issue 草案

已创建后续 Issue：[#1882](https://github.com/Mang-X/Nerv-IIP/issues/1882)。以下草案原文保留，
用于追溯 Issue 的范围、验收和来源。

建议标题：`[前端] Maintenance / IndustrialTelemetry 稳定错误值中文映射（保持 wire 兼容）`

建议 labels：`scope:M`、`area:frontend`、`type:tech-debt`、`priority:p2`。

建议正文：

```markdown
> **级别：M**（共享映射 + Business Console / PDA 接入，单 PR）
>
> **难度：中** ｜ 主要难点：`lifecycle-conflict` 既是展示码，也是 PDA 的恢复分支；
> 必须保持 reset / refresh / 幂等重试语义，不能只改 toast ｜ 建议模型：GPT 5.6
>
> 来源：#1870；母票：#1864

## 已确认事实

- 当前生产稳定值只有 5 个：
  `stored-maintenance-work-order-receipt-is-invalid`、
  `source-alarm-already-bound-to-a-different-create-intent`、
  `stored-maintenance-completion-receipt-is-invalid`、
  `idempotency-conflict`、`lifecycle-conflict`。
- 3 个 Maintenance 值经 Gateway 保持为 400；两个共享冲突值保持为 409。
- PDA 对 `message === 'lifecycle-conflict'` 有精确恢复分支；Business Console 按 409 恢复。
- 公开失败信封的 `code` 已是 `int32`，本票不新增或改变后端契约字段。

## 范围

1. 在 `@nerv-iip/business-core` 提供上述 5 个原始值到简体中文的精确映射；未知值不吞掉既有兜底。
2. Business Console 在通用英文/状态正则之前使用映射。
3. PDA 通用错误描述使用同一映射；保留 `lifecycle-conflict` 的精确判断与 reset + refresh 行为。
4. 为 5 个映射、未知值兜底、Business Console 通知和 PDA 生命周期恢复补聚焦测试。

## 非范围

- 不修改 backend、Gateway、生产 `message`、HTTP 状态或现有 wire 值；
- 不修改 OpenAPI / generated client，不把字符串写入现有数值型 `code`；
- 不扩成 #1864 的全仓 KnownException 中文化；
- 不顺带处理 Gateway 前置校验码或跨域通用 transport code。

## 验收

- 两个前端的目标操作不再把 5 个稳定值显示为英文 kebab-case 或无信息兜底；
- `lifecycle-conflict` 仍触发原有 reset、权威刷新和固定冲突提示；
- 精确搜索证明 5 个后端 wire 值和 400 / 409 状态未改；
- 共享包、Business Console、PDA 聚焦测试与受影响类型检查通过；
- PR 正文列出实际运行命令、通过数及未运行项。
```

## 已知不确定项

- 全仓精确反查只能证明本仓库内的消费者；已发布 SDK、外部集成或未纳入仓库的客户端是否按
  `message` 分支未知，因此兼容裁决采取保守策略。
- 本 spike 没有启动真实服务或发送 HTTP 请求；状态与转发结论来自当前源码、现有服务/Gateway
  HTTP 合同和 FullChain 断言。实施票仍需运行与改动相称的前端门禁。
- `errorCode` 的字段名、schema、兼容窗口和外部 SDK 影响尚未设计；本文只裁决不得复用当前
  `code: int32`，不替未来契约票预先选定方案。
