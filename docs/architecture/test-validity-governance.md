# 测试有效性治理

## 目的与边界

本文规定测试断言的语义来源，以及不同 provider（提供程序）和 lane（执行通道）允许声明的证明范围。它回答“断言为什么正确”和“这次运行最多证明什么”。CI 是否实际执行、TRX 脱敏、skip 政策、zero-execution、证据留存和运行身份仍以 [测试证据治理](test-evidence-governance.md) 为准，本文不重复其执行证据契约。

一条测试只有同时满足以下条件才是有效证据：

1. 先有独立于当前实现输出的权威语义来源，再从该来源推导预期值；
2. 测试名称、trait、依赖和 lane 如实描述实际执行边界；
3. 测试在目标错误实现上会失败，并在符合权威来源的实现上通过；
4. 结论不超过所用 provider、拓扑、数据和时序实际覆盖的范围。

实现代码、当前数据库内容、一次运行输出或刚生成的 snapshot 可以是被测对象或诊断材料，不能仅凭“现在就是这样”升级为合同来源。

## 六类合同来源

每条合同测试必须能归入下表至少一类。组合分类允许，但每一项断言都应能追溯到具体来源；不得以宽泛的“Regression”替代缺失的业务依据。

| 分类 | 定义 | 允许的权威来源 | 合规示例 | 不合规反例 |
| --- | --- | --- | --- | --- |
| `DomainInvariant` | 领域对象在所有实现中都必须成立的业务不变量、状态机或计算边界。 | 已批准的 ADR/spec、领域规则文档、Issue 验收条件、法律或行业规则，以及经业务 owner 明确裁决并落盘的规则。 | ADR 规定已关闭会计期间拒绝新凭证；测试构造关闭期间并断言命令失败。 | 读取当前 handler 返回的错误码，把该值抄入测试，再称其为领域规则。 |
| `PublicContract` | 对外可观察且受兼容性治理的 HTTP、事件、SDK、CLI 或文件格式契约。 | 受治理的 OpenAPI、公共 Contracts 源码、ADR、版本化协议/标准和明确的兼容性政策。 | 从公开 OpenAPI 的 required 字段推导请求缺字段必须被拒绝。 | 根据当前内部 DTO 的偶然 JSON 形状更新公开 snapshot，而无公开契约变更依据。 |
| `ReferenceData` | 受控码表、目录、标准向量或跨生产者/消费者共享的确定性业务数据。 | 仓库权威目录文档、批准的主数据标准、外部正式标准，或经 owner 审核并独立落盘的规范向量。 | 以 `master-data-dictionary-rules.md` 的标准码值验证 seed 和 API。 | 先运行当前 seed，再把生成出来的全部码值作为“标准码表”固定。 |
| `ProviderBehavior` | 某个真实 provider、协议或运行时特有的翻译、约束、事务、并发、持久化和故障行为。 | provider 官方契约、仓库批准的 provider/profile 设计、migration/schema 约束，以及已确认的 provider 缺陷或兼容案例；目标 provider 的真实运行负责证明实现，不负责定义预期。 | 在 PostgreSQL 上并发提交冲突写入，验证唯一约束和事务结果。 | 用 EF Core InMemory 通过的 LINQ 测试宣称 PostgreSQL SQL 翻译、唯一索引或隔离级别正确。 |
| `Regression` | 已被确认的缺陷、事故或边界条件的最小可复现防线。 | Issue/事故记录中的错误行为与期望行为、修复前失败样本、生产兼容案例，或能在旧缺陷实现上复现的最小 fixture。 | 保留修复前导致重复过账的事件序列，并证明旧实现失败、新实现收敛一次。 | 只保存修复后的输出；测试从未在旧实现或等价错误变异上失败。 |
| `Governance` | 约束仓库结构、依赖方向、清单闭合、生成入口或证据流程的机器可检查规则。 | `AGENTS.md`、架构治理文档、ADR、机读 manifest/schema 及其明确的失败关闭规则。 | 变异 fixture 删除必填 lane 身份后，治理检查器必须非零退出。 | 测试只搜索注释中的关键词，命中即宣称实际行为受约束。 |

