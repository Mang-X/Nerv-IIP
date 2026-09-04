# API 契约与代码生成治理

本文是 Nerv-IIP API/OpenAPI/codegen 的当前规范性入口。运行时结构见 [`../../architecture/integration/api-contracts.md`](../../architecture/integration/api-contracts.md)，具体命令见 [`../../runbooks/api-codegen.md`](../../runbooks/api-codegen.md)，稳定路径与受控例外见 [`../../reference/api/contracts-and-codegen.md`](../../reference/api/contracts-and-codegen.md)。

## 单一事实来源

1. OpenAPI 是 HTTP 公开契约的事实来源；不得手工修改 OpenAPI snapshot 来绕过后端契约。
2. 前端不得手写大批与 OpenAPI 重复的 DTO、请求函数或并行网络层；生成代码与手写稳定封装必须隔离。
3. 生成目录不是手写扩展点；稳定消费面由 `@nerv-iip/api-client` 提供，应用不得深层导入 generated 文件。
4. API 契约升级必须能追踪到后端契约/测试、OpenAPI、生成客户端和真实消费方；若需要架构决策或迁移说明，还必须同步对应 ADR/文档。
5. 任何 JSON/text 序列化字段进入 API、SDK、IntegrationEvent 或外部协议前，必须有明确 schema/version/compat 语义，不能把数据库 JSON blob 直接提升为公开契约。
6. endpoint、operationId、DTO、权限和生成类型的精确清单由当前代码/OpenAPI/生成物生产，不在治理 Markdown 中维护第二份可漂移登记表。

## 兼容、版本与 SDK

- Platform SDK、HTTP API、事件和协议的兼容主版本必须与平台主版本边界一致；受管应用的 `ApplicationVersion` 仍只表示应用自身业务/镜像/发布版本，两者不得混用。
- 同一主版本内应保持向后兼容：新增可选字段、新增 endpoint、新增能力码或错误码可以作为兼容演进。
- 删除字段、改变字段语义或必填性、改变认证/授权语义、改变事件含义或移除能力属于破坏性变更，必须经过明确的主版本治理和迁移窗口。
- SDK 应由 OpenAPI、公开 DTO 与版本化协议生成或包装，不得引用服务端 Domain、Infrastructure 或数据库模型。SDK 模块/能力边界以 `docs/architecture/platform-sdk-baseline.md` 为权威；本页不复制模块清单。
- 一次性兼容例外必须记录到 Reference，写明精确范围和失效条件；例外不得反向放宽一般版本规则。

## OpenAPI 与 operationId

1. PlatformGateway 与 BusinessGateway 必须输出稳定 OpenAPI；本地文档入口为 `/swagger/v1/swagger.json`。
2. 面向生成客户端的公开 endpoint 必须提供稳定、可读、lower camelCase 的 `operationId`，以业务动作表达意图。
3. 新增或修改 Gateway 公开 endpoint 时，先修改后端 endpoint/DTO/授权及 OpenAPI、授权、代理测试，再导出 snapshot，最后重新生成客户端与消费方。
4. facade 的 `exposed` / `deferred` / `internal` 分类必须按 [`facade-coverage.md`](./facade-coverage.md) 同 PR 更新；`exposed` 只有在 Gateway OpenAPI 中存在可验证 operationId 时才成立。
5. OpenAPI snapshot 与 generated client 是派生产物：发现漂移时只能修正上游契约/生产流程并重新生成，不得反向手改派生产物。

## Gateway、授权与事实边界

- Platform Console 不直接调用平台领域服务 URL；Business Console 不直接调用 BusinessMasterData、Inventory、Quality、MES、IAM、FileStorage 等下游服务 URL。
- BusinessGateway 可以做用户鉴权、IAM 权限检查、可信上下文传递、内部令牌调用、传输映射和页面级聚合，但不拥有业务事实、业务状态机或业务决策。
- 内部服务令牌只证明服务调用方身份，不能单独代表终端用户拥有高风险权限。需要把“用户已通过额外权限检查”传给下游时，入口 Gateway 必须先实时完成 IAM 检查，再发送由共享受控密钥签名的最小授权声明；下游必须校验签发方、签名、明确 permission code、组织/环境、请求绑定信息和短时效。缺密钥、签名无效、上下文不匹配或过期必须按未授权处理。不得使用可由客户端伪造的裸权限 header。
- 聚合多个来源的页面级 API 必须对每个来源分别做当前主体权限检查；无权来源不得查询或泄漏对象名称、金额、消息标题等敏感内容，只能返回不泄密的来源状态。
- 服务端已经存在 endpoint/聚合根，不等于 Gateway facade、Business Console 菜单或页面已经获准公开；公开面必须由对应交付范围和 facade/OpenAPI 证据决定。
- Gateway 只透传或显式映射下游事实，不得凭旧样本、默认值、当前页数据或 UI 需要反造状态、质量、新鲜度、授权范围、业务编号或领域关系。
- BusinessGateway client public surface 的独立 canonicalization/restore 合同见 [`business-gateway-surface.md`](./business-gateway-surface.md)。

