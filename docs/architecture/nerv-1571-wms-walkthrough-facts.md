# NERV-1571 WMS 走查事实向量

本文是 GitHub [#1912](https://github.com/Mang-X/Nerv-IIP/issues/1912) 真实机器走查要求下，NERV-1571 所绑定的 WMS 页面事实向量。它是测试预期的独立来源；页面实现、一次 HTTP 响应和 mock fixture 都不能反向生成本文的值。

## 事实来源与证明边界

- 业务范围来源：Linear `NERV-1571`，对应 GitHub #1912 的走查验收入口；本文把票面要求落成可审的页面选择、列表请求和失败关闭规则，而不是只重复票号。
- 回归样本来源：旧走查在 `frontend/apps/business-console/e2e/issue1912-real-machine-walkthrough.spec.ts` 中把 WMS 首屏 `take=100` 写成预期，但页面 `usePagedList` 默认写出 `take=10`；入库还在没有页面工厂选择证据时固定写入 `SITE-001`，范围则可能来自 localStorage/目录首项。这些已确认的错误绑定是 `Regression` 的最小失败样本。
- 测试有效性分类：`Regression`（上述旧走查错误）与 `PublicContract`（浏览器公开列表请求的路径、状态和查询字段）。后者按受治理的 [`test-validity-governance.md`](test-validity-governance.md) `PublicContract` 规则，以页面可观察 HTTP 请求作为被测边界；合同预期仍只来自本文和场景输入，不来自实现或响应回读。合同测试还遵循该文档的独立来源和负向变异要求。
- 真实运行边界：只有真实管理 FullStack/FullChain 浏览器运行，且同时具备真实身份、公开请求、provider、readiness 和 cleanup 记录时，才能把页面事实结论写成 `runtime-confirmed`。纯函数、Vitest 和 Playwright mock fixture 只能证明前端事实校验与编排，不证明 WMS API、身份、provider 或完整拓扑。

## 场景事实

走查在页面上先显式选择授权作业范围；入库页还必须从已加载的工厂目录显式选择目标工厂，选择结果才允许作为列表查询事实。不能用 localStorage 记忆值、目录首项或响应 URL 猜测代替选择。

完成页面选择后纳入精确证明的 WMS 列表请求，其必需查询字段为：当前登录租户 `organizationId`、环境 `environmentId`、已选择的 `scopeKind=work-pool`、已选择的 `scopeId`、`skip=0` 和 `take=10`。默认分页事实由 NERV-1571 场景固定为首屏 10 条；若该证明请求实际发出其他值，走查必须失败关闭。

| 页面 | 独立场景输入 | 查询约束 |
| --- | --- | --- |
| 入库 | `作业范围` 的授权作业池；工厂目录中的明确编码 `SITE-001` | 列表请求必须包含所选 `siteCode=SITE-001` |
| 出库 | `作业范围` 的授权作业池 | 列表请求不得包含 `siteCode` |

以下确定性 vector 只供前端合同和 mock fixture 使用，不是 seed、权限或 FullChain 事实：`org-live/env-live`、`pool-receiving-001`、`pool-shipping-001`、`SITE-001`、`skip=0`、`take=10`。真实走查必须以登录返回的租户/环境和页面公开目录选择结果填充同一类型的查询事实。

## 首次 WMS 请求边界

页面进入时由页面生命周期自动发出的首个 WMS 列表请求纳入公开证据，但只校验其目标路径和 HTTP 200 响应，不能把它冒充成已经完成的范围/工厂选择事实：范围可能由授权目录异步装载，入库工厂在首个请求时也可能尚未选择。WMS proof 随后必须在页面显式完成选择后，通过 action-bound 刷新重新取得列表响应，并对该响应完整校验组织、环境、范围、分页和 `siteCode` 约束；首个请求的查询值不生成预期 fingerprint，也不抵消后续精确校验。首个请求路径或状态错误仍立即失败关闭。

## 失败关闭要求

合同与 WMS 专属 proof 必须拒绝：组织或环境不匹配、`scopeKind`/`scopeId` 不匹配、`skip`/`take` 不匹配、列表路径或状态错误、入库缺少或错误 `siteCode`、出库带有 `siteCode`，以及没有选择或同时提供两个选择事实。点击后目录尚未完成加载时不得把暂时的零选项数当成缺失；应等待可见目录稳定后再判定唯一性。
