# ADR 0030：BusinessGateway 按用途分面的受控文件字节通路

- 状态：已接受
- 日期：2026-09-03
- 关联：[Issue #3085](https://github.com/Mang-X/Nerv-IIP/issues/3085)、[ADR 0023](0023-filestorage-tus-proxy-staging-final-complete-invariants.md)
- 修订对象：ADR 0023 决策 1.3，以及决策 1.1 中「自研 tus endpoint 不得新增消费方」这一面

## 背景

[ADR 0023](0023-filestorage-tus-proxy-staging-final-complete-invariants.md) 决策 1.3 规定「Console/浏览器只访问 **PlatformGateway** 暴露的受控 tus URL」，决策 1.1 把现有自研 tus endpoint 列入待退役范围、不再作为可扩展的目标架构。本 ADR 点名推翻的正是这两条：前者的「只由 PlatformGateway 暴露」，与后者在「新增消费方」这一面上的禁止。

推翻的直接原因是两条已生效的约束把业务面挤到了 0023 没有覆盖的位置：

1. [`docs/architecture/api-contract-and-codegen.md`](../architecture/api-contract-and-codegen.md) 规定业务控制台前端只消费 BusinessGateway 暴露的 `/api/business-console/v1/**`，不得直连 FileStorage 服务 URL；PlatformGateway 的 `/api/console/v1/**` 是平台控制台门面，不是业务控制台的消费面。
2. 业务面的授权口径由业务域权限码承担。交接班附件的读写归 `business.mes.handovers.read` / `business.mes.handovers.manage`，而 PlatformGateway 的文件门面统一走平台级 `files.*` 权限。让业务控制台走 platform 面，等于要求一线交接班用户额外持有平台文件权限。

0023 写作时业务面尚无字节需求，因此「只由 PlatformGateway 暴露」在当时是完备的；#3085 引入第一个业务侧字节通路后不再成立。

## 决策

1. **代理拓扑不再唯一。** BusinessGateway 可以暴露受控 tus 代理入口，与 PlatformGateway 并列。两者的共同约束不变：客户端只取得网关自有 URL，不得取得 FileStorage 内部 URL、存储地址、`ObjectKey` 或长期存储凭据；网关只做鉴权与代理，文件事实仍由 FileStorage 拥有。
2. **业务面的字节通路按用途分面。** 业务侧每条文件门面固定一个 `filePurpose` 与 owner，不从请求体读取；在签发下载授权或交付字节之前必须复核目标文件的用途属于本门面。业务域读权限不得因为共用 FileStorage 而退化成通用文件读权限。
3. **业务面不得把 FileStorage 的 download grant id 交给调用方。** grant id 是 FileStorage 全服务共用命名空间，其兑换面不校验用途；一旦交给调用方，任一业务门面的读权限持有者都能兑换其它门面签发的 grant。业务面的下载授权必须由网关在服务端签发并立即兑换，对外只暴露以业务标识（如 `fileId`）为入参的单跳字节路由。
4. **传输语义不变。** tus 协议语义、staging/final 生命周期、`ObjectKey` 不公开、complete 提交不变量与失败矩阵完全按 ADR 0023 执行，本 ADR 不修改其中任何一条。

## 已考虑的替代方案

1. **维持旧判断：只由 PlatformGateway 暴露 tus，business-console 走 platform 面。** 拒绝。它与 `api-contract-and-codegen.md` 的业务控制台消费面约束直接冲突，且会把平台级 `files.*` 权限强加给一线交接班用户；授权口径与门面归属两处同时被破坏，代价大于新增一个受同样约束的代理拓扑。
2. **让 business-console 直连 FileStorage 服务 URL。** 拒绝。它同时推翻 0023 决策 1.3 的「客户端不得取得内部 URL」与业务控制台的消费面约束，且没有任何一层能施加业务域权限检查。
3. **业务面沿用 PlatformGateway 的 grant + content 两跳形状。** 拒绝。PlatformGateway 的文件面只有一个权限口径（`files.*`），grant id 共用命名空间不构成越权；业务面按用途拆权限后，同一命名空间的 grant id 会成为跨用途兑换通道。这是决策 3 的由来。
4. **由 FileStorage 在 download grant 上记录签发门面并在兑换时校验。** 拒绝（本次）。它需要改动 FileStorage 的公开契约与 schema，跨服务且影响既有 PlatformGateway 消费方；决策 3 在网关侧即可关闭同一缺口，代价小一个数量级。若将来出现网关侧无法关闭的同类需求，应重新评估此项。

## 后果

1. 受控 tus 入口从「一个」变成「一类」：新增网关代理拓扑时，必须同时满足决策 1 的不泄露约束与决策 2 的用途分面约束，不能只照抄路由形状。
2. 业务面的下载不再有可分享的短期 URL——字节路由要求 `Authorization` 与组织/环境上下文。需要在页面上直接渲染图片的调用方必须自行取字节并构造 blob，不能把 URL 直接交给 `<img src>`。这与 grant id 从未真正可匿名访问（兑换仍需组织/环境头）的现状一致，不构成能力回退。
3. 每次取字节多一次 FileStorage 往返（用途复核 + 签发 + 兑换）。这是把用途口径收在网关侧的直接成本。
4. 自研 tus endpoint 的退役范围扩大：它现在有两个网关消费方，ADR 0023 决策 1.1 的退役工作必须同时迁移两处。
