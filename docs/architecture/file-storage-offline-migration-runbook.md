# FileStorage 停服离线迁移运行手册

## 用途与适用范围

本文件是目标技术规格/operator runbook；执行资格与当前交付状态以 GitHub Issue 树和 `implementation-readiness.md` 为准。

本手册把 [ADR 0027](../adr/0027-filestorage-offline-migration-cutover-and-rollback.md) 的稳定决策展开为 FileStorage 单 active provider 停服离线迁移的目标接口、人工决策和证据契约。通用文件与 `VersionedArchive` 分轨清点、复制、验证和清源，只共享全局 admission fence、operator 决策与 evidence pack 汇总。`VersionedArchive` 保持 MinIO-only，不并入通用 provider。

provider 搬迁只保持已经 canonical 的 v1 `ObjectKey`；legacy/non-canonical key 的审计和显式修复映射归 #994，在其未闭合时失败关闭，不在迁移中静默 normalize、重写或同时接受 alias。对象存储 I/O 与 PostgreSQL activation transaction 是有序、分别留证的动作，不是跨系统原子事务。

## 执行资格与状态权威

执行资格与状态权威只指向 [Issue #1644](https://github.com/Mang-X/Nerv-IIP/issues/1644)、[Issue #1013](https://github.com/Mang-X/Nerv-IIP/issues/1013) 和 [implementation-readiness.md](implementation-readiness.md)。本 PR 不修改 readiness，也不在本手册复制动态责任、进度或日期信息。

## 角色与决策分离

- migration operator：建立运行输入、执行受管目标接口、维护 checkpoint，并汇总证据。
- storage/infra owner：确认 source/target 物理身份、连接、权限、容量、配额、版本化及合规能力。
- 业务冻结确认人：确认所有业务入口停止、在途 mutation 清空，且独立 admission fence 持续有效。
- independent verifier：独立复核双 manifest、全量 exact verify、catalog/remap、CAS 与抽样证据，不执行最终授权。
- 最终 approver：在 pre-open 窗口内明确选择 rollback 或 commit，并另行授权延迟清源。

任何人不得把 storage 配置变更、工具返回成功或未观察到写入当作另一角色的批准。operator 与 verifier 的证据职责、最终 approver 的业务开放决策以及 cleanup 的独立授权必须分开记录。

## 动作类型图例

- `事实核对`：只读取并核对现有环境、配置、身份、状态或证据，不建立迁移事实。
- `人工门禁`：需要明确的人工作业、授权、停服确认或二选一决策。
- `目标接口（非当前命令）`：未来实现必须提供的目标契约；它不是当前可复制执行的 CLI、API 或脚本命令。

## activation 状态机

全局 activation record 固定使用 `archiveStoreName=compliance-archive`，并至少记录 `activeStorageIdentity`、单调 `revision`、migration generation、`manifestDigest`、`migrationRunId` 与 activation state。append-only activation audit 必须覆盖每次 activation、拒绝、rollback 尝试及成功结果，记录 expected/observed CAS 事实、run/manifest、operator 和时间，不覆盖历史行。state 名称只允许：

- `source-active`：source 是授权 store；属于开放态。
- `target-pre-open`：target 已写入授权事实但业务尚未开放；属于唯一封闭迁移态。
- `committed-for-open`：target 已经 operator commit；属于开放态。

开放态只表示 activation 允许成为正常业务候选；正常业务 v2 Put/Get/Delete 还必须同时满足外部 admission fence 已解除。`target-pre-open` 一律阻断正常业务 v2 Put/Get/Delete。`absent` 仅表示 genesis 前数据库行不存在，不是第四个 state 或别名。

允许的转换矩阵精确为：

| 转换 | 用途 | 必要约束 |
| --- | --- | --- |
| `absent → source-active` | genesis bootstrap | 只允许幂等 `create-if-absent`，初始 revision 和 audit 同事务写入。 |
| `source-active → target-pre-open` | 正向迁移进入 pre-open | fence、双 manifest、全量验证、catalog/remap 与 v2 readiness 全部通过。 |
| `target-pre-open → source-active` | pre-open rollback | 业务从未开放、fence 完整、source readiness 通过，并满足精确 CAS。 |
| `target-pre-open → committed-for-open` | commit target | 最终 approver 明确授权，state 与 activation audit 同事务提交。 |
| `committed-for-open（旧 run）→ target-pre-open（新 run）` | reverse migration | 新建 run，把旧 run 的 committed target 作为新 source，并先完成 freeze/manifest/preflight。 |

同一 `migrationRunId` 内，`committed-for-open` 不可回到 `source-active`。reverse migration 必须使用新的 `migrationRunId`、generation、manifest 与证据链。除矩阵所列转换外，所有转换以及任何 state 别名都失败关闭。

## genesis 与配置/数据库双权威

首次部署且 activation record 不存在时，startup 从显式 `ComplianceArchiveBucket` 解析 actual opaque storage identity 与 config fingerprint，再调用幂等 PostgreSQL transaction `create-if-absent`。该事务只可创建 `source-active`、`revision=1`，并令 `migrationRunId`、migration generation、`manifestDigest` 均为 null；同一事务追加 genesis activation audit。

并发 bootstrap 只允许唯一赢家。竞争失败者必须读取赢家提交的行并走正常双权威核对；existing row 不得 reseed、overwrite，亦不得根据当前配置改写。

configured identity/fingerprint 负责解析实际物理身份，数据库 `activeStorageIdentity` 负责授权。除 genesis `create-if-absent` 外，两者不得互相覆盖。已有 record 时 configured identity/fingerprint 与授权身份不一致、record 缺失但不满足 bootstrap、record/state 非法，或授权 store 不可用，startup 必须 `startup fail-fast/unready`；不得自动改配置、自动改 record 或 fallback 到其他 bucket。

## 运行输入与身份冻结

运行在阶段 0 冻结以下输入；后续所有证据显式引用这些值，不依赖目录名、当前配置或人工记忆：

| 输入 | 来源 | 冻结时点与规则 |
| --- | --- | --- |
| 唯一 `migrationRunId` | 受授权的迁移编排 | 计划批准后生成，不复用；reverse migration 使用新值。 |
| source/target opaque storage identity | 各 provider 的实际 identity preflight | fence 前读取，必须证明不是同一物理后端。 |
| source/target 配置指纹 | 脱敏后的配置解析结果 | 与 opaque identity 一起冻结；credential 不进入证据。 |
| activation `revision`/state | PostgreSQL activation record | freeze 时读取，并作为后续 CAS expected 输入。 |
| migration generation 与 `manifestDigest` | activation/manifest 目标接口 | generation 在运行建立时冻结；digest 在双 manifest 闭合后冻结。 |
| 双轨 scope | 通用文件 metadata 与归档完整 recorded key 集合 | 明确 organization/environment 和边界，不从目录隐式推导。 |
| operator/approver | 本次人工授权记录 | 记录身份与职责分离，不记录 credential。 |
| 停服窗口与回退责任 | 变更授权 | freeze 前批准；超窗即停止，不自行延长。 |
| 工具/接口版本 | 迁移目标接口与 resolver capability 输出 | 首次 preflight 冻结，重试不得静默升级。 |
| evidence pack 位置 | 本次运行的受控证据存储 | 计划阶段分配；所有分区绑定 run/manifest。 |

## 双 manifest、evidence 与 remap

### 通用文件 manifest

通用文件 manifest 每项必须包含：

- canonical v1 `ObjectKey`；
- source actual existence、source actual size、source actual canonical SHA-256；
- metadata identity、expected size/checksum 及其与实际字节的 reconciliation；
- source provider identity/config fingerprint；
- 稳定状态和失败原因。

manifest 同时列出一致、missing、orphan、size/SHA conflict 计数，并冻结整体 digest。任一 missing、orphan、冲突，或 legacy/non-canonical key 尚未由 #994 显式闭合，都阻断 copy/cutover；不得静默 normalize、重写、跳过或接受 alias。digest 冻结后，同一 run 重试不得增删或重新解释条目。

### VersionedArchive manifest

归档 manifest 每项必须包含：

- `archiveStoreName=compliance-archive`；
- 完整 recorded key；
- nullable `archiveEvidenceId`，且 legacy 的 `archiveEvidenceId` 永久为 null；
- source opaque storage identity；
- source exact `versionId`；
- source 实际 canonical SHA-256；
- source 实际 size；
- object lock/retention/legal hold 状态与读取证据。

完整 recorded key 固定为：

`organizationId + environmentId + archiveKind + batchId + objectKey + recordedVersionId + canonicalSha256 + sizeBytes`

新 evidence 与经受控 exact source readback 接纳的 legacy evidence 都是 immutable recorded evidence。locator 不得只凭 `objectKey/versionId`、当前 bucket、endpoint、prefix 或当前配置匹配。

### target evidence 与 immutable remap

每个归档对象 copy 后必须按 exact version 从 target 回读，并追加 immutable remap。每条 remap 至少包含：

- 完整 recorded key；
- source opaque storage identity 与 target opaque storage identity；
- `targetObjectKey`；
- target exact `versionId`；
- target canonical SHA-256/size；
- `migrationRunId`；
- `manifestDigest`；
- 稳定 `remapId`。

target `versionId` 不要求等于 source/recorded `versionId`。source evidence/audit 不改写。缺项、歧义、多映射、链冲突或环均失败关闭；resolver 不得猜测 target version。

## additive v2 resolver 结果语义

### 共同 admission

正常 Put/Get/Delete 仅在 activation 为 `source-active` 或 `committed-for-open` 且 admission fence 已解除时运行；`target-pre-open` 一律阻断。resolver 必须核对 active identity、activation revision 和 evidence/remap 链，不能由 caller 的 physical locator 绕过授权。

### Put v2

Put v2 按 organization/environment/archiveKind/batchId 等完整业务 scope 写 active store。active store exact readback 复验 canonical SHA-256/size 后，持久化 immutable recorded evidence，并返回 `archiveEvidenceId` 与 recorded evidence。写入、readback、catalog append 分别留证，不能把它们称为单一原子动作。

### Get v2

Get v2 以 `archiveEvidenceId` 或完整 recorded key 进入 resolver，解析 recorded evidence、可选 remap 与 active identity，按 resolved exact object key/version 回读并复验 SHA-256/size。结果同时返回 recorded evidence、resolved physical evidence、`remapId` 与 activation revision。证据缺失、歧义、多映射、环，或 resolved identity 与 active identity 不一致时失败关闭。

### Delete v2 与 legacy

Delete v2 必须复用 Get v2 的同一 resolver/证据链，按 resolved exact version 执行 authorization、legal hold/retention 门禁和删除；禁止 caller physical locator 绕过 remap。

legacy batch 的 locator 永久为 null，以完整 recorded key 解析。成功解析也不回填 batch 或历史 audit。只有新的 restore/delete audit 可以 append-only 记录 recorded evidence、resolved physical evidence、`remapId`、activation revision、actor/reason/authorization/result；历史 recorded version 保持不变。

## rollout 与持续 admission fence

v2 rollout 必须能在 genesis/`source-active` 下正常生产运行。全部 producer、exact Get/restore consumer、exact Delete consumer 切换 v2 并通过 capability gate 后，迁移前禁用 v1，且必须先于进入 `target-pre-open`；任何 v1 旁路都保持 source 冻结并阻断 activation。

从 producer freeze 到 operator 最终决策，持续阻断通用文件与 archive 的 Put/Delete、upload/PATCH/complete、Get/restore、GC、retention，以及未来任何 physical mutation。停进程不是 fence 的充分证据；必须有独立网络/admission fence，并证明无在途 mutation。pre-open target 只有绑定同一 `migrationRunId`/`manifestDigest` 的 `目标接口（非当前命令）` 可以写入本 manifest 拥有的对象和受管 catalog/remap；其他 writer 或越界写入立即失败关闭并隔离 target。

## evidence pack 与脱敏

evidence pack 使用以下分区，并使每份记录绑定 `migrationRunId`、generation 与 `manifestDigest`：

- `planning-and-authorization`：scope、角色分离、窗口和批准。
- `preflight`：source/target identity、配置指纹、连接、权限、容量与合规能力。
- `freeze-and-fence`：入口清单、在途 mutation 清零和持续 fence 证明。
- `manifests`：双 manifest、reconciliation、计数与冻结 digest。
- `copy-checkpoints`：逐轨 checkpoint、same/conflict 与续跑边界。
- `exact-verification`：目标实际字节 size/SHA-256、exact version 与合规状态。
- `catalog-and-remap`：recorded evidence、immutable remap 与闭合计数。
- `activation-and-config`：expected/observed CAS、配置切换、revision/state 与 activation audit。
- `operator-decision`：rollback/commit 授权和结果。
- `post-open-samples`：确定性样本的 recorded/resolved evidence。
- `cleanup`：分轨授权、目标复验、source 删除和保留清单。
- `failures-and-retries`：稳定原因、checkpoint、重试次数与下一安全态。

证据不得包含 credential、对象内容、完整敏感 root 或不必要的完整 key。需要关联 key 时使用受控摘要与 evidence ID；只有受权验证环境保存最小必要的 exact locator。

## 操作流程

每张操作表固定使用“前置、输入、动作类型、动作、成功证据、失败停止/重试、下一安全态”七字段。阶段 0–7 各一张；阶段 8 分 rollback/commit 两张；阶段 9–10 各一张，总计 12 张。未实现的自动动作只使用 `目标接口（非当前命令）`，人工授权/停服/决策使用 `人工门禁`，读取既有事实使用 `事实核对`。

### 0. 计划、授权与身份冻结

| 前置 | 输入 | 动作类型 | 动作 | 成功证据 | 失败停止/重试 | 下一安全态 |
| --- | --- | --- | --- | --- | --- | --- |
| 迁移范围已获评审，尚未建立任何 target 字节 | source/target 连接描述、双轨 scope | 事实核对 | 解析两端 opaque storage identity/config fingerprint，证明 source/target 非同一物理后端 | 脱敏 identity/fingerprint 对照与非同源结论 | 无法证明时停止；修正输入后可重新计划，不复用已冲突 run | 未迁移，source 继续为授权 store |
| 身份核对通过 | run、角色、窗口、回退责任、工具/接口版本、evidence pack 位置 | 人工门禁 | 冻结唯一 run、双轨 scope、角色分离、停服窗口和证据位置 | planning-and-authorization 记录，所有批准引用同一 run | 任一输入不完整时不进入 preflight；补齐后同一 run 可重核 | 已授权但尚未停服，source 保持开放 |

### 1. target preflight

| 前置 | 输入 | 动作类型 | 动作 | 成功证据 | 失败停止/重试 | 下一安全态 |
| --- | --- | --- | --- | --- | --- | --- |
| 阶段 0 身份已冻结 | target identity/fingerprint、预期 scope | 目标接口（非当前命令） | 验证连接、最小权限、目标认领、容量、配额与 emergency reserve；拒绝未知既有内容 | target preflight 报告与认领证据 | 同 identity 修复连接/权限/容量后可重试；identity 变化须废止 run | source 仍开放，target 未被迁移写入 |
| 通用 target preflight 通过 | archive target 与合规策略 | 目标接口（非当前命令） | 额外验证 bucket 初始化/认领、versioning、object lock、retention、legal hold 与 exact-version readback 能力 | archive capability 报告与脱敏读取证据 | 任一能力缺失即停止；同 identity 修复后可重试 | source 仍开放，target 仅被认领 |

### 2. 停服与持续 admission fence

| 前置 | 输入 | 动作类型 | 动作 | 成功证据 | 失败停止/重试 | 下一安全态 |
| --- | --- | --- | --- | --- | --- | --- |
| target preflight 通过且停服窗口生效 | 全部通用/archive 入口、worker 与在途请求清单 | 人工门禁 | 停止 Put/Delete/upload/PATCH/complete/Get/restore/GC/retention 和未来 mutation 入口，等待在途 mutation 清零 | freeze 时间点、入口状态和在途计数为零 | 任一入口或在途 mutation 未闭合即停止；闭合后重建 freeze 证据 | source 字节冻结，业务未开放 |
| 业务入口已停止 | 网络/admission policy、冻结 activation revision/state/generation | 目标接口（非当前命令） | 建立独立持续 fence，拒绝业务重启和旁路调用，并冻结 activation 输入 | fence 正反探针、冻结 revision/state/generation | fence 破坏即废止其后证据，修复后从 freeze 重新生成 | source 冻结且 fence 生效 |

### 3. 生成并冻结双 manifest

| 前置 | 输入 | 动作类型 | 动作 | 成功证据 | 失败停止/重试 | 下一安全态 |
| --- | --- | --- | --- | --- | --- | --- |
| source 冻结且 fence 持续有效 | 通用 metadata、归档 recorded evidence、source identity | 目标接口（非当前命令） | 分轨生成通用文件 manifest 与 VersionedArchive manifest；逐项 actual byte readback，完成 metadata/evidence reconciliation | 两份 manifest 的一致/missing/orphan/conflict 清单及读取证据 | 任一 legacy key 未闭合或任一 missing/orphan/conflict 非零即阻断；修复 source 事实后须重新 freeze | source 保持冻结，尚未 copy |
| 双轨 reconciliation 全部闭合 | 两份不可变 manifest | 目标接口（非当前命令） | 冻结统一 `manifestDigest`；digest 后禁止重解释、增删条目 | digest、条目计数、生成工具版本与签章 | digest 不可重现则废止 run；同 run 只可重复核对同一字节 | source 冻结，manifest 已冻结 |

### 4. copy 与 checkpoint resume

| 前置 | 输入 | 动作类型 | 动作 | 成功证据 | 失败停止/重试 | 下一安全态 |
| --- | --- | --- | --- | --- | --- | --- |
| manifest 冻结、fence 完整且 target writer scope 已建立 | 同一 run/digest、双 manifest、checkpoint | 目标接口（非当前命令） | 通用与 archive 分轨复制；只写本 manifest 对象。target 已存在时 exact readback 判定 same/conflict | 每项 copied/same/conflict 结果、target 物理证据与 checkpoint | conflict 或越界 writer 立即停止并隔离 target；不得覆盖或吸收未知对象 | source 冻结；target 为未验证副本 |
| copy 中断且 digest 未变 | 同一 run/digest 和已提交 checkpoint | 目标接口（非当前命令） | 从最后一个已证明 checkpoint 幂等 resume，不重导 manifest | resume 起点、跳过项的 exact same 证据与新 checkpoint | checkpoint/digest 不匹配即拒绝；只有同 digest 可续跑 | source 冻结；target 保留可验证部分副本 |

### 5. 全量 exact verify 与 catalog 闭合

| 前置 | 输入 | 动作类型 | 动作 | 成功证据 | 失败停止/重试 | 下一安全态 |
| --- | --- | --- | --- | --- | --- | --- |
| copy 已遍历全部 manifest 条目 | target identity、双 manifest、copy checkpoints | 目标接口（非当前命令） | 逐对象从 target 实际字节计算 size/canonical SHA-256；archive 固定 target exact `versionId`，复验 object lock/retention/legal hold | 通用与 archive 逐项 exact-verification 记录 | missing/conflict/readback 失败即停止；同 run/digest 修复可重试失败项但仍需重新汇总全量计数 | source 冻结；target 未获开放资格 |
| target 逐项验证成功 | recorded evidence 与 target exact evidence | 目标接口（非当前命令） | 为 archive 追加 immutable remap 并验证 catalog 无缺项、歧义、多映射、冲突或环 | `expected=verified` 且 `failed=missing=conflict=0`，catalog/remap 闭合 | 闭合失败即保持 activation 不变；post-open 样本不替代全量证明 | source 冻结；target exact 证据闭合 |

### 6. v2 rollout/capability gate

| 前置 | 输入 | 动作类型 | 动作 | 成功证据 | 失败停止/重试 | 下一安全态 |
| --- | --- | --- | --- | --- | --- | --- |
| v2 已能在 genesis/`source-active` 运行 | persistence、resolver/runtime、业务 schema 与全部 Put/Get/Delete consumer capability | 事实核对 | 验证 catalog/remap persistence、统一 resolver、业务 locator/audit schema 和所有 consumer 的 v2 结果语义 | capability matrix、合同结果与完整 consumer 清单 | 任一能力缺失或存在物理 locator 旁路即停止；修复后在 source-active 重核 | source 冻结或在窗口外继续 source-active |
| 全部 consumer 已切 v2 | v1/v2 路由与阻断探针 | 目标接口（非当前命令） | 迁移前禁用 v1，并证明 v1 Put/Get/Delete 无法旁路 remap | v1 拒绝探针与 v2 exact resolver 证据 | 任一 v1 旁路保持 source 冻结，不进入 target-pre-open | source 冻结，v2 readiness 通过 |

### 7. 切换到 target-pre-open

| 前置 | 输入 | 动作类型 | 动作 | 成功证据 | 失败停止/重试 | 下一安全态 |
| --- | --- | --- | --- | --- | --- | --- |
| fence、manifest、exact verify、remap 与 v2 readiness 全通过 | target 通用 provider 配置、冻结 source/target identity | 目标接口（非当前命令） | 保持 fence，将通用 provider 配置切到 target；核对配置与 target identity 一致 | 配置指纹、identity 对照与未开放探针 | 配置不一致即恢复冻结 source 配置并停止；不得起服 | 业务仍封闭，source 未清理 |
| 通用配置已指向已验证 target | expected active identity/revision/generation/`manifestDigest`/state/`migrationRunId` | 目标接口（非当前命令） | 以全部 expected 做 CAS，执行正向 `source-active → target-pre-open` 或新 reverse run 的合法转换；state transition 与 append-only activation audit 同 PostgreSQL 事务 | observed revision/state、事务 audit 与 run/manifest 绑定 | stale expected、双权威不一致或证据缺口使事务不变并记录拒绝，恢复冻结 source 配置且不解除 fence；对象 I/O 与本事务不宣称原子 | `target-pre-open`，fence 持续，业务未开放 |

### 8. operator 最终决策

pre-open rollback：

| 前置 | 输入 | 动作类型 | 动作 | 成功证据 | 失败停止/重试 | 下一安全态 |
| --- | --- | --- | --- | --- | --- | --- |
| 业务从未开放、fence 完整、source 未清理且 source readiness 通过 | approver、expected/observed revision、run/manifest、冻结 source identity/config | 人工门禁 | approver 明确选择 rollback；source 未清理仅是必要条件，不是充分条件 | operator-decision 记录与全部 rollback 条件核对 | 任一条件不足即保持 target-pre-open；补齐可证明条件后同 run 重决策 | `target-pre-open`，业务仍封闭 |
| rollback 已获授权 | target identity、cutover revision、generation/digest/state | 目标接口（非当前命令） | 在同一 PostgreSQL 事务 CAS `target-pre-open → source-active` 并追加 audit；事务成功后按顺序恢复冻结 source identity/config，source readiness 通过后才解除 fence | expected/observed CAS、audit、配置指纹、source readiness 与 fence 解除记录 | stale CAS 或双权威不一致时事务不变、配置不变、fence 不解除；同 expected 更新后可重核 | `source-active` 且 source 恢复开放；本 run 不开放 target |

commit target：

| 前置 | 输入 | 动作类型 | 动作 | 成功证据 | 失败停止/重试 | 下一安全态 |
| --- | --- | --- | --- | --- | --- | --- |
| `target-pre-open`、fence 完整且全量证据仍有效 | approver、expected/observed revision、run/manifest | 人工门禁 | approver 明确选择 commit target，并确认本 run pure rollback 将永久关闭 | operator-decision 授权与证据摘要 | 未授权或证据漂移即保持 target-pre-open；重建证据后再决策 | `target-pre-open`，业务仍封闭 |
| commit 已获授权 | expected target identity/revision/generation/digest/state/run | 目标接口（非当前命令） | PostgreSQL 同事务 CAS `target-pre-open → committed-for-open` 并追加 activation audit；事务成功后才解除 fence | expected/observed CAS、`committed-for-open` audit 和 fence 解除记录 | CAS 失败则事务不变且 fence 不解除；成功后不得在同 run rollback | `committed-for-open`，target 获得开放资格 |

### 9. post-open 起服与确定性样本

| 前置 | 输入 | 动作类型 | 动作 | 成功证据 | 失败停止/重试 | 下一安全态 |
| --- | --- | --- | --- | --- | --- | --- |
| activation 为 `committed-for-open` 且 fence 已按 commit 记录解除 | target identity/config、activation revision | 目标接口（非当前命令） | 启动并开放 target，核对双权威与 readiness | startup readiness、identity/revision 与开放时间证据 | 启动或 readiness 失败立即重新停服；不得修改原 run 回 source | target 停服，准备新的 reverse migration |
| target 已开放且 readiness 正常 | 确定性样本集 | 目标接口（非当前命令） | 样本覆盖通用 download、archive v2 exact Get/restore 与受控 Delete；记录 recorded/resolved evidence、`remapId`、activation revision | post-open-samples 逐项结果；post-open 样本不替代阶段 5 全量证明 | 任一样本失败立即停服并创建新的 reverse migration run，不在原 run 回 source | 成功则 target 继续开放；失败则 target 停服并启动 reverse 计划 |

### 10. 延迟、独立授权的 source cleanup

| 前置 | 输入 | 动作类型 | 动作 | 成功证据 | 失败停止/重试 | 下一安全态 |
| --- | --- | --- | --- | --- | --- | --- |
| cutover 已独立验收，cleanup 延迟窗口届满且仍可取消 | 通用/archive 分轨范围、source identity/key/version、单独 approver | 人工门禁 | 分别授权两轨 source cleanup；授权不由 cutover 自动产生，不允许 bucket/root 广泛删除 | 两份可撤销授权、精确 manifest 范围与 source identity | 未授权、范围过宽或身份漂移即不删除；修正后重新授权 | target 继续开放，source 保留 |
| 对应轨道已获独立授权 | target exact bytes、catalog/remap、committed audit、source exact locator | 目标接口（非当前命令） | 删除前逐项复验 target 与 activation 证据，再按 source identity/key/version 删除并复核；legal hold/retention 阻断时保留对象 | 分轨 expected/deleted/blocked/failed 计数、删除复核与稳定 `cleanup-blocked-by-legal-hold` 原因 | 部分成功保留 checkpoint，可按同 scope 重试未删项；不得解除 hold、缩短 retention 或把阻断计成功 | target 继续开放；source 仅保留明确 blocked/failed 对象与证据 |

## 容量与健康优先级

容量准入以 [ADR 0024](../adr/0024-filestorage-storage-provider-and-local-production-semantics.md) 为单一权威，本手册只应用其动作语义，不复制第二张分叉表：

- `capacity restricted` 时，离线迁移 `source read/verify` 允许，`target copy/new target bytes` 拒绝；source 可读不表示 target 可写。
- `restore write` 默认拒绝；只有 FileStorage 停服恢复模式、容量 preflight 通过、显式 `operator override` 三项同时具备时，才可作为受控例外允许。
- `runtime critical/unready` 优先级更高，覆盖 capacity restricted 下的允许项并阻断全部动作；后端身份、安全性或可读性恢复可信前，不继续 read、copy、verify、rename、delete、cleanup 或 restore write。

## 失败停止与重试矩阵

| 场景 | 停止结果 | 是否可按同 run/digest 重试 | 下一安全态 |
| --- | --- | --- | --- |
| identity/config 漂移 | 阻断批次，废止漂移后的证据 | 否；重新计划并冻结身份 | source 保持或恢复冻结前授权态 |
| fence 破坏或发现未冻结 mutation | 立即停止，废止 freeze 后 manifest/copy 资格 | 否；修复 fence 后从 freeze 重建证据 | 业务停服，source 不清理 |
| manifest missing/orphan/conflict | copy/cutover 阻断并输出逐项原因 | 可；修复 source 事实后重建 manifest 与 digest | source 冻结，target 不开放 |
| legacy evidence 缺失或 non-canonical key 未闭合 | 阻断 catalog 与 copy，不猜填 | 否；先取得完整 recorded key/exact readback 或闭合 #994 | source 冻结，target 不开放 |
| capacity/health 拒绝 | 按 ADR 0024 拒绝相应 I/O；critical 阻断全部动作 | 可；同 identity 恢复健康并重新 preflight | source 保持，target 不新增字节 |
| target 已存在且 exact readback 为 same | 记录 same，不覆盖 | 可；按同 checkpoint 幂等继续 | target 保留一致对象，仍未开放 |
| target 已存在且为 conflict | 停止并隔离 target，禁止覆盖 | 否；调查未知写入并重新认领/计划 | source 冻结，target 隔离 |
| copy 中断 | 保留已证明 checkpoint，不重新解释 manifest | 可；仅同 run/digest/checkpoint | source 冻结，target 保留部分副本 |
| remap 缺项、歧义、多映射、链冲突或环 | resolver/cutover 失败关闭 | 可；只追加纠正事实后重做全量闭合 | activation 不变，业务封闭 |
| v1 旁路仍可运行 | activation 阻断 | 可；禁用旁路并重跑 capability gate | source 冻结，未进 pre-open |
| CAS stale | 事务不变并追加拒绝 audit | 可；读取新 observed 事实后重新判定，不盲重放 | 原 activation state 保持不变 |
| 越界 target writer | 批次失败关闭并隔离 target | 否；未知对象不得吸收进原 manifest | source 冻结，target 隔离 |
| 对象 I/O/DB 原子性误述 | 拒绝该证明，要求拆成有序动作和独立证据 | 可；重建正确 evidence pack | activation 不变，业务封闭 |
| pre-open rollback 条件不足 | 不执行 CAS/config 回拨 | 可；仅在业务从未开放、fence/readiness 等条件全部可证时重决策 | `target-pre-open`，fence 保持 |
| operator 已 commit | 本 run pure rollback 永久关闭 | 否；故障处理必须新建 reverse migration | `committed-for-open` 或重新停服后的 reverse 计划 |
| post-open 失败 | 立即停服，禁止在原 run 改配置回 source | 否；创建新 reverse migration run | committed target 成为新 source |
| cleanup 部分成功 | 停止受影响轨道，保留逐项 checkpoint/失败清单 | 可；同授权 scope 仅重试未删项 | target 开放，source 保留未删项 |
| legal hold/retention 阻断 | 输出稳定原因并保留 source version | 可；仅待合法阻断自然解除并重新授权 | target 开放，受保护 source 保留 |

## evidence pack 完成判据

各层证据必须独立满足，前一层不得冒充后一层：

1. 文档审计：ADR 链接、状态机、字段、阶段、失败关闭、容量继承和 operator 决策语义经独立复核。
2. runtime/schema/contracts 资格：activation/evidence/remap persistence、CAS/audit、v2 contract、resolver、业务 locator/audit schema 和全部 consumer capability 有可重复合同证据。
3. 目标迁移接口可用性：双 manifest、copy/checkpoint、exact verify、catalog/remap、activation/config、reverse 与 cleanup 目标接口具备版本化的结果和失败语义证明。
4. 真实基础设施演练：在受管 Local/S3-compatible 与 MinIO `VersionedArchive` 环境分别验证身份、容量、versioning/object lock/retention/legal hold、实际字节、故障恢复与 fence。
5. operator 决策：每个 run 的 rollback 或 commit 具有明确 approver、expected/observed CAS、run/manifest、audit 与开放边界证据。
6. cleanup 独立验收：延迟窗口、分轨授权、target 再验证、source 精确删除、blocked/failed 保留与重试证据单独闭合。

任一层缺失时，只能陈述已取得的该层证据，不能推导生产切换、回退、清源或 tracker 结论。
