# 标签模板资产退役与配额释放合同预研

## 结论

#2101 在 `origin/main` 基线 `490782607023795a721124a125a3d369cb1c8a2e` 上仍然成立。
`barcode-label-template` 没有 retention，FileStorage files API 也没有业务授权的删除入口；用途级
`8,388,608` bytes quota 会按非 `deleted` 文件与未过期上传预留累计，最多容纳 128 份达到
`65,536` bytes 上限的资产。当前只有 retention worker 能把正式文件软删，而该 purpose 刻意不配置
`RetentionSeconds`。

建议由 BarcodeLabel 独占“资产是否仍被引用”和“以后不得再引用”的业务裁决，FileStorage 只执行
精确 scope/owner/purpose/file/checksum 的幂等逻辑退役、quota 释放和物理清理。模板 `inactive` 不等于资产
无引用；历史批次冻结 `TemplateFileIdSnapshot`，仍可 dispatch 或 reprint 时必须保留资产。

当前实现只足以核实 dispatch/reprint 守卫，并不存在生产资产删除引用函数。下文执行可达性矩阵只是
拟议的释放策略；它会收窄 #2065 已批准的“历史批次引用即保留”边界，必须由 owner 显式修订后才能
进入生产。在修订前，任何历史批次快照指向的资产都继续按引用处理。模板当前引用如何
原子转为不可复用的 retirement、最终用户使用何种权限仍需 owner 批准；两跳 HMAC proof 与有限 replay
horizon 已完成熔断裁决。本文只给出推荐候选和每项不超过
`scope:M` 的拆解，不以报告代替长期 ADR 或 owner 裁决。

本次只提交调查报告，不修改生产代码、Schema、API、权限、事件、UI、配置或测试治理。

## 范围门

- 级别：`scope:spike`。
- 难度：高；主要难点是跨 BarcodeLabel/FileStorage 的并发引用冻结、不可逆副作用、失败重放与
  “quota 已释放”和“物理字节已删除”不能混报。
- 调查范围：引用权威、授权链、软删/物理删除、幂等、审计、失败恢复、quota 时点、无 retention
  运营兜底及实施拆解。
- 非目标：手工删库、扩大 quota、自动按模板年龄删除、通用文件删除平台、Ops 假任务、legal hold、
  restore API、printer-agent、物理出纸确认或全平台 service-auth 重构。
- 所有生产子项按真实 seam 独立交付，不把 BarcodeLabel Schema、FileStorage Schema/API、Gateway、
  Console、provider 和告警混进同一个 PR。

## 已核实的当前事实

| 面 | 当前事实 | producer |
| --- | --- | --- |
| purpose | `barcode-label-template` 只允许 `.json`、专用 MIME、64 KiB、SHA-256 与 `business-barcode-label / label-template / {templateCode}`；默认 quota 为 8 MiB，无 retention。 | [`appsettings.json`](../../../backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/appsettings.json)、[`file-storage-baseline.md`](../../architecture/file-storage-baseline.md) |
| FileStorage API | files API 只有上传、完成、metadata/list/usage、download grant，没有删除/退役 endpoint。 | [`FileStorageEndpoints.cs`](../../../backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Endpoints/Files/FileStorageEndpoints.cs)、[`IFileStorageService.cs`](../../../backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Application/Files/IFileStorageService.cs) |
| quota | `CalculateUsedBytesAsync` 排除 `status == deleted` 的文件，并计入未过期、未完成 upload session 的预留。 | [`PostgreSqlFileStorageService.cs`](../../../backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Application/Files/PostgreSqlFileStorageService.cs) |
| 清理 | retention worker 先软删，grace 到期后删除 metadata/grant/session；当前字节清理只覆盖本地 tus 路径。 | [`FileStorageGarbageCollection.cs`](../../../backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Application/Files/FileStorageGarbageCollection.cs) |
| 物理定位 | `ObjectKey` 尚未参与通用文件实际读写/删除；#994 传递依赖 #1628，#997/#1012 提供二选一生产 provider。 | [`file-storage-baseline.md`](../../architecture/file-storage-baseline.md)、[Issue #994](https://github.com/Mang-X/Nerv-IIP/issues/994) |
| 模板 | 模板只有一个当前 `TemplateFileId` 与 `active / inactive`；更新可以替换 fileId，也可以把 inactive 再改为 active。 | [`LabelTemplate.cs`](../../../backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Domain/AggregatesModel/LabelTemplateAggregate/LabelTemplate.cs)、[`CreateOrUpdateLabelTemplateCommand.cs`](../../../backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/LabelTemplates/CreateOrUpdateLabelTemplateCommand.cs) |
| 批次快照 | 新批次只从 active 模板创建，并冻结 fileId、asset SHA-256、变量 schema、码制和 renderer version；旧全空快照失败关闭。 | [`CreateLabelPrintBatchCommand.cs`](../../../backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/PrintBatches/CreateLabelPrintBatchCommand.cs)、[`business-platform-domain-architecture.md`](../../architecture/business-platform-domain-architecture.md) |
| dispatch | 只有 `pending / failed` 可 dispatch；编译会重新下载冻结资产，并为整批全部 item 生成文档。 | [`LabelPrintBatch.cs`](../../../backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Domain/AggregatesModel/LabelPrintBatchAggregate/LabelPrintBatch.cs)、[`PrintLabelLifecycleCommands.cs`](../../../backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/PrintBatches/PrintLabelLifecycleCommands.cs) |
| reprint | 只有 `sent-to-printer / printed` 批次可 reprint；item 只在 `voided / consumed` 时被拒绝。reprint 仍先编译整批，再选择一个文档发送。 | 同上 |
| unknown | dispatch `delivery-unknown` 同时阻断 dispatch/reprint；reprint unknown 不改批次状态，仍允许操作员现场确认后再次发起，但当前没有确认落库 API。 | 同上 |
| attempt 审计 | #2148 已冻结未来独立 attempt 候选；历史不合成 attempt，空时间线不证明“从未打印”，`started` 代表结果未封闭且禁止自动重放。 | [`barcode-label-transport-attempt-audit.md`](barcode-label-transport-attempt-audit.md) |
| 内部身份 | 当前共享 Bearer 只产生统一的 `internal-service` principal，不能识别具体调用服务或替代最终用户授权。 | [`InternalServiceAuthentication.cs`](../../../backend/common/ServiceAuth/Nerv.IIP.ServiceAuth/InternalServiceAuthentication.cs)、[`authorization.md`](../../governance/security/authorization.md) |

