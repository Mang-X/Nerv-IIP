# Architecture 文档入口

本页是 M0 迁移期的当前架构路由。`docs/architecture/` 现阶段仍混合多种生命周期；文件位于本目录不等于它就是当前 Architecture。M2 会按类型分批迁移，当前任务先按下表选择必要文档。

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

## 迁移期类型提示

以下内容目前仍在本目录，但不要把它们全部当作“当前架构”读取：

- `*-module-product-design.md` 属 Product，按所改业务域加载。
- `*-runbook.md`、`*-troubleshooting.md` 与操作型 deployment 文档属 Runbook。
- `*-catalog.md`、`*-matrix.*`、`*-inventory.md` 属 Reference 或生成伴随物。
- `*-investigation.md`、`*-spike.md`、`*-remediation.md` 属 Report，完成后应冻结。
- `first-vertical-slice.md`、`second-vertical-slice-ops.md`、`third-vertical-slice-console.md`、`fourth-vertical-slice-real-infra.md` 与 `project-status-dashboard.html` 是阶段或时点记录，不构成当前执行入口。

物理迁移与混合大文件拆分由 [GitHub #2290](https://github.com/Mang-X/Nerv-IIP/issues/2290) 跟踪；M0 不批量移动文件，也不建立永久分类 manifest。

## implementation-readiness

`implementation-readiness.md` 已停止接收新的功能完成日志、Issue/PR 级实施说明、事故过程和 focused gate 明细。它在 M1 完成前只用于发布、里程碑和跨域能力盘点；普通局部实现、修复、测试、重构和 UI 调整不读取它。

当前任务进度与验收证据留在 GitHub/Linear；当前命令与行为仍以代码、配置、脚本、帮助输出和测试为准。
