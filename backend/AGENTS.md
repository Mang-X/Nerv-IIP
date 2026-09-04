# 后端目录路由

- **后端结构或实现：** 先读 `docs/governance/backend/clean-ddd-netcorepal.md`。
- **HTTP endpoint、Gateway 或契约：** 先读 `docs/governance/api/contracts-and-codegen.md` 与 `docs/governance/api/facade-coverage.md`；当前运行时结构见 `docs/architecture/integration/api-contracts.md`，并按其中的完成定义执行。
- **schema 或 migration：** 先读 `docs/governance/data/database-schema.md`、`docs/reference/data/database-schema-catalog.md` 与 `docs/runbooks/database-release.md`；物理结构以 EF migrations / EntityConfigurations 为准。
- **provider、AutoMigrate、连接解析或真实 PostgreSQL 测试生命周期：** 读 `docs/governance/data/persistence-startup.md`。
- **KnownException / Gateway 用户可见错误：** 读 `docs/governance/errors/user-visibility.md`。
- **时间、等待、网络、全局状态、隔离或 determinism baseline 测试：** 读 `docs/governance/testing/determinism.md`；具体 checker/verifier 操作见 `docs/runbooks/testing/determinism.md`。
- **验证：** 从受影响 solution/project、`docs/governance/testing/README.md` 和上述 producer 取得命令；只报告实际执行的 lane。