## 引用权威与历史执行可达性候选

### 权威边界

- BarcodeLabel 是模板、批次快照、批次/item 状态和未来 retirement decision 的唯一业务事实源。
- FileStorage 只知道文件 metadata、owner、purpose、checksum、可下载状态、quota 和物理位置；不得跨服务
  猜测模板/批次引用，也不得把 `inactive` 或文件年龄解释为删除授权。
- BusinessGateway 只完成最终用户 IAM 授权、scope 校验和 facade；不持久化引用或清理事实。
- 两服务不得跨 schema join；BarcodeLabel 形成持久授权后，通过公开窄契约请求 FileStorage 执行。

### 已批准保守边界与推荐候选函数

#2065 当前已批准边界是：模板仍被 active 模板或历史批次引用时不得物理清理。因此在 owner 1 批准
完整矩阵并登记 #2065 新修订前，只要历史批次的完整快照指向目标 fileId，就继续返回 `referenced`，
不得按 batch/item 状态缩小。以下函数不是 current producer，也不是既有裁决；它是 owner 1 待裁决的
推荐候选，用于说明若允许释放不可再执行的历史资产，应如何失败关闭。

对目标 `organizationId + environmentId + fileId + checksum`，只有完整 scope 精确匹配才进入判断；
任何查询失败、未知状态、部分快照、缺失模板 owner 或摘要冲突都返回 `unknown`，删除流程失败关闭。

`TemplateReachable(fileId)`：

1. 任一 active 模板的当前 `TemplateFileId == fileId`，返回 `true`。
2. 任一 inactive 模板的当前 `TemplateFileId == fileId`，在尚未由同一受审 retirement 转换冻结为
   “不可再用”前仍返回 `true`。`inactive` 本身不终止引用，因为当前命令可以换资产或重新激活。
3. 没有当前模板指向该 fileId，返回 `false`。

候选 `BatchExecutionReachable(batch, fileId)`：

| 冻结快照与批次/item 事实 | 结果 | 原因 |
| --- | --- | --- |
| `TemplateFileIdSnapshot != fileId` | `false` | 该批次不消费目标资产。 |
| 全空 legacy snapshot | `false` | 没有目标 fileId 引用，且现有链不能合成重放资产。 |
| 部分快照、缺失同 scope 模板 owner、摘要冲突或未知状态 | `unknown` | 持久事实不完整，禁止据此删除。 |
| 完整 snapshot，batch=`pending` | `true` | 可以整批 dispatch，且会下载/编译该资产。 |
| 完整 snapshot，batch=`failed` | `true` | 可以再次整批 dispatch。 |
| 完整 snapshot，batch=`sent-to-printer`，至少一个 item 非 `voided / consumed` | `true` | 该 item 可以 reprint；`created` 也未被现有守卫拒绝。 |
| 完整 snapshot，batch=`printed`，至少一个 item 非 `voided / consumed` | `true` | 兼容 reprint 可达。 |
| 完整 snapshot，batch=`sent-to-printer / printed`，全部 item 均为 `voided / consumed` | `false` | 现有 reprint 守卫没有可发送 item。 |
| 完整 snapshot，batch=`delivery-unknown` | `false`，但形成 deletion hold | 当前 dispatch/reprint 均不可达；没有持久现场确认，因此不能把“不可执行”提升为“可安全物理删除”。 |

只有 owner 1 批准该矩阵及 #2065 修订后，目标资产才可按以下条件成为候选：

```text
TemplateReachable(fileId) == false
and all matching batches have BatchExecutionReachable == false
and no matching batch/attempt has deletion hold
and no matching fact is unknown
```

候选判断必须与“以后不得再引用”原子闭合。否则检查结束后并发模板更新可重新选择该 fileId，或并发
批次创建可冻结它。推荐候选是在 BarcodeLabel 持久化 scope+fileId 唯一的 retirement decision，并保证
同一 fileId 下 retirement 与模板复用/批次冻结不可同时成功。owner 1 只裁决该业务不变量与完整矩阵；
具体事务、锁或并发 token 由实现选择，并以真实 PostgreSQL 并发验收，不作为 owner 选型题。

