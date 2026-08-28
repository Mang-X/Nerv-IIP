# NERV-1571 WMS 走查事实向量

本文是 GitHub [#1912](https://github.com/Mang-X/Nerv-IIP/issues/1912) 真实机器走查要求下，NERV-1571 所绑定的 WMS 页面事实向量。它是测试预期的独立来源；页面实现、一次 HTTP 响应和 mock fixture 都不能反向生成本文的值。

## 事实来源与证明边界

- 业务范围来源：Linear `NERV-1571`，对应 GitHub #1912 的走查验收入口；本文把票面要求落成可审的页面选择、列表请求和失败关闭规则，而不是只重复票号。
- 回归样本来源：旧走查在 `frontend/apps/business-console/e2e/issue1912-real-machine-walkthrough.spec.ts` 中把 WMS 首屏 `take=100` 写成预期，却没有保留页面走查的页窗口输入；入库还在没有页面工厂选择证据时固定写入 `SITE-001`，范围则可能来自 localStorage/目录首项。这些已确认的错误绑定是 `Regression` 的最小失败样本。
- 测试有效性分类：`Regression`。本文和场景输入只把已确认的旧走查错误（固定首屏页窗口、未选择即写入工厂/范围）固定为回归预期；页窗口现在必须由走查场景显式提供，不把页面当前默认值冒充为合同来源。当前没有受治理的 OpenAPI、公共 Contracts、ADR 或兼容性政策可作为 `PublicContract` 来源，因此不能把本文自建的事实向量或页面当前响应升级为 `PublicContract`。合同测试仍遵循 [`test-validity-governance.md`](test-validity-governance.md) 的独立来源、负向变异和证明范围要求。
- 真实运行边界：只有真实管理 FullStack/FullChain 浏览器运行，且同时具备真实身份、公开请求、provider、readiness 和 cleanup 记录时，才能把页面事实结论写成 `runtime-confirmed`。纯函数、Vitest 和 Playwright mock fixture 只能证明前端事实校验与编排，不证明 WMS API、身份、provider 或完整拓扑。

## 场景事实

走查在页面上先显式选择授权作业范围；入库页还必须从已加载的工厂目录显式选择目标工厂，选择结果才允许作为列表查询事实。不能用 localStorage 记忆值、目录首项或响应 URL 猜测代替选择。

范围 proof 会用页面搜索控件检索已选的规范值 `scopeKind:scopeId`，等待唯一可见 option，再点击并重新打开目录验证该 option 的底层 value 已被选中；入库工厂 proof 等待异步目录稳定为唯一 option，点击后验证触发器回显 `siteCode`。因此页面 option/readback 与随后公开请求中的 `scopeKind`、`scopeId`、`siteCode` 是同一次显式选择的绑定，不能由响应 URL 反推。

完成页面选择后纳入精确证明的 WMS 列表请求，其必需查询字段为：当前登录租户 `organizationId`、环境 `environmentId`、已选择的 `scopeKind=work-pool`、已选择的 `scopeId`，以及由本次走查场景显式提供的 `pageWindow.skip=0` 和正整数 `pageWindow.take`。本文的 mock vector 选择 `take=10` 仅是一个可审的场景输入，不是页面默认值或公共合同；真实走查必须在证明前提供该场景输入，若输入缺失或页面发出其他值则失败关闭。走查关键字是同一场景输入 `IN-WALK-001` 或 `DO-WALK-001`；在关键字过滤请求中还必须按原值出现一次，不能用响应 URL 回填预期。选择绑定的刷新请求为空关键字是有意的两阶段边界，随后过滤请求由通用输入 proof 单独绑定关键字。

| 页面 | 独立场景输入 | 查询约束 |
| --- | --- | --- |
| 入库 | `作业范围` 的授权作业池；工厂目录中的明确编码 `SITE-001`；关键字 `IN-WALK-001` | 选择绑定的刷新列表请求必须包含所选 `siteCode=SITE-001`；过滤请求还必须包含同一关键字 |
| 出库 | `作业范围` 的授权作业池；关键字 `DO-WALK-001` | 选择绑定的刷新列表请求不得包含 `siteCode`；过滤请求还必须包含同一关键字 |

以下确定性 vector 只供前端合同和 mock fixture 使用，不是 seed、权限或 FullChain 事实：`org-live/env-live`、`pool-receiving-001`、`pool-shipping-001`、`SITE-001`，以及场景输入 `pageWindow={skip:0,take:10}`。真实走查必须以登录返回的租户/环境、场景页窗口输入和页面公开目录选择结果填充同一类型的查询事实；不得从首个响应回填页窗口。

## 首次 WMS 请求边界

页面进入时由页面生命周期自动发出的首个 WMS 列表请求纳入公开证据，但只校验其两个精确目标路径之一和 HTTP 200 响应，不能把它冒充成已经完成的范围/工厂选择事实：范围可能由授权目录异步装载，入库工厂在首个请求时也可能尚未选择。启动阶段已知的辅助 WMS 读取由同一个 endpoint registry 明确登记；任何未登记的 WMS 路径、嵌套/尾斜杠变体、列表候选的非 GET 方法、首个请求的非 2xx 状态或网络失败都会立即失败关闭，不能由后续 200 掩盖。WMS proof 随后必须在页面显式完成选择后，通过 action-bound 刷新重新取得列表响应，并对该响应完整校验组织、环境、范围、分页和 `siteCode` 约束；首个请求的查询值不生成预期 fingerprint，也不抵消后续精确校验。

## 失败关闭要求

合同与 WMS 专属 proof 必须拒绝：组织或环境不匹配、`scopeKind`/`scopeId` 不匹配、场景输入的 `skip`/`take` 不匹配、列表路径、方法、状态或网络错误、入库缺少或错误 `siteCode`、出库带有 `siteCode`，以及没有选择或同时提供两个选择事实。点击后目录尚未完成加载时不得把暂时的零选项数当成缺失；应等待可见目录稳定后再判定唯一性。
