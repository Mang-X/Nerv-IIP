# 真实依赖测试 Lane 架构

本页冻结 [NERV-688 / #1256](https://github.com/Mang-X/Nerv-IIP/issues/1256) 第一层的架构裁决。它只定义真实依赖测试的边界、触发层级、运行环境和证据语义；合并后才允许实施 PostgreSQL 试点、逐服务铺开及 Redis/CAP 接入。

票面的四层在本文中映射如下：`Fast Tests` 保持为不启动真实依赖的现有快速层，不属于下列三条 lane；`PostgreSQL Provider Tests` 对应 `postgres`；票面的 `Business Integration Acceptance` 对应稳定证据 ID `full-chain`，Actions 展示名细化为 `Business FullChain Acceptance / <scenario>`；`Nightly` 是横跨已登记 lane 的触发与覆盖维度，不是第四条 lane。

## Lane 与唯一归属

| 稳定 ID | Actions 展示名 | 归属判据与边界 |
| --- | --- | --- |
| `postgres` | `PostgreSQL Provider Tests` | 单服务或单测试宿主，证明 PostgreSQL 查询翻译、migration、约束、事务、并发及持久 inbox/outbox 等生产数据库语义。只有当测试不声称验证真实消息 transport 时，才可使用 CAP InMemory。当前 `scripts/backend-test-shards.json` 的 `real-postgres` 是过渡执行别名，第二层接线时必须收敛到既有证据 ID `postgres`，不得形成第四条 lane。 |
| `redis-cap` | `Redis/CAP Transport Tests` | 使用真实 Redis Streams，证明发布、消费、重复、乱序、重试、幂等及消费恢复；允许 PostgreSQL 作为持久化依赖，但断言终点是 transport 或单个消费者的持久结果，不冒充跨服务业务闭环。拆解④的 [#920](https://github.com/Mang-X/Nerv-IIP/issues/920) 前置已完成：生产修复由 PR #1308 合并，真 PostgreSQL 回归与调查结论由 PR #1530 收口；第四层不再受“调查未解”阻塞，但接入 MES 用例时必须以该实现和回归为基线。 |
| `full-chain` | `Business FullChain Acceptance / <scenario>` | 断言跨服务业务终局，使用多个真实服务进程及场景声明所需的 PostgreSQL、Redis/CAP 等依赖。NERV-767 负责接入现有 FullChain 用例，NERV-673 后续以 scenario matrix 管理扩展场景。 |

每个测试按“它证明什么”唯一归属一条 `requiredLane`，不按类名或设置了哪个环境变量归类；一个测试同时承担两类结论时必须拆分。`SeedScale` 与 `performance` 可以复用本页的容器生命周期，但仍分别由 NERV-677、NERV-183 管理，不并入 `postgres`。

## 触发层级与 branch protection

| 事件 | 运行策略 | 门禁关系 |
| --- | --- | --- |
| pull request | NERV-668 按变更影响选择精确 lane、政策规则或 FullChain scenario；影响计划失败或输出缺失时保守运行。 | lane job 不直接设为 required；稳定 required `CI Summary` 汇总选择政策与实际结果。被选择结果失败、取消、缺席或零执行均阻止合并，未选择结果必须精确为 `skipped` 并显示 `skipped by policy`。 |
| `main` push | 运行全部 `core` 且非规模型的 PostgreSQL 用例，以及全部 `active + core` Redis/CAP、FullChain 场景。 | 不回写既有 PR 结论；失败立即把 `main` 的真实依赖可信度标为降级。 |
| nightly | 运行全部已登记的真实依赖用例、全部 active FullChain 场景及重试、乱序、故障注入变体；NERV-677 的全规模 `SeedScale` 在同层但保持独立 lane，性能继续使用 NERV-183 的独立 workflow。 | 不进入 PR branch protection；负责穷举和高成本变体，失败是 release blocker。 |
| `workflow_dispatch` | 可选择单 lane、单 policy/scenario 或 `full`；复用自动运行的同一入口。 | 不得降低执行数、证据、清理或失败语义，手工成功也不替代下一次应运行的自动门禁。 |

这形成“影响感知 PR、核心 main、全量 nightly”：PR 只支付受影响部分，main 保住核心闭环，nightly 承担穷举覆盖。成员清单必须显式记录 `core`/`extended` 与 `active`/`deferred`/`blocked`，不得靠维护者记忆决定运行层级。

## Service container 与环境约定

调度形态沿用 `.github/workflows/nightly-business-performance.yml` 已验证的 `schedule` + `workflow_dispatch`、service container、受治理验证脚本和受限 artifact 样板；真实依赖 lane 在此基础上增加多 lane/scenario 选择与 MAN-661 证据语义。每个 job 自有 GitHub Actions service container；PostgreSQL 18 使用 `pg_isready`，Redis 8 使用 `redis-cli ping`，service health 通过后 runner 仍须执行一次协议级 readiness probe。容器由 Actions 按 job 回收，不跨 job 共享。

- `NERV_IIP_TEST_POSTGRES` 保持管理员/基础连接串语义。runner 使用现有 `PostgreSqlTestDatabase` 或等价受管生命周期，为每个测试或 scenario 创建带 run/attempt/lane 前缀的动态数据库，执行 migration/seed，并在 `finally` 中显式删除；禁止 `EnsureCreated()`。
- `NERV_IIP_TEST_REDIS` 保持 Redis endpoint 语义。`NERV_IIP_TEST_CAP_VERSION` 由 run、attempt、lane、scenario 与随机后缀派生；stream、consumer group 和业务测试 key 使用同一命名空间，禁止以 `FLUSHALL` 代替精确清理。
- FullChain runner 可以为子进程派生既有 `NERV_IIP_TEST_<SERVICE>_POSTGRES` 与 probe 变量，但外部入口仍是上述基础变量，不再增加 CI 专用连接串契约。
- 开发者本机可继续手工设置同名变量运行真实依赖测试；测试代码不得读取 `GITHUB_ACTIONS` 决定行为。CI 选中 lane 后若变量缺失或 readiness 失败，必须在测试发现前失败，不能退化为绿色 skip。

失败时先保留脱敏的 PostgreSQL、Redis stream、consumer group pending（`XPENDING`）、CAP inbox/outbox/DLQ 与业务状态诊断，再由 `always()` 路径清理动态数据库、Redis 命名空间和子进程，并把清理结果写入证据；不得保存连接串、凭据、header 或业务正文。

## 执行与跳过证据

`test-evidence-policy.json` 及后续 lane/scenario manifest 是测试归属和预期执行数的权威来源；env gate 只兼容本地启用方式。每次绿色结果必须回答：选择了什么、为什么选择、测试了哪个 SHA、各选中单元预期与实际执行多少、通过/失败/跳过多少、依赖版本是什么、清理是否完成。

选中的每条 policy rule 或 scenario 都必须生成机器可读结果并满足其 `expectedRuntimeTestCount`；job 未创建、被取消、缺少 artifact、执行数为零、全部 skip、只执行部分预期测试、非法 quarantine、测试失败或清理残留均为红灯。未选中的 lane 不制造 VSTest skip，由 summary 以 `skipped by policy` 和具体影响理由表示；合法 optional skip 仍须显式计数和说明。

测试步骤保留自然退出码；证据采集使用 `if: always()`。禁止 `continue-on-error`、`|| true`、吞掉退出码，或用单个已执行测试掩盖同一 lane 中其他被选单元的零执行。

## 非目标与后续边界

本页的架构冻结不设计各业务 scenario 的步骤；Nightly 故障工单自动化、去重和优先级升级策略须另行治理，不由本页裁决。第二层由 `scripts/postgres-test-lane.json`、受治理 runner 与 CI job 实现 PostgreSQL 模板及 Inventory 单服务试点；第三层才逐服务接入并收编 NERV-423，第四层接入 Redis/CAP，第五层由 NERV-767 接入 FullChain。NERV-673、NERV-677 消费本页裁决但保留各自业务矩阵和种子分层职责。
