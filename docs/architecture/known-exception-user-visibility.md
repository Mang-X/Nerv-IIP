# KnownException 用户可见口径与 Gateway 传输政策

本文档规定如何判断一个 `KnownException` 源码位点是否属于用户可见领域拒绝消息，并冻结
Gateway 传输政策、临时计数边界和后续域票的销账模板。它是 #1864 各域中文化子票的共同
口径，不是全仓位点账本，也不证明任何域已经完成中文化。

## 范围与非范围

本文档覆盖：

- `transportVisible` 与 `uiRenderedNow` 的定义；
- 从异常位点到公开 Gateway 响应的六步判定；
- BusinessGateway、IAM、Notification、AppHub 的当前消息传输政策；
- 动态消息、异常透传、错误码形态和临时计数的处理；
- 后续域票冻结分母、实施和验收时必须使用的模板。

本文档不执行下列工作：

- 不建立 783 条全仓位点账本或最终销账分母；
- 不新增全仓 scanner、共享测试项目或 CI 门禁；
- 不修改生产路由、公开契约、Gateway、前端或业务消息；
- 本文不裁决 Maintenance、IndustrialTelemetry 等模块的稳定错误码是否改为自然语言；这两个
  模块在 #1870 基准上的逐值证据与兼容结论见
  [Maintenance / IndustrialTelemetry 稳定错误值调查与兼容裁决](maintenance-industrial-telemetry-stable-error-contract.md)；
- 不用本次临时盘点代替后续域票基于当时 `main` 的重新扫描和逐项分类。

## 两个概念

### `transportVisible`

`transportVisible=true` 表示一个 `KnownException` 位点满足本文后述全部六步条件：异常在同一
同步请求中到达服务 HTTP 边界；对应服务 endpoint 已通过公开 Gateway facade 暴露；Gateway
读取并保留该异常的原始 `message`，再将它写入公开响应。

这是 #1864 中文化的销账口径。只要消息已经能进入公开 Gateway 响应，就不能因为当前页面尚未
消费该 operation 而把它排除。否则新增页面或启用既有入口时，旧英文消息会在没有后端变更的
情况下重新暴露。

### `uiRenderedNow`

`uiRenderedNow=true` 表示当前前端页面确实消费对应 Gateway operation，并把失败送入 toast、
行内错误或其他用户可见反馈路径。它只描述当前产品实现，不决定 #1864 的中文化分母。

`uiRenderedNow` 必须以实际 operation 消费点和错误展示代码为证据。OpenAPI 中存在 operation、
生成了客户端、页面能发起相邻请求，均不能单独证明错误消息已经上屏。

## 权威来源与证据优先级

业务服务 HTTP 使用面以
[`facade-coverage-matrix.json`](facade-coverage-matrix.json) 为机器可读事实源；分类语义与门禁见
[`facade-coverage-matrix.md`](facade-coverage-matrix.md)。其中 `exposed` 表示已交付 Gateway
operation、OpenAPI snapshot、生成客户端和稳定导出；`deferred` 与 `internal` 均不构成公开
facade。

平台服务没有纳入业务 facade matrix 时，必须同时给出服务 endpoint、Gateway endpoint、Gateway
client 和公开 operation 的源码证据，不能从“服务有 HTTP endpoint”推断前端可见。

证据按以下顺序使用；低层证据不得推翻高层证据：

1. 通过真实服务和 Gateway HTTP 边界触发该位点，并检查公开响应的可重复测试或运行证据；
2. 服务 endpoint、facade matrix、Gateway client 和公开响应写入点形成的完整源码链；
3. endpoint 调度到命令/查询及其处理器、领域行为或同步领域事件处理器的静态调用证据；
4. 目录名、类名、异常文本、注释或“看起来像用户操作”的推测。

第 4 层只能用于寻找代码，不能单独完成分类。没有足够证据时必须登记为“待分类”，不得就近
猜成可见或不可见。

## `transportVisible` 六步判定

后续域票必须对每个候选位点依次回答以下六项。只有六项全部为“是”时，才能登记
`transportVisible=true`。

1. **同步来源**：异常在用户发起请求的同一同步执行链上抛出。命令处理器、查询处理器、领域
   行为及同事务同步领域事件处理器可以继续判定；CAP/集成事件消费者、scheduler、seed、startup
   和后台 worker 不属于原请求同步链。
2. **到达服务 HTTP 边界**：异常没有在到达 HTTP 中间件前被捕获、改写或转为成功/半成功响应，
   服务实际把它映射为带 `message` 的失败响应。仅证明某个方法会抛异常不够。
