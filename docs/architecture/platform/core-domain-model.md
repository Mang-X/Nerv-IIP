# 平台核心领域事实边界

本文只描述 Nerv-IIP **当前平台控制面的事实所有权与跨服务边界**。聚合、表、事件、字段和阶段完成状态的精确事实以当前代码、Contracts、迁移、OpenAPI 与测试为准；本文不维护第二份实现总账。

M2-K 迁移前的 `core-domain-model-v1.md` 同时包含大量“首批建议 / 首批不做 / 后续规划”，已按原 Git blob 冻结到 [`../../status/archive/core-domain-model-v1-pre-m2-k-2026-09-07.md`](../../status/archive/core-domain-model-v1-pre-m2-k-2026-09-07.md)。该快照只用于历史追溯，不是 Current Architecture。

## 当前事实所有权

| 上下文 | 当前拥有的事实 | 不拥有的事实 |
| --- | --- | --- |
| IAM | 用户、Membership、角色、权限、组织/环境、会话、外部客户端、Connector Host 凭据与授权授予 | 应用实例、运维任务、文件字节、通知投递、知识索引 |
| File Storage | 文件元数据、上传会话、下载授权、内部对象定位与保留策略 | 文件的业务语义、知识分块、运维任务 |
| AppHub | 应用目录、版本、节点、能力声明、实例、心跳与 reported state | 动作执行、审批、审计结果 |
| Ops | OperationTask、执行尝试、审批门禁、动作结果与运维审计 | ApplicationInstance 的最终状态 |
| Notification | 通知意图、用户消息、待办、偏好、订阅、投递尝试与消费侧死信 | 业务规则、IAM 授权事实、Observability 阈值规则 |
| Knowledge | 知识源、文档处理、分块、嵌入、索引、检索权限与引用 | MCP 工具治理、模型提供方配置 |
| AI Integration | 模型提供方、MCP Server、Skill、工具授权、执行审批与人工确认 | 知识索引、业务事实、运维事实 |

更完整的上下文关系以 [`../overview/context-map.md`](../overview/context-map.md) 为当前总览；IAM、File Storage、Notification 等服务的细化边界从 [`README.md`](README.md) 继续路由。

## 跨域不变量

1. 领域事实由其 owner 服务写入和裁决，跨服务不得通过共享数据库表形成隐式协作。
2. Gateway 负责聚合、认证上下文和传输映射，不接管下游领域事实。
3. Platform SDK 只封装公开契约与客户端协议，不成为新的事实中心。
4. Connector Host 通过版本化协议、公开 API、Platform SDK 与 IAM 授权协作，不引用平台服务内部实现。
5. 文件、身份、通知、知识、观测和运维事实保持独立；组合场景通过公开契约、IntegrationEvent、`fileId`、resource reference 和受治理上下文关联。
6. 精确当前实现若与本页摘要冲突，以代码、配置、公开契约、迁移和测试为准，并修正本页，不从历史快照反向恢复实现。
