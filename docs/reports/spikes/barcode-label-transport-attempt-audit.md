# BarcodeLabel transport attempt 审计模型预研

## 结论

#2148 在基线 `97d3e6eb3437d9927684dfdf4b967940fa5ca205` 上仍然成立。当前
`label_print_batches.printer_id / print_job_id / failure_reason` 只保存最近一次 transport
attempt；单项 reprint 会覆盖整批 dispatch 的这些列，`label_print_items` 又没有 printer、job
或 failure 字段，因此当前模型无法回答“某个 item 先后重打了几次、每次发送到哪台打印机、结果是什么”。

建议后续采用 BarcodeLabel 自有的独立 `label_print_transport_attempts` 表，逐次记录 dispatch
和 reprint 的 transport 事实。该表不是物理打印确认、outbox、printer-agent job/ack，也不接管
`LabelPrintBatch` 的业务生命周期；现有批次列继续作为兼容的“最近一次尝试投影”。历史行不从批次列
伪造 attempt。只有 #2065 的 attempt 边界修订获批，并由 owner 确认本文列出的 6 项产品与治理
选择后，才能进入生产实施。

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
| `status` | `started` / `failed` / `delivery-unknown` / `sent-to-printer`。不允许 `printed`。 |
| `print_job_id` | `sent-to-printer` 与 `delivery-unknown` 必填，其余为空。 |
| `failure_reason` | `failed` 与 `delivery-unknown` 必填；`started` / `sent-to-printer` 为空；只存安全摘要。 |
| `started_at_utc` | 调用 printer 前、attempt 预登记成功时写入。 |
| `completed_at_utc` | 三个封闭 transport 结果必填；`started` 为空。 |

必要约束：operation/item 空值矩阵；status/job/failure/completed 时间矩阵；batch 上
`(id, organization_id, environment_id)` 候选键与 item 上 `(id, label_print_batch_id)` 候选键，配合
上述两个复合 FK 让跨 scope batch 和错 batch item 无法成为持久事实。Application 写入边界仍需先
按 scope 加载，但不能代替数据库不变量。建议查询索引只保留
`(organization_id, environment_id, label_print_batch_id, started_at_utc desc, id desc)` 与
reprint 查询所需的 `(label_print_item_id, started_at_utc desc, id desc)`，不为尚不存在的报表
增加 printer/status 全局索引。

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
| terminal 或 batch 最近投影任一同库写入失败 | 同一事务回滚，attempt 保持 `started` | batch 最近投影保持原值 | 禁止自动重放；不得形成只有一侧成功的半应用 |

最后两行只保留同步 TCP 真正不可消除的“attempt 预登记已提交—外部发送—terminal 与 batch 投影
原子提交”窗口，不主动制造两个同库 terminal 事实之间的窗口。引入同库 outbox 仍不能让数据库事务
包住外部 TCP 副作用；printer-agent ack 可以缩小/重定义窗口，但属于 #2065 明确排除的另一套架构。
本候选不以更多重试或状态掩盖该事实。

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
- 响应只返回 attempt ID、operation、sequenceNo、printerId、status、job、failure 摘要和时间；不返回
  ZPL、标签值、endpoint 或原始异常。
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
合同；batch 删除不得 cascade 删除未到期 attempt，attempt 到期也不得驱动 batch 删除。

| 选择 | 优点 | 风险 | 裁决 |
| --- | --- | --- | --- |
| 永久保留、无清理 | 不会误删审计 | 数据单向增长，无法给容量与删除授权边界 | 不作为最终合同，也不作为未声明期限的临时默认。 |
| 独立审计在线期 + 总保留期 | 容量、在线查询与合规边界可分别治理 | 需要 owner 给出时长、legal hold 与删除授权 | 唯一推荐候选；精确时长待 owner 批准。 |
| 跟随 batch 生命周期或 cascade | 实现表面简单 | 与 ADR 0003 冲突，业务删除会提前删除审计 | 拒绝；若未来确需采用，必须先有新 ADR 明确取代 ADR 0003 对应条款。 |

清理必须按 organization/environment 分批、可观察、可重入，并在 attempt 仍被调查/hold 时跳过；删除
权限不能等同于 `business.barcodes.print`。当前没有足够事实决定保留天数、legal hold 或执行 owner，
因此独立审计 retention 实施票必须等待 owner 裁决，不能在 Schema PR 中顺手加一个任意定时任务，
也不能等待或复用 batch retention。

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

