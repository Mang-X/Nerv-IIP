# 第四阶段真实基础设施纵切

本文档记录第四阶段从“控制台纵切已跑通”推进到“真实基础设施门禁已通过”的统一验收口径。前三阶段分别验证接入查询、低风险动作闭环和控制台 API/codegen；第四阶段不扩大业务范围，重点把 AppHub、Ops、Gateway、Connector Host 和 Console 链路放到可验证的真实基础设施底座上。

## 目标

1. 将 AppHub 和 Ops 从内存态纵切推进到 netcorepal/CleanDDD 形态。
2. 以 PostgreSQL 作为首个真实持久化 profile，验证服务事实跨 DbContext 生命周期保存。
3. 接入 Redis、RabbitMQ 和 CAP 基础包，冻结后续缓存、消息和 outbox 接线边界。
4. 建立平台级 Aspire AppHost，作为 AppHub、Ops、Gateway、Connector Host 与基础设施资源的统一拓扑入口。
5. 保留前三阶段验证入口，并提供一个能拉起真实依赖、复跑控制台链路的第四阶段总门禁。

## 已落地范围

1. AppHub 已拆出 Domain aggregate、Application command/query、Infrastructure `ApplicationDbContext`、entity configuration 和 repository。
2. Ops 已拆出 Domain aggregate、Application command/query、Infrastructure `ApplicationDbContext`、entity configuration 和 repository。
3. AppHub/Ops Web endpoint 已通过 MediatR 调用 command/query，不再直接把 endpoint 绑定到具体 store。
4. AppHub/Ops PostgreSQL profile 已通过集成测试证明核心事实可持久化。
5. AppHub/Ops 已暴露 `/code-analysis`，用于查看 netcorepal 识别的命令、查询、聚合、事件和处理器流向。
6. `scripts/verify-second-slice-ops.ps1` 和 `scripts/verify-third-slice-console.ps1` 支持 `-UsePostgres`。
7. `scripts/verify-fourth-slice-real-infra.ps1` 会拉起 PostgreSQL、Redis、RabbitMQ，重建验证库并复跑第三阶段控制台纵切。
8. 平台级 AppHost 已落到 `infra/aspire/Nerv.IIP.AppHost`，当前覆盖 AppHub、Ops、PlatformGateway、Connector Host、PostgreSQL、Redis 和 RabbitMQ。

## 验证命令

第四阶段总门禁：

```powershell
pwsh scripts/verify-fourth-slice-real-infra.ps1
```

通过时最终输出：

```text
Fourth vertical slice real infrastructure verified.
```

该脚本覆盖 AppHub/Ops PostgreSQL profile tests、backend solution tests、connector-hosts solution tests、Gateway OpenAPI 导出、frontend api-client 生成、console typecheck/test/build，以及 PostgreSQL 模式下的 Gateway/AppHub/Ops/Connector Host 联调。

前端质量门禁仍需单独保持：

```powershell
pnpm -C frontend check
pnpm -C frontend fmt
pnpm -C frontend lint
pnpm -C frontend typecheck
pnpm -C frontend test
pnpm -C frontend build
```

## 当前限制

1. 第四阶段仍允许 AppHub/Ops 在本地验证路径中使用 `EnsureCreated()`；生产级迁移、初始化、seed 和回滚策略由 ADR 0009 承接。
2. CAP/RabbitMQ 当前是基础包、连接和资源拓扑已接线；业务集成事件 outbox、消费者幂等和发布订阅验收尚未进入本阶段完成定义。
3. IAM 仍是内存态认证授权骨架，控制台登录、权限 guard、Connector Host 正式授权链路尚未完成。
4. FileStorage 仍是边界骨架，真实上传下载、MinIO provider、下载授权和清理任务尚未完成。
5. AppHost 尚未覆盖 Iam、FileStorage、Notification、Knowledge、AI Integration、MinIO、Qdrant、OpenTelemetry Collector 和 frontend console。
6. Docker Compose 仍是本地依赖兜底入口；完整 Compose 产物、安装包和 Windows/Linux 整合安装脚本尚未落地。

## 后续承接

第五阶段不应直接跳到生产 MVP、高风险运维或复杂 AI 能力。推荐优先选择一个可以形成真实用户价值且能补齐平台底座的纵切，例如：

1. IAM 登录与 Gateway/Console 权限 guard。
2. FileStorage 上传下载闭环与 MinIO provider。
3. 数据库迁移发布基线、初始化脚本和部署硬化。
4. Ops 审批、复杂重试、outbox 和通知联动。

这些方向可以并行设计，但实施计划应保持单纵切可验收，避免一次性打开过多服务边界。
