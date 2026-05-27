# P2 Production Hardening Design

## 背景

#77 已把业务 P0 full-chain acceptance 收口到可继续推进业务系统的状态；#170 到 #175 已落地事件契约守门、CAP/outbox 单服务真实基础设施验收、client_credentials、生产安全配置、部署产物和 opt-in 性能基线骨架。当前缺口不再是“业务功能是否能开发”，而是“生产环境是否能可靠交付、审计、恢复和验收”。

P2 不作为业务建模和 Business Console 后续页面的前置阻塞。它作为生产准入线并行推进，按可靠性、交付验收、运维安全、企业身份四条线拆分成小 PR，避免一个超大 PR 同时改消息、IAM、Ops、部署和性能。

## 总体目标

1. 事件可靠性硬化：把现有 in-memory DLQ 基线推进到 Notification、AppHub 和 MES 的 PostgreSQL 持久 DLQ/inbox，并补跨服务真实 CAP 多进程验收入口。
2. 部署演练与性能阈值化：在现有生产部署产物和 opt-in 性能基线基础上增加阈值、失败门禁和部署演练脚本。
3. Ops 高风险动作审批与通知联动：把 restart 等高风险动作从直接执行升级为申请、审批、执行、通知和审计闭环。
4. IAM 企业身份深化：在现有 IAM 用户、角色、权限、session 和 client_credentials 基线上补 OIDC/SSO 入口、MFA 钩子和资源范围 ABAC 约束。

## 分阶段设计

### 1. 事件可靠性硬化

第一阶段只处理事件可靠性，不改业务领域语义。Maintenance 已有 `integration_event_dead_letters` PostgreSQL 表和 `MaintenanceIntegrationEventDeadLetterStore`，它是当前实现样板。Notification、AppHub 和 MES 继续保留 consumer guard，但 PostgreSQL profile 下必须使用持久 DLQ store；非 PostgreSQL 或测试替换场景仍可用 in-memory store。持久 DLQ 的 EF Core 实现应放在 `Nerv.IIP.Messaging.CAP.EntityFrameworkCore` 扩展包中，而不是放入基础 `Nerv.IIP.Messaging.CAP` 包；这样可复用 EF store 和映射，同时避免只需要 guard/in-memory DLQ 的消费者被迫携带 EF Core 传递依赖。

本阶段还要引入业务 inbox 的明确边界：CAP `received` 表仍负责 broker 级接收去重，业务 inbox 负责“业务副作用已处理”的服务内幂等事实。Notification 已有 `processed_integration_events`，本阶段先把它纳入 P2 inbox 事实；AppHub 和 MES 后续按同一形态补齐。

跨服务真实 CAP 多进程验收作为 opt-in hardening profile，不进入默认本地 verify。脚本要求真实 PostgreSQL；RabbitMQ profile 只有显式选择时才要求 broker。该 gate 放在 persistent DLQ 之后的独立 PR，至少覆盖 Ops 发布 `OperationTaskFailed`，Notification 与 AppHub 独立服务进程消费，并在各自数据库看到业务副作用、CAP outbox/inbox 和 DLQ/inbox 表结构。

### 2. 部署演练与性能阈值化

现有 `scripts/verify-production-deployment-artifacts.ps1` 只证明产物存在和 Compose config 可解析；现有 `scripts/verify-business-performance-baseline.ps1` 输出性能指标但没有阈值。P2 需要增加 release rehearsal 脚本，执行依赖启动、迁移/健康检查、核心 smoke test、关闭清理；性能脚本增加阈值参数和 machine-readable 输出，超过阈值即失败。

部署演练不默认启动完整平台，必须显式选择 profile。没有 Docker 或 PostgreSQL 时报告为环境不可用，不把环境缺失伪装成代码失败。

2026-05-26 收口：`scripts/verify-production-release-rehearsal.ps1` 已提供 `dependencies` 与 `platform-smoke` 两个显式 profile，使用 disposable Compose project 和默认清理；`scripts/verify-business-performance-baseline.ps1` 已写出 metrics JSONL/summary JSON，并支持全局和 inventory/mes/erp 分场景 elapsed-time 阈值。

### 3. Ops 高风险动作审批与通知联动

Ops 当前已经有低风险 restart 闭环和审计基础。P2 只把高风险动作纳入审批，不扩大业务审批模型范围。默认方案是 Ops 自有轻量 approval gate：高风险操作生成 `OperationApprovalRequest`，审批通过后才进入原有 task claim/execute 流程；审批结果和执行结果都发布 Notification intent，审计记录带关联 id。

BusinessApproval 可作为业务审批能力，不直接承载平台运维审批，避免把平台控制面和业务审批上下文混在一起。

### 4. IAM 企业身份深化

IAM 已有 persistent users/roles/permissions/sessions、JWT、refresh token、internal authorization check 和 client_credentials。P2 不一次性实现完整 OAuth/OIDC 认证服务器。推荐顺序是：先做外部 OIDC provider login callback 和 claims mapping，再做 SSO session binding，再做 MFA challenge hook，最后做 ABAC resource scope。

ABAC 的第一版只覆盖组织、环境、资源类型和资源 id 范围，不引入复杂策略语言。Gateway 继续是终端用户 bearer 的 enforcement 边界，业务服务只接受 internal service token。

## 验收口径

- 默认本地验证仍保持轻量，不强制 Docker/PostgreSQL/RabbitMQ。
- P2 hardening 验收通过显式脚本入口运行，并清楚声明依赖和副作用。
- 每条线必须更新 `docs/architecture/implementation-readiness.md`、数据库 schema catalog 或部署/脚本文档中对应事实。
- 每条线单独 PR、单独验证、单独可回滚。

## 非目标

- 不在 P2 第一阶段实现完整 OAuth/OIDC 授权码服务器。
- 不把 RabbitMQ 变成默认本地开发依赖。
- 不把 BusinessApproval 与 Ops 运维审批合并成同一个业务模型。
- 不为所有业务服务一次性补 inbox/DLQ 管理 UI。
- 不把性能指标写成固定不可调的“生产 SLA”；阈值先作为可配置 release gate。
