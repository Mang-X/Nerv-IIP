# 真实依赖测试 Lane 治理

本文定义真实依赖测试的稳定 lane 边界、唯一归属、触发证明和失败关闭规则。当前成员、场景、状态、expected identities 和 CI 接线以 machine-readable manifests、`.github/workflows/ci.yml` 与相应验证脚本为准；本文不维护某次 PR/run 的时点结果。

## 稳定 Lane

| 稳定 ID | 证明边界 |
| --- | --- |
| `postgres` | 单服务/单测试宿主上的 PostgreSQL 查询翻译、migration、schema/约束、事务、锁、并发与持久化行为。 |
| `redis-cap` | 真实 Redis/CAP transport 的发布、消费、重投、乱序、inbox/outbox、consumer group、恢复与精确清理；允许 PostgreSQL 作为持久化依赖，但结论终点仍是 transport/单消费者。 |
| `full-chain` | 多服务真实进程和场景声明的真实依赖，经公开入口完成跨服务业务终局；只证明被选中的精确场景。 |

每个测试按“它证明什么”唯一归属一个 `requiredLane`，不按类名、环境变量或最重依赖机械归类。一个测试承担两个不同证明结论时应拆分。

## Lane 接管整个项目时的覆盖闭合

heavy lane 在 shard manifest 里接管一个测试项目后，该项目即被排除出全部 fast shard。此时 lane owner 必须对**该项目里发现到的每一条用例**负责，不得只跑名单里的若干条：白名单式选取遇到「同项目里出现了不同类别的测试」时必然遗漏，而遗漏不会红——被排除出快分片 + 不在名单 = 谁都不跑。

因此接管整个项目的 lane owner 采用「默认全跑」口径：以测试框架自身的用例发现结果为权威全集，减去 manifest 冻结的成员身份，余量必须在同一 lane 内执行完毕并计入 lane 的执行数。**不设排除注册表**——空注册表拿不出鉴别力证据，而一个逃生口会被用来重新制造暗测试。确有用例无法在该 lane 执行时，改 lane owner 脚本并走脚本治理，不是往名单里加一行。

用例发现结果的解析不得依赖测试框架输出里的本地化表头（CI 与开发机的 CLI UI 语言不同），也不得把参数化用例当作互不相同的方法身份。

覆盖边界：本节的闭合由各 lane owner 自行实现，当前只有 `full-chain` 一个 lane 接管整个项目并落地了该闭合；不存在跨 lane 的通用静态护栏（登记式簿记拿不出鉴别力证据，刻意不造）。新增接管项目的 heavy lane 时，本节要求由该 lane 的 owner 脚本与其契约测试自行承担。

## 选择与触发

- PR 使用影响计划选择受影响的 lane/policy/scenario；影响计划失败、缺失或无法可靠判定时保守选择。
- `main`、nightly、`workflow_dispatch` 的当前选择集合由 manifest/workflow producer 定义；Governance 只要求不能降低执行数、身份、证据、cleanup 或失败语义。
- 未选中 lane 由稳定汇总报告 `skipped by policy`；被选中后 job 缺失、取消、artifact 缺失、执行数为零、全部 skip、身份不闭合或 cleanup 残留必须是红灯。
- stable aggregate 可作为 branch protection 的汇总接口，但不能反向成为一条未由它执行的测试 lane 的 formal evidence owner。

## 环境与资源所有权

- PostgreSQL 基础连接只用于创建/管理测试自有临时数据库或 schema；测试使用 migration/受治理初始化，不用 `EnsureCreated()` 冒充生产结构。
- Redis/CAP 的 stream、consumer group、lock 和业务 key 使用 invocation/session 唯一命名空间；禁止 `FLUSHALL` 或清理未知 namespace。
- FullChain 子进程、数据库、Redis namespace、端口和 artifact 必须能追溯到当前 run/attempt/lane/scenario，并在失败路径 best-effort 精确清理。
- CI 选中真实依赖后若环境变量/readiness 缺失，必须在发现/执行前失败；测试代码不得通过读取 `GITHUB_ACTIONS` 决定是否偷偷跳过。

本地执行与残留回收见 [`../../runbooks/testing/real-dependencies.md`](../../runbooks/testing/real-dependencies.md)。

## 证据语义

每次绿色结果至少能回答：选中了什么、为什么、测试 SHA、预期/实际身份与执行数、通过/失败/skip、依赖环境以及 cleanup 结果。formal retained evidence 还受 [`evidence.md`](evidence.md) 的 provenance、隐私和 actual-vs-skipped 规则约束。

当前 producer 与清单入口见 [`../../reference/testing/producers.md`](../../reference/testing/producers.md)。历史 lane 接线、PR/run 取证和治理演进见 [`../../reports/audits/real-dependency-test-lane-evolution-2026-08.md`](../../reports/audits/real-dependency-test-lane-evolution-2026-08.md)。
