# BarcodeLabel transport attempt 审计模型预研

## 结论

#2148 在基线 `97d3e6eb3437d9927684dfdf4b967940fa5ca205` 上仍然成立。当前
`label_print_batches.printer_id / print_job_id / failure_reason` 只保存最近一次 transport
attempt；单项 reprint 会覆盖整批 dispatch 的这些列，`label_print_items` 又没有 printer、job
或 failure 字段，因此当前模型无法回答“某个 item 先后重打了几次、每次发送到哪台打印机、结果是什么”。

建议后续采用 BarcodeLabel 自有的独立 `label_print_transport_attempts` 表，逐次记录 dispatch
和 reprint 的 transport 事实。该表不是物理打印确认、outbox、printer-agent job/ack，也不接管
`LabelPrintBatch` 的业务生命周期；现有批次列继续作为兼容的“最近一次尝试投影”。历史行不从批次列
伪造 attempt。所有生产实施都以 #2065 的 attempt 边界修订获批为共同前置；本文列出的 6 项 owner
裁决按各自 seam 门控后续子项，不设置“六项全部确认后才能开始任何实施”的全局串行门。

本次只提交调查报告，不修改生产代码、Schema、API、权限、事件、UI 或测试治理。

## 范围门

- 级别：`scope:spike`。
- 调查范围：BarcodeLabel 内的 attempt 事实所有权、状态、事件、授权、失败重放、retention、历史迁移及实施拆解。
- 非目标：修改 #2065 已批准的模板/renderer/TCP 合同，新增物理出纸确认，新增 printer-agent、outbox、自动重试或人工确认命令。
- 建议后续工作均为单域、独立验收且不超过 `scope:M`；Gateway、Console 和 retention 不与 Schema 核心混入同一 PR。

## 已核实的当前事实

