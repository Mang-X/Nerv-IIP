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

## M2 后的目录契约

M2 已把原先混在 `docs/architecture/` 平铺层的 Product、Governance、Reference、Runbook、Status 与 Report 内容迁到各自权威住所。此后遵循下面的稳定规则：

1. **Current Architecture 正文只从本页进入主题目录。** 新增或继续维护的现态架构不得回到根目录平铺旧文件名。
2. **根目录旧 `.md` 文件只允许作为显式兼容 shim。** shim 只说明 canonical owner 与必要迁移背景，不继续累积架构、产品、状态、规则、命令或事实总账。
3. **新链接不得以 shim 作为权威来源。** 遇到旧路径时先跟随其导航到 Product / Architecture / Governance / Reference / Runbook / Status / Report 的 canonical path，再引用最终 owner。
4. 当前状态唯一入口是 [`../status/current.md`](../status/current.md)；已退役入口的历史从 [`../status/archive/README.md`](../status/archive/README.md) 与 Git 追溯。
5. **测试逐字依赖不是兼容页的长期保留理由。** 应从消费方解除对历史标题、计数、运行编号与登记文案的耦合，保留真实行为回归；不得把旧文案合同平移到当前 Governance。解除文案耦合后，仍须逐项核清代码旁说明、CI 路由及活跃文档入链，不能据此直接宣称兼容页可删除。
6. 冻结 ADR、Report、Status archive 与 `docs/superpowers/**` 不为消除旧 URL 批量改写。物理删除兼容 shim 时应先证明活跃消费者和机器依赖已清零，再运行 Docs/链接与受影响门禁。
