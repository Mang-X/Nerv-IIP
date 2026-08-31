# API 契约运行时架构

本文只描述 Nerv-IIP 当前 API 契约的边界、依赖方向和事实所有权；规范性约束见 [`../../governance/api/contracts-and-codegen.md`](../../governance/api/contracts-and-codegen.md)，执行命令见 [`../../runbooks/api-codegen.md`](../../runbooks/api-codegen.md)。

## 事实所有权与对外入口

- 领域服务拥有各自业务事实和业务规则；HTTP endpoint 是其服务契约，不因 endpoint 已存在就自动成为控制台公开能力。
- PlatformGateway 是 Platform Console 的页面级聚合入口；Platform Console 只直接消费 `/api/console/**`，不直接调用 AppHub、Ops、Iam、FileStorage 等平台服务 URL。
- BusinessGateway 是 Business Console 的业务聚合入口；Business Console 只直接消费 `/api/business-console/v1/**`，不直接调用 BusinessMasterData、Inventory、Quality、MES、IAM、FileStorage 等下游服务 URL。
- Gateway 可以执行认证、授权、可信上下文透传、内部服务调用、传输映射和页面级响应整理，但不拥有下游业务事实，也不得把业务决策搬到 Gateway。
- 前端展示、菜单或页面不是 API 事实来源；后端已有聚合根或 endpoint 也不授权前端提前暴露功能。

## 契约事实链

1. 后端 endpoint、DTO、授权与 FastEndpoints.Swagger 产生 Gateway OpenAPI。
2. 受控 Gateway OpenAPI snapshot 是前端代码生成输入；snapshot 是派生输入，不是可手工编辑的替代事实来源。
3. `@hey-api/openapi-ts` 由 snapshot 生成隔离的 type/client/SDK/Pinia Colada 代码。
4. `@nerv-iip/api-client` 的稳定导出是应用消费边界；应用不得从 generated 目录深层导入。
5. facade 的业务服务 endpoint→公开面分类由 [`../../reference/api/facade-coverage-matrix.json`](../../reference/api/facade-coverage-matrix.json) 提供机器事实，并由 [`../../governance/api/facade-coverage.md`](../../governance/api/facade-coverage.md) 规定治理语义。

因此依赖方向固定为：

```text
Domain/Service contract
  -> Gateway endpoint + OpenAPI
  -> committed OpenAPI snapshot
  -> generated api-client
  -> stable api-client export
  -> application UI
```

任何下游层都不能反向定义上游契约。

## Gateway 分层

PlatformGateway 与 BusinessGateway 分别拥有自己的 OpenAPI 输出和生成子目录，避免多输入生成相互清理。BusinessGateway 对下游业务服务的 facade 还受 [`../../governance/api/business-gateway-surface.md`](../../governance/api/business-gateway-surface.md) 与 facade coverage 机器事实约束。

跨服务调用中的“内部服务身份”和“终端用户已获授权”是两种不同事实：内部服务令牌只证明服务调用方；需要向下游传递额外用户权限结果时，由入口 Gateway 先完成 IAM 检查，再传递受信任的最小授权声明。具体安全规则属于 Governance，不在 Architecture 重复编码。

## Platform SDK 边界

Platform SDK 是平台向应用、Connector Host 和扩展提供的稳定能力集合，模块与能力基线由 [`../platform-sdk-baseline.md`](../platform-sdk-baseline.md) 维护。SDK 应从 OpenAPI、公开 DTO 和版本化协议生成或包装，不依赖服务端 Domain、Infrastructure 或数据库模型；SDK/ApplicationVersion 的版本治理见 Governance。

## Business PDA / Mobile 边界

当前 `frontend/apps/business-pda` 复用 BusinessGateway `/api/business-console/v1/**` 与 `@nerv-iip/api-client` 的 business-console 稳定导出；仓库当前没有独立 `/api/mobile/v1/**`、`business-gateway-mobile.v1.json` 或 mobile generated client 作为已交付契约。

未来若引入移动专用 API，它只承担 PDA 特有的 bootstrap、个人任务、弱网同步、设备注册、诊断等能力，不能复制已有 business-console facade。具体是否落地及 operationId 由届时后端/OpenAPI 生产者决定，本文不保存未来端点清单。

## 权威边界

本文不维护命令、具体 operationId/端点表、工具版本、例外清单、阶段历史或一次性审计结果：

- 规范性规则：[`../../governance/api/contracts-and-codegen.md`](../../governance/api/contracts-and-codegen.md)
- 执行与排障：[`../../runbooks/api-codegen.md`](../../runbooks/api-codegen.md)
- 稳定路径、机器事实与受控例外：[`../../reference/api/contracts-and-codegen.md`](../../reference/api/contracts-and-codegen.md)
- 历史总账与端点表快照：[`../../reports/audits/api-contract-and-codegen.md`](../../reports/audits/api-contract-and-codegen.md)

精确 endpoint、operationId、DTO 和生成类型始终以当前后端/OpenAPI/生成代码为准，禁止从历史报告反向恢复当前契约。