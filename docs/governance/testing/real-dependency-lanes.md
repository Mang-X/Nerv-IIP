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

## 类级排除的覆盖闭合

上一节说的是「lane 接管整个项目」；本节说的是**另一张面**：fast shard 在 shard manifest 里用**类级** selector（`excludedTestClasses`）把一个类整体过滤掉。这两张面的失效方向不同，上一节的结论不覆盖本节。

类级排除的射程是整个类：runner 发出 `FullyQualifiedName!~<类>.`，类里每一条用例都被移出快分片。因此一条类级排除只有在**该类直接声明的每一条用例**都能解析到一条 environment-gated real-dependency 证据身份时才算有据。只凭「类里有某一条用例被登记」放行，等于把同类其余裸 `[Fact]`/`[Theory]` 一起送进「谁都不跑」：它们不在任何 fast shard 的过滤结果里，不在任何 heavy lane 的 filter 里，**也不产生 skipped 记录**——TRX 里根本没有这一行，所以 skip/quarantine/zero-execution 那套基于执行记录的检查在构造上也看不见它们。

由此派生两条：

- 混合类（既有 env-gated 真实依赖用例、又有普通用例）不得整类排除，必须逐条用方法级 selector 交给 heavy lane。
- 一条类级 selector 必须能在后端测试源码里解析到至少一个声明了用例的类。解析不到时它的分母为零，「每条都有据」会空洞成立；这种 selector 按红处理，而不是按通过处理。

用例识别按 `FactAttribute`/`TheoryAttribute` 的**继承闭包**判定，不按属性名字列举：自定义派生属性还会继续增加，按名单判定的失效方向是假绿。识别也不得依赖 `async` 这类关键字窗口——表达式体写法（`public Task X() => ...`）不带 `async`。嵌套类里的用例不归属外层 selector：VSTest 把它拼作 `Outer+Inner.Method`，类级过滤器同样匹配不到它。

覆盖边界：本节由 `scripts/verify-backend-test-shards.ps1` 的 `inventory-source` 阶段静态承担，鉴别力读数在 `scripts/tests/backend-test-shards.Tests.ps1` 的类级排除变异格里；它只管 fast shard 的类级排除这一张面，不替代上一节由 lane owner 承担的项目级闭合。


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
