# 第三阶段控制台纵切说明

本文档记录第三阶段已经落地的控制台纵切。它不是完整管理后台，而是验证 Nerv-IIP 能否从 Gateway 暴露稳定控制台契约，生成类型安全前端客户端，并在 Vue 控制台里完成实例查看、低风险 restart 动作和 OperationTask 状态查看。

## 目标

第三阶段只证明一件事：控制台前端可以通过 PlatformGateway 的 OpenAPI 契约消费第一、第二阶段已经跑通的平台能力，而不直接耦合 AppHub、Ops、Iam 或 FileStorage 的内部实现。

最短链路：

```text
PlatformGateway /swagger/v1/swagger.json
  -> scripts/export-gateway-openapi.ps1
  -> frontend/packages/api-client
  -> Hey API generated fetch SDK/types/Pinia Colada options
  -> frontend/apps/console
  -> instance list/detail
  -> restart operation
  -> operation task detail polling
```

## 已落地范围

1. PlatformGateway 已通过 FastEndpoints.Swagger 暴露 `/swagger/v1/swagger.json`。
2. 控制台 API 已冻结稳定 `operationId`：`listConsoleInstances`、`getConsoleInstanceDetail`、`restartConsoleInstance`、`getConsoleOperationTask`。
3. `scripts/export-gateway-openapi.ps1` 可导出 Gateway OpenAPI 快照到 `frontend/packages/api-client/openapi/platform-gateway.v1.json`。
4. `frontend/packages/api-client` 使用 Hey API 生成 TypeScript DTO、fetch SDK 和 Pinia Colada query/mutation options。
5. `frontend/apps/console` 使用 Vue Router 官方文件路由插件、Pinia、Pinia Colada 和生成客户端展示实例列表、实例详情、restart 动作入口和 OperationTask 状态页。
6. `frontend/packages/ui` 和 `frontend/packages/app-shell` 已提供第三阶段所需的最小 UI primitive 与应用壳层。
7. `scripts/verify-third-slice-console.ps1` 串起第二阶段验证、OpenAPI 导出、api-client 生成、前端 typecheck/test/build。

## 公开契约

控制台前端只直接消费 PlatformGateway 的 `/api/console/**` 接口。

### PlatformGateway.Web

1. `GET /api/console/v1/instances`
   - 查询当前组织和环境下的实例列表。
   - `operationId` 固定为 `listConsoleInstances`。

2. `GET /api/console/v1/instances/{instanceKey}`
   - 查询实例详情、能力清单、最近心跳与最近状态快照。
   - `operationId` 固定为 `getConsoleInstanceDetail`。

3. `POST /api/console/v1/instances/{instanceKey}/operations/restart`
   - 通过 Gateway facade 创建 Ops restart 任务。
   - `operationId` 固定为 `restartConsoleInstance`。

4. `GET /api/console/v1/operation-tasks/{operationTaskId}`
   - 查询 Ops OperationTask、attempts 和 audit records。
   - `operationId` 固定为 `getConsoleOperationTask`。

## 前端边界

1. 页面、组件和 composables 只从 `@nerv-iip/api-client` 稳定入口消费控制台契约，不直接引用 generated 深层路径。
2. `frontend/packages/api-client/src/generated` 只放生成代码，不手写业务逻辑。
3. `frontend/packages/api-client/src/transport` 只处理 base URL、认证头、错误归一化和请求级策略。
4. Pinia 只保存客户端状态；实例列表、实例详情、restart mutation 和任务详情轮询都走 Pinia Colada。
5. Vue 页面保持薄，实例表格、详情面板和任务时间线分别落到 `frontend/apps/console/src/components/console`。
6. `typed-router.d.ts` 由 Vue Router 官方文件路由插件生成并纳入 TypeScript 检查。

## 质量门禁

第三阶段总验收命令：

```powershell
pwsh scripts/verify-third-slice-console.ps1
```

该脚本覆盖：

1. backend 与 connector-hosts 既有纵切验证。
2. Gateway OpenAPI 导出。
3. frontend 依赖安装、api-client 生成、typecheck、test 和 build。

前端单独质量门禁：

```powershell
pnpm -C frontend check
pnpm -C frontend lint
pnpm -C frontend fmt
pnpm -C frontend typecheck
pnpm -C frontend test
pnpm -C frontend build
```

`check`、`lint`、`fmt` 通过 Vite+ 读取 `frontend/vite.config.ts`，需要 Node.js `>=22.18.0`。仓库根 `.node-version` 固定为 `22.22.3`。

## 非目标

1. 不提供完整 IAM 登录、refresh token、权限 guard 和控制台会话 UI。
2. 不提供高风险动作审批、人工确认、通知待办或批量动作。
3. 不迁移当前内存态 AppHub、Ops、IAM store 到 PostgreSQL。
4. 不落地 FileStorage 真实上传下载闭环。
5. 不落地平台级 Aspire AppHost、Compose 生成或安装包。

## 下一阶段衔接

第三阶段之后，项目已经具备“后端纵切 + 控制台契约 + 前端消费”的可持续实施形态。下一阶段不再以脚手架为核心，而应优先把当前内存态事实推进到 PostgreSQL、RabbitMQ、Redis 和平台级 AppHost，并在这个过程中保持 Gateway OpenAPI、api-client、控制台页面和验证脚本同步演进。