| 面 | 当前事实 | producer |
| --- | --- | --- |
| 所有权 | BarcodeLabel 拥有打印批次、打印项和 transport adapter；BusinessGateway 不持久化业务事实。 | [`business-platform-domain-architecture.md`](../../architecture/business-platform-domain-architecture.md) |
| 批次投影 | `label_print_batches` 的 `status` 是批次生命周期；`printer_id`、`print_job_id`、`failure_reason` 是最近一次 transport attempt。 | [`LabelPrintBatchEntityTypeConfiguration.cs`](../../../backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Infrastructure/EntityConfigurations/LabelPrintBatchEntityTypeConfiguration.cs)、[`database-schema-catalog.md`](../../reference/data/database-schema-catalog.md) |
| 单项事实 | `label_print_items` 只有标签业务状态及 void/consume 时间，没有 printer/job/failure。生产 reprint 不写 `reprinted`。 | [`LabelPrintBatch.cs`](../../../backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Domain/AggregatesModel/LabelPrintBatchAggregate/LabelPrintBatch.cs)、[`database-schema-catalog.md`](../../reference/data/database-schema-catalog.md) |
| transport 结果 | 首字节前失败是 `failed`；写入任意字节后失败是 `delivery-unknown`；全部写入并 half-close 是 `sent-to-printer`。这些结果都不证明物理出纸。 | [`ILabelPrinter.cs`](../../../backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Domain/Printing/ILabelPrinter.cs)、[`ZplTcpLabelPrinter.cs`](../../../backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Infrastructure/Printing/ZplTcpLabelPrinter.cs) |
| 状态投影 | dispatch 改批次状态及最近尝试列；reprint 只改最近尝试列，不改批次完成事实或 item 状态。 | [`PrintLabelLifecycleCommands.cs`](../../../backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/PrintBatches/PrintLabelLifecycleCommands.cs) |
| 取消 | printer 已开始后取消会携带封闭的 transport result；独立 recorder 在新 DbContext 中重载并重跑守卫，尽力覆盖批次最近尝试列，再传播原取消。它不是 attempt ledger。 | [`PrintLabelLifecycleCommands.cs`](../../../backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/PrintBatches/PrintLabelLifecycleCommands.cs)、[`BarcodeLabelPostgresProfileTests.cs`](../../../backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests/BarcodeLabelPostgresProfileTests.cs) |
| 允许集 | dispatch 只允许 `pending` / `failed`；reprint 只允许 `sent-to-printer` / 兼容 `printed`，并拒绝 voided/consumed item。 | [`LabelPrintBatch.cs`](../../../backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Domain/AggregatesModel/LabelPrintBatchAggregate/LabelPrintBatch.cs) |
| unknown 恢复 | dispatch 的 `delivery-unknown` 会锁住同批 dispatch/reprint；reprint 的 unknown 不改批次状态，响应提示操作员先现场确认，再自行决定是否重打。没有确认落库 API。 | [`LabelPrintBatch.cs`](../../../backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Domain/AggregatesModel/LabelPrintBatchAggregate/LabelPrintBatch.cs)、[`PrintLabelLifecycleCommands.cs`](../../../backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/PrintBatches/PrintLabelLifecycleCommands.cs) |
| 授权 | 当前打印读写端点使用 `business.barcodes.print`；scoped internal lifecycle 入口还要求 organization/environment 并按完整 scope 读取批次。 | [`BarcodeLabelPermissionCodes.cs`](../../../backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Application/Auth/BarcodeLabelPermissionCodes.cs)、[`BarcodeLabelEndpoints.cs`](../../../backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Endpoints/BarcodeLabel/BarcodeLabelEndpoints.cs) |
| 读模型 | batch detail/list 把 `Status / PrinterId / PrintJobId / FailureReason` 作为当前批次投影返回，没有 attempt 集合或历史覆盖范围。 | [`GetLabelPrintBatchQuery.cs`](../../../backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Application/Queries/PrintBatches/GetLabelPrintBatchQuery.cs)、[`ListLabelPrintBatchesQuery.cs`](../../../backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Application/Queries/PrintBatches/ListLabelPrintBatchesQuery.cs) |
| 事件 | 当前打印批次相关集成事件只有 batch created/completed；BarcodeLabel 域另有 `LabelScanned` / `ScanRejected` 事件。生产链没有物理 `printed` 来源，也没有 transport attempt 消费者。 | [`BarcodeLabelIntegrationEvents.cs`](../../../backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Application/IntegrationEvents/BarcodeLabelIntegrationEvents.cs)、[`event-consumption-matrix.md`](../../reference/integration/event-consumption-matrix.md) |

## 方案比较

| 方案 | 能否逐次审计 | 对现有模型的影响 | 结论 |
| --- | --- | --- | --- |
| 继续只写批次最近尝试列 | 否 | 无 | 不满足 #2148。只能保留兼容投影。 |
| 给 `label_print_items` 加最近尝试列 | 否；仍会覆盖历史，dispatch 也难表达整批 attempt | 把 transport 事实塞入标签业务实体 | 拒绝。只是把同一缺口从 batch 搬到 item。 |
| BarcodeLabel 独立 attempt 表 | 是；可同时表达 batch dispatch 与 item reprint | 单域新增 Schema 与查询，不改变 transport seam | 推荐候选。 |
| 外部 printer-agent job/ack | 可扩展为现场执行与 ack | 新身份、租约、离线、升级和跨进程协议 | 当前拒绝。会推翻 #2065 的已批准边界。 |
| 保存 raw TCP/ZPL 或声称物理出纸 | 不能证明出纸，且扩大敏感数据面 | 泄漏模板/标签 payload，并产生错误事实 | 拒绝。 |

## 最小候选合同

### 事实所有者与边界

- `label_print_transport_attempts` 属于 BarcodeLabel `barcode` Schema，由 BarcodeLabel 单独写入。它与
  batch 业务事务表分开建模，并使用独立审计 retention 合同。
- `LabelPrintBatch` 仍是打印业务生命周期聚合；attempt 是 transport 审计记录，不以 attempt
  状态驱动 item 的 `printed/reprinted`，也不发布 batch completed。
