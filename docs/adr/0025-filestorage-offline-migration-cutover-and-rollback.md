# ADR 0025：FileStorage 停服离线迁移、切换、回退与归档证据 remap

- 状态：已接受
- 日期：2026-08-18
- 关联：[ADR 0023](0023-filestorage-tus-proxy-staging-final-complete-invariants.md)、[ADR 0024](0024-filestorage-storage-provider-and-local-production-semantics.md)、[Issue #1644](https://github.com/Mang-X/Nerv-IIP/issues/1644)、[Issue #1013](https://github.com/Mang-X/Nerv-IIP/issues/1013)

## 背景

ADR 0023 已接受通用文件 staging、final 与 complete 的提交不变量，ADR 0024 已接受单一 active provider、canonical `ObjectKey`、容量准入以及独立 `VersionedArchive` 边界。provider 搬迁、归档 recorded evidence 到新物理版本的可达性，以及 active-store 切换后的回退边界仍需要一条不会随实施推进变化的架构裁决。

本 ADR 决定离线搬迁的稳定拓扑、身份与完整性原则、通用文件和归档的证据分轨、数据库激活与 operator 决策边界，以及切换后清源的长期约束。

## 决策

### 停服拓扑与单 active provider

迁移必须 offline only。任一时刻只有单 active provider，业务写入与会改变物理字节或版本的入口在迁移和切换决策期间必须由持续有效的业务 fence 阻断。禁止 dual-write、在线 delta queue、read fallback、多 placement，以及服务运行中原地切换 provider。

### Canonical identity 与实际字节完整性

provider 搬迁只保持已经符合 canonical v1 `ObjectKey` 契约的 key，不得在搬迁中静默归一化、重写或兼容 legacy key。目标完整性必须由目标实际字节的 size 与服务端计算的 canonical SHA-256 证明；ETag、客户端声明、对象 metadata 或 copy success 均不能替代实际字节证据。

### 双 manifest 与 evidence/remap

通用文件与 `VersionedArchive` 必须使用独立的双 manifest，分别保留范围与证据边界。归档 recorded evidence 保持不可变，只能通过只追加 evidence/remap 把 source recorded evidence 连接到 target exact evidence；target 的物理版本 identity 不要求与 source identity 相同，历史证据不得因搬迁被原地改写。

### activation 与 operator 决策门

数据库 activation record 是归档 active-store 的授权事实。切换必须处于持续业务 fence 下，并经过 pre-open 与 open 的明确 operator 决策门；配置变化本身不能替代数据库授权和人工决策。对象 I/O 与 PostgreSQL 决策事务只能形成有序的证据衔接，不得宣称具有跨系统原子性。

### pre-open rollback 与 post-open reverse migration

pure rollback 仅限业务从未开放且 fence 完整的 pre-open window。一旦进入 post-open，恢复到保留 source 必须创建新的 reverse migration run，并重新完成完整的停服、搬迁、验证与切换证明；不得把原批次配置回拨描述为确定性回退。

### 容量与 source cleanup

所有迁移动作继承 ADR 0024 的容量准入和更高优先级健康阻断，不建立第二张容量分叉表。source cleanup 必须延迟执行、独立授权、可取消，受 legal hold 与 retention 约束，且不得由 cutover 隐式触发。

## 理由

1. 停服和单 active provider 把一致性问题收敛为有界的离线证明，避免并行业务 mutation 使 manifest 在生成后立即失真。
2. 以目标实际字节的 size 与 canonical SHA-256 为完整性依据，可以防止传输回执或 metadata 造成伪完整。
3. 不可变 recorded evidence 与只追加 remap 既保留历史审计含义，也允许旧证据稳定到达目标物理版本。
4. pre-open/open operator 决策门把 pure rollback 限定在业务尚未使用目标存储的窗口，避免开放后 mutation 被配置回拨丢失。
5. 继承 ADR 0024 的容量权威可以防止迁移另造一套准入语义，并保持健康阻断优先级一致。
6. 延迟 source cleanup 保留 reverse migration 所需的恢复边界；独立授权和合规约束避免 cutover 被误当作删除授权。

## 实施说明

- 目标技术规格与 operator 流程由 [FileStorage 离线迁移 runbook](../architecture/file-storage-offline-migration-runbook.md) 承载。
- 规划权威由 [Issue #1644](https://github.com/Mang-X/Nerv-IIP/issues/1644) 与 [Issue #1013](https://github.com/Mang-X/Nerv-IIP/issues/1013) 的 Issue tree 承载。
- 执行资格与当前交付状态以 GitHub Issue tree 和 [实施状态清单](../architecture/implementation-readiness.md) 为准；二者是状态权威，本 ADR 不承载状态。

## 已考虑的替代方案

1. **在线 dual-write 与 delta queue。** 拒绝。该方案引入并行写入、增量追赶和运行时路由，使单 active provider 与确定性 manifest 不再成立。
2. **保留 source physical locator 而不建立 remap。** 拒绝。目标存储的物理版本 identity 可以变化，没有 remap 就无法同时保持历史 evidence 不变与目标 exact evidence 可达。
3. **post-open 后直接修改配置回拨。** 拒绝。业务开放后目标已经可能产生新 mutation，配置回拨无法证明这些事实被保留。
4. **cutover 自动触发 source cleanup。** 拒绝。自动清源会过早破坏 reverse migration 边界，并绕过独立授权、legal hold 与 retention。
5. **在本 ADR 复制 ADR 0024 的容量矩阵。** 拒绝。复制会形成两个可能漂移的容量权威，迁移动作应直接继承既有裁决。

## 后果

1. 系统获得可审计的单 active provider 边界，以及从 immutable recorded evidence 到目标物理版本的稳定可达性。
2. pre-open 与 open 的 operator 决策门使 pure rollback 和 reverse migration 的语义边界明确，避免用配置切换冒充数据恢复。
3. 代价是迁移必须安排停服窗口，并维护通用/归档双 manifest 与 evidence/remap。
4. 实际字节验证、保留 source 和延迟 cleanup 会增加 I/O、证据存储与容量成本。
5. operator 必须承担显式的切换、开放与清源决策，不能依赖自动回退或自动删除。

## 范围之外

- 在线迁移、多 active provider、多 placement、同步复制与运行时读写路由；
- legacy/non-canonical key 的归一化、修复映射与兼容策略；
- 具体 schema、类型、字段、API、CLI、配置键和工具名称；
- Scheduling 等业务域的归档 batch、retention、restore、delete 与审计生命周期；
- 具体 rollout、阶段编号、操作步骤、重试规则、失败处置、接口结果语义与证据包格式。

## 复评触发条件

出现以下任一架构变化时，必须由新 ADR 取代或部分取代本记录：

1. 业务要求在线迁移、多 active provider 或多 placement；
2. 归档证据模型无法继续用 immutable evidence/remap 保持历史证据和目标物理版本之间的可达性；
3. 对象存储与数据库之间出现可证明、可依赖的跨系统原子能力；
4. ADR 0024 的容量治理权威被新 ADR 取代；
5. 合规规则要求改变 source cleanup 的延迟、独立授权、可取消、legal hold 或 retention 原则。
