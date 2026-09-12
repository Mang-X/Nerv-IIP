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
| Business | [`business/README.md`](business/README.md) | 业务域、MasterData、现场 scope、Scheduling、设备/MES/ERP/WMS/Planning 当前边界 |
| Frontend | [`frontend/README.md`](frontend/README.md) | 前端 workspace、app/package、导航壳层与依赖方向 |
| Mobile | [`mobile/README.md`](mobile/README.md) | PDA / Capacitor 运行时架构 |

## 按任务路由

| 任务 | 先读 |
| --- | --- |
| 服务边界、事实所有权、跨域调用 | [`overview/context-map.md`](overview/context-map.md) + [`platform/core-domain-model.md`](platform/core-domain-model.md) |
| 仓库目录、solution / package 放置与依赖边界 | [`overview/repo-layout.md`](overview/repo-layout.md) |
| IAM、认证、授权上下文 | [`platform/iam-authentication.md`](platform/iam-authentication.md) + [`../governance/security/authorization.md`](../governance/security/authorization.md) |
| API、Gateway、OpenAPI 与生成客户端 | [`integration/api-contracts.md`](integration/api-contracts.md) + [`../governance/api/contracts-and-codegen.md`](../governance/api/contracts-and-codegen.md) |
| Connector Host / 平台接入 | [`integration/connector-protocol-v1.md`](integration/connector-protocol-v1.md) + [`integration/connector-host-machine-auth.md`](integration/connector-host-machine-auth.md) |
| 业务域划分与平台/业务边界 | [`business/domain-architecture.md`](business/domain-architecture.md) |
| MasterData / 现场作业 / Scheduling / MES / ERP / WMS / Planning | [`business/README.md`](business/README.md) 后只选直接相关专题 |
| 前端工作区、应用和包职责 | [`frontend/workspace-structure.md`](frontend/workspace-structure.md) |
| 前端导航壳层与应用边界 | [`frontend/navigation.md`](frontend/navigation.md)；产品 IA 与当前事实分别回到 [`../product/navigation.md`](../product/navigation.md) / [`../reference/frontend/navigation-map.md`](../reference/frontend/navigation-map.md) |
| PDA / Capacitor 运行时 | [`mobile/capacitor.md`](mobile/capacitor.md) |
| 业务导航、页面 IA 与产品语义 | [`../product/README.md`](../product/README.md) |
| 数据库 Schema 规则 / 当前目录 | [`data/README.md`](data/README.md) |
| 测试有效性、确定性与 evidence | [`../governance/testing/README.md`](../governance/testing/README.md) + [`../runbooks/testing/README.md`](../runbooks/testing/README.md) + [`../reference/testing/README.md`](../reference/testing/README.md) |

Platform 其它专题（File Storage、Notification、Observability、缓存、AI/Knowledge、SDK、部署）从 [`platform/README.md`](platform/README.md) 继续路由。

## 不属于 Architecture 的内容

- 项目重点、阻塞、里程碑与当前实施摘要：[`../status/current.md`](../status/current.md)。
- 历史时点判断和迁移前快照：[`../status/archive/`](../status/archive/) 与 [`../reports/`](../reports/)。
- 可执行命令、启动、迁移、恢复和排障：[`../runbooks/README.md`](../runbooks/README.md)。
- 强制工程规则：[`../governance/README.md`](../governance/README.md)。
- Schema、权限、码表、事件消费、矩阵和技术资料人工目录：[`../reference/README.md`](../reference/README.md)。
- 用户、角色旅程、业务语义、IA 和 UX：[`../product/README.md`](../product/README.md)。
- 长期不可轻易反转的决策及理由：[`../adr/README.md`](../adr/README.md)。

## M4 后的目录契约

M2 已把原先混在 `docs/architecture/` 平铺层的 Product、Governance、Reference、Runbook、Status 与 Report 内容迁到各自权威住所；M4 完成迁移期兼容入口退出。此后遵循下面的稳定规则：

1. **Current Architecture 正文只从本页进入主题目录。** 根目录只保留本 `README.md` 与主题目录，不再保留旧文件名 shim，也不得新增平铺正文。
2. **新链接只能引用 canonical owner。** Product / Architecture / Governance / Reference / Runbook / Status / Report 各自维护自己的当前职责，不通过旧 Architecture 路径转发。
3. 当前状态唯一入口是 [`../status/current.md`](../status/current.md)；历史时点判断从 [`../status/archive/README.md`](../status/archive/README.md)、[`../reports/README.md`](../reports/README.md) 与 Git 历史追溯。
4. 冻结 ADR、Report、Status archive 与 `docs/superpowers/**` 不为消除历史路径字面批量改写；其中的旧路径属于对应提交时点的坐标，不重新升级成当前入口。
5. **测试和脚本不得逐字依赖人工文档来证明运行行为。** 当前机器事实由代码、workflow、manifest、脚本帮助与现有行为测试生产；文档只解释稳定边界。
6. 以后确需移动当前文档时，应在同一迁移批次更新活跃消费者并运行现有 Docs/链接门禁。只有确有跨版本消费者时才允许短期兼容入口，并须在对应 Issue/PR 写明消费者、删除条件与最迟退出阶段；不得建立永久墓碑。
