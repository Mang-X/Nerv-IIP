# ADR 0030：BusinessGateway 按用途分面的受控文件字节通路

- 状态：已接受
- 日期：2026-09-03
- 关联：[Issue #3085](https://github.com/Mang-X/Nerv-IIP/issues/3085)、[ADR 0023](0023-filestorage-tus-proxy-staging-final-complete-invariants.md)
- 修订对象：ADR 0023 决策 1.3 与决策 1.1 中「自研 tus endpoint 不得新增消费方」这一面；ADR 0015 决策 2 的「单次调用超时为 10 秒」对字节流跳的适用性

## 背景

[ADR 0023](0023-filestorage-tus-proxy-staging-final-complete-invariants.md) 决策 1.3 规定「Console/浏览器只访问 **PlatformGateway** 暴露的受控 tus URL」，决策 1.1 把现有自研 tus endpoint 列入待退役范围、不再作为可扩展的目标架构。本 ADR 点名推翻的正是这两条：前者的「只由 PlatformGateway 暴露」，与后者在「新增消费方」这一面上的禁止。

推翻的直接原因是两条已生效的约束把业务面挤到了 0023 没有覆盖的位置：

1. [`docs/architecture/api-contract-and-codegen.md`](../architecture/api-contract-and-codegen.md) 规定业务控制台前端只消费 BusinessGateway 暴露的 `/api/business-console/v1/**`，不得直连 FileStorage 服务 URL；PlatformGateway 的 `/api/console/v1/**` 是平台控制台门面，不是业务控制台的消费面。
2. 业务面的授权口径由业务域权限码承担。交接班附件的读写归 `business.mes.handovers.read` / `business.mes.handovers.manage`，而 PlatformGateway 的文件门面统一走平台级 `files.*` 权限。让业务控制台走 platform 面，等于要求一线交接班用户额外持有平台文件权限。

0023 写作时业务面尚无字节需求，因此「只由 PlatformGateway 暴露」在当时是完备的；#3085 引入第一个业务侧字节通路后不再成立。

同一次改动还触及 [ADR 0015](0015-gateway-http-client-resilience-strategy.md)。0015 决策 2 冻结的「单次调用超时为 10 秒」是按单次 JSON 调用定的；本仓在它之前没有任何经网关代理的字节流面，因此该参数当时不需要区分调用形态。业务侧出现 tus 字节代理后，同一参数会切断合法的大文件传输，需要按 0015 自己规定的「参数变化必须作为本决策的修订处理」登记，见决策 5。

## 决策

1. **代理拓扑不再唯一。** BusinessGateway 可以暴露受控 tus 代理入口，与 PlatformGateway 并列。两者的共同约束不变：客户端只取得网关自有 URL，不得取得 FileStorage 内部 URL、存储地址、`ObjectKey` 或长期存储凭据；网关只做鉴权与代理，文件事实仍由 FileStorage 拥有。
2. **业务面的字节通路按用途分面。** 业务侧每条文件门面固定一个 `filePurpose` 与 owner，不从请求体读取；在签发下载授权或交付字节之前必须复核目标文件的用途属于本门面。业务域读权限不得因为共用 FileStorage 而退化成通用文件读权限。
3. **本 ADR 之后新开的业务字节面不得把 FileStorage 的 download grant id 交给调用方。** grant id 是 FileStorage 全服务共用命名空间，其兑换面不校验用途；一旦交给调用方，任一业务门面的读权限持有者都能兑换其它门面签发的 grant。新开业务面的下载授权必须由网关在服务端签发并立即兑换，对外只暴露以业务标识（如 `fileId`）为入参的单跳字节路由。

   **既有存量例外（登记，不追认为合规形状）**：工程 SOP 文件面
   `POST /api/business-console/v1/files/{fileId}/download-grants` 与
   `GET /api/business-console/v1/files/download-grants/{downloadGrantId}/content`
   早于本 ADR，仍把 grant id 交给调用方。它当前不构成跨门面兑换：该 grant id 只能由持
   `business.engineering.documents.read` 的主体开出，且兑换面要求同一权限码，命名空间内没有
   第二个权限口径可被跨越。**再新增任何一个消费该 content 路由的权限口径，就会立即让它变成本
   决策要防的兑换通道**；届时必须先按本决策改造，不得沿用。该存量的收敛不在本 ADR 范围内，由 [Issue #3297](https://github.com/Mang-X/Nerv-IIP/issues/3297) 承接。

   **#3297 选路的关键前提**：FileStorage 当前**没有按 `downloadGrantId` 反查所属 file 的读面**——以 `uploadSessionId`/`downloadGrantId` 为键的公开路由都不回出归属事实，因此网关侧无法在兑换时复核 grant 属于哪个用途。存量面要么照本决策改成「授权在服务端签发并立即兑换、不交出 id」，要么先由 FileStorage 提供该反查能力；这两条路的代价差别就落在这个前提上。
4. **传输语义不变。** tus 协议语义、staging/final 生命周期、`ObjectKey` 不公开、complete 提交不变量与失败矩阵完全按 ADR 0023 执行，本 ADR 不修改其中任何一条。
5. **字节流跳不适用 [ADR 0015](0015-gateway-http-client-resilience-strategy.md) 决策 2 的 10 秒总超时；该参数对字节流跳的适用性由本 ADR 部分修订。**

   0015 决策 2 的原文是「单次调用超时为 10 秒；超时直接失败返回，不自动重试。」，并规定「参数变化会改变可用性与重复执行风险之间的权衡，必须作为本决策的修订处理，不能仅在某个 Gateway 中静默漂移。」——因此本 ADR 走部分取代程序登记，而不是在 Gateway 里静默改。

   **被修订的只有一句**：单次调用超时 10 秒不适用于字节流传输跳。理由是该参数按单次 JSON 调用的时长分布定，而合法的大文件传输耗时由文件大小与现场带宽决定，与调用是否健康无关。

   **0015 中继续完全有效的部分**（逐条点名，避免读成整条失效）：
   - 决策 1 末行「策略选择以客户端**实际可发起的操作**为准，不以客户端名称、所属 Gateway 或页面来源推断。」——**正是它要求本次按「每一跳实际发起什么」而不是按类名划分接缝**；
   - 决策 2 的熔断参数（FailureRatio 0.5 / MinimumThroughput 10 / SamplingDuration 30s / BreakDuration 15s）与「不自动重试」；
   - 决策 3.2「只要客户端能够发起非幂等写操作，就必须使用无自动重试的非幂等安全策略。」——字节面的 tus `PATCH` 是非幂等写，因此字节面**必须挂无重试策略，不允许零策略注册**。

   落地形态：业务字节面单独注册一个去掉总超时、保留上述熔断与无重试的弹性档；单次传输的时限由调用方的取消令牌承担。**纯 JSON RPC 的跳（即使发生在下载链路中）仍留在 JSON 面的注册上**，不得因为它与字节跳同属一条业务链路就一起搬走。

## 已考虑的替代方案

1. **维持旧判断：只由 PlatformGateway 暴露 tus，business-console 走 platform 面。** 拒绝。它与 `api-contract-and-codegen.md` 的业务控制台消费面约束直接冲突，且会把平台级 `files.*` 权限强加给一线交接班用户；授权口径与门面归属两处同时被破坏，代价大于新增一个受同样约束的代理拓扑。
2. **让 business-console 直连 FileStorage 服务 URL。** 拒绝。它同时推翻 0023 决策 1.3 的「客户端不得取得内部 URL」与业务控制台的消费面约束，且没有任何一层能施加业务域权限检查。
3. **业务面沿用 PlatformGateway 的 grant + content 两跳形状。** 拒绝。PlatformGateway 的文件面只有一个权限口径（`files.*`），grant id 共用命名空间不构成越权；业务面按用途拆权限后，同一命名空间的 grant id 会成为跨用途兑换通道。这是决策 3 的由来。
4. **维持旧判断：字节面沿用 `NonIdempotentSafe` 全套参数（含 10 秒总超时）。** 拒绝。`shift-handover-photo` 允许 20,971,520 bytes（`FileStorage.Web/appsettings.json` 的 `MaximumFileSizeBytes`），单次满额 tus `PATCH` 需端到端持续 ≥ 2 MB/s 才能压进 10 秒，现场手机网络下必然超时并触发同一 client 的熔断，连带打掉建会话、complete 与同下游的其它 JSON 面。保留该参数等于用一条为 JSON 调用定的时限否决一类合法传输。

5. **由 FileStorage 在 download grant 上记录签发门面并在兑换时校验。** 拒绝（本次）。它需要改动 FileStorage 的公开契约与 schema，跨服务且影响既有 PlatformGateway 消费方；决策 3 在网关侧即可关闭同一缺口，代价小一个数量级。若将来出现网关侧无法关闭的同类需求，应重新评估此项。

## 后果

1. 受控 tus 入口从「一个」变成「一类」：新增网关代理拓扑时，必须同时满足决策 1 的不泄露约束与决策 2 的用途分面约束，不能只照抄路由形状。
2. 业务面的下载不再有可分享的短期 URL——字节路由要求 `Authorization` 与组织/环境上下文。需要在页面上直接渲染图片的调用方必须自行取字节并构造 blob，不能把 URL 直接交给 `<img src>`。这与 grant id 从未真正可匿名访问（兑换仍需组织/环境头）的现状一致，不构成能力回退。
3. 每次取字节多一次 FileStorage 往返（用途复核 + 签发 + 兑换）。这是把用途口径收在网关侧的直接成本。
4. 自研 tus endpoint 的退役范围扩大：它现在有两个网关消费方，ADR 0023 决策 1.1 的退役工作必须同时迁移两处。
5. 同一下游（FileStorage）在 BusinessGateway 有两个 typed client 注册。它们的差别只有弹性契约，不是领域切分；新增字节路由时必须落在字节面注册上，否则决策 5 会被静默绕过。
6. BusinessGateway 出现第二个弹性档（字节流档）。ADR 0015 决策 3.4 的口径不变：客户端到策略的完整映射由 Gateway 注册代码与对应测试证明，本 ADR 不维护清单。
7. 决策 3 的存量例外是**一条会过期的豁免**：它依赖「SOP content 路由只有一个权限口径」这一当前事实。该事实由权限口径本身承担，不由本 ADR 承担。