## 查询、分页与受控枚举

- 对外列表存在 `total` 时，关键字、状态、范围、时间等已支持过滤必须在事实 owner 的服务端查询中、`total` 计算前应用；不得在已分页结果上叠加当前页过滤却向用户表现为全量搜索。
- Gateway 不得为了 UI 方便扩展下游没有的过滤维度或业务语义；暂不支持的过滤应明确不暴露或按契约返回 unsupported，而不是伪造参数。
- 有稳定有限集合的状态/类型应通过 OpenAPI 受控枚举表达，避免公开自由文本导致文档、生成客户端和实际校验漂移。

## 平台敏感传输边界

- FileStorage 的 Gateway 响应不得暴露内部 `objectKey`/`object_key`、MinIO/S3 等对象存储直连 URL 或内部服务 URL；上传/下载必须使用受控指令或 Gateway 代理路径。
- 日志/可观测查询的公开 DTO 必须保持平台中立，只暴露受控过滤条件，不把 LogsQL、VictoriaLogs 内部 API、租户 header、数据源 URL 或凭据提升为平台契约。
- 这些规则约束公开边界，不把具体存储/日志实现变成 API 事实所有者。

## 生成代码与稳定消费面

- `frontend/packages/api-client/openapi-ts.config.ts` 是当前 Hey API 输入/输出配置生产者；版本精确值以 package/build 配置为准。
- generated 目录只包含生成文件，不允许手改。PlatformGateway 与 BusinessGateway 使用隔离输出路径；多输入生成不得互相清理别的输入结果。
- 应用只从 `@nerv-iip/api-client` 的稳定导出消费。`frontend/apps/business-console` 不得深层导入 `src/generated/business-console/**`；其他应用同样不得绕过其稳定入口。
- 后端/OpenAPI 机械生成出新 client 并不自动授权新增页面、菜单或产品能力；UI 公开范围仍服从相应产品/交付治理。

## 可复现 OpenAPI 导出

OpenAPI 导出必须尽量与开发机 NuGet 缓存无关。Gateway 的 Swagger/NJsonSchema 配置保持 `ResolveExternalXmlDocumentation = false`，禁止从 NuGet 全局缓存或 SDK 目录探测外部 XML 文档并把机器差异写入 schema description；仓库项目随构建输出的 XML 文档不受此规则禁止。

`scripts/export-gateway-openapi.ps1` 在构建/导出路径固定 `NUGET_XMLDOC_MODE=skip`，与 CI 还原行为对齐，避免导出流程继续污染或依赖本机 NuGet XML 缓存。该环境变量是辅助一致性措施，不能替代上面的 Swagger 配置；发现 snapshot 漂移时不得通过删除本机单个 XML 文件、改变 `NUGET_PACKAGES` 或手工改 snapshot 规避。

## 变更完成定义

一次公开 API 变更只有在适用事项一致时才完成：

1. 后端 endpoint/DTO/授权与对应契约测试；
2. Gateway OpenAPI 与稳定 operationId；
3. 需要的 facade coverage 分类；
4. 受控 OpenAPI snapshot；
5. 生成代码与稳定 barrel；
6. 实际应用消费方；
7. 现有 OpenAPI/api-client Drift、Script Governance 及受影响 CI lane。

不得用“生成成功”替代契约审核，也不得用前端临时适配掩盖后端漂移。只报告实际执行的测试/lane；docs-only、局部测试或某个 provider 绿灯不能外推为未运行范围的证明。

本次 M2-I 只重构权威住所，不新增 API registry、scanner、工具升级或第二套 CI。