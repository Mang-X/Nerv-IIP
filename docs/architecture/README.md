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
| 业务导航、页面 IA | [frontend-navigation-map.md](frontend-navigation-map.md) 与对应模块产品文档 |
| API、OpenAPI 与生成客户端 | [api-contract-and-codegen.md](api-contract-and-codegen.md) |
| Connector Host / 平台协议 | [connector-platform-protocol-v1.md](connector-platform-protocol-v1.md)、[connector-host-machine-auth.md](connector-host-machine-auth.md) |
| 数据库命名与 Schema | [database-schema-conventions.md](database-schema-conventions.md)、[database-schema-catalog.md](database-schema-catalog.md) |
| 本地开发与排障 | [local-dev-troubleshooting.md](local-dev-troubleshooting.md) |
| 部署与发布 | [deployment-baseline.md](deployment-baseline.md)、[database-release-runbook.md](database-release-runbook.md) |
| 脚本治理 | [script-automation-governance.md](script-automation-governance.md) |
| 测试有效性、证据与真实依赖 | [test-validity-governance.md](test-validity-governance.md)、[test-evidence-governance.md](test-evidence-governance.md)、[real-dependency-test-lanes.md](real-dependency-test-lanes.md) |
| 文档语言与决策分层 | [document-language-governance.md](document-language-governance.md)、[decision-record-governance.md](decision-record-governance.md) |
| 可观测性 | [observability-baseline.md](observability-baseline.md) |

## 不属于 Architecture 的内容

- 当前全仓级状态位于 [`../status/current.md`](../status/current.md)。
- 历史阶段与状态快照位于 [`../status/archive/`](../status/archive/)。
- 调查、实验、审计和修复记录位于 [`../reports/README.md`](../reports/README.md)。
- 当前任务进度、负责人和验收证据位于 GitHub/Linear。

M2-B 已把纵切历史与明确报告的完整正文迁出本目录。原文件名暂时只保留短兼容入口，不能再作为当前架构或报告正文读取；兼容入口的删除条件由 M2-M 汇总后交给 M4。

## 迁移期类型提示

以下内容目前仍可能位于本目录，但不要全部当作当前 Architecture 读取：

- `*-module-product-design.md` 属 Product，按所改业务域加载。
- `*-runbook.md`、`*-troubleshooting.md` 与操作型 deployment 文档属 Runbook。
- `*-catalog.md`、`*-matrix.*`、`*-inventory.md` 属 Reference、Report 或生成伴随物，必须按正文生命周期判断。
- `*-investigation.md`、`*-spike.md`、`*-remediation.md` 的完整正文已由 M2-B 开始迁往 Reports；留在本目录的同名短页只可能是兼容入口。

物理迁移与混合大文件拆分由 [GitHub #2290](https://github.com/Mang-X/Nerv-IIP/issues/2290) 跟踪；本阶段不建立永久分类 manifest。