### #2148 对本函数的约束

1. reprint 的 `failed / delivery-unknown / sent-to-printer` attempt 不改变 batch/item 状态，因此 terminal
   attempt 不能缩小上述执行可达集。只要 batch/item 守卫仍允许，资产继续被引用。
2. 历史批次列不回填 attempt；没有 attempt 行不能证明历史上未发送，也不能作为资产无引用证据。
3. 如果未来 attempt ledger 已实施，匹配 batch 的未封闭 `started` attempt 额外形成 deletion hold；它按
   “可能已发送”处理，不能自动重放或用删除模板资产掩盖。
4. owner 2 只决定 attempt API/Console 如何表达 post-cutover 覆盖，并不门控本函数；引用判断读取当前
   模板、冻结快照和状态 producer，不根据 attempt 时间线是否完整推断。
5. owner 3 只门控谁可读取 attempt 详情。服务端引用判断不需要向发起人泄漏 attempt；若清理页面嵌入
   attempt 时间线，必须另持获批的 `business.barcodes.transport-attempts.read`，不能以退役权限隐式扩大读取。
6. owner 6 只决定是否另立持久现场核验合同。当前没有该事实，因此口头确认、correlation 或 reprint
   guidance 都不能解除 `delivery-unknown / started` deletion hold；未来若要解除，须先修订 #2065 和本合同。

## 推荐候选合同

### 发起、授权与执行

| 阶段 | 责任方 | 推荐候选 | 失败关闭条件 |
| --- | --- | --- | --- |
| 发起 | Business Console user | 选择 inactive 模板/旧资产，提交必填 reason 与 idempotency key | 无最终用户身份、跨 scope、reason/key 缺失 |
| 最终用户授权 | BusinessGateway + IAM | 使用 owner 批准的独立退役 permission；Gateway 校验 principal、organization/environment 与 template/file resource，并签发给 BarcodeLabel 的短时 final-user proof | 只有按钮隐藏或 internal token，没有 IAM 授权事实 |
| 业务裁决 | BarcodeLabel | 在同一并发边界内执行引用函数并持久化唯一 retirement decision | 任一 `true / unknown / hold`、并发所有权丢失 |
| 服务间授权 | BarcodeLabel -> FileStorage | purpose-specific、短时、不可篡改 capability，绑定 decisionId、scope、fileId、checksum、owner、purpose | 共享 internal Bearer 单独出现、签名/TTL/字段不匹配 |
| 生命周期执行 | FileStorage | 精确核对 stored metadata 与 capability，幂等进入 physical-hold 并写 durable tombstone | 非 `barcode-label-template`、owner/scope/checksum/decision 冲突 |
| 物理清理 | FileStorage provider worker | grace 后按 canonical final locator 删除并回读证明不存在 | provider 只返回成功、却不能证明对象不存在 |

推荐独立退役 permission，而不直接复用 `business.barcodes.templates.manage`：后者当前允许可逆的模板创建、
替换和启停，物理删除会消除历史重放资产并释放 quota，风险和审计读取面不同。permission 名称、默认角色、
是否需要第二人批准仍由 owner 裁决 2 冻结，报告不自行注册权限或角色。

已批准 purpose-specific、版本化 HMAC proof，只保护本窄动作，不重构全平台 internal auth。两跳使用不同
部署 secret：BusinessGateway/BarcodeLabel 负责第一跳，BarcodeLabel/FileStorage 负责第二跳；Production
缺 secret 必须启动失败，secret 不入数据库、日志、响应或审计。worker 可为同一持久 decision 重签短时
capability，不创建第二个业务决定；共享 internal Bearer 单独出现时两跳都失败关闭。
BusinessGateway owner 负责第一跳 secret 的签发侧配置与协调轮换，BarcodeLabel owner 负责第二跳；verifier
只接受当前部署的 active key。v1 不新增 key discovery、数据库 key registry 或通用轮换平台，轮换走受控的
issuer/verifier 协调部署。

v1 wire contract 对两跳完全对称：payload 以字段表规定的固定顺序编码，每个字段使用
`{UTF8-byte-length}:{value}`，字段之间只用 ASCII LF（`0x0A`）分隔；整段使用 UTF-8、无 BOM、不得做
locale/case/Unicode 隐式改写。算法固定 HMAC-SHA-256，signature 使用无 padding Base64Url 并常量时间比较；
版本、算法、字段缺失/重复、顺序、长度、编码、分隔、signature、issuer、audience、clock、TTL、key 或
payload 任一错误都失败关闭。`issuedAt`/`expiresAt` 是 Unix seconds；TTL 与 clock skew 的精确边界如下，
未来改变算法、字段顺序或 canonicalization 必须提升版本。