## 必须由 owner 裁决的 6 项

1. **审计保证等级：** attempt 预登记失败时是否严格禁止 transport；terminal 落库失败时 HTTP、告警与
   `started` 行如何处理。推荐“预登记失败则零 transport；未封闭行禁止自动重放”。
2. **历史覆盖表达：** 只在 API 文档声明 post-cutover，还是增加 batch 级覆盖起点。推荐显式覆盖起点，
   避免空历史被读成“从未打印”。
3. **读取授权批准：** 确认采用已冻结候选 `business.barcodes.transport-attempts.read`，并批准哪些角色显式获得；
   不再把“复用 print 或新建 permission”的二选一留给实现者。
4. **操作人归因：** 只记录 trusted service/correlation，还是扩展 Gateway -> BarcodeLabel 的终端 actor
   传递合同。当前 internal Bearer 不能证明终端操作人，禁止反向猜测。
5. **Retention：** 在线/总保留期、legal hold、删除授权与执行 owner。没有裁决前不得承诺自动清理。
6. **人工确认：** 是否继续完全不落库，还是另立“现场核验”产品合同。推荐本轮保持不落库；若新增，
   必须单独修订 #2065，且不得把人工陈述改写成物理设备 ack。

## 建议实施拆解

| 顺序 | 建议子项 | Gate | 独立验收 | 依赖 |
| ---: | --- | --- | --- | --- |
| 1 | BarcodeLabel attempt 实体、迁移、复合归属约束、最小索引、独立预登记、原子 terminal + batch 投影 recorder 与真实 PostgreSQL crash-window/并发测试 | `scope:M` / 高 | 每次 printer 调用一条 attempt；预登记失败时 printer 零调用；跨 scope batch 与错 batch item 的原始插入被数据库拒绝；两个受控并发完成者恰好一个成功，败者不能覆盖 terminal/job/failure/time 或 batch 投影；删除 scope/关联约束、把条件更新改为无条件更新的等价错误变异必须判红；既有 batch/item 状态机不变 | #2065 attempt 规格修订已批准；owner 1、2；[`validity.md`](../../governance/testing/validity.md) |
| 2 | BarcodeLabel scoped internal 分页查询、覆盖语义、OpenAPI 与安全投影测试 | `scope:M` / 中 | 错 scope 安全未找到；分页稳定；无 raw ZPL/endpoint/异常泄漏；历史空集合不冒充完整 | 1 合并，owner 2、3 |
| 3 | BusinessGateway 只读 facade、权限目录、generated client | `scope:M` / 中 | 认证 scope/permission 失败关闭；只调用 scoped internal；codegen 无漂移 | 2 合并，owner 3、4 |
| 4 | Business Console 单批 attempt 时间线 | `scope:M` / 中 | dispatch/reprint 可区分；unknown 与不完整 `started` 明示；不显示“已打印” | 3 合并 |
| 5 | 独立审计 retention/hold worker 与真实 PostgreSQL 分批清理证据 | `scope:M` / 高 | 独立周期、scope 隔离、hold 跳过、失败可重入；batch 删除不 cascade，attempt 清理不删除 batch | owner 5；不依赖或复用 batch retention |
| 6 | 现场核验/恢复授权 spike（仅当 owner 选择新增） | `scope:spike` / 高 | 先修订 #2065；冻结 actor、结论、授权和状态机后再拆生产票 | owner 4、6 |

所有生产子项都以“#2065 的 attempt 边界修订已批准”为共同前置；子项 1 不应同时承担
API/UI/retention；子项 2 不应顺手开放跨租户 printer 报表；子项 5 在独立审计 retention 未裁决时
不得启动。这样每个生产 PR 都保持单一 owner seam，且不会把质量轴问题扩张为 printer-agent 或通用
审计平台。

## 验证与未验证

本报告通过 live GitHub Issue、当前 `origin/main` 的 Domain/Application/Infrastructure producer、当前
Architecture/Reference 和既有测试核对结论。只执行了只读源码与历史查询及 Markdown 静态检查；未运行
.NET 测试、PostgreSQL、CI、FullChain、真实 FileStorage、真实打印机/扫码枪或物理标签验证，因为本次
没有生产改动，也不对任何运行能力作新增声明。