3. **存在公开 facade**：业务 endpoint 在 facade matrix 中为 `exposed`；平台服务则有明确的公开
   Gateway endpoint 与 operation。`deferred`、`internal`、服务间 callback 和只能直连服务 URL 的
   endpoint 在本步即为“否”。
4. **Gateway 读取消息**：对应 Gateway client 从下游失败信封的 `message` 字段读取文本，而不是只
   检查状态码、`ReasonPhrase` 或 `EnsureSuccessStatusCode()`。
5. **Gateway 保留消息**：该状态码和消息形态通过 Gateway 的安全过滤，没有被固定错误码、通用
   文案或另一个异常构造器替换。必须按 endpoint 实际使用的 client 判定，不能只看同一 Gateway
   中存在一个 preserve helper。
6. **Gateway 写回公开响应**：保留下来的文本进入公开 Gateway 失败响应的 `message`、`detail` 或
   等价用户错误字段，而不是只进入日志、内部异常、健康状态或诊断证据。

一个源码位点可有多个同步根。只要至少一条根路径通过六步，它就是
`transportVisible=true`；销账按源码位点计一次，同时记录全部已确认根。只有证明所有根都无法
通过六步，才能归为不可见。

## 异步与无 facade 边界

- CAP/IntegrationEventHandler 中抛出的异常不会回到原始 HTTP 调用者，因此不属于原操作的
  `transportVisible`。若异常内容另行持久化到死信并被管理页面展示，那是另一个“死信读取
  endpoint → Gateway → 页面”的可见链，必须按该读取链单独举证，不能把消费者抛出本身算作同步
  用户拒绝。
- 同事务同步 DomainEventHandler 的异常可能使命令失败并沿原 HTTP 请求返回，不能仅因类名包含
  `EventHandler` 就排除；必须确认事件类型和调度时机。
- `deferred` endpoint 当前没有公开 facade，记为 `transportVisible=false`；后续转成 `exposed` 时
  必须重新分类其可达位点。
- `internal` endpoint、scheduler endpoint、Connector/WCS callback 和服务间 RPC 按设计没有前端
  facade，记为 `transportVisible=false`。如果同一处理器还被另一个 `exposed` endpoint 调用，仍按
  多根规则重新判定。
- `backend/common/Coding` 等共享库没有独立 facade。其异常位点不得按目录整体纳入或排除，而应
  继承实际调用方的同步根、endpoint 分类和 Gateway 传输政策。

## Gateway 传输政策证据表

下表描述当前实现，而不是要求所有 Gateway 统一行为。后续域票必须选择实际经过的行；如果代码
已经变化，应先更新证据和结论，不能沿用过期政策。