权威性看来源和批准边界，不看文件名。名为 `golden.json` 的文件不天然属于 `ReferenceData`；领域测试也不能仅凭位于 `Domain.Tests` 就自动获得 `DomainInvariant` 身份。

## Golden、snapshot 与 digest

Golden、snapshot 和 digest 都是权威语义的派生表示，不是语义来源本身。新增或更新时必须同时具备：

- 可定位的来源：文档章节、协议版本、外部标准、Issue 验收或已确认回归样本；
- 生成或规范化方法：排序、时区、编码、舍入、脱敏和哈希算法等影响字节的规则；
- 语义变化说明：哪些字段或案例为什么变化，兼容性是否改变；
- 独立预期：在观察当前实现输出之前，能够从来源推导关键值或性质。

禁止以下做法：

- 用当前实现批量生成输出后直接接受全部差异；
- 以“测试需要变绿”“snapshot 已更新”作为语义理由；
- 先对当前输出求 digest，再用同一实现重算 digest 证明自身正确；
- 在没有逐项解释的情况下，用大范围 snapshot 刷新掩盖新增、缺失、顺序或默认值变化。

允许工具生成机械文件，但 reviewer 必须从独立来源审核语义差异。若输出过大，应断言来源要求的关键性质，并把完整 snapshot 仅作为可审的派生载体；不能用不可读的体积替代合同说明。

正例：GS1 标准和仓库条码规则先给出输入与期望 AI 段，再由固定规范化器生成 golden；实现输出只与该 golden 比较。反例：运行当前条码生成器覆盖全部 golden，看到测试变绿后才把新输出解释为标准。

## Provider 与 lane 的证明范围

测试结论必须使用下表中的“可证明”口径，并显式保留“不能证明”的边界。增加更重的 provider 不会自动扩大到未实际执行的行为；例如 PostgreSQL 测试若没有并发交错，就不能声明已证明串行化。

| 执行形态 | 可证明 | 不能据此证明 |
| --- | --- | --- |
| pure/fake/EF Core InMemory | 纯函数、领域状态机、应用编排、确定性输入输出，以及 fake 明确模拟的分支。 | SQL 翻译、migration、schema/索引/外键/唯一约束、事务隔离、锁、真实持久化重启、provider 特有异常。 |
| PostgreSQL | 在实际执行路径覆盖到的 migration、schema、SQL 翻译、约束、事务、锁、并发和重启持久化行为。 | Redis/CAP 传输、跨进程消息恢复、完整服务拓扑、浏览器流程，或测试未制造的并发/故障分支。 |
| Redis/CAP | 在真实 Redis/CAP 与所声明持久化依赖上实际触发的发布、消费、重投、乱序、inbox/outbox、consumer group 和清理行为。 | 未参与的 broker/provider、完整 HTTP 用户链路、所有业务服务，或仅凭消息最终出现推导出的数据库隔离正确性。 |
| FullChain | 清单中精确场景经真实公开入口、实际服务拓扑和声明的真实依赖完成，并具有身份、readiness、结果与 cleanup 证据。 | 清单外场景、未触达的异常分支、平台全量正确性、性能容量，或另一 provider/profile 的等价性。 |

mock 浏览器只能证明前端对 mock 契约的交互；真实浏览器若后端仍为 stub，也不能称为 FullChain。反过来，协议级 FullChain 未启动浏览器时也不能声明视觉或可访问性通过。

### 命名与 trait