- dispatch attempt 归属 batch，`label_print_item_id` 为空；reprint attempt 同时归属 batch 与一个
  item。item 必须由同一 batch 拥有，不能只凭可猜测 ID 关联。
- 每次真正调用 `ILabelPrinter.PrintAsync` 都产生新的 UUID v7 attempt ID。调用前的模板读取、冻结
  快照校验或编译失败不是 transport attempt，不得伪造一行。
- 不保存 ZPL、模板 JSON、标签值、host、port、异常堆栈或 raw TCP 字节；failure 只保存与当前
  `LabelPrinterDispatchResult` 同等级的安全摘要。

### Schema 候选

| 列 | 约束与语义 |
| --- | --- |
| `id` | UUID v7，attempt 唯一标识。 |
| `organization_id` / `environment_id` | 从 batch 复制的不可变 scope，不接受客户端独立决定；与 batch ID 共同组成复合 FK。 |
| `label_print_batch_id` | 必填；`(label_print_batch_id, organization_id, environment_id)` 复合 FK 指向 batch 同序候选键，删除行为 `Restrict`。 |
| `label_print_item_id` | reprint 必填、dispatch 必须为空；`(label_print_item_id, label_print_batch_id)` 复合 FK 指向 item 同序候选键，强制 item 属于该 batch。 |
| `operation` | 封闭值 `dispatch` / `reprint`。 |
| `printer_id` | 受控逻辑 printer ID，不保存 endpoint。 |
| `initiator_kind` | 固定为 `trusted-internal-service`，由服务端写入；不接受请求体或客户端 header 自报。 |
| `correlation_id` | 可空；只从既有可信 request context 复制，不接受客户端覆盖。没有可信值时保持空。 |
| `status` | `started` / `failed` / `delivery-unknown` / `sent-to-printer`。不允许 `printed`。 |
| `print_job_id` | `sent-to-printer` 与 `delivery-unknown` 必填，其余为空。 |
| `failure_reason` | `failed` 与 `delivery-unknown` 必填；`started` / `sent-to-printer` 为空；只存安全摘要。 |
| `started_at_utc` | 调用 printer 前、attempt 预登记成功时写入。 |
| `completed_at_utc` | 三个封闭 transport 结果必填；`started` 为空。 |

必要约束的封闭真值表如下；`operation` 或 `status` 的未知值以及表外任意 nullness 组合都必须由
PostgreSQL CHECK 拒绝：

| `operation` | `label_print_item_id` |
| --- | --- |
| `dispatch` | 必须为空 |
| `reprint` | 必须非空 |

| `status` | `print_job_id` | `failure_reason` | `completed_at_utc` |
| --- | --- | --- | --- |
| `started` | 空 | 空 | 空 |
| `failed` | 空 | 非空 | 非空 |
| `delivery-unknown` | 非空 | 非空 | 非空 |
| `sent-to-printer` | 非空 | 空 | 非空 |

其余必要约束包括 batch 上
`(id, organization_id, environment_id)` 候选键与 item 上 `(id, label_print_batch_id)` 候选键，配合
上述两个复合 FK 让跨 scope batch 和错 batch item 无法成为持久事实。Application 写入边界仍需先
按 scope 加载，但不能代替数据库不变量。建议查询索引只保留
`(organization_id, environment_id, label_print_batch_id, started_at_utc desc, id desc)` 与
reprint 查询所需的 `(label_print_item_id, started_at_utc desc, id desc)`，不为尚不存在的报表
增加 printer/status 全局索引。

候选不增加 `actor_subject`：当前 internal Bearer 只能证明可信调用服务，不能证明终端用户；不得用
header、用户名或 Gateway 推测值填补不存在的 actor 事实。

attempt 允许且只允许 `started -> failed|delivery-unknown|sent-to-printer` 一次转换，封闭后不可
改写。正常返回路径在命令的同一数据库事务内，取消路径在 recorder 的同一个独立数据库事务内，
同时完成 attempt terminal 条件更新与 batch 最近尝试投影；任一步失败都回滚这两个同库事实。为防止
两个完成者覆盖，终结必须使用 `status = started` 的条件更新或等价并发 token，并要求恰好一行受影响。
批次最近尝试列继续只是兼容投影，不反向生成或修补 attempt 历史。