第一跳由 BusinessGateway 生产、BarcodeLabel 验证，字段依次为 version、algorithm、issuer、audience、
issuedAt、expiresAt、subject、permission、canonical request digest。digest 另以同一长度前缀规则覆盖
action、organizationId、environmentId、templateId、fileId、checksum、reason、idempotency key，再做
SHA-256/Base64Url；BarcodeLabel 必须从实际 request 重算。第二跳由 BarcodeLabel 生产、FileStorage 验证，
字段依次为 version、algorithm、issuer、audience、issuedAt、expiresAt、decisionId、replayPolicyVersion、
`W_client`、BarcodeLabel retirement executor lease/max-backoff、organizationId、environmentId、fileId、
checksum、ownerService、ownerType、ownerId、purpose。pre-terminal proof 只授权当前一次调用，不携带
`ReplayUntilUtc`；FileStorage 不消费 subject、permission、reason 等最终用户展示字段，其 tombstone 只引用
decisionId。issuer/audience 是由每跳 producer/verifier owner 各自配置的唯一常量，不接受调用方任意值。

两跳都要求 `expiresAt > issuedAt` 且 `expiresAt - issuedAt <= 300` 秒；使用同一个注入的
`TimeProvider` 判定 `issuedAt >= now + 300s` 为尚未生效、`expiresAt <= now - 300s` 为已过期，边界包含在
拒绝侧。测试分别钉住正 TTL、零/负 TTL、超过 300 秒、未来 issuedAt 及过期 expiresAt；不得用一个
“签名失败”断言替代时钟分支。

### 持久审计候选

BarcodeLabel retirement decision 至少保留：decision/idempotency、scope、template id/code、file id、
checksum、requester subject、permission、reason、correlation、引用判定结果、状态、attempt count、safe
error code 与关键时间。它证明“谁依据哪些 BarcodeLabel 事实授权”，不保存模板正文或 ZPL。

FileStorage durable deletion tombstone 至少保留：decision、scope、file/owner/purpose、size/checksum、进入
physical-hold 与 quota 释放时间、物理删除状态/attempt/safe error/completed time及上游审计引用。tombstone 必须晚于
`stored_files`/对象物理删除继续存在，不暴露 `object_key` 或存储凭据。

两类记录分别属于业务裁决与文件生命周期执行，不能只留临时日志，也不能把 FileStorage 的
`deletion_reason` 在 metadata 行删除后当作永久审计。已批准有限 replay horizon：

```text
H = clamp(max(W_client, physical grace + 2 * GC interval,
              execution lease + 2 * max recovery backoff), 8 days, 90 days)
```

`W_client` 是 `barcode-label-template` purpose-specific 配置，默认 30 天；不是平台通用 retention。current
main 只证明 physical grace=7 天、GC interval=5 分钟；现有 upload-commit 的 lease/backoff 不是 retirement
producer。未来每个 BarcodeLabel retirement executor 与 FileStorage physical executor 都提供各自专用的
lease/max-backoff 配置，lease 与 max-backoff 的默认值分别为 5 分钟并在启动时要求正数，不能复用
upload-commit 配置。公式中的 execution lease 与 max recovery backoff 分别取所有参与 retirement executor
冻结值的最大值；这只是把已批准的单个恢复下限应用到完整执行链，不增加第四种 horizon。

BarcodeLabel 创建 pending decision 时冻结 replay policy version、`W_client` 与自身 executor 输入；短时
capability 携带这份快照。FileStorage 首次接受 decision 时冻结自身 physical grace、GC interval 与 executor
输入，用版本化纯函数解析 H、持久化 H，并在同 decision 的幂等结果中返回原 H；配置改变后重放仍返回
首次结果，不按 live 配置重算。BarcodeLabel 收到结果后保存同一 H。响应丢失只需以同 decision 重试，不
增加第二阶段、补偿 API 或分布式事务。

由于不存在 2PC/ack 且 FileStorage 不保留永久 decision fence，pending 不能被宣称永久可恢复。
BarcodeLabel 在首次发送前的本地 decision 事务内同时写 `FirstSentAtUtc` 与
`RecoveryUntilUtc = FirstSentAtUtc + 7 days`；在 `now < RecoveryUntilUtc` 时才可为同 decision 重签和
重试。若到 `now >= RecoveryUntilUtc` 仍未取得 FileStorage 的幂等结果，BarcodeLabel 原子转为永久
fail-closed `execution-outcome-unknown`/hold，停止 signer/executor，保留最小非敏感 fence 与审计。该状态
只说明“FileStorage 可能从未接受、可能已接受但 physical delete 尚未完成、也可能已经完成”，不得伪造
quota/物理删除结论；hold 的解除需要另行
裁决，本合同没有人工 purge。FileStorage 对首次接受的 decision 至少保留 8 天；其自身 terminal+H 通常
更晚。这样 7 天恢复期严格落在下游最短保证内，同时不要求两服务共享绝对时钟或增加确认阶段。

两服务分别在自身 terminal-complete 数据库事务内由注入的本地 `TimeProvider` 同时写
`CompletedAtUtc`、H 和 `ReplayUntilUtc = CompletedAtUtc + H`；因此共享的是冻结时长 H，不宣称两个绝对
截止时刻相同。后续配置变更不追溯改写 pending 已冻结输入、FileStorage 首次接受结果、terminal 记录或
历史截止。pending/failed 不因窗口到期清理；只有 terminal complete 且 `now >= ReplayUntilUtc` 才由各自
service owner 的自动 worker 清理，不提供人工 purge。BarcodeLabel
永久 retirement fence 只保留 file 已不可复用及 replay 已过期的非敏感业务事实，不保留 subject/reason；
它是窗口外稳定 `replay-window-expired` 的唯一公开 producer。FileStorage tombstone 到自身截止后可清理，
不再从已清记录推断业务错误码；BarcodeLabel 在其 fence 判定过期后不得重签 capability。两侧都不得重执行、
重建 decision/tombstone 或尝试从日志推断旧结果。

