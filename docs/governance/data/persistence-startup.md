# 持久化启动与真实 PostgreSQL 测试治理

本文规定服务启动时 provider / connection / AutoMigrate 的解析边界，以及测试数据库的生命周期约束。它不维护某日有多少服务已迁移、哪些 helper 尚待清理或某批 Issue 的推进状态。

数据库 Schema 与 migration 规则见 [`database-schema.md`](database-schema.md)；发布操作见 [`../../runbooks/database-release.md`](../../runbooks/database-release.md)。

## 启动治理入口

拥有数据库的服务应通过仓库当前共享 persistence-startup 入口解析运行配置，而不是在每个 `Program.cs` 重复手写 provider、connection alias、environment 和 migration 判断。当前实现以共享 `PersistenceStartupGovernance` / DI 扩展及其测试为准。

共享入口至少要统一回答：

1. 当前 `Persistence:Provider`；
2. 当前环境是否 Development；
3. `Persistence:AutoMigrate` 是否被允许；
4. 服务数据库连接字符串按哪些受支持别名解析；
5. provider 不受支持、连接缺失或非 Development 自动迁移时如何 fail closed；
6. 日志中哪些配置可以安全记录，哪些 secret 必须隐藏。

## Provider 解析

1. provider 名称按仓库当前稳定值解析，未知值必须启动失败，不静默回退到 PostgreSQL/InMemory/SQLite。
2. provider 专有注册只在 Infrastructure/DI 层发生；Domain/Application/Endpoint 不感知 provider。
3. 某 provider 是否受支持由当前代码、migration 与真实 provider tests 共同证明，不由配置字符串“能写进去”证明。
4. 非 Development 环境不得因为配置缺失自动采用 Development fallback。

## Connection string 别名

1. 每个服务只能声明有限、明确的连接字符串 key/alias 集合；解析顺序由共享治理统一，不能各服务随意追加模糊 fallback。
2. 找不到数据库连接时必须给出不含 secret 的明确启动错误，不能自动连接本机默认库或其他服务数据库。
3. 日志允许记录服务名、provider、使用的 key/alias、目标环境和数据库身份的非敏感摘要；不得输出密码、完整连接串或含 credential 的 URI。
4. 测试/临时数据库必须显式使用测试生命周期提供的连接，不能复用开发人员持久化 dev 数据库。

## AutoMigrate

1. Web/Worker 默认不自动 migration。
2. `Persistence:AutoMigrate=true` 只在 [`../../runbooks/database-release.md`](../../runbooks/database-release.md) 允许的环境边界内生效；非 Development/未授权环境必须 fail closed。
3. 自动迁移调用仍走正式 EF migrations，不使用 `EnsureCreated` 或手写建表 SQL 旁路 migration history。
4. seed 与 migration 是两件事：允许 AutoMigrate 不自动意味着可以执行任何 demo/business seed。
5. 启动迁移失败必须让服务启动失败并保留可诊断日志；不能 catch 后继续以部分 schema 运行。

## FileStorage 等窄例外

服务若因 provider 抽象、对象存储初始化或特殊 host 生命周期存在窄例外，必须：

- 仍遵守未知 provider fail closed、secret 不落日志和非 Development migration 边界；
- 把例外收敛在该服务 Infrastructure/host 层，不扩成全仓第二套 startup helper；
- 有对应行为测试证明例外不会改变其它服务的基线。

具体是否仍存在某服务例外，以当前代码为准；本页不把历史例外名单永久化。

## 真实 PostgreSQL 测试生命周期

真实数据库测试通过仓库共享 testing package / fixture 管理，核心要求：

1. 测试数据库/容器由测试生命周期拥有，创建、migration、seed、使用和清理边界明确；
2. 测试不得连接开发者持久化 dev 数据库、共享客户数据库或无归属的固定数据库；
3. database name / schema / resource label 必须足够唯一，支持并发 CI 与本地并行会话；
4. cleanup 只删除当前测试拥有的数据库/容器/volume，不做按名称前缀广泛清理；
5. 测试失败、取消或异常退出也要执行 best-effort 精确回收，同时保留足够诊断证据；
6. migration 必须是真实服务 migrations，不用 `EnsureCreated` 假装 migration 可用；
7. provider 行为、索引、事务、并发和 SQL 语义需要 PostgreSQL 证明时，不用 InMemory/SQLite 替代。

## 测试 opt-in 与 lane

1. 真实 PostgreSQL 测试进入受治理的 real-dependency lane，普通 fast unit test 不因为本页而全部拉起数据库。
2. 本地执行需要显式 opt-in/入口；CI 是否选中由 impact plan 和测试 Governance 决定。
3. lane 被 policy skip 时必须如实报告 skipped，不把聚合 `Backend Tests` 绿等价成 provider lane 已执行。
4. 数据库测试的 timeout、并行度、artifact 与 cleanup 规则从当前 testing package / CI producer 读取，本页不复制易漂移数值。

## 禁止的启动/测试模式

- 每个服务复制自己的 provider 字符串 switch、连接别名 fallback 和 AutoMigrate 环境判断；
- 未识别 provider 自动退回默认 provider；
- 非 Development 自动 migration；
- `EnsureCreated` 代替 migration 验证；
- 连接失败后换另一个数据库继续启动；
- 测试直接 DROP/清理不属于本次测试的数据库或容器；
- 为通过测试在脚本里临时重写业务 schema；
- 把某次全仓 grep 得到的 helper 数量做成永久治理断言。

## 变更与验收

1. startup 规则变化先改共享代码与行为测试，再同步本页；不能只改文档。
2. 新服务接入时复用共享入口并验证 fail-closed、AutoMigrate 边界和连接解析，不复制旧服务模板。
3. 新 provider 先有真实 migration/provider lane 再宣称支持。
4. 历史服务迁移批次、Issue 编号、helper 清理计数和时点盘点通过 Reports/Git/Tracker 追溯，不进入现态 Governance。