### 状态与重放矩阵

| 场景 | attempt | batch/item | 自动重试 |
| --- | --- | --- | --- |
| printer 调用前校验/编译失败 | 不创建 | 不变 | 沿现有命令语义，由调用方修正后重试 |
| printer 调用开始，首字节前确定失败 | `failed` | dispatch 仍投影为 batch `failed`；reprint 保持 batch/item 状态 | 不自动；dispatch 可按既有守卫再次整批下发，reprint 可在既有允许集内再次发起 |
| 已写任意字节后结果不确定 | `delivery-unknown` | dispatch 投影为 batch `delivery-unknown`；reprint 保持 batch/item 状态 | 不自动；dispatch 继续锁死，reprint 继续由操作员现场确认后决定是否再次发起 |
| 全部写入并 half-close | `sent-to-printer` | 保持当前 dispatch/reprint 投影 | 不自动；不得解释为 `printed` |
| 同一 HTTP 请求被人工再次提交 | 新 attempt ID | 重新执行既有守卫 | 不去重为旧 attempt；当前 reprint 没有 request idempotency 合同 |
| 进程在 attempt 预登记后、封闭前退出 | 保留 `started` | 不据此虚构 batch/item 终态 | 禁止自动重放；在 owner 裁决前只展示“结果未封闭，按可能已发送处理” |
| terminal 或 batch 最近投影任一同库写入失败 | 同一事务回滚，attempt 保持 `started` | batch 最近投影保持原值 | 稳定非成功响应携带 attempt ID，明确禁止自动重试并要求人工核验；结构化错误、metric 与 alert 必须可观察 |

最后两行只保留同步 TCP 真正不可消除的“attempt 预登记已提交—外部发送—terminal 与 batch 投影
原子提交”窗口，不主动制造两个同库 terminal 事实之间的窗口。引入同库 outbox 仍不能让数据库事务
包住外部 TCP 副作用；printer-agent ack 可以缩小/重定义窗口，但属于 #2065 明确排除的另一套架构。
本候选不以更多重试或状态掩盖该事实。

该失败闭合是已裁决合同：attempt 预登记失败严格禁止调用 printer；printer 已运行但 terminal 事务失败
时，系统不能判断外部副作用是否发生，必须按可能已发送处理。调用方取消仍传播原取消；若取消路径的
recorder 失败，则另行记录结构化错误、metric 与 alert，不能用 recorder 错误替换原取消，也不能自动
重放。恢复依赖操作员核验现场，而不是新增一条“确认”持久化事实。

### 人工确认

当前候选不新增“已出纸”“未出纸”或“已现场确认”字段，也不允许操作员把
`delivery-unknown` 改写成 `sent-to-printer/failed`。因此：

- dispatch unknown 继续保持系统级 fail-closed，只能按现行流程另建批次；
- reprint unknown 继续把安全责任交给站在打印机旁的操作员，响应明确提示先确认，再决定是否重打；
- 若产品需要把确认人、确认时间、确认结论和恢复授权落库，应先修订 #2065，并另做产品/授权 spike，
  不能把它伪装成 transport attempt 的终态。

### 事件候选

| 事件 | 候选裁决 | 理由 |
| --- | --- | --- |
| `LabelPrintTransportAttemptStarted` | 不发布 | 当前没有消费者；内部预登记不应扩大公开事件面。 |
| `LabelPrintTransportAttemptCompleted` | 不发布 | 查询直接读取 owner Schema；先有真实跨服务消费者再冻结版本化事件。 |
| 既有 batch created/completed | 不改变 | attempt 不是物理完成来源，不能触发 completed。 |

### 授权与读面候选

初始只提供 batch-scoped、分页的只读接口，不提供跨 scope、按 printer 的全局搜索：

