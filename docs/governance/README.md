# Governance 文档入口

本目录只承载**当前必须遵守的工程规则**：适用范围、约束、例外原则和规则的权威 producer。Governance 不保存项目进度、CI run、事故过程、逐轮审计、阶段形成史或时点计数；这些内容留在 GitHub/Linear、冻结 Reports 或 Git 历史。

实现行为与规则发生冲突时，先确认规则是否仍有效；代码、配置、公开契约、测试和命令帮助仍是实现事实来源，Governance 不复制第二套运行时真相。

## 按主题路由

| 主题 | 当前规则 |
| --- | --- |
| 决策记录 / ADR 生命周期 | [`decisions/records.md`](decisions/records.md) |
| 人工文档与协作语言 | [`docs/language.md`](docs/language.md) |
| 后端 CleanDDD / NetCorePal | [`backend/clean-ddd-netcorepal.md`](backend/clean-ddd-netcorepal.md) |
| 前端设计系统 | [`frontend/design-system.md`](frontend/design-system.md) |
| 授权、主体与 scope | [`security/authorization.md`](security/authorization.md) |
| KnownException 用户可见性 | [`errors/user-visibility.md`](errors/user-visibility.md) |
| 数据库 Schema | [`data/database-schema.md`](data/database-schema.md) |
| 持久化启动与真实 PostgreSQL 测试生命周期 | [`data/persistence-startup.md`](data/persistence-startup.md) |
| ReferenceData / CodeSet | [`data/reference-data.md`](data/reference-data.md) |
| 集成事件消费分类 | [`integration/event-consumption.md`](integration/event-consumption.md) |
| 脚本与自动化 | [`script-automation.md`](script-automation.md) |
| 测试有效性、证据与真实依赖 | M2-H 迁移完成前仍从 `docs/architecture/test-validity-governance.md`、`test-evidence-governance.md`、`real-dependency-test-lanes.md` 路由 |

## 使用纪律

1. Governance 回答“当前必须遵守什么”，不回答“现在做到哪一步”“哪次修复发现了什么”或“某次 CI 是否通过”。
2. 规则里出现精确版本、权限码、Schema、路由、provider、seed 或运行时行为时，必须能回到明确 producer；易漂移事实优先使用链接而不是手抄副本。
3. 规则变化若属于长期取舍，先按 [`decisions/records.md`](decisions/records.md) 判断是否需要 ADR；不能用直接改 Governance 绕过长期决策记录。
4. 调查、事故、整改批次和历史计数进入 `docs/reports/` 或 Git 历史；完成后的 Report 冻结，不反向成为现态规则。
5. 不因为迁移 Governance 就新增永久 registry、自然语言 scanner、mutation fixture 或独立 CI step。既有机器契约只做必要路径重定向，不改变其规则语义。
6. `docs/architecture/*` 的 M2 兼容页只导航；不能把 Governance 正文再长回旧路径。