- 名称先写行为，再写必要的执行边界；`Postgres`、`RedisCap`、`FullChain` 等后缀只在测试确实连接该依赖、执行目标行为并由对应 lane 认证时使用。
- `Integration`、`Provider`、`EndToEnd` 过于宽泛，不能单独表达依赖或证明范围。需要 provider 语义时，名称或 trait 必须点明实际 provider。
- trait 是路由和盘点契约，不是装饰。声明的 trait、manifest 身份、skip 条件和责任 lane 必须一致；缺少环境时应按[测试证据治理](test-evidence-governance.md)登记并失败关闭，不得 `return` 静默空跑。
- lane 名称描述实际拓扑，不描述愿望。`postgres` lane 中使用 CAP InMemory 的用例不得改称 `RedisCap`；只验证 HTTP mock 的用例不得改称 `FullChain`。
- 同一测试组合多个真实依赖时，名称和证据应写明实际组合；不能用其中最强的一个标签替代其余依赖身份。

正例：`Concurrent_create_enforces_unique_source_key_on_postgres` 在 PostgreSQL lane 中制造两个事务并验证唯一结果。反例：`PostgresProfileTests.Create_succeeds` 实际替换为 EF Core InMemory，只因配置键写着 `PostgreSQL` 就宣称 provider 证明。

## Review checklist

新增、修改、迁移或删除测试时，author 与 reviewer 逐项核对：

- [ ] **来源：**每项关键预期归入六类之一，并能定位独立权威来源；当前实现输出没有反向成为合同。
- [ ] **red-green：**测试在修复前提交、最小错误实现或等价 mutation 上会失败，失败原因正是目标行为；修复后才通过。
- [ ] **旧实现反例：**测试保留能区分旧错误实现与新实现的输入/交错/fixture，而不是只验证新实现存在。
- [ ] **并发：**并发结论实际控制交错、事务和同步边沿，并证明冲突结果；并行启动或循环多次不等于竞态证据。
- [ ] **时间：**使用 `TimeProvider`/明确 UTC、时区和边界；等待计时器注册边沿后再推进假时钟；不依赖墙钟睡眠碰运气。
- [ ] **skip/trait：**trait、skip 来源、provider、manifest 和责任 lane 一致；零执行、静默 `return` 或错 lane 不算通过。
- [ ] **隔离：**组织/环境、数据库/schema、Redis namespace、端口、文件目录、进程和 cleanup 均有精确归属；测试不清理共享或未知资源。
- [ ] **证明范围：**标题、注释、PR 和验收结论没有超过实际 provider、拓扑、数据量及场景。
- [ ] **golden/snapshot：**来源、规范化方法和语义变化可审，没有批量接受当前输出。
- [ ] **负向路径：**缺依赖、坏输入、旧格式、冲突和 cleanup 失败按合同 fail closed；只测 happy path 不足以替代既有负向防线。

### 删除或弱化负向隔离测试

负向隔离测试包括跨租户/环境/数据库/schema/namespace/端口/进程所有权、越权访问、错误身份和 cleanup 边界。删除、合并或弱化这类测试前，必须提供**更强的行为证据**，同时满足：

1. 在同一或更真实的 provider/拓扑上执行目标边界，而不是换成源码搜索、mock 或正向 happy path；
2. 保留旧实现或等价错误 mutation 作为反例，证明替代测试会因隔离被破坏而失败；
3. 覆盖原测试保护的身份维度和失败结果，并新增至少一种更强条件，例如真实并发交错、真实约束、重启恢复或 cleanup 读回；
4. PR 逐项说明旧断言由哪条新行为证据承接。无法建立映射时保留原测试，或先把改动拆到后续 Issue。

正例：用真实 PostgreSQL 的两个 organization 事务和提交后读回，替代只检查 query filter 表达式的隔离测试；新测试在移除 organization 谓词的 mutation 上失败。反例：删除“不能删除其他 run 的 Redis key”测试，只保留“本次 key 已删除”的正向断言；后者无法发现广泛清理。

## PR 结论格式

涉及测试的 PR 至少说明：合同分类与来源、实际 provider/lane、red-green 或反例证据、受影响的隔离/时间/并发边界，以及未运行或不能证明的事项。执行数量、CI 状态和产物链接继续按[测试证据治理](test-evidence-governance.md)报告；“CI 绿色”不能替代本文件要求的语义来源与证明范围审核。