```text
GET /api/business/internal/v1/barcodes/print-batches/{printBatchId}/transport-attempts
    ?organizationId=...&environmentId=...&sequenceNo=...&skip=...&take=...
```

- organization/environment 必填，查询以完整 scope + batch 定位；错误 scope 返回安全的未找到结果。
- `sequenceNo` 只筛 reprint；省略时返回 batch dispatch 与 reprint 的统一时间线。
- 响应只返回 attempt ID、operation、sequenceNo、printerId、status、job、failure 摘要、时间、
  `initiatorKind` 与可空 `correlationId`；不返回 ZPL、标签值、endpoint、原始异常或虚构的终端用户名。
- `initiatorKind` 必须读回服务端写入的 `trusted-internal-service`；`correlationId` 只读回可信 request
  context 的原值，没有可信值时为 null。Console 只能呈现“受信服务请求”和 correlation，不能把它
  解释为终端用户归因。
- BusinessGateway 若公开 facade，仍只透传认证 scope，不持久化 attempt，也不直接访问 BarcodeLabel 表。
- 冻结候选使用新的 `business.barcodes.transport-attempts.read`，不复用
  `business.barcodes.print`。理由是 attempt 是独立 retention 的长期失败与操作轨迹，执行打印权不自动
  包含读取长期审计历史；角色可显式同时获得两项权限，但不得通过隐式继承扩大读取面。
- BarcodeLabel internal endpoint 同时要求 internal service policy、上述 permission code 与完整 scope；
  BusinessGateway facade 使用相同 permission 做 IAM 授权并只透传已认证 scope。新 permission 的 IAM
  seed、权限目录、Gateway contract 与 generated client 由后续 Gateway 子项一次闭合。

## Retention 与历史迁移矩阵

### Retention

[`ADR 0003`](../../adr/0003-data-and-messaging-baseline.md) 冻结了审计记录不得与业务事务数据共用
retention 策略/周期。因此 attempt 即使与 batch 同属 BarcodeLabel owner，也必须有独立审计 retention
合同；batch 删除不得 cascade 删除仍在保留的 attempt，attempt 按自身策略清理也不得驱动 batch 删除。

| 选择 | 优点 | 风险 | 裁决 |
| --- | --- | --- | --- |
| 显式独立审计策略 | 可以按真实合规、容量和运维需求选择单周期、多阶段或明确的无限期保留 | 需要 owner 明确保留期限、删除授权与执行方式 | 唯一推荐候选；本 spike 不预设双周期、legal hold 或 worker。 |
| 跟随 batch 生命周期或 cascade | 实现表面简单 | 与 ADR 0003 冲突，业务删除会提前删除审计 | 拒绝；若未来确需采用，必须先有新 ADR 明确取代 ADR 0003 对应条款。 |

已裁决采用复合 FK + `Restrict`：batch/item 的最短物理存续期不得短于所有仍引用它们的 attempt，
父记录清理必须排在 attempt 按独立策略到期/删除之后；有 retained attempt 时删除父记录必须由数据库
拒绝。不得改成 tombstone、弱 FK 或无 FK。该裁决不新建 retention worker，也不虚构当前不存在的
batch/item 删除 API；未来若引入父记录删除或 retention API，必须在实施前复评授权与顺序。

attempt 的具体保留期限、删除授权与执行方式仍必须由 owner 在 retention 子项前明确；若选择自动清理，
清理必须可观察、失败可恢复，删除权限不能等同于 `business.barcodes.print`。当前没有足够事实决定期限、
是否需要 legal hold、是否需要多阶段归档或采用 worker/人工运维入口，因此不能在 Schema PR 中顺手
固化这些机械。

### 历史数据

| 现有形态 | 迁移处理 | 查询解释 |
| --- | --- | --- |
| batch 三列全空 | 不生成 attempt | 不能证明历史上从未尝试。 |
| batch 有 printer/job/failure | 不生成 attempt | 只能证明迁移时的最近投影；无法确定 dispatch/reprint、item、次数或原始时间。 |
| 历史 `printed/reprinted` seed | 不生成 attempt | seed 是演示物理状态，不是 transport 证据。 |
| 上线后新 attempt | 逐次记录 | 只对功能启用后的窗口完整。 |