公式的纯函数证据必须使用显式数值真值表分别让 `W_client`、`physical grace + 2 * GC interval`、
`lease + 2 * max recovery backoff` 三个 max 分支胜出，并覆盖默认 30 天、8 天 clamp 的小于/等于/大于、
90 天 clamp 的小于/等于/大于。时间证据使用同一 `FakeTimeProvider` 证明 `now < ReplayUntilUtc` 可重放，
`now == ReplayUntilUtc` 与 `now > ReplayUntilUtc` 均过期；推进同一时钟后同时观察 owner cleanup、公开响应和
零 provider/executor 重执行，避免各自设置时钟形成假绿。
若 clamp 前任何原始安全输入 `physical grace + 2 * GC interval` 或
`lease + 2 * max recovery backoff` 已超过 90 天，服务必须启动失败；不得用 90 天上限静默生成短于安全
输入的 H。90 天 clamp 只限制可配置的 client replay 诉求，不覆盖内部安全下限。

### quota 与物理删除时点

1. FileStorage 在一个 metadata 事务内验证 capability、写 durable tombstone，并把 available 文件转为
   quota 不计入、下载不可兑换、但不具备 legacy GC eligibility 的 physical-hold 逻辑状态或等价 discriminator；
   具体 Schema 形态留给子项 4。不得直接调用
   当前 `MarkDeleted` 形成到期的 `PhysicalDeleteAfterUtc`，否则旧 collector 会先删 metadata 再 best-effort
   删除 local tus 字节，破坏重试事实。
2. 该事务提交后，quota 计算必须像当前排除 `deleted` 一样排除 physical-hold size；这是唯一
   quota-released 时点。事务失败或回滚时文件仍 available，quota 不变。
3. 当前新建 download grant 不检查 file status，只有 content 兑换要求 available。本合同只冻结字节边界：
   physical-hold 后新旧 grant 均不得兑换 content；不额外引入“禁止创建无用 grant”的首版门禁。
4. 子项 6 显式把 physical-hold 转为 physical-eligible 后，才可按 grace 删除 final bytes、关联
   grant/session/metadata；只有 provider 回读证明不存在后，才把
   tombstone 标为 physical-complete。
5. 当前 `ObjectKey` 不是实际通用 final locator；#994 传递依赖 #1628，且 #994 与实际启用 provider
   （#997 或 #1012）完成前，任何
   实施只能如实报告 metadata physical-hold / quota-released，不能报告物理字节已删除。

## 失败与重放矩阵

| 场景 | 必须结果 |
| --- | --- |
| replay window 内，相同 idempotency key + 相同 scope/file/checksum/reason | 返回同一 retirement decision，不重复释放 quota |
| replay window 内，相同 key + 不同 payload | conflict，原 decision 不变 |
| replay window 内，不同 key + 同 scope/file（无论 requester/reason 是否相同） | 稳定 conflict 并可返回安全的既有 decision 引用；不得创建第二 decision、泄漏原 requester/reason 或暴露数据库唯一约束错误 |
| BarcodeLabel terminal complete 且 `now >= ReplayUntilUtc` | BarcodeLabel 由永久最小非敏感 retirement fence 返回稳定 `replay-window-expired`，不再签发 capability；这是唯一公开 producer。两侧都不得重执行、重建记录或把数据库 not-found 冒充从未发生 |
| retirement 与模板重新选择同 fileId 并发 | 同一 fileId 栅栏只允许一方提交；retirement 赢则后续复用失败，模板更新赢则 retirement 重查并拒绝 |
| retirement 与新批次冻结同 fileId 并发 | 同上；不能出现“批次已引用但 decision 仍获批” |
| BarcodeLabel 查询失败或读到未知/hold | 不创建授权，不调用 FileStorage |
| BarcodeLabel decision 已提交，FileStorage 未收到 | decision 保持待执行，退避重试；文件 available，quota 未释放 |
| pending/failed decision 的 5 分钟 capability 过期 | 不改状态；尚未形成 `replay-window-expired` fence 且 `now < RecoveryUntilUtc` 时，为同一 decision 重签后重试；proof TTL 不充当 replay horizon |
| FileStorage metadata 事务失败 | 文件 available、quota 不变、tombstone/metadata 不得半应用 |
| FileStorage physical-hold 已提交，响应丢失 | replay window 内同 decision 重放返回同一 quota-released 结果，不产生第二 tombstone |
| FileStorage 已接受且可能仍在 physical pending 或已 physical-complete，响应持续丢失至 `now >= RecoveryUntilUtc` | BarcodeLabel 原子转永久 `execution-outcome-unknown`/hold，停止重签与 executor；只报告下游可能未完成或已完成，不伪报 quota/physical 结论，不重建任何记录 |
| FileStorage 从未接受，且 `now >= RecoveryUntilUtc` | 与上一行相同的永久 unknown/hold；不因本地没有下游结果而重建 decision 或继续执行，解除另行裁决 |
| recovery 截止转换与已在途成功响应并发 | 同一 BarcodeLabel decision/CAS 只允许一个结果提交：已知结果先提交则 terminal-known；unknown 先提交则迟到响应不得覆盖 hold、补报 quota/physical 或触发新的 signature、HTTP、executor |
| 文件已经由同 decision 删除 | replay window 内幂等成功 |
| 文件被不同 decision/reason 删除 | conflict；不得冒领对方审计 |
| scope/owner/purpose/checksum 任一不匹配 | 失败关闭，不改变文件或 quota |
| legacy GC 跨过 grace 运行 | physical-hold 不满足旧 collector eligibility；metadata/session/bytes 与 durable tombstone 均保留，content 仍不可兑换且 quota 保持已释放 |
| provider 物理删除失败 | 文件保持 physical-hold/不可下载，quota 保持已释放；记录 safe error，退避重试 |
| provider 返回成功但回读不能证明不存在 | 不标 physical-complete |
| tombstone complete 且 `stored_files` 已不存在 | FileStorage 自身 replay window 内同 decision 重放仍返回同一完成事实；到自身截止清 tombstone 后不承担公开过期码 |
| matching batch 有 `delivery-unknown`，或未来 ledger 有 `started` | deletion hold；没有 owner 6 的持久现场核验合同不得解除 |

