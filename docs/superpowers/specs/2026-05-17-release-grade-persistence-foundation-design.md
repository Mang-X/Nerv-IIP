# 发布级持久化基础设计

## 背景

第四个纵向切片已通过真实基础设施门禁，证明 AppHub 和 Ops 可以运行在 PostgreSQL、Redis 与 RabbitMQ 上。当前仍有一项薄弱点：PostgreSQL 路径在本地验证和服务启动时仍依赖 `EnsureCreated()`。这对纵向切片尚可接受，但不适合作为 IAM、File Storage、Ops 审批、Notification、包安装和客户自主部署的基础。

因此，下一阶段要在增加更多面向用户的能力之前强化持久化。前端工作被有意延后：大多数后端 SDK 与迁移验证无需修改 Console，而且视觉 Design System 尚未选定。只有后端契约发生变化时，本阶段才可以重新生成 API client 并运行前端质量门禁；不得引入新的 Console 页面、组件皮肤或布局决策。

## 推荐方案

以已经拥有真实 PostgreSQL 模型的 AppHub 和 Ops 两个服务为中心，实施发布级持久化纵向切片。增加 EF Core migrations、显式迁移执行 helper、启动防护和验证脚本，证明数据库能够从 migrations 创建，而不是依赖 `EnsureCreated()`。范围应保持足够收敛，以便可靠完成。

已考虑的替代方案：

1. 先实施 IAM。这样可以更早启用认证，但会把关键安全状态建立在未经验证的迁移路径上。
2. 先实施 File Storage。这样会让平台显得更完整，但文件元数据、对象状态和授权都依赖可靠的 schema 演进。
3. 先强化持久化。其产品可见面较少，但能减少 IAM、File Storage、Ops、Notification 和部署方面的后续返工。

本设计选择第三种方案。

## 范围

范围内：

1. 为 AppHub 和 Ops PostgreSQL 模型增加初始 EF Core migrations。
2. 为每个服务增加一个由服务自身拥有、可由测试和脚本调用的小型迁移入口。
3. 将使用 `EnsureCreated()` 的 PostgreSQL 测试准备方式替换为基于迁移的准备方式。
4. 移除 PostgreSQL 服务启动时的 `EnsureCreated()`，替换为适用于 local/dev 脚本、需显式启用的迁移开关。
5. 增加第五阶段验证脚本，用于启动本地基础设施、重置验证数据库、应用 migrations，并重新运行后端 contract/SDK 测试。
6. 如需确保迁移生成可复现，则增加本地 `dotnet-ef` 工具清单。当前 .NET 10 工具会在仓库根目录创建 `dotnet-tools.json`。
7. 更新文档，明确说明前端延后以及 Design System 规划。

范围外：

1. IAM schema migration。
2. File Storage 元数据/对象 provider 实现。
3. CAP 业务 outbox 和 consumer 幂等性。
4. 生产安装程序、Windows Service、systemd 或打包。
5. 前端功能工作或视觉组件重设计。
6. 选择 shadcn-vue、UnoCSS、token 命名、主题或交互设计。

## 架构

每个服务继续在自己的 Infrastructure 项目内拥有数据库 schema 和 migrations：

```text
backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/
  Migrations/
  AppHubDatabaseMigrationRunner.cs

backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/
  Migrations/
  OpsDatabaseMigrationRunner.cs
```

迁移 runner 刻意保持精简：接收服务的 `ApplicationDbContext`、调用 `Database.MigrateAsync`，并只暴露一个异步方法。仅在选择 PostgreSQL 时，Web 启动过程才通过依赖注入接入该 runner。Web host 默认不得静默修改生产数据库。本地脚本可以通过 `Persistence:AutoMigrate=true` 显式启用。

测试使用 migrations 在空数据库中创建 schema。由此证明，干净数据库无需依赖 EF 的 `EnsureCreated()` 快捷方式即可演进到当前模型。

## 数据流

PostgreSQL 验证流程如下：

1. 脚本通过 `infra/docker-compose.dev.yml` 启动 PostgreSQL、Redis 和 RabbitMQ。
2. 脚本删除并重新创建 AppHub/Ops 验证数据库。
3. 测试或迁移命令应用 AppHub 和 Ops migrations。
4. AppHub commands 记录注册、心跳和状态事实。
5. Ops endpoints 创建、派发并完成 operation task。
6. SDK 和 contract 测试在不依赖前端 UI 的情况下运行。
7. 第四阶段真实基础设施门禁继续作为更广泛的回归检查入口。

## 错误处理

迁移失败必须明确暴露。出现以下情况时，脚本和测试必须立即失败：

1. `dotnet-ef` 不可用，或无法还原迁移生成能力。
2. `Database.MigrateAsync` 失败。
3. PostgreSQL 模式下缺少 PostgreSQL connection string。
4. 服务试图在类生产验证路径中使用 `EnsureCreated()`。

迁移生成必须显式使用 PostgreSQL profile。当前 Web 启动默认使用 `Persistence:Provider=InMemory`，因此除非后续引入 design-time factory，否则 `dotnet tool run dotnet-ef ...` 命令需要同时提供 `Persistence__Provider=PostgreSQL` 和服务 connection string。

仅当 `Persistence:AutoMigrate` 为 true 时，运行时服务启动才自动迁移。没有该标志时，启动过程应注册数据库 profile，但将 schema 变更留给 deploy/install 脚本。

## 测试

第一轮实现必须测试先行：

1. 增加测试，断言 AppHub 和 Ops PostgreSQL 配置能够迁移空数据库并持久化当前事实。
2. 在迁移支持缺失或仍使用 `EnsureCreated()` 时，确认这些测试会失败。
3. 增加 migration runners 和 migrations。
4. 重新运行有针对性的 PostgreSQL 测试。
5. 运行后端 solution 测试、connector-host 测试及 SDK/contract 测试。
6. 仅当 Gateway OpenAPI 或 generated client 输入发生变化时，才运行前端生成/build 门禁。本阶段不包含任何前端页面/组件工作。

## 前端延后

Console 仍是有价值的验证界面，但不应成为本阶段的节奏瓶颈。在有意规划 Design System 之前：

1. 不得为迁移状态创建新的 Console 页面。
2. 不得增加新的 UI primitives，也不得重新设计 `packages/ui` 样式。
3. 不得引入组件库或 token system。
4. generated API client 工作必须保持机械化，并可追溯到后端 OpenAPI。
5. 在实现前，将前端 Design System 规划作为一份独立的后续 spec。

## 完成定义

满足以下条件时，本阶段才可关闭：

1. AppHub 和 Ops 已提交初始 migrations。
2. PostgreSQL 测试通过 migrations 而非 `EnsureCreated()` 创建 schema。
3. Web 启动过程在 PostgreSQL 模式下不再调用 `EnsureCreated()`。
4. local/dev 自动迁移已显式启用并有文档说明。
5. 第五阶段验证脚本以 `0` 退出。
6. backend、connector-host 和 contract/SDK 测试通过。
7. 文档已记录：前端功能工作继续延后，等待 Design System 规划。