禁止创建 `legacy-latest` 合成 attempt；它会把未知 operation/item/time 伪造成审计事实。读 API 必须显式
返回“post-cutover only”的覆盖语义。是否增加 batch 级
`transport_attempt_history_started_at_utc` 来精确表达每批覆盖起点，属于 owner 裁决；不允许硬编码某个
发布日后按 `CreatedAtUtc` 猜测完整性。

## Owner 裁决状态

1. **审计保证等级（已裁决）：** 预登记失败严格零 transport；terminal 原子事务失败返回携带 attempt ID
   的稳定非成功响应，明确禁止自动重试并要求人工核验；attempt 保持 `started`、batch 投影不变，且必须
   产生结构化错误、metric 与 alert。取消路径保留原取消，recorder 失败另行观察。
2. **历史覆盖表达：** 只在 API 文档声明 post-cutover，还是增加 batch 级覆盖起点。推荐显式覆盖起点，
   避免空历史被读成“从未打印”。
3. **读取授权批准：** 确认采用已冻结候选 `business.barcodes.transport-attempts.read`，并批准哪些角色显式获得；
   不再把“复用 print 或新建 permission”的二选一留给实现者。
4. **操作人归因（已裁决）：** 只记录服务端固定的 `trusted-internal-service` 与可信 request context
   中可空的 correlation；不新增终端 actor 合同，不允许客户端自报或推测用户名。
5. **Retention 关系（已裁决）：** 复合 FK + `Restrict`；父记录存续期不短于仍引用它的 attempt，清理
   顺序先 attempt 后 parent，不引入 tombstone/弱 FK。具体 attempt 期限、删除授权与执行方式仍是
   retention 子项的局部门禁，不在 Schema 子项内预设 worker、legal hold 或多阶段归档。
6. **人工确认：** 是否继续完全不落库，还是另立“现场核验”产品合同。推荐本轮保持不落库；若新增，
   必须单独修订 #2065，且不得把人工陈述改写成物理设备 ack。

## 建议实施拆解

### #2065 修订前置

任何生产子项开始前，#2065 的 owner 修订必须明确接纳 attempt ledger，并逐字保留以下已裁决边界：

- 预登记失败零 transport；terminal 同库事务失败保持 `started` 与原 batch 投影，稳定失败响应携带
  attempt ID、禁止自动重试、要求人工核验，并有结构化错误、metric 与 alert；
- provenance 只到 `trusted-internal-service` + 可信 correlation，不增加或伪造终端 actor；
- attempt 使用复合 FK + `Restrict`，父记录存续期不短于引用 attempt，不使用 tombstone/弱 FK；
- 不改变 #2065 的首字节边界、`delivery-unknown`、不把 transport 结果解释为物理出纸，也不引入
  printer-agent、outbox、自动重试或持久化人工确认。

该修订是 attempt 能力进入生产的前置，不是以本报告暗改 #2065；owner 2/3/6 仍只门控各自 seam。

