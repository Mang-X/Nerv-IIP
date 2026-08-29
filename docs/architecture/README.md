# Architecture 文档入口

本页路由系统的**当前架构**。`docs/architecture/` 仍处于 M2 分类迁移期；文件位于本目录不等于它就是当前 Architecture。普通任务只读取与目标范围直接相关的当前架构页。

## 基础边界

- [平台上下文地图](context-map.md)：服务职责、事实所有权与交互方式。
- [仓库布局说明](repo-layout.md)：顶层目录、放置规则与引用边界。
- [业务平台领域架构](business-platform-domain-architecture.md)：业务域划分与平台/业务边界。
- [核心领域模型](core-domain-model-v1.md)：首批平台领域事实与关系。
- [Platform SDK 基线](platform-sdk-baseline.md)：公开客户端能力与服务内部实现的边界。

## 按任务路由

| 任务 | 当前文档 |
| --- | --- |
| 前端工作区、应用和包职责 | [frontend-structure.md](frontend-structure.md) |
| 业务导航、页面 IA 与产品语义 | [frontend-navigation-map.md](frontend-navigation-map.md) + [`../product/README.md`](../product/README.md) |
| API、OpenAPI 与生成客户端 | [api-contract-and-codegen.md](api-contract-and-codegen.md) |
| Connector Host / 平台协议 | [connector-platform-protocol-v1.md](connector-platform-protocol-v1.md)、[connector-host-machine-auth.md](connector-host-machine-auth.md) |
| 数据库命名与 Schema 规则 | [`../governance/data/database-schema.md`](../governance/data/database-schema.md)；当前 Schema 人工目录见 [`../reference/data/database-schema-catalog.md`](../reference/data/database-schema-catalog.md) |
| 当前工程 Governance | [`../governance/README.md`](../governance/README.md) |
| 当前 Schema/码表/事件消费矩阵/权限导航/术语/技术资料 | [`../reference/README.md`](../reference/README.md) |
| 本地开发与排障 | [`../runbooks/local-development.md`](../runbooks/local-development.md) |
| 部署拓扑 | [deployment-baseline.md](deployment-baseline.md) |
| 数据库发布、迁移与恢复 | [`../runbooks/database-release.md`](../runbooks/database-release.md) |
| 测试有效性、证据与真实依赖 | M2-H 迁移完成前仍由 [test-validity-governance.md](test-validity-governance.md)、[test-evidence-governance.md](test-evidence-governance.md)、[real-dependency-test-lanes.md](real-dependency-test-lanes.md) 路由 |
| 脚本治理 | 对应 M2 owner 收口前仍由 [script-automation-governance.md](script-automation-governance.md) 路由 |
| 文档语言与决策分层 | [`../governance/docs/language.md`](../governance/docs/language.md)、[`../governance/decisions/records.md`](../governance/decisions/records.md) |
| 可观测性 | [observability-baseline.md](observability-baseline.md) |

## 不属于 Architecture 的内容

- 当前全仓级状态位于 [`../status/current.md`](../status/current.md)。
- 当前 Governance 位于 [`../governance/README.md`](../governance/README.md)。
- 当前 Product 位于 [`../product/README.md`](../product/README.md)。
- 当前 Runbook 位于 [`../runbooks/README.md`](../runbooks/README.md)。
- 当前 Reference 位于 [`../reference/README.md`](../reference/README.md)。
- 历史阶段与状态快照位于 [`../status/archive/`](../status/archive/)。
- 调查、实验、审计和修复记录位于 [`../reports/README.md`](../reports/README.md)。
- 当前任务进度、负责人和验收证据位于 GitHub/Linear。

M2-B 已迁出纵切历史与报告；M2-C 已迁出 Product；M2-D 已迁出 Runbook；M2-E 已迁出 Reference；M2-F 已迁出本批授权、后端、Schema、决策记录、语言、设计系统、KnownException 和持久化启动 Governance。上述旧文件名只保留短兼容入口，不能再作为正文读取；兼容入口删除条件由 M2-M 汇总后交给 M4。

## 迁移期类型提示

- M2-D 的 `database-release-runbook.md`、`file-storage-offline-migration-runbook.md`、`local-dev-troubleshooting.md`、`mobile-pda-deployment.md` 仅为兼容入口。
- M2-E 的 `database-schema-catalog.md`、`factory-world-bible.md`、`frontline-contract-page-scope-action-matrix.md`、`glossary.md`、`integration-event-consumption-matrix.md`、`master-data-dictionary-rules.md`、`technology-stack-references.md` 仅为兼容入口。
- M2-F 的 `authorization-matrix.md`、`backend-cleanddd-netcorepal-guidelines.md`、`database-schema-conventions.md`、`decision-record-governance.md`、`document-language-governance.md`、`frontend-design-system-planning.md`、`known-exception-user-visibility.md`、`persistence-startup-governance.md` 仅为兼容入口。
- 其它 `*-catalog.md`、`*-matrix.*`、`*-inventory.md` 仍可能属于 Reference、Report 或机器伴随物，按正文生命周期判断。
- `*-investigation.md`、`*-spike.md`、`*-remediation.md` 的完整正文已由 M2-B 迁往 Reports；同名短页只可能是兼容入口。
- `*-module-product-design.md` 与 `frontline-role-journey-acceptance-matrix.md` 的完整 Product 正文已由 M2-C 迁往 `docs/product/`。

物理迁移与混合大文件拆分由 [GitHub #2290](https://github.com/Mang-X/Nerv-IIP/issues/2290) 跟踪；本阶段不建立永久分类 manifest。