不提供 restore。误退役恢复会改变不可逆安全边界，必须另立合同；不能把隐式 undelete 塞进首版。

## 无 retention 的运营兜底

1. `barcode-label-template` 继续不配置自动 retention；只有上述受权 decision 能释放 quota。
2. usage 继续使用 FileStorage `/api/files/v1/usage` 的实际 quota 口径。推荐阈值为可配置：80% warning；
   剩余空间小于该 purpose 的单文件上限时 critical；100% 仍由上传 API 硬拒绝。
3. 运营指标分别报告待执行 retirement 数、最老 pending age、`execution-outcome-unknown` count/oldest age、
   quota-released 但未 physical-complete 数、provider retry 数；unknown 必须与引用 hold、attempt hold 分开，
   decision 离开 pending 后仍保持可见，不能只报一个“已清理”布尔值。
4. 运营顺序是：停用/替换模板 -> 请求 retirement -> 观察 quota-released -> 追踪 physical-complete。
   手工修改 `stored_files.status`、直接删对象或扩大 quota 都不是正式恢复动作。
5. 历史批次引用、`delivery-unknown / started` hold 必须单独可见；它们可能让一部分 quota 长期不可释放。
   没有另行批准并持久化的现场核验合同时，运营人员不能用口头确认绕过；若业务必须解除，应另立
   `scope:spike`，不得把它挂靠为 #2148 owner 6 已经批准的能力。

## Owner 局部门控

1. **模板 retirement 语义：** 是否批准完整候选引用矩阵并显式修订 #2065，把 inactive 模板当前 file
   引用原子转为“保留历史指针但不可再用于模板/批次”，且保证同 fileId 的 retirement 与模板复用/
   批次冻结不可同时成功。前置 BarcodeLabel 核心 Schema/命令子项；锁形态不由 owner 选择。
2. **退役权限与批准人：** 独立 permission 的规范名、默认角色，以及首版是否需要第二人批准。选项 A：
   首版单人授权，负面影响是高风险误操作只由权限与审计约束，但可直接进入既有 ≤M seam；选项 B：需要
   第二人批准，负面影响是多一个审批事实与等待/失效流程，必须先另做 `scope:spike` 冻结 approval owner、
   请求/批准人隔离、拒绝/撤回/过期。B 的克制 seam 固定在最终 retirement decision 上游：批准完成后
   BusinessGateway 才签发 final-user proof，BarcodeLabel 随后重新读取引用并原子创建最终 decision/fence；
   批准前不存在 pending retirement decision，也不得启动 executor。若 owner 不接受该不变量，B 被选中时
   子项 1/2/3/5/9 全部暂停并在 approval spike 后重新 Gate 2。推荐 A，避免在没有事实模型时暗造审批系统；
   本报告不代 owner 选择。前置 Gateway/Console 发起面，不阻塞只读 reference predicate 原型。
3. **服务间删除授权（已批准）：** 使用上述窄、版本化 HMAC proof，两跳独立 secret 与对称验证，不建设
   通用认证平台。该项不再门控生产拆解。
4. **审计 retention（已批准）：** 使用上述 purpose-specific horizon 公式、默认 30 天、8–90 天边界；
   terminal complete 起算，pending/failed 不因窗口到期清理，过期稳定返回 `replay-window-expired`，只由
   BarcodeLabel 永久最小非敏感 fence 对外产生该错误；FileStorage 到期自动清理且不保留永久 fence、
   不对外推断该错误。首次发送另有 7 天 recovery deadline，超时进入永久
   `execution-outcome-unknown`/hold；各事实 owner 自动清理且无人工 purge。该项不再门控生产拆解。

因此 #2101 四项新裁决中只剩 1、2 未决；3、4 已完成熔断裁决。