| 顺序 | 建议子项 | Gate | 独立验收 | 依赖 |
| ---: | --- | --- | --- | --- |
| 1 | BarcodeLabel attempt 实体、迁移、复合归属约束、最小索引、独立预登记、原子 terminal + batch 投影 recorder 与真实 PostgreSQL crash-window/并发测试 | `scope:M` / 高 | 每次 printer 调用一条 attempt；预登记失败时 printer 零调用；terminal 事务失败时返回携带 attempt ID 的稳定非成功结果、禁止自动重试并要求人工核验，结构化错误/metric/alert 可观察，数据库读回 `started` 且 batch 投影不变；raw INSERT 参数化覆盖两张封闭真值表，表内格成功、未知值与表外 nullness 组合被拒绝；UPDATE 只验证 `started -> terminal`，terminal 再终结必须失败且 batch 不变；每个独立 CHECK、复合 FK 与 `status = started` 条件更新至少一个等价错误变异判红；跨 scope/wrong-item 插入及 retained attempt 下删除 parent 被 PostgreSQL 拒绝；可信 provenance/correlation 可读回，客户端自报/覆盖失败，缺失 correlation 保持 null；既有 batch/item 状态机不变 | #2065 attempt 规格修订已批准；owner 1/4/5 已冻结，owner 2；[`validity.md`](../../governance/testing/validity.md) |
| 2 | BarcodeLabel scoped internal 分页查询、覆盖语义、OpenAPI 与安全投影测试 | `scope:M` / 中 | 错 scope 安全未找到；分页稳定；无 raw ZPL/endpoint/异常泄漏；返回可信服务 provenance 与可空 correlation，不返回或推测终端 actor；历史空集合不冒充完整 | 1 合并，owner 2、3 |
| 3 | BusinessGateway 只读 facade、权限目录、generated client | `scope:M` / 中 | 认证 scope/permission 失败关闭；只调用 scoped internal；codegen 无漂移；不接受客户端伪造 provenance/correlation，不新增终端 actor 透传合同 | 2 合并，owner 3 |
| 4 | Business Console 单批 attempt 时间线 | `scope:M` / 中 | dispatch/reprint 可区分；unknown 与不完整 `started` 明示；显示“受信服务请求”与可用 correlation，不显示虚构用户名或“已打印” | 3 合并 |
| 5 | 独立审计 retention 落地；仅在 owner 选择自动清理时实现对应清理入口与真实 PostgreSQL 证据 | `scope:M` / 按裁决复评 | retention 与 batch 独立、scope 隔离、复合 FK `Restrict`；retained attempt 时 parent 删除失败，attempt 到期/删除后未来 parent 清理才可继续；若有自动清理则失败可恢复且 attempt 清理不删除 batch | owner 5 已冻结关系；具体期限、删除授权与执行方式仍待局部批准 |
| 6 | 现场核验/恢复授权 spike（仅当 owner 选择新增） | `scope:spike` / 高 | 先修订 #2065；冻结 actor、结论、授权和状态机后再拆生产票 | owner 4、6 |

所有生产子项都以“#2065 的 attempt 边界修订已批准”为共同前置，除此之外按表中局部门控：owner
1/4/5 的 Schema 关系已冻结，owner 2 前置于历史覆盖字段选择；owner 3 前置读面/Gateway；owner 5 的
具体期限/执行方式只前置 retention 子项 5；owner 6 只决定是否启动可选子项 6。子项 1 不应同时承担 API/UI/retention，子项 2
不应顺手开放跨租户 printer 报表。这样不会用无关裁决串行化独立工作，也不会把质量轴问题扩张为
printer-agent 或通用审计平台。

## 验证与未验证

本报告通过 live GitHub Issue、当前 `origin/main` 的 Domain/Application/Infrastructure producer、当前
Architecture/Reference 和既有测试核对结论。作者本地只执行了只读源码/历史查询、相对链接检查与
`git diff --check`，未在本地运行 .NET、PostgreSQL、FullChain、真实 FileStorage、真实打印机/扫码枪或
物理标签验证。

GitHub CI 并非“未运行”：上一审核 head `c2944dba2b9d69f5272ab5d685be446dd45dbea9` 的 run
`33529538293` 中，`CI Impact Plan` 实际运行并按 docs-only 影响计划将 PostgreSQL、Business FullChain、
后端业务分片等测试 worker 标记为 policy-skipped；`Backend Tests`、`Frontend Unit Tests`、
`Frontend Typecheck and Build` 等 aggregate jobs 与 `CI Summary` 实际运行且成功。aggregate 成功只证明
影响计划与汇总闭合，不替代被跳过 worker 的 .NET/PostgreSQL/FullChain 执行证据。后续新 head 的
exact-head 状态必须从 PR checks 重新读取，不沿用该 run。
