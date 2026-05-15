# API 契约与代码生成规范

本文档定义 Nerv-IIP 前后端接口契约的单一事实来源、代码生成链路、目录边界与变更流程。

## 总原则

1. OpenAPI 是接口的单一事实来源。
2. 前端不手写大批 DTO 与重复请求函数。
3. 生成代码与手写代码必须隔离。
4. API 契约升级必须能追踪到 ADR、后端接口变更和前端消费更新。
5. 主平台对外提供的 SDK、OpenAPI、事件和协议遵循主版本对齐、小版本兼容策略。

## Platform SDK 与版本策略

1. Platform SDK 是主平台提供给应用、Connector Host、行业扩展和前端包的稳定能力集合，详细模块边界见 docs/architecture/platform-sdk-baseline.md。
2. Platform SDK 可包含 OpenAPI 生成客户端、公开 DTO、Connector Protocol、认证与授权上下文、文件存储客户端、上传指令 DTO、运维客户端、通知客户端、观测上下文辅助、错误模型、事件契约和缓存键辅助约定。
3. 应用、Connector Host 或扩展的主版本必须与主平台主版本对齐；例如 1.x 应用面向 1.x 主平台，2.x 应用面向 2.x 主平台。
4. 应用、Connector Host 或扩展的小版本可以低于主平台小版本；主平台 1.5 应尽量兼容基于 1.0 到 1.5 SDK 构建的应用。
5. 同一主版本内的 SDK、API、事件和协议变更应保持向后兼容，包括新增可选字段、新增端点、新增能力码和新增错误码。
6. 删除字段、改变字段语义、改变必填性、改变认证语义、改变事件含义或移除能力属于破坏性变更，必须提升主版本。
7. 主平台大版本升级时，应明确支持的旧主版本迁移窗口；迁移窗口结束后，旧主版本应用需要升级到相同主版本。
8. 这里的版本策略指 Platform SDK 兼容版本，不等同于 AppHub 记录的 ApplicationVersion。ApplicationVersion 仍表示受管应用自身的业务版本、镜像版本或发布版本。

## SDK 模块与契约生成

1. `Nerv.IIP.Sdk.Core` 提供 transport、错误模型、correlationId、idempotencyKey、组织环境上下文和版本上报。
2. `Nerv.IIP.Sdk.Auth` 提供 token、client credential 和认证头处理，但不做最终授权判断。
3. `Nerv.IIP.Sdk.ConnectorProtocol` 提供注册、心跳、状态快照和动作结果回传客户端，不拥有 AppHub 事实。
4. `Nerv.IIP.Sdk.FileStorage` 提供上传会话、上传指令、完成/取消上传和下载授权客户端，隐藏 tus、S3 multipart 和 server-proxy 差异。
5. `Nerv.IIP.Sdk.Ops` 提供运维任务与动作结果客户端，可提交审计意图，但正式 AuditRecord 仍由 Ops 服务端生成。
6. `Nerv.IIP.Sdk.Notification` 提交通知意图、查询通知、标记已读和查询待办，不直接调用外部通知通道。
7. `Nerv.IIP.Sdk.Observability` 提供 trace context、correlationId 和标准日志字段辅助，不替代日志采集或审计落库。
8. SDK 模块应优先从 OpenAPI、公开 DTO 和版本化契约生成或包装，不允许引用服务端 Domain、Infrastructure 或数据库模型。

## 契约来源

### 后端责任

1. 后端服务或 Gateway 负责输出稳定 OpenAPI 文档。
2. Gateway 暴露给前端的页面级接口也必须纳入 OpenAPI。
3. 契约变更需要遵循同主版本向后兼容或显式主版本升级原则。

### 前端责任

1. 前端通过 Hey API 从 OpenAPI 生成 types、sdk、client 与 Pinia Colada 查询、变更函数。
2. 页面和 composables 不直接拼接 URL，也不绕过生成客户端手写重复网络层。

## 推荐目录结构

```text
frontend/packages/api-client/
  openapi-ts.config.ts
  src/
    generated/
    transport/
    index.ts
```

### generated

- 只放代码生成文件。
- 推荐包含 client.gen.ts、sdk.gen.ts、types.gen.ts，以及 Colada 查询与变更生成文件。
- 不允许手改。

### transport

- base-url.ts
- auth.ts
- error.ts
- client-config.ts

职责是统一 baseURL、认证头、错误归一化和请求级策略。

### index.ts

- 作为稳定导出入口。
- 应用层只消费这里，而不消费 generated 深层路径。

## 生成链路

1. 后端更新接口并同步 OpenAPI。
2. 前端运行 Hey API 生成命令。
3. api-client 更新 generated 与 transport 组合导出。
4. console 应用与共享 composables 通过稳定入口消费新的 sdk/query/mutation。
5. 变更涉及 breaking change 时，必须同步更新对应页面、组合函数和文档。

## 使用规则

1. 页面组件和 composables 不直接写 fetch 或 axios URL。
2. 页面优先消费生成的 query/mutation helpers。
3. 少量页面特有参数整理可以放在 src/api/领域名/adapters.ts 中。
4. api-client 只做契约、transport 和稳定导出，不放业务视图逻辑。

## 版本与变更管理

1. OpenAPI 变更必须在 PR 中可见。
2. 生成文件允许较大 diff，但 hand-written transport 层需要保持最小、稳定。
3. 破坏性改动必须提升主版本，并同步更新前端消费点、SDK、迁移说明与文档。
4. 不允许前端在契约未更新时通过手写 DTO 临时绕过。
5. SDK 模块新增或行为变化必须同步更新 docs/architecture/platform-sdk-baseline.md。

## 反模式

1. 在页面里直接手写 URL、headers 和重复错误处理。
2. 在 generated 目录里补自定义逻辑。
3. 让多个包各自维护不同版本的相同接口类型。
4. 让 Gateway 返回未进入 OpenAPI 的隐式接口。
5. 让 SDK 变成服务发现中心、权限事实源、审计事实源、通知事实源或服务端领域模型副本。