| 边界 | 政策 | 当前行为 | 源码证据 |
| --- | --- | --- | --- |
| BusinessGateway 通用 `BusinessServiceHttpClient`，下游 HTTP 400 | `preserve`（有条件） | 从失败信封读取 `message`，只对 400 使用 business-message 路径；消息须非空、最长 500 字符、首字符非空白，且不含控制字符或 `<>{}/\\`，否则改为 `downstream-request-failed` | [`BusinessServiceClients.cs`](../../backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessServiceClients.cs) 第 1793-1839、1903-1908、1983-2005 行 |
| BusinessGateway 通用 `BusinessServiceHttpClient`，下游 HTTP 2xx 且 `success=false` | `preserve`（有条件） | 默认从成功状态的失败信封读取 `message`，再走与 400 相同的 business-message 安全过滤；调用方设置 `failClosedOnFailureEnvelope=true` 时内部先抛 `InvalidOperationException`，随后在请求包装层转为 `BusinessServiceProxyException("downstream-invalid-response")`，公开响应不保留原消息 | `BusinessServiceClients.cs` 第 1793-1802、1926-1931、1952-1963 行 |
| BusinessGateway 通用 client，下游非 2xx 且非 400 | `redact` | 只允许最长 128 字符的 ASCII 字母、数字、`-_.` 稳定码；自然语言消息会改为 `downstream-request-failed` | 同文件第 1781-1821、1903-1908 行 |
| BusinessGateway `AuthorizedBusinessProxyEndpoint` | `preserve` | 捕获 `BusinessServiceProxyException`，把 `ex.Message` 写入公开 `ResponseData.message` | [`AuthorizedBusinessProxyEndpoint.cs`](../../backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/Auth/AuthorizedBusinessProxyEndpoint.cs) 第 44-56 行；[`ResponseDataEndpointResults.cs`](../../backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/ResponseDataEndpointResults.cs) 第 26-38 行 |
| BusinessGateway 直接调用公开构造器 `new BusinessServiceProxyException(status, message)` | `redact` | 构造器刻意丢弃 `message`，异常文本固定为 `downstream-request-failed` | `BusinessServiceClients.cs` 第 1755-1767 行 |
| PlatformGateway → IAM 认证/管理，下游非 2xx | `redact` | IAM 管理调用把 400 映射为 `iam-bad-request`；认证调用只保留受控登录失败码，其他状态映射为 Gateway 稳定码，不读取并透传失败信封中的 KnownException 文本 | [`GatewayIamAdminClient.cs`](../../backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Application/IamAdmin/GatewayIamAdminClient.cs) 第 235-272、310-320 行；[`GatewayIamAuthClient.cs`](../../backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Application/Auth/GatewayIamAuthClient.cs) 第 108-188 行 |
| PlatformGateway → IAM 认证/管理，下游 2xx 且 `success=false` | `preserve` | 两个 client 都读取 `envelope.Message` 并放入 `GatewayAuthException.Reason`。IAM Admin facade 由 `AuthorizedProxyEndpointExecutor` 写回公开错误响应；认证 facade 由 `ConsoleAuthEndpointResults` 写回。该分支与上面的非 2xx 状态映射必须分开判定 | `GatewayIamAdminClient.cs` 第 192-213 行；`GatewayIamAuthClient.cs` 第 75-96 行；[`AuthorizedProxyEndpoint.cs`](../../backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Application/Auth/AuthorizedProxyEndpoint.cs) 第 143-178 行；[`ConsoleAuthEndpoints.cs`](../../backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Endpoints/Auth/ConsoleAuthEndpoints.cs) 第 18-25、112-129 行 |
| PlatformGateway → Notification | `preserve` | client 从失败信封读取 `Message` 并放入 `GatewayNotificationException`；Gateway endpoint 再把 `exception.Message` 写回公开响应 | [`GatewayNotificationClient.cs`](../../backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Application/NotificationClient/GatewayNotificationClient.cs) 第 255-276 行；[`ConsoleNotificationEndpoints.cs`](../../backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Endpoints/Notifications/ConsoleNotificationEndpoints.cs) 第 644-652 行 |
| BusinessGateway → Notification 直接 endpoint | `preserve`（有条件） | `HttpBusinessNotificationClient` 已注册并继承通用 client；消息列表、任务列表和标记已读三个公开 endpoint 分别通过它转发。因此这三个 operation 具备 facade 证据，并沿用“400 或默认 2xx failure envelope 保留”的政策；其他 Notification 调用不能仅凭 client-level policy 推断相同结论 | [`Program.cs`](../../backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Program.cs) 第 128-131 行；`BusinessServiceClients.cs` 第 2211-2250 行；[`BusinessConsoleNotificationEndpoints.cs`](../../backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Notifications/BusinessConsoleNotificationEndpoints.cs) 第 10-80 行 |
| BusinessGateway → Notification Workbench 汇总 | `redact` / `no-message-write` | Workbench 使用同一 `IBusinessNotificationClient` 调用消息和任务列表，但捕获 `BusinessServiceProxyException` 后只把 Notification source 标记为不可用，公开汇总响应不写回原 `message` | [`BusinessConsoleWorkbenchEndpoints.cs`](../../backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Workbench/BusinessConsoleWorkbenchEndpoints.cs) 第 20-23、56、184-199、218-222 行 |
| PlatformGateway → AppHub | HTTP 400 `redact`；HTTP 2xx 且 `success=false` `preserve`（有条件） | AppHub 当前把 `KnownException` 映射为 HTTP 400，因此 `EnsureSuccessStatusCode()` 会在读取失败信封前拦截，原消息不被保留。若响应形态变为 HTTP 2xx 且 `success=false`，`HttpAppHubClient` 会读取 `envelope.Message` 并抛出 `HttpRequestException`，`InstanceEndpoints` 再将其写入公开的 AppHub 不可用消息，因此该分支为 conditional preserve；后续状态码配置或响应形态变化时必须重新分类 | [`Program.cs`](../../backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Program.cs) 第 109 行；[`AppHubClient.cs`](../../backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Application/AppHubClient/AppHubClient.cs) 第 12-42 行；[`InstanceEndpoints.cs`](../../backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Endpoints/Instances/InstanceEndpoints.cs) 第 117-125 行 |
| BusinessGateway → AppHub | `redact` | client 虽读取下游信封，但失败时调用会丢弃 `message` 的公开 `BusinessServiceProxyException` 构造器 | [`BusinessAppHubClient.cs`](../../backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessAppHubClient.cs) 第 28-38 行；`BusinessServiceClients.cs` 第 1755-1767 行 |
| facade matrix 中的 `deferred` / `internal` endpoint | `no-facade` | 当前没有可由业务前端消费的公开 Gateway facade；`internal` 还必须带设计理由 | `facade-coverage-matrix.md` 第 19-39、41-55 行 |
| CAP/后台入口及只有共享库源码位点、尚未找到公开同步根的情况 | `no-facade` | 不存在从该执行入口返回原请求的公开 Gateway 错误链；共享位点等待按调用根归属 | 本文“异步与无 facade 边界” |

