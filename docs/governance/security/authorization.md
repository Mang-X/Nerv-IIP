# 授权、主体与 Scope 治理

本文规定 Nerv-IIP 的权限命名、调用主体、授权范围和强制校验边界。**当前具体权限码、默认角色和 Gateway operation 映射不在本页维护**；查询入口见 [`../../reference/security/authorization-catalog.md`](../../reference/security/authorization-catalog.md)，运行事实回到 IAM 与 Gateway producer。

相关长期边界仍由 IAM/平台 ADR 与当前 Architecture 决定，本页只维护现态规则。

## 权限命名

1. 权限码采用 `{domain}.{resource}.{action}`，全部小写；资源名默认使用复数。
2. 同一主版本内既有权限码不得改变语义。收窄、放宽或重解释授权边界属于契约变化，必须有明确迁移策略；涉及长期取舍时新开 ADR。
3. 对外 API、Gateway facade、SDK 与服务间调用不得各自发明未登记权限码。新增权限必须进入 IAM 的权限 producer，并由实际端点/服务授权检查与测试证明。
4. 权限码只表达动作类别；资源所有权、生命周期、不变式和业务状态仍由对应领域或应用层校验。

## 授权输入

一次授权判断至少区分四类输入：

1. `principalType`：调用主体类型；
2. permission code：稳定动作权限；
3. organization / environment scope：租户与环境边界；
4. resource / capability scope：资源或能力边界。

端点声明所需权限与上下文，但不得用“有权限码”替代资源归属、scope 和领域不变式检查。

## principalType

| principalType | 用途 | 约束 |
| --- | --- | --- |
| `user` | Console/Gateway、IAM 管理和业务操作 | 角色权限只在有效组织/环境成员关系及授权 scope 内生效 |
| `connector-host` | 注册、心跳、状态同步、受控任务结果 | 必须同时受 organization、environment、resource/capability 约束；不得获得 IAM 管理捷径 |
| `external-client` | Platform SDK、公开 API、受控第三方 | 授权必须可撤销，并按 organization/environment/resource/capability 约束 |
| `internal-service` | 平台内部服务到服务调用 | 只证明服务身份；不得替代最终 user、external-client 或 connector-host 的 IAM 授权事实 |

## Scope 维度

| scope | 含义 | 规则 |
| --- | --- | --- |
| `organization` | 组织边界 | 请求必须携带或可靠解析 organizationId，授权事实必须匹配 |
| `environment` | 环境边界 | 不允许跨环境隐式复用授权 |
| `resource` | 单个或一组资源 | 权限码之外还要校验资源归属、可见性和状态 |
| `capability` | 能力边界 | 必须与权限和资源范围共同成立；能力声明本身不是授权 |

默认约束：`connector-host` 必须同时具备 organization + environment，并通常具备 resource + capability；`external-client` 至少具备 organization，常规还需 environment/resource/capability；`internal-service` 按调用所需携带 scope，但不能成为绕过最终主体授权的后门。

## 内部服务身份

平台内部 Bearer 身份只解决服务到服务认证。调用链代表最终用户、外部客户端或 Connector Host 执行动作时，服务必须继续保留并验证相应主体与 scope；不能因为调用来自可信内部服务就跳过最终授权。

内部服务令牌、Development 默认值和非 Development 配置要求属于部署/代码事实，不在本页复制；以当前配置、AppHost/部署定义和认证实现为准。

## 权限命名空间

下列前缀冻结的是**命名边界**，不是实施路线图：

| 命名空间 | 语义 |
| --- | --- |
| `iam.*` | 身份、角色、会话和授权管理 |
| `connectors.*` | Connector Host 注册、心跳、状态上报等接入能力 |
| `apphub.*` | 应用与实例查询/管理 |
| `files.*` | 文件上传、读取、下载授权与归档 |
| `ops.*` | 运维任务、结果和审计 |
| `notifications.*` | 通知消息、订阅、模板与投递 |
| `knowledge.*` | 知识检索、知识源和索引 |
| `ai.*` | 模型配置、工具注册/执行、审批与提示词 |
| `observability.*` | 日志、诊断与保留策略 |
| `mobile.*` | 移动设备、部署与诊断治理；不得表达 WMS/MES/Quality/Maintenance 等业务动作 |
| `business.*` | 业务域读写、管理与执行权限 |

新增资源或 action 时优先复用现有语义（如 `read`、`manage`、`create`、`write`、`run`）；只有真实授权边界不同才新增权限码，不能为页面按钮或单个客户端制造同义权限。

参考数据词表是否拥有独立读权限码，按 [ADR 0029](../../adr/0029-reference-data-vocabulary-read-permission.md) 决策 2 的三条件裁定，命名与换绑形态见其决策 4/5；未触发不做预防性拆分。

## Gateway 与公开入口

1. 浏览器端以 Gateway/公开 facade 为授权执行边界时，Gateway 必须使用当前 principal 和明确 organization/environment/resource 上下文完成授权，不得只依赖前端隐藏按钮。
2. Gateway 代理内部服务时，内部服务身份与最终主体权限是两层不同检查；内部 token 不提升最终主体权限。
3. 某个 facade 是否还需邻接域权限，应由当前公开契约与目标服务所有权决定，不从历史矩阵或旧 Issue 推断。
4. 当前 operationId → permission 映射从 [`../../reference/security/authorization-catalog.md`](../../reference/security/authorization-catalog.md) 回到 Gateway 代码核实。

## 变更与验收

1. 新增或修改权限：先修改 IAM/目标端点的真实 producer 与测试，再同步 Reference；若规则本身变化，再修改本页。
2. Gateway facade 强制的每个 `business.*` 码必须同时存在于 IAM 的权限 producer，否则该码无法授予任何角色、端点对所有主体恒 403。这条包含关系由 `scripts/verify-permission-code-producer-consistency.ps1` 在 CI 强制，方向是单向的（IAM 可以多，Gateway 不可以多）——仅经 internal service policy 使用的服务端码按 [ADR 0029](../../adr/0029-reference-data-vocabulary-read-permission.md) 实施说明 1 合法只在 IAM 一侧。各业务服务自己的 `*PermissionCodes` 常量是消费方，不在该门禁的登记面内。
3. `connector-host` 和 `external-client` 不得只校验权限码而忽略 scope。
4. `internal-service` 不得作为最终授权绕过机制。
5. 不能用默认角色 seed 是否包含某权限，替代端点真实授权检查；也不能用端点声明替代 seed/role 是否具备该权限的独立事实。
6. 历史权限盘点、Issue 状态和“尚未落地”清单留在 Reports/GitHub/Linear，不进入现态 Governance。