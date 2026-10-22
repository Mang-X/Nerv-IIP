# KnownException 用户可见性与 Gateway 传输治理

本文规定 `KnownException` 等稳定业务错误从领域服务到 Gateway/前端时，如何判断“可传输”和“当前 UI 已呈现”。它维护**判定规则**，不维护某次全仓扫描数量、Issue 状态或当前每个服务的逐行证据。

历史审计、整改结果和时点证据从 `docs/reports/` 查询；当前 Gateway/服务行为最终以代码、公开契约、测试与当前 facade Reference 为准。

## 两个概念必须分开

- `transportVisible`：错误已经通过公开 HTTP/facade 路径，以稳定、安全、可识别的业务错误形式传给调用方。
- `uiRenderedNow`：当前产品 UI 在真实调用路径上识别该错误并向用户显示可操作信息。

`transportVisible=true` 不自动推出 `uiRenderedNow=true`。反过来，UI 本地校验文案也不能证明服务端 KnownException 已有稳定传输契约。

## 证据优先级

判定某条错误是否用户可见时，优先按当前链路证据核实：

1. 抛出源与稳定 error code / safe message；
2. 服务 HTTP 边界如何序列化；
3. 是否存在当前公开 facade；
4. Gateway 是否读取上游错误；
5. Gateway 是否保留稳定 code / safe message，而不是改成泛化 500；
6. Gateway 最终公开响应；
7. 只有要声明 `uiRenderedNow` 时，再检查 generated client、页面调用与错误渲染。

不能用旧 Issue、历史报告、直接服务调用或“前端有 toast”替代整条链路。

## 六步 transport 判定

### 1. 同步来源

先确认错误发生在当前请求链可同步返回的路径。后台任务、CAP consumer、outbox、定时扫描等异步错误不能因为同名 KnownException 就声称本次 HTTP 调用可见。

### 2. 到达服务 HTTP 边界

错误必须由服务端统一异常/结果管线转成稳定公开错误，不允许把堆栈、内部异常类型、SQL/provider 信息或敏感数据直接透传。

### 3. 存在公开 facade

面向 Console/Business Console/PDA 的能力若要求经 Gateway 访问，必须确认对应 facade 存在。没有 facade 的服务端错误不能标记为该 UI 的 `transportVisible`。

### 4. Gateway 读取上游错误

Gateway 必须读取结构化 error code / message（或明确兼容的上游错误模型），不能只判断 HTTP status 后丢掉业务语义。

### 5. Gateway 保留安全业务语义

稳定 code 和被批准传输的 safe message 必须在 Gateway 代理层保持；不得把所有 4xx/409/422 统一改成无差别“请求失败”，也不得把内部异常细节升级成公开信息。

### 6. Gateway 写出公开响应

最终响应必须符合公开错误契约，状态码、code、message/correlation 等字段可由客户端稳定消费。完成本步才能认定 `transportVisible=true`。

## 无 facade 与异步场景

1. 没有当前公开 facade：对目标 UI 记为不可传输；是否需要 facade 由对应 API/facade owner 决定，不在错误治理票里顺手补业务门面。
2. 异步 consumer/worker：错误进入重试、DLQ、任务/通知或审计路径，不冒充同步用户错误。
3. 需要用户操作的异步失败应通过对应领域的任务/通知/状态读面暴露；具体产品语义由该领域拥有。
4. internal-service 直调成功看到错误，不证明最终用户经 Gateway 也可见。

## 安全传输规则

可以公开的消息必须是业务安全信息：

- 稳定 error code；
- 用户能理解且不会泄漏内部结构的 message；
- 必要 correlationId / resource reference；
- 与公开契约一致的 validation/conflict/not-found 等状态。

不得公开：stack trace、内部类型名、完整连接串、SQL、token/secret、文件系统内部路径、第三方 credential、未脱敏 PII 或只供运维诊断的异常正文。

安全 message 的 owner 在领域/公开错误契约；Gateway 负责保持，不负责重新解释领域规则。

## UI 呈现

只有同时满足以下条件才可声明 `uiRenderedNow=true`：

1. 真实 UI 通过当前公开 facade 发起请求；
2. generated/manual client 保留公开错误结构；
3. 页面或共享错误层识别该 code/message；
4. 用户能看到可操作反馈，而非只在 devtools/console；
5. 需要动作约束时，UI 文案与服务端最终状态一致。

UI 可以进一步把稳定 code 映射成更友好文案，但不能靠前端映射覆盖服务端未公开或不安全的信息。

