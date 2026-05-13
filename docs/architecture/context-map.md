# 平台上下文地图

本文档定义 Nerv-IIP 平台的核心上下文、服务边界与交互方式，用于约束后端服务划分、前端聚合接口和 Agent 协议设计。

## 总体视角

平台由三层上下文构成：

1. 平台控制面：负责身份、组织、应用目录、实例事实、运维动作、审计、AI 治理。
2. 应用接入面：负责 Agent Host、Connector、本地资源探测、状态上报与动作执行。
3. 知识与 AI 面：负责知识引入与检索、MCP 工具、模型接入与执行治理。

## 服务上下文

### IAM

- 负责用户、角色、权限、组织、工厂、环境和访问策略。
- 提供统一身份与授权事实。
- 不承载应用注册、实例状态或运维动作。

### AppHub

- 负责应用目录、版本、节点、能力声明、实例事实、实例存活与 reported state。
- 是平台关于“当前管理了哪些应用和实例”的事实源。
- 不负责任务执行、审批与审计闭环。

### Ops

- 负责动作任务、执行尝试、审计记录、失败分类、审批挂点与结果回传。
- 所有会改变目标系统状态的动作都应进入 Ops 的任务闭环。
- 不成为实例最终状态的真相源。

### AI Integration

- 负责模型提供方配置、MCP Server、Skill 注册、工具授权、执行审批与人机确认编排。
- 不托管模型，不拥有知识索引。

### Knowledge

- 负责知识源接入、文档抽取、分块、嵌入、索引、检索、权限过滤和引用回显。
- 为 AI Integration、Gateway 和其他平台服务提供统一检索能力。

### PlatformGateway

- 负责前端聚合查询、上下文透传、页面级 BFF 接口。
- 不沉淀领域规则，不直接依赖任何服务的 Domain 或 Infrastructure。

### Agent Host

- 负责本地资源发现、能力探测、命令执行、日志采集、备份入口与本地状态上报。
- Connector 是 Agent Host 的适配层，而不是平台服务的一部分。

## 边界关系

### AppHub 与 Ops

1. AppHub 拥有应用、版本、节点、能力、实例、存活和状态事实。
2. Ops 拥有动作任务、执行结果、审计与审批挂点。
3. restart 一类动作由 Ops 创建任务并记录结果。
4. 实例最终状态是否变化，由 Agent 后续状态同步驱动 AppHub 更新，而不是由 Ops 直接改写。

### AI Integration 与 Knowledge

1. AI Integration 负责工具治理与模型接入。
2. Knowledge 负责文档与检索数据处理。
3. Knowledge 不是 MCP 执行编排器。
4. AI Integration 不是知识索引服务。

### Gateway 与领域服务

1. Gateway 只能聚合查询和透传上下文。
2. Gateway 不接管领域规则。
3. 前端不应直连多个服务，优先通过 Gateway 或明确服务 API。

## 交互方式

### 同步调用

- 登录、鉴权、页面查询聚合、必要的强一致入口可走同步 HTTP API。
- 同步 API 应优先依赖服务 Contracts，而不是服务内部实现。

### 异步传播

- 注册后产生的目录更新、状态变化、任务结果、不可达标记等优先通过集成事件传播。
- 禁止通过共享表实现跨服务协作。

## 首批纵切

### 应用注册到实例可见

1. Agent 发送注册契约。
2. AppHub 创建或更新 Application、ApplicationVersion、ManagedNode、ApplicationInstance。
3. Gateway 查询到新增实例事实。

### 心跳与状态同步

1. Agent 上报心跳。
2. AppHub 更新实例存活投影。
3. Agent 上报实例状态。
4. AppHub 更新 ApplicationInstance.reportedStatus 与状态历史。

### 低风险动作闭环

1. 控制台或 Gateway 发起重启动作。
2. Ops 创建 OperationTask 与 AuditRecord。
3. Agent 执行动作并回传结果。
4. Ops 记录 OperationCompleted 或 OperationFailed。
5. AppHub 通过后续状态同步确认最终实例状态。

## 不允许的耦合

1. 服务之间直接共享数据库表。
2. Gateway 直接依赖服务 Domain 或 Infrastructure。
3. Connector 直接依赖 AppHub、Ops、IAM 的内部实现。
4. AI Integration 直接维护知识索引。
5. Ops 成为实例状态真相源。