`preserve` 只证明原消息能到达公开 Gateway 响应，不证明消息适合直接展示，也不自动令
`uiRenderedNow=true`。

IAM 的 `KnownException` 中间件路径当前返回非 2xx，因此对应位点通常命中“非 2xx redact”行；
只有证明某个位点在到达 Gateway 前被转换成 2xx `success=false` 信封，才能使用 IAM 的 preserve
行。BusinessGateway 同理，域票必须记录实际状态与 `failClosedOnFailureEnvelope` 参数，不能只写
“使用通用 client”。

## 前端当前安全边界

Business Console 的统一分层反馈链先从响应信封读取 `message`、RFC 7807 `detail`/`title` 或
校验错误，再交给 `friendlyErrorMessage`。只有包含中文且长度不超过 60 个字符的服务端消息会
原样展示；英文 HTTP/5xx 文案映射为中文通用提示，未上屏原文只进入 `console.error`。证据见
[`notify.ts`](../../frontend/apps/business-console/src/utils/notify.ts) 第 77-90、212-235 行。

因此，后续域票对 `transportVisible=true` 的自然语言消息仍须满足：

- 固定叙述使用简体中文；动态编码、ID、编号和原始值不翻译；
- 估算展示长度不超过 60 个字符；
- 不包含会触发 Gateway 安全替换的控制字符或 `<>{}/\\`；
- 文案点名被拒绝对象并给出可执行下一步，但不泄露堆栈、provider、连接信息或敏感事实。

60 字符与中文透传是 Business Console 当前实现边界，不能无证据推广为 Platform Console、PDA
或外部消费者的统一行为。判定 `uiRenderedNow` 时必须检查对应前端的真实错误处理入口。

## 动态消息与 pass-through

以下形态不能由“取最长字符串字面量”安全分类：

- `new KnownException(message)`；
- `new KnownException(exception.Message)`；
- `new KnownException(verification.Message)`；
- target-typed `new(...)`；
- 跨方法返回的字符串、资源键、拼接、条件表达式和本地化结果。

这些位点不得因为扫描器提取不到字面量而从候选中消失。后续域票必须先按六步判断
`transportVisible`，再处理消息来源：

1. 能证明所有可能值均为安全、简短、可行动中文时，记录来源及证明；
2. 可能透传下游、provider、异常类型、英文或超长内容时，改为稳定的中文外层消息，完整异常只
   用于受控日志；
3. 无法穷举可能值时登记为“待裁决”，不得计入已完成；
4. kebab-case、snake_case 或其他稳定码先标记 `messageShape=code`，交由错误码契约子票裁决，
   不在普通自然语言中文化子票中直接改值。

## 临时计数边界