### 稳定码必须在前端词表登记（跨语言单向包含）

后端发布的每个稳定码都必须在对应的前端展示词表里登记。两侧由
`scripts/verify-stable-code-frontend-vocabulary.ps1` 在 CI 强制，方向是**单向包含（后端 ⊆ 前端）**：
前端为已下线的历史码保留展示是合法的，要求相等会误报。两条通道的 producer 与 consumer 都不同，
分别比对：

| 通道 | 后端 producer | 前端 consumer | 未登记的后果 |
| --- | --- | --- | --- |
| 错误信封 `message` 位 | `public const string SafeCode` / `StableErrorCode` 成员，以及 `*StableWireCodes` 注册表类的 `public const string` | `STABLE_ERROR_MESSAGES` | message 位**就是裸码**、不含中文，英文码直接上屏 |
| MES readiness 阻断原因（`CODE: 中文`） | `MesReadinessReasonCodes` | `MES_READINESS_REASON_DISPLAYS` | 中文事实仍显示，但 `describeMesReadinessReason` 走兜底，码拿不到标签与下一步，且 `RELEASE_IGNORED_TASK_BLOCKERS.has(code)` 为 false，**静默阻断下达** |

**登记行为是「在 producer 类里声明一个 `public const string`」**，不是「写出一个长得像码的字面量」。
这条区分是实测的，而且两个方向都不依赖计数：形状口径**看不见 `WORK_ORDER_NOT_RELEASED`**
（它写成 `WorkOrderNotReleased + ": …"`，从不整串出现在一个字面量里），声明口径**看不见
`MATERIAL_REQUIREMENT_SOURCE_UNAVAILABLE` 这类只存在于 throw 点字面量、从未声明的码**，
两个口径互不包含。此处刻意不写命中条数：那个数会随着码被收进注册表而变，写死就会过期而不报红；
要复量请自己跑并注明 head：
`git grep -rhoE '"[A-Z][A-Z0-9_]{2,}: [^"]*"' -- 'backend/**/src/**/*.cs'`。以裸字面量写在产出点的码不在检查器扫描面内，也就不受这条契约保护——
`WORK_ORDER_NOT_FOUND` 就是这样与 `WORK_ORDER_NOT_RELEASED` 相隔一行却长期无人看守的（#3155）。
把这段残余也关上需要枚举「一个码的所有写法」，#3214 / #3176 三轮实测该形态不收敛，故不做。

后端把码 pin 成逐字断言（如 `Stable_wire_code_literal_is_pinned`）**不能替代**这条契约：
pin 保证码不被悄悄改名，不保证前端认得它。#3333 / PR #3359 的变异格实测过这个差别——
改后端码字面量后**后端红 1、前端三包全绿**。

## 与权限、范围和生命周期的关系

- 403/权限拒绝的公开语义由授权层拥有；错误治理不能为了“让用户看到”而放宽权限。
- 404/403 的选择若用于防枚举，按安全/授权规则执行，不由 UI 需求决定。
- 409/422 等冲突必须由领域当前状态与公开 contract 支撑；不要根据历史页面行为硬编码。
- 同一 error code 的业务语义不能随页面变化；页面差异只影响展示。

## 域票验收模板

聚焦某一域的错误可见性时，每条候选至少记录：

1. source code / 抛出位置；
2. sync / async；
3. service HTTP contract；
4. facade operation；
5. Gateway transport evidence；
6. `transportVisible` 结论；
7. 若在范围内，再给 UI consumer 与 `uiRenderedNow` 结论；
8. 无法闭合时把缺口留给真正 owner，不在本治理文件复制项目状态。

## 当前事实怎么查

- PlatformGateway / BusinessGateway 的实际错误代理：对应 Gateway endpoint/client/exception transport 代码与测试；
- 业务服务错误来源：目标 Domain/Application/Web；
- facade 是否存在及 ownership：当前 API/facade 文档与机器伴随物；
- 历史 KnownException 审计：`docs/reports/` 相关报告；
- 历史报告中的旧相对链接兼容入口：`docs/reports/known-exception-user-visibility.md`。

## 变更纪律

- 本页不保存“扫描了多少处”“哪些 Issue 已补丁”“截至某日哪些服务已覆盖”等时点事实。
- 改变稳定错误 code、安全 message、Gateway transport 或公开错误结构属于实现/契约变更，必须由对应代码与测试先证明，再同步 Governance。
- 不新增依赖自然语言文本的 checker 来证明错误可见性；核心证据是实际调用链与行为测试。