#2148 的 owner 2/3/6 不计入上述四项新裁决，继续按其报告局部门控：owner 2 只门控 attempt 覆盖展示；
owner 3 只门控 attempt 详情读权限/角色；owner 6 只决定是否另开现场核验 spike。三者都不能被解释为
当前已有资产删除授权或解除 deletion hold 的事实。

## 建议实施拆解

| 顺序 | 建议子项 | Gate | 独立验收 | 依赖 |
| ---: | --- | --- | --- | --- |
| 1 | BarcodeLabel retirement decision、候选引用函数、模板/批次复用拒绝与同 fileId 并发栅栏 | `scope:M` / 高 | 先以 owner 批准的 #2065 修订为 oracle；真实 PostgreSQL 覆盖 active/inactive current reference、非目标/legacy/partial snapshot、owner/checksum 冲突、batch/item 各等价状态分区与 unknown/hold，并让每个独立分区的最小错误变异判红；受控并发证明 retirement 与模板复用/批次冻结不可同时成功；同 key 重放、不同 key 同 scope/file 及原始唯一冲突均映射为稳定结果且不创建第二 decision | owner 1 及 #2065 显式修订；若 attempt ledger 已实施则接入 `started` hold |
| 2 | BarcodeLabel scoped retirement endpoint 与 final-user proof verifier | `scope:M` / 中 | 交付窄且版本化的 v1 wire contract；按实际 request 重算 digest；version/algorithm/canonicalization/encoding/delimiter/签名缺失或篡改/错误 key/issuer/audience/clock/TTL/subject/permission/action/scope/resource/checksum/reason/key 每个独立约束各用一个最小错误变异证明零 decision，不做字段笛卡尔积；internal Bearer 不替代 proof | 1 合同；owner 2；已批准 proof 合同 |
| 3 | BusinessGateway retirement facade、IAM permission、final-user proof producer、OpenAPI/codegen | `scope:M` / 中 | principal/permission/scope/resource/reason 失败关闭；生产 BusinessGateway signer/client 发出 actual HTTP request，由子项 2 的生产 BarcodeLabel endpoint/verifier 消费，成功 wire 接线归现有 `backend` fast shard/manifest；字段级负例仍归子项 2，不复制 verifier 矩阵、不升 FullChain、不隐式开放 attempt 详情 | 2；owner 2；若 owner 2 选择 B 则先完成 approval spike；#2148 owner 3 仅在嵌入 attempt timeline 时 |
| 4 | FileStorage purpose-specific delete endpoint、capability verifier、physical-hold、durable tombstone 与 quota 原子释放 | `scope:M` / 高 | 真实 PostgreSQL 重放/并发；生产 FileStorage verifier 对 version/algorithm/canonicalization/encoding/delimiter/签名/错误 key/issuer/audience/clock/TTL/decisionId/replay policy/version/冻结输入/scope/file/checksum/owner/purpose 每个独立约束用一个最小错误变异证明零 metadata/tombstone/quota 改动；正 TTL、零/负 TTL、超过 300 秒、future-issued `>=` 边界与过期 expiresAt 分别判红；首次接受冻结 FileStorage 输入、按纯函数真值表解析并持久化 H，同 decision 在配置变化后仍返回原 H；进入 physical-hold、不同 decision 不冒领、事务不半应用、usage 只下降一次、新旧 grant 均不能兑换 content；跨过 grace 运行旧 collector 后 metadata/session/bytes/tombstone 仍在。本子项不实现 BarcodeLabel signer，不写 physical-complete/ReplayUntilUtc | 已批准 proof/replay 合同；1，可与 3 实现并行 |
| 5 | BarcodeLabel durable executor 与 FileStorage 失败重放收敛 | `scope:M` / 高 | 首次发送前同一 decision 事务冻结 FirstSentAtUtc/RecoveryUntilUtc；唯一持久恢复 evidence owner 为“BarcodeLabel 生产 executor + 真实 PostgreSQL”lane：重建宿主后证明扫描、5 分钟 capability 过期后重签、响应丢失后重放取回同一 H并本地 terminalize；生产 BarcodeLabel signer/client→actual HTTP wire→子项 4 生产 FileStorage verifier 的成功接线另归现有 `backend` fast shard，只补一个代表性 tampered/wrong-key 请求，完整字段负例仍归子项 4；同一 FakeTimeProvider 与现有两种反例证明 `< RecoveryUntilUtc` 可恢复、`==`/`>` 原子转永久 `execution-outcome-unknown`/hold，同时分别断言零 signature、零 outbound HTTP、零 executor、零第二 decision。截止转换与已在途成功响应用同一真实 PostgreSQL decision/CAS 竞态：已知结果先提交则 terminal-known；unknown 先提交则迟到响应不得覆盖 hold、补报 quota/physical 或触发 signature/HTTP/executor | 1+4 |
| 6 | provider-aware final delete、回读不存在证明与物理重试 | `scope:M` / 高 | provider 回读证明不存在后，在同一 physical-complete 事务用冻结 H 与注入 TimeProvider 写 CompletedAtUtc/H/ReplayUntilUtc；PostgreSQL 崩溃矩阵只由生产 executor + 真实 PostgreSQL lane负责；Local/实际 provider 各只验证最小 delete/readback contract；`FileStorage Retirement PhysicalDelete Acceptance` 是场景名，不是新 required lane：以“生产 executor 持久状态与失败恢复”为唯一结论登记到既有 `postgres` requiredLane/manifest，并在其中运行真实 PostgreSQL + 当前实际启用 provider；选中后 actual identities/execution count/cleanup 必须闭合，zero-run、全部 skip 或 policy-skip 均失败；禁止 fake/非 PostgreSQL 代替，失败/回读不确定不假完成 | #1628 -> #994；实际启用 provider #997 或 #1012；4 |
| 7 | BarcodeLabel retirement decision retention | `scope:M` / 中 | 保存 FileStorage 幂等结果返回的同一 H，在本地 terminal 事务用注入 TimeProvider 写 CompletedAtUtc/H/ReplayUntilUtc；同一 FakeTimeProvider 覆盖 `<`、`==`、`>` replay 边界及 status/scope 变异，钉住 `now >= ReplayUntilUtc` 过期；另覆盖 recovery unknown/hold 后永久最小非敏感 fence 保留，并用 spies/counters 分别证明零 signature、零 outbound HTTP、零 executor、零 decision 重建；清理失败可重试收敛且不丢 fence，unknown hold 的解除不在本子项 | 已批准 replay/recovery 合同；5 |
| 8 | FileStorage deletion tombstone retention | `scope:M` / 中 | 使用首次接受时冻结并持久化的同一 H及子项 6 写入的 CompletedAtUtc/ReplayUntilUtc；配置变化不改历史；清理下限同时满足“首次接受后至少 8 天”和自身 terminal+H，取更晚者；同一 FakeTimeProvider 覆盖两个下限的 `<`、`==`、`>` 及 status/scope 变异；在 `==`/`>` 完成清理后以既有 spies/counters 证明零 provider delete、零 provider readback、零 tombstone 重建；未 terminal complete 不清理，到期后不保留永久 FileStorage fence、不产出公开 `replay-window-expired`，清理失败可重试收敛 | 已批准 replay/recovery 合同；6 |
| 9 | Business Console 基础退役发起与 decision/hold/quota 状态展示 | `scope:M` / 中 | 不把 inactive 当无引用，不把空 attempt 历史当从未打印；明确展示永久 `execution-outcome-unknown` hold，且不提供人工 purge/解除入口；基础页面不需要 attempt 详情权限；reason 与安全冲突不泄漏其他 requester | 3+5；#2148 owner 2/3 仅在展示 attempt 覆盖/详情时门控 |
| 10 | BarcodeLabel retirement/hold/pending 指标 | `scope:M` / 中 | decision 数、最老 pending、`execution-outcome-unknown` count/oldest age、引用 hold 分开从 BarcodeLabel owner 事实产生，decision 离开 pending 后 unknown 仍可见；`started` attempt hold 只在 ledger 已实施时接入且保持独立维度 | 1+5；attempt `started` 条件依赖 ledger |
| 11 | FileStorage physical retry/complete 指标与告警 | `scope:M` / 中 | quota-released、physical-complete、provider retry 与最老 physical pending 只从 FileStorage owner 事实产生并分显 | 6 |
| 12 | 跨服务资产退役 runbook 集成 | `scope:S` / 低 | 只串联子项 10/11 已有观测和处置；不新增第三套聚合状态、跨服务持久化或通用告警平台；无手工删库、直接删对象或扩 quota 的正式恢复方案 | 10+11；owner 6 只门控另行批准的可选现场核验 |

