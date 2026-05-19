# 第二阶段低风险动作闭环说明

本文档记录第二阶段已经落地的低风险运维动作纵切。它不是完整运维平台，而是验证 Nerv-IIP 能否从控制面创建动作、让 Connector Host 安全领取并执行、再把结果与审计事实回写到平台。

## 目标

第二阶段只证明一件事：平台可以围绕单个已纳管实例创建 `lifecycle.restart` 低风险动作，并完成任务、尝试、执行结果和审计记录的闭环。

最短链路：

```text
Console or PlatformGateway
  -> POST /api/console/v1/instances/{instanceKey}/operations/restart
  -> Ops operation task
  -> Connector Host polls pending operation task
  -> Docker Connector executes lifecycle.restart
  -> Connector Host posts operation result
  -> Ops records attempt and audit fact
  -> GET /api/console/v1/operation-tasks/{operationTaskId}
```

AppHub 仍然是实例状态事实来源。Ops 只记录动作事实和执行结果，不直接修改 ApplicationInstance 的最终状态；实例状态变化仍依赖后续 state snapshot。

## 已落地范围

1. `Nerv.IIP.Contracts.Ops` 固化 Ops 公开 DTO，包括创建任务、pending task、任务详情和执行结果。
2. `Nerv.IIP.Sdk.Ops` 提供 Connector Host 使用的 pending 拉取与 result 回传客户端。
3. Ops.Web 提供任务创建、任务详情、pending 拉取和 result 回传接口。
4. PlatformGateway 提供 restart facade 和 operation task detail facade。
5. Connector Host 增加 operation loop，按组织、环境和 connectorHostId 拉取 pending task。
6. Docker Connector 支持 `lifecycle.restart` 执行抽象，并在测试中用 fake process runner 验证命令构造。
7. 本地验证脚本 `scripts/verify-second-slice-ops.ps1` 会启动 AppHub、Ops、PlatformGateway 和 Connector Host，走通一次 restart 闭环。

## 公开接口

### PlatformGateway.Web

1. `POST /api/console/v1/instances/{instanceKey}/operations/restart`
   - 根据 AppHub 实例详情生成低风险 restart 运维任务。
   - 不直接执行动作，也不绕过 Ops。

2. `GET /api/console/v1/operation-tasks/{operationTaskId}`
   - 返回任务、最近一次尝试、审计记录和失败分类。

### Ops.Web

1. `POST /api/ops/v1/operation-tasks`
   - 创建 OperationTask，并记录 request 审计事实。

2. `GET /api/ops/v1/operation-tasks/{operationTaskId}`
   - 查询任务详情、attempts 和 audit records。

3. `GET /api/ops/v1/operation-tasks/pending`
   - Connector Host 按 organizationId、environmentId、connectorHostId 拉取待执行任务。

4. `POST /api/ops/v1/operation-results`
   - Connector Host 回传执行结果，Ops 写入 OperationAttempt 和完成/失败审计事实。

## 认证与范围

第二阶段仍是本地纵切验证，尚未接入完整 IAM 授权链路。

1. Connector Host 调用 pending 与 result 接口时必须携带 `X-Connector-Host-Id`、`X-Connector-Secret`、`X-Organization-Id` 和 `X-Environment-Id`。
2. Ops 会校验 connectorHostId、secret、organizationId、environmentId 与请求范围一致，避免 Connector Host 跨范围领取或回传任务。
3. Gateway restart facade 暂时保留本地开发入口，后续接入控制台登录、权限和审批。

该 header-secret 机制不是最终生产认证方案。Connector Host 机器身份终态以 [Connector Host 机器身份认证终态](connector-host-machine-auth.md) 为准：Connector Host 通过 IAM 换发短期 bearer token，Ops pending/result 接口按 bearer token、permission code 和 capability scope 授权。

## 配置

本地开发默认端口：

1. AppHub：`http://localhost:5101`
2. Ops：`http://localhost:5105`
3. PlatformGateway：由验证脚本动态分配

关键配置：

1. PlatformGateway 使用 `Ops:BaseUrl` 调用 Ops。
2. Connector Host 使用 `Platform:OpsBaseUrl` 调用 Ops。
3. Connector Host 使用 `Platform:ConnectorHostId` 和 `Platform:ConnectorSecret` 作为本地 Connector Host 凭证。

## 可靠性边界

1. Connector Host 会在内存中缓存尚未成功回传的 OperationResult，并在下一轮轮询前优先重试。
2. 第二阶段不提供持久化 outbox；Connector Host 进程退出后，内存中的未发送结果可能丢失。
3. Ops 当前任务状态仍以内存态 store 验证行为，后续需要迁移到 PostgreSQL，并补齐唯一键、并发领取、租约超时和重试策略。

## 验证

第二阶段验收命令：

```powershell
pwsh scripts/verify-second-slice-ops.ps1
```

该脚本覆盖：

1. backend 与 connector-hosts 关键项目 build/test。
2. AppHub 注册、心跳和状态快照。
3. Gateway restart facade 创建 Ops task。
4. Connector Host pending 拉取、Docker restart 执行与 result 回传。
5. Gateway task detail 查询。
6. Gateway 和 Connector Host 不直接引用 Ops、AppHub 的 Domain 或 Infrastructure 项目。

## 非目标

1. 不提供高风险动作、批量动作、回滚动作或人工审批 UI。
2. 不提供生产级持久化、分布式锁、租约续期和持久化 outbox。
3. 不绕过 AppHub 的状态事实边界；动作结果不会直接改写实例状态。
4. 不把本地 Connector Host secret 机制视为最终 IAM 方案。
