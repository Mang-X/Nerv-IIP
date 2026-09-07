# Architecture 文档入口

本目录只承载 **Current Architecture**：系统现在由哪些组件组成、边界在哪里、事实归谁、依赖方向和交互方式是什么。项目进度、一次性调查、操作步骤、规范性规则和人工查询总账分别进入 Status / Reports、Runbook、Governance 与 Reference。

当前实现行为最终以代码、配置、公开契约、迁移、测试和命令帮助为准；Architecture 不维护第二份可漂移的 endpoint、版本、Schema、CI job 或完成状态清单。

## 主题目录

| 主题 | 当前入口 | 主要问题 |
| --- | --- | --- |
| Overview | [`overview/README.md`](overview/README.md) | 仓库级上下文、事实所有权、目录与依赖边界 |
| Platform | [`platform/README.md`](platform/README.md) | IAM、File Storage、Notification、Observability、SDK、缓存、AI/Knowledge、部署等控制面架构 |
| Integration | [`integration/README.md`](integration/README.md) | Gateway/OpenAPI 契约链、Connector Host 身份与协议 |
| Data | [`data/README.md`](data/README.md) | 数据架构任务路由；Schema 规则与目录分别回到 Governance / Reference |

业务领域与前端架构仍由 M2-L 后续治理；在完成迁移前，现有 `business-platform-domain-architecture.md`、`frontend-structure.md`、`frontend-navigation-map.md` 等 Current Architecture owner 继续保留原路径，不由 M2-K 越界改写。

## 按任务路由

| 任务 | 先读 |
| --- | --- |
| 服务边界、事实所有权、跨域调用 | [`overview/context-map.md`](overview/context-map.md) + [`platform/core-domain-model.md`](platform/core-domain-model.md) |
| 仓库目录、solution / package 放置与依赖边界 | [`overview/repo-layout.md`](overview/repo-layout.md) |
| IAM、认证、授权上下文 | [`platform/iam-authentication.md`](platform/iam-authentication.md) + [`../governance/security/authorization.md`](../governance/security/authorization.md) |
| File Storage | [`platform/file-storage.md`](platform/file-storage.md) |
| Notification | [`platform/notification.md`](platform/notification.md) |
| Observability / 日志 / 平台告警 | [`platform/observability.md`](platform/observability.md) |
| 缓存 | [`platform/caching.md`](platform/caching.md) |
| AI / Knowledge | [`platform/ai-boundaries.md`](platform/ai-boundaries.md) + [`platform/knowledge-source-lifecycle.md`](platform/knowledge-source-lifecycle.md) |
| Platform SDK | [`platform/sdk.md`](platform/sdk.md) |
| 当前部署拓扑 | [`platform/deployment.md`](platform/deployment.md)；执行操作见 [`../runbooks/deployment.md`](../runbooks/deployment.md) |
| API、Gateway、OpenAPI 与生成客户端 | [`integration/api-contracts.md`](integration/api-contracts.md) + [`../governance/api/contracts-and-codegen.md`](../governance/api/contracts-and-codegen.md) |
| Connector Host / 平台接入 | [`integration/connector-protocol-v1.md`](integration/connector-protocol-v1.md) + [`integration/connector-host-machine-auth.md`](integration/connector-host-machine-auth.md) |
| 数据库 Schema 规则 / 当前目录 | [`data/README.md`](data/README.md) |
| 业务域划分与平台/业务边界 | [`business-platform-domain-architecture.md`](business-platform-domain-architecture.md) |
| 前端工作区、应用和包职责 | [`frontend-structure.md`](frontend-structure.md) |
| 业务导航、页面 IA 与产品语义 | [`frontend-navigation-map.md`](frontend-navigation-map.md) + [`../product/README.md`](../product/README.md) |
| 测试有效性、确定性与 evidence | [`../governance/testing/README.md`](../governance/testing/README.md) + [`../runbooks/testing/README.md`](../runbooks/testing/README.md) + [`../reference/testing/README.md`](../reference/testing/README.md) |

## 不属于 Architecture 的内容

- 项目重点、阻塞、里程碑与当前实施摘要：[`../status/current.md`](../status/current.md)。
- 历史时点判断和迁移前快照：[`../status/archive/`](../status/archive/) 与 [`../reports/`](../reports/)。
- 可执行命令、启动、迁移、恢复和排障：[`../runbooks/README.md`](../runbooks/README.md)。
- 强制工程规则：[`../governance/README.md`](../governance/README.md)。
- Schema、权限、码表、事件消费、矩阵和技术资料人工目录：[`../reference/README.md`](../reference/README.md)。
- 用户、角色旅程、业务语义、IA 和 UX：[`../product/README.md`](../product/README.md)。
- 长期不可轻易反转的决策及理由：[`../adr/README.md`](../adr/README.md)。

## M2 迁移兼容

M2-B 至 M2-K 已逐步把混合生命周期内容迁出 `docs/architecture/` 平铺层。旧 owner 文件名只在仍有高价值历史/活跃引用时保留**短兼容导航**，不得继续写正文；删除条件由 M2-M/M4 统一收口。

M2-K 的 canonical owner 已迁入 `overview/`、`platform/`、`integration/` 与 `data/` 路由。以下旧路径只作为兼容 shim：

`ai-boundaries.md`、`caching-baseline.md`、`connector-host-machine-auth.md`、`connector-platform-protocol-v1.md`、`context-map.md`、`core-domain-model-v1.md`、`file-storage-baseline.md`、`iam-authentication-baseline.md`、`knowledge-source-lifecycle.md`、`notification-baseline.md`、`observability-baseline.md`、`platform-sdk-baseline.md`、`repo-layout.md`。

当前入口、AGENTS、ADR 和活跃 README 应直接链接 canonical path；历史 spec/report 可通过 shim 保持可追溯。