以下数字只用于解释 [GitHub Issue #1864](https://github.com/Mang-X/Nerv-IIP/issues/1864)
的起始盘点为何不能充当关闭分母：

| 临时指标 | 当前值 | 边界 |
| --- | ---: | --- |
| 生产范围内显式 `new KnownException(` 文本命中 | 783 | 2026-08-19 在 `e49013f7bf539193df6a2da8a2a6ad7745bb3917`，范围为 `backend/services/Business`、IAM、Notification、AppHub、`backend/common/Coding`，排除 `tests/bin/obj`；不覆盖 target-typed `new(...)`，不是完整位点数 |
| 其中 Business 目录 | 689 | 只按目录计数，不代表可见，也不代表英文 |
| 其中 IAM、Notification、AppHub、common/Coding | 94 | 只按目录计数；共享 Coding 必须归属调用根 |
| 明确位于 `IntegrationEventHandler` 命名路径或文件的文本命中 | 37 | 只是可优先调查的异步候选；不能靠名称排除同步 DomainEventHandler 或同文件其他同步根 |
| #1864 票面字符串快照被分类总数 | 657 | Business 582（中文 234、英文 348）加其他服务英文 75；与显式文本命中 783 不是同一种扫描集合，不能用差值反推具体漏项 |
| #1864 票面快照英文数 | 423 | Business 英文 348 加其他服务英文 75；未经过六步判定，不能作为关闭分母 |
| #1864 票面快照暂扣 IAM 31、AppHub 4 后的消息保留候选上界 | 388 | 计算仅为 `423 - 31 - 4`；仍含 CAP/后台、`internal`、`deferred`、共享 Coding 和无公开根位点，只能用于估算后续调查量 |

生成 783 条显式命中清单的只读命令如下。只保留 `backend/services/Business` 路径可复核 689；
删除该路径、保留其余四个范围可复核 94：

```bash
rg -n -o --glob '*.cs' --glob '!**/tests/**' --glob '!**/obj/**' --glob '!**/bin/**' \
  'new\s+KnownException\s*\(' \
  backend/services/Business backend/services/Iam backend/services/Notification \
  backend/services/AppHub backend/common/Coding | wc -l
```

后续域票不得写“将本域英文快照全部翻译”作为完成条件。必须从当时 `main` 重新生成候选，逐项
完成六步判定，并以 `transportVisible=true` 且消息不是合规中文自然语言的集合为销账分母。

657、423、388 只转录自 #1864 当前票面正文；仓库内没有可复现这三项分类结果的 artifact、逐项
清单或权威命令。上面的 `rg` 只能复现 783/689/94 三个显式文本命中数，不能复现票面的语言分类
或 388 候选推导。后续域票不得把 657、423、388 写成已由仓库证据复核。

## #1864 后续实施顺序

各域子票按以下顺序独立推进，不把分类、错误码裁决和多个业务域重新揉进同一 PR：

1. 从最新 `main` 生成本域候选，包含显式、target-typed、动态和 pass-through 形态；
2. 按六步冻结 `transportVisible` 分母，并单列“待分类”和“错误码形态”；
3. 错误码形态等待对应契约裁决；普通自然语言位点在本域 PR 内中文化；
4. 运行本域测试、Gateway 相关合同和文案检查，只报告实际执行证据；
5. PR 审核以“冻结分母 = 已合规 + 本 PR 修复 + 明确排除 + 待后续裁决”闭合，不以 grep 归零
   代替；
6. 全仓防回潮门禁由 #1864 的后续门禁子票单独交付，不搭入任一域中文化 PR。

## 后续域票模板

每个域票的 Issue/PR 正文至少包含以下内容：

```markdown
## 范围与基线

- 基准 `main`：`<SHA>`；盘点时间：`<UTC>`
- 服务/域：`<service>`
- 候选总数：`<n>`
- `transportVisible=true`：`<n>`
- `transportVisible=false`：`<n>`
- `uiRenderedNow=true`：`<n>`（现状说明，不参与中文化分母）
- 待分类：`<n>`
- 错误码形态待裁决：`<n>`
- 动态/pass-through 待裁决：`<n>`
- 本 PR 中文化分母：`<n>`

## 可见性证据

| 位点或分组 | 同步根 | 服务 endpoint | facade 分类/operationId | Gateway 政策 | transportVisible | uiRenderedNow/前端证据 | 后端证据 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `<symbol>` | `<command/query>` | `<method route>` | `<exposed/op>` | `<preserve/redact/no-facade>` | `<true/false/待分类>` | `<true/false + operation消费点>` | `<path:line/test>` |

## 消息处理

- 已合规中文：`<n>`
- 本 PR 中文化：`<n>`
- 动态/pass-through 已收口：`<n>`
- 动态/pass-through 待裁决：`<n>`
- 明确排除及理由：`<n>`
- 留给错误码契约子票：`<n>`

## 验证

- 本域构建/测试：`<命令与结果>`
- Gateway/HTTP 传输证据：`<命令、测试或静态链>`
- 文案检查：`<命令与结果>`
- 未运行项及原因：`<事实>`

## 闭合等式

`候选总数 = transportVisible true + transportVisible false + 待分类`

`transportVisible true = 已合规中文 + 本 PR 中文化 + 错误码形态待裁决 + 动态/pass-through 待裁决`
```

“明确排除”必须引用六步中失败的具体步骤；“待分类”不能写成完成项；同一源码位点被多个
operation 调用时只计一次，并在证据表中列出全部已确认公开根。`已合规中文`、`本 PR 中文化`、
`错误码形态待裁决`、`动态/pass-through 待裁决` 四类必须互斥；已收口的动态/pass-through 位点
按最终消息归入前两类，不得在“已收口”和“待裁决”中重复计数。
