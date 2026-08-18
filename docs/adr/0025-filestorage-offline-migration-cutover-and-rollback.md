# ADR 0025：FileStorage 停服离线迁移、切换、回退与归档证据 remap

- 状态：已接受
- 日期：2026-08-18
- 关联：[Issue #992](https://github.com/Mang-X/Nerv-IIP/issues/992)、[Issue #1644](https://github.com/Mang-X/Nerv-IIP/issues/1644)、[Issue #1013](https://github.com/Mang-X/Nerv-IIP/issues/1013)、[Issue #1649](https://github.com/Mang-X/Nerv-IIP/issues/1649)、[Issue #1650](https://github.com/Mang-X/Nerv-IIP/issues/1650)、[Issue #1651](https://github.com/Mang-X/Nerv-IIP/issues/1651)、[Issue #1652](https://github.com/Mang-X/Nerv-IIP/issues/1652)、[Issue #1653](https://github.com/Mang-X/Nerv-IIP/issues/1653)

## 背景

[ADR 0023](0023-filestorage-tus-proxy-staging-final-complete-invariants.md) 已接受 staging、final 与 complete 提交不变量；[ADR 0024](0024-filestorage-storage-provider-and-local-production-semantics.md) 已接受通用文件运行时单一 active provider、canonical v1 `ObjectKey`、容量准入以及独立 `VersionedArchive` 边界。二者没有定义第 3 层的停服、manifest、逐对象复制与校验、active-store 切换、回退、归档 evidence/remap 和延迟清源契约。

本 ADR 承接该第 3 层，冻结通用文件与 `VersionedArchive` 的 offline-only 目标契约。它把 [Issue #1644](https://github.com/Mang-X/Nerv-IIP/issues/1644) 已批准 Spec v5 转化为长期架构决策，供 #1649 至 #1653 的未来实现使用。

## 范围与非范围

本 ADR 的范围仅包括通用文件与独立 `VersionedArchive` 的停服离线迁移拓扑、两类 manifest、证据/remap 所有权、activation 状态与 CAS、pre-open/post-open 回退边界、容量门禁及 source cleanup 语义。

以下内容不在本 ADR 范围内：

- 不实现或修改代码、测试、schema、migration、API、SDK、脚本、配置或部署模板；
- 不执行对象复制、真实迁移、生产切换、回退演练或 source cleanup；
- 不修改 ADR 0023、ADR 0024，也不重开其 staging/final/complete、单一 active provider、canonical v1 `ObjectKey`、容量准入或独立归档边界；
- 不完成第 4 层 FileStorage baseline、database schema catalog、deployment baseline、implementation readiness 等周边文档同步；
- 不把 `VersionedArchive` 合并进通用 provider，不设计在线迁移、dual-write、delta queue、read fallback、多 placement 或在线观察期。

## 当前实现事实

- 当前仓库尚无完整的通用 `IStorageProvider` runtime、canonical v1 `ObjectKey` 完整落地、archive evidence/remap catalog、global activation record、activation audit、统一 v2 resolver 或对象迁移工具。
- `scripts/install/migrate-file-storage.ps1` 只执行 FileStorage PostgreSQL EF migration，不搬迁对象字节。
- 当前 `VersionedArchive` v1 Put 返回 `ObjectKey`、`VersionId`、`Sha256`、`SizeBytes`、`VerifiedAtUtc` 五个 physical evidence 字段，没有 `archiveEvidenceId`。
- Scheduling 持久化上述物理 locator，并在 v1 Get/Delete 请求中直接提交 `ObjectKey`、`VersionId` 等物理证据。
- 下文的 `filestorage` schema seam、additive v2 contract、resolver、activation CAS 和迁移工具均是已接受目标。ADR 接受不等于这些能力已经交付。

## 决策

### 1. 离线拓扑、身份与不变量

1. 每个 FileStorage 运行实例始终只有一个 active provider。迁移必须 offline only：先停服，并使用独立网络/admission fence 阻断所有业务入口和旁路调用；禁止 dual-write、delta queue、read fallback、在线观察期以及服务运行中切换 provider。
2. 每个批次使用不可复用的 `migrationRunId`，绑定 source/target opaque storage identity、双方配置指纹、`manifestDigest`、operator、时间与工具版本。批次不得通过目录名、当前配置或人工记忆隐式关联。
3. 通用文件与 `VersionedArchive` 分轨生成 manifest、计数和证据。对象 I/O 与 PostgreSQL 事务分别留证，不宣称跨系统原子。
4. 从 freeze 到 operator 最终决策，所有 Put/Delete、upload/PATCH/complete、Get/restore、GC、retention worker 以及未来会改变物理字节或版本的入口都必须持续停服或被 fence 阻断。

### 2. 通用文件 manifest

通用文件使用独立且不可变的 manifest，整体冻结 digest。每个条目至少包含：

- canonical v1 `ObjectKey`；
- source 实际 existence、实际 size 与服务端按实际字节计算的 canonical SHA-256；
- metadata identity、expected size 与 checksum；
- source opaque provider identity 与 config fingerprint；
- reconciliation 状态、稳定失败原因和批次整体 `manifestDigest`。

provider 搬迁只保持已经 canonical 的 v1 `ObjectKey` 不变，不改变 `fileId`。legacy/non-canonical key 必须先由 #994 生成显式、无冲突的修复映射；本迁移不得静默 normalize、重写或同时接受新旧 alias。missing、orphan、identity conflict、size conflict 或 SHA conflict 任一未闭合时，都必须阻断 copy 与 cutover。

### 3. `VersionedArchive` manifest

归档必须使用独立 manifest，不得藏入通用文件 manifest。其全局 store 固定为 `archiveStoreName=compliance-archive`。每个条目至少冻结：

- 完整 recorded key，以及 nullable `archiveEvidenceId`；legacy 的 `archiveEvidenceId` 永久为 null；
- source opaque storage identity、source exact `versionId`、source 实际 canonical SHA-256、source 实际 size；
- object lock、retention、legal hold 状态及其读取证据；
- target opaque identity、`targetObjectKey`、target exact `versionId`、target exact readback、target 实际 canonical SHA-256、target 实际 size 与合规状态；
- `migrationRunId`、`manifestDigest`、条目状态与稳定失败原因。

只有 target exact readback 成功并证明 exact version、实际 size、canonical SHA-256 与合规状态后，才可追加 immutable remap。target `versionId` 不要求等于 source 或 recorded `versionId`；历史 evidence 与 audit 不得改写。

### 4. archive evidence、remap 与事实所有权

FileStorage 的 `filestorage` schema 拥有 global activation record、immutable evidence catalog、immutable physical remap catalog 和 append-only activation audit。Scheduling 继续拥有 archive batch、retention、source-row deletion、restore/delete 生命周期及业务审计；FileStorage 不复制或接管这些业务状态。

archive evidence 的完整 recorded key 固定为：

```text
organizationId + environmentId + archiveKind + batchId + objectKey + recordedVersionId + canonicalSha256 + sizeBytes
```

remap 除完整 recorded key 外，至少包含 source/target opaque storage identity、`targetObjectKey`、target exact version/SHA-256/size、`migrationRunId`、`manifestDigest` 与稳定 `remapId`。任何字段缺失、歧义、多重映射、链冲突或环都必须失败关闭；resolver 不得只凭 `objectKey/versionId`、当前 bucket、endpoint、prefix 或当前配置猜测目标。

### 5. activation 三态与转换

activation state 只能是 `source-active`、`target-pre-open`、`committed-for-open`，不得使用别名。`source-active` 与 `committed-for-open` 是开放态；`target-pre-open` 是唯一封闭迁移态。

同一 run 允许的转换固定为：首次 genesis `absent → source-active`；开始迁移 `source-active → target-pre-open`；pre-open operator 决策只允许 `target-pre-open → source-active` 或 `target-pre-open → committed-for-open`。同一 `migrationRunId` 的 `committed-for-open` 不可逆；返回原存储必须创建新的 reverse migration run。

### 6. genesis 与配置/数据库双重权威

首次部署且 activation row 不存在时，#1650 从显式 `ComplianceArchiveBucket` 解析 configured identity/fingerprint，再调用 #1649 提供的 PostgreSQL create-if-absent transaction seam。初值固定为 `source-active`、`revision=1`，`migrationRunId`、generation 与 `manifestDigest` 均为 null，并在同一事务追加逐字命名为 genesis activation audit 的记录。并发调用只允许唯一赢家；existing row 不得 reseed、overwrite 或根据配置重写。

配置负责解析 configured identity/fingerprint，数据库 activation record 通过 `activeStorageIdentity` 授权 active identity。除 genesis 外，两者不一致、record/state 非法或授权 store 不可用时，必须 startup fail-fast/unready；不得自动修改配置、自动修改数据库 record 或回落其他 bucket。

### 7. additive v2 contract 与 resolver

FileStorage additive v2 internal contract 的结果语义固定如下：

1. Put v2 只在开放态且 fence 已解除时运行；它完成 exact readback 后追加不可变 recorded evidence，并返回 `archiveEvidenceId` 与 recorded evidence。
2. Get v2 与 Delete v2 共用同一 resolver，以 `archiveEvidenceId` 或完整 recorded key 解析 recorded evidence、可选 remap 与 active identity，再访问 resolved exact object key/version。响应或审计记录必须包含 recorded/resolved evidence、`remapId` 与 activation revision。
3. Delete v2 还必须执行 authorization、legal hold 与 retention 门禁，不得继续按 caller 提交的物理 `objectKey/versionId` 绕过 remap。
4. legacy 的 `ArchiveEvidenceId` 永久为 null，并按完整 recorded key 解析；历史 batch、restore audit 与 delete audit 不回填、不改写。legacy catalog/remap 只能在停服且 source identity 已冻结后，由 #1653 按完整 recorded key 执行 exact source readback 再追加。只有新的 audit 可以 append-only 记录 recorded/resolved evidence、`remapId` 与 activation revision。
5. 正常 v2 Put/Get/Delete 只在 `source-active` 或 `committed-for-open` 且 fence 解除时运行；`target-pre-open` 阶段全部阻断。

### 8. rollout 顺序

v2 必须先能在 genesis/`source-active` 下正常生产运行。rollout 顺序固定为：全部 archive producer、exact Get/restore consumer 与 exact Delete consumer 完成 v2 rollout/capability 证明 → 迁移前禁用 v1，即禁用所有不识别 remap 的 v1 Put/Get/Delete → 才允许进入 `target-pre-open`。不得等待首次 target 激活后再禁用 v1。

### 9. 阶段顺序与门禁

离线迁移严格按以下顺序执行：

1. 冻结 `migrationRunId`、source/target identity、配置指纹、范围、授权、operator、工具版本、证据目录和停机窗口，并证明 source/target 不是同一物理后端。
2. 执行 target 连接、权限、认领、容量/配额/emergency reserve preflight；归档目标还验证 versioning、object lock、retention、legal hold 和 bucket 初始化能力。
3. 停服并建立网络/admission fence，drain 在途 mutation，冻结 source identity、activation revision/state、generation 与 manifest 入口事实。
4. 生成通用/归档双 manifest 与 digest，完成 metadata/evidence reconciliation；零失败后才可 copy。
5. 按同一 run/manifest/checkpoint 幂等 copy/resume；target 已存在时执行 exact readback 并判定 same/conflict。
6. 执行全量 exact verify/catalog：逐对象验证 target exact version、size、canonical SHA-256 与合规状态，闭合 evidence/remap；必须满足 expected=verified、failed=missing=conflict=0。
7. 证明 v2 readiness、全部 consumer 已切 v2 且 v1 已禁用。
8. 保持 fence，执行 pre-open cutover；通用配置切到 target，归档 activation CAS 进入 `target-pre-open`，并在同一事务写 activation audit。
9. operator 在 pre-open rollback 与 commit target 之间作最终二选一决策。
10. commit 后解除 fence、执行 post-open 起服与确定性样本；样本不替代 pre-open 全量 verify。
11. 进入延迟且单独授权的 source cleanup。

从 freeze 到最终决策期间，仅 #1653 的未来迁移工具可按同一 `migrationRunId`/`manifestDigest` 写 target 对象与受管 evidence/remap。非该工具、非本 run 或超出 manifest 的写入都必须失败关闭并隔离 target。

### 10. activation CAS 与 operator 决策

activation CAS 的 expected 只包含 PostgreSQL 可原子比较的事实：当前 `activeStorageIdentity`、revision、generation/`manifestDigest`、state 与 `migrationRunId`。state transition 与 activation audit 必须在同一 `filestorage` PostgreSQL 事务提交；stale identity/revision/generation/manifest/state 任一不符都失败关闭，不允许 last-write-wins。对象 copy/verify 与该事务是有序、分别留证的独立动作，不得描述成跨对象 I/O 与 PostgreSQL 的原子事务。

pre-open rollback（pure rollback）仅限业务从未开放且 fence 保持完整的窗口。它以冻结的 target identity、cutover revision、同一 generation/`manifestDigest`、`migrationRunId` 和 `target-pre-open` 执行 CAS，完成 `target-pre-open → source-active`，恢复冻结 source 的 active identity 与通用 provider 配置，并在同一事务追加 audit；source readiness 通过后才可解除 fence。source 尚未清理只是必要条件，不是允许 pure rollback 的充分条件。

operator 选择 commit target 时，必须先在同一 PostgreSQL 事务中执行 `target-pre-open → committed-for-open` 并追加 activation audit；事务成功后才允许解除 fence 和启动或开放业务入口。

### 11. post-open 与 reverse migration

target 一旦起服或任何业务入口开放，同一 run 的 pure rollback 永久关闭。此后任何失败都必须重新停服，以当前 committed target 为新 source、以保留的原 source 为新 target，创建新的 `migrationRunId`，重新执行 identity freeze、manifest、copy、全量 exact verify、catalog 与 cutover；只有新 run 的前置证据闭合后，才允许 `committed-for-open`（旧 run）进入 `target-pre-open`（新 run）。不得在原 run 修改配置或 activation 回 source。

post-open 确定性样本覆盖通用下载、archive v2 exact Get/restore 与受控 Delete 门禁，但不能替代 pre-open 的 manifest 全量 exact verify。

### 12. 容量准入与 source cleanup

容量准入逐字继承 ADR 0024 的动作语义，不创建含义不同的第二张表：

- `capacity restricted` 时，离线迁移 source read/verify 允许，target copy/new bytes 拒绝；
- restore write 默认拒绝；只有 FileStorage 停服恢复模式、容量 preflight 通过并具备显式 operator override 时，才可作为受控例外允许；
- runtime critical/unready 始终优先，覆盖 `capacity restricted` 下全部动作；在身份、mount、只读状态或 confinement 恢复可信前，read、rename、delete、cleanup 与 restore write 均不得继续。

source cleanup 必须延迟执行、独立授权、可取消，并按 manifest 精确限定 source identity/key/version。通用与归档分轨授权、计数和输出失败清单；一轨成功不授权另一轨清理。legal hold 阻断时保留 source 且不解除 hold；retention 或 object lock 阻断时保留 source 且不缩短 retention。所有阻断都输出稳定原因，不得跳过报告或计为成功。

## 失败矩阵

| 场景 | 必须结果 | 禁止结果 |
| --- | --- | --- |
| producer、Get/Delete consumer 或 mutation 入口冻结不完整 | 停止迁移，修复 fence 后重新生成 freeze/manifest 证据 | 仅凭 FileStorage 进程退出或“未观察到写入”继续 |
| source/target identity、config 或 activation freeze 事实漂移 | 批次失败关闭 | 从变化中的 source、target 或 record 继续 |
| legacy evidence 缺少完整 recorded key 或 exact source readback | 阻断并报告 | 从 bucket、prefix 或相似 key 猜填 catalog |
| metadata/evidence missing、orphan、size/SHA conflict | 显式报告并阻断 copy/cutover | 静默跳过、修猜或覆盖 |
| `capacity restricted` 下 source read/verify | 允许只读与证据生成 | 因 source 可读而放行 target 写入 |
| `capacity restricted` 下 target copy/new bytes | 拒绝并停止 copy | 绕过容量 preflight 建立 target 字节 |
| restore write | 默认拒绝；仅停服恢复模式、容量 preflight 通过和显式 operator override 齐备时受控允许 | 自建第二套容量准入语义 |
| runtime critical/unready | 覆盖所有动作并阻断 | 继续 read、rename、delete、cleanup 或 restore write |
| target 已存在同 key/version 候选 | exact size/SHA readback 后判 same/conflict | 仅按名称、metadata 或 API success skip/覆盖 |
| copy 中断 | 使用同 run/manifest/checkpoint 幂等续跑 | 重导变化 manifest 冒充原 run |
| remap 缺项、歧义、多重映射、链冲突、环或 exact verify 失败 | resolver 与 archive cutover 失败关闭 | 猜测 target version 或选择任意映射 |
| 任一 v1 archive consumer 可绕过 remap | activation 阻断 | 先激活后补 consumer |
| freeze 后出现未知 target 写或越过 manifest 的写入 | 批次失败关闭并隔离 target | 把未知对象纳入本 run |
| activation CAS stale | 事务不变并追加失败 audit | last-write-wins |
| 文档或实现声称对象 I/O 与 PostgreSQL 写原子 | 拒绝并改为有序动作、独立证据 | 用数据库事务掩盖对象 I/O 窗口 |
| pre-open rollback 条件不足 | 保持 fence 与 `target-pre-open`，停止决策 | 仅因 source 未清理就回退 |
| operator 已提交 `committed-for-open` | 同 run pure rollback 永久关闭 | 重开同 run 或 CAS 回 `source-active` |
| post-open 起服、业务开放或样本失败 | 立即停服并创建新的 reverse migration run | 修改原 run 配置/activation 回 source |
| legal hold、retention 或 object lock 阻断 source delete | 报告稳定原因并保留 source | 自动解除、缩短或计为 cleanup success |

## 已考虑的替代方案

1. **在线迁移、dual-write 或 read fallback。** 拒绝。它们会引入并行写入、增量追赶、路由与多 placement，无法保持单一 active provider 和确定性 manifest。
2. **只修改配置完成切换。** 拒绝。配置单边切换没有数据库 activation 授权、CAS、audit 或与 evidence/remap 闭合的证明。
3. **用 ETag、对象 metadata 或复制 API 成功替代 exact verify。** 拒绝。它们不能证明目标实际字节的 canonical SHA-256、size 与 exact version。
4. **原地改写 archive evidence、Scheduling batch 或历史 audit。** 拒绝。历史 recorded evidence 必须不可变，跨 store 的物理变化只能通过追加 evidence/remap 和新 audit 表达。
5. **cutover 后自动清源。** 拒绝。它会在 post-open 故障前删除 reverse migration 所需 source，也会绕过 legal hold、retention 与独立授权。
6. **将 `VersionedArchive` 并入通用 provider。** 拒绝。该方案会弱化独立 MinIO-only 的 versioning、object lock、retention 与 legal hold 合规边界。

## 后果

1. offline-only 迁移需要明确停机窗口和持续 fence，带来业务停机成本。
2. source/target 全量字节读取与 canonical SHA-256、size、exact version 证明增加迁移时间、I/O 和证据存储成本。
3. FileStorage 必须新增持久 evidence/remap/activation seam、additive v2 contract 与统一 resolver，带来 schema、并发 CAS 和 rollout 成本。
4. pre-open 窗口在业务从未开放且 fence 完整时提供可确定 pure rollback，避免把“source 尚未清理”误当作安全证明。
5. post-open 后必须以新 run 执行 reverse migration，运维成本高于原 run 改配置回退，但能保留开放后 mutation 的完整证据边界。
6. source cleanup 延迟且可能因 legal hold/retention 长期阻断，带来额外容量成本；换取可审计、可取消和按 manifest 精确限定的删除行为。

## 实施状态声明

本 ADR 仅接受目标架构，不证明实现已经存在：

| Issue | 未来实施所有权 |
| --- | --- |
| #1649 | FileStorage `filestorage` schema 的 immutable evidence/remap catalog、global activation CAS/activation audit、EF migration 与 PostgreSQL 并发证明 |
| #1650 | FileStorage additive v2 Put/Get/Delete contract、统一 resolver/runtime、genesis bootstrap 调用、storage identity 与 startup readiness |
| #1651 | Scheduling 新 batch 的 nullable evidence locator，以及 restore/delete append-only audit schema；legacy locator 保持 null |
| #1652 | Scheduling v2 producer、exact Get/restore 与 exact Delete consumer，legacy 完整 recorded key 和 resolved audit |
| #1653 | legacy exact-source import、manifest/copy/verify/cutover/rollback/cleanup-source 工具、配置接线与真实演练 |

实施顺序固定为 `#1649 → #1650 → #1651 → #1652 → #1653`。本 ADR 不证明代码、测试、真实运行、CI、PR 合并、生产迁移或 tracker 完成；#992 保持开放。