每个子项独立 PR。子项 1 不承担 FileStorage/Gateway/UI；子项 2/3 沿 #2067 的 producer/consumer 顺序，
不把跨服务改动硬塞进一个 M；子项 4 不顺手实现通用 purpose 删除，且在子项 6 上线前始终保持
physical-hold、不能被 legacy GC 清理；子项 6 不借本票实施完整 #994/#997/#1012；子项 9 不因缺
attempt-read permission 阻断基础退役状态。owner 2 若选择 B，approval 完全位于最终 decision 上游，先完成
approval spike 后再实施子项 3/9；若 owner 不接受该上游 seam，则子项 1/2/3/5/9 全部暂停并重新 Gate 2。

## 验证与未验证

本报告读取 [Issue #2101](https://github.com/Mang-X/Nerv-IIP/issues/2101)、[Issue #2148](https://github.com/Mang-X/Nerv-IIP/issues/2148)、
[PR #3023](https://github.com/Mang-X/Nerv-IIP/pull/3023) 合并报告、当前 main 的 BarcodeLabel/FileStorage
Domain/Application/Infrastructure producer，以及 Architecture/Governance/Reference。#2148 merge SHA
`3b6320e77f068bd3e83ff38e1dbeb512bb9863c3` 的 main CI run `33579745199` 成功，只证明该已合并报告
自身的 main acceptance；不替代本报告新 head 的 exact-head CI。

本 PR 是 docs-only spike。作者本地应执行相对链接存在性检查与 `git diff --check`；不运行或不宣称
.NET、PostgreSQL、FullChain、真实 FileStorage provider、真实打印机/扫码枪或物理标签验证。新 head 的
CI 必须如实区分实际运行的 impact/aggregate jobs 与 policy-skipped worker；aggregate success 不能冒充
被跳过的 provider/FullChain 证据。
